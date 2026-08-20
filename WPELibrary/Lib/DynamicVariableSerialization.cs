using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml.Linq;

namespace WPELibrary.Lib
{
    public sealed class DynamicVariableCompositeStyleProvider : Be.Windows.Forms.IByteStyleProvider
    {
        private readonly IList<Socket_ByteAnnotationInfo> annotations;
        private readonly IList<DynamicVariableDisplayRange> dynamicRanges;

        public DynamicVariableCompositeStyleProvider(
            IList<Socket_ByteAnnotationInfo> annotations,
            IList<DynamicVariableDisplayRange> dynamicRanges)
        {
            this.annotations = annotations;
            this.dynamicRanges = dynamicRanges;
        }

        public bool TryGetStyle(long index, out Be.Windows.Forms.ByteStyle style)
        {
            Socket_ByteAnnotationInfo annotation = annotations == null
                ? null
                : annotations.FirstOrDefault(item => item != null && index >= item.Start && index < item.End);
            DynamicVariableDisplayRange variable = dynamicRanges == null
                ? null
                : dynamicRanges.FirstOrDefault(item => item != null && index >= item.Offset && index < item.End);

            if (annotation == null && variable == null)
            {
                style = new Be.Windows.Forms.ByteStyle();
                return false;
            }

            Color background = annotation == null
                ? Color.FromArgb(205, 235, 255)
                : Socket_ByteAnnotationEngine.GetBackColor(annotation.Color);
            if (variable != null)
            {
                Color variableColor = Color.FromArgb(205, 235, 255);
                background = annotation == null ? variableColor : Blend(background, variableColor);
            }
            style = new Be.Windows.Forms.ByteStyle(Color.FromArgb(31, 31, 31), background);
            return true;
        }

        private static Color Blend(Color first, Color second)
        {
            return Color.FromArgb(
                (first.R + second.R) / 2,
                (first.G + second.G) / 2,
                (first.B + second.B) / 2);
        }
    }

    internal static class DynamicVariableSerialization
    {
        public static XElement ToBackupElement(
            IEnumerable<DynamicVariableDefinition> definitions,
            IEnumerable<ExtractionRule> rules,
            IEnumerable<KnownVariableValue> knownValues)
        {
            XElement root = new XElement("DynamicVariables", new XAttribute("Version", "1"));
            XElement definitionsElement = new XElement("Definitions");
            foreach (DynamicVariableDefinition definition in definitions ?? Enumerable.Empty<DynamicVariableDefinition>())
            {
                definitionsElement.Add(new XElement("Definition",
                    new XAttribute("VariableId", definition.VariableId.ToString("N")),
                    new XAttribute("Symbol", definition.Symbol ?? string.Empty),
                    new XAttribute("DisplayName", definition.DisplayName ?? string.Empty),
                    new XAttribute("Description", definition.Description ?? string.Empty),
                    new XAttribute("Length", definition.Length),
                    new XAttribute("AutoUpdate", definition.IsAutoUpdateEnabled)));
            }
            root.Add(definitionsElement);

            XElement rulesElement = new XElement("Rules");
            foreach (ExtractionRule rule in rules ?? Enumerable.Empty<ExtractionRule>())
            {
                XElement ruleElement = new XElement("Rule",
                    new XAttribute("RuleId", rule.RuleId.ToString("N")),
                    new XAttribute("Name", rule.Name ?? string.Empty),
                    new XAttribute("Enabled", rule.IsEnabled),
                    new XAttribute("PacketType", (int)rule.PacketType),
                    new XAttribute("Pattern", DynamicVariableFormatting.ToHex(rule.PatternBytes)),
                    new XAttribute("WildcardMask", DynamicVariableFormatting.ToHex(rule.WildcardMask)));
                XElement fields = new XElement("Fields");
                foreach (DynamicField field in rule.Fields ?? new List<DynamicField>())
                {
                    fields.Add(new XElement("Field",
                        new XAttribute("FieldId", field.FieldId.ToString("N")),
                        new XAttribute("VariableId", field.VariableId.ToString("N")),
                        new XAttribute("Offset", field.Offset),
                        new XAttribute("Length", field.Length),
                        new XAttribute("Description", field.Description ?? string.Empty)));
                }
                ruleElement.Add(fields);
                rulesElement.Add(ruleElement);
            }
            root.Add(rulesElement);

            XElement valuesElement = new XElement("KnownValues");
            foreach (KnownVariableValue value in knownValues ?? Enumerable.Empty<KnownVariableValue>())
            {
                valuesElement.Add(new XElement("Value",
                    new XAttribute("VariableId", value.VariableId.ToString("N")),
                    new XAttribute("Value", DynamicVariableFormatting.ToHex(value.Value)),
                    new XAttribute("Label", value.Label ?? string.Empty),
                    new XAttribute("FirstSeenUtc", DynamicVariableFormatting.ToInvariantDate(value.FirstSeenUtc)),
                    new XAttribute("LastSeenUtc", DynamicVariableFormatting.ToInvariantDate(value.LastSeenUtc)),
                    new XAttribute("SeenCount", value.SeenCount),
                    new XAttribute("SourceRuleId", value.SourceRuleId.ToString("N"))));
            }
            root.Add(valuesElement);
            return root;
        }

        public static bool FromBackupElement(
            XElement root,
            out List<DynamicVariableDefinition> definitions,
            out List<ExtractionRule> rules,
            out List<KnownVariableValue> knownValues)
        {
            definitions = new List<DynamicVariableDefinition>();
            rules = new List<ExtractionRule>();
            knownValues = new List<KnownVariableValue>();
            if (root == null || !string.Equals(root.Name.LocalName, "DynamicVariables", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (XElement element in (root.Element("Definitions") ?? new XElement("Definitions")).Elements("Definition"))
            {
                Guid variableId;
                int length;
                if (!Guid.TryParse((string)element.Attribute("VariableId"), out variableId) ||
                    !int.TryParse((string)element.Attribute("Length"), out length) ||
                    variableId == Guid.Empty || length <= 0)
                {
                    continue;
                }
                bool autoUpdate;
                bool.TryParse((string)element.Attribute("AutoUpdate"), out autoUpdate);
                definitions.Add(new DynamicVariableDefinition
                {
                    VariableId = variableId,
                    Symbol = (string)element.Attribute("Symbol") ?? string.Empty,
                    DisplayName = (string)element.Attribute("DisplayName") ?? string.Empty,
                    Description = (string)element.Attribute("Description") ?? string.Empty,
                    Length = length,
                    IsAutoUpdateEnabled = autoUpdate
                });
            }

            foreach (XElement element in (root.Element("Rules") ?? new XElement("Rules")).Elements("Rule"))
            {
                Guid ruleId;
                int packetType;
                bool enabled;
                if (!Guid.TryParse((string)element.Attribute("RuleId"), out ruleId) ||
                    !int.TryParse((string)element.Attribute("PacketType"), out packetType) ||
                    !bool.TryParse((string)element.Attribute("Enabled"), out enabled) ||
                    ruleId == Guid.Empty)
                {
                    continue;
                }
                ExtractionRule rule = new ExtractionRule
                {
                    RuleId = ruleId,
                    Name = (string)element.Attribute("Name") ?? string.Empty,
                    IsEnabled = enabled,
                    PacketType = (Socket_Cache.SocketPacket.PacketType)packetType,
                    PatternBytes = DynamicVariableFormatting.ParseHex((string)element.Attribute("Pattern")),
                    WildcardMask = DynamicVariableFormatting.ParseHex((string)element.Attribute("WildcardMask"))
                };
                foreach (XElement fieldElement in (element.Element("Fields") ?? new XElement("Fields")).Elements("Field"))
                {
                    Guid fieldId;
                    Guid variableId;
                    int offset;
                    int length;
                    if (!Guid.TryParse((string)fieldElement.Attribute("FieldId"), out fieldId) ||
                        !Guid.TryParse((string)fieldElement.Attribute("VariableId"), out variableId) ||
                        !int.TryParse((string)fieldElement.Attribute("Offset"), out offset) ||
                        !int.TryParse((string)fieldElement.Attribute("Length"), out length))
                    {
                        continue;
                    }
                    rule.Fields.Add(new DynamicField
                    {
                        FieldId = fieldId,
                        VariableId = variableId,
                        Offset = offset,
                        Length = length,
                        Description = (string)fieldElement.Attribute("Description") ?? string.Empty
                    });
                }
                rules.Add(rule);
            }

            foreach (XElement element in (root.Element("KnownValues") ?? new XElement("KnownValues")).Elements("Value"))
            {
                Guid variableId;
                Guid sourceRuleId;
                long count;
                if (!Guid.TryParse((string)element.Attribute("VariableId"), out variableId) ||
                    !long.TryParse((string)element.Attribute("SeenCount"), out count))
                {
                    continue;
                }
                Guid.TryParse((string)element.Attribute("SourceRuleId"), out sourceRuleId);
                knownValues.Add(new KnownVariableValue
                {
                    VariableId = variableId,
                    Value = DynamicVariableFormatting.ParseHex((string)element.Attribute("Value")),
                    Label = (string)element.Attribute("Label") ?? string.Empty,
                    FirstSeenUtc = DynamicVariableFormatting.ParseDate((string)element.Attribute("FirstSeenUtc"), DateTime.UtcNow),
                    LastSeenUtc = DynamicVariableFormatting.ParseDate((string)element.Attribute("LastSeenUtc"), DateTime.UtcNow),
                    SeenCount = Math.Max(0, count),
                    SourceRuleId = sourceRuleId
                });
            }
            return true;
        }

        public static XElement ToBindingsElement(IEnumerable<PresetVariableBinding> bindings)
        {
            List<PresetVariableBinding> list = (bindings ?? Enumerable.Empty<PresetVariableBinding>())
                .Where(item => item != null)
                .ToList();
            if (list.Count == 0)
            {
                return null;
            }

            XElement root = new XElement("VariableBindings");
            foreach (PresetVariableBinding binding in list)
            {
                root.Add(new XElement("Binding",
                    new XAttribute("VariableId", binding.VariableId.ToString("N")),
                    new XAttribute("Offset", binding.Offset),
                    new XAttribute("Length", binding.Length)));
            }
            return root;
        }

        public static List<PresetVariableBinding> FromBindingsElement(XElement element)
        {
            List<PresetVariableBinding> result = new List<PresetVariableBinding>();
            if (element == null)
            {
                return result;
            }

            foreach (XElement item in element.Elements("Binding"))
            {
                Guid variableId;
                int offset;
                int length;
                if (!Guid.TryParse((string)item.Attribute("VariableId"), out variableId) ||
                    !int.TryParse((string)item.Attribute("Offset"), out offset) ||
                    !int.TryParse((string)item.Attribute("Length"), out length) ||
                    variableId == Guid.Empty || offset < 0 || length <= 0)
                {
                    continue;
                }
                result.Add(new PresetVariableBinding
                {
                    VariableId = variableId,
                    Offset = offset,
                    Length = length
                });
            }
            return result;
        }

        public static List<PresetVariableBinding> ForSelection(
            IEnumerable<PresetVariableBinding> bindings,
            long selectionStart,
            long selectionLength)
        {
            if (selectionStart < 0 || selectionLength <= 0)
            {
                return new List<PresetVariableBinding>();
            }
            long selectionEnd = selectionStart + selectionLength;
            return (bindings ?? Enumerable.Empty<PresetVariableBinding>())
                .Where(item => item != null && item.Offset >= selectionStart && item.End <= selectionEnd)
                .Select(item => new PresetVariableBinding
                {
                    VariableId = item.VariableId,
                    Offset = checked(item.Offset - (int)selectionStart),
                    Length = item.Length
                })
                .ToList();
        }
    }
}
