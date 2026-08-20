using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace WPELibrary.Lib
{
    public sealed class CompiledExtractionRule
    {
        private readonly byte[] pattern;
        private readonly byte[] wildcardMask;

        public CompiledExtractionRule(ExtractionRule source)
        {
            Rule = source.Clone();
            pattern = Rule.PatternBytes ?? new byte[0];
            wildcardMask = Rule.WildcardMask ?? new byte[pattern.Length];
        }

        public ExtractionRule Rule { get; private set; }

        public bool IsMatch(byte[] buffer)
        {
            if (buffer == null || buffer.Length != pattern.Length)
            {
                return false;
            }

            for (int i = 0; i < pattern.Length; i++)
            {
                if (wildcardMask[i] == 0 && buffer[i] != pattern[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static class PatternMatcher
    {
        public static bool ValidateRule(
            ExtractionRule rule,
            IDictionary<Guid, DynamicVariableDefinition> definitions,
            out string error)
        {
            error = string.Empty;
            if (rule == null)
            {
                error = "规则不能为空。";
                return false;
            }

            if (rule.PatternBytes == null || rule.PatternBytes.Length == 0)
            {
                error = "规则模板不能为空。";
                return false;
            }

            if (!Enum.IsDefined(typeof(Socket_Cache.SocketPacket.PacketType), rule.PacketType))
            {
                error = "规则封包方向无效。";
                return false;
            }

            if (rule.WildcardMask == null || rule.WildcardMask.Length != rule.PatternBytes.Length)
            {
                error = "规则通配掩码长度必须与模板一致。";
                return false;
            }

            bool hasFixedByte = false;
            for (int i = 0; i < rule.WildcardMask.Length; i++)
            {
                if (rule.WildcardMask[i] == 0)
                {
                    hasFixedByte = true;
                    break;
                }
            }

            if (!hasFixedByte)
            {
                error = "规则至少需要一个固定字节。";
                return false;
            }

            if (rule.Fields == null || rule.Fields.Count == 0)
            {
                error = "规则至少需要一个动态字段。";
                return false;
            }

            HashSet<Guid> fieldIds = new HashSet<Guid>();
            HashSet<Guid> variableIds = new HashSet<Guid>();
            for (int i = 0; i < rule.Fields.Count; i++)
            {
                DynamicField field = rule.Fields[i];
                if (field == null || field.Length <= 0 || field.Offset < 0 ||
                    (long)field.Offset + field.Length > rule.PatternBytes.Length)
                {
                    error = "动态字段范围越界或长度无效。";
                    return false;
                }

                if (field.FieldId == Guid.Empty || !fieldIds.Add(field.FieldId))
                {
                    error = "动态字段 ID 重复或为空。";
                    return false;
                }
                if (!variableIds.Add(field.VariableId))
                {
                    error = "同一条规则不能重复提取同一个变量。";
                    return false;
                }

                for (int j = i + 1; j < rule.Fields.Count; j++)
                {
                    DynamicField other = rule.Fields[j];
                    if (other != null && DynamicVariableRange.Overlaps(
                        field.Offset, field.Length, other.Offset, other.Length))
                    {
                        error = "动态字段不能互相重叠。";
                        return false;
                    }
                }

                if (definitions != null)
                {
                    DynamicVariableDefinition definition;
                    if (!definitions.TryGetValue(field.VariableId, out definition) || definition == null)
                    {
                        error = "动态字段引用的变量不存在。";
                        return false;
                    }

                    if (definition.Length != field.Length)
                    {
                        error = string.Format("变量 {0} 的长度必须为 {1} 字节。", definition.Symbol, field.Length);
                        return false;
                    }
                }

                for (int position = field.Offset; position < field.Offset + field.Length; position++)
                {
                    if (rule.WildcardMask[position] == 0)
                    {
                        error = "动态字段对应的模板位置必须是通配字节。";
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool Matches(ExtractionRule rule, byte[] buffer)
        {
            if (rule == null || buffer == null || rule.PatternBytes == null ||
                rule.WildcardMask == null || buffer.Length != rule.PatternBytes.Length ||
                rule.WildcardMask.Length != rule.PatternBytes.Length)
            {
                return false;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                if (rule.WildcardMask[i] == 0 && rule.PatternBytes[i] != buffer[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static class VariableExtractor
    {
        internal static List<DynamicVariableUpdate> ExtractMatched(ExtractionRule rule, byte[] buffer)
        {
            List<DynamicVariableUpdate> updates = new List<DynamicVariableUpdate>();
            if (rule == null || buffer == null || rule.Fields == null)
            {
                return updates;
            }

            DateTime observed = DateTime.UtcNow;
            foreach (DynamicField field in rule.Fields)
            {
                byte[] value = new byte[field.Length];
                Buffer.BlockCopy(buffer, field.Offset, value, 0, field.Length);
                updates.Add(new DynamicVariableUpdate
                {
                    VariableId = field.VariableId,
                    Value = value,
                    SourceRuleId = rule.RuleId,
                    ObservedUtc = observed
                });
            }
            return updates;
        }

        public static bool TryExtract(
            ExtractionRule rule,
            byte[] buffer,
            IDictionary<Guid, DynamicVariableDefinition> definitions,
            out List<DynamicVariableUpdate> updates,
            out string error)
        {
            updates = new List<DynamicVariableUpdate>();
            if (!PatternMatcher.ValidateRule(rule, definitions, out error) ||
                !PatternMatcher.Matches(rule, buffer))
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "封包不匹配规则。";
                }
                return false;
            }

            DateTime observed = DateTime.UtcNow;
            foreach (DynamicField field in rule.Fields)
            {
                byte[] value = new byte[field.Length];
                Buffer.BlockCopy(buffer, field.Offset, value, 0, field.Length);
                updates.Add(new DynamicVariableUpdate
                {
                    VariableId = field.VariableId,
                    Value = value,
                    SourceRuleId = rule.RuleId,
                    ObservedUtc = observed
                });
            }

            return true;
        }
    }

    internal sealed class DynamicRuleSnapshot
    {
        private readonly Dictionary<string, List<CompiledExtractionRule>> groups;

        public DynamicRuleSnapshot(IEnumerable<ExtractionRule> rules, IDictionary<Guid, DynamicVariableDefinition> definitions, ISet<Guid> disabledRuleIds)
        {
            groups = new Dictionary<string, List<CompiledExtractionRule>>(StringComparer.Ordinal);
            foreach (ExtractionRule rule in rules ?? Enumerable.Empty<ExtractionRule>())
            {
                if (rule == null || !rule.IsEnabled || rule.RuleId == Guid.Empty ||
                    (disabledRuleIds != null && disabledRuleIds.Contains(rule.RuleId)))
                {
                    continue;
                }

                string error;
                if (!PatternMatcher.ValidateRule(rule, definitions, out error))
                {
                    continue;
                }

                if ((rule.Fields ?? new List<DynamicField>()).Any(field =>
                    !definitions.ContainsKey(field.VariableId) ||
                    !definitions[field.VariableId].IsAutoUpdateEnabled))
                {
                    continue;
                }

                string key = MakeKey(rule.PacketType, rule.PatternBytes.Length);
                List<CompiledExtractionRule> list;
                if (!groups.TryGetValue(key, out list))
                {
                    list = new List<CompiledExtractionRule>();
                    groups.Add(key, list);
                }
                list.Add(new CompiledExtractionRule(rule));
            }
        }

        public IEnumerable<CompiledExtractionRule> Get(Socket_Cache.SocketPacket.PacketType packetType, int length)
        {
            List<CompiledExtractionRule> list;
            return groups.TryGetValue(MakeKey(packetType, length), out list)
                ? list
                : Enumerable.Empty<CompiledExtractionRule>();
        }

        private static string MakeKey(Socket_Cache.SocketPacket.PacketType packetType, int length)
        {
            return ((int)packetType).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public sealed class VariableStore
    {
        private readonly object sync = new object();
        private readonly Dictionary<Guid, DynamicVariableDefinition> definitions = new Dictionary<Guid, DynamicVariableDefinition>();
        private readonly Dictionary<Guid, ExtractionRule> rules = new Dictionary<Guid, ExtractionRule>();
        private readonly Dictionary<Guid, CurrentVariableValue> current = new Dictionary<Guid, CurrentVariableValue>();
        private readonly HashSet<Guid> sessionDisabledRuleIds = new HashSet<Guid>();
        private volatile DynamicRuleSnapshot ruleSnapshot = new DynamicRuleSnapshot(null, null, null);

        public event EventHandler Changed;

        public void Load(IEnumerable<DynamicVariableDefinition> loadedDefinitions, IEnumerable<ExtractionRule> loadedRules)
        {
            lock (sync)
            {
                definitions.Clear();
                rules.Clear();
                current.Clear();
                sessionDisabledRuleIds.Clear();
                HashSet<string> loadedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<Guid> loadedDefinitionIds = new HashSet<Guid>();
                foreach (DynamicVariableDefinition definition in loadedDefinitions ?? Enumerable.Empty<DynamicVariableDefinition>())
                {
                    if (definition == null || definition.VariableId == Guid.Empty || definition.Length <= 0)
                    {
                        continue;
                    }
                    string symbol = DynamicVariableNames.NormalizeSymbol(definition.Symbol);
                    if (symbol == null || !loadedDefinitionIds.Add(definition.VariableId) || !loadedSymbols.Add(symbol))
                    {
                        continue;
                    }
                    DynamicVariableDefinition clone = definition.Clone();
                    clone.Symbol = symbol;
                    clone.DisplayName = clone.DisplayName ?? string.Empty;
                    clone.Description = clone.Description ?? string.Empty;
                    definitions[clone.VariableId] = clone;
                }
                HashSet<Guid> loadedSourceVariables = new HashSet<Guid>();
                foreach (ExtractionRule rule in loadedRules ?? Enumerable.Empty<ExtractionRule>())
                {
                    if (rule != null && rule.RuleId != Guid.Empty)
                    {
                        ExtractionRule clone = rule.Clone();
                        string ruleError;
                        if (!PatternMatcher.ValidateRule(clone, definitions, out ruleError))
                        {
                            continue;
                        }
                        bool sourceConflict = (clone.Fields ?? new List<DynamicField>())
                            .Any(field => field == null ||
                                loadedSourceVariables.Contains(field.VariableId));
                        if (sourceConflict)
                        {
                            continue;
                        }
                        rules[clone.RuleId] = clone;
                        foreach (DynamicField field in clone.Fields ?? new List<DynamicField>())
                        {
                            loadedSourceVariables.Add(field.VariableId);
                        }
                    }
                }
                RebuildSnapshotLocked();
            }
            RaiseChanged();
        }

        public bool AddOrUpdateDefinition(DynamicVariableDefinition definition, out string error)
        {
            error = string.Empty;
            if (definition == null || definition.VariableId == Guid.Empty)
            {
                error = "变量定义无效。";
                return false;
            }

            string symbol = DynamicVariableNames.NormalizeSymbol(definition.Symbol);
            if (symbol == null)
            {
                error = "变量符号只能包含字母、数字和下划线。";
                return false;
            }
            if (definition.Length <= 0)
            {
                error = "变量长度必须大于零。";
                return false;
            }

            lock (sync)
            {
                DynamicVariableDefinition existingDefinition;
                if (definitions.TryGetValue(definition.VariableId, out existingDefinition) &&
                    existingDefinition.Length != definition.Length &&
                    rules.Values.Any(rule => (rule.Fields ?? new List<DynamicField>())
                        .Any(field => field != null && field.VariableId == definition.VariableId)))
                {
                    error = "变量已经被提取规则引用，不能直接修改长度。";
                    return false;
                }
                foreach (DynamicVariableDefinition other in definitions.Values)
                {
                    if (other.VariableId != definition.VariableId &&
                        string.Equals(other.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "变量符号已经存在。";
                        return false;
                    }
                }

                DynamicVariableDefinition normalized = definition.Clone();
                normalized.Symbol = symbol;
                normalized.DisplayName = normalized.DisplayName ?? string.Empty;
                normalized.Description = normalized.Description ?? string.Empty;
                if (existingDefinition != null && existingDefinition.Length != normalized.Length)
                {
                    current.Remove(normalized.VariableId);
                }
                definitions[normalized.VariableId] = normalized;
                RebuildSnapshotLocked();
            }
            RaiseChanged();
            return true;
        }

        public bool RemoveDefinition(Guid variableId, out string error)
        {
            error = string.Empty;
            lock (sync)
            {
                if (!definitions.Remove(variableId))
                {
                    error = "变量不存在。";
                    return false;
                }
                foreach (Guid ruleId in rules.Values
                    .Where(rule => (rule.Fields ?? new List<DynamicField>())
                        .Any(field => field != null && field.VariableId == variableId))
                    .Select(rule => rule.RuleId)
                    .ToList())
                {
                    rules.Remove(ruleId);
                }
                current.Remove(variableId);
                RebuildSnapshotLocked();
            }
            RaiseChanged();
            return true;
        }

        public bool AddOrUpdateRule(ExtractionRule rule, out string error)
        {
            error = string.Empty;
            lock (sync)
            {
                if (rule == null || rule.RuleId == Guid.Empty)
                {
                    error = "规则 ID 无效。";
                    return false;
                }
                if (!PatternMatcher.ValidateRule(rule, definitions, out error))
                {
                    return false;
                }

                Dictionary<Guid, Guid> sourceOwners = BuildSourceOwnersLocked(rule.RuleId);
                foreach (DynamicField field in rule.Fields)
                {
                    Guid owner;
                    if (sourceOwners.TryGetValue(field.VariableId, out owner) && owner != rule.RuleId)
                    {
                        DynamicVariableDefinition definition = definitions[field.VariableId];
                        error = string.Format("变量 {0} 已由另一条规则提供。", definition.Symbol);
                        return false;
                    }
                }

                ExtractionRule previous;
                if (rules.TryGetValue(rule.RuleId, out previous) && HasStructureChanged(previous, rule))
                {
                    foreach (Guid variableId in (previous.Fields ?? new List<DynamicField>())
                        .Concat(rule.Fields ?? new List<DynamicField>())
                        .Select(field => field.VariableId)
                        .Distinct()
                        .ToList())
                    {
                        current.Remove(variableId);
                    }
                }
                rules[rule.RuleId] = rule.Clone();
                RebuildSnapshotLocked();
            }
            RaiseChanged();
            return true;
        }

        public bool RemoveRule(Guid ruleId, out string error)
        {
            error = string.Empty;
            lock (sync)
            {
                if (!rules.Remove(ruleId))
                {
                    error = "规则不存在。";
                    return false;
                }
                sessionDisabledRuleIds.Remove(ruleId);
                foreach (Guid variableId in current.Values
                    .Where(value => value.SourceRuleId == ruleId)
                    .Select(value => value.VariableId)
                    .ToList())
                {
                    current.Remove(variableId);
                }
                RebuildSnapshotLocked();
            }
            RaiseChanged();
            return true;
        }

        public bool TryGetDefinition(Guid variableId, out DynamicVariableDefinition definition)
        {
            lock (sync)
            {
                DynamicVariableDefinition found;
                if (!definitions.TryGetValue(variableId, out found))
                {
                    definition = null;
                    return false;
                }
                definition = found.Clone();
                return true;
            }
        }

        public List<DynamicVariableDefinition> GetDefinitionsSnapshot()
        {
            lock (sync)
            {
                return definitions.Values.Select(item => item.Clone()).OrderBy(item => item.Symbol).ToList();
            }
        }

        public List<ExtractionRule> GetRulesSnapshot()
        {
            lock (sync)
            {
                return rules.Values.Select(item => item.Clone()).OrderBy(item => item.Name).ToList();
            }
        }

        public List<CurrentVariableValue> GetCurrentSnapshot()
        {
            lock (sync)
            {
                return definitions.Values.Select(definition =>
                {
                    CurrentVariableValue value;
                    if (current.TryGetValue(definition.VariableId, out value))
                    {
                        return value.Clone();
                    }
                    return new CurrentVariableValue { VariableId = definition.VariableId };
                }).ToList();
            }
        }

        public DynamicVariableSnapshot CaptureSnapshot()
        {
            lock (sync)
            {
                Dictionary<Guid, byte[]> values = current.Values
                    .Where(item => item != null && item.Value != null)
                    .ToDictionary(item => item.VariableId, item => (byte[])item.Value.Clone());
                return new DynamicVariableSnapshot(values, DateTime.UtcNow);
            }
        }

        internal IEnumerable<CompiledExtractionRule> GetMatchingRules(Socket_Cache.SocketPacket.PacketType packetType, int length)
        {
            DynamicRuleSnapshot snapshot = ruleSnapshot;
            return snapshot.Get(packetType, length);
        }

        internal bool ApplyUpdatesAtomically(IEnumerable<DynamicVariableUpdate> updates, out List<KnownVariableValue> historyItems)
        {
            historyItems = new List<KnownVariableValue>();
            List<DynamicVariableUpdate> list = (updates ?? Enumerable.Empty<DynamicVariableUpdate>()).ToList();
            if (list.Count == 0)
            {
                return false;
            }

            lock (sync)
            {
                Dictionary<Guid, DynamicVariableUpdate> unique = new Dictionary<Guid, DynamicVariableUpdate>();
                foreach (DynamicVariableUpdate update in list)
                {
                    DynamicVariableDefinition definition;
                    if (update == null || update.Value == null ||
                        !definitions.TryGetValue(update.VariableId, out definition) ||
                        definition.Length != update.Value.Length)
                    {
                        return false;
                    }
                    if (unique.ContainsKey(update.VariableId))
                    {
                        DynamicVariableUpdate previous = unique[update.VariableId];
                        if (!previous.Value.SequenceEqual(update.Value))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        unique.Add(update.VariableId, update);
                    }
                }

                foreach (DynamicVariableUpdate update in unique.Values)
                {
                    CurrentVariableValue value;
                    if (!current.TryGetValue(update.VariableId, out value))
                    {
                        value = new CurrentVariableValue { VariableId = update.VariableId };
                        current[update.VariableId] = value;
                    }
                    value.Value = (byte[])update.Value.Clone();
                    value.SourceRuleId = update.SourceRuleId;
                    value.UpdatedUtc = update.ObservedUtc == default(DateTime) ? DateTime.UtcNow : update.ObservedUtc;
                    historyItems.Add(new KnownVariableValue
                    {
                        VariableId = update.VariableId,
                        Value = (byte[])update.Value.Clone(),
                        SourceRuleId = update.SourceRuleId,
                        FirstSeenUtc = value.UpdatedUtc,
                        LastSeenUtc = value.UpdatedUtc,
                        SeenCount = 1
                    });
                }
            }

            return true;
        }

        public void ClearCurrent(Guid? variableId = null)
        {
            lock (sync)
            {
                if (variableId.HasValue)
                {
                    current.Remove(variableId.Value);
                }
                else
                {
                    current.Clear();
                }
            }
            RaiseChanged();
        }

        public bool SetRuleSessionEnabled(Guid ruleId, bool enabled)
        {
            lock (sync)
            {
                if (!rules.ContainsKey(ruleId))
                {
                    return false;
                }
                if (enabled)
                {
                    sessionDisabledRuleIds.Remove(ruleId);
                }
                else
                {
                    sessionDisabledRuleIds.Add(ruleId);
                }
                RebuildSnapshotLocked();
            }
            RaiseChanged();
            return true;
        }

        public bool IsRuleSessionEnabled(Guid ruleId)
        {
            lock (sync)
            {
                return rules.ContainsKey(ruleId) && !sessionDisabledRuleIds.Contains(ruleId);
            }
        }

        private Dictionary<Guid, Guid> BuildSourceOwnersLocked(Guid replacingRuleId)
        {
            Dictionary<Guid, Guid> owners = new Dictionary<Guid, Guid>();
            foreach (ExtractionRule existing in rules.Values)
            {
                if (existing.RuleId == replacingRuleId)
                {
                    continue;
                }
                foreach (DynamicField field in existing.Fields ?? new List<DynamicField>())
                {
                    if (field == null)
                    {
                        continue;
                    }
                    if (!owners.ContainsKey(field.VariableId))
                    {
                        owners.Add(field.VariableId, existing.RuleId);
                    }
                }
            }
            return owners;
        }

        private void RebuildSnapshotLocked()
        {
            ruleSnapshot = new DynamicRuleSnapshot(rules.Values, definitions, sessionDisabledRuleIds);
        }

        private static bool HasStructureChanged(ExtractionRule previous, ExtractionRule currentRule)
        {
            if (previous == null || currentRule == null ||
                previous.PacketType != currentRule.PacketType ||
                !(previous.PatternBytes ?? new byte[0]).SequenceEqual(currentRule.PatternBytes ?? new byte[0]) ||
                !(previous.WildcardMask ?? new byte[0]).SequenceEqual(currentRule.WildcardMask ?? new byte[0]))
            {
                return true;
            }
            List<DynamicField> oldFields = previous.Fields ?? new List<DynamicField>();
            List<DynamicField> newFields = currentRule.Fields ?? new List<DynamicField>();
            return oldFields.Count != newFields.Count || oldFields.Any(oldField =>
                oldField == null || !newFields.Any(newField => newField != null &&
                    oldField != null && newField.VariableId == oldField.VariableId &&
                    newField.Offset == oldField.Offset && newField.Length == oldField.Length));
        }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(nameof(VariableStore), ex.Message);
                }
            }
        }

        internal void NotifyChanged()
        {
            RaiseChanged();
        }
    }

    public sealed class KnownValueStore
    {
        private sealed class PendingObservation
        {
            public KnownVariableValue Value;
        }

        private readonly object sync = new object();
        private readonly Dictionary<string, KnownVariableValue> values = new Dictionary<string, KnownVariableValue>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<PendingObservation> pending = new ConcurrentQueue<PendingObservation>();
        private int pendingCount;
        private const int MaxPending = 4096;

        public void Load(IEnumerable<KnownVariableValue> loadedValues)
        {
            lock (sync)
            {
                values.Clear();
                foreach (KnownVariableValue value in loadedValues ?? Enumerable.Empty<KnownVariableValue>())
                {
                    if (value != null && value.Value != null)
                    {
                        values[MakeKey(value.VariableId, value.Value)] = value.Clone();
                    }
                }
            }
            while (pending.TryDequeue(out PendingObservation discarded))
            {
                Interlocked.Decrement(ref pendingCount);
            }
        }

        public void Enqueue(IEnumerable<KnownVariableValue> observations)
        {
            foreach (KnownVariableValue observation in observations ?? Enumerable.Empty<KnownVariableValue>())
            {
                if (observation == null || observation.Value == null ||
                    Volatile.Read(ref pendingCount) >= MaxPending)
                {
                    continue;
                }
                lock (sync)
                {
                    string key = MakeKey(observation.VariableId, observation.Value);
                    KnownVariableValue value;
                    if (!values.TryGetValue(key, out value))
                    {
                        value = observation.Clone();
                        value.SeenCount = 0;
                        values[key] = value;
                    }
                    if (value.FirstSeenUtc == default(DateTime) || observation.FirstSeenUtc < value.FirstSeenUtc)
                    {
                        value.FirstSeenUtc = observation.FirstSeenUtc;
                    }
                    value.LastSeenUtc = observation.LastSeenUtc;
                    value.SeenCount++;
                    value.SourceRuleId = observation.SourceRuleId;
                }
                if (Interlocked.Increment(ref pendingCount) <= MaxPending)
                {
                    pending.Enqueue(new PendingObservation { Value = observation.Clone() });
                }
                else
                {
                    Interlocked.Decrement(ref pendingCount);
                    break;
                }
            }
        }

        public List<KnownVariableValue> GetSnapshot()
        {
            lock (sync)
            {
                return values.Values.Select(item => item.Clone())
                    .OrderByDescending(item => item.LastSeenUtc)
                    .ToList();
            }
        }

        public bool SetLabel(Guid variableId, byte[] value, string label)
        {
            lock (sync)
            {
                KnownVariableValue item;
                if (!values.TryGetValue(MakeKey(variableId, value), out item))
                {
                    return false;
                }
                item.Label = label ?? string.Empty;
                return true;
            }
        }

        public int FlushPending(Action<IList<KnownVariableValue>> writer)
        {
            Dictionary<string, KnownVariableValue> batch = new Dictionary<string, KnownVariableValue>(StringComparer.Ordinal);
            PendingObservation item;
            while (pending.TryDequeue(out item))
            {
                Interlocked.Decrement(ref pendingCount);
                if (item != null && item.Value != null)
                {
                    string key = MakeKey(item.Value.VariableId, item.Value.Value);
                    KnownVariableValue aggregate;
                    if (!batch.TryGetValue(key, out aggregate))
                    {
                        aggregate = item.Value.Clone();
                        batch[key] = aggregate;
                    }
                    else
                    {
                        aggregate.LastSeenUtc = item.Value.LastSeenUtc;
                        aggregate.SeenCount++;
                        aggregate.SourceRuleId = item.Value.SourceRuleId;
                    }
                }
            }

            if (batch.Count > 0 && writer != null)
            {
                writer(batch.Values.Select(value => value.Clone()).ToList());
            }
            return batch.Count;
        }

        public int PendingCount { get { return Volatile.Read(ref pendingCount); } }

        private static string MakeKey(Guid variableId, byte[] value)
        {
            return variableId.ToString("N") + ":" + DynamicVariableFormatting.ToHex(value);
        }
    }

    public static class VariableResolver
    {
        public static bool TryResolveBuffer(
            byte[] source,
            IEnumerable<PresetVariableBinding> bindings,
            DynamicVariableSnapshot snapshot,
            Func<Guid, DynamicVariableDefinition> definitionResolver,
            out byte[] resolved,
            out string error)
        {
            Guid ignoredVariableId;
            return TryResolveBuffer(
                source,
                bindings,
                snapshot,
                definitionResolver,
                out resolved,
                out error,
                out ignoredVariableId);
        }

        public static bool TryResolveBuffer(
            byte[] source,
            IEnumerable<PresetVariableBinding> bindings,
            DynamicVariableSnapshot snapshot,
            Func<Guid, DynamicVariableDefinition> definitionResolver,
            out byte[] resolved,
            out string error,
            out Guid failedVariableId)
        {
            resolved = source == null ? null : (byte[])source.Clone();
            error = string.Empty;
            failedVariableId = Guid.Empty;
            if (resolved == null)
            {
                error = "预设封包为空。";
                return false;
            }

            List<PresetVariableBinding> list = (bindings ?? Enumerable.Empty<PresetVariableBinding>()).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                PresetVariableBinding binding = list[i];
                failedVariableId = binding == null ? Guid.Empty : binding.VariableId;
                if (binding == null || binding.Offset < 0 || binding.Length <= 0 ||
                    (long)binding.Offset + binding.Length > resolved.Length)
                {
                    error = "变量绑定范围无效。";
                    return false;
                }
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (list[j] != null && DynamicVariableRange.Overlaps(
                        binding.Offset, binding.Length, list[j].Offset, list[j].Length))
                    {
                        error = "变量绑定范围互相重叠。";
                        return false;
                    }
                }

                DynamicVariableDefinition definition = definitionResolver == null ? null : definitionResolver(binding.VariableId);
                if (definition == null)
                {
                    error = "绑定变量不存在。";
                    return false;
                }
                if (definition.Length != binding.Length)
                {
                    error = string.Format("变量 {0} 的绑定长度不一致。", definition.Symbol);
                    return false;
                }

                byte[] value;
                if (snapshot == null || !snapshot.TryGetValue(binding.VariableId, out value) || value.Length != binding.Length)
                {
                    error = string.Format("变量 {0} 当前尚未获取。", definition.Symbol);
                    return false;
                }
                Buffer.BlockCopy(value, 0, resolved, binding.Offset, binding.Length);
            }
            failedVariableId = Guid.Empty;
            return true;
        }

        public static bool HasRangeConflict(IEnumerable<PresetVariableBinding> bindings, int offset, int length)
        {
            return (bindings ?? Enumerable.Empty<PresetVariableBinding>())
                .Any(item => item != null && DynamicVariableRange.Overlaps(item.Offset, item.Length, offset, length));
        }
    }

    public static class DynamicVariableRuntime
    {
        private static readonly object sync = new object();
        private static readonly VariableStore variableStore = new VariableStore();
        private static readonly KnownValueStore knownValueStore = new KnownValueStore();
        private static Timer historyFlushTimer;
        private static int initialized;
        private static int databaseLoadCompleted;

        public static VariableStore Variables { get { return variableStore; } }
        public static KnownValueStore KnownValues { get { return knownValueStore; } }
        public static event EventHandler Changed;

        public static void InitializeFromDatabase()
        {
            if (Interlocked.Exchange(ref initialized, 1) != 0)
            {
                return;
            }

            try
            {
                List<DynamicVariableDefinition> definitions;
                List<ExtractionRule> rules;
                List<KnownVariableValue> values;
                bool loadedFromDatabase = Socket_Cache.DataBase.LoadDynamicVariableData(
                    out definitions,
                    out rules,
                    out values);
                if (!loadedFromDatabase)
                {
                    definitions = new List<DynamicVariableDefinition>();
                    rules = new List<ExtractionRule>();
                    values = new List<KnownVariableValue>();
                }
                variableStore.Load(definitions, rules);
                Dictionary<Guid, DynamicVariableDefinition> loadedDefinitions = variableStore
                    .GetDefinitionsSnapshot()
                    .ToDictionary(item => item.VariableId);
                knownValueStore.Load((values ?? new List<KnownVariableValue>())
                    .Where(value => value != null && value.Value != null &&
                        loadedDefinitions.ContainsKey(value.VariableId) &&
                        loadedDefinitions[value.VariableId].Length == value.Value.Length));
                if (loadedFromDatabase)
                {
                    variableStore.Changed += VariableStore_Changed;
                    historyFlushTimer = new Timer(FlushHistoryTimer, null, 2000, 2000);
                    Volatile.Write(ref databaseLoadCompleted, 1);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(InitializeFromDatabase), ex.Message);
            }
        }

        public static void ProcessPacket(Socket_Cache.SocketPacket.PacketType packetType, byte[] buffer)
        {
            try
            {
                if (buffer == null || buffer.Length == 0)
                {
                    return;
                }

                List<DynamicVariableUpdate> updates = new List<DynamicVariableUpdate>();
                foreach (CompiledExtractionRule compiled in variableStore.GetMatchingRules(packetType, buffer.Length))
                {
                    if (!compiled.IsMatch(buffer))
                    {
                        continue;
                    }
                    List<DynamicVariableUpdate> extracted = VariableExtractor.ExtractMatched(compiled.Rule, buffer);
                    if (extracted.Count > 0)
                    {
                        updates.AddRange(extracted);
                    }
                }

                List<KnownVariableValue> history;
                if (variableStore.ApplyUpdatesAtomically(updates, out history))
                {
                    try
                    {
                        knownValueStore.Enqueue(history);
                    }
                    finally
                    {
                        variableStore.NotifyChanged();
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(ProcessPacket), ex.Message);
            }
        }

        public static DynamicVariableSnapshot CaptureSnapshot()
        {
            return variableStore.CaptureSnapshot();
        }

        public static bool TryGetDefinition(Guid variableId, out DynamicVariableDefinition definition)
        {
            return variableStore.TryGetDefinition(variableId, out definition);
        }

        public static List<DynamicVariableDisplayRange> GetDisplayRanges(
            Socket_Cache.SocketPacket.PacketType packetType,
            byte[] buffer)
        {
            List<DynamicVariableDisplayRange> result = new List<DynamicVariableDisplayRange>();
            if (buffer == null)
            {
                return result;
            }
            Dictionary<Guid, DynamicVariableDefinition> definitions = variableStore.GetDefinitionsSnapshot()
                .ToDictionary(item => item.VariableId);
            foreach (ExtractionRule rule in variableStore.GetRulesSnapshot())
            {
                string ruleError;
                if (rule == null || !PatternMatcher.ValidateRule(rule, definitions, out ruleError))
                {
                    continue;
                }
                if (rule == null || rule.PacketType != packetType || rule.PatternBytes == null ||
                    rule.PatternBytes.Length != buffer.Length ||
                    !PatternMatcher.Matches(rule, buffer))
                {
                    continue;
                }
                foreach (DynamicField field in rule.Fields ?? new List<DynamicField>())
                {
                    DynamicVariableDefinition definition;
                    if (definitions.TryGetValue(field.VariableId, out definition))
                    {
                        result.Add(new DynamicVariableDisplayRange
                        {
                            Offset = field.Offset,
                            Length = field.Length,
                            VariableId = field.VariableId,
                            Symbol = definition.Symbol,
                            DisplayName = definition.DisplayName,
                            RuleName = rule.Name
                        });
                    }
                }
            }
            return result;
        }

        public static bool SaveToDatabase()
        {
            if (Volatile.Read(ref databaseLoadCompleted) == 0)
            {
                Socket_Operation.DoLog(nameof(SaveToDatabase), "动态变量数据库尚未完成加载，跳过保存以避免覆盖现有数据。");
                return false;
            }
            try
            {
                FlushHistory();
                bool saved = Socket_Cache.DataBase.SaveDynamicVariableData(
                    variableStore.GetDefinitionsSnapshot(),
                    variableStore.GetRulesSnapshot(),
                    knownValueStore.GetSnapshot());
                if (!saved)
                {
                    Socket_Operation.DoLog(nameof(SaveToDatabase), "动态变量保存失败，数据库原内容已保留。");
                }
                return saved;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(SaveToDatabase), ex.Message);
                return false;
            }
        }

        public static XElement ExportToXElement()
        {
            FlushHistory();
            return DynamicVariableSerialization.ToBackupElement(
                variableStore.GetDefinitionsSnapshot(),
                variableStore.GetRulesSnapshot(),
                knownValueStore.GetSnapshot());
        }

        public static bool ImportFromXElement(XElement element, out string error)
        {
            error = string.Empty;
            List<DynamicVariableDefinition> definitions;
            List<ExtractionRule> rules;
            List<KnownVariableValue> knownValues;
            if (!DynamicVariableSerialization.FromBackupElement(element, out definitions, out rules, out knownValues))
            {
                error = "动态变量备份格式无效。";
                return false;
            }
            HashSet<Guid> definitionIds = new HashSet<Guid>();
            HashSet<string> symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DynamicVariableDefinition definition in definitions)
            {
                if (definition == null || definition.VariableId == Guid.Empty || definition.Length <= 0)
                {
                    error = "动态变量定义无效。";
                    return false;
                }
                string normalizedSymbol = DynamicVariableNames.NormalizeSymbol(definition.Symbol);
                if (normalizedSymbol == null)
                {
                    error = "动态变量符号无效。";
                    return false;
                }
                if (!definitionIds.Add(definition.VariableId) || !symbols.Add(normalizedSymbol))
                {
                    error = "动态变量 ID 或符号重复。";
                    return false;
                }
                definition.Symbol = normalizedSymbol;
                definition.DisplayName = definition.DisplayName ?? string.Empty;
                definition.Description = definition.Description ?? string.Empty;
            }
            Dictionary<Guid, DynamicVariableDefinition> definitionMap = definitions
                .Where(item => item != null && item.VariableId != Guid.Empty)
                .ToDictionary(item => item.VariableId);
            HashSet<Guid> ruleIds = new HashSet<Guid>();
            HashSet<Guid> fieldIds = new HashSet<Guid>();
            HashSet<Guid> sourceVariableIds = new HashSet<Guid>();
            foreach (ExtractionRule rule in rules)
            {
                if (rule == null || rule.RuleId == Guid.Empty || !ruleIds.Add(rule.RuleId) ||
                    !Enum.IsDefined(typeof(Socket_Cache.SocketPacket.PacketType), rule.PacketType))
                {
                    error = "动态变量提取规则 ID 或方向无效。";
                    return false;
                }
                if (!PatternMatcher.ValidateRule(rule, definitionMap, out error))
                {
                    return false;
                }
                foreach (DynamicField field in rule.Fields ?? new List<DynamicField>())
                {
                    if (!fieldIds.Add(field.FieldId) || !sourceVariableIds.Add(field.VariableId))
                    {
                        error = "动态变量字段 ID 或提取来源重复。";
                        return false;
                    }
                }
            }
            knownValues = knownValues
                .Where(value => value != null && value.Value != null &&
                    definitionMap.ContainsKey(value.VariableId) &&
                    definitionMap[value.VariableId].Length == value.Value.Length)
                .Select(value => value.Clone())
                .ToList();
            variableStore.Load(definitions, rules);
            knownValueStore.Load(knownValues);
            return true;
        }

        public static void FlushHistory()
        {
            if (Volatile.Read(ref databaseLoadCompleted) == 0)
            {
                return;
            }
            knownValueStore.FlushPending(items => Socket_Cache.DataBase.UpsertDynamicKnownValues(items));
        }

        private static void FlushHistoryTimer(object state)
        {
            try
            {
                FlushHistory();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(FlushHistoryTimer), ex.Message);
            }
        }

        private static void VariableStore_Changed(object sender, EventArgs e)
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(null, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(nameof(DynamicVariableRuntime), ex.Message);
                }
            }
        }
    }
}
