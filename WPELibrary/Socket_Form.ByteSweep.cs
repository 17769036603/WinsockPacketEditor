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
        private Guid activeByteSweepPresetId = Guid.Empty;
        private long byteSweepTotalSend;
        private long byteSweepSuccess;
        private long byteSweepFailure;

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
            this.tsByteSweepStart = CreateByteSweepToolButton(UiText("UI_SendSelected"), this.tsByteSweepStart_Click);
            this.tsByteSweepStop = CreateByteSweepToolButton(UiText("UI_StopBatch"), this.tsByteSweepStop_Click);
            this.tsByteSweepContext = new ToolStripLabel { Alignment = ToolStripItemAlignment.Right };
            this.tsByteSweep.Items.AddRange(new ToolStripItem[]
            {
                this.tsByteSweepAdd,
                this.tsByteSweepSelectAll,
                this.tsByteSweepStart,
                this.tsByteSweepStop,
                this.tsByteSweepContext
            });

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
                AccessibleName = UiText("UI_ByteSweep")
            };
            this.AddByteSweepColumns();
            this.dgvByteSweep.CellContentClick += this.dgvByteSweep_CellContentClick;
            this.dgvByteSweep.CellDoubleClick += this.dgvByteSweep_CellDoubleClick;
            this.dgvByteSweep.CellEndEdit += this.dgvByteSweep_CellEndEdit;
            this.dgvByteSweep.CellFormatting += this.dgvByteSweep_CellFormatting;
            this.dgvByteSweep.SelectionChanged += this.dgvByteSweep_SelectionChanged;

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
                Margin = new Padding(3)
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
                Width = 40
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "cByteSweepEnable",
                HeaderText = UiText("UI_Select"),
                DataPropertyName = "IsEnable",
                ReadOnly = true,
                Width = 42
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepName",
                HeaderText = UiText("UI_Name"),
                DataPropertyName = "BName",
                ReadOnly = true,
                Width = 110
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepRange",
                HeaderText = UiText("UI_Range"),
                DataPropertyName = "RangeText",
                ReadOnly = true,
                Width = 75
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepLoop",
                HeaderText = UiText("UI_Loop"),
                DataPropertyName = "BLoopCount",
                ReadOnly = true,
                Width = 60
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepInterval",
                HeaderText = UiText("UI_SweepInterval"),
                DataPropertyName = "BInterval",
                ReadOnly = true,
                Width = 90
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "cByteSweepNextInterval",
                HeaderText = UiText("UI_NextInterval"),
                DataPropertyName = "BNextInterval",
                ReadOnly = true,
                Width = 90
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "cByteSweepSend",
                HeaderText = UiText("UI_Send"),
                Text = UiText("UI_Send"),
                UseColumnTextForButtonValue = true,
                Width = 60
            });
            this.dgvByteSweep.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "cByteSweepStop",
                HeaderText = UiText("UI_Stop"),
                Text = UiText("UI_Stop"),
                UseColumnTextForButtonValue = true,
                Width = 60
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
            this.tsByteSweepSelectAll.Text =
                UiText(allSelected ? "UI_ClearSelection" : "UI_SelectAll");
            this.tsByteSweepStart.Enabled = folderSelected && selectedCount > 0 && !this.byteSweepRunning;
            this.tsByteSweepStop.Enabled = this.byteSweepRunning;
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

            if (!Socket_Cache.ByteSweepList.AddFolder(folder.Trim()))
            {
                MessageBox.Show(this, UiText("UI_GroupExists"), UiText("UI_SweepGroups"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            this.selectedByteSweepFolder = folder.Trim();
            this.RefreshByteSweepFolderTree();
        }

        private void cmsByteSweepFolder_Rename_Click(object sender, EventArgs e)
        {
            if (!this.HasSelectedByteSweepFolder())
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

            Socket_Cache.ByteSweepList.RenameFolder(oldName, newName.Trim());
            this.selectedByteSweepFolder = newName.Trim();
            this.RefreshByteSweepFolderTree();
        }

        private void cmsByteSweepFolder_Delete_Click(object sender, EventArgs e)
        {
            if (!this.HasSelectedByteSweepFolder())
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

            Socket_Cache.ByteSweepList.RemoveFolder(this.selectedByteSweepFolder);
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
                    Socket_Cache.ByteSweepList.AddPreset(dialog.Result);
                    Socket_Cache.ByteSweepList.SaveByteSweepList_ToDB();
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
                    Socket_Cache.ByteSweepList.UpdatePreset(preset, dialog.Result);
                    Socket_Cache.ByteSweepList.SaveByteSweepList_ToDB();
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
                Socket_Cache.ByteSweepList.lstPresets.Remove(preset);
                Socket_Cache.ByteSweepList.SaveByteSweepList_ToDB();
                this.RefreshByteSweepView();
            }
        }

        private void tsByteSweepSelectAll_Click(object sender, EventArgs e)
        {
            List<Socket_ByteSweepPresetInfo> items = this.GetCurrentByteSweepPresets();
            bool select = !items.Any() || !items.All(item => item.IsEnable);
            foreach (Socket_ByteSweepPresetInfo item in items)
            {
                item.IsEnable = select;
            }
            Socket_Cache.ByteSweepList.SaveByteSweepList_ToDB();
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
                preset.IsEnable = !preset.IsEnable;
                Socket_Cache.ByteSweepList.SaveByteSweepList_ToDB();
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
            for (int index = 0; index < items.Count; index++)
            {
                items[index].BSortOrder = index + 1;
            }
            Socket_Cache.ByteSweepList.SaveByteSweepList_ToDB();
            this.RefreshByteSweepView();
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
            if (column == "cByteSweepSend")
            {
                e.Value = active ? "发送中…" : "发送";
                e.CellStyle.ForeColor = active ? SystemColors.GrayText : SystemColors.ControlText;
            }
            else if (column == "cByteSweepStop")
            {
                e.CellStyle.ForeColor = active ? SystemColors.ControlText : SystemColors.GrayText;
            }
        }

        private void dgvByteSweep_SelectionChanged(object sender, EventArgs e)
        {
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

        private void tsByteSweepStart_Click(object sender, EventArgs e)
        {
            this.StartByteSweepPresets(this.GetCurrentByteSweepPresets().Where(item => item.IsEnable));
        }

        private void tsByteSweepStop_Click(object sender, EventArgs e)
        {
            this.StopByteSweep();
        }

        private static Dictionary<Guid, int> ResolveByteSweepSockets(
            IEnumerable<Socket_ByteSweepPresetInfo> presets,
            out Socket_ByteSweepPresetInfo unresolvedPreset)
        {
            Dictionary<Guid, int> resolvedSockets = new Dictionary<Guid, int>();
            unresolvedPreset = null;

            foreach (Socket_ByteSweepPresetInfo preset in presets)
            {
                Socket_PacketInfo packetTemplate = new Socket_PacketInfo
                {
                    PacketType = preset.PacketType,
                    PacketFrom = preset.PacketFrom,
                    PacketTo = preset.PacketTo
                };
                int resolvedSocket = Socket_Cache.SocketList.ResolveCurrentSocket(
                    new[] { packetTemplate });
                if (resolvedSocket <= 0)
                {
                    unresolvedPreset = preset;
                    return null;
                }

                resolvedSockets[preset.BID] = resolvedSocket;
            }

            return resolvedSockets;
        }

        private async void StartByteSweepPresets(IEnumerable<Socket_ByteSweepPresetInfo> source)
        {
            List<Socket_ByteSweepPresetInfo> presets = source
                .Where(item => item != null)
                .OrderBy(item => item.BSortOrder)
                .Select(item => item.Clone())
                .ToList();
            if (this.byteSweepRunning || presets.Count == 0)
            {
                return;
            }

            Socket_ByteSweepPresetInfo invalid = presets.FirstOrDefault(item => !item.IsValid);
            if (invalid != null)
            {
                MessageBox.Show(this, "预设“" + invalid.BName + "”的封包或递进范围无效，请先编辑。", "递进发送",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Socket_ByteSweepPresetInfo unresolvedPreset;
            Dictionary<Guid, int> presetSockets = ResolveByteSweepSockets(
                presets,
                out unresolvedPreset);
            if (presetSockets == null)
            {
                MessageBox.Show(this, UiText("UI_CurrentSocketRequired"), "递进发送",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            this.byteSweepRunning = true;
            this.byteSweepTotalSend = 0;
            this.byteSweepSuccess = 0;
            this.byteSweepFailure = 0;
            this.byteSweepCts = new CancellationTokenSource();
            this.RefreshByteSweepView();

            try
            {
                for (int presetIndex = 0; presetIndex < presets.Count; presetIndex++)
                {
                    Socket_ByteSweepPresetInfo preset = presets[presetIndex];
                    int socket = presetSockets[preset.BID];
                    if (this.byteSweepCts.IsCancellationRequested)
                    {
                        break;
                    }

                    this.activeByteSweepPresetId = preset.BID;
                    this.RefreshByteSweepView();

                    for (int loop = 1; loop <= preset.BLoopCount; loop++)
                    {
                        int currentLoop = loop;
                        Socket_ByteSweepResult result = await Task.Run(() =>
                            Socket_ByteSweepEngine.Execute(
                                preset.Buffer,
                                preset.BStart,
                                preset.BLength,
                                preset.BInterval,
                                buffer => Socket_Operation.SendPacket(
                                    socket,
                                    preset.PacketType,
                                    preset.PacketFrom,
                                    preset.PacketTo,
                                    buffer),
                                this.byteSweepCts.Token,
                                progress => this.PostByteSweepPresetProgress(
                                    preset, progress, currentLoop, preset.BLoopCount)));
                        this.byteSweepTotalSend += result.TotalSend;
                        this.byteSweepSuccess += result.Success;
                        this.byteSweepFailure += result.Failure;
                        if (result.Cancelled)
                        {
                            break;
                        }
                    }

                    if (this.byteSweepCts.IsCancellationRequested)
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
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(StartByteSweepPresets), ex.Message);
                MessageBox.Show(this, "递进任务发生错误：" + ex.Message, "递进发送",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                this.activeByteSweepPresetId = Guid.Empty;
                this.byteSweepRunning = false;
                if (this.byteSweepCts != null)
                {
                    this.byteSweepCts.Dispose();
                    this.byteSweepCts = null;
                }
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
                this.BeginInvoke((Action)(() =>
                {
                    if (this.IsDisposed)
                    {
                        return;
                    }

                    if (!this.byteSweepRunning || this.activeByteSweepPresetId != preset.BID)
                    {
                        return;
                    }

                    this.tsByteSweepContext.Text = string.Format(
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
            if (this.byteSweepCts != null)
            {
                this.byteSweepCts.Cancel();
            }
        }
    }
}
