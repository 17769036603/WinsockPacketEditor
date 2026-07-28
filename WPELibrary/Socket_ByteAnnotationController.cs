using Be.Windows.Forms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    internal sealed class Socket_ByteAnnotationController : IDisposable
    {
        private readonly HexBox hexBox;
        private readonly Socket_ByteAnnotationPanel panel;
        private readonly ToolTip toolTip = new ToolTip();
        private readonly TableLayoutPanel layout;
        private readonly int column;
        private readonly ToolStripSeparator menuSeparator;
        private readonly ToolStripItem addMenuItem;
        private readonly ToolStripItem editMenuItem;
        private readonly ToolStripItem deleteMenuItem;
        private IList<Socket_ByteAnnotationInfo> annotations = new List<Socket_ByteAnnotationInfo>();
        private long lastTipIndex = -1;
        private readonly Func<bool> canEdit;
        private bool collapsed;
        private bool disposed;

        public event EventHandler Changed;

        public Socket_ByteAnnotationController(HexBox hexBox, TableLayoutPanel layout, int column, int row, int rowSpan, Func<bool> canEdit = null)
        {
            this.hexBox = hexBox;
            this.layout = layout;
            this.column = column;
            this.canEdit = canEdit ?? delegate { return true; };
            while (layout.ColumnStyles.Count <= column)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            layout.ColumnCount = Math.Max(layout.ColumnCount, column + 1);
            layout.ColumnStyles[column].SizeType = SizeType.Absolute;
            layout.ColumnStyles[column].Width = 220F;

            panel = new Socket_ByteAnnotationPanel();
            layout.Controls.Add(panel, column, row);
            layout.SetRowSpan(panel, rowSpan);
            panel.AddRequested += Panel_AddRequested;
            panel.EditRequested += Panel_EditRequested;
            panel.DeleteRequested += Panel_DeleteRequested;
            panel.SelectionRequested += Panel_SelectionRequested;
            panel.CollapseRequested += Panel_CollapseRequested;

            if (hexBox.ContextMenuStrip != null)
            {
                menuSeparator = new ToolStripSeparator();
                addMenuItem = hexBox.ContextMenuStrip.Items.Add(Text("ByteAnnotation_AddMenu"), null, Panel_AddRequested);
                editMenuItem = hexBox.ContextMenuStrip.Items.Add(Text("ByteAnnotation_EditMenu"), null, Panel_EditRequested);
                deleteMenuItem = hexBox.ContextMenuStrip.Items.Add(Text("ByteAnnotation_DeleteMenu"), null, Panel_DeleteRequested);
                hexBox.ContextMenuStrip.Items.Insert(
                    hexBox.ContextMenuStrip.Items.IndexOf(addMenuItem), menuSeparator);
            }
            hexBox.MouseMove += HexBox_MouseMove;
            hexBox.Disposed += HexBox_Disposed;
        }

        public void Bind(IList<Socket_ByteAnnotationInfo> value)
        {
            annotations = value ?? new List<Socket_ByteAnnotationInfo>();
            panel.Bind(annotations);
            hexBox.ByteStyleProvider = new Socket_ByteAnnotationStyleProvider(annotations);
            hexBox.Invalidate();
        }

        public void Refresh()
        {
            panel.RefreshItems();
            hexBox.Invalidate();
        }

        private void Add()
        {
            if (!canEdit()) { ShowMessage("ByteAnnotation_SendLocked"); return; }
            if (hexBox.ByteProvider == null || hexBox.SelectionLength <= 0) { ShowMessage("ByteAnnotation_SelectBytes"); return; }
            int start = checked((int)hexBox.SelectionStart);
            int length = checked((int)hexBox.SelectionLength);
            if (Socket_ByteAnnotationEngine.Overlaps(annotations, start, length, null)) { ShowMessage("ByteAnnotation_NoOverlap"); return; }
            Socket_ByteAnnotationInfo item = new Socket_ByteAnnotationInfo { Start = start, Length = length, Color = Socket_ByteAnnotationColor.Yellow };
            using (Socket_ByteAnnotationDialog dialog = new Socket_ByteAnnotationDialog(item))
                if (dialog.ShowDialog(hexBox.FindForm()) == DialogResult.OK)
                {
                    annotations.Add(item);
                    Refresh();
                    OnChanged();
                }
        }

        private void Edit()
        {
            if (!canEdit()) { ShowMessage("ByteAnnotation_SendLocked"); return; }
            Socket_ByteAnnotationInfo item = GetCurrent();
            if (item == null) { ShowMessage("ByteAnnotation_SelectAnnotation"); return; }
            Socket_ByteAnnotationInfo copy = item.Clone();
            using (Socket_ByteAnnotationDialog dialog = new Socket_ByteAnnotationDialog(copy))
                if (dialog.ShowDialog(hexBox.FindForm()) == DialogResult.OK)
                {
                    item.Note = copy.Note;
                    item.Color = copy.Color;
                    Refresh();
                    OnChanged();
                }
        }

        private void Delete()
        {
            if (!canEdit()) { ShowMessage("ByteAnnotation_SendLocked"); return; }
            Socket_ByteAnnotationInfo item = GetCurrent();
            if (item == null) { ShowMessage("ByteAnnotation_SelectAnnotation"); return; }
            annotations.Remove(item);
            Refresh();
            OnChanged();
        }

        private Socket_ByteAnnotationInfo GetCurrent()
        {
            Socket_ByteAnnotationInfo selected = panel.SelectedAnnotation;
            if (selected != null) return selected;
            return annotations.FirstOrDefault(x => hexBox.SelectionStart >= x.Start && hexBox.SelectionStart < x.End);
        }

        private void SelectCurrent()
        {
            Socket_ByteAnnotationInfo item = panel.SelectedAnnotation;
            if (item == null || hexBox.ByteProvider == null) return;
            hexBox.Select(item.Start, item.Length);
            hexBox.ScrollByteIntoView(item.Start);
            hexBox.Focus();
        }

        private void HexBox_MouseMove(object sender, MouseEventArgs e)
        {
            long index = hexBox.GetByteIndexAt(e.Location);
            if (index == lastTipIndex) return;
            lastTipIndex = index;
            Socket_ByteAnnotationInfo item = annotations.FirstOrDefault(x => index >= x.Start && index < x.End);
            toolTip.SetToolTip(hexBox, item == null ? string.Empty : item.Note);
        }

        private void Panel_AddRequested(object sender, EventArgs e) { Add(); }
        private void Panel_EditRequested(object sender, EventArgs e) { Edit(); }
        private void Panel_DeleteRequested(object sender, EventArgs e) { Delete(); }
        private void Panel_SelectionRequested(object sender, EventArgs e) { SelectCurrent(); }
        private void Panel_CollapseRequested(object sender, EventArgs e)
        {
            collapsed = !collapsed;
            layout.ColumnStyles[column].Width = collapsed ? 28F : 220F;
            panel.SetCollapsed(collapsed);
        }
        private void HexBox_Disposed(object sender, EventArgs e) { Dispose(); }
        private void OnChanged() { if (Changed != null) Changed(this, EventArgs.Empty); }
        private static string Text(string key) { return Properties.Resources.ResourceManager.GetString(key) ?? key; }
        private void ShowMessage(string key)
        {
            MessageBox.Show(hexBox.FindForm(), Text(key), Text("ByteAnnotation_Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1,
                hexBox.RightToLeft == RightToLeft.Yes
                    ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading
                    : 0);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            hexBox.MouseMove -= HexBox_MouseMove;
            hexBox.Disposed -= HexBox_Disposed;
            panel.AddRequested -= Panel_AddRequested;
            panel.EditRequested -= Panel_EditRequested;
            panel.DeleteRequested -= Panel_DeleteRequested;
            panel.SelectionRequested -= Panel_SelectionRequested;
            panel.CollapseRequested -= Panel_CollapseRequested;
            if (hexBox.ContextMenuStrip != null && !hexBox.ContextMenuStrip.IsDisposed)
            {
                if (menuSeparator != null) hexBox.ContextMenuStrip.Items.Remove(menuSeparator);
                if (addMenuItem != null) hexBox.ContextMenuStrip.Items.Remove(addMenuItem);
                if (editMenuItem != null) hexBox.ContextMenuStrip.Items.Remove(editMenuItem);
                if (deleteMenuItem != null) hexBox.ContextMenuStrip.Items.Remove(deleteMenuItem);
            }
            if (menuSeparator != null) menuSeparator.Dispose();
            if (addMenuItem != null) addMenuItem.Dispose();
            if (editMenuItem != null) editMenuItem.Dispose();
            if (deleteMenuItem != null) deleteMenuItem.Dispose();
            hexBox.ByteStyleProvider = null;
            toolTip.Dispose();
            if (!panel.IsDisposed) panel.Dispose();
            Changed = null;
        }
    }
}
