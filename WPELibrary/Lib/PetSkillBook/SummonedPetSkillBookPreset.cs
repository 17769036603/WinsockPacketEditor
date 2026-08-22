using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 召唤兽技能书预设定义
    /// 使用强类型模型，不写入假物品 ID。
    /// </summary>
    public class SummonedPetSkillBookPreset
    {
        public const int SchemaVersion = 1;

        public int SchemaVersionProperty { get; set; } = SchemaVersion;
        public string Name { get; set; } = "召唤兽技能";
        public PetMode PetMode { get; set; } = PetMode.Current;
        public int? PetId { get; set; } = null;
        public bool OpenAllSlots { get; set; } = true;
        public int? OpenItemId { get; set; } = null;
        public List<SkillBookEntry> Books { get; set; } = new List<SkillBookEntry>();
        public MissingMaterialPolicy MissingMaterialPolicy { get; set; } = MissingMaterialPolicy.Pause;
        public bool StopOnError { get; set; } = true;
        public int TimeoutMs { get; set; } = 8000;
        public bool EnableStepLog { get; set; } = true;

        /// <summary>
        /// 开格银两成本。0 表示未配置：不检查银两余额，也不做扣减判断。
        /// 真实成本未确认前不得填写猜测值。
        /// </summary>
        public int OpenSlotSilverCost { get; set; } = 0;

        /// <summary>
        /// 学习技能书银两成本。0 表示未配置：不检查银两余额，也不做扣减判断。
        /// 真实成本未确认前不得填写猜测值。
        /// </summary>
        public int StudySilverCost { get; set; } = 0;

        /// <summary>
        /// 获取预设是否有效（books 不能为空）
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (Books == null || Books.Count == 0)
            {
                errorMessage = "技能书列表为空，必须添加至少一个技能书。";
                return false;
            }

            foreach (var book in Books)
            {
                if (book.SkillId <= 0)
                {
                    errorMessage = $"技能书技能 ID 无效：{book.SkillId}。";
                    return false;
                }
                if (book.ItemId <= 0)
                {
                    errorMessage = $"技能书物品 ID 无效：{book.ItemId}。";
                    return false;
                }
            }

            if (TimeoutMs <= 0)
            {
                errorMessage = $"超时时间无效：{TimeoutMs}。";
                return false;
            }

            if (OpenAllSlots && !OpenItemId.HasValue)
            {
                errorMessage = "自动开满技能格时必须指定开格材料物品 ID。";
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 召唤兽技能书预设运行上下文
    /// </summary>
    public class SummonedPetSkillBookContext
    {
        public SummonedPetSkillBookPreset Preset { get; set; }
        public int CurrentPetId { get; set; }
        public PetStateSnapshot PetState { get; set; }
        public InventorySnapshot Inventory { get; set; }
        public List<StepLogEntry> LogEntries { get; set; } = new List<StepLogEntry>();
        public int CurrentBookIndex { get; set; } = 0;
        public bool IsPaused { get; set; } = false;
        public string PauseReason { get; set; } = string.Empty;
    }
}