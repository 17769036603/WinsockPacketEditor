using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 法术升级操作结果
    /// </summary>
    public enum SkillUpgradeResult
    {
        Accepted = 0,
        Rejected = 1,
        Unknown = 2,
        Unavailable = 3
    }

    /// <summary>
    /// 单个法术升级任务：目标法术与目标等级
    /// 默认值均为未经验证的占位值，需按真实抓包结果修正
    /// </summary>
    public class SkillUpgradeTask
    {
        public int SkillId { get; set; }
        public int TargetLevel { get; set; }
        public string Remark { get; set; } = string.Empty;
    }

    /// <summary>
    /// 法术升级操作参数
    /// roleId 不在此定义：发送时始终沿用捕获模板中的真实角色 ID，不硬编码
    /// </summary>
    public class SkillUpgradeOperation
    {
        public int SkillId { get; set; }
        public int TargetLevel { get; set; }
    }

    /// <summary>
    /// 法术升级步骤日志
    /// </summary>
    public class SkillUpgradeStepLog
    {
        public string State { get; set; }
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 法术升级运行上下文
    /// </summary>
    public class SkillUpgradeContext
    {
        public SkillUpgradePreset Preset { get; set; }
        public int CurrentTaskIndex { get; set; } = 0;
        public int CompletedTasks { get; set; } = 0;
        public bool IsPaused { get; set; } = false;
        public string PauseReason { get; set; } = string.Empty;
        public List<SkillUpgradeStepLog> LogEntries { get; } = new List<SkillUpgradeStepLog>();
    }
}
