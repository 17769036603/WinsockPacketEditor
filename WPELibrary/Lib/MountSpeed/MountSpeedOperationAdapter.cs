using System;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑速度操作适配器。
    /// 提供坐骑速度的写入功能。
    /// 
    /// 使用说明：
    /// 1. 通过内存写入：实现 IMountSpeedMemoryWriter 接口，指定坐骑速度的内存地址
    /// 2. 通过封包注入：实现 IMountSpeedPacketInjector 接口，将修改速度的封包注入游戏
    /// </summary>
    public class MountSpeedOperationAdapter : IMountSpeedOperationAdapter
    {
        private readonly IMountSpeedMemoryWriter _memoryWriter;
        private readonly IMountSpeedPacketInjector _packetInjector;

        /// <summary>
        /// 使用内存写入器创建适配器。
        /// </summary>
        public MountSpeedOperationAdapter(IMountSpeedMemoryWriter memoryWriter)
        {
            _memoryWriter = memoryWriter ?? throw new ArgumentNullException(nameof(memoryWriter));
        }

        /// <summary>
        /// 使用封包注入器创建适配器。
        /// </summary>
        public MountSpeedOperationAdapter(IMountSpeedPacketInjector packetInjector)
        {
            _packetInjector = packetInjector ?? throw new ArgumentNullException(nameof(packetInjector));
        }

        /// <summary>
        /// 使用两个注入器创建适配器（优先使用内存写入）。
        /// </summary>
        public MountSpeedOperationAdapter(
            IMountSpeedMemoryWriter memoryWriter,
            IMountSpeedPacketInjector packetInjector)
        {
            _memoryWriter = memoryWriter;
            _packetInjector = packetInjector;
        }

        /// <summary>
        /// 设置角色的坐骑速度加成。
        /// </summary>
        public async Task<OperationResult> SetRideAddSpeedAsync(double rideAddSpeed, CancellationToken cancellationToken)
        {
            if (_memoryWriter != null)
            {
                return await _memoryWriter.SetRideAddSpeedAsync(rideAddSpeed, cancellationToken);
            }

            if (_packetInjector != null)
            {
                return await _packetInjector.SetRideAddSpeedAsync(rideAddSpeed, cancellationToken);
            }

            return OperationResult.Error;
        }

        /// <summary>
        /// 切换坐骑状态（乘坐/下马）。
        /// </summary>
        public async Task<OperationResult> ToggleMountAsync(bool mount, CancellationToken cancellationToken)
        {
            if (_memoryWriter != null)
            {
                return await _memoryWriter.ToggleMountAsync(mount, cancellationToken);
            }

            if (_packetInjector != null)
            {
                return await _packetInjector.ToggleMountAsync(mount, cancellationToken);
            }

            return OperationResult.Error;
        }
    }

    /// <summary>
    /// 内存写入器接口。
    /// 实现此接口可通过直接写入游戏进程内存来修改坐骑速度。
    /// 
    /// 实现提示：
    /// - 需要使用 Windows API WriteProcessMemory 写入目标进程内存
    /// - 需要获得目标进程的 PROCESS_VM_WRITE 权限
    /// - 位于 WPELibrary.Lib.Memory 命名空间下的内存写入工具可供参考
    /// </summary>
    public interface IMountSpeedMemoryWriter
    {
        /// <summary>
        /// 设置坐骑速度加成。
        /// </summary>
        /// <param name="rideAddSpeed">坐骑速度加成百分比</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>操作结果</returns>
        Task<OperationResult> SetRideAddSpeedAsync(double rideAddSpeed, CancellationToken cancellationToken);

        /// <summary>
        /// 切换坐骑状态。
        /// </summary>
        /// <param name="mount">true 为乘坐，false 为下马</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>操作结果</returns>
        Task<OperationResult> ToggleMountAsync(bool mount, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 封包注入器接口。
    /// 实现此接口可通过修改游戏网络封包来间接改变坐骑速度。
    /// 
    /// 实现提示：
    /// - 监听游戏的速度相关封包（如坐骑召唤、速度变更协议）
    /// - 修改封包中的速度数据字段
    /// - 重新注入修改后的封包
    /// </summary>
    public interface IMountSpeedPacketInjector
    {
        /// <summary>
        /// 设置坐骑速度加成。
        /// </summary>
        /// <param name="rideAddSpeed">坐骑速度加成百分比</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>操作结果</returns>
        Task<OperationResult> SetRideAddSpeedAsync(double rideAddSpeed, CancellationToken cancellationToken);

        /// <summary>
        /// 切换坐骑状态。
        /// </summary>
        /// <param name="mount">true 为乘坐，false 为下马</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>操作结果</returns>
        Task<OperationResult> ToggleMountAsync(bool mount, CancellationToken cancellationToken);
    }
}