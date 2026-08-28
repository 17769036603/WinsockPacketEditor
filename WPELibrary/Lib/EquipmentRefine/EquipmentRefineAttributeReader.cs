using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 从 reader JSON 的 rawFields 读取炼化属性。
    /// 默认没有字段映射；只有传入已验收映射时才会返回有效快照。
    /// </summary>
    public class EquipmentRefineAttributeReader
    {
        private readonly Dictionary<string, TargetAttribute> _verifiedFieldMap;

        public EquipmentRefineAttributeReader(IDictionary<string, TargetAttribute> verifiedFieldMap)
        {
            this._verifiedFieldMap = new Dictionary<string, TargetAttribute>(StringComparer.Ordinal);
            if (verifiedFieldMap == null) return;

            foreach (KeyValuePair<string, TargetAttribute> pair in verifiedFieldMap)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != TargetAttribute.Unknown)
                {
                    this._verifiedFieldMap[pair.Key] = pair.Value;
                }
            }
        }

        public bool HasVerifiedFieldMapping
        {
            get { return this._verifiedFieldMap.Count > 0; }
        }

        public Task<EquipmentAttributesSnapshot> ReadAttributesAsync(
            EquipmentRefineDetector.EquipmentSlot slot,
            string expectedBaseAttributeHash,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            EquipmentAttributesSnapshot snapshot = new EquipmentAttributesSnapshot
            {
                MemberIdentity = slot == null ? string.Empty : slot.MemberIdentity,
                Slot = slot == null ? string.Empty : slot.Slot,
                EquipmentId = slot == null ? string.Empty : slot.EquipmentId,
                ItemId = slot == null ? string.Empty : slot.ItemId,
                ItemTypeId = slot == null ? string.Empty : slot.ItemTypeId,
                EquipmentName = slot == null ? string.Empty : slot.EquipmentName,
                BaseAttributeHash = slot == null ? string.Empty : slot.BaseAttributeHash,
                CandidateOnly = slot != null && slot.CandidateOnly,
                ReadTime = DateTime.UtcNow,
                IsValid = false
            };

            if (slot == null)
            {
                snapshot.ReadError = "equipment_missing";
                return Task.FromResult(snapshot);
            }

            if (!slot.CandidateOnly)
            {
                snapshot.ReadError = "candidate_only_required";
                return Task.FromResult(snapshot);
            }

            if (slot.RawFields == null || slot.RawFields.Count == 0)
            {
                snapshot.ReadError = "raw_fields_missing";
                return Task.FromResult(snapshot);
            }

            if (!string.IsNullOrWhiteSpace(expectedBaseAttributeHash) &&
                !string.Equals(slot.BaseAttributeHash, expectedBaseAttributeHash, StringComparison.Ordinal))
            {
                snapshot.ReadError = "equipment_identity_changed";
                return Task.FromResult(snapshot);
            }

            if (!this.HasVerifiedFieldMapping)
            {
                snapshot.ReadError = "verified_attribute_mapping_missing";
                return Task.FromResult(snapshot);
            }

            Dictionary<string, int> rawFieldMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (EquipmentRefineDetector.RawField rawField in slot.RawFields)
            {
                int rawValue;
                if (int.TryParse(rawField.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rawValue))
                {
                    rawFieldMap[rawField.Key] = rawValue;
                }
            }
            snapshot.SetRawFieldMap(rawFieldMap);

            List<AttributeValue> attributes = new List<AttributeValue>();
            foreach (KeyValuePair<string, TargetAttribute> mapping in this._verifiedFieldMap)
            {
                EquipmentRefineDetector.RawField rawField = slot.RawFields.FirstOrDefault(field =>
                    string.Equals(field.Key, mapping.Key, StringComparison.Ordinal));
                if (rawField == null) continue;

                if (!IsIntegerKind(rawField.Kind))
                {
                    snapshot.ReadError = "attribute_kind_unverified:" + mapping.Key;
                    return Task.FromResult(snapshot);
                }

                int value;
                if (!int.TryParse(rawField.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    snapshot.ReadError = "attribute_value_invalid:" + mapping.Key;
                    return Task.FromResult(snapshot);
                }

                if (attributes.Any(item => item.Type == mapping.Value))
                {
                    snapshot.ReadError = "attribute_mapping_ambiguous:" + mapping.Value;
                    return Task.FromResult(snapshot);
                }

                attributes.Add(new AttributeValue
                {
                    Type = mapping.Value,
                    Name = GetAttributeDisplayName(mapping.Value),
                    CurrentValue = value,
                    RawValue = value,
                    LastReadTime = snapshot.ReadTime
                });
            }

            if (attributes.Count == 0)
            {
                snapshot.ReadError = "verified_attributes_missing";
                return Task.FromResult(snapshot);
            }

            attributes = attributes.OrderBy(item => item.Type).ToList();
            snapshot.SetAttributes(attributes);
            snapshot.RefineAttributeHash = CalculateRefineAttributeHash(
                this._verifiedFieldMap,
                slot.RawFields);
            snapshot.AttributeHash = snapshot.RefineAttributeHash;
            ApplyLegacyScalarValues(snapshot);
            snapshot.IsValid = !string.IsNullOrWhiteSpace(snapshot.RefineAttributeHash);
            if (!snapshot.IsValid)
            {
                snapshot.ReadError = "attribute_hash_missing";
            }

            return Task.FromResult(snapshot);
        }

        public Task<EquipmentAttributesSnapshot> ReadAttributesFromFileAsync(
            string stateFilePath,
            EquipmentRefineDetector.EquipmentTargetSelector selector,
            string expectedBaseAttributeHash,
            IDictionary<string, TargetAttribute> verifiedFieldMap,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            EquipmentRefineAttributeReader reader = ReferenceEquals(verifiedFieldMap, null)
                ? this
                : new EquipmentRefineAttributeReader(verifiedFieldMap);
            return reader.ReadFromFileInternalAsync(
                stateFilePath,
                selector,
                expectedBaseAttributeHash,
                cancellationToken);
        }

        public Task<EquipmentAttributesSnapshot> ReadAttributesFromFileAsync(
            string stateFilePath,
            EquipmentRefineDetector.EquipmentTargetSelector selector,
            string expectedBaseAttributeHash,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.ReadFromFileInternalAsync(
                stateFilePath,
                selector,
                expectedBaseAttributeHash,
                cancellationToken);
        }

        /// <summary>
        /// 兼容旧调用方，但不再允许状态机用默认 slot=0 冒险选择装备。
        /// </summary>
        public Task<EquipmentAttributesSnapshot> ReadAttributesFromFileAsync(
            string stateFilePath,
            int slotIndex,
            string expectedBaseAttributeHash,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            EquipmentRefineDetector.EquipmentTargetSelector selector = new EquipmentRefineDetector.EquipmentTargetSelector
            {
                SlotIndex = slotIndex
            };
            return this.ReadFromFileInternalAsync(
                stateFilePath,
                selector,
                expectedBaseAttributeHash,
                cancellationToken);
        }

        private async Task<EquipmentAttributesSnapshot> ReadFromFileInternalAsync(
            string stateFilePath,
            EquipmentRefineDetector.EquipmentTargetSelector selector,
            string expectedBaseAttributeHash,
            CancellationToken cancellationToken)
        {
            EquipmentRefineDetector.EquipmentInventory inventory =
                await EquipmentRefineDetector.ReadInventoryAsync(stateFilePath, cancellationToken);
            if (inventory == null)
            {
                return InvalidSnapshot("inventory_unavailable");
            }

            EquipmentRefineDetector.EquipmentSlot slot =
                EquipmentRefineDetector.FindByTarget(inventory, selector);
            if (slot == null)
            {
                return InvalidSnapshot("target_equipment_not_found_or_ambiguous");
            }

            EquipmentAttributesSnapshot snapshot = await this.ReadAttributesAsync(
                slot,
                expectedBaseAttributeHash,
                cancellationToken);
            snapshot.SnapshotId = inventory.SnapshotId;
            snapshot.StreamSessionId = inventory.StreamSessionId;
            snapshot.Sequence = inventory.Sequence;
            return snapshot;
        }

        private static EquipmentAttributesSnapshot InvalidSnapshot(string error)
        {
            return new EquipmentAttributesSnapshot
            {
                IsValid = false,
                ReadError = error,
                ReadTime = DateTime.UtcNow
            };
        }

        private static bool IsIntegerKind(string kind)
        {
            string normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "int" ||
                normalized == "integer" ||
                normalized == "i32" ||
                normalized == "i64";
        }

        public static string CalculateRefineAttributeHash(
            IDictionary<string, TargetAttribute> verifiedFieldMap,
            IEnumerable<EquipmentRefineDetector.RawField> rawFields)
        {
            if (verifiedFieldMap == null || rawFields == null) return string.Empty;

            List<string> values = new List<string>();
            foreach (KeyValuePair<string, TargetAttribute> mapping in verifiedFieldMap.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                EquipmentRefineDetector.RawField field = rawFields.FirstOrDefault(item =>
                    string.Equals(item.Key, mapping.Key, StringComparison.Ordinal));
                if (field == null) continue;
                values.Add(mapping.Key + "|" + ((int)mapping.Value).ToString(CultureInfo.InvariantCulture) + "|" + field.Value);
            }

            if (values.Count == 0) return string.Empty;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", values)));
                return BitConverter.ToString(digest).Replace("-", string.Empty).Substring(0, 16);
            }
        }

        public static bool SnapshotsEqual(EquipmentAttributesSnapshot first, EquipmentAttributesSnapshot second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null) return false;

            return string.Equals(first.IdentityKey, second.IdentityKey, StringComparison.Ordinal) &&
                string.Equals(first.BaseAttributeHash, second.BaseAttributeHash, StringComparison.Ordinal) &&
                string.Equals(first.RefineAttributeHash, second.RefineAttributeHash, StringComparison.Ordinal) &&
                first.Attributes.SequenceEqual(second.Attributes);
        }

        public static string GetAttributeDisplayName(TargetAttribute type)
        {
            return EquipmentRefineAttributeCatalog.GetDisplayName(type);
        }

        private static void ApplyLegacyScalarValues(EquipmentAttributesSnapshot snapshot)
        {
            foreach (AttributeValue attribute in snapshot.Attributes)
            {
                switch (attribute.Type)
                {
                    case TargetAttribute.AttackPower: snapshot.AttackPower = attribute.CurrentValue; break;
                    case TargetAttribute.DefensePower: snapshot.DefensePower = attribute.CurrentValue; break;
                    case TargetAttribute.CritRate: snapshot.CritRate = attribute.CurrentValue; break;
                    case TargetAttribute.CritDamage: snapshot.CritDamage = attribute.CurrentValue; break;
                    case TargetAttribute.HitRate: snapshot.HitRate = attribute.CurrentValue; break;
                    case TargetAttribute.DodgeRate: snapshot.DodgeRate = attribute.CurrentValue; break;
                    case TargetAttribute.HpRecovery: snapshot.HpRecovery = attribute.CurrentValue; break;
                    case TargetAttribute.MpRecovery: snapshot.MpRecovery = attribute.CurrentValue; break;
                    case TargetAttribute.RareDegree: snapshot.RareDegree = attribute.CurrentValue; break;
                }
            }
        }
    }
}
