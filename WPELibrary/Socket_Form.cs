using Be.Windows.Forms;
using EasyHook;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using WPELibrary.Lib;
using WPELibrary.Lib.NativeMethods;
using WPELibrary.TextComparison;

namespace WPELibrary
{
    public partial class Socket_Form : Form
    {
        private static readonly Color UiNavigationTextColor = Color.FromArgb(31, 41, 55);
        private static readonly Color UiNavigationBorderColor = Color.FromArgb(210, 214, 220);
        private static readonly Color UiNavigationHoverColor = Color.FromArgb(245, 248, 252);
        private static readonly Color UiNavigationPressedColor = Color.FromArgb(230, 238, 248);
        private static readonly Color PacketSendAccentColor = Color.FromArgb(255, 167, 38);
        private static readonly Color PacketReceiveAccentColor = Color.FromArgb(41, 182, 246);
        private static readonly Color PacketSelectionTextColor = Color.FromArgb(15, 23, 42);
        private readonly Socket_Cache.System.SystemMode RunMode = Socket_Cache.System.SystemMode.Process;
        private bool bWakeUp = true;
        private readonly ToolTip tt = new ToolTip();
        private readonly WinSockHook ws = new WinSockHook();
        private string hookStatusKey = "UI_HookStatusReady";
        private bool hookUiTransitioning;
        private bool socketListTickRunning;
        private Button bSettings;
        private TabControl tcAdvancedTools;
        private TableLayoutPanel tlpCurrentProcess;
        private TreeView tvSendFolders;
        private Label lSendFoldersTitle;
        private Button bSendFolderAdd;
        private ToolStripButton tsSendListSelectAll;
        private ToolStripButton tsSendListParallel;
        private ToolStripDropDownButton tsSendListMore;
        private ToolStripLabel tsSendListContext;
        private ToolStripMenuItem cmsSendListMoveToFolder;
        private ToolStripMenuItem cmsSendListEdit;
        private ContextMenuStrip cmsSendFolder;
        private ToolStripMenuItem cmsSendFolderMoveUp;
        private ToolStripMenuItem cmsSendFolderMoveDown;
        private ToolStripMenuItem cmsSocketListPacketDetails;
        private string selectedSendFolder = "__ALL__";
        private List<Socket_SendInfo> sendBatchQueue = new List<Socket_SendInfo>();
        private readonly Dictionary<Guid, Socket_Send> manualSendOperations =
            new Dictionary<Guid, Socket_Send>();
        private readonly object sendOperationSync = new object();
        private readonly Dictionary<Guid, Socket_Send> activeBatchSendOperations =
            new Dictionary<Guid, Socket_Send>();
        private bool sendListParallelMode;
        private Socket_PacketInfo packetDataEditingPacket;
        private GroupBox gbReadableContent;
        private Label lReadableContentMeta;
        private RichTextBox rtbReadableContent;
        private bool robotSettingsPageActive;
        private ToolStripDropDownButton tsRobotListMore;
        private ToolStripDropDownButton tsFilterListMore;
        private TableLayoutPanel tlpAutomationHome;
        private TableLayoutPanel tlpAutomationNavigation;
        private Button bAutomationSend;
        private Button bAutomationSweep;
        private Button bAutomationAssistant;
        private Button bAutomationFilter;
        private TreeView tvRobotFolders;
        private Label lRobotFoldersTitle;
        private Button bRobotFolderAdd;
        private TableLayoutPanel tlpAssistantButtons;
        private Label lAssistantEmptyState;
        private ContextMenuStrip cmsAssistantButton;
        private ContextMenuStrip cmsRobotFolder;
        private string selectedRobotFolder = "常用";
        private Socket_Robot activeAssistantRobot;
        private Guid activeAssistantRobotId = Guid.Empty;
        private readonly Dictionary<Guid, Button> assistantButtons = new Dictionary<Guid, Button>();
        private int requestedClientWidth = -1;

        protected override void SetClientSizeCore(int x, int y)
        {
            this.requestedClientWidth = x;
            base.SetClientSizeCore(x, y);
            this.UpdateAutomationNavigationButtonLayout();
        }

        #region//加载窗体

        public Socket_Form()
        {
            try
            {
                Socket_Cache.System.LoadSystemConfig_FromDB();
                MultiLanguage.SetDefaultLanguage(Socket_Cache.System.DefaultLanguage);

                InitializeComponent();
                this.InitSettingsButton();
                this.cSocket.Visible = false;
                this.cmsSocketList_SystemSocket.Visible = false;
                this.InitSocketListPacketMenu();

                Socket_Cache.System.InvokeAction = action =>
                {
                    if (this.InvokeRequired)
                    {
                        this.Invoke(action);
                    }
                    else
                    {
                        action();
                    }
                };

                this.InitSocketDGV();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//设置子页面

        private void InitSettingsButton()
        {
            this.MinimumSize = new System.Drawing.Size(900, 620);
            this.AccessibleRole = AccessibleRole.Window;
            this.dgvSocketList.AccessibleName = UiText("Main_PacketList");
            this.dgvSocketList.AccessibleRole = AccessibleRole.Table;
            this.hbPacketData.AccessibleName = UiText("Main_PacketData");
            this.tcAutomation.AccessibleName = UiText("Main_Automation");
            this.tcAutomation.AccessibleRole = AccessibleRole.PageTabList;

            this.bSettings = new Button
            {
                Name = "bSettings",
                Text = UiText("Main_Settings"),
                UseVisualStyleBackColor = true,
                Size = new System.Drawing.Size(84, 38),
                Anchor = AnchorStyles.None,
                Margin = new Padding(4),
                AccessibleName = UiText("Main_Settings")
            };
            this.bSettings.Click += this.bSettings_Click;
            this.tt.SetToolTip(this.bSettings, this.bSettings.Text);

            this.tlpCurrentProcess = new TableLayoutPanel
            {
                Name = "tlpCurrentProcess",
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(4, 2, 4, 2),
                Padding = new Padding(2)
            };
            this.tlpCurrentProcess.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            this.tlpCurrentProcess.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            this.tlpCurrentProcess.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));
            this.tlpCurrentProcess.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            this.tlpCurrentProcess.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tlpCurrentProcess.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tlpCurrentProcess.Controls.Add(this.bStartHook, 0, 0);
            this.tlpCurrentProcess.Controls.Add(this.bStopHook, 1, 0);
            this.tlpCurrentProcess.Controls.Add(this.bCleanUp, 2, 0);
            this.tlpCurrentProcess.Controls.Add(this.bSettings, 3, 0);

            this.bCleanUp.Text = UiText("Main_Clear");
            this.bCleanUp.Size = new System.Drawing.Size(120, 38);
            this.bCleanUp.Anchor = AnchorStyles.None;
            this.bCleanUp.Margin = new Padding(4);
            this.bCleanUp.TextImageRelation = TextImageRelation.ImageBeforeText;
            this.bCleanUp.AccessibleName = this.bCleanUp.Text;
            this.tt.SetToolTip(this.bCleanUp, this.bCleanUp.Text);

            this.bStartHook.Text = UiText("Main_Start");
            this.bStartHook.Size = new System.Drawing.Size(96, 38);
            this.bStartHook.Anchor = AnchorStyles.None;
            this.bStartHook.Margin = new Padding(4);
            this.bStartHook.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.bStartHook.AccessibleName = this.bStartHook.Text;
            this.tt.SetToolTip(this.bStartHook, this.bStartHook.Text);

            this.bStopHook.Text = UiText("Main_Stop");
            this.bStopHook.Size = new System.Drawing.Size(96, 38);
            this.bStopHook.Anchor = AnchorStyles.None;
            this.bStopHook.Margin = new Padding(4);
            this.bStopHook.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.bStopHook.AccessibleName = this.bStopHook.Text;
            this.tt.SetToolTip(this.bStopHook, this.bStopHook.Text);

            this.tcAdvancedTools = new TabControl
            {
                Name = "tcAdvancedTools",
                Dock = DockStyle.Fill,
                Multiline = true
            };
            this.tcAdvancedTools.Controls.Add(this.tpComparison);
            this.tcAdvancedTools.Controls.Add(this.tpXOR);
            this.tcAdvancedTools.Controls.Add(this.tpEncoding);
            this.tcAdvancedTools.Controls.Add(this.tpExtraction);
            this.tcAdvancedTools.Controls.Add(this.tpSystemLog);
            this.tcPacketInfo.Controls.Remove(this.tpPacketStatistics);
            this.tpFilterList.Text = UiText("Main_Filter");
            this.tpRobotList.Text = UiText("UI_Assistant");

            this.tlpParameter.Controls.Remove(this.tcSocketInfo);
            this.tlpParameter.Controls.Remove(this.gbHookButton_Search);
            this.tlpParameter.Controls.Remove(this.tlpHookButton);
            this.tlpParameter.Margin = Padding.Empty;
            this.tlpParameter.Controls.Add(this.tlpCurrentProcess, 0, 0);
            this.tlpParameter.SetColumnSpan(this.tlpCurrentProcess, 3);

            if (this.tlpSocketForm.RowStyles.Count >= 5)
            {
                this.tlpSocketForm.RowStyles[0].SizeType = SizeType.Absolute;
                this.tlpSocketForm.RowStyles[0].Height = 50F;
                this.tlpSocketForm.RowStyles[1].SizeType = SizeType.AutoSize;
                this.tlpSocketForm.RowStyles[2].SizeType = SizeType.Percent;
                this.tlpSocketForm.RowStyles[2].Height = 50F;
                this.tlpSocketForm.RowStyles[3].SizeType = SizeType.Percent;
                this.tlpSocketForm.RowStyles[3].Height = 50F;
                this.tlpSocketForm.RowStyles[4].SizeType = SizeType.AutoSize;
            }

            if (this.tlpInformation.ColumnStyles.Count >= 2)
            {
                this.tlpInformation.ColumnStyles[0].SizeType = SizeType.Percent;
                this.tlpInformation.ColumnStyles[0].Width = 60F;
                this.tlpInformation.ColumnStyles[1].SizeType = SizeType.Percent;
                this.tlpInformation.ColumnStyles[1].Width = 40F;
            }

            this.hbPacketData.ReadOnly = false;
            this.Layout += this.Socket_Form_Layout;
            this.hbPacketData.SelectionStartChanged += this.hbPacketData_ByteSweepSelectionChanged;
            this.hbPacketData.SelectionLengthChanged += this.hbPacketData_ByteSweepSelectionChanged;
            this.InitSendFolderUI();
            this.InitByteSweepPresetUI();
            this.InitByteSweepLogUI();
            this.InitAssistantButtonUI();
            this.InitAutomationHomeUI();
            this.ConfigureRobotToolbarTextButtons();
            this.ConfigureFilterToolbarTextButtons();
        }

        private void InitReadableContentUI()
        {
            this.gbReadableContent = new GroupBox
            {
                Name = "gbReadableContent",
                Text = UiText("UI_ReadableContentTitle"),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(6, 5, 6, 6),
                TabStop = false
            };

            TableLayoutPanel readableLayout = new TableLayoutPanel
            {
                Name = "tlpReadableContent",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            readableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            readableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            readableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            this.lReadableContentMeta = new Label
            {
                Name = "lReadableContentMeta",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = UiText("UI_ReadableContentNone"),
                AccessibleName = UiText("UI_ReadableContentMetaLabel")
            };

            this.rtbReadableContent = new RichTextBox
            {
                Name = "rtbReadableContent",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                Multiline = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                ForeColor = SystemColors.ControlText,
                Font = new Font("Consolas", 9F),
                AccessibleName = UiText("UI_ReadableContentPreview")
            };

            readableLayout.Controls.Add(this.lReadableContentMeta, 0, 0);
            readableLayout.Controls.Add(this.rtbReadableContent, 0, 1);
            this.gbReadableContent.Controls.Add(readableLayout);

            this.tlpPacketData.Controls.Remove(this.tlpHexBox);
            this.tlpPacketData.RowCount = 2;
            this.tlpPacketData.RowStyles.Clear();
            this.tlpPacketData.RowStyles.Add(new RowStyle(SizeType.Absolute, 122F));
            this.tlpPacketData.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tlpPacketData.Controls.Add(this.gbReadableContent, 0, 0);
            this.tlpPacketData.Controls.Add(this.tlpHexBox, 0, 1);

            this.ClearReadableContent();
        }

        private void ClearReadableContent()
        {
            if (this.lReadableContentMeta == null || this.rtbReadableContent == null)
            {
                return;
            }

            this.lReadableContentMeta.Text = UiText("UI_ReadableContentNone");
            this.rtbReadableContent.Clear();
        }

        private void UpdateReadableContent(byte[] buffer)
        {
            if (this.lReadableContentMeta == null || this.rtbReadableContent == null)
            {
                return;
            }

            Socket_PacketReadableResult result = Socket_PacketReadableAnalyzer.Analyze(buffer);
            string kind = UiText(result.KindKey);
            string encoding = string.IsNullOrEmpty(result.EncodingName)
                ? UiText("UI_ReadableEncodingNone")
                : result.EncodingName;
            int length = buffer == null ? 0 : buffer.Length;

            this.lReadableContentMeta.Text = string.Format(
                UiText("UI_ReadableContentMeta"),
                kind,
                encoding,
                length);

            StringBuilder content = new StringBuilder();
            if (result.IsEmpty)
            {
                content.Append(UiText("UI_ReadableContentEmpty"));
            }
            else
            {
                if (result.IsBinary)
                {
                    content.AppendLine(UiText("UI_ReadableContentBinaryNotice"));
                }

                if (!string.IsNullOrEmpty(result.Details))
                {
                    content.AppendLine(string.Format(
                        UiText("UI_ReadableContentDetails"),
                        result.Details));
                }

                if (result.IsBinary && !string.IsNullOrEmpty(result.Preview))
                {
                    content.AppendLine(UiText("UI_ReadableContentStrings"));
                }

                if (!string.IsNullOrEmpty(result.Preview))
                {
                    content.Append(result.Preview);
                }
                else if (result.IsBinary)
                {
                    content.Append(UiText("UI_ReadableContentNoStrings"));
                }

                if (result.IsTruncated)
                {
                    content.AppendLine();
                    content.Append(UiText("UI_ReadableContentTruncated"));
                }
            }

            this.rtbReadableContent.Text = content.ToString();
            this.rtbReadableContent.SelectionStart = 0;
            this.rtbReadableContent.SelectionLength = 0;
        }

        private void ConfigureFilterToolbarTextButtons()
        {
            this.ConfigureSendToolbarTextButton(this.tsFilterList_Load, UiText("Filter_Load"));
            this.ConfigureSendToolbarTextButton(this.tsFilterList_Save, UiText("Filter_Save"));
            this.ConfigureSendToolbarTextButton(this.tsFilterList_SelectAll, UiText("UI_SelectAll"));
            this.ConfigureSendToolbarTextButton(this.tsFilterList_SelectNo, UiText("UI_ClearSelection"));
            this.ConfigureSendToolbarTextButton(this.tsFilterList_Add, UiText("Filter_Add"));
            this.ConfigureSendToolbarTextButton(this.tsFilterList_CleanUp, UiText("Filter_Clear"));
            this.tsFilterList_SelectAll.ToolTipText = UiText("UI_SelectAllTip");
            this.tsFilterList_SelectNo.ToolTipText = UiText("UI_ClearSelectionTip");
            this.tsFilterList_Add.ToolTipText = UiText("Filter_Add");

            this.tsFilterList_Load.Visible = false;
            this.tsFilterList_Save.Visible = false;
            this.tsFilterList_CleanUp.Visible = false;
            this.toolStripSeparator1.Visible = false;
            this.toolStripSeparator2.Visible = false;

            this.tsFilterListMore = new ToolStripDropDownButton
            {
                Name = "tsFilterListMore",
                Text = UiText("UI_More"),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = true,
                Overflow = ToolStripItemOverflow.Never,
                Margin = new Padding(3)
            };
            this.tsFilterListMore.DropDownItems.Add(CreateSendMoreItem(
                "FilterMoreLoad", UiText("Filter_Load"), this.tsFilterList_Load_Click));
            this.tsFilterListMore.DropDownItems.Add(CreateSendMoreItem(
                "FilterMoreSave", UiText("Filter_Save"), this.tsFilterList_Save_Click));
            this.tsFilterListMore.DropDownItems.Add(new ToolStripSeparator());
            this.tsFilterListMore.DropDownItems.Add(CreateSendMoreItem(
                "FilterMoreClear", UiText("Filter_Clear"), this.tsFilterList_CleanUp_Click));

            this.tsFilterList.Items.Remove(this.tsFilterList_Load);
            this.tsFilterList.Items.Remove(this.tsFilterList_Save);
            this.tsFilterList.Items.Remove(this.toolStripSeparator1);
            this.tsFilterList.Items.Remove(this.toolStripSeparator2);
            this.tsFilterList.Items.Remove(this.tsFilterList_SelectAll);
            this.tsFilterList.Items.Remove(this.tsFilterList_SelectNo);
            this.tsFilterList.Items.Remove(this.tsFilterList_Add);
            this.tsFilterList.Items.Remove(this.tsFilterList_CleanUp);
            this.tsFilterList.Items.AddRange(new ToolStripItem[]
            {
                this.tsFilterList_Add,
                this.tsFilterList_SelectAll,
                this.tsFilterList_SelectNo,
                this.tsFilterListMore
            });
        }

        private void ConfigureRobotToolbarTextButtons()
        {
            this.ConfigureSendToolbarTextButton(this.tsRobotList_Load, UiText("Robot_Load"));
            this.ConfigureSendToolbarTextButton(this.tsRobotList_Save, UiText("Robot_Save"));
            this.ConfigureSendToolbarTextButton(this.tsRobotList_Add, UiText("Robot_Add"));
            this.ConfigureSendToolbarTextButton(this.tsRobotList_CleanUp, UiText("Robot_Clear"));
            this.tsRobotList_Start.ForeColor = System.Drawing.Color.ForestGreen;
            this.tsRobotList_Stop.ForeColor = System.Drawing.Color.Firebrick;
            this.tsRobotList_Start.Visible = false;
            this.tsRobotList_Stop.Visible = false;
            this.tsRobotList_Load.Visible = false;
            this.tsRobotList_Save.Visible = false;
            this.tsRobotList_CleanUp.Visible = false;
            this.toolStripSeparator4.Visible = false;
            this.toolStripSeparator12.Visible = false;

            this.tsRobotListMore = new ToolStripDropDownButton
            {
                Name = "tsRobotListMore",
                Text = UiText("UI_More"),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = true,
                Overflow = ToolStripItemOverflow.Never,
                Margin = new Padding(3)
            };
            this.tsRobotListMore.DropDownItems.Add(CreateRobotMoreItem(
                "RobotMoreLoad", UiText("Robot_Load"), this.tsRobotList_Load_Click));
            this.tsRobotListMore.DropDownItems.Add(CreateRobotMoreItem(
                "RobotMoreSave", UiText("Robot_Save"), this.tsRobotList_Save_Click));
            this.tsRobotListMore.DropDownItems.Add(CreateRobotMoreItem(
                "RobotMoreCopy", UiText("Robot_Copy"), this.tsRobotList_Copy_Click));
            this.tsRobotListMore.DropDownItems.Add(new ToolStripSeparator());
            this.tsRobotListMore.DropDownItems.Add(CreateRobotMoreItem(
                "RobotMoreClear", UiText("Robot_Clear"), this.tsRobotList_CleanUp_Click));
            this.tsRobotList.Items.Remove(this.tsRobotList_Load);
            this.tsRobotList.Items.Remove(this.tsRobotList_Save);
            this.tsRobotList.Items.Remove(this.toolStripSeparator4);
            this.tsRobotList.Items.Remove(this.tsRobotList_Start);
            this.tsRobotList.Items.Remove(this.tsRobotList_Stop);
            this.tsRobotList.Items.Remove(this.toolStripSeparator12);
            this.tsRobotList.Items.Remove(this.tsRobotList_Add);
            this.tsRobotList.Items.Remove(this.tsRobotList_CleanUp);
            this.tsRobotList.Items.AddRange(new ToolStripItem[]
            {
                this.tsRobotList_Add,
                this.tsRobotListMore
            });
            this.UpdateRobotToolbarState();
        }

        private void InitAutomationHomeUI()
        {
            if (this.tlpAutomationHome != null)
            {
                return;
            }

            this.tlpAutomationNavigation = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(12, 8, 12, 8),
                Margin = Padding.Empty
            };
            for (int i = 0; i < 2; i++)
            {
                this.tlpAutomationNavigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            }
            for (int i = 0; i < 2; i++)
            {
                this.tlpAutomationNavigation.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            }
            this.tlpAutomationNavigation.Resize += this.tlpAutomationNavigation_Resize;

            this.bAutomationSend = CreateAutomationNavigationButton(UiText("UI_Send"), this.tpSendList);
            this.bAutomationSweep = CreateAutomationNavigationButton(UiText("UI_ByteSweep"), this.tpByteSweepList);
            this.bAutomationAssistant = CreateAutomationNavigationButton(UiText("UI_Assistant"), this.tpRobotList);
            this.bAutomationFilter = CreateAutomationNavigationButton(UiText("Main_Filter"), this.tpFilterList);
            this.tlpAutomationNavigation.Controls.Add(this.bAutomationSend, 0, 0);
            this.tlpAutomationNavigation.Controls.Add(this.bAutomationSweep, 1, 0);
            this.tlpAutomationNavigation.Controls.Add(this.bAutomationAssistant, 0, 1);
            this.tlpAutomationNavigation.Controls.Add(this.bAutomationFilter, 1, 1);

            this.tlpAutomationHome = new TableLayoutPanel
            {
                Name = "tlpAutomationHome",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            this.tlpAutomationHome.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
            this.tlpAutomationHome.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tlpAutomationHome.Controls.Add(this.tlpAutomationNavigation, 0, 0);
            this.tlpAutomationHome.Controls.Add(this.tcAutomation, 0, 1);

            this.tcAutomation.Dock = DockStyle.Fill;
            this.tcAutomation.Appearance = TabAppearance.Buttons;
            this.tcAutomation.SizeMode = TabSizeMode.Fixed;
            this.tcAutomation.ItemSize = new Size(0, 1);
            this.tcAutomation.SelectedIndexChanged += this.tcAutomation_SelectedIndexChanged;
            this.tlpInformation.Controls.Remove(this.tcAutomation);
            this.tlpInformation.Controls.Add(this.tlpAutomationHome, 0, 0);
            this.tlpInformation.SetColumnSpan(this.tlpAutomationHome, 1);
            this.tcAutomation.SelectedTab = this.tpSendList;
            this.UpdateAutomationNavigationButtonLayout();
            this.UpdateAutomationNavigationState();
        }

        private Button CreateAutomationNavigationButton(string text, TabPage tab)
        {
            Button button = new Button
            {
                Text = text,
                Anchor = AnchorStyles.None,
                Dock = DockStyle.None,
                Size = new Size(240, 36),
                Margin = new Padding(4, 0, 4, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiNavigationTextColor,
                UseVisualStyleBackColor = false,
                TextAlign = ContentAlignment.MiddleCenter,
                AccessibleName = text,
                AccessibleRole = AccessibleRole.PushButton,
                Tag = tab
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = UiNavigationBorderColor;
            button.FlatAppearance.MouseOverBackColor = UiNavigationHoverColor;
            button.FlatAppearance.MouseDownBackColor = UiNavigationPressedColor;
            button.Click += this.automationNavigationButton_Click;
            return button;
        }

        private void tlpAutomationNavigation_Resize(object sender, EventArgs e)
        {
            this.UpdateAutomationNavigationButtonLayout();
        }

        private void Socket_Form_Layout(object sender, LayoutEventArgs e)
        {
            this.UpdateAutomationNavigationButtonLayout();
        }

        private void UpdateAutomationNavigationButtonLayout()
        {
            if (this.tlpAutomationNavigation == null)
            {
                return;
            }

            int availableWidth = Math.Max(1, (this.tlpAutomationNavigation.ClientSize.Width -
                this.tlpAutomationNavigation.Padding.Horizontal) / 2 - 8);
            int buttonWidth = Math.Min(240, availableWidth);

            foreach (Control control in this.tlpAutomationNavigation.Controls)
            {
                Button button = control as Button;
                if (button != null)
                {
                    button.Width = buttonWidth;
                }
            }
        }

        private void automationNavigationButton_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            TabPage tab = button == null ? null : button.Tag as TabPage;
            if (tab != null)
            {
                this.tcAutomation.SelectedTab = tab;
            }
        }

        private void tcAutomation_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.UpdateAutomationNavigationState();
        }

        private void UpdateAutomationNavigationState()
        {
            if (this.tcAutomation == null || this.bAutomationSend == null)
            {
                return;
            }

            Button[] buttons =
            {
                this.bAutomationSend,
                this.bAutomationSweep,
                this.bAutomationAssistant,
                this.bAutomationFilter
            };
            foreach (Button button in buttons)
            {
                bool active = ReferenceEquals(button.Tag, this.tcAutomation.SelectedTab);
                button.BackColor = active ? SystemColors.Highlight : Color.White;
                button.ForeColor = active ? SystemColors.HighlightText : UiNavigationTextColor;
                button.FlatAppearance.BorderSize = active ? 2 : 1;
                button.FlatAppearance.BorderColor = active
                    ? SystemColors.Highlight
                    : UiNavigationBorderColor;
                button.FlatAppearance.MouseOverBackColor = active
                    ? SystemColors.Highlight
                    : UiNavigationHoverColor;
                button.FlatAppearance.MouseDownBackColor = active
                    ? SystemColors.Highlight
                    : UiNavigationPressedColor;
            }
        }

        private void InitAssistantButtonUI()
        {
            this.tvRobotFolders = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                FullRowSelect = true,
                ShowLines = false,
                ShowPlusMinus = false,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 4, 0),
                AccessibleName = UiText("UI_AssistantGroups")
            };
            this.tvRobotFolders.AfterSelect += this.tvRobotFolders_AfterSelect;
            this.tvRobotFolders.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Button == MouseButtons.Right)
                {
                    this.tvRobotFolders.SelectedNode = e.Node;
                }
            };

            this.cmsRobotFolder = new ContextMenuStrip();
            this.cmsRobotFolder.Items.Add(UiText("UI_RenameGroup"), null, this.RenameRobotFolder_Click);
            this.cmsRobotFolder.Items.Add(UiText("UI_MoveGroupUp"), null, this.MoveRobotFolderUp_Click);
            this.cmsRobotFolder.Items.Add(UiText("UI_MoveGroupDown"), null, this.MoveRobotFolderDown_Click);
            this.cmsRobotFolder.Items.Add(new ToolStripSeparator());
            this.cmsRobotFolder.Items.Add(UiText("UI_DeleteGroup"), null, this.DeleteRobotFolder_Click);
            this.tvRobotFolders.ContextMenuStrip = this.cmsRobotFolder;

            this.bRobotFolderAdd = new Button
            {
                Text = UiText("UI_NewGroup"),
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 4, 6, 4),
                UseVisualStyleBackColor = true,
                AccessibleName = UiText("UI_NewGroup")
            };
            this.bRobotFolderAdd.Click += this.AddRobotFolder_Click;
            this.lRobotFoldersTitle = new Label
            {
                Text = UiText("UI_AssistantGroups"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Margin = Padding.Empty
            };
            this.tlpAssistantButtons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                AutoScroll = true,
                Padding = new Padding(8, 6, 8, 6),
                Margin = Padding.Empty,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            for (int i = 0; i < 5; i++)
            {
                this.tlpAssistantButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            }
            this.lAssistantEmptyState = new Label
            {
                Text = UiText("UI_AssistantEmpty"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(4),
                AccessibleName = UiText("UI_AssistantEmpty")
            };

            this.tlpRobotList.SuspendLayout();
            this.tlpRobotList.Controls.Clear();
            this.tlpRobotList.ColumnStyles.Clear();
            this.tlpRobotList.RowStyles.Clear();
            this.tlpRobotList.ColumnCount = 2;
            this.tlpRobotList.RowCount = 3;
            this.tlpRobotList.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122F));
            this.tlpRobotList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tlpRobotList.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            this.tlpRobotList.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tlpRobotList.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            this.tlpRobotList.Controls.Add(this.lRobotFoldersTitle, 0, 0);
            this.tlpRobotList.Controls.Add(this.tsRobotList, 1, 0);
            this.tlpRobotList.Controls.Add(this.tvRobotFolders, 0, 1);
            this.tlpRobotList.Controls.Add(this.tlpAssistantButtons, 1, 1);
            this.tlpRobotList.Controls.Add(this.bRobotFolderAdd, 0, 2);
            this.tlpRobotList.SetColumnSpan(this.tsRobotList, 1);
            this.tsRobotList.Dock = DockStyle.Fill;
            this.dgvRobotList.Visible = false;
            this.tlpRobotList.ResumeLayout(true);

            this.cmsAssistantButton = new ContextMenuStrip();
            this.cmsAssistantButton.Items.Add(UiText("UI_Edit"), null, this.EditAssistantButton_Click);
            ToolStripMenuItem moveAssistant = new ToolStripMenuItem(UiText("UI_MoveToGroup"));
            this.cmsAssistantButton.Items.Add(moveAssistant);
            this.cmsAssistantButton.Opening += this.cmsAssistantButton_Opening;
            this.RefreshAssistantFolders();
        }

        private void cmsAssistantButton_Opening(object sender, CancelEventArgs e)
        {
            if (this.activeAssistantRobot != null)
            {
                e.Cancel = true;
                return;
            }

            ToolStripMenuItem moveAssistant = this.cmsAssistantButton.Items
                .OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => item.Text == UiText("UI_MoveToGroup"));
            if (moveAssistant == null)
            {
                return;
            }
            moveAssistant.DropDownItems.Clear();
            Button source = this.cmsAssistantButton.SourceControl as Button;
            Socket_RobotInfo robot = source == null ? null : source.Tag as Socket_RobotInfo;
            foreach (TreeNode node in this.tvRobotFolders.Nodes)
            {
                if (robot != null && string.Equals(robot.RFolder, node.Text, StringComparison.Ordinal))
                {
                    continue;
                }
                ToolStripMenuItem item = new ToolStripMenuItem(node.Text) { Tag = node.Text };
                item.Click += this.MoveAssistantToFolder_Click;
                moveAssistant.DropDownItems.Add(item);
            }
            moveAssistant.Enabled = moveAssistant.DropDownItems.Count > 0;
        }

        private void MoveAssistantToFolder_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            Button source = this.cmsAssistantButton.SourceControl as Button;
            Socket_RobotInfo robot = source == null ? null : source.Tag as Socket_RobotInfo;
            string folder = item == null ? string.Empty : item.Tag as string;
            if (robot == null || string.IsNullOrWhiteSpace(folder) || this.activeAssistantRobot != null)
            {
                return;
            }
            robot.RFolder = folder;
            if (!Socket_Cache.RobotList.lstFolders.Contains(folder))
            {
                Socket_Cache.RobotList.lstFolders.Add(folder);
            }
            this.SaveRobotFolderData();
            this.RefreshAssistantFolders();
        }

        private void RefreshAssistantFolders()
        {
            if (this.tvRobotFolders == null)
            {
                return;
            }

            List<string> folders = Socket_Cache.RobotList.lstFolders.Distinct().ToList();
            if (!folders.Contains("常用"))
            {
                folders.Insert(0, "常用");
            }
            foreach (Socket_RobotInfo robot in Socket_Cache.RobotList.lstRobot)
            {
                string folder = string.IsNullOrWhiteSpace(robot.RFolder) ? "常用" : robot.RFolder;
                if (!folders.Contains(folder))
                {
                    folders.Add(folder);
                }
            }

            this.tvRobotFolders.BeginUpdate();
            this.tvRobotFolders.Nodes.Clear();
            foreach (string folder in folders)
            {
                this.tvRobotFolders.Nodes.Add(new TreeNode(folder) { Name = folder });
            }
            this.tvRobotFolders.EndUpdate();
            TreeNode selected = this.tvRobotFolders.Nodes.Cast<TreeNode>()
                .FirstOrDefault(node => string.Equals(node.Text, this.selectedRobotFolder, StringComparison.Ordinal));
            if (selected == null)
            {
                selected = this.tvRobotFolders.Nodes[0];
            }
            if (selected != null)
            {
                this.tvRobotFolders.SelectedNode = selected;
                this.selectedRobotFolder = selected.Text;
            }
            this.RenderAssistantButtons();
        }

        private void tvRobotFolders_AfterSelect(object sender, TreeViewEventArgs e)
        {
            this.selectedRobotFolder = e.Node == null ? "常用" : e.Node.Text;
            this.RenderAssistantButtons();
        }

        private void RenderAssistantButtons()
        {
            if (this.tlpAssistantButtons == null)
            {
                return;
            }

            this.tlpAssistantButtons.SuspendLayout();
            this.tlpAssistantButtons.Controls.Clear();
            this.assistantButtons.Clear();
            List<Socket_RobotInfo> robots = Socket_Cache.RobotList.lstRobot
                .Where(robot => string.Equals(robot.RFolder, this.selectedRobotFolder, StringComparison.Ordinal))
                .ToList();
            this.tlpAssistantButtons.RowCount = Math.Max(1, (robots.Count + 4) / 5);
            this.tlpAssistantButtons.RowStyles.Clear();
            for (int row = 0; row < this.tlpAssistantButtons.RowCount; row++)
            {
                this.tlpAssistantButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            }
            for (int index = 0; index < robots.Count; index++)
            {
                Socket_RobotInfo robot = robots[index];
                Button button = new Button
                {
                    Text = robot.RName,
                    Anchor = AnchorStyles.Left | AnchorStyles.Top,
                    Dock = DockStyle.None,
                    Size = new Size(84, 34),
                    Margin = new Padding(6, 3, 6, 3),
                    AutoEllipsis = true,
                    Padding = new Padding(2, 0, 2, 0),
                    TextAlign = ContentAlignment.MiddleCenter,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(245, 248, 255),
                    ForeColor = Color.FromArgb(32, 43, 61),
                    UseVisualStyleBackColor = false,
                    Cursor = Cursors.Hand,
                    Tag = robot,
                    ContextMenuStrip = this.cmsAssistantButton,
                    AccessibleName = robot.RName
                };
                button.FlatAppearance.BorderColor = Color.FromArgb(147, 177, 222);
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(226, 237, 255);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(207, 225, 251);
                button.AccessibleDescription = robot.RName;
                this.tt.SetToolTip(button, robot.RName);
                button.Click += this.AssistantButton_Click;
                button.DoubleClick += this.AssistantButton_DoubleClick;
                this.assistantButtons[robot.RID] = button;
                this.tlpAssistantButtons.Controls.Add(button, index % 5, index / 5);
            }
            if (robots.Count == 0)
            {
                this.tlpAssistantButtons.Controls.Add(this.lAssistantEmptyState, 0, 0);
                this.tlpAssistantButtons.SetColumnSpan(this.lAssistantEmptyState, 5);
            }
            this.UpdateAssistantButtonState();
            this.tlpAssistantButtons.ResumeLayout(true);
        }

        private void AssistantButton_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            Socket_RobotInfo robot = button == null ? null : button.Tag as Socket_RobotInfo;
            if (robot == null)
            {
                return;
            }
            if (this.activeAssistantRobot != null)
            {
                if (this.activeAssistantRobotId == robot.RID)
                {
                    this.activeAssistantRobot.StopRobot();
                }
                return;
            }

            Socket_Robot running = Socket_Cache.Robot.DoRobot(robot.RID, null);
            if (running == null)
            {
                return;
            }
            this.activeAssistantRobot = running;
            this.activeAssistantRobotId = robot.RID;
            running.Worker.RunWorkerCompleted += this.AssistantRobot_RunWorkerCompleted;
            this.UpdateAssistantButtonState();
        }

        private void AssistantButton_DoubleClick(object sender, EventArgs e)
        {
            this.EditAssistantButton_Click(sender, EventArgs.Empty);
        }

        private void EditAssistantButton_Click(object sender, EventArgs e)
        {
            ToolStripItem item = sender as ToolStripItem;
            Button button = item == null ? sender as Button : this.cmsAssistantButton.SourceControl as Button;
            Socket_RobotInfo robot = button == null ? null : button.Tag as Socket_RobotInfo;
            if (robot != null && this.activeAssistantRobot == null)
            {
                Socket_Operation.ShowRobotForm_Dialog(robot);
                this.RefreshAssistantFolders();
            }
        }

        private void AssistantRobot_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (this.activeAssistantRobot != null && ReferenceEquals(sender, this.activeAssistantRobot.Worker))
            {
                this.activeAssistantRobot = null;
                this.activeAssistantRobotId = Guid.Empty;
                this.UpdateAssistantButtonState();
            }
        }

        private void UpdateAssistantButtonState()
        {
            foreach (KeyValuePair<Guid, Button> pair in this.assistantButtons)
            {
                bool active = pair.Key == this.activeAssistantRobotId && this.activeAssistantRobot != null;
                bool enabled = this.activeAssistantRobot == null || active;
                pair.Value.Enabled = enabled;
                pair.Value.BackColor = active
                    ? Color.FromArgb(255, 223, 124)
                    : enabled ? Color.FromArgb(245, 248, 255) : Color.FromArgb(239, 241, 245);
                pair.Value.ForeColor = active
                    ? Color.FromArgb(104, 73, 12)
                    : enabled ? Color.FromArgb(32, 43, 61) : Color.FromArgb(142, 149, 160);
                pair.Value.FlatAppearance.BorderColor = active
                    ? Color.FromArgb(208, 143, 30)
                    : enabled ? Color.FromArgb(147, 177, 222) : Color.FromArgb(210, 215, 224);
                pair.Value.FlatAppearance.MouseOverBackColor = active
                    ? Color.FromArgb(255, 232, 163)
                    : Color.FromArgb(226, 237, 255);
                pair.Value.FlatAppearance.MouseDownBackColor = active
                    ? Color.FromArgb(245, 210, 102)
                    : Color.FromArgb(207, 225, 251);
                pair.Value.Text = active ? UiText("UI_Stop") : (pair.Value.Tag as Socket_RobotInfo).RName;
            }
            this.UpdateRobotToolbarState();
        }

        private void AddRobotFolder_Click(object sender, EventArgs e)
        {
            string folder = this.PromptForText(UiText("UI_NewGroup"), UiText("UI_GroupName"), string.Empty);
            if (string.IsNullOrWhiteSpace(folder) || this.tvRobotFolders.Nodes.Cast<TreeNode>()
                .Any(node => string.Equals(node.Text, folder.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            this.tvRobotFolders.Nodes.Add(new TreeNode(folder.Trim()) { Name = folder.Trim() });
            Socket_Cache.RobotList.lstFolders.Add(folder.Trim());
            this.tvRobotFolders.SelectedNode = this.tvRobotFolders.Nodes[this.tvRobotFolders.Nodes.Count - 1];
            this.SaveRobotFolderData();
        }

        private TreeNode GetSelectedRobotFolderNode()
        {
            return this.tvRobotFolders == null ? null : this.tvRobotFolders.SelectedNode;
        }

        private void RenameRobotFolder_Click(object sender, EventArgs e)
        {
            TreeNode node = this.GetSelectedRobotFolderNode();
            if (node == null)
            {
                return;
            }
            string folder = this.PromptForText(UiText("UI_RenameGroup"), UiText("UI_GroupName"), node.Text);
            if (string.IsNullOrWhiteSpace(folder) || string.Equals(folder.Trim(), node.Text, StringComparison.Ordinal))
            {
                return;
            }
            string oldFolder = node.Text;
            string newFolder = folder.Trim();
            if (this.tvRobotFolders.Nodes.Cast<TreeNode>().Any(item =>
                !ReferenceEquals(item, node) && string.Equals(item.Text, newFolder, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            foreach (Socket_RobotInfo robot in Socket_Cache.RobotList.lstRobot.Where(item => item.RFolder == oldFolder))
            {
                robot.RFolder = newFolder;
            }
            int folderIndex = Socket_Cache.RobotList.lstFolders.IndexOf(oldFolder);
            if (folderIndex >= 0)
            {
                Socket_Cache.RobotList.lstFolders[folderIndex] = newFolder;
            }
            node.Text = newFolder;
            node.Name = newFolder;
            this.selectedRobotFolder = newFolder;
            this.SaveRobotFolderData();
            this.RenderAssistantButtons();
        }

        private void MoveRobotFolderUp_Click(object sender, EventArgs e)
        {
            TreeNode node = this.GetSelectedRobotFolderNode();
            if (node == null || node.Index <= 0)
            {
                return;
            }
            int index = node.Index;
            this.tvRobotFolders.Nodes.RemoveAt(index);
            this.tvRobotFolders.Nodes.Insert(index - 1, node);
            this.MoveRobotFolderData(index, index - 1);
            this.tvRobotFolders.SelectedNode = node;
            this.SaveRobotFolderData();
        }

        private void MoveRobotFolderDown_Click(object sender, EventArgs e)
        {
            TreeNode node = this.GetSelectedRobotFolderNode();
            if (node == null || node.Index >= this.tvRobotFolders.Nodes.Count - 1)
            {
                return;
            }
            int index = node.Index;
            this.tvRobotFolders.Nodes.RemoveAt(index);
            this.tvRobotFolders.Nodes.Insert(index + 1, node);
            this.MoveRobotFolderData(index, index + 1);
            this.tvRobotFolders.SelectedNode = node;
            this.SaveRobotFolderData();
        }

        private void DeleteRobotFolder_Click(object sender, EventArgs e)
        {
            TreeNode node = this.GetSelectedRobotFolderNode();
            if (node == null || this.tvRobotFolders.Nodes.Count <= 1)
            {
                return;
            }
            string targetFolder = node.Text;
            if (Socket_Cache.RobotList.lstRobot.Any(item => item.RFolder == targetFolder))
            {
                Socket_Operation.ShowMessageBox(UiText("UI_GroupMustBeEmpty"));
                return;
            }
            Socket_Cache.RobotList.lstFolders.Remove(targetFolder);
            this.tvRobotFolders.Nodes.Remove(node);
            this.selectedRobotFolder = this.tvRobotFolders.Nodes[0].Text;
            this.SaveRobotFolderData();
            this.RefreshAssistantFolders();
        }

        private void SaveRobotFolderData()
        {
            Socket_Cache.RobotList.SaveRobotList_ToDB();
        }

        private void MoveRobotFolderData(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= Socket_Cache.RobotList.lstFolders.Count ||
                newIndex < 0 || newIndex >= Socket_Cache.RobotList.lstFolders.Count)
            {
                return;
            }
            string folder = Socket_Cache.RobotList.lstFolders[oldIndex];
            Socket_Cache.RobotList.lstFolders.RemoveAt(oldIndex);
            Socket_Cache.RobotList.lstFolders.Insert(newIndex, folder);
        }

        private static ToolStripMenuItem CreateRobotMoreItem(
            string name,
            string text,
            EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem
            {
                Name = name,
                Text = text
            };
            item.Click += handler;
            return item;
        }

        private void tsRobotList_Copy_Click(object sender, EventArgs e)
        {
            if (this.dgvRobotList.Rows.Count == 0)
            {
                return;
            }

            List<Socket_RobotInfo> selected = Socket_Operation.GetSelectedRobot(this.dgvRobotList);
            if (selected.Count > 0)
            {
                Socket_Cache.RobotList.UpdateRobotList_ByListAction(
                    Socket_Cache.System.ListAction.Copy,
                    selected);
                this.dgvRobotList.ClearSelection();
                this.dgvRobotList.Refresh();
            }
        }

        private void bSettings_Click(object sender, EventArgs e)
        {
            using (Form settingsForm = new Form())
            using (TabControl settingsSections = new TabControl())
            using (TabPage generalSettingsPage = new TabPage(UiText("Main_GeneralSettings")))
            using (TabPage advancedToolsPage = new TabPage(UiText("Main_AdvancedTools")))
            using (TableLayoutPanel settingsLayout = new TableLayoutPanel())
            using (TableLayoutPanel settingsHost = new TableLayoutPanel())
            using (FlowLayoutPanel settingsActions = new FlowLayoutPanel())
            using (Label settingsHint = new Label())
            using (Button settingsClose = new Button())
            {
                settingsForm.Text = UiText("Main_Settings");
                settingsForm.StartPosition = FormStartPosition.CenterParent;
                settingsForm.Size = new System.Drawing.Size(900, 460);
                settingsForm.MinimumSize = new System.Drawing.Size(760, 360);
                settingsForm.MinimizeBox = false;
                settingsForm.MaximizeBox = false;
                settingsForm.ShowInTaskbar = false;
                settingsForm.AutoScaleMode = AutoScaleMode.Dpi;
                settingsForm.AutoScroll = true;
                settingsForm.Font = this.Font;

                settingsHost.Dock = DockStyle.Fill;
                settingsHost.ColumnCount = 1;
                settingsHost.RowCount = 3;
                settingsHost.Padding = new Padding(6);
                settingsHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                settingsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                settingsHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                settingsHint.Dock = DockStyle.Fill;
                settingsHint.AutoSize = false;
                settingsHint.Height = 30;
                settingsHint.Padding = new Padding(4, 0, 4, 2);
                settingsHint.Text = UiText("UI_SettingsHint");
                settingsHint.AccessibleName = settingsHint.Text;
                settingsHint.TextAlign = ContentAlignment.MiddleLeft;
                settingsHint.ForeColor = UiNavigationTextColor;
                settingsHost.Controls.Add(settingsHint, 0, 0);

                settingsActions.Dock = DockStyle.Fill;
                settingsActions.AutoSize = true;
                settingsActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                settingsActions.FlowDirection = FlowDirection.RightToLeft;
                settingsActions.WrapContents = false;
                settingsActions.Padding = new Padding(0, 4, 0, 0);
                settingsClose.Name = "bSettingsClose";
                settingsClose.Text = UiText("UI_Close");
                settingsClose.AutoSize = true;
                settingsClose.DialogResult = DialogResult.Cancel;
                settingsClose.UseVisualStyleBackColor = true;
                settingsClose.AccessibleName = settingsClose.Text;
                settingsClose.AccessibleRole = AccessibleRole.PushButton;
                settingsActions.Controls.Add(settingsClose);

                settingsLayout.Dock = DockStyle.Fill;
                settingsLayout.AutoScroll = true;
                settingsLayout.Padding = new Padding(4);
                settingsLayout.ColumnCount = 2;
                settingsLayout.RowCount = 1;
                settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
                settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                settingsLayout.Controls.Add(this.gbHookButton_Search, 0, 0);
                settingsLayout.Controls.Add(this.tcSocketInfo, 1, 0);
                this.gbHookButton_Search.Dock = DockStyle.Top;
                this.tcSocketInfo.Dock = DockStyle.Fill;
                generalSettingsPage.AutoScroll = true;
                advancedToolsPage.AutoScroll = true;

                generalSettingsPage.Controls.Add(settingsLayout);
                advancedToolsPage.Controls.Add(this.tcAdvancedTools);
                settingsSections.Dock = DockStyle.Fill;
                settingsSections.Multiline = false;
                settingsSections.Controls.Add(generalSettingsPage);
                settingsSections.Controls.Add(this.tpFilterList);
                settingsSections.Controls.Add(this.tpRobotList);
                settingsSections.Controls.Add(advancedToolsPage);
                settingsSections.SelectedIndexChanged += delegate
                {
                    this.robotSettingsPageActive =
                        ReferenceEquals(settingsSections.SelectedTab, this.tpRobotList);
                };
                settingsHost.Controls.Add(settingsSections, 0, 1);
                settingsHost.Controls.Add(settingsActions, 0, 2);
                settingsForm.Controls.Add(settingsHost);
                settingsForm.CancelButton = settingsClose;

                try
                {
                    settingsForm.ShowDialog(this);
                }
                finally
                {
                    this.robotSettingsPageActive = false;
                    settingsLayout.Controls.Remove(this.gbHookButton_Search);
                    settingsLayout.Controls.Remove(this.tcSocketInfo);
                    advancedToolsPage.Controls.Remove(this.tcAdvancedTools);
                    settingsSections.Controls.Remove(this.tpFilterList);
                    settingsSections.Controls.Remove(this.tpRobotList);
                    if (!this.tcAutomation.Controls.Contains(this.tpFilterList))
                    {
                        this.tcAutomation.Controls.Add(this.tpFilterList);
                    }
                    if (!this.tcAutomation.Controls.Contains(this.tpRobotList))
                    {
                        this.tcAutomation.Controls.Add(this.tpRobotList);
                    }
                }
            }
        }

        private static string UiText(string key)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? key;
        }

        private void InitSocketListPacketMenu()
        {
            this.cmsSocketListPacketDetails = new ToolStripMenuItem
            {
                Name = "cmsSocketList_PacketDetails",
                Text = UiText("UI_PacketDetails"),
                Image = Properties.Resources.Info16,
                ImageScaling = ToolStripItemImageScaling.None
            };

            int sendIndex = this.cmsSocketList.Items.IndexOf(this.cmsSocketList_Send);
            this.cmsSocketList.Items.Insert(sendIndex + 1, this.cmsSocketListPacketDetails);
        }

        #endregion

        #region//发送列表文件夹

        private void InitSendFolderUI()
        {
            this.tpSendList.Text = UiText("UI_Send");
            this.tvSendFolders = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                FullRowSelect = true,
                ShowLines = false,
                ShowPlusMinus = false,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 4, 0),
                AccessibleName = UiText("UI_PacketGroups")
            };
            this.tvSendFolders.AfterSelect += this.tvSendFolders_AfterSelect;
            this.tvSendFolders.NodeMouseClick += (sender, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    this.tvSendFolders.SelectedNode = e.Node;
                }
            };

            this.cmsSendFolder = new ContextMenuStrip();
            this.cmsSendFolder.Opening += this.cmsSendFolder_Opening;
            this.cmsSendFolder.Items.Add(UiText("UI_RenameGroup"), null, this.cmsSendFolder_Rename_Click);
            this.cmsSendFolder.Items.Add(new ToolStripSeparator());
            this.cmsSendFolderMoveUp = new ToolStripMenuItem(
                UiText("UI_MoveGroupUp"),
                null,
                this.cmsSendFolder_MoveUp_Click);
            this.cmsSendFolderMoveDown = new ToolStripMenuItem(
                UiText("UI_MoveGroupDown"),
                null,
                this.cmsSendFolder_MoveDown_Click);
            this.cmsSendFolder.Items.Add(this.cmsSendFolderMoveUp);
            this.cmsSendFolder.Items.Add(this.cmsSendFolderMoveDown);
            this.cmsSendFolder.Items.Add(new ToolStripSeparator());
            this.cmsSendFolder.Items.Add(UiText("UI_DeleteGroup"), null, this.cmsSendFolder_Delete_Click);
            this.tvSendFolders.ContextMenuStrip = this.cmsSendFolder;

            this.bSendFolderAdd = new Button
            {
                Name = "bSendFolderAdd",
                Text = UiText("UI_NewGroup"),
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 4, 6, 4),
                UseVisualStyleBackColor = true,
                AccessibleName = UiText("UI_NewGroup")
            };
            this.bSendFolderAdd.Click += this.tsSendFolderAdd_Click;
            this.tt.SetToolTip(this.bSendFolderAdd, UiText("UI_NewGroup"));
            this.lSendFoldersTitle = new Label
            {
                Name = "lSendFoldersTitle",
                Text = UiText("UI_PacketGroups"),
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Margin = Padding.Empty
            };
            this.ConfigureSendToolbarTextButton(this.tsSendList_Load, UiText("UI_Load"));
            this.tsSendList_Load.Visible = false;
            this.ConfigureSendToolbarTextButton(this.tsSendList_Save, UiText("UI_Save"));
            this.tsSendList_Save.Visible = false;
            this.ConfigureSendToolbarTextButton(this.tsSendList_Add, UiText("UI_NewPacket"));
            this.ConfigureSendToolbarTextButton(this.tsSendList_Start, UiText("UI_SendSelected"));
            this.ConfigureSendToolbarTextButton(this.tsSendList_Stop, UiText("UI_StopBatch"));
            this.ConfigureSendToolbarTextButton(this.tsSendList_CleanUp, UiText("Main_Clear"));
            this.tsSendList_CleanUp.Visible = false;
            this.tsSendList_Add.ToolTipText = UiText("UI_NewPacket");
            this.tsSendList_Start.ToolTipText = UiText("UI_SendSelected");
            this.tsSendList_Stop.ToolTipText = UiText("UI_StopBatch");
            this.tsSendList_Start.ForeColor = System.Drawing.Color.ForestGreen;
            this.tsSendList_Stop.ForeColor = System.Drawing.Color.Firebrick;
            this.toolStripSeparator8.Visible = false;
            this.toolStripSeparator7.Visible = false;

            this.tsSendListSelectAll = new ToolStripButton
            {
                Name = "tsSendListSelectAll",
                Text = UiText("UI_SelectAll"),
                ToolTipText = UiText("UI_SelectAllTip"),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = true,
                Overflow = ToolStripItemOverflow.AsNeeded,
                Margin = new Padding(3),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter
            };
            this.tsSendListSelectAll.Click += this.tsSendListSelectAll_Click;
            this.tsSendListParallel = new ToolStripButton
            {
                Name = "tsSendListParallel",
                Text = UiText("UI_SequentialSend"),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = true,
                CheckOnClick = true,
                Overflow = ToolStripItemOverflow.AsNeeded,
                Margin = new Padding(3),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter
            };
            this.tsSendListParallel.Click += this.tsSendListParallel_Click;
            this.tsSendList.Items.Remove(this.tsSendList_Add);
            this.tsSendList.Items.Remove(this.tsSendListSelectAll);
            this.tsSendList.Items.Remove(this.tsSendListParallel);
            this.tsSendList.Items.Remove(this.tsSendList_Start);
            this.tsSendList.Items.Remove(this.tsSendList_Stop);
            this.tsSendList.Items.AddRange(new ToolStripItem[]
            {
                this.tsSendList_Add,
                this.tsSendListSelectAll,
                this.tsSendListParallel,
                this.tsSendList_Start,
                this.tsSendList_Stop
            });
            this.UpdateSendListParallelModeText();
            this.tsSendListContext = new ToolStripLabel
            {
                Name = "tsSendListContext",
                Alignment = ToolStripItemAlignment.Right,
                AutoSize = true,
                Margin = new Padding(8, 3, 6, 3),
                Overflow = ToolStripItemOverflow.AsNeeded,
                ForeColor = System.Drawing.SystemColors.GrayText
            };
            this.tsSendList.Items.Add(this.tsSendListContext);
            this.tsSendList.Visible = true;

            this.dgvSendList.ColumnHeadersVisible = true;
            this.dgvSendList.ColumnHeadersHeight = 28;
            this.dgvSendList.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dgvSendList.AutoGenerateColumns = false;
            this.dgvSendList.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            this.dgvSendList.ScrollBars = ScrollBars.Vertical;
            this.dgvSendList.AccessibleName = UiText("Main_Automation");
            this.cIsEnable.HeaderText = UiText("UI_Select");
            this.cIsEnable.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            this.cIsEnable.Width = 48;
            this.cIsEnable.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            this.cIsEnable.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            this.cICO.Visible = false;
            this.cName.HeaderText = UiText("UI_Name");
            this.cName.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            this.cName.MinimumWidth = 80;
            this.cName.Width = 100;
            this.cName.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            this.cName.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            this.cName.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

            DataGridViewTextBoxColumn sortOrderColumn = new DataGridViewTextBoxColumn
            {
                Name = "cSortOrder",
                HeaderText = UiText("UI_Order"),
                DataPropertyName = "SSortOrder",
                ReadOnly = false,
                ValueType = typeof(int),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 46,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Format = "D2",
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                }
            };
            sortOrderColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            this.dgvSendList.Columns.Insert(0, sortOrderColumn);

            DataGridViewButtonColumn sendNowColumn = new DataGridViewButtonColumn
            {
                Name = "cSendNow",
                HeaderText = string.Empty,
                Text = UiText("UI_Send"),
                UseColumnTextForButtonValue = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 60,
                FlatStyle = FlatStyle.Standard,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    BackColor = System.Drawing.SystemColors.Control,
                    ForeColor = System.Drawing.SystemColors.ControlText,
                    SelectionBackColor = System.Drawing.SystemColors.Control,
                    SelectionForeColor = System.Drawing.SystemColors.ControlText,
                    Padding = new Padding(2, 1, 2, 1)
                }
            };
            this.dgvSendList.Columns.Insert(1, sendNowColumn);

            DataGridViewButtonColumn stopNowColumn = new DataGridViewButtonColumn
            {
                Name = "cStopNow",
                HeaderText = string.Empty,
                Text = UiText("UI_Stop"),
                UseColumnTextForButtonValue = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 60,
                FlatStyle = FlatStyle.Standard,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    BackColor = System.Drawing.SystemColors.Control,
                    ForeColor = System.Drawing.SystemColors.GrayText,
                    SelectionBackColor = System.Drawing.SystemColors.Control,
                    SelectionForeColor = System.Drawing.SystemColors.GrayText,
                    Padding = new Padding(2, 1, 2, 1)
                }
            };
            this.dgvSendList.Columns.Insert(2, stopNowColumn);

            DataGridViewTextBoxColumn loopCountColumn = new DataGridViewTextBoxColumn
            {
                Name = "cLoopCount",
                HeaderText = UiText("UI_SendCount"),
                DataPropertyName = "SLoopCNT",
                ReadOnly = false,
                ValueType = typeof(int),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 84,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    SelectionBackColor = System.Drawing.SystemColors.Window,
                    SelectionForeColor = System.Drawing.SystemColors.WindowText
                }
            };
            loopCountColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            DataGridViewTextBoxColumn loopIntervalColumn = new DataGridViewTextBoxColumn
            {
                Name = "cLoopInterval",
                HeaderText = UiText("UI_IntervalMs"),
                DataPropertyName = "SLoopINT",
                ReadOnly = false,
                ValueType = typeof(int),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 90,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    SelectionBackColor = System.Drawing.SystemColors.Window,
                    SelectionForeColor = System.Drawing.SystemColors.WindowText
                }
            };
            loopIntervalColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            this.dgvSendList.Columns.Add(loopCountColumn);
            this.dgvSendList.Columns.Add(loopIntervalColumn);
            this.cICO.DisplayIndex = this.dgvSendList.Columns.Count - 1;
            sortOrderColumn.DisplayIndex = 0;
            this.cIsEnable.DisplayIndex = 1;
            this.cName.DisplayIndex = 2;
            loopCountColumn.DisplayIndex = 3;
            loopIntervalColumn.DisplayIndex = 4;
            sendNowColumn.DisplayIndex = 5;
            stopNowColumn.DisplayIndex = 6;
            this.dgvSendList.CellValidating += this.dgvSendList_CellValidating;
            this.dgvSendList.CellBeginEdit += this.dgvSendList_CellBeginEdit;
            this.dgvSendList.CellEndEdit += this.dgvSendList_CellEndEdit;
            this.dgvSendList.CellClick += this.dgvSendList_CellClick;
            this.dgvSendList.EditingControlShowing +=
                this.dgvSendList_EditingControlShowing;
            this.dgvSendList.CellFormatting += this.dgvSendList_CellFormatting;
            this.dgvSendList.CellToolTipTextNeeded += this.dgvSendList_CellToolTipTextNeeded;
            this.dgvSendList.Paint += this.dgvSendList_Paint;

            this.cmsSendListMoveToFolder = new ToolStripMenuItem(UiText("UI_MoveToGroup"));
            this.cmsSendListEdit = new ToolStripMenuItem(UiText("UI_EditPacket"));
            this.cmsSendListEdit.Click += this.cmsSendListEdit_Click;
            this.cmsSendList.Items.Insert(0, this.cmsSendListEdit);
            int deleteIndex = this.cmsSendList.Items.IndexOf(this.cmsSendList_Delete);
            this.cmsSendList.Items.Insert(deleteIndex, this.cmsSendListMoveToFolder);
            this.cmsSendList.Opening += this.cmsSendList_Opening;

            this.tlpSendList.Controls.Remove(this.dgvSendList);
            this.tlpSendList.Controls.Remove(this.tsSendList);
            this.tlpSendList.ColumnStyles.Clear();
            this.tlpSendList.ColumnCount = 2;
            this.tlpSendList.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            this.tlpSendList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tlpSendList.RowStyles.Clear();
            this.tlpSendList.RowCount = 3;
            this.tlpSendList.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
            this.tlpSendList.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tlpSendList.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            this.tlpSendList.Controls.Add(this.lSendFoldersTitle, 0, 0);
            this.tlpSendList.Controls.Add(this.tsSendList, 1, 0);
            this.tlpSendList.Controls.Add(this.tvSendFolders, 0, 1);
            this.tlpSendList.Controls.Add(this.dgvSendList, 1, 1);
            this.tlpSendList.SetRowSpan(this.dgvSendList, 2);
            this.tlpSendList.Controls.Add(this.bSendFolderAdd, 0, 2);

            Socket_Cache.SendList.lstFolders.ListChanged += this.SendFolders_ListChanged;
            Socket_Cache.SendList.lstSend.ListChanged += this.SendList_ListChanged;
            this.ConfigureSendListMoreButton();
            this.RefreshSendFolderTree();
        }

        private void ConfigureSendListMoreButton()
        {
            this.tsSendListMore = new ToolStripDropDownButton
            {
                Name = "tsSendListMore",
                Text = UiText("UI_More"),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = true,
                Overflow = ToolStripItemOverflow.Never,
                Margin = new Padding(3)
            };
            this.tsSendListMore.DropDownItems.Add(CreateSendMoreItem(
                "SendMoreLoad", UiText("Send_Load"), this.tsSendList_Load_Click));
            this.tsSendListMore.DropDownItems.Add(CreateSendMoreItem(
                "SendMoreSave", UiText("Send_Save"), this.tsSendList_Save_Click));
            this.tsSendListMore.DropDownItems.Add(CreateSendMoreItem(
                "SendMoreCopy", UiText("Send_Copy"), this.tsSendList_Copy_Click));
            this.tsSendListMore.DropDownItems.Add(new ToolStripSeparator());
            this.tsSendListMore.DropDownItems.Add(CreateSendMoreItem(
                "SendMoreClear", UiText("Send_Clear"), this.tsSendList_CleanUp_Click));

            int contextIndex = this.tsSendList.Items.IndexOf(this.tsSendListContext);
            if (contextIndex < 0)
            {
                this.tsSendList.Items.Add(this.tsSendListMore);
            }
            else
            {
                this.tsSendList.Items.Insert(contextIndex, this.tsSendListMore);
            }
        }

        private static ToolStripMenuItem CreateSendMoreItem(
            string name,
            string text,
            EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem
            {
                Name = name,
                Text = text
            };
            item.Click += handler;
            return item;
        }

        private void tsSendList_Copy_Click(object sender, EventArgs e)
        {
            if (this.dgvSendList.Rows.Count == 0)
            {
                return;
            }

            List<Socket_SendInfo> selected = Socket_Operation.GetSelectedSend(this.dgvSendList);
            if (selected.Count == 0 || !this.CanModifySendLists(selected, "复制封包"))
            {
                return;
            }

            Socket_Cache.SendList.UpdateSendList_ByListAction(
                Socket_Cache.System.ListAction.Copy,
                selected);
            this.RefreshSendFolderView();
        }

        private void ConfigureSendToolbarTextButton(ToolStripButton button, string text)
        {
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.Image = null;
            button.Text = text;
            button.ToolTipText = text;
            button.AccessibleName = text;
            button.AutoSize = true;
            button.Overflow = ToolStripItemOverflow.AsNeeded;
            button.Margin = new Padding(3);
        }

        private void RefreshSendFolderTree()
        {
            this.MigrateUngroupedSendLists();
            string selectedKey = this.selectedSendFolder;
            this.tvSendFolders.BeginUpdate();
            this.tvSendFolders.Nodes.Clear();

            foreach (string folderName in Socket_Cache.SendList.lstFolders)
            {
                this.tvSendFolders.Nodes.Add(new TreeNode(folderName) { Tag = folderName });
            }

            TreeNode selectedNode = this.tvSendFolders.Nodes.Cast<TreeNode>()
                .FirstOrDefault(node => string.Equals(node.Tag as string, selectedKey, StringComparison.Ordinal));
            if (selectedNode == null && this.tvSendFolders.Nodes.Count > 0)
            {
                selectedNode = this.tvSendFolders.Nodes[0];
            }

            if (selectedNode != null)
            {
                this.selectedSendFolder = selectedNode.Tag as string ?? "__ALL__";
                this.tvSendFolders.SelectedNode = selectedNode;
            }
            else
            {
                this.selectedSendFolder = "__ALL__";
                this.RefreshSendFolderView();
            }

            this.tvSendFolders.EndUpdate();
        }

        private void RefreshSendFolderView()
        {
            this.MigrateUngroupedSendLists();
            List<Socket_SendInfo> visibleItems = this.GetCurrentFolderSendLists();
            for (int index = 0; index < visibleItems.Count; index++)
            {
                visibleItems[index].SSortOrder = index + 1;
            }

            this.dgvSendList.DataSource = new BindingList<Socket_SendInfo>(visibleItems);
            HashSet<string> visibleColumnNames = new HashSet<string>
            {
                "cSortOrder",
                "cIsEnable",
                "cName",
                "cLoopCount",
                "cLoopInterval",
                "cSendNow",
                "cStopNow"
            };
            foreach (DataGridViewColumn column in this.dgvSendList.Columns)
            {
                column.Visible = visibleColumnNames.Contains(column.Name);
            }

            bool hasSelectedFolder = this.HasSelectedSendFolder();
            bool batchIsBusy = this.bgwSendList.IsBusy;
            this.tsSendList_Add.Enabled = hasSelectedFolder && !batchIsBusy;
            this.tsSendListSelectAll.Enabled = hasSelectedFolder && visibleItems.Count > 0 && !batchIsBusy;
            this.UpdateSendListSelectAllState(visibleItems);
            this.UpdateSendListContext(visibleItems);
            this.UpdateSendExecutionControls(visibleItems);
            this.dgvSendList.Invalidate();
        }

        private void UpdateSendListSelectAllState(List<Socket_SendInfo> currentItems = null)
        {
            List<Socket_SendInfo> items = currentItems ?? this.GetCurrentFolderSendLists();
            bool allSelected = items.Count > 0 && items.All(item => item.IsEnable);
            this.tsSendListSelectAll.Text =
                UiText(allSelected ? "UI_ClearSelection" : "UI_SelectAll");
            this.tsSendListSelectAll.ToolTipText = allSelected
                ? UiText("UI_ClearSelectionTip")
                : UiText("UI_SelectAllTip");
        }

        private void UpdateSendListContext(List<Socket_SendInfo> currentItems = null)
        {
            if (this.tsSendListContext == null)
            {
                return;
            }

            if (!this.HasSelectedSendFolder())
            {
                this.tsSendListContext.Text = UiText("UI_NoGroup");
                this.tsSendListContext.ToolTipText = UiText("UI_NoGroupTip");
                return;
            }

            List<Socket_SendInfo> items = currentItems ?? this.GetCurrentFolderSendLists();
            int selectedCount = items.Count(item => item.IsEnable);
            string displayFolder = this.selectedSendFolder.Length > 12
                ? this.selectedSendFolder.Substring(0, 12) + "…"
                : this.selectedSendFolder;
            this.tsSendListContext.Text = string.Format(
                UiText("UI_SelectedContext"), displayFolder, selectedCount);
            this.tsSendListContext.ToolTipText = string.Format(
                UiText("UI_SelectedContextTip"),
                this.selectedSendFolder,
                selectedCount);
        }

        private void UpdateSendExecutionControls(List<Socket_SendInfo> currentItems = null)
        {
            List<Socket_SendInfo> items = currentItems ?? this.GetCurrentFolderSendLists();
            bool hasManualSend = this.manualSendOperations.Values.Any(send => send.Worker.IsBusy);
            bool batchIsBusy = this.bgwSendList.IsBusy;
            this.tsSendList_Start.Enabled =
                this.HasSelectedSendFolder() &&
                items.Count > 0 &&
                !batchIsBusy &&
                !hasManualSend;
            this.tsSendList_Stop.Enabled = batchIsBusy;
            if (this.tsSendListParallel != null)
            {
                this.tsSendListParallel.Enabled = !batchIsBusy;
            }
            if (this.tsSendListMore != null)
            {
                this.tsSendListMore.Enabled = !batchIsBusy;
            }
        }

        private void UpdateSendListParallelModeText()
        {
            if (this.tsSendListParallel == null)
            {
                return;
            }

            this.tsSendListParallel.Text = UiText(
                this.tsSendListParallel.Checked ? "UI_ConcurrentSend" : "UI_SequentialSend");
            this.tsSendListParallel.ToolTipText = UiText(
                this.tsSendListParallel.Checked ? "UI_ConcurrentSendTip" : "UI_SequentialSendTip");
        }

        private bool HasSelectedSendFolder()
        {
            return this.selectedSendFolder != "__ALL__" &&
                Socket_Cache.SendList.lstFolders.Any(folder =>
                    string.Equals(folder, this.selectedSendFolder, StringComparison.Ordinal));
        }

        private void MigrateUngroupedSendLists()
        {
            if (Socket_Cache.SendList.lstFolders.Count == 0)
            {
                return;
            }

            string targetFolder = Socket_Cache.SendList.lstFolders[0];
            int nextOrder = Socket_Cache.SendList.lstSend
                .Where(item => string.Equals(item.SFolder, targetFolder, StringComparison.Ordinal))
                .Select(item => item.SSortOrder)
                .DefaultIfEmpty(0)
                .Max() + 1;

            foreach (Socket_SendInfo sendInfo in Socket_Cache.SendList.lstSend
                .Where(item => string.IsNullOrEmpty(item.SFolder))
                .ToList())
            {
                sendInfo.SFolder = targetFolder;
                sendInfo.SSortOrder = nextOrder++;
            }
        }

        private List<Socket_SendInfo> GetCurrentFolderSendLists()
        {
            IEnumerable<Socket_SendInfo> items = Socket_Cache.SendList.lstSend
                .Where(item => item.SCollection != null && item.SCollection.Count > 0);
            if (this.selectedSendFolder != "__ALL__")
            {
                items = items.Where(item =>
                    string.Equals(item.SFolder, this.selectedSendFolder, StringComparison.Ordinal));
            }

            return items
                .Select((item, index) => new { Item = item, Index = index })
                .OrderBy(entry => entry.Item.SSortOrder <= 0 ? int.MaxValue : entry.Item.SSortOrder)
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Item)
                .ToList();
        }

        private void tvSendFolders_AfterSelect(object sender, TreeViewEventArgs e)
        {
            this.selectedSendFolder = e.Node.Tag as string ?? "__ALL__";
            this.RefreshSendFolderView();
        }

        private void SendFolders_ListChanged(object sender, ListChangedEventArgs e)
        {
            this.RefreshSendFolderTree();
        }

        private void SendList_ListChanged(object sender, ListChangedEventArgs e)
        {
            this.RefreshSendFolderView();
        }

        private void tsSendListSelectAll_Click(object sender, EventArgs e)
        {
            if (!this.HasSelectedSendFolder())
            {
                MessageBox.Show(this, UiText("UI_SelectGroupFirst"), UiText("UI_SelectAll"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<Socket_SendInfo> currentItems = this.GetCurrentFolderSendLists();
            bool selectAll = currentItems.Any(item => !item.IsEnable);
            foreach (Socket_SendInfo sendInfo in currentItems)
            {
                sendInfo.IsEnable = selectAll;
            }

            this.dgvSendList.Refresh();
            this.UpdateSendListSelectAllState(currentItems);
            this.UpdateSendListContext(currentItems);
        }

        private void tsSendFolderAdd_Click(object sender, EventArgs e)
        {
            string folderName = this.PromptForText(
                UiText("UI_NewGroup"), UiText("UI_GroupName"), string.Empty);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }

            if (!Socket_Cache.SendList.AddFolder(folderName))
            {
                MessageBox.Show(this, UiText("UI_GroupExists"), UiText("UI_NewGroup"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            this.selectedSendFolder = folderName.Trim();
            this.RefreshSendFolderTree();
        }

        private void cmsSendFolder_Rename_Click(object sender, EventArgs e)
        {
            string oldName = this.tvSendFolders.SelectedNode == null
                ? string.Empty
                : this.tvSendFolders.SelectedNode.Tag as string;
            if (string.IsNullOrEmpty(oldName) || oldName == "__ALL__")
            {
                return;
            }

            string newName = this.PromptForText(
                UiText("UI_RenameGroup"), UiText("UI_GroupName"), oldName);
            if (string.IsNullOrWhiteSpace(newName) ||
                Socket_Cache.SendList.lstFolders.Any(item =>
                    !string.Equals(item, oldName, StringComparison.Ordinal) &&
                    string.Equals(item, newName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Socket_Cache.SendList.RenameFolder(oldName, newName.Trim());
            this.selectedSendFolder = newName.Trim();
            this.RefreshSendFolderTree();
            this.RefreshSendFolderView();
        }

        private void cmsSendFolder_Opening(object sender, CancelEventArgs e)
        {
            string folderName = this.tvSendFolders.SelectedNode == null
                ? string.Empty
                : this.tvSendFolders.SelectedNode.Tag as string;
            int index = Socket_Cache.SendList.lstFolders.IndexOf(folderName);
            this.cmsSendFolderMoveUp.Enabled = index > 0;
            this.cmsSendFolderMoveDown.Enabled =
                index >= 0 && index < Socket_Cache.SendList.lstFolders.Count - 1;
        }

        private void cmsSendFolder_MoveUp_Click(object sender, EventArgs e)
        {
            this.MoveSelectedSendFolder(-1);
        }

        private void cmsSendFolder_MoveDown_Click(object sender, EventArgs e)
        {
            this.MoveSelectedSendFolder(1);
        }

        private void MoveSelectedSendFolder(int offset)
        {
            string folderName = this.tvSendFolders.SelectedNode == null
                ? string.Empty
                : this.tvSendFolders.SelectedNode.Tag as string;
            if (string.IsNullOrEmpty(folderName) ||
                !Socket_Cache.SendList.MoveFolder(folderName, offset))
            {
                return;
            }

            this.selectedSendFolder = folderName;
            this.RefreshSendFolderTree();
        }

        private void cmsSendFolder_Delete_Click(object sender, EventArgs e)
        {
            string folderName = this.tvSendFolders.SelectedNode == null
                ? string.Empty
                : this.tvSendFolders.SelectedNode.Tag as string;
            if (string.IsNullOrEmpty(folderName) || folderName == "__ALL__")
            {
                return;
            }

            if (Socket_Cache.SendList.lstSend.Any(item =>
                string.Equals(item.SFolder, folderName, StringComparison.Ordinal) &&
                item.SCollection != null &&
                item.SCollection.Count > 0))
            {
                MessageBox.Show(this, UiText("UI_GroupHasSendLists"),
                    UiText("UI_DeleteGroup"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show(this, UiText("UI_ConfirmDeleteEmptyGroup"),
                UiText("UI_DeleteGroup"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (result != DialogResult.OK)
            {
                return;
            }

            foreach (Socket_SendInfo emptyPlaceholder in Socket_Cache.SendList.lstSend
                .Where(item =>
                    string.Equals(item.SFolder, folderName, StringComparison.Ordinal) &&
                    (item.SCollection == null || item.SCollection.Count == 0))
                .ToList())
            {
                Socket_Cache.SendList.lstSend.Remove(emptyPlaceholder);
            }

            Socket_Cache.SendList.RemoveFolder(folderName);
            this.selectedSendFolder = "__ALL__";
            this.RefreshSendFolderTree();
            this.RefreshSendFolderView();
        }

        private void cmsSendList_Opening(object sender, CancelEventArgs e)
        {
            List<Socket_SendInfo> selectedItems = Socket_Operation.GetSelectedSend(this.dgvSendList);
            Socket_SendInfo currentItem = this.dgvSendList.CurrentRow == null
                ? null
                : this.dgvSendList.CurrentRow.DataBoundItem as Socket_SendInfo;
            bool selectedItemIsLocked =
                selectedItems.Any(item => this.IsSendListLockedForModification(item.SID));
            bool currentItemIsLocked =
                currentItem != null && this.IsSendListLockedForModification(currentItem.SID);

            this.cmsSendListEdit.Enabled = currentItem != null && !currentItemIsLocked;
            this.cmsSendListMoveToFolder.Enabled = selectedItems.Count > 0 && !selectedItemIsLocked;
            this.cmsSendList_Top.Enabled = !selectedItemIsLocked;
            this.cmsSendList_Up.Enabled = !selectedItemIsLocked;
            this.cmsSendList_Down.Enabled = !selectedItemIsLocked;
            this.cmsSendList_Bottom.Enabled = !selectedItemIsLocked;
            this.cmsSendList_Delete.Enabled = !selectedItemIsLocked;

            this.cmsSendListMoveToFolder.DropDownItems.Clear();
            foreach (string folderName in Socket_Cache.SendList.lstFolders)
            {
                this.AddMoveToFolderMenuItem(folderName, folderName);
            }
        }

        private void AddMoveToFolderMenuItem(string displayName, string folderName)
        {
            ToolStripMenuItem folderItem = new ToolStripMenuItem(displayName) { Tag = folderName };
            folderItem.Click += this.MoveSelectedSendListsToFolder_Click;
            this.cmsSendListMoveToFolder.DropDownItems.Add(folderItem);
        }

        private void MoveSelectedSendListsToFolder_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem folderItem = sender as ToolStripMenuItem;
            if (folderItem == null)
            {
                return;
            }

            string folderName = folderItem.Tag as string ?? string.Empty;
            List<Socket_SendInfo> selectedItems = Socket_Operation.GetSelectedSend(this.dgvSendList)
                .OrderBy(item => item.SSortOrder)
                .ToList();
            if (!this.CanModifySendLists(selectedItems, "移动封包"))
            {
                return;
            }

            HashSet<string> sourceFolders = new HashSet<string>(
                selectedItems.Select(item => item.SFolder ?? string.Empty),
                StringComparer.Ordinal);
            int nextOrder = Socket_Cache.SendList.lstSend.Count(item =>
                string.Equals(item.SFolder, folderName, StringComparison.Ordinal) &&
                !selectedItems.Contains(item)) + 1;

            foreach (Socket_SendInfo sendInfo in selectedItems)
            {
                sendInfo.SFolder = folderName;
                sendInfo.SSortOrder = nextOrder++;
            }

            foreach (string sourceFolder in sourceFolders)
            {
                List<Socket_SendInfo> remainingItems = Socket_Cache.SendList.lstSend
                    .Where(item => string.Equals(item.SFolder, sourceFolder, StringComparison.Ordinal))
                    .OrderBy(item => item.SSortOrder)
                    .ToList();
                for (int index = 0; index < remainingItems.Count; index++)
                {
                    remainingItems[index].SSortOrder = index + 1;
                }
            }

            this.RefreshSendFolderView();
        }

        private string PromptForText(string title, string prompt, string initialValue)
        {
            using (Form promptForm = new Form())
            using (Label promptLabel = new Label())
            using (Panel inputPanel = new Panel())
            using (TextBox input = new TextBox())
            using (Panel divider = new Panel())
            using (Button okButton = new Button())
            using (Button cancelButton = new Button())
            {
                promptForm.Text = title;
                promptForm.StartPosition = FormStartPosition.CenterParent;
                promptForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                promptForm.ClientSize = new System.Drawing.Size(400, 160);
                promptForm.MinimizeBox = false;
                promptForm.MaximizeBox = false;
                promptForm.ShowInTaskbar = false;
                promptForm.BackColor = System.Drawing.Color.FromArgb(248, 248, 248);
                promptForm.Font = this.Font;

                promptLabel.Text = prompt;
                promptLabel.AutoSize = true;
                promptLabel.Location = new System.Drawing.Point(20, 18);

                input.Text = initialValue ?? string.Empty;
                input.BorderStyle = BorderStyle.None;
                input.Location = new System.Drawing.Point(10, 8);
                input.Width = 338;

                inputPanel.BackColor = System.Drawing.Color.White;
                inputPanel.BorderStyle = BorderStyle.FixedSingle;
                inputPanel.Location = new System.Drawing.Point(20, 43);
                inputPanel.Size = new System.Drawing.Size(360, 34);
                inputPanel.Controls.Add(input);

                divider.BackColor = System.Drawing.Color.FromArgb(220, 220, 220);
                divider.Location = new System.Drawing.Point(20, 94);
                divider.Size = new System.Drawing.Size(360, 1);

                okButton.Text = UiText("UI_OK");
                okButton.DialogResult = DialogResult.OK;
                okButton.Location = new System.Drawing.Point(198, 111);
                okButton.Size = new System.Drawing.Size(88, 32);

                cancelButton.Text = UiText("UI_Cancel");
                cancelButton.DialogResult = DialogResult.Cancel;
                cancelButton.Location = new System.Drawing.Point(292, 111);
                cancelButton.Size = new System.Drawing.Size(88, 32);

                promptForm.Controls.Add(promptLabel);
                promptForm.Controls.Add(inputPanel);
                promptForm.Controls.Add(divider);
                promptForm.Controls.Add(okButton);
                promptForm.Controls.Add(cancelButton);
                promptForm.AcceptButton = okButton;
                promptForm.CancelButton = cancelButton;
                promptForm.Shown += delegate
                {
                    input.Focus();
                    input.SelectAll();
                };

                return promptForm.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
            }
        }

        #endregion

        #region//窗体事件

        private void Socket_Form_Load(object sender, EventArgs e)
        {
            this.InitSocketForm();
            this.InitHexBox_XOR();
            this.LoadConfigs_Parameter();
            this.InitHotKeys();

            // 当前产品固定为本地注入模式。即使旧数据库残留远程管理配置，
            // 也不得在没有可见配置入口的情况下启动 HTTP 服务。
            Socket_Cache.System.IsRemote = false;
            Socket_Cache.System.LoadSystemList_FromDB();
        }

        private void Socket_Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.CommitPacketDataEdits();
            this.StopByteSweep();
            this.SaveConfigs_Parameter();
            this.ExitMainForm();
        }

        private void Socket_Form_Resize(object sender, EventArgs e)
        {
            try
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    this.Hide();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void niWPE_Click(object sender, EventArgs e)
        {
            try
            {
                if (((MouseEventArgs)e).Button == MouseButtons.Left)
                {
                    this.ShowMainForm();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void ShowMainForm()
        {
            try
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public void ExitMainForm()
        {
            try
            {
                foreach (Socket_Send manualSend in this.manualSendOperations.Values.ToList())
                {
                    if (!manualSend.WaitForStop(2000))
                    {
                        Socket_Operation.DoLog(nameof(ExitMainForm), "Timed out while stopping a manual send task.");
                    }
                }
                this.manualSendOperations.Clear();
                List<Socket_Send> batchSends;
                lock (this.sendOperationSync)
                {
                    batchSends = this.activeBatchSendOperations.Values.ToList();
                    this.activeBatchSendOperations.Clear();
                }
                foreach (Socket_Send batchSend in batchSends)
                {
                    if (!batchSend.WaitForStop(2000))
                    {
                        Socket_Operation.DoLog(nameof(ExitMainForm), "Timed out while stopping a batch send task.");
                    }
                }

                ws.ExitHook();
                this.niWPE.Visible = false;

                Socket_Operation.StopRemoteMGT(this.RunMode);
                Socket_Cache.System.SaveSystemList_ToDB();
                Socket_Cache.System.SaveRunConfig_ToDB(this.RunMode);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == User32.WM_HOTKEY)
                {
                    int HOTKEY_ID = m.WParam.ToInt32();

                    if (this.robotSettingsPageActive || ReferenceEquals(this.tcAutomation.SelectedTab, this.tpRobotList))
                    {
                        Socket_Cache.Robot.DoRobot_ByHotKey(HOTKEY_ID);
                    }
                    else if (ReferenceEquals(this.tcAutomation.SelectedTab, this.tpSendList))
                    {
                        Socket_Cache.Send.DoSend_ByHotKey(HOTKEY_ID);
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            base.WndProc(ref m);
        }

        #endregion

        #region//初始化

        private void InitSocketForm()
        {
            try
            {
                this.Text = Socket_Cache.System.WPE + " - " + Socket_Operation.AssemblyVersion;

                tt.SetToolTip(cbWorkingMode_Speed, MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_22));
                tt.SetToolTip(rbFilterSet_Priority, MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_63));
                tt.SetToolTip(rbFilterSet_Sequence, MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_64));
                tt.SetToolTip(bSearch, MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_25));
                tt.SetToolTip(bSearchNext, MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_26));

                Socket_Cache.System.MainHandle = this.Handle;
                string sProcessName = Socket_Operation.GetProcessName();
                this.tsslProcessName.Text = sProcessName;
                this.niWPE.Text = Socket_Cache.System.WPE + "\r\n" + sProcessName;

                this.tSocketInfo.Enabled = true;
                this.tSocketList.Enabled = true;

                this.tsslProcessInfo.Text = Socket_Operation.GetProcessInfo();
                this.tsslWinSock.Text = Socket_Operation.GetWinSockSupportInfo();
                this.tsslProcessInfo.Visible = true;
                this.tsslWinSock.Visible = false;
                this.tsslSplit1.Visible = true;
                this.tsslSplit2.Visible = false;
                this.tsslProcessInfo.Text = UiText("UI_HookStatusReady");

                this.bStartHook.Enabled = true;
                this.bStopHook.Enabled = false;
                this.cmsIcon_StartHook.Enabled = true;
                this.cmsIcon_StopHook.Enabled = false;
                this.cbbExtraction.SelectedIndex = 0;

                this.InitFilterActionColor();
                Socket_Operation.InitCPUAndMemoryCounter();

                this.UpdateCompactTrafficStatus();

                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sProcessName);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void InitHexBox_XOR()
        {
            try
            {
                this.hbXOR_From.ByteProvider = new DynamicByteProvider(new byte[0]);
                this.hbXOR_To.ByteProvider = new DynamicByteProvider(new byte[0]);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void InitHotKeys()
        {
            this.txtHotKey1.RegisterHotkeyFromText(9001);
            this.txtHotKey2.RegisterHotkeyFromText(9002);
            this.txtHotKey3.RegisterHotkeyFromText(9003);
            this.txtHotKey4.RegisterHotkeyFromText(9004);
            this.txtHotKey5.RegisterHotkeyFromText(9005);
            this.txtHotKey6.RegisterHotkeyFromText(9006);
            this.txtHotKey7.RegisterHotkeyFromText(9007);
            this.txtHotKey8.RegisterHotkeyFromText(9008);
            this.txtHotKey9.RegisterHotkeyFromText(9009);
            this.txtHotKey10.RegisterHotkeyFromText(9010);
            this.txtHotKey11.RegisterHotkeyFromText(9011);
            this.txtHotKey12.RegisterHotkeyFromText(9012);
        }

        #endregion

        #region//初始化数据表

        private void InitSocketDGV()
        {
            try
            {
                dgvSocketList.AutoGenerateColumns = false;
                dgvSocketList.DataSource = Socket_Cache.SocketList.lstRecPacket;
                dgvSocketList.BackgroundColor = System.Drawing.Color.FromArgb(248, 248, 248);
                dgvSocketList.Paint += this.dgvSocketList_Paint;
                dgvSocketList.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dgvSocketList, true, null);

                dgvFilterList.AutoGenerateColumns = false;
                dgvFilterList.DataSource = Socket_Cache.FilterList.lstFilter;
                dgvFilterList.AccessibleName = UiText("Main_Filter");
                dgvFilterList.AccessibleRole = AccessibleRole.Table;
                dgvFilterList.Paint += this.dgvFilterList_Paint;
                dgvFilterList.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dgvFilterList, true, null);

                dgvSendList.AutoGenerateColumns = false;
                dgvSendList.DataSource = Socket_Cache.SendList.lstSend;
                dgvSendList.AccessibleRole = AccessibleRole.Table;
                dgvSendList.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dgvSendList, true, null);
                this.RefreshSendFolderView();

                dgvRobotList.AutoGenerateColumns = false;
                dgvRobotList.DataSource = Socket_Cache.RobotList.lstRobot;
                dgvRobotList.AccessibleRole = AccessibleRole.Table;
                dgvRobotList.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dgvRobotList, true, null);
                this.RefreshAssistantFolders();

                dgvLogList.AutoGenerateColumns = false;
                dgvLogList.DataSource = Socket_Cache.LogList.lstSocketLog;
                dgvLogList.AccessibleRole = AccessibleRole.Table;
                dgvLogList.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dgvLogList, true, null);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        

        #region//加载本界面的运行配置

        private void LoadConfigs_Parameter()
        {
            try
            {
                Socket_Cache.System.LoadRunConfig_FromDB();

                this.cbHookWS1_Send.Checked = Socket_Cache.SocketPacket.HookWS1_Send;
                this.cbHookWS1_SendTo.Checked = Socket_Cache.SocketPacket.HookWS1_SendTo;
                this.cbHookWS1_Recv.Checked = Socket_Cache.SocketPacket.HookWS1_Recv;
                this.cbHookWS1_RecvFrom.Checked = Socket_Cache.SocketPacket.HookWS1_RecvFrom;
                this.cbHookWS2_Send.Checked = Socket_Cache.SocketPacket.HookWS2_Send;
                this.cbHookWS2_SendTo.Checked = Socket_Cache.SocketPacket.HookWS2_SendTo;
                this.cbHookWS2_Recv.Checked = Socket_Cache.SocketPacket.HookWS2_Recv;
                this.cbHookWS2_RecvFrom.Checked = Socket_Cache.SocketPacket.HookWS2_RecvFrom;
                this.cbHookWSA_Send.Checked = Socket_Cache.SocketPacket.HookWSA_Send;
                this.cbHookWSA_SendTo.Checked = Socket_Cache.SocketPacket.HookWSA_SendTo;
                this.cbHookWSA_Recv.Checked = Socket_Cache.SocketPacket.HookWSA_Recv;
                this.cbHookWSA_RecvFrom.Checked = Socket_Cache.SocketPacket.HookWSA_RecvFrom;

                this.txtHotKey1.Text = Socket_Cache.SocketPacket.HotKey1;
                this.txtHotKey2.Text = Socket_Cache.SocketPacket.HotKey2;
                this.txtHotKey3.Text = Socket_Cache.SocketPacket.HotKey3;
                this.txtHotKey4.Text = Socket_Cache.SocketPacket.HotKey4;
                this.txtHotKey5.Text = Socket_Cache.SocketPacket.HotKey5;
                this.txtHotKey6.Text = Socket_Cache.SocketPacket.HotKey6;
                this.txtHotKey7.Text = Socket_Cache.SocketPacket.HotKey7;
                this.txtHotKey8.Text = Socket_Cache.SocketPacket.HotKey8;
                this.txtHotKey9.Text = Socket_Cache.SocketPacket.HotKey9;
                this.txtHotKey10.Text = Socket_Cache.SocketPacket.HotKey10;
                this.txtHotKey11.Text = Socket_Cache.SocketPacket.HotKey11;
                this.txtHotKey12.Text = Socket_Cache.SocketPacket.HotKey12;

                if (Socket_Cache.SocketPacket.CheckNotShow)
                {
                    this.rbFilter_NotShow.Checked = true;
                }
                else
                {
                    this.rbFilter_Show.Checked = true;
                }

                this.cbCheckSocket.Checked = Socket_Cache.SocketPacket.CheckSocket;
                this.cbCheckIP.Checked = Socket_Cache.SocketPacket.CheckIP;
                this.cbCheckPort.Checked = Socket_Cache.SocketPacket.CheckPort;
                this.cbCheckHead.Checked = Socket_Cache.SocketPacket.CheckHead;
                this.cbCheckData.Checked = Socket_Cache.SocketPacket.CheckData;
                this.cbCheckSize.Checked = Socket_Cache.SocketPacket.CheckSize;

                this.txtCheckSocket.Text = Socket_Cache.SocketPacket.CheckSocket_Value;
                this.txtCheckLength.Text = Socket_Cache.SocketPacket.CheckLength_Value;
                this.txtCheckIP.Text = Socket_Cache.SocketPacket.CheckIP_Value;
                this.txtCheckPort.Text = Socket_Cache.SocketPacket.CheckPort_Value;
                this.txtCheckHead.Text = Socket_Cache.SocketPacket.CheckHead_Value;
                this.txtCheckData.Text = Socket_Cache.SocketPacket.CheckData_Value;

                this.cbSocketList_AutoRoll.Checked = Socket_Cache.SocketList.AutoRoll;
                this.cbSocketList_AutoClear.Checked = Socket_Cache.SocketList.AutoClear;
                this.nudSocketList_AutoClearValue.Value = Socket_Cache.SocketList.AutoClear_Value;
                this.SocketList_AutoClearChange();

                this.cbLogList_AutoRoll.Checked = Socket_Cache.LogList.Socket_AutoRoll;
                this.cbLogList_AutoClear.Checked = Socket_Cache.LogList.Socket_AutoClear;
                this.nudLogList_AutoClearValue.Value = Socket_Cache.LogList.Socket_AutoClear_Value;
                this.LogList_AutoClearChange();

                this.cbWorkingMode_Speed.Checked = Socket_Cache.SocketPacket.SpeedMode;

                switch (Socket_Cache.System.ListExecute)
                {
                    case Socket_Cache.System.Execute.Together:
                        this.rbListExecute_Together.Checked = true;
                        break;

                    case Socket_Cache.System.Execute.Sequence:
                        this.rbListExecute_Sequence.Checked = true;
                        break;
                }

                switch (Socket_Cache.Filter.FilterExecute)
                {
                    case Socket_Cache.Filter.Execute.Priority:
                        this.rbFilterSet_Priority.Checked = true;
                        break;

                    case Socket_Cache.Filter.Execute.Sequence:
                        this.rbFilterSet_Sequence.Checked = true;
                        break;
                }

                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_35));
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//保存本页面运行配置

        private void SaveConfigs_Parameter()
        {
            try
            {
                Socket_Cache.SocketPacket.HookWS1_Send = cbHookWS1_Send.Checked;
                Socket_Cache.SocketPacket.HookWS1_SendTo = cbHookWS1_SendTo.Checked;
                Socket_Cache.SocketPacket.HookWS1_Recv = cbHookWS1_Recv.Checked;
                Socket_Cache.SocketPacket.HookWS1_RecvFrom = cbHookWS1_RecvFrom.Checked;
                Socket_Cache.SocketPacket.HookWS2_Send = cbHookWS2_Send.Checked;
                Socket_Cache.SocketPacket.HookWS2_SendTo = cbHookWS2_SendTo.Checked;
                Socket_Cache.SocketPacket.HookWS2_Recv = cbHookWS2_Recv.Checked;
                Socket_Cache.SocketPacket.HookWS2_RecvFrom = cbHookWS2_RecvFrom.Checked;
                Socket_Cache.SocketPacket.HookWSA_Send = cbHookWSA_Send.Checked;
                Socket_Cache.SocketPacket.HookWSA_SendTo = cbHookWSA_SendTo.Checked;
                Socket_Cache.SocketPacket.HookWSA_Recv = cbHookWSA_Recv.Checked;
                Socket_Cache.SocketPacket.HookWSA_RecvFrom = cbHookWSA_RecvFrom.Checked;

                Socket_Cache.SocketPacket.HotKey1 = this.txtHotKey1.Text.Trim();
                Socket_Cache.SocketPacket.HotKey2 = this.txtHotKey2.Text.Trim();
                Socket_Cache.SocketPacket.HotKey3 = this.txtHotKey3.Text.Trim();
                Socket_Cache.SocketPacket.HotKey4 = this.txtHotKey4.Text.Trim();
                Socket_Cache.SocketPacket.HotKey5 = this.txtHotKey5.Text.Trim();
                Socket_Cache.SocketPacket.HotKey6 = this.txtHotKey6.Text.Trim();
                Socket_Cache.SocketPacket.HotKey7 = this.txtHotKey7.Text.Trim();
                Socket_Cache.SocketPacket.HotKey8 = this.txtHotKey8.Text.Trim();
                Socket_Cache.SocketPacket.HotKey9 = this.txtHotKey9.Text.Trim();
                Socket_Cache.SocketPacket.HotKey10 = this.txtHotKey10.Text.Trim();
                Socket_Cache.SocketPacket.HotKey11 = this.txtHotKey11.Text.Trim();
                Socket_Cache.SocketPacket.HotKey12 = this.txtHotKey12.Text.Trim();

                Socket_Cache.SocketPacket.CheckNotShow = rbFilter_NotShow.Checked;
                Socket_Cache.SocketPacket.CheckSocket = cbCheckSocket.Checked;
                Socket_Cache.SocketPacket.CheckIP = cbCheckIP.Checked;
                Socket_Cache.SocketPacket.CheckPort = cbCheckPort.Checked;
                Socket_Cache.SocketPacket.CheckHead = cbCheckHead.Checked;
                Socket_Cache.SocketPacket.CheckData = cbCheckData.Checked;
                Socket_Cache.SocketPacket.CheckSize = cbCheckSize.Checked;

                Socket_Cache.SocketPacket.CheckSocket_Value = this.txtCheckSocket.Text.Trim();
                Socket_Cache.SocketPacket.CheckLength_Value = this.txtCheckLength.Text.Trim();
                Socket_Cache.SocketPacket.CheckIP_Value = this.txtCheckIP.Text.Trim();
                Socket_Cache.SocketPacket.CheckPort_Value = this.txtCheckPort.Text.Trim();
                Socket_Cache.SocketPacket.CheckHead_Value = this.txtCheckHead.Text.Trim();
                Socket_Cache.SocketPacket.CheckData_Value = this.txtCheckData.Text.Trim();

                Socket_Cache.SocketList.AutoRoll = this.cbSocketList_AutoRoll.Checked;
                Socket_Cache.SocketList.AutoClear = this.cbSocketList_AutoClear.Checked;
                Socket_Cache.SocketList.AutoClear_Value = this.nudSocketList_AutoClearValue.Value;

                Socket_Cache.LogList.Socket_AutoRoll = this.cbLogList_AutoRoll.Checked;
                Socket_Cache.LogList.Socket_AutoClear = this.cbLogList_AutoClear.Checked;
                Socket_Cache.LogList.Socket_AutoClear_Value = this.nudLogList_AutoClearValue.Value;

                Socket_Cache.SocketPacket.SpeedMode = this.cbWorkingMode_Speed.Checked;

                if (this.rbListExecute_Together.Checked)
                {
                    Socket_Cache.System.ListExecute = Socket_Cache.System.Execute.Together;
                }
                else
                {
                    Socket_Cache.System.ListExecute = Socket_Cache.System.Execute.Sequence;
                }

                if (this.rbFilterSet_Priority.Checked)
                {
                    Socket_Cache.Filter.FilterExecute = Socket_Cache.Filter.Execute.Priority;
                }
                else
                {
                    Socket_Cache.Filter.FilterExecute = Socket_Cache.Filter.Execute.Sequence;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        

        #region//检测过滤参数输入

        private void txtCheckSocket_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!Socket_Operation.CheckTextInput_IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void txtCheckLength_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!Socket_Operation.CheckTextInput_IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void txtCheckPacket_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!Socket_Operation.CheckTextInput_IsHex(e.KeyChar))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void txtCheckHead_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!Socket_Operation.CheckTextInput_IsHex(e.KeyChar))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void txtCheckIP_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!Socket_Operation.CheckTextInput_IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void txtCheckPort_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!Socket_Operation.CheckTextInput_IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//列表设置

        private void cbSocketList_AutoClear_CheckedChanged(object sender, EventArgs e)
        {
            this.SocketList_AutoClearChange();
        }

        private void SocketList_AutoClearChange()
        {
            try
            {
                if (this.cbSocketList_AutoClear.Checked)
                {
                    this.nudSocketList_AutoClearValue.Enabled = true;
                }
                else
                {
                    this.nudSocketList_AutoClearValue.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void cbLogList_AutoClear_CheckedChanged(object sender, EventArgs e)
        {
            this.LogList_AutoClearChange();
        }

        private void LogList_AutoClearChange()
        {
            try
            {
                if (this.cbLogList_AutoClear.Checked)
                {
                    this.nudLogList_AutoClearValue.Enabled = true;
                }
                else
                {
                    this.nudLogList_AutoClearValue.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void AutoScrollDataGridView(DataGridView dgv, bool autoScroll)
        {
            if (autoScroll && !dgv.IsDisposed)
            {
                if (dgv.InvokeRequired)
                {
                    dgv.Invoke(new Action(() =>
                    {
                        if (dgv.Rows.Count > 0 && dgv.Height > dgv.RowTemplate.Height)
                        {
                            dgv.FirstDisplayedScrollingRowIndex = dgv.RowCount - 1;
                        }
                    }));
                }
                else
                {
                    if (dgv.Rows.Count > 0 && dgv.Height > dgv.RowTemplate.Height)
                    {
                        dgv.FirstDisplayedScrollingRowIndex = dgv.RowCount - 1;
                    }
                }
            }
        }

        #endregion        

        #region//快捷键

        private void bHotKey1_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey1.RegisterHotkeyFromText(9001))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey1.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey2_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey2.RegisterHotkeyFromText(9002))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey2.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey3_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey3.RegisterHotkeyFromText(9003))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey3.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey4_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey4.RegisterHotkeyFromText(9004))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey4.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey5_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey5.RegisterHotkeyFromText(9005))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey5.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey6_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey6.RegisterHotkeyFromText(9006))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey6.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey7_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey7.RegisterHotkeyFromText(9007))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey7.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey8_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey8.RegisterHotkeyFromText(9008))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey8.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey9_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey9.RegisterHotkeyFromText(9009))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey9.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey10_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey10.RegisterHotkeyFromText(9010))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey10.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey11_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey11.RegisterHotkeyFromText(9011))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey11.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        private void bHotKey12_Click(object sender, EventArgs e)
        {
            if (this.txtHotKey12.RegisterHotkeyFromText(9012))
            {
                string Msg = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_133), this.txtHotKey12.Text.Trim());
                Socket_Operation.ShowMessageBox(Msg);
            }
        }

        #endregion

        #region//系统设置

        private void cbTopMost_CheckedChanged(object sender, EventArgs e)
        {
            this.TopMostCheckedChanged();
        }

        private void TopMostCheckedChanged()
        {
            this.TopMost = this.cbTopMost.Checked;
        }

        private void cbWorkingMode_Speed_CheckedChanged(object sender, EventArgs e)
        {
            Socket_Cache.SocketPacket.SpeedMode = this.cbWorkingMode_Speed.Checked;
        }

        private void rbListExecute_Together_CheckedChanged(object sender, EventArgs e)
        {
            this.ListExecute_Changed();
        }

        private void ListExecute_Changed()
        {
            if (this.rbListExecute_Together.Checked)
            {
                Socket_Cache.System.ListExecute = Socket_Cache.System.Execute.Together;
            }
            else
            {
                Socket_Cache.System.ListExecute = Socket_Cache.System.Execute.Sequence;
            }
        }

        private void InitFilterActionColor()
        {
            try
            {
                this.lFAColor_Replace.ForeColor = Socket_Cache.Filter.FilterActionForeColor_Replace;
                this.lFAColor_Replace.BackColor = Socket_Cache.Filter.FilterActionBackColor_Replace;

                this.lFAColor_Intercept.ForeColor = Socket_Cache.Filter.FilterActionForeColor_Intercept;
                this.lFAColor_Intercept.BackColor = Socket_Cache.Filter.FilterActionBackColor_Intercept;

                this.lFAColor_Change.ForeColor = Socket_Cache.Filter.FilterActionForeColor_Change;
                this.lFAColor_Change.BackColor = Socket_Cache.Filter.FilterActionBackColor_Change;

                this.lFAColor_Other.ForeColor = Socket_Cache.Filter.FilterActionForeColor_Other;
                this.lFAColor_Other.BackColor = Socket_Cache.Filter.FilterActionBackColor_Other;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//系统备份

        private void bBackUp_Export_Click(object sender, EventArgs e)
        {
            try
            {
                string FileName = Socket_Operation.AssemblyVersion;
                bool SystemConfig = this.cbBackUp_SystemConfig.Checked;
                bool ProxySet = this.cbBackUp_ProxySet.Checked;
                bool ProxyAccount = this.cbBackUp_ProxyAccount.Checked;
                bool ProxyMapping = this.cbBackUp_ProxyMapping.Checked;
                bool InjectionSet = this.cbBackUp_InjectionSet.Checked;
                bool FilterList = this.cbBackUp_FilterList.Checked;
                bool SendList = this.cbBackUp_SendList.Checked;
                bool RobotList = this.cbBackUp_RobotList.Checked;

                Socket_Cache.System.ExportSystemBackUp_Dialog(
                    FileName,
                    SystemConfig,
                    ProxySet,
                    ProxyAccount,
                    ProxyMapping,
                    InjectionSet,
                    FilterList,
                    SendList,
                    RobotList);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//清空数据

        private void bCleanUp_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                this,
                UiText("UI_ClearCaptureConfirm"),
                UiText("UI_ClearCaptureTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                return;
            }

            this.CleanUp_MainForm();
        }

        private void CleanUp_MainForm()
        {
            this.CleanUp_SocketInfo();
            this.CleanUp_SocketList();
            this.CleanUp_HexBox();
            this.CleanUp_LogList();
        }

        private void CleanUp_SocketInfo()
        {
            try
            {
                Socket_Cache.SocketPacket.TotalPackets = 0;
                Socket_Cache.SocketPacket.Total_SendBytes = 0;
                Socket_Cache.SocketPacket.Total_RecvBytes = 0;
                Socket_Cache.Filter.FilterExecute_CNT = 0;

                Socket_Cache.SocketQueue.FilterSocketList_CNT = 0;
                Socket_Cache.SocketQueue.Send_CNT = 0;
                Socket_Cache.SocketQueue.Recv_CNT = 0;
                Socket_Cache.SocketQueue.SendTo_CNT = 0;
                Socket_Cache.SocketQueue.RecvFrom_CNT = 0;
                Socket_Cache.SocketQueue.WSASend_CNT = 0;
                Socket_Cache.SocketQueue.WSARecv_CNT = 0;
                Socket_Cache.SocketQueue.WSASendTo_CNT = 0;
                Socket_Cache.SocketQueue.WSARecvFrom_CNT = 0;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void CleanUp_SocketList()
        {
            try
            {
                Socket_Cache.SocketQueue.ResetSocketQueue();
                Socket_Cache.SocketList.lstRecPacket.Clear();
                Socket_Cache.SocketList.spiSelect = null;
                this.dgvSocketList.Rows.Clear();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void CleanUp_LogList()
        {
            try
            {
                Socket_Cache.LogQueue.ResetLogQueue(Socket_Cache.System.LogType.Socket);
                Socket_Cache.LogList.ResetLogList(Socket_Cache.System.LogType.Socket);
                this.dgvLogList.Rows.Clear();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void CleanUp_HexBox()
        {
            this.ReleasePacketDataEditor();
        }

        private void ReleasePacketDataEditor()
        {
            IByteProvider provider = hbPacketData.ByteProvider;
            if (provider != null)
            {
                provider.Changed -= this.PacketDataProvider_Changed;
                provider.LengthChanged -= this.PacketDataProvider_Changed;
            }

            if (hbPacketData.ByteProvider != null)
            {
                IDisposable byteProvider = hbPacketData.ByteProvider as IDisposable;

                if (byteProvider != null)
                {
                    byteProvider.Dispose();
                }

                hbPacketData.ByteProvider = null;
            }

            this.packetDataEditingPacket = null;
            this.byteSweepEditingPreset = null;
            this.ClearReadableContent();
        }        

        private void AutoCleanUp_SocketList()
        {
            try
            {
                if (this.cbSocketList_AutoClear.Checked && !this.dgvSocketList.IsDisposed)
                {
                    decimal dClearCount = this.nudSocketList_AutoClearValue.Value;

                    if (dClearCount > 0)
                    {
                        if (this.dgvSocketList.Rows.Count > dClearCount)
                        {
                            this.CleanUp_SocketList();
                            this.CleanUp_HexBox();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void AutoCleanUp_LogList()
        {
            try
            {
                if (this.cbLogList_AutoClear.Checked && !this.dgvLogList.IsDisposed)
                {
                    decimal dClearCount = this.nudLogList_AutoClearValue.Value;

                    if (dClearCount > 0)
                    {
                        if (this.dgvLogList.Rows.Count > dClearCount)
                        {
                            this.CleanUp_LogList();
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

        #region//开始拦截

        private void bStartHook_Click(object sender, EventArgs e)
        {
            this.StartHook_MainForm();
        }

        private void StartHook_MainForm()
        {
            try
            {
                this.SetHookUiState("UI_HookStatusStarting", false, true);

                this.SaveConfigs_Parameter();
                Socket_Cache.FilterList.InitFilterList_Count();

                HookStartResult hookResult = ws.StartHook();
                if (!hookResult.Success)
                {
                    this.SetHookUiState("UI_HookStatusFailed", false, false);
                    Socket_Operation.DoLog(
                        MethodBase.GetCurrentMethod().Name,
                        string.Format(
                            "Hook start failed at {0}: {1}",
                            hookResult.FailedHook,
                            hookResult.ErrorMessage));
                    return;
                }

                this.SetHookUiState("UI_HookStatusListening", true, false);

                if (this.cbWorkingMode_Speed.Checked)
                {
                    this.CleanUp_MainForm();
                    Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_41));
                }

                if (bWakeUp)
                {
                    RemoteHooking.WakeUpProcess();
                    this.bWakeUp = false;
                }

                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_39));
            }
            catch (Exception ex)
            {
                this.SetHookUiState("UI_HookStatusFailed", false, false);
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void SetHookUiState(string statusKey, bool isRunning, bool isTransitioning)
        {
            this.hookStatusKey = statusKey;
            this.hookUiTransitioning = isTransitioning;
            bool editable = !isRunning && !isTransitioning;
            this.tcSocketInfo_FilterSet.Enabled = editable;
            this.tcSocketInfo_HookSet.Enabled = editable;
            this.gbSystemSet_FilterSet.Enabled = editable;
            this.bStartHook.Enabled = editable;
            this.bStopHook.Enabled = isRunning && !isTransitioning;
            this.cmsIcon_StartHook.Enabled = editable;
            this.cmsIcon_StopHook.Enabled = isRunning && !isTransitioning;
            this.tsslProcessInfo.Text = UiText(statusKey);
            this.tsslProcessInfo.ForeColor =
                statusKey == "UI_HookStatusFailed"
                    ? Color.Firebrick
                    : statusKey == "UI_HookStatusListening"
                        ? Color.ForestGreen
                        : SystemColors.ControlText;
        }

        private void SynchronizeHookUiState()
        {
            if (this.hookUiTransitioning)
            {
                return;
            }

            string expectedStatusKey = this.ws.IsRunning
                ? "UI_HookStatusListening"
                : "UI_HookStatusReady";

            if (!string.Equals(this.hookStatusKey, expectedStatusKey, StringComparison.Ordinal))
            {
                this.SetHookUiState(expectedStatusKey, this.ws.IsRunning, false);
            }
        }

        #endregion

        #region//结束拦截

        private void bStopHook_Click(object sender, EventArgs e)
        {
            this.StopHook_MainForm();
        }

        private void StopHook_MainForm()
        {
            try
            {
                this.SetHookUiState("UI_HookStatusStopping", false, true);

                ws.StopHook();

                this.SetHookUiState("UI_HookStatusReady", false, false);

                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_40));
            }
            catch (Exception ex)
            {
                this.SetHookUiState("UI_HookStatusFailed", false, false);
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//计时器

        private void UpdateCompactTrafficStatus()
        {
            this.tsslTotalBytes.Text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                UiText("UI_TrafficStatus"),
                Socket_Operation.GetDisplayBytes(Socket_Cache.SocketPacket.Total_SendBytes),
                Socket_Operation.GetDisplayBytes(Socket_Cache.SocketPacket.Total_RecvBytes));
        }

        private void tSocketInfo_Tick(object sender, EventArgs e)
        {
            try
            {
                this.SynchronizeHookUiState();
                this.tlTotal_CNT.Text = Socket_Cache.SocketPacket.TotalPackets.ToString();
                this.tlFilterExecute_CNT.Text = Socket_Cache.Filter.FilterExecute_CNT.ToString();
                long droppedCount = Socket_Cache.SocketQueue.Dropped_CNT;
                this.tlQueue_CNT.Text = droppedCount > 0
                    ? string.Format(UiText("UI_QueueDropped"), Socket_Cache.SocketQueue.qSocket_PacketInfo.Count, droppedCount)
                    : Socket_Cache.SocketQueue.qSocket_PacketInfo.Count.ToString();
                this.tlFilterSocketList_CNT.Text = Socket_Cache.SocketQueue.FilterSocketList_CNT.ToString();
                this.tlSend_CNT.Text = Socket_Cache.SocketQueue.Send_CNT.ToString();
                this.tlRecv_CNT.Text = Socket_Cache.SocketQueue.Recv_CNT.ToString();
                this.tlSendTo_CNT.Text = Socket_Cache.SocketQueue.SendTo_CNT.ToString();
                this.tlRecvFrom_CNT.Text = Socket_Cache.SocketQueue.RecvFrom_CNT.ToString();
                this.tlWSASend_CNT.Text = Socket_Cache.SocketQueue.WSASend_CNT.ToString();
                this.tlWSARecv_CNT.Text = Socket_Cache.SocketQueue.WSARecv_CNT.ToString();
                this.tlWSASendTo_CNT.Text = Socket_Cache.SocketQueue.WSASendTo_CNT.ToString();
                this.tlWSARecvFrom_CNT.Text = Socket_Cache.SocketQueue.WSARecvFrom_CNT.ToString();

                Socket_Cache.SocketPacket.SocketBytesInfo = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_31), Socket_Operation.GetDisplayBytes(Socket_Cache.SocketPacket.Total_SendBytes), Socket_Operation.GetDisplayBytes(Socket_Cache.SocketPacket.Total_RecvBytes));
                this.UpdateCompactTrafficStatus();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private async void tSocketList_Tick(object sender, EventArgs e)
        {
            if (this.socketListTickRunning)
            {
                return;
            }

            this.socketListTickRunning = true;
            try
            {
                if (Socket_Cache.SocketQueue.qSocket_PacketInfo.Count > 0)
                {
                    await Socket_Cache.SocketList.SocketToList(200);
                    this.AutoScrollDataGridView(dgvSocketList, cbSocketList_AutoRoll.Checked);
                    this.AutoCleanUp_SocketList();
                }

                if (Socket_Cache.LogQueue.qSocket_Log.Count > 0)
                {
                    Socket_Cache.LogList.LogToList(Socket_Cache.System.LogType.Socket);
                    this.AutoScrollDataGridView(dgvLogList, cbLogList_AutoRoll.Checked);
                    this.AutoCleanUp_LogList();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(tSocketList_Tick), ex.Message);
            }
            finally
            {
                this.socketListTickRunning = false;
            }
        }

        #endregion

        #region//显示封包列表（异步）

        private void dgvSocketList_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            try
            {
                if (e.ColumnIndex == dgvSocketList.Columns["cTypeImg"].Index)
                {
                    e.Value = Socket_Cache.SocketPacket.GetImg_ByPacketType((Socket_Cache.SocketPacket.PacketType)dgvSocketList.Rows[e.RowIndex].Cells["cPacketType"].Value);
                    e.FormattingApplied = true;
                }
                else if (e.ColumnIndex == dgvSocketList.Columns["cPacketType"].Index)
                {
                    Socket_Cache.SocketPacket.PacketType packetType =
                        (Socket_Cache.SocketPacket.PacketType)dgvSocketList.Rows[e.RowIndex].Cells["cPacketType"].Value;
                    bool isSend =
                        packetType == Socket_Cache.SocketPacket.PacketType.WS1_Send ||
                        packetType == Socket_Cache.SocketPacket.PacketType.WS2_Send ||
                        packetType == Socket_Cache.SocketPacket.PacketType.WS1_SendTo ||
                        packetType == Socket_Cache.SocketPacket.PacketType.WS2_SendTo ||
                        packetType == Socket_Cache.SocketPacket.PacketType.WSASend ||
                        packetType == Socket_Cache.SocketPacket.PacketType.WSASendTo;
                    System.Drawing.Color packetTypeColor = isSend
                        ? PacketSendAccentColor
                        : PacketReceiveAccentColor;

                    e.Value = Socket_Cache.SocketPacket.GetName_ByPacketType(packetType);
                    e.CellStyle.ForeColor = packetTypeColor;
                    e.CellStyle.SelectionBackColor = packetTypeColor;
                    e.CellStyle.SelectionForeColor = PacketSelectionTextColor;
                    e.FormattingApplied = true;
                }
                else if (e.ColumnIndex == dgvSocketList.Columns["cPacketID"].Index)
                {
                    e.Value = (e.RowIndex + 1).ToString();
                    e.FormattingApplied = true;
                }
                else if (e.ColumnIndex == dgvSocketList.Columns["cData"].Index)
                {
                    switch (Socket_Cache.SocketList.lstRecPacket[e.RowIndex].FilterAction)
                    {
                        case Socket_Cache.Filter.FilterAction.Replace:
                            this.dgvSocketList.Rows[e.RowIndex].DefaultCellStyle.ForeColor = Socket_Cache.Filter.FilterActionForeColor_Replace;
                            this.dgvSocketList.Rows[e.RowIndex].DefaultCellStyle.BackColor = Socket_Cache.Filter.FilterActionBackColor_Replace;
                            break;

                        case Socket_Cache.Filter.FilterAction.Intercept:
                            this.dgvSocketList.Rows[e.RowIndex].DefaultCellStyle.ForeColor = Socket_Cache.Filter.FilterActionForeColor_Intercept;
                            this.dgvSocketList.Rows[e.RowIndex].DefaultCellStyle.BackColor = Socket_Cache.Filter.FilterActionBackColor_Intercept;
                            break;

                        case Socket_Cache.Filter.FilterAction.Change:
                            this.dgvSocketList.Rows[e.RowIndex].DefaultCellStyle.ForeColor = Socket_Cache.Filter.FilterActionForeColor_Change;
                            this.dgvSocketList.Rows[e.RowIndex].DefaultCellStyle.BackColor = Socket_Cache.Filter.FilterActionBackColor_Change;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//显示日志列表（异步）

        private void dgvLogList_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            try
            {
                if (e.ColumnIndex == dgvLogList.Columns["cLogID"].Index)
                {
                    e.Value = (e.RowIndex + 1).ToString();
                    e.FormattingApplied = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//显示滤镜列表（异步）        

        private void dgvFilterList_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (dgvFilterList.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn && e.RowIndex >= 0)
                {
                    int FIndex = e.RowIndex;
                    bool bCheck = !bool.Parse(dgvFilterList.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString());

                    dgvFilterList.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = bCheck;
                    Socket_Cache.FilterList.lstFilter[FIndex].IsEnable = bCheck;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void dgvFilterList_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                int iSelectIndex = this.dgvFilterList.SelectedRows[0].Index;

                if (iSelectIndex >= 0 && iSelectIndex < Socket_Cache.FilterList.lstFilter.Count)
                {
                    Socket_Operation.ShowFilterForm_Dialog(Socket_Cache.FilterList.lstFilter[iSelectIndex]);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//显示发送列表（异步）        

        private async void dgvSendList_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0)
                {
                    return;
                }

                DataGridViewColumn clickedColumn = dgvSendList.Columns[e.ColumnIndex];
                Socket_SendInfo sendInfo = dgvSendList.Rows[e.RowIndex].DataBoundItem as Socket_SendInfo;

                if (clickedColumn is DataGridViewCheckBoxColumn)
                {
                    bool bCheck = !bool.Parse(dgvSendList.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString());

                    dgvSendList.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = bCheck;
                    if (sendInfo != null)
                    {
                        sendInfo.IsEnable = bCheck;
                        this.UpdateSendListSelectAllState();
                        this.UpdateSendListContext();
                    }
                }
                else if (clickedColumn.Name == "cSendNow" && sendInfo != null)
                {
                    if (sendInfo.SCollection == null || sendInfo.SCollection.Count == 0)
                    {
                        MessageBox.Show(this, "这个封包没有可发送的数据。", "发送",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    if (!this.EnsureCurrentSystemSocket(new[] { sendInfo }))
                    {
                        return;
                    }

                    if (this.bgwSendList.IsBusy)
                    {
                        MessageBox.Show(this, "批量发送正在进行，请先停止批量发送。",
                            "发送", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    Socket_Send activeSend;
                    if (this.TryGetActiveSendOperation(sendInfo.SID, out activeSend))
                    {
                        return;
                    }

                    Socket_Send manualSend = await Socket_Cache.Send.DoSendAsync(sendInfo.SID);
                    if (manualSend != null)
                    {
                        this.TrackManualSend(sendInfo.SID, manualSend);
                    }
                }
                else if (clickedColumn.Name == "cStopNow" && sendInfo != null)
                {
                    Socket_Send activeSend;
                    if (this.TryGetActiveSendOperation(sendInfo.SID, out activeSend))
                    {
                        activeSend.StopSend();
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void TrackManualSend(Guid sendListId, Socket_Send manualSend)
        {
            this.manualSendOperations[sendListId] = manualSend;
            this.InvalidateManualSendRow(sendListId);
            this.UpdateSendExecutionControls();

            RunWorkerCompletedEventHandler completedHandler = null;
            completedHandler = delegate
            {
                manualSend.Worker.RunWorkerCompleted -= completedHandler;
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke(new Action(delegate
                    {
                        Socket_Send currentSend;
                        if (this.manualSendOperations.TryGetValue(sendListId, out currentSend) &&
                            ReferenceEquals(currentSend, manualSend))
                        {
                            this.manualSendOperations.Remove(sendListId);
                        }
                        this.InvalidateManualSendRow(sendListId);
                        this.UpdateSendExecutionControls();
                    }));
                }
            };

            manualSend.Worker.RunWorkerCompleted += completedHandler;
            if (!manualSend.Worker.IsBusy)
            {
                manualSend.Worker.RunWorkerCompleted -= completedHandler;
                this.manualSendOperations.Remove(sendListId);
                this.InvalidateManualSendRow(sendListId);
                this.UpdateSendExecutionControls();
            }
        }

        private bool TryGetActiveSendOperation(Guid sendListId, out Socket_Send activeSend)
        {
            Socket_Send manualSend;
            if (this.manualSendOperations.TryGetValue(sendListId, out manualSend) &&
                manualSend.Worker.IsBusy)
            {
                activeSend = manualSend;
                return true;
            }

            lock (this.sendOperationSync)
            {
                Socket_Send batchSend;
                if (this.activeBatchSendOperations.TryGetValue(sendListId, out batchSend) &&
                    batchSend != null &&
                    batchSend.Worker.IsBusy)
                {
                    activeSend = batchSend;
                    return true;
                }
            }

            activeSend = null;
            return false;
        }

        private bool IsSendListActive(Guid sendListId)
        {
            Socket_Send activeSend;
            return this.TryGetActiveSendOperation(sendListId, out activeSend);
        }

        private bool IsSendListLockedForModification(Guid sendListId)
        {
            return this.IsSendListActive(sendListId) ||
                (this.bgwSendList.IsBusy &&
                 this.sendBatchQueue.Any(item => item.SID == sendListId));
        }

        private void SetActiveBatchSend(Guid sendListId, Socket_Send batchSend)
        {
            lock (this.sendOperationSync)
            {
                this.activeBatchSendOperations[sendListId] = batchSend;
            }
            this.NotifySendListStateChanged(sendListId);
        }

        private void ClearActiveBatchSend(Guid sendListId, Socket_Send batchSend)
        {
            lock (this.sendOperationSync)
            {
                Socket_Send activeBatchSend;
                if (this.activeBatchSendOperations.TryGetValue(sendListId, out activeBatchSend) &&
                    ReferenceEquals(activeBatchSend, batchSend))
                {
                    this.activeBatchSendOperations.Remove(sendListId);
                }
            }
            this.NotifySendListStateChanged(sendListId);
        }

        private List<Socket_Send> GetActiveBatchSends()
        {
            lock (this.sendOperationSync)
            {
                return this.activeBatchSendOperations.Values
                    .Where(send => send != null)
                    .Distinct()
                    .ToList();
            }
        }

        private void StopActiveBatchSends()
        {
            foreach (Socket_Send batchSend in this.GetActiveBatchSends())
            {
                batchSend.StopSend();
            }
        }

        private void NotifySendListStateChanged(Guid sendListId)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<Guid>(this.NotifySendListStateChanged), sendListId);
                return;
            }

            this.InvalidateManualSendRow(sendListId);
            this.UpdateSendExecutionControls();
        }

        private bool CanModifySendLists(IEnumerable<Socket_SendInfo> sendLists, string actionName)
        {
            if (sendLists != null &&
                sendLists.Any(item => this.IsSendListLockedForModification(item.SID)))
            {
                MessageBox.Show(this, UiText("UI_PacketSendingLocked"),
                    actionName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            return true;
        }

        private void InvalidateManualSendRow(Guid sendListId)
        {
            DataGridViewRow row = this.dgvSendList.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(item =>
                {
                    Socket_SendInfo sendInfo = item.DataBoundItem as Socket_SendInfo;
                    return sendInfo != null && sendInfo.SID == sendListId;
                });
            if (row != null)
            {
                this.dgvSendList.InvalidateRow(row.Index);
            }
        }

        private void dgvSendList_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = this.dgvSendList.Columns[e.ColumnIndex].Name;
            if (columnName == "cLoopCount")
            {
                Socket_SendInfo formattedSendInfo =
                    this.dgvSendList.Rows[e.RowIndex].DataBoundItem as Socket_SendInfo;
                if (formattedSendInfo != null &&
                    formattedSendInfo.SLoopCNT == 0 &&
                    !this.dgvSendList.Rows[e.RowIndex].Cells[e.ColumnIndex].IsInEditMode)
                {
                    e.Value = UiText("UI_ContinuousSend");
                    e.FormattingApplied = true;
                }
                return;
            }

            if (columnName != "cSendNow" && columnName != "cStopNow")
            {
                return;
            }

            Socket_SendInfo sendInfo =
                this.dgvSendList.Rows[e.RowIndex].DataBoundItem as Socket_SendInfo;
            bool isActive = sendInfo != null && this.IsSendListActive(sendInfo.SID);
            if (columnName == "cSendNow")
            {
                e.Value = isActive ? "发送中…" : "发送";
                e.CellStyle.BackColor = System.Drawing.SystemColors.Control;
                e.CellStyle.ForeColor = isActive
                    ? System.Drawing.SystemColors.GrayText
                    : System.Drawing.SystemColors.ControlText;
                e.CellStyle.SelectionBackColor = e.CellStyle.BackColor;
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
            }
            else
            {
                e.Value = "停止";
                e.CellStyle.BackColor = System.Drawing.SystemColors.Control;
                e.CellStyle.ForeColor = isActive
                    ? System.Drawing.SystemColors.ControlText
                    : System.Drawing.SystemColors.GrayText;
                e.CellStyle.SelectionBackColor = e.CellStyle.BackColor;
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
            }
            e.FormattingApplied = true;
        }

        private void dgvSendList_CellToolTipTextNeeded(
            object sender,
            DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = this.dgvSendList.Columns[e.ColumnIndex].Name;
            Socket_SendInfo sendInfo =
                this.dgvSendList.Rows[e.RowIndex].DataBoundItem as Socket_SendInfo;
            if (sendInfo == null)
            {
                return;
            }

            if (columnName == "cName")
            {
                e.ToolTipText = sendInfo.SName + Environment.NewLine +
                    UiText("UI_SendNameTip");
            }
            else if (columnName == "cSendNow")
            {
                e.ToolTipText = this.IsSendListActive(sendInfo.SID)
                    ? UiText("UI_PacketSending")
                    : UiText("UI_SendPacketNow");
            }
            else if (columnName == "cStopNow")
            {
                e.ToolTipText = this.IsSendListActive(sendInfo.SID)
                    ? UiText("UI_StopPacketNow")
                    : UiText("UI_PacketNotSending");
            }
        }

        private void dgvSendList_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            Socket_SendInfo sendInfo =
                this.dgvSendList.Rows[e.RowIndex].DataBoundItem as Socket_SendInfo;
            if (sendInfo != null && this.IsSendListLockedForModification(sendInfo.SID))
            {
                e.Cancel = true;
            }
        }

        private void dgvSendList_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = this.dgvSendList.Columns[e.ColumnIndex].Name;
            if (columnName != "cLoopCount" && columnName != "cLoopInterval")
            {
                return;
            }

            Socket_SendInfo sendInfo =
                this.dgvSendList.Rows[e.RowIndex].DataBoundItem as Socket_SendInfo;
            if (sendInfo != null && this.IsSendListLockedForModification(sendInfo.SID))
            {
                return;
            }

            this.dgvSendList.CurrentCell =
                this.dgvSendList.Rows[e.RowIndex].Cells[e.ColumnIndex];
            this.dgvSendList.BeginEdit(true);
        }

        private void dgvSendList_EditingControlShowing(
            object sender,
            DataGridViewEditingControlShowingEventArgs e)
        {
            if (this.dgvSendList.CurrentCell == null)
            {
                return;
            }

            string columnName =
                this.dgvSendList.Columns[this.dgvSendList.CurrentCell.ColumnIndex].Name;
            if (columnName != "cLoopCount" && columnName != "cLoopInterval")
            {
                return;
            }

            TextBox textBox = e.Control as TextBox;
            if (textBox == null)
            {
                return;
            }

            textBox.BackColor = System.Drawing.SystemColors.Window;
            textBox.ForeColor = System.Drawing.SystemColors.WindowText;
            textBox.TextAlign = HorizontalAlignment.Center;
            this.BeginInvoke(new Action(textBox.SelectAll));
        }

        private void dgvSocketList_Paint(object sender, PaintEventArgs e)
        {
            if (this.dgvSocketList.Rows.Count > 0)
            {
                return;
            }

            System.Drawing.Rectangle contentBounds = this.dgvSocketList.ClientRectangle;
            contentBounds.Y += this.dgvSocketList.ColumnHeadersHeight;
            contentBounds.Height -= this.dgvSocketList.ColumnHeadersHeight;
            TextRenderer.DrawText(
                e.Graphics,
                UiText("UI_PacketListEmpty"),
                this.dgvSocketList.Font,
                contentBounds,
                System.Drawing.SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
        }

        private void dgvFilterList_Paint(object sender, PaintEventArgs e)
        {
            if (this.dgvFilterList.Rows.Count > 0)
            {
                return;
            }

            System.Drawing.Rectangle contentBounds = this.dgvFilterList.ClientRectangle;
            contentBounds.Y += this.dgvFilterList.ColumnHeadersHeight;
            contentBounds.Height -= this.dgvFilterList.ColumnHeadersHeight;
            TextRenderer.DrawText(
                e.Graphics,
                UiText("UI_FilterEmpty"),
                this.dgvFilterList.Font,
                contentBounds,
                System.Drawing.SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
        }

        private void dgvSendList_Paint(object sender, PaintEventArgs e)
        {
            if (this.dgvSendList.Rows.Count > 0)
            {
                return;
            }

            string emptyText = this.HasSelectedSendFolder()
                ? UiText("UI_CurrentGroupEmpty")
                : UiText("UI_CreatePacketGroup");
            System.Drawing.Rectangle contentBounds = this.dgvSendList.ClientRectangle;
            contentBounds.Y += this.dgvSendList.ColumnHeadersHeight;
            contentBounds.Height -= this.dgvSendList.ColumnHeadersHeight;
            TextRenderer.DrawText(
                e.Graphics,
                emptyText,
                this.dgvSendList.Font,
                contentBounds,
                System.Drawing.SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
        }

        private void dgvSendList_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            string columnName = this.dgvSendList.Columns[e.ColumnIndex].Name;
            if (columnName != "cSortOrder" &&
                columnName != "cLoopCount" &&
                columnName != "cLoopInterval")
            {
                return;
            }

            int value;
            int minimum = columnName == "cSortOrder" ? 1 : 0;
            if (!int.TryParse(Convert.ToString(e.FormattedValue), out value) || value < minimum)
            {
                e.Cancel = true;
                if (columnName == "cSortOrder")
                {
                    this.dgvSendList.Rows[e.RowIndex].ErrorText = "序号必须大于 0。";
                }
                else
                {
                    this.dgvSendList.Rows[e.RowIndex].ErrorText =
                        columnName == "cLoopCount"
                            ? "次数不能小于 0；0 表示连续发送。"
                            : "间隔不能小于 0。";
                }
            }
        }

        private void dgvSendList_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                this.dgvSendList.Rows[e.RowIndex].ErrorText = string.Empty;
            }

            if (e.RowIndex >= 0 && this.dgvSendList.Columns[e.ColumnIndex].Name == "cSortOrder")
            {
                Socket_SendInfo movedItem =
                    this.dgvSendList.Rows[e.RowIndex].DataBoundItem as Socket_SendInfo;
                if (movedItem != null)
                {
                    List<Socket_SendInfo> orderedItems = this.GetCurrentFolderSendLists();
                    orderedItems.Remove(movedItem);
                    int targetIndex = Math.Max(0, Math.Min(movedItem.SSortOrder - 1, orderedItems.Count));
                    orderedItems.Insert(targetIndex, movedItem);
                    for (int index = 0; index < orderedItems.Count; index++)
                    {
                        orderedItems[index].SSortOrder = index + 1;
                    }

                    this.BeginInvoke(new Action(this.RefreshSendFolderView));
                }
            }
        }

        private void dgvSendList_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == this.cName.Index)
                {
                    Socket_SendInfo sendInfo = this.dgvSendList.Rows[e.RowIndex].DataBoundItem as Socket_SendInfo;
                    if (sendInfo != null)
                    {
                        if (!this.CanModifySendLists(new[] { sendInfo }, "重命名封包"))
                        {
                            return;
                        }

                        string newName = this.PromptForText(
                            "重命名封包",
                            "封包名称",
                            sendInfo.SName);
                        if (!string.IsNullOrWhiteSpace(newName))
                        {
                            sendInfo.SName = newName.Trim();
                            this.RefreshSendFolderView();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void cmsSendListEdit_Click(object sender, EventArgs e)
        {
            try
            {
                Socket_SendInfo sendInfo = this.dgvSendList.CurrentRow == null
                    ? null
                    : this.dgvSendList.CurrentRow.DataBoundItem as Socket_SendInfo;
                if (sendInfo != null)
                {
                    if (!this.CanModifySendLists(new[] { sendInfo }, "编辑封包内容"))
                    {
                        return;
                    }

                    if (sendInfo.SCollection != null && sendInfo.SCollection.Count == 1)
                    {
                        Socket_Operation.ShowSendForm(sendInfo.SCollection[0]);
                    }
                    else
                    {
                        // 仅为旧数据库中的多封包记录保留兼容入口。
                        Socket_Operation.ShowSendListForm_Dialog(sendInfo);
                    }
                    this.RefreshSendFolderView();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//显示机器人列表（异步）

        private void dgvRobotList_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (dgvRobotList.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn && e.RowIndex >= 0)
                {
                    int RIndex = e.RowIndex;
                    bool bCheck = !bool.Parse(dgvRobotList.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString());

                    dgvRobotList.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = bCheck;
                    Socket_Cache.RobotList.lstRobot[RIndex].IsEnable = bCheck;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void dgvRobotList_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                int iSelectIndex = this.dgvRobotList.SelectedRows[0].Index;

                if (iSelectIndex >= 0 && iSelectIndex < Socket_Cache.RobotList.lstRobot.Count)
                {
                    Socket_Operation.ShowRobotForm_Dialog(Socket_Cache.RobotList.lstRobot[iSelectIndex]);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//搜索封包内容（异步）

        private void bSearch_Click(object sender, EventArgs e)
        {
            Socket_Operation.ShowFindForm();

            if (Socket_Cache.SocketList.DoSearch)
            {
                this.bSearchNext.Focus();
                this.SearchSocketListNext();
            }
        }

        private void bSearchNext_Click(object sender, EventArgs e)
        {
            this.SearchSocketListNext();
        }

        private void HexBox_FindNext()
        {
            try
            {
                if (Socket_Cache.SocketList.FindOptions.IsValid)
                {
                    long res = this.hbPacketData.Find(Socket_Cache.SocketList.FindOptions);

                    if (res == -1)
                    {
                        Socket_Cache.SocketList.Search_Index += 1;
                        this.SearchSocketListNext();
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void SearchSocketListNext()
        {
            if (!this.bgwSearch.IsBusy)
            {
                this.bgwSearch.RunWorkerAsync();
            }
        }

        private void bgwSearch_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            try
            {
                if (dgvSocketList.Rows.Count > 0)
                {
                    if (Socket_Cache.SocketList.FindOptions.IsValid)
                    {
                        byte[] bSearchContent = null;
                        FindType fType = Socket_Cache.SocketList.FindOptions.Type;
                        Socket_Cache.SocketPacket.EncodingFormat efFormat = new Socket_Cache.SocketPacket.EncodingFormat();

                        switch (fType)
                        {
                            case FindType.Text:
                                efFormat = Socket_Cache.SocketPacket.EncodingFormat.UTF7;
                                bSearchContent = Socket_Operation.StringToBytes(efFormat, Socket_Cache.SocketList.FindOptions.Text);
                                break;

                            case FindType.Hex:
                                efFormat = Socket_Cache.SocketPacket.EncodingFormat.Hex;
                                bSearchContent = Socket_Cache.SocketList.FindOptions.Hex;
                                break;
                        }

                        if (rbFromHead.Checked)
                        {
                            this.Invoke(new MethodInvoker(() =>
                            {
                                this.dgvSocketList.ClearSelection();
                                this.rbFromIndex.Checked = true;
                                this.hbPacketData.SelectionStart = 0;
                                Socket_Cache.SocketList.Search_Index = 0;
                            }));
                        }

                        e.Result = Socket_Cache.SocketList.SearchForSocketList(Socket_Cache.SocketList.Search_Index, bSearchContent);
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void bgwSearch_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            try
            {
                if (e.Error == null && !e.Cancelled && e.Result != null)
                {
                    if (int.TryParse(e.Result.ToString(), out int iSearchResultIndex))
                    {
                        if (iSearchResultIndex >= 0)
                        {
                            this.dgvSocketList.Rows[iSearchResultIndex].Selected = true;
                            this.dgvSocketList.CurrentCell = this.dgvSocketList.Rows[iSearchResultIndex].Cells[0];
                            this.dgvSocketList.FirstDisplayedScrollingRowIndex = iSearchResultIndex;

                            this.HexBox_FindNext();
                        }
                        else
                        {
                            Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_23));
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

        #region//显示封包数据        

        private void dgvSocketInfo_SelectionChanged(object sender, EventArgs e)
        {
            this.ShowSelectSocketData();
        }

        private void ShowSelectSocketData()
        {
            try
            {
                if (this.dgvSocketList.SelectedRows.Count > 0)
                {
                    int iSelectIndex = this.dgvSocketList.SelectedRows[0].Index;

                    if (iSelectIndex >= 0 && iSelectIndex < Socket_Cache.SocketList.lstRecPacket.Count)
                    {
                        this.CommitPacketDataEdits();
                        this.ReleasePacketDataEditor();

                        Socket_Cache.SocketList.Search_Index = iSelectIndex;
                        Socket_Cache.SocketList.spiSelect = Socket_Cache.SocketList.lstRecPacket[iSelectIndex];

                        Socket_AnnotatedByteProvider dbp = new Socket_AnnotatedByteProvider(
                            Socket_Cache.SocketList.spiSelect.PacketBuffer,
                            Socket_Cache.SocketList.spiSelect.ByteAnnotations);
                        dbp.Changed += this.PacketDataProvider_Changed;
                        dbp.LengthChanged += this.PacketDataProvider_Changed;
                        hbPacketData.ByteProvider = dbp;
                        this.packetDataEditingPacket = Socket_Cache.SocketList.spiSelect;
                        this.UpdateReadableContent(this.packetDataEditingPacket.PacketBuffer);
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void PacketDataProvider_Changed(object sender, EventArgs e)
        {
            if (this.byteSweepLivePreviewUpdating)
            {
                return;
            }

            this.CommitPacketDataEdits();
        }

        private void CommitPacketDataEdits()
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (provider == null || this.packetDataEditingPacket == null)
            {
                return;
            }

            byte[] editedBuffer = Socket_ByteAnnotationEngine.GetBytes(provider);
            this.packetDataEditingPacket.PacketBuffer = editedBuffer;
            this.packetDataEditingPacket.PacketLen = editedBuffer.Length;
            this.packetDataEditingPacket.PacketData = Socket_Operation.GetPacketData_Hex(
                editedBuffer.AsSpan(),
                Socket_Cache.SocketPacket.PacketData_MaxLen);
            provider.ApplyChanges();
            this.CommitByteSweepPresetBuffer(editedBuffer);
            this.UpdateReadableContent(editedBuffer);

            int rowIndex = Socket_Cache.SocketList.lstRecPacket.IndexOf(this.packetDataEditingPacket);
            if (rowIndex >= 0 && rowIndex < this.dgvSocketList.Rows.Count)
            {
                this.dgvSocketList.InvalidateRow(rowIndex);
            }
        }

        private Socket_PacketInfo GetCurrentEditedPacket()
        {
            this.CommitPacketDataEdits();
            return this.packetDataEditingPacket ?? Socket_Cache.SocketList.spiSelect;
        }

        #endregion        

        #region//显示封包统计（异步）

        private void bPacketStatistics_Click(object sender, EventArgs e)
        {
            if (!this.bgwPacketStatistics.IsBusy)
            {
                int selectedIndex = cbbPacketStatistics.SelectedIndex;
                this.bgwPacketStatistics.RunWorkerAsync(selectedIndex);
            }
        }

        private void bgwPacketStatistics_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                int selectedIndex = (int)e.Argument;

                switch (selectedIndex)
                {
                    case 0:
                        e.Result = Socket_Cache.SocketList.StatisticalSocketList_ByPacketLen();
                        break;

                    case 1:
                        e.Result = Socket_Cache.SocketList.StatisticalSocketList_ByPacketSocket();
                        break;

                    case 2:
                        e.Result = Socket_Cache.SocketList.StatisticalFilterList_ByExecutionCount();
                        break;

                    default:
                        e.Result = null;
                        break;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void bgwPacketStatistics_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                this.dgvPacketStatistics.DataSource = e.Result;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//封包编辑器菜单

        private void cmsHexBox_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.BuildGroupedSendListMenu(this.cmsHexBox_SendList, this.cmsHexBox_SendTarget_Click);
        }

        private void BuildGroupedSendListMenu(ToolStripMenuItem parentMenu, EventHandler targetClick)
        {
            parentMenu.DropDownItems.Clear();
            parentMenu.Text = UiText("UI_AddToPacketGroup");

            if (Socket_Cache.SendList.lstFolders.Count == 0)
            {
                parentMenu.DropDownItems.Add(
                    new ToolStripMenuItem(UiText("UI_CreateGroupFirst")) { Enabled = false });
                return;
            }

            foreach (string folderName in Socket_Cache.SendList.lstFolders)
            {
                ToolStripMenuItem folderItem = new ToolStripMenuItem(folderName)
                {
                    Tag = folderName
                };
                folderItem.Click += targetClick;
                parentMenu.DropDownItems.Add(folderItem);
            }
        }

        private void cmsHexBox_SendTarget_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem targetItem = sender as ToolStripMenuItem;
            string folderName = targetItem == null ? null : targetItem.Tag as string;
            if (string.IsNullOrEmpty(folderName))
            {
                return;
            }

            this.AddCurrentPacketDataToGroup(folderName);
            this.cmsHexBox.Close();
        }

        private void AddCurrentPacketDataToGroup(string folderName)
        {
            if (Socket_Cache.SocketList.spiSelect == null)
            {
                return;
            }

            Socket_PacketInfo packet = Socket_Cache.SocketList.spiSelect;
            byte[] buffer;
            IEnumerable<Socket_ByteAnnotationInfo> annotations;
            if (this.hbPacketData.CanCopy())
            {
                this.hbPacketData.CopyHex();
                buffer = Socket_Operation.StringToBytes(
                    Socket_Cache.SocketPacket.EncodingFormat.Hex,
                    Clipboard.GetText());
                annotations = Socket_ByteAnnotationEngine.ForSelection(
                    packet.ByteAnnotations,
                    this.hbPacketData.SelectionStart,
                    this.hbPacketData.SelectionLength);
            }
            else
            {
                buffer = packet.PacketBuffer;
                annotations = packet.ByteAnnotations;
            }

            this.PromptAndAddPacketPreset(packet, folderName, buffer, annotations);
        }

        private Socket_SendInfo PromptAndAddPacketPreset(
            Socket_PacketInfo packet,
            string suggestedFolder,
            byte[] buffer,
            IEnumerable<Socket_ByteAnnotationInfo> annotations)
        {
            if (packet == null || buffer == null)
            {
                return null;
            }

            string defaultName = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                UiText("UI_DefaultSendPresetName"),
                Socket_Cache.SendList.lstSend.Count + 1);
            using (Socket_SendPresetForm dialog =
                new Socket_SendPresetForm(defaultName, suggestedFolder))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return null;
                }

                string targetFolder = Socket_Cache.SendList.lstFolders.FirstOrDefault(folder =>
                    string.Equals(folder, dialog.FolderName, StringComparison.OrdinalIgnoreCase));
                if (targetFolder == null)
                {
                    Socket_Cache.SendList.AddFolder(dialog.FolderName);
                    targetFolder = dialog.FolderName;
                }

                return this.AddPacketPresetToGroup(
                    packet,
                    targetFolder,
                    buffer,
                    annotations,
                    dialog.PresetName);
            }
        }

        private Socket_SendInfo AddPacketPresetToGroup(
            Socket_PacketInfo packet,
            string folderName,
            byte[] buffer,
            IEnumerable<Socket_ByteAnnotationInfo> annotations,
            string presetName = null)
        {
            if (packet == null || string.IsNullOrEmpty(folderName) || buffer == null)
            {
                return null;
            }

            Socket_PacketInfo packetCopy = new Socket_PacketInfo(
                packet.PacketTime,
                packet.PacketSocket,
                packet.PacketType,
                packet.PacketFrom,
                packet.PacketTo,
                packet.RawBuffer == null ? null : (byte[])packet.RawBuffer.Clone(),
                (byte[])buffer.Clone(),
                buffer.Length,
                packet.FilterAction);
            packetCopy.PacketData = Socket_Operation.GetPacketData_Hex(
                packetCopy.PacketBuffer.AsSpan(),
                Socket_Cache.SocketPacket.PacketData_MaxLen);
            packetCopy.ByteAnnotations = Socket_ByteAnnotationEngine.Clone(annotations);

            if (string.IsNullOrWhiteSpace(presetName))
            {
                presetName = string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    UiText("UI_DefaultSendPresetName"),
                    Socket_Cache.SendList.lstSend.Count + 1);
            }
            Socket_SendInfo sendInfo = Socket_SendForm.CreateSendPreset(
                packetCopy, presetName, folderName, 1, 1000);
            Socket_Cache.SendList.SendToList(sendInfo);
            return sendInfo;
        }

        private void cmsHexBox_tscbSendList_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (Socket_Cache.SocketList.spiSelect != null)
                {
                    if (this.cmsHexBox_tscbSendList.SelectedItem != null)
                    {
                        Socket_Cache.SendList.SendListItem item = this.cmsHexBox_tscbSendList.SelectedItem as Socket_Cache.SendList.SendListItem;
                        if (item == null)
                        {
                            return;
                        }
                        Guid SID = item.SID;
                        BindingList<Socket_PacketInfo> SCollection = Socket_Cache.Send.GetSendCollection_ByGuid(SID);

                        if (SCollection != null)
                        {
                            int iSocket = Socket_Cache.SocketList.spiSelect.PacketSocket;
                            Socket_Cache.SocketPacket.PacketType ptType = Socket_Cache.SocketList.spiSelect.PacketType;
                            string sIPFrom = Socket_Cache.SocketList.spiSelect.PacketFrom;
                            string sIPTo = Socket_Cache.SocketList.spiSelect.PacketTo;

                            byte[] bBuffer = null;

                            if (this.hbPacketData.CanCopy())
                            {
                                this.hbPacketData.CopyHex();
                                bBuffer = Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, Clipboard.GetText());
                            }
                            else
                            {
                                bBuffer = Socket_Cache.SocketList.spiSelect.PacketBuffer;
                            }

                            Socket_Cache.Send.AddSendCollection(SCollection, iSocket, ptType, sIPFrom, sIPTo, bBuffer,
                                Socket_ByteAnnotationEngine.ForSelection(
                                    Socket_Cache.SocketList.spiSelect.ByteAnnotations,
                                    this.hbPacketData.CanCopy() ? this.hbPacketData.SelectionStart : 0,
                                    this.hbPacketData.CanCopy() ? this.hbPacketData.SelectionLength : 0));
                        }

                        this.cmsHexBox.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void cmsHexBox_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            string sItemText = e.ClickedItem.Name;
            this.cmsHexBox.Close();

            try
            {
                if (Socket_Cache.SocketList.spiSelect != null)
                {
                    switch (sItemText)
                    {
                        case "cmsHexBox_Send":

                            Socket_Operation.ShowSendForm(this.GetCurrentEditedPacket());

                            break;

                        case "cmsHexBox_FilterList":

                            if (this.hbPacketData.CanCopy())
                            {
                                this.hbPacketData.CopyHex();

                                byte[] bBuffer = Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, Clipboard.GetText());
                                Socket_Cache.Filter.AddFilter_ByPacketInfo(Socket_Cache.SocketList.spiSelect, bBuffer);
                            }
                            else
                            {
                                Socket_Cache.Filter.AddFilter_ByPacketInfo(Socket_Cache.SocketList.spiSelect, null);
                            }

                            break;

                        case "cmsHexBox_CopyHex":

                            this.hbPacketData.CopyHex();

                            break;

                        case "cmsHexBox_CopyText":

                            this.hbPacketData.Copy();

                            break;

                        case "cmsHexBox_Comparison_A":

                            this.hbPacketData.CopyHex();
                            this.rtbComparison_A.Text = Clipboard.GetText();

                            break;

                        case "cmsHexBox_Comparison_B":

                            this.hbPacketData.CopyHex();
                            this.rtbComparison_B.Text = Clipboard.GetText();

                            break;

                        case "cmsHexBox_SelectAll":

                            this.hbPacketData.SelectAll();

                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//图标菜单

        private void cmsIcon_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            string sItemText = e.ClickedItem.Name;
            cmsIcon.Close();

            try
            {
                switch (sItemText)
                {
                    case "cmsIcon_Show":
                        this.ShowMainForm();
                        break;

                    case "cmsIcon_StartHook":
                        this.StartHook_MainForm();
                        break;

                    case "cmsIcon_StopHook":
                        this.StopHook_MainForm();
                        break;

                    case "cmsIcon_CleanUp":
                        this.CleanUp_MainForm();
                        break;

                    case "cmsIcon_Exit":
                        this.Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//封包列表菜单

        private void cmsSocketList_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.BuildGroupedSendListMenu(this.cmsSocketList_SendList, this.cmsSocketList_SendTarget_Click);
        }

        private void cmsSocketList_SendTarget_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem targetItem = sender as ToolStripMenuItem;
            string folderName = targetItem == null ? null : targetItem.Tag as string;
            if (string.IsNullOrEmpty(folderName))
            {
                return;
            }

            List<Socket_PacketInfo> packets = Socket_Operation.GetSelectedSocket(this.dgvSocketList);
            foreach (Socket_PacketInfo packet in packets)
            {
                Socket_SendInfo addedPreset = this.PromptAndAddPacketPreset(
                    packet,
                    folderName,
                    packet.PacketBuffer,
                    packet.ByteAnnotations);
                if (addedPreset == null)
                {
                    break;
                }
            }

            this.cmsSocketList.Close();
        }

        private void tscbSendList_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (this.tscbSendList.SelectedItem != null)
                {
                    Socket_Cache.SendList.SendListItem item = this.tscbSendList.SelectedItem as Socket_Cache.SendList.SendListItem;
                    if (item == null)
                    {
                        return;
                    }
                    Guid SID = item.SID;

                    List<Socket_PacketInfo> spiList = Socket_Operation.GetSelectedSocket(this.dgvSocketList);

                    if (spiList.Count > 0)
                    {
                        Socket_Cache.Send.AddSendCollection_ByPacketInfo(SID, spiList);
                    }

                    this.cmsSocketList.Close();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void cmsSocketList_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            string sItemText = e.ClickedItem.Name;
            this.cmsSocketList.Close();

            try
            {
                if (Socket_Cache.SocketList.spiSelect != null)
                {
                    switch (sItemText)
                    {
                        case "cmsSocketList_Send":

                            this.ResendCurrentPacket();

                            break;

                        case "cmsSocketList_PacketDetails":

                            Socket_Operation.ShowSendForm(this.GetCurrentEditedPacket());

                            break;

                        case "cmsSocketList_FilterList":

                            Socket_Cache.Filter.AddFilter_ByPacketInfo(Socket_Cache.SocketList.spiSelect, null);

                            break;

                        case "cmsSocketList_SystemSocket":

                            Socket_Cache.System.SystemSocket = Socket_Cache.SocketList.spiSelect.PacketSocket;

                            break;

                        case "cmsSocketList_ShowModified":

                            Socket_Operation.ShowSocketCompareForm(Socket_Cache.SocketList.spiSelect);

                            break;

                        case "cmsSocketList_ToExcel":

                            if (dgvSocketList.Rows.Count > 0)
                            {
                                Socket_Cache.SocketList.SaveSocketList_Dialog();
                            }

                            break;

                        case "cmsSocketList_Comparison_A":

                            this.rtbComparison_A.Text = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Hex, Socket_Cache.SocketList.spiSelect.PacketBuffer);

                            break;

                        case "cmsSocketList_Comparison_B":

                            this.rtbComparison_B.Text = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Hex, Socket_Cache.SocketList.spiSelect.PacketBuffer);

                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void ResendCurrentPacket()
        {
            Socket_PacketInfo packet = this.GetCurrentEditedPacket();
            if (packet == null)
            {
                return;
            }

            if (packet.PacketSocket <= 0)
            {
                Socket_Operation.ShowMessageBox(
                    MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_45));
                return;
            }

            if (packet.PacketBuffer == null || packet.PacketBuffer.Length == 0)
            {
                Socket_Operation.ShowMessageBox(
                    MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_46));
                return;
            }

            Socket_Operation.SendPacket(
                packet.PacketSocket,
                packet.PacketType,
                packet.PacketFrom,
                packet.PacketTo,
                packet.PacketBuffer);
        }

        #endregion

        #region//滤镜列表菜单

        private void cmsFilterList_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            string sItemText = e.ClickedItem.Name;
            cmsFilterList.Close();

            try
            {
                if (dgvFilterList.Rows.Count > 0)
                {
                    List<Socket_FilterInfo> sfiList = Socket_Operation.GetSelectedFilter(this.dgvFilterList);

                    if (sfiList.Count > 0)
                    {
                        switch (sItemText)
                        {
                            case "cmsFilterList_Top":
                                Socket_Cache.FilterList.UpdateFilterList_ByListAction(Socket_Cache.System.ListAction.Top, sfiList);
                                break;

                            case "cmsFilterList_Up":
                                Socket_Cache.FilterList.UpdateFilterList_ByListAction(Socket_Cache.System.ListAction.Up, sfiList);
                                break;

                            case "cmsFilterList_Down":
                                Socket_Cache.FilterList.UpdateFilterList_ByListAction(Socket_Cache.System.ListAction.Down, sfiList);
                                break;

                            case "cmsFilterList_Bottom":
                                Socket_Cache.FilterList.UpdateFilterList_ByListAction(Socket_Cache.System.ListAction.Bottom, sfiList);
                                break;

                            case "cmsFilterList_Copy":
                                Socket_Cache.FilterList.UpdateFilterList_ByListAction(Socket_Cache.System.ListAction.Copy, sfiList);
                                break;

                            case "cmsFilterList_Export":
                                Socket_Cache.FilterList.UpdateFilterList_ByListAction(Socket_Cache.System.ListAction.Export, sfiList);
                                break;

                            case "cmsFilterList_Delete":
                                Socket_Cache.FilterList.UpdateFilterList_ByListAction(Socket_Cache.System.ListAction.Delete, sfiList);
                                break;
                        }

                        this.dgvFilterList.ClearSelection();

                        foreach (Socket_FilterInfo sfi in sfiList)
                        {
                            int iIndex = Socket_Cache.FilterList.lstFilter.IndexOf(sfi);

                            if (iIndex > -1 && iIndex < dgvFilterList.RowCount)
                            {
                                this.dgvFilterList.Rows[iIndex].Selected = true;
                                dgvFilterList.FirstDisplayedScrollingRowIndex = iIndex;
                            }
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

        #region//发送列表菜单

        private void cmsSendList_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            string sItemText = e.ClickedItem.Name;
            cmsSendList.Close();

            try
            {
                if (dgvSendList.Rows.Count > 0)
                {
                    List<Socket_SendInfo> ssiList = Socket_Operation.GetSelectedSend(this.dgvSendList);

                    if (ssiList.Count > 0)
                    {
                        bool changesSendList =
                            sItemText == "cmsSendList_Top" ||
                            sItemText == "cmsSendList_Up" ||
                            sItemText == "cmsSendList_Down" ||
                            sItemText == "cmsSendList_Bottom" ||
                            sItemText == "cmsSendList_Delete";
                        if (changesSendList && !this.CanModifySendLists(ssiList, "修改封包"))
                        {
                            return;
                        }

                        switch (sItemText)
                        {
                            case "cmsSendList_Top":
                                this.MoveSelectedSendListOrder(Socket_Cache.System.ListAction.Top, ssiList);
                                break;

                            case "cmsSendList_Up":
                                this.MoveSelectedSendListOrder(Socket_Cache.System.ListAction.Up, ssiList);
                                break;

                            case "cmsSendList_Down":
                                this.MoveSelectedSendListOrder(Socket_Cache.System.ListAction.Down, ssiList);
                                break;

                            case "cmsSendList_Bottom":
                                this.MoveSelectedSendListOrder(Socket_Cache.System.ListAction.Bottom, ssiList);
                                break;

                            case "cmsSendList_Copy":
                                Socket_Cache.SendList.UpdateSendList_ByListAction(Socket_Cache.System.ListAction.Copy, ssiList);
                                break;

                            case "cmsSendList_Export":
                                Socket_Cache.SendList.UpdateSendList_ByListAction(Socket_Cache.System.ListAction.Export, ssiList);
                                break;

                            case "cmsSendList_Delete":
                                Socket_Cache.SendList.UpdateSendList_ByListAction(Socket_Cache.System.ListAction.Delete, ssiList);
                                break;
                        }

                        this.dgvSendList.ClearSelection();

                        foreach (Socket_SendInfo ssi in ssiList)
                        {
                            DataGridViewRow visibleRow = this.dgvSendList.Rows
                                .Cast<DataGridViewRow>()
                                .FirstOrDefault(row => ReferenceEquals(row.DataBoundItem, ssi));
                            if (visibleRow != null)
                            {
                                visibleRow.Selected = true;
                                dgvSendList.FirstDisplayedScrollingRowIndex = visibleRow.Index;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void MoveSelectedSendListOrder(
            Socket_Cache.System.ListAction listAction,
            List<Socket_SendInfo> selectedItems)
        {
            List<Socket_SendInfo> orderedItems = this.GetCurrentFolderSendLists();
            HashSet<Socket_SendInfo> selectedSet = new HashSet<Socket_SendInfo>(selectedItems);

            if (listAction == Socket_Cache.System.ListAction.Top)
            {
                orderedItems = orderedItems.Where(selectedSet.Contains)
                    .Concat(orderedItems.Where(item => !selectedSet.Contains(item)))
                    .ToList();
            }
            else if (listAction == Socket_Cache.System.ListAction.Bottom)
            {
                orderedItems = orderedItems.Where(item => !selectedSet.Contains(item))
                    .Concat(orderedItems.Where(selectedSet.Contains))
                    .ToList();
            }
            else if (listAction == Socket_Cache.System.ListAction.Up)
            {
                for (int index = 1; index < orderedItems.Count; index++)
                {
                    if (selectedSet.Contains(orderedItems[index]) &&
                        !selectedSet.Contains(orderedItems[index - 1]))
                    {
                        Socket_SendInfo previousItem = orderedItems[index - 1];
                        orderedItems[index - 1] = orderedItems[index];
                        orderedItems[index] = previousItem;
                    }
                }
            }
            else if (listAction == Socket_Cache.System.ListAction.Down)
            {
                for (int index = orderedItems.Count - 2; index >= 0; index--)
                {
                    if (selectedSet.Contains(orderedItems[index]) &&
                        !selectedSet.Contains(orderedItems[index + 1]))
                    {
                        Socket_SendInfo nextItem = orderedItems[index + 1];
                        orderedItems[index + 1] = orderedItems[index];
                        orderedItems[index] = nextItem;
                    }
                }
            }

            for (int index = 0; index < orderedItems.Count; index++)
            {
                orderedItems[index].SSortOrder = index + 1;
            }

            this.RefreshSendFolderView();
        }

        #endregion

        #region//机器人列表菜单

        private void cmsRobotList_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            string sItemText = e.ClickedItem.Name;
            cmsRobotList.Close();

            try
            {
                if (dgvRobotList.Rows.Count > 0)
                {
                    List<Socket_RobotInfo> sriList = Socket_Operation.GetSelectedRobot(this.dgvRobotList);

                    if (sriList.Count > 0)
                    {
                        switch (sItemText)
                        {
                            case "cmsRobotList_Top":
                                Socket_Cache.RobotList.UpdateRobotList_ByListAction(Socket_Cache.System.ListAction.Top, sriList);
                                break;

                            case "cmsRobotList_Up":
                                Socket_Cache.RobotList.UpdateRobotList_ByListAction(Socket_Cache.System.ListAction.Up, sriList);
                                break;

                            case "cmsRobotList_Down":
                                Socket_Cache.RobotList.UpdateRobotList_ByListAction(Socket_Cache.System.ListAction.Down, sriList);
                                break;

                            case "cmsRobotList_Bottom":
                                Socket_Cache.RobotList.UpdateRobotList_ByListAction(Socket_Cache.System.ListAction.Bottom, sriList);
                                break;

                            case "cmsRobotList_Copy":
                                Socket_Cache.RobotList.UpdateRobotList_ByListAction(Socket_Cache.System.ListAction.Copy, sriList);
                                break;

                            case "cmsRobotList_Export":
                                Socket_Cache.RobotList.UpdateRobotList_ByListAction(Socket_Cache.System.ListAction.Export, sriList);
                                break;

                            case "cmsRobotList_Delete":
                                Socket_Cache.RobotList.UpdateRobotList_ByListAction(Socket_Cache.System.ListAction.Delete, sriList);
                                break;
                        }

                        this.dgvRobotList.ClearSelection();

                        foreach (Socket_RobotInfo sri in sriList)
                        {
                            int iIndex = Socket_Cache.RobotList.lstRobot.IndexOf(sri);

                            if (iIndex > -1 && iIndex < dgvRobotList.RowCount)
                            {
                                this.dgvRobotList.Rows[iIndex].Selected = true;
                                this.dgvRobotList.FirstDisplayedScrollingRowIndex = iIndex;
                            }
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

        #region//日志菜单

        private void cmsLogList_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            string sItemText = e.ClickedItem.Name;
            cmsLogList.Close();

            try
            {
                switch (sItemText)
                {
                    case "cmsLogList_ToExcel":

                        if (dgvLogList.Rows.Count > 0)
                        {
                            Socket_Cache.LogList.SaveLogListToExcel();
                        }

                        break;

                    case "cmsLogList_CleanUp":

                        this.CleanUp_LogList();

                        break;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        

        #region//滤镜列表按钮

        private void tsFilterList_Load_Click(object sender, EventArgs e)
        {
            Socket_Cache.FilterList.LoadFilterList_Dialog();
        }

        private void tsFilterList_Save_Click(object sender, EventArgs e)
        {
            try
            {
                if (Socket_Cache.FilterList.lstFilter.Count > 0)
                {
                    List<Socket_FilterInfo> sfiList = new List<Socket_FilterInfo>();
                    sfiList.AddRange(Socket_Cache.FilterList.lstFilter);

                    Socket_Cache.FilterList.SaveFilterList_Dialog(string.Empty, sfiList);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void tsFilterList_Add_Click(object sender, EventArgs e)
        {
            Socket_Cache.Filter.AddFilter_New();

            this.dgvFilterList.ClearSelection();
            this.dgvFilterList.CurrentCell = this.dgvFilterList.Rows[this.dgvFilterList.Rows.Count - 1].Cells[0];
        }

        private void tsFilterList_CleanUp_Click(object sender, EventArgs e)
        {
            if (dgvFilterList.Rows.Count > 0)
            {
                Socket_Cache.FilterList.CleanUpFilterList_Dialog();
            }
        }

        private void tsFilterList_SelectAll_Click(object sender, EventArgs e)
        {
            try
            {
                if (Socket_Cache.FilterList.lstFilter.Count > 0)
                {
                    List<Socket_FilterInfo> sfiList = Socket_Operation.GetSelectedFilter(this.dgvFilterList);

                    foreach (Socket_FilterInfo sfi in sfiList)
                    {
                        sfi.IsEnable = true;
                    }

                    this.dgvFilterList.Refresh();
                    this.dgvSocketList.Focus();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void tsFilterList_SelectNo_Click(object sender, EventArgs e)
        {
            try
            {
                if (Socket_Cache.FilterList.lstFilter.Count > 0)
                {
                    List<Socket_FilterInfo> sfiList = Socket_Operation.GetSelectedFilter(this.dgvFilterList);

                    foreach (Socket_FilterInfo sfi in sfiList)
                    {
                        sfi.IsEnable = false;
                    }

                    this.dgvFilterList.Refresh();
                    this.dgvSocketList.Focus();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        

        #region//发送列表按钮

        private void tsSendList_Load_Click(object sender, EventArgs e)
        {
            Socket_Cache.SendList.LoadSendList_Dialog();
            this.RefreshSendFolderTree();
            this.RefreshSendFolderView();
        }

        private void tsSendList_Save_Click(object sender, EventArgs e)
        {
            try
            {
                if (Socket_Cache.SendList.lstSend.Count > 0)
                {
                    List<Socket_SendInfo> ssiList = new List<Socket_SendInfo>();
                    ssiList.AddRange(Socket_Cache.SendList.lstSend);

                    Socket_Cache.SendList.SaveSendList_Dialog(string.Empty, ssiList);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void tsSendList_Add_Click(object sender, EventArgs e)
        {
            if (!this.HasSelectedSendFolder())
            {
                MessageBox.Show(this, UiText("UI_CreateGroupFirst"), UiText("UI_NewPacket"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            byte[] buffer = new byte[] { 0 };
            Socket_PacketInfo packet = new Socket_PacketInfo(
                DateTime.Now,
                Socket_Cache.System.SystemSocket,
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                string.Empty,
                string.Empty,
                (byte[])buffer.Clone(),
                (byte[])buffer.Clone(),
                buffer.Length,
                Socket_Cache.Filter.FilterAction.None);
            packet.PacketData = Socket_Operation.GetPacketData_Hex(
                packet.PacketBuffer.AsSpan(),
                Socket_Cache.SocketPacket.PacketData_MaxLen);

            Socket_SendInfo sendInfo = this.AddPacketPresetToGroup(
                packet,
                this.selectedSendFolder,
                buffer,
                packet.ByteAnnotations);
            this.RefreshSendFolderView();
            if (sendInfo == null || sendInfo.SCollection == null || sendInfo.SCollection.Count != 1)
            {
                return;
            }

            foreach (DataGridViewRow row in this.dgvSendList.Rows)
            {
                if (ReferenceEquals(row.DataBoundItem, sendInfo))
                {
                    row.Selected = true;
                    this.dgvSendList.CurrentCell = row.Cells
                        .Cast<DataGridViewCell>()
                        .FirstOrDefault(cell => cell.Visible);
                    break;
                }
            }

            Socket_Operation.ShowSendForm(sendInfo.SCollection[0]);
            this.RefreshSendFolderView();
        }

        private void tsSendList_Start_Click(object sender, EventArgs e)
        {
            if (!this.HasSelectedSendFolder())
            {
                MessageBox.Show(this, UiText("UI_SelectGroupFirst"), UiText("UI_StartSending"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (dgvSendList.Rows.Count > 0 && !this.bgwSendList.IsBusy)
            {
                if (this.manualSendOperations.Values.Any(send => send.Worker.IsBusy))
                {
                    MessageBox.Show(this, UiText("UI_IndividualSendActive"),
                        UiText("UI_StartSending"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.UpdateSendExecutionControls();
                    return;
                }

                this.dgvSendList.EndEdit();
                this.sendBatchQueue = this.GetCurrentFolderSendLists()
                    .Where(item => item.IsEnable)
                    .OrderBy(item => item.SSortOrder)
                    .ToList();

                if (this.sendBatchQueue.Count == 0)
                {
                    MessageBox.Show(this, UiText("UI_SelectSendItems"), UiText("UI_StartSending"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!this.EnsureCurrentSystemSocket(this.sendBatchQueue))
                {
                    return;
                }

                this.sendListParallelMode = this.tsSendListParallel != null &&
                    this.tsSendListParallel.Checked;
                this.tsSendList_Start.Enabled = false;
                this.tsSendList_Stop.Enabled = true;
                Socket_Cache.SendList.lstExecute.Clear();
                this.bgwSendList.RunWorkerAsync();
                this.UpdateSendExecutionControls();
            }
        }

        private bool EnsureCurrentSystemSocket(IEnumerable<Socket_SendInfo> sendInfos)
        {
            List<Socket_SendInfo> items = sendInfos == null
                ? new List<Socket_SendInfo>()
                : sendInfos.Where(item => item != null).ToList();
            bool allResolved = items.Count > 0 && items.All(item =>
                item.SCollection != null &&
                item.SCollection.Count > 0 &&
                Socket_Cache.SocketList.ResolveCurrentSocket(item.SCollection) > 0);
            if (allResolved)
            {
                return true;
            }

            MessageBox.Show(
                this,
                UiText("UI_CurrentSocketRequired"),
                UiText("UI_StartSending"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        private void tsSendList_Stop_Click(object sender, EventArgs e)
        {
            this.bgwSendList.CancelAsync();
            this.StopActiveBatchSends();
        }

        private void tsSendListParallel_Click(object sender, EventArgs e)
        {
            this.UpdateSendListParallelModeText();
        }

        private void tsSendList_CleanUp_Click(object sender, EventArgs e)
        {
            if (dgvSendList.Rows.Count > 0)
            {
                Socket_Cache.SendList.CleanUpSendList_Dialog();
                this.RefreshSendFolderTree();
                this.RefreshSendFolderView();
            }
        }

        #endregion

        #region//执行发送列表（异步）

        private void bgwSendList_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                List<Socket_SendInfo> queue = this.sendBatchQueue.ToList();
                if (this.sendListParallelMode)
                {
                    Task[] tasks = queue
                        .Select(ssi => Task.Run(() => this.ExecuteSendListItem(ssi)))
                        .ToArray();
                    Task.WaitAll(tasks);
                    if (this.bgwSendList.CancellationPending)
                    {
                        e.Cancel = true;
                    }
                    return;
                }

                foreach (Socket_SendInfo ssi in queue)
                {
                    if (this.bgwSendList.CancellationPending)
                    {
                        e.Cancel = true;
                        return;
                    }

                    this.ExecuteSendListItem(ssi);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void ExecuteSendListItem(Socket_SendInfo sendInfo)
        {
            if (sendInfo == null || this.bgwSendList.CancellationPending)
            {
                return;
            }

            Socket_Send sendOperation = Socket_Cache.Send.DoSend(sendInfo.SID);
            if (sendOperation == null)
            {
                return;
            }

            this.SetActiveBatchSend(sendInfo.SID, sendOperation);
            lock (this.sendOperationSync)
            {
                Socket_Cache.SendList.lstExecute.Add(sendOperation);
            }
            try
            {
                while (sendOperation.Worker.IsBusy)
                {
                    if (this.bgwSendList.CancellationPending)
                    {
                        sendOperation.StopSend();
                    }

                    Thread.Sleep(100);
                }
            }
            finally
            {
                lock (this.sendOperationSync)
                {
                    Socket_Cache.SendList.lstExecute.Remove(sendOperation);
                }
                this.ClearActiveBatchSend(sendInfo.SID, sendOperation);
            }
        }

        private void bgwSendList_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                this.RefreshSendFolderView();
                this.UpdateSendExecutionControls();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        

        #region//机器人列表按钮

        private void tsRobotList_Load_Click(object sender, EventArgs e)
        {
            Socket_Cache.RobotList.LoadRobotList_Dialog();
        }

        private void tsRobotList_Save_Click(object sender, EventArgs e)
        {
            try
            {
                if (Socket_Cache.RobotList.lstRobot.Count > 0)
                {
                    List<Socket_RobotInfo> sriList = new List<Socket_RobotInfo>();
                    sriList.AddRange(Socket_Cache.RobotList.lstRobot);

                    Socket_Cache.RobotList.SaveRobotList_Dialog(string.Empty, sriList);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void tsRobotList_Start_Click(object sender, EventArgs e)
        {
            if (dgvRobotList.Rows.Count > 0)
            {
                if (!this.bgwRobotList.IsBusy)
                {
                    Socket_Cache.RobotList.lstExecute.Clear();

                    this.bgwRobotList.RunWorkerAsync();
                    this.UpdateRobotToolbarState();
                }
            }
        }

        private void tsRobotList_Stop_Click(object sender, EventArgs e)
        {
            this.bgwRobotList.CancelAsync();
        }

        private void tsRobotList_Add_Click(object sender, EventArgs e)
        {
            if (this.bgwRobotList.IsBusy)
            {
                return;
            }

            Socket_Cache.Robot.AddRobot_New();
            Socket_RobotInfo addedRobot = Socket_Cache.RobotList.lstRobot.LastOrDefault();
            if (addedRobot != null)
            {
                addedRobot.RFolder = this.selectedRobotFolder;
                if (!Socket_Cache.RobotList.lstFolders.Contains(this.selectedRobotFolder))
                {
                    Socket_Cache.RobotList.lstFolders.Add(this.selectedRobotFolder);
                }
            }
            Socket_Cache.RobotList.SaveRobotList_ToDB();
            this.RefreshAssistantFolders();
            this.dgvRobotList.ClearSelection();
            if (this.dgvRobotList.Rows.Count > 0)
            {
                this.dgvRobotList.CurrentCell = this.dgvRobotList.Rows[this.dgvRobotList.Rows.Count - 1].Cells[0];
            }
        }

        private void tsRobotList_CleanUp_Click(object sender, EventArgs e)
        {
            if (!this.bgwRobotList.IsBusy && dgvRobotList.Rows.Count > 0)
            {
                Socket_Cache.RobotList.CleanUpRobotList_Dialog();
                this.RefreshAssistantFolders();
            }
        }

        private void UpdateRobotToolbarState()
        {
            bool running = this.bgwRobotList.IsBusy;
            this.tsRobotList_Start.Enabled = !running && this.dgvRobotList.Rows.Count > 0;
            this.tsRobotList_Stop.Enabled = running;
            this.tsRobotList_Add.Enabled = !running;
            if (this.tsRobotListMore != null)
            {
                this.tsRobotListMore.Enabled = !running;
            }
        }

        #endregion

        #region//执行机器人列表（异步）

        private void bgwRobotList_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                foreach (Socket_RobotInfo sri in Socket_Cache.RobotList.lstRobot)
                {
                    if (sri.IsEnable)
                    {
                        Socket_Robot sr = Socket_Cache.Robot.DoRobot(sri.RID, null);
                        if (sr != null)
                        {
                            if (Socket_Cache.System.ListExecute == Socket_Cache.System.Execute.Together)
                            {
                                Socket_Cache.RobotList.lstExecute.Add(sr);
                            }
                            else
                            {
                                while (sr.Worker.IsBusy)
                                {
                                    if (this.bgwRobotList.CancellationPending)
                                    {
                                        sr.StopRobot();

                                        e.Cancel = true;
                                        return;
                                    }

                                    Thread.Sleep(100);
                                }
                            }                                
                        }
                    }
                }

                while (Socket_Cache.RobotList.lstExecute.Count > 0)
                {
                    foreach (Socket_Robot sr in Socket_Cache.RobotList.lstExecute.ToList())
                    {
                        if (this.bgwRobotList.CancellationPending)
                        {
                            sr.StopRobot();
                        }

                        if (!sr.Worker.IsBusy)
                        {
                            Socket_Cache.RobotList.lstExecute.Remove(sr);
                        }
                    }

                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void bgwRobotList_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            try
            {
                this.UpdateRobotToolbarState();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//文本对比

        private void bTextCompare_Click(object sender, EventArgs e)
        {
            string TextA = this.rtbComparison_A.Text.Trim();
            string TextB = this.rtbComparison_B.Text.Trim();

            if (!Socket_Cache.System.IsShow_TextCompare)
            {
                TextCompareForm tcForm = new TextCompareForm(TextA, TextB);
                tcForm.Show();
            }            
        }

        private void bTextDuplicate_Click(object sender, EventArgs e)
        {
            string TextA = this.rtbComparison_A.Text.Trim();
            string TextB = this.rtbComparison_B.Text.Trim();

            if (!Socket_Cache.System.IsShow_TextDuplicate)
            {
                TextDuplicateForm tdForm = new TextDuplicateForm(TextA, TextB);
                tdForm.Show();
            }
        }

        #endregion

        #region//编码转换

        private void bPacketInfo_Encoding_Click(object sender, EventArgs e)
        {
            try
            {
                string sEncodingText = this.rtbPacketInfo_Encoding.Text;

                string sBytes = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Bytes, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Default, sEncodingText));
                string sANSI_GBK = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Hex, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.GBK, sEncodingText));

                string sUTF7 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Default, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.UTF7, sEncodingText));
                string sANSI_UTF7 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Hex, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.UTF7, sEncodingText));

                string sUTF8 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Default, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.UTF8, sEncodingText));
                string sANSI_UTF8 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Hex, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.UTF8, sEncodingText));

                string sUTF16 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Default, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.UTF16, sEncodingText));
                string sANSI_UTF16 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Hex, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.UTF16, sEncodingText));

                string sUTF32 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Default, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.UTF32, sEncodingText));
                string sANSI_UTF32 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Hex, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.UTF32, sEncodingText));

                string sUnicode = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Default, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Unicode, sEncodingText));
                string sANSI_Unicode = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Hex, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Unicode, sEncodingText));

                string sBase64 = Socket_Operation.Base64_Encoding(sEncodingText);
                string sANSI_Base64 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Hex, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Default, sBase64));

                this.txtPacketInfo_Encoding_Bytes.Text = sBytes;
                this.txtPacketInfo_Encoding_ANSIGBK.Text = sANSI_GBK;
                this.txtPacketInfo_Encoding_UTF7.Text = sUTF7;
                this.txtPacketInfo_Encoding_ANSIUTF7.Text = sANSI_UTF7;
                this.txtPacketInfo_Encoding_UTF8.Text = sUTF8;
                this.txtPacketInfo_Encoding_ANSIUTF8.Text = sANSI_UTF8;
                this.txtPacketInfo_Encoding_UTF16.Text = sUTF16;
                this.txtPacketInfo_Encoding_ANSIUTF16.Text = sANSI_UTF16;
                this.txtPacketInfo_Encoding_UTF32.Text = sUTF32;
                this.txtPacketInfo_Encoding_ANSIUTF32.Text = sANSI_UTF32;
                this.txtPacketInfo_Encoding_Unicode.Text = sUnicode;
                this.txtPacketInfo_Encoding_ANSIUnicode.Text = sANSI_Unicode;
                this.txtPacketInfo_Encoding_base64.Text = sBase64;
                this.txtPacketInfo_Encoding_ANSIbase64.Text = sANSI_Base64;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void bPacketInfo_Decoding_Click(object sender, EventArgs e)
        {
            try
            {
                string sDecodingText = this.rtbPacketInfo_Encoding.Text;

                string sBytes = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Bytes, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Default, sDecodingText));
                string sANSI_GBK = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.GBK, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, sDecodingText));

                string sUTF7 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UTF7, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Default, sDecodingText));
                string sANSI_UTF7 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UTF7, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, sDecodingText));

                string sUTF8 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UTF8, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Default, sDecodingText));
                string sANSI_UTF8 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UTF8, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, sDecodingText));

                string sUTF16 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UTF16, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Default, sDecodingText));
                string sANSI_UTF16 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UTF16, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, sDecodingText));

                string sUTF32 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UTF32, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Default, sDecodingText));
                string sANSI_UTF32 = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UTF32, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, sDecodingText));

                string sUnicode = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Unicode, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Default, sDecodingText));
                string sANSI_Unicode = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Unicode, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, sDecodingText));

                string sBase64 = Socket_Operation.Base64_Decoding(sDecodingText);
                string sANSI_Base64 = Socket_Operation.Base64_Decoding(Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Default, Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, sDecodingText)));

                this.txtPacketInfo_Encoding_Bytes.Text = sBytes;
                this.txtPacketInfo_Encoding_ANSIGBK.Text = sANSI_GBK;
                this.txtPacketInfo_Encoding_UTF7.Text = sUTF7;
                this.txtPacketInfo_Encoding_ANSIUTF7.Text = sANSI_UTF7;
                this.txtPacketInfo_Encoding_UTF8.Text = sUTF8;
                this.txtPacketInfo_Encoding_ANSIUTF8.Text = sANSI_UTF8;
                this.txtPacketInfo_Encoding_UTF16.Text = sUTF16;
                this.txtPacketInfo_Encoding_ANSIUTF16.Text = sANSI_UTF16;
                this.txtPacketInfo_Encoding_UTF32.Text = sUTF32;
                this.txtPacketInfo_Encoding_ANSIUTF32.Text = sANSI_UTF32;
                this.txtPacketInfo_Encoding_Unicode.Text = sUnicode;
                this.txtPacketInfo_Encoding_ANSIUnicode.Text = sANSI_Unicode;
                this.txtPacketInfo_Encoding_base64.Text = sBase64;
                this.txtPacketInfo_Encoding_ANSIbase64.Text = sANSI_Base64;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//异或计算

        private void bXOR_Click(object sender, EventArgs e)
        {
            try
            {
                this.hbXOR_To.ByteProvider = new DynamicByteProvider(new byte[0]);

                DynamicByteProvider dbpXOR_From = this.hbXOR_From.ByteProvider as DynamicByteProvider;
                if (dbpXOR_From == null)
                {
                    return;
                }

                byte[] blXOR_From = dbpXOR_From.Bytes.ToArray();

                string sXOR_Value = this.txtXOR.Text.Trim();
                string[] slXOR_Value = sXOR_Value.Split(' ');

                if (blXOR_From.Length == 0 || string.IsNullOrEmpty(sXOR_Value) || slXOR_Value.Length == 0)
                {
                    return;
                }

                byte[] blXOR_To = new byte[blXOR_From.Length];
                int j = 0;

                foreach (byte bXOR_From in blXOR_From)
                {
                    if (j == slXOR_Value.Length)
                    {
                        j = 0;
                    }

                    if (!Byte.TryParse(slXOR_Value[j], System.Globalization.NumberStyles.HexNumber, null, out byte bXOR_Value))
                    {
                        Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_21));
                        return;
                    }

                    blXOR_To[j] = (byte)(bXOR_From ^ bXOR_Value);
                    j++;
                }

                DynamicByteProvider dbpXOR_To = new DynamicByteProvider(blXOR_To);
                this.hbXOR_To.ByteProvider = dbpXOR_To;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void txtXOR_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!Socket_Operation.CheckTextInput_IsHex(e.KeyChar))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void bXOR_Clear_Click(object sender, EventArgs e)
        {
            this.InitHexBox_XOR();
            this.txtXOR.Clear();
        }

        #endregion

        #region//数据提取        

        private void bExtraction_Click(object sender, EventArgs e)
        {
            try
            {
                this.rtbExtraction.Clear();

                int iSelectIndex = this.cbbExtraction.SelectedIndex;

                if (iSelectIndex == 0)
                {
                    this.ofdExtraction.Filter = "Charles 会话文件（*.chlsx）|*.chlsx";
                }
                else if (iSelectIndex == 1)
                {
                    this.ofdExtraction.Filter = "FILT 过滤器文件（*.filt）|*.filt";
                }

                ofdExtraction.ShowDialog();

                string FilePath = ofdExtraction.FileName;

                if (!string.IsNullOrEmpty(FilePath))
                {
                    if (File.Exists(FilePath))
                    {
                        switch (iSelectIndex)
                        {
                            case 0:

                                #region//Charles XML 会话文件

                                try
                                {
                                    XDocument xdoc_Charles = new XDocument();
                                    xdoc_Charles = XDocument.Load(FilePath);

                                    XElement xeRoot_Charles = xdoc_Charles.Descendants("response").FirstOrDefault();

                                    if (xeRoot_Charles != null)
                                    {
                                        if (xeRoot_Charles.Element("body") != null)
                                        {
                                            string sBody = xeRoot_Charles.Element("body").Value;

                                            byte[] bBody = Convert.FromBase64String(sBody);
                                            this.rtbExtraction.Text = BitConverter.ToString(bBody).Replace("-", " ");
                                        }
                                    }
                                }
                                catch
                                {
                                    //                            
                                }

                                #endregion

                                break;

                            case 1:

                                #region//FILT 过滤器文件

                                string[] lines = File.ReadAllLines(FilePath, Encoding.Default);

                                XDocument xdoc_Filt = new XDocument
                                {
                                    Declaration = new XDeclaration("1.0", "utf-8", "yes")
                                };

                                XElement xeRoot_Filt = new XElement("FilterList");
                                xdoc_Filt.Add(xeRoot_Filt);

                                foreach (string line in lines)
                                {
                                    if (line.IndexOf("￥") >= 0)
                                    {
                                        string[] slFilter = line.Split('￥');

                                        if (slFilter.Length == 35)
                                        {
                                            string s0 = slFilter[0].ToString();//是否指定长度 bool （真，假）
                                            string s1 = slFilter[1].ToString();//指定长度 int
                                            string s2 = slFilter[2].ToString();//是否指定套接字 bool （真，假）
                                            string s3 = slFilter[3].ToString();//套接字 int
                                            string s4 = slFilter[4].ToString();//是否指定包头 bool （真，假）
                                            string s5 = slFilter[5].ToString();//包头 string (十六进制不带空格)
                                            string s6 = slFilter[6].ToString();//未知 bool （真，假）
                                            string s7 = slFilter[7].ToString();//未知 int 0
                                            string s8 = slFilter[8].ToString();//未知 int 0
                                            string s9 = slFilter[9].ToString();//是否替换 bool （真，假）
                                            string s10 = slFilter[10].ToString();//是否拦截 bool （真，假）
                                            string s11 = slFilter[11].ToString();//是否不可视 bool （真，假）
                                            string s12 = slFilter[12].ToString();//步长 int
                                            string s13 = slFilter[13].ToString();//过滤器名称 string
                                            string s14 = slFilter[14].ToString();//发送 bool （1，0）
                                            string s15 = slFilter[15].ToString();//接收 bool （1，0）
                                            string s16 = slFilter[16].ToString();//发送到 bool （1，0）
                                            string s17 = slFilter[17].ToString();//接收自 bool （1，0）
                                            string s18 = slFilter[18].ToString();//WSA发送 bool （1，0）
                                            string s19 = slFilter[19].ToString();//WSA接收 bool （1，0）
                                            string s20 = slFilter[20].ToString();//WSA发送到 bool （1，0）
                                            string s21 = slFilter[21].ToString();//未知 -1
                                            string s22 = slFilter[22].ToString();//普通模式 bool （真，假）
                                            string s23 = slFilter[23].ToString();//高级模式 bool （真，假）
                                            string s24 = slFilter[24].ToString();//数据包开头 bool （真，假）
                                            string s25 = slFilter[25].ToString();//自发式连锁位 bool （真，假）
                                            string s26 = slFilter[26].ToString();//普通-搜索 string （列Index（支持负数）$十六进制数值不带空格$数据个数$）
                                            string s27 = slFilter[27].ToString();//普通-修改 string（列Index（支持负数）$十六进制数值不带空格$数据个数$）
                                            string s28 = slFilter[28].ToString();//高级-搜索 string（列Index（支持负数）$十六进制数值不带空格$数据个数$）
                                            string s29 = slFilter[29].ToString();//高级-修改 string（列Index（支持负数）$十六进制数值不带空格$数据个数$）
                                            string s30 = slFilter[30].ToString();//递进 bool （真，假）
                                            string s31 = slFilter[31].ToString();//普通-修改-递进 string（列Index（支持负数）$十六进制数值不带空格$数据个数$）
                                            string s32 = slFilter[32].ToString();//高级-修改-递进 string（列Index（支持负数）$十六进制数值不带空格$数据个数$）
                                            string s33 = slFilter[33].ToString();//未知 1

                                            string sIsEnable = bool.FalseString;
                                            string sFID = Guid.NewGuid().ToString();
                                            string sFName = s13;
                                            string sIsExecute = bool.FalseString;
                                            string sRID = Guid.Empty.ToString();
                                            string sFAppointHeader = Socket_Operation.GetBoolFromChineseString(s4).ToString();
                                            string sFHeaderContent = s5;
                                            string sFAppointSocket = Socket_Operation.GetBoolFromChineseString(s2).ToString();
                                            string sFSocketContent = s3;
                                            string sFAppointLength = Socket_Operation.GetBoolFromChineseString(s0).ToString();
                                            string sFLengthContent = s1;

                                            Socket_Cache.Filter.FilterMode FMode = new Socket_Cache.Filter.FilterMode();
                                            if (Socket_Operation.GetBoolFromChineseString(s22) == true)
                                            {
                                                FMode = Socket_Cache.Filter.FilterMode.Normal;
                                            }
                                            else if (Socket_Operation.GetBoolFromChineseString(s23) == true)
                                            {
                                                FMode = Socket_Cache.Filter.FilterMode.Advanced;
                                            }
                                            string sFMode = ((int)FMode).ToString();

                                            Socket_Cache.Filter.FilterAction FAction = new Socket_Cache.Filter.FilterAction();
                                            if (Socket_Operation.GetBoolFromChineseString(s9) == true)
                                            {
                                                FAction = Socket_Cache.Filter.FilterAction.Replace;
                                            }
                                            else if (Socket_Operation.GetBoolFromChineseString(s10) == true)
                                            {
                                                FAction = Socket_Cache.Filter.FilterAction.Intercept;
                                            }
                                            else if (Socket_Operation.GetBoolFromChineseString(s11) == true)
                                            {
                                                FAction = Socket_Cache.Filter.FilterAction.NoModify_NoDisplay;
                                            }
                                            else
                                            {
                                                FAction = Socket_Cache.Filter.FilterAction.NoModify_Display;
                                            }
                                            string sFAction = ((int)FAction).ToString();

                                            bool bSend = Convert.ToBoolean(int.Parse(s14));
                                            bool bRecv = Convert.ToBoolean(int.Parse(s15));
                                            bool bSendTo = Convert.ToBoolean(int.Parse(s16));
                                            bool bRecvFrom = Convert.ToBoolean(int.Parse(s17));
                                            bool bWSASend = Convert.ToBoolean(int.Parse(s18));
                                            bool bWSARecv = Convert.ToBoolean(int.Parse(s19));
                                            bool bWSASendTo = Convert.ToBoolean(int.Parse(s20));
                                            bool bWSARecvFrom = false;

                                            Socket_Cache.Filter.FilterFunction filterFunction = new Socket_Cache.Filter.FilterFunction(bSend, bSendTo, bRecv, bRecvFrom, bWSASend, bWSASendTo, bWSARecv, bWSARecvFrom);
                                            string sFFunction = Socket_Cache.Filter.GetFilterFunctionString(filterFunction);

                                            Socket_Cache.Filter.FilterStartFrom FStartFrom = new Socket_Cache.Filter.FilterStartFrom();
                                            if (Socket_Operation.GetBoolFromChineseString(s24) == true)
                                            {
                                                FStartFrom = Socket_Cache.Filter.FilterStartFrom.Head;
                                            }
                                            else if (Socket_Operation.GetBoolFromChineseString(s25) == true)
                                            {
                                                FStartFrom = Socket_Cache.Filter.FilterStartFrom.Position;
                                            }
                                            string sFStartFrom = ((int)FStartFrom).ToString();

                                            string sFProgressionStep = s12;
                                            string sFProgressionPosition = string.Empty;

                                            string sFSearch = string.Empty;
                                            string sFModify = string.Empty;
                                            if (FMode == Socket_Cache.Filter.FilterMode.Normal)
                                            {
                                                sFProgressionPosition = Socket_Operation.ConvertFILTString(s31, false);
                                                sFSearch = Socket_Operation.ConvertFILTString(s26, false);
                                                sFModify = Socket_Operation.ConvertFILTString(s27, false);
                                            }
                                            else if (FMode == Socket_Cache.Filter.FilterMode.Advanced)
                                            {
                                                sFProgressionPosition = Socket_Operation.ConvertFILTString(s32, false);
                                                sFSearch = Socket_Operation.ConvertFILTString(s28, false);

                                                if (FStartFrom == Socket_Cache.Filter.FilterStartFrom.Position)
                                                {
                                                    sFModify = Socket_Operation.ConvertFILTString(s29, true);
                                                }
                                                else
                                                {
                                                    sFModify = Socket_Operation.ConvertFILTString(s29, false);
                                                }
                                            }

                                            XElement xeFilter =
                                                new XElement("Filter",
                                                new XElement("IsEnable", sIsEnable),
                                                new XElement("ID", sFID),
                                                new XElement("Name", sFName),
                                                new XElement("AppointHeader", sFAppointHeader),
                                                new XElement("HeaderContent", sFHeaderContent),
                                                new XElement("AppointSocket", sFAppointSocket),
                                                new XElement("SocketContent", sFSocketContent),
                                                new XElement("AppointLength", sFAppointLength),
                                                new XElement("LengthContent", sFLengthContent),
                                                new XElement("Mode", sFMode),
                                                new XElement("Action", sFAction),
                                                new XElement("IsExecute", sIsExecute),
                                                new XElement("RobotID", sRID),
                                                new XElement("Function", sFFunction),
                                                new XElement("StartFrom", sFStartFrom),
                                                new XElement("ProgressionStep", sFProgressionStep),
                                                new XElement("ProgressionPosition", sFProgressionPosition),
                                                new XElement("Search", sFSearch),
                                                new XElement("Modify", sFModify)
                                                );

                                            xeRoot_Filt.Add(xeFilter);
                                        }

                                    }
                                }

                                this.rtbExtraction.Text = xdoc_Filt.Declaration.ToString() + "\r\n" + xdoc_Filt.ToString();

                                #endregion

                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void cmsExtraction_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            string sItemText = e.ClickedItem.Name;
            cmsExtraction.Close();

            try
            {
                switch (sItemText)
                {
                    case "cmsExtraction_Export":

                        string sFileContent = this.rtbExtraction.Text.Trim();

                        if (!string.IsNullOrEmpty(sFileContent))
                        {
                            int iSelectIndex = this.cbbExtraction.SelectedIndex;

                            switch (iSelectIndex)
                            {
                                case 0:

                                    this.sfdExtraction.Filter = "TXT（*.txt）|*.txt";

                                    break;

                                case 1:

                                    this.sfdExtraction.Filter = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_75) + "（*.fp）|*.fp";

                                    break;
                            }

                            if (this.sfdExtraction.ShowDialog() == DialogResult.OK)
                            {
                                string sFilePath = this.sfdExtraction.FileName;

                                if (!string.IsNullOrEmpty(sFilePath))
                                {
                                    File.WriteAllText(sFilePath, sFileContent);
                                }
                            }
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }


        #endregion        
    }
}
