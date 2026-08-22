using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 只读适配器负责读取游戏数据。
    /// 返回的快照不包含内存地址信息，仅包含验证后的数据。
    /// </summary>
    public interface IPetSkillBookReadOnlyAdapter
    {
        /// <summary>
        /// 读取当前参战召唤兽信息
        /// </summary>
        Task<CurrentPetSnapshot> ReadCurrentPetAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 读取召唤兽状态（技能格、技能列表、锁定状态）
        /// </summary>
        Task<PetStateSnapshot> ReadPetStateAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 读取背包资源（物品、银两）
        /// </summary>
        Task<InventorySnapshot> ReadResourcesAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 读取技能目录（可学习的技能列表）
        /// </summary>
        Task<List<SkillBookCatalogEntry>> ReadSkillCatalogAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 等待状态刷新完成
        /// </summary>
        Task<bool> WaitForStateRefreshAsync(int stateVersion, int timeoutMs, CancellationToken cancellationToken);

        /// <summary>
        /// 验证进程身份
        /// </summary>
        Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 操作适配器负责提交游戏操作。
    /// </summary>
    public interface IPetSkillBookOperationAdapter
    {
        /// <summary>
        /// 提交开启技能格操作
        /// </summary>
        Task<OperationResult> SubmitOpenSlotAsync(int slotIndex, CancellationToken cancellationToken);

        /// <summary>
        /// 提交使用技能书操作
        /// </summary>
        Task<OperationResult> SubmitStudyBookAsync(int itemId, int skillId, int slotIndex, CancellationToken cancellationToken);

        /// <summary>
        /// 提交锁定技能格操作
        /// </summary>
        Task<OperationResult> SubmitLockSkillSlotAsync(int slotIndex, CancellationToken cancellationToken);
    }
}