using Be.Windows.Forms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    public sealed class MobileByteSweepStartResult
    {
        public bool Accepted { get; set; }
        public Guid JobId { get; set; }
        public string Error { get; set; }
        public string ErrorCode { get; set; }

        public static MobileByteSweepStartResult Failure(string error)
        {
            return Failure(error, "preset_invalid");
        }

        public static MobileByteSweepStartResult Failure(string error, string errorCode)
        {
            return new MobileByteSweepStartResult
            {
                Accepted = false,
                JobId = Guid.Empty,
                Error = error ?? string.Empty,
                ErrorCode = string.IsNullOrWhiteSpace(errorCode)
                    ? "preset_invalid"
                    : errorCode
            };
        }

        public static MobileByteSweepStartResult Success(Guid jobId)
        {
            return new MobileByteSweepStartResult
            {
                Accepted = true,
                JobId = jobId,
                Error = string.Empty,
                ErrorCode = string.Empty
            };
        }
    }

    public partial class Socket_Form
    {
        private TabPage tpByteSweepList;
        private TableLayoutPanel tlpByteSweep;
        private TreeView tvByteSweepFolders;
        private Button bByteSweepFolderAdd;
        private DataGridView dgvByteSweep;
        private ToolStrip tsByteSweep;
        private ToolStripButton tsByteSweepAdd;
        private ToolStripButton tsByteSweepSelectAll;
        private ToolStripButton tsByteSweepParallel;
        private ToolStripButton tsByteSweepStart;
        private ToolStripButton tsByteSweepStop;
        private ToolStripLabel tsByteSweepContext;
        private ContextMenuStrip cmsByteSweepFolder;
        private ContextMenuStrip cmsByteSweepPreset;
        private ToolStripMenuItem cmsSocketListByteSweep;
        private string selectedByteSweepFolder = "__ALL__";
        private Socket_ByteSweepPresetInfo byteSweepEditingPreset;
        private CancellationTokenSource byteSweepCts;
        private bool byteSweepRunning;
        private bool byteSweepParallelMode;
        private bool byteSweepLivePreviewUpdating;
        private Guid activeByteSweepPresetId = Guid.Empty;
        private long byteSweepTotalSend;
        private long byteSweepSuccess;
        private long byteSweepFailure;
        private Guid byteSweepJobId = Guid.Empty;
        private long byteSweepPlannedTotal;
        private bool byteSweepSelectionGuard;
        private long byteSweepOriginalSelectionStart;
        private long byteSweepOriginalSelectionLength;
        private long byteSweepLiveSelectionStart = -1;
        private long byteSweepLiveSelectionLength = 1;
        private string lastByteSweepRouteErrorCode = string.Empty;

        private void InitByteSweepPresetUI()
        {
            this.tpByteSweepList = new TabPage
            {
                Name = "tpByteSweepList",
                Text = UiText("UI_ByteSweep"),
                BackColor = SystemColors.Control,
                Padding = new Padding(3)
            };

            this.tlpByteSweep = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = Padding.Empty
            };
            this.tlpByteSweep.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            this.tlpByteSweep.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tlpByteSweep.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
            this.tlpByteSweep.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tlpByteSweep.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

            Label title = new Label
            {
                Text = UiText("UI_SweepGroups"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };

            this.tvByteSweepFolders = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = UiText("UI_SweepGroups")
            };
            this.tvByteSweepFolders.AfterSelect += this.tvByteSweepFolders_AfterSelect;

            this.cmsByteSweepFolder = new ContextMenuStrip();
            this.cmsByteSweepFolder.Items.Add(UiText("UI_RenameGroup"), null, this.cmsByteSweepFolder_Rename_Click);
            this.cmsByteSweepFolder.Items.Add(UiText("UI_DeleteGroup"), null, this.cmsByteSweepFolder_Delete_Click);
            this.tvByteSweepFolders.ContextMenuStrip = this.cmsByteSweepFolder;
            this.tvByteSweepFolders.NodeMouseClick += (sender, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    this.tvByteSweepFolders.SelectedNode = e.Node;
                }
            };

            this.bByteSweepFolderAdd = new Button
            {
                Text = UiText("UI_NewGroup"),
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 4, 6, 4),
                UseVisualStyleBackColor = true,
                AccessibleName = UiText("UI_NewGroup")
            };
            this.bByteSweepFolderAdd.Click += this.bByteSweepFolderAdd_Click;

            this.tsByteSweep = new ToolStrip
            {
                Dock = DockStyle.Fill,
                GripStyle = ToolStripGripStyle.Hidden,
                RenderMode = ToolStripRenderMode.System
            };
            this.tsByteSweepAdd = CreateByteSweepToolButton(UiText("UI_NewPreset"), this.tsByteSweepAdd_Click);
            this.tsByteSweepSelectAll = CreateByteSweepToolButton(UiText("UI_SelectAll"), this.tsByteSweepSelectAll_Click);
            this.tsByteSweepParallel = CreateByteSweepToolButton(
                UiText("UI_SequentialSend"), this.tsByteSweepParallel_Click);
            this.tsByteSweepParallel.CheckOnClick = true;
            this.tsByteSweepStart = CreateByteSweepToolButton(UiText("UI_SendSelected"), this.tsByteSweepStart_Click);
            this.tsByteSweepStop = CreateByteSweepToolButton(UiText("UI_StopBatch"), this.tsByteSweepStop_Click);
            this.tsByteSweepContext = new ToolStripLabel { Alignment = ToolStripItemAlignment.Right };
            this.tsByteSweep.Items.AddRange(new ToolStripItem[]
            {
                this.tsByteSweepAdd,
                this.tsByteSweepSelectAll,
                this.tsByteSweepParallel,
                this.tsByteSweepStart,
                this.tsByteSweepStop,
                this.tsByteSweepContext
            });
            this.UpdateByteSweepParallelModeText();

            this.dgvByteSweep = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeight = 28,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                MultiSelect = false,
                ReadOnly = false,
                RowHeadersVisible = false,
                ScrollBars = ScrollBars.Vertical,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AccessibleName = UiText("UI_ByteSweep"),
                AccessibleRole = AccessibleRole.Table
            };
            this.AddByteSweepColumns();
            this.dgvByteSweep.CellContentClick += this.dgvByteSweep_CellContentClick;
            this.dgvByteSweep.CellDoubleClick += this.dgvByteSweep_CellDoubleClick;
            this.dgvByteSweep.CellEndEdit += this.dgvByteSweep_CellEndEdit;
            this.dgvByteSweep.DataError += this.dgvByteSweep_DataError;
            this.dgvByteSweep.CellFormatting += this.dgvByteSweep_CellFormatting;
            this.dgvByteSweep.SelectionChanged += this.dgvByteSweep_SelectionChanged;
            this.dgvByteSweep.Paint += this.dgvByteSweep_Paint;

            this.cmsByteSweepPreset = new ContextMenuStrip();
            this.cmsByteSweepPreset.Items.Add(UiText("UI_EditMove"), null, this.cmsByteSweepPreset_Edit_Click);
            this.cmsByteSweepPreset.Items.Add(UiText("UI_DeletePreset"), null, this.cmsByteSweepPreset_Delete_Click);
            this.dgvByteSweep.ContextMenuStrip = this.cmsByteSweepPreset;

            this.tlpByteSweep.Controls.Add(title, 0, 0);
            this.tlpByteSweep.Controls.Add(this.tsByteSweep, 1, 0);
            this.tlpByteSweep.Controls.Add(this.tvByteSweepFolders, 0, 1);
            this.tlpByteSweep.Controls.Add(this.dgvByteSweep, 1, 1);
            this.tlpByteSweep.SetRowSpan(this.dgvByteSweep, 2);
            this.tlpByteSweep.Controls.Add(this.bByteSweepFolderAdd, 0, 2);
            this.tpByteSweepList.Controls.Add(this.tlpByteSweep);
            this.tcAutomation.Controls.Add(this.tpByteSweepList);

            this.cmsSocketListByteSweep = new ToolStripMenuItem(UiText("UI_AddToSweep"));
            this.cmsSocketListByteSweep.Click += this.cmsSocketListByteSweep_Click;
            int insertIndex = this.cmsSocketList.Items.IndexOf(this.cmsSocketList_tss1);
            this.cmsSocketList.Items.Insert(Math.Max(0, insertIndex), this.cmsSocketListByteSweep);

            Socket_Cache.ByteSweepList.lstFolders.ListChanged += this.ByteSweepFolders_ListChanged;
            Socket_Cache.ByteSweepList.lstPresets.ListChanged += this.ByteSweepPresets_ListChanged;
            this.RefreshByteSweepFolderTree();
        }

        private ToolStripButton CreateByteSweepToolButton(string text, EventHandler handler)
        {
            ToolStripButton button = new ToolStripButton
            {
                Text = text,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = true,
                Overflow = ToolStripItemOverflow.AsNeeded,
                Margin = new Padding(3),
                ToolTipText = text,
                AccessibleName = text
            };
            button.Click += handler;
            return button;
        }

        private void AddByteSweepColumns()
        {
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepOrder",
                HeaderText = UiText("UI_Order"),
                DataPropertyName = "BSortOrder",
                Width = 35
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "cByteSweepEnable",
                HeaderText = UiText("UI_Select"),
                DataPropertyName = "IsEnable",
                ReadOnly = true,
                Width = 40
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepName",
                HeaderText = UiText("UI_Name"),
                DataPropertyName = "BName",
                ReadOnly = true,
                Width = 100
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepLoop",
                HeaderText = UiText("UI_Loop"),
                DataPropertyName = "BLoopCount",
                ReadOnly = true,
                Width = 55
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepInterval",
                HeaderText = UiText("UI_SweepInterval"),
                DataPropertyName = "BInterval",
                ReadOnly = true,
                Width = 65
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepNextInterval",
                HeaderText = UiText("UI_NextInterval"),
                DataPropertyName = "BNextInterval",
                ReadOnly = true,
                Width = 65
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "cByteSweepSend",
                HeaderText = UiText("UI_Send"),
                Text = UiText("UI_Send"),
                UseColumnTextForButtonValue = true,
                Width = 55
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "cByteSweepStop",
                HeaderText = UiText("UI_Stop"),
                Text = UiText("UI_Stop"),
                UseColumnTextForButtonValue = true,
                Width = 55
            });
        }

        private void RefreshByteSweepFolderTree()
        {
            if (this.tvByteSweepFolders == null)
            {
                return;
            }

            string selected = this.selectedByteSweepFolder;
            this.tvByteSweepFolders.BeginUpdate();
            this.tvByteSweepFolders.Nodes.Clear();
            foreach (string folder in Socket_Cache.ByteSweepList.lstFolders)
            {
                this.tvByteSweepFolders.Nodes.Add(new TreeNode(folder) { Tag = folder });
            }

            TreeNode node = this.tvByteSweepFolders.Nodes.Cast<TreeNode>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, selected, StringComparison.Ordinal));
            if (node == null && this.tvByteSweepFolders.Nodes.Count > 0)
            {
                node = this.tvByteSweepFolders.Nodes[0];
            }

            if (node == null)
            {
                this.selectedByteSweepFolder = "__ALL__";
                this.RefreshByteSweepView();
            }
            else
            {
                this.selectedByteSweepFolder = node.Tag as string;
                this.tvByteSweepFolders.SelectedNode = node;
            }
            this.tvByteSweepFolders.EndUpdate();
        }

        private List<Socket_ByteSweepPresetInfo> GetCurrentByteSweepPresets()
        {
            return Socket_Cache.ByteSweepList.lstPresets
                .Where(item => string.Equals(item.BFolder, this.selectedByteSweepFolder, StringComparison.Ordinal))
                .Select((item, index) => new { Item = item, Index = index })
                .OrderBy(item => item.Item.BSortOrder <= 0 ? int.MaxValue : item.Item.BSortOrder)
                .ThenBy(item => item.Index)
                .Select(item => item.Item)
                .ToList();
        }

        private bool HasSelectedByteSweepFolder()
        {
            return this.selectedByteSweepFolder != "__ALL__" &&
                Socket_Cache.ByteSweepList.lstFolders.Any(item =>
                    string.Equals(item, this.selectedByteSweepFolder, StringComparison.Ordinal));
        }

        private void RefreshByteSweepView()
        {
            if (this.dgvByteSweep == null)
            {
                return;
            }

            List<Socket_ByteSweepPresetInfo> items = this.GetCurrentByteSweepPresets();
            for (int index = 0; index < items.Count; index++)
            {
                items[index].BSortOrder = index + 1;
            }
            this.dgvByteSweep.DataSource = new BindingList<Socket_ByteSweepPresetInfo>(items);
            this.SelectActiveByteSweepPresetRow();

            bool folderSelected = this.HasSelectedByteSweepFolder();
            bool allSelected = items.Count > 0 && items.All(item => item.IsEnable);
            int selectedCount = items.Count(item => item.IsEnable);
            long planned = items.Where(item => item.IsEnable).Sum(item => item.TotalSend);
            string socketState = UiText(
                Socket_Cache.System.SystemSocket > 0 ? "UI_SocketSet" : "UI_SocketNotSet");
            string resultState = this.byteSweepTotalSend > 0
                ? string.Format(UiText("UI_ResultState"), this.byteSweepSuccess, this.byteSweepFailure)
                : string.Empty;
            this.tsByteSweepAdd.Enabled = folderSelected && !this.byteSweepRunning;
            this.tsByteSweepSelectAll.Enabled = folderSelected && items.Count > 0 && !this.byteSweepRunning;
            this.tsByteSweepParallel.Enabled = !this.byteSweepRunning;
            this.tsByteSweepSelectAll.Text =
                UiText(allSelected ? "UI_ClearSelection" : "UI_SelectAll");
            this.tsByteSweepStart.Enabled = folderSelected && selectedCount > 0 && !this.byteSweepRunning;
            this.tsByteSweepStop.Enabled = this.byteSweepRunning;
            this.dgvByteSweep.Columns["cByteSweepOrder"].ReadOnly =
                this.byteSweepRunning;
            this.tsByteSweepContext.Text = folderSelected
                ? string.Format(UiText("UI_SweepContext"),
                    ShortText(this.selectedByteSweepFolder, 10), selectedCount, planned, socketState) + resultState
                : UiText("UI_NoGroup");
            this.tsByteSweepContext.ToolTipText = string.Format(
                UiText("UI_SweepContextTip"),
                folderSelected ? this.selectedByteSweepFolder : UiText("UI_NotSelected"),
                selectedCount,
                planned,
                this.byteSweepTotalSend,
                this.byteSweepSuccess,
                this.byteSweepFailure,
                socketState);
            this.dgvByteSweep.Invalidate();
        }

        private void dgvByteSweep_Paint(object sender, PaintEventArgs e)
        {
            if (this.dgvByteSweep.Rows.Count > 0)
            {
                return;
            }

            string emptyText = this.HasSelectedByteSweepFolder()
                ? UiText("UI_ByteSweepEmpty")
                : UiText("UI_NoGroupTip");
            Rectangle contentBounds = this.dgvByteSweep.ClientRectangle;
            contentBounds.Y += this.dgvByteSweep.ColumnHeadersHeight;
            contentBounds.Height -= this.dgvByteSweep.ColumnHeadersHeight;
            TextRenderer.DrawText(
                e.Graphics,
                emptyText,
                this.dgvByteSweep.Font,
                contentBounds,
                SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
        }

        private void SelectActiveByteSweepPresetRow()
        {
            if (!this.byteSweepRunning ||
                this.byteSweepParallelMode ||
                this.activeByteSweepPresetId == Guid.Empty ||
                this.dgvByteSweep == null)
            {
                return;
            }

            foreach (DataGridViewRow row in this.dgvByteSweep.Rows)
            {
                Socket_ByteSweepPresetInfo preset =
                    row.DataBoundItem as Socket_ByteSweepPresetInfo;
                if (preset == null || preset.BID != this.activeByteSweepPresetId)
                {
                    continue;
                }

                row.Selected = true;
                DataGridViewCell firstVisibleCell = row.Cells
                    .Cast<DataGridViewCell>()
                    .FirstOrDefault(cell => cell.Visible);
                if (firstVisibleCell != null)
                {
                    this.dgvByteSweep.CurrentCell = firstVisibleCell;
                }
                if (row.Index >= 0 && row.Index < this.dgvByteSweep.Rows.Count)
                {
                    this.dgvByteSweep.FirstDisplayedScrollingRowIndex = row.Index;
                }
                return;
            }
        }

        private void UpdateByteSweepParallelModeText()
        {
            if (this.tsByteSweepParallel == null)
            {
                return;
            }

            this.tsByteSweepParallel.Text = UiText(
                this.tsByteSweepParallel.Checked ? "UI_ConcurrentSend" : "UI_SequentialSend");
            this.tsByteSweepParallel.ToolTipText = UiText(
                this.tsByteSweepParallel.Checked ? "UI_ConcurrentSendTip" : "UI_SequentialSendTip");
        }

        private static string ShortText(string value, int maxLength)
        {
            return value != null && value.Length > maxLength ? value.Substring(0, maxLength) + "…" : value;
        }

        private void tvByteSweepFolders_AfterSelect(object sender, TreeViewEventArgs e)
        {
            this.selectedByteSweepFolder = e.Node.Tag as string ?? "__ALL__";
            this.RefreshByteSweepView();
        }

        private void ByteSweepFolders_ListChanged(object sender, ListChangedEventArgs e)
        {
            this.RefreshByteSweepFolderTree();
        }

        private void ByteSweepPresets_ListChanged(object sender, ListChangedEventArgs e)
        {
            this.RefreshByteSweepView();
        }

        private void bByteSweepFolderAdd_Click(object sender, EventArgs e)
        {
            string folder = this.PromptForText(
                UiText("UI_NewSweepGroup"), UiText("UI_GroupName"), string.Empty);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            bool added = false;
            bool saved = Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                () => added = Socket_Cache.ByteSweepList.AddFolder(folder.Trim()));
            if (!added)
            {
                MessageBox.Show(this, UiText("UI_GroupExists"), UiText("UI_SweepGroups"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!saved)
            {
                MessageBox.Show(this, UiText("UI_PresetSaveFailed"), UiText("UI_Save"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            this.selectedByteSweepFolder = folder.Trim();
            this.RefreshByteSweepFolderTree();
        }

        private void cmsByteSweepFolder_Rename_Click(object sender, EventArgs e)
        {
            if (this.byteSweepRunning || !this.HasSelectedByteSweepFolder())
            {
                return;
            }

            string oldName = this.selectedByteSweepFolder;
            string newName = this.PromptForText(
                UiText("UI_RenameSweepGroup"), UiText("UI_GroupName"), oldName);
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(oldName, newName.Trim(), StringComparison.Ordinal))
            {
                return;
            }

            if (Socket_Cache.ByteSweepList.lstFolders.Any(item =>
                string.Equals(item, newName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, UiText("UI_GroupExists"), UiText("UI_SweepGroups"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                () => Socket_Cache.ByteSweepList.RenameFolder(oldName, newName.Trim())))
            {
                MessageBox.Show(this, UiText("UI_PresetSaveFailed"), UiText("UI_Save"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            this.selectedByteSweepFolder = newName.Trim();
            this.RefreshByteSweepFolderTree();
        }

        private void cmsByteSweepFolder_Delete_Click(object sender, EventArgs e)
        {
            if (this.byteSweepRunning || !this.HasSelectedByteSweepFolder())
            {
                return;
            }

            if (Socket_Cache.ByteSweepList.lstPresets.Any(item =>
                string.Equals(item.BFolder, this.selectedByteSweepFolder, StringComparison.Ordinal)))
            {
                MessageBox.Show(this, UiText("UI_GroupHasSweepPresets"), UiText("UI_DeleteSweepGroup"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                () => Socket_Cache.ByteSweepList.RemoveFolder(this.selectedByteSweepFolder)))
            {
                MessageBox.Show(this, UiText("UI_PresetSaveFailed"), UiText("UI_Save"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            this.selectedByteSweepFolder = "__ALL__";
            this.RefreshByteSweepFolderTree();
        }

        private void tsByteSweepAdd_Click(object sender, EventArgs e)
        {
            Socket_PacketInfo packet = this.GetCurrentEditedPacket();
            if (packet == null || packet.PacketBuffer == null || packet.PacketBuffer.Length == 0)
            {
                MessageBox.Show(this, UiText("UI_SelectPacketFirst"), UiText("ByteSweep_NewTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int start = this.hbPacketData.SelectionLength > 0 ? (int)this.hbPacketData.SelectionStart : 0;
            int length = this.hbPacketData.SelectionLength > 0 ? (int)this.hbPacketData.SelectionLength : 1;
            this.CreateByteSweepPreset(packet, start, length, 1000, this.selectedByteSweepFolder);
        }

        private void cmsSocketListByteSweep_Click(object sender, EventArgs e)
        {
            Socket_PacketInfo packet = this.GetCurrentEditedPacket();
            if (packet == null)
            {
                return;
            }

            string folder = this.HasSelectedByteSweepFolder()
                ? this.selectedByteSweepFolder
                : Socket_Cache.ByteSweepList.lstFolders.FirstOrDefault();
            int start = this.hbPacketData.SelectionLength > 0 ? (int)this.hbPacketData.SelectionStart : 0;
            int length = this.hbPacketData.SelectionLength > 0 ? (int)this.hbPacketData.SelectionLength : 1;
            this.CreateByteSweepPreset(packet, start, length, 1000, folder);
        }

        internal void CreateByteSweepPreset(
            Socket_PacketInfo packet,
            int start,
            int length,
            int interval,
            string folder)
        {
            if (packet == null || packet.PacketBuffer == null || packet.PacketBuffer.Length == 0)
            {
                return;
            }

            Socket_ByteSweepPresetInfo value = new Socket_ByteSweepPresetInfo
            {
                BID = Guid.NewGuid(),
                BName = UiText("UI_DefaultSweepPresetName") +
                    (Socket_Cache.ByteSweepList.lstPresets.Count + 1),
                BFolder = folder ?? string.Empty,
                BStart = Math.Max(0, Math.Min(start, packet.PacketBuffer.Length - 1)),
                BLength = Math.Max(1, Math.Min(length, packet.PacketBuffer.Length - Math.Max(0, start))),
                BLoopCount = 1,
                BInterval = Math.Max(0, interval),
                BNextInterval = 0,
                PacketType = packet.PacketType,
                PacketFrom = packet.PacketFrom,
                PacketTo = packet.PacketTo,
                Buffer = (byte[])packet.PacketBuffer.Clone(),
                ByteAnnotations = Socket_ByteAnnotationEngine.Clone(packet.ByteAnnotations)
            };

            using (Socket_ByteSweepPresetForm dialog = new Socket_ByteSweepPresetForm(value, true))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                        () => Socket_Cache.ByteSweepList.AddPreset(dialog.Result)))
                    {
                        MessageBox.Show(this, UiText("UI_PresetSaveFailed"), UiText("UI_Save"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    this.selectedByteSweepFolder = dialog.Result.BFolder;
                    this.RefreshByteSweepFolderTree();
                    this.tcAutomation.SelectedTab = this.tpByteSweepList;
                }
            }
        }

        private Socket_ByteSweepPresetInfo GetSelectedByteSweepPreset()
        {
            return this.dgvByteSweep != null && this.dgvByteSweep.SelectedRows.Count > 0
                ? this.dgvByteSweep.SelectedRows[0].DataBoundItem as Socket_ByteSweepPresetInfo
                : null;
        }

        private void EditSelectedByteSweepPreset()
        {
            Socket_ByteSweepPresetInfo preset = this.GetSelectedByteSweepPreset();
            if (preset == null || this.byteSweepRunning)
            {
                return;
            }

            this.CommitPacketDataEdits();
            using (Socket_ByteSweepPresetForm dialog = new Socket_ByteSweepPresetForm(preset))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                        () => Socket_Cache.ByteSweepList.UpdatePreset(preset, dialog.Result)))
                    {
                        MessageBox.Show(this, UiText("UI_PresetSaveFailed"), UiText("UI_Save"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    this.selectedByteSweepFolder = preset.BFolder;
                    this.RefreshByteSweepFolderTree();
                    if (dialog.EditDetailsRequested)
                    {
                        Socket_PacketInfo packet = CreatePacketFromByteSweepPreset(preset);
                        using (Socket_SendForm sendForm =
                            new Socket_SendForm(packet, preset))
                        {
                            sendForm.ShowDialog(this);
                        }
                        this.selectedByteSweepFolder = preset.BFolder;
                        this.RefreshByteSweepFolderTree();
                    }
                }
            }
        }

        private static Socket_PacketInfo CreatePacketFromByteSweepPreset(
            Socket_ByteSweepPresetInfo preset)
        {
            byte[] buffer = preset.Buffer == null
                ? new byte[0]
                : (byte[])preset.Buffer.Clone();
            return new Socket_PacketInfo
            {
                PacketTime = DateTime.Now,
                PacketSocket = Socket_Cache.System.SystemSocket,
                PacketType = preset.PacketType,
                PacketFrom = preset.PacketFrom,
                PacketTo = preset.PacketTo,
                RawBuffer = (byte[])buffer.Clone(),
                PacketBuffer = buffer,
                PacketData = Socket_Operation.GetPacketData_Hex(
                    buffer.AsSpan(),
                    Socket_Cache.SocketPacket.PacketData_MaxLen),
                PacketLen = buffer.Length,
                ByteAnnotations =
                    Socket_ByteAnnotationEngine.Clone(preset.ByteAnnotations)
            };
        }

        private void cmsByteSweepPreset_Edit_Click(object sender, EventArgs e)
        {
            this.EditSelectedByteSweepPreset();
        }

        private void cmsByteSweepPreset_Delete_Click(object sender, EventArgs e)
        {
            Socket_ByteSweepPresetInfo preset = this.GetSelectedByteSweepPreset();
            if (preset == null || this.byteSweepRunning)
            {
                return;
            }

            if (MessageBox.Show(this, UiText("UI_ConfirmDeleteSweepPreset"), UiText("UI_DeleteSweepPreset"),
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
            {
                if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                    () => Socket_Cache.ByteSweepList.lstPresets.Remove(preset)))
                {
                    MessageBox.Show(this, UiText("UI_PresetSaveFailed"), UiText("UI_Save"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                this.RefreshByteSweepView();
            }
        }

        private void tsByteSweepSelectAll_Click(object sender, EventArgs e)
        {
            List<Socket_ByteSweepPresetInfo> items = this.GetCurrentByteSweepPresets();
            bool select = !items.Any() || !items.All(item => item.IsEnable);
            if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(() =>
            {
                foreach (Socket_ByteSweepPresetInfo item in items)
                {
                    item.IsEnable = select;
                }
            }))
            {
                MessageBox.Show(this, UiText("UI_PresetSaveFailed"), UiText("UI_Save"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            this.RefreshByteSweepView();
        }

        private void dgvByteSweep_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            Socket_ByteSweepPresetInfo preset = this.dgvByteSweep.Rows[e.RowIndex].DataBoundItem as Socket_ByteSweepPresetInfo;
            string columnName = this.dgvByteSweep.Columns[e.ColumnIndex].Name;
            if (preset == null)
            {
                return;
            }

            if (columnName == "cByteSweepEnable" && !this.byteSweepRunning)
            {
                if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(
                    () => preset.IsEnable = !preset.IsEnable))
                {
                    MessageBox.Show(this, UiText("UI_PresetSaveFailed"), UiText("UI_Save"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                this.RefreshByteSweepView();
            }
            else if (columnName == "cByteSweepSend")
            {
                if (!this.byteSweepRunning)
                {
                    this.StartByteSweepPresets(new[] { preset });
                }
            }
            else if (columnName == "cByteSweepStop" && this.byteSweepRunning &&
                this.activeByteSweepPresetId == preset.BID)
            {
                this.StopByteSweep();
            }
        }

        private void dgvByteSweep_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                this.EditSelectedByteSweepPreset();
            }
        }

        private void dgvByteSweep_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (this.byteSweepRunning)
            {
                this.RefreshByteSweepView();
                return;
            }

            if (e.RowIndex < 0 || this.dgvByteSweep.Columns[e.ColumnIndex].Name != "cByteSweepOrder")
            {
                return;
            }

            Socket_ByteSweepPresetInfo edited =
                this.dgvByteSweep.Rows[e.RowIndex].DataBoundItem as Socket_ByteSweepPresetInfo;
            if (edited == null)
            {
                return;
            }

            int requestedOrder = Math.Max(1, edited.BSortOrder);
            List<Socket_ByteSweepPresetInfo> items = this.dgvByteSweep.Rows
                .Cast<DataGridViewRow>()
                .Select(row => row.DataBoundItem as Socket_ByteSweepPresetInfo)
                .Where(item => item != null && item != edited)
                .ToList();
            items.Insert(Math.Min(requestedOrder - 1, items.Count), edited);
            if (!Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(() =>
            {
                for (int index = 0; index < items.Count; index++)
                {
                    items[index].BSortOrder = index + 1;
                }
            }))
            {
                MessageBox.Show(this, UiText("UI_PresetSaveFailed"), UiText("UI_Save"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            this.RefreshByteSweepView();
        }

        private void dgvByteSweep_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                this.dgvByteSweep.Columns[e.ColumnIndex].Name == "cByteSweepOrder")
            {
                e.ThrowException = false;
                e.Cancel = true;
            }
        }

        private void dgvByteSweep_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            Socket_ByteSweepPresetInfo preset = this.dgvByteSweep.Rows[e.RowIndex].DataBoundItem as Socket_ByteSweepPresetInfo;
            if (preset == null)
            {
                return;
            }

            string column = this.dgvByteSweep.Columns[e.ColumnIndex].Name;
            bool active = this.byteSweepRunning && preset.BID == this.activeByteSweepPresetId;
            if (active)
            {
                e.CellStyle.BackColor = Color.LightGoldenrodYellow;
                e.CellStyle.SelectionBackColor = Color.DarkOrange;
                e.CellStyle.SelectionForeColor = Color.Black;
                e.CellStyle.ForeColor = SystemColors.ControlText;
            }
            if (column == "cByteSweepSend")
            {
                e.Value = active ? UiText("UI_SweepActive") : UiText("UI_Send");
                e.CellStyle.ForeColor = active || !this.byteSweepRunning
                    ? SystemColors.ControlText
                    : SystemColors.GrayText;
            }
            else if (column == "cByteSweepStop")
            {
                e.CellStyle.ForeColor = active ? SystemColors.ControlText : SystemColors.GrayText;
            }
        }

        private void dgvByteSweep_SelectionChanged(object sender, EventArgs e)
        {
            if (this.byteSweepRunning)
            {
                return;
            }

            Socket_ByteSweepPresetInfo preset = this.GetSelectedByteSweepPreset();
            if (preset == null || preset.Buffer == null)
            {
                return;
            }

            this.CommitPacketDataEdits();
            this.ReleasePacketDataEditor();
            this.byteSweepEditingPreset = preset;
            this.packetDataEditingPacket = new Socket_PacketInfo(
                DateTime.Now,
                0,
                preset.PacketType,
                preset.PacketFrom,
                preset.PacketTo,
                (byte[])preset.Buffer.Clone(),
                (byte[])preset.Buffer.Clone(),
                preset.Buffer.Length,
                Socket_Cache.Filter.FilterAction.None);
            this.packetDataEditingPacket.ByteAnnotations = Socket_ByteAnnotationEngine.Clone(preset.ByteAnnotations);
            Socket_AnnotatedByteProvider provider = new Socket_AnnotatedByteProvider(
                this.packetDataEditingPacket.PacketBuffer,
                this.packetDataEditingPacket.ByteAnnotations);
            provider.Changed += this.PacketDataProvider_Changed;
            provider.LengthChanged += this.PacketDataProvider_Changed;
            this.hbPacketData.ByteProvider = provider;
            if (preset.IsValid)
            {
                this.hbPacketData.Select(preset.BStart, preset.BLength);
                this.hbPacketData.ScrollByteIntoView(preset.BStart);
            }
        }

        private void CommitByteSweepPresetBuffer(byte[] buffer)
        {
            if (this.byteSweepEditingPreset == null || buffer == null)
            {
                return;
            }

            this.byteSweepEditingPreset.Buffer = (byte[])buffer.Clone();
            this.byteSweepEditingPreset.ByteAnnotations = Socket_ByteAnnotationEngine.Clone(this.packetDataEditingPacket.ByteAnnotations);
            this.dgvByteSweep?.Invalidate();
        }

        private void UpdateByteSweepLivePreview(
            Socket_ByteSweepPresetInfo preset,
            Socket_ByteSweepProgress progress)
        {
            if (this.byteSweepEditingPreset == null ||
                this.byteSweepEditingPreset.BID != preset.BID ||
                this.hbPacketData == null ||
                !(this.hbPacketData.ByteProvider is Socket_AnnotatedByteProvider provider) ||
                preset.Buffer == null ||
                provider.Length != preset.Buffer.Length)
            {
                return;
            }

            try
            {
                this.byteSweepLivePreviewUpdating = true;
                for (long index = 0; index < provider.Length; index++)
                {
                    byte value = preset.Buffer[(int)index];
                    if (provider.ReadByte(index) != value)
                    {
                        provider.WriteByte(index, value);
                    }
                }

                if (progress.IsPairCombination)
                {
                    if (progress.PairFirstPosition >= 0 && progress.PairFirstPosition < provider.Length)
                    {
                        provider.WriteByte(
                            progress.PairFirstPosition,
                            progress.PairFirstValue);
                    }
                    if (progress.PairSecondPosition >= 0 && progress.PairSecondPosition < provider.Length)
                    {
                        provider.WriteByte(
                            progress.PairSecondPosition,
                            progress.PairSecondValue);
                    }
                }
                else if (progress.Position >= 0 && progress.Position < provider.Length)
                {
                    provider.WriteByte(progress.Position, progress.CurrentValue);
                }

                this.SetByteSweepLiveSelection(
                    progress.IsPairCombination
                        ? progress.PairFirstPosition
                        : progress.Position,
                    1);
                this.hbPacketData.Invalidate();
            }
            finally
            {
                this.byteSweepLivePreviewUpdating = false;
            }
        }

        private void RestoreByteSweepLivePreview(Socket_ByteSweepPresetInfo preset)
        {
            if (preset == null ||
                preset.Buffer == null ||
                this.byteSweepEditingPreset == null ||
                this.byteSweepEditingPreset.BID != preset.BID ||
                !(this.hbPacketData.ByteProvider is Socket_AnnotatedByteProvider provider) ||
                provider.Length != preset.Buffer.Length)
            {
                return;
            }

            try
            {
                this.byteSweepLivePreviewUpdating = true;
                for (long index = 0; index < provider.Length; index++)
                {
                    byte value = preset.Buffer[(int)index];
                    if (provider.ReadByte(index) != value)
                    {
                        provider.WriteByte(index, value);
                    }
                }
                provider.ApplyChanges();
                this.hbPacketData.Invalidate();
            }
            finally
            {
                this.byteSweepLivePreviewUpdating = false;
            }
        }

        private void SetByteSweepLiveSelection(long start, long length)
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (!this.byteSweepRunning ||
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
            if (!this.byteSweepRunning || this.byteSweepSelectionGuard)
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
                : this.byteSweepOriginalSelectionStart;
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

        private void RestoreByteSweepLiveSelection()
        {
            IByteProvider provider = this.hbPacketData.ByteProvider;
            if (provider == null || provider.Length == 0)
            {
                this.byteSweepLiveSelectionStart = -1;
                this.byteSweepLiveSelectionLength = 1;
                return;
            }

            long start = Math.Min(
                Math.Max(this.byteSweepOriginalSelectionStart, 0),
                provider.Length - 1);
            long length = Math.Min(
                Math.Max(this.byteSweepOriginalSelectionLength, 0),
                provider.Length - start);

            this.byteSweepSelectionGuard = true;
            try
            {
                this.hbPacketData.Select(start, length);
                this.hbPacketData.ScrollByteIntoView(start);
            }
            finally
            {
                this.byteSweepSelectionGuard = false;
                this.byteSweepLiveSelectionStart = -1;
                this.byteSweepLiveSelectionLength = 1;
            }
        }

        private void hbPacketData_ByteSweepSelectionChanged(object sender, EventArgs e)
        {
            if (this.byteSweepRunning && !this.byteSweepSelectionGuard)
            {
                this.KeepByteSweepLiveSelection();
            }
        }

        private void tsByteSweepStart_Click(object sender, EventArgs e)
        {
            this.StartByteSweepPresets(this.GetCurrentByteSweepPresets().Where(item => item.IsEnable));
        }

        private void tsByteSweepParallel_Click(object sender, EventArgs e)
        {
            this.UpdateByteSweepParallelModeText();
        }

        private void tsByteSweepStop_Click(object sender, EventArgs e)
        {
            this.StopByteSweep();
        }

        private static Dictionary<Guid, Socket_Cache.SocketList.CurrentSocketRoute> ResolveByteSweepRoutes(
            IEnumerable<Socket_ByteSweepPresetInfo> presets,
            out Socket_ByteSweepPresetInfo unresolvedPreset,
            out Socket_Cache.SocketList.CurrentSocketRouteResolution unresolvedResolution)
        {
            List<Socket_ByteSweepPresetInfo> items = presets == null
                ? new List<Socket_ByteSweepPresetInfo>()
                : presets.Where(item => item != null).ToList();
            Dictionary<Guid, Socket_Cache.SocketList.CurrentSocketRoute> resolvedRoutes =
                new Dictionary<Guid, Socket_Cache.SocketList.CurrentSocketRoute>();
            unresolvedPreset = null;
            unresolvedResolution = null;

            List<Socket_PacketInfo> templates = items
                .Select(preset => new Socket_PacketInfo
                {
                    PacketType = preset.PacketType,
                    PacketFrom = preset.PacketFrom,
                    PacketTo = preset.PacketTo
                })
                .ToList();
            Socket_Cache.SocketList.CurrentSocketRoutesResolution resolution =
                Socket_Cache.SocketList.ResolveCurrentRoutes(templates);

            for (int index = 0; index < items.Count; index++)
            {
                Socket_ByteSweepPresetInfo preset = items[index];
                Socket_Cache.SocketList.CurrentSocketRouteResolution itemResolution =
                    resolution.Items[index];
                Socket_Cache.SocketList.LogCurrentRouteResolution(
                    preset.BID,
                    1,
                    templates[index],
                    itemResolution);
                if (!itemResolution.Succeeded)
                {
                    unresolvedPreset = preset;
                    unresolvedResolution = itemResolution;
                    return null;
                }

                resolvedRoutes[preset.BID] = itemResolution.Route;
            }

            return resolvedRoutes;
        }

        private async Task<Socket_ByteSweepResult> ExecuteByteSweepPresetAsync(
            Socket_ByteSweepPresetInfo preset,
            Socket_Cache.SocketList.CurrentSocketRoute route,
            CancellationToken cancellationToken)
        {
            Socket_ByteSweepResult aggregate = new Socket_ByteSweepResult();
            for (int loop = 1; loop <= preset.BLoopCount; loop++)
            {
                int currentLoop = loop;
                Socket_ByteSweepResult result = await Task.Run(() =>
                    preset.BMode == Socket_ByteSweepMode.PairCombination
                        ? Socket_ByteSweepEngine.ExecutePairCombination(
                            preset.Buffer,
                            preset.BCombinationFirstPosition,
                            preset.BCombinationFirstLength,
                            preset.BCombinationFirstInterval,
                            preset.BCombinationSecondPosition,
                            preset.BCombinationSecondLength,
                            preset.BCombinationSecondInterval,
                            buffer => Socket_Operation.SendPacket(
                                route.Socket,
                                preset.PacketType,
                                route.PacketFrom,
                                route.PacketTo,
                                buffer),
                            cancellationToken,
                            progress => this.PostByteSweepPresetProgress(
                                preset, progress, currentLoop, preset.BLoopCount))
                        : Socket_ByteSweepEngine.Execute(
                            preset.Buffer,
                            preset.BStart,
                            preset.BLength,
                            preset.BInterval,
                            buffer => Socket_Operation.SendPacket(
                                route.Socket,
                                preset.PacketType,
                                route.PacketFrom,
                                route.PacketTo,
                                buffer),
                            cancellationToken,
                            progress => this.PostByteSweepPresetProgress(
                                preset, progress, currentLoop, preset.BLoopCount)));
                aggregate.TotalSend += result.TotalSend;
                aggregate.Success += result.Success;
                aggregate.Failure += result.Failure;
                if (result.Cancelled)
                {
                    aggregate.Cancelled = true;
                    break;
                }
            }

            return aggregate;
        }

        private async void StartByteSweepPresets(IEnumerable<Socket_ByteSweepPresetInfo> source)
        {
            Guid jobId;
            Task execution;
            string error;
            if (!this.TryBeginByteSweepPresets(source, out jobId, out execution, out error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    MessageBox.Show(
                        this,
                        error,
                        UiText("ByteSweep_BatchTitle"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            await execution;
        }

        private bool TryBeginByteSweepPresets(
            IEnumerable<Socket_ByteSweepPresetInfo> source,
            out Guid jobId,
            out Task execution,
            out string error,
            string revision = null)
        {
            jobId = Guid.Empty;
            execution = null;
            error = string.Empty;

            List<Socket_ByteSweepPresetInfo> presets = source
                .Where(item => item != null)
                .OrderBy(item => item.BSortOrder)
                .Select(item => item.Clone())
                .ToList();
            if (this.byteSweepRunning || Socket_ByteSweepRuntime.Current.IsBusy)
            {
                error = UiText("ByteSweep_RuntimeBusy");
                return false;
            }

            if (presets.Count == 0)
            {
                error = UiText("UI_CurrentSocketRequired");
                return false;
            }

            this.byteSweepParallelMode = this.tsByteSweepParallel != null &&
                this.tsByteSweepParallel.Checked;
            this.lastByteSweepRouteErrorCode = string.Empty;

            Socket_ByteSweepPresetInfo invalid = presets.FirstOrDefault(item => !item.IsValid);
            if (invalid != null)
            {
                error = string.Format(UiText("ByteSweep_BatchInvalid"), invalid.BName);
                return false;
            }

            Socket_ByteSweepPresetInfo unresolvedPreset;
            Socket_Cache.SocketList.CurrentSocketRouteResolution unresolvedResolution;
            Dictionary<Guid, Socket_Cache.SocketList.CurrentSocketRoute> presetRoutes =
                ResolveByteSweepRoutes(
                presets,
                out unresolvedPreset,
                out unresolvedResolution);
            if (presetRoutes == null)
            {
                this.lastByteSweepRouteErrorCode = unresolvedResolution == null
                    ? "runtime_not_connected"
                    : unresolvedResolution.ErrorCode;
                error = unresolvedResolution == null ||
                    string.IsNullOrWhiteSpace(unresolvedResolution.ErrorMessage)
                    ? UiText("UI_CurrentSocketRequired")
                    : unresolvedResolution.ErrorMessage;
                return false;
            }

            long plannedTotal = presets.Sum(item =>
                Math.Max(1, (long)item.BLoopCount) *
                (item.BMode == Socket_ByteSweepMode.PairCombination
                    ? Math.Max(1L, (long)item.BCombinationFirstLength * item.BCombinationSecondLength)
                    : Math.Max(1L, (long)item.BLength * 255L)));
            CancellationTokenSource sharedCancellation;
            Socket_ByteSweepPresetInfo firstPreset = presets[0];
            if (!Socket_ByteSweepRuntime.Current.TryStart(
                firstPreset.BID,
                firstPreset.BName,
                firstPreset.BMode == Socket_ByteSweepMode.PairCombination
                    ? UiText("ByteSweep_PairMode")
                    : UiText("ByteSweep_SequentialMode"),
                firstPreset.BLoopCount,
                plannedTotal,
                out jobId,
                out sharedCancellation))
            {
                error = UiText("ByteSweep_RuntimeBusy");
                return false;
            }

            this.byteSweepRunning = true;
            this.byteSweepOriginalSelectionStart = this.hbPacketData.SelectionStart;
            this.byteSweepOriginalSelectionLength = this.hbPacketData.SelectionLength;
            this.byteSweepLiveSelectionStart = this.byteSweepOriginalSelectionStart;
            this.byteSweepLiveSelectionLength = Math.Max(1, this.byteSweepOriginalSelectionLength);
            this.byteSweepJobId = jobId;
            this.byteSweepPlannedTotal = plannedTotal;
            this.byteSweepTotalSend = 0;
            this.byteSweepSuccess = 0;
            this.byteSweepFailure = 0;
            this.byteSweepCts = sharedCancellation;
            this.activeByteSweepPresetId = this.byteSweepParallelMode
                ? Guid.Empty
                : firstPreset.BID;
            this.RefreshByteSweepView();
            // Publish the revision before the worker can report its first runtime
            // snapshot. Mobile polling must never observe a busy job with an empty
            // revision and reject an otherwise valid action as stale.
            Socket_ByteSweepRuntime.Current.SetRevision(jobId, revision);
            Socket_ByteSweepRuntime.Current.MarkRunning(jobId);
            execution = this.RunByteSweepPresetsAsync(presets, presetRoutes, jobId);
            return true;
        }

        private async Task RunByteSweepPresetsAsync(
            List<Socket_ByteSweepPresetInfo> presets,
            Dictionary<Guid, Socket_Cache.SocketList.CurrentSocketRoute> presetRoutes,
            Guid jobId)
        {
            Exception runtimeError = null;

            try
            {
                if (this.byteSweepParallelMode)
                {
                    Task<Socket_ByteSweepResult>[] tasks = presets
                        .Select(preset => this.ExecuteByteSweepPresetAsync(
                            preset,
                            presetRoutes[preset.BID],
                            this.byteSweepCts.Token))
                        .ToArray();
                    Socket_ByteSweepResult[] results = await Task.WhenAll(tasks);
                    foreach (Socket_ByteSweepResult result in results)
                    {
                        this.byteSweepTotalSend += result.TotalSend;
                        this.byteSweepSuccess += result.Success;
                        this.byteSweepFailure += result.Failure;
                    }
                }
                else
                {
                    for (int presetIndex = 0; presetIndex < presets.Count; presetIndex++)
                    {
                        Socket_ByteSweepPresetInfo preset = presets[presetIndex];
                        Socket_Cache.SocketList.CurrentSocketRoute route =
                            presetRoutes[preset.BID];
                        if (this.byteSweepCts.IsCancellationRequested)
                        {
                            break;
                        }

                        this.activeByteSweepPresetId = preset.BID;
                        this.RefreshByteSweepView();

                        Socket_ByteSweepResult result = await this.ExecuteByteSweepPresetAsync(
                            preset,
                            route,
                            this.byteSweepCts.Token);
                        this.byteSweepTotalSend += result.TotalSend;
                        this.byteSweepSuccess += result.Success;
                        this.byteSweepFailure += result.Failure;

                        this.RestoreByteSweepLivePreview(preset);

                        if (result.Cancelled || this.byteSweepCts.IsCancellationRequested)
                        {
                            break;
                        }

                        bool hasNextPreset = presetIndex < presets.Count - 1;
                        if (hasNextPreset && preset.BNextInterval > 0)
                        {
                            try
                            {
                                await Task.Delay(preset.BNextInterval, this.byteSweepCts.Token);
                            }
                            catch (TaskCanceledException)
                            {
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                runtimeError = ex;
                Socket_Operation.DoLog(nameof(StartByteSweepPresets), ex.Message);
            }
            finally
            {
                bool cancelled = this.byteSweepCts != null && this.byteSweepCts.IsCancellationRequested;
                Socket_ByteSweepRuntime.Current.Finish(
                    jobId,
                    cancelled,
                    runtimeError,
                    runtimeError == null
                        ? (cancelled ? UiText("ByteSweep_LogCancelled") : UiText("ByteSweep_LogCompleted"))
                        : runtimeError.Message);
                Socket_ByteSweepPresetInfo activePreset = presets.FirstOrDefault(
                    item => item.BID == this.activeByteSweepPresetId);
                this.RestoreByteSweepLivePreview(activePreset);
                this.RestoreByteSweepLiveSelection();
                this.activeByteSweepPresetId = Guid.Empty;
                this.byteSweepRunning = false;
                if (this.byteSweepCts != null)
                {
                    this.byteSweepCts.Dispose();
                    this.byteSweepCts = null;
                }
                this.byteSweepJobId = Guid.Empty;
                this.byteSweepPlannedTotal = 0;
                this.RefreshByteSweepView();
            }
        }

        private void PostByteSweepPresetProgress(
            Socket_ByteSweepPresetInfo preset,
            Socket_ByteSweepProgress progress,
            int currentLoop,
            int loopCount)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                Socket_ByteSweepRuntime.Current.WaitIfPaused(
                    this.byteSweepJobId,
                    this.byteSweepCts == null ? CancellationToken.None : this.byteSweepCts.Token);
                Socket_ByteSweepRuntime.Current.PublishProgress(
                    this.byteSweepJobId,
                    preset.BID,
                    preset.BName,
                    preset.BMode == Socket_ByteSweepMode.PairCombination
                        ? UiText("ByteSweep_PairMode")
                        : UiText("ByteSweep_SequentialMode"),
                    progress,
                    currentLoop,
                    loopCount,
                    this.byteSweepPlannedTotal);
                this.BeginInvoke((Action)(() =>
                {
                    if (this.IsDisposed)
                    {
                        return;
                    }

                    if (!this.byteSweepRunning ||
                        (!this.byteSweepParallelMode && this.activeByteSweepPresetId != preset.BID))
                    {
                        return;
                    }

                    if (!this.byteSweepParallelMode)
                    {
                        this.UpdateByteSweepLivePreview(preset, progress);
                    }

                    this.tsByteSweepContext.Text = progress.IsPairCombination
                        ? string.Format(
                            UiText("ByteSweep_BatchPairProgress"),
                            ShortText(preset.BName, 10),
                            currentLoop,
                            loopCount,
                            progress.PairFirstValue,
                            progress.PairFirstValueNumber,
                            progress.PairFirstValueCount,
                            progress.PairSecondValue,
                            progress.PairSecondValueNumber,
                            progress.PairSecondValueCount,
                            progress.TotalSend,
                            (long)progress.PairFirstValueCount * progress.PairSecondValueCount)
                        : string.Format(
                            UiText("UI_SweepProgress"),
                            ShortText(preset.BName, 10),
                            currentLoop,
                            loopCount,
                            progress.ByteNumber,
                            progress.ByteCount,
                            progress.ValueNumber);
                    this.tsByteSweepContext.ToolTipText = string.Format(
                        UiText("UI_SweepProgressTip"),
                        currentLoop,
                        loopCount,
                        progress.Position,
                        progress.OriginalValue,
                        progress.CurrentValue,
                        progress.TotalSend,
                        progress.Success,
                        progress.Failure);
                    this.dgvByteSweep.Invalidate();
                }));
            }
            catch (InvalidOperationException)
            {
                // 窗口关闭时不再刷新进度。
            }
        }

        private void StopByteSweep()
        {
            if (this.byteSweepJobId != Guid.Empty)
            {
                Socket_ByteSweepRuntime.Current.RequestStop(this.byteSweepJobId);
            }
            else if (Socket_ByteSweepRuntime.Current.IsBusy)
            {
                Socket_ByteSweepRuntime.Current.RequestStop();
            }
            else if (this.byteSweepCts != null)
            {
                this.byteSweepCts.Cancel();
            }
        }

        public MobileByteSweepStartResult StartByteSweepFromMobile(Guid presetId)
        {
            return this.StartByteSweepFromMobile(presetId, string.Empty);
        }

        public MobileByteSweepStartResult StartByteSweepFromMobile(Guid presetId, string revision)
        {
            if (this.IsDisposed || this.Disposing)
            {
                return MobileByteSweepStartResult.Failure(
                    "The packet window is closing.",
                    "runtime_not_connected");
            }

            if (this.InvokeRequired)
            {
                try
                {
                    return (MobileByteSweepStartResult)this.Invoke(
                        new Func<MobileByteSweepStartResult>(() =>
                            this.StartByteSweepFromMobile(presetId, revision)));
                }
                catch (ObjectDisposedException)
                {
                    return MobileByteSweepStartResult.Failure(
                        "The packet window is closing.",
                        "runtime_not_connected");
                }
                catch (InvalidOperationException)
                {
                    return MobileByteSweepStartResult.Failure(
                        "The packet window is not ready.",
                        "runtime_not_connected");
                }
            }

            if (!this.IsHandleCreated)
            {
                return MobileByteSweepStartResult.Failure(
                    "The packet window is not ready.",
                    "runtime_not_connected");
            }

            if (this.byteSweepRunning || Socket_ByteSweepRuntime.Current.IsBusy)
            {
                return MobileByteSweepStartResult.Failure(
                    UiText("ByteSweep_RuntimeBusy"),
                    "runtime_busy");
            }

            Socket_ByteSweepPresetInfo preset = Socket_Cache.ByteSweepList.lstPresets
                .FirstOrDefault(item => item.BID == presetId);
            if (preset == null || !preset.IsValid)
            {
                return MobileByteSweepStartResult.Failure(
                    "The progression preset is invalid or missing.");
            }

            Guid jobId;
            Task execution;
            string error;
            if (!this.TryBeginByteSweepPresets(
                new[] { preset },
                out jobId,
                out execution,
                out error,
                revision))
            {
                string errorCode = !string.IsNullOrWhiteSpace(this.lastByteSweepRouteErrorCode)
                    ? this.lastByteSweepRouteErrorCode
                    : string.Equals(
                    error,
                    UiText("ByteSweep_RuntimeBusy"),
                    StringComparison.Ordinal)
                    ? "runtime_busy"
                    : string.Equals(
                        error,
                        UiText("UI_CurrentSocketRequired"),
                        StringComparison.Ordinal)
                        ? "runtime_not_connected"
                        : "preset_invalid";
                return MobileByteSweepStartResult.Failure(error, errorCode);
            }

            return MobileByteSweepStartResult.Success(jobId);
        }

        public bool StopByteSweepFromMobile()
        {
            if (this.IsDisposed || this.Disposing)
            {
                return false;
            }

            if (this.InvokeRequired)
            {
                try
                {
                    return (bool)this.Invoke(new Func<bool>(this.StopByteSweepFromMobile));
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            if (!this.IsHandleCreated)
            {
                return false;
            }

            bool wasBusy = this.byteSweepRunning || Socket_ByteSweepRuntime.Current.IsBusy;
            this.StopByteSweep();
            return wasBusy;
        }

    }
}
