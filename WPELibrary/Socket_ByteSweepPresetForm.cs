using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    public sealed class Socket_ByteSweepPresetForm : Form
    {
        private readonly TextBox txtName;
        private readonly ComboBox cbbFolder;
        private readonly NumericUpDown nudStart;
        private readonly NumericUpDown nudLength;
        private readonly NumericUpDown nudLoopCount;
        private readonly NumericUpDown nudInterval;
        private readonly NumericUpDown nudNextInterval;
        private readonly Socket_ByteSweepPresetInfo source;
        private readonly int bufferLength;
        private readonly bool isNew;

        public Socket_ByteSweepPresetInfo Result { get; private set; }

        public Socket_ByteSweepPresetForm(Socket_ByteSweepPresetInfo preset)
            : this(preset, false)
        {
        }

        public Socket_ByteSweepPresetForm(Socket_ByteSweepPresetInfo preset, bool isNew)
        {
            this.isNew = isNew;
            this.source = preset == null ? null : preset.Clone();
            Socket_ByteSweepPresetInfo value = this.source ?? new Socket_ByteSweepPresetInfo();
            this.bufferLength = value.Buffer == null ? 0 : value.Buffer.Length;

            this.Text = ResourceText(this.isNew ? "ByteSweep_NewTitle" : "ByteSweep_EditTitle");
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(420, 330);
            this.MinimumSize = new Size(436, 369);
            this.Font = SystemFonts.MessageBoxFont;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 2,
                RowCount = 9
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < 8; index++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            this.txtName = new TextBox { Dock = DockStyle.Fill, Text = value.BName ?? string.Empty };
            this.cbbFolder = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            this.cbbFolder.Items.AddRange(Socket_Cache.ByteSweepList.lstFolders.Cast<object>().ToArray());
            this.cbbFolder.Text = value.BFolder ?? string.Empty;
            this.nudStart = CreateNumber(0, Math.Max(0, this.bufferLength - 1), Math.Min(Math.Max(0, value.BStart), Math.Max(0, this.bufferLength - 1)));
            this.nudLength = CreateNumber(1, Math.Max(1, this.bufferLength), Math.Max(1, value.BLength));
            this.nudStart.ValueChanged += this.nudStart_ValueChanged;
            this.nudStart_ValueChanged(this.nudStart, EventArgs.Empty);
            this.nudLoopCount = CreateNumber(1, 999999, Math.Max(1, value.BLoopCount));
            this.nudInterval = CreateNumber(0, 999999999, Math.Max(0, value.BInterval));
            this.nudNextInterval = CreateNumber(0, 999999999, Math.Max(0, value.BNextInterval));

            this.txtName.AccessibleName = ResourceText("ByteSweep_Name");
            this.cbbFolder.AccessibleName = ResourceText("ByteSweep_Folder");
            this.nudStart.AccessibleName = ResourceText("ByteSweep_Start");
            this.nudLength.AccessibleName = ResourceText("ByteSweep_Length");
            this.nudLoopCount.AccessibleName = ResourceText("ByteSweep_LoopCount");
            this.nudInterval.AccessibleName = ResourceText("ByteSweep_Interval");
            this.nudNextInterval.AccessibleName = ResourceText("ByteSweep_NextInterval");

            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_Name")), 0, 0);
            layout.Controls.Add(this.txtName, 1, 0);
            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_Folder")), 0, 1);
            layout.Controls.Add(this.cbbFolder, 1, 1);
            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_Start")), 0, 2);
            layout.Controls.Add(this.nudStart, 1, 2);
            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_Length")), 0, 3);
            layout.Controls.Add(this.nudLength, 1, 3);
            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_LoopCount")), 0, 4);
            layout.Controls.Add(this.nudLoopCount, 1, 4);
            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_Interval")), 0, 5);
            layout.Controls.Add(this.nudInterval, 1, 5);
            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_NextInterval")), 0, 6);
            layout.Controls.Add(this.nudNextInterval, 1, 6);
            layout.Controls.Add(CreateLabel(ResourceText("ByteSweep_PacketLength")), 0, 7);
            layout.Controls.Add(CreateLabel(string.Format(
                CultureInfo.CurrentCulture,
                ResourceText("ByteSweep_PacketLengthValue"),
                this.bufferLength)), 1, 7);

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
            layout.Controls.Add(buttons, 0, 8);
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

        private static NumericUpDown CreateNumber(decimal minimum, decimal maximum, decimal value)
        {
            return new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = minimum,
                Maximum = maximum,
                Value = Math.Min(Math.Max(value, minimum), maximum)
            };
        }

        private static string ResourceText(string key)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? key;
        }

        private void nudStart_ValueChanged(object sender, EventArgs e)
        {
            int remaining = Math.Max(1, this.bufferLength - (int)this.nudStart.Value);
            this.nudLength.Maximum = remaining;
            if (this.nudLength.Value > remaining)
            {
                this.nudLength.Value = remaining;
            }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            string name = this.txtName.Text.Trim();
            string folder = this.cbbFolder.Text.Trim();
            int start = (int)this.nudStart.Value;
            int length = (int)this.nudLength.Value;
            int currentBufferLength = this.source == null || this.source.Buffer == null ? 0 : this.source.Buffer.Length;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(folder))
            {
                MessageBox.Show(this, ResourceText("ByteSweep_Required"), ResourceText("ByteSweep_MessageTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (currentBufferLength == 0 || start >= currentBufferLength || length > currentBufferLength - start)
            {
                MessageBox.Show(this, ResourceText("ByteSweep_RangeInvalid"), ResourceText("ByteSweep_MessageTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool duplicate = Socket_Cache.ByteSweepList.lstPresets.Any(item =>
                (this.source == null || item.BID != this.source.BID) &&
                string.Equals(item.BFolder, folder, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.BName, name, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                MessageBox.Show(this, ResourceText("ByteSweep_Duplicate"), ResourceText("ByteSweep_MessageTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            this.Result = this.source == null
                ? new Socket_ByteSweepPresetInfo { BID = Guid.NewGuid() }
                : this.source.Clone();
            this.Result.BName = name;
            this.Result.BFolder = folder;
            this.Result.BStart = start;
            this.Result.BLength = length;
            this.Result.BLoopCount = (int)this.nudLoopCount.Value;
            this.Result.BInterval = (int)this.nudInterval.Value;
            this.Result.BNextInterval = (int)this.nudNextInterval.Value;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
