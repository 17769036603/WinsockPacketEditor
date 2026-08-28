using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// Host-facing response contract.  Wire decoding remains TODO until a
    /// confirmed 20-card response sample is available.
    /// </summary>
    public sealed class RefineResponseCard
    {
        public int CardIndex { get; set; }
        public List<AttributeValue> Attributes { get; private set; } = new List<AttributeValue>();
        public List<RefineRawProperty> RawProperties { get; private set; } = new List<RefineRawProperty>();
        public bool IsComplete { get; set; }
    }

    public sealed class RefineRawProperty
    {
        public int EntryIndex { get; set; }
        public string RawId { get; set; } = string.Empty;
        public string RawValue { get; set; } = string.Empty;
        public string RawOrder { get; set; } = string.Empty;
        public string PropertyKey { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public TargetAttribute MappedAttribute { get; set; } = TargetAttribute.Unknown;
    }

    public sealed class RefineResponseEnvelope
    {
        public long Sequence { get; set; }
        public string EquipmentIdentity { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public bool IsStable { get; set; }
        public List<RefineResponseCard> Cards { get; private set; } = new List<RefineResponseCard>();
    }

    public sealed class RefineResponseResult
    {
        public bool Success { get; set; }
        public bool ContinueAllowed { get; set; }
        public bool Replaced { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int MatchedCount { get; set; }
        public int RequiredMatches { get; set; }
        public int TargetCount { get; set; }
        public RefineResponseCard MatchedCard { get; set; }
        public List<int> MatchedRuleIndexes { get; private set; } = new List<int>();
    }

    public interface IEquipmentRefineResultPresenter
    {
        void Present(EquipmentRefineStateMachine.ExecutionResult result);
        void PresentStop(string reason, string displayMessage);
    }

    public sealed class StringEquipmentRefineResultPresenter : IEquipmentRefineResultPresenter
    {
        public string LastMessage { get; private set; } = string.Empty;

        public void Present(EquipmentRefineStateMachine.ExecutionResult result)
        {
            if (result == null)
            {
                this.LastMessage = "炼化结果未知";
                return;
            }

            string card = result.MatchedResponseCard == null
                ? string.Empty
                : string.Format(
                    "卡片{0}: {1}",
                    result.MatchedResponseCard.CardIndex,
                    string.Join(", ", result.MatchedResponseCard.Attributes.Select(FormatAttribute)));
            this.LastMessage = string.Format(
                "状态={0};停止原因={1};命中={2}/{3};命中规则={4};{5};未替换属性",
                result.FinalState,
                result.StopReason,
                result.MatchedCount,
                result.RequiredMatches,
                string.Join(",", result.MatchedRuleIndexes),
                card);
        }

        public void PresentStop(string reason, string displayMessage)
        {
            this.LastMessage = string.Format("停止={0};{1}", reason, displayMessage ?? string.Empty);
        }

        private static string FormatAttribute(AttributeValue value)
        {
            if (value == null) return "";
            string displayValue = string.IsNullOrEmpty(value.RawValueText)
                ? value.CurrentValue.ToString()
                : value.RawValueText;
            string text = string.Format("{0}={1}", value.Name ?? value.Type.ToString(), displayValue);
            if (!string.IsNullOrEmpty(value.RawId)) text += "[rawId=" + value.RawId + "]";
            if (!string.IsNullOrEmpty(value.RawOrder)) text += "[rawOrder=" + value.RawOrder + "]";
            if (!string.IsNullOrEmpty(value.PropertyKey)) text += "[propertyKey=" + value.PropertyKey + "]";
            return text;
        }
    }

    public interface IRefineStopReasonMapper
    {
        string Map(string protocolOrResourceCode);
    }

    public sealed class ExplicitRefineStopReasonMapper : IRefineStopReasonMapper
    {
        private readonly IDictionary<string, string> _mapping;

        public ExplicitRefineStopReasonMapper(IDictionary<string, string> mapping)
        {
            this._mapping = mapping ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public string Map(string protocolOrResourceCode)
        {
            string value;
            return this._mapping.TryGetValue(protocolOrResourceCode ?? string.Empty, out value)
                ? value
                : "未映射协议/资源错误: " + (protocolOrResourceCode ?? string.Empty);
        }
    }

    public interface IRefineResponseAdapter
    {
        bool TryDecode(byte[] frame, out RefineResponseEnvelope response, out string error);
    }

    /// <summary>每轮发送后创建一次响应读取会话；实现必须绑定本轮发送，不得复用旧帧。</summary>
    public interface IRefineResponseSource
    {
        Task<byte[]> ReceiveAfterSendAsync(long baselineSequence, int timeoutMs, CancellationToken cancellationToken);
    }

    public sealed class PlaceholderRefineResponseSource : IRefineResponseSource
    {
        public Task<byte[]> ReceiveAfterSendAsync(long baselineSequence, int timeoutMs, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]>(null);
        }
    }

    public sealed class UnconfiguredRefineResponseAdapter : IRefineResponseAdapter
    {
        public bool TryDecode(byte[] frame, out RefineResponseEnvelope response, out string error)
        {
            response = null;
            error = "response_protocol_unverified";
            return false;
        }
    }

    public static class RefineResponseEvaluator
    {
        public static RefineResponseResult Evaluate(
            RefineResponseEnvelope response,
            IList<RefineRule> rules,
            int requiredMatches,
            string expectedEquipmentIdentity,
            long baselineSequence)
        {
            RefineResponseResult result = new RefineResponseResult
            {
                Replaced = false,
                RequiredMatches = requiredMatches,
                TargetCount = rules == null ? 0 : rules.Count(rule => rule != null && rule.Enabled),
                ContinueAllowed = false
            };

            if (rules == null || rules.Any(rule => rule == null) || result.TargetCount == 0 ||
                requiredMatches < 1 || requiredMatches > result.TargetCount)
            {
                result.Reason = "invalid_target_config";
                return result;
            }
            foreach (RefineRule rule in rules)
            {
                string ruleError;
                if (!rule.IsValid(out ruleError))
                {
                    result.Reason = "invalid_target_config";
                    return result;
                }
            }
            if (response == null || !response.IsStable || response.Sequence <= baselineSequence)
            {
                result.Reason = "response_sequence_unstable_or_unchanged";
                return result;
            }
            if (!string.Equals(response.EquipmentIdentity ?? string.Empty, expectedEquipmentIdentity ?? string.Empty, StringComparison.Ordinal))
            {
                result.Reason = "equipment_identity_changed";
                return result;
            }
            if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            {
                result.Reason = response.ErrorCode;
                return result;
            }
            if (response.Cards == null || response.Cards.Count != 20 ||
                response.Cards.Any(card => card == null || !card.IsComplete) ||
                response.Cards.Select(card => card.CardIndex).Distinct().Count() != 20 ||
                !response.Cards.Select(card => card.CardIndex).OrderBy(index => index)
                    .SequenceEqual(Enumerable.Range(1, 20)))
            {
                result.Reason = "response_cards_incomplete";
                return result;
            }

            List<RefineRule> activeRules = rules.Where(rule => rule != null && rule.Enabled).ToList();
            foreach (RefineResponseCard card in response.Cards.OrderBy(item => item.CardIndex))
            {
                List<int> matched = MatchOneToOne(card.Attributes, activeRules);
                if (matched.Count >= requiredMatches)
                {
                    result.Success = true;
                    result.Reason = "target_reached";
                    result.MatchedCount = matched.Count;
                    result.MatchedCard = card;
                    result.MatchedRuleIndexes.AddRange(matched);
                    return result;
                }
            }

            result.Reason = "target_not_reached";
            result.ContinueAllowed = true;
            return result;
        }

        private static List<int> MatchOneToOne(IList<AttributeValue> attributes, IList<RefineRule> rules)
        {
            List<List<int>> edges = new List<List<int>>();
            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                RefineRule rule = rules[ruleIndex];
                List<int> matches = new List<int>();
                if (attributes != null)
                {
                    for (int attributeIndex = 0; attributeIndex < attributes.Count; attributeIndex++)
                    {
                        AttributeValue attribute = attributes[attributeIndex];
                        if (attribute != null && attribute.Type == rule.Attribute && rule.Evaluate(attribute.CurrentValue))
                        {
                            matches.Add(attributeIndex);
                        }
                    }
                }
                edges.Add(matches);
            }

            Dictionary<int, int> propertyToRule = new Dictionary<int, int>();
            for (int ruleIndex = 0; ruleIndex < edges.Count; ruleIndex++)
            {
                Augment(ruleIndex, edges, propertyToRule, new HashSet<int>());
            }
            return propertyToRule.Values.Distinct().OrderBy(value => value).ToList();
        }

        private static bool Augment(int ruleIndex, IList<List<int>> edges, IDictionary<int, int> propertyToRule, ISet<int> seen)
        {
            foreach (int propertyIndex in edges[ruleIndex])
            {
                if (!seen.Add(propertyIndex)) continue;
                int previousRule;
                if (!propertyToRule.TryGetValue(propertyIndex, out previousRule) || Augment(previousRule, edges, propertyToRule, seen))
                {
                    propertyToRule[propertyIndex] = ruleIndex;
                    return true;
                }
            }
            return false;
        }
    }
}
