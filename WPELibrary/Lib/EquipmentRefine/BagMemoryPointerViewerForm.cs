using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>只显示当前只读背包快照中的物品表和字段对象地址。</summary>
    public sealed class BagMemoryPointerViewerForm : Form
    {
        private readonly List<PointerRow> rows;
        private readonly BindingSource source = new BindingSource();
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox filter = new TextBox();

        public BagMemoryPointerViewerForm(EquipmentRefineDetector.EquipmentInventory inventory)
        {
            if (inventory == null || inventory.Items == null ||
                !inventory.IsUsableFor(EquipmentTargetMode.Bag))
            {
                throw new ArgumentException("需要有效的只读背包快照。", "inventory");
            }

            this.rows = BuildRows(inventory);
            this.Text = "背包内存指针（只读快照）";
            this.Width = 1280;
            this.Height = 700;
            this.MinimumSize = new Size(900, 480);
            this.StartPosition = FormStartPosition.CenterParent;

            int itemPointers = inventory.Items.Count(item =>
                item != null && !string.IsNullOrWhiteSpace(item.ItemAddress));
            Label status = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "只读：readOnly=true / actionAuthorized=false | PID={0}, startTicks={1} | 物品 {2} 件 | 物品表指针 {3}/{2} | 快照时间 {4}",
                    inventory.Binding.pid,
                    inventory.Binding.startTicks,
                    inventory.Items.Count,
                    itemPointers,
                    inventory.UpdatedAtUtc == DateTime.MinValue
                        ? "未知"
                        : inventory.UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)),
                Padding = new Padding(0, 0, 0, 6)
            };
            if (itemPointers != inventory.Items.Count)
            {
                status.Text += " | 设备端 reader 未提供部分 itemAddress；需更新 equipment-streamd 后重新读取";
                status.ForeColor = Color.DarkOrange;
            }

            FlowLayoutPanel filterRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 6)
            };
            filterRow.Controls.Add(new Label
            {
                Text = "筛选物品/字段：",
                AutoSize = true,
                Padding = new Padding(0, 5, 0, 0)
            });
            this.filter.Width = 260;
            this.filter.TextChanged += this.FilterChanged;
            filterRow.Controls.Add(this.filter);

            this.ConfigureGrid();
            this.source.DataSource = this.rows;
            this.grid.DataSource = this.source;

            Label note = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = "物品表指针指向本次快照读取到的 LuaJIT GCtable；字段值对象指针仅在字段值为 GC 对象时出现。进程重启、背包变化或 GC 搬移后请重新读取。此窗口不写内存、不发包、不操作游戏。",
                Padding = new Padding(0, 6, 0, 0)
            };

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(status, 0, 0);
            layout.Controls.Add(filterRow, 0, 1);
            layout.Controls.Add(this.grid, 0, 2);
            layout.Controls.Add(note, 0, 3);
            this.Controls.Add(layout);
        }

        private void ConfigureGrid()
        {
            this.grid.Dock = DockStyle.Fill;
            this.grid.ReadOnly = true;
            this.grid.AllowUserToAddRows = false;
            this.grid.AllowUserToDeleteRows = false;
            this.grid.AllowUserToResizeRows = false;
            this.grid.RowHeadersVisible = false;
            this.grid.AutoGenerateColumns = false;
            this.grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            this.grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            this.grid.MultiSelect = false;
            this.grid.Columns.Add(CreateColumn("ItemName", "物品", 18F));
            this.grid.Columns.Add(CreateColumn("ItemId", "物品 ID", 8F));
            this.grid.Columns.Add(CreateColumn("ItemTypeId", "类型 ID", 8F));
            this.grid.Columns.Add(CreateColumn("Slot", "背包键", 12F));
            this.grid.Columns.Add(CreateColumn("ItemAddress", "物品表指针", 12F, true));
            this.grid.Columns.Add(CreateColumn("FieldKey", "字段", 17F));
            this.grid.Columns.Add(CreateColumn("Kind", "类型", 7F));
            this.grid.Columns.Add(CreateColumn("Value", "值", 20F));
            this.grid.Columns.Add(CreateColumn("ValueAddress", "字段值对象指针", 13F, true));
        }

        private static DataGridViewTextBoxColumn CreateColumn(
            string propertyName,
            string title,
            float fillWeight,
            bool addressColumn = false)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn
            {
                Name = propertyName,
                HeaderText = title,
                DataPropertyName = propertyName,
                FillWeight = fillWeight,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            if (addressColumn)
            {
                column.DefaultCellStyle.Font = new Font(FontFamily.GenericMonospace, 8.5F);
            }
            return column;
        }

        private static List<PointerRow> BuildRows(
            EquipmentRefineDetector.EquipmentInventory inventory)
        {
            List<PointerRow> result = new List<PointerRow>();
            foreach (EquipmentRefineDetector.EquipmentSlot item in inventory.Items ??
                new List<EquipmentRefineDetector.EquipmentSlot>())
            {
                if (item == null) continue;
                string name = string.IsNullOrWhiteSpace(item.EquipmentName)
                    ? "未解码物品"
                    : item.EquipmentName;
                if (item.RawFields == null || item.RawFields.Count == 0)
                {
                    result.Add(new PointerRow
                    {
                        ItemName = name,
                        ItemId = item.ItemId ?? string.Empty,
                        ItemTypeId = item.ItemTypeId ?? string.Empty,
                        Slot = item.Slot ?? string.Empty,
                        ItemAddress = FormatAddress(item.ItemAddress),
                        FieldKey = "<未提供字段>",
                        Kind = string.Empty,
                        Value = string.Empty,
                        ValueAddress = string.Empty
                    });
                    continue;
                }

                foreach (EquipmentRefineDetector.RawField field in item.RawFields)
                {
                    if (field == null) continue;
                    result.Add(new PointerRow
                    {
                        ItemName = name,
                        ItemId = item.ItemId ?? string.Empty,
                        ItemTypeId = item.ItemTypeId ?? string.Empty,
                        Slot = item.Slot ?? string.Empty,
                        ItemAddress = FormatAddress(item.ItemAddress),
                        FieldKey = field.Key ?? string.Empty,
                        Kind = field.Kind ?? string.Empty,
                        Value = field.Value ?? string.Empty,
                        ValueAddress = FormatAddress(field.Address)
                    });
                }
            }
            return result;
        }

        private static string FormatAddress(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<未提供>" : value;
        }

        private void FilterChanged(object sender, EventArgs e)
        {
            string text = this.filter.Text ?? string.Empty;
            if (text.Length == 0)
            {
                this.source.DataSource = this.rows;
                return;
            }

            this.source.DataSource = this.rows.Where(row =>
                row.ItemName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.ItemId.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.ItemTypeId.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.Slot.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.ItemAddress.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.FieldKey.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.Value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.ValueAddress.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        private sealed class PointerRow
        {
            public string ItemName { get; set; }
            public string ItemId { get; set; }
            public string ItemTypeId { get; set; }
            public string Slot { get; set; }
            public string ItemAddress { get; set; }
            public string FieldKey { get; set; }
            public string Kind { get; set; }
            public string Value { get; set; }
            public string ValueAddress { get; set; }
        }
    }
}
