using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 装备炼化预设定义
    /// </summary>
    public class EquipmentRefinePreset
    {
        public const int SchemaVersion = 1;

        [Browsable(false)]
        public int SchemaVersionProperty { get; set; } = SchemaVersion;

        /// <summary>
        /// 预设名称
        /// </summary>
        public string Name { get; set; } = "装备炼化";

        /// <summary>
        /// 炼化规则列表
        /// </summary>
        public List<RefineRule> Rules { get; set; } = new List<RefineRule>();

        /// <summary>
        /// 目标装备选择器。必须至少填写一个稳定身份字段。
        /// </summary>
        public EquipmentRefineDetector.EquipmentTargetSelector Target { get; set; } =
            new EquipmentRefineDetector.EquipmentTargetSelector();

        /// <summary>独立背包目标模式；false 时沿用旧穿戴目标严格契约。</summary>
        public bool BagTargetMode { get; set; }

        /// <summary>背包目标确认信息。不会猜测 equipmentId 或名称。</summary>
        public EquipmentRefineDetector.BagTargetSelector BagTarget { get; set; } =
            new EquipmentRefineDetector.BagTargetSelector();

        /// <summary>
        /// 规则组合逻辑。
        /// </summary>
        public RuleLogic RuleLogic { get; set; } = RuleLogic.All;

        /// <summary>每张候选卡至少命中的规则数；新响应路径不使用 All/Any。</summary>
        public int RequiredMatches { get; set; } = 1;

        /// <summary>
        /// 使用宿主提供的 typed resident 内存结果；未配置 source 时发送前安全停止。
        /// 不表示进程附加，也不包含地址或偏移。
        /// </summary>
        public bool MemoryResultMode { get; set; }

        /// <summary>只读 resident JSON/JSONL 结果文件路径；为空时使用运行参数或环境变量。</summary>
        public string MemoryResultPath { get; set; } = string.Empty;

        /// <summary>已验收炼化模板 JSON 路径；为空时使用运行参数或环境变量。</summary>
        public string PacketTemplatePath { get; set; } = string.Empty;

        /// <summary>
        /// 已验收的 rawFields 属性映射；空映射时运行器拒绝读属性。
        /// </summary>
        public Dictionary<string, TargetAttribute> VerifiedAttributeFields { get; set; } =
            new Dictionary<string, TargetAttribute>(StringComparer.Ordinal);

        /// <summary>
        /// 已验收的协议 typeCode；使用 TypeCode 模板字段时必须配置。
        /// </summary>
        public int TypeCode { get; set; } = -1;

        /// <summary>
        /// 已验收的协议 operationCode；使用 OperationCode 模板字段时必须配置。
        /// </summary>
        public int OperationCode { get; set; } = -1;

        /// <summary>
        /// 最大炼化次数（0 表示无限次）
        /// </summary>
        public int MaxAttempts { get; set; } = 20;

        /// <summary>
        /// 每次炼化之间的间隔时间（毫秒）
        /// </summary>
        public int IntervalMs { get; set; } = 1500;

        /// <summary>
        /// 是否连续循环运行
        /// </summary>
        public bool EnableLooping { get; set; } = false;

        /// <summary>
        /// 炼化结果确认等待时间（毫秒）
        /// </summary>
        public int ResultConfirmTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 属性读取超时时间（毫秒）
        /// </summary>
        public int AttributeReadTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 兼容旧配置的废弃字段，不参与纯发包流程。
        /// </summary>
        [Browsable(false)]
        [Obsolete("EquipmentRefine no longer uses OCR or vision.")]
        public int VisionRegionOffsetX { get; set; } = 0;

        [Browsable(false)]
        [Obsolete("EquipmentRefine no longer uses OCR or vision.")]
        public int VisionRegionOffsetY { get; set; } = 0;

        /// <summary>
        /// 当前游戏窗口标题匹配关键词
        /// </summary>
        public string WindowTitleKeyword { get; set; } = string.Empty;

        /// <summary>
        /// 启用调试日志（0=仅错误, 1=正常, 2=详细）
        /// </summary>
        public int LogLevel { get; set; } = 1;

        /// <summary>
        /// 是否跳过已满目标值的装备
        /// </summary>
        public bool SkipTargetReached { get; set; } = true;

        /// <summary>
        /// 检查预设有效性
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = "装备炼化";
            }

            if (Rules == null)
            {
                errorMessage = "炼化规则列表不能为空。";
                return false;
            }

            if (!Enum.IsDefined(typeof(RuleLogic), this.RuleLogic))
            {
                errorMessage = "规则组合逻辑无效。";
                return false;
            }

            foreach (var rule in Rules)
            {
                if (rule == null)
                {
                    errorMessage = "炼化规则不能包含空项。";
                    return false;
                }
                string ruleError;
                if (!rule.IsValid(out ruleError))
                {
                    errorMessage = ruleError;
                    return false;
                }
            }

            if (!this.BagTargetMode && (Target == null ||
                string.IsNullOrWhiteSpace(Target.Slot)))
            {
                errorMessage = "必须选择当前穿戴部位；成员身份和装备 ID 将在启动时从当前穿戴快照绑定。";
                return false;
            }

            if (this.BagTargetMode && (BagTarget == null || !BagTarget.IsValid))
            {
                errorMessage = "背包目标必须指定 slot 和 memberIdentity。";
                return false;
            }
            if (this.BagTargetMode && this.BagTarget != null && this.BagTarget.RequestSlotIndex < -1)
            {
                errorMessage = "背包协议位置不能小于 -1。";
                return false;
            }

            if (Rules == null || !Rules.Any(rule => rule != null && rule.Enabled))
            {
                errorMessage = "至少需要一条启用的炼化规则。";
                return false;
            }
            int activeRuleCount = Rules.Count(rule => rule != null && rule.Enabled);
            if (RequiredMatches < 1 || RequiredMatches > activeRuleCount)
            {
                errorMessage = "命中数量 K 必须满足 1 <= K <= N。";
                return false;
            }
            if (Rules.Where(rule => rule != null && rule.Enabled)
                .GroupBy(rule => new { rule.Attribute, rule.Operator, rule.TargetValue })
                .Any(group => group.Count() > 1))
            {
                errorMessage = "启用规则不能重复。";
                return false;
            }

            if (MaxAttempts < 0)
            {
                errorMessage = "最大炼化次数不能为负数。";
                return false;
            }

            if (IntervalMs < 0)
            {
                errorMessage = "间隔时间不能为负数。";
                return false;
            }

            if (ResultConfirmTimeoutMs <= 0)
            {
                errorMessage = "结果确认超时时间必须为正数。";
                return false;
            }

            if (AttributeReadTimeoutMs <= 0)
            {
                errorMessage = "属性读取超时时间必须为正数。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 创建默认预设
        /// </summary>
        public static EquipmentRefinePreset CreateDefault()
        {
            return new EquipmentRefinePreset
            {
                Name = "默认炼化",
                MaxAttempts = 20,
                IntervalMs = 1500,
                ResultConfirmTimeoutMs = 3000,
                AttributeReadTimeoutMs = 5000,
                LogLevel = 1,
                SkipTargetReached = true,
                Target = new EquipmentRefineDetector.EquipmentTargetSelector(),
                Rules = new List<RefineRule>
                {
                    new RefineRule
                    {
                        Attribute = TargetAttribute.RootBone,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 0,
                        Enabled = true
                    },
                    new RefineRule
                    {
                        Attribute = TargetAttribute.Spirit,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 0,
                        Enabled = false
                    },
                    new RefineRule
                    {
                        Attribute = TargetAttribute.Strength,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 0,
                        Enabled = false
                    },
                    new RefineRule
                    {
                        Attribute = TargetAttribute.Agility,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 0,
                        Enabled = false
                    },
                    new RefineRule
                    {
                        Attribute = TargetAttribute.HitRatePercent,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 0,
                        Enabled = false
                    }
                }
            };
        }

        /// <summary>
        /// 检查是否满足停止条件
        /// </summary>
        public bool ShouldStop(EquipmentAttributes currentAttrs, int attemptCount)
        {
            // 检查最大次数
            if (MaxAttempts > 0 && attemptCount >= MaxAttempts)
            {
                return true;
            }

            if (currentAttrs == null || Rules == null)
            {
                return false;
            }

            var enabledRules = Rules.Where(rule => rule != null && rule.Enabled).ToList();
            if (enabledRules.Count == 0)
            {
                return false;
            }

            // 检查启用规则，遵循预设的 All/Any 逻辑。
            var results = new List<bool>();
            foreach (var rule in enabledRules)
            {
                int value = GetAttributeValue(currentAttrs, rule.Attribute);
                results.Add(rule.Evaluate(value));
            }

            bool satisfied = RuleLogic == RuleLogic.Any
                ? results.Any(value => value)
                : results.All(value => value);
            return satisfied && SkipTargetReached;
        }

        /// <summary>
        /// 获取指定属性的值
        /// </summary>
        private int GetAttributeValue(EquipmentAttributes attrs, TargetAttribute attribute)
        {
            EquipmentAttributesSnapshot snapshot = attrs as EquipmentAttributesSnapshot;
            if (snapshot != null)
            {
                return snapshot.GetValue(attribute);
            }

            switch (attribute)
            {
                case TargetAttribute.AttackPower: return attrs.AttackPower;
                case TargetAttribute.DefensePower: return attrs.DefensePower;
                case TargetAttribute.CritRate: return attrs.CritRate;
                case TargetAttribute.CritDamage: return attrs.CritDamage;
                case TargetAttribute.HitRate: return attrs.HitRate;
                case TargetAttribute.DodgeRate: return attrs.DodgeRate;
                case TargetAttribute.HpRecovery: return attrs.HpRecovery;
                case TargetAttribute.MpRecovery: return attrs.MpRecovery;
                case TargetAttribute.Speed: return 0;
                case TargetAttribute.MagicAttack: return 0;
                case TargetAttribute.MagicDefense: return 0;
                case TargetAttribute.Block: return 0;
                case TargetAttribute.Parry: return 0;
                case TargetAttribute.Counter: return 0;
                case TargetAttribute.CriticalResist: return 0;
                case TargetAttribute.DamageReduction: return 0;
                case TargetAttribute.RareDegree: return attrs.RareDegree;
                default: return 0;
            }
        }

        /// <summary>
        /// 克隆预设
        /// </summary>
        public EquipmentRefinePreset Clone()
        {
            return new EquipmentRefinePreset
            {
                SchemaVersionProperty = this.SchemaVersionProperty,
                Name = this.Name,
                Rules = this.Rules == null
                    ? new List<RefineRule>()
                    : this.Rules.Select(rule => rule == null ? null : new RefineRule
                    {
                        Attribute = rule.Attribute,
                        Operator = rule.Operator,
                        TargetValue = rule.TargetValue,
                        Enabled = rule.Enabled
                    }).ToList(),
                Target = this.Target == null
                    ? new EquipmentRefineDetector.EquipmentTargetSelector()
                    : new EquipmentRefineDetector.EquipmentTargetSelector
                    {
                        MemberIdentity = this.Target.MemberIdentity,
                        Slot = this.Target.Slot,
                        EquipmentId = this.Target.EquipmentId,
                        EquipmentName = this.Target.EquipmentName,
                        SlotIndex = this.Target.SlotIndex
                    },
                BagTargetMode = this.BagTargetMode,
                BagTarget = this.BagTarget == null
                    ? new EquipmentRefineDetector.BagTargetSelector()
                    : new EquipmentRefineDetector.BagTargetSelector
                    {
                        Slot = this.BagTarget.Slot,
                        MemberIdentity = this.BagTarget.MemberIdentity,
                        ItemId = this.BagTarget.ItemId,
                        ItemTypeId = this.BagTarget.ItemTypeId,
                        XianqiTier = this.BagTarget.XianqiTier,
                        XianqiTierLabel = this.BagTarget.XianqiTierLabel,
                        Name = this.BagTarget.Name,
                        RawFieldsSummary = this.BagTarget.RawFieldsSummary,
                        RequestSlotIndex = this.BagTarget.RequestSlotIndex
                    },
                RuleLogic = this.RuleLogic,
                RequiredMatches = this.RequiredMatches,
                MemoryResultMode = this.MemoryResultMode,
                MemoryResultPath = this.MemoryResultPath,
                PacketTemplatePath = this.PacketTemplatePath,
                VerifiedAttributeFields = this.VerifiedAttributeFields == null
                    ? new Dictionary<string, TargetAttribute>(StringComparer.Ordinal)
                    : new Dictionary<string, TargetAttribute>(this.VerifiedAttributeFields, StringComparer.Ordinal),
                TypeCode = this.TypeCode,
                OperationCode = this.OperationCode,
                MaxAttempts = this.MaxAttempts,
                IntervalMs = this.IntervalMs,
                EnableLooping = this.EnableLooping,
                ResultConfirmTimeoutMs = this.ResultConfirmTimeoutMs,
                AttributeReadTimeoutMs = this.AttributeReadTimeoutMs,
#pragma warning disable 0618
                VisionRegionOffsetX = this.VisionRegionOffsetX,
                VisionRegionOffsetY = this.VisionRegionOffsetY,
#pragma warning restore 0618
                WindowTitleKeyword = this.WindowTitleKeyword,
                LogLevel = this.LogLevel,
                SkipTargetReached = this.SkipTargetReached
            };
        }
    }
}
