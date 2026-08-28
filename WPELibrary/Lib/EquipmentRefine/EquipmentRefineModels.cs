using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>装备目标范围。Worn 保持旧穿戴契约，Bag 是独立的背包条目契约。</summary>
    public enum EquipmentTargetMode
    {
        Worn = 0,
        Bag = 1
    }

    /// <summary>
    /// 炼化属性类型。字段名到该枚举的映射必须来自已验收的 reader/抓包证据。
    /// </summary>
    public enum TargetAttribute
    {
        AttackPower = 0,
        DefensePower = 1,
        MagicAttack = 2,
        MagicDefense = 3,
        CritRate = 4,
        CritDamage = 5,
        HitRate = 6,
        DodgeRate = 7,
        HpRecovery = 8,
        MpRecovery = 9,
        Speed = 10,
        Block = 11,
        Parry = 12,
        Counter = 13,
        CriticalResist = 14,
        DamageReduction = 15,
        RareDegree = 16,
        RootBone = 100,
        Spirit = 101,
        Strength = 102,
        Agility = 103,
        HitRatePercent = 104,
        DodgeRatePercent = 105,
        ComboCount = 106,
        ComboRatePercent = 107,
        FuryRatePercent = 108,
        ResistConfusion = 109,
        ResistPoison = 110,
        ResistSleep = 111,
        ResistSeal = 112,
        ResistWind = 113,
        ResistThunder = 114,
        ResistWater = 115,
        ResistFire = 116,
        ResistShock = 117,
        ResistThreeCorpse = 118,
        ResistGhostFire = 119,
        ResistForget = 120,
        PhysicalAbsorb = 121,
        IgnoreResistConfusion = 122,
        IgnoreResistPoison = 123,
        IgnoreResistSleep = 124,
        IgnoreResistSeal = 125,
        IgnoreResistWind = 126,
        IgnoreResistThunder = 127,
        IgnoreResistWater = 128,
        IgnoreResistFire = 129,
        IgnoreResistShock = 130,
        IgnoreResistThreeCorpse = 131,
        IgnoreResistGhostFire = 132,
        IgnoreResistForget = 133,
        WindFuryRate = 134,
        ThunderFuryRate = 135,
        WaterFuryRate = 136,
        FireFuryRate = 137,
        ThreeCorpseFuryRate = 138,
        GhostFireFuryRate = 139,
        EnhanceHaste = 140,
        EnhanceDefense = 141,
        EnhanceAttack = 142,
        EnhanceArmorBreak = 143,
        EnhanceShock = 144,
        EnhanceHeal = 145,
        EnhanceSweep = 146,
        EnhanceCharm = 147,
        StrongCounterMetal = 148,
        StrongCounterWood = 149,
        StrongCounterWater = 150,
        StrongCounterFire = 151,
        StrongCounterEarth = 152,
        ResistGanShan = 153,
        ResistXiaoYue = 154,
        Unknown = 99
    }

    /// <summary>
    /// 预设属性的中文显示定义。PropertyKey/RawId 只用于与已验收的 reader 字段对接，
    /// 不直接显示在预设页面。
    /// </summary>
    public sealed class TargetAttributeDefinition
    {
        public TargetAttribute Attribute { get; private set; }
        public string DisplayName { get; private set; }
        public string PropertyKey { get; private set; }
        public string RawId { get; private set; }
        public bool IsPercentage { get; private set; }

        public TargetAttributeDefinition(
            TargetAttribute attribute,
            string displayName,
            string propertyKey,
            string rawId,
            bool isPercentage)
        {
            this.Attribute = attribute;
            this.DisplayName = displayName ?? string.Empty;
            this.PropertyKey = propertyKey ?? string.Empty;
            this.RawId = rawId ?? string.Empty;
            this.IsPercentage = isPercentage;
        }
    }

    /// <summary>
    /// 炼化属性目录。新预设只展示读取器已解码的 55 个中文属性；旧预设中仍在使用的
    /// 历史占位属性由编辑器按需追加，保证旧 JSON 可以继续打开和保存。
    /// </summary>
    public static class EquipmentRefineAttributeCatalog
    {
        private static readonly TargetAttributeDefinition[] ReaderDefinitions =
        {
            new TargetAttributeDefinition(TargetAttribute.RootBone, "根骨", "lh_gg", "80001", false),
            new TargetAttributeDefinition(TargetAttribute.Spirit, "灵性", "lh_lx", "80002", false),
            new TargetAttributeDefinition(TargetAttribute.Strength, "力量", "lh_ll", "80003", false),
            new TargetAttributeDefinition(TargetAttribute.Agility, "敏捷", "lh_mj", "80004", false),
            new TargetAttributeDefinition(TargetAttribute.HitRatePercent, "命中率", "lh_hitrate", "80100", true),
            new TargetAttributeDefinition(TargetAttribute.DodgeRatePercent, "闪躲率", "lh_missrate", "80101", true),
            new TargetAttributeDefinition(TargetAttribute.ComboCount, "连击次数", "lh_ljtimes", "80103", false),
            new TargetAttributeDefinition(TargetAttribute.ComboRatePercent, "连击率", "lh_ljrate", "80104", true),
            new TargetAttributeDefinition(TargetAttribute.FuryRatePercent, "狂暴率", "lh_kbrate", "80105", true),
            new TargetAttributeDefinition(TargetAttribute.ResistConfusion, "抗混乱", "lh_kanghunluan", "80200", true),
            new TargetAttributeDefinition(TargetAttribute.ResistPoison, "抗毒", "lh_kangdu", "80201", true),
            new TargetAttributeDefinition(TargetAttribute.ResistSleep, "抗昏睡", "lh_kanghunshui", "80202", true),
            new TargetAttributeDefinition(TargetAttribute.ResistSeal, "抗封印", "lh_kangfengyin", "80203", true),
            new TargetAttributeDefinition(TargetAttribute.ResistWind, "抗风", "lh_kangfeng", "80204", true),
            new TargetAttributeDefinition(TargetAttribute.ResistThunder, "抗雷", "lh_kanglei", "80205", true),
            new TargetAttributeDefinition(TargetAttribute.ResistWater, "抗水", "lh_kangshui", "80206", true),
            new TargetAttributeDefinition(TargetAttribute.ResistFire, "抗火", "lh_kanghuo", "80207", true),
            new TargetAttributeDefinition(TargetAttribute.ResistShock, "抗震慑", "lh_kangzhen", "80208", true),
            new TargetAttributeDefinition(TargetAttribute.ResistThreeCorpse, "抗三尸", "lh_kangsanshi", "80209", true),
            new TargetAttributeDefinition(TargetAttribute.ResistGhostFire, "抗鬼火", "lh_kangguihuo", "80210", true),
            new TargetAttributeDefinition(TargetAttribute.ResistForget, "抗遗忘", "lh_kangyiwang", "80211", true),
            new TargetAttributeDefinition(TargetAttribute.PhysicalAbsorb, "物理吸收", "lh_absordef", "80212", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistConfusion, "忽视抗混乱", "lh_nokanghunluan", "80300", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistPoison, "忽视抗毒", "lh_nokangdu", "80301", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistSleep, "忽视抗昏睡", "lh_nokanghunshui", "80302", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistSeal, "忽视抗封印", "lh_nokangfengyin", "80303", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistWind, "忽视抗风", "lh_nokangfeng", "80304", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistThunder, "忽视抗雷", "lh_nokanglei", "80305", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistWater, "忽视抗水", "lh_nokangshui", "80306", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistFire, "忽视抗火", "lh_nokanghuo", "80307", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistShock, "忽视抗震慑", "lh_nokangzhen", "80308", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistThreeCorpse, "忽视抗三尸", "lh_nokangsanshi", "80309", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistGhostFire, "忽视抗鬼火", "lh_nokangguihuo", "80310", true),
            new TargetAttributeDefinition(TargetAttribute.IgnoreResistForget, "忽视抗遗忘", "lh_nokangyiwang", "80311", true),
            new TargetAttributeDefinition(TargetAttribute.WindFuryRate, "风系狂暴率", "lh_magickuangbao_feng", "80400", true),
            new TargetAttributeDefinition(TargetAttribute.ThunderFuryRate, "雷系狂暴率", "lh_magickuangbao_lei", "80401", true),
            new TargetAttributeDefinition(TargetAttribute.WaterFuryRate, "水系狂暴率", "lh_magickuangbao_shui", "80402", true),
            new TargetAttributeDefinition(TargetAttribute.FireFuryRate, "火系狂暴率", "lh_magickuangbao_huo", "80403", true),
            new TargetAttributeDefinition(TargetAttribute.ThreeCorpseFuryRate, "三尸狂暴率", "lh_magickuangbao_sanshi", "80404", true),
            new TargetAttributeDefinition(TargetAttribute.GhostFireFuryRate, "鬼火狂暴率", "lh_magickuangbao_guihuo", "80405", true),
            new TargetAttributeDefinition(TargetAttribute.EnhanceHaste, "加强加速", "lh_item_add_magic_su", "80500", true),
            new TargetAttributeDefinition(TargetAttribute.EnhanceDefense, "加强加防", "lh_item_add_magic_fang", "80501", true),
            new TargetAttributeDefinition(TargetAttribute.EnhanceAttack, "加强加攻", "lh_item_add_magic_gong", "80502", true),
            new TargetAttributeDefinition(TargetAttribute.EnhanceArmorBreak, "加强破甲", "lh_item_add_magic_pojia", "80503", true),
            new TargetAttributeDefinition(TargetAttribute.EnhanceShock, "加强震击", "lh_item_add_magic_zhenji", "80504", true),
            new TargetAttributeDefinition(TargetAttribute.EnhanceHeal, "加强治愈", "lh_item_add_magic_zhiyu", "80505", true),
            new TargetAttributeDefinition(TargetAttribute.EnhanceSweep, "加强横扫", "lh_item_add_magic_hengsao", "80506", true),
            new TargetAttributeDefinition(TargetAttribute.EnhanceCharm, "加强魅惑", "lh_item_add_magic_meihuo", "80518", true),
            new TargetAttributeDefinition(TargetAttribute.StrongCounterMetal, "强力克金", "lh_qkwx_JIN", "80605", true),
            new TargetAttributeDefinition(TargetAttribute.StrongCounterWood, "强力克木", "lh_qkwx_MU", "80606", true),
            new TargetAttributeDefinition(TargetAttribute.StrongCounterWater, "强力克水", "lh_qkwx_SHUI", "80607", true),
            new TargetAttributeDefinition(TargetAttribute.StrongCounterFire, "强力克火", "lh_qkwx_HUO", "80608", true),
            new TargetAttributeDefinition(TargetAttribute.StrongCounterEarth, "强力克土", "lh_qkwx_TU", "80609", true),
            new TargetAttributeDefinition(TargetAttribute.ResistGanShan, "抗感山", string.Empty, "80613", true),
            new TargetAttributeDefinition(TargetAttribute.ResistXiaoYue, "抗啸月", string.Empty, "80611", true)
        };

        private static readonly TargetAttributeDefinition[] LegacyDefinitions =
        {
            new TargetAttributeDefinition(TargetAttribute.AttackPower, "攻击力", string.Empty, string.Empty, false),
            new TargetAttributeDefinition(TargetAttribute.DefensePower, "防御力", string.Empty, string.Empty, false),
            new TargetAttributeDefinition(TargetAttribute.MagicAttack, "法术攻击", string.Empty, string.Empty, false),
            new TargetAttributeDefinition(TargetAttribute.MagicDefense, "法术防御", string.Empty, string.Empty, false),
            new TargetAttributeDefinition(TargetAttribute.CritRate, "暴击", string.Empty, string.Empty, true),
            new TargetAttributeDefinition(TargetAttribute.CritDamage, "暴击伤害", string.Empty, string.Empty, true),
            new TargetAttributeDefinition(TargetAttribute.HitRate, "命中", string.Empty, string.Empty, true),
            new TargetAttributeDefinition(TargetAttribute.DodgeRate, "闪避", string.Empty, string.Empty, true),
            new TargetAttributeDefinition(TargetAttribute.HpRecovery, "生命值", string.Empty, string.Empty, false),
            new TargetAttributeDefinition(TargetAttribute.MpRecovery, "法力值", string.Empty, string.Empty, false),
            new TargetAttributeDefinition(TargetAttribute.Speed, "速度", string.Empty, string.Empty, false),
            new TargetAttributeDefinition(TargetAttribute.Block, "格挡", string.Empty, string.Empty, true),
            new TargetAttributeDefinition(TargetAttribute.Parry, "招架", string.Empty, string.Empty, true),
            new TargetAttributeDefinition(TargetAttribute.Counter, "反击", string.Empty, string.Empty, true),
            new TargetAttributeDefinition(TargetAttribute.CriticalResist, "暴击抵抗", string.Empty, string.Empty, true),
            new TargetAttributeDefinition(TargetAttribute.DamageReduction, "伤害减免", string.Empty, string.Empty, true),
            new TargetAttributeDefinition(TargetAttribute.RareDegree, "稀有度", string.Empty, string.Empty, false)
        };

        public static IList<TargetAttributeDefinition> GetReaderDefinitions()
        {
            return new List<TargetAttributeDefinition>(ReaderDefinitions);
        }

        public static IList<TargetAttributeDefinition> GetPresetDefinitions(IEnumerable<TargetAttribute> existingAttributes)
        {
            List<TargetAttributeDefinition> result = new List<TargetAttributeDefinition>(ReaderDefinitions);
            HashSet<TargetAttribute> included = new HashSet<TargetAttribute>(
                ReaderDefinitions.Select(definition => definition.Attribute));
            if (existingAttributes == null) return result;

            foreach (TargetAttribute attribute in existingAttributes)
            {
                if (attribute == TargetAttribute.Unknown || included.Contains(attribute)) continue;
                TargetAttributeDefinition legacy = LegacyDefinitions.FirstOrDefault(
                    definition => definition.Attribute == attribute);
                if (legacy == null) continue;
                result.Add(legacy);
                included.Add(attribute);
            }
            return result;
        }

        public static string GetDisplayName(TargetAttribute attribute)
        {
            TargetAttributeDefinition definition = ReaderDefinitions.FirstOrDefault(
                item => item.Attribute == attribute);
            if (definition == null)
            {
                definition = LegacyDefinitions.FirstOrDefault(item => item.Attribute == attribute);
            }
            return definition == null ? "未知属性" : definition.DisplayName;
        }

        /// <summary>
        /// 创建供外部 reader/JSON source 显式注入的字段映射。不会自动写入预设，
        /// 也不会改变未配置映射时的 fail-closed 行为。
        /// </summary>
        public static IDictionary<string, TargetAttribute> CreateReaderFieldMap()
        {
            Dictionary<string, TargetAttribute> result = new Dictionary<string, TargetAttribute>(StringComparer.Ordinal);
            foreach (TargetAttributeDefinition definition in ReaderDefinitions)
            {
                string key = string.IsNullOrWhiteSpace(definition.PropertyKey)
                    ? definition.RawId
                    : definition.PropertyKey;
                if (!string.IsNullOrWhiteSpace(key)) result[key] = definition.Attribute;
            }
            return result;
        }
    }

    public enum AttributeOperator
    {
        GreaterThan = 0,
        LessThan = 1,
        GreaterThanOrEqual = 2,
        LessThanOrEqual = 3,
        Equal = 4,
        NotEqual = 5
    }

    public enum RuleLogic
    {
        All = 0,
        Any = 1
    }

    public class RefineRule
    {
        public TargetAttribute Attribute { get; set; } = TargetAttribute.AttackPower;
        public AttributeOperator Operator { get; set; } = AttributeOperator.GreaterThanOrEqual;
        /// <summary>
        /// 整数协议单位的目标阈值。百分比属性沿用现有约定：输入/保存 2 表示 2.0%，不转换为浮点数。
        /// </summary>
        public int TargetValue { get; set; }
        public bool Enabled { get; set; } = true;

        public static bool IsSupportedOperator(AttributeOperator op)
        {
            return op == AttributeOperator.Equal ||
                op == AttributeOperator.GreaterThanOrEqual;
        }

        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!Enum.IsDefined(typeof(TargetAttribute), this.Attribute) ||
                !Enum.IsDefined(typeof(AttributeOperator), this.Operator))
            {
                errorMessage = "炼化规则包含无效属性或比较运算符。";
                return false;
            }
            if (this.Attribute == TargetAttribute.Unknown)
            {
                errorMessage = "炼化规则包含未知属性。";
                return false;
            }
            if (!IsSupportedOperator(this.Operator))
            {
                errorMessage = "炼化规则只支持 = 和 >=。";
                return false;
            }
            return true;
        }

        public bool Evaluate(int currentValue)
        {
            switch (this.Operator)
            {
                case AttributeOperator.GreaterThanOrEqual:
                    return currentValue >= this.TargetValue;
                case AttributeOperator.Equal:
                    return currentValue == this.TargetValue;
                default:
                    return false;
            }
        }

        public string GetDescription()
        {
            string op;
            switch (this.Operator)
            {
                case AttributeOperator.GreaterThanOrEqual: op = ">="; break;
                case AttributeOperator.Equal: op = "="; break;
                default: op = "?"; break;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} {2}",
                EquipmentRefineAttributeCatalog.GetDisplayName(this.Attribute),
                op,
                this.TargetValue);
        }
    }

    /// <summary>
    /// 旧预设模型仍使用的标量属性容器。
    /// </summary>
    public class EquipmentAttributes
    {
        public string InstanceId { get; set; } = string.Empty;
        public int SlotIndex { get; set; } = -1;
        public int AttackPower { get; set; }
        public int DefensePower { get; set; }
        public int CritRate { get; set; }
        public int CritDamage { get; set; }
        public int HitRate { get; set; }
        public int DodgeRate { get; set; }
        public int HpRecovery { get; set; }
        public int MpRecovery { get; set; }
        public int RareDegree { get; set; }
        public string EquipmentName { get; set; } = string.Empty;
        public string EquipmentType { get; set; } = string.Empty;
        public string EquipmentQuality { get; set; } = string.Empty;
        public DateTime LastReadTime { get; set; } = DateTime.MinValue;
        public string AttributeHash { get; set; } = string.Empty;

        public bool HasChangedSince(EquipmentAttributes previous)
        {
            return previous == null || !string.Equals(
                this.AttributeHash,
                previous.AttributeHash,
                StringComparison.Ordinal);
        }

        public virtual string CalculateHash()
        {
            string source = string.Join(
                "|",
                new[]
                {
                    this.InstanceId ?? string.Empty,
                    this.SlotIndex.ToString(CultureInfo.InvariantCulture),
                    this.AttackPower.ToString(CultureInfo.InvariantCulture),
                    this.DefensePower.ToString(CultureInfo.InvariantCulture),
                    this.CritRate.ToString(CultureInfo.InvariantCulture),
                    this.CritDamage.ToString(CultureInfo.InvariantCulture),
                    this.HitRate.ToString(CultureInfo.InvariantCulture),
                    this.DodgeRate.ToString(CultureInfo.InvariantCulture),
                    this.HpRecovery.ToString(CultureInfo.InvariantCulture),
                    this.MpRecovery.ToString(CultureInfo.InvariantCulture),
                    this.RareDegree.ToString(CultureInfo.InvariantCulture)
                });

            return Hash(source);
        }

        protected static string Hash(string source)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(source ?? string.Empty);
                byte[] digest = sha.ComputeHash(bytes);
                return BitConverter.ToString(digest).Replace("-", string.Empty).Substring(0, 16);
            }
        }
    }

    /// <summary>
    /// 一个已解析的属性值。实现值相等，避免快照比较依赖对象引用。
    /// </summary>
    public sealed class AttributeValue : IEquatable<AttributeValue>
    {
        public TargetAttribute Type { get; set; } = TargetAttribute.Unknown;
        public string Name { get; set; } = string.Empty;
        public int CurrentValue { get; set; }
        public int RawValue { get; set; }
        public string RawValueText { get; set; } = string.Empty;
        public string RawId { get; set; } = string.Empty;
        public string RawOrder { get; set; } = string.Empty;
        public string PropertyKey { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public DateTime LastReadTime { get; set; } = DateTime.MinValue;

        public bool Equals(AttributeValue other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            return this.Type == other.Type &&
                string.Equals(this.Name, other.Name, StringComparison.Ordinal) &&
                this.CurrentValue == other.CurrentValue &&
                this.RawValue == other.RawValue &&
                string.Equals(this.RawValueText, other.RawValueText, StringComparison.Ordinal) &&
                string.Equals(this.RawId, other.RawId, StringComparison.Ordinal) &&
                string.Equals(this.RawOrder, other.RawOrder, StringComparison.Ordinal) &&
                string.Equals(this.PropertyKey, other.PropertyKey, StringComparison.Ordinal) &&
                string.Equals(this.OriginalName, other.OriginalName, StringComparison.Ordinal) &&
                string.Equals(this.Unit, other.Unit, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as AttributeValue);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)this.Type;
                hash = (hash * 397) ^ (this.Name ?? string.Empty).GetHashCode();
                hash = (hash * 397) ^ this.CurrentValue;
                hash = (hash * 397) ^ this.RawValue;
                hash = (hash * 397) ^ (this.RawValueText ?? string.Empty).GetHashCode();
                hash = (hash * 397) ^ (this.RawId ?? string.Empty).GetHashCode();
                hash = (hash * 397) ^ (this.RawOrder ?? string.Empty).GetHashCode();
                hash = (hash * 397) ^ (this.PropertyKey ?? string.Empty).GetHashCode();
                hash = (hash * 397) ^ (this.OriginalName ?? string.Empty).GetHashCode();
                hash = (hash * 397) ^ (this.Unit ?? string.Empty).GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 纯发包链路使用的完整属性快照。
    /// </summary>
    public class EquipmentAttributesSnapshot : EquipmentAttributes
    {
        public string MemberIdentity { get; set; } = string.Empty;
        public string Slot { get; set; } = string.Empty;
        public string EquipmentId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string ItemTypeId { get; set; } = string.Empty;
        public string BaseAttributeHash { get; set; } = string.Empty;
        public string RefineAttributeHash { get; set; } = string.Empty;
        public string SnapshotId { get; set; } = string.Empty;
        public string StreamSessionId { get; set; } = string.Empty;
        public long Sequence { get; set; }
        public bool CandidateOnly { get; set; } = true;
        public bool IsValid { get; set; }
        public string ReadError { get; set; } = string.Empty;
        public DateTime ReadTime { get; set; } = DateTime.UtcNow;
        public List<AttributeValue> Attributes { get; private set; } = new List<AttributeValue>();
        public Dictionary<string, int> RawFieldMap { get; private set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string IdentityKey
        {
            get
            {
                return string.Join(
                    "|",
                    new[]
                    {
                        this.MemberIdentity ?? string.Empty,
                        this.Slot ?? string.Empty,
                        this.EquipmentId ?? string.Empty
                    });
            }
        }

        public int GetValue(TargetAttribute attribute)
        {
            AttributeValue value = this.Attributes.FirstOrDefault(item => item.Type == attribute);
            return value == null ? 0 : value.CurrentValue;
        }

        public void SetAttributes(IEnumerable<AttributeValue> attributes)
        {
            this.Attributes.Clear();
            if (attributes == null) return;

            foreach (AttributeValue attribute in attributes)
            {
                if (attribute != null) this.Attributes.Add(attribute);
            }
        }

        public void SetRawFieldMap(IDictionary<string, int> values)
        {
            this.RawFieldMap.Clear();
            if (values == null) return;

            foreach (KeyValuePair<string, int> pair in values)
            {
                this.RawFieldMap[pair.Key] = pair.Value;
            }
        }

        public EquipmentAttributesSnapshot Clone()
        {
            EquipmentAttributesSnapshot copy = new EquipmentAttributesSnapshot
            {
                InstanceId = this.InstanceId,
                SlotIndex = this.SlotIndex,
                AttackPower = this.AttackPower,
                DefensePower = this.DefensePower,
                CritRate = this.CritRate,
                CritDamage = this.CritDamage,
                HitRate = this.HitRate,
                DodgeRate = this.DodgeRate,
                HpRecovery = this.HpRecovery,
                MpRecovery = this.MpRecovery,
                RareDegree = this.RareDegree,
                EquipmentName = this.EquipmentName,
                EquipmentType = this.EquipmentType,
                EquipmentQuality = this.EquipmentQuality,
                LastReadTime = this.LastReadTime,
                AttributeHash = this.AttributeHash,
                MemberIdentity = this.MemberIdentity,
                Slot = this.Slot,
                EquipmentId = this.EquipmentId,
                ItemId = this.ItemId,
                ItemTypeId = this.ItemTypeId,
                BaseAttributeHash = this.BaseAttributeHash,
                RefineAttributeHash = this.RefineAttributeHash,
                SnapshotId = this.SnapshotId,
                StreamSessionId = this.StreamSessionId,
                Sequence = this.Sequence,
                CandidateOnly = this.CandidateOnly,
                IsValid = this.IsValid,
                ReadError = this.ReadError,
                ReadTime = this.ReadTime
            };

            copy.SetAttributes(this.Attributes.Select(item => new AttributeValue
            {
                Type = item.Type,
                Name = item.Name,
                CurrentValue = item.CurrentValue,
                RawValue = item.RawValue,
                RawValueText = item.RawValueText,
                RawId = item.RawId,
                RawOrder = item.RawOrder,
                PropertyKey = item.PropertyKey,
                OriginalName = item.OriginalName,
                Unit = item.Unit,
                LastReadTime = item.LastReadTime
            }));
            copy.SetRawFieldMap(this.RawFieldMap);
            return copy;
        }
    }

    public enum RefineResult
    {
        Unknown = 0,
        Accepted = 1,
        Failed = 2,
        Cancelled = 3,
        Timeout = 4,
        GameNotResponding = 5
    }

    public enum RefineStopReason
    {
        None = 0,
        TargetReached = 1,
        UserStopped = 2,
        MaxAttemptsReached = 3,
        EquipmentNotFound = 4,
        EquipmentIdentityChanged = 5,
        AttributeReadFailed = 6,
        RefineResultTimeout = 7,
        GameProcessLost = 8,
        GameStateInvalid = 9,
        InternalError = 10
    }
}
