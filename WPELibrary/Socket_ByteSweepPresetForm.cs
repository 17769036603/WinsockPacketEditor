using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    public sealed class Socket_ByteSweepPresetForm : Form
    {
        private readonly TextBox txtName;
        private readonly ComboBox cbbFolder;
        private readonly Socket_ByteSweepPresetInfo source;
        private readonly bool isNew;

        public Socket_ByteSweepPresetInfo Result { get; private set; }
        public bool EditDetailsRequested { get; private set; }
        public bool OverwriteConfirmed { get; private set; }
        public Guid? OverwriteTargetId { get; private set; }

        public Socket_ByteSweepPresetForm(Socket_ByteSweepPresetInfo preset)
            : this(preset, false)
        {
        }

        public Socket_ByteSweepPresetForm(Socket_ByteSweepPresetInfo preset, bool isNew)
        {
            this.isNew = isNew;
            this.source = preset == null ? null : preset.Clone();
            Socket_ByteSweepPresetInfo value = this.source ?? new Socket_ByteSweepPresetInfo();

            this.Text = ResourceText(this.isNew ? "ByteSweep_NewTitle" : "ByteSweep_EditTitle");
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(420, 160);
            this.MinimumSize = new Size(436, 199);
            this.Font = SystemFonts.MessageBoxFont;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 2,
                RowCount = 3
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            this.txtName = new TextBox { Dock = DockStyle.Fill, Text = value.BName ?? string.Empty };
            this.cbbFolder = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            this.cbbFolder.Items.AddRange(Socket_Cache.ByteSweepList.lstFolders.Cast<object>().ToArray());
            this.cbbFolder.Text = value.BFolder ?? string.Empty;

            this.txtName.AccessibleName = ResourceText("ByteSweep_Name");
            this.cbbFolder.AccessibleName = ResourceText("ByteSweep_Folder");

            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_Name")), 0, 0);
            layout.Controls.Add(this.txtName, 1, 0);
            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_Folder")), 0, 1);
            layout.Controls.Add(this.cbbFolder, 1, 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            Button cancel = new Button { Text = ResourceText("ByteSweep_Cancel"), DialogResult = DialogResult.Cancel, Width = 88, Height = 32 };
            Button save = new Button { Text = ResourceText("ByteSweep_Save"), Width = 88, Height = 32 };
            save.Click += this.Save_Click;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            if (!this.isNew)
            {
                Button editDetails = new Button
                {
                    Name = "bEditByteSweepDetails",
                    Text = ResourceText("ByteSweep_EditDetails"),
                    AutoSize = true,
                    MinimumSize = new Size(104, 32),
                    Height = 32
                };
                editDetails.Click += this.EditDetails_Click;
                buttons.Controls.Add(editDetails);
            }
            layout.Controls.Add(buttons, 0, 2);
            layout.SetColumnSpan(buttons, 2);

            this.Controls.Add(layout);
            this.AcceptButton = save;
            this.CancelButton = cancel;
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
            string chinese;
            string english;
            switch (key)
            {
                case "ByteSweep_NewTitle": chinese = "新建递进预设"; english = "New byte-sweep preset"; break;
                case "ByteSweep_EditTitle": chinese = "编辑递进预设"; english = "Edit byte-sweep preset"; break;
                case "ByteSweep_EditDetails": chinese = "其他修改项"; english = "Other settings"; break;
                case "ByteSweep_Name": chinese = "预设名称"; english = "Preset name"; break;
                case "ByteSweep_Folder": chinese = "递进分组"; english = "Sweep group"; break;
                case "ByteSweep_Cancel": chinese = "取消"; english = "Cancel"; break;
                case "ByteSweep_Save": chinese = "保存"; english = "Save"; break;
                default: return Properties.Resources.ResourceManager.GetString(key) ?? key;
            }

            return MultiLanguage.GetDefaultLanguage(new[] { chinese, english });
        }

        private void EditDetails_Click(object sender, EventArgs e)
        {
            if (!this.TryBuildResult())
            {
                return;
            }

            this.EditDetailsRequested = true;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!this.TryBuildResult())
            {
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private bool TryBuildResult()
        {
            string name = this.txtName.Text.Trim();
            string folder = this.cbbFolder.Text.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(folder))
            {
                MessageBox.Show(this, ResourceText("ByteSweep_Required"), ResourceText("ByteSweep_MessageTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            Socket_ByteSweepPresetInfo conflicting =
                Socket_Cache.ByteSweepList.lstPresets.FirstOrDefault(item =>
                    (this.source == null || item.BID != this.source.BID) &&
                    string.Equals(item.BFolder, folder, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.BName, name, StringComparison.OrdinalIgnoreCase));
            if (conflicting != null)
            {
                DialogResult result = MessageBox.Show(
                    this,
                    ResourceText("ByteSweep_DuplicateOverwrite"),
                    ResourceText("ByteSweep_MessageTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    return false;
                }

                this.OverwriteConfirmed = true;
                this.OverwriteTargetId = conflicting.BID;
            }

            this.Result = this.source == null
                ? new Socket_ByteSweepPresetInfo { BID = Guid.NewGuid() }
                : this.source.Clone();
            this.Result.BName = name;
            this.Result.BFolder = folder;
            return true;
        }
    }
}
