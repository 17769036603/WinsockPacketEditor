using System;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 五行升级操作适配器接口
    /// 负责提交五行升级的游戏操作
    /// </summary>
    public interface IWuxingUpgradeActionAdapter
    {
        /// <summary>
        /// 提交五行元素升级请求
        /// </summary>
        /// <param name="operation">升级操作配置，包含元素类型和卡片ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>操作结果</returns>
        Task<WuxingUpgradeResult> SubmitElementUpgradeAsync(WuxingUpgradeOperation operation, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 五行升级读取适配器接口
    /// 负责读取五行升级相关的游戏状态（可选，用于状态验证）
    /// </summary>
    public interface IWuxingUpgradeReadOnlyAdapter
    {
        /// <summary>
        /// 验证进程身份
        /// </summary>
        Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 等待状态刷新完成
        /// </summary>
        Task<bool> WaitForStateRefreshAsync(int stateVersion, int timeoutMs, CancellationToken cancellationToken);
    }
}