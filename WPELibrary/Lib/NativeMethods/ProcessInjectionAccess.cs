using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WPELibrary.Lib.NativeMethods
{
    public static class ProcessInjectionAccess
    {
        private const uint TokenQuery = 0x0008;
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint SePrivilegeEnabled = 0x00000002;
        private const int ErrorNotAllAssigned = 1300;
        private const string SeDebugPrivilege = "SeDebugPrivilege";

        private const uint ProcessCreateThread = 0x0002;
        private const uint ProcessVmOperation = 0x0008;
        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessVmWrite = 0x0020;
        private const uint ProcessQueryInformation = 0x0400;

        public static void EnsureAccess(int processId)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processId));
            }

            int privilegeError;
            bool debugPrivilegeEnabled = TryEnableDebugPrivilege(out privilegeError);
            IntPtr processHandle = OpenProcess(
                ProcessCreateThread |
                ProcessVmOperation |
                ProcessVmRead |
                ProcessVmWrite |
                ProcessQueryInformation,
                false,
                processId);

            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
                return;
            }

            int processError = Marshal.GetLastWin32Error();
            string message = string.Format(
                "无法打开目标进程进行注入（Windows错误 {0}: {1}）。",
                processError,
                new Win32Exception(processError).Message);

            if (!debugPrivilegeEnabled)
            {
                message += string.Format(
                    "调试权限未启用（Windows错误 {0}: {1}）。",
                    privilegeError,
                    new Win32Exception(privilegeError).Message);
            }

            message += "请重新启动本工具并在UAC提示中选择“是”；如果仍失败，目标程序可能拒绝用户态注入。";
            throw new InvalidOperationException(message);
        }

        private static bool TryEnableDebugPrivilege(out int errorCode)
        {
            errorCode = 0;
            IntPtr tokenHandle = IntPtr.Zero;

            try
            {
                if (!OpenProcessToken(
                    Kernel32.GetCurrentProcess(),
                    TokenAdjustPrivileges | TokenQuery,
                    out tokenHandle))
                {
                    errorCode = Marshal.GetLastWin32Error();
                    return false;
                }

                Luid luid;
                if (!LookupPrivilegeValue(null, SeDebugPrivilege, out luid))
                {
                    errorCode = Marshal.GetLastWin32Error();
                    return false;
                }

                TokenPrivileges privileges = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SePrivilegeEnabled
                };

                if (!AdjustTokenPrivileges(
                    tokenHandle,
                    false,
                    ref privileges,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero))
                {
                    errorCode = Marshal.GetLastWin32Error();
                    return false;
                }

                errorCode = Marshal.GetLastWin32Error();
                return errorCode != ErrorNotAllAssigned;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    CloseHandle(tokenHandle);
                }
            }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(
            string systemName,
            string privilegeName,
            out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr tokenHandle,
            bool disableAllPrivileges,
            ref TokenPrivileges newState,
            int bufferLength,
            IntPtr previousState,
            IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint desiredAccess,
            bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public Luid Luid;
            public uint Attributes;
        }
    }
}
