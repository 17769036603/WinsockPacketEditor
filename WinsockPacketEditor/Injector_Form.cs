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
        private Button bRemoteSettings;
        private Button bMobileAccountSettings;
        private bool autoInjectLastProcess;

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
            this.ClientSize = new System.Drawing.Size(820, 300);
            this.MinimumSize = new System.Drawing.Size(836, 339);
            this.MaximizeBox = false;
            this.AccessibleRole = AccessibleRole.Window;
            this.Text = Socket_Cache.System.WPE;
            this.lProcessName.Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_20);
            this.bSelectProcess.Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_1);
            this.bInject.Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_2);
            this.tlpProcessInject.ColumnStyles[2].Width = 140F;
            this.tlpProcessInject.ColumnStyles[3].Width = 150F;
            this.tlpProcessInject.ColumnCount = 6;
            this.tlpProcessInject.ColumnStyles.Add(
                new System.Windows.Forms.ColumnStyle(
                    System.Windows.Forms.SizeType.Absolute,
                    100F));
            this.tlpProcessInject.ColumnStyles.Add(
                new System.Windows.Forms.ColumnStyle(
                    System.Windows.Forms.SizeType.Absolute,
                    100F));
            this.bRemoteSettings = new Button
            {
                Name = "bRemoteSettings",
                Text = "远程设置",
                AutoSize = true,
                UseVisualStyleBackColor = true,
                AccessibleName = "远程设置"
            };
            this.bRemoteSettings.Click += this.bRemoteSettings_Click;
            this.tlpProcessInject.Controls.Add(this.bRemoteSettings, 4, 0);

            this.bMobileAccountSettings = new Button
            {
                Name = "bMobileAccountSettings",
                Text = "移动端账号",
                AutoSize = true,
                UseVisualStyleBackColor = true,
                AccessibleName = "移动端账号"
            };
            this.bMobileAccountSettings.Click += this.bMobileAccountSettings_Click;
            this.tlpProcessInject.Controls.Add(this.bMobileAccountSettings, 5, 0);

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

        private void bRemoteSettings_Click(object sender, EventArgs e)
        {
            using (SystemMode_Form form = new SystemMode_Form())
            {
                form.ShowDialog(this);
            }
        }

        private void bMobileAccountSettings_Click(object sender, EventArgs e)
        {
            Socket_Operation.ShowProxyAccountListForm();
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!this.autoInjectLastProcess || !this.bInject.Enabled)
            {
                return;
            }

            // Keep the normal injector UI unchanged. The explicit command-line
            // switch is used by unattended desktop restarts after a ClickOnce
            // update, where no one is available to press the button.
            this.BeginInvoke(new Action(() =>
            {
                if (!this.IsDisposed && this.bInject.Enabled)
                {
                    this.bInject.PerformClick();
                }
            }));
        }

        private static bool HasAutoInjectSwitch()
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, "--auto-inject", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
                        this.autoInjectLastProcess = HasAutoInjectSwitch();
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
                if (this.TrySelectSingleEmulator())
                {
                    return;
                }

                ProcessList_Form plf = new ProcessList_Form();
                plf.ShowDialog();

                this.ShowSelectProcess();
            }
            catch (Exception ex)
            {
                ShowLog(ex.Message);
            }
        }

        private bool TrySelectSingleEmulator()
        {
            Process selected = null;
            int matchCount = 0;

            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        if (!ProcessList_Form.IsSupportedInjectionProcess(process.ProcessName))
                        {
                            process.Dispose();
                            continue;
                        }

                        selected?.Dispose();
                        selected = process;
                        matchCount++;
                    }
                    catch (Exception ex)
                    {
                        Socket_Operation.DoLog(
                            nameof(TrySelectSingleEmulator),
                            string.Format("读取进程 {0} 失败：{1}", process.Id, ex.Message));
                        process.Dispose();
                    }
                }

                if (matchCount != 1 || selected == null)
                {
                    return false;
                }

                Program.PID = selected.Id;
                Program.PNAME = selected.ProcessName;
                Program.PATH = string.Empty;
                this.ShowSelectProcess();
                return true;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(TrySelectSingleEmulator), ex.Message);
                return false;
            }
            finally
            {
                selected?.Dispose();
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
                            throw new InvalidOperationException(
                                Socket_Operation.GetUiText("Injector_TargetNotApproved"));
                        }
                    }

                    if (!File.Exists(injectionLibrary_x86) || !File.Exists(injectionLibrary_x64))
                    {
                        throw new FileNotFoundException(
                            Socket_Operation.GetUiText("Injector_LibraryMissing"),
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
