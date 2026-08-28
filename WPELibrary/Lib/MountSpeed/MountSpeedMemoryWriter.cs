using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.Memory;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 基于内存写入的坐骑速度写入器。
    /// 
    /// 使用说明：
    /// 1. 在游戏进程中使用内存扫描工具（如 Cheat Engine）找到坐骑速度的内存地址
    /// 2. 将发现的地址填入配置中，然后调用 SetMemoryAddresses 方法配置写入器
    /// 3. 该写入器将负责实际的内存写入操作
    /// 
    /// 注意：部分游戏会定期从服务器同步速度数据，内存写入可能被覆盖。
    /// </summary>
    public class MountSpeedMemoryWriter : IMountSpeedMemoryWriter
    {
        private readonly ReadOnlyProcessIdentity _processIdentity;
        private WritableProcessMemoryHelper _memoryWriter;
        
        /// <summary>
        /// 坐骑速度加成的内存地址（字节数组形式）。
        /// </summary>
        public long RideAddSpeedAddress { get; private set; } = 0;

        /// <summary>
        /// 坐骑状态（是否乘坐）的内存地址。
        /// </summary>
        public long IsMountedAddress { get; private set; } = 0;

        /// <summary>
        /// 角色当前移动速度的内存地址。
        /// </summary>
        public long RoleMoveSpeedAddress { get; private set; } = 0;

        /// <summary>
        /// 速度值的写入数据类型。默认为 Float。
        /// </summary>
        public SpeedValueType SpeedValueType { get; set; } = SpeedValueType.Float;

        /// <summary>
        /// 坐骑状态的写入数据类型。默认为 Int32（1=乘坐，0=下马）。
        /// </summary>
        public DataType MountStateType { get; set; } = DataType.Int32;

        public MountSpeedMemoryWriter(ReadOnlyProcessIdentity processIdentity)
        {
            _processIdentity = processIdentity ?? throw new ArgumentNullException(nameof(processIdentity));
        }

        /// <summary>
        /// 设置内存地址配置。
        /// </summary>
        /// <param name="rideAddSpeedAddress">坐骑速度加成地址</param>
        /// <param name="isMountedAddress">坐骑状态地址</param>
        /// <param name="roleMoveSpeedAddress">角色移动速度地址</param>
        public void SetMemoryAddresses(
            long rideAddSpeedAddress,
            long isMountedAddress = 0,
            long roleMoveSpeedAddress = 0)
        {
            RideAddSpeedAddress = rideAddSpeedAddress;
            IsMountedAddress = isMountedAddress;
            RoleMoveSpeedAddress = roleMoveSpeedAddress;
        }

        /// <summary>
        /// 设置速度值的数据类型。
        /// </summary>
        public void SetSpeedValueType(SpeedValueType type)
        {
            SpeedValueType = type;
        }

        public async Task<OperationResult> SetRideAddSpeedAsync(double rideAddSpeed, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OperationResult.Rejected;
            }

            if (RideAddSpeedAddress <= 0)
            {
                return OperationResult.Error;
            }

            try
            {
                if (_memoryWriter == null)
                {
                    if (!WritableProcessMemoryHelper.TryOpen(_processIdentity, out _memoryWriter, out string error))
                    {
                        return OperationResult.Error;
                    }
                }

                byte[] bytes;
                switch (SpeedValueType)
                {
                    case SpeedValueType.Double:
                        bytes = BitConverter.GetBytes(rideAddSpeed);
                        break;

                    case SpeedValueType.Int32:
                        bytes = BitConverter.GetBytes((int)rideAddSpeed);
                        break;

                    default:
                        bytes = BitConverter.GetBytes((float)rideAddSpeed);
                        break;
                }

                bool success = await Task.Run(() => _memoryWriter.WriteBytes(RideAddSpeedAddress, bytes));
                return success ? OperationResult.Accepted : OperationResult.Error;
            }
            catch (Exception)
            {
                return OperationResult.Error;
            }
        }

        public async Task<OperationResult> ToggleMountAsync(bool mount, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OperationResult.Rejected;
            }

            if (IsMountedAddress <= 0)
            {
                return OperationResult.Error;
            }

            try
            {
                if (_memoryWriter == null)
                {
                    if (!WritableProcessMemoryHelper.TryOpen(_processIdentity, out _memoryWriter, out string error))
                    {
                        return OperationResult.Error;
                    }
                }

                byte[] bytes;
                switch (MountStateType)
                {
                    case DataType.Bool:
                    case DataType.Byte:
                        bytes = new byte[] { (byte)(mount ? 1 : 0) };
                        break;

                    default:
                        bytes = BitConverter.GetBytes(mount ? 1 : 0);
                        break;
                }

                bool success = await Task.Run(() => _memoryWriter.WriteBytes(IsMountedAddress, bytes));
                return success ? OperationResult.Accepted : OperationResult.Error;
            }
            catch (Exception)
            {
                return OperationResult.Error;
            }
        }

        public void Dispose()
        {
            _memoryWriter?.Dispose();
            _memoryWriter = null;
        }
    }

    /// <summary>
    /// 进程内存写入帮助类。
    /// 提供对目标进程内存的写入能力。
    /// </summary>
    public class WritableProcessMemoryHelper : IDisposable
    {
        private readonly IntPtr _processHandle;
        private readonly int _pointerSize;
        private bool _disposed = false;

        private WritableProcessMemoryHelper(IntPtr processHandle, int pointerSize)
        {
            _processHandle = processHandle;
            _pointerSize = pointerSize;
        }

        public int PointerSize => _pointerSize;

        public static bool TryOpen(ReadOnlyProcessIdentity identity, out WritableProcessMemoryHelper writer, out string error)
        {
            writer = null;
            error = string.Empty;

            if (identity == null)
            {
                error = "进程身份不能为null。";
                return false;
            }

            try
            {
                // 需要 PROCESS_VM_WRITE 和 PROCESS_VM_OPERATION 权限
                const uint PROCESS_VM_WRITE = 0x0020;
                const uint PROCESS_VM_OPERATION = 0x0008;
                uint desiredAccess = PROCESS_VM_WRITE | PROCESS_VM_OPERATION;

                IntPtr handle = NativeMethodsForMountSpeed.OpenProcess(
                    desiredAccess,
                    false,
                    identity.ProcessId);

                if (handle == IntPtr.Zero)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    error = $"OpenProcess 失败，错误码: {errorCode}";
                    return false;
                }

                writer = new WritableProcessMemoryHelper(handle, identity.PointerSize);
                return true;
            }
            catch (Exception ex)
            {
                error = $"打开进程内存写入器失败: {ex.Message}";
                return false;
            }
        }

        public bool WriteBytes(long address, byte[] data)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WritableProcessMemoryHelper));
            }
            if (data == null || data.Length == 0)
            {
                throw new ArgumentException("数据不能为null或空", nameof(data));
            }

            try
            {
                UIntPtr bytesRead;
                bool result = NativeMethodsForMountSpeed.WriteProcessMemory(
                    _processHandle,
                    new IntPtr(address),
                    data,
                    new UIntPtr((uint)data.Length),
                    out bytesRead);

                return result && bytesRead.ToUInt64() == (ulong)data.Length;
            }
            catch
            {
                return false;
            }
        }

        public bool WriteInt32(long address, int value)
        {
            return WriteBytes(address, BitConverter.GetBytes(value));
        }

        public bool WriteFloat(long address, float value)
        {
            return WriteBytes(address, BitConverter.GetBytes(value));
        }

        public bool WriteDouble(long address, double value)
        {
            return WriteBytes(address, BitConverter.GetBytes(value));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                NativeMethodsForMountSpeed.CloseHandle(_processHandle);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 速度值的数据类型。
    /// </summary>
    public enum SpeedValueType
    {
        Float,
        Double,
        Int32
    }

    /// <summary>
    /// 数据类型。
    /// </summary>
    public enum DataType
    {
        Int32,
        Int64,
        UInt32,
        UInt64,
        Float,
        Double,
        Bool,
        Byte,
        Char
    }

    /// <summary>
    /// 内存写入的本地方法。
    /// </summary>
    internal static class NativeMethodsForMountSpeed
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WriteProcessMemory(
            IntPtr process,
            IntPtr baseAddress,
            byte[] buffer,
            UIntPtr size,
            out UIntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}