using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;
using WPELibrary;
using WPELibrary.Lib;
using WPELibrary.Lib.NativeMethods;

namespace WinsockPacketEditor
{
    static class Program
    {
        private static readonly string[] BundledRuntimeAssemblies =
        {
            "Microsoft.ML.OnnxRuntime",
            "System.Memory",
            "System.Buffers",
            "System.Runtime.CompilerServices.Unsafe",
            "System.Threading.Tasks.Extensions"
        };

        public static int PID = -1;
        public static string PNAME = string.Empty;
        public static string PATH = string.Empty;        

        static Program()
        {
            // ONNX Runtime may request an older assembly identity (for example
            // System.Memory 4.0.1.2). Binding redirects cover the normal EXE
            // host, while this resolver also supports ClickOnce/isolated hosts
            // whose configuration is not inherited from the main application.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveBundledRuntimeAssembly;
        }

        private static Assembly ResolveBundledRuntimeAssembly(
            object sender,
            ResolveEventArgs args)
        {
            AssemblyName requested;
            try
            {
                requested = new AssemblyName(args.Name);
            }
            catch (FileLoadException)
            {
                return null;
            }

            bool isBundledRuntimeAssembly = false;
            foreach (string assemblyName in BundledRuntimeAssemblies)
            {
                if (string.Equals(requested.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    isBundledRuntimeAssembly = true;
                    break;
                }
            }
            if (!isBundledRuntimeAssembly)
            {
                return null;
            }

            foreach (Assembly loadedAssembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                AssemblyName loadedName = loadedAssembly.GetName();
                if (string.Equals(loadedName.Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return loadedAssembly;
                }
            }

            string path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                requested.Name + ".dll");
            if (!File.Exists(path))
            {
                return null;
            }

            return Assembly.LoadFrom(path);
        }

        #region//主函数

        [STAThread]

        static void Main()
        {
            WriteStartupLog("process_started", null, string.Empty, null, null);
            try
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    User32.SetProcessDPIAware();
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool isAdministrator = IsCurrentProcessAdministrator();
                WriteStartupLog(
                    "privilege_checked",
                    isAdministrator,
                    string.Empty,
                    null,
                    null);

                if (isAdministrator)
                {
                    // Only the elevated process opens the user database. The
                    // ClickOnce launcher must stay read-only while handing off
                    // to the administrator process.
                    WriteStartupLog("database_initializing", true, string.Empty, null, null);
                    Socket_Cache.DataBase.InitDB();
                    Socket_Cache.System.LoadSystemConfig_FromDB();
                    MultiLanguage.SetDefaultLanguage(Socket_Cache.System.DefaultLanguage);
                    WriteStartupLog("database_initialized", true, string.Empty, null, null);

                    // 当前版本固定进入注入模式；代理模式和远程管理入口暂不展示。
                    WriteStartupLog("main_form_starting", true, string.Empty, null, null);
                    Application.Run(new Injector_Form());
                    WriteStartupLog("main_form_exited", true, string.Empty, null, null);
                }
                else
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.UseShellExecute = true;
                    startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    startInfo.FileName = Application.ExecutablePath;
                    // Preserve explicit unattended switches when the normal
                    // non-elevated launcher hands off to the administrator
                    // process. Without this, --auto-inject was silently
                    // dropped at the UAC boundary and the elevated injector
                    // opened with no action pending.
                    string[] commandLineArguments = Environment.GetCommandLineArgs();
                    if (commandLineArguments.Length > 1)
                    {
                        List<string> forwardedArguments = new List<string>();
                        for (int index = 1; index < commandLineArguments.Length; index++)
                        {
                            string argument = commandLineArguments[index];
                            if (string.IsNullOrWhiteSpace(argument))
                            {
                                continue;
                            }

                            forwardedArguments.Add(QuoteProcessArgument(argument));
                        }

                        startInfo.Arguments = string.Join(" ", forwardedArguments.ToArray());
                    }
                    
                    startInfo.Verb = "runas";

                    try
                    {
                        WriteStartupLog(
                            "elevation_requested",
                            false,
                            string.Empty,
                            null,
                            null);
                        using (Process elevatedProcess = Process.Start(startInfo))
                        {
                            if (elevatedProcess == null)
                            {
                                throw new InvalidOperationException(
                                    "Windows 未返回提权进程。请查看启动日志：" +
                                    StartupDiagnosticLogStore.LogFilePath);
                            }

                            int childProcessId = elevatedProcess.Id;
                            WriteStartupLog(
                                "elevation_process_started",
                                false,
                                string.Empty,
                                childProcessId,
                                null);

                            // A process that exits immediately is not a valid
                            // elevation hand-off. Keep the launcher alive just
                            // long enough to surface this previously silent
                            // failure while a normal administrator UI continues.
                            if (elevatedProcess.WaitForExit(1500))
                            {
                                int childExitCode = elevatedProcess.ExitCode;
                                WriteStartupLog(
                                    "elevation_process_exited_early",
                                    false,
                                    "提权进程启动后立即退出。",
                                    childProcessId,
                                    childExitCode);
                                throw new InvalidOperationException(
                                    string.Format(
                                        "提权进程启动后立即退出（退出码 {0}）。启动日志：{1}",
                                        childExitCode,
                                        StartupDiagnosticLogStore.LogFilePath));
                            }

                            WriteStartupLog(
                                "elevation_handoff_completed",
                                false,
                                string.Empty,
                                childProcessId,
                                null);
                        }
                    }
                    catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                    {
                        WriteStartupLog(
                            "elevation_cancelled",
                            false,
                            ex.Message,
                            null,
                            ex.NativeErrorCode);
                        MessageBox.Show(
                            Socket_Operation.GetUiText("Startup_AdminRequired"),
                            GetStartupWindowTitle(),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                    catch (Exception ex)
                    {
                        WriteStartupLog(
                            "elevation_failed",
                            false,
                            ex.ToString(),
                            null,
                            null);
                        MessageBox.Show(
                            string.Format(
                                Socket_Operation.GetUiText("Startup_AdminRestartFailed"),
                                ex.Message),
                            GetStartupWindowTitle(),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                   
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                WriteStartupLog(
                    "startup_failed",
                    TryGetAdministratorState(),
                    ex.ToString(),
                    null,
                    null);
                MessageBox.Show(
                    string.Format(Socket_Operation.GetUiText("Startup_Failed"), ex.Message),
                    GetStartupWindowTitle(),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static bool IsCurrentProcessAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static bool? TryGetAdministratorState()
        {
            try
            {
                return IsCurrentProcessAdministrator();
            }
            catch
            {
                return null;
            }
        }

        private static void WriteStartupLog(
            string eventName,
            bool? isAdministrator,
            string message,
            int? childProcessId,
            int? exitCode)
        {
            string ignored;
            StartupDiagnosticLogStore.TryAppend(
                eventName,
                isAdministrator,
                message,
                childProcessId,
                exitCode,
                out ignored);
        }

        private static string GetStartupWindowTitle()
        {
            string title = Socket_Cache.System.WPE;
            return string.IsNullOrWhiteSpace(title) ? "小黑封包助手" : title;
        }

        private static string QuoteProcessArgument(string argument)
        {
            if (argument.IndexOfAny(new[] { ' ', '\t', '\"' }) < 0)
            {
                return argument;
            }

            return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        #endregion        
    }
}
