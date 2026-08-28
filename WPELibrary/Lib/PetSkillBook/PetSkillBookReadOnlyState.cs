using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// Read-only classification of a raw skill or lock value.  The
    /// classification deliberately does not infer whether a slot is open or
    /// locked; it only describes the observed runtime representation.
    /// </summary>
    public enum PetSkillSlotValueClass
    {
        Nil = 0,
        Zero = 1,
        NegativeSentinel = 2,
        PositiveSkill = 3,
        Other = 4
    }

    /// <summary>
    /// One normalized, read-only slot.  Raw skill and lock values are kept so
    /// later evidence can refine their meanings without losing observations.
    /// </summary>
    public sealed class PetSkillReadOnlySlot
    {
        public int SlotIndex { get; internal set; }

        public string SkillKind { get; internal set; }

        public string SkillRaw { get; internal set; }

        public int? SkillInteger { get; internal set; }

        public int? SkillId { get; internal set; }

        /// <summary>
        /// Positive integer decoded from the runtime skill value.  This is a
        /// runtime candidate; catalog membership is exposed separately and
        /// must not be confused with a confirmed game-level mapping.
        /// </summary>
        public int? DecodedSkillId { get; internal set; }

        public bool SkillIdIsCatalogKnown
        {
            get
            {
                PetSkillDefinition definition;
                return this.DecodedSkillId.HasValue &&
                    PetSkillBookCatalog.TryGetSkill(this.DecodedSkillId.Value, out definition);
            }
        }

        public string CatalogSkillName
        {
            get
            {
                PetSkillDefinition definition;
                return this.DecodedSkillId.HasValue &&
                    PetSkillBookCatalog.TryGetSkill(this.DecodedSkillId.Value, out definition)
                    ? definition.Name
                    : string.Empty;
            }
        }

        public PetSkillSlotValueClass SkillClass { get; internal set; }

        public string LockKind { get; internal set; }

        public string LockRaw { get; internal set; }

        public int? LockInteger { get; internal set; }

        /// <summary>
        /// Raw lock-value class only.  Open/locked semantics remain
        /// unconfirmed until independently cross-checked with the game.
        /// </summary>
        public PetSkillSlotValueClass LockClass { get; internal set; }

        public bool HasPositiveSkill
        {
            get { return this.SkillId.HasValue && this.SkillId.Value > 0; }
        }
    }

    /// <summary>
    /// Normalized read-only state used by the dry-run/preflight path.
    /// StateFingerprint is a local change token, not a game-provided version.
    /// </summary>
    public sealed class PetSkillBookReadOnlyState
    {
        public AndroidPetSkillProcessIdentity Process { get; internal set; }

        public int PetId { get; internal set; }

        public int CurrentPetId { get; internal set; }

        public bool IsCurrentPetMatch
        {
            get { return this.PetId > 0 && this.PetId == this.CurrentPetId; }
        }

        public IList<PetSkillReadOnlySlot> Slots { get; internal set; }

        /// <summary>
        /// Optional inventory projection copied from the read-only snapshot.
        /// Null means that the current reader did not provide inventory data;
        /// an empty list means that an empty inventory was read.
        /// </summary>
        public IList<InventoryItemSnapshot> InventoryItems { get; internal set; }

        public bool HasInventorySnapshot
        {
            get { return this.InventoryItems != null; }
        }

        /// <summary>
        /// Skill-keyed inventory projection from the current Android reader.
        /// It is kept separate from generic item IDs because the live game
        /// stores skill-book objects by m_ItemTypeId.
        /// </summary>
        public IList<PetSkillBookSkillInventorySnapshot> SkillBookInventory { get; internal set; }

        public bool HasSkillBookInventorySnapshot
        {
            get { return this.SkillBookInventory != null; }
        }

        public bool TryGetInventoryItemCount(int itemId, out int count)
        {
            count = 0;
            if (!this.HasInventorySnapshot || itemId <= 0)
            {
                return false;
            }

            InventoryItemSnapshot item = this.InventoryItems.FirstOrDefault(
                candidate => candidate != null && candidate.ItemId == itemId);
            count = item == null ? 0 : Math.Max(0, item.Count);
            return true;
        }

        public bool TryGetSkillBookCount(int skillId, out int count)
        {
            count = 0;
            if (!this.HasSkillBookInventorySnapshot || skillId <= 0)
            {
                return false;
            }

            PetSkillBookSkillInventorySnapshot item = this.SkillBookInventory.FirstOrDefault(
                candidate => candidate != null && candidate.SkillId == skillId);
            count = item == null ? 0 : Math.Max(0, item.Count);
            return true;
        }

        public int PopulatedSkillCount
        {
            get
            {
                return this.Slots == null
                    ? 0
                    : this.Slots.Count(slot => slot != null && slot.HasPositiveSkill);
            }
        }

        public int SlotCount
        {
            get { return this.Slots == null ? 0 : this.Slots.Count; }
        }

        public int CatalogConfirmedSkillCount
        {
            get
            {
                return this.Slots == null
                    ? 0
                    : this.Slots.Count(slot => slot != null && slot.SkillIdIsCatalogKnown);
            }
        }

        public int NilSkillCount
        {
            get { return CountClass(this.Slots, slot => slot.SkillClass, PetSkillSlotValueClass.Nil); }
        }

        public int ZeroSkillCount
        {
            get { return CountClass(this.Slots, slot => slot.SkillClass, PetSkillSlotValueClass.Zero); }
        }

        public int NegativeSkillCount
        {
            get { return CountClass(this.Slots, slot => slot.SkillClass, PetSkillSlotValueClass.NegativeSentinel); }
        }

        public int OtherSkillCount
        {
            get { return CountClass(this.Slots, slot => slot.SkillClass, PetSkillSlotValueClass.Other); }
        }

        public int NilLockCount
        {
            get { return CountClass(this.Slots, slot => slot.LockClass, PetSkillSlotValueClass.Nil); }
        }

        public int ZeroLockCount
        {
            get { return CountClass(this.Slots, slot => slot.LockClass, PetSkillSlotValueClass.Zero); }
        }

        public int NegativeLockCount
        {
            get { return CountClass(this.Slots, slot => slot.LockClass, PetSkillSlotValueClass.NegativeSentinel); }
        }

        public int PositiveLockCount
        {
            get { return CountClass(this.Slots, slot => slot.LockClass, PetSkillSlotValueClass.PositiveSkill); }
        }

        public int OtherLockCount
        {
            get { return CountClass(this.Slots, slot => slot.LockClass, PetSkillSlotValueClass.Other); }
        }

        private static int CountClass(
            IList<PetSkillReadOnlySlot> slots,
            Func<PetSkillReadOnlySlot, PetSkillSlotValueClass> selector,
            PetSkillSlotValueClass expected)
        {
            return slots == null
                ? 0
                : slots.Count(slot => slot != null && selector(slot) == expected);
        }

        public string StateFingerprint { get; internal set; }

        public DateTime ReadAt { get; internal set; }
    }

    /// <summary>
    /// Converts the validated Android raw snapshot into a safe read-only
    /// planning state.  It never supplies memory addresses or operation calls.
    /// </summary>
    public static class PetSkillBookReadOnlyStateAdapter
    {
        private const string NilRawValue = "0xFFFFFFFFFFFFFFFF";

        public static bool TryCreate(
            PetSkillBookReadOnlySnapshot snapshot,
            out PetSkillBookReadOnlyState state,
            out string error)
        {
            state = null;
            error = string.Empty;

            if (snapshot == null)
            {
                error = "只读宠物技能快照为空。";
                return false;
            }

            if (!snapshot.ReadOnly || snapshot.ActionAuthorized)
            {
                error = "技能状态快照不是只读快照。";
                return false;
            }

            if (!snapshot.IsCurrentPetMatch)
            {
                error = "技能状态快照中的当前宠物 ID 不一致。";
                return false;
            }

            if (snapshot.Slots == null || snapshot.Slots.Count == 0)
            {
                error = "技能状态快照没有槽位。";
                return false;
            }

            List<PetSkillReadOnlySlot> slots = new List<PetSkillReadOnlySlot>();
            foreach (PetSkillRawSlotSnapshot rawSlot in snapshot.Slots)
            {
                if (rawSlot == null || rawSlot.Skill == null || rawSlot.Lock == null)
                {
                    error = "技能状态快照包含不完整槽位。";
                    return false;
                }

                if (rawSlot.SlotIndex <= 0)
                {
                    error = "技能状态快照包含无效槽位序号。";
                    return false;
                }

                slots.Add(new PetSkillReadOnlySlot
                {
                    SlotIndex = rawSlot.SlotIndex,
                    SkillKind = rawSlot.Skill.Kind ?? string.Empty,
                    SkillRaw = rawSlot.Skill.Raw ?? string.Empty,
                    SkillInteger = rawSlot.Skill.Integer,
                    SkillId = rawSlot.Skill.Integer.HasValue && rawSlot.Skill.Integer.Value > 0
                        ? rawSlot.Skill.Integer
                        : (int?)null,
                    DecodedSkillId = rawSlot.Skill.Integer.HasValue && rawSlot.Skill.Integer.Value > 0
                        ? rawSlot.Skill.Integer
                        : (int?)null,
                    SkillClass = ClassifySkillValue(rawSlot.Skill),
                    LockKind = rawSlot.Lock.Kind ?? string.Empty,
                    LockRaw = rawSlot.Lock.Raw ?? string.Empty,
                    LockInteger = rawSlot.Lock.Integer,
                    LockClass = ClassifySkillValue(rawSlot.Lock)
                });
            }

            if (slots.Select(slot => slot.SlotIndex).Distinct().Count() != slots.Count)
            {
                error = "技能状态快照包含重复槽位序号。";
                return false;
            }

            IList<InventoryItemSnapshot> inventoryItems = null;
            if (snapshot.InventoryItems != null)
            {
                HashSet<int> inventoryIds = new HashSet<int>();
                foreach (PetSkillInventoryItemSnapshot item in snapshot.InventoryItems)
                {
                    if (item == null || item.ItemId <= 0 || item.Count < 0 || !inventoryIds.Add(item.ItemId))
                    {
                        error = "技能状态快照包含无效或重复的背包物品。";
                        return false;
                    }
                }

                inventoryItems = snapshot.InventoryItems
                    .Select(item => item == null
                        ? null
                        : new InventoryItemSnapshot
                        {
                            ItemId = item.ItemId,
                            Count = item.Count
                        })
                    .ToList();
            }

            IList<PetSkillBookSkillInventorySnapshot> skillBookInventory = null;
            if (snapshot.SkillBookInventory != null)
            {
                HashSet<int> skillIds = new HashSet<int>();
                foreach (PetSkillBookSkillInventorySnapshot item in snapshot.SkillBookInventory)
                {
                    if (item == null || item.SkillId <= 0 || item.Count < 0 || !skillIds.Add(item.SkillId))
                    {
                        error = "技能状态快照包含无效或重复的技能书库存。";
                        return false;
                    }
                }

                skillBookInventory = snapshot.SkillBookInventory
                    .Select(item => item == null
                        ? null
                        : new PetSkillBookSkillInventorySnapshot
                        {
                            SkillId = item.SkillId,
                            Count = item.Count
                        })
                    .ToList();
            }

            state = new PetSkillBookReadOnlyState
            {
                Process = snapshot.Process,
                PetId = snapshot.PetId,
                CurrentPetId = snapshot.CurrentPetId,
                Slots = slots,
                InventoryItems = inventoryItems,
                SkillBookInventory = skillBookInventory,
                StateFingerprint = ComputeFingerprint(snapshot),
                ReadAt = snapshot.ReadAt
            };
            return true;
        }

        public static string Describe(PetSkillBookReadOnlyState state)
        {
            if (state == null)
            {
                return "宠物技能只读状态为空。";
            }

            List<string> details = new List<string>();
            foreach (PetSkillReadOnlySlot slot in state.Slots ?? new List<PetSkillReadOnlySlot>())
            {
                if (slot == null) continue;

                string skill = slot.HasPositiveSkill
                    ? slot.DecodedSkillId.Value.ToString() +
                        (slot.SkillIdIsCatalogKnown
                            ? "(" + slot.CatalogSkillName + ")"
                            : "(目录未收录)")
                    : ValueDescription(slot);
                string lockValue = slot.LockInteger.HasValue
                    ? slot.LockInteger.Value.ToString()
                    : (string.IsNullOrWhiteSpace(slot.LockRaw) ? "<nil>" : slot.LockRaw);
                details.Add(string.Format(
                    "槽{0}={1}(lock={2})",
                    slot.SlotIndex,
                    skill,
                    lockValue));
            }

            return string.Format(
                "只读规划快照：petId={0}，当前宠物匹配={1}，技能槽={2}，已填充技能={3}，指纹={4}。{5} {6}",
                state.PetId,
                state.IsCurrentPetMatch,
                state.Slots == null ? 0 : state.Slots.Count,
                state.PopulatedSkillCount,
                state.StateFingerprint,
                details.Count == 0 ? string.Empty : " " + string.Join("、", details),
                DescribeSlotEvidence(state));
        }

        public static string DescribeSlotEvidence(PetSkillBookReadOnlyState state)
        {
            if (state == null)
            {
                return "槽位证据：只读状态为空。";
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "槽位证据：总数={0}，技能正值候选={1}，目录已确认={2}，技能nil={3}，技能0={4}，技能负哨兵={5}，技能其他={6}；锁值nil={7}，锁值0={8}，锁值负哨兵={9}，锁值正值={10}，锁值其他={11}；开槽/锁槽业务语义=未确认。",
                state.SlotCount,
                state.PopulatedSkillCount,
                state.CatalogConfirmedSkillCount,
                state.NilSkillCount,
                state.ZeroSkillCount,
                state.NegativeSkillCount,
                state.OtherSkillCount,
                state.NilLockCount,
                state.ZeroLockCount,
                state.NegativeLockCount,
                state.PositiveLockCount,
                state.OtherLockCount);
        }

        public static string DescribePresetTargets(
            SummonedPetSkillBookPreset preset,
            PetSkillBookReadOnlyState state)
        {
            if (preset == null)
            {
                return "只读预演：未加载召唤兽技能书配置，未评估目标技能。";
            }

            if (preset.Books == null || preset.Books.Count == 0)
            {
                return "只读预演：预设尚未配置目标技能书，未执行任何游戏操作。";
            }

            List<string> results = new List<string>();
            for (int index = 0; index < preset.Books.Count; index++)
            {
                SkillBookEntry book = preset.Books[index];
                if (book == null)
                {
                    results.Add(string.Format("第{0}本=配置为空", index + 1));
                    continue;
                }

                PetSkillReadOnlySlot existing = state == null || state.Slots == null
                    ? null
                    : state.Slots.FirstOrDefault(slot => slot != null && slot.SkillId == book.SkillId);
                string catalogDescription = PetSkillBookCatalog.DescribeEntry(
                    book.SkillId,
                    book.ItemId);
                if (existing != null)
                {
                    results.Add(string.Format(
                        "第{0}本 {1}=已存在于槽{2}，只读预演跳过",
                        index + 1,
                        catalogDescription,
                        existing.SlotIndex));
                }
                else
                {
                    int inventoryCount = 0;
                    bool inventoryKnown = false;
                    if (state != null)
                    {
                        int itemCount = 0;
                        bool itemKnown = state.TryGetInventoryItemCount(
                            book.ItemId,
                            out itemCount);
                        int skillCount = 0;
                        bool skillKnown = state.TryGetSkillBookCount(
                            book.SkillId,
                            out skillCount);
                        if (itemKnown && itemCount > 0)
                        {
                            inventoryCount = itemCount;
                            inventoryKnown = true;
                        }
                        else if (skillKnown)
                        {
                            inventoryCount = skillCount;
                            inventoryKnown = true;
                        }
                        else if (itemKnown)
                        {
                            inventoryCount = itemCount;
                            inventoryKnown = true;
                        }
                    }
                    if (inventoryKnown)
                    {
                        results.Add(string.Format(
                            "第{0}本 {1}=当前快照未发现，背包数量={2}{3}",
                            index + 1,
                            catalogDescription,
                            inventoryCount,
                            inventoryCount > 0 ? "，可进入后续预演" : "，缺少该技能书"));
                    }
                    else
                    {
                        results.Add(string.Format(
                            "第{0}本 {1}=当前快照未发现，背包数量尚未读取",
                            index + 1,
                            catalogDescription));
                    }
                }
            }

            return "只读预演：" + string.Join("；", results) + "。未执行任何游戏操作。";
        }

        private static PetSkillSlotValueClass ClassifySkillValue(PetSkillRuntimeValue value)
        {
            if (value == null)
            {
                return PetSkillSlotValueClass.Other;
            }

            if (string.Equals(value.Kind, "other", StringComparison.Ordinal) &&
                string.Equals(value.Raw, NilRawValue, StringComparison.OrdinalIgnoreCase))
            {
                return PetSkillSlotValueClass.Nil;
            }

            if (!value.Integer.HasValue)
            {
                return PetSkillSlotValueClass.Other;
            }

            if (value.Integer.Value > 0)
            {
                return PetSkillSlotValueClass.PositiveSkill;
            }

            if (value.Integer.Value == 0)
            {
                return PetSkillSlotValueClass.Zero;
            }

            if (value.Integer.Value == -1)
            {
                return PetSkillSlotValueClass.NegativeSentinel;
            }

            return PetSkillSlotValueClass.Other;
        }

        private static string ValueDescription(PetSkillReadOnlySlot slot)
        {
            if (slot == null)
            {
                return "<null>";
            }

            switch (slot.SkillClass)
            {
                case PetSkillSlotValueClass.Nil:
                    return "nil";
                case PetSkillSlotValueClass.Zero:
                    return "0";
                case PetSkillSlotValueClass.NegativeSentinel:
                    return slot.SkillInteger.HasValue
                        ? slot.SkillInteger.Value.ToString()
                        : "negative";
                default:
                    return string.IsNullOrWhiteSpace(slot.SkillRaw)
                        ? "<unknown>"
                        : slot.SkillRaw;
            }
        }

        private static string ComputeFingerprint(PetSkillBookReadOnlySnapshot snapshot)
        {
            StringBuilder canonical = new StringBuilder();
            canonical.Append(snapshot.PetId).Append('|');
            canonical.Append(snapshot.CurrentPetId).Append('|');
            if (snapshot.Process != null)
            {
                canonical.Append(snapshot.Process.Pid).Append('|');
                canonical.Append(snapshot.Process.StartTicks).Append('|');
                canonical.Append(snapshot.Process.Executable ?? string.Empty).Append('|');
            }

            foreach (PetSkillRawSlotSnapshot slot in snapshot.Slots ?? new List<PetSkillRawSlotSnapshot>())
            {
                canonical.Append(slot == null ? 0 : slot.SlotIndex).Append('|');
                AppendValue(canonical, slot == null ? null : slot.Skill);
                canonical.Append('|');
                AppendValue(canonical, slot == null ? null : slot.Lock);
                canonical.Append(';');
            }

            canonical.Append("|inventory:");
            if (snapshot.InventoryItems == null)
            {
                canonical.Append("<unavailable>");
            }
            else
            {
                foreach (PetSkillInventoryItemSnapshot item in snapshot.InventoryItems
                    .Where(candidate => candidate != null)
                    .OrderBy(candidate => candidate.ItemId))
                {
                    canonical.Append(item.ItemId).Append(':').Append(item.Count).Append(';');
                }
            }

            canonical.Append("|skillBookInventory:");
            if (snapshot.SkillBookInventory == null)
            {
                canonical.Append("<unavailable>");
            }
            else
            {
                foreach (PetSkillBookSkillInventorySnapshot item in snapshot.SkillBookInventory
                    .Where(candidate => candidate != null)
                    .OrderBy(candidate => candidate.SkillId))
                {
                    canonical.Append(item.SkillId).Append(':').Append(item.Count).Append(';');
                }
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void AppendValue(StringBuilder builder, PetSkillRuntimeValue value)
        {
            if (value == null)
            {
                builder.Append("<null>");
                return;
            }

            builder.Append(value.Kind ?? string.Empty).Append(':');
            builder.Append(value.Raw ?? string.Empty).Append(':');
            builder.Append(value.Integer.HasValue ? value.Integer.Value.ToString() : string.Empty).Append(':');
            builder.Append(value.Number.HasValue ? value.Number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty).Append(':');
            builder.Append(value.Text ?? string.Empty);
        }
    }
}
