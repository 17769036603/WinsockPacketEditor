using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    public partial class Socket_RobotForm : Form
    {
        private Socket_RobotInfo sriSelect;
        private bool bIsModifierKeys = true;
        private DataTable dtRobotInstruction = new DataTable();
        private readonly Socket_Robot sr = new Socket_Robot();        
        private readonly ResourceManager sendPresetPickerResources =
            new ResourceManager(
                "WPELibrary.Socket_SendPresetPickerForm",
                typeof(Socket_RobotForm).Assembly);
        private Guid selectedSendPresetId = Guid.Empty;
        private Label lSelectedSendPreset;
        private Label lSelectedSendFolder;
        private Button bSelectSendPreset;

        #region//窗体加载

        public Socket_RobotForm(Socket_RobotInfo sri)
        {
            try
            {
                MultiLanguage.SetDefaultLanguage(MultiLanguage.DefaultLanguage);
                InitializeComponent();
                this.InitSendPresetPickerLayout();

                if (sri != null)
                { 
                    this.sriSelect = sri;

                    this.InitFrom();
                    this.InitDGV();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }            
        }

        #endregion

        #region//初始化

        private void InitFrom()
        {
            try
            {
                this.Text = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_93), sriSelect.RName);

                this.txtRobotName.Text = sriSelect.RName;
                this.dtRobotInstruction = sriSelect.RInstruction.Copy();

                this.cbbKeyBoard_KeyType.SelectedIndex = 0;
                this.cbbMouse.SelectedIndex = 0;
                this.cbbMouseWheel_Direction.SelectedIndex = 0;
                
                this.InitSendPresetPicker();
                this.InitRobot();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void InitRobot()
        {
            try
            {
                this.sr.Worker.ProgressChanged -= this.Worker_ProgressChanged;
                this.sr.Worker.ProgressChanged += this.Worker_ProgressChanged;

                this.sr.Worker.RunWorkerCompleted -= this.Worker_RunWorkerCompleted;
                this.sr.Worker.RunWorkerCompleted += this.Worker_RunWorkerCompleted;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }        

        private void InitDGV()
        {
            try
            {
                dgvRobotInstruction.AutoGenerateColumns = false;
                dgvRobotInstruction.BackgroundColor = Color.FromArgb(248, 248, 248);
                dgvRobotInstruction.Paint += this.dgvRobotInstruction_Paint;
                dgvRobotInstruction.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dgvRobotInstruction, true, null);
                dgvRobotInstruction.DataSource = this.dtRobotInstruction;
                this.txtExecute.BackColor = Color.FromArgb(248, 248, 248);
                this.txtExecute.ForeColor = Color.FromArgb(55, 65, 81);
                if (string.IsNullOrEmpty(this.txtExecute.Text))
                {
                    this.txtExecute.Text = UiText("Robot_ExecuteEmpty");
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private string SendPresetPickerText(string key)
        {
            return this.sendPresetPickerResources.GetString(
                key,
                CultureInfo.CurrentUICulture) ?? key;
        }

        private static string UiText(string key)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? key;
        }

        private void dgvRobotInstruction_Paint(object sender, PaintEventArgs e)
        {
            if (this.dgvRobotInstruction.Rows.Count > 0)
            {
                return;
            }

            TextRenderer.DrawText(
                e.Graphics,
                UiText("Robot_InstructionEmpty"),
                this.dgvRobotInstruction.Font,
                this.dgvRobotInstruction.ClientRectangle,
                SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
        }

        private void InitSendPresetPickerLayout()
        {
            this.cbbSend_SendLIst.Visible = false;
            this.tlpSend_SendLIst.Controls.Clear();
            this.tlpSend_SendLIst.ColumnCount = 4;
            this.tlpSend_SendLIst.RowCount = 2;
            this.tlpSend_SendLIst.ColumnStyles.Clear();
            this.tlpSend_SendLIst.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tlpSend_SendLIst.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8F));
            this.tlpSend_SendLIst.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            this.tlpSend_SendLIst.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            this.tlpSend_SendLIst.RowStyles.Clear();
            this.tlpSend_SendLIst.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            this.tlpSend_SendLIst.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            this.lSelectedSendPreset = new Label
            {
                Name = "lSelectedSendPreset",
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 1, 3, 1),
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };
            this.lSelectedSendFolder = new Label
            {
                Name = "lSelectedSendFolder",
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 1, 3, 1),
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };
            this.bSelectSendPreset = new Button
            {
                Name = "bSelectSendPreset",
                Dock = DockStyle.Fill,
                Margin = new Padding(1),
                Text = this.SendPresetPickerText("Robot_SelectList"),
                UseVisualStyleBackColor = true
            };
            this.bSelectSendPreset.Click += this.bSelectSendPreset_Click;

            this.tlpSend_SendLIst.Controls.Add(this.lSelectedSendPreset, 0, 0);
            this.tlpSend_SendLIst.Controls.Add(this.lSelectedSendFolder, 0, 1);
            this.tlpSend_SendLIst.Controls.Add(this.bSelectSendPreset, 2, 0);
            this.tlpSend_SendLIst.Controls.Add(this.bSend_SendList, 3, 0);
            this.tlpSend_SendLIst.PerformLayout();
        }

        private void InitSendPresetPicker()
        {
            try
            {
                Socket_SendInfo firstPreset = Socket_Cache.SendList.lstSend.FirstOrDefault();
                this.selectedSendPresetId = firstPreset == null ? Guid.Empty : firstPreset.SID;
                this.UpdateSelectedSendPresetDisplay();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void UpdateSelectedSendPresetDisplay()
        {
            Socket_SendInfo selectedPreset = Socket_Cache.SendList.lstSend.FirstOrDefault(
                item => item.SID == this.selectedSendPresetId);
            bool hasPreset = selectedPreset != null;

            if (hasPreset)
            {
                this.lSelectedSendPreset.Text = string.Format(
                    this.SendPresetPickerText("Robot_SelectedNameFormat"),
                    selectedPreset.SName ?? string.Empty);
                this.lSelectedSendFolder.Text = string.Format(
                    this.SendPresetPickerText("Robot_SelectedFolderFormat"),
                    string.IsNullOrWhiteSpace(selectedPreset.SFolder)
                        ? this.SendPresetPickerText("Picker_Ungrouped")
                        : selectedPreset.SFolder);
            }
            else
            {
                this.lSelectedSendPreset.Text = this.SendPresetPickerText("Robot_NoPreset");
                this.lSelectedSendFolder.Text = string.Empty;
            }

            this.bSelectSendPreset.Enabled = Socket_Cache.SendList.lstSend.Count > 0;
            this.bSend_SendList.Enabled = hasPreset;
        }

        private void bSelectSendPreset_Click(object sender, EventArgs e)
        {
            try
            {
                using (Socket_SendPresetPickerForm picker =
                    new Socket_SendPresetPickerForm(this.selectedSendPresetId))
                {
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        this.selectedSendPresetId = picker.SelectedSendPresetId;
                        this.UpdateSelectedSendPresetDisplay();
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void dgvRobotInstruction_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            try
            {
                if (e.ColumnIndex == dgvRobotInstruction.Columns["cRobotInstruction_Type"].Index)
                {
                    Socket_Cache.Robot.InstructionType instructionType = (Socket_Cache.Robot.InstructionType)dgvRobotInstruction.Rows[e.RowIndex].Cells["cRobotInstruction_Type"].Value;
                    e.Value = Socket_Cache.Robot.GetName_ByInstructionType(instructionType);
                    e.CellStyle.ForeColor = Socket_Cache.Robot.GetColor_ByInstructionType(instructionType);
                    e.FormattingApplied = true;
                }
                else if (e.ColumnIndex == dgvRobotInstruction.Columns["cRobotInstruction_ID"].Index)
                {
                    e.Value = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_98), e.RowIndex + 1);
                    e.CellStyle.ForeColor = Color.RoyalBlue;
                    e.FormattingApplied = true;
                }
                else if (e.ColumnIndex == dgvRobotInstruction.Columns["cRobotInstruction_Content"].Index)
                {
                    string sContent = dgvRobotInstruction.Rows[e.RowIndex].Cells["cRobotInstruction_Content"].Value.ToString();
                    Socket_Cache.Robot.InstructionType instructionType = (Socket_Cache.Robot.InstructionType)dgvRobotInstruction.Rows[e.RowIndex].Cells["cRobotInstruction_Type"].Value;
                    e.Value = Socket_Cache.Robot.GetContentString_ByInstructionType(instructionType, sContent);                    
                    e.FormattingApplied = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }        

        #endregion

        #region//保存按钮

        private void bSave_Click(object sender, EventArgs e)
        {
            try
            {
                string RName_New = this.txtRobotName.Text.Trim();

                if (string.IsNullOrEmpty(RName_New))
                {
                    Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_19));
                    return;
                }

                if (this.dtRobotInstruction.Rows.Count > 0)
                {
                    int iReturn = Socket_Cache.Robot.CheckRobotInstruction(this.dtRobotInstruction, false);

                    if (iReturn > -1 && iReturn < dgvRobotInstruction.Rows.Count)
                    {
                        this.dgvRobotInstruction.CurrentCell = dgvRobotInstruction.Rows[iReturn].Cells[0];
                        this.dgvRobotInstruction.FirstDisplayedScrollingRowIndex = iReturn;

                        return;
                    }
                }

                Socket_Cache.Robot.UpdateRobot(sriSelect, RName_New, this.dtRobotInstruction);

                this.Close();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//关闭按钮

        private void bClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #endregion

        #region//执行按钮

        private void bExecute_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.dtRobotInstruction.Rows.Count > 0)
                {
                    int iReturn = Socket_Cache.Robot.CheckRobotInstruction(this.dtRobotInstruction, false);

                    if (iReturn > -1 && iReturn < dgvRobotInstruction.Rows.Count)
                    {
                        this.dgvRobotInstruction.CurrentCell = dgvRobotInstruction.Rows[iReturn].Cells[0];
                        this.dgvRobotInstruction.FirstDisplayedScrollingRowIndex = iReturn;

                        return;
                    }

                    if (!this.sr.Worker.IsBusy)
                    {
                        this.txtExecute.Clear();
                        this.bExecute.Enabled = false;
                        this.bStop.Enabled = true;
                        this.tcRobotInstruction.Enabled = false;

                        if (this.dgvRobotInstruction.ContextMenuStrip != null)
                        {
                            this.dgvRobotInstruction.ContextMenuStrip.Enabled = false;
                        }

                        sr.StartRobot(sriSelect.RName, this.dtRobotInstruction, null);
                    }
                }                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void Worker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            try
            {
                if (e.Cancelled)
                {
                    string sMsg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_110), sriSelect.RName);
                    Socket_Operation.ShowMessageBox(sMsg);
                }
                else if (e.Error != null)
                {
                    string sMsg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_111), sriSelect.RName, e.Error.Message);
                    Socket_Operation.ShowMessageBox(sMsg);
                }
                else
                {
                    string sMsg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_112), sriSelect.RName);
                    Socket_Operation.ShowMessageBox(sMsg);
                }                

                this.bExecute.Enabled = true;
                this.bStop.Enabled = false;
                this.tcRobotInstruction.Enabled = true;

                if (this.dgvRobotInstruction.ContextMenuStrip != null)
                {
                    this.dgvRobotInstruction.ContextMenuStrip.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void Worker_ProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            try
            {
                int iIndex = e.ProgressPercentage;                

                if (iIndex > -1 && iIndex < dgvRobotInstruction.Rows.Count)
                {
                    this.dgvRobotInstruction.CurrentCell = this.dgvRobotInstruction.Rows[iIndex].Cells[0];
                    this.dgvRobotInstruction.FirstDisplayedScrollingRowIndex = iIndex;
                }

                this.txtExecute.AppendText((iIndex + 1).ToString() + ", ");
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//终止按钮

        private void bStop_Click(object sender, EventArgs e)
        {
            this.sr.StopRobot();
        }

        #endregion        

        #region//指令集的列表操作

        public int UpdateInstruction_ByListAction(Socket_Cache.System.ListAction listAction, int InstructionIndex)
        {
            int iReturn = -1;

            try
            {                
                int iInstructionCount = this.dtRobotInstruction.Rows.Count;
                DataRow dr = this.dtRobotInstruction.NewRow();
                dr.ItemArray = this.dtRobotInstruction.Rows[InstructionIndex].ItemArray;

                switch (listAction)
                {
                    case Socket_Cache.System.ListAction.Top:

                        if (InstructionIndex > 0)
                        {
                            this.dtRobotInstruction.Rows.RemoveAt(InstructionIndex);
                            this.dtRobotInstruction.Rows.InsertAt(dr, 0);
                            iReturn = 0;
                        }

                        break;

                    case Socket_Cache.System.ListAction.Up:

                        if (InstructionIndex > 0)
                        {
                            this.dtRobotInstruction.Rows.RemoveAt(InstructionIndex);
                            this.dtRobotInstruction.Rows.InsertAt(dr, InstructionIndex - 1);
                            iReturn = InstructionIndex - 1;
                        }

                        break;

                    case Socket_Cache.System.ListAction.Down:

                        if (InstructionIndex < iInstructionCount - 1)
                        {
                            this.dtRobotInstruction.Rows.RemoveAt(InstructionIndex);
                            this.dtRobotInstruction.Rows.InsertAt(dr, InstructionIndex + 1);
                            iReturn = InstructionIndex + 1;
                        }

                        break;

                    case Socket_Cache.System.ListAction.Bottom:

                        if (InstructionIndex < iInstructionCount - 1)
                        {
                            this.dtRobotInstruction.Rows.RemoveAt(InstructionIndex);
                            this.dtRobotInstruction.Rows.Add(dr);
                            iReturn = this.dtRobotInstruction.Rows.Count - 1;
                        }

                        break;

                    case Socket_Cache.System.ListAction.Delete:

                        this.dtRobotInstruction.Rows.RemoveAt(InstructionIndex);                        

                        break;

                    case Socket_Cache.System.ListAction.CleanUp:

                        this.dtRobotInstruction.Clear();

                        break;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return iReturn;
        }

        #endregion

        #region//指令集右键菜单

        private void cmsRobotInstruction_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            string sItemText = e.ClickedItem.Name;
            cmsRobotInstruction.Close();

            try
            {
                if (dgvRobotInstruction.Rows.Count > 0)
                {
                    int iIndex = -1;
                    int iInstructionIndex = this.dgvRobotInstruction.CurrentRow.Index;

                    if (iInstructionIndex > -1)
                    {
                        switch (sItemText)
                        {
                            case "cmsRobotInstruction_Top":
                                iIndex = this.UpdateInstruction_ByListAction(Socket_Cache.System.ListAction.Top, iInstructionIndex);
                                break;

                            case "cmsRobotInstruction_Up":
                                iIndex = this.UpdateInstruction_ByListAction(Socket_Cache.System.ListAction.Up, iInstructionIndex);
                                break;

                            case "cmsRobotInstruction_Down":
                                iIndex = this.UpdateInstruction_ByListAction(Socket_Cache.System.ListAction.Down, iInstructionIndex);
                                break;

                            case "cmsRobotInstruction_Bottom":
                                iIndex = this.UpdateInstruction_ByListAction(Socket_Cache.System.ListAction.Bottom, iInstructionIndex);
                                break;

                            case "cmsRobotInstruction_Delete":
                                iIndex = this.UpdateInstruction_ByListAction(Socket_Cache.System.ListAction.Delete, iInstructionIndex);
                                break;

                            case "cmsRobotInstruction_CleanUp":
                                iIndex = this.UpdateInstruction_ByListAction(Socket_Cache.System.ListAction.CleanUp, iInstructionIndex);
                                break;
                        }

                        if (iIndex > -1 && iIndex < dgvRobotInstruction.RowCount)
                        {                            
                            this.dgvRobotInstruction.ClearSelection();
                            this.dgvRobotInstruction.Rows[iIndex].Selected = true;
                            this.dgvRobotInstruction.CurrentCell = this.dgvRobotInstruction.Rows[iIndex].Cells[0];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//添加指令

        public void AddInstruction(Socket_Cache.Robot.InstructionType instructionType, string sContent)
        {
            try
            {
                DataRow dr = this.dtRobotInstruction.NewRow();
                dr[0] = instructionType;
                dr[1] = sContent;                

                if (this.dgvRobotInstruction.CurrentCell != null)
                {
                    int iIndex = this.dgvRobotInstruction.CurrentCell.RowIndex + 1;
                    this.dtRobotInstruction.Rows.InsertAt(dr, iIndex);
                    this.dgvRobotInstruction.CurrentCell = dgvRobotInstruction.Rows[iIndex].Cells[0];
                }
                else
                {
                    this.dtRobotInstruction.Rows.Add(dr);                   
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//封包指令 - 发送 - 发送列表

        private void bSend_SendList_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.selectedSendPresetId != Guid.Empty)
                {
                    string sContent = this.selectedSendPresetId.ToString().ToUpper();

                    this.AddInstruction(Socket_Cache.Robot.InstructionType.SendSendList, sContent);
                }                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }           
        }

        #endregion

        #region//封包指令 - 发送 - 封包列表

        private void bSend_SocketList_Click(object sender, EventArgs e)
        {
            try
            {
                this.AddInstruction(Socket_Cache.Robot.InstructionType.SendSocketList, string.Empty);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//封包指令 - 设置 - 系统套接字

        private void bSet_SystemSocket_Click(object sender, EventArgs e)
        {
            try
            {
                string sContent = string.Empty;
                if (this.rbSet_SystemSocket_SocketList.Checked)
                {
                    sContent = "SocketList";
                }
                else if (this.rbSet_SystemSocket_Filter.Checked)
                {
                    sContent = "FilterSocket";
                }
                else if (this.rbSet_SystemSocket_Customize.Checked)
                {
                    string sSocket = this.nudSet_SystemSocket_Customize.Value.ToString();
                    sContent = "Customize" + "|" + sSocket;
                }

                this.AddInstruction(Socket_Cache.Robot.InstructionType.SetSystemSocket, sContent);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//封包指令 - 延迟

        private void rbDelay_Fix_CheckedChanged(object sender, EventArgs e)
        {
            this.nudDelay_Fix.Enabled = this.rbDelay_Fix.Checked;
        }

        private void rbDelay_Random_CheckedChanged(object sender, EventArgs e)
        {
            this.nudDelay_RandomFrom.Enabled = this.nudDelay_RandomTo.Enabled = this.rbDelay_Random.Checked;
        }

        private void bDelay_Click(object sender, EventArgs e)
        {
            try
            {
                string sContent = string.Empty;

                if (this.rbDelay_Fix.Checked)
                {
                    int Delay = ((int)this.nudDelay_Fix.Value);
                    sContent = Delay.ToString();
                }
                else
                { 
                    int DelayFrom = ((int)this.nudDelay_RandomFrom.Value);
                    int DelayTo = ((int)this.nudDelay_RandomTo.Value);
                    sContent = DelayFrom.ToString() + "-" + DelayTo.ToString();
                }

                this.AddInstruction(Socket_Cache.Robot.InstructionType.Delay, sContent);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }            
        }

        #endregion

        #region//封包指令 - 循环

        private void bLoopStart_Click(object sender, EventArgs e)
        {
            try
            {
                int LoopCNT = ((int)this.nudLoop.Value);
                string sContent = LoopCNT.ToString();

                this.AddInstruction(Socket_Cache.Robot.InstructionType.LoopStart, sContent);                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }            
        }

        private void bLoopEnd_Click(object sender, EventArgs e)
        {
            try
            {
                this.AddInstruction(Socket_Cache.Robot.InstructionType.LoopEnd, string.Empty);                    
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }            
        }

        #endregion

        #region//键盘指令 - 按键

        private void bKeyBoard_Click(object sender, EventArgs e)
        {
            try
            {
                string KeyCode = this.txtKeyBoard_KeyCode.Text.Trim();

                if (!string.IsNullOrEmpty(KeyCode))
                {
                    Socket_Cache.Robot.KeyBoardType kbType = new Socket_Cache.Robot.KeyBoardType();

                    switch (this.cbbKeyBoard_KeyType.SelectedIndex)
                    {
                        case 0:
                            kbType = Socket_Cache.Robot.KeyBoardType.Press;
                            break;

                        case 1:
                            kbType = Socket_Cache.Robot.KeyBoardType.Down;
                            break;

                        case 2:
                            kbType = Socket_Cache.Robot.KeyBoardType.Up;
                            break;
                    }

                    string sContent = kbType + "|" + KeyCode;
                    this.AddInstruction(Socket_Cache.Robot.InstructionType.KeyBoard, sContent);
                }                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void txtKeyBoard_Key_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                e.SuppressKeyPress = true;
                e.Handled = true;

                this.txtKeyBoard_KeyCode.Text = e.KeyCode.ToString();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//键盘指令 - 按键组合

        private void bKeyboard_combination_Click(object sender, EventArgs e)
        {
            try
            {
                string KeyCode = this.txtKeyboard_combination.Text.Trim();                

                if (!string.IsNullOrEmpty(KeyCode))
                {
                    Socket_Cache.Robot.KeyBoardType kbType = Socket_Cache.Robot.KeyBoardType.Combine;
                    string sContent = kbType + "|" + KeyCode;

                    this.AddInstruction(Socket_Cache.Robot.InstructionType.KeyBoard, sContent);
                }                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void txtKeyboard_combination_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                e.SuppressKeyPress = true;
                e.Handled = true;

                bIsModifierKeys = true;
                string sKeyCode = string.Empty;
                Keys modifiers = Control.ModifierKeys;

                if ((modifiers & Keys.Control) == Keys.Control && (modifiers & Keys.Shift) == Keys.Shift && (modifiers & Keys.Alt) == Keys.Alt)
                {
                    sKeyCode = Keys.ControlKey.ToString() + " + " + Keys.Menu.ToString() + " + " + Keys.ShiftKey.ToString() + " + ";
                }
                else if ((modifiers & Keys.Control) == Keys.Control && (modifiers & Keys.Shift) == Keys.Shift)
                {
                    sKeyCode = Keys.ControlKey.ToString() + " + " + Keys.ShiftKey.ToString() + " + ";
                }
                else if ((modifiers & Keys.Control) == Keys.Control && (modifiers & Keys.Alt) == Keys.Alt)
                {
                    sKeyCode = Keys.ControlKey.ToString() + " + " + Keys.Menu.ToString() + " + ";
                }
                else if ((modifiers & Keys.Shift) == Keys.Shift && (modifiers & Keys.Alt) == Keys.Alt)
                {
                    sKeyCode = Keys.ShiftKey.ToString() + " + " + Keys.Menu.ToString() + " + ";
                }
                else if ((modifiers & Keys.Control) == Keys.Control)
                {
                    sKeyCode = Keys.ControlKey.ToString() + " + ";
                }
                else if ((modifiers & Keys.Shift) == Keys.Shift)
                {
                    sKeyCode = Keys.ShiftKey.ToString() + " + ";
                }
                else if ((modifiers & Keys.Alt) == Keys.Alt)
                {
                    sKeyCode = Keys.Menu.ToString() + " + ";
                }

                if (e.KeyCode != Keys.Control &&
                e.KeyCode != Keys.ControlKey &&
                e.KeyCode != Keys.Shift &&
                e.KeyCode != Keys.ShiftKey &&
                e.KeyCode != Keys.Menu &&
                e.KeyCode != Keys.Alt)
                {
                    sKeyCode += e.KeyCode.ToString();
                    bIsModifierKeys = false;
                }
                
                this.txtKeyboard_combination.Text = sKeyCode;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void txtKeyboard_combination_KeyUp(object sender, KeyEventArgs e)
        {
            try
            {
                if (bIsModifierKeys)
                {
                    this.txtKeyboard_combination.Clear();                    
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//键盘指令 - 文本

        private void bKeyboard_Text_Click(object sender, EventArgs e)
        {
            try
            {
                string KeyCode = this.txtKeyboard_Text.Text.Trim();

                if (!string.IsNullOrEmpty(KeyCode))
                {
                    Socket_Cache.Robot.KeyBoardType kbType = Socket_Cache.Robot.KeyBoardType.Text;
                    string sContent = kbType + "|" + KeyCode;

                    this.AddInstruction(Socket_Cache.Robot.InstructionType.KeyBoard, sContent);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//鼠标指令 - 按键

        private void bMouse_Click(object sender, EventArgs e)
        {
            try
            {
                Socket_Cache.Robot.MouseType mType = new Socket_Cache.Robot.MouseType();
                switch (this.cbbMouse.SelectedIndex)
                { 
                    case 0:
                        mType = Socket_Cache.Robot.MouseType.LeftClick;
                        break;

                    case 1:
                        mType = Socket_Cache.Robot.MouseType.RightClick;
                        break;

                    case 2:
                        mType = Socket_Cache.Robot.MouseType.LeftDBClick;
                        break;

                    case 3:
                        mType = Socket_Cache.Robot.MouseType.RightDBClick;
                        break;

                    case 4:
                        mType = Socket_Cache.Robot.MouseType.LeftDown;
                        break;

                    case 5:
                        mType = Socket_Cache.Robot.MouseType.LeftUp;
                        break;

                    case 6:
                        mType = Socket_Cache.Robot.MouseType.RightDown;
                        break;

                    case 7:
                        mType = Socket_Cache.Robot.MouseType.RightUp;
                        break;
                }
                
                string sContent = mType + "|" + string.Empty;
                this.AddInstruction(Socket_Cache.Robot.InstructionType.Mouse, sContent);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//鼠标指令 - 滚轮

        private void bMouseWheel_Click(object sender, EventArgs e)
        {
            try
            {
                int MouseWheel_Distance = ((int)this.nudMouseWheel_Distance.Value);             

                Socket_Cache.Robot.MouseType mType = new Socket_Cache.Robot.MouseType();
                switch (this.cbbMouseWheel_Direction.SelectedIndex)
                {
                    case 0:
                        mType = Socket_Cache.Robot.MouseType.WheelUp;
                        break;

                    case 1:
                        mType = Socket_Cache.Robot.MouseType.WheelDown;
                        break;              
                }

                string sContent = mType + "|" + MouseWheel_Distance;

                this.AddInstruction(Socket_Cache.Robot.InstructionType.Mouse, sContent);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//鼠标指令 - 移动

        private void bMouseMove_Click(object sender, EventArgs e)
        {
            try
            {
                int MouseMove_X = ((int)this.nudMouseMove_X.Value);
                int MouseMove_Y = ((int)this.nudMouseMove_Y.Value);

                Socket_Cache.Robot.MouseType mType =new Socket_Cache.Robot.MouseType();
                if (this.rbMoveTo.Checked)
                {
                    mType = Socket_Cache.Robot.MouseType.MoveTo;
                }
                else
                {
                    mType = Socket_Cache.Robot.MouseType.MoveBy;
                }                

                string sContent = mType + "|" + MouseMove_X + ", " + MouseMove_Y;

                this.AddInstruction(Socket_Cache.Robot.InstructionType.Mouse, sContent);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        
    }
}
