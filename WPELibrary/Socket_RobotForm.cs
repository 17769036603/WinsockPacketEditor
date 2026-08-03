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
        private ComboBox cbbVisionWindows;
        private NumericUpDown nudVisionX;
        private NumericUpDown nudVisionY;
        private NumericUpDown nudVisionWidth;
        private NumericUpDown nudVisionHeight;
        private PictureBox pbVisionPreview;
        private PictureBox pbVisionTemplate;
        private Label lVisionStatus;
        private Label lVisionMatchStatus;
        private Label lVisionOcrStatus;
        private Label lVisionAssistantStatus;
        private TextBox txtVisionAssistantLog;
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
        private Button bVisionRecognizeText;
        private Button bVisionCancelOcr;
        private Button bVisionPreviewOcr;
        private Button bVisionRestorePreview;
        private Button bVisionMatchTemplate;
        private Button bVisionCancelMatch;
        private TextBox txtVisionOcrKeyword;
        private ComboBox cbbVisionSteps;
        private ComboBox cbbVisionConditionType;
        private NumericUpDown nudVisionNumberMinimum;
        private NumericUpDown nudVisionNumberMaximum;
        private NumericUpDown nudVisionConfirmations;
        private NumericUpDown nudVisionPollInterval;
        private NumericUpDown nudVisionTimeout;
        private NumericUpDown nudVisionRetries;
        private ComboBox cbbVisionFailurePolicy;
        private Button bVisionRunSteps;
        private Button bVisionStopSteps;
        private IVisionTextRecognizer visionTextRecognizer;
        private CancellationTokenSource visionAssistantCancellation;
        private Task<VisionAssistantRunResult> visionAssistantTask;
        private bool updatingVisionStepEditor;
        private Bitmap visionPreview;
        private Bitmap visionTemplate;
        private Bitmap visionPreprocessedPreview;
        private VisionRegion visionPreviewOriginRegion;
        private Rectangle visionPreviewSelection;
        private bool visionPreviewSelecting;
        private Point visionPreviewSelectionStart;
        private CheckBox chkVisionNormalized;
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

        #region//窗体加载

        public Socket_RobotForm(Socket_RobotInfo sri)
        {
            try
            {
                MultiLanguage.SetDefaultLanguage(MultiLanguage.DefaultLanguage);
                InitializeComponent();
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

        private void InitVisionLayout()
        {
            TabPage visionTab = new TabPage
            {
                Name = "tpInstruction_Vision",
                Text = UiText("Vision_Tab"),
                Padding = new Padding(6),
                AutoScroll = true,
                UseVisualStyleBackColor = true
            };

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                RowCount = 15,
                Padding = new Padding(3),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));

            this.cbbVisionWindows = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true,
                DisplayMember = "DisplayName"
            };
            this.cbbVisionWindows.SelectedIndexChanged += this.cbbVisionWindows_SelectedIndexChanged;

            Button bRefreshVisionWindows = new Button
            {
                Dock = DockStyle.Fill,
                Text = UiText("Vision_RefreshWindows"),
                UseVisualStyleBackColor = true
            };
            bRefreshVisionWindows.Click += this.bRefreshVisionWindows_Click;

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = UiText("Vision_TargetWindow"),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            root.Controls.Add(this.cbbVisionWindows, 1, 0);
            root.SetColumnSpan(this.cbbVisionWindows, 2);
            root.Controls.Add(bRefreshVisionWindows, 3, 0);

            this.nudVisionX = CreateVisionNumber(0);
            this.nudVisionY = CreateVisionNumber(0);
            this.nudVisionWidth = CreateVisionNumber(1);
            this.nudVisionHeight = CreateVisionNumber(1);
            AddVisionNumber(root, UiText("Vision_X"), this.nudVisionX, 0, 1);
            AddVisionNumber(root, UiText("Vision_Y"), this.nudVisionY, 2, 1);
            AddVisionNumber(root, UiText("Vision_Width"), this.nudVisionWidth, 0, 2);
            AddVisionNumber(root, UiText("Vision_Height"), this.nudVisionHeight, 2, 2);

            FlowLayoutPanel captureToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 4, 0, 2),
                Margin = new Padding(0)
            };
            Button bCaptureVision = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_Capture"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            bCaptureVision.Click += this.bCaptureVision_Click;
            captureToolbar.Controls.Add(bCaptureVision);

            captureToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_CaptureSource"),
                Margin = new Padding(0, 5, 4, 0)
            });
            this.cbbVisionCaptureSource = new ComboBox
            {
                Width = 90,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                Margin = new Padding(0, 2, 6, 0)
            };
            this.cbbVisionCaptureSource.Items.Add(new VisionCaptureSourceChoice(VisionCaptureSourceMode.Auto, UiText("Vision_CaptureAuto")));
            this.cbbVisionCaptureSource.Items.Add(new VisionCaptureSourceChoice(VisionCaptureSourceMode.Screen, UiText("Vision_CaptureScreen")));
            this.cbbVisionCaptureSource.Items.Add(new VisionCaptureSourceChoice(VisionCaptureSourceMode.WindowRender, UiText("Vision_CaptureWindowRender")));
            this.cbbVisionCaptureSource.SelectedIndex = 0;
            captureToolbar.Controls.Add(this.cbbVisionCaptureSource);

            captureToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_CaptureInterval"),
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionCaptureInterval = CreateVisionNumber(0);
            this.nudVisionCaptureInterval.Maximum = 60000;
            this.nudVisionCaptureInterval.Value = 150;
            this.nudVisionCaptureInterval.Width = 58;
            this.nudVisionCaptureInterval.Margin = new Padding(0, 2, 6, 0);
            captureToolbar.Controls.Add(this.nudVisionCaptureInterval);
            this.chkVisionSkipUnchanged = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_SkipUnchanged"),
                Checked = true,
                Margin = new Padding(0, 5, 6, 0)
            };
            captureToolbar.Controls.Add(this.chkVisionSkipUnchanged);
            this.chkVisionNormalized = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_NormalizedRegion"),
                Checked = true,
                Margin = new Padding(0, 5, 6, 0)
            };
            captureToolbar.Controls.Add(this.chkVisionNormalized);
            captureToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_HistoryLimit"),
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionHistoryLimit = CreateVisionNumber(0);
            this.nudVisionHistoryLimit.Maximum = 200;
            this.nudVisionHistoryLimit.Value = 30;
            this.nudVisionHistoryLimit.Width = 48;
            this.nudVisionHistoryLimit.Margin = new Padding(0, 2, 6, 0);
            captureToolbar.Controls.Add(this.nudVisionHistoryLimit);
            this.chkVisionSaveFailureSnapshots = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_SaveFailureSnapshots"),
                Margin = new Padding(0, 5, 6, 0)
            };
            captureToolbar.Controls.Add(this.chkVisionSaveFailureSnapshots);

            Button bSaveVisionProfile = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_SaveProfile"),
                UseVisualStyleBackColor = true
            };
            bSaveVisionProfile.Click += this.bSaveVisionProfile_Click;
            Button bSaveVisionPreview = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_SaveScreenshot"),
                UseVisualStyleBackColor = true
            };
            bSaveVisionPreview.Click += (sender, e) => this.SaveVisionPreview();
            captureToolbar.Controls.Add(bSaveVisionPreview);
            captureToolbar.Controls.Add(bSaveVisionProfile);
            root.Controls.Add(captureToolbar, 0, 3);
            root.SetColumnSpan(captureToolbar, 4);

            FlowLayoutPanel templateToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 4, 0, 2),
                Margin = new Padding(0)
            };
            templateToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_Template"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 6, 0)
            });
            Button bLoadVisionTemplate = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_LoadTemplate"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            bLoadVisionTemplate.Click += this.bLoadVisionTemplate_Click;
            templateToolbar.Controls.Add(bLoadVisionTemplate);
            Button bSaveVisionTemplate = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_SaveTemplate"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 12, 0)
            };
            bSaveVisionTemplate.Click += this.bSaveVisionTemplate_Click;
            templateToolbar.Controls.Add(bSaveVisionTemplate);
            Button bAddVisionTemplateVariant = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_AddTemplateVariant"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 12, 0)
            };
            bAddVisionTemplateVariant.Click += this.bAddVisionTemplateVariant_Click;
            templateToolbar.Controls.Add(bAddVisionTemplateVariant);
            templateToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_Threshold"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 6, 0)
            });
            this.nudVisionThreshold = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100,
                Value = 90,
                Increment = 1,
                Width = 64,
                Margin = new Padding(0, 2, 8, 0)
            };
            templateToolbar.Controls.Add(this.nudVisionThreshold);
            this.chkVisionTemplateNormalize = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_TemplateNormalize"),
                Checked = true,
                Margin = new Padding(0, 5, 6, 0)
            };
            templateToolbar.Controls.Add(this.chkVisionTemplateNormalize);
            this.chkVisionTemplateScale = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_TemplateScale"),
                Margin = new Padding(0, 5, 6, 0)
            };
            templateToolbar.Controls.Add(this.chkVisionTemplateScale);
            templateToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_TemplateTolerance"),
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionTemplateScaleTolerance = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 50,
                Value = 10,
                Width = 48,
                Margin = new Padding(0, 2, 6, 0)
            };
            templateToolbar.Controls.Add(this.nudVisionTemplateScaleTolerance);
            this.bVisionMatchTemplate = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_Match"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            this.bVisionMatchTemplate.Click += this.bMatchVisionTemplate_Click;
            templateToolbar.Controls.Add(this.bVisionMatchTemplate);
            this.bVisionCancelMatch = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_MatchCancel"),
                UseVisualStyleBackColor = true,
                Enabled = false,
                Margin = new Padding(0)
            };
            this.bVisionCancelMatch.Click += this.bCancelVisionMatch_Click;
            templateToolbar.Controls.Add(this.bVisionCancelMatch);
            root.Controls.Add(templateToolbar, 0, 4);
            root.SetColumnSpan(templateToolbar, 4);

            FlowLayoutPanel ocrToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 4, 0, 2),
                Margin = new Padding(0)
            };
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_Ocr"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 6, 0)
            });
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_OcrScale"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionOcrScale = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 4,
                Value = 2,
                Width = 48,
                Margin = new Padding(0, 2, 8, 0)
            };
            ocrToolbar.Controls.Add(this.nudVisionOcrScale);
            this.chkVisionOcrBinary = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_OcrBinary"),
                Margin = new Padding(0, 5, 8, 0)
            };
            ocrToolbar.Controls.Add(this.chkVisionOcrBinary);
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_OcrThreshold"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionOcrThreshold = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 255,
                Value = 160,
                Width = 58,
                Margin = new Padding(0, 2, 8, 0)
            };
            ocrToolbar.Controls.Add(this.nudVisionOcrThreshold);
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_OcrContrast"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionOcrContrast = new NumericUpDown
            {
                Minimum = 0.1M,
                Maximum = 5M,
                DecimalPlaces = 1,
                Increment = 0.1M,
                Value = 1M,
                Width = 54,
                Margin = new Padding(0, 2, 8, 0)
            };
            ocrToolbar.Controls.Add(this.nudVisionOcrContrast);
            this.chkVisionOcrAdaptive = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_OcrAdaptive"),
                Margin = new Padding(0, 5, 6, 0)
            };
            ocrToolbar.Controls.Add(this.chkVisionOcrAdaptive);
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_OcrAdaptiveWindow"),
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionOcrAdaptiveWindow = new NumericUpDown
            {
                Minimum = 3,
                Maximum = 51,
                Increment = 2,
                Value = 15,
                Width = 48,
                Margin = new Padding(0, 2, 6, 0)
            };
            ocrToolbar.Controls.Add(this.nudVisionOcrAdaptiveWindow);
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_OcrAdaptiveOffset"),
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionOcrAdaptiveOffset = new NumericUpDown
            {
                Minimum = -64,
                Maximum = 64,
                Value = 8,
                Width = 48,
                Margin = new Padding(0, 2, 6, 0)
            };
            ocrToolbar.Controls.Add(this.nudVisionOcrAdaptiveOffset);
            this.chkVisionOcrInvert = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_OcrInvert"),
                Margin = new Padding(0, 5, 6, 0)
            };
            ocrToolbar.Controls.Add(this.chkVisionOcrInvert);
            this.chkVisionOcrDenoise = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_OcrDenoise"),
                Margin = new Padding(0, 5, 6, 0)
            };
            ocrToolbar.Controls.Add(this.chkVisionOcrDenoise);
            this.chkVisionOcrSharpen = new CheckBox
            {
                AutoSize = true,
                Text = UiText("Vision_OcrSharpen"),
                Margin = new Padding(0, 5, 6, 0)
            };
            ocrToolbar.Controls.Add(this.chkVisionOcrSharpen);
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_OcrWhitelist"),
                Margin = new Padding(0, 5, 4, 0)
            });
            this.txtVisionOcrWhitelist = new TextBox
            {
                Width = 120,
                Margin = new Padding(0, 2, 8, 0)
            };
            ocrToolbar.Controls.Add(this.txtVisionOcrWhitelist);
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_OcrKeyword"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 4, 0)
            });
            this.txtVisionOcrKeyword = new TextBox
            {
                Width = 140,
                Margin = new Padding(0, 2, 8, 0)
            };
            ocrToolbar.Controls.Add(this.txtVisionOcrKeyword);
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_Minimum"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionNumberMinimum = CreateVisionDecimal(-1000000000D, 1000000000D, 0D);
            this.nudVisionNumberMinimum.Width = 72;
            ocrToolbar.Controls.Add(this.nudVisionNumberMinimum);
            ocrToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_Maximum"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 4, 0)
            });
            this.nudVisionNumberMaximum = CreateVisionDecimal(-1000000000D, 1000000000D, 1000000000D);
            this.nudVisionNumberMaximum.Width = 72;
            ocrToolbar.Controls.Add(this.nudVisionNumberMaximum);
            this.bVisionRecognizeText = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_OcrRecognize"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0)
            };
            this.bVisionRecognizeText.Click += this.bRecognizeVisionText_Click;
            ocrToolbar.Controls.Add(this.bVisionRecognizeText);
            this.bVisionCancelOcr = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_OcrCancel"),
                UseVisualStyleBackColor = true,
                Enabled = false,
                Margin = new Padding(0, 0, 6, 0)
            };
            this.bVisionCancelOcr.Click += this.bCancelVisionOcr_Click;
            ocrToolbar.Controls.Add(this.bVisionCancelOcr);
            this.bVisionPreviewOcr = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_OcrPreview"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            this.bVisionPreviewOcr.Click += this.bPreviewVisionOcr_Click;
            ocrToolbar.Controls.Add(this.bVisionPreviewOcr);
            this.bVisionRestorePreview = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_RestorePreview"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0)
            };
            this.bVisionRestorePreview.Click += this.bRestoreVisionPreview_Click;
            ocrToolbar.Controls.Add(this.bVisionRestorePreview);
            root.Controls.Add(ocrToolbar, 0, 5);
            root.SetColumnSpan(ocrToolbar, 4);

            FlowLayoutPanel stepToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 4, 0, 2),
                Margin = new Padding(0)
            };
            stepToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_Steps"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 6, 0)
            });
            this.cbbVisionConditionType = new ComboBox
            {
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                Margin = new Padding(0, 2, 6, 0)
            };
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(
                VisionConditionType.TextAppears,
                UiText("Vision_TextAppears")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(
                VisionConditionType.TextDisappears,
                UiText("Vision_TextDisappears")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(
                VisionConditionType.NumberInRange,
                UiText("Vision_NumberRange")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(
                VisionConditionType.TemplateAppears,
                UiText("Vision_TemplateAppears")));
            this.cbbVisionConditionType.Items.Add(new VisionConditionChoice(
                VisionConditionType.TemplateDisappears,
                UiText("Vision_TemplateDisappears")));
            this.cbbVisionConditionType.SelectedIndex = 0;
            stepToolbar.Controls.Add(this.cbbVisionConditionType);
            this.cbbVisionSteps = new ComboBox
            {
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Name",
                Margin = new Padding(0, 2, 6, 0)
            };
            this.cbbVisionSteps.SelectedIndexChanged += this.cbbVisionSteps_SelectedIndexChanged;
            stepToolbar.Controls.Add(this.cbbVisionSteps);
            Button bAddVisionOcrStep = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_AddOcrStep"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            bAddVisionOcrStep.Click += this.bAddVisionOcrStep_Click;
            stepToolbar.Controls.Add(bAddVisionOcrStep);
            Button bAddVisionTemplateStep = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_AddTemplateStep"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            bAddVisionTemplateStep.Click += this.bAddVisionTemplateStep_Click;
            stepToolbar.Controls.Add(bAddVisionTemplateStep);
            Button bRemoveVisionStep = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_RemoveStep"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0)
            };
            bRemoveVisionStep.Click += this.bRemoveVisionStep_Click;
            stepToolbar.Controls.Add(bRemoveVisionStep);
            stepToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_Confirmations"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(8, 5, 4, 0)
            });
            this.nudVisionConfirmations = CreateVisionNumber(1);
            this.nudVisionConfirmations.Maximum = 10;
            this.nudVisionConfirmations.Value = 3;
            this.nudVisionConfirmations.Width = 52;
            stepToolbar.Controls.Add(this.nudVisionConfirmations);
            stepToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_PollMilliseconds"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(8, 5, 4, 0)
            });
            this.nudVisionPollInterval = CreateVisionNumber(10);
            this.nudVisionPollInterval.Maximum = 60000;
            this.nudVisionPollInterval.Value = 250;
            this.nudVisionPollInterval.Width = 72;
            stepToolbar.Controls.Add(this.nudVisionPollInterval);
            stepToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_TimeoutMilliseconds"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(8, 5, 4, 0)
            });
            this.nudVisionTimeout = CreateVisionNumber(10);
            this.nudVisionTimeout.Maximum = 3600000;
            this.nudVisionTimeout.Value = 10000;
            this.nudVisionTimeout.Width = 80;
            stepToolbar.Controls.Add(this.nudVisionTimeout);
            stepToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_Retries"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(8, 5, 4, 0)
            });
            this.nudVisionRetries = CreateVisionNumber(0);
            this.nudVisionRetries.Maximum = 100;
            this.nudVisionRetries.Value = 0;
            this.nudVisionRetries.Width = 52;
            stepToolbar.Controls.Add(this.nudVisionRetries);
            this.cbbVisionFailurePolicy = new ComboBox
            {
                Width = 90,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                Margin = new Padding(8, 2, 0, 0)
            };
            this.cbbVisionFailurePolicy.Items.Add(new VisionFailureChoice(
                VisionFailurePolicy.Stop,
                UiText("Vision_StopPolicy")));
            this.cbbVisionFailurePolicy.Items.Add(new VisionFailureChoice(
                VisionFailurePolicy.Skip,
                UiText("Vision_SkipPolicy")));
            this.cbbVisionFailurePolicy.SelectedIndex = 0;
            stepToolbar.Controls.Add(this.cbbVisionFailurePolicy);
            root.Controls.Add(stepToolbar, 0, 6);
            root.SetColumnSpan(stepToolbar, 4);

            FlowLayoutPanel assistantToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 4, 0, 2),
                Margin = new Padding(0)
            };
            assistantToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_Assistant"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 8, 0)
            });
            this.bVisionRunSteps = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_RunSteps"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            this.bVisionRunSteps.Click += this.bRunVisionAssistant_Click;
            assistantToolbar.Controls.Add(this.bVisionRunSteps);
            this.bVisionStopSteps = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_StopSteps"),
                UseVisualStyleBackColor = true,
                Enabled = false,
                Margin = new Padding(0)
            };
            this.bVisionStopSteps.Click += this.bStopVisionAssistant_Click;
            assistantToolbar.Controls.Add(this.bVisionStopSteps);
            root.Controls.Add(assistantToolbar, 0, 7);
            root.SetColumnSpan(assistantToolbar, 4);

            this.pbVisionPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 248, 248),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            this.pbVisionPreview.MouseDown += this.pbVisionPreview_MouseDown;
            this.pbVisionPreview.MouseMove += this.pbVisionPreview_MouseMove;
            this.pbVisionPreview.MouseUp += this.pbVisionPreview_MouseUp;
            this.pbVisionPreview.Paint += this.pbVisionPreview_Paint;
            root.Controls.Add(this.pbVisionPreview, 0, 8);
            root.SetColumnSpan(this.pbVisionPreview, 2);

            this.pbVisionTemplate = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 248, 248),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            root.Controls.Add(this.pbVisionTemplate, 2, 8);
            root.SetColumnSpan(this.pbVisionTemplate, 2);

            FlowLayoutPanel historyToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 2, 0, 2),
                Margin = new Padding(0)
            };
            historyToolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiText("Vision_History"),
                Margin = new Padding(0, 5, 6, 0)
            });
            this.cbbVisionHistory = new ComboBox
            {
                Width = 420,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "DisplayName",
                Margin = new Padding(0, 2, 6, 0)
            };
            this.cbbVisionHistory.SelectedIndexChanged += this.cbbVisionHistory_SelectedIndexChanged;
            historyToolbar.Controls.Add(this.cbbVisionHistory);
            Button bClearVisionHistory = new Button
            {
                AutoSize = true,
                Text = UiText("Vision_ClearHistory"),
                UseVisualStyleBackColor = true,
                Margin = new Padding(0)
            };
            bClearVisionHistory.Click += this.bClearVisionHistory_Click;
            historyToolbar.Controls.Add(bClearVisionHistory);
            root.Controls.Add(historyToolbar, 0, 9);
            root.SetColumnSpan(historyToolbar, 4);

            this.lVisionStatus = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
                Text = UiText("Vision_SelectWindowHint")
            };
            root.Controls.Add(this.lVisionStatus, 0, 10);
            root.SetColumnSpan(this.lVisionStatus, 4);

            this.lVisionMatchStatus = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
                Text = UiText("Vision_NoTemplate")
            };
            root.Controls.Add(this.lVisionMatchStatus, 0, 11);
            root.SetColumnSpan(this.lVisionMatchStatus, 4);

            this.lVisionOcrStatus = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
                Text = UiText("Vision_OcrHint")
            };
            root.Controls.Add(this.lVisionOcrStatus, 0, 12);
            root.SetColumnSpan(this.lVisionOcrStatus, 4);

            this.lVisionAssistantStatus = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
                Text = UiText("Vision_AssistantHint")
            };
            root.Controls.Add(this.lVisionAssistantStatus, 0, 13);
            root.SetColumnSpan(this.lVisionAssistantStatus, 4);

            this.txtVisionAssistantLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                ForeColor = SystemColors.GrayText,
                BorderStyle = BorderStyle.FixedSingle,
                TabStop = false
            };
            root.Controls.Add(this.txtVisionAssistantLog, 0, 14);
            root.SetColumnSpan(this.txtVisionAssistantLog, 4);

            this.visionTextRecognizer = new VisionTesseractRecognizer("tesseract.exe");
            this.RefreshVisionAssistantSteps();

            Panel visionScrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(0)
            };
            visionScrollHost.Controls.Add(root);
            visionTab.Controls.Add(visionScrollHost);
            this.tcRobotInstruction.TabPages.Add(visionTab);
            this.FormClosed += this.Socket_RobotForm_FormClosed;
        }

        private static NumericUpDown CreateVisionNumber(int minimum)
        {
            return new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = minimum,
                Maximum = 100000,
                Increment = 1,
                ThousandsSeparator = false
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
                Margin = new Padding(0, 2, 8, 0)
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
            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = labelText,
                TextAlign = ContentAlignment.MiddleLeft
            }, labelColumn, row);
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
            this.txtVisionOcrKeyword.Text = profile.OcrCondition == null
                ? string.Empty
                : profile.OcrCondition.ExpectedText ?? string.Empty;
            this.RefreshVisionAssistantSteps();
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

            this.cbbVisionWindows.BeginUpdate();
            try
            {
                this.cbbVisionWindows.Items.Clear();
                IList<VisionWindowInfo> windows = VisionWindowService.EnumerateVisibleWindows(
                    Process.GetCurrentProcess().Id);
                int selectedIndex = -1;
                for (int i = 0; i < windows.Count; i++)
                {
                    VisionWindowInfo window = windows[i];
                    this.cbbVisionWindows.Items.Add(window);
                    if ((selectedHandle != 0 && window.Handle.ToInt64() == selectedHandle) ||
                        (selectedIndex < 0 &&
                         string.Equals(window.WindowTitle, selectedTitle, StringComparison.Ordinal) &&
                         string.Equals(window.ProcessName, selectedProcess, StringComparison.Ordinal)))
                    {
                        selectedIndex = i;
                    }
                }

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
        }

        private void cbbVisionWindows_SelectedIndexChanged(object sender, EventArgs e)
        {
            VisionWindowInfo window = this.cbbVisionWindows.SelectedItem as VisionWindowInfo;
            if (window == null)
            {
                return;
            }

            this.SetVisionStatus(string.Format(
                UiText("Vision_WindowSelected"),
                window.DisplayName,
                window.ClientSize.Width,
                window.ClientSize.Height));
        }

        private void bRefreshVisionWindows_Click(object sender, EventArgs e)
        {
            this.RefreshVisionWindows();
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
            }
            catch (Exception ex)
            {
                this.SetVisionStatus(string.Format(UiText("Vision_CaptureFailed"), ex.Message));
            }
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
                    this.visionTextRecognizer = new VisionTesseractRecognizer(options.ExecutablePath);
                }

                Bitmap input = new Bitmap(this.visionPreview);
                this.visionOcrCancellation = new CancellationTokenSource();
                CancellationToken cancellationToken = this.visionOcrCancellation.Token;
                this.SetVisionOcrButtons(true);
                this.SetVisionOcrStatus(UiText("Vision_OcrRunning"));
                this.visionOcrTask = Task.Run(
                    () =>
                    {
                        using (input)
                        {
                            return this.visionTextRecognizer.Recognize(input, options, cancellationToken);
                        }
                    },
                    cancellationToken);
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
                    if (condition.Matches(result, out reason))
                    {
                        status += " " + UiText("Vision_OcrConditionMatched");
                    }
                    else
                    {
                        status += " " + string.Format(
                            UiText("Vision_OcrConditionNotMatched"),
                            reason);
                    }
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
            }
        }

        private void bAddVisionOcrStep_Click(object sender, EventArgs e)
        {
            if (this.sriSelect == null)
            {
                return;
            }

            VisionConditionType conditionType = this.ReadVisionConditionType();
            if (conditionType != VisionConditionType.TextAppears &&
                conditionType != VisionConditionType.TextDisappears &&
                conditionType != VisionConditionType.NumberInRange)
            {
                this.SetVisionOcrStatus(UiText("Vision_SelectOcrCondition"));
                return;
            }

            string keyword = this.txtVisionOcrKeyword.Text.Trim();
            if (conditionType != VisionConditionType.NumberInRange && string.IsNullOrEmpty(keyword))
            {
                this.SetVisionOcrStatus(UiText("Vision_NoKeyword"));
                return;
            }

            VisionRegion region = this.ReadVisionRegion();
            if (!region.IsValid)
            {
                this.SetVisionOcrStatus(string.Format(
                    UiText("Vision_ProfileInvalid"),
                    "The region must be positive."));
                return;
            }

            Socket_VisionProfile profile = this.sriSelect.VisionProfile;
            profile.OcrOptions = this.ReadVisionOcrOptions();
            VisionTextCondition textCondition = profile.OcrCondition == null
                ? new VisionTextCondition()
                : profile.OcrCondition.Clone();
            textCondition.ExpectedText = conditionType == VisionConditionType.NumberInRange
                ? string.Empty
                : keyword;
            textCondition.MatchMode = conditionType == VisionConditionType.NumberInRange
                ? VisionTextMatchMode.NumberRange
                : VisionTextMatchMode.Contains;
            textCondition.MinimumNumber = (double)this.nudVisionNumberMinimum.Value;
            textCondition.MaximumNumber = (double)this.nudVisionNumberMaximum.Value;
            profile.OcrCondition = textCondition;
            string stepName = conditionType == VisionConditionType.NumberInRange
                ? string.Format(
                    "{0}: {1} - {2}",
                    UiText("Vision_NumberRange"),
                    textCondition.MinimumNumber,
                    textCondition.MaximumNumber)
                : keyword;
            profile.AssistantSteps.Add(new VisionAssistantStep
            {
                Name = stepName,
                Condition = new VisionConditionDefinition
                {
                    Name = stepName,
                    Type = conditionType,
                    Region = region.Clone(),
                    TextCondition = textCondition.Clone(),
                    RequiredConfirmations = (int)this.nudVisionConfirmations.Value,
                    PollIntervalMilliseconds = (int)this.nudVisionPollInterval.Value,
                    TimeoutMilliseconds = (int)this.nudVisionTimeout.Value,
                    MaxRetries = (int)this.nudVisionRetries.Value,
                    FailurePolicy = this.ReadVisionFailurePolicy()
                }
            });
            this.RefreshVisionAssistantSteps();
            this.SetVisionOcrStatus(UiText("Vision_StepAdded"));
        }

        private void bAddVisionTemplateStep_Click(object sender, EventArgs e)
        {
            if (this.sriSelect == null)
            {
                return;
            }
            if (this.visionTemplate == null)
            {
                this.SetVisionMatchStatus(UiText("Vision_NoTemplate"));
                return;
            }

            VisionConditionType conditionType = this.ReadVisionConditionType();
            if (conditionType != VisionConditionType.TemplateAppears &&
                conditionType != VisionConditionType.TemplateDisappears)
            {
                this.SetVisionMatchStatus(UiText("Vision_SelectTemplateCondition"));
                return;
            }

            VisionRegion region = this.ReadVisionRegion();
            if (!region.IsValid)
            {
                this.SetVisionMatchStatus(string.Format(
                    UiText("Vision_ProfileInvalid"),
                    "The region must be positive."));
                return;
            }

            string stepName = conditionType == VisionConditionType.TemplateDisappears
                ? UiText("Vision_TemplateDisappearStepName")
                : UiText("Vision_TemplateStepName");
            this.sriSelect.VisionProfile.AssistantSteps.Add(new VisionAssistantStep
            {
                Name = stepName,
                Condition = new VisionConditionDefinition
                {
                    Name = stepName,
                    Type = conditionType,
                    Region = region.Clone(),
                    Template = new Bitmap(this.visionTemplate),
                    MinimumSimilarity = (double)this.nudVisionThreshold.Value / 100D,
                    NormalizeTemplateBrightness = this.chkVisionTemplateNormalize.Checked,
                    AllowTemplateScaleVariation = this.chkVisionTemplateScale.Checked,
                    TemplateMinimumScale = this.ReadVisionTemplateMatchOptions().MinimumScale,
                    TemplateMaximumScale = this.ReadVisionTemplateMatchOptions().MaximumScale,
                    TemplateScaleStep = this.ReadVisionTemplateMatchOptions().ScaleStep,
                    RequiredConfirmations = (int)this.nudVisionConfirmations.Value,
                    PollIntervalMilliseconds = (int)this.nudVisionPollInterval.Value,
                    TimeoutMilliseconds = (int)this.nudVisionTimeout.Value,
                    MaxRetries = (int)this.nudVisionRetries.Value,
                    FailurePolicy = this.ReadVisionFailurePolicy()
                }
            });
            this.RefreshVisionAssistantSteps();
            this.SetVisionMatchStatus(UiText("Vision_StepAdded"));
        }

        private void bRemoveVisionStep_Click(object sender, EventArgs e)
        {
            if (this.sriSelect == null || this.cbbVisionSteps.SelectedIndex < 0)
            {
                return;
            }

            VisionAssistantStep step = this.cbbVisionSteps.SelectedItem as VisionAssistantStep;
            if (step != null)
            {
                this.sriSelect.VisionProfile.AssistantSteps.Remove(step);
                if (step.Condition != null && step.Condition.Template != null)
                {
                    step.Condition.Template.Dispose();
                    step.Condition.Template = null;
                }
            }
            this.RefreshVisionAssistantSteps();
            this.SetVisionOcrStatus(UiText("Vision_StepRemoved"));
        }

        private void RefreshVisionAssistantSteps()
        {
            if (this.cbbVisionSteps == null)
            {
                return;
            }

            this.cbbVisionSteps.BeginUpdate();
            try
            {
                this.cbbVisionSteps.Items.Clear();
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
                if (this.cbbVisionSteps.Items.Count > 0)
                {
                    this.cbbVisionSteps.SelectedIndex = this.cbbVisionSteps.Items.Count - 1;
                }
            }
            finally
            {
                this.cbbVisionSteps.EndUpdate();
            }
        }

        private VisionConditionType ReadVisionConditionType()
        {
            VisionConditionChoice choice = this.cbbVisionConditionType == null
                ? null
                : this.cbbVisionConditionType.SelectedItem as VisionConditionChoice;
            return choice == null ? VisionConditionType.TextAppears : choice.Type;
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
            if (step == null || step.Condition == null)
            {
                return;
            }

            this.updatingVisionStepEditor = true;
            try
            {
                VisionConditionDefinition condition = step.Condition;
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
                this.SetVisionNumber(this.nudVisionConfirmations, condition.RequiredConfirmations);
                this.SetVisionNumber(this.nudVisionPollInterval, condition.PollIntervalMilliseconds);
                this.SetVisionNumber(this.nudVisionTimeout, condition.TimeoutMilliseconds);
                this.SetVisionNumber(this.nudVisionRetries, condition.MaxRetries);
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

        private void bRunVisionAssistant_Click(object sender, EventArgs e)
        {
            if (this.visionAssistantTask != null && !this.visionAssistantTask.IsCompleted)
            {
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

            Socket_VisionProfile runProfile = this.sriSelect.VisionProfile.Clone();
            this.visionAssistantCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = this.visionAssistantCancellation.Token;
            this.ClearVisionAssistantLog();
            this.SetVisionAssistantButtons(true);
            this.SetVisionAssistantStatus(UiText("Vision_AssistantRunning"));
            this.AppendVisionAssistantLog(UiText("Vision_AssistantRunning"));

            try
            {
                this.visionAssistantTask = Task.Run(
                    () => VisionAssistantRunner.Run(
                        runProfile,
                        this.visionTextRecognizer,
                        cancellationToken,
                        this.VisionAssistantLogEmitted));
                this.visionAssistantTask.ContinueWith(
                    task => this.CompleteVisionAssistant(task, runProfile),
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
                this.DisposeVisionAssistantCancellation();
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
            Socket_VisionProfile runProfile)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                this.DisposeVisionProfileTemplates(runProfile);
                this.DisposeVisionAssistantCancellation();
                return;
            }

            try
            {
                this.BeginInvoke(new Action(() =>
                {
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
                        this.DisposeVisionAssistantCancellation();
                        this.visionAssistantTask = null;
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                this.DisposeVisionProfileTemplates(runProfile);
                this.DisposeVisionAssistantCancellation();
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

        private void SetVisionAssistantStatus(string status)
        {
            if (this.lVisionAssistantStatus == null || this.IsDisposed)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke(new Action(() => this.SetVisionAssistantStatus(status)));
                }
                catch (InvalidOperationException)
                {
                }
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
                try
                {
                    this.BeginInvoke(new Action(() => this.AppendVisionAssistantLog(message)));
                }
                catch (InvalidOperationException)
                {
                }
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

        private void DisposeVisionAssistantCancellation()
        {
            if (this.visionAssistantCancellation != null)
            {
                this.visionAssistantCancellation.Dispose();
                this.visionAssistantCancellation = null;
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
                if (step != null && step.Condition != null && step.Condition.Template != null)
                {
                    step.Condition.Template.Dispose();
                    step.Condition.Template = null;
                }
                if (step != null && step.Condition != null)
                {
                    step.Condition.DisposeTemplateVariants();
                }
            }
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

        private void bSaveVisionProfile_Click(object sender, EventArgs e)
        {
            if (!this.TryApplyVisionProfile())
            {
                return;
            }

            Socket_Cache.RobotList.SaveRobotList_ToDB();
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
                VisionRegion region = this.ReadVisionRegion();
                if (!region.FitsWithin(window.ClientSize))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            "The region must stay inside the client area ({0}x{1}).",
                            window.ClientSize.Width,
                            window.ClientSize.Height));
                }

                Socket_VisionProfile profile = this.sriSelect.VisionProfile ?? new Socket_VisionProfile();
                profile.WindowHandle = window.Handle.ToInt64();
                profile.ProcessId = window.ProcessId;
                profile.ProcessName = window.ProcessName;
                profile.ProcessPath = window.ProcessPath;
                profile.ProcessStartTimeUtcTicks = window.ProcessStartTimeUtcTicks;
                profile.WindowTitle = window.WindowTitle;
                profile.Region = region;
                profile.CaptureSettings = this.ReadVisionCaptureSettings();
                profile.OcrOptions = this.ReadVisionOcrOptions();
                profile.OcrCondition = profile.OcrCondition ?? new VisionTextCondition();
                profile.OcrCondition.ExpectedText = this.txtVisionOcrKeyword.Text.Trim();
                profile.OcrCondition.MatchMode = VisionTextMatchMode.Contains;
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
            return settings;
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
                this.lVisionOcrStatus.Text = status ?? string.Empty;
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
            this.DisposeVisionPreview();
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

                if (this.cbbVisionWindows != null && this.cbbVisionWindows.SelectedItem != null &&
                    !this.TryApplyVisionProfile())
                {
                    return;
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
