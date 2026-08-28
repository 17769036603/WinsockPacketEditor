using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 五行升级预设定义
    /// 用于自动化五行修炼升级流程
    /// </summary>
    public class WuxingUpgradePreset
    {
        public const int SchemaVersion = 1;

        public int SchemaVersionProperty { get; set; } = SchemaVersion;
        
        /// <summary>
        /// 预设名称
        /// </summary>
        public string Name { get; set; } = "五行升级";
        
        /// <summary>
        /// 每种元素升级次数（默认10次）
        /// 木→水→土→金→火 → 整体升1级
        /// </summary>
        public int ElementUpgradeCount { get; set; } = 10;
        
        /// <summary>
        /// 整体升级次数（默认1次）
        /// </summary>
        public int OverallUpgradeCount { get; set; } = 1;
        
        /// <summary>
        /// 是否启用轮次间隔
        /// </summary>
        public bool EnableRoundInterval { get; set; } = true;

        /// <summary>
        /// 每轮间隔时间（毫秒）
        /// </summary>
        public int RoundIntervalMs { get; set; } = 200;
        
        /// <summary>
        /// 每个请求间隔时间（毫秒）
        /// </summary>
        public int RequestIntervalMs { get; set; } = 100;
        
        /// <summary>
        /// 是否循环运行
        /// </summary>
        public bool EnableLooping { get; set; } = true;
        
        /// <summary>
        /// 最大运行轮数（0 表示无限循环）
        /// </summary>
        public int MaxRounds { get; set; } = 0;
        
        /// <summary>
        /// 木元素卡片ID（C2S_UseCsCard cardId 参数）
        /// </summary>
        public int WoodCardId { get; set; } = 1001;
        
        /// <summary>
        /// 水元素卡片ID
        /// </summary>
        public int WaterCardId { get; set; } = 1002;
        
        /// <summary>
        /// 土元素卡片ID
        /// </summary>
        public int SoilCardId { get; set; } = 1003;
        
        /// <summary>
        /// 金元素卡片ID
        /// </summary>
        public int GoldCardId { get; set; } = 1004;
        
        /// <summary>
        /// 火元素卡片ID
        /// </summary>
        public int FireCardId { get; set; } = 1005;
        
        /// <summary>
        /// 整体升级卡片ID
        /// </summary>
        public int OverallUpgradeCardId { get; set; } = 1006;
        
        /// <summary>
        /// 是否在每次操作后等待请求间隔时间
        /// </summary>
        public bool WaitForRequestInterval { get; set; } = true;
        
        /// <summary>
        /// 是否在每次轮次结束后等待轮次间隔时间
        /// </summary>
        public bool WaitForRoundInterval { get; set; } = true;
        
        /// <summary>
        /// 日志级别：0=仅错误, 1=正常, 2=详细
        /// </summary>
        public int LogLevel { get; set; } = 1;
        
        /// <summary>
        /// 验证预设有效性
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (ElementUpgradeCount < 1)
            {
                errorMessage = "元素升级次数必须大于0。";
                return false;
            }

            if (OverallUpgradeCount < 1)
            {
                errorMessage = "整体升级次数必须大于0。";
                return false;
            }

            if (RequestIntervalMs < 0)
            {
                errorMessage = "请求间隔时间不能为负数。";
                return false;
            }

            if (RoundIntervalMs < 0)
            {
                errorMessage = "轮次间隔时间不能为负数。";
                return false;
            }

            if (MaxRounds < 0)
            {
                errorMessage = "最大轮数不能为负数。";
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 五行升级运行上下文
    /// </summary>
    public class WuxingUpgradeRunWithContext
    {
        public WuxingUpgradePreset Preset { get; set; }
        public int CurrentRound { get; set; } = 1;
        public int CurrentElementIndex { get; set; } = 0;
        public int CurrentOperationCount { get; set; } = 0;
        public int TotalOperationsInRound { get; set; } = 0;
        public ElementalType? CurrentElementType { get; set; }
        public bool IsOverallUpgrade { get; set; } = false;
        public bool IsPaused { get; set; } = false;
        public string PauseReason { get; set; } = string.Empty;
        public DateTime RoundStartTime { get; set; }
        public List<WuxingUpgradeStepLog> LogEntries { get; set; } = new List<WuxingUpgradeStepLog>();
    }
}