using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WPELibrary.Lib;
using WPELibrary.Lib.Vision;

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
        private Button bToggleRobotInstructionPanel;
        private bool robotInstructionPanelExpanded;
        private bool robotInstructionPanelManuallyCollapsed;
        private bool robotInstructionPanelManuallyExpanded;
        private TableLayoutPanel executeLogHost;
        private Button bToggleExecuteLog;
        private bool executeLogExpanded;
        private ComboBox cbbVisionWindows;
        private NumericUpDown nudVisionX;
        private NumericUpDown nudVisionY;
        private NumericUpDown nudVisionWidth;
        private NumericUpDown nudVisionHeight;
        private PictureBox pbVisionPreview;
        private Label lVisionPreviewEmpty;
        private PictureBox pbVisionTemplate;
        private Label lVisionStatus;
        private Label lVisionMatchStatus;
        private Label lVisionOcrStatus;
        private Label lVisionAssistantStatus;
        private TableLayoutPanel visionAssistantLogHost;
        private Button bToggleVisionAssistantLog;
        private bool visionAssistantLogExpanded;
        private TextBox txtVisionAssistantLog;
        private Socket_VisionProfile robotExecutionVisionProfile;
        private NumericUpDown nudVisionThreshold;
        private CheckBox chkVisionTemplateNormalize;
        private CheckBox chkVisionTemplateScale;
        private NumericUpDown nudVisionTemplateScaleTolerance;
        private NumericUpDown nudVisionOcrScale;
        private NumericUpDown nudVisionOcrThreshold;
        private NumericUpDown nudVisionOcrContrast;
        private NumericUpDown nudVisionOcrAdaptiveWindow;
        private NumericUpDown nudVisionOcrAdaptiveOffset;
        private CheckBox chkVisionOcrBinary;
        private CheckBox chkVisionOcrAdaptive;
        private CheckBox chkVisionOcrInvert;
        private CheckBox chkVisionOcrDenoise;
        private CheckBox chkVisionOcrSharpen;
        private TextBox txtVisionOcrWhitelist;
        private ComboBox cbbVisionOcrEngine;
        private TextBox txtVisionOcrModelDirectory;
        private NumericUpDown nudVisionOcrDetectionThreshold;
        private NumericUpDown nudVisionOcrRecognitionThreshold;
        private NumericUpDown nudVisionOcrMaxImageSide;
        private TextBox txtVisionPythonExecutable;
        private TextBox txtVisionPythonWorkerScript;
        private NumericUpDown nudVisionPythonWorkerTimeout;
        private Button bVisionBrowsePythonExecutable;
        private Button bVisionBrowsePythonWorkerScript;
        private Button bVisionResetPythonSettings;
        private Button bVisionTestPythonWorker;
        private Label lVisionOcrModelStatus;
        private Button bVisionRecognizeText;
        private Button bVisionSelectRegion;
        private Button bVisionRecapture;
        private Button bVisionCancelOcr;
        private Button bVisionPreviewOcr;
        private Button bVisionRestorePreview;
        private Button bVisionMatchTemplate;
        private Button bVisionCancelMatch;
        private TextBox txtVisionOcrKeyword;
        private ListBox cbbVisionSteps;
        private ComboBox cbbVisionConditionType;
        private ComboBox cbbVisionActionType;
        private ComboBox cbbVisionScrollDirection;
        private NumericUpDown nudVisionScrollAmount;
        private Button bVisionConfirmAction;
        private Button bVisionCancelAction;
        private CheckBox chkVisionActionVerification;
        private Button bVisionActionVerificationMenu;
        private ComboBox cbbVisionVerificationType;
        private TextBox txtVisionVerificationKeyword;
        private Label lVisionVerificationKeyword;
        private CheckBox chkVisionVerificationSeparateRegion;
        private Button bVisionSelectVerificationRegion;
        private Label lVisionVerificationHint;
        private VisionRegion visionVerificationRegion;
        private TableLayoutPanel visionVerificationAdvancedPanel;
        private Button bVisionAdvancedSettings;
        private Panel visionAdvancedStorage;
        private readonly List<Control> visionAdvancedModules = new List<Control>();
        private NumericUpDown nudVisionNumberMinimum;
        private NumericUpDown nudVisionNumberMaximum;
        private TextBox txtVisionColorRgb;
        private NumericUpDown nudVisionColorTolerance;
        private NumericUpDown nudVisionColorMinimumPixels;
        private NumericUpDown nudVisionColorMinimumRatio;
        private NumericUpDown nudVisionConfirmations;
        private NumericUpDown nudVisionPollInterval;
        private NumericUpDown nudVisionTimeout;
        private NumericUpDown nudVisionRetries;
        private ComboBox cbbVisionFailurePolicy;
        private Button bVisionRunSteps;
        private Button bVisionStopSteps;
        private IVisionTextRecognizer visionTextRecognizer;
        private VisionPythonWorkerTextRecognizer visionPythonWorker;
        private CancellationTokenSource visionAssistantCancellation;
        private Task<VisionAssistantRunResult> visionAssistantTask;
        private bool updatingVisionStepEditor;
        private bool visionRobotClosing;
        private readonly VisionAssistantStep visionNewStep = new VisionAssistantStep();
        private Bitmap visionPreview;
        private Bitmap visionTemplate;
        private Bitmap visionPreprocessedPreview;
        private VisionRegion visionPreviewOriginRegion;
        private Rectangle visionPreviewSelection;
        private bool visionPreviewSelecting;
        private Point visionPreviewSelectionStart;
        private CheckBox chkVisionNormalized;
        private CheckBox chkVisionFixedClientSize;
        private NumericUpDown nudVisionRequiredWidth;
        private NumericUpDown nudVisionRequiredHeight;
        private Label lVisionFixedClientSizeStatus;
        private Button bVisionApplyFixedSize;
        private ComboBox cbbVisionCaptureSource;
        private NumericUpDown nudVisionCaptureInterval;
        private CheckBox chkVisionSkipUnchanged;
        private NumericUpDown nudVisionHistoryLimit;
        private CheckBox chkVisionSaveFailureSnapshots;
        private ComboBox cbbVisionHistory;
        private readonly List<VisionHistoryEntry> visionHistory = new List<VisionHistoryEntry>();
        private CancellationTokenSource visionOcrCancellation;
        private Task<VisionOcrResult> visionOcrTask;
        private CancellationTokenSource visionMatchCancellation;
        private Task<VisionMatchResult> visionMatchTask;
        private CancellationTokenSource visionPythonHealthCancellation;
        private Task<VisionPythonWorkerHealth> visionPythonHealthTask;
        private readonly object visionAssistantUiSync = new object();
        private readonly Queue<string> pendingVisionAssistantLogs = new Queue<string>();
        private string pendingVisionAssistantStatus;
        private bool visionAssistantUiUpdateScheduled;

        #region//窗体加载

        public Socket_RobotForm(Socket_RobotInfo sri)
        {
            try
            {
                MultiLanguage.SetDefaultLanguage(MultiLanguage.DefaultLanguage);
                InitializeComponent();
                this.AutoScaleMode = AutoScaleMode.Dpi;
                this.MinimumSize = new Size(600, 511);
                this.InitExecutionLogLayout();
                this.InitSendPresetPickerLayout();
                this.InitVisionLayout();

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
                this.InitVisionProfile();
                this.EnsureVisionInstructionRows();
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
                dgvRobotInstruction.AccessibleName = gbRobotInstruction.Text;
                dgvRobotInstruction.AccessibleRole = AccessibleRole.Table;
                dgvRobotInstruction.ShowCellToolTips = true;
                dgvRobotInstruction.Paint += this.dgvRobotInstruction_Paint;
                dgvRobotInstruction.CellToolTipTextNeeded += this.dgvRobotInstruction_CellToolTipTextNeeded;
                dgvRobotInstruction.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dgvRobotInstruction, true, null);
                dgvRobotInstruction.DataSource = this.dtRobotInstruction;
                this.InitRobotInstructionPanel();
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

        private void InitRobotInstructionPanel()
        {
            if (this.tlpListInfo == null || this.gbRobotInstruction == null)
            {
                return;
            }

            if (this.bToggleRobotInstructionPanel == null)
            {
                this.bToggleRobotInstructionPanel = new Button
                {
                    Name = "bToggleRobotInstructionPanel",
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    Margin = new Padding(4, 3, 3, 3),
                    TextAlign = ContentAlignment.MiddleCenter,
                    AccessibleRole = AccessibleRole.PushButton,
                    UseVisualStyleBackColor = false,
                    BackColor = Color.FromArgb(245, 247, 250),
                    ForeColor = Color.FromArgb(55, 65, 81),
                    FlatStyle = FlatStyle.Flat
                };
                this.bToggleRobotInstructionPanel.FlatAppearance.BorderColor =
                    Color.FromArgb(209, 213, 219);
                this.bToggleRobotInstructionPanel.Click +=
                    this.bToggleRobotInstructionPanel_Click;
            }

            if (!this.tlpListInfo.Controls.Contains(this.bToggleRobotInstructionPanel))
            {
                this.tlpListInfo.Controls.Add(this.bToggleRobotInstructionPanel, 1, 0);
            }

            this.UpdateRobotInstructionPanel();
        }

        private void bToggleRobotInstructionPanel_Click(object sender, EventArgs e)
        {
            if (this.robotInstructionPanelExpanded)
            {
                this.robotInstructionPanelManuallyCollapsed = true;
                this.robotInstructionPanelManuallyExpanded = false;
            }
            else
            {
                this.robotInstructionPanelManuallyCollapsed = false;
                this.robotInstructionPanelManuallyExpanded = true;
            }

            this.SetRobotInstructionPanelExpanded(!this.robotInstructionPanelExpanded);
        }

        private void UpdateRobotInstructionPanel()
        {
            if (this.tlpListInfo == null || this.gbRobotInstruction == null ||
                this.bToggleRobotInstructionPanel == null)
            {
                return;
            }

            int instructionCount = this.dtRobotInstruction == null
                ? 0
                : this.dtRobotInstruction.Rows.Count;
            bool expand = instructionCount > 0
                ? !this.robotInstructionPanelManuallyCollapsed
                : this.robotInstructionPanelManuallyExpanded;
            this.SetRobotInstructionPanelExpanded(expand, instructionCount);
        }

        private void SetRobotInstructionPanelExpanded(bool expanded, int? instructionCount = null)
        {
            if (this.tlpListInfo == null || this.gbRobotInstruction == null ||
                this.bToggleRobotInstructionPanel == null)
            {
                return;
            }

            int count = instructionCount.HasValue
                ? instructionCount.Value
                : (this.dtRobotInstruction == null ? 0 : this.dtRobotInstruction.Rows.Count);
            this.robotInstructionPanelExpanded = expanded;
            this.gbRobotInstruction.Visible = expanded;
            this.bToggleRobotInstructionPanel.Visible = !expanded;
            this.bToggleRobotInstructionPanel.Text = string.Format(
                UiText(expanded
                    ? "Robot_InstructionPanelHide"
                    : "Robot_InstructionPanelShow"),
                count);
            this.bToggleRobotInstructionPanel.AccessibleName =
                this.bToggleRobotInstructionPanel.Text;

            if (this.tlpListInfo.ColumnStyles.Count >= 2)
            {
                this.tlpListInfo.ColumnStyles[0] = expanded
                    ? new ColumnStyle(SizeType.Percent, 100F)
                    : new ColumnStyle(SizeType.Percent, 100F);
                this.tlpListInfo.ColumnStyles[1] = expanded
                    ? new ColumnStyle(SizeType.Absolute, 280F)
                    : new ColumnStyle(SizeType.Absolute, 280F);
            }

            this.tlpListInfo.PerformLayout();
            // A hidden Dock=Fill control keeps its previous bounds in WinForms;
            // keep the instruction column's measured width in sync for the next
            // expansion and for accessibility/UI audits.
            this.gbRobotInstruction.Width = 280;
            if (!expanded)
            {
                this.gbRobotInstruction.Location = new Point(
                    Math.Max(0, this.tlpListInfo.ClientSize.Width - this.gbRobotInstruction.Width),
                    this.gbRobotInstruction.Location.Y);
            }
        }

        private void InitExecutionLogLayout()
        {
            this.tlpRobotForm.Controls.Remove(this.gbExecute);
            this.gbExecute.Controls.Remove(this.txtExecute);
            this.gbExecute.Dispose();

            this.executeLogHost = new TableLayoutPanel
            {
                Name = "executeLogHost",
                ColumnCount = 1,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 3, 0),
                Padding = new Padding(0),
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            this.bToggleExecuteLog = new Button
            {
                Name = "bToggleExecuteLog",
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Text = UiText("Robot_ExecuteLogShow"),
                UseVisualStyleBackColor = true,
                AccessibleName = UiText("Robot_ExecuteLogShow")
            };
            this.bToggleExecuteLog.Click += this.bToggleExecuteLog_Click;

            this.txtExecute.Dock = DockStyle.Fill;
            this.txtExecute.Margin = new Padding(0, 3, 0, 0);
            this.executeLogHost.Controls.Add(this.bToggleExecuteLog, 0, 0);
            this.tlpRobotForm.Controls.Add(this.executeLogHost, 0, 2);
            this.SetExecuteLogExpanded(false);
        }

        private void bToggleExecuteLog_Click(object sender, EventArgs e)
        {
            this.SetExecuteLogExpanded(!this.executeLogExpanded);
        }

        private void SetExecuteLogExpanded(bool expanded)
        {
            if (this.executeLogHost == null || this.bToggleExecuteLog == null)
            {
                return;
            }

            this.executeLogExpanded = expanded;
            this.executeLogHost.SuspendLayout();
            this.executeLogHost.Controls.Remove(this.txtExecute);
            this.executeLogHost.RowStyles.Clear();

            if (expanded)
            {
                this.executeLogHost.RowCount = 2;
                this.executeLogHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
                this.executeLogHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                this.executeLogHost.Controls.Add(this.txtExecute, 0, 1);
                this.txtExecute.Visible = true;
                this.bToggleExecuteLog.Text = UiText("Robot_ExecuteLogHide");
                this.bToggleExecuteLog.AccessibleName = UiText("Robot_ExecuteLogHide");
                this.tlpRobotForm.RowStyles[2].Height = 50F;
            }
            else
            {
                this.executeLogHost.RowCount = 1;
                this.executeLogHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                this.txtExecute.Visible = false;
                this.bToggleExecuteLog.Text = UiText("Robot_ExecuteLogShow");
                this.bToggleExecuteLog.AccessibleName = UiText("Robot_ExecuteLogShow");
                this.tlpRobotForm.RowStyles[2].Height = 34F;
            }

            this.executeLogHost.ResumeLayout(true);
            this.tlpRobotForm.PerformLayout();
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

        private void InitVisionLayout()
        {
            TabPage visionTab = new TabPage
            {
                Name = "tpInstruction_Vision",
                Text = UiText("Vision_Tab"),
                Padding = new Padding(4),
                UseVisualStyleBackColor = true
            };

            FlowLayoutPanel page = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding = new Padding(4),
                Margin = new Padding(0)
            };
            page.Resize += (sender, e) => FitVisionSections(page);
            page.Layout += (sender, e) => FitVisionSections(page);
            visionTab.Resize += (sender, e) => FitVisionSections(page);
            visionTab.Enter += (sender, e) => FitVisionSections(page);
            visionTab.Controls.Add(page);

            TabPage visionSettingsTab = new TabPage
            {
                Name = "tpInstruction_VisionSettings",
                Text = UiText("Main_Settings"),
                Padding = new Padding(4),
                UseVisualStyleBackColor = true
            };
            FlowLayoutPanel settingsPage = CreateVisionVerticalFlow();
            settingsPage.Dock = DockStyle.Fill;
            settingsPage.BackColor = Color.FromArgb(245, 247, 250);
            settingsPage.Padding = new Padding(4);
            settingsPage.Resize += (sender, e) => FitVisionSections(settingsPage);
            settingsPage.Layout += (sender, e) => FitVisionSections(settingsPage);
            visionSettingsTab.Resize += (sender, e) => FitVisionSections(settingsPage);
            visionSettingsTab.Enter += (sender, e) => FitVisionSections(settingsPage);
            visionSettingsTab.Controls.Add(settingsPage);

            this.visionAdvancedStorage = new Panel
            {
                Visible = false,
                Size = Size.Empty
            };
            this.Controls.Add(this.visionAdvancedStorage);

            GroupBox captureSection = CreateVisionSection(UiText("Vision_SectionCapture"));
            TableLayoutPanel captureLayout = CreateVisionGrid(1);

            this.cbbVisionWindows = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true,
                DisplayMember = "DisplayName",
                AccessibleName = UiText("Vision_TargetWindow"),
                Margin = new Padding(0, 2, 4, 2)
            };
            this.cbbVisionWindows.SelectedIndexChanged += this.cbbVisionWindows_SelectedIndexChanged;
            this.cbbVisionWindows.Visible = false;
            Label lVisionTargetWindow = CreateVisionLabel(UiText("Vision_TargetWindow"));
            lVisionTargetWindow.Visible = false;
            Button bRefreshVisionWindows = new Button
            {
                Dock = DockStyle.Fill,
                Text = UiText("Vision_RefreshWindows"),
                AccessibleName = UiText("Vision_RefreshWindows"),
                AccessibleRole = AccessibleRole.PushButton,
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 2, 0, 2),
                Visible = false
            };
            bRefreshVisionWindows.Click += this.bRefreshVisionWindows_Click;
            captureLayout.Controls.Add(lVisionTargetWindow, 0, 0);
            captureLayout.Controls.Add(this.cbbVisionWindows, 0, 1);
            captureLayout.Controls.Add(bRefreshVisionWindows, 0, 2);

            this.nudVisionX = CreateVisionNumber(0);
            this.nudVisionY = CreateVisionNumber(0);
            this.nudVisionWidth = CreateVisionNumber(1);
            this.nudVisionHeight = CreateVisionNumber(1);
            this.bVisionSelectRegion = CreateVisionButton(UiText("Vision_SelectRegion"));
            StyleVisionPrimaryButton(this.bVisionSelectRegion);
            this.bVisionSelectRegion.Click += this.bVisionSelectRegion_Click;

            FlowLayoutPanel captureActions = CreateVisionFlow();
            captureActions.Controls.Add(this.bVisionSelectRegion);
            this.bVisionSelectRegion.AutoSize = false;
            this.bVisionSelectRegion.Size = new Size(84, 26);
            this.bVisionSelectRegion.Margin = new Padding(0, 2, 1, 2);
            this.bVisionRecapture = CreateVisionButton(UiText("Vision_Recapture"));
            this.bVisionRecapture.Click += this.bCaptureVision_Click;
            this.bVisionRecapture.AutoSize = false;
            this.bVisionRecapture.Size = new Size(84, 26);
            this.bVisionRecapture.Margin = new Padding(0, 2, 1, 2);
            captureActions.Controls.Add(this.bVisionRecapture);
            Button bSaveVisionProfile = CreateVisionButton(UiText("Vision_SaveProfile"));
            bSaveVisionProfile.Click += this.bSaveVisionProfile_Click;
            bSaveVisionProfile.AutoSize = false;
            bSaveVisionProfile.Size = new Size(84, 26);
            bSaveVisionProfile.Margin = new Padding(0, 2, 0, 2);
            captureActions.Controls.Add(bSaveVisionProfile);
            captureActions.WrapContents = true;
            captureLayout.Controls.Add(captureActions, 0, 3);

            FlowLayoutPanel captureAdvancedContent = CreateVisionVerticalFlow();
            TableLayoutPanel manualRegionSettings = CreateVisionGrid(2);
            manualRegionSettings.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 58F);
            manualRegionSettings.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100F);
            AddVisionNumber(manualRegionSettings, UiText("Vision_X"), this.nudVisionX, 0, 0);
            AddVisionNumber(manualRegionSettings, UiText("Vision_Y"), this.nudVisionY, 0, 1);
            AddVisionNumber(manualRegionSettings, UiText("Vision_Width"), this.nudVisionWidth, 0, 2);
            AddVisionNumber(manualRegionSettings, UiText("Vision_Height"), this.nudVisionHeight, 0, 3);
            captureAdvancedContent.Controls.Add(manualRegionSettings);
            captureAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_CaptureSource")));
            this.cbbVisionCaptureSource = new ComboBox
            {
                Width = 92,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                AccessibleName = UiText("Vision_CaptureSource"),
                Margin = new Padding(0, 2, 8, 2)
            };
            this.cbbVisionCaptureSource.Items.Add(new VisionCaptureSourceChoice(VisionCaptureSourceMode.Auto, UiText("Vision_CaptureAuto")));
            this.cbbVisionCaptureSource.Items.Add(new VisionCaptureSourceChoice(VisionCaptureSourceMode.Screen, UiText("Vision_CaptureScreen")));
            this.cbbVisionCaptureSource.Items.Add(new VisionCaptureSourceChoice(VisionCaptureSourceMode.WindowRender, UiText("Vision_CaptureWindowRender")));
            this.cbbVisionCaptureSource.Items.Add(new VisionCaptureSourceChoice(VisionCaptureSourceMode.Airtest, UiText("Vision_CaptureAirtest")));
            this.cbbVisionCaptureSource.SelectedIndex = 0;
            captureAdvancedContent.Controls.Add(this.cbbVisionCaptureSource);
            captureAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_CaptureInterval")));
            this.nudVisionCaptureInterval = CreateVisionNumber(0);
            this.nudVisionCaptureInterval.Maximum = 60000;
            this.nudVisionCaptureInterval.Value = 150;
            this.nudVisionCaptureInterval.Width = 66;
            this.nudVisionCaptureInterval.Dock = DockStyle.None;
            captureAdvancedContent.Controls.Add(this.nudVisionCaptureInterval);
            this.chkVisionSkipUnchanged = CreateVisionCheckBox(UiText("Vision_SkipUnchanged"));
            this.chkVisionSkipUnchanged.Checked = true;
            captureAdvancedContent.Controls.Add(this.chkVisionSkipUnchanged);
            FlowLayoutPanel fixedClientSizeLayout = CreateVisionFlow();
            this.chkVisionFixedClientSize = CreateVisionCheckBox(UiText("Vision_FixedClientSize"));
            this.chkVisionFixedClientSize.CheckedChanged += (sender, e) => this.UpdateVisionFixedClientSizeUi();
            fixedClientSizeLayout.Controls.Add(this.chkVisionFixedClientSize);
            fixedClientSizeLayout.Controls.Add(CreateVisionLabel(UiText("Vision_RequiredWidth")));
            this.nudVisionRequiredWidth = CreateVisionNumber(1);
            this.nudVisionRequiredWidth.Maximum = 8192;
            this.nudVisionRequiredWidth.Value = 1280;
            this.nudVisionRequiredWidth.Width = 64;
            this.nudVisionRequiredWidth.ValueChanged += (sender, e) => this.UpdateVisionFixedClientSizeUi();
            fixedClientSizeLayout.Controls.Add(this.nudVisionRequiredWidth);
            fixedClientSizeLayout.Controls.Add(CreateVisionLabel(UiText("Vision_RequiredHeight")));
            this.nudVisionRequiredHeight = CreateVisionNumber(1);
            this.nudVisionRequiredHeight.Maximum = 8192;
            this.nudVisionRequiredHeight.Value = 720;
            this.nudVisionRequiredHeight.Width = 64;
            this.nudVisionRequiredHeight.ValueChanged += (sender, e) => this.UpdateVisionFixedClientSizeUi();
            fixedClientSizeLayout.Controls.Add(this.nudVisionRequiredHeight);
            this.bVisionApplyFixedSize = CreateVisionButton(UiText("Vision_Apply1280x720"));
            this.bVisionApplyFixedSize.Click += this.bVisionApplyFixedSize_Click;
            fixedClientSizeLayout.Controls.Add(this.bVisionApplyFixedSize);
            captureAdvancedContent.Controls.Add(fixedClientSizeLayout);
            this.lVisionFixedClientSizeStatus = CreateVisionStatusLabel(UiText("Vision_ClientSizeAny"));
            captureAdvancedContent.Controls.Add(this.lVisionFixedClientSizeStatus);
            this.chkVisionNormalized = CreateVisionCheckBox(UiText("Vision_NormalizedRegion"));
            this.chkVisionNormalized.Checked = true;
            captureAdvancedContent.Controls.Add(this.chkVisionNormalized);
            this.nudVisionHistoryLimit = CreateVisionNumber(0);
            this.nudVisionHistoryLimit.Maximum = 200;
            this.nudVisionHistoryLimit.Value = 30;
            this.nudVisionHistoryLimit.Width = 52;
            this.nudVisionHistoryLimit.Dock = DockStyle.None;
            captureAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_HistoryLimit")));
            captureAdvancedContent.Controls.Add(this.nudVisionHistoryLimit);
            this.chkVisionSaveFailureSnapshots = CreateVisionCheckBox(UiText("Vision_SaveFailureSnapshots"));
            captureAdvancedContent.Controls.Add(this.chkVisionSaveFailureSnapshots);
            Button bSaveVisionPreview = CreateVisionButton(UiText("Vision_SaveScreenshot"));
            bSaveVisionPreview.Click += (sender, e) => this.SaveVisionPreview();
            captureAdvancedContent.Controls.Add(bSaveVisionPreview);
            TableLayoutPanel captureAdvancedPanel = CreateVisionCollapsiblePanel(
                UiText("Vision_AdvancedRegion"),
                UiText("Vision_LessCaptureSettings"),
                captureAdvancedContent);
            captureAdvancedPanel.Controls[0].Name = "bVisionAdvancedRegionToggle";
            this.RegisterVisionAdvancedModule(captureAdvancedPanel);

            this.lVisionStatus = CreateVisionStatusLabel(UiText("Vision_SelectWindowHint"));
            captureLayout.Controls.Add(this.lVisionStatus, 0, 6);
            AttachVisionSectionContent(captureSection, captureLayout);
            page.Controls.Add(captureSection);

            GroupBox previewSection = CreateVisionSection(UiText("Vision_SectionPreview"));
            TableLayoutPanel previewLayout = CreateVisionGrid(1);
            previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            previewLayout.Controls.Add(CreateVisionLabel(UiText("Vision_CurrentScreenshot")), 0, 0);
            this.pbVisionPreview = CreateVisionPictureBox();
            this.pbVisionPreview.MouseDown += this.pbVisionPreview_MouseDown;
            this.pbVisionPreview.MouseMove += this.pbVisionPreview_MouseMove;
            this.pbVisionPreview.MouseUp += this.pbVisionPreview_MouseUp;
            this.pbVisionPreview.Paint += this.pbVisionPreview_Paint;
            this.pbVisionTemplate = CreateVisionPictureBox();
            previewLayout.Controls.Add(this.pbVisionPreview, 0, 1);
            this.lVisionPreviewEmpty = CreateVisionStatusLabel(UiText("Vision_NoPreview"));
            previewLayout.Controls.Add(this.lVisionPreviewEmpty, 0, 2);

            TableLayoutPanel historyLayout = CreateVisionGrid(2);
            historyLayout.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100F);
            historyLayout.ColumnStyles[1] = new ColumnStyle(SizeType.AutoSize);
            FlowLayoutPanel historyLabel = CreateVisionFlow();
            historyLabel.Controls.Add(CreateVisionLabel(UiText("Vision_History")));
            this.cbbVisionHistory = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "DisplayName",
                AccessibleName = UiText("Vision_History"),
                Margin = new Padding(0, 2, 6, 2)
            };
            this.cbbVisionHistory.SelectedIndexChanged += this.cbbVisionHistory_SelectedIndexChanged;
            historyLabel.Controls.Add(this.cbbVisionHistory);
            historyLayout.Controls.Add(historyLabel, 0, 0);
            Button bClearVisionHistory = CreateVisionButton(UiText("Vision_ClearHistory"));
            bClearVisionHistory.Click += this.bClearVisionHistory_Click;
            historyLayout.Controls.Add(bClearVisionHistory, 1, 0);
            TableLayoutPanel historyPanel = CreateVisionCollapsiblePanel(
                UiText("Vision_MoreCaptureSettings"),
                UiText("Vision_LessCaptureSettings"),
                historyLayout);
            this.RegisterVisionAdvancedModule(historyPanel);
            AttachVisionSectionContent(previewSection, previewLayout);
            page.Controls.Add(previewSection);

            GroupBox recognitionSection = CreateVisionSection(UiText("Vision_SectionRecognition"));
            TableLayoutPanel recognitionLayout = CreateVisionGrid(1);
            TabControl modeTabs = new TabControl
            {
                Dock = DockStyle.Top,
                Height = 130,
                Margin = new Padding(0, 0, 0, 6)
            };

            TabPage ocrPage = new TabPage
            {
                Text = UiText("Vision_ModeOcr"),
                Padding = new Padding(6),
                // 主视觉页已经提供唯一的页面级滚动容器；这里不再嵌套滚动。
                AutoScroll = false,
                UseVisualStyleBackColor = true
            };
            FlowLayoutPanel ocrRoot = CreateVisionVerticalFlow();
            TableLayoutPanel ocrBasic = CreateVisionGrid(1);
            this.nudVisionOcrScale = CreateVisionNumber(1);
            this.nudVisionOcrScale.Maximum = 4;
            this.nudVisionOcrScale.Value = 2;
            this.nudVisionOcrScale.Width = 60;
            this.nudVisionOcrScale.Dock = DockStyle.None;
            ocrBasic.Controls.Add(CreateVisionLabel(UiText("Vision_OcrKeyword")), 0, 0);
            this.txtVisionOcrKeyword = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiText("Vision_OcrKeyword"),
                AccessibleRole = AccessibleRole.Text,
                Margin = new Padding(0, 2, 0, 2)
            };
            ocrBasic.Controls.Add(this.txtVisionOcrKeyword, 0, 1);
            FlowLayoutPanel ocrActions = CreateVisionFlow();
            this.bVisionRecognizeText = CreateVisionButton(UiText("Vision_OcrTest"));
            this.bVisionRecognizeText.Click += this.bRecognizeVisionText_Click;
            ocrActions.Controls.Add(this.bVisionRecognizeText);
            this.bVisionCancelOcr = CreateVisionButton(UiText("Vision_OcrCancel"));
            this.bVisionCancelOcr.Enabled = false;
            this.bVisionCancelOcr.Visible = false;
            this.bVisionCancelOcr.Click += this.bCancelVisionOcr_Click;
            ocrActions.Controls.Add(this.bVisionCancelOcr);
            this.bVisionPreviewOcr = CreateVisionButton(UiText("Vision_OcrPreview"));
            this.bVisionPreviewOcr.Click += this.bPreviewVisionOcr_Click;
            this.bVisionRestorePreview = CreateVisionButton(UiText("Vision_RestorePreview"));
            this.bVisionRestorePreview.Click += this.bRestoreVisionPreview_Click;
            ocrBasic.Controls.Add(ocrActions, 0, 2);
            ocrRoot.Controls.Add(ocrBasic);

            FlowLayoutPanel ocrAdvancedContent = CreateVisionFlow();
            FlowLayoutPanel ocrScaleSettings = CreateVisionFlow();
            ocrScaleSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrScale")));
            ocrScaleSettings.Controls.Add(this.nudVisionOcrScale);
            ocrAdvancedContent.Controls.Add(ocrScaleSettings);
            FlowLayoutPanel binarySettings = CreateVisionFlow();
            this.chkVisionOcrBinary = CreateVisionCheckBox(UiText("Vision_OcrBinary"));
            binarySettings.Controls.Add(this.chkVisionOcrBinary);
            binarySettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrThreshold")));
            this.nudVisionOcrThreshold = CreateVisionNumber(0);
            this.nudVisionOcrThreshold.Maximum = 255;
            this.nudVisionOcrThreshold.Value = 160;
            this.nudVisionOcrThreshold.Width = 60;
            this.nudVisionOcrThreshold.Dock = DockStyle.None;
            binarySettings.Controls.Add(this.nudVisionOcrThreshold);
            ocrAdvancedContent.Controls.Add(binarySettings);
            ocrAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_OcrContrast")));
            this.nudVisionOcrContrast = CreateVisionDecimal(0.1D, 5D, 1D);
            this.nudVisionOcrContrast.Width = 60;
            this.nudVisionOcrContrast.Dock = DockStyle.None;
            ocrAdvancedContent.Controls.Add(this.nudVisionOcrContrast);
            FlowLayoutPanel adaptiveSettings = CreateVisionFlow();
            this.chkVisionOcrAdaptive = CreateVisionCheckBox(UiText("Vision_OcrAdaptive"));
            adaptiveSettings.Controls.Add(this.chkVisionOcrAdaptive);
            adaptiveSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrAdaptiveWindow")));
            this.nudVisionOcrAdaptiveWindow = CreateVisionNumber(3);
            this.nudVisionOcrAdaptiveWindow.Maximum = 51;
            this.nudVisionOcrAdaptiveWindow.Value = 15;
            this.nudVisionOcrAdaptiveWindow.Width = 52;
            this.nudVisionOcrAdaptiveWindow.Dock = DockStyle.None;
            adaptiveSettings.Controls.Add(this.nudVisionOcrAdaptiveWindow);
            adaptiveSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrAdaptiveOffset")));
            this.nudVisionOcrAdaptiveOffset = CreateVisionNumber(-64);
            this.nudVisionOcrAdaptiveOffset.Minimum = -64;
            this.nudVisionOcrAdaptiveOffset.Maximum = 64;
            this.nudVisionOcrAdaptiveOffset.Value = 8;
            this.nudVisionOcrAdaptiveOffset.Width = 52;
            this.nudVisionOcrAdaptiveOffset.Dock = DockStyle.None;
            adaptiveSettings.Controls.Add(this.nudVisionOcrAdaptiveOffset);
            ocrAdvancedContent.Controls.Add(adaptiveSettings);
            FlowLayoutPanel filterSettings = CreateVisionFlow();
            this.chkVisionOcrInvert = CreateVisionCheckBox(UiText("Vision_OcrInvert"));
            this.chkVisionOcrDenoise = CreateVisionCheckBox(UiText("Vision_OcrDenoise"));
            this.chkVisionOcrSharpen = CreateVisionCheckBox(UiText("Vision_OcrSharpen"));
            filterSettings.Controls.Add(this.chkVisionOcrInvert);
            filterSettings.Controls.Add(this.chkVisionOcrDenoise);
            filterSettings.Controls.Add(this.chkVisionOcrSharpen);
            ocrAdvancedContent.Controls.Add(filterSettings);
            FlowLayoutPanel whitelistSettings = CreateVisionFlow();
            whitelistSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrWhitelist")));
            this.txtVisionOcrWhitelist = new TextBox
            {
                Width = 150,
                AccessibleName = UiText("Vision_OcrWhitelist"),
                AccessibleRole = AccessibleRole.Text,
                Margin = new Padding(0, 2, 0, 2)
            };
            whitelistSettings.Controls.Add(this.txtVisionOcrWhitelist);
            ocrAdvancedContent.Controls.Add(whitelistSettings);
            FlowLayoutPanel engineSettings = CreateVisionFlow();
            engineSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrEngine")));
            this.cbbVisionOcrEngine = new ComboBox
            {
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                AccessibleName = UiText("Vision_OcrEngine"),
                Margin = new Padding(0, 2, 8, 2)
            };
            this.cbbVisionOcrEngine.Items.Add(new VisionOcrEngineChoice(
                VisionOcrEngine.Auto,
                UiText("Vision_OcrEngineAuto")));
            this.cbbVisionOcrEngine.Items.Add(new VisionOcrEngineChoice(
                VisionOcrEngine.Onnx,
                UiText("Vision_OcrEngineOnnx")));
            this.cbbVisionOcrEngine.Items.Add(new VisionOcrEngineChoice(
                VisionOcrEngine.Tesseract,
                UiText("Vision_OcrEngineTesseract")));
            this.cbbVisionOcrEngine.Items.Add(new VisionOcrEngineChoice(
                VisionOcrEngine.PythonWorker,
                UiText("Vision_OcrEnginePython")));
            this.cbbVisionOcrEngine.SelectedIndexChanged += (sender, e) => this.UpdateVisionOcrModelStatus();
            this.cbbVisionOcrEngine.SelectedIndex = 0;
            engineSettings.Controls.Add(this.cbbVisionOcrEngine);
            engineSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrModelDirectory")));
            this.txtVisionOcrModelDirectory = new TextBox
            {
                Width = 220,
                AccessibleName = UiText("Vision_OcrModelDirectory"),
                AccessibleRole = AccessibleRole.Text,
                Margin = new Padding(0, 2, 8, 2)
            };
            this.txtVisionOcrModelDirectory.TextChanged += (sender, e) => this.UpdateVisionOcrModelStatus();
            engineSettings.Controls.Add(this.txtVisionOcrModelDirectory);
            engineSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrDetectionThreshold")));
            this.nudVisionOcrDetectionThreshold = CreateVisionDecimal(0D, 1D, 0.3D);
            this.nudVisionOcrDetectionThreshold.Width = 58;
            this.nudVisionOcrDetectionThreshold.Dock = DockStyle.None;
            engineSettings.Controls.Add(this.nudVisionOcrDetectionThreshold);
            engineSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrRecognitionThreshold")));
            this.nudVisionOcrRecognitionThreshold = CreateVisionDecimal(0D, 1D, 0.5D);
            this.nudVisionOcrRecognitionThreshold.Width = 58;
            this.nudVisionOcrRecognitionThreshold.Dock = DockStyle.None;
            engineSettings.Controls.Add(this.nudVisionOcrRecognitionThreshold);
            engineSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrMaxImageSide")));
            this.nudVisionOcrMaxImageSide = CreateVisionNumber(128);
            this.nudVisionOcrMaxImageSide.Maximum = 4096;
            this.nudVisionOcrMaxImageSide.Value = 960;
            this.nudVisionOcrMaxImageSide.Width = 66;
            this.nudVisionOcrMaxImageSide.Dock = DockStyle.None;
            engineSettings.Controls.Add(this.nudVisionOcrMaxImageSide);
            this.lVisionOcrModelStatus = CreateVisionStatusLabel(string.Empty);
            engineSettings.Controls.Add(this.lVisionOcrModelStatus);
            ocrAdvancedContent.Controls.Add(engineSettings);
            FlowLayoutPanel pythonSettings = CreateVisionVerticalFlow();
            FlowLayoutPanel pythonExecutableSettings = CreateVisionFlow();
            pythonExecutableSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrPythonExecutable")));
            this.txtVisionPythonExecutable = new TextBox
            {
                Width = 210,
                AccessibleName = UiText("Vision_OcrPythonExecutable"),
                AccessibleRole = AccessibleRole.Text,
                Margin = new Padding(0, 2, 8, 2)
            };
            this.txtVisionPythonExecutable.TextChanged += (sender, e) => this.UpdateVisionOcrModelStatus();
            pythonExecutableSettings.Controls.Add(this.txtVisionPythonExecutable);
            this.bVisionBrowsePythonExecutable = CreateVisionButton(UiText("Vision_OcrPythonBrowse"));
            this.bVisionBrowsePythonExecutable.Click += this.bVisionBrowsePythonExecutable_Click;
            pythonExecutableSettings.Controls.Add(this.bVisionBrowsePythonExecutable);
            pythonSettings.Controls.Add(pythonExecutableSettings);
            FlowLayoutPanel pythonScriptSettings = CreateVisionFlow();
            pythonScriptSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrPythonWorkerScript")));
            this.txtVisionPythonWorkerScript = new TextBox
            {
                Width = 210,
                AccessibleName = UiText("Vision_OcrPythonWorkerScript"),
                AccessibleRole = AccessibleRole.Text,
                Margin = new Padding(0, 2, 8, 2)
            };
            this.txtVisionPythonWorkerScript.TextChanged += (sender, e) => this.UpdateVisionOcrModelStatus();
            pythonScriptSettings.Controls.Add(this.txtVisionPythonWorkerScript);
            this.bVisionBrowsePythonWorkerScript = CreateVisionButton(UiText("Vision_OcrPythonBrowse"));
            this.bVisionBrowsePythonWorkerScript.Click += this.bVisionBrowsePythonWorkerScript_Click;
            pythonScriptSettings.Controls.Add(this.bVisionBrowsePythonWorkerScript);
            pythonSettings.Controls.Add(pythonScriptSettings);
            FlowLayoutPanel pythonRuntimeSettings = CreateVisionFlow();
            pythonRuntimeSettings.Controls.Add(CreateVisionLabel(UiText("Vision_OcrPythonWorkerTimeout")));
            this.nudVisionPythonWorkerTimeout = CreateVisionNumber(500);
            this.nudVisionPythonWorkerTimeout.Minimum = 500;
            this.nudVisionPythonWorkerTimeout.Maximum = 120000;
            this.nudVisionPythonWorkerTimeout.Value = 15000;
            this.nudVisionPythonWorkerTimeout.Width = 76;
            this.nudVisionPythonWorkerTimeout.Dock = DockStyle.None;
            pythonRuntimeSettings.Controls.Add(this.nudVisionPythonWorkerTimeout);
            this.bVisionTestPythonWorker = CreateVisionButton(UiText("Vision_OcrPythonTest"));
            this.bVisionTestPythonWorker.Click += this.bVisionTestPythonWorker_Click;
            pythonRuntimeSettings.Controls.Add(this.bVisionTestPythonWorker);
            this.bVisionResetPythonSettings = CreateVisionButton(UiText("Vision_OcrPythonReset"));
            this.bVisionResetPythonSettings.Click += this.bVisionResetPythonSettings_Click;
            pythonRuntimeSettings.Controls.Add(this.bVisionResetPythonSettings);
            pythonSettings.Controls.Add(pythonRuntimeSettings);
            pythonSettings.Controls.Add(CreateVisionStatusLabel(UiText("Vision_OcrPythonAutoHint")));
            ocrAdvancedContent.Controls.Add(pythonSettings);
            FlowLayoutPanel ocrExtraActions = CreateVisionFlow();
            ocrExtraActions.Controls.Add(this.bVisionPreviewOcr);
            ocrExtraActions.Controls.Add(this.bVisionRestorePreview);
            ocrAdvancedContent.Controls.Add(ocrExtraActions);
            TableLayoutPanel ocrAdvancedPanel = CreateVisionCollapsiblePanel(
                UiText("Vision_MoreOcrSettings"),
                UiText("Vision_LessOcrSettings"),
                ocrAdvancedContent);
            this.RegisterVisionAdvancedModule(ocrAdvancedPanel);
            this.lVisionOcrStatus = CreateVisionStatusLabel(string.Empty);
            ocrRoot.Controls.Add(this.lVisionOcrStatus);
            ocrPage.Controls.Add(ocrRoot);
            modeTabs.TabPages.Add(ocrPage);

            TabPage templatePage = new TabPage
            {
                Text = UiText("Vision_ModeTemplate"),
                Padding = new Padding(6),
                // 模板页随内容自动增高，由外层视觉页统一滚动。
                AutoScroll = false,
                UseVisualStyleBackColor = true
            };
            FlowLayoutPanel templateRoot = CreateVisionVerticalFlow();
            // 参考图片由截图流程自动准备；保留内部模板匹配入口供旧配置和回归调用，
            // 但不再把手动加载、保存、变体和开始匹配按钮放到界面上。
            this.bVisionMatchTemplate = CreateVisionButton(UiText("Vision_Match"));
            this.bVisionMatchTemplate.Click += this.bMatchVisionTemplate_Click;
            this.bVisionMatchTemplate.Visible = false;
            this.bVisionCancelMatch = CreateVisionButton(UiText("Vision_MatchCancel"));
            this.bVisionCancelMatch.Enabled = false;
            this.bVisionCancelMatch.Visible = false;
            this.bVisionCancelMatch.Click += this.bCancelVisionMatch_Click;
            FlowLayoutPanel templateBasic = CreateVisionFlow();
            templateBasic.Controls.Add(CreateVisionLabel(UiText("Vision_Threshold")));
            this.nudVisionThreshold = CreateVisionNumber(0);
            this.nudVisionThreshold.Maximum = 100;
            this.nudVisionThreshold.Value = 90;
            this.nudVisionThreshold.Width = 60;
            this.nudVisionThreshold.Dock = DockStyle.None;
            templateBasic.Controls.Add(this.nudVisionThreshold);
            templateRoot.Controls.Add(templateBasic);
            FlowLayoutPanel templateAdvancedContent = CreateVisionFlow();
            this.chkVisionTemplateNormalize = CreateVisionCheckBox(UiText("Vision_TemplateNormalize"));
            this.chkVisionTemplateNormalize.Checked = true;
            this.chkVisionTemplateScale = CreateVisionCheckBox(UiText("Vision_TemplateScale"));
            templateAdvancedContent.Controls.Add(this.chkVisionTemplateNormalize);
            templateAdvancedContent.Controls.Add(this.chkVisionTemplateScale);
            templateAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_TemplateTolerance")));
            this.nudVisionTemplateScaleTolerance = CreateVisionNumber(0);
            this.nudVisionTemplateScaleTolerance.Maximum = 50;
            this.nudVisionTemplateScaleTolerance.Value = 10;
            this.nudVisionTemplateScaleTolerance.Width = 52;
            this.nudVisionTemplateScaleTolerance.Dock = DockStyle.None;
            templateAdvancedContent.Controls.Add(this.nudVisionTemplateScaleTolerance);
            TableLayoutPanel templateAdvancedPanel = CreateVisionCollapsiblePanel(
                UiText("Vision_MoreTemplateSettings"),
                UiText("Vision_LessTemplateSettings"),
                templateAdvancedContent);
            this.RegisterVisionAdvancedModule(templateAdvancedPanel);
            this.lVisionMatchStatus = CreateVisionStatusLabel(UiText("Vision_NoTemplate"));
            templateRoot.Controls.Add(this.lVisionMatchStatus);
            templateRoot.Controls.Add(CreateVisionLabel(UiText("Vision_RecognitionPreview")));
            Panel templatePreviewHost = new Panel
            {
                Height = 108,
                Width = 145,
                Margin = new Padding(0, 2, 0, 4)
            };
            this.pbVisionTemplate.Dock = DockStyle.Fill;
            templatePreviewHost.Controls.Add(this.pbVisionTemplate);
            templateRoot.Controls.Add(templatePreviewHost);
            templatePage.Controls.Add(templateRoot);
            modeTabs.TabPages.Add(templatePage);
            Action fitModeTabs = () =>
            {
                Control selectedContent = modeTabs.SelectedIndex == 0
                    ? (Control)ocrRoot
                    : templateRoot;
                FitVisionModeTabs(modeTabs, selectedContent, modeTabs.SelectedIndex == 0 ? 130 : 225);
                recognitionSection.PerformLayout();
            };
            modeTabs.Resize += (sender, e) => fitModeTabs();
            ocrRoot.Layout += (sender, e) => fitModeTabs();
            templateRoot.Layout += (sender, e) => fitModeTabs();
            modeTabs.SelectedIndexChanged += (sender, e) => fitModeTabs();
            recognitionLayout.Controls.Add(modeTabs, 0, 0);
            AttachVisionSectionContent(recognitionSection, recognitionLayout);

            GroupBox assistantSection = CreateVisionSection(UiText("Vision_SectionAssistant"));
            TableLayoutPanel assistantLayout = CreateVisionGrid(1);
            this.bVisionAdvancedSettings = CreateVisionButton(UiText("Vision_AdvancedSettings"));
            this.bVisionAdvancedSettings.Click += this.bVisionAdvancedSettings_Click;
            FlowLayoutPanel conditionTitle = CreateVisionFlow();
            conditionTitle.FlowDirection = FlowDirection.TopDown;
            conditionTitle.WrapContents = false;
            conditionTitle.Controls.Add(CreateVisionLabel(UiText("Vision_Condition")));
            this.cbbVisionConditionType = new ComboBox
            {
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                AccessibleName = UiText("Vision_Condition"),
                Margin = new Padding(0, 2, 6, 2)
            };
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(VisionConditionType.TextAppears, UiText("Vision_TextAppears")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(VisionConditionType.TextDisappears, UiText("Vision_TextDisappears")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(VisionConditionType.NumberInRange, UiText("Vision_NumberRange")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(VisionConditionType.TemplateAppears, UiText("Vision_TemplateAppears")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(VisionConditionType.TemplateDisappears, UiText("Vision_TemplateDisappears")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(VisionConditionType.ColorAppears, UiText("Vision_ColorAppears")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(VisionConditionType.ColorDisappears, UiText("Vision_ColorDisappears")));
            this.cbbVisionConditionType.SelectedIndex = 0;
            conditionTitle.Controls.Add(this.cbbVisionConditionType);
            Label conditionSourceHint = CreateVisionStatusLabel(UiText("Vision_ConditionSourceHint"));
            conditionTitle.Controls.Add(conditionSourceHint);
            assistantLayout.Controls.Add(conditionTitle, 0, 0);

            FlowLayoutPanel conditionRange = CreateVisionFlow();
            conditionRange.Controls.Add(CreateVisionLabel(UiText("Vision_Minimum")));
            this.nudVisionNumberMinimum = CreateVisionDecimal(-1000000000D, 1000000000D, 0D);
            this.nudVisionNumberMinimum.Width = 76;
            this.nudVisionNumberMinimum.Dock = DockStyle.None;
            conditionRange.Controls.Add(this.nudVisionNumberMinimum);
            conditionRange.Controls.Add(CreateVisionLabel(UiText("Vision_Maximum")));
            this.nudVisionNumberMaximum = CreateVisionDecimal(-1000000000D, 1000000000D, 1000000000D);
            this.nudVisionNumberMaximum.Width = 76;
            this.nudVisionNumberMaximum.Dock = DockStyle.None;
            conditionRange.Controls.Add(this.nudVisionNumberMaximum);
            conditionRange.Visible = false;
            assistantLayout.Controls.Add(conditionRange, 0, 1);

            FlowLayoutPanel colorSettings = CreateVisionFlow();
            colorSettings.Controls.Add(CreateVisionLabel(UiText("Vision_Color")));
            this.txtVisionColorRgb = new TextBox
            {
                Width = 92,
                Text = "255,255,255",
                AccessibleName = UiText("Vision_Color"),
                AccessibleRole = AccessibleRole.Text,
                Margin = new Padding(0, 2, 8, 2)
            };
            colorSettings.Controls.Add(this.txtVisionColorRgb);
            colorSettings.Controls.Add(CreateVisionLabel(UiText("Vision_ColorTolerance")));
            this.nudVisionColorTolerance = CreateVisionNumber(0);
            this.nudVisionColorTolerance.Maximum = 255;
            this.nudVisionColorTolerance.Value = 16;
            this.nudVisionColorTolerance.Width = 52;
            this.nudVisionColorTolerance.Dock = DockStyle.None;
            colorSettings.Controls.Add(this.nudVisionColorTolerance);
            colorSettings.Controls.Add(CreateVisionLabel(UiText("Vision_ColorMinimumPixels")));
            this.nudVisionColorMinimumPixels = CreateVisionNumber(1);
            this.nudVisionColorMinimumPixels.Maximum = 100000;
            this.nudVisionColorMinimumPixels.Value = 10;
            this.nudVisionColorMinimumPixels.Width = 64;
            this.nudVisionColorMinimumPixels.Dock = DockStyle.None;
            colorSettings.Controls.Add(this.nudVisionColorMinimumPixels);
            colorSettings.Controls.Add(CreateVisionLabel(UiText("Vision_ColorMinimumRatio")));
            this.nudVisionColorMinimumRatio = CreateVisionDecimal(0D, 1D, 0D);
            this.nudVisionColorMinimumRatio.Width = 58;
            this.nudVisionColorMinimumRatio.Dock = DockStyle.None;
            colorSettings.Controls.Add(this.nudVisionColorMinimumRatio);
            colorSettings.Visible = false;
            assistantLayout.Controls.Add(colorSettings, 0, 2);

            Label conditionHint = CreateVisionStatusLabel(UiText("Vision_ConditionHint"));
            assistantLayout.Controls.Add(conditionHint, 0, 3);

            FlowLayoutPanel actionSettings = CreateVisionFlow();
            actionSettings.Controls.Add(CreateVisionLabel(UiText("Vision_AfterAction")));
            this.cbbVisionActionType = new ComboBox
            {
                Width = 112,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                AccessibleName = UiText("Vision_AfterAction"),
                Margin = new Padding(0, 2, 6, 2)
            };
            this.cbbVisionActionType.Items.Add(new VisionActionChoice(VisionActionType.None, UiText("Vision_ActionNone")));
            this.cbbVisionActionType.Items.Add(new VisionActionChoice(VisionActionType.LeftClick, UiText("Vision_ActionLeftClick")));
            this.cbbVisionActionType.Items.Add(new VisionActionChoice(VisionActionType.RightClick, UiText("Vision_ActionRightClick")));
            this.cbbVisionActionType.Items.Add(new VisionActionChoice(VisionActionType.DoubleClick, UiText("Vision_ActionDoubleClick")));
            this.cbbVisionActionType.Items.Add(new VisionActionChoice(VisionActionType.Scroll, UiText("Vision_ActionScroll")));
            this.cbbVisionActionType.SelectedIndexChanged += (sender, e) => this.UpdateVisionActionEditor();
            this.cbbVisionActionType.SelectedIndex = 0;
            actionSettings.Controls.Add(this.cbbVisionActionType);
            this.cbbVisionScrollDirection = new ComboBox
            {
                Width = 70,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                AccessibleName = UiText("Vision_ScrollDirection"),
                Margin = new Padding(0, 2, 4, 2)
            };
            this.cbbVisionScrollDirection.Items.Add(new VisionScrollChoice(VisionScrollDirection.Up, UiText("Vision_ScrollUp")));
            this.cbbVisionScrollDirection.Items.Add(new VisionScrollChoice(VisionScrollDirection.Down, UiText("Vision_ScrollDown")));
            this.cbbVisionScrollDirection.SelectedIndex = 1;
            actionSettings.Controls.Add(this.cbbVisionScrollDirection);
            this.nudVisionScrollAmount = CreateVisionNumber(1);
            this.nudVisionScrollAmount.Maximum = 100;
            this.nudVisionScrollAmount.Value = 3;
            this.nudVisionScrollAmount.Width = 48;
            this.nudVisionScrollAmount.Dock = DockStyle.None;
            actionSettings.Controls.Add(this.nudVisionScrollAmount);
            assistantLayout.Controls.Add(actionSettings, 0, 4);

            FlowLayoutPanel verificationSettings = CreateVisionFlow();
            this.chkVisionActionVerification = CreateVisionCheckBox(UiText("Vision_ActionVerification"));
            this.chkVisionActionVerification.CheckedChanged += (sender, e) => this.UpdateVisionVerificationEditor();
            this.chkVisionActionVerification.Visible = false;
            this.bVisionActionVerificationMenu = CreateVisionButton(
                UiText("Vision_ActionVerification") + "  ▼");
            this.bVisionActionVerificationMenu.Name = "bVisionActionVerificationMenu";
            ContextMenuStrip verificationMenu = new ContextMenuStrip();
            this.bVisionActionVerificationMenu.ContextMenuStrip = verificationMenu;
            this.bVisionActionVerificationMenu.Click += (sender, e) => verificationMenu.Show(
                this.bVisionActionVerificationMenu,
                new Point(0, this.bVisionActionVerificationMenu.Height));
            ToolStripMenuItem noVerification = new ToolStripMenuItem(
                UiText("Vision_VerificationNone"));
            noVerification.Click += (sender, e) => this.SetVisionVerificationMode(null);
            ToolStripMenuItem textVerification = new ToolStripMenuItem(
                UiText("Vision_AddTextWait"));
            textVerification.Click += (sender, e) => this.SetVisionVerificationMode(
                VisionConditionType.TextAppears);
            ToolStripMenuItem imageVerification = new ToolStripMenuItem(
                UiText("Vision_AddImageWait"));
            imageVerification.Click += (sender, e) => this.SetVisionVerificationMode(
                VisionConditionType.TemplateAppears);
            verificationMenu.Items.Add(noVerification);
            verificationMenu.Items.Add(textVerification);
            verificationMenu.Items.Add(imageVerification);
            verificationSettings.Controls.Add(this.bVisionActionVerificationMenu);
            this.lVisionVerificationKeyword = CreateVisionLabel(UiText("Vision_VerificationKeyword"));
            verificationSettings.Controls.Add(this.lVisionVerificationKeyword);
            this.cbbVisionVerificationType = new ComboBox
            {
                Width = 92,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                AccessibleName = UiText("Vision_VerificationType"),
                Margin = new Padding(0, 2, 6, 2)
            };
            this.cbbVisionVerificationType.Items.Add(new VisionVerificationChoice(
                VisionConditionType.TextAppears,
                UiText("Vision_VerificationText")));
            this.cbbVisionVerificationType.Items.Add(new VisionVerificationChoice(
                VisionConditionType.TemplateAppears,
                UiText("Vision_VerificationImage")));
            this.cbbVisionVerificationType.SelectedIndex = 0;
            this.cbbVisionVerificationType.SelectedIndexChanged += (sender, e) => this.UpdateVisionVerificationEditor();
            this.txtVisionVerificationKeyword = new TextBox
            {
                Width = 120,
                AccessibleName = UiText("Vision_VerificationKeyword"),
                AccessibleRole = AccessibleRole.Text,
                Margin = new Padding(0, 2, 0, 2)
            };
            verificationSettings.Controls.Add(this.txtVisionVerificationKeyword);

            FlowLayoutPanel verificationAdvancedContent = CreateVisionVerticalFlow();
            FlowLayoutPanel verificationTypeSettings = CreateVisionFlow();
            verificationTypeSettings.Controls.Add(CreateVisionLabel(UiText("Vision_VerificationType")));
            verificationTypeSettings.Controls.Add(this.cbbVisionVerificationType);
            verificationAdvancedContent.Controls.Add(verificationTypeSettings);
            this.chkVisionVerificationSeparateRegion = CreateVisionCheckBox(UiText("Vision_VerificationRegion"));
            this.chkVisionVerificationSeparateRegion.CheckedChanged += (sender, e) => this.UpdateVisionVerificationEditor();
            verificationAdvancedContent.Controls.Add(this.chkVisionVerificationSeparateRegion);
            this.bVisionSelectVerificationRegion = CreateVisionButton(UiText("Vision_SelectVerificationRegion"));
            this.bVisionSelectVerificationRegion.Click += this.bVisionSelectVerificationRegion_Click;
            verificationAdvancedContent.Controls.Add(this.bVisionSelectVerificationRegion);
            this.lVisionVerificationHint = CreateVisionStatusLabel(string.Empty);
            verificationAdvancedContent.Controls.Add(this.lVisionVerificationHint);
            this.visionVerificationAdvancedPanel = CreateVisionCollapsiblePanel(
                UiText("Vision_MoreVerificationSettings"),
                UiText("Vision_LessVerificationSettings"),
                verificationAdvancedContent);
            this.visionVerificationAdvancedPanel.Controls[0].Visible = false;
            verificationSettings.Controls.Add(this.visionVerificationAdvancedPanel);
            assistantLayout.Controls.Add(verificationSettings, 0, 5);

            FlowLayoutPanel confirmSettings = CreateVisionFlow();
            this.bVisionConfirmAction = CreateVisionButton(UiText("Vision_ConfirmAction"));
            this.bVisionConfirmAction.Name = "bVisionConfirmAction";
            this.bVisionConfirmAction.Click += this.bConfirmVisionAction_Click;
            StyleVisionPrimaryButton(this.bVisionConfirmAction);
            confirmSettings.Controls.Add(this.bVisionConfirmAction);
            this.bVisionCancelAction = CreateVisionButton(UiText("Vision_CancelAction"));
            this.bVisionCancelAction.Name = "bVisionCancelAction";
            this.bVisionCancelAction.Click += this.bCancelVisionAction_Click;
            confirmSettings.Controls.Add(this.bVisionCancelAction);
            confirmSettings.Controls.Add(CreateVisionStatusLabel(UiText("Vision_ConfirmActionHint")));

            FlowLayoutPanel stepActions = CreateVisionFlow();
            stepActions.Controls.Add(CreateVisionLabel(UiText("Vision_Steps")));
            this.cbbVisionSteps = new ListBox
            {
                Width = 210,
                Height = 88,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.One,
                DisplayMember = "Name",
                AccessibleName = UiText("Vision_Steps"),
                AccessibleRole = AccessibleRole.List,
                Margin = new Padding(0, 2, 6, 2)
            };
            this.cbbVisionSteps.SelectedIndexChanged += this.cbbVisionSteps_SelectedIndexChanged;
            stepActions.Controls.Add(this.cbbVisionSteps);
            Button bRemoveVisionStep = CreateVisionButton(UiText("Vision_RemoveStep"));
            bRemoveVisionStep.Click += this.bRemoveVisionStep_Click;
            stepActions.Controls.Add(bRemoveVisionStep);
            assistantLayout.Controls.Add(stepActions, 0, 7);
            stepActions.Visible = false;

            FlowLayoutPanel waitAdvancedContent = CreateVisionFlow();
            waitAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_Confirmations")));
            this.nudVisionConfirmations = CreateVisionNumber(1);
            this.nudVisionConfirmations.Maximum = 10;
            this.nudVisionConfirmations.Value = 2;
            this.nudVisionConfirmations.Width = 52;
            this.nudVisionConfirmations.Dock = DockStyle.None;
            waitAdvancedContent.Controls.Add(this.nudVisionConfirmations);
            waitAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_PollMilliseconds")));
            this.nudVisionPollInterval = CreateVisionNumber(10);
            this.nudVisionPollInterval.Maximum = 60000;
            this.nudVisionPollInterval.Value = 150;
            this.nudVisionPollInterval.Width = 72;
            this.nudVisionPollInterval.Dock = DockStyle.None;
            waitAdvancedContent.Controls.Add(this.nudVisionPollInterval);
            waitAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_TimeoutMilliseconds")));
            this.nudVisionTimeout = CreateVisionNumber(10);
            this.nudVisionTimeout.Maximum = 3600000;
            this.nudVisionTimeout.Value = 8000;
            this.nudVisionTimeout.Width = 80;
            this.nudVisionTimeout.Dock = DockStyle.None;
            waitAdvancedContent.Controls.Add(this.nudVisionTimeout);
            waitAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_Retries")));
            this.nudVisionRetries = CreateVisionNumber(0);
            this.nudVisionRetries.Maximum = 100;
            this.nudVisionRetries.Value = 1;
            this.nudVisionRetries.Width = 52;
            this.nudVisionRetries.Dock = DockStyle.None;
            waitAdvancedContent.Controls.Add(this.nudVisionRetries);
            waitAdvancedContent.Controls.Add(CreateVisionLabel(UiText("Vision_FailurePolicy")));
            this.cbbVisionFailurePolicy = new ComboBox
            {
                Width = 110,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                AccessibleName = UiText("Vision_FailurePolicy"),
                Margin = new Padding(0, 2, 0, 2)
            };
            this.cbbVisionFailurePolicy.Items.Add(new VisionFailureChoice(VisionFailurePolicy.Stop, UiText("Vision_StopPolicy")));
            this.cbbVisionFailurePolicy.Items.Add(new VisionFailureChoice(VisionFailurePolicy.Skip, UiText("Vision_SkipPolicy")));
            this.cbbVisionFailurePolicy.SelectedIndex = 0;
            waitAdvancedContent.Controls.Add(this.cbbVisionFailurePolicy);
            TableLayoutPanel waitAdvancedPanel = CreateVisionCollapsiblePanel(
                UiText("Vision_MoreWaitSettings"),
                UiText("Vision_LessWaitSettings"),
                waitAdvancedContent);
            this.RegisterVisionAdvancedModule(waitAdvancedPanel);

            FlowLayoutPanel assistantActions = CreateVisionFlow();
            this.bVisionRunSteps = CreateVisionButton(UiText("Vision_RunSteps"));
            StyleVisionPrimaryButton(this.bVisionRunSteps);
            this.bVisionRunSteps.Click += this.bRunVisionAssistant_Click;
            assistantActions.Controls.Add(this.bVisionRunSteps);
            this.bVisionStopSteps = CreateVisionButton(UiText("Vision_StopSteps"));
            this.bVisionStopSteps.Enabled = false;
            this.bVisionStopSteps.Click += this.bStopVisionAssistant_Click;
            assistantActions.Controls.Add(this.bVisionStopSteps);
            assistantLayout.Controls.Add(assistantActions, 0, 10);
            assistantLayout.Controls.Add(confirmSettings, 0, 11);
            this.lVisionAssistantStatus = CreateVisionStatusLabel(UiText("Vision_AssistantHint"));
            assistantLayout.Controls.Add(this.lVisionAssistantStatus, 0, 12);
            this.visionAssistantLogHost = CreateVisionGrid(1);
            FlowLayoutPanel assistantLogHeader = CreateVisionFlow();
            assistantLogHeader.Controls.Add(CreateVisionLabel(UiText("Vision_AssistantLog")));
            this.bToggleVisionAssistantLog = CreateVisionButton(UiText("Vision_AssistantLogShow"));
            this.bToggleVisionAssistantLog.Name = "bToggleVisionAssistantLog";
            this.bToggleVisionAssistantLog.Click += this.bToggleVisionAssistantLog_Click;
            assistantLogHeader.Controls.Add(this.bToggleVisionAssistantLog);
            this.visionAssistantLogHost.Controls.Add(assistantLogHeader, 0, 0);
            assistantLayout.Controls.Add(this.visionAssistantLogHost, 0, 13);
            this.txtVisionAssistantLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Height = 64,
                Multiline = true,
                ReadOnly = true,
                AccessibleName = UiText("Vision_AssistantLog"),
                AccessibleRole = AccessibleRole.Text,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                ForeColor = Color.FromArgb(80, 80, 80),
                BorderStyle = BorderStyle.FixedSingle,
                TabStop = false,
                Margin = new Padding(0, 4, 0, 0)
            };
            this.SetVisionAssistantLogExpanded(false);
            AttachVisionSectionContent(assistantSection, assistantLayout);

            GroupBox settingsSection = CreateVisionSection(UiText("Vision_SectionSettings"));
            TableLayoutPanel settingsLayout = CreateVisionGrid(1);
            FlowLayoutPanel settingsActions = CreateVisionFlow();
            settingsActions.Controls.Add(this.bVisionAdvancedSettings);
            settingsLayout.Controls.Add(settingsActions, 0, 0);
            settingsLayout.Controls.Add(
                CreateVisionStatusLabel(UiText("Vision_AdvancedSettingsHint")),
                0,
                1);
            AttachVisionSectionContent(settingsSection, settingsLayout);

            page.Controls.Add(recognitionSection);
            page.Controls.Add(assistantSection);
            settingsPage.Controls.Add(settingsSection);

            this.cbbVisionConditionType.SelectedIndexChanged += (sender, e) =>
            {
                VisionConditionType type = this.ReadVisionConditionType();
                bool isNumber = type == VisionConditionType.NumberInRange;
                bool isTemplate = type == VisionConditionType.TemplateAppears ||
                    type == VisionConditionType.TemplateDisappears;
                bool isColor = type == VisionConditionType.ColorAppears ||
                    type == VisionConditionType.ColorDisappears;
                conditionRange.Visible = isNumber;
                colorSettings.Visible = isColor;
                conditionHint.Text = isTemplate
                    ? UiText("Vision_TemplateConditionHint")
                    : isColor
                        ? UiText("Vision_ColorConditionHint")
                    : UiText("Vision_ConditionHint");
                conditionSourceHint.Visible = !isTemplate && !isColor;
                assistantSection.PerformLayout();
            };
            this.cbbVisionConditionType.SelectedIndex = 0;
            this.UpdateVisionActionEditor();
            this.UpdateVisionVerificationEditor();

            this.visionPythonWorker = new VisionPythonWorkerTextRecognizer();
            this.visionTextRecognizer = new VisionAutoTextRecognizer(
                new VisionOnnxTextRecognizer(),
                new VisionTesseractRecognizer("tesseract.exe"),
                this.visionPythonWorker);
            this.RefreshVisionAssistantSteps();
            this.tcRobotInstruction.TabPages.Add(visionTab);
            this.tcRobotInstruction.TabPages.Add(visionSettingsTab);
            FitVisionSections(page);
            FitVisionSections(settingsPage);
            fitModeTabs();
            this.FormClosed += this.Socket_RobotForm_FormClosed;
        }

        private void RegisterVisionAdvancedModule(Control module)
        {
            if (module == null || this.visionAdvancedStorage == null)
            {
                return;
            }
            this.visionAdvancedModules.Add(module);
            this.visionAdvancedStorage.Controls.Add(module);
        }

        private void bVisionAdvancedSettings_Click(object sender, EventArgs e)
        {
            if (this.visionAdvancedStorage == null || this.visionAdvancedModules.Count == 0)
            {
                return;
            }

            using (Form settingsForm = new Form())
            using (TableLayoutPanel root = new TableLayoutPanel())
            using (Label hint = CreateVisionStatusLabel(UiText("Vision_AdvancedSettingsHint")))
            using (FlowLayoutPanel settingsFlow = CreateVisionVerticalFlow())
            using (Button closeButton = CreateVisionButton(UiText("UI_Close")))
            {
                settingsForm.Text = UiText("Vision_AdvancedSettings");
                settingsForm.StartPosition = FormStartPosition.CenterParent;
                settingsForm.Size = new Size(720, 620);
                settingsForm.MinimumSize = new Size(560, 420);
                settingsForm.MinimizeBox = false;
                settingsForm.MaximizeBox = false;
                settingsForm.ShowInTaskbar = false;
                settingsForm.AutoScaleMode = AutoScaleMode.Dpi;
                settingsForm.AutoScroll = true;
                settingsForm.Font = this.Font;

                root.Dock = DockStyle.Fill;
                root.ColumnCount = 1;
                root.RowCount = 3;
                root.Padding = new Padding(8);
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                hint.Dock = DockStyle.Fill;
                root.Controls.Add(hint, 0, 0);
                settingsFlow.Dock = DockStyle.Fill;
                settingsFlow.AutoScroll = true;
                settingsFlow.WrapContents = false;
                settingsFlow.Resize += (controlSender, resizeEvent) =>
                {
                    int width = Math.Max(1, settingsFlow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
                    foreach (Control module in this.visionAdvancedModules)
                    {
                        module.Width = width;
                    }
                };

                foreach (Control module in this.visionAdvancedModules)
                {
                    this.visionAdvancedStorage.Controls.Remove(module);
                    settingsFlow.Controls.Add(module);
                }
                root.Controls.Add(settingsFlow, 0, 1);

                FlowLayoutPanel actions = CreateVisionFlow();
                closeButton.DialogResult = DialogResult.Cancel;
                actions.FlowDirection = FlowDirection.RightToLeft;
                actions.WrapContents = false;
                actions.Controls.Add(closeButton);
                root.Controls.Add(actions, 0, 2);
                settingsForm.Controls.Add(root);
                settingsForm.CancelButton = closeButton;

                try
                {
                    settingsForm.ShowDialog(this);
                }
                finally
                {
                    foreach (Control module in this.visionAdvancedModules)
                    {
                        settingsFlow.Controls.Remove(module);
                        this.visionAdvancedStorage.Controls.Add(module);
                    }
                }
            }
        }

        private static GroupBox CreateVisionSection(string title)
        {
            return new GroupBox
            {
                Text = title,
                AutoSize = false,
                Dock = DockStyle.None,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(31, 41, 55),
                Padding = new Padding(10, 24, 10, 10),
                Margin = new Padding(0, 0, 0, 10)
            };
        }

        private static TableLayoutPanel CreateVisionGrid(int columns)
        {
            TableLayoutPanel grid = new TableLayoutPanel
            {
                ColumnCount = columns,
                RowCount = 0,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                Padding = new Padding(0),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            for (int index = 0; index < columns; index++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
            }
            return grid;
        }

        private static FlowLayoutPanel CreateVisionFlow()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Padding = new Padding(0, 3, 0, 3),
                Margin = new Padding(0)
            };
        }

        private static FlowLayoutPanel CreateVisionVerticalFlow()
        {
            FlowLayoutPanel panel = CreateVisionFlow();
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            return panel;
        }

        private static Button CreateVisionButton(string text)
        {
            return new Button
            {
                AutoSize = true,
                Text = text,
                AccessibleName = text,
                AccessibleRole = AccessibleRole.PushButton,
                UseVisualStyleBackColor = true,
                MinimumSize = new Size(76, 27),
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 3, 8, 3)
            };
        }

        private static void StyleVisionPrimaryButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.UseVisualStyleBackColor = false;
            button.BackColor = Color.FromArgb(0, 120, 215);
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(0, 84, 153);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 102, 184);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 84, 153);
        }

        private static CheckBox CreateVisionCheckBox(string text)
        {
            return new CheckBox
            {
                AutoSize = true,
                Text = text,
                AccessibleName = text,
                AccessibleRole = AccessibleRole.CheckButton,
                Margin = new Padding(0, 5, 10, 3)
            };
        }

        private static Label CreateVisionLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                AccessibleName = text,
                AccessibleRole = AccessibleRole.StaticText,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(55, 65, 81),
                Margin = new Padding(0, 5, 8, 2)
            };
        }

        private static Label CreateVisionStatusLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                MaximumSize = new Size(0, 0),
                AutoEllipsis = false,
                AccessibleName = text,
                AccessibleRole = AccessibleRole.StaticText,
                Tag = "VisionStatusLabel",
                Text = text,
                ForeColor = Color.FromArgb(107, 114, 128),
                Margin = new Padding(0, 5, 0, 5),
                Padding = new Padding(0, 1, 0, 1)
            };
        }

        private static PictureBox CreateVisionPictureBox()
        {
            return new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                AccessibleRole = AccessibleRole.Graphic,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 2, 4, 4)
            };
        }

        private static TableLayoutPanel CreateVisionCollapsiblePanel(
            string collapsedText,
            string expandedText,
            Control content)
        {
            TableLayoutPanel wrapper = CreateVisionGrid(1);
            Button toggle = CreateVisionButton(collapsedText + "  ▶");
            toggle.TextAlign = ContentAlignment.MiddleLeft;
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.FlatAppearance.BorderSize = 0;
            toggle.BackColor = Color.FromArgb(245, 247, 250);
            toggle.ForeColor = Color.FromArgb(55, 65, 81);
            content.Visible = false;
            content.Margin = new Padding(8, 0, 0, 4);
            wrapper.Controls.Add(toggle, 0, 0);
            wrapper.Controls.Add(content, 0, 1);
            toggle.Click += (sender, e) =>
            {
                content.Visible = !content.Visible;
                toggle.Text = (content.Visible ? expandedText + "  ▼" : collapsedText + "  ▶");
                wrapper.PerformLayout();
            };
            return wrapper;
        }

        private static void AttachVisionSectionContent(GroupBox section, Control content)
        {
            content.Dock = DockStyle.Top;
            content.Margin = new Padding(0);
            section.Controls.Add(content);
            bool updating = false;
            Action updateLayout = () =>
            {
                if (updating)
                {
                    return;
                }
                updating = true;
                try
                {
                    content.Width = Math.Max(1, section.ClientSize.Width - section.Padding.Horizontal - 2);
                    SetVisionStatusLabelWidths(content, content.Width);
                    int contentHeight = content.GetPreferredSize(new Size(content.Width, 0)).Height;
                    section.Height = section.Padding.Top + contentHeight + section.Padding.Bottom + 2;
                }
                finally
                {
                    updating = false;
                }
            };
            section.Resize += (sender, e) => updateLayout();
            content.Layout += (sender, e) => updateLayout();
            updateLayout();
        }

        private static void SetVisionStatusLabelWidths(Control root, int width)
        {
            if (root == null)
            {
                return;
            }

            foreach (Control child in root.Controls)
            {
                Label label = child as Label;
                if (label != null && string.Equals(
                    label.Tag as string,
                    "VisionStatusLabel",
                    StringComparison.Ordinal))
                {
                    label.MaximumSize = new Size(Math.Max(1, width), 0);
                }
                if (child.HasChildren)
                {
                    SetVisionStatusLabelWidths(child, width);
                }
            }
        }

        private static void FitVisionSections(FlowLayoutPanel page)
        {
            int scrollBarWidth = page.VerticalScroll.Visible
                ? SystemInformation.VerticalScrollBarWidth
                : 0;
            int width = Math.Max(
                1,
                page.ClientSize.Width - page.Padding.Horizontal - scrollBarWidth - 4);
            foreach (Control section in page.Controls)
            {
                section.Width = width;
            }
        }

        private static void FitVisionModeTabs(
            TabControl tabs,
            Control selectedContent,
            int minimumHeight)
        {
            if (tabs == null || selectedContent == null || tabs.ClientSize.Width <= 0)
            {
                return;
            }

            int contentWidth = Math.Max(1, tabs.ClientSize.Width - (tabs.Padding.X * 2) - 12);
            int contentHeight = selectedContent.GetPreferredSize(new Size(contentWidth, 0)).Height;
            int tabHeaderHeight = Math.Max(24, tabs.ItemSize.Height) + 12;
            int targetHeight = Math.Max(minimumHeight, contentHeight + tabHeaderHeight);
            if (tabs.Height != targetHeight)
            {
                tabs.Height = targetHeight;
            }
        }

        private static NumericUpDown CreateVisionNumber(int minimum)
        {
            return new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = minimum,
                Maximum = 100000,
                Increment = 1,
                ThousandsSeparator = false,
                AccessibleRole = AccessibleRole.SpinButton,
                AccessibleName = "Numeric input",
                TabStop = true
            };
        }

        private static NumericUpDown CreateVisionDecimal(
            double minimum,
            double maximum,
            double value)
        {
            NumericUpDown editor = new NumericUpDown
            {
                Minimum = (decimal)minimum,
                Maximum = (decimal)maximum,
                Value = (decimal)value,
                DecimalPlaces = 2,
                Increment = 0.1M,
                ThousandsSeparator = false,
                Margin = new Padding(0, 2, 8, 0),
                AccessibleRole = AccessibleRole.SpinButton,
                AccessibleName = "Decimal input",
                TabStop = true
            };
            return editor;
        }

        private static void AddVisionNumber(
            TableLayoutPanel root,
            string labelText,
            NumericUpDown editor,
            int labelColumn,
            int row)
        {
            Label label = new Label
            {
                Dock = DockStyle.Fill,
                Text = labelText,
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleName = labelText,
                AccessibleRole = AccessibleRole.StaticText
            };
            editor.AccessibleName = labelText;
            root.Controls.Add(label, labelColumn, row);
            root.Controls.Add(editor, labelColumn + 1, row);
        }

        private void InitVisionProfile()
        {
            this.RefreshVisionWindows();
            Socket_VisionProfile profile = this.sriSelect == null
                ? new Socket_VisionProfile()
                : this.sriSelect.VisionProfile;
            this.SetVisionNumber(this.nudVisionX, profile.Region.X);
            this.SetVisionNumber(this.nudVisionY, profile.Region.Y);
            this.SetVisionNumber(this.nudVisionWidth, Math.Max(1, profile.Region.Width));
            this.SetVisionNumber(this.nudVisionHeight, Math.Max(1, profile.Region.Height));
            this.chkVisionNormalized.Checked = profile.Region.UseNormalizedCoordinates;
            VisionCaptureSettings captureSettings = profile.CaptureSettings ?? new VisionCaptureSettings();
            this.chkVisionFixedClientSize.Checked = captureSettings.RequireExactClientSize;
            this.SetVisionNumber(
                this.nudVisionRequiredWidth,
                captureSettings.RequiredClientWidth <= 0 ? 1280 : captureSettings.RequiredClientWidth);
            this.SetVisionNumber(
                this.nudVisionRequiredHeight,
                captureSettings.RequiredClientHeight <= 0 ? 720 : captureSettings.RequiredClientHeight);
            this.SetVisionNumber(this.nudVisionCaptureInterval, captureSettings.MinimumIntervalMilliseconds);
            this.chkVisionSkipUnchanged.Checked = captureSettings.SkipUnchangedFrames;
            this.SetVisionNumber(this.nudVisionHistoryLimit, captureSettings.HistoryLimit);
            this.chkVisionSaveFailureSnapshots.Checked = captureSettings.SaveFailureSnapshots;
            this.SelectVisionCaptureSource(captureSettings.SourceMode);
            VisionOcrOptions ocrOptions = profile.OcrOptions ?? new VisionOcrOptions();
            this.SetVisionNumber(this.nudVisionOcrScale, ocrOptions.ScaleFactor);
            this.chkVisionOcrBinary.Checked = ocrOptions.UseBinaryThreshold;
            this.SetVisionNumber(this.nudVisionOcrThreshold, ocrOptions.BinaryThreshold);
            this.SetVisionDecimal(this.nudVisionOcrContrast, ocrOptions.Contrast);
            this.chkVisionOcrAdaptive.Checked = ocrOptions.UseAdaptiveThreshold;
            this.SetVisionNumber(this.nudVisionOcrAdaptiveWindow, ocrOptions.AdaptiveThresholdWindowSize);
            this.SetVisionNumber(this.nudVisionOcrAdaptiveOffset, ocrOptions.AdaptiveThresholdOffset);
            this.chkVisionOcrInvert.Checked = ocrOptions.Invert;
            this.chkVisionOcrDenoise.Checked = ocrOptions.UseDenoise;
            this.chkVisionOcrSharpen.Checked = ocrOptions.UseSharpen;
            this.txtVisionOcrWhitelist.Text = ocrOptions.CharacterWhitelist ?? string.Empty;
            this.SelectVisionOcrEngine(ocrOptions.Engine);
            this.txtVisionOcrModelDirectory.Text = ocrOptions.OnnxModelDirectory ?? "models\\ocr";
            this.SetVisionDecimal(this.nudVisionOcrDetectionThreshold, ocrOptions.OnnxDetectionThreshold);
            this.SetVisionDecimal(this.nudVisionOcrRecognitionThreshold, ocrOptions.OnnxRecognitionThreshold);
            this.SetVisionNumber(this.nudVisionOcrMaxImageSide, ocrOptions.OnnxMaxImageSide);
            this.txtVisionPythonExecutable.Text = ocrOptions.PythonExecutablePath ?? string.Empty;
            this.txtVisionPythonWorkerScript.Text = ocrOptions.PythonWorkerScriptPath ?? string.Empty;
            this.SetVisionNumber(
                this.nudVisionPythonWorkerTimeout,
                ocrOptions.PythonWorkerTimeoutMilliseconds);
            this.UpdateVisionOcrModelStatus();
            if (ocrOptions.Engine == VisionOcrEngine.PythonWorker)
            {
                this.BeginVisionPythonPrewarm();
            }
            this.txtVisionOcrKeyword.Text = profile.OcrCondition == null
                ? string.Empty
                : profile.OcrCondition.ExpectedText ?? string.Empty;
            this.RefreshVisionAssistantSteps();
            this.UpdateVisionFixedClientSizeUi();
        }

        private void RefreshVisionWindows()
        {
            if (this.cbbVisionWindows == null)
            {
                return;
            }

            long selectedHandle = this.sriSelect == null || this.sriSelect.VisionProfile == null
                ? 0L
                : this.sriSelect.VisionProfile.WindowHandle;
            string selectedTitle = this.sriSelect == null || this.sriSelect.VisionProfile == null
                ? string.Empty
                : this.sriSelect.VisionProfile.WindowTitle;
            string selectedProcess = this.sriSelect == null || this.sriSelect.VisionProfile == null
                ? string.Empty
                : this.sriSelect.VisionProfile.ProcessName;

            int injectedTargetWindowIndex = -1;
            this.cbbVisionWindows.BeginUpdate();
            try
            {
                this.cbbVisionWindows.Items.Clear();
                IList<VisionWindowInfo> windows = VisionWindowService.EnumerateVisibleWindows(
                    Process.GetCurrentProcess().Id);
                VisionWindowInfo injectedTargetWindow = null;
                using (Process currentProcess = Process.GetCurrentProcess())
                {
                    VisionWindowService.TryFindInjectedTargetWindow(
                        windows,
                        currentProcess.Id,
                        currentProcess.ProcessName,
                        out injectedTargetWindow);
                }
                int selectedIndex = -1;
                int savedSelectionIndex = -1;
                for (int i = 0; i < windows.Count; i++)
                {
                    VisionWindowInfo window = windows[i];
                    this.cbbVisionWindows.Items.Add(window);
                    if (injectedTargetWindow != null &&
                        window.Handle == injectedTargetWindow.Handle)
                    {
                        injectedTargetWindowIndex = i;
                    }
                    if ((selectedHandle != 0 && window.Handle.ToInt64() == selectedHandle) ||
                        (savedSelectionIndex < 0 &&
                         string.Equals(window.WindowTitle, selectedTitle, StringComparison.Ordinal) &&
                         string.Equals(window.ProcessName, selectedProcess, StringComparison.Ordinal)))
                    {
                        savedSelectionIndex = i;
                    }
                }

                selectedIndex = injectedTargetWindowIndex >= 0
                    ? injectedTargetWindowIndex
                    : savedSelectionIndex;

                this.cbbVisionWindows.SelectedIndex = selectedIndex;
            }
            finally
            {
                this.cbbVisionWindows.EndUpdate();
            }

            if (this.cbbVisionWindows.SelectedIndex < 0)
            {
                this.SetVisionStatus(UiText("Vision_SelectWindowHint"));
            }
            else if (injectedTargetWindowIndex >= 0)
            {
                VisionWindowInfo window = this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
                if (window != null)
                {
                    this.SetVisionStatus(string.Format(
                        UiText("Vision_WindowAutoBound"),
                        window.DisplayName));
                }
            }
        }

        private void cbbVisionWindows_SelectedIndexChanged(object sender, EventArgs e)
        {
            VisionWindowInfo window = this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
            if (window == null)
            {
                return;
            }

            Size selectedClientSize = GetVisionWindowClientSizeForDisplay(window);
            this.SetVisionStatus(string.Format(
                UiText("Vision_WindowSelected"),
                window.DisplayName,
                selectedClientSize.Width,
                selectedClientSize.Height));
            this.UpdateVisionFixedClientSizeUi();
        }

        private void bVisionApplyFixedSize_Click(object sender, EventArgs e)
        {
            this.chkVisionFixedClientSize.Checked = true;
            this.SetVisionNumber(this.nudVisionRequiredWidth, 1280);
            this.SetVisionNumber(this.nudVisionRequiredHeight, 720);
            this.chkVisionNormalized.Checked = false;
            this.SetVisionStatus(UiText("Vision_Fixed1280Applied"));
            this.UpdateVisionFixedClientSizeUi();
        }

        private void UpdateVisionFixedClientSizeUi()
        {
            if (this.chkVisionFixedClientSize == null ||
                this.nudVisionRequiredWidth == null ||
                this.nudVisionRequiredHeight == null)
            {
                return;
            }

            bool fixedSize = this.chkVisionFixedClientSize.Checked;
            this.nudVisionRequiredWidth.Enabled = fixedSize;
            this.nudVisionRequiredHeight.Enabled = fixedSize;
            if (this.lVisionFixedClientSizeStatus == null)
            {
                return;
            }

            VisionWindowInfo window = this.cbbVisionWindows == null
                ? null
                : this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
            if (!fixedSize)
            {
                this.lVisionFixedClientSizeStatus.Text = UiText("Vision_ClientSizeAny");
                return;
            }
            int requiredWidth = (int)this.nudVisionRequiredWidth.Value;
            int requiredHeight = (int)this.nudVisionRequiredHeight.Value;
            if (window == null)
            {
                this.lVisionFixedClientSizeStatus.Text = string.Format(
                    UiText("Vision_ClientSizeRequiredOnly"),
                    requiredWidth,
                    requiredHeight);
                return;
            }
            Size currentClientSize;
            if (!TryGetVisionWindowClientSizeForDisplay(window, out currentClientSize))
            {
                this.lVisionFixedClientSizeStatus.Text = string.Format(
                    "{0}；要求客户区：{1}×{2}",
                    UiText("Vision_ClientSizeUnavailable"),
                    requiredWidth,
                    requiredHeight);
                return;
            }
            this.lVisionFixedClientSizeStatus.Text = string.Format(
                UiText("Vision_ClientSizeStatus"),
                currentClientSize.Width,
                currentClientSize.Height,
                requiredWidth,
                requiredHeight,
                currentClientSize.Width == requiredWidth &&
                    currentClientSize.Height == requiredHeight
                    ? UiText("Vision_ClientSizeValid")
                    : UiText("Vision_ClientSizeInvalid"));
        }

        private static Size GetVisionWindowClientSizeForDisplay(VisionWindowInfo window)
        {
            Size clientSize;
            return TryGetVisionWindowClientSizeForDisplay(window, out clientSize)
                ? clientSize
                : (window == null ? Size.Empty : window.ClientSize);
        }

        private static bool TryGetVisionWindowClientSizeForDisplay(
            VisionWindowInfo window,
            out Size clientSize)
        {
            clientSize = Size.Empty;
            if (window == null)
            {
                return false;
            }
            if (!VisionWindowService.IsWindowUsable(window.Handle))
            {
                return false;
            }

            Rectangle clientBounds;
            return VisionWindowService.TryGetClientBounds(
                window.Handle,
                out clientBounds,
                out clientSize);
        }

        private void bRefreshVisionWindows_Click(object sender, EventArgs e)
        {
            this.RefreshVisionWindows();
        }

        private void bVisionSelectRegion_Click(object sender, EventArgs e)
        {
            VisionWindowInfo window = this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
            if (window == null)
            {
                this.SetVisionStatus(UiText("Vision_SelectWindowHint"));
                return;
            }

            Rectangle clientBounds;
            Size clientSize;
            if (!VisionWindowService.TryGetClientBounds(window.Handle, out clientBounds, out clientSize) ||
                clientBounds.IsEmpty ||
                !SystemInformation.VirtualScreen.Contains(clientBounds))
            {
                this.SetVisionStatus(UiText("Vision_RegionPickerNotVisible"));
                return;
            }

            window.ClientBoundsScreen = clientBounds;
            using (VisionRegionPickerForm picker = new VisionRegionPickerForm(
                clientBounds,
                UiText("Vision_RegionPickerHint")))
            {
                DialogResult result = picker.ShowDialog(this);
                if (result != DialogResult.OK)
                {
                    this.SetVisionStatus(UiText("Vision_RegionSelectionCancelled"));
                    return;
                }

                Rectangle selected = picker.SelectedRectangle;
                if (selected.Width < 2 || selected.Height < 2 ||
                    selected.Right > clientSize.Width || selected.Bottom > clientSize.Height)
                {
                    this.SetVisionStatus(UiText("Vision_RegionSelectionInvalid"));
                    return;
                }

                this.chkVisionNormalized.Checked = false;
                this.SetVisionNumber(this.nudVisionX, selected.X);
                this.SetVisionNumber(this.nudVisionY, selected.Y);
                this.SetVisionNumber(this.nudVisionWidth, selected.Width);
                this.SetVisionNumber(this.nudVisionHeight, selected.Height);
                this.SetVisionStatus(string.Format(
                    UiText("Vision_RegionSelected"),
                    selected.X,
                    selected.Y,
                    selected.Width,
                    selected.Height));
            }

            // 选择层已关闭后再截图，避免选择层本身被捕获到预览中。
            this.bCaptureVision_Click(this, EventArgs.Empty);
        }

        private void bVisionSelectVerificationRegion_Click(object sender, EventArgs e)
        {
            VisionWindowInfo window = this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
            if (window == null)
            {
                this.SetVisionStatus(UiText("Vision_SelectWindowHint"));
                return;
            }
            Rectangle clientBounds;
            Size clientSize;
            if (!VisionWindowService.TryGetClientBounds(window.Handle, out clientBounds, out clientSize) ||
                clientBounds.IsEmpty ||
                !SystemInformation.VirtualScreen.Contains(clientBounds))
            {
                this.SetVisionStatus(UiText("Vision_RegionPickerNotVisible"));
                return;
            }
            using (VisionRegionPickerForm picker = new VisionRegionPickerForm(
                clientBounds,
                UiText("Vision_RegionPickerHint")))
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                {
                    this.SetVisionStatus(UiText("Vision_RegionSelectionCancelled"));
                    return;
                }
                Rectangle selected = picker.SelectedRectangle;
                if (selected.Width < 2 || selected.Height < 2 ||
                    selected.Right > clientSize.Width || selected.Bottom > clientSize.Height)
                {
                    this.SetVisionStatus(UiText("Vision_RegionSelectionInvalid"));
                    return;
                }
                this.visionVerificationRegion = new VisionRegion
                {
                    X = selected.X,
                    Y = selected.Y,
                    Width = selected.Width,
                    Height = selected.Height,
                    UseNormalizedCoordinates = false,
                    ReferenceWidth = clientSize.Width,
                    ReferenceHeight = clientSize.Height
                };
                this.chkVisionVerificationSeparateRegion.Checked = true;
                this.SetVisionStatus(string.Format(
                    UiText("Vision_RegionSelected"),
                    selected.X,
                    selected.Y,
                    selected.Width,
                    selected.Height));
            }
        }

        private void bCaptureVision_Click(object sender, EventArgs e)
        {
            VisionWindowInfo window = this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
            if (window == null)
            {
                this.SetVisionStatus(UiText("Vision_SelectWindowHint"));
                return;
            }

            try
            {
                VisionRegion region = this.ReadVisionRegion();
                using (VisionCaptureResult captureResult = VisionWindowService.CaptureClientRegionDetailed(
                    window.Handle,
                    region,
                    this.ReadVisionCaptureSettings()))
                {
                    Bitmap capture = new Bitmap(captureResult.Image);
                    this.DisposeVisionCapture();
                    this.visionPreview = capture;
                    this.visionPreviewOriginRegion = new VisionRegion
                    {
                        X = region.Resolve(window.ClientSize).X,
                        Y = region.Resolve(window.ClientSize).Y,
                        Width = region.Resolve(window.ClientSize).Width,
                        Height = region.Resolve(window.ClientSize).Height
                    };
                    this.visionPreviewSelection = Rectangle.Empty;
                    this.pbVisionPreview.Image = this.visionPreview;
                    this.UpdateVisionPreviewEmptyState();
                    this.AddVisionHistory(
                        capture,
                        string.Format(
                            "{0} {1}x{2} {3} {4:0.0}/{5:0.0}",
                            DateTime.Now.ToString("HH:mm:ss"),
                            capture.Width,
                            capture.Height,
                            captureResult.SourceMode,
                            captureResult.MeanBrightness,
                            captureResult.Contrast));
                    this.SetVisionStatus(string.Format(
                        UiText("Vision_CaptureQuality"),
                        captureResult.SourceMode,
                        captureResult.MeanBrightness,
                        captureResult.Contrast,
                        captureResult.Warning));
                }

                // 首次截图自动准备一份参考图，避免图片识别页仍显示为空；
                // 已经加载或保存过的参考图必须保留，后续截图只更新当前截图。
                this.AutoLoadVisionTemplateFromCapture();

                // 截图完成后默认自动识别，并将结果填入可手动修改的关键词框。
                this.bRecognizeVisionText_Click(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                this.SetVisionStatus(string.Format(UiText("Vision_CaptureFailed"), ex.Message));
            }
        }

        private void AutoLoadVisionTemplateFromCapture()
        {
            if (this.visionPreview == null || this.visionTemplate != null)
            {
                return;
            }

            this.DisposeVisionPreprocessedPreview();
            this.visionTemplate = new Bitmap(this.visionPreview);
            if (this.pbVisionTemplate != null)
            {
                this.pbVisionTemplate.Image = this.visionTemplate;
            }
            this.SetVisionMatchStatus(UiText("Vision_TemplateAutoLoaded"));
        }

        private void bLoadVisionTemplate_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = UiText("Vision_TemplateFilter");
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    Bitmap loadedTemplate;
                    using (Image loadedImage = Image.FromFile(dialog.FileName))
                    {
                        loadedTemplate = new Bitmap(loadedImage);
                    }

                    this.DisposeVisionTemplate();
                    this.visionTemplate = loadedTemplate;
                    this.pbVisionTemplate.Image = this.visionTemplate;
                    this.SetVisionMatchStatus(string.Format(
                        UiText("Vision_TemplateLoaded"),
                        dialog.FileName));
                }
                catch (Exception ex)
                {
                    this.SetVisionMatchStatus(string.Format(
                        UiText("Vision_TemplateLoadFailed"),
                        ex.Message));
                }
            }
        }

        private void bSaveVisionTemplate_Click(object sender, EventArgs e)
        {
            if (this.visionPreview == null)
            {
                this.SetVisionStatus(UiText("Vision_NoPreview"));
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = UiText("Vision_TemplateFilter");
                dialog.DefaultExt = "png";
                dialog.AddExtension = true;
                dialog.FileName = UiText("Vision_DefaultFileName");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    using (Bitmap template = this.CreateVisionTemplateFromSelection())
                    {
                        VisionWindowService.SavePng(template, dialog.FileName);
                        this.DisposeVisionTemplate();
                        this.visionTemplate = new Bitmap(template);
                    }
                    this.pbVisionTemplate.Image = this.visionTemplate;
                    this.SetVisionMatchStatus(string.Format(
                        UiText("Vision_TemplateSaved"),
                        dialog.FileName));
                }
                catch (Exception ex)
                {
                    this.SetVisionMatchStatus(string.Format(
                        UiText("Vision_CaptureFailed"),
                        ex.Message));
                }
            }
        }

        private async void bMatchVisionTemplate_Click(object sender, EventArgs e)
        {
            if (this.visionPreview == null ||
                (this.visionMatchTask != null && !this.visionMatchTask.IsCompleted))
            {
                this.SetVisionMatchStatus(UiText("Vision_NoPreview"));
                return;
            }
            if (this.visionTemplate == null)
            {
                this.SetVisionMatchStatus(UiText("Vision_NoTemplate"));
                return;
            }

            Bitmap source = null;
            Bitmap template = null;
            bool matchTaskOwnsBitmaps = false;
            try
            {
                source = new Bitmap(this.visionPreview);
                template = new Bitmap(this.visionTemplate);
                double minimumSimilarity = (double)this.nudVisionThreshold.Value / 100D;
                VisionTemplateMatchOptions options = this.ReadVisionTemplateMatchOptions();
                this.visionMatchCancellation = new CancellationTokenSource();
                CancellationToken cancellationToken = this.visionMatchCancellation.Token;
                this.SetVisionMatchButtons(true);
                this.SetVisionMatchStatus(UiText("Vision_MatchRunning"));
                this.visionMatchTask = Task.Run(
                    () =>
                    {
                        using (source)
                        using (template)
                        {
                            return VisionTemplateMatcher.FindBestMatch(
                                source,
                                template,
                                minimumSimilarity,
                                options,
                                cancellationToken);
                        }
                    },
                    cancellationToken);
                matchTaskOwnsBitmaps = true;
                VisionMatchResult result = await this.visionMatchTask;
                this.ApplyVisionMatchResult(result);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    this.SetVisionMatchStatus(UiText("Vision_MatchCancelled"));
                }
                else
                {
                    this.SetVisionMatchStatus(string.Format(
                        UiText("Vision_CaptureFailed"),
                        ex.Message));
                }
            }
            finally
            {
                this.SetVisionMatchButtons(false);
                if (this.visionMatchCancellation != null)
                {
                    this.visionMatchCancellation.Dispose();
                    this.visionMatchCancellation = null;
                }
                this.visionMatchTask = null;
                if (!matchTaskOwnsBitmaps)
                {
                    if (source != null) { source.Dispose(); }
                    if (template != null) { template.Dispose(); }
                }
            }
        }

        private void bCancelVisionMatch_Click(object sender, EventArgs e)
        {
            if (this.visionMatchCancellation != null)
            {
                this.visionMatchCancellation.Cancel();
            }
            if (this.visionPythonHealthCancellation != null)
            {
                this.visionPythonHealthCancellation.Cancel();
            }
        }

        private void SetVisionMatchButtons(bool running)
        {
            if (this.bVisionMatchTemplate != null)
            {
                this.bVisionMatchTemplate.Enabled = !running;
            }
            if (this.bVisionCancelMatch != null)
            {
                this.bVisionCancelMatch.Enabled = running;
                this.bVisionCancelMatch.Visible = running;
            }
        }

        private void ApplyVisionMatchResult(VisionMatchResult result)
        {
            if (result == null || result.Cancelled)
            {
                this.SetVisionMatchStatus(UiText("Vision_MatchCancelled"));
            }
            else if (result.Found)
            {
                this.SetVisionMatchStatus(string.Format(
                    UiText("Vision_MatchFound"),
                    result.Location.X,
                    result.Location.Y,
                    result.Size.Width,
                    result.Size.Height,
                    result.Similarity));
            }
            else
            {
                this.SetVisionMatchStatus(string.Format(
                    UiText("Vision_MatchNotFound"),
                    result.Similarity));
            }
        }

        private void bAddVisionTemplateVariant_Click(object sender, EventArgs e)
        {
            VisionAssistantStep step = this.cbbVisionSteps == null
                ? null
                : this.cbbVisionSteps.SelectedItem as VisionAssistantStep;
            if (step == null || step.Condition == null || this.visionTemplate == null ||
                (step.Condition.Type != VisionConditionType.TemplateAppears &&
                 step.Condition.Type != VisionConditionType.TemplateDisappears))
            {
                this.SetVisionMatchStatus(UiText("Vision_SelectTemplateStep"));
                return;
            }
            step.Condition.TemplateVariants.Add(new Bitmap(this.visionTemplate));
            this.SetVisionMatchStatus(string.Format(
                UiText("Vision_TemplateVariantAdded"),
                step.Condition.TemplateVariants.Count + (step.Condition.Template == null ? 0 : 1)));
        }

        private async void bRecognizeVisionText_Click(object sender, EventArgs e)
        {
            await this.RecognizeVisionTextAsync(true);
        }

        private async Task RecognizeVisionTextAsync(bool fillKeyword)
        {
            if (this.visionPreview == null || (this.visionOcrTask != null && !this.visionOcrTask.IsCompleted))
            {
                this.SetVisionOcrStatus(UiText("Vision_NoPreview"));
                return;
            }

            try
            {
                VisionOcrOptions options = this.ReadVisionOcrOptions();
                if (this.visionTextRecognizer == null)
                {
                    this.visionPythonWorker = new VisionPythonWorkerTextRecognizer();
                    this.visionTextRecognizer = new VisionAutoTextRecognizer(
                        new VisionOnnxTextRecognizer(),
                        new VisionTesseractRecognizer(options.ExecutablePath),
                        this.visionPythonWorker);
                }
                IVisionTextRecognizer recognizer = this.visionTextRecognizer;

                Bitmap input = new Bitmap(this.visionPreview);
                this.visionOcrCancellation = new CancellationTokenSource();
                CancellationToken cancellationToken = this.visionOcrCancellation.Token;
                this.SetVisionOcrButtons(true);
                this.SetVisionOcrStatus(UiText("Vision_OcrRunning"));
                try
                {
                    // Always run the delegate so the cloned bitmap is disposed even
                    // when the caller cancelled before the worker was scheduled.
                    this.visionOcrTask = Task.Run(
                        () =>
                        {
                            using (input)
                            {
                                return recognizer.Recognize(input, options, cancellationToken);
                            }
                        },
                        CancellationToken.None);
                }
                catch
                {
                    input.Dispose();
                    throw;
                }
                VisionOcrResult result = await this.visionOcrTask;
                if (result.Cancelled)
                {
                    this.SetVisionOcrStatus(UiText("Vision_OcrCancelled"));
                    return;
                }
                if (!result.Available)
                {
                    this.SetVisionOcrStatus(string.Format(
                        UiText("Vision_OcrUnavailable"),
                        result.Error));
                    return;
                }
                if (!result.Success)
                {
                    this.SetVisionOcrStatus(string.Format(
                        UiText("Vision_OcrFailed"),
                        result.Error));
                    return;
                }

                string status = string.Format(
                    UiText("Vision_OcrSuccess"),
                    result.Text,
                    result.Confidence);
                if (fillKeyword && !string.IsNullOrWhiteSpace(result.Text))
                {
                    this.txtVisionOcrKeyword.Text = string.Join(
                        " ",
                        result.Text.Split(
                            new[] { ' ', '\t', '\r', '\n' },
                            StringSplitOptions.RemoveEmptyEntries));
                }
                string keyword = this.txtVisionOcrKeyword == null
                    ? string.Empty
                    : this.txtVisionOcrKeyword.Text.Trim();
                if (!string.IsNullOrEmpty(keyword))
                {
                    VisionTextCondition condition = new VisionTextCondition
                    {
                        ExpectedText = keyword,
                        MinimumConfidence = 0D,
                        MatchMode = VisionTextMatchMode.Contains
                    };
                    string reason;
                    if (!condition.Matches(result, out reason))
                    {
                        this.SetVisionOcrStatus(string.Format(
                            UiText("Vision_OcrConditionNotMatched"),
                            reason));
                        return;
                    }
                    status += " " + UiText("Vision_OcrConditionMatched");
                }

                this.SetVisionOcrStatus(status);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    this.SetVisionOcrStatus(UiText("Vision_OcrCancelled"));
                }
                else
                {
                this.SetVisionOcrStatus(string.Format(
                    UiText("Vision_OcrFailed"),
                    ex.Message));
                }
            }
            finally
            {
                this.SetVisionOcrButtons(false);
                if (this.visionOcrCancellation != null)
                {
                    this.visionOcrCancellation.Dispose();
                    this.visionOcrCancellation = null;
                }
                this.visionOcrTask = null;
            }
        }

        private void bCancelVisionOcr_Click(object sender, EventArgs e)
        {
            if (this.visionOcrCancellation != null)
            {
                this.visionOcrCancellation.Cancel();
            }
        }

        private void SetVisionOcrButtons(bool running)
        {
            if (this.bVisionRecognizeText != null)
            {
                this.bVisionRecognizeText.Enabled = !running;
            }
            if (this.bVisionCancelOcr != null)
            {
                this.bVisionCancelOcr.Enabled = running;
                this.bVisionCancelOcr.Visible = running;
            }
        }

        private static bool IsVisionInstructionType(Socket_Cache.Robot.InstructionType instructionType)
        {
            return instructionType == Socket_Cache.Robot.InstructionType.VisionWait;
        }

        private static string BuildVisionInstructionContent(
            int stepIndex,
            VisionAssistantStep step)
        {
            return Socket_Cache.Robot.VisionInstructionContentPrefix +
                stepIndex.ToString(CultureInfo.InvariantCulture) +
                "|" +
                (step == null ? string.Empty : step.Name ?? string.Empty);
        }

        private static bool TryGetVisionInstructionStepIndex(
            string content,
            out int stepIndex)
        {
            stepIndex = -1;
            if (string.IsNullOrEmpty(content) ||
                !content.StartsWith(
                    Socket_Cache.Robot.VisionInstructionContentPrefix,
                    StringComparison.Ordinal))
            {
                return false;
            }

            string payload = content.Substring(Socket_Cache.Robot.VisionInstructionContentPrefix.Length);
            int separator = payload.IndexOf('|');
            string indexText = separator < 0 ? payload : payload.Substring(0, separator);
            return int.TryParse(
                indexText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out stepIndex) && stepIndex >= 0;
        }

        private bool HasVisionInstructionRows()
        {
            if (this.dtRobotInstruction == null)
            {
                return false;
            }

            foreach (DataRow row in this.dtRobotInstruction.Rows)
            {
                if (IsVisionInstructionType((Socket_Cache.Robot.InstructionType)row["Type"]))
                {
                    return true;
                }
            }
            return false;
        }

        private void EnsureVisionInstructionRows()
        {
            Socket_VisionProfile profile = this.sriSelect == null
                ? null
                : this.sriSelect.VisionProfile;
            if (profile == null || profile.AssistantSteps == null || this.dtRobotInstruction == null)
            {
                return;
            }

            HashSet<int> referencedIndexes = new HashSet<int>();
            foreach (DataRow row in this.dtRobotInstruction.Rows)
            {
                Socket_Cache.Robot.InstructionType type =
                    (Socket_Cache.Robot.InstructionType)row["Type"];
                int stepIndex;
                if (IsVisionInstructionType(type) &&
                    TryGetVisionInstructionStepIndex(row["Content"].ToString(), out stepIndex) &&
                    stepIndex < profile.AssistantSteps.Count)
                {
                    referencedIndexes.Add(stepIndex);
                }
            }

            for (int stepIndex = 0; stepIndex < profile.AssistantSteps.Count; stepIndex++)
            {
                if (referencedIndexes.Contains(stepIndex))
                {
                    continue;
                }

                DataRow row = this.dtRobotInstruction.NewRow();
                row["Type"] = Socket_Cache.Robot.InstructionType.VisionWait;
                row["Content"] = BuildVisionInstructionContent(
                    stepIndex,
                    profile.AssistantSteps[stepIndex]);
                this.dtRobotInstruction.Rows.Add(row);
            }
        }

        private bool SyncVisionAssistantStepsFromInstructions(Socket_VisionProfile profile)
        {
            if (profile == null || profile.AssistantSteps == null || this.dtRobotInstruction == null)
            {
                return true;
            }

            List<VisionAssistantStep> previousSteps = profile.AssistantSteps.ToList();
            List<VisionAssistantStep> orderedSteps = new List<VisionAssistantStep>();
            HashSet<int> usedIndexes = new HashSet<int>();
            List<DataRow> visionRows = new List<DataRow>();
            foreach (DataRow row in this.dtRobotInstruction.Rows)
            {
                Socket_Cache.Robot.InstructionType type =
                    (Socket_Cache.Robot.InstructionType)row["Type"];
                if (!IsVisionInstructionType(type))
                {
                    continue;
                }

                int stepIndex;
                if (!TryGetVisionInstructionStepIndex(row["Content"].ToString(), out stepIndex) ||
                    stepIndex >= previousSteps.Count ||
                    !usedIndexes.Add(stepIndex))
                {
                    this.SetVisionStatus(string.Format(
                        UiText("Vision_ProfileInvalid"),
                        "右侧指令集中的视觉等待步骤引用无效。"));
                    return false;
                }

                visionRows.Add(row);
                orderedSteps.Add(previousSteps[stepIndex]);
            }

            foreach (VisionAssistantStep previousStep in previousSteps)
            {
                if (!orderedSteps.Contains(previousStep))
                {
                    this.DisposeVisionConditionTemplates(previousStep.Condition);
                    this.DisposeVisionConditionTemplates(previousStep.Verification);
                }
            }

            profile.AssistantSteps.Clear();
            profile.AssistantSteps.AddRange(orderedSteps);
            for (int index = 0; index < visionRows.Count; index++)
            {
                visionRows[index]["Content"] = BuildVisionInstructionContent(
                    index,
                    orderedSteps[index]);
            }
            return true;
        }

        private void AddVisionInstructionRow(int stepIndex, VisionAssistantStep step)
        {
            if (this.dtRobotInstruction == null)
            {
                return;
            }

            DataRow row = this.dtRobotInstruction.NewRow();
            row["Type"] = Socket_Cache.Robot.InstructionType.VisionWait;
            row["Content"] = BuildVisionInstructionContent(stepIndex, step);
            if (this.dgvRobotInstruction.CurrentCell != null)
            {
                this.dtRobotInstruction.Rows.InsertAt(
                    row,
                    this.dgvRobotInstruction.CurrentCell.RowIndex + 1);
            }
            else
            {
                this.dtRobotInstruction.Rows.Add(row);
            }
        }

        private VisionAssistantStep BuildVisionAssistantStepFromEditors()
        {
            VisionConditionType conditionType = this.ReadVisionConditionType();
            bool isTemplate = conditionType == VisionConditionType.TemplateAppears ||
                conditionType == VisionConditionType.TemplateDisappears;
            bool isColor = conditionType == VisionConditionType.ColorAppears ||
                conditionType == VisionConditionType.ColorDisappears;
            bool isOcr = conditionType == VisionConditionType.TextAppears ||
                conditionType == VisionConditionType.TextDisappears ||
                conditionType == VisionConditionType.NumberInRange ||
                isColor;
            if (!isOcr && !isTemplate)
            {
                this.SetVisionAssistantStatus(UiText("Vision_SelectOcrCondition"));
                return null;
            }

            VisionWindowInfo window = this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
            VisionRegion region = this.ReadVisionRegion();
            VisionCaptureSettings captureSettings = this.ReadVisionCaptureSettings();
            Size clientSize = Size.Empty;
            string clientSizeError = string.Empty;
            if (window == null || !VisionWindowService.TryValidateClientSize(
                window.Handle,
                captureSettings,
                out clientSize,
                out clientSizeError))
            {
                this.SetVisionAssistantStatus(string.Format(
                    UiText("Vision_ProfileInvalid"),
                    window == null
                        ? "Select a target window first."
                        : clientSizeError));
                return null;
            }
            if (!region.FitsWithin(clientSize))
            {
                this.SetVisionAssistantStatus(string.Format(
                    UiText("Vision_ProfileInvalid"),
                    "The region must stay inside the target client area."));
                return null;
            }
            if (captureSettings.RequireExactClientSize && region.UseNormalizedCoordinates)
            {
                this.SetVisionAssistantStatus(string.Format(
                    UiText("Vision_ProfileInvalid"),
                    UiText("Vision_FixedCoordinatesRequired")));
                return null;
            }

            if (isTemplate && this.visionTemplate == null)
            {
                this.SetVisionAssistantStatus(UiText("Vision_NoTemplate"));
                return null;
            }

            Socket_VisionProfile profile = this.sriSelect.VisionProfile ?? new Socket_VisionProfile();
            VisionConditionDefinition verification = this.ReadVisionVerificationCondition();
            if (this.chkVisionActionVerification.Checked && verification == null)
            {
                this.SetVisionAssistantStatus(UiText("Vision_VerificationIncomplete"));
                return null;
            }
            if (verification != null && !verification.Region.FitsWithin(clientSize))
            {
                this.DisposeVisionConditionTemplates(verification);
                this.SetVisionAssistantStatus(string.Format(
                    UiText("Vision_ProfileInvalid"),
                    "The verification region must stay inside the target client area."));
                return null;
            }
            if (verification != null && captureSettings.RequireExactClientSize &&
                verification.Region.UseNormalizedCoordinates)
            {
                this.DisposeVisionConditionTemplates(verification);
                this.SetVisionAssistantStatus(string.Format(
                    UiText("Vision_ProfileInvalid"),
                    UiText("Vision_FixedCoordinatesRequired")));
                return null;
            }

            VisionAssistantStep step = new VisionAssistantStep
            {
                ActionDefinition = this.ReadVisionActionDefinition(),
                VerificationEnabled = verification != null,
                Verification = verification,
                VerificationUsesSeparateRegion = verification != null &&
                    this.chkVisionVerificationSeparateRegion.Checked,
                Condition = new VisionConditionDefinition
                {
                    Type = conditionType,
                    Region = region.Clone(),
                    RequiredConfirmations = (int)this.nudVisionConfirmations.Value,
                    PollIntervalMilliseconds = (int)this.nudVisionPollInterval.Value,
                    TimeoutMilliseconds = (int)this.nudVisionTimeout.Value,
                    MaxRetries = (int)this.nudVisionRetries.Value,
                    FailurePolicy = this.ReadVisionFailurePolicy()
                }
            };

            if (isTemplate)
            {
                string stepName = conditionType == VisionConditionType.TemplateDisappears
                    ? UiText("Vision_TemplateDisappearStepName")
                    : UiText("Vision_TemplateStepName");
                step.Name = stepName;
                step.Condition.Name = stepName;
                step.Condition.Template = new Bitmap(this.visionTemplate);
                step.Condition.MinimumSimilarity = (double)this.nudVisionThreshold.Value / 100D;
                step.Condition.NormalizeTemplateBrightness = this.chkVisionTemplateNormalize.Checked;
                step.Condition.AllowTemplateScaleVariation = this.chkVisionTemplateScale.Checked;
                VisionTemplateMatchOptions templateOptions = this.ReadVisionTemplateMatchOptions();
                step.Condition.TemplateMinimumScale = templateOptions.MinimumScale;
                step.Condition.TemplateMaximumScale = templateOptions.MaximumScale;
                step.Condition.TemplateScaleStep = templateOptions.ScaleStep;
            }
            else
            {
                string keyword = this.txtVisionOcrKeyword.Text.Trim();
                if (!isColor && conditionType != VisionConditionType.NumberInRange &&
                    string.IsNullOrEmpty(keyword))
                {
                    this.DisposeVisionConditionTemplates(step.Verification);
                    this.SetVisionAssistantStatus(UiText("Vision_NoKeyword"));
                    return null;
                }

                VisionColorCondition colorCondition = isColor
                    ? this.ReadVisionColorCondition()
                    : null;
                if (isColor && colorCondition == null)
                {
                    this.DisposeVisionConditionTemplates(step.Verification);
                    this.SetVisionAssistantStatus(UiText("Vision_ColorInvalid"));
                    return null;
                }
                profile.OcrOptions = this.ReadVisionOcrOptions();
                VisionTextCondition textCondition = profile.OcrCondition == null
                    ? new VisionTextCondition()
                    : profile.OcrCondition.Clone();
                textCondition.ExpectedText = isColor || conditionType == VisionConditionType.NumberInRange
                    ? string.Empty
                    : keyword;
                textCondition.MatchMode = isColor || conditionType == VisionConditionType.NumberInRange
                    ? VisionTextMatchMode.NumberRange
                    : VisionTextMatchMode.Contains;
                textCondition.MinimumNumber = (double)this.nudVisionNumberMinimum.Value;
                textCondition.MaximumNumber = (double)this.nudVisionNumberMaximum.Value;
                profile.OcrCondition = textCondition;
                step.Name = isColor
                    ? UiText(conditionType == VisionConditionType.ColorDisappears
                        ? "Vision_ColorDisappearStepName"
                        : "Vision_ColorStepName")
                    : conditionType == VisionConditionType.NumberInRange
                    ? string.Format(
                        "{0}: {1} - {2}",
                        UiText("Vision_NumberRange"),
                        textCondition.MinimumNumber,
                        textCondition.MaximumNumber)
                    : keyword;
                step.Condition.Name = step.Name;
                step.Condition.TextCondition = textCondition.Clone();
                step.Condition.ColorCondition = colorCondition == null
                    ? null
                    : colorCondition.Clone();
            }

            return step;
        }

        private void bCancelVisionAction_Click(object sender, EventArgs e)
        {
            if (this.updatingVisionStepEditor)
            {
                return;
            }

            this.updatingVisionStepEditor = true;
            try
            {
                this.cbbVisionConditionType.SelectedIndex = 0;
                this.txtVisionOcrKeyword.Clear();
                this.SetVisionNumber(this.nudVisionNumberMinimum, 0);
                this.SetVisionNumber(this.nudVisionNumberMaximum, 1000000000);
                if (this.txtVisionColorRgb != null)
                {
                    this.txtVisionColorRgb.Text = "255,255,255";
                }
                this.SetVisionNumber(this.nudVisionColorTolerance, 16);
                this.SetVisionNumber(this.nudVisionColorMinimumPixels, 10);
                this.SetVisionDecimal(this.nudVisionColorMinimumRatio, 0D);
                this.SetVisionNumber(this.nudVisionX, 0);
                this.SetVisionNumber(this.nudVisionY, 0);
                this.SetVisionNumber(this.nudVisionWidth, 1);
                this.SetVisionNumber(this.nudVisionHeight, 1);
                if (this.chkVisionNormalized != null)
                {
                    this.chkVisionNormalized.Checked = true;
                }

                this.ApplyVisionActionDefinition(new VisionActionDefinition());
                if (this.cbbVisionVerificationType != null)
                {
                    this.cbbVisionVerificationType.SelectedIndex = 0;
                }
                this.ApplyVisionVerificationCondition(null);
                this.DisposeVisionPreview();
                this.visionPreviewOriginRegion = null;
                this.visionPreviewSelection = Rectangle.Empty;
                this.visionPreviewSelecting = false;
                if (this.cbbVisionHistory != null)
                {
                    this.cbbVisionHistory.Items.Clear();
                }
                if (this.cbbVisionSteps != null && this.cbbVisionSteps.Items.Count > 0)
                {
                    this.cbbVisionSteps.SelectedIndex = 0;
                }
            }
            finally
            {
                this.updatingVisionStepEditor = false;
            }

            this.SetVisionAssistantStatus(UiText("Vision_ActionCancelled"));
        }

        private void bConfirmVisionAction_Click(object sender, EventArgs e)
        {
            if (this.sriSelect == null)
            {
                return;
            }

            Socket_VisionProfile profile = this.sriSelect.VisionProfile ?? new Socket_VisionProfile();
            VisionAssistantStep draft = this.BuildVisionAssistantStepFromEditors();
            if (draft == null)
            {
                return;
            }

            VisionAssistantStep selectedStep = this.cbbVisionSteps == null
                ? null
                : this.cbbVisionSteps.SelectedItem as VisionAssistantStep;
            if (selectedStep == this.visionNewStep)
            {
                selectedStep = null;
            }

            if (selectedStep == null)
            {
                profile.AssistantSteps.Add(draft);
                this.AddVisionInstructionRow(profile.AssistantSteps.Count - 1, draft);
            }
            else
            {
                int stepIndex = profile.AssistantSteps.IndexOf(selectedStep);
                if (stepIndex < 0)
                {
                    this.DisposeVisionConditionTemplates(draft.Condition);
                    this.DisposeVisionConditionTemplates(draft.Verification);
                    this.SetVisionAssistantStatus(UiText("Vision_SelectStep"));
                    return;
                }

                this.DisposeVisionConditionTemplates(selectedStep.Condition);
                this.DisposeVisionConditionTemplates(selectedStep.Verification);
                selectedStep.Name = draft.Name;
                selectedStep.Condition = draft.Condition;
                selectedStep.Action = null;
                selectedStep.ActionDefinition = draft.ActionDefinition;
                selectedStep.VerificationEnabled = draft.VerificationEnabled;
                selectedStep.Verification = draft.Verification;
                selectedStep.VerificationUsesSeparateRegion = draft.VerificationUsesSeparateRegion;
                this.UpdateVisionInstructionRow(stepIndex, selectedStep);
            }

            if (!this.SyncVisionAssistantStepsFromInstructions(profile))
            {
                return;
            }
            this.sriSelect.VisionProfile = profile;
            VisionAssistantStep committedStep = selectedStep ?? draft;
            this.RefreshVisionAssistantSteps(committedStep);
            this.SetVisionAssistantStatus(selectedStep == null
                ? UiText("Vision_StepAdded")
                : UiText("Vision_StepUpdated"));
        }

        private void UpdateVisionInstructionRow(int stepIndex, VisionAssistantStep step)
        {
            if (this.dtRobotInstruction == null)
            {
                return;
            }

            foreach (DataRow row in this.dtRobotInstruction.Rows)
            {
                int referencedIndex;
                if (IsVisionInstructionType((Socket_Cache.Robot.InstructionType)row["Type"]) &&
                    TryGetVisionInstructionStepIndex(row["Content"].ToString(), out referencedIndex) &&
                    referencedIndex == stepIndex)
                {
                    row["Content"] = BuildVisionInstructionContent(stepIndex, step);
                    return;
                }
            }

            this.AddVisionInstructionRow(stepIndex, step);
        }

        private void bRemoveVisionStep_Click(object sender, EventArgs e)
        {
            if (this.sriSelect == null || this.cbbVisionSteps.SelectedIndex < 0)
            {
                return;
            }

            VisionAssistantStep step = this.cbbVisionSteps.SelectedItem as VisionAssistantStep;
            if (step != null && step != this.visionNewStep)
            {
                int stepIndex = this.sriSelect.VisionProfile.AssistantSteps.IndexOf(step);
                if (stepIndex >= 0 && this.dtRobotInstruction != null)
                {
                    foreach (DataRow row in this.dtRobotInstruction.Rows.Cast<DataRow>().ToList())
                    {
                        int referencedIndex;
                        if (IsVisionInstructionType((Socket_Cache.Robot.InstructionType)row["Type"]) &&
                            TryGetVisionInstructionStepIndex(row["Content"].ToString(), out referencedIndex) &&
                            referencedIndex == stepIndex)
                        {
                            this.dtRobotInstruction.Rows.Remove(row);
                            break;
                        }
                    }
                }
                this.sriSelect.VisionProfile.AssistantSteps.Remove(step);
                this.DisposeVisionConditionTemplates(step.Condition);
                this.DisposeVisionConditionTemplates(step.Verification);
                foreach (DataRow row in this.dtRobotInstruction.Rows)
                {
                    int referencedIndex;
                    if (!IsVisionInstructionType((Socket_Cache.Robot.InstructionType)row["Type"]) ||
                        !TryGetVisionInstructionStepIndex(row["Content"].ToString(), out referencedIndex) ||
                        referencedIndex <= stepIndex ||
                        referencedIndex >= this.sriSelect.VisionProfile.AssistantSteps.Count + 1)
                    {
                        continue;
                    }

                    int newIndex = referencedIndex - 1;
                    row["Content"] = BuildVisionInstructionContent(
                        newIndex,
                        this.sriSelect.VisionProfile.AssistantSteps[newIndex]);
                }
            }
            this.SyncVisionAssistantStepsFromInstructions(this.sriSelect.VisionProfile);
            this.RefreshVisionAssistantSteps();
            this.SetVisionOcrStatus(UiText("Vision_StepRemoved"));
        }

        private void RefreshVisionAssistantSteps(VisionAssistantStep selectedStep = null)
        {
            if (this.cbbVisionSteps == null)
            {
                return;
            }

            this.cbbVisionSteps.BeginUpdate();
            try
            {
                this.cbbVisionSteps.Items.Clear();
                this.visionNewStep.Name = UiText("Vision_NewStep");
                this.cbbVisionSteps.Items.Add(this.visionNewStep);
                if (this.sriSelect != null && this.sriSelect.VisionProfile != null &&
                    this.sriSelect.VisionProfile.AssistantSteps != null)
                {
                    foreach (VisionAssistantStep step in this.sriSelect.VisionProfile.AssistantSteps)
                    {
                        if (step != null)
                        {
                            this.cbbVisionSteps.Items.Add(step);
                        }
                    }
                }
                int selectedIndex = selectedStep == null
                    ? 0
                    : this.cbbVisionSteps.Items.IndexOf(selectedStep);
                this.cbbVisionSteps.SelectedIndex = selectedIndex < 0 ? 0 : selectedIndex;
                if (selectedStep == null)
                {
                    this.ResetVisionStepEditorForNewStep();
                }
            }
            finally
            {
                this.cbbVisionSteps.EndUpdate();
            }
        }

        private void ResetVisionStepEditorForNewStep()
        {
            if (this.updatingVisionStepEditor)
            {
                return;
            }

            this.updatingVisionStepEditor = true;
            try
            {
                this.cbbVisionConditionType.SelectedIndex = 0;
                this.txtVisionOcrKeyword.Clear();
                this.SetVisionNumber(this.nudVisionNumberMinimum, 0);
                this.SetVisionNumber(this.nudVisionNumberMaximum, 0);
                this.ApplyVisionActionDefinition(new VisionActionDefinition());
                this.ApplyVisionVerificationCondition(null);
                this.DisposeVisionPreprocessedPreview();
                this.DisposeVisionTemplate();
                if (this.pbVisionTemplate != null)
                {
                    this.pbVisionTemplate.Image = null;
                }
            }
            finally
            {
                this.updatingVisionStepEditor = false;
            }
        }

        private VisionConditionType ReadVisionConditionType()
        {
            VisionConditionChoice choice = this.cbbVisionConditionType == null
                ? null
                : this.cbbVisionConditionType.SelectedItem as VisionConditionChoice;
            return choice == null ? VisionConditionType.TextAppears : choice.Type;
        }

        private VisionColorCondition ReadVisionColorCondition()
        {
            if (this.txtVisionColorRgb == null)
            {
                return null;
            }
            string[] parts = this.txtVisionColorRgb.Text.Split(
                new[] { ',', ';', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            int red;
            int green;
            int blue;
            if (parts.Length != 3 ||
                !int.TryParse(parts[0], out red) ||
                !int.TryParse(parts[1], out green) ||
                !int.TryParse(parts[2], out blue) ||
                red < 0 || red > 255 ||
                green < 0 || green > 255 ||
                blue < 0 || blue > 255)
            {
                return null;
            }

            VisionColorCondition condition = new VisionColorCondition
            {
                Red = (byte)red,
                Green = (byte)green,
                Blue = (byte)blue,
                Tolerance = this.nudVisionColorTolerance == null
                    ? 16
                    : (int)this.nudVisionColorTolerance.Value,
                MinimumPixelCount = this.nudVisionColorMinimumPixels == null
                    ? 10
                    : (int)this.nudVisionColorMinimumPixels.Value,
                MinimumMatchRatio = this.nudVisionColorMinimumRatio == null
                    ? 0D
                    : (double)this.nudVisionColorMinimumRatio.Value
            };
            try
            {
                condition.Validate();
                return condition;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private VisionActionDefinition ReadVisionActionDefinition()
        {
            VisionActionChoice action = this.cbbVisionActionType == null
                ? null
                : this.cbbVisionActionType.SelectedItem as VisionActionChoice;
            VisionScrollChoice direction = this.cbbVisionScrollDirection == null
                ? null
                : this.cbbVisionScrollDirection.SelectedItem as VisionScrollChoice;
            return new VisionActionDefinition
            {
                Type = action == null ? VisionActionType.None : action.Type,
                ScrollDirection = direction == null ? VisionScrollDirection.Down : direction.Direction,
                ScrollAmount = this.nudVisionScrollAmount == null ? 3 : (int)this.nudVisionScrollAmount.Value,
                DelayMilliseconds = 300
            };
        }

        private void ApplyVisionActionDefinition(VisionActionDefinition definition)
        {
            VisionActionDefinition value = definition == null
                ? new VisionActionDefinition()
                : definition;
            for (int i = 0; i < this.cbbVisionActionType.Items.Count; i++)
            {
                VisionActionChoice choice = this.cbbVisionActionType.Items[i] as VisionActionChoice;
                if (choice != null && choice.Type == value.Type)
                {
                    this.cbbVisionActionType.SelectedIndex = i;
                    break;
                }
            }
            for (int i = 0; i < this.cbbVisionScrollDirection.Items.Count; i++)
            {
                VisionScrollChoice choice = this.cbbVisionScrollDirection.Items[i] as VisionScrollChoice;
                if (choice != null && choice.Direction == value.ScrollDirection)
                {
                    this.cbbVisionScrollDirection.SelectedIndex = i;
                    break;
                }
            }
            this.SetVisionNumber(this.nudVisionScrollAmount, value.ScrollAmount);
            this.UpdateVisionActionEditor();
        }

        private void UpdateVisionActionEditor()
        {
            if (this.cbbVisionActionType == null || this.cbbVisionScrollDirection == null ||
                this.nudVisionScrollAmount == null)
            {
                return;
            }
            VisionActionChoice choice = this.cbbVisionActionType.SelectedItem as VisionActionChoice;
            bool isScroll = choice != null && choice.Type == VisionActionType.Scroll;
            this.cbbVisionScrollDirection.Visible = isScroll;
            this.nudVisionScrollAmount.Visible = isScroll;
        }

        private VisionConditionDefinition ReadVisionVerificationCondition()
        {
            if (this.chkVisionActionVerification == null || !this.chkVisionActionVerification.Checked)
            {
                return null;
            }
            VisionVerificationChoice choice = this.cbbVisionVerificationType.SelectedItem as VisionVerificationChoice;
            VisionConditionType type = choice == null ? VisionConditionType.TextAppears : choice.Type;
            VisionRegion region = this.ReadVisionRegion();
            if (!region.IsValid)
            {
                return null;
            }
            if (this.chkVisionVerificationSeparateRegion.Checked)
            {
                if (this.visionVerificationRegion == null || !this.visionVerificationRegion.IsValid)
                {
                    return null;
                }
                region = this.visionVerificationRegion.Clone();
            }

            VisionConditionDefinition condition = new VisionConditionDefinition
            {
                Name = UiText("Vision_ActionVerification"),
                Type = type,
                Region = region.Clone(),
                RequiredConfirmations = (int)this.nudVisionConfirmations.Value,
                PollIntervalMilliseconds = (int)this.nudVisionPollInterval.Value,
                TimeoutMilliseconds = (int)this.nudVisionTimeout.Value,
                MaxRetries = (int)this.nudVisionRetries.Value,
                FailurePolicy = this.ReadVisionFailurePolicy(),
                MinimumSimilarity = (double)this.nudVisionThreshold.Value / 100D,
                NormalizeTemplateBrightness = this.chkVisionTemplateNormalize.Checked,
                AllowTemplateScaleVariation = this.chkVisionTemplateScale.Checked,
                TemplateMinimumScale = this.ReadVisionTemplateMatchOptions().MinimumScale,
                TemplateMaximumScale = this.ReadVisionTemplateMatchOptions().MaximumScale,
                TemplateScaleStep = this.ReadVisionTemplateMatchOptions().ScaleStep
            };
            if (type == VisionConditionType.TextAppears)
            {
                string expectedText = this.txtVisionVerificationKeyword.Text.Trim();
                if (string.IsNullOrEmpty(expectedText))
                {
                    return null;
                }
                VisionTextCondition source = this.sriSelect == null || this.sriSelect.VisionProfile == null
                    ? null
                    : this.sriSelect.VisionProfile.OcrCondition;
                condition.TextCondition = source == null ? new VisionTextCondition() : source.Clone();
                condition.TextCondition.ExpectedText = expectedText;
                condition.TextCondition.MatchMode = VisionTextMatchMode.Contains;
            }
            else
            {
                Bitmap template = this.GetVisionVerificationTemplate();
                if (template == null)
                {
                    return null;
                }
                condition.Template = template;
            }
            return condition;
        }

        private Bitmap GetVisionVerificationTemplate()
        {
            if (this.visionTemplate != null)
            {
                return new Bitmap(this.visionTemplate);
            }
            VisionAssistantStep step = this.cbbVisionSteps == null
                ? null
                : this.cbbVisionSteps.SelectedItem as VisionAssistantStep;
            if (step != null && step.Verification != null && step.Verification.Template != null)
            {
                return new Bitmap(step.Verification.Template);
            }
            if (step != null && step.Condition != null && step.Condition.Template != null)
            {
                return new Bitmap(step.Condition.Template);
            }
            return null;
        }

        private void SetVisionVerificationMode(VisionConditionType? type)
        {
            if (this.chkVisionActionVerification == null)
            {
                return;
            }

            this.chkVisionActionVerification.Checked = type.HasValue;
            if (type.HasValue)
            {
                for (int i = 0; i < this.cbbVisionVerificationType.Items.Count; i++)
                {
                    VisionVerificationChoice choice =
                        this.cbbVisionVerificationType.Items[i] as VisionVerificationChoice;
                    if (choice != null && choice.Type == type.Value)
                    {
                        this.cbbVisionVerificationType.SelectedIndex = i;
                        break;
                    }
                }
            }
            else
            {
                this.txtVisionVerificationKeyword.Clear();
                this.chkVisionVerificationSeparateRegion.Checked = false;
                this.visionVerificationRegion = null;
            }

            if (this.bVisionActionVerificationMenu != null &&
                this.bVisionActionVerificationMenu.ContextMenuStrip != null)
            {
                for (int i = 0; i < this.bVisionActionVerificationMenu.ContextMenuStrip.Items.Count; i++)
                {
                    ToolStripMenuItem item = this.bVisionActionVerificationMenu.ContextMenuStrip.Items[i]
                        as ToolStripMenuItem;
                    if (item != null)
                    {
                        item.Checked = type.HasValue
                            ? i == (type.Value == VisionConditionType.TemplateAppears ? 2 : 1)
                            : i == 0;
                    }
                }
            }
            this.UpdateVisionVerificationEditor();
        }

        private void ApplyVisionVerificationCondition(VisionAssistantStep step)
        {
            VisionConditionDefinition condition = step == null || !step.VerificationEnabled
                ? null
                : step.Verification;
            this.SetVisionVerificationMode(condition == null ? (VisionConditionType?)null : condition.Type);
            this.chkVisionVerificationSeparateRegion.Checked = step != null &&
                step.VerificationEnabled && step.VerificationUsesSeparateRegion;
            this.visionVerificationRegion = this.chkVisionVerificationSeparateRegion.Checked && condition != null &&
                condition.Region != null
                ? condition.Region.Clone()
                : null;
            if (condition == null)
            {
                this.txtVisionVerificationKeyword.Clear();
                this.UpdateVisionVerificationEditor();
                return;
            }
            for (int i = 0; i < this.cbbVisionVerificationType.Items.Count; i++)
            {
                VisionVerificationChoice choice = this.cbbVisionVerificationType.Items[i] as VisionVerificationChoice;
                if (choice != null && choice.Type == condition.Type)
                {
                    this.cbbVisionVerificationType.SelectedIndex = i;
                    break;
                }
            }
            this.txtVisionVerificationKeyword.Text = condition.TextCondition == null
                ? string.Empty
                : condition.TextCondition.ExpectedText ?? string.Empty;
            this.UpdateVisionVerificationEditor();
        }

        private void UpdateVisionVerificationEditor()
        {
            if (this.chkVisionActionVerification == null ||
                this.cbbVisionVerificationType == null ||
                this.txtVisionVerificationKeyword == null)
            {
                return;
            }
            VisionVerificationChoice choice = this.cbbVisionVerificationType.SelectedItem as VisionVerificationChoice;
            bool isText = choice == null || choice.Type == VisionConditionType.TextAppears;
            this.cbbVisionVerificationType.Visible = this.chkVisionActionVerification.Checked;
            this.txtVisionVerificationKeyword.Visible = this.chkVisionActionVerification.Checked && isText;
            this.visionVerificationAdvancedPanel.Visible = this.chkVisionActionVerification.Checked;
            this.lVisionVerificationKeyword.Visible = this.chkVisionActionVerification.Checked && isText;
            this.chkVisionVerificationSeparateRegion.Visible = this.chkVisionActionVerification.Checked;
            this.bVisionSelectVerificationRegion.Visible = this.chkVisionActionVerification.Checked &&
                this.chkVisionVerificationSeparateRegion.Checked;
            this.lVisionVerificationHint.Visible = this.chkVisionActionVerification.Checked;
            this.lVisionVerificationHint.Text = isText
                ? UiText("Vision_VerificationTextHint")
                : UiText("Vision_VerificationImageHint");
            if (this.bVisionActionVerificationMenu != null)
            {
                string verificationMode = !this.chkVisionActionVerification.Checked
                    ? UiText("Vision_ActionVerificationOff")
                    : isText
                        ? UiText("Vision_ActionVerificationText")
                        : UiText("Vision_ActionVerificationImage");
                this.bVisionActionVerificationMenu.Text = verificationMode + "  ▼";
                this.bVisionActionVerificationMenu.AccessibleName = verificationMode;
            }
        }

        private VisionFailurePolicy ReadVisionFailurePolicy()
        {
            VisionFailureChoice choice = this.cbbVisionFailurePolicy == null
                ? null
                : this.cbbVisionFailurePolicy.SelectedItem as VisionFailureChoice;
            return choice == null ? VisionFailurePolicy.Stop : choice.Policy;
        }

        private void cbbVisionSteps_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.updatingVisionStepEditor)
            {
                return;
            }

            VisionAssistantStep step = this.cbbVisionSteps.SelectedItem as VisionAssistantStep;
            if (step == this.visionNewStep)
            {
                this.ResetVisionStepEditorForNewStep();
                return;
            }
            if (step == null || step.Condition == null)
            {
                return;
            }

            this.updatingVisionStepEditor = true;
            try
            {
                VisionConditionDefinition condition = step.Condition;
                this.ApplyVisionRegionToEditors(condition.Region);
                this.LoadVisionTemplateForStep(step);
                for (int i = 0; i < this.cbbVisionConditionType.Items.Count; i++)
                {
                    VisionConditionChoice choice = this.cbbVisionConditionType.Items[i] as VisionConditionChoice;
                    if (choice != null && choice.Type == condition.Type)
                    {
                        this.cbbVisionConditionType.SelectedIndex = i;
                        break;
                    }
                }

                VisionTextCondition textCondition = condition.TextCondition;
                if (textCondition != null)
                {
                    this.txtVisionOcrKeyword.Text = textCondition.ExpectedText ?? string.Empty;
                    this.SetVisionDecimal(
                        this.nudVisionNumberMinimum,
                        textCondition.MinimumNumber);
                    this.SetVisionDecimal(
                        this.nudVisionNumberMaximum,
                        textCondition.MaximumNumber);
                }
                if ((condition.Type == VisionConditionType.ColorAppears ||
                     condition.Type == VisionConditionType.ColorDisappears) &&
                    condition.ColorCondition != null)
                {
                    this.txtVisionColorRgb.Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1},{2}",
                        condition.ColorCondition.Red,
                        condition.ColorCondition.Green,
                        condition.ColorCondition.Blue);
                    this.SetVisionNumber(this.nudVisionColorTolerance, condition.ColorCondition.Tolerance);
                    this.SetVisionNumber(this.nudVisionColorMinimumPixels, condition.ColorCondition.MinimumPixelCount);
                    this.SetVisionDecimal(this.nudVisionColorMinimumRatio, condition.ColorCondition.MinimumMatchRatio);
                }
                this.SetVisionNumber(this.nudVisionConfirmations, condition.RequiredConfirmations);
                this.SetVisionNumber(this.nudVisionPollInterval, condition.PollIntervalMilliseconds);
                this.SetVisionNumber(this.nudVisionTimeout, condition.TimeoutMilliseconds);
                this.SetVisionNumber(this.nudVisionRetries, condition.MaxRetries);
                this.ApplyVisionActionDefinition(step.ActionDefinition);
                this.ApplyVisionVerificationCondition(step);
                for (int i = 0; i < this.cbbVisionFailurePolicy.Items.Count; i++)
                {
                    VisionFailureChoice choice = this.cbbVisionFailurePolicy.Items[i] as VisionFailureChoice;
                    if (choice != null && choice.Policy == condition.FailurePolicy)
                    {
                        this.cbbVisionFailurePolicy.SelectedIndex = i;
                        break;
                    }
                }
                this.SetVisionNumber(
                    this.nudVisionThreshold,
                    (int)Math.Round(condition.MinimumSimilarity * 100D));
                this.chkVisionTemplateNormalize.Checked = condition.NormalizeTemplateBrightness;
                this.chkVisionTemplateScale.Checked = condition.AllowTemplateScaleVariation;
                this.SetVisionNumber(
                    this.nudVisionTemplateScaleTolerance,
                    (int)Math.Round(Math.Max(
                        0D,
                        Math.Max(1D - condition.TemplateMinimumScale, condition.TemplateMaximumScale - 1D)) * 100D));
            }
            finally
            {
                this.updatingVisionStepEditor = false;
            }
        }

        private void ApplyVisionRegionToEditors(VisionRegion region)
        {
            VisionRegion value = region == null ? new VisionRegion() : region;
            this.SetVisionNumber(this.nudVisionX, value.X);
            this.SetVisionNumber(this.nudVisionY, value.Y);
            this.SetVisionNumber(this.nudVisionWidth, Math.Max(1, value.Width));
            this.SetVisionNumber(this.nudVisionHeight, Math.Max(1, value.Height));
            if (this.chkVisionNormalized != null)
            {
                this.chkVisionNormalized.Checked = value.UseNormalizedCoordinates;
            }
        }

        private void LoadVisionTemplateForStep(VisionAssistantStep step)
        {
            Bitmap selectedTemplate = null;
            if (step != null && step.Condition != null && step.Condition.Template != null)
            {
                selectedTemplate = new Bitmap(step.Condition.Template);
            }
            else if (step != null && step.VerificationEnabled && step.Verification != null &&
                (step.Verification.Type == VisionConditionType.TemplateAppears ||
                 step.Verification.Type == VisionConditionType.TemplateDisappears) &&
                step.Verification.Template != null)
            {
                selectedTemplate = new Bitmap(step.Verification.Template);
            }

            this.DisposeVisionPreprocessedPreview();
            this.DisposeVisionTemplate();
            this.visionTemplate = selectedTemplate;
            if (this.pbVisionTemplate != null)
            {
                this.pbVisionTemplate.Image = this.visionTemplate;
            }
        }

        private void ApplyVisionConditionEditors(
            VisionAssistantStep step,
            VisionRegion region)
        {
            if (step == null || region == null)
            {
                throw new ArgumentNullException("step");
            }

            VisionConditionDefinition previous = step.Condition;
            VisionConditionDefinition condition = previous == null
                ? new VisionConditionDefinition()
                : previous.Clone();
            bool committed = false;
            try
            {
                VisionConditionType type = this.ReadVisionConditionType();
                condition.Name = string.IsNullOrWhiteSpace(step.Name)
                    ? condition.Name
                    : step.Name;
                condition.Type = type;
                condition.Region = region.Clone();
                condition.RequiredConfirmations = (int)this.nudVisionConfirmations.Value;
                condition.PollIntervalMilliseconds = (int)this.nudVisionPollInterval.Value;
                condition.TimeoutMilliseconds = (int)this.nudVisionTimeout.Value;
                condition.MaxRetries = (int)this.nudVisionRetries.Value;
                condition.FailurePolicy = this.ReadVisionFailurePolicy();
                condition.MinimumSimilarity = (double)this.nudVisionThreshold.Value / 100D;
                condition.NormalizeTemplateBrightness = this.chkVisionTemplateNormalize.Checked;
                condition.AllowTemplateScaleVariation = this.chkVisionTemplateScale.Checked;
                VisionTemplateMatchOptions templateOptions = this.ReadVisionTemplateMatchOptions();
                condition.TemplateMinimumScale = templateOptions.MinimumScale;
                condition.TemplateMaximumScale = templateOptions.MaximumScale;
                condition.TemplateScaleStep = templateOptions.ScaleStep;

                if (type == VisionConditionType.TextAppears ||
                    type == VisionConditionType.TextDisappears ||
                    type == VisionConditionType.NumberInRange)
                {
                    VisionTextCondition textCondition = condition.TextCondition == null
                        ? new VisionTextCondition()
                        : condition.TextCondition.Clone();
                    bool isNumber = type == VisionConditionType.NumberInRange;
                    textCondition.ExpectedText = isNumber
                        ? string.Empty
                        : this.txtVisionOcrKeyword.Text.Trim();
                    textCondition.MatchMode = isNumber
                        ? VisionTextMatchMode.NumberRange
                        : VisionTextMatchMode.Contains;
                    textCondition.MinimumNumber = (double)this.nudVisionNumberMinimum.Value;
                    textCondition.MaximumNumber = (double)this.nudVisionNumberMaximum.Value;
                    condition.TextCondition = textCondition;
                }

                if (type == VisionConditionType.ColorAppears ||
                    type == VisionConditionType.ColorDisappears)
                {
                    VisionColorCondition colorCondition = this.ReadVisionColorCondition();
                    if (colorCondition == null)
                    {
                        throw new InvalidOperationException(UiText("Vision_ColorInvalid"));
                    }
                    condition.ColorCondition = colorCondition;
                }

                bool isTemplate = type == VisionConditionType.TemplateAppears ||
                    type == VisionConditionType.TemplateDisappears;
                if (isTemplate)
                {
                    if (this.visionTemplate == null)
                    {
                        throw new InvalidOperationException(UiText("Vision_NoTemplate"));
                    }
                    if (condition.Template != null)
                    {
                        condition.Template.Dispose();
                    }
                    condition.Template = new Bitmap(this.visionTemplate);
                }
                else
                {
                    if (condition.Template != null)
                    {
                        condition.Template.Dispose();
                        condition.Template = null;
                    }
                    condition.DisposeTemplateVariants();
                }

                condition.Validate();
                this.DisposeVisionConditionTemplates(previous);
                step.Condition = condition;
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    this.DisposeVisionConditionTemplates(condition);
                }
            }
        }

        private void bRunVisionAssistant_Click(object sender, EventArgs e)
        {
            if (this.visionAssistantTask != null && !this.visionAssistantTask.IsCompleted)
            {
                this.SetVisionAssistantStatus(UiText("Vision_AssistantBusy"));
                this.AppendVisionAssistantLog(UiText("Vision_AssistantBusy"));
                return;
            }
            if (this.sriSelect == null || this.sriSelect.VisionProfile == null ||
                this.sriSelect.VisionProfile.AssistantSteps == null ||
                this.sriSelect.VisionProfile.AssistantSteps.Count == 0)
            {
                this.SetVisionAssistantStatus(UiText("Vision_AssistantNoSteps"));
                return;
            }
            if (!this.TryApplyVisionProfile())
            {
                return;
            }

            IVisionTextRecognizer recognizer = this.visionTextRecognizer;
            if (recognizer == null)
            {
                this.SetVisionAssistantStatus(string.Format(
                    UiText("Vision_OcrUnavailable"),
                    "recognizer is unavailable."));
                return;
            }

            Socket_VisionProfile runProfile = this.sriSelect.VisionProfile.Clone();
            bool hasSystemInputAction = runProfile.AssistantSteps.Any(
                step => step != null && step.ActionDefinition != null &&
                    step.ActionDefinition.Type != VisionActionType.None);
            if (hasSystemInputAction)
            {
                DialogResult confirmation = MessageBox.Show(
                    this,
                    UiText("Vision_ActionSafetyPrompt"),
                    UiText("Vision_ActionSafetyTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes)
                {
                    this.DisposeVisionProfileTemplates(runProfile);
                    this.SetVisionAssistantStatus(UiText("Vision_ActionSafetyCancelled"));
                    return;
                }
                runProfile.AllowSystemInput = true;
            }
            CancellationTokenSource runCancellation = new CancellationTokenSource();
            this.visionAssistantCancellation = runCancellation;
            CancellationToken cancellationToken = runCancellation.Token;
            this.ClearVisionAssistantLog();
            this.SetVisionAssistantLogExpanded(true);
            this.SetVisionAssistantButtons(true);
            this.SetVisionAssistantStatus(UiText("Vision_AssistantRunning"));
            this.AppendVisionAssistantLog(UiText("Vision_AssistantRunning"));

            try
            {
                Task<VisionAssistantRunResult> runTask = Task.Run(
                    () => VisionAssistantRunner.Run(
                        runProfile,
                        recognizer,
                        cancellationToken,
                        this.VisionAssistantLogEmitted));
                this.visionAssistantTask = runTask;
                runTask.ContinueWith(
                    task => this.CompleteVisionAssistant(task, runProfile, runCancellation),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                this.DisposeVisionProfileTemplates(runProfile);
                this.SetVisionAssistantButtons(false);
                this.SetVisionAssistantStatus(string.Format(
                    UiText("Vision_AssistantFailed"),
                    ex.Message));
                this.DisposeVisionAssistantCancellation(runCancellation);
            }
        }

        private void bStopVisionAssistant_Click(object sender, EventArgs e)
        {
            if (this.visionAssistantCancellation != null)
            {
                this.visionAssistantCancellation.Cancel();
                this.SetVisionAssistantStatus(UiText("Vision_AssistantStopping"));
                this.AppendVisionAssistantLog(UiText("Vision_AssistantStopping"));
            }
        }

        private void VisionAssistantLogEmitted(object sender, VisionAssistantLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            string message = entry.StepIndex < 0
                ? entry.Message
                : string.Format("[{0}] {1}", entry.StepIndex + 1, entry.Message);
            this.SetVisionAssistantStatus(message);
            this.AppendVisionAssistantLog(message);
        }

        private void CompleteVisionAssistant(
            Task<VisionAssistantRunResult> task,
            Socket_VisionProfile runProfile,
            CancellationTokenSource runCancellation)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                this.DisposeVisionProfileTemplates(runProfile);
                this.DisposeVisionAssistantCancellation(runCancellation);
                return;
            }

            try
            {
                this.BeginInvoke(new Action(() =>
                {
                    // A completed task can queue this callback just before the user
                    // starts the next run. An old callback must never reset the new
                    // run's buttons, task field, or cancellation source.
                    if (!object.ReferenceEquals(this.visionAssistantTask, task))
                    {
                        this.DisposeVisionProfileTemplates(runProfile);
                        this.DisposeVisionAssistantCancellation(runCancellation);
                        return;
                    }

                    try
                    {
                        if (task.IsCanceled)
                        {
                            this.SetVisionAssistantStatus(UiText("Vision_AssistantCancelled"));
                            this.AppendVisionAssistantLog(UiText("Vision_AssistantCancelled"));
                        }
                        else if (task.IsFaulted)
                        {
                            Exception error = task.Exception == null
                                ? null
                                : task.Exception.GetBaseException();
                            this.SetVisionAssistantStatus(string.Format(
                                UiText("Vision_AssistantFailed"),
                                error == null ? "Unknown error." : error.Message));
                            this.AppendVisionAssistantLog(this.lVisionAssistantStatus.Text);
                        }
                        else
                        {
                            VisionAssistantRunResult result = task.Result;
                            if (result == null || (!result.Succeeded && !result.Cancelled))
                            {
                                this.SetVisionAssistantStatus(string.Format(
                                    UiText("Vision_AssistantFailed"),
                                    result == null ? "No result." : result.Error));
                                this.AppendVisionAssistantLog(this.lVisionAssistantStatus.Text);
                            }
                            else if (result.Cancelled)
                            {
                                this.SetVisionAssistantStatus(UiText("Vision_AssistantCancelled"));
                                this.AppendVisionAssistantLog(UiText("Vision_AssistantCancelled"));
                            }
                            else
                            {
                                this.SetVisionAssistantStatus(string.Format(
                                    UiText("Vision_AssistantSucceeded"),
                                    result.CompletedSteps));
                                this.AppendVisionAssistantLog(this.lVisionAssistantStatus.Text);
                            }
                        }
                        this.SetVisionAssistantButtons(false);
                    }
                    finally
                    {
                        this.DisposeVisionProfileTemplates(runProfile);
                        this.DisposeVisionAssistantCancellation(runCancellation);
                        if (object.ReferenceEquals(this.visionAssistantTask, task))
                        {
                            this.visionAssistantTask = null;
                        }
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                this.DisposeVisionProfileTemplates(runProfile);
                this.DisposeVisionAssistantCancellation(runCancellation);
            }
        }

        private void SetVisionAssistantButtons(bool running)
        {
            if (this.bVisionRunSteps != null)
            {
                this.bVisionRunSteps.Enabled = !running;
            }
            if (this.bVisionStopSteps != null)
            {
                this.bVisionStopSteps.Enabled = running;
            }
        }

        private void bToggleVisionAssistantLog_Click(object sender, EventArgs e)
        {
            this.SetVisionAssistantLogExpanded(!this.visionAssistantLogExpanded);
        }

        private void SetVisionAssistantLogExpanded(bool expanded)
        {
            if (this.visionAssistantLogHost == null || this.bToggleVisionAssistantLog == null ||
                this.txtVisionAssistantLog == null)
            {
                return;
            }

            this.visionAssistantLogExpanded = expanded;
            this.visionAssistantLogHost.SuspendLayout();
            this.visionAssistantLogHost.Controls.Remove(this.txtVisionAssistantLog);
            this.visionAssistantLogHost.RowStyles.Clear();

            if (expanded)
            {
                this.visionAssistantLogHost.RowCount = 2;
                this.visionAssistantLogHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                this.visionAssistantLogHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
                this.visionAssistantLogHost.Controls.Add(this.txtVisionAssistantLog, 0, 1);
                this.txtVisionAssistantLog.Visible = true;
                this.bToggleVisionAssistantLog.Text = UiText("Vision_AssistantLogHide");
                this.bToggleVisionAssistantLog.AccessibleName = UiText("Vision_AssistantLogHide");
            }
            else
            {
                this.visionAssistantLogHost.RowCount = 1;
                this.visionAssistantLogHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                this.txtVisionAssistantLog.Visible = false;
                this.bToggleVisionAssistantLog.Text = UiText("Vision_AssistantLogShow");
                this.bToggleVisionAssistantLog.AccessibleName = UiText("Vision_AssistantLogShow");
            }

            this.visionAssistantLogHost.ResumeLayout(true);
            this.PerformLayout();
        }

        private void SetVisionAssistantStatus(string status)
        {
            if (this.lVisionAssistantStatus == null || this.IsDisposed)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.QueueVisionAssistantUiUpdate(status, null);
                return;
            }

            this.lVisionAssistantStatus.Text = status ?? string.Empty;
        }

        private void ClearVisionAssistantLog()
        {
            if (this.txtVisionAssistantLog == null || this.IsDisposed)
            {
                return;
            }
            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke(new Action(this.ClearVisionAssistantLog));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            lock (this.visionAssistantUiSync)
            {
                this.pendingVisionAssistantLogs.Clear();
                this.pendingVisionAssistantStatus = null;
            }

            this.txtVisionAssistantLog.Clear();
        }

        private void AppendVisionAssistantLog(string message)
        {
            if (this.txtVisionAssistantLog == null || this.IsDisposed ||
                string.IsNullOrWhiteSpace(message))
            {
                return;
            }
            if (this.InvokeRequired)
            {
                this.QueueVisionAssistantUiUpdate(null, message);
                return;
            }

            this.txtVisionAssistantLog.AppendText(
                string.Format(
                    "[{0}] {1}{2}",
                    DateTime.Now.ToString("HH:mm:ss"),
                    message.Trim(),
                    Environment.NewLine));
            if (this.txtVisionAssistantLog.TextLength > 16000)
            {
                this.txtVisionAssistantLog.Text = this.txtVisionAssistantLog.Text.Substring(4000);
                this.txtVisionAssistantLog.SelectionStart = this.txtVisionAssistantLog.TextLength;
            }
            this.txtVisionAssistantLog.ScrollToCaret();
        }

        private void QueueVisionAssistantUiUpdate(string status, string logMessage)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            bool schedule;
            lock (this.visionAssistantUiSync)
            {
                if (status != null)
                {
                    this.pendingVisionAssistantStatus = status;
                }
                if (!string.IsNullOrWhiteSpace(logMessage))
                {
                    if (this.pendingVisionAssistantLogs.Count >= 128)
                    {
                        this.pendingVisionAssistantLogs.Dequeue();
                    }
                    this.pendingVisionAssistantLogs.Enqueue(logMessage);
                }
                schedule = !this.visionAssistantUiUpdateScheduled;
                this.visionAssistantUiUpdateScheduled = true;
            }

            if (!schedule)
            {
                return;
            }

            try
            {
                this.BeginInvoke(new Action(this.FlushVisionAssistantUiUpdates));
            }
            catch (ObjectDisposedException)
            {
                lock (this.visionAssistantUiSync)
                {
                    this.visionAssistantUiUpdateScheduled = false;
                }
            }
            catch (InvalidOperationException)
            {
                lock (this.visionAssistantUiSync)
                {
                    this.visionAssistantUiUpdateScheduled = false;
                }
            }
        }

        private void FlushVisionAssistantUiUpdates()
        {
            string status;
            List<string> logs = new List<string>();
            lock (this.visionAssistantUiSync)
            {
                status = this.pendingVisionAssistantStatus;
                this.pendingVisionAssistantStatus = null;
                while (this.pendingVisionAssistantLogs.Count > 0)
                {
                    logs.Add(this.pendingVisionAssistantLogs.Dequeue());
                }
                this.visionAssistantUiUpdateScheduled = false;
            }

            if (this.IsDisposed)
            {
                return;
            }
            if (status != null)
            {
                this.SetVisionAssistantStatus(status);
            }
            foreach (string message in logs)
            {
                this.AppendVisionAssistantLog(message);
            }
        }

        private void DisposeVisionAssistantCancellation(CancellationTokenSource cancellation)
        {
            if (cancellation != null)
            {
                cancellation.Dispose();
                if (object.ReferenceEquals(this.visionAssistantCancellation, cancellation))
                {
                    this.visionAssistantCancellation = null;
                }
            }
        }

        private void DisposeVisionProfileTemplates(Socket_VisionProfile profile)
        {
            if (profile == null || profile.AssistantSteps == null)
            {
                return;
            }

            foreach (VisionAssistantStep step in profile.AssistantSteps)
            {
                if (step == null)
                {
                    continue;
                }
                this.DisposeVisionConditionTemplates(step.Condition);
                this.DisposeVisionConditionTemplates(step.Verification);
            }
        }

        private void DisposeVisionConditionTemplates(VisionConditionDefinition condition)
        {
            if (condition == null)
            {
                return;
            }
            if (condition.Template != null)
            {
                condition.Template.Dispose();
                condition.Template = null;
            }
            condition.DisposeTemplateVariants();
        }

        private VisionOcrOptions ReadVisionOcrOptions()
        {
            VisionOcrOptions options = this.sriSelect == null || this.sriSelect.VisionProfile == null ||
                this.sriSelect.VisionProfile.OcrOptions == null
                ? new VisionOcrOptions()
                : this.sriSelect.VisionProfile.OcrOptions.Clone();
            options.ScaleFactor = (int)this.nudVisionOcrScale.Value;
            options.ConvertToGrayscale = true;
            options.UseBinaryThreshold = this.chkVisionOcrBinary.Checked;
            options.BinaryThreshold = (byte)this.nudVisionOcrThreshold.Value;
            options.Contrast = (double)this.nudVisionOcrContrast.Value;
            options.UseAdaptiveThreshold = this.chkVisionOcrAdaptive.Checked;
            options.AdaptiveThresholdWindowSize = (int)this.nudVisionOcrAdaptiveWindow.Value;
            options.AdaptiveThresholdOffset = (int)this.nudVisionOcrAdaptiveOffset.Value;
            options.Invert = this.chkVisionOcrInvert.Checked;
            options.UseDenoise = this.chkVisionOcrDenoise.Checked;
            options.UseSharpen = this.chkVisionOcrSharpen.Checked;
            options.CharacterWhitelist = this.txtVisionOcrWhitelist.Text.Trim();
            VisionOcrEngineChoice engine = this.cbbVisionOcrEngine == null
                ? null
                : this.cbbVisionOcrEngine.SelectedItem as VisionOcrEngineChoice;
            options.Engine = engine == null ? VisionOcrEngine.Auto : engine.Engine;
            options.OnnxModelDirectory = this.txtVisionOcrModelDirectory == null
                ? "models\\ocr"
                : this.txtVisionOcrModelDirectory.Text.Trim();
            options.OnnxDetectionThreshold = this.nudVisionOcrDetectionThreshold == null
                ? 0.3D
                : (double)this.nudVisionOcrDetectionThreshold.Value;
            options.OnnxRecognitionThreshold = this.nudVisionOcrRecognitionThreshold == null
                ? 0.5D
                : (double)this.nudVisionOcrRecognitionThreshold.Value;
            options.OnnxMaxImageSide = this.nudVisionOcrMaxImageSide == null
                ? 960
                : (int)this.nudVisionOcrMaxImageSide.Value;
            options.PythonExecutablePath = this.txtVisionPythonExecutable == null
                ? string.Empty
                : this.txtVisionPythonExecutable.Text.Trim();
            options.PythonWorkerScriptPath = this.txtVisionPythonWorkerScript == null
                ? string.Empty
                : this.txtVisionPythonWorkerScript.Text.Trim();
            options.PythonWorkerTimeoutMilliseconds = this.nudVisionPythonWorkerTimeout == null
                ? 15000
                : (int)this.nudVisionPythonWorkerTimeout.Value;
            return options;
        }

        private VisionTemplateMatchOptions ReadVisionTemplateMatchOptions()
        {
            double tolerance = this.nudVisionTemplateScaleTolerance == null
                ? 10D
                : (double)this.nudVisionTemplateScaleTolerance.Value / 100D;
            return new VisionTemplateMatchOptions
            {
                NormalizeBrightness = this.chkVisionTemplateNormalize == null ||
                    this.chkVisionTemplateNormalize.Checked,
                AllowScaleVariation = this.chkVisionTemplateScale != null &&
                    this.chkVisionTemplateScale.Checked,
                MinimumScale = Math.Max(0.5D, 1D - tolerance),
                MaximumScale = Math.Min(2D, 1D + tolerance),
                ScaleStep = 0.05D
            };
        }

        private void SelectVisionOcrEngine(VisionOcrEngine engine)
        {
            if (this.cbbVisionOcrEngine == null)
            {
                return;
            }
            for (int i = 0; i < this.cbbVisionOcrEngine.Items.Count; i++)
            {
                VisionOcrEngineChoice choice = this.cbbVisionOcrEngine.Items[i] as VisionOcrEngineChoice;
                if (choice != null && choice.Engine == engine)
                {
                    this.cbbVisionOcrEngine.SelectedIndex = i;
                    return;
                }
            }
            this.cbbVisionOcrEngine.SelectedIndex = 0;
        }

        private void UpdateVisionOcrModelStatus()
        {
            if (this.lVisionOcrModelStatus == null || this.txtVisionOcrModelDirectory == null)
            {
                return;
            }
            VisionOcrEngineChoice choice = this.cbbVisionOcrEngine == null
                ? null
                : this.cbbVisionOcrEngine.SelectedItem as VisionOcrEngineChoice;
            if (choice != null && choice.Engine == VisionOcrEngine.Tesseract)
            {
                this.lVisionOcrModelStatus.Text = UiText("Vision_OcrTesseractStatus");
                return;
            }
            if (choice != null && choice.Engine == VisionOcrEngine.PythonWorker)
            {
                try
                {
                    VisionOcrOptions options = this.ReadVisionOcrOptions();
                    VisionPythonWorkerHealth health = this.EnsureVisionPythonWorker().CheckHealth(
                        options,
                        false,
                        CancellationToken.None);
                    this.lVisionOcrModelStatus.Text = health.IsReady
                        ? UiText("Vision_OcrPythonReady") + " - " + health.Summary
                        : UiText("Vision_OcrPythonUnavailable") + ": " + health.Summary;
                }
                catch (Exception ex)
                {
                    this.lVisionOcrModelStatus.Text = UiText("Vision_OcrPythonUnavailable") + ": " + ex.Message;
                }
                return;
            }
            this.lVisionOcrModelStatus.Text = VisionOnnxTextRecognizer.DescribeModelDirectory(
                this.txtVisionOcrModelDirectory.Text.Trim());
        }

        private VisionPythonWorkerTextRecognizer EnsureVisionPythonWorker()
        {
            if (this.visionPythonWorker == null)
            {
                this.visionPythonWorker = new VisionPythonWorkerTextRecognizer();
            }
            if (this.visionTextRecognizer == null)
            {
                this.visionTextRecognizer = new VisionAutoTextRecognizer(
                    new VisionOnnxTextRecognizer(),
                    new VisionTesseractRecognizer("tesseract.exe"),
                    this.visionPythonWorker);
            }
            return this.visionPythonWorker;
        }

        private void bVisionBrowsePythonExecutable_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Python executable|python.exe;py.exe|Executable files|*.exe|All files|*.*";
                dialog.Title = UiText("Vision_OcrPythonExecutable");
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.txtVisionPythonExecutable.Text = dialog.FileName;
                }
            }
        }

        private void bVisionBrowsePythonWorkerScript_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Python worker|worker.py|Python files|*.py|All files|*.*";
                dialog.Title = UiText("Vision_OcrPythonWorkerScript");
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.txtVisionPythonWorkerScript.Text = dialog.FileName;
                }
            }
        }

        private void bVisionResetPythonSettings_Click(object sender, EventArgs e)
        {
            this.txtVisionPythonExecutable.Text = string.Empty;
            this.txtVisionPythonWorkerScript.Text = string.Empty;
            this.txtVisionOcrModelDirectory.Text = "models\\ocr";
            this.SetVisionNumber(this.nudVisionPythonWorkerTimeout, 15000);
            this.UpdateVisionOcrModelStatus();
        }

        private async void bVisionTestPythonWorker_Click(object sender, EventArgs e)
        {
            await this.RunVisionPythonHealthCheckAsync(true, true);
        }

        private async void BeginVisionPythonPrewarm()
        {
            await this.RunVisionPythonHealthCheckAsync(true, false);
        }

        private async Task RunVisionPythonHealthCheckAsync(bool warmup, bool fromButton)
        {
            if (this.visionRobotClosing ||
                this.visionPythonHealthTask != null && !this.visionPythonHealthTask.IsCompleted)
            {
                return;
            }
            VisionOcrOptions options;
            try
            {
                options = this.ReadVisionOcrOptions();
            }
            catch (Exception ex)
            {
                this.lVisionOcrModelStatus.Text = UiText("Vision_OcrPythonUnavailable") + ": " + ex.Message;
                return;
            }

            CancellationTokenSource cancellation = new CancellationTokenSource();
            this.visionPythonHealthCancellation = cancellation;
            if (fromButton && this.bVisionTestPythonWorker != null)
            {
                this.bVisionTestPythonWorker.Enabled = false;
            }
            this.lVisionOcrModelStatus.Text = warmup
                ? UiText("Vision_OcrPythonPrewarming")
                : UiText("Vision_OcrPythonTesting");
            try
            {
                VisionPythonWorkerTextRecognizer worker = this.EnsureVisionPythonWorker();
                this.visionPythonHealthTask = Task.Run(
                    () => worker.CheckHealth(options, warmup, cancellation.Token),
                    CancellationToken.None);
                VisionPythonWorkerHealth health = await this.visionPythonHealthTask;
                if (this.IsDisposed || this.visionRobotClosing)
                {
                    return;
                }
                this.lVisionOcrModelStatus.Text = health.IsReady
                    ? UiText("Vision_OcrPythonTestSuccess") + " " + health.Summary
                    : UiText("Vision_OcrPythonTestFailed") + " " + health.Summary;
            }
            catch (OperationCanceledException)
            {
                if (!this.IsDisposed && !this.visionRobotClosing)
                {
                    this.lVisionOcrModelStatus.Text = UiText("Vision_OcrPythonUnavailable");
                }
            }
            catch (Exception ex)
            {
                if (!this.IsDisposed && !this.visionRobotClosing)
                {
                    this.lVisionOcrModelStatus.Text = UiText("Vision_OcrPythonTestFailed") + " " + ex.Message;
                }
            }
            finally
            {
                if (object.ReferenceEquals(this.visionPythonHealthCancellation, cancellation))
                {
                    this.visionPythonHealthCancellation = null;
                }
                cancellation.Dispose();
                if (fromButton && this.bVisionTestPythonWorker != null && !this.IsDisposed)
                {
                    this.bVisionTestPythonWorker.Enabled = true;
                }
                this.visionPythonHealthTask = null;
            }
        }

        private void bSaveVisionProfile_Click(object sender, EventArgs e)
        {
            DataTable originalRobotInstructions = this.sriSelect == null
                ? new DataTable()
                : CopyInstructions(this.sriSelect.RInstruction);
            DataTable originalEditorInstructions = CopyInstructions(this.dtRobotInstruction);
            string originalName = this.sriSelect == null ? string.Empty : this.sriSelect.RName;
            Socket_VisionProfile originalProfile = this.sriSelect == null ||
                this.sriSelect.VisionProfile == null
                ? null
                : this.sriSelect.VisionProfile.Clone();

            if (!this.TryApplyVisionProfile())
            {
                this.RestoreRobotEditorState(
                    originalName,
                    originalRobotInstructions,
                    originalEditorInstructions,
                    originalProfile);
                return;
            }

            if (!Socket_Cache.RobotList.SaveRobotList_ToDB())
            {
                this.RestoreRobotEditorState(
                    originalName,
                    originalRobotInstructions,
                    originalEditorInstructions,
                    originalProfile);
                this.SetVisionStatus(UiText("UI_AssistantSaveFailed"));
                return;
            }
            this.DisposeVisionProfileTemplates(originalProfile);
            this.SetVisionStatus(UiText("Vision_ProfileSaved"));
        }

        private bool TryApplyVisionProfile()
        {
            VisionWindowInfo window = this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
            if (window == null)
            {
                this.SetVisionStatus(UiText("Vision_SelectWindowHint"));
                return false;
            }

            try
            {
                VisionCaptureSettings captureSettings = this.ReadVisionCaptureSettings();
                captureSettings.Validate();
                Size clientSize;
                string clientSizeError;
                if (!VisionWindowService.TryValidateClientSize(
                    window.Handle,
                    captureSettings,
                    out clientSize,
                    out clientSizeError))
                {
                    throw new InvalidOperationException(clientSizeError);
                }
                VisionRegion region = this.ReadVisionRegion();
                if (!region.FitsWithin(clientSize))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            "The region must stay inside the client area ({0}x{1}).",
                            clientSize.Width,
                            clientSize.Height));
                }
                if (captureSettings.RequireExactClientSize && region.UseNormalizedCoordinates)
                {
                    throw new InvalidOperationException(UiText("Vision_FixedCoordinatesRequired"));
                }

                Socket_VisionProfile profile = this.sriSelect.VisionProfile ?? new Socket_VisionProfile();
                VisionAssistantStep selectedStep = this.cbbVisionSteps == null
                    ? null
                    : this.cbbVisionSteps.SelectedItem as VisionAssistantStep;
                if (selectedStep != null && !this.updatingVisionStepEditor)
                {
                    this.ApplyVisionConditionEditors(selectedStep, region);
                    selectedStep.ActionDefinition = this.ReadVisionActionDefinition();
                    VisionConditionDefinition verification = null;
                    try
                    {
                        verification = this.ReadVisionVerificationCondition();
                        if (this.chkVisionActionVerification.Checked && verification == null)
                        {
                            this.SetVisionStatus(UiText("Vision_VerificationIncomplete"));
                            return false;
                        }
                        if (verification != null && !verification.Region.FitsWithin(clientSize))
                        {
                            this.SetVisionStatus(
                                string.Format(
                                    UiText("Vision_ProfileInvalid"),
                                    "The verification region must stay inside the target client area."));
                            return false;
                        }
                        if (verification != null && captureSettings.RequireExactClientSize &&
                            verification.Region.UseNormalizedCoordinates)
                        {
                            this.SetVisionStatus(string.Format(
                                UiText("Vision_ProfileInvalid"),
                                UiText("Vision_FixedCoordinatesRequired")));
                            return false;
                        }
                        this.DisposeVisionConditionTemplates(selectedStep.Verification);
                        selectedStep.VerificationEnabled = verification != null;
                        selectedStep.Verification = verification;
                        selectedStep.VerificationUsesSeparateRegion = verification != null &&
                            this.chkVisionVerificationSeparateRegion.Checked;
                        verification = null;
                    }
                    finally
                    {
                        if (verification != null)
                        {
                            this.DisposeVisionConditionTemplates(verification);
                        }
                    }
                }
                profile.WindowHandle = window.Handle.ToInt64();
                profile.ProcessId = window.ProcessId;
                profile.ProcessName = window.ProcessName;
                profile.ProcessPath = window.ProcessPath;
                profile.ProcessStartTimeUtcTicks = window.ProcessStartTimeUtcTicks;
                profile.WindowTitle = window.WindowTitle;
                profile.Region = region;
                profile.CaptureSettings = captureSettings;
                profile.OcrOptions = this.ReadVisionOcrOptions();
                profile.OcrCondition = profile.OcrCondition ?? new VisionTextCondition();
                profile.OcrCondition.ExpectedText = this.txtVisionOcrKeyword.Text.Trim();
                profile.OcrCondition.MatchMode = VisionTextMatchMode.Contains;
                if (!this.SyncVisionAssistantStepsFromInstructions(profile))
                {
                    return false;
                }
                this.ValidateVisionProfileRegions(profile, captureSettings, clientSize);
                this.sriSelect.VisionProfile = profile;
                return true;
            }
            catch (Exception ex)
            {
                this.SetVisionStatus(string.Format(UiText("Vision_ProfileInvalid"), ex.Message));
                return false;
            }
        }

        private VisionRegion ReadVisionRegion()
        {
            VisionWindowInfo window = this.cbbVisionWindows == null
                ? null
                : this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
            return new VisionRegion
            {
                X = (int)this.nudVisionX.Value,
                Y = (int)this.nudVisionY.Value,
                Width = (int)this.nudVisionWidth.Value,
                Height = (int)this.nudVisionHeight.Value,
                UseNormalizedCoordinates = this.chkVisionNormalized != null && this.chkVisionNormalized.Checked,
                ReferenceWidth = window == null ? 0 : window.ClientSize.Width,
                ReferenceHeight = window == null ? 0 : window.ClientSize.Height
            };
        }

        private VisionCaptureSettings ReadVisionCaptureSettings()
        {
            VisionCaptureSettings settings = this.sriSelect == null || this.sriSelect.VisionProfile == null ||
                this.sriSelect.VisionProfile.CaptureSettings == null
                ? new VisionCaptureSettings()
                : this.sriSelect.VisionProfile.CaptureSettings.Clone();
            VisionCaptureSourceChoice choice = this.cbbVisionCaptureSource == null
                ? null
                : this.cbbVisionCaptureSource.SelectedItem as VisionCaptureSourceChoice;
            settings.SourceMode = choice == null ? VisionCaptureSourceMode.Auto : choice.Mode;
            settings.MinimumIntervalMilliseconds = (int)this.nudVisionCaptureInterval.Value;
            settings.SkipUnchangedFrames = this.chkVisionSkipUnchanged.Checked;
            settings.HistoryLimit = (int)this.nudVisionHistoryLimit.Value;
            settings.SaveFailureSnapshots = this.chkVisionSaveFailureSnapshots != null &&
                this.chkVisionSaveFailureSnapshots.Checked;
            settings.RequireExactClientSize = this.chkVisionFixedClientSize != null &&
                this.chkVisionFixedClientSize.Checked;
            settings.RequiredClientWidth = this.nudVisionRequiredWidth == null
                ? 0
                : (int)this.nudVisionRequiredWidth.Value;
            settings.RequiredClientHeight = this.nudVisionRequiredHeight == null
                ? 0
                : (int)this.nudVisionRequiredHeight.Value;
            return settings;
        }

        private void ValidateVisionProfileRegions(
            Socket_VisionProfile profile,
            VisionCaptureSettings captureSettings,
            Size clientSize)
        {
            if (profile == null || captureSettings == null)
            {
                throw new ArgumentNullException("profile");
            }
            if (profile.Region == null || !profile.Region.FitsWithin(clientSize))
            {
                throw new InvalidOperationException(
                    string.Format(
                        "The profile region must stay inside the target client area ({0}x{1}).",
                        clientSize.Width,
                        clientSize.Height));
            }
            if (captureSettings.RequireExactClientSize && profile.Region.UseNormalizedCoordinates)
            {
                throw new InvalidOperationException(UiText("Vision_FixedCoordinatesRequired"));
            }
            if (profile.AssistantSteps == null)
            {
                return;
            }
            foreach (VisionAssistantStep step in profile.AssistantSteps)
            {
                if (step == null)
                {
                    throw new InvalidOperationException("A vision assistant step is missing.");
                }
                if (step.Condition == null)
                {
                    throw new InvalidOperationException(
                        string.Format("Step '{0}' has no vision condition.", step.Name));
                }
                if (step.ActionDefinition != null)
                {
                    step.ActionDefinition.Validate();
                }
                string error;
                if (!step.Condition.FitsWithin(clientSize, out error))
                {
                    throw new InvalidOperationException(
                        string.Format("Step '{0}' has an invalid region: {1}", step.Name, error));
                }
                if (captureSettings.RequireExactClientSize &&
                    step.Condition.Region != null &&
                    step.Condition.Region.UseNormalizedCoordinates)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            UiText("Vision_ProfileInvalid"),
                            UiText("Vision_FixedCoordinatesRequired")));
                }
                if (step.VerificationEnabled)
                {
                    if (step.Verification == null)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                UiText("Vision_ProfileInvalid"),
                                UiText("Vision_VerificationIncomplete")));
                    }
                    if (!step.Verification.FitsWithin(clientSize, out error))
                    {
                        throw new InvalidOperationException(
                            string.Format("Step '{0}' has an invalid verification region: {1}", step.Name, error));
                    }
                    if (captureSettings.RequireExactClientSize &&
                        step.Verification.Region != null &&
                        step.Verification.Region.UseNormalizedCoordinates)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                UiText("Vision_ProfileInvalid"),
                                UiText("Vision_FixedCoordinatesRequired")));
                    }
                }
            }
        }

        private void SelectVisionCaptureSource(VisionCaptureSourceMode mode)
        {
            if (this.cbbVisionCaptureSource == null)
            {
                return;
            }
            for (int i = 0; i < this.cbbVisionCaptureSource.Items.Count; i++)
            {
                VisionCaptureSourceChoice choice = this.cbbVisionCaptureSource.Items[i] as VisionCaptureSourceChoice;
                if (choice != null && choice.Mode == mode)
                {
                    this.cbbVisionCaptureSource.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SaveVisionPreview()
        {
            if (this.visionPreview == null)
            {
                this.SetVisionStatus(UiText("Vision_NoPreview"));
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = UiText("Vision_PngFilter");
                dialog.DefaultExt = "png";
                dialog.AddExtension = true;
                dialog.FileName = UiText("Vision_DefaultFileName");
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        VisionWindowService.SavePng(this.visionPreview, dialog.FileName);
                        this.SetVisionStatus(string.Format(UiText("Vision_ScreenshotSaved"), dialog.FileName));
                    }
                    catch (Exception ex)
                    {
                        this.SetVisionStatus(string.Format(UiText("Vision_CaptureFailed"), ex.Message));
                    }
                }
            }
        }

        private void bPreviewVisionOcr_Click(object sender, EventArgs e)
        {
            if (this.visionPreview == null)
            {
                this.SetVisionOcrStatus(UiText("Vision_NoPreview"));
                return;
            }
            try
            {
                Bitmap processed = VisionImagePreprocessor.Preprocess(
                    this.visionPreview,
                    this.ReadVisionOcrOptions(),
                    CancellationToken.None);
                this.DisposeVisionPreprocessedPreview();
                this.visionPreprocessedPreview = processed;
                this.pbVisionTemplate.Image = this.visionPreprocessedPreview;
                this.SetVisionOcrStatus(UiText("Vision_OcrPreviewReady"));
            }
            catch (Exception ex)
            {
                this.SetVisionOcrStatus(string.Format(UiText("Vision_OcrFailed"), ex.Message));
            }
        }

        private void bRestoreVisionPreview_Click(object sender, EventArgs e)
        {
            this.DisposeVisionPreprocessedPreview();
            this.pbVisionTemplate.Image = this.visionTemplate;
            this.SetVisionOcrStatus(UiText("Vision_RestorePreviewReady"));
        }

        private void AddVisionHistory(Bitmap capture, string displayName)
        {
            int limit = this.nudVisionHistoryLimit == null
                ? 30
                : (int)this.nudVisionHistoryLimit.Value;
            if (limit <= 0)
            {
                return;
            }
            this.visionHistory.Insert(0, new VisionHistoryEntry(displayName, capture));
            while (this.visionHistory.Count > limit)
            {
                VisionHistoryEntry last = this.visionHistory[this.visionHistory.Count - 1];
                this.visionHistory.RemoveAt(this.visionHistory.Count - 1);
                last.Dispose();
            }
            this.RefreshVisionHistory();
        }

        private void RefreshVisionHistory()
        {
            if (this.cbbVisionHistory == null)
            {
                return;
            }
            int selected = this.cbbVisionHistory.SelectedIndex;
            this.cbbVisionHistory.BeginUpdate();
            try
            {
                this.cbbVisionHistory.Items.Clear();
                foreach (VisionHistoryEntry entry in this.visionHistory)
                {
                    this.cbbVisionHistory.Items.Add(entry);
                }
                if (this.cbbVisionHistory.Items.Count > 0)
                {
                    this.cbbVisionHistory.SelectedIndex = Math.Max(0, Math.Min(selected, this.cbbVisionHistory.Items.Count - 1));
                }
            }
            finally
            {
                this.cbbVisionHistory.EndUpdate();
            }
        }

        private void cbbVisionHistory_SelectedIndexChanged(object sender, EventArgs e)
        {
            VisionHistoryEntry entry = this.cbbVisionHistory.SelectedItem as VisionHistoryEntry;
            if (entry == null || entry.Image == null)
            {
                return;
            }
            this.DisposeVisionCapture();
            this.visionPreview = new Bitmap(entry.Image);
            this.pbVisionPreview.Image = this.visionPreview;
            this.UpdateVisionPreviewEmptyState();
            this.visionPreviewSelection = Rectangle.Empty;
            this.SetVisionStatus(string.Format(UiText("Vision_HistoryLoaded"), entry.DisplayName));
        }

        private void bClearVisionHistory_Click(object sender, EventArgs e)
        {
            foreach (VisionHistoryEntry entry in this.visionHistory)
            {
                entry.Dispose();
            }
            this.visionHistory.Clear();
            this.RefreshVisionHistory();
        }

        private Bitmap CreateVisionTemplateFromSelection()
        {
            if (this.visionPreview == null)
            {
                throw new InvalidOperationException(UiText("Vision_NoPreview"));
            }
            Rectangle crop = this.visionPreviewSelection;
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                crop = new Rectangle(Point.Empty, this.visionPreview.Size);
            }
            crop.Intersect(new Rectangle(Point.Empty, this.visionPreview.Size));
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                throw new InvalidOperationException(UiText("Vision_NoSelection"));
            }
            return this.visionPreview.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        }

        private Rectangle GetVisionPreviewImageRectangle()
        {
            if (this.pbVisionPreview == null || this.visionPreview == null)
            {
                return Rectangle.Empty;
            }
            float scale = Math.Min(
                this.pbVisionPreview.ClientSize.Width / (float)this.visionPreview.Width,
                this.pbVisionPreview.ClientSize.Height / (float)this.visionPreview.Height);
            int width = Math.Max(1, (int)Math.Round(this.visionPreview.Width * scale));
            int height = Math.Max(1, (int)Math.Round(this.visionPreview.Height * scale));
            return new Rectangle(
                (this.pbVisionPreview.ClientSize.Width - width) / 2,
                (this.pbVisionPreview.ClientSize.Height - height) / 2,
                width,
                height);
        }

        private Point PreviewPointToImage(Point point)
        {
            Rectangle imageRectangle = this.GetVisionPreviewImageRectangle();
            if (imageRectangle.IsEmpty)
            {
                return Point.Empty;
            }
            int x = (int)Math.Round((point.X - imageRectangle.X) * (double)this.visionPreview.Width / imageRectangle.Width);
            int y = (int)Math.Round((point.Y - imageRectangle.Y) * (double)this.visionPreview.Height / imageRectangle.Height);
            return new Point(
                Math.Max(0, Math.Min(this.visionPreview.Width - 1, x)),
                Math.Max(0, Math.Min(this.visionPreview.Height - 1, y)));
        }

        private void pbVisionPreview_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || this.visionPreview == null ||
                !this.GetVisionPreviewImageRectangle().Contains(e.Location))
            {
                return;
            }
            this.visionPreviewSelecting = true;
            this.visionPreviewSelectionStart = this.PreviewPointToImage(e.Location);
            this.visionPreviewSelection = new Rectangle(this.visionPreviewSelectionStart, Size.Empty);
        }

        private void pbVisionPreview_MouseMove(object sender, MouseEventArgs e)
        {
            if (!this.visionPreviewSelecting || this.visionPreview == null)
            {
                return;
            }
            Point current = this.PreviewPointToImage(e.Location);
            this.visionPreviewSelection = Rectangle.FromLTRB(
                Math.Min(this.visionPreviewSelectionStart.X, current.X),
                Math.Min(this.visionPreviewSelectionStart.Y, current.Y),
                Math.Max(this.visionPreviewSelectionStart.X, current.X),
                Math.Max(this.visionPreviewSelectionStart.Y, current.Y));
            this.pbVisionPreview.Invalidate();
        }

        private void pbVisionPreview_MouseUp(object sender, MouseEventArgs e)
        {
            if (!this.visionPreviewSelecting)
            {
                return;
            }
            this.visionPreviewSelecting = false;
            if (this.visionPreviewSelection.Width > 1 && this.visionPreviewSelection.Height > 1 &&
                this.visionPreviewOriginRegion != null)
            {
                this.chkVisionNormalized.Checked = false;
                this.SetVisionNumber(this.nudVisionX, this.visionPreviewOriginRegion.X + this.visionPreviewSelection.X);
                this.SetVisionNumber(this.nudVisionY, this.visionPreviewOriginRegion.Y + this.visionPreviewSelection.Y);
                this.SetVisionNumber(this.nudVisionWidth, this.visionPreviewSelection.Width);
                this.SetVisionNumber(this.nudVisionHeight, this.visionPreviewSelection.Height);
                this.SetVisionStatus(UiText("Vision_SelectionApplied"));
            }
            this.pbVisionPreview.Invalidate();
        }

        private void pbVisionPreview_Paint(object sender, PaintEventArgs e)
        {
            if (this.visionPreview == null || this.visionPreviewSelection.Width <= 0 ||
                this.visionPreviewSelection.Height <= 0)
            {
                return;
            }
            Rectangle imageRectangle = this.GetVisionPreviewImageRectangle();
            Rectangle selection = new Rectangle(
                imageRectangle.X + (int)Math.Round(this.visionPreviewSelection.X * imageRectangle.Width / (double)this.visionPreview.Width),
                imageRectangle.Y + (int)Math.Round(this.visionPreviewSelection.Y * imageRectangle.Height / (double)this.visionPreview.Height),
                Math.Max(1, (int)Math.Round(this.visionPreviewSelection.Width * imageRectangle.Width / (double)this.visionPreview.Width)),
                Math.Max(1, (int)Math.Round(this.visionPreviewSelection.Height * imageRectangle.Height / (double)this.visionPreview.Height)));
            using (Pen pen = new Pen(Color.Red, 2F))
            {
                e.Graphics.DrawRectangle(pen, selection);
            }
        }

        private void SetVisionStatus(string status)
        {
            if (this.lVisionStatus != null)
            {
                this.lVisionStatus.Text = status ?? string.Empty;
            }
        }

        private void SetVisionMatchStatus(string status)
        {
            if (this.lVisionMatchStatus != null)
            {
                this.lVisionMatchStatus.Text = status ?? string.Empty;
            }
        }

        private void SetVisionOcrStatus(string status)
        {
            if (this.lVisionOcrStatus != null)
            {
                string text = status ?? string.Empty;
                this.lVisionOcrStatus.Text = text;
                this.lVisionOcrStatus.Visible = !string.IsNullOrWhiteSpace(text);
            }
        }

        private void SetVisionNumber(NumericUpDown editor, int value)
        {
            if (editor == null)
            {
                return;
            }

            decimal safeValue = Math.Max(editor.Minimum, Math.Min(editor.Maximum, value));
            editor.Value = safeValue;
        }

        private void SetVisionDecimal(NumericUpDown editor, double value)
        {
            if (editor == null)
            {
                return;
            }

            decimal decimalValue;
            try
            {
                decimalValue = (decimal)value;
            }
            catch
            {
                decimalValue = value < 0D ? editor.Minimum : editor.Maximum;
            }
            editor.Value = Math.Max(editor.Minimum, Math.Min(editor.Maximum, decimalValue));
        }

        private void DisposeVisionPreview()
        {
            this.DisposeVisionCapture();
            this.DisposeVisionTemplate();
            this.DisposeVisionPreprocessedPreview();
            this.ClearVisionHistoryForDispose();
        }

        private void DisposeVisionCapture()
        {
            if (this.pbVisionPreview != null)
            {
                this.pbVisionPreview.Image = null;
            }
            if (this.visionPreview != null)
            {
                this.visionPreview.Dispose();
                this.visionPreview = null;
            }
            this.UpdateVisionPreviewEmptyState();
        }

        private void UpdateVisionPreviewEmptyState()
        {
            if (this.lVisionPreviewEmpty != null)
            {
                this.lVisionPreviewEmpty.Visible = this.visionPreview == null;
            }
        }

        private void DisposeVisionTemplate()
        {
            if (this.pbVisionTemplate != null)
            {
                this.pbVisionTemplate.Image = null;
            }
            if (this.visionTemplate != null)
            {
                this.visionTemplate.Dispose();
                this.visionTemplate = null;
            }
        }

        private void DisposeVisionPreprocessedPreview()
        {
            if (this.visionPreprocessedPreview != null)
            {
                this.visionPreprocessedPreview.Dispose();
                this.visionPreprocessedPreview = null;
            }
        }

        private void ClearVisionHistoryForDispose()
        {
            foreach (VisionHistoryEntry entry in this.visionHistory)
            {
                entry.Dispose();
            }
            this.visionHistory.Clear();
        }

        private void Socket_RobotForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            this.visionRobotClosing = true;
            if (this.sr.Worker.IsBusy)
            {
                this.sr.StopRobot();
            }
            if (this.visionAssistantCancellation != null)
            {
                this.visionAssistantCancellation.Cancel();
            }
            if (this.visionOcrCancellation != null)
            {
                this.visionOcrCancellation.Cancel();
            }
            if (this.visionMatchCancellation != null)
            {
                this.visionMatchCancellation.Cancel();
            }
            if (!this.sr.Worker.IsBusy)
            {
                this.DisposeVisionTextRecognizerWhenIdle();
            }
            this.DisposeVisionPreview();
        }

        private void DisposeVisionTextRecognizerWhenIdle()
        {
            IDisposable disposableRecognizer = this.visionTextRecognizer as IDisposable;
            this.visionTextRecognizer = null;
            if (disposableRecognizer == null)
            {
                return;
            }

            List<Task> runningTasks = new List<Task>();
            if (this.visionAssistantTask != null && !this.visionAssistantTask.IsCompleted)
            {
                runningTasks.Add(this.visionAssistantTask);
            }
            if (this.visionOcrTask != null && !this.visionOcrTask.IsCompleted)
            {
                runningTasks.Add(this.visionOcrTask);
            }
            if (this.visionPythonHealthTask != null && !this.visionPythonHealthTask.IsCompleted)
            {
                runningTasks.Add(this.visionPythonHealthTask);
            }
            if (runningTasks.Count == 0)
            {
                disposableRecognizer.Dispose();
                return;
            }

            Task.WhenAll(runningTasks).ContinueWith(
                task => disposableRecognizer.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
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

        private void dgvRobotInstruction_CellToolTipTextNeeded(
            object sender,
            DataGridViewCellToolTipTextNeededEventArgs e)
        {
            try
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                    e.RowIndex >= this.dgvRobotInstruction.Rows.Count ||
                    e.ColumnIndex >= this.dgvRobotInstruction.Columns.Count)
                {
                    return;
                }

                DataGridViewColumn column = this.dgvRobotInstruction.Columns[e.ColumnIndex];
                if (column.Name == "cRobotInstruction_Content")
                {
                    DataGridViewCell cell = this.dgvRobotInstruction.Rows[e.RowIndex].Cells[e.ColumnIndex];
                    e.ToolTipText = cell.FormattedValue == null
                        ? string.Empty
                        : cell.FormattedValue.ToString();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//保存按钮

        private void RestoreRobotEditorState(
            string originalName,
            DataTable originalRobotInstructions,
            DataTable originalEditorInstructions,
            Socket_VisionProfile originalProfile)
        {
            if (this.sriSelect == null)
            {
                return;
            }

            Socket_VisionProfile currentProfile = this.sriSelect.VisionProfile;
            if (currentProfile != null && !object.ReferenceEquals(currentProfile, originalProfile))
            {
                this.DisposeVisionProfileTemplates(currentProfile);
            }

            this.sriSelect.RName = originalName;
            this.sriSelect.RInstruction = originalRobotInstructions == null
                ? new DataTable()
                : originalRobotInstructions.Copy();
            this.sriSelect.VisionProfile = originalProfile;
            this.txtRobotName.Text = originalName;

            if (this.dgvRobotInstruction != null)
            {
                this.dgvRobotInstruction.DataSource = null;
            }
            this.dtRobotInstruction = originalEditorInstructions == null
                ? new DataTable()
                : originalEditorInstructions.Copy();
            if (this.dgvRobotInstruction != null)
            {
                this.dgvRobotInstruction.DataSource = this.dtRobotInstruction;
            }

            this.InitVisionProfile();
            this.UpdateRobotInstructionPanel();
        }

        private static DataTable CopyInstructions(DataTable source)
        {
            return source == null ? new DataTable() : source.Copy();
        }

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

                string originalName = this.sriSelect == null
                    ? string.Empty
                    : this.sriSelect.RName;
                DataTable originalRobotInstructions = this.sriSelect == null
                    ? new DataTable()
                    : CopyInstructions(this.sriSelect.RInstruction);
                DataTable originalEditorInstructions = CopyInstructions(this.dtRobotInstruction);
                Socket_VisionProfile originalProfile = this.sriSelect == null ||
                    this.sriSelect.VisionProfile == null
                    ? null
                    : this.sriSelect.VisionProfile.Clone();
                bool hasVisionConfiguration = this.HasVisionInstructionRows() ||
                    (this.cbbVisionWindows != null && this.cbbVisionWindows.SelectedItem != null);
                if (hasVisionConfiguration && !this.TryApplyVisionProfile())
                {
                    this.RestoreRobotEditorState(
                        originalName,
                        originalRobotInstructions,
                        originalEditorInstructions,
                        originalProfile);
                    return;
                }

                Socket_Cache.Robot.UpdateRobot(sriSelect, RName_New, this.dtRobotInstruction);

                if (!Socket_Cache.RobotList.SaveRobotList_ToDB())
                {
                    this.RestoreRobotEditorState(
                        originalName,
                        originalRobotInstructions,
                        originalEditorInstructions,
                        originalProfile);
                    Socket_Operation.ShowMessageBox(
                        Socket_Operation.GetUiText("UI_AssistantSaveFailed"));
                    return;
                }

                this.DisposeVisionProfileTemplates(originalProfile);

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
                    if (this.HasVisionInstructionRows() && !this.TryApplyVisionProfile())
                    {
                        return;
                    }

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

                        Dictionary<string, object> parameters;
                        if (!this.TryBuildRobotExecutionParameters(out parameters))
                        {
                            this.bExecute.Enabled = true;
                            this.bStop.Enabled = false;
                            this.tcRobotInstruction.Enabled = true;
                            if (this.dgvRobotInstruction.ContextMenuStrip != null)
                            {
                                this.dgvRobotInstruction.ContextMenuStrip.Enabled = true;
                            }
                            return;
                        }
                        sr.StartRobot(sriSelect.RName, this.dtRobotInstruction, parameters);
                    }
                }                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private bool TryBuildRobotExecutionParameters(out Dictionary<string, object> parameters)
        {
            parameters = new Dictionary<string, object>();
            if (!this.HasVisionInstructionRows())
            {
                return true;
            }

            Socket_VisionProfile runProfile = this.sriSelect.VisionProfile.Clone();
            bool hasSystemInputAction = runProfile.AssistantSteps.Any(
                step => step != null && step.ActionDefinition != null &&
                    step.ActionDefinition.Type != VisionActionType.None);
            if (hasSystemInputAction)
            {
                DialogResult confirmation = MessageBox.Show(
                    this,
                    UiText("Vision_ActionSafetyPrompt"),
                    UiText("Vision_ActionSafetyTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes)
                {
                    this.DisposeVisionProfileTemplates(runProfile);
                    this.SetVisionAssistantStatus(UiText("Vision_ActionSafetyCancelled"));
                    return false;
                }
                runProfile.AllowSystemInput = true;
            }

            this.robotExecutionVisionProfile = runProfile;
            parameters["VisionProfile"] = runProfile;
            parameters["VisionTextRecognizer"] = this.visionTextRecognizer;
            return true;
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

                if (this.robotExecutionVisionProfile != null)
                {
                    this.DisposeVisionProfileTemplates(this.robotExecutionVisionProfile);
                    this.robotExecutionVisionProfile = null;
                }

                if (this.visionRobotClosing)
                {
                    this.DisposeVisionTextRecognizerWhenIdle();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
            finally
            {
                if (this.robotExecutionVisionProfile != null)
                {
                    this.DisposeVisionProfileTemplates(this.robotExecutionVisionProfile);
                    this.robotExecutionVisionProfile = null;
                }
                if (this.visionRobotClosing)
                {
                    this.DisposeVisionTextRecognizerWhenIdle();
                }
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

            this.UpdateRobotInstructionPanel();

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

                        if (this.sriSelect != null)
                        {
                            this.SyncVisionAssistantStepsFromInstructions(this.sriSelect.VisionProfile);
                            this.RefreshVisionAssistantSteps();
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
                this.robotInstructionPanelManuallyCollapsed = false;
                this.robotInstructionPanelManuallyExpanded = false;
                this.UpdateRobotInstructionPanel();
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

        private static Rectangle CalculateVisionSelectionRectangle(Point start, Point end)
        {
            return Rectangle.FromLTRB(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X),
                Math.Max(start.Y, end.Y));
        }

        private sealed class VisionRegionPickerForm : Form
        {
            private readonly string hint;
            private bool selecting;
            private Point selectionStart;

            public Rectangle SelectedRectangle { get; private set; }

            public VisionRegionPickerForm(Rectangle clientBoundsScreen, string hint)
            {
                this.hint = hint ?? string.Empty;
                this.FormBorderStyle = FormBorderStyle.None;
                this.StartPosition = FormStartPosition.Manual;
                this.ShowInTaskbar = false;
                this.TopMost = true;
                this.DoubleBuffered = true;
                this.KeyPreview = true;
                this.Cursor = Cursors.Cross;
                this.BackColor = Color.Black;
                this.Opacity = 0.28D;
                this.Location = clientBoundsScreen.Location;
                this.ClientSize = clientBoundsScreen.Size;
                this.SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer,
                    true);
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if ((keyData & Keys.KeyCode) == Keys.Escape)
                {
                    this.SelectedRectangle = Rectangle.Empty;
                    this.DialogResult = DialogResult.Cancel;
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                this.selecting = true;
                this.selectionStart = e.Location;
                this.SelectedRectangle = new Rectangle(e.Location, Size.Empty);
                this.Capture = true;
                this.Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!this.selecting)
                {
                    return;
                }

                this.SelectedRectangle = CalculateVisionSelectionRectangle(
                    this.selectionStart,
                    new Point(
                        Math.Max(0, Math.Min(this.ClientSize.Width, e.X)),
                        Math.Max(0, Math.Min(this.ClientSize.Height, e.Y))));
                this.Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (!this.selecting || e.Button != MouseButtons.Left)
                {
                    return;
                }

                this.selecting = false;
                this.Capture = false;
                this.SelectedRectangle = CalculateVisionSelectionRectangle(
                    this.selectionStart,
                    new Point(
                        Math.Max(0, Math.Min(this.ClientSize.Width, e.X)),
                        Math.Max(0, Math.Min(this.ClientSize.Height, e.Y))));
                if (this.SelectedRectangle.Width >= 2 && this.SelectedRectangle.Height >= 2)
                {
                    this.DialogResult = DialogResult.OK;
                }
                else
                {
                    this.SelectedRectangle = Rectangle.Empty;
                }
                this.Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (SolidBrush brush = new SolidBrush(Color.White))
                using (Font font = new Font(SystemFonts.DefaultFont, FontStyle.Bold))
                {
                    e.Graphics.DrawString(this.hint, font, brush, new PointF(12F, 12F));
                }

                if (this.SelectedRectangle.Width <= 0 || this.SelectedRectangle.Height <= 0)
                {
                    return;
                }

                using (Pen pen = new Pen(Color.Red, 2F))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(45, Color.DeepSkyBlue)))
                {
                    e.Graphics.FillRectangle(fill, this.SelectedRectangle);
                    e.Graphics.DrawRectangle(pen, this.SelectedRectangle);
                }
            }
        }

        private sealed class VisionCaptureSourceChoice
        {
            public VisionCaptureSourceMode Mode { get; private set; }

            public string Text { get; private set; }

            public VisionCaptureSourceChoice(VisionCaptureSourceMode mode, string text)
            {
                this.Mode = mode;
                this.Text = text;
            }

            public override string ToString()
            {
                return this.Text;
            }
        }

        private sealed class VisionHistoryEntry : IDisposable
        {
            public string DisplayName { get; private set; }

            public Bitmap Image { get; private set; }

            public VisionHistoryEntry(string displayName, Bitmap source)
            {
                this.DisplayName = displayName ?? string.Empty;
                this.Image = source == null ? null : new Bitmap(source);
            }

            public void Dispose()
            {
                if (this.Image != null)
                {
                    this.Image.Dispose();
                    this.Image = null;
                }
            }

            public override string ToString()
            {
                return this.DisplayName;
            }
        }

        private sealed class VisionConditionChoice
        {
            public VisionConditionType Type { get; private set; }

            public string Text { get; private set; }

            public VisionConditionChoice(VisionConditionType type, string text)
            {
                this.Type = type;
                this.Text = text;
            }

            public override string ToString()
            {
                return this.Text;
            }
        }

        private sealed class VisionOcrEngineChoice
        {
            public VisionOcrEngine Engine { get; private set; }

            public string Text { get; private set; }

            public VisionOcrEngineChoice(VisionOcrEngine engine, string text)
            {
                this.Engine = engine;
                this.Text = text;
            }

            public override string ToString()
            {
                return this.Text;
            }
        }

        private sealed class VisionActionChoice
        {
            public VisionActionType Type { get; private set; }

            public string Text { get; private set; }

            public VisionActionChoice(VisionActionType type, string text)
            {
                this.Type = type;
                this.Text = text;
            }

            public override string ToString()
            {
                return this.Text;
            }
        }

        private sealed class VisionScrollChoice
        {
            public VisionScrollDirection Direction { get; private set; }

            public string Text { get; private set; }

            public VisionScrollChoice(VisionScrollDirection direction, string text)
            {
                this.Direction = direction;
                this.Text = text;
            }

            public override string ToString()
            {
                return this.Text;
            }
        }
        private sealed class VisionVerificationChoice
        {
            public VisionConditionType Type { get; private set; }
            public string Text { get; private set; }
            public VisionVerificationChoice(VisionConditionType type, string text)
            {
                this.Type = type;
                this.Text = text;
            }
            public override string ToString()
            {
                return this.Text;
            }
        }

        private sealed class VisionFailureChoice
        {
            public VisionFailurePolicy Policy { get; private set; }

            public string Text { get; private set; }

            public VisionFailureChoice(VisionFailurePolicy policy, string text)
            {
                this.Policy = policy;
                this.Text = text;
            }

            public override string ToString()
            {
                return this.Text;
            }
        }
    }
}
