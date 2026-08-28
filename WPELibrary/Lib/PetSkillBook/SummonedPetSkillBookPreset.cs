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
        public const int SchemaVersion = 2;

        public int SchemaVersionProperty { get; set; } = SchemaVersion;
        public string Name { get; set; } = "召唤兽技能";
        public PetMode PetMode { get; set; } = PetMode.Current;

        /// <summary>
        /// 可选目标宠物 ID。
        /// 当 PetMode 为 Specified 时有效；为 null 或 0 时表示当前参战宠物。
        /// </summary>
        public int PetId { get; set; } = 0;

        /// <summary>
        /// 学习后锁定技能格。
        /// 运行时强制为 true：每本技能书学习成功后必须锁定变化技能格，
        /// 该字段不再作为可配置项，仅保留用于序列化兼容。
        /// </summary>
        public bool LockAfter { get; set; } = true;

        /// <summary>
        /// 自动开满技能格。
        /// 运行时固定为 true：运行器始终自动开满所有技能格，不提供关闭选项。
        /// </summary>
        public bool OpenAllSlots { get; set; } = true;

        /// <summary>
        /// 开格材料物品 ID。旧配置兼容字段；当前运行流程不做材料、银两或目录预检。
        /// </summary>
        public int? OpenItemId { get; set; } = null;

        public List<SkillBookEntry> Books { get; set; } = new List<SkillBookEntry>();

        /// <summary>
        /// 缺失材料处理策略。旧配置兼容字段；当前运行流程不读取材料快照。
        /// </summary>
        public MissingMaterialPolicy MissingMaterialPolicy { get; set; } = MissingMaterialPolicy.Pause;

        public bool StopOnError { get; set; } = true;
        public int TimeoutMs { get; set; } = 8000;
        public bool EnableStepLog { get; set; } = true;

        /// <summary>
        /// 开格银两成本。0 表示未配置：不检查银两余额，也不做扣减判断。
        /// 真实成本未确认前不得填写猜测值。
        /// 保留字段：运行时不再使用，仅为旧模板 JSON 兼容保留。
        /// </summary>
        public int OpenSlotSilverCost { get; set; } = 0;

        /// <summary>
        /// 学习技能书银两成本。0 表示未配置：不检查银两余额，也不做扣减判断。
        /// 真实成本未确认前不得填写猜测值。
        /// 保留字段：运行时不再使用，仅为旧模板 JSON 兼容保留。
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

                string catalogError;
                if (!PetSkillBookCatalog.TryValidateEntry(
                    book.SkillId,
                    book.ItemId,
                    out catalogError))
                {
                    errorMessage = catalogError;
                    return false;
                }
            }

            if (TimeoutMs <= 0)
            {
                errorMessage = $"超时时间无效：{TimeoutMs}。";
                return false;
            }

            // OpenAllSlots、LockAfter 为运行时强制字段。
            // OpenItemId、材料策略和银两字段仅为旧配置兼容保留，运行时不做资源预检。

            // 验证指定宠物模式下 PetId 必须有效
            if (PetMode == PetMode.Specified && PetId <= 0)
            {
                errorMessage = "目标宠物模式为\"指定宠物\"时必须填写有效的宠物 ID。";
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
        public SummonedPetSkillBookRunStatus RunStatus { get; set; } = new SummonedPetSkillBookRunStatus();
    }
}
