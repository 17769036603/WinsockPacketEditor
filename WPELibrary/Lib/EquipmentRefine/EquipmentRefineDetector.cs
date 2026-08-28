using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 读取 Android C6 resident state 的只读装备探测器。
    /// 任何 envelope、绑定或候选字段校验失败都会返回 null。
    /// </summary>
    public class EquipmentRefineDetector
    {
        public const int SupportedSchemaVersion = 3;

        public class EquipmentTargetSelector
        {
            public string MemberIdentity { get; set; } = string.Empty;
            public string Slot { get; set; } = string.Empty;
            public string EquipmentId { get; set; } = string.Empty;
            public string EquipmentName { get; set; } = string.Empty;
            public int SlotIndex { get; set; } = -1;

            public bool HasSelector
            {
                get
                {
                    return !string.IsNullOrWhiteSpace(this.MemberIdentity) ||
                        !string.IsNullOrWhiteSpace(this.Slot) ||
                        !string.IsNullOrWhiteSpace(this.EquipmentId) ||
                        !string.IsNullOrWhiteSpace(this.EquipmentName) ||
                        this.SlotIndex >= 0;
                }
            }
        }

        /// <summary>
        /// 背包目标选择器。只要求同一快照内的 slot + memberIdentity；
        /// 其余字段是 reader 已读到时用于确认的可选证据，不会被猜测补全。
        /// </summary>
        public sealed class BagTargetSelector
        {
            public string Slot { get; set; } = string.Empty;
            public string MemberIdentity { get; set; } = string.Empty;
            public string ItemId { get; set; } = string.Empty;
            public string ItemTypeId { get; set; } = string.Empty;
            public int? XianqiTier { get; set; }
            public string XianqiTierLabel { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string RawFieldsSummary { get; set; } = string.Empty;

            /// <summary>
            /// Optional numeric bag position used only when an explicitly
            /// verified packet template contains a SlotIndex field. The
            /// resident reader's stable slot may be a non-numeric item key,
            /// so this value must come from independent packet evidence
            /// instead of being inferred from list order.
            /// </summary>
            public int RequestSlotIndex { get; set; } = -1;

            public bool IsValid
            {
                get
                {
                    return !string.IsNullOrWhiteSpace(this.Slot) &&
                        !string.IsNullOrWhiteSpace(this.MemberIdentity);
                }
            }
        }

        public class EquipmentSlot
        {
            public string MemberIdentity { get; set; } = string.Empty;
            public string Slot { get; set; } = string.Empty;
            public string EquipmentId { get; set; } = string.Empty;
            public string EquipmentName { get; set; } = string.Empty;
            public string EquipmentNameStatus { get; set; } = string.Empty;
            public string EquipmentNameSource { get; set; } = string.Empty;
            public string EquipmentNamePath { get; set; } = string.Empty;
            public int? XianqiTier { get; set; }
            public string XianqiTierLabel { get; set; } = string.Empty;
            public string XianqiTierStatus { get; set; } = string.Empty;
            public string XianqiTierSource { get; set; } = string.Empty;
            public string XianqiTierPath { get; set; } = string.Empty;
            public string ItemId { get; set; } = string.Empty;
            public string ItemTypeId { get; set; } = string.Empty;
            public bool CandidateOnly { get; set; }
            public bool IsWorn { get; set; }
            public List<RawField> RawFields { get; set; } = new List<RawField>();
            public string BaseAttributeHash { get; set; } = string.Empty;
            public DateTime LastReadTime { get; set; } = DateTime.MinValue;

            public string IdentityKey
            {
                get
                {
                    return string.Join(
                        "|",
                        new[]
                        {
                            this.MemberIdentity ?? string.Empty,
                            this.Slot ?? string.Empty,
                            this.EquipmentId ?? string.Empty
                        });
                }
            }
        }

        public class RawField
        {
            public string Key { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        public class EquipmentInventory
        {
            public int SchemaVersion { get; set; }
            public string SnapshotId { get; set; } = string.Empty;
            public string StreamSessionId { get; set; } = string.Empty;
            public long Sequence { get; set; }
            public bool Available { get; set; }
            public bool ReadOnly { get; set; }
            public bool ActionAuthorized { get; set; }
            public string ScanMode { get; set; } = "full";
            public string DiagnosticCode { get; set; } = string.Empty;
            public int ItemCount { get; set; }
            public long UpdatedAtUnixMs { get; set; }
            public DateTime UpdatedAtUtc
            {
                get
                {
                    try
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(this.UpdatedAtUnixMs).UtcDateTime;
                    }
                    catch
                    {
                        return DateTime.MinValue;
                    }
                }
            }

            public ProcessIdentity Binding { get; set; }
            public ProcessIdentity ProcessIdentity { get; set; }
            public string ContainerIdentity { get; set; } = string.Empty;
            public List<EquipmentSlot> Items { get; set; } = new List<EquipmentSlot>();
            public bool WornOnly { get; set; }
            public bool BagMode { get; set; }

            public bool IsUsable
            {
                get
                {
                    return this.Available &&
                        this.ReadOnly &&
                        !this.ActionAuthorized &&
                        this.SchemaVersion == SupportedSchemaVersion &&
                        this.Binding != null &&
                        this.ProcessIdentity != null &&
                        !string.IsNullOrWhiteSpace(this.ContainerIdentity) &&
                        !string.IsNullOrWhiteSpace(this.StreamSessionId) &&
                        !string.IsNullOrWhiteSpace(this.SnapshotId) &&
                        this.WornOnly &&
                        this.Sequence > 0;
                }
            }

            public bool IsUsableFor(EquipmentTargetMode mode)
            {
                if (!this.Available || !this.ReadOnly || this.ActionAuthorized ||
                    this.SchemaVersion != SupportedSchemaVersion ||
                    this.Binding == null || !this.Binding.IsValid ||
                    this.ProcessIdentity == null || !this.ProcessIdentity.IsValid ||
                    string.IsNullOrWhiteSpace(this.ContainerIdentity) ||
                    string.IsNullOrWhiteSpace(this.StreamSessionId) ||
                    string.IsNullOrWhiteSpace(this.SnapshotId) || this.Sequence <= 0)
                {
                    return false;
                }

                return mode == EquipmentTargetMode.Bag ? this.BagMode : this.WornOnly;
            }
        }

        public class ProcessIdentity
        {
            public int pid { get; set; }
            public long startTicks { get; set; }
            public string exe { get; set; } = string.Empty;

            public bool IsValid
            {
                get { return this.pid > 0 && this.startTicks > 0; }
            }
        }

        public static Task<EquipmentInventory> ReadInventoryAsync(
            string stateFilePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(stateFilePath) || !File.Exists(stateFilePath))
            {
                return Task.FromResult<EquipmentInventory>(null);
            }

            try
            {
                string json = File.ReadAllText(stateFilePath, Encoding.UTF8);
                cancellationToken.ThrowIfCancellationRequested();
                EquipmentInventory inventory = ParseInventory(json);
                return Task.FromResult(inventory);
            }
            catch
            {
                return Task.FromResult<EquipmentInventory>(null);
            }
        }

        public static Task<EquipmentInventory> ReadBagInventoryAsync(
            string stateFilePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(stateFilePath) || !File.Exists(stateFilePath))
            {
                return Task.FromResult<EquipmentInventory>(null);
            }

            try
            {
                string json = File.ReadAllText(stateFilePath, Encoding.UTF8);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(ParseInventory(json, EquipmentTargetMode.Bag));
            }
            catch
            {
                return Task.FromResult<EquipmentInventory>(null);
            }
        }

        public static EquipmentInventory ParseInventory(string json)
        {
            return ParseInventory(json, EquipmentTargetMode.Worn);
        }

        public static EquipmentInventory ParseBagInventory(string json)
        {
            return ParseInventory(json, EquipmentTargetMode.Bag);
        }

        private static EquipmentInventory ParseInventory(string json, EquipmentTargetMode mode)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch
            {
                return null;
            }

            if (!string.Equals(
                root.Value<string>("event"),
                "equipment_inventory_resident_state",
                StringComparison.Ordinal))
            {
                return null;
            }

            int schemaVersion = root.Value<int?>("schemaVersion") ?? 0;
            if (schemaVersion != SupportedSchemaVersion)
            {
                return null;
            }

            bool available = root.Value<bool?>("available") ?? false;
            bool readOnly = root.Value<bool?>("readOnly") ?? false;
            bool actionAuthorized = root.Value<bool?>("actionAuthorized") ?? true;
            if (!available || !readOnly || actionAuthorized)
            {
                return null;
            }

            string scanMode = root.Value<string>("scanMode") ?? "full";
            if (!string.Equals(scanMode, "full", StringComparison.Ordinal) &&
                !string.Equals(scanMode, "equipment", StringComparison.Ordinal))
            {
                return null;
            }

            string snapshotId = root.Value<string>("snapshotId") ?? string.Empty;
            string streamSessionId = root.Value<string>("streamSessionId") ?? string.Empty;
            string containerIdentity = root.Value<string>("containerIdentity") ?? string.Empty;
            long sequence = root.Value<long?>("sequence") ?? 0L;
            long updatedAtUnixMs = root.Value<long?>("updatedAtUnixMs") ?? 0L;
            if (string.IsNullOrWhiteSpace(snapshotId) ||
                string.IsNullOrWhiteSpace(streamSessionId) ||
                string.IsNullOrWhiteSpace(containerIdentity) ||
                sequence <= 0 ||
                updatedAtUnixMs <= 0)
            {
                return null;
            }

            ProcessIdentity binding = ParseProcessIdentity(root["binding"]);
            ProcessIdentity processIdentity = ParseProcessIdentity(root["processIdentity"]);
            if (binding == null || !binding.IsValid || processIdentity == null || !processIdentity.IsValid)
            {
                return null;
            }

            JArray itemsArray = root["items"] as JArray;
            if (itemsArray == null)
            {
                return null;
            }

            int declaredCount = root.Value<int?>("itemCount") ?? -1;
            if (declaredCount < 0 || declaredCount != itemsArray.Count)
            {
                return null;
            }

            EquipmentInventory inventory = new EquipmentInventory
            {
                SchemaVersion = schemaVersion,
                SnapshotId = snapshotId,
                StreamSessionId = streamSessionId,
                Sequence = sequence,
                Available = available,
                ReadOnly = readOnly,
                ActionAuthorized = actionAuthorized,
                ScanMode = scanMode,
                DiagnosticCode = root.Value<string>("diagnosticCode") ?? string.Empty,
                ItemCount = declaredCount,
                UpdatedAtUnixMs = updatedAtUnixMs,
                Binding = binding,
                ProcessIdentity = processIdentity,
                ContainerIdentity = containerIdentity
            };
            inventory.WornOnly = root.Value<bool?>("wornOnly") ?? false;
            inventory.BagMode = mode == EquipmentTargetMode.Bag && !inventory.WornOnly;
            if (mode == EquipmentTargetMode.Worn && !inventory.WornOnly) return null;
            if (mode == EquipmentTargetMode.Bag && !inventory.BagMode) return null;

            foreach (JToken itemToken in itemsArray)
            {
                JObject item = itemToken as JObject;
                EquipmentSlot slot = ParseSlot(item, mode);
                if (slot == null)
                {
                    return null;
                }

                inventory.Items.Add(slot);
            }

            if (inventory.Items
                .GroupBy(item => item.MemberIdentity, StringComparer.Ordinal)
                .Any(group => group.Count() != 1) ||
                inventory.Items
                .GroupBy(item => item.Slot, StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
            {
                return null;
            }

            return inventory.IsUsableFor(mode) ? inventory : null;
        }

        private static EquipmentSlot ParseSlot(JObject item, EquipmentTargetMode mode)
        {
            if (item == null) return null;

            bool candidateOnly = item.Value<bool?>("candidateOnly") ?? false;
            bool isWorn = item.Value<bool?>("isWorn") ?? false;
            string memberIdentity = item.Value<string>("memberIdentity") ?? string.Empty;
            string slotName = item.Value<string>("slot") ?? string.Empty;
            if (!candidateOnly || string.IsNullOrWhiteSpace(memberIdentity) || string.IsNullOrWhiteSpace(slotName) ||
                (mode == EquipmentTargetMode.Worn && !isWorn))
            {
                return null;
            }

            JArray fieldsArray = item["rawFields"] as JArray;
            if (fieldsArray == null || fieldsArray.Count == 0)
            {
                return null;
            }

            EquipmentSlot slot = new EquipmentSlot
            {
                MemberIdentity = memberIdentity,
                Slot = slotName,
                EquipmentId = item.Value<string>("equipmentId") ?? string.Empty,
                EquipmentName = item.Value<string>("equipmentName") ?? string.Empty,
                EquipmentNameStatus = item.Value<string>("displayNameStatus") ?? string.Empty,
                EquipmentNameSource = item.Value<string>("displayNameSource") ?? string.Empty,
                EquipmentNamePath = item.Value<string>("displayNamePath") ?? string.Empty,
                XianqiTier = item.Value<int?>("xianqiTier"),
                XianqiTierLabel = item.Value<string>("xianqiTierLabel") ?? string.Empty,
                XianqiTierStatus = item.Value<string>("xianqiTierStatus") ?? string.Empty,
                XianqiTierSource = item.Value<string>("xianqiTierSource") ?? string.Empty,
                XianqiTierPath = item.Value<string>("xianqiTierPath") ?? string.Empty,
                CandidateOnly = candidateOnly,
                IsWorn = isWorn,
                LastReadTime = DateTime.UtcNow,
                RawFields = new List<RawField>()
            };
            if (mode == EquipmentTargetMode.Worn && string.IsNullOrWhiteSpace(slot.EquipmentId))
            {
                return null;
            }

            string decodedName = item.Value<string>("displayName") ?? string.Empty;
            if (string.Equals(slot.EquipmentNameStatus, "decoded", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(decodedName))
            {
                slot.EquipmentName = decodedName;
            }

            foreach (JToken fieldToken in fieldsArray)
            {
                JObject field = fieldToken as JObject;
                if (field == null) return null;

                string key = field.Value<string>("key") ?? string.Empty;
                string kind = field.Value<string>("kind") ?? string.Empty;
                string value = field.Value<string>("value");
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(kind) || value == null)
                {
                    return null;
                }

                if (slot.RawFields.Any(existing => string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                slot.RawFields.Add(new RawField { Key = key, Kind = kind, Value = value });
            }

            slot.ItemId = FirstRawValue(slot.RawFields, "m_ItemId", "m_Id");
            slot.ItemTypeId = FirstRawValue(slot.RawFields, "m_ItemTypeId", "m_LtypeId");
            if (string.IsNullOrWhiteSpace(slot.EquipmentName))
            {
                string rawName = FirstRawValue(slot.RawFields, "name", "itemName");
                if (!string.IsNullOrWhiteSpace(rawName) && !string.Equals(rawName, "<table>", StringComparison.Ordinal))
                {
                    slot.EquipmentName = rawName;
                }
            }

            string suppliedBaseHash = item.Value<string>("baseAttributeHash") ?? string.Empty;
            slot.BaseAttributeHash = string.IsNullOrWhiteSpace(suppliedBaseHash)
                ? CalculateIdentityHash(slot.MemberIdentity, slot.Slot, slot.EquipmentId)
                : suppliedBaseHash;
            return string.IsNullOrWhiteSpace(slot.BaseAttributeHash) ? null : slot;
        }

        private static string FirstRawValue(IEnumerable<RawField> fields, params string[] keys)
        {
            if (fields == null || keys == null) return string.Empty;
            foreach (string key in keys)
            {
                RawField field = fields.FirstOrDefault(item =>
                    string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
                if (field != null && !string.IsNullOrWhiteSpace(field.Value) && field.Value != "<table>")
                {
                    return field.Value;
                }
            }
            return string.Empty;
        }

        private static ProcessIdentity ParseProcessIdentity(JToken token)
        {
            JObject value = token as JObject;
            if (value == null) return null;

            return new ProcessIdentity
            {
                pid = value.Value<int?>("pid") ?? 0,
                startTicks = value.Value<long?>("startTicks") ?? 0L,
                exe = value.Value<string>("exe") ?? string.Empty
            };
        }

        public static string CalculateIdentityHash(string memberIdentity, string slot, string equipmentId)
        {
            if (string.IsNullOrWhiteSpace(memberIdentity) || string.IsNullOrWhiteSpace(slot))
            {
                return string.Empty;
            }

            string source = string.Join(
                "|",
                new[] { memberIdentity.Trim(), slot.Trim(), equipmentId ?? string.Empty });
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
                return BitConverter.ToString(digest).Replace("-", string.Empty).Substring(0, 16);
            }
        }

        /// <summary>
        /// 保留给旧调用方的 rawFields 哈希工具；不作为装备身份哈希。
        /// </summary>
        public static string CalculateBaseAttributeHash(List<RawField> rawFields)
        {
            if (rawFields == null || rawFields.Count == 0) return string.Empty;

            string source = string.Join(
                "|",
                rawFields
                    .OrderBy(field => field.Key, StringComparer.Ordinal)
                    .Select(field => (field.Key ?? string.Empty) + "|" + (field.Value ?? string.Empty)));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
                return BitConverter.ToString(digest).Replace("-", string.Empty).Substring(0, 16);
            }
        }

        public static EquipmentSlot FindBySlotIndex(EquipmentInventory inventory, int slotIndex)
        {
            if (inventory == null || inventory.Items == null) return null;
            return inventory.Items.FirstOrDefault(item =>
            {
                int parsed;
                return TryParseSlotIndex(item.Slot, out parsed) && parsed == slotIndex;
            });
        }

        public static EquipmentSlot FindBySlot(EquipmentInventory inventory, string slot)
        {
            if (inventory == null || inventory.Items == null || string.IsNullOrWhiteSpace(slot)) return null;
            return inventory.Items.FirstOrDefault(item =>
                string.Equals(item.Slot, slot, StringComparison.Ordinal));
        }

        public static EquipmentSlot FindByMemberIdentity(EquipmentInventory inventory, string memberIdentity)
        {
            if (inventory == null || inventory.Items == null || string.IsNullOrWhiteSpace(memberIdentity)) return null;
            return inventory.Items.FirstOrDefault(item =>
                string.Equals(item.MemberIdentity, memberIdentity, StringComparison.Ordinal));
        }

        public static EquipmentSlot FindByEquipmentId(EquipmentInventory inventory, string equipmentId)
        {
            if (inventory == null || inventory.Items == null || string.IsNullOrWhiteSpace(equipmentId)) return null;
            return inventory.Items.FirstOrDefault(item =>
                string.Equals(item.EquipmentId, equipmentId, StringComparison.Ordinal));
        }

        public static EquipmentSlot FindByTarget(
            EquipmentInventory inventory,
            EquipmentTargetSelector selector)
        {
            if (inventory == null || inventory.Items == null || selector == null ||
                string.IsNullOrWhiteSpace(selector.Slot))
            {
                return null;
            }
            if (!inventory.IsUsable) return null;
            List<EquipmentSlot> candidates = inventory.Items.Where(item =>
                (string.IsNullOrWhiteSpace(selector.MemberIdentity) ||
                    string.Equals(item.MemberIdentity, selector.MemberIdentity, StringComparison.Ordinal)) &&
                (string.IsNullOrWhiteSpace(selector.Slot) ||
                    string.Equals(item.Slot, selector.Slot, StringComparison.Ordinal)) &&
                (string.IsNullOrWhiteSpace(selector.EquipmentId) ||
                    string.Equals(item.EquipmentId, selector.EquipmentId, StringComparison.Ordinal)) &&
                (string.IsNullOrWhiteSpace(selector.EquipmentName) ||
                    string.Equals(item.EquipmentName, selector.EquipmentName, StringComparison.Ordinal)) &&
                (selector.SlotIndex < 0 ||
                    (TryParseSlotIndex(item.Slot, out int parsed) && parsed == selector.SlotIndex)) &&
                item.IsWorn)
                .ToList();

            return candidates.Count == 1 ? candidates[0] : null;
        }

        public static EquipmentSlot FindBagTarget(
            EquipmentInventory inventory,
            BagTargetSelector selector)
        {
            if (inventory == null || selector == null || !selector.IsValid ||
                !inventory.IsUsableFor(EquipmentTargetMode.Bag))
            {
                return null;
            }

            List<EquipmentSlot> candidates = inventory.Items.Where(item =>
                item != null &&
                string.Equals(item.Slot, selector.Slot, StringComparison.Ordinal) &&
                string.Equals(item.MemberIdentity, selector.MemberIdentity, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(selector.ItemId) || string.Equals(item.ItemId, selector.ItemId, StringComparison.Ordinal)) &&
                (string.IsNullOrWhiteSpace(selector.ItemTypeId) || string.Equals(item.ItemTypeId, selector.ItemTypeId, StringComparison.Ordinal)) &&
                (!selector.XianqiTier.HasValue || item.XianqiTier == selector.XianqiTier) &&
                (string.IsNullOrWhiteSpace(selector.Name) || string.Equals(item.EquipmentName, selector.Name, StringComparison.Ordinal)))
                .ToList();

            return candidates.Count == 1 ? candidates[0] : null;
        }

        public static bool TryParseSlotIndex(string slot, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(slot)) return false;

            string value = slot.Trim();
            if (value.StartsWith("slot_", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring("slot_".Length);
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && index >= 0;
        }

        public static bool EquipmentIdentityChanged(EquipmentSlot current, EquipmentSlot previous)
        {
            if (current == null || previous == null) return true;
            if (!string.IsNullOrWhiteSpace(current.ItemId) || !string.IsNullOrWhiteSpace(previous.ItemId))
            {
                if (!string.Equals(current.ItemId, previous.ItemId, StringComparison.Ordinal)) return true;
            }
            if (!string.IsNullOrWhiteSpace(current.ItemTypeId) || !string.IsNullOrWhiteSpace(previous.ItemTypeId))
            {
                if (!string.Equals(current.ItemTypeId, previous.ItemTypeId, StringComparison.Ordinal)) return true;
            }
            return !string.Equals(current.IdentityKey, previous.IdentityKey, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(current.BaseAttributeHash) &&
                 !string.IsNullOrWhiteSpace(previous.BaseAttributeHash) &&
                 !string.Equals(current.BaseAttributeHash, previous.BaseAttributeHash, StringComparison.Ordinal));
        }

        public static bool TryGetRawFieldValue(EquipmentSlot slot, string key, out int value)
        {
            value = 0;
            if (slot == null || slot.RawFields == null || string.IsNullOrWhiteSpace(key)) return false;

            RawField field = slot.RawFields.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.Ordinal));
            return field != null && int.TryParse(
                field.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        public static int GetRawFieldValue(EquipmentSlot slot, string key)
        {
            int value;
            return TryGetRawFieldValue(slot, key, out value) ? value : 0;
        }
    }
}
