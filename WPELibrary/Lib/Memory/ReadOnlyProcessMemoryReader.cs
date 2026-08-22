using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace WPELibrary.Lib.Memory
{
    /// <summary>
    /// Captures the minimum identity needed to prevent a PID-reuse read.
    /// This type contains no address or game-specific layout information.
    /// </summary>
    public sealed class ReadOnlyProcessIdentity
    {
        private ReadOnlyProcessIdentity(
            int processId,
            string processName,
            string processPath,
            long startTimeUtcTicks,
            int pointerSize)
        {
            this.ProcessId = processId;
            this.ProcessName = processName ?? string.Empty;
            this.ProcessPath = processPath ?? string.Empty;
            this.StartTimeUtcTicks = startTimeUtcTicks;
            this.PointerSize = pointerSize;
        }

        public int ProcessId { get; private set; }

        public string ProcessName { get; private set; }

        public string ProcessPath { get; private set; }

        public long StartTimeUtcTicks { get; private set; }

        public int PointerSize { get; private set; }

        public static bool TryCapture(
            int processId,
            out ReadOnlyProcessIdentity identity,
            out string error)
        {
            identity = null;
            error = string.Empty;
            if (processId <= 0)
            {
                error = "A positive process ID is required.";
                return false;
            }

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    process.Refresh();
                    DateTime startTime = process.StartTime;
                    string processPath = string.Empty;
                    try
                    {
                        processPath = process.MainModule == null
                            ? string.Empty
                            : process.MainModule.FileName ?? string.Empty;
                    }
                    catch (Exception)
                    {
                        // A path is useful evidence but is not required when
                        // the PID and start time are available.
                    }

                    IntPtr processHandle = ReadOnlyProcessMemoryNativeMethods.OpenProcess(
                        ReadOnlyProcessMemoryNativeMethods.ProcessQueryLimitedInformation |
                        ReadOnlyProcessMemoryNativeMethods.ProcessVmRead,
                        false,
                        processId);
                    if (processHandle == IntPtr.Zero)
                    {
                        error = BuildWin32Error("OpenProcess");
                        return false;
                    }

                    try
                    {
                        int pointerSize = GetPointerSize(processHandle);
                        identity = new ReadOnlyProcessIdentity(
                            processId,
                            process.ProcessName,
                            processPath,
                            startTime.ToUniversalTime().Ticks,
                            pointerSize);
                        return true;
                    }
                    finally
                    {
                        ReadOnlyProcessMemoryNativeMethods.CloseHandle(processHandle);
                    }
                }
            }
            catch (Exception ex)
            {
                error = "The target process identity could not be captured: " + ex.Message;
                return false;
            }
        }

        public bool MatchesCurrentProcess(out string error)
        {
            error = string.Empty;
            try
            {
                using (Process process = Process.GetProcessById(this.ProcessId))
                {
                    process.Refresh();
                    long startTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
                    if (startTimeUtcTicks != this.StartTimeUtcTicks)
                    {
                        error = "The target process was replaced; its start time changed.";
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(this.ProcessName) &&
                        !string.Equals(
                            process.ProcessName,
                            this.ProcessName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        error = "The target process name changed.";
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(this.ProcessPath))
                    {
                        string currentPath;
                        try
                        {
                            currentPath = process.MainModule == null
                                ? string.Empty
                                : process.MainModule.FileName ?? string.Empty;
                        }
                        catch (Exception ex)
                        {
                            error = "The target process path could not be verified: " + ex.Message;
                            return false;
                        }

                        if (!string.Equals(
                            currentPath,
                            this.ProcessPath,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            error = "The target process path changed.";
                            return false;
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "The target process identity could not be verified: " + ex.Message;
                return false;
            }
        }

        private static int GetPointerSize(IntPtr processHandle)
        {
            bool isWow64;
            if (!ReadOnlyProcessMemoryNativeMethods.IsWow64Process(processHandle, out isWow64))
            {
                return 0;
            }

            return Environment.Is64BitOperatingSystem && !isWow64 ? 8 : 4;
        }

        private static string BuildWin32Error(string operation)
        {
            int errorCode = Marshal.GetLastWin32Error();
            return string.Format(
                "{0} failed with Win32 error {1}.",
                operation,
                errorCode);
        }
    }

    /// <summary>
    /// A bounded, read-only process-memory reader.
    ///
    /// The reader accepts explicit addresses only. It deliberately exposes no
    /// write, allocation, remote-thread, module-scan, or signature-scan API.
    /// Game-specific readers must validate their own versioned layout before
    /// calling these methods.
    /// </summary>
    public sealed class ReadOnlyProcessMemoryReader : IDisposable
    {
        private const int MaximumReadBytes = 1024 * 1024;
        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessQueryLimitedInformation = 0x1000;

        private readonly SafeProcessHandle processHandle;
        private readonly ReadOnlyProcessIdentity identity;
        private bool disposed;

        private ReadOnlyProcessMemoryReader(
            SafeProcessHandle processHandle,
            ReadOnlyProcessIdentity identity)
        {
            this.processHandle = processHandle;
            this.identity = identity;
        }

        public ReadOnlyProcessIdentity Identity
        {
            get { return this.identity; }
        }

        public bool IsDisposed
        {
            get { return this.disposed; }
        }

        public static bool TryOpen(
            ReadOnlyProcessIdentity identity,
            out ReadOnlyProcessMemoryReader reader,
            out string error)
        {
            reader = null;
            error = string.Empty;
            if (identity == null)
            {
                error = "A process identity is required.";
                return false;
            }

            string identityError;
            if (!identity.MatchesCurrentProcess(out identityError))
            {
                error = identityError;
                return false;
            }
            if (identity.PointerSize != 4 && identity.PointerSize != 8)
            {
                error = "The target process pointer size could not be determined.";
                return false;
            }
            if (IntPtr.Size < identity.PointerSize)
            {
                error = "The current reader process cannot represent the target process addresses.";
                return false;
            }

            IntPtr handle = ReadOnlyProcessMemoryNativeMethods.OpenProcess(
                ProcessQueryLimitedInformation | ProcessVmRead,
                false,
                identity.ProcessId);
            if (handle == IntPtr.Zero)
            {
                error = BuildWin32Error("OpenProcess");
                return false;
            }

            reader = new ReadOnlyProcessMemoryReader(
                new SafeProcessHandle(handle),
                identity);
            return true;
        }

        public bool EnsureProcessIdentity(out string error)
        {
            if (this.disposed)
            {
                error = "The memory reader has been disposed.";
                return false;
            }

            return this.identity.MatchesCurrentProcess(out error);
        }

        public bool TryReadBytes(
            long address,
            int length,
            out byte[] value,
            out string error)
        {
            value = null;
            error = string.Empty;
            if (this.disposed)
            {
                error = "The memory reader has been disposed.";
                return false;
            }
            if (address <= 0)
            {
                error = "A positive memory address is required.";
                return false;
            }
            if (this.identity.PointerSize == 4 && address > uint.MaxValue)
            {
                error = "The address does not fit the target process pointer size.";
                return false;
            }
            if (IntPtr.Size == 4 && address > int.MaxValue)
            {
                error = "The address does not fit the current reader process pointer size.";
                return false;
            }
            if (length <= 0 || length > MaximumReadBytes)
            {
                error = string.Format(
                    "Memory read length must be between 1 and {0} bytes.",
                    MaximumReadBytes);
                return false;
            }
            if (address > long.MaxValue - length)
            {
                error = "The requested memory range overflows the address type.";
                return false;
            }

            byte[] buffer = new byte[length];
            UIntPtr bytesRead;
            bool succeeded = ReadOnlyProcessMemoryNativeMethods.ReadProcessMemory(
                this.processHandle.DangerousGetHandle(),
                new IntPtr(address),
                buffer,
                new UIntPtr((uint)length),
                out bytesRead);
            ulong readLength = bytesRead.ToUInt64();
            if (!succeeded || readLength != (ulong)length)
            {
                error = BuildWin32Error("ReadProcessMemory");
                return false;
            }

            value = buffer;
            return true;
        }

        public bool TryReadInt32(
            long address,
            out int value,
            out string error)
        {
            byte[] bytes;
            if (!this.TryReadBytes(address, sizeof(int), out bytes, out error))
            {
                value = 0;
                return false;
            }

            value = BitConverter.ToInt32(bytes, 0);
            return true;
        }

        public bool TryReadInt64(
            long address,
            out long value,
            out string error)
        {
            byte[] bytes;
            if (!this.TryReadBytes(address, sizeof(long), out bytes, out error))
            {
                value = 0L;
                return false;
            }

            value = BitConverter.ToInt64(bytes, 0);
            return true;
        }

        public bool TryReadUInt32(
            long address,
            out uint value,
            out string error)
        {
            byte[] bytes;
            if (!this.TryReadBytes(address, sizeof(uint), out bytes, out error))
            {
                value = 0U;
                return false;
            }

            value = BitConverter.ToUInt32(bytes, 0);
            return true;
        }

        public bool TryReadPointer(
            long address,
            out long value,
            out string error)
        {
            if (this.identity.PointerSize == 4)
            {
                uint pointer;
                if (!this.TryReadUInt32(address, out pointer, out error))
                {
                    value = 0L;
                    return false;
                }

                value = pointer;
                return true;
            }

            if (this.identity.PointerSize == 8)
            {
                long pointer;
                if (!this.TryReadInt64(address, out pointer, out error))
                {
                    value = 0L;
                    return false;
                }
                if (pointer < 0L)
                {
                    value = 0L;
                    error = "The target pointer is outside the supported address range.";
                    return false;
                }

                value = pointer;
                return true;
            }

            value = 0L;
            error = "The target process pointer size is unknown.";
            return false;
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.processHandle.Dispose();
        }

        private static string BuildWin32Error(string operation)
        {
            int errorCode = Marshal.GetLastWin32Error();
            return string.Format(
                "{0} failed with Win32 error {1}.",
                operation,
                errorCode);
        }

        private sealed class SafeProcessHandle : SafeHandle
        {
            public SafeProcessHandle(IntPtr handle)
                : base(IntPtr.Zero, true)
            {
                this.SetHandle(handle);
            }

            public override bool IsInvalid
            {
                get { return this.handle == IntPtr.Zero || this.handle == new IntPtr(-1); }
            }

            protected override bool ReleaseHandle()
            {
                return ReadOnlyProcessMemoryNativeMethods.CloseHandle(this.handle);
            }
        }
    }

    [SuppressUnmanagedCodeSecurity]
    internal static class ReadOnlyProcessMemoryNativeMethods
    {
        internal const uint ProcessVmRead = 0x0010;
        internal const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadProcessMemory(
            IntPtr process,
            IntPtr baseAddress,
            [Out] byte[] buffer,
            UIntPtr size,
            out UIntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWow64Process(
            IntPtr process,
            [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);
    }
}
