using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    internal sealed class DynamicVariableDefinitionOption
    {
        public DynamicVariableDefinition Definition { get; set; }
        public override string ToString()
        {
            return Definition == null ? "新建变量" : Definition.ToString();
        }
    }

    internal sealed class DynamicVariableDefinitionDialog : Form
    {
        private readonly ComboBox definitionCombo = new ComboBox();
        private readonly TextBox symbolBox = new TextBox();
        private readonly TextBox displayNameBox = new TextBox();
        private readonly TextBox descriptionBox = new TextBox();
        private readonly NumericUpDown lengthBox = new NumericUpDown();
        private readonly CheckBox autoUpdateBox = new CheckBox();
        private readonly List<DynamicVariableDefinitionOption> options;

        public DynamicVariableDefinition Result { get; private set; }

        private DynamicVariableDefinitionDialog(
            int length,
            IEnumerable<DynamicVariableDefinition> definitions,
            DynamicVariableDefinition selected,
            string title)
        {
            this.Text = title;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(430, 260);
            this.MinimumSize = this.ClientSize;

            options = new List<DynamicVariableDefinitionOption>
            {
                new DynamicVariableDefinitionOption()
            };
            options.AddRange((definitions ?? Enumerable.Empty<DynamicVariableDefinition>())
                .Where(item => item != null && item.Length == length)
                .Select(item => new DynamicVariableDefinitionOption { Definition = item.Clone() }));

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 2,
                RowCount = 7
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 6; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            definitionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            definitionCombo.DataSource = options;
            definitionCombo.SelectedIndexChanged += DefinitionCombo_SelectedIndexChanged;
            layout.Controls.Add(new Label { Text = "变量", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            layout.Controls.Add(definitionCombo, 1, 0);
            AddRow(layout, "符号", symbolBox, 1);
            AddRow(layout, "显示名称", displayNameBox, 2);
            AddRow(layout, "说明", descriptionBox, 3);
            lengthBox.Minimum = 1;
            lengthBox.Maximum = 1024;
            lengthBox.Value = Math.Max(1, length);
            lengthBox.Enabled = false;
            AddRow(layout, "长度", lengthBox, 4);
            autoUpdateBox.Text = "自动更新当前变量";
            autoUpdateBox.Checked = true;
            autoUpdateBox.AutoSize = true;
            layout.Controls.Add(autoUpdateBox, 1, 5);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            Button ok = new Button { Text = "确定", AutoSize = true };
            ok.Click += Ok_Click;
            actions.Controls.Add(cancel);
            actions.Controls.Add(ok);
            layout.Controls.Add(actions, 0, 6);
            layout.SetColumnSpan(actions, 2);
            this.Controls.Add(layout);
            this.AcceptButton = ok;
            this.CancelButton = cancel;

            int selectedIndex = selected == null
                ? 0
                : options.FindIndex(item => item.Definition != null && item.Definition.VariableId == selected.VariableId);
            definitionCombo.SelectedIndex = selectedIndex < 0 ? 0 : selectedIndex;
            if (selected != null && selectedIndex < 0)
            {
                Fill(selected);
            }
        }

        public static bool Show(
            IWin32Window owner,
            int length,
            IEnumerable<DynamicVariableDefinition> definitions,
            DynamicVariableDefinition selected,
            string title,
            out DynamicVariableDefinition result)
        {
            using (DynamicVariableDefinitionDialog dialog = new DynamicVariableDefinitionDialog(
                length, definitions, selected, title))
            {
                bool accepted = dialog.ShowDialog(owner) == DialogResult.OK;
                result = accepted ? dialog.Result : null;
                return accepted;
            }
        }

        private void AddRow(TableLayoutPanel layout, string caption, Control control, int row)
        {
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void DefinitionCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            DynamicVariableDefinitionOption option = definitionCombo.SelectedItem as DynamicVariableDefinitionOption;
            Fill(option == null ? null : option.Definition);
        }

        private void Fill(DynamicVariableDefinition definition)
        {
            symbolBox.Text = definition == null ? string.Empty : definition.Symbol;
            displayNameBox.Text = definition == null ? string.Empty : definition.DisplayName;
            descriptionBox.Text = definition == null ? string.Empty : definition.Description;
            autoUpdateBox.Checked = definition == null || definition.IsAutoUpdateEnabled;
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            string symbol = DynamicVariableNames.NormalizeSymbol(symbolBox.Text);
            if (symbol == null)
            {
                MessageBox.Show(this, "符号只能包含字母、数字和下划线，且不能超过 64 个字符。", "动态变量", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DynamicVariableDefinitionOption option = definitionCombo.SelectedItem as DynamicVariableDefinitionOption;
            Guid id = option == null || option.Definition == null
                ? Guid.NewGuid()
                : option.Definition.VariableId;
            Result = new DynamicVariableDefinition
            {
                VariableId = id,
                Symbol = symbol,
                DisplayName = displayNameBox.Text.Trim(),
                Description = descriptionBox.Text.Trim(),
                Length = (int)lengthBox.Value,
                IsAutoUpdateEnabled = autoUpdateBox.Checked
            };
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    internal sealed class DynamicVariableRulePicker : Form
    {
        private readonly ComboBox combo = new ComboBox();
        public ExtractionRule Result { get; private set; }

        private DynamicVariableRulePicker(IEnumerable<ExtractionRule> rules)
        {
            Text = "选择动态规则";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 120);
            combo.Dock = DockStyle.Top;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.DataSource = (rules ?? Enumerable.Empty<ExtractionRule>()).ToList();
            combo.DisplayMember = "Name";
            Button ok = new Button { Text = "确定", AutoSize = true, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 38
            };
            actions.Controls.Add(cancel);
            actions.Controls.Add(ok);
            Controls.Add(combo);
            Controls.Add(actions);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static bool Show(IWin32Window owner, IEnumerable<ExtractionRule> rules, out ExtractionRule result)
        {
            using (DynamicVariableRulePicker picker = new DynamicVariableRulePicker(rules))
            {
                bool accepted = picker.ShowDialog(owner) == DialogResult.OK;
                result = accepted ? picker.combo.SelectedItem as ExtractionRule : null;
                return accepted && result != null;
            }
        }
    }

    internal sealed class DynamicVariableRuleEditDialog : Form
    {
        private readonly TextBox nameBox = new TextBox();
        private readonly CheckBox enabledBox = new CheckBox();
        private readonly Label detailsLabel = new Label();

        public ExtractionRule Result { get; private set; }

        private DynamicVariableRuleEditDialog(ExtractionRule source)
        {
            Text = "编辑提取规则";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(480, 190);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 2,
                RowCount = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            nameBox.Dock = DockStyle.Fill;
            nameBox.Text = source == null ? string.Empty : source.Name;
            layout.Controls.Add(new Label { Text = "规则名称", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            layout.Controls.Add(nameBox, 1, 0);

            enabledBox.Text = "启用自动提取";
            enabledBox.AutoSize = true;
            enabledBox.Checked = source != null && source.IsEnabled;
            layout.Controls.Add(new Label { Text = "状态", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(enabledBox, 1, 1);

            detailsLabel.AutoSize = false;
            detailsLabel.Dock = DockStyle.Fill;
            detailsLabel.Text = source == null
                ? string.Empty
                : string.Format("模板：{0}\r\n方向：{1}\r\n字段：{2}",
                    source.OriginalTemplate,
                    source.PacketType,
                    string.Join(", ", (source.Fields ?? new List<DynamicField>())
                        .Select(field => field.Offset + "+" + field.Length)));
            layout.Controls.Add(new Label { Text = "详情", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            layout.Controls.Add(detailsLabel, 1, 2);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            Button ok = new Button { Text = "确定", AutoSize = true };
            ok.Click += Ok_Click;
            actions.Controls.Add(cancel);
            actions.Controls.Add(ok);
            layout.Controls.Add(actions, 0, 3);
            layout.SetColumnSpan(actions, 2);
            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static bool Show(IWin32Window owner, ExtractionRule source, out ExtractionRule result)
        {
            using (DynamicVariableRuleEditDialog dialog = new DynamicVariableRuleEditDialog(source))
            {
                bool accepted = dialog.ShowDialog(owner) == DialogResult.OK;
                result = accepted ? dialog.Result : null;
                return accepted;
            }
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            string name = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "规则名称不能为空。", "动态变量", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = new ExtractionRule
            {
                RuleId = Guid.Empty,
                Name = name,
                IsEnabled = enabledBox.Checked
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal static class DynamicVariableUiActions
    {
        public static bool CreateRuleFromSelection(
            IWin32Window owner,
            Socket_PacketInfo packet,
            int offset,
            int length)
        {
            if (packet == null || packet.PacketBuffer == null ||
                offset < 0 || length <= 0 || (long)offset + length > packet.PacketBuffer.Length)
            {
                return false;
            }

            DynamicVariableDefinition definition;
            if (!DynamicVariableDefinitionDialog.Show(
                owner,
                length,
                DynamicVariableRuntime.Variables.GetDefinitionsSnapshot(),
                null,
                "创建动态变量",
                out definition))
            {
                return false;
            }

            byte[] pattern = (byte[])packet.PacketBuffer.Clone();
            byte[] mask = new byte[pattern.Length];
            for (int i = offset; i < offset + length; i++)
            {
                mask[i] = 1;
            }
            if (!ConfirmWeakPattern(owner, mask))
            {
                return false;
            }
            if (HasExtractionSource(definition.VariableId, Guid.Empty))
            {
                ShowError(owner, "所选变量已经由另一条规则提供，请选择其他变量。");
                return false;
            }
            string error;
            if (!DynamicVariableRuntime.Variables.AddOrUpdateDefinition(definition, out error))
            {
                ShowError(owner, error);
                return false;
            }
            ExtractionRule rule = new ExtractionRule
            {
                RuleId = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(definition.DisplayName)
                    ? "规则-" + definition.Symbol
                    : definition.DisplayName,
                IsEnabled = true,
                PatternBytes = pattern,
                WildcardMask = mask,
                PacketType = packet.PacketType,
                Fields = new List<DynamicField>
                {
                    new DynamicField
                    {
                        FieldId = Guid.NewGuid(),
                        VariableId = definition.VariableId,
                        Offset = offset,
                        Length = length
                    }
                }
            };
            if (!DynamicVariableRuntime.Variables.AddOrUpdateRule(rule, out error))
            {
                ShowError(owner, error);
                return false;
            }
            if (!DynamicVariableRuntime.SaveToDatabase())
            {
                ShowError(owner, "动态变量保存失败，当前修改未写入数据库。");
                return false;
            }
            return true;
        }

        public static bool AddFieldToExistingRule(
            IWin32Window owner,
            Socket_PacketInfo packet,
            int offset,
            int length)
        {
            if (packet == null || packet.PacketBuffer == null || offset < 0 || length <= 0 ||
                (long)offset + length > packet.PacketBuffer.Length)
            {
                return false;
            }
            List<ExtractionRule> candidates = DynamicVariableRuntime.Variables.GetRulesSnapshot()
                .Where(rule => rule.PacketType == packet.PacketType &&
                    rule.PatternBytes != null && rule.PatternBytes.Length == packet.PacketBuffer.Length &&
                    PatternMatcher.Matches(rule, packet.PacketBuffer) &&
                    (rule.Fields ?? new List<DynamicField>()).All(field => field != null &&
                        !DynamicVariableRange.Overlaps(field.Offset, field.Length, offset, length)))
                .ToList();
            ExtractionRule selected;
            if (!DynamicVariableRulePicker.Show(owner, candidates, out selected))
            {
                return false;
            }

            DynamicVariableDefinition definition;
            if (!DynamicVariableDefinitionDialog.Show(
                owner,
                length,
                DynamicVariableRuntime.Variables.GetDefinitionsSnapshot(),
                null,
                "添加动态字段",
                out definition))
            {
                return false;
            }

            ExtractionRule updated = selected.Clone();
            for (int i = offset; i < offset + length; i++)
            {
                updated.WildcardMask[i] = 1;
            }
            if (!ConfirmWeakPattern(owner, updated.WildcardMask))
            {
                return false;
            }
            if ((updated.Fields ?? new List<DynamicField>()).Any(field =>
                field != null && field.VariableId == definition.VariableId))
            {
                ShowError(owner, "同一条规则不能重复提取同一个变量。");
                return false;
            }
            if (HasExtractionSource(definition.VariableId, selected.RuleId))
            {
                ShowError(owner, "所选变量已经由另一条规则提供，请选择其他变量。");
                return false;
            }
            string error;
            if (!DynamicVariableRuntime.Variables.AddOrUpdateDefinition(definition, out error))
            {
                ShowError(owner, error);
                return false;
            }
            updated.Fields.Add(new DynamicField
            {
                FieldId = Guid.NewGuid(),
                VariableId = definition.VariableId,
                Offset = offset,
                Length = length
            });
            if (!DynamicVariableRuntime.Variables.AddOrUpdateRule(updated, out error))
            {
                ShowError(owner, error);
                return false;
            }
            if (!DynamicVariableRuntime.SaveToDatabase())
            {
                ShowError(owner, "动态变量保存失败，当前修改未写入数据库。");
                return false;
            }
            return true;
        }

        public static bool AddBinding(
            IWin32Window owner,
            Socket_PacketInfo packet,
            int offset,
            int length)
        {
            if (packet == null || packet.PacketBuffer == null || offset < 0 || length <= 0 ||
                (long)offset + length > packet.PacketBuffer.Length)
            {
                ShowError(owner, "没有长度匹配的动态变量。请先创建变量。");
                return false;
            }
            if (VariableResolver.HasRangeConflict(packet.VariableBindings, offset, length))
            {
                ShowError(owner, "变量绑定范围不能重叠。");
                return false;
            }
            List<DynamicVariableDefinition> definitions = DynamicVariableRuntime.Variables.GetDefinitionsSnapshot()
                .Where(item => item.Length == length)
                .ToList();
            DynamicVariableDefinition definition;
            if (!DynamicVariableDefinitionDialog.Show(owner, length, definitions, null, "选择变量绑定", out definition))
            {
                return false;
            }
            List<PresetVariableBinding> bindings = packet.VariableBindings ?? new List<PresetVariableBinding>();
            if (bindings.Any(item => item != null && item.VariableId == definition.VariableId))
            {
                ShowError(owner, "同一封包不能重复绑定同一个变量。");
                return false;
            }
            string error;
            if (!DynamicVariableRuntime.Variables.AddOrUpdateDefinition(definition, out error))
            {
                ShowError(owner, error);
                return false;
            }
            bindings.Add(new PresetVariableBinding
            {
                VariableId = definition.VariableId,
                Offset = offset,
                Length = length
            });
            packet.VariableBindings = bindings;
            if (!DynamicVariableRuntime.SaveToDatabase())
            {
                bindings.RemoveAt(bindings.Count - 1);
                packet.VariableBindings = bindings;
                ShowError(owner, "动态变量保存失败，当前绑定未写入数据库。");
                return false;
            }
            return true;
        }

        public static bool ChangeBinding(
            IWin32Window owner,
            Socket_PacketInfo packet,
            int offset,
            int length)
        {
            if (packet == null)
            {
                return false;
            }
            PresetVariableBinding binding = (packet.VariableBindings ?? new List<PresetVariableBinding>())
                .FirstOrDefault(item => item != null && item.Offset == offset && item.Length == length);
            if (binding == null)
            {
                ShowError(owner, "当前选区没有变量绑定。");
                return false;
            }
            DynamicVariableDefinition current;
            DynamicVariableRuntime.TryGetDefinition(binding.VariableId, out current);
            DynamicVariableDefinition edited;
            if (!DynamicVariableDefinitionDialog.Show(
                owner,
                length,
                DynamicVariableRuntime.Variables.GetDefinitionsSnapshot(),
                current,
                "修改变量绑定",
                out edited))
            {
                return false;
            }
            if ((packet.VariableBindings ?? new List<PresetVariableBinding>())
                .Any(item => item != null && item != binding && item.VariableId == edited.VariableId))
            {
                ShowError(owner, "同一封包不能重复绑定同一个变量。");
                return false;
            }
            string error;
            if (!DynamicVariableRuntime.Variables.AddOrUpdateDefinition(edited, out error))
            {
                ShowError(owner, error);
                return false;
            }
            Guid previousVariableId = binding.VariableId;
            binding.VariableId = edited.VariableId;
            if (!DynamicVariableRuntime.SaveToDatabase())
            {
                binding.VariableId = previousVariableId;
                ShowError(owner, "动态变量保存失败，当前绑定未写入数据库。");
                return false;
            }
            return true;
        }

        public static bool RemoveBinding(IWin32Window owner, Socket_PacketInfo packet, int offset, int length)
        {
            if (packet == null || packet.VariableBindings == null)
            {
                return false;
            }
            PresetVariableBinding binding = packet.VariableBindings
                .FirstOrDefault(item => item != null && item.Offset == offset && item.Length == length);
            if (binding == null)
            {
                ShowError(owner, "当前选区没有变量绑定。");
                return false;
            }
            if (MessageBox.Show(owner, "取消当前变量绑定？", "动态变量", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return false;
            }
            packet.VariableBindings.Remove(binding);
            return true;
        }

        public static bool EditDefinition(IWin32Window owner, Guid variableId, int length)
        {
            DynamicVariableDefinition current;
            if (!DynamicVariableRuntime.TryGetDefinition(variableId, out current))
            {
                return false;
            }
            DynamicVariableDefinition edited;
            if (!DynamicVariableDefinitionDialog.Show(
                owner,
                length,
                DynamicVariableRuntime.Variables.GetDefinitionsSnapshot(),
                current,
                "修改动态变量",
                out edited))
            {
                return false;
            }
            string error;
            if (!DynamicVariableRuntime.Variables.AddOrUpdateDefinition(edited, out error))
            {
                ShowError(owner, error);
                return false;
            }
            if (!DynamicVariableRuntime.SaveToDatabase())
            {
                ShowError(owner, "动态变量保存失败，当前修改未写入数据库。");
                return false;
            }
            return true;
        }

        public static bool EditRule(IWin32Window owner, ExtractionRule source)
        {
            if (source == null)
            {
                return false;
            }
            ExtractionRule edited;
            if (!DynamicVariableRuleEditDialog.Show(owner, source, out edited))
            {
                return false;
            }
            ExtractionRule updated = source.Clone();
            updated.Name = edited.Name;
            updated.IsEnabled = edited.IsEnabled;
            string error;
            if (!DynamicVariableRuntime.Variables.AddOrUpdateRule(updated, out error))
            {
                ShowError(owner, error);
                return false;
            }
            if (!DynamicVariableRuntime.SaveToDatabase())
            {
                ShowError(owner, "动态变量保存失败，当前修改未写入数据库。");
                return false;
            }
            return true;
        }

        public static void TestRule(IWin32Window owner, ExtractionRule source)
        {
            if (source == null)
            {
                return;
            }
            string initial = source.PatternBytes == null || source.WildcardMask == null
                ? string.Empty
                : string.Join(" ", Enumerable.Range(0, source.PatternBytes.Length)
                    .Select(index => source.WildcardMask[index] == 0
                        ? source.PatternBytes[index].ToString("X2")
                        : "00"));
            string input;
            if (!DynamicVariablePrompt.Show(owner, "测试提取规则", "输入封包 Hex", initial, out input))
            {
                return;
            }
            byte[] buffer = Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, input);
            Dictionary<Guid, DynamicVariableDefinition> definitions = DynamicVariableRuntime.Variables
                .GetDefinitionsSnapshot()
                .ToDictionary(item => item.VariableId);
            List<DynamicVariableUpdate> updates;
            string error;
            if (!VariableExtractor.TryExtract(source, buffer, definitions, out updates, out error))
            {
                ShowError(owner, error);
                return;
            }
            string result = string.Join("\r\n", updates.Select(update =>
            {
                DynamicVariableDefinition definition;
                return definitions.TryGetValue(update.VariableId, out definition)
                    ? definition.Symbol + " = " + DynamicVariableFormatting.ToHex(update.Value)
                    : update.VariableId.ToString("N") + " = " + DynamicVariableFormatting.ToHex(update.Value);
            }));
            MessageBox.Show(owner, string.IsNullOrEmpty(result) ? "规则匹配，但没有动态字段。" : result,
                "测试提取规则", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static bool RemoveField(IWin32Window owner, Socket_PacketInfo packet, int offset, int length)
        {
            ExtractionRule rule = FindMatchingRule(packet, offset, length);
            if (rule == null)
            {
                ShowError(owner, "当前选区没有匹配的动态字段。");
                return false;
            }
            if (MessageBox.Show(owner, "取消动态字段会修改全局提取规则，是否继续？", "动态变量", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return false;
            }
            ExtractionRule updated = rule.Clone();
            DynamicField field = (updated.Fields ?? new List<DynamicField>())
                .FirstOrDefault(item => item != null && item.Offset == offset && item.Length == length);
            if (field == null)
            {
                return false;
            }
            updated.Fields.Remove(field);
            string error;
            bool ok = updated.Fields.Count == 0
                ? DynamicVariableRuntime.Variables.RemoveRule(updated.RuleId, out error)
                : DynamicVariableRuntime.Variables.AddOrUpdateRule(updated, out error);
            if (!ok)
            {
                ShowError(owner, error);
                return false;
            }
            if (!DynamicVariableRuntime.SaveToDatabase())
            {
                ShowError(owner, "动态变量保存失败，当前修改未写入数据库。");
                return false;
            }
            return true;
        }

        public static ExtractionRule FindMatchingRule(Socket_PacketInfo packet, int offset, int length)
        {
            if (packet == null || packet.PacketBuffer == null || offset < 0 || length <= 0 ||
                (long)offset + length > packet.PacketBuffer.Length)
            {
                return null;
            }
            return DynamicVariableRuntime.Variables.GetRulesSnapshot()
                .Where(rule => rule.PacketType == packet.PacketType &&
                    rule.PatternBytes != null && rule.PatternBytes.Length == packet.PacketBuffer.Length &&
                    PatternMatcher.Matches(rule, packet.PacketBuffer))
                .FirstOrDefault(rule => (rule.Fields ?? new List<DynamicField>())
                    .Any(field => field != null && field.Offset == offset && field.Length == length));
        }

        private static bool HasExtractionSource(Guid variableId, Guid exceptRuleId)
        {
            return DynamicVariableRuntime.Variables.GetRulesSnapshot()
                .Where(rule => rule != null && rule.RuleId != exceptRuleId)
                .SelectMany(rule => rule.Fields ?? new List<DynamicField>())
                .Any(field => field != null && field.VariableId == variableId);
        }

        private static bool ConfirmWeakPattern(IWin32Window owner, byte[] wildcardMask)
        {
            int fixedByteCount = (wildcardMask ?? new byte[0]).Count(value => value == 0);
            if (fixedByteCount <= 0)
            {
                ShowError(owner, "规则至少需要一个固定字节。");
                return false;
            }
            if (fixedByteCount < 2)
            {
                return MessageBox.Show(
                    owner,
                    "当前规则只有一个固定字节，可能匹配过宽，是否继续保存？",
                    "动态变量",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes;
            }
            return true;
        }

        private static void ShowError(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message ?? "动态变量操作失败。", "动态变量", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
