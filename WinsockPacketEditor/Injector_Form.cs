using System;
using System.Windows.Forms;
using System.IO;
using WPELibrary.Lib;
using EasyHook;
using System.Reflection;
using System.Diagnostics;

namespace WinsockPacketEditor
{
    public partial class Injector_Form : Form
    {
        private int ProcessID = -1;        
        private string ProcessName = string.Empty;
        private string ProcessPath = string.Empty;        

        private readonly ToolTip tt = new ToolTip();

        #region//窗体事件

        public Injector_Form()
        {            
            InitializeComponent();            

            this.rtbLog.Clear();
            this.ConfigureAccessibleLayout();
            this.InitToolTip();
            this.InitLastInjection();
            this.UpdateSelectedProcessState();
        }

        private void ConfigureAccessibleLayout()
        {
            this.ClientSize = new System.Drawing.Size(620, 300);
            this.MinimumSize = new System.Drawing.Size(636, 339);
            this.MaximizeBox = false;
            this.AccessibleRole = AccessibleRole.Window;
            this.Text = Socket_Cache.System.WPE;
            this.lProcessName.Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_20);
            this.bSelectProcess.Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_1);
            this.bInject.Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_2);
            this.tlpProcessInject.ColumnStyles[2].Width = 140F;
            this.tlpProcessInject.ColumnStyles[3].Width = 150F;

            this.tbProcessID.TabStop = false;
            this.tbProcessID.AccessibleName = this.bSelectProcess.Text;

            this.bSelectProcess.TextImageRelation = TextImageRelation.ImageBeforeText;
            this.bSelectProcess.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.bSelectProcess.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.bSelectProcess.TabIndex = 0;
            this.bSelectProcess.AccessibleName = this.bSelectProcess.Text;

            this.bInject.TabIndex = 1;
            this.bInject.AccessibleName = this.bInject.Text;
            this.rtbLog.TabIndex = 2;
            this.rtbLog.AccessibleName = new System.ComponentModel.ComponentResourceManager(
                typeof(Injector_Form)).GetString("rtbLog.AccessibleName") ?? "Injection log";
            this.rtbLog.AccessibleRole = AccessibleRole.Text;
            this.rtbLog.ScrollBars = RichTextBoxScrollBars.Both;
            this.AcceptButton = this.bInject;
        }

        private void InitToolTip()
        {
            try
            {
                tt.SetToolTip(bSelectProcess, this.bSelectProcess.Text);
                tt.SetToolTip(bInject, this.bInject.Text);
                                
                ShowLog(string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_5), Socket_Operation.AssemblyVersion));
            }
            catch (Exception ex)
            {
                ShowLog(ex.Message);
            }            
        }

        private void Injector_Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            Socket_Cache.System.SaveSystemConfig_LastInjection_ToDB();
        }

        #endregion

        #region//初始化上次注入信息

        private void InitLastInjection()
        {
            try
            {
                if (!string.IsNullOrEmpty(Socket_Cache.System.LastInjection))
                {
                    Process[] plProcess = Process.GetProcessesByName(Socket_Cache.System.LastInjection);

                    if (plProcess.Length > 0 && ProcessList_Form.IsSupportedInjectionProcess(plProcess[0].ProcessName))
                    { 
                        Program.PID = plProcess[0].Id;
                        Program.PNAME = plProcess[0].ProcessName;

                        this.ShowSelectProcess();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowLog(ex.Message);
            }
        }        

        #endregion

        #region//选择进程

        private void bSelectProcess_Click(object sender, EventArgs e)
        {
            try
            {
                ProcessList_Form plf = new ProcessList_Form();
                plf.ShowDialog();

                this.ShowSelectProcess();
            }
            catch (Exception ex)
            {
                ShowLog(ex.Message);
            }
        }

        private void ShowSelectProcess()
        {
            try
            {
                if (Program.PID != -1 && Program.PNAME != string.Empty)
                {
                    tbProcessID.Text = Program.PNAME + " [" + Program.PID + "]";
                }
                else if (Program.PID == -1 && !string.IsNullOrEmpty(Program.PNAME) && !string.IsNullOrEmpty(Program.PATH))
                {
                    tbProcessID.Text = Program.PNAME;
                }

                this.UpdateSelectedProcessState();
            }
            catch (Exception ex)
            {
                ShowLog(ex.Message);
            }
        }

        private void UpdateSelectedProcessState()
        {
            bool hasSelection =
                Program.PID > 0 && !string.IsNullOrEmpty(Program.PNAME);
            this.bInject.Enabled = hasSelection;

            if (hasSelection)
            {
                this.bInject.Focus();
            }
            else
            {
                this.tbProcessID.Clear();
                this.bSelectProcess.Focus();
            }
        }

        #endregion

        #region//注入选择的进程

        private void bInject_Click(object sender, EventArgs e)
        {
            try
            {
                string channelName = "WPE64";
                ProcessID = Program.PID;
                ProcessPath = Program.PATH;
                ProcessName = Program.PNAME;

                if (ProcessID <= 0 || string.IsNullOrEmpty(ProcessName))
                {                    
                    Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_6));
                }
                else
                {
                    string injectionLibrary_x86 = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Socket_Cache.System.WPE64_DLL);
                    string injectionLibrary_x64 = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Socket_Cache.System.WPE64_DLL);

                    using (Process targetProcess = Process.GetProcessById(ProcessID))
                    {
                        if (!ProcessList_Form.IsSupportedInjectionProcess(targetProcess.ProcessName))
                        {
                            throw new InvalidOperationException(MultiLanguage.DefaultLanguage == "en-US"
                                ? "The selected process is no longer an approved injection target. Refresh the process list."
                                : "当前进程已不再是允许注入的目标，请刷新进程列表后重试。");
                        }
                    }

                    if (!File.Exists(injectionLibrary_x86) || !File.Exists(injectionLibrary_x64))
                    {
                        throw new FileNotFoundException(
                            MultiLanguage.DefaultLanguage == "en-US"
                                ? "WPELibrary.dll is missing from the application directory."
                                : "程序目录中缺少 WPELibrary.dll。",
                            injectionLibrary_x64);
                    }

                    this.bInject.Enabled = false;

                    ShowLog(DateTime.Now.ToString("G"));
                    ShowLog(string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_7), ProcessName));

                    RemoteHooking.Inject(ProcessID, injectionLibrary_x86, injectionLibrary_x64, channelName);

                    Socket_Cache.System.LastInjection = Program.PNAME;
                    int targetPlat = Socket_Operation.IsWin64Process(ProcessID) ? 64 : 32;

                    ShowLog(string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_8), targetPlat));                    
                    ShowLog(string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_9), ProcessName, ProcessID));
                    ShowLog(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_10));
                    this.BeginInvoke(new Action(this.Close));
                }
            }
            catch (Exception ex)
            {  
                ShowLog(string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_11), ex.Message));
                ShowLog(string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_102), Socket_Cache.System.WPE64_URL));
                this.bInject.Enabled = true;
            }

            this.rtbLog.ScrollToCaret();            
        }

        #endregion                

        #region//显示日志

        private void ShowLog(string ShowInfo)
        {
            try
            {
                if (string.IsNullOrEmpty(this.rtbLog.Text.Trim()))
                {
                    this.rtbLog.AppendText(ShowInfo);
                }
                else
                {
                    this.rtbLog.AppendText("\n" + ShowInfo);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void rtbLog_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            try
            {
                Process.Start(e.LinkText);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        
    }
}
