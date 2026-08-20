using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    public sealed class Socket_SendPresetPickerForm : Form
    {
        private sealed class GroupFilter
        {
            public string Name { get; set; }

            public bool IsAll { get; set; }

            public bool IsUngrouped { get; set; }
        }

        private static readonly ResourceManager PickerResources =
            new ResourceManager(
                "WPELibrary.Socket_SendPresetPickerForm",
                typeof(Socket_SendPresetPickerForm).Assembly);

        private readonly List<Socket_SendInfo> sendPresets;
        private TreeView tvGroups;
        private TextBox txtSearch;
        private DataGridView dgvPresets;
        private Label lEmptyState;
        private Button bConfirm;
        private SplitContainer splitContent;
        private Socket_SendInfo selectedPreset;

        public Guid SelectedSendPresetId { get; private set; }

        public Socket_SendPresetPickerForm(Guid selectedPresetId)
        {
            this.SelectedSendPresetId = selectedPresetId;
            this.sendPresets = Socket_Cache.SendList.lstSend.ToList();

            this.InitializeLayout();
            this.InitializeGroups();
            this.RefreshPresetRows();
        }

        private static string ResourceText(string key)
        {
            return PickerResources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }

        private void InitializeLayout()
        {
            this.Text = ResourceText("Picker_Title");
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(640, 420);
            this.MinimumSize = new Size(560, 360);
            this.Font = SystemFonts.MessageBoxFont;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            TableLayoutPanel searchLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Label searchLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = ResourceText("Picker_Search"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 6, 0)
            };
            this.txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                AccessibleName = ResourceText("Picker_Search")
            };
            this.txtSearch.TextChanged += this.txtSearch_TextChanged;
            searchLayout.Controls.Add(searchLabel, 0, 0);
            searchLayout.Controls.Add(this.txtSearch, 1, 0);

            this.splitContent = new SplitContainer();
            this.splitContent.Dock = DockStyle.Fill;
            this.splitContent.Orientation = Orientation.Vertical;
            this.splitContent.Size = new Size(624, 332);
            this.splitContent.SplitterDistance = 170;
            this.splitContent.Panel1MinSize = 130;
            this.splitContent.Panel2MinSize = 260;
            this.splitContent.FixedPanel = FixedPanel.Panel1;
            this.splitContent.Margin = new Padding(0);

            this.tvGroups = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                FullRowSelect = true,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = ResourceText("Picker_Groups")
            };
            this.tvGroups.AfterSelect += this.tvGroups_AfterSelect;
            this.splitContent.Panel1.Controls.Add(this.tvGroups);

            Panel resultPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            this.dgvPresets = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                StandardTab = true,
                AccessibleName = ResourceText("Picker_Presets")
            };
            DataGridViewTextBoxColumn nameColumn = new DataGridViewTextBoxColumn
            {
                Name = "cPresetName",
                HeaderText = ResourceText("Picker_NameColumn"),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 160,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ToolTipText = ResourceText("Picker_NameColumn")
            };
            this.dgvPresets.Columns.Add(nameColumn);
            this.dgvPresets.CellDoubleClick += this.dgvPresets_CellDoubleClick;
            this.dgvPresets.KeyDown += this.dgvPresets_KeyDown;
            this.dgvPresets.SelectionChanged += this.dgvPresets_SelectionChanged;

            this.lEmptyState = new Label
            {
                Dock = DockStyle.Fill,
                Text = ResourceText("Picker_EmptyState"),
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            resultPanel.Controls.Add(this.lEmptyState);
            resultPanel.Controls.Add(this.dgvPresets);
            this.splitContent.Panel2.Controls.Add(resultPanel);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0),
                Padding = new Padding(0, 7, 0, 0),
                WrapContents = false
            };
            Button cancel = new Button
            {
                Text = ResourceText("UI_Cancel"),
                DialogResult = DialogResult.Cancel,
                Width = 88,
                Height = 28,
                Margin = new Padding(6, 0, 0, 0)
            };
            this.bConfirm = new Button
            {
                Text = ResourceText("UI_OK"),
                Width = 88,
                Height = 28,
                Margin = new Padding(6, 0, 0, 0)
            };
            this.bConfirm.Click += this.bConfirm_Click;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(this.bConfirm);

            root.Controls.Add(searchLayout, 0, 0);
            root.Controls.Add(this.splitContent, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            this.Controls.Add(root);
            this.Shown += this.Socket_SendPresetPickerForm_Shown;
            this.AcceptButton = this.bConfirm;
            this.CancelButton = cancel;
        }

        private void Socket_SendPresetPickerForm_Shown(object sender, EventArgs e)
        {
            int maximumSplitterDistance =
                this.splitContent.Width - this.splitContent.Panel2MinSize;
            if (maximumSplitterDistance >= this.splitContent.Panel1MinSize)
            {
                this.splitContent.SplitterDistance = Math.Min(
                    170,
                    maximumSplitterDistance);
            }
        }

        private void InitializeGroups()
        {
            this.tvGroups.Nodes.Clear();
            HashSet<string> knownFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TreeNode allNode = new TreeNode(ResourceText("Picker_All"))
            {
                Tag = new GroupFilter { IsAll = true }
            };
            this.tvGroups.Nodes.Add(allNode);

            foreach (string folder in Socket_Cache.SendList.lstFolders)
            {
                this.AddGroupNode(folder, false, knownFolders);
            }

            foreach (Socket_SendInfo preset in this.sendPresets)
            {
                if (!string.IsNullOrWhiteSpace(preset.SFolder))
                {
                    this.AddGroupNode(preset.SFolder, false, knownFolders);
                }
            }

            if (this.sendPresets.Any(item => string.IsNullOrWhiteSpace(item.SFolder)))
            {
                TreeNode ungroupedNode = new TreeNode(ResourceText("Picker_Ungrouped"))
                {
                    Tag = new GroupFilter { IsUngrouped = true }
                };
                this.tvGroups.Nodes.Add(ungroupedNode);
            }

            this.tvGroups.SelectedNode = allNode;
            allNode.Expand();
        }

        private void AddGroupNode(
            string folder,
            bool isUngrouped,
            HashSet<string> knownFolders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !knownFolders.Add(folder))
            {
                return;
            }

            TreeNode node = new TreeNode(folder)
            {
                Tag = new GroupFilter
                {
                    Name = folder,
                    IsUngrouped = isUngrouped
                }
            };
            this.tvGroups.Nodes.Add(node);
        }

        private void RefreshPresetRows()
        {
            GroupFilter filter = this.tvGroups.SelectedNode == null
                ? new GroupFilter { IsAll = true }
                : this.tvGroups.SelectedNode.Tag as GroupFilter;
            string search = (this.txtSearch.Text ?? string.Empty).Trim();
            Guid preferredId = this.SelectedSendPresetId;

            this.dgvPresets.Rows.Clear();
            this.selectedPreset = null;

            foreach (Socket_SendInfo preset in this.sendPresets)
            {
                if (!MatchesGroup(preset, filter) ||
                    (!string.IsNullOrEmpty(search) &&
                     (preset.SName ?? string.Empty).IndexOf(
                         search,
                         StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                int rowIndex = this.dgvPresets.Rows.Add(preset.SName ?? string.Empty);
                DataGridViewRow row = this.dgvPresets.Rows[rowIndex];
                row.Tag = preset;
                row.Cells[0].ToolTipText = preset.SName ?? string.Empty;
                if (preset.SID == preferredId)
                {
                    row.Selected = true;
                }
            }

            if (this.dgvPresets.Rows.Count > 0 && this.dgvPresets.SelectedRows.Count == 0)
            {
                this.dgvPresets.Rows[0].Selected = true;
            }

            this.dgvPresets.Visible = this.dgvPresets.Rows.Count > 0;
            this.lEmptyState.Visible = !this.dgvPresets.Visible;
            if (this.lEmptyState.Visible)
            {
                this.lEmptyState.BringToFront();
            }
            else
            {
                this.dgvPresets.BringToFront();
            }
            this.UpdateSelectedPreset();
        }

        private static bool MatchesGroup(Socket_SendInfo preset, GroupFilter filter)
        {
            if (filter == null || filter.IsAll)
            {
                return true;
            }

            if (filter.IsUngrouped)
            {
                return string.IsNullOrWhiteSpace(preset.SFolder);
            }

            return string.Equals(
                preset.SFolder,
                filter.Name,
                StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSelectedPreset()
        {
            this.selectedPreset = null;
            if (this.dgvPresets.SelectedRows.Count > 0)
            {
                this.selectedPreset = this.dgvPresets.SelectedRows[0].Tag as Socket_SendInfo;
            }

            this.bConfirm.Enabled = this.selectedPreset != null;
            if (this.selectedPreset != null)
            {
                this.SelectedSendPresetId = this.selectedPreset.SID;
            }
        }

        private void ConfirmSelection()
        {
            this.UpdateSelectedPreset();
            if (this.selectedPreset == null)
            {
                return;
            }

            this.SelectedSendPresetId = this.selectedPreset.SID;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void tvGroups_AfterSelect(object sender, TreeViewEventArgs e)
        {
            this.RefreshPresetRows();
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            this.RefreshPresetRows();
        }

        private void dgvPresets_SelectionChanged(object sender, EventArgs e)
        {
            this.UpdateSelectedPreset();
        }

        private void dgvPresets_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                this.ConfirmSelection();
            }
        }

        private void dgvPresets_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                this.ConfirmSelection();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        private void bConfirm_Click(object sender, EventArgs e)
        {
            this.ConfirmSelection();
        }
    }
}
