using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// Android game-process identity carried by the read-only pet snapshot.
    /// This is deliberately separate from the Windows process identity type.
    /// </summary>
    public sealed class AndroidPetSkillProcessIdentity
    {
        public int Pid { get; internal set; }

        public long StartTicks { get; internal set; }

        public string Executable { get; internal set; }
    }

    /// <summary>
    /// One raw runtime value from a pet skill or lock array. The parser keeps
    /// the runtime kind and integer value; it does not guess whether a lock
    /// integer means open, closed, or protected.
    /// </summary>
    public sealed class PetSkillRuntimeValue
    {
        public string Kind { get; internal set; }

        public string Raw { get; internal set; }

        public int? Integer { get; internal set; }

        public double? Number { get; internal set; }

        public string Text { get; internal set; }
    }

    /// <summary>
    /// Read-only raw slot pair. IsOpen/IsLocked are intentionally absent until
    /// the game's lock-value semantics are independently verified.
    /// </summary>
    public sealed class PetSkillRawSlotSnapshot
    {
        public int SlotIndex { get; internal set; }

        public PetSkillRuntimeValue Skill { get; internal set; }

        public PetSkillRuntimeValue Lock { get; internal set; }
    }

    /// <summary>
    /// Optional read-only inventory item carried by the Android snapshot.
    /// A missing inventoryItems property means that inventory was not read;
    /// an empty array means that the reader did read an empty inventory.
    /// </summary>
    public sealed class PetSkillInventoryItemSnapshot
    {
        public int ItemId { get; internal set; }

        public int Count { get; internal set; }
    }

    /// <summary>
    /// Read-only skill-book inventory entry.  The current Android runtime
    /// stores a learned skill-book object by its skill ID in m_ItemTypeId;
    /// PropertyValueDict.num is the verified stack count.  This is separate
    /// from the generic item-ID inventory projection for older readers.
    /// </summary>
    public sealed class PetSkillBookSkillInventorySnapshot
    {
        public int SkillId { get; internal set; }

        public int Count { get; internal set; }
    }

    /// <summary>
    /// Validated schema-1 snapshot produced by the Android read-only reader.
    /// No operation, socket, or memory-write capability is represented here.
    /// </summary>
    public sealed class PetSkillBookReadOnlySnapshot
    {
        public const string SchemaName = "pet_skill_snapshot.v1";

        public const int SupportedSchemaVersion = 1;

        public bool ReadOnly { get; internal set; }

        public bool ActionAuthorized { get; internal set; }

        public AndroidPetSkillProcessIdentity Process { get; internal set; }

        public int PetId { get; internal set; }

        public int CurrentPetId { get; internal set; }

        public IList<PetSkillRawSlotSnapshot> Slots { get; internal set; }

        /// <summary>
        /// Optional inventory data. This remains null for older probes that
        /// only know how to read pet skills and lock values.
        /// </summary>
        public IList<PetSkillInventoryItemSnapshot> InventoryItems { get; internal set; }

        public bool HasInventorySnapshot
        {
            get { return this.InventoryItems != null; }
        }

        /// <summary>
        /// Optional skill-keyed inventory data emitted by the current
        /// read-only Android probe.  Missing means unavailable; an empty
        /// array means the probe read no skill-book objects.
        /// </summary>
        public IList<PetSkillBookSkillInventorySnapshot> SkillBookInventory { get; internal set; }

        public bool HasSkillBookInventorySnapshot
        {
            get { return this.SkillBookInventory != null; }
        }

        public DateTime ReadAt { get; internal set; }

        public bool IsCurrentPetMatch
        {
            get { return this.PetId > 0 && this.PetId == this.CurrentPetId; }
        }
    }

    /// <summary>
    /// Strict parser for the one-line Android pet snapshot protocol.
    /// </summary>
    public static class PetSkillBookReadOnlySnapshotProtocol
    {
        public static bool TryParse(
            string json,
            out PetSkillBookReadOnlySnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "快照内容为空。";
                return false;
            }

            try
            {
                JObject root = JObject.Parse(
                    json,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });

                if (!string.Equals(
                    root.Value<string>("schema"),
                    PetSkillBookReadOnlySnapshot.SchemaName,
                    StringComparison.Ordinal))
                {
                    error = "宠物技能快照 schema 不支持。";
                    return false;
                }

                if ((root.Value<int?>("schemaVersion") ?? 0) !=
                    PetSkillBookReadOnlySnapshot.SupportedSchemaVersion)
                {
                    error = "宠物技能快照版本不支持。";
                    return false;
                }

                if (root.Value<bool?>("readOnly") != true ||
                    root.Value<bool?>("actionAuthorized") != false)
                {
                    error = "快照不是只读或包含操作授权。";
                    return false;
                }

                AndroidPetSkillProcessIdentity process;
                if (!TryReadProcess(root["process"] as JObject, out process, out error))
                {
                    return false;
                }

                int petId;
                int currentPetId;
                if (!TryReadPositiveInt(root, "petId", out petId, out error) ||
                    !TryReadPositiveInt(root, "currentPetId", out currentPetId, out error))
                {
                    return false;
                }

                if (petId != currentPetId)
                {
                    error = "快照中的当前宠物 ID 不一致。";
                    return false;
                }

                IList<PetSkillRawSlotSnapshot> slots;
                if (!TryReadSlots(root["skillSlots"] as JArray, root["lockSlots"] as JArray, out slots, out error))
                {
                    return false;
                }

                IList<PetSkillInventoryItemSnapshot> inventoryItems;
                if (!TryReadInventory(root["inventoryItems"], out inventoryItems, out error))
                {
                    return false;
                }

                IList<PetSkillBookSkillInventorySnapshot> skillBookInventory;
                if (!TryReadSkillBookInventory(
                    root["skillBookInventory"],
                    out skillBookInventory,
                    out error))
                {
                    return false;
                }

                snapshot = new PetSkillBookReadOnlySnapshot
                {
                    ReadOnly = true,
                    ActionAuthorized = false,
                    Process = process,
                    PetId = petId,
                    CurrentPetId = currentPetId,
                    Slots = slots,
                    InventoryItems = inventoryItems,
                    SkillBookInventory = skillBookInventory,
                    ReadAt = DateTime.UtcNow
                };
                return true;
            }
            catch (JsonException ex)
            {
                error = "快照 JSON 无效：" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = "快照解析失败：" + ex.Message;
                return false;
            }
        }

        private static bool TryReadProcess(
            JObject value,
            out AndroidPetSkillProcessIdentity process,
            out string error)
        {
            process = null;
            error = string.Empty;
            if (value == null)
            {
                error = "快照缺少 Android 进程身份。";
                return false;
            }

            int pid;
            long startTicks;
            if (!TryReadPositiveInt(value, "pid", out pid, out error) ||
                !TryReadPositiveLong(value, "startTicks", out startTicks, out error))
            {
                return false;
            }

            string executable = value.Value<string>("exe");
            if (string.IsNullOrWhiteSpace(executable) || executable.Length > 1024)
            {
                error = "Android 进程可执行文件无效。";
                return false;
            }

            process = new AndroidPetSkillProcessIdentity
            {
                Pid = pid,
                StartTicks = startTicks,
                Executable = executable
            };
            return true;
        }

        private static bool TryReadSlots(
            JArray skillArray,
            JArray lockArray,
            out IList<PetSkillRawSlotSnapshot> slots,
            out string error)
        {
            slots = null;
            error = string.Empty;
            if (skillArray == null || lockArray == null ||
                skillArray.Count == 0 || skillArray.Count != lockArray.Count ||
                skillArray.Count > 64)
            {
                error = "技能槽快照数量无效。";
                return false;
            }

            List<PetSkillRawSlotSnapshot> result = new List<PetSkillRawSlotSnapshot>();
            for (int index = 0; index < skillArray.Count; index++)
            {
                JObject skillObject = skillArray[index] as JObject;
                JObject lockObject = lockArray[index] as JObject;
                int skillIndex;
                int lockIndex;
                if (!TryReadSlotValue(skillObject, out skillIndex, out PetSkillRuntimeValue skill, out error) ||
                    !TryReadSlotValue(lockObject, out lockIndex, out PetSkillRuntimeValue lockValue, out error))
                {
                    return false;
                }

                if (skillIndex != lockIndex || skillIndex != index + 1)
                {
                    error = "技能槽序号不连续或两组槽位不匹配。";
                    return false;
                }

                result.Add(new PetSkillRawSlotSnapshot
                {
                    SlotIndex = skillIndex,
                    Skill = skill,
                    Lock = lockValue
                });
            }

            slots = result;
            return true;
        }

        private static bool TryReadInventory(
            JToken value,
            out IList<PetSkillInventoryItemSnapshot> items,
            out string error)
        {
            items = null;
            error = string.Empty;

            // The field is optional for backward compatibility with the
            // deployed pet-skill probe. Absence is different from an empty
            // array: the former means unavailable, the latter means empty.
            if (value == null)
            {
                return true;
            }

            JArray array = value as JArray;
            if (array == null || array.Count > 4096)
            {
                error = "背包技能书快照数量无效。";
                return false;
            }

            List<PetSkillInventoryItemSnapshot> result =
                new List<PetSkillInventoryItemSnapshot>();
            HashSet<int> itemIds = new HashSet<int>();
            for (int index = 0; index < array.Count; index++)
            {
                JObject item = array[index] as JObject;
                int itemId;
                int count;
                if (item == null ||
                    !TryReadPositiveInt(item, "itemId", out itemId, out error) ||
                    !TryReadNonNegativeInt(item, "count", out count, out error))
                {
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        error = "背包物品对象无效。";
                    }
                    return false;
                }

                if (!itemIds.Add(itemId))
                {
                    error = "背包技能书快照包含重复物品 ID。";
                    return false;
                }

                result.Add(new PetSkillInventoryItemSnapshot
                {
                    ItemId = itemId,
                    Count = count
                });
            }

            items = result;
            return true;
        }

        private static bool TryReadSkillBookInventory(
            JToken value,
            out IList<PetSkillBookSkillInventorySnapshot> items,
            out string error)
        {
            items = null;
            error = string.Empty;
            if (value == null)
            {
                return true;
            }

            JArray array = value as JArray;
            if (array == null || array.Count > 4096)
            {
                error = "技能书库存快照数量无效。";
                return false;
            }

            List<PetSkillBookSkillInventorySnapshot> result =
                new List<PetSkillBookSkillInventorySnapshot>();
            HashSet<int> skillIds = new HashSet<int>();
            for (int index = 0; index < array.Count; index++)
            {
                JObject item = array[index] as JObject;
                int skillId;
                int count;
                if (item == null ||
                    !TryReadPositiveInt(item, "skillId", out skillId, out error) ||
                    !TryReadNonNegativeInt(item, "count", out count, out error))
                {
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        error = "技能书库存对象无效。";
                    }
                    return false;
                }

                if (!skillIds.Add(skillId))
                {
                    error = "技能书库存快照包含重复技能 ID。";
                    return false;
                }

                result.Add(new PetSkillBookSkillInventorySnapshot
                {
                    SkillId = skillId,
                    Count = count
                });
            }

            items = result;
            return true;
        }

        private static bool TryReadSlotValue(
            JObject value,
            out int slotIndex,
            out PetSkillRuntimeValue runtimeValue,
            out string error)
        {
            slotIndex = 0;
            runtimeValue = null;
            error = string.Empty;
            if (value == null || !TryReadPositiveInt(value, "slotIndex", out slotIndex, out error))
            {
                if (string.IsNullOrWhiteSpace(error)) error = "技能槽对象无效。";
                return false;
            }

            JObject rawValue = value["value"] as JObject;
            if (rawValue == null)
            {
                error = "技能槽缺少原始值。";
                return false;
            }

            string kind = rawValue.Value<string>("kind");
            string raw = rawValue.Value<string>("raw");
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(raw) ||
                !new[] { "other", "string", "table", "i32", "f64" }.Contains(kind, StringComparer.Ordinal))
            {
                error = "技能槽原始值类型无效。";
                return false;
            }

            int? integer = null;
            double? number = null;
            string text = null;
            if (kind == "i32")
            {
                if (rawValue["integer"] == null || !TryReadInt32(rawValue["integer"], out int parsedInteger))
                {
                    error = "i32 技能槽缺少整数值。";
                    return false;
                }
                integer = parsedInteger;
            }
            else if (kind == "f64")
            {
                if (rawValue["number"] == null || !double.TryParse(
                    rawValue["number"].ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsedNumber))
                {
                    error = "f64 技能槽缺少数值。";
                    return false;
                }
                number = parsedNumber;
            }
            else if (kind == "string")
            {
                text = rawValue.Value<string>("text");
                if (text == null)
                {
                    error = "string 技能槽缺少文本值。";
                    return false;
                }
            }

            runtimeValue = new PetSkillRuntimeValue
            {
                Kind = kind,
                Raw = raw,
                Integer = integer,
                Number = number,
                Text = text
            };
            return true;
        }

        private static bool TryReadPositiveInt(
            JObject value,
            string propertyName,
            out int result,
            out string error)
        {
            result = 0;
            error = string.Empty;
            JToken token = value == null ? null : value[propertyName];
            if (!TryReadInt32(token, out result) || result <= 0)
            {
                error = "字段 " + propertyName + " 必须是正整数。";
                return false;
            }
            return true;
        }

        private static bool TryReadPositiveLong(
            JObject value,
            string propertyName,
            out long result,
            out string error)
        {
            result = 0L;
            error = string.Empty;
            JToken token = value == null ? null : value[propertyName];
            if (token == null || !long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) || result <= 0L)
            {
                error = "字段 " + propertyName + " 必须是正整数。";
                return false;
            }
            return true;
        }

        private static bool TryReadNonNegativeInt(
            JObject value,
            string propertyName,
            out int result,
            out string error)
        {
            result = 0;
            error = string.Empty;
            JToken token = value == null ? null : value[propertyName];
            if (!TryReadInt32(token, out result) || result < 0)
            {
                error = "字段 " + propertyName + " 必须是非负整数。";
                return false;
            }
            return true;
        }

        private static bool TryReadInt32(JToken token, out int result)
        {
            result = 0;
            return token != null && int.TryParse(
                token.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result);
        }
    }
}
