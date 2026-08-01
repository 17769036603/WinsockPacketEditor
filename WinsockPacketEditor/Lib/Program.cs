using System;
using System.ComponentModel;
using System.Windows.Forms;
using WPELibrary;
using WPELibrary.Lib;
using WPELibrary.Lib.NativeMethods;

namespace WinsockPacketEditor
{
    static class Program
    {
        public static int PID = -1;
        public static string PNAME = string.Empty;
        public static string PATH = string.Empty;        

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
                            MultiLanguage.DefaultLanguage == "en-US"
                                ? "Administrator permission is required to start injection mode."
                                : "注入模式需要管理员权限才能启动。",
                            Socket_Cache.System.WPE,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            (MultiLanguage.DefaultLanguage == "en-US" ? "Failed to restart with administrator permission: " : "请求管理员权限失败：") + ex.Message,
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
                    (MultiLanguage.DefaultLanguage == "en-US" ? "Startup failed: " : "启动失败：") + ex.Message,
                    Socket_Cache.System.WPE,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }            
        }

        #endregion        
    }
}
