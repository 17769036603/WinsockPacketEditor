using System;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 只读适配器负责读取游戏中的坐骑速度数据。
    /// 返回的快照不包含内存地址信息，仅包含验证后的数据。
    /// </summary>
    public interface IMountSpeedReadOnlyAdapter
    {
        /// <summary>
        /// 读取角色的基础移动速度。
        /// </summary>
        Task<MountSpeedSnapshot> ReadMountSpeedAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 验证进程身份。
        /// </summary>
        Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 操作适配器负责提交修改坐骑速度的操作。
    /// </summary>
    public interface IMountSpeedOperationAdapter
    {
        /// <summary>
        /// 设置角色的坐骑加成速度。
        /// </summary>
        Task<OperationResult> SetRideAddSpeedAsync(double rideAddSpeed, CancellationToken cancellationToken);

        /// <summary>
        /// 切换坐骑状态（乘坐/下马）。
        /// </summary>
        Task<OperationResult> ToggleMountAsync(bool mount, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 坐骑速度快照。
    /// </summary>
    public class MountSpeedSnapshot
    {
        public double BaseSpeed { get; set; } = 0;
        public double RideAddSpeed { get; set; } = 0;
        public double RoleMoveSpeed { get; set; } = 0;
        public bool IsMounted { get; set; } = false;
        public DateTime ReadAt { get; set; } = DateTime.UtcNow;
        public bool IsValid => BaseSpeed > 0;

        public override string ToString()
        {
            return $"baseSpeed={BaseSpeed}, rideAddSpeed={RideAddSpeed}, roleMoveSpeed={RoleMoveSpeed}, mounted={IsMounted}";
        }
    }

    /// <summary>
    /// 操作结果枚举。
    /// </summary>
    public enum OperationResult
    {
        Accepted,
        Rejected,
        Error
    }
}