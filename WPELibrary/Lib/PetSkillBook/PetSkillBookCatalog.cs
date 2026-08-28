using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 技能书等级分类。
    /// SourceException 用于保留源码中单独标注、但尚未确认普通/高级/终极归属的技能。
    /// </summary>
    public enum PetSkillBookTier
    {
        Unknown = 0,
        Ordinary = 1,
        Advanced = 2,
        Ultimate = 3,
        SourceException = 4
    }

    /// <summary>
    /// 已确认的召唤兽技能目录条目。
    /// HexId 与 SkillId 保留为两个字段，便于和抓包、内存或日志中的表示互相核对。
    /// </summary>
    public sealed class PetSkillDefinition
    {
        public PetSkillDefinition(
            int skillId,
            int hexId,
            string name,
            PetSkillBookTier tier)
        {
            this.SkillId = skillId;
            this.HexId = hexId;
            this.Name = name ?? string.Empty;
            this.Tier = tier;
        }

        public int SkillId { get; private set; }

        public int HexId { get; private set; }

        public string Name { get; private set; }

        public PetSkillBookTier Tier { get; private set; }
    }

    /// <summary>
    /// 已确认的技能书或技能书礼包物品目录条目。
    /// </summary>
    public sealed class PetSkillBookItemDefinition
    {
        public PetSkillBookItemDefinition(
            int itemId,
            int hexId,
            string name,
            PetSkillBookTier tier,
            bool isBundle)
        {
            this.ItemId = itemId;
            this.HexId = hexId;
            this.Name = name ?? string.Empty;
            this.Tier = tier;
            this.IsBundle = isBundle;
        }

        public int ItemId { get; private set; }

        public int HexId { get; private set; }

        public string Name { get; private set; }

        public PetSkillBookTier Tier { get; private set; }

        public bool IsBundle { get; private set; }
    }

    /// <summary>
    /// 召唤兽技能和技能书物品的本地已知目录。
    /// 目录只用于预设校验、显示和只读预演，不读取背包，也不执行游戏操作。
    /// </summary>
    public static class PetSkillBookCatalog
    {
        private static readonly List<PetSkillDefinition> SkillDefinitions =
            new List<PetSkillDefinition>
            {
                new PetSkillDefinition(92001, 0x16761, "闪现", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92002, 0x16762, "妙手仁心", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92003, 0x16763, "天魔解体", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92004, 0x16764, "封印", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92005, 0x16765, "分光化影", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92006, 0x16766, "混乱", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92007, 0x16767, "青面獠牙", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92008, 0x16768, "忠诚", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92009, 0x16769, "小楼夜哭", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92010, 0x1676A, "大义", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92011, 0x1676B, "隔山打牛", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92012, 0x1676C, "自医", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92013, 0x1676D, "遗产", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92014, 0x1676E, "清明术", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92015, 0x1676F, "帐饮东都", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92016, 0x16770, "脱困术", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92017, 0x16771, "源泉万斛", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92018, 0x16772, "强心术", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92019, 0x16773, "神工鬼力", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92020, 0x16774, "取之有道", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92021, 0x16775, "倍道兼行", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92022, 0x16776, "灵犀诀", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92023, 0x16777, "分裂攻击", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92024, 0x16778, "以退为进", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92025, 0x16779, "慈乌反哺", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92026, 0x1677A, "清风拂面", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92027, 0x1677B, "反哺之私", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92028, 0x1677C, "蹒跚", PetSkillBookTier.Ordinary),
                new PetSkillDefinition(92029, 0x1677D, "仙风道骨", PetSkillBookTier.Ordinary),

                new PetSkillDefinition(92101, 0x167C5, "审时度势", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92102, 0x167C6, "分花拂柳", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92103, 0x167C7, "福禄双全", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92104, 0x167C8, "吉人天相", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92105, 0x167C9, "柳暗花明", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92106, 0x167CA, "春风拂面", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92107, 0x167CB, "视死如归", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92108, 0x167CC, "妙手回春", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92109, 0x167CD, "春意盎然", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92110, 0x167CE, "高级天魔解体", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92111, 0x167CF, "高级分光化影", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92112, 0x167D0, "高级青面獠牙", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92113, 0x167D1, "高级小楼夜哭", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92114, 0x167D2, "扶伤", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92115, 0x167D3, "天地同寿", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92116, 0x167D4, "朗月清风", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92117, 0x167D5, "舍生取义", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92118, 0x167D6, "遗患", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92119, 0x167D7, "悬刃", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92120, 0x167D8, "高级闪现", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92121, 0x167D9, "高级清明术", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92122, 0x167DA, "高级脱困术", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92123, 0x167DB, "高级强心术", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92124, 0x167DC, "高级分裂攻击", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92125, 0x167DD, "高级慈乌反哺", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92126, 0x167DE, "高级反哺之私", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92127, 0x167DF, "人来疯", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92128, 0x167E0, "讨命", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92129, 0x167E1, "回源", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92130, 0x167E2, "报复", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92131, 0x167E3, "隐身", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92132, 0x167E4, "高级隔山打牛", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92133, 0x167E5, "高级帐饮东都", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92134, 0x167E6, "高级源泉万斛", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92135, 0x167E7, "高级神工鬼力", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92136, 0x167E8, "高级倍道兼行", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92137, 0x167E9, "高级蹒跚", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92138, 0x167EA, "炊金馔玉", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92139, 0x167EB, "枯木逢春", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92140, 0x167EC, "如人饮水", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92141, 0x167ED, "风火燎原", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92142, 0x167EE, "西天净土", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92143, 0x167EF, "凝神屏气", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92144, 0x167F0, "百不得一", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92145, 0x167F1, "戕身伐命", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92146, 0x167F2, "一力拒守", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92147, 0x167F3, "高级仙风道骨", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92148, 0x167F4, "高级妙手仁心", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92149, 0x167F5, "抵制", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92150, 0x167F6, "断刃", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92151, 0x167F7, "长驱直入", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92152, 0x167F8, "相煎太急", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92153, 0x167F9, "纵横四海", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92154, 0x167FA, "坚韧", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92155, 0x167FB, "鸟尽弓藏", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92156, 0x167FC, "击其不意", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92157, 0x167FD, "牵制", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92158, 0x167FE, "掣肘", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92159, 0x167FF, "制衡", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92160, 0x16800, "进退自如", PetSkillBookTier.Advanced),

                new PetSkillDefinition(92201, 0x16829, "当头棒喝", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92202, 0x1682A, "春回大地", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92203, 0x1682B, "扭转乾坤", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92204, 0x1682C, "子虚乌有", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92205, 0x1682D, "明察秋毫", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92206, 0x1682E, "如沐春风", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92207, 0x1682F, "噤若寒蝉", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92208, 0x16830, "化无", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92209, 0x16831, "双管齐下", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92210, 0x16832, "成仁取义", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92211, 0x16833, "步履维艰", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92212, 0x16834, "将死", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92213, 0x16835, "作鸟兽散", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92214, 0x16836, "势如破竹", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92215, 0x16837, "绝境逢生", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92216, 0x16838, "天罡战气", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92217, 0x16839, "阴阳法眼", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92218, 0x1683A, "无色无相", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92219, 0x1683B, "因果业障", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92220, 0x1683C, "势不可挡", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92221, 0x1683D, "无色无相-超", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92222, 0x1683E, "阴阳法眼-超", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92223, 0x1683F, "超级飞魔", PetSkillBookTier.Advanced),
                new PetSkillDefinition(92224, 0x16840, "超级抗性", PetSkillBookTier.Advanced),

                new PetSkillDefinition(92190, 0x1681E, "超级闪现", PetSkillBookTier.SourceException)
            };

        private static readonly List<PetSkillBookItemDefinition> ItemDefinitions =
            new List<PetSkillBookItemDefinition>
            {
                new PetSkillBookItemDefinition(90027, 0x15FAB, "普通技能书", PetSkillBookTier.Ordinary, false),
                new PetSkillBookItemDefinition(99002, 0x182BA, "普通技能书", PetSkillBookTier.Ordinary, false),
                new PetSkillBookItemDefinition(99003, 0x182BB, "高级技能书", PetSkillBookTier.Advanced, false),
                new PetSkillBookItemDefinition(99004, 0x182BC, "终级技能书", PetSkillBookTier.Ultimate, false),
                new PetSkillBookItemDefinition(30010, 0x0753A, "高级技能书礼包", PetSkillBookTier.Advanced, true),
                new PetSkillBookItemDefinition(30011, 0x0753B, "普通技能书", PetSkillBookTier.Ordinary, false),
                new PetSkillBookItemDefinition(30018, 0x0753E, "终级技能书", PetSkillBookTier.Ultimate, false),
                new PetSkillBookItemDefinition(30040, 0x07570, "高级技能书礼包", PetSkillBookTier.Advanced, true),
                new PetSkillBookItemDefinition(30041, 0x07571, "终极技能书礼包", PetSkillBookTier.Ultimate, true)
            };

        private static readonly Dictionary<int, PetSkillDefinition> SkillsById =
            BuildSkillMap();

        private static readonly Dictionary<int, PetSkillBookItemDefinition> ItemsById =
            BuildItemMap();

        public static IList<PetSkillDefinition> Skills
        {
            get { return SkillDefinitions.AsReadOnly(); }
        }

        public static IList<PetSkillBookItemDefinition> Items
        {
            get { return ItemDefinitions.AsReadOnly(); }
        }

        public static bool TryGetSkill(int skillId, out PetSkillDefinition definition)
        {
            return SkillsById.TryGetValue(skillId, out definition);
        }

        public static bool TryGetItem(int itemId, out PetSkillBookItemDefinition definition)
        {
            return ItemsById.TryGetValue(itemId, out definition);
        }

        /// <summary>
        /// 校验已知技能与已知技能书的等级关系。
        /// 目录并非完整游戏数据库：任一侧尚未收录时保持兼容，只校验正数 ID。
        /// </summary>
        public static bool TryValidateEntry(
            int skillId,
            int itemId,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            if (skillId <= 0)
            {
                errorMessage = string.Format("技能书技能 ID 无效：{0}。", skillId);
                return false;
            }
            if (itemId <= 0)
            {
                errorMessage = string.Format("技能书物品 ID 无效：{0}。", itemId);
                return false;
            }

            PetSkillDefinition skill;
            PetSkillBookItemDefinition item;
            bool knownSkill = TryGetSkill(skillId, out skill);
            bool knownItem = TryGetItem(itemId, out item);
            if (!knownSkill || !knownItem || skill.Tier == PetSkillBookTier.SourceException)
            {
                return true;
            }

            if (skill.Tier != item.Tier)
            {
                errorMessage = string.Format(
                    "技能书等级不匹配：SkillId={0}「{1}」属于{2}，ItemId={3}「{4}」属于{5}。",
                    skill.SkillId,
                    skill.Name,
                    GetTierName(skill.Tier),
                    item.ItemId,
                    item.Name,
                    GetTierName(item.Tier));
                return false;
            }

            return true;
        }

        public static string DescribeEntry(int skillId, int itemId)
        {
            PetSkillDefinition skill;
            PetSkillBookItemDefinition item;
            bool knownSkill = TryGetSkill(skillId, out skill);
            bool knownItem = TryGetItem(itemId, out item);
            if (!knownSkill && !knownItem)
            {
                return string.Format("SkillId={0}, ItemId={1}（目录未收录）", skillId, itemId);
            }
            if (!knownSkill)
            {
                return string.Format(
                    "SkillId={0}（技能目录未收录），ItemId={1}「{2}」",
                    skillId,
                    itemId,
                    item.Name);
            }
            if (!knownItem)
            {
                return string.Format(
                    "SkillId={0}「{1}」，ItemId={2}（物品目录未收录）",
                    skillId,
                    skill.Name,
                    itemId);
            }

            return string.Format(
                "SkillId={0}「{1}」，ItemId={2}「{3}」",
                skillId,
                skill.Name,
                itemId,
                item.Name);
        }

        public static string GetTierName(PetSkillBookTier tier)
        {
            switch (tier)
            {
                case PetSkillBookTier.Ordinary:
                    return "普通";
                case PetSkillBookTier.Advanced:
                    return "高级";
                case PetSkillBookTier.Ultimate:
                    return "终极";
                case PetSkillBookTier.SourceException:
                    return "源码异常项";
                default:
                    return "未知";
            }
        }

        private static Dictionary<int, PetSkillDefinition> BuildSkillMap()
        {
            Dictionary<int, PetSkillDefinition> result =
                new Dictionary<int, PetSkillDefinition>();
            foreach (PetSkillDefinition definition in SkillDefinitions)
            {
                result.Add(definition.SkillId, definition);
            }
            return result;
        }

        private static Dictionary<int, PetSkillBookItemDefinition> BuildItemMap()
        {
            Dictionary<int, PetSkillBookItemDefinition> result =
                new Dictionary<int, PetSkillBookItemDefinition>();
            foreach (PetSkillBookItemDefinition definition in ItemDefinitions)
            {
                result.Add(definition.ItemId, definition);
            }
            return result;
        }
    }
}
