using System;
using System.Collections.Generic;
using System.Linq;

namespace WPELibrary.Lib
{
    public static class DynamicVariableNames
    {
        public static string NormalizeSymbol(string value)
        {
            string text = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (text.Length == 0 || text.Length > 64)
            {
                return null;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (!((ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9') || ch == '_'))
                {
                    return null;
                }
            }

            return text;
        }
    }

    public static class DynamicVariableRange
    {
        public static bool TryFromSelection(
            long selectionStart,
            long selectionLength,
            long bufferLength,
            out int offset,
            out int length)
        {
            offset = 0;
            length = 0;
            if (bufferLength <= 0 || selectionStart < 0 || selectionStart >= bufferLength)
            {
                return false;
            }

            long normalizedLength = selectionLength <= 0 ? 1 : selectionLength;
            if (normalizedLength > bufferLength - selectionStart || normalizedLength > int.MaxValue)
            {
                return false;
            }

            offset = (int)selectionStart;
            length = (int)normalizedLength;
            return true;
        }

        public static bool TryFromEndpoints(
            long first,
            long last,
            long bufferLength,
            out int offset,
            out int length)
        {
            offset = 0;
            length = 0;
            if (bufferLength <= 0 || first < 0 || last < 0 || first >= bufferLength || last >= bufferLength)
            {
                return false;
            }

            long start = Math.Min(first, last);
            long end = Math.Max(first, last);
            long span = end - start + 1;
            if (span <= 0 || span > int.MaxValue)
            {
                return false;
            }

            offset = (int)start;
            length = (int)span;
            return true;
        }

        public static bool Overlaps(int leftOffset, int leftLength, int rightOffset, int rightLength)
        {
            if (leftLength <= 0 || rightLength <= 0)
            {
                return false;
            }

            long leftEnd = (long)leftOffset + leftLength;
            long rightEnd = (long)rightOffset + rightLength;
            return leftOffset < rightEnd && rightOffset < leftEnd;
        }
    }

    public sealed class DynamicVariableDefinition
    {
        public Guid VariableId { get; set; }
        public string Symbol { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int Length { get; set; }
        public bool IsAutoUpdateEnabled { get; set; }

        public DynamicVariableDefinition Clone()
        {
            return new DynamicVariableDefinition
            {
                VariableId = VariableId,
                Symbol = Symbol,
                DisplayName = DisplayName,
                Description = Description,
                Length = Length,
                IsAutoUpdateEnabled = IsAutoUpdateEnabled
            };
        }

        public override string ToString()
        {
            string label = string.IsNullOrWhiteSpace(DisplayName) ? Symbol : DisplayName;
            return string.Format("{0} ({1}, {2} 字节)", label, Symbol, Length);
        }
    }

    public sealed class DynamicField
    {
        public Guid FieldId { get; set; }
        public Guid VariableId { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public string Description { get; set; }
        public long End { get { return (long)Offset + Length; } }

        public DynamicField Clone()
        {
            return new DynamicField
            {
                FieldId = FieldId,
                VariableId = VariableId,
                Offset = Offset,
                Length = Length,
                Description = Description
            };
        }
    }

    public sealed class ExtractionRule
    {
        public Guid RuleId { get; set; }
        public string Name { get; set; }
        public bool IsEnabled { get; set; }
        public byte[] PatternBytes { get; set; }
        public byte[] WildcardMask { get; set; }
        public Socket_Cache.SocketPacket.PacketType PacketType { get; set; }
        public List<DynamicField> Fields { get; set; } = new List<DynamicField>();

        public string OriginalTemplate
        {
            get { return DynamicVariableFormatting.FormatTemplate(PatternBytes, WildcardMask); }
        }

        public ExtractionRule Clone()
        {
            return new ExtractionRule
            {
                RuleId = RuleId,
                Name = Name,
                IsEnabled = IsEnabled,
                PatternBytes = PatternBytes == null ? null : (byte[])PatternBytes.Clone(),
                WildcardMask = WildcardMask == null ? null : (byte[])WildcardMask.Clone(),
                PacketType = PacketType,
                Fields = (Fields ?? new List<DynamicField>()).Select(item => item.Clone()).ToList()
            };
        }
    }

    public sealed class KnownVariableValue
    {
        public Guid VariableId { get; set; }
        public byte[] Value { get; set; }
        public string Label { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public long SeenCount { get; set; }
        public Guid SourceRuleId { get; set; }

        public string ValueHex
        {
            get { return DynamicVariableFormatting.ToHex(Value); }
        }

        public KnownVariableValue Clone()
        {
            return new KnownVariableValue
            {
                VariableId = VariableId,
                Value = Value == null ? null : (byte[])Value.Clone(),
                Label = Label,
                FirstSeenUtc = FirstSeenUtc,
                LastSeenUtc = LastSeenUtc,
                SeenCount = SeenCount,
                SourceRuleId = SourceRuleId
            };
        }
    }

    public sealed class PresetVariableBinding
    {
        public Guid VariableId { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public long End { get { return (long)Offset + Length; } }

        public PresetVariableBinding Clone()
        {
            return new PresetVariableBinding
            {
                VariableId = VariableId,
                Offset = Offset,
                Length = Length
            };
        }
    }

    public sealed class CurrentVariableValue
    {
        public Guid VariableId { get; set; }
        public byte[] Value { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public Guid SourceRuleId { get; set; }
        public string Label { get; set; }

        public bool IsValid { get { return Value != null; } }

        public CurrentVariableValue Clone()
        {
            return new CurrentVariableValue
            {
                VariableId = VariableId,
                Value = Value == null ? null : (byte[])Value.Clone(),
                UpdatedUtc = UpdatedUtc,
                SourceRuleId = SourceRuleId,
                Label = Label
            };
        }
    }

    public sealed class DynamicVariableUpdate
    {
        public Guid VariableId { get; set; }
        public byte[] Value { get; set; }
        public Guid SourceRuleId { get; set; }
        public DateTime ObservedUtc { get; set; }
    }

    public sealed class DynamicVariableSnapshot
    {
        private readonly Dictionary<Guid, byte[]> values;

        internal DynamicVariableSnapshot(IDictionary<Guid, byte[]> source, DateTime capturedUtc)
        {
            values = new Dictionary<Guid, byte[]>();
            if (source != null)
            {
                foreach (KeyValuePair<Guid, byte[]> item in source)
                {
                    values[item.Key] = item.Value == null ? null : (byte[])item.Value.Clone();
                }
            }

            CapturedUtc = capturedUtc;
        }

        public DateTime CapturedUtc { get; private set; }

        public IReadOnlyDictionary<Guid, byte[]> Values
        {
            get
            {
                Dictionary<Guid, byte[]> copy = new Dictionary<Guid, byte[]>();
                foreach (KeyValuePair<Guid, byte[]> item in values)
                {
                    copy[item.Key] = item.Value == null ? null : (byte[])item.Value.Clone();
                }
                return copy;
            }
        }

        public bool TryGetValue(Guid variableId, out byte[] value)
        {
            byte[] stored;
            if (!values.TryGetValue(variableId, out stored) || stored == null)
            {
                value = null;
                return false;
            }

            value = (byte[])stored.Clone();
            return true;
        }
    }

    public sealed class DynamicVariableDisplayRange
    {
        public int Offset { get; set; }
        public int Length { get; set; }
        public Guid VariableId { get; set; }
        public string Symbol { get; set; }
        public string DisplayName { get; set; }
        public string RuleName { get; set; }
        public long End { get { return (long)Offset + Length; } }
    }

    internal static class DynamicVariableFormatting
    {
        public static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(" ", bytes.Select(value => value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
        }

        public static byte[] ParseHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new byte[0];
            }
            string[] parts = value.Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            byte[] result = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                byte parsed;
                if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    return new byte[0];
                }
                result[i] = parsed;
            }
            return result;
        }

        public static string FormatTemplate(byte[] pattern, byte[] mask)
        {
            if (pattern == null || pattern.Length == 0)
            {
                return string.Empty;
            }

            string[] parts = new string[pattern.Length];
            for (int i = 0; i < pattern.Length; i++)
            {
                bool wildcard = mask != null && i < mask.Length && mask[i] != 0;
                parts[i] = wildcard ? "??" : pattern[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
            }

            return string.Join(" ", parts);
        }

        public static string ToInvariantDate(DateTime value)
        {
            return value.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static DateTime ParseDate(object value, DateTime fallback)
        {
            DateTime parsed;
            return DateTime.TryParse(
                value == null || value == DBNull.Value ? string.Empty : value.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out parsed)
                ? parsed.ToUniversalTime()
                : fallback;
        }
    }
}
