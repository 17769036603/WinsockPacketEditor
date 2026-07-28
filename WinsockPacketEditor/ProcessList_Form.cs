using System;
using System.Windows.Forms;
using System.Data;
using System.IO;
using System.Drawing;
using WPELibrary.Lib;
using System.Reflection;
using System.Threading.Tasks;

namespace WinsockPacketEditor
{
    public partial class ProcessList_Form : Form
    {
        private bool ShowEmulatorOnly = false;
        private readonly string selectEmulatorText;
        private readonly string showAllProcessesText;
        private static readonly string[] EmulatorMainProcessNames =
        {
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
            System.ComponentModel.ComponentResourceManager resources =
                new System.ComponentModel.ComponentResourceManager(typeof(ProcessList_Form));
            this.selectEmulatorText =
                resources.GetString("bSelectEmulator.Text") ?? this.bSelectEmulator.Text;
            this.showAllProcessesText =
                resources.GetString("bSelectEmulator.ShowAllText") ?? "显示全部";
            this.ConfigureInteraction(resources);
            this.InitDGV();
        }

        private void ConfigureInteraction(System.ComponentModel.ComponentResourceManager resources)
        {
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.MaximizeBox = true;
            this.MinimumSize = new Size(680, 420);
            this.AcceptButton = this.bSelected;
            this.bSelected.Enabled = false;
            this.tlpProcessInfoButton.ColumnStyles[1].Width = 140F;
            this.tlpProcessInfoButton.ColumnStyles[3].Width = 140F;
            this.tlpProcessInfoButton.ColumnStyles[5].Width = 140F;
            this.tlpProcessInfoButton.ColumnStyles[7].Width = 140F;
            string searchText = resources.GetString("txtProcessSearch.Label");
            Label searchLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = searchText,
                TextAlign = ContentAlignment.MiddleRight
            };
            this.tlpProcessInfoSearch.Controls.Add(searchLabel, 0, 0);
            this.txtProcessSearch.AccessibleName = searchText.TrimEnd('：', ':');
            this.dgvProcessList.AccessibleName =
                resources.GetString("dgvProcessList.AccessibleName");
            this.pbLoading.AccessibleName =
                resources.GetString("pbLoading.AccessibleName");
            this.dgvProcessList.SelectionChanged += this.dgvProcessList_SelectionChanged;
            this.dgvProcessList.CellDoubleClick += this.dgvProcessList_CellDoubleClick;
            this.UpdateFilterButtonText();
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
                    if (processName.StartsWith(searchText, StringComparison.CurrentCultureIgnoreCase))
                    {
                        processSearch.ImportRow(row);
                    }
                }

                this.dgvProcessList.DataSource = processSearch;
            }
        }

        #endregion

        #region//选中某个进程

        private void bSelected_Click(object sender, EventArgs e)
        {
            try
            {
                if (dgvProcessList.SelectedRows.Count == 1)
                {
                    Program.PID = (int)dgvProcessList.SelectedRows[0].Cells["cProcessID"].Value;
                    Program.PNAME = dgvProcessList.SelectedRows[0].Cells["cProcessName"].Value.ToString();

                    this.Close();
                }
                else
                {                    
                    Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_13));
                }
            }
            catch
            {
                //
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
            catch
            {
                //
            }
        }

        #endregion

        #region//选择程序

        private void bCreate_Click(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog ofdCreate = new OpenFileDialog())
                {
                    ofdCreate.Title = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_14);
                    ofdCreate.Multiselect = false;
                    ofdCreate.InitialDirectory = string.Empty;
                    ofdCreate.Filter = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_15);

                    if (ofdCreate.ShowDialog(this) != DialogResult.OK ||
                        string.IsNullOrEmpty(ofdCreate.FileName))
                    {
                        return;
                    }

                    Program.PID = -1;
                    Program.PATH = ofdCreate.FileName;
                    Program.PNAME = Path.GetFileName(Program.PATH);
                    base.Close();
                }
            }
            catch
            {
                //
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
            catch
            {
                //
            }                    
        }

        #endregion
    }
}
