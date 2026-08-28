using System;
using System.Collections.Generic;
using WPELibrary.Lib.Memory;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 宠物参战快照
    /// </summary>
    public class CurrentPetSnapshot
    {
        public int PetId { get; set; }
        public bool IsCurrentParticipant { get; set; }
        public DateTime ReadAt { get; set; }
        public ReadOnlyProcessIdentity Identity { get; set; }
    }

    /// <summary>
    /// 宠物状态快照
    /// </summary>
    public class PetStateSnapshot
    {
        public int PetId { get; set; }
        public int StateVersion { get; set; }
        public int OpenSlotCount { get; set; }
        public int MaxSlotCount { get; set; }
        public List<PetSkillSlotSnapshot> SkillSlots { get; set; }
        public DateTime ReadAt { get; set; }
    }

    /// <summary>
    /// 技能格快照
    /// </summary>
    public class PetSkillSlotSnapshot
    {
        public int SlotIndex { get; set; }
        public bool IsOpen { get; set; }
        public bool IsLocked { get; set; }
        public int SkillId { get; set; }
    }

    /// <summary>
    /// 背包快照
    /// </summary>
    public class InventorySnapshot
    {
        public List<InventoryItemSnapshot> Items { get; set; }
        public int Silver { get; set; }
        public DateTime ReadAt { get; set; }
    }

    /// <summary>
    /// 背包物品快照
    /// </summary>
    public class InventoryItemSnapshot
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// 技能书目录条目
    /// </summary>
    public class SkillBookCatalogEntry
    {
        public int SkillId { get; set; }
        public int ItemId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public int Level { get; set; }
        public bool IsKnown { get; set; }
    }

    /// <summary>
    /// 宠物模式
    /// </summary>
    public enum PetMode
    {
        Current = 0,
        Specified = 1
    }

    /// <summary>
    /// 缺失材料处理策略
    /// </summary>
    public enum MissingMaterialPolicy
    {
        Pause = 0
    }

    /// <summary>
    /// 操作结果
    /// </summary>
    public enum OperationResult
    {
        Accepted = 0,
        Rejected = 1,
        Unknown = 2,
        Unavailable = 3
    }

    /// <summary>
    /// 步骤日志
    /// </summary>
    public class StepLogEntry
    {
        public string State { get; set; }
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }
    }

    /// <summary>
    /// 一本技能书的配置
    /// </summary>
    public class SkillBookEntry
    {
        public SkillBookEntry() { }

        public SkillBookEntry(int skillId, int itemId, bool lockAfter = false)
        {
            this.SkillId = skillId;
            this.ItemId = itemId;
            this.LockAfter = lockAfter;
        }

        public int SkillId { get; set; }
        public int ItemId { get; set; }
        public bool LockAfter { get; set; }
    }

    /// <summary>
    /// 技能书执行状态
    /// </summary>
    public enum BookExecutionStatus
    {
        /// <summary>
        /// 跳过：技能已存在（无需学习）
        /// </summary>
        SKIPPED_ALREADY_PRESENT = 0,
        /// <summary>
        /// 执行成功
        /// </summary>
        SUCCESS = 1,
        /// <summary>
        /// 执行失败
        /// </summary>
        FAILED = 2
    }

    /// <summary>
    /// 单本技能书执行结果
    /// </summary>
    public class BookExecutionResult
    {
        public BookExecutionStatus Status { get; set; }
        public int SkillId { get; set; }
        public int ItemId { get; set; }
        public int Sequence { get; set; }
        public int? SkillSlotIndex { get; set; }
        public string FailureReason { get; set; } = string.Empty;
        public string StateVersionBefore { get; set; } = string.Empty;
        public string StateVersionAfter { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; }
    }

    /// <summary>
    /// 召唤兽技能书整体运行状态
    /// </summary>
    public class SummonedPetSkillBookRunStatus
    {
        public bool IsCompleted { get; set; }
        public bool IsFailed { get; set; }
        public bool IsCancelled { get; set; }
        public List<BookExecutionResult> BookResults { get; set; } = new List<BookExecutionResult>();
        public string FailureReason { get; set; } = string.Empty;
        public DateTime CompletedAt { get; set; }
    }
}