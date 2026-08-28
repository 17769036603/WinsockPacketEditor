using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// Strict adapter for the versioned resident JSON/JSONL document boundary.
    /// It reads a supplied document only; it does not attach to a process or
    /// infer addresses, offsets, protocol codes, or max values.
    /// </summary>
    public sealed class ResidentJsonlRefineMemoryResultSource : IRefineMemoryResultSource
    {
        public const int SupportedSchemaVersion = 3;

        private readonly Func<CancellationToken, Task<string>> _documentProvider;
        private readonly IDictionary<string, TargetAttribute> _attributeMapping;

        public ResidentJsonlRefineMemoryResultSource(
            string jsonlPath,
            IDictionary<string, TargetAttribute> attributeMapping = null)
            : this(CreateFileProvider(jsonlPath), attributeMapping)
        {
        }

        public ResidentJsonlRefineMemoryResultSource(
            Func<CancellationToken, Task<string>> documentProvider,
            IDictionary<string, TargetAttribute> attributeMapping = null)
        {
            this._documentProvider = documentProvider;
            this._attributeMapping = new Dictionary<string, TargetAttribute>(
                attributeMapping ?? new Dictionary<string, TargetAttribute>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public async Task<RefineMemoryResultSnapshot> ReceiveAfterSendAsync(
            RefineMemoryBaseline baseline,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            if (baseline == null || !baseline.IsValid)
            {
                return Failure("resident_baseline_invalid");
            }
            if (this._documentProvider == null)
            {
                return Failure("resident_document_provider_unconfigured");
            }

            try
            {
                string document = await this._documentProvider(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return DecodeDocument(document, baseline);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return Failure("resident_json_parse_failed");
            }
            catch (IOException)
            {
                return Failure("resident_json_read_failed");
            }
            catch (Exception)
            {
                return Failure("resident_source_exception");
            }
        }

        private RefineMemoryResultSnapshot DecodeDocument(
            string document,
            RefineMemoryBaseline baseline)
        {
            string json = LastJsonObject(document);
            JObject root = JObject.Parse(json);

            if (root.Value<int?>("schemaVersion") != SupportedSchemaVersion)
            {
                return Failure("resident_schema_version_unsupported");
            }
            string eventName = root.Value<string>("event") ?? string.Empty;
            if (!string.Equals(eventName, "equipment_inventory_resident_state", StringComparison.Ordinal))
            {
                return Failure("resident_event_invalid");
            }

            string inventoryMode = root.Value<string>("inventoryMode");
            bool bagResidentMode = baseline.TargetMode == EquipmentTargetMode.Bag &&
                string.Equals(inventoryMode, "bag", StringComparison.Ordinal);
            string explicitError = FirstNonEmpty(
                root.Value<string>("errorCode"),
                root.Value<string>("refineProbeReason"),
                root.Value<string>("diagnosticCode"));
            if (!string.IsNullOrWhiteSpace(explicitError))
            {
                return Failure(explicitError);
            }
            if (root.Value<bool?>("available") != true ||
                root.Value<bool?>("readOnly") != true ||
                root.Value<bool?>("actionAuthorized") != false)
            {
                return Failure("resident_read_only_state_invalid");
            }
            string refineProbeState = root.Value<string>("refineProbeState") ?? string.Empty;
            if ((!bagResidentMode && !string.Equals(refineProbeState, "ready", StringComparison.Ordinal)) ||
                (bagResidentMode && string.IsNullOrWhiteSpace(refineProbeState)))
            {
                return Failure("resident_probe_not_ready");
            }

            EquipmentRefineDetector.ProcessIdentity process = ParseProcessIdentity(root["processIdentity"]);
            if (process == null || !process.IsValid)
            {
                return Failure("resident_process_identity_missing");
            }
            string container = root.Value<string>("containerIdentity") ?? string.Empty;
            string session = root.Value<string>("streamSessionId") ?? string.Empty;
            string snapshotId = root.Value<string>("snapshotId") ?? string.Empty;
            long sequence = root.Value<long?>("sequence") ?? 0L;
            if (string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(session) ||
                string.IsNullOrWhiteSpace(snapshotId) || sequence <= 0)
            {
                return Failure("resident_snapshot_identity_missing");
            }

            JArray items = root["items"] as JArray;
            if (items == null)
            {
                return Failure("resident_target_items_missing");
            }
            List<JObject> matchingItems = items
                .OfType<JObject>()
                .Where(item => ItemMatchesBaseline(item, baseline))
                .ToList();
            if (matchingItems.Count != 1)
            {
                return Failure("resident_target_identity_missing_or_changed");
            }

            // Native inventory-only sessions identify themselves explicitly.
            // Keep older offline documents readable, but reject a declared
            // non-bag stream when the preset is bound to a bag item.
            if (baseline.TargetMode == EquipmentTargetMode.Bag &&
                !string.IsNullOrWhiteSpace(inventoryMode) &&
                !string.Equals(inventoryMode, "bag", StringComparison.Ordinal))
            {
                return Failure("resident_bag_inventory_mode_invalid");
            }

            if (bagResidentMode)
            {
                string bagTargetSlot = root.Value<string>("bagTargetSlot") ?? string.Empty;
                string bagTargetMemberIdentity = root.Value<string>("bagTargetMemberIdentity") ?? string.Empty;
                string bagTargetReason = root.Value<string>("bagTargetReason") ?? string.Empty;
                bool anyBagTarget = !string.IsNullOrWhiteSpace(bagTargetSlot) ||
                    !string.IsNullOrWhiteSpace(bagTargetMemberIdentity);
                if (anyBagTarget &&
                    (!string.Equals(bagTargetSlot, baseline.Slot, StringComparison.Ordinal) ||
                     !string.Equals(bagTargetMemberIdentity, baseline.EquipmentIdentity, StringComparison.Ordinal)))
                {
                    return Failure("resident_bag_target_identity_mismatch");
                }
                if (anyBagTarget && !string.Equals(bagTargetReason, "target_bound", StringComparison.Ordinal))
                {
                    return Failure("resident_bag_target_not_bound");
                }
            }

            JObject matchingItem = matchingItems[0];

            JArray candidates = root["refineCandidates"] as JArray;
            if (candidates == null || candidates.Count == 0 ||
                candidates.Any(candidate => !(candidate is JObject) ||
                    ((JObject)candidate).Value<bool?>("candidateOnly") != true ||
                    !(((JObject)candidate)["refineCards"] is JArray)))
            {
                return Failure("resident_refine_candidates_missing");
            }
            List<RefineResponseCard> cards = new List<RefineResponseCard>();
            HashSet<string> associatedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject candidate in candidates.OfType<JObject>())
            {
                if (candidate.Value<bool?>("candidateOnly") != true) continue;
                string associationState = candidate.Value<string>("associationState");
                if (!string.IsNullOrWhiteSpace(associationState) &&
                    !string.Equals(associationState, "unique", StringComparison.Ordinal))
                {
                    return Failure("resident_bag_candidate_association_invalid");
                }
                if (baseline.TargetMode == EquipmentTargetMode.Bag)
                {
                    string targetSlot = candidate.Value<string>("targetSlot") ?? string.Empty;
                    string targetMemberIdentity = candidate.Value<string>("targetMemberIdentity") ?? string.Empty;
                    if ((!string.IsNullOrWhiteSpace(targetSlot) &&
                         !string.Equals(targetSlot, baseline.Slot, StringComparison.Ordinal)) ||
                        (!string.IsNullOrWhiteSpace(targetMemberIdentity) &&
                         !string.Equals(targetMemberIdentity, baseline.EquipmentIdentity, StringComparison.Ordinal)))
                    {
                        return Failure("resident_bag_candidate_target_mismatch");
                    }
                }
                JArray candidateCards = candidate["refineCards"] as JArray;
                if (candidateCards == null) continue;
                foreach (JToken cardToken in candidateCards)
                {
                    JObject cardObject = cardToken as JObject;
                    if (baseline.TargetMode == EquipmentTargetMode.Bag &&
                        !CardBelongsToSlot(cardObject, baseline.Slot))
                    {
                        continue;
                    }
                    if (baseline.TargetMode == EquipmentTargetMode.Bag && cardObject != null)
                    {
                        associatedPaths.Add(cardObject.Value<string>("equipmentPath") ?? string.Empty);
                    }
                    RefineResponseCard card;
                    string error;
                    if (!TryDecodeCard(cardObject, out card, out error))
                    {
                        return Failure(error);
                    }
                    cards.Add(card);
                }
            }

            if (baseline.TargetMode == EquipmentTargetMode.Bag && associatedPaths.Count != 1)
            {
                return Failure("resident_bag_candidate_association_ambiguous");
            }

            if (cards.Count != 20 || cards.Any(card => card == null || !card.IsComplete) ||
                cards.Select(card => card.CardIndex).Distinct().Count() != 20 ||
                !cards.Select(card => card.CardIndex).OrderBy(index => index)
                    .SequenceEqual(Enumerable.Range(1, 20)))
            {
                return Failure("resident_refine_cards_incomplete");
            }

            RefineMemoryResultSnapshot result = new RefineMemoryResultSnapshot
            {
                ProcessIdentity = process,
                ContainerIdentity = container,
                StreamSessionId = session,
                SnapshotId = snapshotId,
                Sequence = sequence,
                EquipmentIdentity = baseline.EquipmentIdentity,
                Slot = baseline.Slot,
                EquipmentId = baseline.EquipmentId,
                TargetMode = baseline.TargetMode,
                ItemId = ItemId(matchingItem),
                ItemTypeId = ItemTypeId(matchingItem),
                IsStable = true
            };
            result.Cards.AddRange(cards);
            return result;
        }

        private bool TryDecodeCard(JObject cardObject, out RefineResponseCard card, out string error)
        {
            card = null;
            error = string.Empty;
            if (cardObject == null || cardObject.Value<bool?>("complete") != true ||
                cardObject.Value<string>("cardSource") != "refine-candidate")
            {
                error = "resident_refine_card_schema_invalid";
                return false;
            }

            int cardIndex = cardObject.Value<int?>("cardIndex") ?? 0;
            if (cardIndex < 1 || cardIndex > 20)
            {
                error = "resident_refine_card_index_invalid";
                return false;
            }
            JArray entries = cardObject["entries"] as JArray;
            JArray propertyEntries = cardObject["propertyEntries"] as JArray;
            if (entries == null || propertyEntries == null || entries.Count == 0 ||
                entries.Count != propertyEntries.Count ||
                entries.Any(entry => !(entry is JObject)) ||
                propertyEntries.Any(property => !(property is JObject)))
            {
                error = "resident_refine_card_properties_missing";
                return false;
            }

            Dictionary<int, Dictionary<int, string>> components =
                new Dictionary<int, Dictionary<int, string>>();
            foreach (JObject entry in entries.OfType<JObject>())
            {
                int entryIndex = entry.Value<int?>("index") ?? 0;
                if (entryIndex <= 0 || entry.Value<bool?>("complete") != true ||
                    components.ContainsKey(entryIndex))
                {
                    error = "resident_refine_entry_invalid";
                    return false;
                }
                JArray fields = entry["fields"] as JArray;
                if (fields == null) { error = "resident_refine_entry_fields_missing"; return false; }
                Dictionary<int, string> values = new Dictionary<int, string>();
                foreach (JObject field in fields.OfType<JObject>())
                {
                    int component = field.Value<int?>("component") ?? 0;
                    string value = TokenText(field["value"]);
                    if (component < 1 || component > 3 || value == null || values.ContainsKey(component))
                    {
                        error = "resident_refine_entry_component_invalid";
                        return false;
                    }
                    values[component] = value;
                }
                if (values.Count != 3) { error = "resident_refine_entry_incomplete"; return false; }
                components[entryIndex] = values;
            }

            RefineResponseCard decoded = new RefineResponseCard
            {
                CardIndex = cardIndex,
                IsComplete = true
            };
            HashSet<int> propertyIndexes = new HashSet<int>();
            foreach (JObject property in propertyEntries.OfType<JObject>())
            {
                int entryIndex = property.Value<int?>("entryIndex") ?? 0;
                string rawId = TokenText(property["rawId"]);
                string rawValue = TokenText(property["rawValue"]);
                string rawOrder = TokenText(property["rawOrder"]);
                // The reader intentionally omits propertyKey for raw IDs that
                // have not been mapped. Raw ID/value/order remain the
                // authoritative integrity check; an absent key is an
                // unmapped property, not a malformed card.
                string propertyKey = TokenText(property["propertyKey"]) ?? string.Empty;
                string originalName = FirstNonEmpty(
                    TokenText(property["originalName"]),
                    TokenText(property["name"]));
                if (entryIndex <= 0 || !propertyIndexes.Add(entryIndex) || rawId == null || rawValue == null || rawOrder == null ||
                    !components.ContainsKey(entryIndex) ||
                    components[entryIndex][1] != rawId ||
                    components[entryIndex][2] != rawValue ||
                    components[entryIndex][3] != rawOrder)
                {
                    error = "resident_refine_property_raw_mismatch";
                    return false;
                }

                TargetAttribute mapped = TargetAttribute.Unknown;
                TargetAttribute byKey;
                if (this._attributeMapping.TryGetValue(rawId, out byKey)) mapped = byKey;
                else if (this._attributeMapping.TryGetValue(propertyKey, out byKey)) mapped = byKey;
                int numericValue;
                bool numeric = int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericValue);
                if (!numeric) mapped = TargetAttribute.Unknown;
                string displayName = string.IsNullOrEmpty(originalName)
                    ? (string.IsNullOrEmpty(propertyKey) ? rawId : propertyKey)
                    : originalName;
                decoded.RawProperties.Add(new RefineRawProperty
                {
                    EntryIndex = entryIndex,
                    RawId = rawId,
                    RawValue = rawValue,
                    RawOrder = rawOrder,
                    PropertyKey = propertyKey,
                    Value = FirstNonEmpty(TokenText(property["value"]), rawValue),
                    OriginalName = originalName,
                    MappedAttribute = mapped
                });
                decoded.Attributes.Add(new AttributeValue
                {
                    Type = mapped,
                    Name = displayName,
                    OriginalName = originalName,
                    CurrentValue = numeric ? numericValue : 0,
                    RawValue = numeric ? numericValue : 0,
                    RawValueText = rawValue,
                    RawId = rawId,
                    RawOrder = rawOrder,
                    PropertyKey = propertyKey
                });
            }
            if (decoded.Attributes.Count != entries.Count)
            {
                error = "resident_refine_property_count_mismatch";
                return false;
            }
            card = decoded;
            return true;
        }

        private static bool ItemMatchesBaseline(JObject item, RefineMemoryBaseline baseline)
        {
            if (item == null || item.Value<bool?>("candidateOnly") != true ||
                !string.Equals(item.Value<string>("memberIdentity"), baseline.EquipmentIdentity, StringComparison.Ordinal) ||
                !string.Equals(item.Value<string>("slot"), baseline.Slot, StringComparison.Ordinal))
            {
                return false;
            }

            if (baseline.TargetMode == EquipmentTargetMode.Worn)
            {
                return item.Value<bool?>("isWorn") == true &&
                    string.Equals(EquipmentId(item), baseline.EquipmentId, StringComparison.Ordinal);
            }

            return (string.IsNullOrWhiteSpace(baseline.ItemId) ||
                    string.Equals(ItemId(item), baseline.ItemId, StringComparison.Ordinal)) &&
                (string.IsNullOrWhiteSpace(baseline.ItemTypeId) ||
                    string.Equals(ItemTypeId(item), baseline.ItemTypeId, StringComparison.Ordinal));
        }

        private static bool CardBelongsToSlot(JObject card, string slot)
        {
            string path = card == null ? string.Empty : card.Value<string>("equipmentPath") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(slot)) return false;
            string suffix = path.Split('.').LastOrDefault() ?? string.Empty;
            return string.Equals(suffix, slot, StringComparison.Ordinal);
        }

        private static string ItemId(JObject item)
        {
            JArray fields = item == null ? null : item["rawFields"] as JArray;
            return FirstRawField(fields, "m_ItemId", "m_Id");
        }

        private static string ItemTypeId(JObject item)
        {
            JArray fields = item == null ? null : item["rawFields"] as JArray;
            return FirstRawField(fields, "m_ItemTypeId", "m_LtypeId");
        }

        private static string FirstRawField(JArray fields, params string[] keys)
        {
            if (fields == null || keys == null) return string.Empty;
            foreach (JObject field in fields.OfType<JObject>())
            {
                string key = field.Value<string>("key") ?? string.Empty;
                if (!keys.Any(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase))) continue;
                string value = field.Value<string>("value") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value) && value != "<table>") return value;
            }
            return string.Empty;
        }

        private static Func<CancellationToken, Task<string>> CreateFileProvider(string path)
        {
            return cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("resident_jsonl_path_missing");
                return Task.FromResult(File.ReadAllText(path, Encoding.UTF8));
            };
        }

        private static string LastJsonObject(string document)
        {
            if (string.IsNullOrWhiteSpace(document)) throw new InvalidDataException("resident_json_empty");
            string trimmed = document.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) &&
                trimmed.EndsWith("}", StringComparison.Ordinal)) return trimmed;
            string[] lines = document.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = lines.Length - 1; index >= 0; index--)
            {
                string line = lines[index].Trim();
                if (line.StartsWith("{", StringComparison.Ordinal) && line.EndsWith("}", StringComparison.Ordinal)) return line;
            }
            throw new InvalidDataException("resident_jsonl_record_missing");
        }

        private static EquipmentRefineDetector.ProcessIdentity ParseProcessIdentity(JToken token)
        {
            JObject value = token as JObject;
            if (value == null) return null;
            return new EquipmentRefineDetector.ProcessIdentity
            {
                pid = value.Value<int?>("pid") ?? 0,
                startTicks = value.Value<long?>("startTicks") ?? 0,
                exe = value.Value<string>("exe") ?? string.Empty
            };
        }

        private static string EquipmentId(JObject item)
        {
            return FirstNonEmpty(item.Value<string>("equipmentId"));
        }

        private static string TokenText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.String) return token.Value<string>();
            return token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values == null ? string.Empty : values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static RefineMemoryResultSnapshot Failure(string error)
        {
            return new RefineMemoryResultSnapshot { ErrorCode = string.IsNullOrWhiteSpace(error) ? "resident_source_failed" : error };
        }
    }
}
