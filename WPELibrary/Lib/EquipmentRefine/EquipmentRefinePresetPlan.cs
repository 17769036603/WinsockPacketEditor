using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 装备炼化预设步骤定义
    /// </summary>
    public sealed class EquipmentRefinePresetStep
    {
        public EquipmentRefinePresetStep(
            int index,
            string state,
            string title,
            string description)
        {
            this.Index = index;
            this.State = state ?? string.Empty;
            this.Title = title ?? string.Empty;
            this.Description = description ?? string.Empty;
        }

        public int Index { get; private set; }

        public string State { get; private set; }

        public string Title { get; private set; }

        public string Description { get; private set; }

        public string DisplayText
        {
            get
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:00}. {1}：{2}",
                    this.Index,
                    this.State,
                    this.Description);
            }
        }
    }

    /// <summary>
    /// 装备炼化预设状态机定义
    /// </summary>
    public static class EquipmentRefinePresetPlan
    {
        public const int Version = 3;
        private const int LegacyVersion = 1;
        private const string EncodedNamePrefix = "~b64:";
        public const string InstructionPrefix = "EquipmentRefine";

        public static List<EquipmentRefinePresetStep> GetSteps()
        {
            return new List<EquipmentRefinePresetStep>
            {
                new EquipmentRefinePresetStep(
                    1,
                    "IDLE",
                    "等待启动",
                    "确认预设有效、运行器空闲，并创建本次运行上下文。"),
                new EquipmentRefinePresetStep(
                    2,
                    "FIND_EQUIPMENT",
                    "寻找目标装备",
                    "读取当前穿戴部位的唯一目标装备。"),
                new EquipmentRefinePresetStep(
                    3,
                    "VERIFY_EQUIPMENT",
                    "验证装备身份",
                    "使用 memberIdentity、slot 和 equipmentId 等稳定字段，确认目标装备未被误炼。"),
                new EquipmentRefinePresetStep(
                    4,
                    "READ_ATTRIBUTES",
                    "读取当前属性",
                    "从 Android 只读 JSON 的已验收 rawFields 中读取属性值；字段未验收时停止。"),
                new EquipmentRefinePresetStep(
                    5,
                    "CHECK_RULES",
                    "检查停止规则",
                    "基于当前属性和预设规则判断是否达到目标。"),
                new EquipmentRefinePresetStep(
                    6,
                    "PERFORM_REFINEMENT",
                    "执行炼化",
                    "使用已验收的炼化封包模板填充变量字段；没有授权发送器时 fail-closed。"),
                new EquipmentRefinePresetStep(
                    7,
                    "WAIT_FOR_RESULT",
                    "等待结果",
                    "等待 resident snapshot/sequence 更新，并重新读取同一目标装备。"),
                new EquipmentRefinePresetStep(
                    8,
                    "VERIFY_RESULT",
                    "验证结果",
                    "确认属性哈希发生变化，再按预设规则判断完成或继续下一轮。"),
                new EquipmentRefinePresetStep(
                    9,
                    "LOG_STEP",
                    "记录日志",
                    "将当前步骤信息写入日志。"),
                new EquipmentRefinePresetStep(
                    10,
                    "CHECK_ATTEMPTS",
                    "检查次数",
                    "检查当前炼化次数是否达到上限。"),
                new EquipmentRefinePresetStep(
                    11,
                    "LOOP_OR_COMPLETE",
                    "循环或结束",
                    "根据配置决定继续循环或结束预设。")
            };
        }

        public static string EncodeStep(EquipmentRefinePresetStep step)
        {
            if (step == null || step.Index <= 0 || string.IsNullOrWhiteSpace(step.State))
            {
                return string.Empty;
            }

            return string.Join(
                "|",
                InstructionPrefix,
                Version.ToString(CultureInfo.InvariantCulture),
                step.Index.ToString(CultureInfo.InvariantCulture),
                step.State);
        }

        public static bool TryDecodeStep(
            string content,
            out EquipmentRefinePresetStep step)
        {
            step = null;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            string[] parts = content.Split(new[] { '|' }, StringSplitOptions.None);
            if (parts.Length != 4 ||
                !string.Equals(parts[0], InstructionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            int version;
            int index;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version) ||
                version < LegacyVersion ||
                version > Version ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return false;
            }

            step = GetSteps().FirstOrDefault(item =>
                item.Index == index &&
                string.Equals(item.State, parts[3], StringComparison.Ordinal));
            return step != null;
        }

        public static string EncodePresetData(EquipmentRefinePreset preset)
        {
            if (preset == null || !preset.IsValid(out _))
            {
                return string.Empty;
            }

            var content = new List<string>();
            content.Add(InstructionPrefix);
            content.Add(Version.ToString(CultureInfo.InvariantCulture));
            content.Add(EncodeName(preset.Name ?? "默认炼化"));
            content.Add(preset.MaxAttempts.ToString(CultureInfo.InvariantCulture));
            content.Add(preset.IntervalMs.ToString(CultureInfo.InvariantCulture));

            // 编码规则列表
            var rulesStr = string.Join(";", preset.Rules.Where(r => r != null).Select(r =>
                string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}",
                    (int)r.Attribute,
                    (int)r.Operator,
                    r.TargetValue,
                    r.Enabled ? 1 : 0)));
            content.Add(rulesStr);

            // 编码其他配置
            content.Add(preset.EnableLooping ? "1" : "0");
            content.Add(preset.ResultConfirmTimeoutMs.ToString(CultureInfo.InvariantCulture));
            content.Add(preset.AttributeReadTimeoutMs.ToString(CultureInfo.InvariantCulture));
            content.Add(preset.SkipTargetReached ? "1" : "0");
            content.Add(((int)preset.RuleLogic).ToString(CultureInfo.InvariantCulture));
            content.Add(preset.RequiredMatches.ToString(CultureInfo.InvariantCulture));
            content.Add(EncodeText(preset.Target == null ? string.Empty : preset.Target.MemberIdentity));
            content.Add(EncodeText(preset.Target == null ? string.Empty : preset.Target.Slot));
            content.Add(EncodeText(preset.Target == null ? string.Empty : preset.Target.EquipmentId));
            content.Add(EncodeText(preset.Target == null ? string.Empty : preset.Target.EquipmentName));
            content.Add(preset.TypeCode.ToString(CultureInfo.InvariantCulture));
            content.Add(preset.OperationCode.ToString(CultureInfo.InvariantCulture));
            content.Add(preset.MemoryResultMode ? "1" : "0");
            content.Add(preset.BagTargetMode ? "1" : "0");
            content.Add(EncodeText(preset.BagTarget == null ? string.Empty : preset.BagTarget.MemberIdentity));
            content.Add(EncodeText(preset.BagTarget == null ? string.Empty : preset.BagTarget.Slot));
            content.Add(EncodeText(preset.BagTarget == null ? string.Empty : preset.BagTarget.ItemId));
            content.Add(EncodeText(preset.BagTarget == null ? string.Empty : preset.BagTarget.ItemTypeId));
            content.Add(EncodeText(preset.BagTarget == null ? string.Empty : preset.BagTarget.Name));
            content.Add(EncodeText(preset.BagTarget == null ? string.Empty : preset.BagTarget.RawFieldsSummary));
            content.Add(preset.BagTarget == null || !preset.BagTarget.XianqiTier.HasValue
                ? string.Empty
                : preset.BagTarget.XianqiTier.Value.ToString(CultureInfo.InvariantCulture));
            content.Add(EncodeText(preset.BagTarget == null ? string.Empty : preset.BagTarget.XianqiTierLabel));
            content.Add(preset.BagTarget == null
                ? "-1"
                : preset.BagTarget.RequestSlotIndex.ToString(CultureInfo.InvariantCulture));
            content.Add(EncodeText(preset.MemoryResultPath));
            content.Add(EncodeText(preset.PacketTemplatePath));

            return string.Join("|", content);
        }

        public static EquipmentRefinePreset DecodePresetData(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            string[] parts = content.Split(new[] { '|' }, StringSplitOptions.None);
            if (parts.Length < 10 ||
                !string.Equals(parts[0], InstructionPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                int version = int.Parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (version < LegacyVersion || version > Version)
                {
                    return null;
                }
                bool hasRequiredMatches = version >= 2;
                bool hasBagTarget = version >= 3;

                var preset = new EquipmentRefinePreset
                {
                    Name = DecodeName(parts[2]),
                    MaxAttempts = int.Parse(parts[3], CultureInfo.InvariantCulture),
                    IntervalMs = int.Parse(parts[4], CultureInfo.InvariantCulture),
                    EnableLooping = parts.Length > 6 && parts[6] == "1",
                    ResultConfirmTimeoutMs = parts.Length > 7 && parts[7].Length > 0 ? int.Parse(parts[7], CultureInfo.InvariantCulture) : 3000,
                    AttributeReadTimeoutMs = parts.Length > 8 && parts[8].Length > 0 ? int.Parse(parts[8], CultureInfo.InvariantCulture) : 5000,
                    SkipTargetReached = parts.Length > 9 && parts[9] == "1",
                    RuleLogic = parts.Length > 10 && parts[10].Length > 0
                        ? (RuleLogic)int.Parse(parts[10], CultureInfo.InvariantCulture)
                        : RuleLogic.All,
                    RequiredMatches = hasRequiredMatches && parts.Length > 11 && parts[11].Length > 0
                        ? int.Parse(parts[11], CultureInfo.InvariantCulture)
                        : 1,
                    Target = new EquipmentRefineDetector.EquipmentTargetSelector
                    {
                        MemberIdentity = parts.Length > (hasRequiredMatches ? 12 : 11) ? DecodeText(parts[hasRequiredMatches ? 12 : 11]) : string.Empty,
                        Slot = parts.Length > (hasRequiredMatches ? 13 : 12) ? DecodeText(parts[hasRequiredMatches ? 13 : 12]) : string.Empty,
                        EquipmentId = parts.Length > (hasRequiredMatches ? 14 : 13) ? DecodeText(parts[hasRequiredMatches ? 14 : 13]) : string.Empty,
                        EquipmentName = parts.Length > (hasRequiredMatches ? 15 : 14) ? DecodeText(parts[hasRequiredMatches ? 15 : 14]) : string.Empty
                    }
                };

                int typeIndex = hasRequiredMatches ? 16 : 15;
                int operationIndex = hasRequiredMatches ? 17 : 16;
                if (parts.Length > typeIndex && parts[typeIndex].Length > 0)
                {
                    preset.TypeCode = int.Parse(parts[typeIndex], CultureInfo.InvariantCulture);
                }
                if (parts.Length > operationIndex && parts[operationIndex].Length > 0)
                {
                    preset.OperationCode = int.Parse(parts[operationIndex], CultureInfo.InvariantCulture);
                }
                if (hasRequiredMatches && parts.Length > 18)
                {
                    preset.MemoryResultMode = parts[18] == "1";
                }
                if (hasBagTarget && parts.Length > 19)
                {
                    preset.BagTargetMode = parts[19] == "1";
                    preset.BagTarget = new EquipmentRefineDetector.BagTargetSelector
                    {
                        MemberIdentity = parts.Length > 20 ? DecodeText(parts[20]) : string.Empty,
                        Slot = parts.Length > 21 ? DecodeText(parts[21]) : string.Empty,
                        ItemId = parts.Length > 22 ? DecodeText(parts[22]) : string.Empty,
                        ItemTypeId = parts.Length > 23 ? DecodeText(parts[23]) : string.Empty,
                        Name = parts.Length > 24 ? DecodeText(parts[24]) : string.Empty,
                        RawFieldsSummary = parts.Length > 25 ? DecodeText(parts[25]) : string.Empty,
                        XianqiTier = parts.Length > 26 && parts[26].Length > 0
                            ? (int?)int.Parse(parts[26], CultureInfo.InvariantCulture)
                            : null,
                        XianqiTierLabel = parts.Length > 27 ? DecodeText(parts[27]) : string.Empty,
                        RequestSlotIndex = parts.Length > 28 && parts[28].Length > 0
                            ? int.Parse(parts[28], CultureInfo.InvariantCulture)
                            : -1
                    };
                }
                if (parts.Length > 29)
                {
                    preset.MemoryResultPath = DecodeText(parts[29]);
                }
                if (parts.Length > 30)
                {
                    preset.PacketTemplatePath = DecodeText(parts[30]);
                }

                // 解码规则
                if (parts.Length > 5 && parts[5].Length > 0)
                {
                    preset.Rules = parts[5].Split(';').Select(r =>
                    {
                        string[] ruleParts = r.Split(':');
                        if (ruleParts.Length != 4)
                        {
                            throw new FormatException("炼化规则字段数量无效。");
                        }

                        TargetAttribute attribute = (TargetAttribute)int.Parse(
                            ruleParts[0],
                            CultureInfo.InvariantCulture);
                        AttributeOperator op = (AttributeOperator)int.Parse(
                            ruleParts[1],
                            CultureInfo.InvariantCulture);
                        RefineRule rule = new RefineRule
                        {
                            Attribute = attribute,
                            Operator = op,
                            TargetValue = int.Parse(ruleParts[2], CultureInfo.InvariantCulture),
                            Enabled = ruleParts[3] == "1"
                        };
                        string ruleError;
                        if (!rule.IsValid(out ruleError))
                        {
                            throw new FormatException(ruleError);
                        }
                        return rule;
                    }).ToList();
                }
                else
                {
                    preset.Rules = new List<RefineRule>();
                }

                if (!Enum.IsDefined(typeof(RuleLogic), preset.RuleLogic))
                {
                    return null;
                }

                return preset;
            }
            catch
            {
                return null;
            }
        }

        private static string EncodeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string EncodeName(string value)
        {
            return EncodedNamePrefix + EncodeText(value);
        }

        private static string DecodeName(string value)
        {
            if (value != null && value.StartsWith(EncodedNamePrefix, StringComparison.Ordinal))
            {
                return DecodeText(value.Substring(EncodedNamePrefix.Length));
            }
            // Version 1-3 stored the name as raw text; retain that compatibility path.
            return value ?? string.Empty;
        }

        private static string DecodeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
