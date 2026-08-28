using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// Formats the final result for the assistant page. It deliberately uses
    /// the evaluator's matched rule indexes and the decoded card itself, so
    /// the displayed card number/property is never inferred from send count.
    /// </summary>
    public static class EquipmentRefineResultFormatter
    {
        public static string Format(
            EquipmentRefineStateMachine.ExecutionResult result,
            EquipmentRefinePreset preset)
        {
            if (result == null)
            {
                return "装备炼化结果未知。";
            }

            if (result.Success && result.MatchedResponseCard != null)
            {
                string matchedProperties = FormatMatchedProperties(result, preset);
                string cardProperties = string.Join(
                    "，",
                    result.MatchedResponseCard.Attributes == null
                        ? new string[0]
                        : result.MatchedResponseCard.Attributes.Select(FormatCardAttribute));
                if (string.IsNullOrWhiteSpace(matchedProperties))
                {
                    matchedProperties = cardProperties;
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "装备炼化成功：第 {0} 张卡片；命中属性：{1}；本次发送 {2} 次。",
                    result.MatchedResponseCard.CardIndex,
                    string.IsNullOrWhiteSpace(matchedProperties) ? "未能显示" : matchedProperties,
                    result.AttemptCount);
            }

            if (result.Success && result.StopReason == EquipmentRefineStateMachine.StopReason.TargetReached)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "装备当前属性已达到预设目标，未发送炼化封包；发送次数 {0}。",
                    result.AttemptCount);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "装备炼化已停止：{0}；发送次数 {1}。",
                FriendlyReason(result),
                result.AttemptCount);
        }

        private static string FormatMatchedProperties(
            EquipmentRefineStateMachine.ExecutionResult result,
            EquipmentRefinePreset preset)
        {
            if (result.MatchedResponseCard == null || preset == null || preset.Rules == null)
            {
                return string.Empty;
            }

            List<RefineRule> activeRules = preset.Rules
                .Where(rule => rule != null && rule.Enabled)
                .ToList();
            List<string> values = new List<string>();
            foreach (int ruleIndex in result.MatchedRuleIndexes ?? new List<int>())
            {
                if (ruleIndex < 0 || ruleIndex >= activeRules.Count)
                {
                    continue;
                }

                RefineRule rule = activeRules[ruleIndex];
                AttributeValue value = result.MatchedResponseCard.Attributes == null
                    ? null
                    : result.MatchedResponseCard.Attributes.FirstOrDefault(attribute =>
                        attribute != null &&
                        attribute.Type == rule.Attribute &&
                        rule.Evaluate(attribute.CurrentValue));
                string name = EquipmentRefineAttributeCatalog.GetDisplayName(rule.Attribute);
                values.Add(value == null
                    ? name + "（" + rule.GetDescription() + "）"
                    : name + "=" + FormatAttributeValue(value));
            }
            return string.Join("，", values);
        }

        private static string FormatCardAttribute(AttributeValue value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return (string.IsNullOrWhiteSpace(value.Name)
                    ? EquipmentRefineAttributeCatalog.GetDisplayName(value.Type)
                    : value.Name) + "=" + FormatAttributeValue(value);
        }

        private static string FormatAttributeValue(AttributeValue value)
        {
            TargetAttributeDefinition definition = EquipmentRefineAttributeCatalog
                .GetReaderDefinitions()
                .FirstOrDefault(item => item.Attribute == value.Type);
            int rawValue;
            string rawText = string.IsNullOrWhiteSpace(value.RawValueText)
                ? value.CurrentValue.ToString(CultureInfo.InvariantCulture)
                : value.RawValueText;
            if (definition != null && definition.IsPercentage &&
                int.TryParse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out rawValue))
            {
                return (rawValue / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
            }
            return rawText;
        }

        private static string FriendlyReason(EquipmentRefineStateMachine.ExecutionResult result)
        {
            switch (result.StopReason)
            {
                case EquipmentRefineStateMachine.StopReason.AuthorizationRequired:
                    return "未获得本次真实发送授权";
                case EquipmentRefineStateMachine.StopReason.ProtocolUnverified:
                    return "只读结果源或炼化协议尚未验收（" + result.Message + "）";
                case EquipmentRefineStateMachine.StopReason.UserStopped:
                    return "用户停止";
                case EquipmentRefineStateMachine.StopReason.EquipmentNotFound:
                    return "未找到唯一目标装备";
                case EquipmentRefineStateMachine.StopReason.EquipmentIdentityChanged:
                    return "目标装备身份发生变化";
                case EquipmentRefineStateMachine.StopReason.RefineResultTimeout:
                    return "等待炼化结果超时";
                default:
                    return string.IsNullOrWhiteSpace(result.Message)
                        ? result.StopReason.ToString()
                        : result.Message;
            }
        }
    }
}
