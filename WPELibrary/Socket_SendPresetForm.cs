using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    public sealed class Socket_SendPresetForm : Form
    {
        private readonly TextBox txtName;
        private readonly ComboBox cbbFolder;
        private readonly ComboBox cbbPresetType;
        private readonly Label lFolder;
        private readonly Guid? editingPresetId;
        private readonly bool allowByteSweep;
        private readonly string sendDefaultName;
        private readonly string sendDefaultFolder;
        private readonly string byteSweepDefaultName;
        private readonly string byteSweepDefaultFolder;

        public string PresetName { get; private set; }

        public string FolderName { get; private set; }

        public bool SaveAsByteSweep
        {
            get
            {
                return this.allowByteSweep &&
                    this.cbbPresetType != null &&
                    this.cbbPresetType.SelectedIndex == 1;
            }
        }

        public Socket_SendPresetForm(
            string defaultName,
            string defaultFolder,
            Guid? editingPresetId = null,
            bool allowByteSweep = false)
        {
            this.editingPresetId = editingPresetId;
            this.allowByteSweep = allowByteSweep;
            this.sendDefaultName = defaultName ?? string.Empty;
            this.sendDefaultFolder = defaultFolder ?? string.Empty;
            this.byteSweepDefaultName = ResourceText("UI_DefaultSweepPresetName") +
                (Socket_Cache.ByteSweepList.lstPresets.Count + 1);
            this.byteSweepDefaultFolder =
                Socket_Cache.ByteSweepList.lstFolders.FirstOrDefault() ?? string.Empty;
            this.Text = ResourceText(
                this.allowByteSweep ? "SendPreset_SavePacketTitle" : "SendPreset_Title");
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(400, this.allowByteSweep ? 181 : 145);
            this.MinimumSize = new Size(416, this.allowByteSweep ? 220 : 184);
            this.Font = SystemFonts.MessageBoxFont;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 2,
                RowCount = this.allowByteSweep ? 4 : 3
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (this.allowByteSweep)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            this.txtName = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = this.sendDefaultName,
                AccessibleName = ResourceText("SendPreset_Name")
            };
            this.cbbFolder = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDown,
                Text = this.sendDefaultFolder,
                AccessibleName = ResourceText("SendPreset_Folder")
            };
            this.lFolder = CreateLabel(ResourceText("SendPreset_Folder"));

            int firstInputRow = 0;
            if (this.allowByteSweep)
            {
                this.cbbPresetType = new ComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    AccessibleName = ResourceText("SendPreset_Type")
                };
                this.cbbPresetType.Items.Add(ResourceText("SendPreset_TypeSend"));
                this.cbbPresetType.Items.Add(ResourceText("SendPreset_TypeSweep"));
                this.cbbPresetType.SelectedIndexChanged += this.PresetType_SelectedIndexChanged;
                layout.Controls.Add(CreateLabel(ResourceText("SendPreset_Type")), 0, 0);
                layout.Controls.Add(this.cbbPresetType, 1, 0);
                firstInputRow = 1;
            }

            layout.Controls.Add(CreateLabel(ResourceText("SendPreset_Name")), 0, firstInputRow);
            layout.Controls.Add(this.txtName, 1, firstInputRow);
            layout.Controls.Add(this.lFolder, 0, firstInputRow + 1);
            layout.Controls.Add(this.cbbFolder, 1, firstInputRow + 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            Button cancel = new Button
            {
                Text = ResourceText("UI_Cancel"),
                DialogResult = DialogResult.Cancel,
                Width = 88,
                Height = 32
            };
            Button save = new Button
            {
                Text = ResourceText("UI_Save"),
                Width = 88,
                Height = 32
            };
            save.Click += this.Save_Click;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            layout.Controls.Add(buttons, 0, firstInputRow + 2);
            layout.SetColumnSpan(buttons, 2);

            this.Controls.Add(layout);
            this.AcceptButton = save;
            this.CancelButton = cancel;
            if (this.allowByteSweep)
            {
                this.cbbPresetType.SelectedIndex = 0;
            }
            else
            {
                this.PopulateFolders(false);
            }
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static string ResourceText(string key)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? key;
        }

        private void PresetType_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool byteSweep = this.SaveAsByteSweep;
            string previousDefaultName = byteSweep
                ? this.sendDefaultName
                : this.byteSweepDefaultName;
            if (string.Equals(this.txtName.Text, previousDefaultName, StringComparison.Ordinal))
            {
                this.txtName.Text = byteSweep
                    ? this.byteSweepDefaultName
                    : this.sendDefaultName;
            }

            this.lFolder.Text = ResourceText(
                byteSweep ? "SendPreset_SweepFolder" : "SendPreset_Folder");
            this.PopulateFolders(byteSweep);
        }

        private void PopulateFolders(bool byteSweep)
        {
            this.cbbFolder.BeginUpdate();
            try
            {
                this.cbbFolder.Items.Clear();
                object[] folders = byteSweep
                    ? Socket_Cache.ByteSweepList.lstFolders.Cast<object>().ToArray()
                    : Socket_Cache.SendList.lstFolders.Cast<object>().ToArray();
                this.cbbFolder.Items.AddRange(folders);
                this.cbbFolder.Text = byteSweep
                    ? this.byteSweepDefaultFolder
                    : this.sendDefaultFolder;
            }
            finally
            {
                this.cbbFolder.EndUpdate();
            }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            string name = this.txtName.Text.Trim();
            string folder = this.cbbFolder.Text.Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(folder))
            {
                MessageBox.Show(this, ResourceText("SendPreset_Required"),
                    this.Text, MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            bool duplicate = this.SaveAsByteSweep
                ? Socket_Cache.ByteSweepList.lstPresets.Any(item =>
                    string.Equals(item.BFolder, folder, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.BName, name, StringComparison.OrdinalIgnoreCase))
                : Socket_Cache.SendList.lstSend.Any(item =>
                    (!this.editingPresetId.HasValue || item.SID != this.editingPresetId.Value) &&
                    string.Equals(item.SFolder, folder, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.SName, name, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                MessageBox.Show(this, ResourceText("SendPreset_Duplicate"),
                    this.Text, MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            this.PresetName = name;
            this.FolderName = folder;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
