using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>宿主可绑定的炼化预设编辑模型。保存前始终执行 EquipmentRefinePreset.IsValid。</summary>
    public sealed class EquipmentRefinePresetEditorModel
    {
        public EquipmentRefinePreset Preset { get; private set; }
        public string ValidationError { get; private set; } = string.Empty;

        public EquipmentRefinePresetEditorModel(EquipmentRefinePreset preset = null)
        {
            this.Preset = preset ?? EquipmentRefinePreset.CreateDefault();
        }

        public IList<RefineRule> Rules { get { return this.Preset.Rules; } }

        public EquipmentRefinePreset CurrentPreset { get { return this.Preset; } }

        public bool SetTargetSlot(string slot)
        {
            EnsureTarget();
            this.Preset.Target.Slot = slot ?? string.Empty;
            return true;
        }

        public bool SetTargetMemberIdentity(string memberIdentity)
        {
            EnsureTarget();
            this.Preset.Target.MemberIdentity = memberIdentity ?? string.Empty;
            return true;
        }

        public bool SetTargetEquipmentId(string equipmentId)
        {
            EnsureTarget();
            this.Preset.Target.EquipmentId = equipmentId ?? string.Empty;
            return true;
        }

        public void SetMemoryResultMode(bool enabled)
        {
            this.Preset.MemoryResultMode = enabled;
        }

        public void SetBagTargetMode(bool enabled)
        {
            this.Preset.BagTargetMode = enabled;
        }

        public void SetBagRequestSlotIndex(int value)
        {
            if (this.Preset.BagTarget == null)
            {
                this.Preset.BagTarget = new EquipmentRefineDetector.BagTargetSelector();
            }
            this.Preset.BagTarget.RequestSlotIndex = value;
        }

        public void SetBagTarget(
            string slot,
            string memberIdentity,
            string itemId,
            string itemTypeId,
            string name,
            string rawFieldsSummary,
            int? xianqiTier,
            string xianqiTierLabel)
        {
            if (this.Preset.BagTarget == null) this.Preset.BagTarget = new EquipmentRefineDetector.BagTargetSelector();
            this.Preset.BagTarget.Slot = slot ?? string.Empty;
            this.Preset.BagTarget.MemberIdentity = memberIdentity ?? string.Empty;
            this.Preset.BagTarget.ItemId = itemId ?? string.Empty;
            this.Preset.BagTarget.ItemTypeId = itemTypeId ?? string.Empty;
            this.Preset.BagTarget.XianqiTier = xianqiTier;
            this.Preset.BagTarget.XianqiTierLabel = xianqiTierLabel ?? string.Empty;
            this.Preset.BagTarget.Name = name ?? string.Empty;
            this.Preset.BagTarget.RawFieldsSummary = rawFieldsSummary ?? string.Empty;
        }

        /// <summary>
        /// 从同一只读背包快照绑定目标，并复制 reader 已解码的名称作为预设显示确认。
        /// 唯一身份仍严格使用 slot + memberIdentity；名称为空时仍允许绑定目标。
        /// </summary>
        public bool SetBagTargetFromInventory(
            EquipmentRefineDetector.EquipmentInventory inventory,
            string slot,
            string memberIdentity,
            out string error)
        {
            error = string.Empty;
            if (inventory == null || !inventory.IsUsableFor(EquipmentTargetMode.Bag))
            {
                error = "背包读取快照不可用。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(slot) || string.IsNullOrWhiteSpace(memberIdentity))
            {
                error = "背包目标必须指定 slot 和 memberIdentity。";
                return false;
            }

            if (inventory.Items == null)
            {
                error = "背包读取快照没有条目。";
                return false;
            }
            List<EquipmentRefineDetector.EquipmentSlot> matches = inventory.Items
                .Where(item => item != null &&
                    string.Equals(item.Slot, slot, StringComparison.Ordinal) &&
                    string.Equals(item.MemberIdentity, memberIdentity, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1)
            {
                error = "背包目标在当前快照中不唯一。";
                return false;
            }

            EquipmentRefineDetector.EquipmentSlot selected = matches[0];
            SetBagTarget(
                selected.Slot,
                selected.MemberIdentity,
                selected.ItemId,
                selected.ItemTypeId,
                selected.EquipmentName,
                BuildRawFieldsSummary(selected.RawFields),
                selected.XianqiTier,
                selected.XianqiTierLabel);
            this.Preset.BagTargetMode = true;
            return true;
        }

        private static string BuildRawFieldsSummary(IList<EquipmentRefineDetector.RawField> fields)
        {
            if (fields == null || fields.Count == 0) return string.Empty;
            return string.Join(
                ";",
                fields
                    .Where(field => field != null && !string.IsNullOrWhiteSpace(field.Key))
                    .Take(32)
                    .Select(field => field.Key + "=" + (field.Value ?? string.Empty)));
        }

        private void EnsureTarget()
        {
            if (this.Preset.Target == null) this.Preset.Target = new EquipmentRefineDetector.EquipmentTargetSelector();
        }

        public bool TryValidateAndSave(out EquipmentRefinePreset preset, out string error)
        {
            if (this.Preset.Rules == null)
            {
                this.Preset.Rules = new List<RefineRule>();
            }
            bool valid = this.Preset.IsValid(out error);
            this.ValidationError = error ?? string.Empty;
            preset = valid ? EquipmentRefinePresetSerializer.DeserializeClone(this.Preset) : null;
            return valid;
        }

        public void AddRule(TargetAttribute attribute, AttributeOperator op, int targetValue, bool enabled)
        {
            if (op != AttributeOperator.Equal && op != AttributeOperator.GreaterThanOrEqual)
            {
                throw new ArgumentException("炼化规则只支持 = 和 >=。", "op");
            }
            this.Preset.Rules.Add(new RefineRule
            {
                Attribute = attribute,
                Operator = op,
                TargetValue = targetValue,
                Enabled = enabled
            });
        }

        public bool RemoveRuleAt(int index)
        {
            if (index < 0 || index >= this.Preset.Rules.Count) return false;
            this.Preset.Rules.RemoveAt(index);
            return true;
        }

        public bool SetRequiredMatches(int value)
        {
            if (value < 1 || value > this.Preset.Rules.Count(rule => rule != null && rule.Enabled)) return false;
            this.Preset.RequiredMatches = value;
            return true;
        }
    }

    /// <summary>
    /// 一个已由只读背包快照确认的下拉选项。显示文本只用于 UI，目标身份仍保存
    /// slot + memberIdentity，并保留 reader 已读到的 item/type/name/tier 摘要。
    /// </summary>
    public sealed class EquipmentRefineBagOption
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Slot { get; set; } = string.Empty;
        public string MemberIdentity { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string ItemTypeId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RawFieldsSummary { get; set; } = string.Empty;
        public int? XianqiTier { get; set; }
        public string XianqiTierLabel { get; set; } = string.Empty;

        public EquipmentRefineDetector.BagTargetSelector ToSelector()
        {
            return new EquipmentRefineDetector.BagTargetSelector
            {
                Slot = this.Slot ?? string.Empty,
                MemberIdentity = this.MemberIdentity ?? string.Empty,
                ItemId = this.ItemId ?? string.Empty,
                ItemTypeId = this.ItemTypeId ?? string.Empty,
                Name = this.Name ?? string.Empty,
                RawFieldsSummary = this.RawFieldsSummary ?? string.Empty,
                XianqiTier = this.XianqiTier,
                XianqiTierLabel = this.XianqiTierLabel ?? string.Empty
            };
        }

        public override string ToString()
        {
            return this.DisplayName ?? string.Empty;
        }
    }

    /// <summary>
    /// 最小 WinForms 编辑入口。它只编辑配置并回调宿主，不导航游戏、不发送封包。
    /// 背包选项来自宿主注入的只读快照或只读 provider；未注入时保持空列表并安全提示。
    /// </summary>
    public sealed class EquipmentRefinePresetEditor : Form
    {
        private readonly EquipmentRefinePresetEditorModel _model;
        private readonly ComboBox _slotBox = new ComboBox();
        private readonly Button _readBag = new Button();
        private readonly DataGridView _rules = new DataGridView();
        private readonly Button _save = new Button();
        private readonly Button _confirm = new Button();
        private readonly Button _addRule = new Button();
        private readonly Button _removeRule = new Button();
        private readonly Func<CancellationToken, Task<EquipmentRefineDetector.EquipmentInventory>>
            _bagInventoryProvider;
        private CancellationTokenSource _bagInventoryCancellation;

        public event Action<EquipmentRefinePreset> PresetSaved;

        public EquipmentRefinePresetEditor(EquipmentRefinePreset preset = null)
            : this(preset, null, null)
        {
        }

        public EquipmentRefinePresetEditor(
            EquipmentRefinePreset preset,
            IEnumerable<EquipmentRefineBagOption> availableBagEquipment,
            Func<CancellationToken, Task<EquipmentRefineDetector.EquipmentInventory>>
                bagInventoryProvider = null)
        {
            this._model = new EquipmentRefinePresetEditorModel(preset);
            this._bagInventoryProvider = bagInventoryProvider;
            this.Text = "装备炼化预设";
            this.Width = 560;
            this.Height = 400;
            this.StartPosition = FormStartPosition.CenterParent;
            this.InitializeBagEquipmentOptions();
            this.ConfigureRuleGrid();
            this._rules.DataSource = this._model.Rules;
            if (availableBagEquipment != null)
            {
                this.SetAvailableBagEquipmentOptions(availableBagEquipment);
            }
            this._readBag.Text = "读取背包";
            this._readBag.AutoSize = true;
            this._readBag.Click += this.ReadBagClick;
            this._save.Text = "保存";
            this._save.Click += this.SaveClick;
            this._confirm.Text = "确定";
            this._confirm.Click += this.SaveClick;
            this._addRule.Text = "添加规则";
            this._addRule.Click += this.AddRuleClick;
            this._removeRule.Text = "删除规则";
            this._removeRule.Click += this.RemoveRuleClick;
            FlowLayoutPanel ruleActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };
            ruleActions.Controls.Add(this._addRule);
            ruleActions.Controls.Add(this._removeRule);
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(10),
                AutoSize = false
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = "炼化装备", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            TableLayoutPanel targetChooser = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            targetChooser.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            targetChooser.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this._slotBox.DropDownStyle = ComboBoxStyle.DropDownList;
            this._slotBox.Dock = DockStyle.Fill;
            targetChooser.Controls.Add(this._slotBox, 0, 0);
            targetChooser.Controls.Add(this._readBag, 1, 0);
            layout.Controls.Add(targetChooser, 1, 0);
            layout.Controls.Add(this._rules, 0, 1);
            layout.SetColumnSpan(this._rules, 2);
            layout.Controls.Add(new Label
            {
                Text = "默认保留 5 条规则（首条启用）；选择属性后填写目标数值，比较方式固定为 ≥。百分比只填写数字，例如 2 表示 2.0%。",
                AutoSize = true,
                Padding = new Padding(0, 6, 0, 6)
            }, 0, 2);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, 2), 2);
            layout.Controls.Add(ruleActions, 0, 3);
            layout.SetColumnSpan(ruleActions, 2);
            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0)
            };
            footer.Controls.Add(this._confirm);
            footer.Controls.Add(this._save);
            layout.Controls.Add(footer, 0, 4);
            layout.SetColumnSpan(footer, 2);
            this.Controls.Add(layout);
        }

        /// <summary>
        /// 从同一份只读背包快照生成装备选项。名称和阶数只使用 reader 已明确解码的字段；
        /// slot/memberIdentity 仍是唯一目标身份，不按数组顺序或名称猜测目标。
        /// </summary>
        public static IList<EquipmentRefineBagOption> BuildBagEquipmentOptions(
            EquipmentRefineDetector.EquipmentInventory inventory)
        {
            List<EquipmentRefineBagOption> result =
                new List<EquipmentRefineBagOption>();
            if (inventory == null ||
                !inventory.IsUsableFor(EquipmentTargetMode.Bag) ||
                inventory.Items == null)
            {
                return result;
            }

            foreach (IGrouping<string, EquipmentRefineDetector.EquipmentSlot> group in inventory.Items
                .Where(item => item != null && item.CandidateOnly && !item.IsWorn &&
                    !string.IsNullOrWhiteSpace(item.Slot) &&
                    !string.IsNullOrWhiteSpace(item.MemberIdentity))
                .GroupBy(item => (item.Slot ?? string.Empty) + "\u001f" +
                    (item.MemberIdentity ?? string.Empty), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                if (group.Count() != 1)
                {
                    continue;
                }
                EquipmentRefineDetector.EquipmentSlot item = group.First();
                bool nameDecoded = string.Equals(
                    item.EquipmentNameStatus,
                    "decoded",
                    StringComparison.Ordinal);
                bool tierDecoded = string.Equals(
                    item.XianqiTierStatus,
                    "decoded",
                    StringComparison.Ordinal);
                string name = nameDecoded && !string.IsNullOrWhiteSpace(item.EquipmentName)
                    ? item.EquipmentName
                    : "未解码装备";
                string tier = tierDecoded
                    ? (!string.IsNullOrWhiteSpace(item.XianqiTierLabel)
                        ? item.XianqiTierLabel
                        : item.XianqiTier.HasValue
                            ? item.XianqiTier.Value.ToString(CultureInfo.InvariantCulture) + "阶"
                            : string.Empty)
                    : string.Empty;
                string displayName = string.IsNullOrWhiteSpace(tier)
                    ? name
                    : name + " · " + tier;
                result.Add(new EquipmentRefineBagOption
                {
                    DisplayName = displayName,
                    Slot = item.Slot,
                    MemberIdentity = item.MemberIdentity,
                    ItemId = item.ItemId,
                    ItemTypeId = item.ItemTypeId,
                    Name = nameDecoded ? item.EquipmentName : string.Empty,
                    RawFieldsSummary = BuildRawFieldsSummary(item.RawFields),
                    XianqiTier = tierDecoded ? item.XianqiTier : null,
                    XianqiTierLabel = tierDecoded ? item.XianqiTierLabel : string.Empty
                });
            }
            return result;
        }

        private static string BuildRawFieldsSummary(
            IList<EquipmentRefineDetector.RawField> fields)
        {
            if (fields == null || fields.Count == 0) return string.Empty;
            return string.Join(
                ";",
                fields
                    .Where(field => field != null && !string.IsNullOrWhiteSpace(field.Key))
                    .Take(32)
                    .Select(field => field.Key + "=" + (field.Value ?? string.Empty)));
        }

        /// <summary>把已验证的只读背包快照注入装备下拉框。</summary>
        public void SetAvailableBagEquipmentOptionsFromInventory(
            EquipmentRefineDetector.EquipmentInventory inventory)
        {
            this.SetAvailableBagEquipmentOptions(BuildBagEquipmentOptions(inventory));
        }

        /// <summary>
        /// 由宿主注入当前只读快照中已验证的背包装备选项。
        /// </summary>
        public void SetAvailableBagEquipmentOptions(
            IEnumerable<EquipmentRefineBagOption> options)
        {
            EquipmentRefineDetector.BagTargetSelector selected = this.GetSelectedBagTarget();
            this._slotBox.Items.Clear();
            HashSet<string> added = new HashSet<string>(StringComparer.Ordinal);
            if (options != null)
            {
                foreach (EquipmentRefineBagOption option in options)
                {
                    if (option == null || string.IsNullOrWhiteSpace(option.Slot) ||
                        string.IsNullOrWhiteSpace(option.MemberIdentity)) continue;
                    string key = option.Slot + "\u001f" + option.MemberIdentity;
                    if (!added.Add(key)) continue;
                    this._slotBox.Items.Add(option);
                }
            }
            if (selected != null && selected.IsValid &&
                !added.Contains(selected.Slot + "\u001f" + selected.MemberIdentity))
            {
                this._slotBox.Items.Add(new EquipmentRefineBagOption
                {
                    DisplayName = BuildSavedBagDisplayName(selected),
                    Slot = selected.Slot,
                    MemberIdentity = selected.MemberIdentity,
                    ItemId = selected.ItemId,
                    ItemTypeId = selected.ItemTypeId,
                    Name = selected.Name,
                    RawFieldsSummary = selected.RawFieldsSummary,
                    XianqiTier = selected.XianqiTier,
                    XianqiTierLabel = selected.XianqiTierLabel
                });
            }
            this.SelectBagTarget(selected);
        }

        private void InitializeBagEquipmentOptions()
        {
            this._slotBox.Items.Clear();
            EquipmentRefineDetector.BagTargetSelector saved = this._model.Preset.BagTarget;
            if (saved != null && saved.IsValid)
            {
                this._slotBox.Items.Add(new EquipmentRefineBagOption
                {
                    DisplayName = BuildSavedBagDisplayName(saved),
                    Slot = saved.Slot,
                    MemberIdentity = saved.MemberIdentity,
                    ItemId = saved.ItemId,
                    ItemTypeId = saved.ItemTypeId,
                    Name = saved.Name,
                    RawFieldsSummary = saved.RawFieldsSummary,
                    XianqiTier = saved.XianqiTier,
                    XianqiTierLabel = saved.XianqiTierLabel
                });
            }
            this.SelectBagTarget(saved);
        }

        private static string BuildSavedBagDisplayName(
            EquipmentRefineDetector.BagTargetSelector target)
        {
            if (target == null) return string.Empty;
            string name = string.IsNullOrWhiteSpace(target.Name) ? "已保存装备" : target.Name;
            string tier = string.IsNullOrWhiteSpace(target.XianqiTierLabel)
                ? string.Empty
                : " · " + target.XianqiTierLabel;
            return name + tier;
        }

        private void SelectBagTarget(EquipmentRefineDetector.BagTargetSelector target)
        {
            for (int index = 0; index < this._slotBox.Items.Count; index++)
            {
                EquipmentRefineBagOption option = this._slotBox.Items[index] as EquipmentRefineBagOption;
                if (option != null && target != null &&
                    string.Equals(option.Slot, target.Slot, StringComparison.Ordinal) &&
                    string.Equals(option.MemberIdentity, target.MemberIdentity, StringComparison.Ordinal))
                {
                    this._slotBox.SelectedIndex = index;
                    return;
                }
            }
            this._slotBox.SelectedIndex = -1;
        }

        private EquipmentRefineDetector.BagTargetSelector GetSelectedBagTarget()
        {
            EquipmentRefineBagOption option = this._slotBox.SelectedItem as EquipmentRefineBagOption;
            return option == null ? null : option.ToSelector();
        }

        private async void ReadBagClick(object sender, EventArgs e)
        {
            if (this._bagInventoryProvider == null)
            {
                MessageBox.Show(
                    this,
                    "当前未接入只读背包来源。请先启动只读 reader 并由宿主注入快照，或配置 state 文件；未读取到实时快照不会展示装备。",
                    "背包读取不可用",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (this._bagInventoryCancellation != null)
            {
                this._bagInventoryCancellation.Cancel();
                this._bagInventoryCancellation.Dispose();
            }
            this._bagInventoryCancellation = new CancellationTokenSource();
            this._readBag.Enabled = false;
            try
            {
                EquipmentRefineDetector.EquipmentInventory inventory =
                    await this._bagInventoryProvider(this._bagInventoryCancellation.Token);
                if (inventory == null || !inventory.IsUsableFor(EquipmentTargetMode.Bag))
                {
                    MessageBox.Show(
                        this,
                        "当前背包只读快照不可用，未加载任何装备。",
                        "背包读取失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                IList<EquipmentRefineBagOption> options = BuildBagEquipmentOptions(inventory);
                this.SetAvailableBagEquipmentOptions(options);
                if (options.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "当前快照没有可确认的背包装备。",
                        "背包为空",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException)
            {
                // Closing or refreshing the editor is a normal cancellation path.
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "读取背包失败：" + ex.Message,
                    "背包读取失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                this._readBag.Enabled = true;
            }
        }

        private void ConfigureRuleGrid()
        {
            this._rules.Dock = DockStyle.Fill;
            this._rules.AutoGenerateColumns = false;
            this._rules.AllowUserToAddRows = false;
            this._rules.AllowUserToDeleteRows = false;
            this._rules.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            this._rules.Columns.Clear();

            this._rules.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "Enabled",
                HeaderText = "启用",
                DataPropertyName = "Enabled",
                FillWeight = 18F
            });

            IEnumerable<TargetAttribute> existingAttributes = this._model.Rules == null
                ? null
                : this._model.Rules
                    .Where(rule => rule != null)
                    .Select(rule => rule.Attribute);
            List<RuleAttributeOption> attributes = EquipmentRefineAttributeCatalog
                .GetPresetDefinitions(existingAttributes)
                .Select(definition => new RuleAttributeOption
                {
                    Value = definition.Attribute,
                    DisplayName = definition.DisplayName
                })
                .ToList();
            this._rules.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "Attribute",
                HeaderText = "属性（中文）",
                DataPropertyName = "Attribute",
                DataSource = attributes,
                DisplayMember = "DisplayName",
                ValueMember = "Value",
                FillWeight = 42F,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            });

            this._rules.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "TargetValue",
                HeaderText = "目标值（数字）",
                DataPropertyName = "TargetValue",
                ValueType = typeof(int),
                FillWeight = 40F
            });
            this._rules.CellValidating -= this.RuleCellValidating;
            this._rules.CellValidating += this.RuleCellValidating;
        }

        private void RuleCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                !string.Equals(
                    this._rules.Columns[e.ColumnIndex].DataPropertyName,
                    "TargetValue",
                    StringComparison.Ordinal))
            {
                return;
            }

            int value;
            if (!int.TryParse(
                Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value))
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "目标值必须填写整数；百分比只填写数字，例如 2 表示 2.0%。",
                    "属性目标值无效",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private sealed class RuleAttributeOption
        {
            public TargetAttribute Value { get; set; }
            public string DisplayName { get; set; }
        }

        private void SaveClick(object sender, EventArgs e)
        {
            this._rules.EndEdit();
            EquipmentRefineDetector.BagTargetSelector selected = this.GetSelectedBagTarget();
            if (selected != null && selected.IsValid)
            {
                this._model.SetBagTarget(
                    selected.Slot,
                    selected.MemberIdentity,
                    selected.ItemId,
                    selected.ItemTypeId,
                    selected.Name,
                    selected.RawFieldsSummary,
                    selected.XianqiTier,
                    selected.XianqiTierLabel);
            }
            this._model.SetBagTargetMode(true);
            this._model.SetTargetSlot(string.Empty);
            this._model.SetTargetMemberIdentity(string.Empty);
            this._model.SetTargetEquipmentId(string.Empty);
            this._model.Preset.RequiredMatches = 1;
            this._model.Preset.MaxAttempts = 20;
            this._model.Preset.IntervalMs = 1500;
            this._model.SetMemoryResultMode(false);
            foreach (RefineRule rule in this._model.Rules)
            {
                if (rule != null) rule.Operator = AttributeOperator.GreaterThanOrEqual;
            }
            EquipmentRefinePreset saved;
            string error;
            if (!this._model.TryValidateAndSave(out saved, out error))
            {
                MessageBox.Show(error, "预设无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Action<EquipmentRefinePreset> handler = this.PresetSaved;
            if (handler != null) handler(saved);
            this.DialogResult = DialogResult.OK;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && this._bagInventoryCancellation != null)
            {
                this._bagInventoryCancellation.Cancel();
                this._bagInventoryCancellation.Dispose();
                this._bagInventoryCancellation = null;
            }
            base.Dispose(disposing);
        }

        private void AddRuleClick(object sender, EventArgs e)
        {
            this._model.AddRule(TargetAttribute.RootBone, AttributeOperator.GreaterThanOrEqual, 0, true);
            this._rules.DataSource = null;
            this.ConfigureRuleGrid();
            this._rules.DataSource = this._model.Rules;
        }

        private void RemoveRuleClick(object sender, EventArgs e)
        {
            if (this._rules.CurrentRow == null) return;
            this._model.RemoveRuleAt(this._rules.CurrentRow.Index);
            this._rules.DataSource = null;
            this.ConfigureRuleGrid();
            this._rules.DataSource = this._model.Rules;
        }
    }
}
