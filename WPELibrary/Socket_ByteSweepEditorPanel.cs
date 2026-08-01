using System;
using System.Drawing;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    internal sealed class Socket_ByteSweepEditorPanel : UserControl
    {
        private readonly ComboBox mode;
        private readonly Label selection;
        private readonly TableLayoutPanel layout;
        private readonly Panel scrollHost;
        private readonly NumericUpDown loopCount;
        private readonly NumericUpDown interval;
        private readonly NumericUpDown nextInterval;
        private readonly NumericUpDown firstPosition;
        private readonly NumericUpDown firstLength;
        private readonly NumericUpDown firstInterval;
        private readonly Button pickFirst;
        private readonly NumericUpDown secondPosition;
        private readonly NumericUpDown secondLength;
        private readonly NumericUpDown secondInterval;
        private readonly Button pickSecond;
        private readonly Button save;
        private readonly Button send;
        private readonly Button stop;
        private readonly Label status;
        private readonly Label title;
        private readonly Label selectionCaption;
        private readonly TableLayoutPanel footer;
        private readonly Font titleFont;
        private readonly ToolTip toolTip;
        private int selectionStart;
        private int selectionLength;
        private bool loading;
        private bool busy;
        private bool pairOnlyMode;
        private int positionPickTarget;

        public event EventHandler SendRequested;
        public event EventHandler StopRequested;
        public event EventHandler SaveRequested;
        public event EventHandler Changed;
        public event EventHandler PickFirstRequested;
        public event EventHandler PickSecondRequested;

        public Socket_ByteSweepEditorPanel()
        {
            this.MinimumSize = new Size(270, 210);
            this.Dock = DockStyle.Fill;
            this.BorderStyle = BorderStyle.FixedSingle;

            this.titleFont = new Font(this.Font, FontStyle.Bold);
            this.toolTip = new ToolTip();
            this.title = new Label
            {
                Dock = DockStyle.Fill,
                Height = 28,
                Padding = new Padding(8, 5, 0, 0),
                Text = ResourceText("ByteSweep_ControlTitle"),
                Font = this.titleFont
            };

            this.scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(6, 4, 6, 4)
            };
            this.layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SystemColors.Window,
                ColumnCount = 2,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                Dock = DockStyle.Top,
                Padding = new Padding(0),
                RowCount = 11
            };
            this.layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            this.layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
            this.scrollHost.Controls.Add(this.layout);
            this.Controls.Add(this.scrollHost);

            this.mode = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Name = "cbbSweepEditorMode",
                AccessibleName = ResourceText("ByteSweep_Mode")
            };
            this.mode.Items.AddRange(new object[]
            {
                ResourceText("ByteSweep_SequentialMode"),
                ResourceText("ByteSweep_PairMode")
            });
            this.mode.SelectedIndex = 0;
            this.mode.SelectedIndexChanged += delegate
            {
                UpdatePairEnabled();
                if (!this.IsPairMode)
                {
                    CancelPositionPick();
                }
                UpdateSummary();
                RaiseChanged();
            };
            AddRow(this.layout, 0, ResourceText("ByteSweep_Mode"), this.mode);

            this.selection = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = ResourceText("ByteSweep_NoSelection"),
                AccessibleName = ResourceText("ByteSweep_RangeEstimate")
            };
            this.selectionCaption = AddRow(
                this.layout,
                1,
                ResourceText("ByteSweep_RangeEstimate"),
                this.selection);
            this.selection.BackColor = Color.FromArgb(235, 243, 252);
            this.selection.Padding = new Padding(3, 0, 2, 0);

            this.loopCount = CreateNumber("nudSweepEditorLoopCount", ResourceText("ByteSweep_LoopCount"), 1, 99999, 1, 10);
            this.interval = CreateNumber("nudSweepEditorInterval", ResourceText("ByteSweep_NormalInterval"), 0, 999999999, 0, 10);
            this.nextInterval = CreateNumber("nudSweepEditorNextInterval", ResourceText("ByteSweep_NextInterval"), 0, 999999999, 0, 10);
            AddRow(this.layout, 2, ResourceText("ByteSweep_LoopCount"), this.loopCount);
            AddRow(this.layout, 3, ResourceText("ByteSweep_NormalInterval"), this.interval);
            AddRow(this.layout, 4, ResourceText("ByteSweep_NextInterval"), this.nextInterval);

            this.firstPosition = CreateNumber("nudSweepEditorFirstPosition", ResourceText("ByteSweep_FirstPosition"), 0, 65535, 0, 1);
            this.firstLength = CreateNumber("nudSweepEditorFirstLength", ResourceText("ByteSweep_FirstLength"), 1, 255, 255, 1);
            this.firstInterval = CreateNumber("nudSweepEditorFirstInterval", ResourceText("ByteSweep_FirstInterval"), 0, 999999999, 1000, 10);
            this.pickFirst = NewButton(
                "bSweepEditorPickFirst",
                ResourceText("ByteSweep_PickFirst"),
                58);
            this.pickFirst.Height = 22;
            this.pickFirst.Click += delegate
            {
                SetPositionPickState(1);
                Raise(PickFirstRequested);
            };
            AddRow(
                this.layout,
                5,
                ResourceText("ByteSweep_FirstPosition"),
                CreatePositionPicker(this.firstPosition, this.pickFirst));
            AddRow(this.layout, 6, ResourceText("ByteSweep_FirstLength"), this.firstLength);
            AddRow(this.layout, 7, ResourceText("ByteSweep_FirstInterval"), this.firstInterval);

            this.secondPosition = CreateNumber("nudSweepEditorSecondPosition", ResourceText("ByteSweep_SecondPosition"), 0, 65535, 1, 1);
            this.secondLength = CreateNumber("nudSweepEditorSecondLength", ResourceText("ByteSweep_SecondLength"), 1, 255, 255, 1);
            this.secondInterval = CreateNumber("nudSweepEditorSecondInterval", ResourceText("ByteSweep_SecondInterval"), 0, 999999999, 10, 10);
            this.pickSecond = NewButton(
                "bSweepEditorPickSecond",
                ResourceText("ByteSweep_PickSecond"),
                58);
            this.pickSecond.Height = 22;
            this.pickSecond.Click += delegate
            {
                SetPositionPickState(2);
                Raise(PickSecondRequested);
            };
            AddRow(
                this.layout,
                8,
                ResourceText("ByteSweep_SecondPosition"),
                CreatePositionPicker(this.secondPosition, this.pickSecond));
            AddRow(this.layout, 9, ResourceText("ByteSweep_SecondLength"), this.secondLength);
            AddRow(this.layout, 10, ResourceText("ByteSweep_SecondInterval"), this.secondInterval);

            this.status = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                BackColor = Color.FromArgb(248, 248, 248),
                Padding = new Padding(4, 2, 4, 2),
                Text = ResourceText("ByteSweep_Idle"),
                ForeColor = Color.RoyalBlue,
                AccessibleName = ResourceText("ByteSweep_Status"),
                TextAlign = ContentAlignment.MiddleLeft
            };

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 2, 0, 0),
                WrapContents = false
            };
            this.save = NewButton("bSweepEditorSave", ResourceText("UI_SaveSweepPreset"), 112);
            this.send = NewButton("bSweepEditorSend", ResourceText("ByteSweep_StartAction"), 72);
            this.stop = NewButton("bSweepEditorStop", ResourceText("ByteSweep_StopAction"), 72);
            this.send.AccessibleName = ResourceText("ByteSweep_StartAction");
            this.stop.AccessibleName = ResourceText("ByteSweep_StopAction");
            this.save.Click += delegate { Raise(SaveRequested); };
            this.send.Click += delegate { Raise(SendRequested); };
            this.stop.Click += delegate { Raise(StopRequested); };
            actions.Controls.Add(this.send);
            actions.Controls.Add(this.stop);
            actions.Controls.Add(this.save);

            this.footer = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Height = 61,
                Padding = new Padding(6, 0, 6, 5),
                RowCount = 2
            };
            this.footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            this.footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            this.footer.Controls.Add(this.status, 0, 0);
            this.footer.Controls.Add(actions, 0, 1);

            TableLayoutPanel root = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0),
                RowCount = 3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 61F));
            root.Controls.Add(this.title, 0, 0);
            root.Controls.Add(this.scrollHost, 0, 1);
            root.Controls.Add(this.footer, 0, 2);
            this.Controls.Add(root);

            foreach (NumericUpDown number in new[]
            {
                this.loopCount,
                this.interval,
                this.nextInterval,
                this.firstPosition,
                this.firstLength,
                this.firstInterval,
                this.secondPosition,
                this.secondLength,
                this.secondInterval
            })
            {
                number.ValueChanged += delegate
                {
                    UpdateSummary();
                    RaiseChanged();
                };
            }

            SetRunning(false, string.Empty);
            UpdatePairEnabled();
        }

        private static Button NewButton(string name, string text, int width)
        {
            return new Button
            {
                Name = name,
                Text = text,
                AccessibleName = text,
                Width = width,
                Height = 26,
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 4, 0)
            };
        }

        private static Control CreatePositionPicker(
            NumericUpDown position,
            Button pickButton)
        {
            TableLayoutPanel picker = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = 1
            };
            picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            picker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            picker.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            position.Margin = new Padding(0, 1, 3, 1);
            pickButton.Dock = DockStyle.Fill;
            pickButton.Margin = new Padding(1, 1, 0, 1);
            picker.Controls.Add(position, 0, 0);
            picker.Controls.Add(pickButton, 1, 0);
            return picker;
        }

        private static NumericUpDown CreateNumber(
            string name,
            string accessibleName,
            decimal minimum,
            decimal maximum,
            decimal value,
            decimal increment)
        {
            NumericUpDown control = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Height = 22,
                Increment = increment,
                Maximum = maximum,
                Minimum = minimum,
                Name = name,
                AccessibleName = accessibleName,
                Value = value,
                Margin = new Padding(0, 1, 0, 1)
            };
            if (control.Controls.Count > 0)
            {
                control.Controls[0].Visible = false;
            }
            return control;
        }

        private static Label AddRow(TableLayoutPanel layout, int row, string labelText, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            Label label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = labelText,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 1, 3, 1)
            };
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
            return label;
        }

        private bool IsPairMode
        {
            get { return this.pairOnlyMode || this.mode.SelectedIndex == 1; }
        }

        public bool IsPairCombinationSelected
        {
            get { return this.IsPairMode; }
        }

        internal void SetPairOnlyMode(bool value)
        {
            this.pairOnlyMode = value;
            this.loading = true;
            try
            {
                if (value)
                {
                    this.mode.SelectedIndex = 1;
                }
            }
            finally
            {
                this.loading = false;
            }

            this.title.Text = value
                ? ResourceText("ByteSweep_PairTitle")
                : ResourceText("ByteSweep_ControlTitle");
            this.selectionCaption.Text = value
                ? ResourceText("ByteSweep_CombinationEstimate")
                : ResourceText("ByteSweep_RangeEstimate");
            UpdatePairEnabled();
            UpdateSummary();
        }

        private void UpdatePairEnabled()
        {
            bool enabled = this.IsPairMode && !this.busy;
            this.mode.Enabled = !this.pairOnlyMode && !this.busy;
            this.loopCount.Enabled = !this.busy;
            this.nextInterval.Enabled = !this.busy;
            this.firstPosition.Enabled = enabled;
            this.firstLength.Enabled = enabled;
            this.firstInterval.Enabled = enabled;
            this.secondPosition.Enabled = enabled;
            this.secondLength.Enabled = enabled;
            this.secondInterval.Enabled = enabled;
            this.pickFirst.Enabled = enabled;
            this.pickSecond.Enabled = enabled;
            this.interval.Enabled = !this.IsPairMode && !this.busy;
            SetRowVisible(0, !this.pairOnlyMode);
            SetRowVisible(3, !this.IsPairMode);
            for (int row = 5; row <= 10; row++)
            {
                SetRowVisible(row, this.IsPairMode);
            }
            UpdateScrollHost();
        }

        private void UpdateScrollHost()
        {
            int minimumHeight = this.IsPairMode ? 300 : 210;
            this.layout.MinimumSize = Size.Empty;
            this.scrollHost.AutoScrollMinSize = new Size(
                0,
                Math.Max(minimumHeight, this.layout.PreferredSize.Height));
        }

        private void SetRowVisible(int row, bool visible)
        {
            this.layout.RowStyles[row].Height = visible ? 25F : 0F;
            for (int column = 0; column < this.layout.ColumnCount; column++)
            {
                Control control = this.layout.GetControlFromPosition(column, row);
                if (control != null)
                {
                    control.Visible = visible;
                }
            }
        }

        private void UpdateSummary()
        {
            if (this.IsPairMode)
            {
                long total =
                    (long)this.firstLength.Value *
                    (long)this.secondLength.Value *
                    (long)this.loopCount.Value;
                this.selection.Text = string.Format(
                    ResourceText("ByteSweep_PairEstimateFormat"),
                    this.firstLength.Value,
                    this.secondLength.Value,
                    this.loopCount.Value,
                    total);
            }
            else
            {
                this.selection.Text = this.selectionLength > 0
                    ? string.Format(
                        ResourceText("ByteSweep_SequentialEstimateFormat"),
                        this.selectionStart,
                        this.selectionLength,
                        (long)this.selectionLength * 255L * (long)this.loopCount.Value)
                    : ResourceText("ByteSweep_NoSelection");
            }
            this.toolTip.SetToolTip(this.selection, this.selection.Text);
        }

        private void RaiseChanged()
        {
            if (!this.loading && this.Changed != null)
            {
                this.Changed(this, EventArgs.Empty);
            }
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void LoadSettings(Socket_ByteSweepPresetInfo value, int bufferLength, int selectionStart, int selectionLength)
        {
            this.loading = true;
            try
            {
                this.mode.SelectedIndex = this.pairOnlyMode
                    ? 1
                    : (value != null && value.BMode == Socket_ByteSweepMode.PairCombination ? 1 : 0);
                this.loopCount.Value = Clamp(this.loopCount, value == null ? 1 : value.BLoopCount);
                this.interval.Value = Clamp(this.interval, value == null ? 0 : value.BInterval);
                this.nextInterval.Value = Clamp(this.nextInterval, value == null ? 0 : value.BNextInterval);
                this.firstPosition.Value = Clamp(this.firstPosition, value == null ? 0 : value.BCombinationFirstPosition);
                this.firstLength.Value = Clamp(this.firstLength, value == null ? 255 : value.BCombinationFirstLength);
                this.firstInterval.Value = Clamp(this.firstInterval, value == null ? 1000 : value.BCombinationFirstInterval);
                this.secondPosition.Value = Clamp(this.secondPosition, value == null ? Math.Min(1, Math.Max(0, bufferLength - 1)) : value.BCombinationSecondPosition);
                this.secondLength.Value = Clamp(this.secondLength, value == null ? 255 : value.BCombinationSecondLength);
                this.secondInterval.Value = Clamp(this.secondInterval, value == null ? 10 : value.BCombinationSecondInterval);
                SetSelection(selectionStart, selectionLength);
            }
            finally
            {
                this.loading = false;
                UpdatePairEnabled();
            }
        }

        private static decimal Clamp(NumericUpDown control, int value)
        {
            return Math.Max(control.Minimum, Math.Min(control.Maximum, value));
        }

        public void SetSelection(int start, int length)
        {
            this.selectionStart = start;
            this.selectionLength = length;
            UpdateSummary();
            RaiseChanged();
        }

        public Socket_ByteSweepPresetInfo ReadSettings(int selectionStart, int selectionLength)
        {
            return new Socket_ByteSweepPresetInfo
            {
                BMode = this.IsPairMode ? Socket_ByteSweepMode.PairCombination : Socket_ByteSweepMode.Sequential,
                BLoopCount = (int)this.loopCount.Value,
                BInterval = (int)this.interval.Value,
                BNextInterval = (int)this.nextInterval.Value,
                BStart = selectionStart,
                BLength = selectionLength,
                BCombinationFirstPosition = (int)this.firstPosition.Value,
                BCombinationFirstLength = (int)this.firstLength.Value,
                BCombinationFirstInterval = (int)this.firstInterval.Value,
                BCombinationSecondPosition = (int)this.secondPosition.Value,
                BCombinationSecondLength = (int)this.secondLength.Value,
                BCombinationSecondInterval = (int)this.secondInterval.Value
            };
        }

        public void SetPresetState(bool hasPreset)
        {
            this.save.Text = hasPreset
                ? ResourceText("UI_UpdateSweepPreset")
                : ResourceText("UI_SaveSweepPreset");
        }

        public void SetRunning(bool running, string progressText)
        {
            this.SetOperationState(running, running, progressText);
        }

        public void SetOperationState(bool busy, bool runningHere, string progressText)
        {
            this.busy = busy;
            if (busy)
            {
                CancelPositionPick();
            }
            this.send.Enabled = !busy;
            this.save.Enabled = !busy;
            this.stop.Enabled = busy && runningHere;
            this.status.Text = string.IsNullOrWhiteSpace(progressText)
                ? (runningHere
                    ? ResourceText("ByteSweep_Running")
                    : ResourceText("ByteSweep_Idle"))
                : progressText;
            this.toolTip.SetToolTip(this.status, this.status.Text);
            UpdatePairEnabled();
        }

        public bool ApplyPickedPosition(bool first, int position)
        {
            NumericUpDown target = first ? this.firstPosition : this.secondPosition;
            NumericUpDown other = first ? this.secondPosition : this.firstPosition;
            if (position < target.Minimum || position > target.Maximum)
            {
                return false;
            }

            if (position == (int)other.Value)
            {
                this.status.ForeColor = Color.Firebrick;
                this.status.Text = ResourceText("ByteSweep_PickDuplicate");
                this.toolTip.SetToolTip(this.status, this.status.Text);
                return false;
            }

            target.Value = position;
            ResetPositionPickButtons();
            this.status.ForeColor = Color.RoyalBlue;
            this.status.Text = string.Format(
                ResourceText("ByteSweep_PickComplete"),
                first ? "A" : "B",
                position);
            this.toolTip.SetToolTip(this.status, this.status.Text);
            return true;
        }

        public void CancelPositionPick()
        {
            if (this.positionPickTarget == 0)
            {
                return;
            }

            ResetPositionPickButtons();
            this.status.ForeColor = Color.RoyalBlue;
            this.status.Text = ResourceText("ByteSweep_Idle");
            this.toolTip.SetToolTip(this.status, this.status.Text);
        }

        private void SetPositionPickState(int target)
        {
            if (this.busy || !this.IsPairMode)
            {
                return;
            }

            this.positionPickTarget = target;
            this.pickFirst.UseVisualStyleBackColor = target != 1;
            this.pickSecond.UseVisualStyleBackColor = target != 2;
            this.pickFirst.BackColor =
                target == 1 ? Color.Gold : SystemColors.Control;
            this.pickSecond.BackColor =
                target == 2 ? Color.Gold : SystemColors.Control;
            this.status.ForeColor = Color.DarkOrange;
            this.status.Text = string.Format(
                ResourceText("ByteSweep_PickInstruction"),
                target == 1 ? "A" : "B");
            this.toolTip.SetToolTip(this.status, this.status.Text);
        }

        private void ResetPositionPickButtons()
        {
            this.positionPickTarget = 0;
            this.pickFirst.UseVisualStyleBackColor = true;
            this.pickSecond.UseVisualStyleBackColor = true;
            this.pickFirst.BackColor = SystemColors.Control;
            this.pickSecond.BackColor = SystemColors.Control;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.toolTip.Dispose();
                this.titleFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private static string ResourceText(string key)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? key;
        }
    }
}
