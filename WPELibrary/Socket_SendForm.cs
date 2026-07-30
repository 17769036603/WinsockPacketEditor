using Be.Windows.Forms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private System.Drawing.Color byteSweepOriginalSelectionBackColor;
        private System.Drawing.Color byteSweepOriginalSelectionForeColor;
        private bool byteSweepHighlightActive;
        private bool byteSweepLiveDisplayEnabled;
        private bool byteSweepLiveValueActive;
        private long byteSweepLivePosition = -1;
        private byte byteSweepLiveOriginalValue;
        private long byteSweepLiveSecondPosition = -1;
        private byte byteSweepLiveSecondOriginalValue;
        private bool byteSweepLiveSecondValueActive;
        private int byteSweepLiveDisplayGeneration;
        private bool byteSweepProviderHadChanges;
        private bool packetEditorLockActive;
        private bool packetEditorOriginalReadOnly;
        private Socket_ByteAnnotationController byteAnnotationController;
        private List<Socket_ByteAnnotationInfo> workingByteAnnotations;
        private Socket_SendInfo savedSendPreset;
        private Socket_PacketInfo savedSendPresetPacket;
        private Socket_ByteSweepPresetInfo savedByteSweepPreset;
        private string baseWindowTitle;
        private ToolStripStatusLabel tlCurrentPacketIdentity;
        private TableLayoutPanel tlpSendActions;
        private TableLayoutPanel tlpByteSweepSettings;
        private Panel pnlByteSweepActions;
        private TableLayoutPanel pnlByteSweepSide;
        private Socket_ByteSweepEditorPanel byteSweepEditorPanel;
        private bool byteSweepEditorSyncing;
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
                this.MinimumSize = new System.Drawing.Size(1200, 620);
                this.InitializeHexSelectionAppearance();
                this.InitializeSendPanelLayout();
                this.InitializeByteSweepSidePanel();
                this.hbPacketData.AccessibleName = UiText("Main_PacketData");
                this.byteAnnotationController = new Socket_ByteAnnotationController(
                    this.hbPacketData, this.pnlByteSweepSide, 0, 0, 1,
                    delegate { return !this.bgwSendPacket.IsBusy; });
                this.InitializeByteSweepControls();
                this.InitializeByteSweepEditorPanel();
                this.baseWindowTitle = this.Text;
                this.tlCurrentPacketIdentity = new ToolStripStatusLabel
                {
                    Name = "tlCurrentPacketIdentity",
                    ForeColor = System.Drawing.Color.Navy,
                    Spring = true,
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft
                };
                this.ssSocketSend.Items.Insert(0, this.tlCurrentPacketIdentity);

                if (spi != null)
                { 
                    this.SPI = spi;
                }
                this.savedByteSweepPreset = byteSweepPreset;
                this.InitializePresetIdentity();
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
            this.tlpParameter.Padding = new Padding(4, 0, 4, 0);
            this.tlpParameter.SetCellPosition(this.gbSendType, new TableLayoutPanelCellPosition(0, 0));
            this.tlpParameter.SetCellPosition(this.gbSendStep, new TableLayoutPanelCellPosition(1, 0));
            this.tlpParameter.SetCellPosition(this.gbSendSocket, new TableLayoutPanelCellPosition(0, 0));
            this.tlpParameter.SetColumnSpan(this.gbSendSocket, 2);
            this.gbSendType.BringToFront();
            this.gbSendStep.BringToFront();
            this.gbSendType.Margin = new Padding(0, 3, 4, 3);
            this.gbSendStep.Margin = new Padding(4, 3, 0, 3);
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
            this.tlpSendType.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

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
            this.tlpSendForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            this.tlpSendForm.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tlpSendForm.SetCellPosition(
                this.ssSocketSend,
                new TableLayoutPanelCellPosition(0, 3));

            this.tlpSendType.ResumeLayout(true);
            this.tlpParameter.ResumeLayout(true);
            this.tlpSendForm.ResumeLayout(true);
        }

        private void InitializeByteSweepSidePanel()
        {
            this.tlpPacketData.ColumnCount = 3;
            this.tlpPacketData.ColumnStyles.Clear();
            this.tlpPacketData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tlpPacketData.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));
            this.tlpPacketData.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            this.tlpPacketData.RowCount = 2;
            this.tlpPacketData.RowStyles.Clear();
            this.tlpPacketData.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            this.tlpPacketData.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            this.pnlByteSweepSide = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 0, 0),
                Name = "pnlByteSweepSide",
                RowCount = 2,
                Padding = new Padding(0)
            };
            this.pnlByteSweepSide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.pnlByteSweepSide.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
            this.pnlByteSweepSide.RowStyles.Add(new RowStyle(SizeType.Percent, 68F));
            this.tlpPacketData.Controls.Add(this.pnlByteSweepSide, 2, 0);
            this.tlpPacketData.SetRowSpan(this.pnlByteSweepSide, 2);
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
            byteSweepActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            byteSweepActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            byteSweepActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
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
            this.nudByteSweepSecondInterval = CreateByteSweepNumber("nudByteSweepSecondInterval", 0, 999999999, 10);
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
            this.byteSweepEditorPanel = new Socket_ByteSweepEditorPanel();
            this.byteSweepEditorPanel.SendRequested += this.byteSweepEditorPanel_SendRequested;
            this.byteSweepEditorPanel.StopRequested += this.byteSweepEditorPanel_StopRequested;
            this.byteSweepEditorPanel.SaveRequested += this.byteSweepEditorPanel_SaveRequested;
            this.byteSweepEditorPanel.Changed += this.byteSweepEditorPanel_Changed;
            this.byteSweepEditorPanel.PickFirstRequested +=
                this.byteSweepEditorPanel_PickFirstRequested;
            this.byteSweepEditorPanel.PickSecondRequested +=
                this.byteSweepEditorPanel_PickSecondRequested;
            this.hbPacketData.MouseUp += this.hbPacketData_ByteSweepPickMouseUp;
            this.pnlByteSweepSide.Controls.Add(this.byteSweepEditorPanel, 0, 1);
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
            this.ApplyByteSweepEditorToLegacy();
            if (!this.IsPairCombinationMode())
            {
                this.CancelByteSweepPositionPick();
            }
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
            if (this.bgwSendPacket.IsBusy || !this.IsPairCombinationMode())
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
                (int)position);
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
            this.CancelByteSweepPositionPick();
            this.ApplyByteSweepEditorToLegacy();
            this.StartSend(true, true);
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
            if (this.byteSweepWasRunning && !this.byteSweepStartedFromEditorPanel)
            {
                this.StopSend();
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
                    control.Enabled = pair && !this.bgwSendPacket.IsBusy;
                    control.Visible = false;
                }
            }
            this.nudByteSweepInterval.Enabled = !this.bgwSendPacket.IsBusy;
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
                this.SendTypeChanged();
                this.ProgressionPositionChange();
        }

        private void Socket_SendForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.CancelByteSweepPositionPick();
            this.RestoreByteSweepVisualState();
            this.StopSend();
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
                    ? Socket_Cache.SocketList.ResolveCurrentSocket(new[] { this.SPI })
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
                Socket_AnnotatedByteProvider dbp = new Socket_AnnotatedByteProvider(
                    this.SPI.PacketBuffer, this.workingByteAnnotations);
                dbp.Changed += new EventHandler(ByteProvider_Changed);
                dbp.LengthChanged += new EventHandler(ByteProvider_LengthChanged);
                hbPacketData.ByteProvider = dbp;
                this.byteAnnotationController.Bind(this.workingByteAnnotations);

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
                this.tlByteSweepProgress.Visible = this.byteSweepWasRunning;
            }

            if (this.bSaveByteSweepPreset != null)
            {
                long selectionStart;
                long selectionLength;
                bool validSelection =
                    this.TryGetByteSweepSelection(out selectionStart, out selectionLength);
                this.bSaveByteSweepPreset.Visible = true;
                this.bSaveByteSweepPreset.Enabled = !busy && validSelection;
                this.bStartByteSweep.Enabled = !busy && validSelection;
            }
        }

        #endregion        

        #region//检查发送数据

        private bool CheckSendPacket(bool byteSweep)
        {
            try
            {
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
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
                return false;
            }

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
            try
            {
                if (this.CheckSendPacket(byteSweep))
                {
                    if (!bgwSendPacket.IsBusy)
                    {
                        SendWorkItem workItem = this.CreateSendWorkItem(byteSweep);

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
                            this.BeginByteSweepHighlight();
                        }

                        this.cts = new CancellationTokenSource();
                        this.bgwSendPacket.RunWorkerAsync(workItem);
                    }
                }
            }
            catch (Exception ex)
            {
                this.RestoreByteSweepVisualState();
                this.byteSweepWasRunning = false;
                this.SetSendRunningState(false);
                if (this.cts != null)
                {
                    this.cts.Dispose();
                    this.cts = null;
                }
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

            return new SendWorkItem
            {
                Socket = this.GetEffectiveSendSocket(),
                Interval = byteSweep
                    ? (int)this.nudByteSweepInterval.Value
                    : (int)this.nudSendType_Interval.Value,
                Times = byteSweep
                    ? (int)this.nudByteSweepLoopCount.Value
                    : (int)this.nudSendType_Times.Value,
                IPFrom = this.txtIPFrom.Text.Trim(),
                IPTo = this.txtIPTo.Text.Trim(),
                Buffer = Socket_ByteAnnotationEngine.GetBytes(dbp),
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
                this.nudByteSweepFirstPosition.Value < provider.Length &&
                this.nudByteSweepSecondPosition.Value < provider.Length &&
                this.nudByteSweepFirstPosition.Value != this.nudByteSweepSecondPosition.Value &&
                this.nudByteSweepFirstLength.Value >= 1 &&
                this.nudByteSweepSecondLength.Value >= 1;
            if (!valid && showMessage)
            {
                MessageBox.Show(
                    this,
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
            return this.savedSendPreset == null &&
                this.savedByteSweepPreset == null
                ? (int)this.nudSendSocket_Socket.Value
                : Socket_Cache.SocketList.ResolveCurrentSocket(new[] { this.SPI });
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
                    if (this.byteSweepEditorPanel != null)
                    {
                        this.byteSweepEditorPanel.SetOperationState(
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
                this.byteSweepStartedFromEditorPanel = false;
                if (this.cts != null)
                {
                    this.cts.Dispose();
                    this.cts = null;
                }
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
            if (workItem.PairCombination)
            {
                Socket_ByteSweepResult pairResult = ExecuteByteSweepPairLoops(
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

            Socket_ByteSweepResult result = ExecuteByteSweepLoops(
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
                    });
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
                    });

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
                        this.hbPacketData.Select(position, 1);
                        this.hbPacketData.ScrollByteIntoView(position);
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
                    this.tlByteSweepProgress.Visible = true;
                    this.tlByteSweepProgress.Text = text;
                    if (this.byteSweepEditorPanel != null)
                    {
                        this.byteSweepEditorPanel.SetOperationState(
                            true,
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
            bool liveDisplayWasActive =
                this.byteSweepLiveDisplayEnabled ||
                this.byteSweepLiveValueActive ||
                this.byteSweepLiveSecondValueActive;
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
            this.byteSweepProviderHadChanges = false;
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

            this.hbPacketData.Select(start, length);
            this.hbPacketData.ScrollByteIntoView(start);
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
                if (this.bgwSendPacket.IsBusy)
                {
                    if (this.cts != null)
                    {
                        this.cts.Cancel();
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

        private void UpdateCurrentByteSweepPreset()
        {
            long selectionStart = 0;
            long selectionLength = 1;
            bool pairMode = this.IsPairCombinationMode();
            if (!pairMode && !this.TryGetByteSweepSelection(out selectionStart, out selectionLength))
            {
                Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_234));
                return;
            }
            if (pairMode && !this.TryGetByteSweepPairConfiguration(true))
            {
                return;
            }

            if (!this.ApplyCurrentPacketEdits())
            {
                return;
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

            Socket_Cache.ByteSweepList.UpdatePreset(this.savedByteSweepPreset, value);
            Socket_Cache.ByteSweepList.SaveByteSweepList_ToDB();
            this.UpdateByteSweepPresetIdentity();
            this.ShowPresetSaveStatus(
                UiText("UI_SweepPresetUpdated"),
                this.savedByteSweepPreset.BFolder,
                this.savedByteSweepPreset.BName);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ShowByteSweepPresetDialog(string defaultName, string defaultFolder)
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (provider == null || provider.Length == 0)
            {
                Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_46));
                return;
            }

            long selectionStart = 0;
            long selectionLength = 1;
            bool pairMode = this.IsPairCombinationMode();
            if (!pairMode && !this.TryGetByteSweepSelection(out selectionStart, out selectionLength))
            {
                Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_234));
                return;
            }
            if (pairMode && !this.TryGetByteSweepPairConfiguration(true))
            {
                return;
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
                    Socket_Cache.ByteSweepList.AddPreset(dialog.Result);
                    Socket_Cache.ByteSweepList.SaveByteSweepList_ToDB();
                    this.tlByteSweepProgress.Visible = true;
                    this.tlByteSweepProgress.Text = string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        UiText("UI_PresetSaved"),
                        dialog.Result.BFolder,
                        dialog.Result.BName);
                }
            }
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

                    if (!this.ApplyCurrentPacketEdits())
                    {
                        return;
                    }

                    string targetFolder = Socket_Cache.SendList.lstFolders.FirstOrDefault(folder =>
                        string.Equals(folder, dialog.FolderName, StringComparison.OrdinalIgnoreCase));
                    if (targetFolder == null)
                    {
                        Socket_Cache.SendList.AddFolder(dialog.FolderName);
                        targetFolder = dialog.FolderName;
                    }

                    int loopCount = this.rbSendType_Continuously.Checked
                        ? 0
                        : (int)this.nudSendType_Times.Value;
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

                        this.savedSendPreset = existingPreset;
                        this.savedSendPresetPacket = containingPreset == null
                            ? this.savedSendPresetPacket
                            : this.SPI;
                        int existingIndex = Socket_Cache.SendList.lstSend.IndexOf(existingPreset);
                        if (existingIndex >= 0)
                        {
                            Socket_Cache.SendList.lstSend.ResetItem(existingIndex);
                        }
                        this.UpdatePresetIdentity(existingPreset);
                        Socket_Cache.SendList.SaveSendList_ToDB();
                        this.ShowPresetSaveStatus(
                            UiText("UI_SendPresetUpdated"),
                            targetFolder,
                            dialog.PresetName);
                        return;
                    }

                    this.savedSendPreset = CreateSendPreset(
                        this.SPI,
                        dialog.PresetName,
                        targetFolder,
                        loopCount,
                        (int)this.nudSendType_Interval.Value);
                    Socket_Cache.SendList.SendToList(this.savedSendPreset);
                    this.savedSendPresetPacket = this.savedSendPreset.SCollection[0];
                    this.UpdatePresetIdentity(this.savedSendPreset);
                    Socket_Cache.SendList.SaveSendList_ToDB();
                    this.ShowPresetSaveStatus(UiText("UI_SendPresetSaved"),
                        targetFolder, dialog.PresetName);
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

        private void cmsHexBox_Opening(object sender, CancelEventArgs e)
        {
            Socket_Operation.InitSendListComboBox(this.tscbSendList);
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

                        if (this.hbPacketData.CanCopy())
                        {
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
                                this.hbPacketData.CanCopy() ? this.hbPacketData.SelectionStart : 0,
                                this.hbPacketData.CanCopy() ? this.hbPacketData.SelectionLength : 0));
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
            this.HexBox_ManageAbilityForCopyAndPaste();
            this.SyncByteSweepEditorFromLegacy();
            this.SendTypeChanged();
        }

        private void hbPacketData_SelectionStartChanged(object sender, EventArgs e)
        {
            this.HexBox_ManageAbilityForCopyAndPaste();
            this.SyncByteSweepEditorFromLegacy();
            this.SendTypeChanged();
        }

        private void ByteProvider_Changed(object sender, EventArgs e)
        {
            this.HexBox_ManageAbility();
            this.byteAnnotationController.Refresh();
        }

        private void ByteProvider_LengthChanged(object sender, EventArgs e)
        {
            this.HexBox_UpdatePacketLen();
            this.byteAnnotationController.Refresh();
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
