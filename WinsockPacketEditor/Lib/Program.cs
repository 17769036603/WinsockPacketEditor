using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
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
            try
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    User32.SetProcessDPIAware();
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);             
               
                System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal principal = new System.Security.Principal.WindowsPrincipal(identity);

                Socket_Cache.DataBase.InitDB();
                Socket_Cache.System.LoadSystemConfig_FromDB();
                MultiLanguage.SetDefaultLanguage(Socket_Cache.System.DefaultLanguage);

                if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                {
                    // 当前版本固定进入注入模式；代理模式和远程管理入口暂不展示。
                    Application.Run(new Injector_Form());
                }
                else
                {                    
                    System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                    startInfo.UseShellExecute = true;
                    startInfo.WorkingDirectory = Environment.CurrentDirectory;
                    startInfo.FileName = Application.ExecutablePath;
                    
                    startInfo.Verb = "runas";

                    try
                    {
                        System.Diagnostics.Process.Start(startInfo);
                    }
                    catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                    {
                        MessageBox.Show(
                            Socket_Operation.GetUiText("Startup_AdminRequired"),
                            Socket_Cache.System.WPE,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            string.Format(
                                Socket_Operation.GetUiText("Startup_AdminRestartFailed"),
                                ex.Message),
                            Socket_Cache.System.WPE,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                   
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(Socket_Operation.GetUiText("Startup_Failed"), ex.Message),
                    Socket_Cache.System.WPE,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }            
        }

        #endregion        
    }
}
