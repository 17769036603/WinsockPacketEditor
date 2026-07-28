using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    internal sealed class Socket_ByteAnnotationPanel : UserControl
    {
        private readonly ListView list;
        private readonly Control buttons;
        private readonly Label title;
        private readonly Button collapse;
        private readonly Font titleFont;
        private IList<Socket_ByteAnnotationInfo> annotations;

        public event EventHandler AddRequested;
        public event EventHandler EditRequested;
        public event EventHandler DeleteRequested;
        public event EventHandler SelectionRequested;
        public event EventHandler CollapseRequested;

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Child controls are owned and disposed by the Controls collection.")]
        public Socket_ByteAnnotationPanel()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(6);
            MinimumSize = new Size(180, 0);

            titleFont = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
            Panel header = new Panel { Dock = DockStyle.Top, Height = 26 };
            title = new Label
            {
                Text = ResourceText("ByteAnnotation_Title"),
                Dock = DockStyle.Fill,
                Font = titleFont,
                TextAlign = ContentAlignment.MiddleLeft
            };
            collapse = new Button
            {
                Text = ResourceText("ByteAnnotation_Collapse"),
                Dock = DockStyle.Right,
                Width = 26,
                FlatStyle = FlatStyle.System,
                AccessibleName = ResourceText("ByteAnnotation_CollapseButton")
            };
            collapse.Click += delegate { Raise(CollapseRequested); };
            header.Controls.Add(title);
            header.Controls.Add(collapse);
            TableLayoutPanel buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0, 3, 0, 0)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Button add = NewButton(ResourceText("ByteAnnotation_Add"), delegate { Raise(AddRequested); });
            Button edit = NewButton(ResourceText("ByteAnnotation_Edit"), delegate { Raise(EditRequested); });
            Button delete = NewButton(ResourceText("ByteAnnotation_Delete"), delegate { Raise(DeleteRequested); });
            buttons.Controls.AddRange(new Control[] { add, edit, delete });

            list = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                HideSelection = false,
                View = View.Details,
                AccessibleName = ResourceText("ByteAnnotation_List")
            };
            list.Columns.Add(ResourceText("ByteAnnotation_Range"), 72);
            list.Columns.Add(ResourceText("ByteAnnotation_Note"), 118);
            list.DoubleClick += delegate { Raise(EditRequested); };
            list.SelectedIndexChanged += delegate { Raise(SelectionRequested); };
            Controls.Add(list);
            Controls.Add(buttons);
            Controls.Add(header);
            this.buttons = buttons;
        }

        public Socket_ByteAnnotationInfo SelectedAnnotation
        {
            get { return list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as Socket_ByteAnnotationInfo; }
        }

        public void Bind(IList<Socket_ByteAnnotationInfo> value)
        {
            annotations = value;
            RefreshItems();
        }

        public void RefreshItems()
        {
            Socket_ByteAnnotationInfo selected = SelectedAnnotation;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (Socket_ByteAnnotationInfo item in (annotations ?? new List<Socket_ByteAnnotationInfo>()).OrderBy(x => x.Start))
            {
                ListViewItem row = new ListViewItem(item.RangeText) { Tag = item, BackColor = Socket_ByteAnnotationEngine.GetBackColor(item.Color) };
                row.SubItems.Add(item.Note ?? string.Empty);
                list.Items.Add(row);
                if (ReferenceEquals(item, selected)) row.Selected = true;
            }
            list.EndUpdate();
        }

        public void SetCollapsed(bool value)
        {
            list.Visible = !value;
            buttons.Visible = !value;
            title.Visible = !value;
            collapse.Text = ResourceText(value ? "ByteAnnotation_Expand" : "ByteAnnotation_Collapse");
            collapse.AccessibleName = ResourceText(
                value ? "ByteAnnotation_ExpandButton" : "ByteAnnotation_CollapseButton");
            Padding = value ? Padding.Empty : new Padding(6);
            MinimumSize = value ? Size.Empty : new Size(180, 0);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The returned control is immediately transferred to a parent Controls collection.")]
        private static Button NewButton(string text, EventHandler click)
        {
            Button button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 2, 0),
                AccessibleName = text
            };
            button.Click += click;
            return button;
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        internal static string ResourceText(string key) { return Properties.Resources.ResourceManager.GetString(key) ?? key; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                titleFont.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class Socket_ByteAnnotationDialog : Form
    {
        private readonly TextBox note;
        private readonly ComboBox color;
        private readonly Socket_ByteAnnotationInfo annotation;

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Child controls are owned and disposed by the form Controls collection.")]
        public Socket_ByteAnnotationDialog(Socket_ByteAnnotationInfo value)
        {
            annotation = value;
            Text = Socket_ByteAnnotationPanel.ResourceText("ByteAnnotation_Title");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(390, 210);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            Label range = new Label { Left = 14, Top = 14, Width = 350, Text = Socket_ByteAnnotationPanel.ResourceText("ByteAnnotation_RangeLabel") + value.RangeText };
            Label noteLabel = new Label { Left = 14, Top = 45, Width = 60, Text = Socket_ByteAnnotationPanel.ResourceText("ByteAnnotation_NoteLabel") };
            note = new TextBox { Left = 70, Top = 42, Width = 300, Height = 85, Multiline = true, Text = value.Note ?? string.Empty };
            Label colorLabel = new Label { Left = 14, Top = 140, Width = 60, Text = Socket_ByteAnnotationPanel.ResourceText("ByteAnnotation_ColorLabel") };
            color = new ComboBox { Left = 70, Top = 136, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            note.AccessibleName = Socket_ByteAnnotationPanel.ResourceText("ByteAnnotation_Note");
            color.AccessibleName = Socket_ByteAnnotationPanel.ResourceText("ByteAnnotation_Color");
            color.Items.AddRange(Enum.GetValues(typeof(Socket_ByteAnnotationColor)).Cast<Socket_ByteAnnotationColor>()
                .Select(item => new ColorChoice(item)).Cast<object>().ToArray());
            color.SelectedItem = color.Items.Cast<ColorChoice>().First(item => item.Value == value.Color);
            Button ok = new Button { Text = Socket_ByteAnnotationPanel.ResourceText("ByteAnnotation_OK"), Left = 214, Top = 174, Width = 75, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Socket_ByteAnnotationPanel.ResourceText("ByteAnnotation_Cancel"), Left = 295, Top = 174, Width = 75, DialogResult = DialogResult.Cancel };
            ok.Click += delegate { annotation.Note = note.Text.Trim(); annotation.Color = ((ColorChoice)color.SelectedItem).Value; };
            Controls.AddRange(new Control[] { range, noteLabel, note, colorLabel, color, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private sealed class ColorChoice
        {
            public ColorChoice(Socket_ByteAnnotationColor value) { Value = value; }
            public Socket_ByteAnnotationColor Value { get; private set; }
            public override string ToString() { return Socket_ByteAnnotationPanel.ResourceText("ByteAnnotation_" + Value); }
        }
    }
}
