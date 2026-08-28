using System;
using System.Collections.Generic;
using WPELibrary.Lib.Memory;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 五行升级元素类型
    /// 对应界面上的木、水、土、金、火按钮顺序
    /// </summary>
    public enum ElementalType
    {
        Wood = 1,   // 木
        Water = 2,  // 水
        Soil = 3,   // 土
        Gold = 4,   // 金
        Fire = 5,   // 火
        Overall = 6 // 整体升级
    }

    /// <summary>
    /// 五行升级卡片类型 ID
    /// 不同元素的功能卡片ID
    /// </summary>
    public enum CsCardTypeId
    {
        Wood = 1,   // 木元素卡片ID
        Water = 2,  // 水元素卡片ID
        Soil = 3,   // 土元素卡片ID
        Gold = 4,   // 金元素卡片ID
        Fire = 5,   // 火元素卡片ID
        Overall = 6 // 整体升级卡片ID（升1级）
    }

    /// <summary>
    /// 五行升级步骤配置
    /// </summary>
    public class WuxingUpgradeStepConfig
    {
        public ElementalType ElementType { get; set; }
        public int TargetCount { get; set; } = 10;
        public int CurrentCount { get; set; } = 0;
        
        public WuxingUpgradeStepConfig() { }
        
        public WuxingUpgradeStepConfig(ElementalType type, int count)
        {
            ElementType = type;
            TargetCount = count;
        }
    }

    /// <summary>
    /// 五行升级轮次上下文
    /// </summary>
    public class WuxingUpgradeRoundContext
    {
        public int RoundNumber { get; set; } = 1;
        public List<WuxingUpgradeStepConfig> ElementSteps { get; set; } = new List<WuxingUpgradeStepConfig>();
        public int CurrentElementIndex { get; set; } = 0;
        public int CurrentOperationCount { get; set; } = 0;
        public DateTime RoundStartTime { get; set; }

        public WuxingUpgradeRoundContext()
        {
            // 初始化五行顺序：木→水→土→金→火→整体
            ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Wood, 10));
            ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Water, 10));
            ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Soil, 10));
            ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Gold, 10));
            ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Fire, 10));
            ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Overall, 1)); // 整体升级
        }
    }

    /// <summary>
    /// 五行升级运行上下文
    /// </summary>
    public class WuxingUpgradeContext
    {
        public WuxingUpgradePreset Preset { get; set; }
        public int TotalRounds { get; set; } = 0;
        public int CompletedRounds { get; set; } = 0;
        public WuxingUpgradeRoundContext CurrentRound { get; set; }
        public List<WuxingUpgradeStepLog> LogEntries { get; set; } = new List<WuxingUpgradeStepLog>();
        public bool IsPaused { get; set; } = false;
        public string PauseReason { get; set; } = string.Empty;
        
        public WuxingUpgradeContext()
        {
            CurrentRound = new WuxingUpgradeRoundContext();
        }
    }

    /// <summary>
    /// 五行升级步骤日志
    /// </summary>
    public class WuxingUpgradeStepLog
    {
        public string State { get; set; }
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }
    }

    /// <summary>
    /// 五行卡片信息
    /// </summary>
    public class WuxingCardInfo
    {
        public int CardId { get; set; }
        public int Count { get; set; }
        public DateTime ReadAt { get; set; }
    }

    /// <summary>
    /// 五行升级读取结果
    /// </summary>
    public class WuxingUpgradeInfo
    {
        public int CurrentLevel { get; set; }
        public int TargetLevel { get; set; }
        public Dictionary<ElementalType, int> ElementalLevels { get; set; } = new Dictionary<ElementalType, int>();
        public Dictionary<ElementalType, int> ElementalCounts { get; set; } = new Dictionary<ElementalType, int>();
        public DateTime ReadAt { get; set; }
    }

    /// <summary>
    /// 操作结果
    /// </summary>
    public enum WuxingUpgradeResult
    {
        Accepted = 0,
        Rejected = 1,
        Unknown = 2,
        Unavailable = 3
    }
}
