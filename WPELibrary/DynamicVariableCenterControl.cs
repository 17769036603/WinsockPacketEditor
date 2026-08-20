using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    internal sealed class DynamicVariableCenterControl : UserControl
    {
        private readonly DataGridView currentGrid = new DataGridView();
        private readonly DataGridView ruleGrid = new DataGridView();
        private readonly DataGridView knownGrid = new DataGridView();
        private readonly Timer refreshTimer = new Timer();
        private readonly Button clearCurrentButton = new Button();
        private readonly Button pauseRuleButton = new Button();
        private readonly Button editRuleButton = new Button();
        private readonly Button testRuleButton = new Button();
        private readonly Button deleteRuleButton = new Button();
        private readonly Button editLabelButton = new Button();
        private bool refreshing;

        public DynamicVariableCenterControl()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(4);
            BuildGrid(currentGrid, new[] { "符号", "显示名称", "当前值", "长度", "来源规则", "更新时间", "状态", "人工标签" });
            BuildGrid(ruleGrid, new[] { "规则", "启用", "方向", "长度", "模板", "字段" });
            BuildGrid(knownGrid, new[] { "变量", "值", "标签", "首次发现", "最后出现", "次数", "来源规则" });

            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            TabPage currentPage = new TabPage("当前变量");
            TabPage rulePage = new TabPage("提取规则");
            TabPage knownPage = new TabPage("已发现值");
            currentPage.Controls.Add(currentGrid);
            rulePage.Controls.Add(ruleGrid);
            knownPage.Controls.Add(knownGrid);
            tabs.TabPages.Add(currentPage);
            tabs.TabPages.Add(rulePage);
            tabs.TabPages.Add(knownPage);

            clearCurrentButton.Text = "清空当前值";
            clearCurrentButton.AutoSize = true;
            clearCurrentButton.Click += (sender, args) => DynamicVariableRuntime.Variables.ClearCurrent();
            pauseRuleButton.Text = "暂停/恢复选中规则";
            pauseRuleButton.AutoSize = true;
            pauseRuleButton.Click += PauseRuleButton_Click;
            editRuleButton.Text = "编辑选中规则";
            editRuleButton.AutoSize = true;
            editRuleButton.Click += EditRuleButton_Click;
            testRuleButton.Text = "测试选中规则";
            testRuleButton.AutoSize = true;
            testRuleButton.Click += TestRuleButton_Click;
            deleteRuleButton.Text = "删除选中规则";
            deleteRuleButton.AutoSize = true;
            deleteRuleButton.Click += DeleteRuleButton_Click;
            editLabelButton.Text = "修改已发现值标签";
            editLabelButton.AutoSize = true;
            editLabelButton.Click += EditLabelButton_Click;
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                AutoSize = true,
                WrapContents = true
            };
            actions.Controls.Add(clearCurrentButton);
            actions.Controls.Add(pauseRuleButton);
            actions.Controls.Add(editRuleButton);
            actions.Controls.Add(testRuleButton);
            actions.Controls.Add(deleteRuleButton);
            actions.Controls.Add(editLabelButton);

            Controls.Add(tabs);
            Controls.Add(actions);
            refreshTimer.Interval = 800;
            refreshTimer.Tick += (sender, args) => RefreshSnapshot();
            DynamicVariableRuntime.Changed += DynamicVariableRuntime_Changed;
            refreshTimer.Start();
            RefreshSnapshot();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
                DynamicVariableRuntime.Changed -= DynamicVariableRuntime_Changed;
            }
            base.Dispose(disposing);
        }

        private void BuildGrid(DataGridView grid, IEnumerable<string> columns)
        {
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            foreach (string column in columns)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column, Name = column });
            }
        }

        private void DynamicVariableRuntime_Changed(object sender, EventArgs e)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                try
                {
                    BeginInvoke((Action)RefreshSnapshot);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        private void RefreshSnapshot()
        {
            if (refreshing || IsDisposed)
            {
                return;
            }
            refreshing = true;
            try
            {
                List<DynamicVariableDefinition> definitions = DynamicVariableRuntime.Variables.GetDefinitionsSnapshot();
                Dictionary<Guid, DynamicVariableDefinition> definitionMap = definitions.ToDictionary(item => item.VariableId);
                Dictionary<Guid, ExtractionRule> ruleMap = DynamicVariableRuntime.Variables.GetRulesSnapshot()
                    .ToDictionary(item => item.RuleId);
                List<KnownVariableValue> knownValues = DynamicVariableRuntime.KnownValues.GetSnapshot();

                currentGrid.Rows.Clear();
                foreach (CurrentVariableValue value in DynamicVariableRuntime.Variables.GetCurrentSnapshot())
                {
                    DynamicVariableDefinition definition;
                    definitionMap.TryGetValue(value.VariableId, out definition);
                    ExtractionRule rule;
                    ruleMap.TryGetValue(value.SourceRuleId, out rule);
                    KnownVariableValue known = knownValues.FirstOrDefault(item =>
                        item.VariableId == value.VariableId &&
                        value.Value != null && item.Value != null && item.Value.SequenceEqual(value.Value));
                    string updated = value.UpdatedUtc == default(DateTime)
                        ? string.Empty
                        : value.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    currentGrid.Rows.Add(
                        definition == null ? string.Empty : definition.Symbol,
                        definition == null ? string.Empty : definition.DisplayName,
                        DynamicVariableFormatting.ToHex(value.Value),
                        value.Value == null ? 0 : value.Value.Length,
                        rule == null ? string.Empty : rule.Name,
                        updated,
                        value.IsValid ? "已获取" : "未获取",
                        value.Label ?? (known == null ? string.Empty : known.Label ?? string.Empty));
                }

                ruleGrid.Rows.Clear();
                foreach (ExtractionRule rule in ruleMap.Values.OrderBy(item => item.Name))
                {
                    DataGridViewRow row = ruleGrid.Rows[ruleGrid.Rows.Add(
                        rule.Name,
                        rule.IsEnabled && DynamicVariableRuntime.Variables.IsRuleSessionEnabled(rule.RuleId) ? "启用" : "暂停",
                        rule.PacketType,
                        rule.PatternBytes == null ? 0 : rule.PatternBytes.Length,
                        rule.OriginalTemplate,
                        string.Join(", ", (rule.Fields ?? new List<DynamicField>())
                            .Where(field => field != null)
                            .Select(field => field.Offset + "+" + field.Length)) )];
                    row.Tag = rule;
                }

                knownGrid.Rows.Clear();
                foreach (KnownVariableValue value in knownValues)
                {
                    DynamicVariableDefinition definition;
                    definitionMap.TryGetValue(value.VariableId, out definition);
                    ExtractionRule rule;
                    ruleMap.TryGetValue(value.SourceRuleId, out rule);
                    DataGridViewRow row = knownGrid.Rows[knownGrid.Rows.Add(
                        definition == null ? value.VariableId.ToString("N") : definition.Symbol,
                        value.ValueHex,
                        value.Label ?? string.Empty,
                        value.FirstSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                        value.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                        value.SeenCount,
                        rule == null ? string.Empty : rule.Name)];
                    row.Tag = value;
                }
            }
            catch (InvalidOperationException)
            {
                // The settings dialog can close while a change notification is queued.
            }
            finally
            {
                refreshing = false;
            }
        }

        private void PauseRuleButton_Click(object sender, EventArgs e)
        {
            if (ruleGrid.CurrentRow == null || ruleGrid.CurrentRow.Tag == null)
            {
                return;
            }
            ExtractionRule rule = ruleGrid.CurrentRow.Tag as ExtractionRule;
            if (rule != null)
            {
                bool enabled = DynamicVariableRuntime.Variables.IsRuleSessionEnabled(rule.RuleId);
                DynamicVariableRuntime.Variables.SetRuleSessionEnabled(rule.RuleId, !enabled);
            }
        }

        private ExtractionRule GetSelectedRule()
        {
            return ruleGrid.CurrentRow == null ? null : ruleGrid.CurrentRow.Tag as ExtractionRule;
        }

        private void EditRuleButton_Click(object sender, EventArgs e)
        {
            ExtractionRule rule = GetSelectedRule();
            if (rule != null && DynamicVariableUiActions.EditRule(this, rule))
            {
                RefreshSnapshot();
            }
        }

        private void TestRuleButton_Click(object sender, EventArgs e)
        {
            DynamicVariableUiActions.TestRule(this, GetSelectedRule());
        }

        private void DeleteRuleButton_Click(object sender, EventArgs e)
        {
            ExtractionRule rule = GetSelectedRule();
            if (rule == null || MessageBox.Show(this, "删除选中的提取规则？\r\n当前值也会失效。", "动态变量",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            string error;
            if (DynamicVariableRuntime.Variables.RemoveRule(rule.RuleId, out error))
            {
                if (DynamicVariableRuntime.SaveToDatabase())
                {
                    RefreshSnapshot();
                }
                else
                {
                    MessageBox.Show(this, "动态变量保存失败，当前修改未写入数据库。", "动态变量", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                MessageBox.Show(this, error, "动态变量", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void EditLabelButton_Click(object sender, EventArgs e)
        {
            if (knownGrid.CurrentRow == null || !(knownGrid.CurrentRow.Tag is KnownVariableValue))
            {
                return;
            }
            KnownVariableValue value = (KnownVariableValue)knownGrid.CurrentRow.Tag;
            string label;
            if (!DynamicVariablePrompt.Show(this, "人工标签", "标签", value.Label ?? string.Empty, out label))
            {
                return;
            }
            if (DynamicVariableRuntime.KnownValues.SetLabel(value.VariableId, value.Value, label))
            {
                if (DynamicVariableRuntime.SaveToDatabase())
                {
                    RefreshSnapshot();
                }
                else
                {
                    MessageBox.Show(this, "动态变量保存失败，当前修改未写入数据库。", "动态变量", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }

    internal static class DynamicVariablePrompt
    {
        public static bool Show(IWin32Window owner, string title, string caption, string initial, out string value)
        {
            using (Form form = new Form())
            using (Label label = new Label())
            using (TextBox box = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(380, 120);
                label.Text = caption;
                label.AutoSize = true;
                label.Location = new Point(12, 16);
                box.Text = initial ?? string.Empty;
                box.Location = new Point(12, 40);
                box.Width = 350;
                ok.Text = "确定";
                ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(206, 78);
                cancel.Text = "取消";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(287, 78);
                form.Controls.Add(label);
                form.Controls.Add(box);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                bool accepted = form.ShowDialog(owner) == DialogResult.OK;
                value = accepted ? box.Text.Trim() : null;
                return accepted;
            }
        }
    }
}
