using System;
using System.Windows.Forms;
using System.Data;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using WPELibrary.Lib;
using System.Reflection;
using System.Threading.Tasks;

namespace WinsockPacketEditor
{
    public partial class ProcessList_Form : Form
    {
        private bool ShowEmulatorOnly = true;
        private readonly string selectEmulatorText;
        private readonly string showAllProcessesText;
        private string processEmptyStateText;
        private string processEmptySearchText;
        internal static readonly string[] EmulatorMainProcessNames =
        {
            "0dcloudCore",
            "Ld9BoxHeadless",
            "LdVBoxHeadless",
            "NoxVMHandle",
            "MEmuHeadless",
            "NemuHeadless",
            "MuMuVMMHeadless",
            "HD-Player",
            "AndroidEmulator",
            "AndroidEmulatorEn",
            "AndroidEmulatorEx",
            "aow_exe"
        };

        #region//窗体加载

        public ProcessList_Form()
        {           
            InitializeComponent();
            this.Text = UiText("进程列表", "Process List");
            this.bSelected.Text = UiText("确定", "OK");
            this.bRefresh.Text = UiText("刷新", "Refresh");
            this.bSelectEmulator.Text = UiText("选择模拟器", "Select emulator");
            System.ComponentModel.ComponentResourceManager resources =
                new System.ComponentModel.ComponentResourceManager(typeof(ProcessList_Form));
            this.selectEmulatorText = UiText("选择模拟器", "Select emulator");
            this.showAllProcessesText = UiText("显示全部", "Show all");
            this.ConfigureInteraction(resources);
            this.InitDGV();
        }

        private static string UiText(string chinese, string english)
        {
            return MultiLanguage.GetDefaultLanguage(new[] { chinese, english });
        }

        private void ConfigureInteraction(System.ComponentModel.ComponentResourceManager resources)
        {
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.MaximizeBox = true;
            this.AccessibleRole = AccessibleRole.Window;
            this.MinimumSize = new Size(680, 420);
            this.AcceptButton = this.bSelected;
            this.bSelected.Enabled = false;
            this.bSelected.AccessibleRole = AccessibleRole.PushButton;
            this.bRefresh.AccessibleRole = AccessibleRole.PushButton;
            this.tlpProcessInfoButton.ColumnStyles[1].Width = 140F;
            this.tlpProcessInfoButton.ColumnStyles[3].Width = 140F;
            this.tlpProcessInfoButton.ColumnStyles[5].Width = 140F;
            this.tlpProcessInfoButton.ColumnStyles[7].Width = 140F;
            // 注入版只接受实时目标进程；保留旧控件和事件以兼容旧资源，但不再暴露任意 EXE 入口。
            this.bCreate.Visible = false;
            this.bCreate.Enabled = false;
            this.bSelectEmulator.Visible = false;
            this.bSelectEmulator.Enabled = false;
            this.tlpProcessInfoButton.ColumnStyles[0].Width = 0F;
            this.tlpProcessInfoButton.ColumnStyles[1].Width = 0F;
            this.tlpProcessInfoButton.ColumnStyles[2].Width = 0F;
            this.tlpProcessInfoButton.ColumnStyles[3].Width = 0F;
            this.tlpProcessInfoButton.ColumnStyles[4].Width = 8F;
            this.tlpProcessInfoButton.ColumnStyles[6].Width = 8F;
            string searchText = UiText("搜索：", "Search:");
            Label searchLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = searchText,
                TextAlign = ContentAlignment.MiddleRight
            };
            this.tlpProcessInfoSearch.Controls.Add(searchLabel, 0, 0);
            this.txtProcessSearch.AccessibleName = searchText.TrimEnd('：', ':');
            this.txtProcessSearch.AccessibleRole = AccessibleRole.Text;
            this.dgvProcessList.AccessibleName = UiText("进程列表", "Process list");
            this.dgvProcessList.AccessibleRole = AccessibleRole.Table;
            this.dgvProcessList.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
            this.dgvProcessList.DefaultCellStyle.SelectionForeColor = Color.White;
            this.dgvProcessList.ColumnHeadersDefaultCellStyle.SelectionBackColor =
                Color.FromArgb(0, 120, 215);
            this.dgvProcessList.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
            this.pbLoading.AccessibleName = UiText("正在加载进程", "Loading processes");
            this.processEmptyStateText = UiText(
                "未找到可注入的模拟器引擎，请启动模拟器后刷新",
                "No injectable emulator engine found. Start an emulator and refresh.");
            this.processEmptySearchText = UiText(
                "没有匹配的模拟器进程",
                "No emulator process matches the search.");
            this.AddProcessInfoColumns(resources);
            this.dgvProcessList.Columns["cProcessName"].HeaderText = UiText("进程名称", "Process");
            this.dgvProcessList.Columns["cProcessID"].HeaderText = UiText("进程编号", "ID");
            this.dgvProcessList.Columns["cPath"].HeaderText = UiText("路径", "Path");
            this.dgvProcessList.SelectionChanged += this.dgvProcessList_SelectionChanged;
            this.dgvProcessList.CellDoubleClick += this.dgvProcessList_CellDoubleClick;
            this.dgvProcessList.Paint += this.dgvProcessList_Paint;
            this.UpdateFilterButtonText();
        }

        private void AddProcessInfoColumns(System.ComponentModel.ComponentResourceManager resources)
        {
            if (!this.dgvProcessList.Columns.Contains("cArchitecture"))
            {
                this.dgvProcessList.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "cArchitecture",
                    DataPropertyName = "PArch",
                    HeaderText = UiText("架构", "Architecture"),
                    ReadOnly = true,
                    Width = 70,
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            }

            if (!this.dgvProcessList.Columns.Contains("cCompatibility"))
            {
                this.dgvProcessList.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "cCompatibility",
                    DataPropertyName = "PCompatibility",
                    HeaderText = UiText("状态", "Status"),
                    ReadOnly = true,
                    Width = 105,
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            }
        }

        private void InitDGV()
        {
            dgvProcessList.AutoGenerateColumns = false;
            dgvProcessList.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dgvProcessList, true, null);
        }

        private async void ProcessList_Form_Load(object sender, EventArgs e)
        {
            try
            {
                await this.ShowProcessList();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(InitDGV), ex.Message);
            }            
        }

        #endregion      

        #region//显示所有进程（异步）

        private async Task ShowProcessList()
        {
            this.SetLoadingState(true);
            try
            {
                this.BindProcessList(await Socket_Operation.GetProcess());
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(ShowProcessList), ex.Message);
            }
            finally
            {
                this.SetLoadingState(false);
            }
        }

        private void SetLoadingState(bool loading)
        {
            this.bCreate.Enabled = !loading;
            this.bRefresh.Enabled = !loading;
            this.bSelectEmulator.Enabled = !loading;
            this.txtProcessSearch.Enabled = !loading;
            this.pbLoading.Visible = loading;
            this.dgvProcessList.Visible = !loading;
            this.UpdateSelectedButtonState();
        }

        #endregion

        #region//选择模拟器

        private void bSelectEmulator_Click(object sender, EventArgs e)
        {
            try
            {
                this.ShowEmulatorOnly = !this.ShowEmulatorOnly;
                this.txtProcessSearch.Text = string.Empty;
                this.UpdateFilterButtonText();
                this.BindProcessList(Socket_Operation.ProcessTable);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(bSelectEmulator_Click), ex.Message);
            }
        }

        private void UpdateFilterButtonText()
        {
            this.bSelectEmulator.Text =
                this.ShowEmulatorOnly ? this.showAllProcessesText : this.selectEmulatorText;
            this.bSelectEmulator.AccessibleName = this.bSelectEmulator.Text;
        }

        private void BindProcessList(DataTable processTable)
        {
            try
            {
                DataTable displayTable = processTable;

                if (this.ShowEmulatorOnly)
                {
                    displayTable = processTable.Clone();

                    foreach (DataRow processRow in processTable.Rows)
                    {
                        string processName = Convert.ToString(processRow["PName"]);

                        if (IsEmulatorMainProcess(processName))
                        {
                            displayTable.ImportRow(processRow);
                        }
                    }

                    // Some protected processes can make the general process
                    // snapshot incomplete. Keep the injection entry usable by
                    // resolving the known emulator engines directly as a
                    // fallback instead of showing an empty chooser.
                    if (displayTable.Rows.Count == 0)
                    {
                        this.AppendDirectEmulatorRows(displayTable);
                    }
                }

                this.BindProcessSearch(displayTable);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(BindProcessList), ex.Message);
            }
        }

        private static bool IsEmulatorMainProcess(string processName)
        {
            foreach (string emulatorProcessName in EmulatorMainProcessNames)
            {
                if (string.Equals(processName, emulatorProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void AppendDirectEmulatorRows(DataTable displayTable)
        {
            foreach (string processName in EmulatorMainProcessNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(
                        nameof(AppendDirectEmulatorRows),
                        string.Format("读取模拟器进程 {0} 失败：{1}", processName, ex.Message));
                    continue;
                }

                foreach (Process process in processes)
                {
                    try
                    {
                        DataRow row = displayTable.NewRow();
                        using (Icon fallback = new Icon(SystemIcons.Application, 256, 256))
                        {
                            row["ICO"] = fallback.ToBitmap();
                        }
                        row["PName"] = process.ProcessName;
                        row["PID"] = process.Id;
                        row["PPath"] = Socket_Operation.GetProcessPath(process);
                        row["PArch"] = Socket_Operation.IsWin64Process(process.Id) ? "x64" : "x86";
                        row["PCompatibility"] = File.Exists(
                            Path.Combine(
                                Path.GetDirectoryName(typeof(Socket_Operation).Assembly.Location),
                                Socket_Cache.System.WPE64_DLL))
                            ? (MultiLanguage.DefaultLanguage == "en-US" ? "Ready" : "可注入")
                            : (MultiLanguage.DefaultLanguage == "en-US" ? "Missing DLL" : "缺少 DLL");
                        displayTable.Rows.Add(row);
                    }
                    catch (Exception ex)
                    {
                        Socket_Operation.DoLog(
                            nameof(AppendDirectEmulatorRows),
                            string.Format("读取模拟器进程 {0}[{1}] 失败：{2}", processName, process.Id, ex.Message));
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }

        internal static bool IsSupportedInjectionProcess(string processName)
        {
            return IsEmulatorMainProcess(processName);
        }

        internal static string[] GetSupportedInjectionProcessNames()
        {
            return (string[])EmulatorMainProcessNames.Clone();
        }

        private void BindProcessSearch(DataTable processTable)
        {
            string searchText = this.txtProcessSearch.Text.Trim();

            if (string.IsNullOrEmpty(searchText))
            {
                this.dgvProcessList.DataSource = processTable;
            }
            else
            {
                DataTable processSearch = processTable.Clone();
                foreach (DataRow row in processTable.Rows)
                {
                    string processName = Convert.ToString(row["PName"]);
                    string processPath = Convert.ToString(row["PPath"]);
                    if (processName.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                        processPath.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    {
                        processSearch.ImportRow(row);
                    }
                }

                this.dgvProcessList.DataSource = processSearch;
            }

            this.dgvProcessList.Invalidate();
        }

        private void dgvProcessList_Paint(object sender, PaintEventArgs e)
        {
            if (this.dgvProcessList.Rows.Count > 0 || this.dgvProcessList.Visible == false)
            {
                return;
            }

            string emptyText = string.IsNullOrWhiteSpace(this.txtProcessSearch.Text)
                ? this.processEmptyStateText
                : this.processEmptySearchText;
            Rectangle contentBounds = this.dgvProcessList.ClientRectangle;
            contentBounds.Y += this.dgvProcessList.ColumnHeadersHeight;
            contentBounds.Height -= this.dgvProcessList.ColumnHeadersHeight;
            TextRenderer.DrawText(
                e.Graphics,
                emptyText,
                this.dgvProcessList.Font,
                contentBounds,
                SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak |
                TextFormatFlags.HorizontalCenter);
        }

        #endregion

        #region//选中某个进程

        private void bSelected_Click(object sender, EventArgs e)
        {
            try
            {
                if (dgvProcessList.SelectedRows.Count == 1)
                {
                    int selectedPid = (int)dgvProcessList.SelectedRows[0].Cells["cProcessID"].Value;
                    string selectedName = dgvProcessList.SelectedRows[0].Cells["cProcessName"].Value.ToString();
                    using (Process process = Process.GetProcessById(selectedPid))
                    {
                        if (!IsSupportedInjectionProcess(process.ProcessName) ||
                            !string.Equals(process.ProcessName, selectedName, StringComparison.OrdinalIgnoreCase))
                        {
                            Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_13));
                            return;
                        }
                    }

                    Program.PID = selectedPid;
                    Program.PNAME = selectedName;
                    Program.PATH = string.Empty;

                    this.Close();
                }
                else
                {                    
                    Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_13));
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(bSelected_Click), ex.Message);
                Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_13));
            }
        }

        #endregion

        #region//刷新进程列表

        private async void bRefresh_Click(object sender, EventArgs e)
        {
            try
            {
                this.txtProcessSearch.Text = string.Empty;
                await this.ShowProcessList();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(bRefresh_Click), ex.Message);
            }
        }

        #endregion

        #region//选择程序

        private void bCreate_Click(object sender, EventArgs e)
        {
            // 注入版不允许从任意 EXE 路径创建目标，保留事件仅用于兼容旧资源。
            try
            {
                Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_13));
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(bCreate_Click), ex.Message);
            }
        }

        #endregion

        #region//列表样式

        private void dgvProcessList_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            dgvProcessList.ClearSelection();
            this.UpdateSelectedButtonState();
        }

        private void dgvProcessList_SelectionChanged(object sender, EventArgs e)
        {
            this.UpdateSelectedButtonState();
        }

        private void UpdateSelectedButtonState()
        {
            this.bSelected.Enabled =
                this.dgvProcessList.Visible &&
                this.dgvProcessList.SelectedRows.Count == 1;
        }

        private void dgvProcessList_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                this.bSelected_Click(sender, EventArgs.Empty);
            }
        }

        private void dgvProcessList_CellMouseMove(object sender, DataGridViewCellMouseEventArgs e)
        {
            try
            {
                if (e.RowIndex >= 0)
                {
                    if (e.RowIndex != -1 && e.ColumnIndex != -1)
                    {
                        this.dgvProcessList.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromName("Control");
                    }
                }
            }
            catch
            {
                //
            }            
        }

        private void dgvProcessList_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex >= 0)
                {
                    if (e.RowIndex != -1 && e.ColumnIndex != -1)
                    {
                        this.dgvProcessList.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromName("Window");
                    }
                }
            }
            catch
            {
                //
            }
        }

        #endregion

        #region//筛选进程

        private void txtProcessSearch_TextChanged(object sender, EventArgs e)
        {
            try
            {
                this.BindProcessList(Socket_Operation.ProcessTable);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(txtProcessSearch_TextChanged), ex.Message);
            }                    
        }

        #endregion
    }
}
