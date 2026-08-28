using Be.Windows.Forms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    public partial class Socket_SendForm : Form
    {
        private Socket_PacketInfo SPI;
        private long Send_CNT = 0;
        private long Send_Success = 0;
        private long Send_Fail = 0;
        private int sendCounterUpdateScheduled;
        private CancellationTokenSource cts;
        private ManualResetEventSlim byteSweepPauseGate;
        private bool byteSweepPaused;
        private Guid byteSweepJobId = Guid.Empty;
        private bool byteSweepRuntimeExternalRunning;
        private RadioButton rbSendType_ByteSweep;
        private Label lByteSweepHint;
        private ToolStripStatusLabel tlByteSweepProgress;
        private Button bSaveByteSweepPreset;
        private Button bStartByteSweep;
        private Button bStopByteSweep;
        private long byteSweepOriginalSelectionStart;
        private long byteSweepOriginalSelectionLength;
        private long byteSweepProcessedLength;
        private bool byteSweepWasRunning;
        private bool byteSweepPairWasRunning;
        private bool byteSweepStartedFromEditorPanel;
        private int byteSweepPairFirstLength;
        private int byteSweepPairSecondLength;
        private int byteSweepRunLoopCount;
        private long byteSweepPlannedTotal;
        private System.Drawing.Color byteSweepOriginalSelectionBackColor;
        private System.Drawing.Color byteSweepOriginalSelectionForeColor;
        private bool byteSweepHighlightActive;
        private bool byteSweepLiveDisplayEnabled;
        private bool byteSweepRestoringLiveDisplay;
        private bool byteSweepLiveValueActive;
        private long byteSweepLivePosition = -1;
        private byte byteSweepLiveOriginalValue;
        private long byteSweepLiveSecondPosition = -1;
        private byte byteSweepLiveSecondOriginalValue;
        private bool byteSweepLiveSecondValueActive;
        private int byteSweepLiveDisplayGeneration;
        private bool byteSweepSelectionGuard;
        private long byteSweepLiveSelectionStart = -1;
        private long byteSweepLiveSelectionLength = 1;
        private bool byteSweepProviderHadChanges;
        private ToolStripSeparator dynamicVariableMenuSeparator;
        private ToolStripMenuItem replaceDynamicVariableMenuItem;
        private ToolStripMenuItem editDynamicBindingMenuItem;
        private ToolStripMenuItem removeDynamicBindingMenuItem;
        private bool packetEditorLockActive;
        private bool packetEditorOriginalReadOnly;
        private Socket_ByteAnnotationController byteAnnotationController;
        private List<Socket_ByteAnnotationInfo> workingByteAnnotations;
        private List<PresetVariableBinding> workingVariableBindings;
        private byte[] preparedVariableBuffer;
        private Socket_SendInfo savedSendPreset;
        private Socket_PacketInfo savedSendPresetPacket;
        private Socket_ByteSweepPresetInfo savedByteSweepPreset;
        private Socket_Cache.SocketList.CurrentSocketRoute resolvedCurrentRoute;
        private string baseWindowTitle;
        private ToolStripStatusLabel tlCurrentPacketIdentity;
        private ToolStripStatusLabel tlDynamicVariablePreview;
        private TableLayoutPanel tlpSendActions;
        private TableLayoutPanel tlpByteSweepSettings;
        private Panel pnlByteSweepActions;
        private TableLayoutPanel pnlByteSweepSide;
        private Button bAdvancedPairEditorToggle;
        private bool advancedPairEditorExpanded = true;
        private Socket_ByteSweepEditorPanel byteSweepEditorPanel;
        private bool byteSweepEditorSyncing;
        private bool byteSweepEditorDirty;
        private NumericUpDown nudByteSweepLoopCount;
        private NumericUpDown nudByteSweepInterval;
        private NumericUpDown nudByteSweepNextInterval;
        private ComboBox cbbByteSweepMode;
        private NumericUpDown nudByteSweepFirstPosition;
        private NumericUpDown nudByteSweepFirstInterval;
        private NumericUpDown nudByteSweepFirstLength;
        private NumericUpDown nudByteSweepSecondPosition;
        private NumericUpDown nudByteSweepSecondInterval;
        private NumericUpDown nudByteSweepSecondLength;
        private bool updatingSendMode;
        private int byteSweepPositionPickTarget;
        private enum SendUiMode
        {
            Normal,
            SequentialSweep,
            PairSweep
        }

        private SendUiMode sendUiMode = SendUiMode.Normal;
        private TableLayoutPanel sendModeSelector;
        private Button sendModeNormal;
        private Button sendModeSequential;
        private Button sendModePair;

        private sealed class SendWorkItem
        {
            public int Socket;
            public int Interval;
            public int Times;
            public string IPFrom;
            public string IPTo;
            public byte[] Buffer;
            public bool Continuously;
            public bool ByteSweep;
            public bool ProgressionEnabled;
            public int ProgressionPosition;
            public int ProgressionStep;
            public bool ProgressionCarryEnabled;
            public int ProgressionCarryCount;
            public int SweepStart;
            public int SweepLength;
            public bool PairCombination;
            public int PairFirstPosition;
            public int PairFirstLength;
            public int PairFirstInterval;
            public int PairSecondPosition;
            public int PairSecondLength;
            public int PairSecondInterval;
        }

        #region//窗体加载

        public Socket_SendForm(Socket_PacketInfo spi)
            : this(spi, null)
        {
        }

        public Socket_SendForm(
            Socket_PacketInfo spi,
            Socket_ByteSweepPresetInfo byteSweepPreset)
        {
            try
            {
                MultiLanguage.SetDefaultLanguage(MultiLanguage.DefaultLanguage);                
                InitializeComponent();
                this.InitializeDynamicVariableMenu();
                this.MinimumSize = new System.Drawing.Size(1200, 620);
                this.InitializeHexSelectionAppearance();
                this.InitializeSendPanelLayout();
                this.InitializeByteSweepSidePanel();
                this.hbPacketData.AccessibleName = UiText("Main_PacketData");
                this.byteAnnotationController = new Socket_ByteAnnotationController(
                this.hbPacketData, this.pnlByteSweepSide, 0, 0, 1,
                delegate { return !this.bgwSendPacket.IsBusy; });
                this.byteAnnotationController.Changed += this.ByteAnnotationController_Changed;
                this.byteAnnotationController.SetUiVisible(false);
                this.InitializeByteSweepControls();
                this.InitializeByteSweepEditorPanel();
                this.byteSweepEditorPanel.StatusChanged += this.byteSweepEditorPanel_StatusChanged;
                this.baseWindowTitle = this.Text;
                this.tlCurrentPacketIdentity = new ToolStripStatusLabel
                {
                    Name = "tlCurrentPacketIdentity",
                    ForeColor = System.Drawing.Color.Navy,
                    Spring = true,
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft
                };
                this.ssSocketSend.Items.Insert(0, this.tlCurrentPacketIdentity);
                this.tlDynamicVariablePreview = new ToolStripStatusLabel
                {
                    Name = "tlDynamicVariablePreview",
                    ForeColor = System.Drawing.Color.DarkSlateGray,
                    AutoSize = true,
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft
                };
                this.ssSocketSend.Items.Insert(1, this.tlDynamicVariablePreview);

                if (spi != null)
                { 
                    this.SPI = spi;
                }
                this.savedByteSweepPreset = byteSweepPreset;
                this.InitializePresetIdentity();
                Socket_ByteSweepRuntime.Current.StateChanged += this.ByteSweepRuntime_StateChanged;
                Socket_ByteSweepRuntime.Current.ProgressChanged += this.ByteSweepRuntime_ProgressChanged;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        private void InitializePresetIdentity()
        {
            if (this.savedByteSweepPreset != null)
            {
                this.UpdateByteSweepPresetIdentity();
                return;
            }

            Socket_SendInfo containingPreset = Socket_Cache.SendList.lstSend.FirstOrDefault(item =>
                item.SCollection != null &&
                item.SCollection.Any(packet => ReferenceEquals(packet, this.SPI)));
            if (containingPreset != null)
            {
                this.savedSendPreset = containingPreset;
                this.savedSendPresetPacket = this.SPI;
            }
            this.UpdatePresetIdentity(containingPreset);
        }

        private void ByteSweepRuntime_StateChanged(Socket_ByteSweepRuntimeSnapshot snapshot)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                this.BeginInvoke((Action)(() =>
                {
                    bool busy = snapshot.State == Socket_ByteSweepRuntimeState.Starting ||
                        snapshot.State == Socket_ByteSweepRuntimeState.Running ||
                        snapshot.State == Socket_ByteSweepRuntimeState.Stopping;
                    bool ownJob = snapshot.JobId != Guid.Empty && snapshot.JobId == this.byteSweepJobId;
                    this.byteSweepRuntimeExternalRunning = busy && !ownJob;
                    if (this.byteSweepRuntimeExternalRunning)
                    {
                        this.tlByteSweepProgress.Visible = true;
                        this.tlByteSweepProgress.Text = snapshot.Detail ?? UiText("ByteSweep_Running");
                    }
                    else if (!busy && !ownJob && !string.IsNullOrEmpty(snapshot.Detail))
                    {
                        this.tlByteSweepProgress.Visible = true;
                        this.tlByteSweepProgress.Text = snapshot.Detail;
                    }
                    this.UpdateByteSweepModeControls();
                    this.SendTypeChanged();
                }));
            }
            catch (InvalidOperationException)
            {
                // 窗口关闭过程中不再更新递进状态。
            }
        }

        private void ByteSweepRuntime_ProgressChanged(Socket_ByteSweepRuntimeSnapshot snapshot)
        {
            if (this.IsDisposed || !this.IsHandleCreated || snapshot.JobId == this.byteSweepJobId)
            {
                return;
            }

            try
            {
                this.BeginInvoke((Action)(() =>
                {
                    if (this.IsDisposed)
                    {
                        return;
                    }

                    this.tlByteSweepProgress.Visible = true;
                    this.tlByteSweepProgress.Text = FormatRuntimeProgress(snapshot);
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static string FormatRuntimeProgress(Socket_ByteSweepRuntimeSnapshot snapshot)
        {
            if (snapshot.IsPairCombination)
            {
                return string.Format(
                    UiText("ByteSweep_PairProgress"),
                    snapshot.CurrentLoop,
                    snapshot.LoopCount,
                    snapshot.PairFirstValue,
                    snapshot.PairFirstValueNumber,
                    snapshot.PairFirstValueCount,
                    snapshot.PairSecondValue,
                    snapshot.PairSecondValueNumber,
                    snapshot.PairSecondValueCount,
                    snapshot.TotalSend,
                    snapshot.PlannedTotal);
            }

            return string.Format(
                UiText("UI_SweepProgress"),
                snapshot.PresetName,
                snapshot.CurrentLoop,
                snapshot.LoopCount,
                snapshot.ByteNumber,
                snapshot.ByteCount,
                snapshot.ValueNumber);
        }

        private void UpdatePresetIdentity(Socket_SendInfo preset)
        {
            if (preset == null)
            {
                this.Text = this.baseWindowTitle;
                this.tlCurrentPacketIdentity.Text = UiText("UI_CurrentPacketUnsaved");
                this.tlCurrentPacketIdentity.ToolTipText = this.tlCurrentPacketIdentity.Text;
                return;
            }

            this.Text = this.baseWindowTitle + " - " + preset.SName;
            this.tlCurrentPacketIdentity.Text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                UiText("UI_CurrentPacketIdentity"),
                preset.SName,
                preset.SFolder);
            this.tlCurrentPacketIdentity.ToolTipText = this.tlCurrentPacketIdentity.Text;
        }

        private void UpdateByteSweepPresetIdentity()
        {
            this.Text = this.baseWindowTitle + " - " + this.savedByteSweepPreset.BName;
            this.tlCurrentPacketIdentity.Text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                UiText("UI_CurrentSweepPresetIdentity"),
                this.savedByteSweepPreset.BName,
                this.savedByteSweepPreset.BFolder);
            this.tlCurrentPacketIdentity.ToolTipText = this.tlCurrentPacketIdentity.Text;
        }

        #region//逐字节递进界面

        private void InitializeHexSelectionAppearance()
        {
            this.hbPacketData.SelectionBackColor =
                System.Drawing.Color.FromArgb(210, 230, 255);
            this.hbPacketData.SelectionForeColor = System.Drawing.Color.Black;
            this.hbPacketData.ConfigureSelectionByteBoxes(
                true,
                System.Drawing.Color.FromArgb(0, 90, 180));
        }

        private void InitializeSendPanelLayout()
        {
            this.tlpSendForm.SuspendLayout();
            this.tlpParameter.SuspendLayout();
            this.tlpSendType.SuspendLayout();

            this.gbSendSocket.Visible = false;
            this.gbSendSocket.TabStop = false;

            this.tlpParameter.ColumnCount = 2;
            this.tlpParameter.ColumnStyles.Clear();
            this.tlpParameter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            this.tlpParameter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            this.tlpParameter.RowCount = 2;
            this.tlpParameter.RowStyles.Clear();
            this.tlpParameter.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            this.tlpParameter.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tlpParameter.Padding = new Padding(4, 0, 4, 0);
            this.sendModeSelector = this.CreateSendModeSelector();
            this.tlpParameter.Controls.Add(this.sendModeSelector, 0, 0);
            this.tlpParameter.SetColumnSpan(this.sendModeSelector, 2);
            this.tlpParameter.SetCellPosition(this.gbSendType, new TableLayoutPanelCellPosition(0, 1));
            this.tlpParameter.SetCellPosition(this.gbSendStep, new TableLayoutPanelCellPosition(0, 1));
            this.tlpParameter.SetColumnSpan(this.gbSendType, 2);
            this.tlpParameter.SetColumnSpan(this.gbSendStep, 2);
            this.tlpParameter.SetCellPosition(this.gbSendSocket, new TableLayoutPanelCellPosition(0, 1));
            this.tlpParameter.SetColumnSpan(this.gbSendSocket, 2);
            this.gbSendType.BringToFront();
            this.gbSendStep.BringToFront();
            this.gbSendType.Margin = new Padding(0, 3, 0, 3);
            this.gbSendStep.Margin = new Padding(0, 3, 0, 3);
            this.gbSendType.TabIndex = 0;
            this.gbSendStep.TabIndex = 1;

            this.tlpSendType.Padding = new Padding(8, 4, 8, 5);
            this.tlpSendType.ColumnStyles.Clear();
            this.tlpSendType.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.tlpSendType.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            this.tlpSendType.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.tlpSendType.RowCount = 3;
            this.tlpSendType.RowStyles.Clear();
            this.tlpSendType.RowStyles.Add(new RowStyle(SizeType.Percent, 29));
            this.tlpSendType.RowStyles.Add(new RowStyle(SizeType.Percent, 29));
            // 三种模式统一操作按钮高度，避免普通发送按钮明显高于递进模式。
            this.tlpSendType.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

            this.tlpSendActions = new TableLayoutPanel
            {
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Name = "tlpSendActions",
                RowCount = 1
            };
            this.tlpSendActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            this.tlpSendActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            this.tlpSendActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
            this.tlpSendActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            this.bSend.Margin = new Padding(3, 4, 3, 3);
            this.bSendStop.Margin = new Padding(3, 4, 3, 3);
            this.bSave.Margin = new Padding(3, 4, 3, 3);
            this.tlpSendActions.Controls.Add(this.bSend, 0, 0);
            this.tlpSendActions.Controls.Add(this.bSendStop, 1, 0);
            this.tlpSendActions.Controls.Add(this.bSave, 2, 0);
            this.tlpSendType.Controls.Add(this.tlpSendActions, 0, 2);
            this.tlpSendType.SetColumnSpan(this.tlpSendActions, 3);

            this.bClose.Visible = false;
            this.bClose.TabStop = false;
            this.tlpButtons.Controls.Remove(this.bClose);
            this.tlpSendForm.Controls.Remove(this.tlpButtons);
            this.tlpButtons.Visible = false;
            this.tlpButtons.TabStop = false;
            this.tlpSendForm.RowCount = 4;
            this.tlpSendForm.RowStyles.Clear();
            this.tlpSendForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            this.tlpSendForm.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            // 发送页在最小窗口高度下也要为递进参数完整留出空间，
            // 适当压缩封包数据显示区，避免下方输入框和操作按钮被裁切。
            this.tlpSendForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
            this.tlpSendForm.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tlpSendForm.SetCellPosition(
                this.ssSocketSend,
                new TableLayoutPanelCellPosition(0, 3));

            this.tlpSendType.ResumeLayout(true);
            this.tlpParameter.ResumeLayout(true);
            this.tlpSendForm.ResumeLayout(true);
        }

        private TableLayoutPanel CreateSendModeSelector()
        {
            TableLayoutPanel selector = new TableLayoutPanel
            {
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 2),
                Name = "tlpSendModeSelector",
                RowCount = 1,
                Padding = new Padding(0)
            };
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));

            this.sendModeNormal = this.CreateSendModeButton("sendModeNormal", UiText("UI_SendModeNormal"), SendUiMode.Normal);
            this.sendModeSequential = this.CreateSendModeButton("sendModeSequential", UiText("UI_SendModeSequential"), SendUiMode.SequentialSweep);
            this.sendModePair = this.CreateSendModeButton("sendModePair", UiText("UI_SendModePair"), SendUiMode.PairSweep);
            selector.Controls.Add(this.sendModeNormal, 0, 0);
            selector.Controls.Add(this.sendModeSequential, 1, 0);
            selector.Controls.Add(this.sendModePair, 2, 0);
            return selector;
        }

        private Button CreateSendModeButton(string name, string text, SendUiMode mode)
        {
            Button button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(2, 0, 2, 0),
                Name = name,
                Text = text,
                TabStop = true,
                UseVisualStyleBackColor = true,
                Tag = mode
            };
            button.AccessibleName = text;
            button.Click += this.SendModeButton_Click;
            return button;
        }

        private void SendModeButton_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button != null && button.Tag is SendUiMode)
            {
                this.SetSendUiMode((SendUiMode)button.Tag);
            }
        }

        private void SetSendUiMode(SendUiMode mode)
        {
            if (this.bgwSendPacket != null && this.bgwSendPacket.IsBusy)
            {
                return;
            }

            this.sendUiMode = mode;
            bool normal = mode == SendUiMode.Normal;
            this.gbSendType.Visible = normal;
            this.gbSendStep.Visible = !normal;
            this.UpdatePairEditorVisibility();

            if (this.cbbByteSweepMode != null && !normal)
            {
                this.cbbByteSweepMode.SelectedIndex = mode == SendUiMode.PairSweep ? 1 : 0;
            }

            this.UpdateSendModeButtonState();
            this.SendTypeChanged();
            if (!normal && this.tlByteSweepProgress != null)
            {
                this.tlByteSweepProgress.Visible = true;
                this.tlByteSweepProgress.Text = this.byteSweepEditorPanel != null &&
                    !string.IsNullOrWhiteSpace(this.byteSweepEditorPanel.StatusText)
                    ? this.byteSweepEditorPanel.StatusText
                    : UiText("ByteSweep_Idle");
                this.tlByteSweepProgress.ForeColor = this.byteSweepEditorPanel == null
                    ? System.Drawing.Color.RoyalBlue
                    : this.byteSweepEditorPanel.StatusForeColor;
            }
        }

        private void UpdateSendModeButtonState()
        {
            Button[] buttons = { this.sendModeNormal, this.sendModeSequential, this.sendModePair };
            for (int index = 0; index < buttons.Length; index++)
            {
                if (buttons[index] == null)
                {
                    continue;
                }

                bool selected = buttons[index].Tag is SendUiMode &&
                    (SendUiMode)buttons[index].Tag == this.sendUiMode;
                buttons[index].BackColor = selected
                    ? System.Drawing.Color.FromArgb(210, 230, 255)
                    : SystemColors.Control;
                buttons[index].ForeColor = selected
                    ? System.Drawing.Color.FromArgb(0, 70, 140)
                    : SystemColors.ControlText;
                buttons[index].FlatStyle = selected
                    ? FlatStyle.Standard
                    : FlatStyle.System;
                buttons[index].UseVisualStyleBackColor = !selected;
                buttons[index].Font = new Font(
                    buttons[index].Font,
                    selected ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        private void UpdatePairEditorVisibility()
        {
            bool visible = this.sendUiMode == SendUiMode.PairSweep;
            if (this.pnlByteSweepSide != null)
            {
                this.pnlByteSweepSide.Visible = visible;
                if (this.byteSweepEditorPanel != null)
                {
                    this.byteSweepEditorPanel.Visible = visible && this.advancedPairEditorExpanded;
                    this.pnlByteSweepSide.RowStyles[2].SizeType = SizeType.Percent;
                    this.pnlByteSweepSide.RowStyles[2].Height = visible ? 100F : 0F;
                }
            }

            if (this.tlpSendForm != null && this.tlpSendForm.RowStyles.Count > 2)
            {
                if (visible)
                {
                    this.tlpSendForm.RowStyles[1].SizeType = SizeType.Absolute;
                    this.tlpSendForm.RowStyles[1].Height = 230F;
                    this.tlpSendForm.RowStyles[2].SizeType = SizeType.Percent;
                    this.tlpSendForm.RowStyles[2].Height = 100F;
                }
                else
                {
                    this.tlpSendForm.RowStyles[1].SizeType = SizeType.Percent;
                    this.tlpSendForm.RowStyles[1].Height = 100F;
                    this.tlpSendForm.RowStyles[2].SizeType = SizeType.Absolute;
                    this.tlpSendForm.RowStyles[2].Height = 220F;
                }
            }
        }

        private void InitializeByteSweepSidePanel()
        {
            this.tlpPacketData.ColumnCount = 1;
            this.tlpPacketData.ColumnStyles.Clear();
            this.tlpPacketData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tlpPacketData.RowCount = 2;
            this.tlpPacketData.RowStyles.Clear();
            this.tlpPacketData.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            this.tlpPacketData.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            // 字节类型解析信息不再占用发送页空间；保留控件和更新逻辑，方便兼容已有调用。
            this.tableLayoutPanel1.Visible = false;
            this.tableLayoutPanel1.TabStop = false;
            this.lHexBox_Position.Visible = false;
            this.lHexBox_Position.TabStop = false;

            this.pnlByteSweepSide = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 0, 0),
                Name = "pnlByteSweepSide",
                RowCount = 3,
                Padding = new Padding(0)
            };
            this.pnlByteSweepSide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            // 双字节选择区显示在封包数据下方；保留旧行结构以兼容已有控制器引用。
            this.pnlByteSweepSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            this.pnlByteSweepSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            this.pnlByteSweepSide.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.pnlByteSweepSide.Visible = false;
            this.tlpParameter.Controls.Add(this.pnlByteSweepSide, 0, 1);
            this.tlpParameter.SetColumnSpan(this.pnlByteSweepSide, 2);
        }

        private void ConfigureSendActionColumns(bool showSaveButton)
        {
            if (this.tlpSendActions == null)
            {
                return;
            }

            this.tlpSendActions.ColumnStyles[0].Width = showSaveButton ? 33.333F : 50F;
            this.tlpSendActions.ColumnStyles[1].Width = showSaveButton ? 33.333F : 50F;
            this.tlpSendActions.ColumnStyles[2].Width = showSaveButton ? 33.334F : 0F;
        }

        private void InitializeByteSweepControls()
        {
            this.rbSendType_ByteSweep = new RadioButton
            {
                AutoSize = true,
                AutoCheck = false,
                Checked = true,
                Dock = DockStyle.Fill,
                Name = "rbSendType_ByteSweep",
                Text = UiText("ByteSweep_SequentialHeader"),
                UseVisualStyleBackColor = true
            };

            this.lByteSweepHint = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Name = "lByteSweepHint",
                Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_233),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            TableLayoutPanel byteSweepMode = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Name = "tlpByteSweepMode",
                RowCount = 1
            };
            byteSweepMode.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            byteSweepMode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            byteSweepMode.Controls.Add(this.rbSendType_ByteSweep, 0, 0);
            byteSweepMode.Controls.Add(this.lByteSweepHint, 1, 0);
            this.cbbByteSweepMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Name = "cbbByteSweepMode",
                TabStop = false,
                Visible = false
            };
            this.cbbByteSweepMode.Items.AddRange(new object[]
            {
                UiText("ByteSweep_SequentialMode"),
                UiText("ByteSweep_PairMode")
            });
            this.cbbByteSweepMode.SelectedIndex = 0;
            this.cbbByteSweepMode.SelectedIndexChanged += this.cbbByteSweepMode_SelectedIndexChanged;

            TableLayoutPanel progressionRoot = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Name = "tlpProgressionRoot",
                Padding = new Padding(6, 3, 6, 3),
                RowCount = 3
            };
            progressionRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            progressionRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            progressionRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            progressionRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            progressionRoot.Controls.Add(byteSweepMode, 0, 0);
            progressionRoot.Controls.Add(this.cbbByteSweepMode, 0, 0);
            byteSweepMode.BringToFront();

            this.gbSendStep.Controls.Remove(this.tlpSendStepSet);
            this.tlpSendStepSet.Dock = DockStyle.Fill;
            this.tlpSendStepSet.Margin = new Padding(0);
            progressionRoot.Controls.Add(this.tlpSendStepSet, 0, 1);
            this.gbSendStep.Controls.Add(progressionRoot);

            this.tlByteSweepProgress = new ToolStripStatusLabel
            {
                ForeColor = System.Drawing.Color.RoyalBlue,
                Name = "tlByteSweepProgress",
                Text = string.Empty,
                Visible = false
            };
            this.ssSocketSend.Items.Add(this.tlByteSweepProgress);

            this.bSaveByteSweepPreset = new Button
            {
                Dock = DockStyle.Fill,
                Name = "bSaveByteSweepPreset",
                Text = UiText("ByteSweep_SaveSequentialPreset"),
                UseVisualStyleBackColor = true,
                Visible = true
            };
            this.bSaveByteSweepPreset.Click += this.bSaveSequentialByteSweepPreset_Click;

            this.bStartByteSweep = new Button
            {
                Dock = DockStyle.Fill,
                Name = "bStartByteSweep",
                Text = UiText("ByteSweep_StartAction"),
                AccessibleName = UiText("ByteSweep_StartAction"),
                UseVisualStyleBackColor = true
            };
            this.bStartByteSweep.Click += this.bStartByteSweep_Click;

            this.bStopByteSweep = new Button
            {
                Dock = DockStyle.Fill,
                Enabled = false,
                Name = "bStopByteSweep",
                Text = UiText("ByteSweep_StopAction"),
                AccessibleName = UiText("ByteSweep_StopAction"),
                UseVisualStyleBackColor = true
            };
            this.bStopByteSweep.Click += this.bStopByteSweep_Click;

            this.pnlByteSweepActions = new Panel
            {
                Dock = DockStyle.Fill,
                Name = "pnlByteSweepActions",
                Padding = new Padding(0, 3, 0, 0),
                Visible = true
            };
            TableLayoutPanel byteSweepActions = new TableLayoutPanel
            {
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Name = "tlpByteSweepActions",
                RowCount = 1
            };
            byteSweepActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            byteSweepActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            byteSweepActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
            byteSweepActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.bStartByteSweep.Margin = new Padding(0, 0, 3, 0);
            this.bStopByteSweep.Margin = new Padding(3, 0, 3, 0);
            this.bSaveByteSweepPreset.Margin = new Padding(3, 0, 0, 0);
            byteSweepActions.Controls.Add(this.bStartByteSweep, 0, 0);
            byteSweepActions.Controls.Add(this.bStopByteSweep, 1, 0);
            byteSweepActions.Controls.Add(this.bSaveByteSweepPreset, 2, 0);
            this.pnlByteSweepActions.Controls.Add(byteSweepActions);

            this.tlpByteSweepSettings = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 6,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                Name = "tlpByteSweepSettings",
                Padding = new Padding(0, 3, 0, 0),
                RowCount = 3,
                Visible = true
            };
            for (int column = 0; column < 6; column++)
            {
                this.tlpByteSweepSettings.ColumnStyles.Add(new ColumnStyle(
                    column % 2 == 0 ? SizeType.AutoSize : SizeType.Percent,
                    column % 2 == 0 ? 0F : 33.333F));
            }
            this.tlpByteSweepSettings.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            this.tlpByteSweepSettings.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            this.tlpByteSweepSettings.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            Label loopCountLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Name = "lByteSweepLoopCount",
                Text = UiText("ByteSweep_LoopCount"),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            this.nudByteSweepLoopCount = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Maximum = 99999,
                Minimum = 1,
                Name = "nudByteSweepLoopCount",
                Value = 1
            };
            this.nudByteSweepLoopCount.AccessibleName = UiText("ByteSweep_LoopCount");
            ConfigureByteSweepInput(this.nudByteSweepLoopCount);
            Label intervalLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Name = "lByteSweepInterval",
                Text = UiText("ByteSweep_Interval"),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            this.nudByteSweepInterval = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Increment = 10,
                Maximum = 999999999,
                Name = "nudByteSweepInterval",
                Value = this.nudSendType_Interval.Value
            };
            this.nudByteSweepInterval.AccessibleName = UiText("ByteSweep_Interval");
            ConfigureByteSweepInput(this.nudByteSweepInterval);
            Label nextIntervalLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Name = "lByteSweepNextInterval",
                Text = UiText("ByteSweep_NextInterval"),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            this.nudByteSweepNextInterval = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Maximum = 999999999,
                Name = "nudByteSweepNextInterval"
            };
            this.nudByteSweepNextInterval.AccessibleName = UiText("ByteSweep_NextInterval");
            ConfigureByteSweepInput(this.nudByteSweepNextInterval);
            HideByteSweepSpinButtons(this.nudByteSweepLoopCount);
            HideByteSweepSpinButtons(this.nudByteSweepInterval);
            HideByteSweepSpinButtons(this.nudByteSweepNextInterval);
            AddByteSweepTripleRow(0,
                UiText("ByteSweep_LoopCount"), this.nudByteSweepLoopCount,
                UiText("ByteSweep_NormalInterval"), this.nudByteSweepInterval,
                UiText("ByteSweep_NextInterval"), this.nudByteSweepNextInterval);

            this.nudByteSweepFirstPosition = CreateByteSweepNumber("nudByteSweepFirstPosition", 0, 65535, 0);
            this.nudByteSweepFirstInterval = CreateByteSweepNumber("nudByteSweepFirstInterval", 0, 999999999, 1000);
            this.nudByteSweepFirstLength = CreateByteSweepNumber("nudByteSweepFirstLength", 1, 255, 255);
            this.nudByteSweepSecondPosition = CreateByteSweepNumber("nudByteSweepSecondPosition", 0, 65535, 1);
            this.nudByteSweepSecondInterval = CreateByteSweepNumber("nudByteSweepSecondInterval", 0, 999999999, 100);
            this.nudByteSweepSecondLength = CreateByteSweepNumber("nudByteSweepSecondLength", 1, 255, 255);
            HideByteSweepSpinButtons(this.nudByteSweepFirstPosition);
            HideByteSweepSpinButtons(this.nudByteSweepFirstInterval);
            HideByteSweepSpinButtons(this.nudByteSweepFirstLength);
            HideByteSweepSpinButtons(this.nudByteSweepSecondPosition);
            HideByteSweepSpinButtons(this.nudByteSweepSecondInterval);
            HideByteSweepSpinButtons(this.nudByteSweepSecondLength);
            AddByteSweepTripleRow(1,
                UiText("ByteSweep_FirstPosition"), this.nudByteSweepFirstPosition,
                UiText("ByteSweep_FirstLength"), this.nudByteSweepFirstLength,
                UiText("ByteSweep_FirstInterval"), this.nudByteSweepFirstInterval);
            AddByteSweepTripleRow(2,
                UiText("ByteSweep_SecondPosition"), this.nudByteSweepSecondPosition,
                UiText("ByteSweep_SecondLength"), this.nudByteSweepSecondLength,
                UiText("ByteSweep_SecondInterval"), this.nudByteSweepSecondInterval);
            progressionRoot.Controls.Add(this.tlpByteSweepSettings, 0, 1);
            progressionRoot.Controls.Add(this.pnlByteSweepActions, 0, 2);
            this.cbbByteSweepMode.SelectedIndexChanged += this.LegacyByteSweepControlChanged;
            this.nudByteSweepLoopCount.ValueChanged += this.LegacyByteSweepControlChanged;
            this.nudByteSweepInterval.ValueChanged += this.LegacyByteSweepControlChanged;
            this.nudByteSweepNextInterval.ValueChanged += this.LegacyByteSweepControlChanged;
            this.nudByteSweepFirstPosition.ValueChanged += this.LegacyByteSweepControlChanged;
            this.nudByteSweepFirstLength.ValueChanged += this.LegacyByteSweepControlChanged;
            this.nudByteSweepFirstInterval.ValueChanged += this.LegacyByteSweepControlChanged;
            this.nudByteSweepSecondPosition.ValueChanged += this.LegacyByteSweepControlChanged;
            this.nudByteSweepSecondLength.ValueChanged += this.LegacyByteSweepControlChanged;
            this.nudByteSweepSecondInterval.ValueChanged += this.LegacyByteSweepControlChanged;
        }

        private NumericUpDown CreateByteSweepNumber(string name, decimal minimum, decimal maximum, decimal value)
        {
            NumericUpDown control = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Maximum = maximum,
                Minimum = minimum,
                Name = name,
                Value = value
            };
            control.Increment = maximum > 255 ? 10 : 1;
            ConfigureByteSweepInput(control);
            return control;
        }

        private static void ConfigureByteSweepInput(Control control)
        {
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 1, 3, 1);
            control.MinimumSize = new System.Drawing.Size(0, 20);
            control.Height = 20;
        }

        private static void HideByteSweepSpinButtons(NumericUpDown control)
        {
            if (control == null || control.Controls.Count == 0)
            {
                return;
            }

            control.Controls[0].Visible = false;
        }

        private void AddByteSweepTripleRow(
            int row,
            string firstLabel, Control firstControl,
            string secondLabel, Control secondControl,
            string thirdLabel, Control thirdControl)
        {
            AddByteSweepField(row, 0, firstLabel, firstControl);
            AddByteSweepField(row, 2, secondLabel, secondControl);
            AddByteSweepField(row, 4, thirdLabel, thirdControl);
        }

        private void AddByteSweepField(int row, int column, string labelText, Control control)
        {
            bool visible = row == 0;
            Label label = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = labelText,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Visible = visible
            };
            control.Visible = visible;
            this.tlpByteSweepSettings.Controls.Add(label, column, row);
            this.tlpByteSweepSettings.Controls.Add(control, column + 1, row);
        }

        private void InitializeByteSweepEditorPanel()
        {
            this.bAdvancedPairEditorToggle = new Button
            {
                AccessibleName = UiText("UI_AdvancedEditorCollapse"),
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.System,
                Name = "bAdvancedPairEditorToggle",
                Text = UiText("UI_AdvancedEditorCollapse"),
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = true,
                Visible = false,
                TabStop = false
            };
            this.bAdvancedPairEditorToggle.Click += this.bAdvancedPairEditorToggle_Click;
            this.pnlByteSweepSide.Controls.Add(this.bAdvancedPairEditorToggle, 0, 1);
            this.pnlByteSweepSide.RowStyles[1].Height = 0F;

            this.byteSweepEditorPanel = new Socket_ByteSweepEditorPanel();
            this.byteSweepEditorPanel.SetPairOnlyMode(true);
            this.byteSweepEditorPanel.SendRequested += this.byteSweepEditorPanel_SendRequested;
            this.byteSweepEditorPanel.PauseRequested += this.byteSweepEditorPanel_PauseRequested;
            this.byteSweepEditorPanel.StopRequested += this.byteSweepEditorPanel_StopRequested;
            this.byteSweepEditorPanel.SaveRequested += this.byteSweepEditorPanel_SaveRequested;
            this.byteSweepEditorPanel.Changed += this.byteSweepEditorPanel_Changed;
            this.byteSweepEditorPanel.PickFirstRequested +=
                this.byteSweepEditorPanel_PickFirstRequested;
            this.byteSweepEditorPanel.PickSecondRequested +=
                this.byteSweepEditorPanel_PickSecondRequested;
            this.hbPacketData.MouseUp += this.hbPacketData_ByteSweepPickMouseUp;
            this.pnlByteSweepSide.Controls.Add(this.byteSweepEditorPanel, 0, 2);
        }

        private void bAdvancedPairEditorToggle_Click(object sender, EventArgs e)
        {
            this.advancedPairEditorExpanded = !this.advancedPairEditorExpanded;
            this.byteSweepEditorPanel.Visible = this.advancedPairEditorExpanded;
            this.pnlByteSweepSide.RowStyles[2].SizeType = this.advancedPairEditorExpanded
                ? SizeType.Percent
                : SizeType.Absolute;
            this.pnlByteSweepSide.RowStyles[2].Height = this.advancedPairEditorExpanded ? 100F : 0F;
            this.bAdvancedPairEditorToggle.Text = UiText(
                this.advancedPairEditorExpanded
                    ? "UI_AdvancedEditorCollapse"
                    : "UI_AdvancedEditorExpand");
            this.bAdvancedPairEditorToggle.AccessibleName = this.bAdvancedPairEditorToggle.Text;
        }

        private void SyncByteSweepEditorFromLegacy()
        {
            if (this.byteSweepEditorPanel == null || this.byteSweepEditorSyncing)
            {
                return;
            }

            int bufferLength = this.hbPacketData.ByteProvider == null
                ? 0
                : (int)this.hbPacketData.ByteProvider.Length;
            int start = (int)this.hbPacketData.SelectionStart;
            int length = (int)this.hbPacketData.SelectionLength;
            Socket_ByteSweepPresetInfo value = this.savedByteSweepPreset == null
                ? new Socket_ByteSweepPresetInfo()
                : this.savedByteSweepPreset.Clone();
            value.BMode = this.IsPairCombinationMode()
                ? Socket_ByteSweepMode.PairCombination
                : Socket_ByteSweepMode.Sequential;
            value.BLoopCount = (int)this.nudByteSweepLoopCount.Value;
            value.BInterval = (int)this.nudByteSweepInterval.Value;
            value.BNextInterval = (int)this.nudByteSweepNextInterval.Value;
            value.BCombinationFirstPosition = (int)this.nudByteSweepFirstPosition.Value;
            value.BCombinationFirstLength = (int)this.nudByteSweepFirstLength.Value;
            value.BCombinationFirstInterval = (int)this.nudByteSweepFirstInterval.Value;
            value.BCombinationSecondPosition = (int)this.nudByteSweepSecondPosition.Value;
            value.BCombinationSecondLength = (int)this.nudByteSweepSecondLength.Value;
            value.BCombinationSecondInterval = (int)this.nudByteSweepSecondInterval.Value;
            value.BStart = start;
            value.BLength = length <= 0 ? 1 : length;
            this.byteSweepEditorSyncing = true;
            try
            {
            this.byteSweepEditorPanel.LoadSettings(
                    value,
                    bufferLength,
                    start,
                    length <= 0 ? 1 : length);
                this.byteSweepEditorPanel.SetPresetState(this.savedByteSweepPreset != null);
                this.UpdateByteSweepEditorByteValues();
            }
            finally
            {
                this.byteSweepEditorSyncing = false;
            }
        }

        private void ApplyByteSweepEditorToLegacy()
        {
            if (this.byteSweepEditorPanel == null || this.byteSweepEditorSyncing)
            {
                return;
            }

            this.byteSweepEditorSyncing = true;
            try
            {
                Socket_ByteSweepPresetInfo value = this.byteSweepEditorPanel.ReadSettings(
                    (int)this.hbPacketData.SelectionStart,
                    Math.Max(1, (int)this.hbPacketData.SelectionLength));
                SetByteSweepNumber(this.nudByteSweepLoopCount, value.BLoopCount);
                SetByteSweepNumber(this.nudByteSweepInterval, value.BInterval);
                SetByteSweepNumber(this.nudByteSweepNextInterval, value.BNextInterval);
                this.cbbByteSweepMode.SelectedIndex = value.BMode == Socket_ByteSweepMode.PairCombination ? 1 : 0;
                SetByteSweepNumber(this.nudByteSweepFirstPosition, value.BCombinationFirstPosition);
                SetByteSweepNumber(this.nudByteSweepFirstLength, value.BCombinationFirstLength);
                SetByteSweepNumber(this.nudByteSweepFirstInterval, value.BCombinationFirstInterval);
                SetByteSweepNumber(this.nudByteSweepSecondPosition, value.BCombinationSecondPosition);
                SetByteSweepNumber(this.nudByteSweepSecondLength, value.BCombinationSecondLength);
                SetByteSweepNumber(this.nudByteSweepSecondInterval, value.BCombinationSecondInterval);
            }
            finally
            {
                this.byteSweepEditorSyncing = false;
            }
            this.SendTypeChanged();
        }

        private void byteSweepEditorPanel_Changed(object sender, EventArgs e)
        {
            this.byteSweepEditorDirty = true;
            this.ApplyByteSweepEditorToLegacy();
            if (!this.IsPairCombinationMode())
            {
                this.CancelByteSweepPositionPick();
            }
        }

        private void byteSweepEditorPanel_StatusChanged(object sender, EventArgs e)
        {
            if (this.tlByteSweepProgress == null || this.byteSweepEditorPanel == null)
            {
                return;
            }

            this.tlByteSweepProgress.Text = this.byteSweepEditorPanel.StatusText;
            this.tlByteSweepProgress.ForeColor = this.byteSweepEditorPanel.StatusForeColor;
            this.tlByteSweepProgress.ToolTipText = this.tlByteSweepProgress.Text;
        }

        private void byteSweepEditorPanel_PickFirstRequested(
            object sender,
            EventArgs e)
        {
            this.BeginByteSweepPositionPick(1);
        }

        private void byteSweepEditorPanel_PickSecondRequested(
            object sender,
            EventArgs e)
        {
            this.BeginByteSweepPositionPick(2);
        }

        private void BeginByteSweepPositionPick(int target)
        {
            if (this.bgwSendPacket.IsBusy ||
                this.byteSweepEditorPanel == null ||
                !this.byteSweepEditorPanel.IsPairCombinationSelected)
            {
                return;
            }

            this.byteSweepPositionPickTarget = target;
            this.hbPacketData.Focus();
        }

        private void hbPacketData_ByteSweepPickMouseUp(
            object sender,
            MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left ||
                this.byteSweepPositionPickTarget == 0 ||
                this.bgwSendPacket.IsBusy ||
                this.byteSweepEditorPanel == null)
            {
                return;
            }

            IByteProvider provider = this.hbPacketData.ByteProvider;
            long position = this.hbPacketData.SelectionStart;
            if (provider == null ||
                position < 0 ||
                position >= provider.Length ||
                position > 65535)
            {
                return;
            }

            this.hbPacketData.SelectionStart = position;
            this.hbPacketData.SelectionLength = 1;
            bool applied = this.byteSweepEditorPanel.ApplyPickedPosition(
                this.byteSweepPositionPickTarget == 1,
                (int)position,
                provider.ReadByte(position));
            if (applied)
            {
                this.byteSweepPositionPickTarget = 0;
            }
        }

        private void CancelByteSweepPositionPick()
        {
            this.byteSweepPositionPickTarget = 0;
            if (this.byteSweepEditorPanel != null)
            {
                this.byteSweepEditorPanel.CancelPositionPick();
            }
        }

        private void byteSweepEditorPanel_SendRequested(object sender, EventArgs e)
        {
            if (this.byteSweepPaused &&
                this.byteSweepWasRunning &&
                this.byteSweepStartedFromEditorPanel)
            {
                this.ResumeByteSweep();
                return;
            }

            this.CancelByteSweepPositionPick();
            this.ApplyByteSweepEditorToLegacy();
            this.StartSend(true, true);
        }

        private void byteSweepEditorPanel_PauseRequested(object sender, EventArgs e)
        {
            this.PauseByteSweep();
        }

        private void byteSweepEditorPanel_StopRequested(object sender, EventArgs e)
        {
            if (this.byteSweepWasRunning && this.byteSweepStartedFromEditorPanel)
            {
                this.StopSend();
            }
        }

        private void byteSweepEditorPanel_SaveRequested(object sender, EventArgs e)
        {
            this.ApplyByteSweepEditorToLegacy();
            this.bSaveByteSweepPreset_Click(this.bSaveByteSweepPreset, EventArgs.Empty);
        }

        private void PauseByteSweep()
        {
            if (!this.byteSweepWasRunning ||
                !this.byteSweepStartedFromEditorPanel ||
                this.byteSweepPaused ||
                this.byteSweepPauseGate == null)
            {
                return;
            }

            this.byteSweepPaused = true;
            this.byteSweepPauseGate.Reset();
            this.tlByteSweepProgress.Text = UiText("ByteSweep_Paused");
            this.tlByteSweepProgress.Visible = true;
            if (this.byteSweepEditorPanel != null)
            {
                this.byteSweepEditorPanel.SetOperationState(
                    true,
                    true,
                    true,
                    UiText("ByteSweep_Paused"));
            }
        }

        private void ResumeByteSweep()
        {
            if (!this.byteSweepWasRunning ||
                !this.byteSweepStartedFromEditorPanel ||
                !this.byteSweepPaused ||
                this.byteSweepPauseGate == null)
            {
                return;
            }

            this.byteSweepPaused = false;
            this.byteSweepPauseGate.Set();
            this.tlByteSweepProgress.Text = UiText("ByteSweep_Running");
            this.tlByteSweepProgress.Visible = true;
            if (this.byteSweepEditorPanel != null)
            {
                this.byteSweepEditorPanel.SetOperationState(
                    true,
                    false,
                    true,
                    UiText("ByteSweep_Running"));
            }
        }

        private void ByteAnnotationController_Changed(object sender, EventArgs e)
        {
            this.byteSweepEditorDirty = true;
        }

        private void LegacyByteSweepControlChanged(object sender, EventArgs e)
        {
            this.SyncByteSweepEditorFromLegacy();
        }

        private void rbSendType_ByteSweep_CheckedChanged(object sender, EventArgs e)
        {
            this.SendTypeChanged();
        }

        private void bStartByteSweep_Click(object sender, EventArgs e)
        {
            this.CancelByteSweepPositionPick();
            this.cbbByteSweepMode.SelectedIndex = 0;
            this.StartSend(true, false);
        }

        private void bSaveSequentialByteSweepPreset_Click(object sender, EventArgs e)
        {
            this.cbbByteSweepMode.SelectedIndex = 0;
            this.bSaveByteSweepPreset_Click(sender, e);
        }

        private void bStopByteSweep_Click(object sender, EventArgs e)
        {
            if (Socket_ByteSweepRuntime.Current.IsBusy)
            {
                if (this.byteSweepJobId != Guid.Empty)
                {
                    this.StopSend();
                }
                else
                {
                    Socket_ByteSweepRuntime.Current.RequestStop();
                }
            }
        }

        private void cbbByteSweepMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.UpdateByteSweepModeControls();
            this.SendTypeChanged();
        }

        private bool IsPairCombinationMode()
        {
            return this.cbbByteSweepMode != null && this.cbbByteSweepMode.SelectedIndex == 1;
        }

        private void UpdateByteSweepModeControls()
        {
            bool pair = this.IsPairCombinationMode();
            bool runtimeBusy = Socket_ByteSweepRuntime.Current.IsBusy;
            Control[] controls =
            {
                this.nudByteSweepFirstPosition,
                this.nudByteSweepFirstInterval,
                this.nudByteSweepFirstLength,
                this.nudByteSweepSecondPosition,
                this.nudByteSweepSecondInterval,
                this.nudByteSweepSecondLength
            };
            foreach (Control control in controls)
            {
                if (control != null)
                {
                    control.Enabled = pair && !this.bgwSendPacket.IsBusy && !runtimeBusy;
                    control.Visible = false;
                }
            }
            this.nudByteSweepInterval.Enabled = !this.bgwSendPacket.IsBusy && !runtimeBusy;
        }

        #endregion

        #region//初始化

        private void Socket_SendForm_Load(object sender, EventArgs e)
        {
            this.bSend.Enabled = true;
            this.bSendStop.Enabled = false;

                this.InitHexBox();
                this.InitSendInfo();
                this.InitSendParameters();
                this.SyncByteSweepEditorFromLegacy();
                this.byteSweepEditorDirty = false;
                this.SetSendUiMode(
                    this.savedByteSweepPreset == null
                        ? SendUiMode.Normal
                        : this.IsPairCombinationMode()
                            ? SendUiMode.PairSweep
                            : SendUiMode.SequentialSweep);
                this.SendTypeChanged();
                this.ProgressionPositionChange();
        }

        private void Socket_SendForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!this.bgwSendPacket.IsBusy &&
                !this.byteSweepWasRunning &&
                !Socket_ByteSweepRuntime.Current.IsBusy &&
                !this.ConfirmByteSweepUnsavedChanges())
            {
                e.Cancel = true;
                return;
            }

            this.CancelByteSweepPositionPick();
            this.RestoreByteSweepVisualState();
            this.StopSend();
            Socket_ByteSweepRuntime.Current.StateChanged -= this.ByteSweepRuntime_StateChanged;
            Socket_ByteSweepRuntime.Current.ProgressChanged -= this.ByteSweepRuntime_ProgressChanged;
        }

        private void InitSendInfo()
        {
            try
            {  
                this.txtPacketTime.Text = this.SPI.PacketTime.ToString("HH: mm: ss: fffffff");              
                this.txtPacketType.Text = Socket_Cache.SocketPacket.GetName_ByPacketType(this.SPI.PacketType);

                this.txtIPFrom.Text = this.SPI.PacketFrom;
                this.txtIPTo.Text = this.SPI.PacketTo;
                this.pbSocketType.Image = Socket_Cache.SocketPacket.GetImg_ByPacketType(this.SPI.PacketType);
                
                this.nudSendSocket_Len.Value = hbPacketData.ByteProvider.Length;
                bool usesCurrentSessionSocket =
                    this.savedSendPreset != null ||
                    this.savedByteSweepPreset != null;
                int displaySocket = usesCurrentSessionSocket
                    ? this.TryResolveCurrentRoute(false)
                        ? this.resolvedCurrentRoute.Socket
                        : 0
                    : this.SPI.PacketSocket;
                this.nudSendSocket_Socket.Value = Math.Min(
                    this.nudSendSocket_Socket.Maximum,
                    Math.Max(this.nudSendSocket_Socket.Minimum, displaySocket));
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void InitSendParameters()
        {
            if (this.savedByteSweepPreset != null)
            {
                this.rbSendType_ByteSweep.Checked = true;
                this.rbSendType_Times.Checked = true;
                this.rbSendType_Times.Enabled = true;
                this.rbSendType_Continuously.Enabled = true;
                this.nudByteSweepLoopCount.Value = Math.Min(
                    this.nudByteSweepLoopCount.Maximum,
                    Math.Max(this.nudByteSweepLoopCount.Minimum, this.savedByteSweepPreset.BLoopCount));
                this.nudByteSweepInterval.Value = Math.Min(
                    this.nudByteSweepInterval.Maximum,
                    Math.Max(this.nudByteSweepInterval.Minimum, this.savedByteSweepPreset.BInterval));
                this.nudByteSweepNextInterval.Value = Math.Min(
                    this.nudByteSweepNextInterval.Maximum,
                    Math.Max(this.nudByteSweepNextInterval.Minimum, this.savedByteSweepPreset.BNextInterval));
                this.cbbByteSweepMode.SelectedIndex =
                    this.savedByteSweepPreset.BMode == Socket_ByteSweepMode.PairCombination ? 1 : 0;
                SetByteSweepNumber(this.nudByteSweepFirstPosition, this.savedByteSweepPreset.BCombinationFirstPosition);
                SetByteSweepNumber(this.nudByteSweepFirstInterval, this.savedByteSweepPreset.BCombinationFirstInterval);
                SetByteSweepNumber(this.nudByteSweepFirstLength, this.savedByteSweepPreset.BCombinationFirstLength);
                SetByteSweepNumber(this.nudByteSweepSecondPosition, this.savedByteSweepPreset.BCombinationSecondPosition);
                SetByteSweepNumber(this.nudByteSweepSecondInterval, this.savedByteSweepPreset.BCombinationSecondInterval);
                SetByteSweepNumber(this.nudByteSweepSecondLength, this.savedByteSweepPreset.BCombinationSecondLength);

                int bufferLength = this.hbPacketData.ByteProvider == null
                    ? 0
                    : (int)this.hbPacketData.ByteProvider.Length;
                if (bufferLength > 0)
                {
                    int start = Math.Max(0, Math.Min(
                        this.savedByteSweepPreset.BStart,
                        bufferLength - 1));
                    int length = Math.Max(1, Math.Min(
                        this.savedByteSweepPreset.BLength,
                        bufferLength - start));
                    this.hbPacketData.SelectionStart = start;
                    this.hbPacketData.SelectionLength = length;
                }

                this.bSave.Visible = false;
                this.ConfigureSendActionColumns(false);
                this.bSaveByteSweepPreset.Text = UiText("ByteSweep_UpdateSequentialPreset");
                return;
            }

            if (this.savedSendPreset == null)
            {
                return;
            }

            int loopCount = this.savedSendPreset.SLoopCNT;
            this.rbSendType_Continuously.Checked = loopCount == 0;
            this.rbSendType_Times.Checked = loopCount != 0;
            if (loopCount > 0)
            {
                this.nudSendType_Times.Value = Math.Min(
                    this.nudSendType_Times.Maximum,
                    Math.Max(this.nudSendType_Times.Minimum, loopCount));
            }

            this.nudSendType_Interval.Value = Math.Min(
                this.nudSendType_Interval.Maximum,
                Math.Max(
                    this.nudSendType_Interval.Minimum,
                    this.savedSendPreset.SLoopINT));
        }

        private void InitHexBox()
        {
            try
            {
                this.workingByteAnnotations = Socket_ByteAnnotationEngine.Clone(this.SPI.ByteAnnotations);
                this.workingVariableBindings = (this.SPI.VariableBindings ?? new List<PresetVariableBinding>())
                    .Where(item => item != null)
                    .Select(item => item.Clone())
                    .ToList();
                Socket_AnnotatedByteProvider dbp = new Socket_AnnotatedByteProvider(
                    this.SPI.PacketBuffer, this.workingByteAnnotations, this.workingVariableBindings);
                dbp.Changed += new EventHandler(ByteProvider_Changed);
                dbp.LengthChanged += new EventHandler(ByteProvider_LengthChanged);
                hbPacketData.ByteProvider = dbp;
                this.byteAnnotationController.Bind(this.workingByteAnnotations);
                this.RefreshDynamicBindingStyle();

                DefaultByteCharConverter defConverter = new DefaultByteCharConverter();
                EbcdicByteCharProvider ebcdicConverter = new EbcdicByteCharProvider();
                tscbEncoding.Items.Add(defConverter);
                tscbEncoding.Items.Add(ebcdicConverter);
                tscbEncoding.SelectedIndex = 0;
                tscbPerLine.SelectedIndex = 0;

                this.HexBox_LinePositionChanged();
                this.HexBox_UpdatePacketLen();
                this.HexBox_ManageAbility();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }        

        #endregion

        #region//递进设置

        private void cbProgressionPosition_CheckedChanged(object sender, EventArgs e)
        {
            this.ProgressionPositionChange();
        }

        private void ProgressionPositionChange()
        {
            if (this.cbProgressionPosition.Checked)
            {
                this.cbProgressionCarry.Enabled = true;
                this.nudProgressionPosition.Enabled = true;
                this.nudProgressionStep.Enabled = true;

                this.ProgressionCarryChange();
            }
            else
            {
                this.cbProgressionCarry.Enabled = false;
                this.nudProgressionPosition.Enabled = false;
                this.nudProgressionStep.Enabled = false;
                this.nudProgressionCarry.Enabled = false;
            }          
        }

        private void cbProgressionCarry_CheckedChanged(object sender, EventArgs e)
        {
            this.ProgressionCarryChange();
        }

        private void ProgressionCarryChange()
        {
            this.nudProgressionCarry.Enabled = this.cbProgressionCarry.Checked;
        }

        #endregion        

        #region//发送类型参数

        private void rbSendType_Continuously_CheckedChanged(object sender, EventArgs e)
        {
            this.UpdateSendModeSelection(this.rbSendType_Continuously);
        }

        private void rbSendType_Times_CheckedChanged(object sender, EventArgs e)
        {
            this.UpdateSendModeSelection(this.rbSendType_Times);
        }

        private void UpdateSendModeSelection(RadioButton selectedMode)
        {
            if (this.updatingSendMode || selectedMode == null || !selectedMode.Checked)
            {
                return;
            }

            this.updatingSendMode = true;
            if (ReferenceEquals(selectedMode, this.rbSendType_Times))
            {
                this.rbSendType_Continuously.Checked = false;
            }
            else if (ReferenceEquals(selectedMode, this.rbSendType_Continuously))
            {
                this.rbSendType_Times.Checked = false;
            }
            this.updatingSendMode = false;
            this.SendTypeChanged();
        }

        private void SendTypeChanged()
        {
            bool busy = this.bgwSendPacket.IsBusy;
            bool runtimeBusy = Socket_ByteSweepRuntime.Current.IsBusy;
            bool normalMode = this.sendUiMode == SendUiMode.Normal;
            bool pairMode = !normalMode && this.IsPairCombinationMode();
            this.gbSendType.Visible = normalMode;
            this.gbSendStep.Visible = !normalMode && !pairMode;
            if (this.pnlByteSweepSide != null)
            {
                this.pnlByteSweepSide.Visible = pairMode;
            }
            this.UpdateSendModeButtonState();
            this.nudSendType_Times.Enabled = !busy && this.rbSendType_Times.Checked;
            this.nudSendType_Interval.Enabled = !busy;
            this.lSendType_Times.Enabled = !busy;
            this.lSendType_Int.Enabled = !busy;
            this.gbSendStep.Enabled = true;
            this.tlpSendStepSet.Visible = false;
            if (this.tlpByteSweepSettings != null)
            {
                this.tlpByteSweepSettings.Visible = true;
                this.tlpByteSweepSettings.BringToFront();
            }
            if (this.pnlByteSweepActions != null)
            {
                this.pnlByteSweepActions.Visible = true;
            }
            this.UpdateByteSweepModeControls();
            if (this.lByteSweepHint != null)
            {
                long selectionStart;
                long selectionLength;
                this.lByteSweepHint.Text =
                    this.TryGetByteSweepSelection(out selectionStart, out selectionLength)
                    ? string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        UiText("ByteSweep_SelectionSummary"),
                        selectionStart,
                        selectionLength)
                    : MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_233);
            }

            if (this.tlByteSweepProgress != null)
            {
                bool sweepEditorMode = this.sendUiMode != SendUiMode.Normal;
                this.tlByteSweepProgress.Visible = sweepEditorMode || this.byteSweepWasRunning;
                if (sweepEditorMode && this.byteSweepEditorPanel != null && !this.byteSweepWasRunning)
                {
                    this.tlByteSweepProgress.Text = UiText("ByteSweep_Idle");
                    this.tlByteSweepProgress.ForeColor = System.Drawing.Color.RoyalBlue;
                    this.byteSweepEditorPanel_StatusChanged(this.byteSweepEditorPanel, EventArgs.Empty);
                }
            }

            if (this.bSaveByteSweepPreset != null)
            {
                long selectionStart;
                long selectionLength;
                bool validSelection =
                    this.TryGetByteSweepSelection(out selectionStart, out selectionLength);
                this.bSaveByteSweepPreset.Visible = !normalMode;
                this.bSaveByteSweepPreset.Enabled = !normalMode && !busy && !runtimeBusy && validSelection;
                this.bStartByteSweep.Visible = !normalMode;
                this.bStartByteSweep.Enabled = !normalMode && !busy && !runtimeBusy && validSelection;
            }
            if (this.bStopByteSweep != null)
            {
                this.bStopByteSweep.Visible = !normalMode;
                this.bStopByteSweep.Enabled = !normalMode && runtimeBusy && !this.byteSweepStartedFromEditorPanel;
            }
        }

        #endregion        

        #region//检查发送数据

        private bool CheckSendPacket(bool byteSweep)
        {
            try
            {
                bool usesCurrentSessionRoute =
                    this.savedSendPreset != null ||
                    this.savedByteSweepPreset != null;
                if (usesCurrentSessionRoute && !this.TryResolveCurrentRoute(true))
                {
                    return false;
                }

                int iSocket = this.GetEffectiveSendSocket();

                if (iSocket <= 0)
                {
                    Socket_Operation.ShowMessageBox(
                        this.savedSendPreset == null
                            ? MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_45)
                            : UiText("UI_CurrentSocketRequired"));
                    return false;
                }

                if (hbPacketData.ByteProvider.Length == 0)
                {
                    Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_46));
                    return false;
                }

                if (byteSweep)
                {
                    if (this.IsPairCombinationMode())
                    {
                        if (!this.TryGetByteSweepPairConfiguration(true))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        long selectionStart;
                        long selectionLength;
                        if (!this.TryGetByteSweepSelection(out selectionStart, out selectionLength))
                        {
                            Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_234));
                            return false;
                        }
                    }
                }

                if (!byteSweep && this.cbProgressionPosition.Checked)
                {
                    int iProgressionPosition = (int)this.nudProgressionPosition.Value;

                    if (iProgressionPosition >= hbPacketData.ByteProvider.Length)
                    {
                        Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_47));
                        return false;
                    }
                }

                if (!this.TryPrepareVariableBuffer(byteSweep))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
                return false;
            }

            return true;
        }

        private bool TryPrepareVariableBuffer(bool byteSweep)
        {
            byte[] buffer = Socket_ByteAnnotationEngine.GetBytes(this.hbPacketData.ByteProvider);
            this.preparedVariableBuffer = buffer == null ? null : (byte[])buffer.Clone();
            List<PresetVariableBinding> bindings = this.workingVariableBindings ?? new List<PresetVariableBinding>();

            if (bindings.Count == 0)
            {
                return this.preparedVariableBuffer != null;
            }

            if (byteSweep)
            {
                if (this.IsPairCombinationMode())
                {
                    if (VariableResolver.HasRangeConflict(bindings,
                        (int)this.nudByteSweepFirstPosition.Value,
                        (int)this.nudByteSweepFirstLength.Value) ||
                        VariableResolver.HasRangeConflict(bindings,
                        (int)this.nudByteSweepSecondPosition.Value,
                        (int)this.nudByteSweepSecondLength.Value))
                    {
                        Socket_Operation.ShowMessageBox("动态变量绑定范围不能与字节扫掠范围重叠。");
                        return false;
                    }
                }
                else
                {
                    long sweepStart;
                    long sweepLength;
                    if (this.TryGetByteSweepSelection(out sweepStart, out sweepLength) &&
                        VariableResolver.HasRangeConflict(bindings, (int)sweepStart, (int)sweepLength))
                    {
                        Socket_Operation.ShowMessageBox("动态变量绑定范围不能与字节扫掠范围重叠。");
                        return false;
                    }
                }
            }
            else if (this.cbProgressionPosition.Checked)
            {
                int position = (int)this.nudProgressionPosition.Value;
                int carry = this.cbProgressionCarry.Checked
                    ? (int)this.nudProgressionCarry.Value
                    : 0;
                int start = Math.Max(0, position - carry);
                if (VariableResolver.HasRangeConflict(bindings, start, position - start + 1))
                {
                    Socket_Operation.ShowMessageBox("动态变量绑定范围不能与递进范围重叠。");
                    return false;
                }
            }

            DynamicVariableSnapshot snapshot = DynamicVariableRuntime.CaptureSnapshot();
            byte[] resolved;
            string error;
            Guid failedVariableId;
            if (!VariableResolver.TryResolveBuffer(
                this.preparedVariableBuffer,
                bindings,
                snapshot,
                variableId =>
                {
                    DynamicVariableDefinition definition;
                    return DynamicVariableRuntime.TryGetDefinition(variableId, out definition)
                        ? definition
                        : null;
                },
                out resolved,
                out error,
                out failedVariableId))
            {
                DynamicVariableDefinition failedDefinition;
                string symbol = failedVariableId != Guid.Empty &&
                    DynamicVariableRuntime.TryGetDefinition(failedVariableId, out failedDefinition)
                    ? failedDefinition.Symbol
                    : failedVariableId == Guid.Empty
                        ? "未知"
                        : failedVariableId.ToString("N");
                Socket_Operation.ShowMessageBox(
                    string.Format("变量 {0} 无法解析：{1}", symbol, error));
                return false;
            }

            this.preparedVariableBuffer = resolved;
            return true;
        }

        #endregion

        #region//发送封包（异步）

        private void SetSendRunningState(bool isRunning)
        {
            bool sweepRunning = isRunning && this.byteSweepWasRunning;
            bool normalSendRunning = isRunning && !this.byteSweepWasRunning;
            if (this.byteSweepEditorPanel != null)
            {
                this.byteSweepEditorPanel.SetOperationState(
                    isRunning,
                    this.byteSweepPaused,
                    sweepRunning && this.byteSweepStartedFromEditorPanel,
                    sweepRunning && this.byteSweepStartedFromEditorPanel
                        ? UiText("ByteSweep_Running")
                        : string.Empty);
            }
            this.bSend.Enabled = !isRunning;
            this.bSendStop.Enabled = normalSendRunning;
            if (this.bStartByteSweep != null)
            {
                this.bStartByteSweep.Enabled = !isRunning;
            }
            if (this.bStopByteSweep != null)
            {
                this.bStopByteSweep.Enabled =
                    sweepRunning && !this.byteSweepStartedFromEditorPanel;
            }
            this.bSave.Enabled = !isRunning && this.hbPacketData.ByteProvider != null;
            if (this.sendModeSelector != null)
            {
                this.sendModeSelector.Enabled = !isRunning;
            }
            this.SetPacketEditorRunningState(isRunning);

            this.gbSendSocket.Enabled = !isRunning;
            this.gbSendStep.Enabled = true;
            this.nudByteSweepLoopCount.Enabled = !isRunning;
            this.nudByteSweepInterval.Enabled = !isRunning;
            this.nudByteSweepNextInterval.Enabled = !isRunning;

            // 发送按钮已经位于 gbSendType 内，运行时不能再禁用整个分组，
            // 否则同组的停止按钮也会被级联禁用。
            this.gbSendType.Enabled = true;
            this.rbSendType_Times.Enabled = !isRunning;
            this.rbSendType_Continuously.Enabled = !isRunning;

            if (isRunning)
            {
                this.nudSendType_Times.Enabled = false;
                this.nudSendType_Interval.Enabled = false;
                this.lSendType_Times.Enabled = false;
                this.lSendType_Int.Enabled = false;
                if (this.bSaveByteSweepPreset != null)
                {
                    this.bSaveByteSweepPreset.Enabled = false;
                }
                return;
            }

            this.SendTypeChanged();
        }

        private void SetPacketEditorRunningState(bool isRunning)
        {
            if (this.hbPacketData == null)
            {
                return;
            }

            if (isRunning)
            {
                if (!this.packetEditorLockActive)
                {
                    this.packetEditorOriginalReadOnly = this.hbPacketData.ReadOnly;
                    this.packetEditorLockActive = true;
                }

                this.hbPacketData.ReadOnly = true;
            }
            else if (this.packetEditorLockActive)
            {
                this.hbPacketData.ReadOnly = this.packetEditorOriginalReadOnly;
                this.packetEditorLockActive = false;
            }

            this.HexBox_ManageAbilityForCopyAndPaste();
        }

        private void bSend_Click(object sender, EventArgs e)
        {
            this.StartSend(false, false);
        }

        private void StartSend(bool byteSweep, bool startedFromEditorPanel)
        {
            bool runtimeStarted = false;
            Guid runtimeJobId = Guid.Empty;
            try
            {
                if (this.CheckSendPacket(byteSweep))
                {
                    if (!bgwSendPacket.IsBusy)
                    {
                        SendWorkItem workItem = this.CreateSendWorkItem(byteSweep);

                        if (workItem.ByteSweep)
                        {
                            long plannedTotal = workItem.PairCombination
                                ? Math.Max(1L, (long)workItem.PairFirstLength * workItem.PairSecondLength) *
                                    Math.Max(1, workItem.Times)
                                : Math.Max(1L, (long)workItem.SweepLength * 255L) *
                                    Math.Max(1, workItem.Times);
                            CancellationTokenSource sharedCancellation;
                            if (!Socket_ByteSweepRuntime.Current.TryStart(
                                this.savedByteSweepPreset == null
                                    ? Guid.Empty
                                    : this.savedByteSweepPreset.BID,
                                this.savedByteSweepPreset == null
                                    ? UiText("UI_CurrentPacketUnsaved")
                                    : this.savedByteSweepPreset.BName,
                                workItem.PairCombination
                                    ? UiText("ByteSweep_PairMode")
                                    : UiText("ByteSweep_SequentialMode"),
                                workItem.Times,
                                plannedTotal,
                                out runtimeJobId,
                                out sharedCancellation))
                            {
                                MessageBox.Show(
                                    this,
                                    UiText("ByteSweep_RuntimeBusy"),
                                    UiText("ByteSweep_BatchTitle"),
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);
                                return;
                            }
                            runtimeStarted = true;
                            this.byteSweepJobId = runtimeJobId;
                            this.byteSweepPlannedTotal = plannedTotal;
                            this.cts = sharedCancellation;
                            this.byteSweepPauseGate = new ManualResetEventSlim(true);
                            this.byteSweepPaused = false;
                        }
                        else
                        {
                            this.byteSweepPauseGate = null;
                            this.byteSweepPaused = false;
                        }

                        Interlocked.Exchange(ref this.Send_CNT, 0);
                        Interlocked.Exchange(ref this.Send_Success, 0);
                        Interlocked.Exchange(ref this.Send_Fail, 0);
                        this.UpdateSendCounterLabels();

                        this.byteSweepWasRunning = workItem.ByteSweep;
                        this.byteSweepPairWasRunning =
                            workItem.ByteSweep && workItem.PairCombination;
                        this.byteSweepPairFirstLength = workItem.PairFirstLength;
                        this.byteSweepPairSecondLength = workItem.PairSecondLength;
                        this.byteSweepRunLoopCount = Math.Max(1, workItem.Times);
                        this.byteSweepStartedFromEditorPanel =
                            workItem.ByteSweep && startedFromEditorPanel;
                        this.SetSendRunningState(true);
                        if (workItem.ByteSweep)
                        {
                            this.tlByteSweepProgress.Visible = true;
                            this.byteSweepOriginalSelectionStart = this.hbPacketData.SelectionStart;
                            this.byteSweepOriginalSelectionLength = this.hbPacketData.SelectionLength;
                            this.byteSweepProcessedLength = workItem.SweepLength;
                            this.BeginByteSweepLiveDisplay();
                            this.byteSweepLiveSelectionStart =
                                this.byteSweepOriginalSelectionStart;
                            this.byteSweepLiveSelectionLength = Math.Max(
                                1,
                                this.byteSweepOriginalSelectionLength);
                            this.BeginByteSweepHighlight();
                        }

                        if (!workItem.ByteSweep)
                        {
                            this.cts = new CancellationTokenSource();
                        }
                        Socket_ByteSweepRuntime.Current.MarkRunning(runtimeJobId);
                        this.bgwSendPacket.RunWorkerAsync(workItem);
                    }
                }
            }
            catch (Exception ex)
            {
                if (runtimeStarted)
                {
                    Socket_ByteSweepRuntime.Current.Finish(runtimeJobId, false, ex, ex.Message);
                }
                this.RestoreByteSweepVisualState();
                this.byteSweepWasRunning = false;
                this.byteSweepJobId = Guid.Empty;
                this.SetSendRunningState(false);
                if (this.cts != null)
                {
                    this.cts.Dispose();
                    this.cts = null;
                }
                if (this.byteSweepPauseGate != null)
                {
                    this.byteSweepPauseGate.Dispose();
                    this.byteSweepPauseGate = null;
                }
                this.byteSweepPaused = false;
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private SendWorkItem CreateSendWorkItem(bool byteSweep)
        {
            IByteProvider dbp = this.hbPacketData.ByteProvider;
            long sweepStart = 0;
            long sweepLength = 0;
            if (byteSweep)
            {
                this.TryGetByteSweepSelection(out sweepStart, out sweepLength);
            }

            bool usesCurrentSessionRoute = this.resolvedCurrentRoute != null;
            return new SendWorkItem
            {
                Socket = usesCurrentSessionRoute
                    ? this.resolvedCurrentRoute.Socket
                    : this.GetEffectiveSendSocket(),
                Interval = byteSweep
                    ? (int)this.nudByteSweepInterval.Value
                    : (int)this.nudSendType_Interval.Value,
                Times = byteSweep
                    ? (int)this.nudByteSweepLoopCount.Value
                    : (int)this.nudSendType_Times.Value,
                IPFrom = usesCurrentSessionRoute
                    ? this.resolvedCurrentRoute.PacketFrom
                    : this.txtIPFrom.Text.Trim(),
                IPTo = usesCurrentSessionRoute
                    ? this.resolvedCurrentRoute.PacketTo
                    : this.txtIPTo.Text.Trim(),
                Buffer = this.preparedVariableBuffer == null
                    ? Socket_ByteAnnotationEngine.GetBytes(dbp)
                    : (byte[])this.preparedVariableBuffer.Clone(),
                Continuously = this.rbSendType_Continuously.Checked,
                ByteSweep = byteSweep,
                ProgressionEnabled = !byteSweep && this.cbProgressionPosition.Checked,
                ProgressionPosition = (int)this.nudProgressionPosition.Value,
                ProgressionStep = (int)this.nudProgressionStep.Value,
                ProgressionCarryEnabled = this.cbProgressionCarry.Checked,
                ProgressionCarryCount = (int)this.nudProgressionCarry.Value,
                SweepStart = byteSweep ? (int)sweepStart : 0,
                SweepLength = byteSweep ? (int)sweepLength : 0,
                PairCombination = byteSweep && this.IsPairCombinationMode(),
                PairFirstPosition = (int)this.nudByteSweepFirstPosition.Value,
                PairFirstLength = (int)this.nudByteSweepFirstLength.Value,
                PairFirstInterval = (int)this.nudByteSweepFirstInterval.Value,
                PairSecondPosition = (int)this.nudByteSweepSecondPosition.Value,
                PairSecondLength = (int)this.nudByteSweepSecondLength.Value,
                PairSecondInterval = (int)this.nudByteSweepSecondInterval.Value
            };
        }

        private bool TryGetByteSweepSelection(out long selectionStart, out long selectionLength)
        {
            selectionStart = this.hbPacketData.SelectionStart;
            selectionLength = this.hbPacketData.SelectionLength;
            IByteProvider provider = this.hbPacketData.ByteProvider;

            if (provider == null ||
                selectionStart < 0 ||
                selectionStart >= provider.Length)
            {
                return false;
            }

            if (selectionLength == 0)
            {
                selectionLength = 1;
            }

            return selectionLength > 0 &&
                selectionLength <= provider.Length - selectionStart;
        }

        private bool TryGetByteSweepPairConfiguration(bool showMessage)
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            bool valid = provider != null && provider.Length > 0 &&
                this.nudByteSweepLoopCount.Value >= 1 &&
                this.nudByteSweepInterval.Value >= 0 &&
                this.nudByteSweepNextInterval.Value >= 0 &&
                this.nudByteSweepFirstPosition.Value >= 0 &&
                this.nudByteSweepFirstPosition.Value < provider.Length &&
                this.nudByteSweepFirstLength.Value >= 1 &&
                this.nudByteSweepFirstLength.Value <= 255 &&
                this.nudByteSweepFirstInterval.Value >= 0 &&
                this.nudByteSweepSecondPosition.Value >= 0 &&
                this.nudByteSweepSecondPosition.Value < provider.Length &&
                this.nudByteSweepFirstPosition.Value != this.nudByteSweepSecondPosition.Value &&
                this.nudByteSweepSecondLength.Value >= 1 &&
                this.nudByteSweepSecondLength.Value <= 255 &&
                this.nudByteSweepSecondInterval.Value >= 0;
            if (!valid && showMessage)
            {
                MessageBox.Show(
                    this,
                    UiText("ByteSweep_ParameterInvalid") + Environment.NewLine +
                        UiText("ByteSweep_PairInvalid"),
                    UiText("ByteSweep_PairTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return valid;
        }

        private static void SetByteSweepNumber(NumericUpDown control, int value)
        {
            if (control == null) return;
            control.Value = Math.Min(control.Maximum, Math.Max(control.Minimum, value));
        }

        private int GetEffectiveSendSocket()
        {
            if ((this.savedSendPreset != null || this.savedByteSweepPreset != null) &&
                this.resolvedCurrentRoute == null)
            {
                this.TryResolveCurrentRoute(false);
            }

            return this.resolvedCurrentRoute != null
                ? this.resolvedCurrentRoute.Socket
                : (int)this.nudSendSocket_Socket.Value;
        }

        private bool TryResolveCurrentRoute(bool showMessage)
        {
            if (this.savedSendPreset == null && this.savedByteSweepPreset == null)
            {
                this.resolvedCurrentRoute = null;
                return true;
            }

            Socket_Cache.SocketList.CurrentSocketRouteResolution resolution =
                Socket_Cache.SocketList.ResolveCurrentRoute(this.SPI);
            Guid presetId = this.savedSendPreset != null
                ? this.savedSendPreset.SID
                : this.savedByteSweepPreset == null
                    ? Guid.Empty
                    : this.savedByteSweepPreset.BID;
            Socket_Cache.SocketList.LogCurrentRouteResolution(
                presetId,
                1,
                this.SPI,
                resolution);
            if (!resolution.Succeeded)
            {
                this.resolvedCurrentRoute = null;
                if (showMessage)
                {
                    string messageKey = resolution.ErrorCode == "runtime_route_ambiguous"
                        ? "UI_CurrentSocketAmbiguous"
                        : "UI_CurrentSocketRequired";
                    string message = UiText(messageKey);
                    if (!string.IsNullOrWhiteSpace(resolution.ErrorMessage))
                    {
                        message += Environment.NewLine + resolution.ErrorMessage;
                    }
                    Socket_Operation.ShowMessageBox(message);
                }
                return false;
            }

            this.resolvedCurrentRoute = resolution.Route;
            this.txtIPFrom.Text = resolution.Route.PacketFrom;
            this.txtIPTo.Text = resolution.Route.PacketTo;
            return true;
        }

        private void bgwSendPacket_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            try
            {
                SendWorkItem workItem = e.Argument as SendWorkItem;
                if (workItem == null)
                {
                    return;
                }

                if (workItem.ByteSweep)
                {
                    if (!this.ExecuteByteSweep(workItem))
                    {
                        e.Cancel = true;
                    }
                }
                else if (workItem.Continuously)
                {
                    int iSendCount = 0;
                    while (!this.IsSendCancellationRequested())
                    {
                        this.DoSendPacket(workItem, iSendCount);
                        iSendCount++;

                        if (!this.WaitForNextSend(workItem.Interval))
                        {
                            e.Cancel = true;
                            return;
                        }
                    }

                    e.Cancel = true;
                }
                else
                {
                    for (int i = 0; i < workItem.Times; i++)
                    {
                        if (this.IsSendCancellationRequested())
                        {
                            e.Cancel = true;
                            return;
                        }

                        this.DoSendPacket(workItem, i);

                        if (i < workItem.Times - 1 && !this.WaitForNextSend(workItem.Interval))
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        private void bgwSendPacket_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            try
            {
                this.UpdateSendCounterLabels();

                this.SetSendRunningState(false);

                if (this.byteSweepWasRunning)
                {
                    this.RestoreByteSweepVisualState();
                    string completionText;

                    if (e.Error != null)
                    {
                        Socket_Operation.DoLog(
                            MethodBase.GetCurrentMethod().Name,
                            e.Error.Message);
                        completionText = this.byteSweepPairWasRunning
                            ? string.Format(
                                UiText("ByteSweep_PairError"),
                                e.Error.Message)
                            : string.Format(
                                MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_238),
                                e.Error.Message);
                    }
                    else if (e.Cancelled)
                    {
                        completionText = this.byteSweepPairWasRunning
                            ? UiText("ByteSweep_PairStopped")
                            : MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_237);
                    }
                    else if (this.byteSweepPairWasRunning)
                    {
                        completionText = string.Format(
                            UiText("ByteSweep_PairCompleted"),
                            this.byteSweepPairFirstLength,
                            this.byteSweepPairSecondLength,
                            this.byteSweepRunLoopCount,
                            Interlocked.Read(ref this.Send_CNT));
                    }
                    else
                    {
                        long byteCount = this.byteSweepProcessedLength;
                        completionText = string.Format(
                            MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_236),
                            byteCount,
                            Interlocked.Read(ref this.Send_CNT));
                    }
                    this.tlByteSweepProgress.Text = completionText;
                    if (this.byteSweepJobId != Guid.Empty)
                    {
                        Socket_ByteSweepRuntime.Current.Finish(
                            this.byteSweepJobId,
                            e.Cancelled,
                            e.Error,
                            completionText);
                    }
                    if (this.byteSweepEditorPanel != null)
                    {
                        this.byteSweepEditorPanel.SetOperationState(
                            false,
                            false,
                            false,
                            completionText);
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
            finally
            {
                this.byteSweepWasRunning = false;
                this.byteSweepPairWasRunning = false;
                this.byteSweepProcessedLength = 0;
                this.byteSweepPairFirstLength = 0;
                this.byteSweepPairSecondLength = 0;
                this.byteSweepRunLoopCount = 0;
                this.byteSweepPlannedTotal = 0;
                this.byteSweepStartedFromEditorPanel = false;
                this.byteSweepJobId = Guid.Empty;
                this.byteSweepRuntimeExternalRunning = false;
                if (this.cts != null)
                {
                    this.cts.Dispose();
                    this.cts = null;
                }
                if (this.byteSweepPauseGate != null)
                {
                    this.byteSweepPauseGate.Set();
                    this.byteSweepPauseGate.Dispose();
                    this.byteSweepPauseGate = null;
                }
                this.byteSweepPaused = false;
            }
        }

        private void DoSendPacket(SendWorkItem workItem, int SendCount)
        {
            try
            {
                if (workItem.ProgressionEnabled)
                {
                    int iCarryCount = 0;
                    int iIndex = workItem.ProgressionPosition;
                    int iStep = workItem.ProgressionStep;

                    byte bValue = workItem.Buffer[iIndex];
                    bValue = Socket_Operation.GetStepByte(bValue, iStep, out iCarryCount);
                    workItem.Buffer[iIndex] = bValue;

                    if (workItem.ProgressionCarryEnabled && iCarryCount > 0)
                    {
                        for (int i = 0; i < workItem.ProgressionCarryCount; i++)
                        {
                            int iIndexPre = iIndex - (i + 1);

                            if (iIndexPre > -1)
                            {
                                byte bValuePrev = workItem.Buffer[iIndexPre];
                                bValuePrev = Socket_Operation.GetStepByte(bValuePrev, iCarryCount, out iCarryCount);
                                workItem.Buffer[iIndexPre] = bValuePrev;

                                if (iCarryCount == 0)
                                {
                                    break;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }

                this.SendPacketBuffer(workItem, workItem.Buffer);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void SendPacketBuffer(SendWorkItem workItem, byte[] buffer)
        {
            try
            {
                bool bSendOK = Socket_Operation.SendPacket(
                    workItem.Socket,
                    this.SPI.PacketType,
                    workItem.IPFrom,
                    workItem.IPTo,
                    buffer);

                if (bSendOK)
                {
                    Interlocked.Increment(ref this.Send_Success);
                }
                else
                {
                    Interlocked.Increment(ref this.Send_Fail);
                }

                Interlocked.Increment(ref this.Send_CNT);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref this.Send_Fail);
                Interlocked.Increment(ref this.Send_CNT);
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
            finally
            {
                this.PostSendCounterUpdate();
            }
        }

        private void PostSendCounterUpdate()
        {
            if (this.IsDisposed || !this.IsHandleCreated ||
                Interlocked.CompareExchange(ref this.sendCounterUpdateScheduled, 1, 0) != 0)
            {
                return;
            }

            try
            {
                this.BeginInvoke((Action)(() =>
                {
                    Interlocked.Exchange(ref this.sendCounterUpdateScheduled, 0);
                    if (!this.IsDisposed)
                    {
                        this.UpdateSendCounterLabels();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref this.sendCounterUpdateScheduled, 0);
            }
        }

        private void UpdateSendCounterLabels()
        {
            this.tlSendTimes_Value.Text = Interlocked.Read(ref this.Send_CNT).ToString();
            this.tlSend_Success_Value.Text = Interlocked.Read(ref this.Send_Success).ToString();
            this.tlSend_Fail_Value.Text = Interlocked.Read(ref this.Send_Fail).ToString();
        }

        private bool ExecuteByteSweep(SendWorkItem workItem)
        {
            CancellationToken token = this.cts == null ? CancellationToken.None : this.cts.Token;
            WaitHandle pauseHandle = this.byteSweepPauseGate == null
                ? null
                : this.byteSweepPauseGate.WaitHandle;
            if (workItem.PairCombination)
            {
                Socket_ByteSweepResult pairResult = ExecuteByteSweepPairLoopsWithPause(
                    workItem.Buffer,
                    workItem.PairFirstPosition,
                    workItem.PairFirstLength,
                    workItem.PairFirstInterval,
                    workItem.PairSecondPosition,
                    workItem.PairSecondLength,
                    workItem.PairSecondInterval,
                    Math.Max(1, workItem.Times),
                    buffer => Socket_Operation.SendPacket(
                        workItem.Socket,
                        this.SPI.PacketType,
                        workItem.IPFrom,
                        workItem.IPTo,
                        buffer),
                    token,
                    pauseHandle,
                    progress =>
                    {
                        Interlocked.Exchange(ref this.Send_CNT, progress.TotalSend);
                        Interlocked.Exchange(ref this.Send_Success, progress.Success);
                        Interlocked.Exchange(ref this.Send_Fail, progress.Failure);
                        this.PostByteSweepPairProgress(progress);
                    });
                Interlocked.Exchange(ref this.Send_CNT, pairResult.TotalSend);
                Interlocked.Exchange(ref this.Send_Success, pairResult.Success);
                Interlocked.Exchange(ref this.Send_Fail, pairResult.Failure);
                return !pairResult.Cancelled;
            }

            Socket_ByteSweepResult result = ExecuteByteSweepLoopsWithPause(
                workItem.Buffer,
                workItem.SweepStart,
                workItem.SweepLength,
                workItem.Interval,
                Math.Max(1, workItem.Times),
                buffer => Socket_Operation.SendPacket(
                    workItem.Socket,
                    this.SPI.PacketType,
                    workItem.IPFrom,
                    workItem.IPTo,
                    buffer),
                token,
                pauseHandle,
                progress =>
                {
                    Interlocked.Exchange(ref this.Send_CNT, progress.TotalSend);
                    Interlocked.Exchange(ref this.Send_Success, progress.Success);
                    Interlocked.Exchange(ref this.Send_Fail, progress.Failure);
                    this.PostByteSweepProgress(
                        progress.Position,
                        progress.OriginalValue,
                        progress.CurrentValue,
                        progress.ByteNumber,
                        progress.ByteCount,
                        progress.ValueNumber,
                        progress.ValueNumber == 1);
                });
            Interlocked.Exchange(ref this.Send_CNT, result.TotalSend);
            Interlocked.Exchange(ref this.Send_Success, result.Success);
            Interlocked.Exchange(ref this.Send_Fail, result.Failure);
            return !result.Cancelled;
        }

        private static Socket_ByteSweepResult ExecuteByteSweepPairLoops(
            byte[] buffer,
            int firstPosition,
            int firstLength,
            int firstInterval,
            int secondPosition,
            int secondLength,
            int secondInterval,
            int loopCount,
            Func<byte[], bool> send,
            CancellationToken token,
            Action<Socket_ByteSweepProgress> reportProgress)
        {
            return ExecuteByteSweepPairLoopsWithPause(
                buffer,
                firstPosition,
                firstLength,
                firstInterval,
                secondPosition,
                secondLength,
                secondInterval,
                loopCount,
                send,
                token,
                null,
                reportProgress);
        }

        private static Socket_ByteSweepResult ExecuteByteSweepPairLoopsWithPause(
            byte[] buffer,
            int firstPosition,
            int firstLength,
            int firstInterval,
            int secondPosition,
            int secondLength,
            int secondInterval,
            int loopCount,
            Func<byte[], bool> send,
            CancellationToken token,
            WaitHandle pauseHandle,
            Action<Socket_ByteSweepProgress> reportProgress)
        {
            Socket_ByteSweepResult combined = new Socket_ByteSweepResult();
            for (int loop = 0; loop < loopCount; loop++)
            {
                if (token.IsCancellationRequested)
                {
                    combined.Cancelled = true;
                    break;
                }

                long completedSend = combined.TotalSend;
                long completedSuccess = combined.Success;
                long completedFailure = combined.Failure;
                Socket_ByteSweepResult current = Socket_ByteSweepEngine.ExecutePairCombination(
                    buffer,
                    firstPosition,
                    firstLength,
                    firstInterval,
                    secondPosition,
                    secondLength,
                    secondInterval,
                    send,
                    token,
                    progress =>
                    {
                        progress.TotalSend += completedSend;
                        progress.Success += completedSuccess;
                        progress.Failure += completedFailure;
                        reportProgress?.Invoke(progress);
                    },
                    pauseHandle);
                combined.TotalSend += current.TotalSend;
                combined.Success += current.Success;
                combined.Failure += current.Failure;
                if (current.Cancelled)
                {
                    combined.Cancelled = true;
                    break;
                }
            }
            return combined;
        }

        private static Socket_ByteSweepResult ExecuteByteSweepLoops(
            byte[] buffer,
            int start,
            int length,
            int interval,
            int loopCount,
            Func<byte[], bool> send,
            CancellationToken token,
            Action<Socket_ByteSweepProgress> reportProgress)
        {
            return ExecuteByteSweepLoopsWithPause(
                buffer,
                start,
                length,
                interval,
                loopCount,
                send,
                token,
                null,
                reportProgress);
        }

        private static Socket_ByteSweepResult ExecuteByteSweepLoopsWithPause(
            byte[] buffer,
            int start,
            int length,
            int interval,
            int loopCount,
            Func<byte[], bool> send,
            CancellationToken token,
            WaitHandle pauseHandle,
            Action<Socket_ByteSweepProgress> reportProgress)
        {
            Socket_ByteSweepResult combined = new Socket_ByteSweepResult();
            for (int loop = 0; loop < loopCount; loop++)
            {
                if (token.IsCancellationRequested)
                {
                    combined.Cancelled = true;
                    break;
                }

                long completedSend = combined.TotalSend;
                long completedSuccess = combined.Success;
                long completedFailure = combined.Failure;
                Socket_ByteSweepResult current = Socket_ByteSweepEngine.Execute(
                    buffer,
                    start,
                    length,
                    interval,
                    send,
                    token,
                    progress =>
                    {
                        progress.TotalSend += completedSend;
                        progress.Success += completedSuccess;
                        progress.Failure += completedFailure;
                        reportProgress?.Invoke(progress);
                    },
                    pauseHandle);

                combined.TotalSend += current.TotalSend;
                combined.Success += current.Success;
                combined.Failure += current.Failure;
                if (current.Cancelled)
                {
                    combined.Cancelled = true;
                    break;
                }
            }

            return combined;
        }

        private bool IsSendCancellationRequested()
        {
            return this.bgwSendPacket.CancellationPending ||
                (this.cts != null && this.cts.IsCancellationRequested);
        }

        private bool WaitForNextSend(int interval)
        {
            if (this.IsSendCancellationRequested())
            {
                return false;
            }

            if (interval <= 0)
            {
                return true;
            }

            CancellationTokenSource currentCts = this.cts;
            return currentCts == null || !currentCts.Token.WaitHandle.WaitOne(interval);
        }

        private void PostByteSweepProgress(
            int position,
            byte originalValue,
            byte currentValue,
            int byteNumber,
            int byteCount,
            int valueNumber,
            bool moveCursor)
        {
            long perLoopTotal = Math.Max(1L, (long)this.byteSweepProcessedLength * 255L);
            int currentLoop = Math.Min(
                Math.Max(1, this.byteSweepRunLoopCount),
                (int)(((Math.Max(1L, this.Send_CNT) - 1L) / perLoopTotal) + 1L));
            Socket_ByteSweepRuntime.Current.PublishProgress(
                this.byteSweepJobId,
                this.savedByteSweepPreset == null ? Guid.Empty : this.savedByteSweepPreset.BID,
                this.savedByteSweepPreset == null
                    ? UiText("UI_CurrentPacketUnsaved")
                    : this.savedByteSweepPreset.BName,
                UiText("ByteSweep_SequentialMode"),
                new Socket_ByteSweepProgress
                {
                    Position = position,
                    OriginalValue = originalValue,
                    CurrentValue = currentValue,
                    ByteNumber = byteNumber,
                    ByteCount = byteCount,
                    ValueNumber = valueNumber,
                    TotalSend = Interlocked.Read(ref this.Send_CNT),
                    Success = Interlocked.Read(ref this.Send_Success),
                    Failure = Interlocked.Read(ref this.Send_Fail)
                },
                currentLoop,
                Math.Max(1, this.byteSweepRunLoopCount),
                this.byteSweepPlannedTotal);
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                int displayGeneration = Volatile.Read(ref this.byteSweepLiveDisplayGeneration);
                this.BeginInvoke((Action)(() =>
                {
                    if (this.IsDisposed ||
                        !this.byteSweepLiveDisplayEnabled ||
                        displayGeneration != this.byteSweepLiveDisplayGeneration)
                    {
                        return;
                    }

                    this.UpdateByteSweepLiveDisplay(
                        position,
                        originalValue,
                        currentValue);
                    if (moveCursor && this.hbPacketData.ByteProvider != null &&
                        position >= 0 && position < this.hbPacketData.ByteProvider.Length)
                    {
                        this.SetByteSweepLiveSelection(position, 1);
                    }

                    string text = string.Format(
                        MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_235),
                        position,
                        originalValue,
                        currentValue,
                        byteNumber,
                        byteCount,
                        valueNumber);
                    this.tlByteSweepProgress.Text = text;
                    this.tlByteSweepProgress.Visible = true;
                    if (this.byteSweepEditorPanel != null)
                    {
                        this.byteSweepEditorPanel.SetOperationState(
                            true,
                            this.byteSweepPaused,
                            this.byteSweepStartedFromEditorPanel,
                            text);
                    }
                    this.UpdateSendCounterLabels();
                }));
            }
            catch (InvalidOperationException)
            {
                // 窗口关闭过程中句柄可能已销毁，无需再更新界面。
            }
        }

        private void PostByteSweepPairProgress(Socket_ByteSweepProgress progress)
        {
            long runtimePerLoopTotal = Math.Max(
                1L,
                (long)this.byteSweepPairFirstLength * this.byteSweepPairSecondLength);
            int runtimeCurrentLoop = Math.Min(
                Math.Max(1, this.byteSweepRunLoopCount),
                (int)(((Math.Max(1L, progress.TotalSend) - 1L) / runtimePerLoopTotal) + 1L));
            Socket_ByteSweepRuntime.Current.PublishProgress(
                this.byteSweepJobId,
                this.savedByteSweepPreset == null ? Guid.Empty : this.savedByteSweepPreset.BID,
                this.savedByteSweepPreset == null
                    ? UiText("UI_CurrentPacketUnsaved")
                    : this.savedByteSweepPreset.BName,
                UiText("ByteSweep_PairMode"),
                progress,
                runtimeCurrentLoop,
                Math.Max(1, this.byteSweepRunLoopCount),
                this.byteSweepPlannedTotal);
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                int displayGeneration = Volatile.Read(ref this.byteSweepLiveDisplayGeneration);
                this.BeginInvoke((Action)(() =>
                {
                    if (this.IsDisposed ||
                        !this.byteSweepLiveDisplayEnabled ||
                        displayGeneration != this.byteSweepLiveDisplayGeneration)
                    {
                        return;
                    }

                    long perLoopTotal = Math.Max(
                        1L,
                        (long)this.byteSweepPairFirstLength *
                        this.byteSweepPairSecondLength);
                    long plannedTotal = perLoopTotal *
                        Math.Max(1, this.byteSweepRunLoopCount);
                    int currentLoop = Math.Min(
                        Math.Max(1, this.byteSweepRunLoopCount),
                        (int)(((Math.Max(1L, progress.TotalSend) - 1L) /
                            perLoopTotal) + 1L));
                    string text = string.Format(
                        UiText("ByteSweep_PairProgress"),
                        currentLoop,
                        Math.Max(1, this.byteSweepRunLoopCount),
                        progress.PairFirstValue,
                        progress.PairFirstValueNumber,
                        progress.PairFirstValueCount,
                        progress.PairSecondValue,
                        progress.PairSecondValueNumber,
                        progress.PairSecondValueCount,
                        progress.TotalSend,
                        plannedTotal);
                    this.UpdateByteSweepLivePairDisplay(progress);
                    this.SetByteSweepLiveSelection(progress.PairFirstPosition, 1);
                    this.tlByteSweepProgress.Visible = true;
                    this.tlByteSweepProgress.Text = text;
                    if (this.byteSweepEditorPanel != null)
                    {
                        this.byteSweepEditorPanel.SetOperationState(
                            true,
                            this.byteSweepPaused,
                            this.byteSweepStartedFromEditorPanel,
                            text);
                    }
                    this.UpdateSendCounterLabels();
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void BeginByteSweepLiveDisplay()
        {
            this.EndByteSweepLiveDisplay();
            Interlocked.Increment(ref this.byteSweepLiveDisplayGeneration);
            IByteProvider provider = this.hbPacketData.ByteProvider;
            this.byteSweepProviderHadChanges =
                provider != null && provider.HasChanges();
            this.byteSweepLiveDisplayEnabled = true;
        }

        private void SetByteSweepLiveSelection(long start, long length)
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (!this.byteSweepWasRunning ||
                provider == null ||
                provider.Length == 0 ||
                start < 0 ||
                start >= provider.Length)
            {
                return;
            }

            this.byteSweepLiveSelectionStart = start;
            this.byteSweepLiveSelectionLength = Math.Min(
                Math.Max(1, length),
                provider.Length - start);
            this.byteSweepSelectionGuard = true;
            try
            {
                this.hbPacketData.Select(
                    this.byteSweepLiveSelectionStart,
                    this.byteSweepLiveSelectionLength);
                this.hbPacketData.ScrollByteIntoView(this.byteSweepLiveSelectionStart);
            }
            finally
            {
                this.byteSweepSelectionGuard = false;
            }
        }

        private void KeepByteSweepLiveSelection()
        {
            if (!this.byteSweepWasRunning || this.byteSweepSelectionGuard)
            {
                return;
            }

            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (provider == null || provider.Length == 0)
            {
                return;
            }

            long start = this.byteSweepLiveSelectionStart >= 0
                ? this.byteSweepLiveSelectionStart
                : Math.Min(
                    Math.Max(this.byteSweepOriginalSelectionStart, 0),
                    provider.Length - 1);
            start = Math.Min(Math.Max(start, 0), provider.Length - 1);
            long length = Math.Min(
                Math.Max(1, this.byteSweepLiveSelectionLength),
                provider.Length - start);
            if (this.hbPacketData.SelectionStart == start &&
                this.hbPacketData.SelectionLength == length)
            {
                return;
            }

            this.byteSweepSelectionGuard = true;
            try
            {
                this.hbPacketData.Select(start, length);
                this.hbPacketData.ScrollByteIntoView(start);
            }
            finally
            {
                this.byteSweepSelectionGuard = false;
            }
        }

        private void UpdateByteSweepLiveDisplay(
            long position,
            byte originalValue,
            byte currentValue)
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (!this.byteSweepLiveDisplayEnabled ||
                provider == null ||
                !provider.SupportsWriteByte() ||
                position < 0 ||
                position >= provider.Length)
            {
                return;
            }

            if (this.byteSweepLiveValueActive && this.byteSweepLivePosition != position)
            {
                this.RestoreByteSweepDisplayedValue();
            }

            if (!this.byteSweepLiveValueActive)
            {
                this.byteSweepLivePosition = position;
                this.byteSweepLiveOriginalValue = originalValue;
                this.byteSweepLiveValueActive = true;
            }

            provider.WriteByte(position, currentValue);
            this.HexBox_LinePositionChanged();
        }

        private void UpdateByteSweepLivePairDisplay(Socket_ByteSweepProgress progress)
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (!this.byteSweepLiveDisplayEnabled ||
                provider == null ||
                !provider.SupportsWriteByte())
            {
                return;
            }

            if (!this.byteSweepLiveValueActive || this.byteSweepLivePosition != progress.PairFirstPosition)
            {
                this.RestoreByteSweepDisplayedValue();
                this.byteSweepLivePosition = progress.PairFirstPosition;
                this.byteSweepLiveOriginalValue = provider.ReadByte(progress.PairFirstPosition);
                this.byteSweepLiveValueActive = true;
            }

            if (!this.byteSweepLiveSecondValueActive || this.byteSweepLiveSecondPosition != progress.PairSecondPosition)
            {
                if (this.byteSweepLiveSecondValueActive &&
                    this.byteSweepLiveSecondPosition >= 0 &&
                    this.byteSweepLiveSecondPosition < provider.Length)
                {
                    provider.WriteByte(this.byteSweepLiveSecondPosition, this.byteSweepLiveSecondOriginalValue);
                }
                this.byteSweepLiveSecondPosition = progress.PairSecondPosition;
                this.byteSweepLiveSecondOriginalValue = provider.ReadByte(progress.PairSecondPosition);
                this.byteSweepLiveSecondValueActive = true;
            }

            provider.WriteByte(progress.PairFirstPosition, progress.PairFirstValue);
            provider.WriteByte(progress.PairSecondPosition, progress.PairSecondValue);
            this.HexBox_LinePositionChanged();
        }

        private void EndByteSweepLiveDisplay()
        {
            // Live preview writes temporary values through the byte provider.
            // Restoring those values must not turn a clean editor into an
            // unsaved editor, otherwise closing a preview-only window can
            // unexpectedly open the unsaved-changes dialog.
            bool editorDirtyBeforeRestore = this.byteSweepEditorDirty;
            bool liveDisplayWasActive =
                this.byteSweepLiveDisplayEnabled ||
                this.byteSweepLiveValueActive ||
                this.byteSweepLiveSecondValueActive;
            this.byteSweepRestoringLiveDisplay = true;
            try
            {
                this.byteSweepLiveDisplayEnabled = false;
                Interlocked.Increment(ref this.byteSweepLiveDisplayGeneration);
                this.RestoreByteSweepDisplayedValue();
                IByteProvider provider = this.hbPacketData.ByteProvider;
                if (liveDisplayWasActive &&
                    provider != null &&
                    !this.byteSweepProviderHadChanges)
                {
                    provider.ApplyChanges();
                }
            }
            finally
            {
                this.byteSweepRestoringLiveDisplay = false;
                this.byteSweepEditorDirty = editorDirtyBeforeRestore;
            }
            this.byteSweepProviderHadChanges = false;
            this.byteSweepLiveSelectionStart = -1;
            this.byteSweepLiveSelectionLength = 1;
        }

        private void RestoreByteSweepDisplayedValue()
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (this.byteSweepLiveValueActive &&
                provider != null &&
                provider.SupportsWriteByte() &&
                this.byteSweepLivePosition >= 0 &&
                this.byteSweepLivePosition < provider.Length)
            {
                provider.WriteByte(
                    this.byteSweepLivePosition,
                    this.byteSweepLiveOriginalValue);
                this.HexBox_LinePositionChanged();
            }

            this.byteSweepLiveValueActive = false;
            this.byteSweepLivePosition = -1;
            IByteProvider secondProvider = this.hbPacketData.ByteProvider;
            if (this.byteSweepLiveSecondValueActive &&
                secondProvider != null &&
                secondProvider.SupportsWriteByte() &&
                this.byteSweepLiveSecondPosition >= 0 &&
                this.byteSweepLiveSecondPosition < secondProvider.Length)
            {
                secondProvider.WriteByte(
                    this.byteSweepLiveSecondPosition,
                    this.byteSweepLiveSecondOriginalValue);
            }
            this.byteSweepLiveSecondValueActive = false;
            this.byteSweepLiveSecondPosition = -1;
        }

        private void RestoreByteSweepVisualState()
        {
            bool restoreSelection =
                this.byteSweepWasRunning ||
                this.byteSweepHighlightActive ||
                this.byteSweepLiveDisplayEnabled ||
                this.byteSweepLiveValueActive;

            this.EndByteSweepLiveDisplay();
            this.RestoreByteSweepHighlight();
            if (restoreSelection)
            {
                this.RestoreByteSweepSelection();
            }
        }

        private void RestoreByteSweepSelection()
        {
            if (this.hbPacketData.ByteProvider == null || this.hbPacketData.ByteProvider.Length == 0)
            {
                return;
            }

            long start = Math.Min(
                Math.Max(this.byteSweepOriginalSelectionStart, 0),
                this.hbPacketData.ByteProvider.Length - 1);
            long length = Math.Min(
                Math.Max(this.byteSweepOriginalSelectionLength, 0),
                this.hbPacketData.ByteProvider.Length - start);

            this.byteSweepSelectionGuard = true;
            try
            {
                this.hbPacketData.Select(start, length);
                this.hbPacketData.ScrollByteIntoView(start);
            }
            finally
            {
                this.byteSweepSelectionGuard = false;
            }
        }

        private void BeginByteSweepHighlight()
        {
            if (this.byteSweepHighlightActive)
            {
                return;
            }

            this.byteSweepOriginalSelectionBackColor = this.hbPacketData.SelectionBackColor;
            this.byteSweepOriginalSelectionForeColor = this.hbPacketData.SelectionForeColor;
            this.hbPacketData.SelectionBackColor = System.Drawing.Color.Gold;
            this.hbPacketData.SelectionForeColor = System.Drawing.Color.Black;
            this.byteSweepHighlightActive = true;
        }

        private void RestoreByteSweepHighlight()
        {
            if (!this.byteSweepHighlightActive)
            {
                return;
            }

            this.hbPacketData.SelectionBackColor = this.byteSweepOriginalSelectionBackColor;
            this.hbPacketData.SelectionForeColor = this.byteSweepOriginalSelectionForeColor;
            this.byteSweepHighlightActive = false;
        }

        #endregion

        #region//停止按钮

        private void bSendStop_Click(object sender, EventArgs e)
        {
            if (!this.byteSweepWasRunning)
            {
                this.StopSend();
            }
        }

        private void StopSend()
        {
            try
            {
                if (this.byteSweepWasRunning && this.byteSweepJobId != Guid.Empty)
                {
                    Socket_ByteSweepRuntime.Current.RequestStop(this.byteSweepJobId);
                }
                if (this.bgwSendPacket.IsBusy)
                {
                    if (this.cts != null)
                    {
                        this.cts.Cancel();
                    }
                    if (this.byteSweepPauseGate != null)
                    {
                        this.byteSweepPauseGate.Set();
                    }
                    
                    this.bgwSendPacket.CancelAsync();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//保存递进预设

        private void bSaveByteSweepPreset_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.savedByteSweepPreset == null)
                {
                    this.ShowByteSweepPresetDialog(null, null);
                }
                else
                {
                    this.UpdateCurrentByteSweepPreset();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        // 保存/更新后保持编辑窗口打开，便于继续调整；关闭由用户主动触发。
        private bool UpdateCurrentByteSweepPreset(bool closeAfterSave = false)
        {
            long selectionStart = 0;
            long selectionLength = 1;
            bool pairMode = this.IsPairCombinationMode();
            if (!pairMode && !this.TryGetByteSweepSelection(out selectionStart, out selectionLength))
            {
                Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_234));
                return false;
            }
            if (pairMode && !this.TryGetByteSweepPairConfiguration(true))
            {
                return false;
            }

            if (!this.ApplyCurrentPacketEdits())
            {
                return false;
            }

            Socket_ByteSweepPresetInfo value = this.savedByteSweepPreset.Clone();
            value.BStart = (int)selectionStart;
            value.BLength = (int)selectionLength;
            value.BLoopCount = (int)this.nudByteSweepLoopCount.Value;
            value.BInterval = (int)this.nudByteSweepInterval.Value;
            value.BNextInterval = (int)this.nudByteSweepNextInterval.Value;
            value.BMode = pairMode ? Socket_ByteSweepMode.PairCombination : Socket_ByteSweepMode.Sequential;
            value.BCombinationFirstPosition = (int)this.nudByteSweepFirstPosition.Value;
            value.BCombinationFirstInterval = (int)this.nudByteSweepFirstInterval.Value;
            value.BCombinationFirstLength = (int)this.nudByteSweepFirstLength.Value;
            value.BCombinationSecondPosition = (int)this.nudByteSweepSecondPosition.Value;
            value.BCombinationSecondInterval = (int)this.nudByteSweepSecondInterval.Value;
            value.BCombinationSecondLength = (int)this.nudByteSweepSecondLength.Value;
            value.PacketType = this.SPI.PacketType;
            value.PacketFrom = this.SPI.PacketFrom;
            value.PacketTo = this.SPI.PacketTo;
            value.Buffer = (byte[])this.SPI.PacketBuffer.Clone();
            value.ByteAnnotations =
                Socket_ByteAnnotationEngine.Clone(this.SPI.ByteAnnotations);

            if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                () => Socket_Cache.ByteSweepList.UpdatePreset(this.savedByteSweepPreset, value)))
            {
                Socket_Operation.ShowMessageBox(UiText("UI_PresetSaveFailed"));
                return false;
            }
            this.UpdateByteSweepPresetIdentity();
            this.ShowPresetSaveStatus(
                UiText("UI_SweepPresetUpdated"),
                this.savedByteSweepPreset.BFolder,
                this.savedByteSweepPreset.BName);
            this.byteSweepEditorDirty = false;
            if (closeAfterSave)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            return true;
        }

        private bool ShowByteSweepPresetDialog(string defaultName, string defaultFolder)
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (provider == null || provider.Length == 0)
            {
                Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_46));
                return false;
            }

            long selectionStart = 0;
            long selectionLength = 1;
            bool pairMode = this.IsPairCombinationMode();
            if (!pairMode && !this.TryGetByteSweepSelection(out selectionStart, out selectionLength))
            {
                Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_234));
                return false;
            }
            if (pairMode && !this.TryGetByteSweepPairConfiguration(true))
            {
                return false;
            }

            Socket_ByteSweepPresetInfo preset = new Socket_ByteSweepPresetInfo
            {
                BID = Guid.NewGuid(),
                BName = string.IsNullOrWhiteSpace(defaultName)
                    ? UiText("UI_DefaultSweepPresetName") +
                        (Socket_Cache.ByteSweepList.lstPresets.Count + 1)
                    : defaultName.Trim(),
                BFolder = string.IsNullOrWhiteSpace(defaultFolder)
                    ? Socket_Cache.ByteSweepList.lstFolders.FirstOrDefault() ?? string.Empty
                    : defaultFolder.Trim(),
                BStart = (int)selectionStart,
                BLength = (int)selectionLength,
                BLoopCount = (int)this.nudByteSweepLoopCount.Value,
                BInterval = (int)this.nudByteSweepInterval.Value,
                BNextInterval = (int)this.nudByteSweepNextInterval.Value,
                BMode = pairMode ? Socket_ByteSweepMode.PairCombination : Socket_ByteSweepMode.Sequential,
                BCombinationFirstPosition = (int)this.nudByteSweepFirstPosition.Value,
                BCombinationFirstInterval = (int)this.nudByteSweepFirstInterval.Value,
                BCombinationFirstLength = (int)this.nudByteSweepFirstLength.Value,
                BCombinationSecondPosition = (int)this.nudByteSweepSecondPosition.Value,
                BCombinationSecondInterval = (int)this.nudByteSweepSecondInterval.Value,
                BCombinationSecondLength = (int)this.nudByteSweepSecondLength.Value,
                PacketType = this.SPI.PacketType,
                PacketFrom = this.txtIPFrom.Text.Trim(),
                PacketTo = this.txtIPTo.Text.Trim(),
                Buffer = Socket_ByteAnnotationEngine.GetBytes(provider),
                ByteAnnotations = Socket_ByteAnnotationEngine.Clone(this.workingByteAnnotations)
            };

            using (Socket_ByteSweepPresetForm dialog =
                new Socket_ByteSweepPresetForm(preset, true))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    bool overwrote = false;
                    if (dialog.OverwriteConfirmed && dialog.OverwriteTargetId.HasValue)
                    {
                        Socket_ByteSweepPresetInfo conflict =
                            Socket_Cache.ByteSweepList.lstPresets.FirstOrDefault(item =>
                                item.BID == dialog.OverwriteTargetId.Value);
                        if (conflict != null)
                        {
                            if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                                () => Socket_Cache.ByteSweepList.UpdatePreset(conflict, dialog.Result)))
                            {
                                Socket_Operation.ShowMessageBox(UiText("UI_PresetSaveFailed"));
                                return false;
                            }

                            overwrote = true;
                        }
                    }

                    if (!overwrote &&
                        !Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                            () => Socket_Cache.ByteSweepList.AddPreset(dialog.Result)))
                    {
                        Socket_Operation.ShowMessageBox(UiText("UI_PresetSaveFailed"));
                        return false;
                    }
                    this.tlByteSweepProgress.Visible = true;
                    this.tlByteSweepProgress.Text = string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        UiText("UI_PresetSaved"),
                        dialog.Result.BFolder,
                        dialog.Result.BName);
                    this.byteSweepEditorDirty = false;
                    return true;
                }
            }

            return false;
        }

        private bool ConfirmByteSweepUnsavedChanges()
        {
            if (!this.byteSweepEditorDirty)
            {
                return true;
            }

            DialogResult result = MessageBox.Show(
                this,
                UiText("ByteSweep_UnsavedChanges"),
                UiText("ByteSweep_UnsavedTitle"),
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1);
            if (result == DialogResult.Cancel)
            {
                return false;
            }

            if (result == DialogResult.No)
            {
                return true;
            }

            return this.savedByteSweepPreset == null
                ? this.ShowByteSweepPresetDialog(null, null)
                : this.UpdateCurrentByteSweepPreset(false);
        }

        #endregion

        private static string UiText(string key)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? key;
        }

        #region//保存按钮

        private void bSave_Click(object sender, EventArgs e)
        {
            try
            {
                Socket_SendInfo containingPreset = Socket_Cache.SendList.lstSend.FirstOrDefault(item =>
                    item.SCollection != null &&
                    item.SCollection.Any(packet => ReferenceEquals(packet, this.SPI)));
                Socket_SendInfo existingPreset = containingPreset ?? this.savedSendPreset;
                string defaultName = existingPreset == null
                    ? string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        UiText("UI_DefaultSendPresetName"),
                        Socket_Cache.SendList.lstSend.Count + 1)
                    : existingPreset.SName;
                string defaultFolder = existingPreset == null
                    ? Socket_Cache.SendList.lstFolders.FirstOrDefault() ??
                        UiText("UI_DefaultSendGroup")
                    : existingPreset.SFolder;
                using (Socket_SendPresetForm dialog =
                    new Socket_SendPresetForm(
                        defaultName,
                        defaultFolder,
                        existingPreset == null ? (Guid?)null : existingPreset.SID,
                        true))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    if (dialog.SaveAsByteSweep)
                    {
                        this.ShowByteSweepPresetDialog(
                            dialog.PresetName,
                            dialog.FolderName);
                        return;
                    }

                    int loopCount = this.rbSendType_Continuously.Checked
                        ? 0
                        : (int)this.nudSendType_Times.Value;
                    string targetFolder = dialog.FolderName;
                    Socket_SendInfo committedPreset = null;
                    bool overwrote = false;
                    bool mutationValid = true;
                    bool saved = Socket_Cache.SendList.TryApplyListChangeAndSave(() =>
                    {
                        if (!this.ApplyCurrentPacketEdits())
                        {
                            mutationValid = false;
                            return;
                        }

                        targetFolder = Socket_Cache.SendList.lstFolders.FirstOrDefault(folder =>
                            string.Equals(folder, dialog.FolderName, StringComparison.OrdinalIgnoreCase));
                        if (targetFolder == null)
                        {
                            Socket_Cache.SendList.AddFolder(dialog.FolderName);
                            targetFolder = dialog.FolderName;
                        }

                        if (dialog.OverwriteConfirmed)
                        {
                            Socket_SendInfo conflict =
                                Socket_Cache.SendList.lstSend.FirstOrDefault(item =>
                                    item.SID == dialog.OverwriteTargetId);
                            if (conflict != null)
                            {
                                OverwriteSendPreset(
                                    conflict,
                                    this.SPI,
                                    dialog.PresetName,
                                    targetFolder,
                                    loopCount,
                                    (int)this.nudSendType_Interval.Value);
                                committedPreset = conflict;
                                overwrote = true;
                                return;
                            }
                        }

                        if (existingPreset != null)
                        {
                            if (this.savedSendPresetPacket != null &&
                                !ReferenceEquals(this.SPI, this.savedSendPresetPacket))
                            {
                                CopyPacket(this.SPI, this.savedSendPresetPacket);
                            }

                            bool folderChanged = !string.Equals(
                                existingPreset.SFolder,
                                targetFolder,
                                StringComparison.Ordinal);
                            existingPreset.SName = dialog.PresetName;
                            existingPreset.SFolder = targetFolder;
                            existingPreset.SLoopCNT = loopCount;
                            existingPreset.SLoopINT = (int)this.nudSendType_Interval.Value;
                            if (folderChanged)
                            {
                                existingPreset.SSortOrder = Socket_Cache.SendList.lstSend.Count(item =>
                                    !ReferenceEquals(item, existingPreset) &&
                                    string.Equals(
                                        item.SFolder,
                                        targetFolder,
                                        StringComparison.Ordinal)) + 1;
                            }

                            committedPreset = existingPreset;
                            return;
                        }

                        committedPreset = CreateSendPreset(
                            this.SPI,
                            dialog.PresetName,
                            targetFolder,
                            loopCount,
                            (int)this.nudSendType_Interval.Value);
                        Socket_Cache.SendList.SendToList(committedPreset);
                    });

                    if (!mutationValid || !saved || committedPreset == null)
                    {
                        Socket_Operation.ShowMessageBox(UiText("UI_PresetSaveFailed"));
                        return;
                    }

                    this.savedSendPreset = committedPreset;
                    this.savedSendPresetPacket = containingPreset == null
                        ? committedPreset.SCollection[0]
                        : this.SPI;
                    this.UpdatePresetIdentity(committedPreset);
                    this.ShowPresetSaveStatus(
                        existingPreset != null || overwrote
                            ? UiText("UI_SendPresetUpdated")
                            : UiText("UI_SendPresetSaved"),
                        targetFolder,
                        dialog.PresetName);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
            finally
            {
                HexBox_ManageAbility();
            }
        }

        private bool ApplyCurrentPacketEdits()
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (provider == null)
            {
                return false;
            }

            byte[] buffer = Socket_ByteAnnotationEngine.GetBytes(provider);
            this.SPI.PacketSocket = (int)this.nudSendSocket_Socket.Value;
            this.SPI.PacketFrom = this.txtIPFrom.Text.Trim();
            this.SPI.PacketTo = this.txtIPTo.Text.Trim();
            this.SPI.PacketBuffer = buffer;
            this.SPI.PacketData = Socket_Operation.GetPacketData_Hex(
                buffer.AsSpan(), Socket_Cache.SocketPacket.PacketData_MaxLen);
            this.SPI.PacketLen = buffer.Length;
            this.SPI.ByteAnnotations =
                Socket_ByteAnnotationEngine.Clone(this.workingByteAnnotations);
            this.SPI.VariableBindings = (this.workingVariableBindings ?? new List<PresetVariableBinding>())
                .Where(item => item != null)
                .Select(item => item.Clone())
                .ToList();
            provider.ApplyChanges();
            return true;
        }

        internal static Socket_SendInfo CreateSendPreset(
            Socket_PacketInfo packet,
            string name,
            string folder,
            int loopCount,
            int interval)
        {
            BindingList<Socket_PacketInfo> collection =
                new BindingList<Socket_PacketInfo>();
            Socket_PacketInfo packetCopy = new Socket_PacketInfo();
            CopyPacket(packet, packetCopy);
            collection.Add(packetCopy);

            int sortOrder = Socket_Cache.SendList.lstSend.Count(item =>
                string.Equals(item.SFolder, folder, StringComparison.Ordinal)) + 1;
            return new Socket_SendInfo(
                false,
                Guid.NewGuid(),
                name,
                true,
                Math.Max(0, loopCount),
                Math.Max(0, interval),
                collection,
                string.Empty,
                folder,
                sortOrder);
        }

        internal static void OverwriteSendPreset(
            Socket_SendInfo target,
            Socket_PacketInfo packet,
            string name,
            string folder,
            int loopCount,
            int interval)
        {
            bool folderChanged = !string.Equals(
                target.SFolder,
                folder,
                StringComparison.Ordinal);

            BindingList<Socket_PacketInfo> collection =
                new BindingList<Socket_PacketInfo>();
            Socket_PacketInfo packetCopy = new Socket_PacketInfo();
            CopyPacket(packet, packetCopy);
            collection.Add(packetCopy);

            target.SName = name;
            target.SFolder = folder;
            target.SLoopCNT = Math.Max(0, loopCount);
            target.SLoopINT = Math.Max(0, interval);
            target.SCollection = collection;
            if (folderChanged)
            {
                target.SSortOrder = Socket_Cache.SendList.lstSend.Count(item =>
                    !ReferenceEquals(item, target) &&
                    string.Equals(item.SFolder, folder, StringComparison.Ordinal)) + 1;
            }
        }

        private static void CopyPacket(Socket_PacketInfo source, Socket_PacketInfo target)
        {
            target.PacketTime = source.PacketTime;
            target.PacketSocket = source.PacketSocket;
            target.PacketType = source.PacketType;
            target.PacketFrom = source.PacketFrom;
            target.PacketTo = source.PacketTo;
            target.RawBuffer = source.RawBuffer == null
                ? null
                : (byte[])source.RawBuffer.Clone();
            target.PacketBuffer = source.PacketBuffer == null
                ? null
                : (byte[])source.PacketBuffer.Clone();
            target.PacketData = source.PacketData;
            target.PacketLen = source.PacketLen;
            target.FilterAction = source.FilterAction;
            target.ByteAnnotations =
                Socket_ByteAnnotationEngine.Clone(source.ByteAnnotations);
            target.VariableBindings = (source.VariableBindings ?? new List<PresetVariableBinding>())
                .Where(item => item != null)
                .Select(item => item.Clone())
                .ToList();
            target.SortOrder = source.SortOrder;
        }

        private void ShowPresetSaveStatus(string template, string folder, string name)
        {
            this.tlByteSweepProgress.Visible = true;
            this.tlByteSweepProgress.Text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                template,
                folder,
                name);
        }

        #endregion

        #region//右键菜单

        private void InitializeDynamicVariableMenu()
        {
            this.dynamicVariableMenuSeparator = new ToolStripSeparator
            {
                Name = "cmsHexBox_DynamicVariableSeparator"
            };
            this.replaceDynamicVariableMenuItem = new ToolStripMenuItem
            {
                Name = "cmsHexBox_ReplaceDynamicVariable",
                Text = "替换为动态变量"
            };
            this.editDynamicBindingMenuItem = new ToolStripMenuItem
            {
                Name = "cmsHexBox_EditDynamicBinding",
                Text = "修改变量绑定"
            };
            this.removeDynamicBindingMenuItem = new ToolStripMenuItem
            {
                Name = "cmsHexBox_RemoveDynamicBinding",
                Text = "取消变量绑定"
            };
            int index = this.cmsHexBox.Items.Count;
            this.cmsHexBox.Items.Insert(index, this.dynamicVariableMenuSeparator);
            this.cmsHexBox.Items.Insert(index + 1, this.replaceDynamicVariableMenuItem);
            this.cmsHexBox.Items.Insert(index + 2, this.editDynamicBindingMenuItem);
            this.cmsHexBox.Items.Insert(index + 3, this.removeDynamicBindingMenuItem);
        }

        private bool TryGetVariableBindingSelection(out int offset, out int length)
        {
            offset = 0;
            length = 0;
            IByteProvider provider = this.hbPacketData.ByteProvider;
            return provider != null && DynamicVariableRange.TryFromSelection(
                this.hbPacketData.SelectionStart,
                this.hbPacketData.SelectionLength,
                provider.Length,
                out offset,
                out length);
        }

        private void cmsHexBox_Opening(object sender, CancelEventArgs e)
        {
            Socket_Operation.InitSendListComboBox(this.tscbSendList);
            int offset;
            int length;
            bool valid = this.TryGetVariableBindingSelection(out offset, out length);
            PresetVariableBinding binding = valid
                ? (this.workingVariableBindings ?? new List<PresetVariableBinding>())
                    .FirstOrDefault(item => item != null && item.Offset == offset && item.Length == length)
                : null;
            this.replaceDynamicVariableMenuItem.Enabled = valid && !this.bgwSendPacket.IsBusy;
            this.editDynamicBindingMenuItem.Enabled = binding != null && !this.bgwSendPacket.IsBusy;
            this.removeDynamicBindingMenuItem.Enabled = binding != null && !this.bgwSendPacket.IsBusy;
        }

        private void tscbSendList_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (this.tscbSendList.SelectedItem != null)
                {
                    Socket_Cache.SendList.SendListItem item = (Socket_Cache.SendList.SendListItem)this.tscbSendList.SelectedItem;
                    Guid SID = item.SID;
                    BindingList<Socket_PacketInfo> SCollection = Socket_Cache.Send.GetSendCollection_ByGuid(SID);

                    if (SCollection != null)
                    {
                        int iSocket = (int)this.nudSendSocket_Socket.Value;
                        string sIPFrom = this.txtIPFrom.Text.Trim();
                        string sIPTo = this.txtIPTo.Text.Trim();

                        byte[] bBuffer = null;
                        long selectionStart = 0;
                        long selectionLength = 0;
                        bool hasSelection = this.hbPacketData.CanCopy();

                        if (hasSelection)
                        {
                            selectionStart = this.hbPacketData.SelectionStart;
                            selectionLength = this.hbPacketData.SelectionLength;
                            this.hbPacketData.CopyHex();
                            bBuffer = Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, Clipboard.GetText());
                        }
                        else
                        {
                            bBuffer = Socket_ByteAnnotationEngine.GetBytes(hbPacketData.ByteProvider);
                        }                        

                        Socket_Cache.Send.AddSendCollection(SCollection, iSocket, this.SPI.PacketType, sIPFrom, sIPTo, bBuffer,
                            Socket_ByteAnnotationEngine.ForSelection(
                                this.workingByteAnnotations,
                                hasSelection ? selectionStart : 0,
                                hasSelection ? selectionLength : bBuffer == null ? 0 : bBuffer.Length),
                            DynamicVariableSerialization.ForSelection(
                                this.workingVariableBindings,
                                hasSelection ? selectionStart : 0,
                                hasSelection ? selectionLength : bBuffer == null ? 0 : bBuffer.Length));
                    }                                       

                    this.cmsHexBox.Close();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void cmsHexBox_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            try
            {
                string sItemText = e.ClickedItem.Name;
                this.cmsHexBox.Close();

                byte[] bBuffer = Socket_ByteAnnotationEngine.GetBytes(hbPacketData.ByteProvider);

                switch (sItemText)
                {  
                    case "cmsHexBox_ReplaceDynamicVariable":
                        this.ChangeDynamicBinding(false);
                        break;

                    case "cmsHexBox_EditDynamicBinding":
                        this.ChangeDynamicBinding(true);
                        break;

                    case "cmsHexBox_RemoveDynamicBinding":
                        this.RemoveDynamicBinding();
                        break;

                    case "cmsHexBox_FilterList":

                        if (this.hbPacketData.CanCopy())
                        {
                            this.hbPacketData.CopyHex();

                            byte[] bBufferCopy = Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, Clipboard.GetText());
                            Socket_Cache.Filter.AddFilter_ByPacketInfo(this.SPI, bBufferCopy);
                        }
                        else
                        {
                            Socket_Cache.Filter.AddFilter_ByPacketInfo(this.SPI, bBuffer);
                        }

                        break;

                    case "cmsHexBox_SelectAll":

                        this.hbPacketData.SelectAll();

                        break;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }            
        }

        private void ChangeDynamicBinding(bool editExisting)
        {
            int offset;
            int length;
            if (!this.TryGetVariableBindingSelection(out offset, out length))
            {
                return;
            }
            this.SPI.VariableBindings = this.workingVariableBindings ?? new List<PresetVariableBinding>();
            bool changed = editExisting
                ? DynamicVariableUiActions.ChangeBinding(this, this.SPI, offset, length)
                : DynamicVariableUiActions.AddBinding(this, this.SPI, offset, length);
            if (changed)
            {
                this.workingVariableBindings = this.SPI.VariableBindings;
                this.byteAnnotationController.Refresh();
                this.RefreshDynamicBindingStyle();
            }
        }

        private void RemoveDynamicBinding()
        {
            int offset;
            int length;
            if (!this.TryGetVariableBindingSelection(out offset, out length))
            {
                return;
            }
            this.SPI.VariableBindings = this.workingVariableBindings ?? new List<PresetVariableBinding>();
            if (DynamicVariableUiActions.RemoveBinding(this, this.SPI, offset, length))
            {
                this.workingVariableBindings = this.SPI.VariableBindings;
                this.byteAnnotationController.Refresh();
                this.RefreshDynamicBindingStyle();
            }
        }

        private void RefreshDynamicBindingStyle()
        {
            List<DynamicVariableDisplayRange> ranges = new List<DynamicVariableDisplayRange>();
            foreach (PresetVariableBinding binding in this.workingVariableBindings ?? new List<PresetVariableBinding>())
            {
                DynamicVariableDefinition definition;
                if (binding != null && DynamicVariableRuntime.TryGetDefinition(binding.VariableId, out definition))
                {
                    ranges.Add(new DynamicVariableDisplayRange
                    {
                        Offset = binding.Offset,
                        Length = binding.Length,
                        VariableId = binding.VariableId,
                        Symbol = definition.Symbol,
                        DisplayName = definition.DisplayName,
                        RuleName = "发送预设绑定"
                    });
                }
            }
            this.hbPacketData.ByteStyleProvider = new DynamicVariableCompositeStyleProvider(
                this.workingByteAnnotations,
                ranges);
            this.UpdateDynamicVariablePreview();
            this.hbPacketData.Invalidate();
        }

        private void UpdateDynamicVariablePreview()
        {
            if (this.tlDynamicVariablePreview == null)
            {
                return;
            }
            byte[] buffer = this.hbPacketData == null || this.hbPacketData.ByteProvider == null
                ? null
                : Socket_ByteAnnotationEngine.GetBytes(this.hbPacketData.ByteProvider);
            List<string> parts = new List<string>();
            List<PresetVariableBinding> bindings = (this.workingVariableBindings ?? new List<PresetVariableBinding>())
                .Where(item => item != null)
                .OrderBy(item => item.Offset)
                .ToList();
            for (int index = 0; buffer != null && index < buffer.Length; index++)
            {
                PresetVariableBinding binding = bindings.FirstOrDefault(item =>
                    item.Offset == index && item.Length > 0 && item.End <= buffer.Length);
                if (binding != null)
                {
                    DynamicVariableDefinition definition;
                    string symbol = DynamicVariableRuntime.TryGetDefinition(binding.VariableId, out definition)
                        ? definition.Symbol
                        : binding.VariableId.ToString("N");
                    parts.Add("{" + symbol + "}");
                    index += binding.Length - 1;
                }
                else
                {
                    parts.Add(buffer[index].ToString("X2"));
                }
            }
            this.tlDynamicVariablePreview.Text = parts.Count == 0
                ? string.Empty
                : "模板：" + string.Join(" ", parts);
            this.tlDynamicVariablePreview.ToolTipText = this.tlDynamicVariablePreview.Text;
        }

        #endregion

        #region//封包编辑器

        #region//可用性

        private void hbPacketData_CurrentLineChanged(object sender, EventArgs e)
        {
            this.HexBox_LinePositionChanged();
        }

        private void hbPacketData_CurrentPositionInLineChanged(object sender, EventArgs e)
        {
            this.HexBox_LinePositionChanged();
        }

        private void hbPacketData_Copied(object sender, EventArgs e)
        {
            this.HexBox_ManageAbilityForCopyAndPaste();
        }

        private void hbPacketData_CopiedHex(object sender, EventArgs e)
        {
            this.HexBox_ManageAbilityForCopyAndPaste();
        }

        private void hbPacketData_SelectionLengthChanged(object sender, EventArgs e)
        {
            if (this.byteSweepWasRunning)
            {
                if (!this.byteSweepSelectionGuard)
                {
                    this.KeepByteSweepLiveSelection();
                }
                return;
            }

            this.HexBox_ManageAbilityForCopyAndPaste();
            this.SyncByteSweepEditorFromLegacy();
            this.SendTypeChanged();
        }

        private void hbPacketData_SelectionStartChanged(object sender, EventArgs e)
        {
            if (this.byteSweepWasRunning)
            {
                if (!this.byteSweepSelectionGuard)
                {
                    this.KeepByteSweepLiveSelection();
                }
                return;
            }

            this.HexBox_ManageAbilityForCopyAndPaste();
            this.SyncByteSweepEditorFromLegacy();
            this.SendTypeChanged();
        }

        private void ByteProvider_Changed(object sender, EventArgs e)
        {
            this.HexBox_ManageAbility();
            this.byteAnnotationController.Refresh();
            this.UpdateDynamicVariablePreview();
            this.UpdateByteSweepEditorByteValues();
            if (!this.byteSweepLiveDisplayEnabled &&
                !this.byteSweepWasRunning &&
                !this.byteSweepRestoringLiveDisplay)
            {
                this.byteSweepEditorDirty = true;
            }
        }

        private void ByteProvider_LengthChanged(object sender, EventArgs e)
        {
            this.HexBox_UpdatePacketLen();
            this.byteAnnotationController.Refresh();
            this.UpdateDynamicVariablePreview();
            this.UpdateByteSweepEditorByteValues();
            this.byteSweepEditorDirty = true;
        }

        private void UpdateByteSweepEditorByteValues()
        {
            if (this.byteSweepEditorPanel == null)
            {
                return;
            }

            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (provider == null)
            {
                this.byteSweepEditorPanel.SetCurrentByteValues(null, null);
                return;
            }

            byte? first = GetByteAtPosition(provider, this.nudByteSweepFirstPosition.Value);
            byte? second = GetByteAtPosition(provider, this.nudByteSweepSecondPosition.Value);
            this.byteSweepEditorPanel.SetCurrentByteValues(first, second);
        }

        private static byte? GetByteAtPosition(IByteProvider provider, decimal position)
        {
            long index = (long)position;
            return index >= 0 && index < provider.Length
                ? (byte?)provider.ReadByte(index)
                : null;
        }

        private void HexBox_UpdatePacketLen()
        {
            try
            {
                this.nudSendSocket_Len.Value = this.hbPacketData.ByteProvider.Length;                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void HexBox_LinePositionChanged()
        {
            try
            {
                int iSelectIndex = (int)hbPacketData.SelectionStart;
                this.nudProgressionPosition.Value = iSelectIndex;                  

                string sPacketDataPosition = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_24), hbPacketData.CurrentLine, hbPacketData.CurrentPositionInLine, iSelectIndex);

                string sBits_Value = string.Empty;
                string sChar_Value = string.Empty;
                string sByte_Value = string.Empty;
                string sShort_Value = string.Empty;
                string sUShort_Value = string.Empty;
                string sInt32_Value = string.Empty;
                string sUInt32_Value = string.Empty;
                string sInt64_Value = string.Empty;
                string sUInt64_Value = string.Empty;
                string sFloat_Value = string.Empty;
                string sDouble_Value = string.Empty;

                if (hbPacketData.ByteProvider != null && hbPacketData.ByteProvider.Length > hbPacketData.SelectionStart)
                {
                    byte bSelected = hbPacketData.ByteProvider.ReadByte(hbPacketData.SelectionStart);

                    Socket_BitInfo bitInfo = new Socket_BitInfo(bSelected, hbPacketData.SelectionStart);

                    if (bitInfo != null)
                    {
                        long start = hbPacketData.SelectionStart;
                        long selected = hbPacketData.SelectionLength;

                        if (selected == 0 || selected > 8)
                        {
                            selected = 8;
                        }

                        long last = hbPacketData.ByteProvider.Length;
                        long end = Math.Min(start + selected, last);

                        byte[] buffer64 = new byte[8];
                        int iBuffIndex = 0;

                        for (long i = start; i < end; i++)
                        {
                            buffer64[iBuffIndex] = hbPacketData.ByteProvider.ReadByte(i);
                            iBuffIndex++;
                        }

                        sBits_Value = bitInfo.ToString();
                        sChar_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Char, buffer64);
                        sByte_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Byte, buffer64);
                        sShort_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Short, buffer64);
                        sUShort_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UShort, buffer64);
                        sInt32_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Int32, buffer64);
                        sUInt32_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UInt32, buffer64);
                        sInt64_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Int64, buffer64);
                        sUInt64_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UInt64, buffer64);
                        sFloat_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Float, buffer64);
                        sDouble_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Double, buffer64);
                    }
                }

                this.lHexBox_Position.Text = sPacketDataPosition;

                this.lBits_Value.Text = sBits_Value;
                this.lChar_Value.Text = sChar_Value;
                this.lByte_Value.Text = sByte_Value;
                this.lShort_Value.Text = sShort_Value;
                this.lUShort_Value.Text = sUShort_Value;
                this.lInt32_Value.Text = sInt32_Value;
                this.lUInt32_Value.Text = sUInt32_Value;
                this.lInt64_Value.Text = sInt64_Value;
                this.lUInt64_Value.Text = sUInt64_Value;
                this.lFloat_Value.Text = sFloat_Value;
                this.lDouble_Value.Text = sDouble_Value;                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }            
        }

        private void HexBox_ManageAbility()
        {
            try
            {
                if (hbPacketData.ByteProvider == null)
                {
                    this.bSave.Enabled = false;
                    tsPacketData_Find.Enabled = false;
                    tsPacketData_FindNext.Enabled = false;
                    tscbEncoding.Enabled = false;
                    tscbPerLine.Enabled = false;
                }
                else
                {
                    this.bSave.Enabled = !this.bgwSendPacket.IsBusy;
                    tsPacketData_Find.Enabled = true;
                    tsPacketData_FindNext.Enabled = true;
                    tscbEncoding.Enabled = true;
                    tscbPerLine.Enabled = true;
                }

                HexBox_ManageAbilityForCopyAndPaste();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void HexBox_ManageAbilityForCopyAndPaste()
        {
            try
            {
                tsPacketData_Copy.Enabled = hbPacketData.CanCopy();
                tsPacketData_Cut.Enabled = hbPacketData.CanCut();
                tsPacketData_Paste.Enabled = hbPacketData.CanPaste();
                tsPacketData_Paste_PasteHex.Enabled = hbPacketData.CanPasteHex();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        

        #region//剪切

        private void tsPacketData_Cut_Click(object sender, EventArgs e)
        {
            this.hbPacketData.Cut();
        }

        #endregion

        #region//复制

        private void tsPacketData_Copy_ButtonClick(object sender, EventArgs e)
        {
            this.hbPacketData.Copy();
        }

        private void tsPacketData_Copy_Copy_Click(object sender, EventArgs e)
        {
            this.hbPacketData.Copy();
        }

        private void tsPacketData_Copy_CopyHex_Click(object sender, EventArgs e)
        {
            this.hbPacketData.CopyHex();
        }

        #endregion

        #region//粘贴

        private void tsPacketData_Paste_ButtonClick(object sender, EventArgs e)
        {
            this.hbPacketData.Paste();
        }

        private void tsPacketData_Paste_Paste_Click(object sender, EventArgs e)
        {
            this.hbPacketData.Paste();
        }

        private void tsPacketData_Paste_PasteHex_Click(object sender, EventArgs e)
        {
            this.hbPacketData.PasteHex();
        }

        #endregion

        #region//查找

        private void tsPacketData_Find_Click(object sender, EventArgs e)
        {
            try
            {
                this.ShowFindForm();

                if (Socket_Cache.SocketList.DoSearch)
                {
                    this.HexBox_FindNext();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void tsPacketData_FindNext_Click(object sender, EventArgs e)
        {
            this.HexBox_FindNext();
        }

        private void ShowFindForm()
        {
            try
            {
                Socket_FindForm sffFindForm = new Socket_FindForm();
                sffFindForm.ShowDialog();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void HexBox_FindNext()
        {
            try
            {
                if (Socket_Cache.SocketList.FindOptions.IsValid)
                {
                    long res = hbPacketData.Find(Socket_Cache.SocketList.FindOptions);

                    if (res == -1)
                    {
                        Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_23));
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//编码

        private void tscbEncoding_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                hbPacketData.ByteCharConverter = tscbEncoding.SelectedItem as IByteCharConverter;
                this.hbPacketData.Focus();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//排列

        private void tscbPerLine_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                this.hbPacketData.UseFixedBytesPerLine = false;

                this.hbPacketData.Focus();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #endregion        
    }
}
