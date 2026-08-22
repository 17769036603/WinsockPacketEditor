using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.Vision
{
    public static class TreasureC6StreamProtocolDefinition
    {
        public const string ProtocolName = "piaomiao.treasure.stream";
        public const int ProtocolVersion = 1;
        public const int SchemaVersion = 2;
        public const string CacheKind = "treasure_inventory";
        public const string ValidationScope = "current_inventory_membership";
        public const int MaxLineBytes = 1 << 20;
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 28765;
    }

    public sealed class TreasureC6ProtocolException : InvalidOperationException
    {
        public TreasureC6ProtocolException(string code, string message)
            : base(message)
        {
            this.Code = code ?? string.Empty;
        }

        public string Code { get; private set; }
    }

    public sealed class TreasureC6TransportException : IOException
    {
        public TreasureC6TransportException(string code, string message)
            : base(message)
        {
            this.Code = code ?? string.Empty;
        }

        public TreasureC6TransportException(string code, string message, Exception innerException)
            : base(message, innerException)
        {
            this.Code = code ?? string.Empty;
        }

        public string Code { get; private set; }
    }

    public sealed class TreasureC6InventoryItem
    {
        public TreasureC6InventoryItem(
            string memberIdentity,
            int packageNum,
            int scene,
            int x,
            int y)
        {
            if (string.IsNullOrWhiteSpace(memberIdentity))
            {
                throw new ArgumentException("memberIdentity is required.", nameof(memberIdentity));
            }

            if (packageNum < 0 || packageNum > 100000)
            {
                throw new ArgumentOutOfRangeException(nameof(packageNum));
            }

            if (scene <= 0 || scene > 1000000)
            {
                throw new ArgumentOutOfRangeException(nameof(scene));
            }

            if (x < 0 || x > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if (y < 0 || y > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            this.MemberIdentity = memberIdentity;
            this.PackageNum = packageNum;
            this.Scene = scene;
            this.X = x;
            this.Y = y;
        }

        public string MemberIdentity { get; private set; }

        public int PackageNum { get; private set; }

        public int Scene { get; private set; }

        public int X { get; private set; }

        public int Y { get; private set; }

        public string Fingerprint
        {
            get
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}|{1}|{2}|{3}|{4}",
                    this.MemberIdentity,
                    this.PackageNum,
                    this.Scene,
                    this.X,
                    this.Y);
            }
        }

        public bool TryCreateTarget(out TreasureInventoryTarget target)
        {
            target = null;
            if (this.PackageNum <= 0)
            {
                return false;
            }

            target = new TreasureInventoryTarget(
                this.PackageNum,
                this.Scene,
                this.X,
                this.Y);
            return true;
        }

        internal TreasureC6InventoryItem Clone()
        {
            return new TreasureC6InventoryItem(
                this.MemberIdentity,
                this.PackageNum,
                this.Scene,
                this.X,
                this.Y);
        }

        internal bool SemanticallyEquals(TreasureC6InventoryItem other)
        {
            return other != null &&
                string.Equals(this.MemberIdentity, other.MemberIdentity, StringComparison.Ordinal) &&
                this.PackageNum == other.PackageNum &&
                this.Scene == other.Scene &&
                this.X == other.X &&
                this.Y == other.Y;
        }
    }

    public sealed class TreasureC6InventorySnapshot
    {
        public TreasureC6InventorySnapshot(
            string streamSessionId,
            string snapshotId,
            IEnumerable<TreasureC6InventoryItem> items,
            string processIdentity,
            string containerIdentity)
        {
            if (string.IsNullOrWhiteSpace(streamSessionId))
            {
                throw new ArgumentException("streamSessionId is required.", nameof(streamSessionId));
            }

            if (string.IsNullOrWhiteSpace(snapshotId))
            {
                throw new ArgumentException("snapshotId is required.", nameof(snapshotId));
            }

            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            List<TreasureC6InventoryItem> copy = items
                .Where(item => item != null)
                .Select(item => item.Clone())
                .ToList();

            this.StreamSessionId = streamSessionId;
            this.SnapshotId = snapshotId;
            this.Items = new ReadOnlyCollection<TreasureC6InventoryItem>(copy);
            this.ProcessIdentity = processIdentity ?? string.Empty;
            this.ContainerIdentity = containerIdentity ?? string.Empty;
        }

        public string StreamSessionId { get; private set; }

        public string SnapshotId { get; private set; }

        public IReadOnlyList<TreasureC6InventoryItem> Items { get; private set; }

        public int FormalCount
        {
            get { return this.Items.Count; }
        }

        public string ProcessIdentity { get; private set; }

        public string ContainerIdentity { get; private set; }
    }

    public sealed class TreasureC6StreamMessage
    {
        internal TreasureC6StreamMessage(
            string messageType,
            string streamSessionId,
            long sequence,
            string state,
            string snapshotId,
            JObject payload,
            TreasureC6InventorySnapshot snapshot)
        {
            this.MessageType = messageType;
            this.StreamSessionId = streamSessionId;
            this.Sequence = sequence;
            this.State = state;
            this.SnapshotId = snapshotId ?? string.Empty;
            this.Payload = payload == null ? null : (JObject)payload.DeepClone();
            this.Snapshot = snapshot;
        }

        public string MessageType { get; private set; }

        public string StreamSessionId { get; private set; }

        public long Sequence { get; private set; }

        public string State { get; private set; }

        public string SnapshotId { get; private set; }

        public JObject Payload { get; private set; }

        public TreasureC6InventorySnapshot Snapshot { get; private set; }
    }

    /// <summary>
    /// Fail-closed JSONL protocol state machine shared by the TCP client and
    /// offline regression tests. It never exposes an event as consumable
    /// inventory until a complete snapshot has established the baseline.
    /// </summary>
    public sealed class TreasureC6StreamProtocol
    {
        private string sessionId;
        private long? lastSequence;
        private string boundProcessIdentity;
        private string boundContainerIdentity;
        private string currentSnapshotId;
        private List<TreasureC6InventoryItem> currentItems =
            new List<TreasureC6InventoryItem>();

        public bool HandshakeComplete { get; private set; }

        public int AcceptedMessages { get; private set; }

        public TreasureC6InventorySnapshot CurrentSnapshot { get; private set; }

        public IReadOnlyList<TreasureC6InventoryItem> CurrentItems
        {
            get
            {
                return new ReadOnlyCollection<TreasureC6InventoryItem>(
                    this.currentItems.Select(item => item.Clone()).ToList());
            }
        }

        public void Reset()
        {
            this.sessionId = null;
            this.lastSequence = null;
            this.boundProcessIdentity = null;
            this.boundContainerIdentity = null;
            this.currentSnapshotId = null;
            this.currentItems.Clear();
            this.CurrentSnapshot = null;
            this.HandshakeComplete = false;
            this.AcceptedMessages = 0;
        }

        public void ClearConsumable()
        {
            this.boundProcessIdentity = null;
            this.boundContainerIdentity = null;
            this.currentSnapshotId = null;
            this.currentItems.Clear();
            this.CurrentSnapshot = null;
            this.HandshakeComplete = false;
        }

        public TreasureC6StreamMessage ProcessLine(string line)
        {
            JObject message = null;
            try
            {
                message = ParseLine(line);
                string messageType;
                string streamSessionId;
                long sequence;
                string state;
                ValidateCommon(message, out messageType, out streamSessionId, out sequence, out state);
                ValidateMessageShape(message, messageType, state);
                AcceptSequence(messageType, streamSessionId, sequence);
                ValidateEvidenceEpoch(message, state);

                if ((messageType == "hello_ack" || messageType == "state") &&
                    IsNonReadyState(state))
                {
                    this.ClearConsumable();
                }

                TreasureC6InventorySnapshot snapshot = null;
                if (messageType == "snapshot")
                {
                    snapshot = ReadSnapshot(message, streamSessionId);
                    this.SetSnapshot(snapshot);
                    this.HandshakeComplete = true;
                }
                else if (messageType == "event")
                {
                    this.ApplyEvent(message);
                    snapshot = this.CurrentSnapshot;
                }
                else if (messageType == "hello_ack")
                {
                    this.HandshakeComplete = true;
                }

                if (state == "ready")
                {
                    this.boundProcessIdentity = ProcessIdentityKey(
                        (JObject)message["processIdentity"]);
                    this.boundContainerIdentity = message["containerIdentity"].Type == JTokenType.Null
                        ? null
                        : message["containerIdentity"].Value<string>();
                }

                this.AcceptedMessages++;
                string snapshotId = message["snapshotId"] == null
                    ? string.Empty
                    : message["snapshotId"].Value<string>();
                return new TreasureC6StreamMessage(
                    messageType,
                    streamSessionId,
                    sequence,
                    state,
                    snapshotId,
                    message,
                    snapshot);
            }
            catch (TreasureC6ProtocolException)
            {
                this.ClearConsumable();
                throw;
            }
            catch (Exception ex)
            {
                this.ClearConsumable();
                throw new TreasureC6ProtocolException(
                    "message_processing_failed",
                    ex.Message);
            }
        }

        public static string BuildHelloLine(string clientName = "piaomiao-c6-client")
        {
            JObject identity = new JObject
            {
                ["pid"] = 0,
                ["startTicks"] = 0,
                ["exe"] = "windows-client"
            };
            JObject hello = new JObject
            {
                ["protocolName"] = TreasureC6StreamProtocolDefinition.ProtocolName,
                ["protocolVersion"] = TreasureC6StreamProtocolDefinition.ProtocolVersion,
                ["messageType"] = "hello",
                ["schemaVersion"] = TreasureC6StreamProtocolDefinition.SchemaVersion,
                ["cacheKind"] = TreasureC6StreamProtocolDefinition.CacheKind,
                ["streamSessionId"] = "client-" + Guid.NewGuid().ToString("N"),
                ["sequence"] = 0,
                ["state"] = "starting",
                ["validationScope"] = TreasureC6StreamProtocolDefinition.ValidationScope,
                ["actionAuthorized"] = false,
                ["processIdentity"] = identity,
                ["containerIdentity"] = JValue.CreateNull(),
                ["monotonicNs"] = MonotonicNanoseconds(),
                ["supportedProtocolVersions"] = new JArray(TreasureC6StreamProtocolDefinition.ProtocolVersion),
                ["supportedSchemaVersions"] = new JArray(TreasureC6StreamProtocolDefinition.SchemaVersion),
                ["clientName"] = string.IsNullOrWhiteSpace(clientName)
                    ? "piaomiao-c6-client"
                    : clientName
            };
            return hello.ToString(Formatting.None) + "\n";
        }

        private static JObject ParseLine(string line)
        {
            if (line == null)
            {
                throw new TreasureC6ProtocolException("line_missing", "C6 JSONL line is null.");
            }

            if (Encoding.UTF8.GetByteCount(line) > TreasureC6StreamProtocolDefinition.MaxLineBytes)
            {
                throw new TreasureC6ProtocolException("message_too_large", "C6 JSONL message exceeds 1 MiB.");
            }

            if (line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
            {
                line = line.TrimEnd('\r', '\n');
                if (line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
                {
                    throw new TreasureC6ProtocolException("line_framing_invalid", "C6 JSONL message contains multiple lines.");
                }
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                throw new TreasureC6ProtocolException("empty_line", "C6 JSONL message is empty.");
            }

            try
            {
                return JObject.Parse(
                    line,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
            }
            catch (Exception ex)
            {
                throw new TreasureC6ProtocolException("json_invalid", ex.Message);
            }
        }

        private static void ValidateCommon(
            JObject message,
            out string messageType,
            out string streamSessionId,
            out long sequence,
            out string state)
        {
            messageType = RequireString(message, "messageType", "message_type_invalid");
            streamSessionId = RequireString(message, "streamSessionId", "field_type_invalid");
            state = RequireString(message, "state", "state_invalid");
            sequence = RequireLong(message, "sequence", 0, "field_type_invalid");

            if (streamSessionId.Length > 128)
            {
                Fail("field_range_invalid", "C6 streamSessionId is too long.");
            }

            if (!string.Equals(
                message.Value<string>("protocolName"),
                TreasureC6StreamProtocolDefinition.ProtocolName,
                StringComparison.Ordinal))
            {
                Fail("protocol_name_invalid", "Unsupported C6 protocolName.");
            }

            if (message.Value<int?>("protocolVersion") != TreasureC6StreamProtocolDefinition.ProtocolVersion)
            {
                Fail("protocol_version_unsupported", "Unsupported C6 protocolVersion.");
            }

            if (message.Value<int?>("schemaVersion") != TreasureC6StreamProtocolDefinition.SchemaVersion)
            {
                Fail("schema_invalid", "C6 requires schemaVersion=2.");
            }

            if (messageType != "hello" && messageType != "hello_ack" &&
                messageType != "snapshot" && messageType != "event" &&
                messageType != "state" && messageType != "ping" &&
                messageType != "pong" && messageType != "error")
            {
                Fail("message_type_invalid", "Unsupported C6 messageType.");
            }

            if (state != "starting" && state != "discovering" && state != "ready" &&
                state != "refreshing" && state != "degraded" && state != "stopped")
            {
                Fail("state_invalid", "Unsupported C6 stream state.");
            }

            if (!string.Equals(
                message.Value<string>("validationScope"),
                TreasureC6StreamProtocolDefinition.ValidationScope,
                StringComparison.Ordinal))
            {
                Fail("validation_scope_invalid", "C6 requires current_inventory_membership.");
            }

            if (message["actionAuthorized"] == null ||
                message["actionAuthorized"].Type != JTokenType.Boolean ||
                message.Value<bool>("actionAuthorized"))
            {
                Fail("action_authorized", "C6 actionAuthorized must be false.");
            }

            JObject processIdentity = message["processIdentity"] as JObject;
            if (processIdentity == null)
            {
                Fail("process_identity_invalid", "processIdentity must be an object.");
            }

            long pid = RequireLong(processIdentity, "pid", 0, "process_identity_invalid");
            long startTicks = RequireLong(processIdentity, "startTicks", 0, "process_identity_invalid");
            string exe = RequireString(processIdentity, "exe", "process_identity_invalid", true);
            bool allowUnbound = messageType == "hello" ||
                state == "starting" || state == "discovering" ||
                state == "degraded" || state == "stopped";
            if ((pid == 0) != (startTicks == 0) ||
                (!allowUnbound && (pid <= 0 || startTicks <= 0 || string.IsNullOrWhiteSpace(exe))))
            {
                Fail("process_identity_invalid", "C6 processIdentity is not bound to a live process.");
            }

            if (message["containerIdentity"] == null)
            {
                Fail("container_identity_invalid", "containerIdentity is required as null or a string.");
            }

            if (message["containerIdentity"].Type != JTokenType.Null &&
                (message["containerIdentity"].Type != JTokenType.String ||
                 string.IsNullOrWhiteSpace(message.Value<string>("containerIdentity"))))
            {
                Fail("container_identity_invalid", "containerIdentity must be null or non-empty.");
            }

            if (state == "ready" && message["containerIdentity"].Type == JTokenType.Null)
            {
                Fail("container_identity_invalid", "ready messages require containerIdentity.");
            }

            RequireLong(message, "monotonicNs", 0, "field_type_invalid");
        }

        private static void ValidateMessageShape(JObject message, string messageType, string state)
        {
            if (messageType == "hello")
            {
                if (!ContainsInt(message["supportedProtocolVersions"] as JArray,
                    TreasureC6StreamProtocolDefinition.ProtocolVersion) ||
                    !ContainsInt(message["supportedSchemaVersions"] as JArray,
                    TreasureC6StreamProtocolDefinition.SchemaVersion))
                {
                    Fail("protocol_version_unsupported", "C6 hello does not support the current protocol/schema.");
                }

                RequireString(message, "clientName", "field_type_invalid");
            }
            else if (messageType == "hello_ack")
            {
                if (message.Value<int?>("selectedProtocolVersion") !=
                    TreasureC6StreamProtocolDefinition.ProtocolVersion)
                {
                    Fail("protocol_version_unsupported", "hello_ack selected an unsupported protocol.");
                }

                if (message.Value<int?>("selectedSchemaVersion") !=
                    TreasureC6StreamProtocolDefinition.SchemaVersion)
                {
                    Fail("schema_invalid", "hello_ack selected an unsupported schema.");
                }

                if (state != "starting" && state != "discovering" &&
                    state != "ready" && state != "degraded")
                {
                    Fail("state_invalid", "hello_ack has an invalid startup state.");
                }
            }
            else if (messageType == "snapshot")
            {
                if (message.Value<string>("cacheKind") != TreasureC6StreamProtocolDefinition.CacheKind ||
                    state != "ready")
                {
                    Fail("snapshot_invalid", "C6 snapshot must be a ready treasure_inventory message.");
                }

                RequireString(message, "snapshotId", "required_field_missing");
                JArray items = message["items"] as JArray;
                if (items == null)
                {
                    Fail("snapshot_items_invalid", "C6 snapshot items must be an array.");
                }

                int formalCount = RequireInt(message, "formalCount", 0, "field_type_invalid");
                if (formalCount != items.Count)
                {
                    Fail("formal_count_mismatch", "C6 formalCount does not match items.");
                }

                RequireInt(message, "rejectedMemberCount", 0, "field_type_invalid");
                JArray events = message["events"] as JArray;
                if (events == null || events.Count != 0)
                {
                    Fail("snapshot_events_invalid", "Complete C6 snapshots must have events=[].");
                }

                ValidateItems(items);
            }
            else if (messageType == "event")
            {
                if (message.Value<string>("cacheKind") != TreasureC6StreamProtocolDefinition.CacheKind ||
                    state != "ready")
                {
                    Fail("event_invalid", "C6 event must be a ready treasure_inventory message.");
                }

                RequireString(message, "snapshotId", "required_field_missing");
                RequireString(message, "eventType", "event_type_invalid");
                RequireInt(message, "formalCount", 0, "field_type_invalid");
                ValidateEventShape(message);
            }
            else if (messageType == "state")
            {
                RequireString(message, "reasonCode", "required_field_missing");
            }
            else if (messageType == "ping" || messageType == "pong")
            {
                RequireString(message, "nonce", "required_field_missing");
            }
            else if (messageType == "error")
            {
                RequireString(message, "reasonCode", "required_field_missing");
            }
        }

        private void AcceptSequence(string messageType, string incomingSessionId, long sequence)
        {
            if (this.sessionId == null)
            {
                this.sessionId = incomingSessionId;
                this.lastSequence = sequence;
                return;
            }

            if (!string.Equals(this.sessionId, incomingSessionId, StringComparison.Ordinal))
            {
                if (messageType != "hello_ack" && messageType != "state" && messageType != "snapshot")
                {
                    Fail("session_boundary_invalid", "A new C6 session must begin with ack/state/snapshot.");
                }

                this.sessionId = incomingSessionId;
                this.lastSequence = sequence;
                this.ClearConsumable();
                return;
            }

            long expected = (this.lastSequence ?? 0) + 1;
            if (sequence <= (this.lastSequence ?? 0))
            {
                Fail("sequence_duplicate", "C6 sequence is duplicated or descending.");
            }

            if (sequence != expected)
            {
                Fail("sequence_gap", "C6 sequence is not contiguous.");
            }

            this.lastSequence = sequence;
        }

        private void ValidateEvidenceEpoch(JObject message, string state)
        {
            if (state != "ready")
            {
                return;
            }

            string process = ProcessIdentityKey((JObject)message["processIdentity"]);
            string container = message["containerIdentity"].Type == JTokenType.Null
                ? null
                : message.Value<string>("containerIdentity");
            if (this.boundProcessIdentity != null &&
                !string.Equals(this.boundProcessIdentity, process, StringComparison.Ordinal))
            {
                Fail("process_identity_changed", "C6 ready message changed process identity within one session.");
            }

            if (this.boundContainerIdentity != null &&
                !string.Equals(this.boundContainerIdentity, container, StringComparison.Ordinal))
            {
                Fail("container_identity_changed", "C6 ready message changed container identity within one session.");
            }
        }

        private TreasureC6InventorySnapshot ReadSnapshot(JObject message, string streamSessionId)
        {
            JArray itemsToken = (JArray)message["items"];
            List<TreasureC6InventoryItem> items = new List<TreasureC6InventoryItem>();
            foreach (JToken token in itemsToken)
            {
                items.Add(ReadItem(token as JObject, "snapshot item"));
            }

            return new TreasureC6InventorySnapshot(
                streamSessionId,
                message.Value<string>("snapshotId"),
                items,
                ProcessIdentityKey((JObject)message["processIdentity"]),
                message["containerIdentity"].Type == JTokenType.Null
                    ? string.Empty
                    : message.Value<string>("containerIdentity"));
        }

        private void SetSnapshot(TreasureC6InventorySnapshot snapshot)
        {
            this.currentSnapshotId = snapshot.SnapshotId;
            this.currentItems = snapshot.Items.Select(item => item.Clone()).ToList();
            this.CurrentSnapshot = snapshot;
        }

        private void ApplyEvent(JObject message)
        {
            if (this.CurrentSnapshot == null ||
                !string.Equals(
                    this.currentSnapshotId,
                    message.Value<string>("snapshotId"),
                    StringComparison.Ordinal))
            {
                Fail("stale_snapshot_rejected", "C6 event has no matching current complete snapshot.");
            }

            string eventType = message.Value<string>("eventType");
            if (eventType == "added")
            {
                TreasureC6InventoryItem incoming = ReadItem(
                    message["item"] as JObject,
                    "added event item");
                if (this.currentItems.Any(item =>
                    string.Equals(item.MemberIdentity, incoming.MemberIdentity, StringComparison.Ordinal) ||
                    item.PackageNum == incoming.PackageNum))
                {
                    Fail("duplicate_member_or_package", "C6 added event collides with current membership.");
                }

                this.currentItems.Add(incoming);
            }
            else if (eventType == "removed")
            {
                TreasureC6InventoryItem incoming = ReadItem(
                    message["item"] as JObject,
                    "removed event item");
                List<TreasureC6InventoryItem> matches = this.currentItems
                    .Where(item => string.Equals(item.MemberIdentity, incoming.MemberIdentity, StringComparison.Ordinal) &&
                                   item.PackageNum == incoming.PackageNum)
                    .ToList();
                if (matches.Count != 1 || !matches[0].SemanticallyEquals(incoming))
                {
                    Fail("removed_member_mismatch", "C6 removed event does not match current membership.");
                }

                this.currentItems.Remove(matches[0]);
            }
            else if (eventType == "changed")
            {
                TreasureC6InventoryItem before = ReadItem(
                    message["before"] as JObject,
                    "changed event before");
                TreasureC6InventoryItem after = ReadItem(
                    message["after"] as JObject,
                    "changed event after");
                if (before.PackageNum != after.PackageNum ||
                    !string.Equals(before.MemberIdentity, after.MemberIdentity, StringComparison.Ordinal) ||
                    before.SemanticallyEquals(after))
                {
                    Fail("changed_event_invalid", "C6 changed event must keep member/package and change coordinates.");
                }

                int index = this.currentItems.FindIndex(item =>
                    item.SemanticallyEquals(before));
                if (index < 0)
                {
                    Fail("changed_before_mismatch", "C6 changed event before does not match current membership.");
                }

                if (this.currentItems.Any(item =>
                    !item.SemanticallyEquals(before) &&
                    (string.Equals(item.MemberIdentity, after.MemberIdentity, StringComparison.Ordinal) ||
                     item.PackageNum == after.PackageNum)))
                {
                    Fail("duplicate_member_or_package", "C6 changed event collides with current membership.");
                }

                this.currentItems[index] = after;
            }
            else
            {
                Fail("event_type_invalid", "Unsupported C6 eventType.");
            }

            int formalCount = message.Value<int>("formalCount");
            if (formalCount != this.currentItems.Count)
            {
                Fail("formal_count_mismatch", "C6 event formalCount does not match materialized membership.");
            }

            this.CurrentSnapshot = new TreasureC6InventorySnapshot(
                this.CurrentSnapshot.StreamSessionId,
                this.CurrentSnapshot.SnapshotId,
                this.currentItems,
                this.CurrentSnapshot.ProcessIdentity,
                this.CurrentSnapshot.ContainerIdentity);
        }

        private static void ValidateEventShape(JObject message)
        {
            string eventType = message.Value<string>("eventType");
            if (eventType != "added" && eventType != "removed" && eventType != "changed")
            {
                Fail("event_type_invalid", "Unsupported C6 eventType.");
            }

            if (eventType == "added" || eventType == "removed")
            {
                ReadItem(message["item"] as JObject, "event item");
                if (message["before"] != null || message["after"] != null)
                {
                    Fail("event_shape_invalid", "Added/removed C6 events cannot carry before/after.");
                }
                return;
            }

            TreasureC6InventoryItem before = ReadItem(
                message["before"] as JObject,
                "event before");
            TreasureC6InventoryItem after = ReadItem(
                message["after"] as JObject,
                "event after");
            if (before.PackageNum != after.PackageNum ||
                !string.Equals(before.MemberIdentity, after.MemberIdentity, StringComparison.Ordinal))
            {
                Fail("slot_replacement_unproven", "C6 changed event cannot switch container members.");
            }

            if (before.SemanticallyEquals(after) || message["item"] != null)
            {
                Fail("event_shape_invalid", "C6 changed event has no valid semantic change.");
            }
        }

        private static void ValidateItems(JArray items)
        {
            HashSet<string> members = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> packages = new HashSet<int>();
            foreach (JToken token in items)
            {
                TreasureC6InventoryItem item = ReadItem(token as JObject, "snapshot item");
                if (!members.Add(item.MemberIdentity))
                {
                    Fail("duplicate_member_identity", "C6 snapshot contains duplicate memberIdentity.");
                }

                if (!packages.Add(item.PackageNum))
                {
                    Fail("duplicate_package_num", "C6 snapshot contains duplicate packageNum.");
                }
            }
        }

        private static TreasureC6InventoryItem ReadItem(JObject item, string name)
        {
            if (item == null)
            {
                Fail("item_invalid", name + " must be an object.");
            }

            string member = RequireString(item, "memberIdentity", "item_field_missing");
            int packageNum = RequireInt(item, "packageNum", 0, "item_field_type_invalid");
            int scene = RequireInt(item, "scene", 1, "item_field_type_invalid");
            int x = RequireInt(item, "x", 0, "item_field_type_invalid");
            int y = RequireInt(item, "y", 0, "item_field_type_invalid");
            if (member.Length > 512 || packageNum > 100000 || scene > 1000000 ||
                x > 10000 || y > 10000)
            {
                Fail("item_field_range_invalid", name + " contains an out-of-range value.");
            }

            if (member == string.Format("{0}:{1}:{2}", scene, x, y) ||
                member == string.Format("{0},{1},{2}", scene, x, y) ||
                member == string.Format("{0}/{1}/{2}", scene, x, y))
            {
                Fail("member_identity_not_opaque", name + " memberIdentity is coordinate-derived.");
            }

            return new TreasureC6InventoryItem(member, packageNum, scene, x, y);
        }

        private static string ProcessIdentityKey(JObject identity)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1}|{2}",
                identity.Value<long>("pid"),
                identity.Value<long>("startTicks"),
                identity.Value<string>("exe") ?? string.Empty);
        }

        private static bool ContainsInt(JArray array, int expected)
        {
            return array != null && array.Any(token =>
                token != null && token.Type == JTokenType.Integer && token.Value<int>() == expected);
        }

        private static bool IsNonReadyState(string state)
        {
            return state == "starting" || state == "discovering" ||
                state == "degraded" || state == "stopped";
        }

        private static string RequireString(
            JObject source,
            string propertyName,
            string code,
            bool allowEmpty = false)
        {
            JToken token = source[propertyName];
            if (token == null || token.Type != JTokenType.String ||
                (!allowEmpty && string.IsNullOrWhiteSpace(token.Value<string>())))
            {
                Fail(code, "C6 field '" + propertyName + "' must be a string.");
            }

            return token.Value<string>();
        }

        private static int RequireInt(JObject source, string propertyName, int minimum, string code)
        {
            JToken token = source[propertyName];
            if (token == null || token.Type != JTokenType.Integer)
            {
                Fail(code, "C6 field '" + propertyName + "' must be an integer.");
            }

            long value;
            try
            {
                value = token.Value<long>();
            }
            catch (Exception)
            {
                Fail(code, "C6 field '" + propertyName + "' is not an integer.");
                return 0;
            }

            if (value < minimum || value > int.MaxValue)
            {
                Fail("field_range_invalid", "C6 field '" + propertyName + "' is out of range.");
            }

            return (int)value;
        }

        private static long RequireLong(JObject source, string propertyName, long minimum, string code)
        {
            JToken token = source[propertyName];
            if (token == null || token.Type != JTokenType.Integer)
            {
                Fail(code, "C6 field '" + propertyName + "' must be an integer.");
            }

            long value;
            try
            {
                value = token.Value<long>();
            }
            catch (Exception)
            {
                Fail(code, "C6 field '" + propertyName + "' is not an integer.");
                return 0;
            }

            if (value < minimum)
            {
                Fail("field_range_invalid", "C6 field '" + propertyName + "' is below its minimum.");
            }

            return value;
        }

        private static void Fail(string code, string message)
        {
            throw new TreasureC6ProtocolException(code, message);
        }

        private static long MonotonicNanoseconds()
        {
            return (long)(Stopwatch.GetTimestamp() * (1000000000.0 / Stopwatch.Frequency));
        }
    }

    public interface ITreasureC6InventoryStream : IDisposable
    {
        bool IsConnected { get; }

        TreasureC6InventorySnapshot CurrentSnapshot { get; }

        void Connect(CancellationToken cancellationToken);

        TreasureC6StreamMessage ReadNext(CancellationToken cancellationToken);

        void Disconnect();
    }

    /// <summary>
    /// Windows-side TCP client for the ADB-forwarded read-only C6 stream.
    /// It never connects to the game process and never writes game actions.
    /// </summary>
    public sealed class TreasureC6StreamClient : ITreasureC6InventoryStream
    {
        private readonly string host;
        private readonly int port;
        private readonly int connectTimeoutMilliseconds;
        private readonly TreasureC6StreamProtocol protocol = new TreasureC6StreamProtocol();
        private TcpClient tcpClient;
        private NetworkStream networkStream;

        public TreasureC6StreamClient(
            string host = TreasureC6StreamProtocolDefinition.DefaultHost,
            int port = TreasureC6StreamProtocolDefinition.DefaultPort,
            int connectTimeoutMilliseconds = 3000)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("C6 host is required.", nameof(host));
            }

            if (port <= 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            this.host = host;
            this.port = port;
            this.connectTimeoutMilliseconds = Math.Max(1, connectTimeoutMilliseconds);
        }

        public bool IsConnected
        {
            get { return this.tcpClient != null && this.networkStream != null; }
        }

        public TreasureC6InventorySnapshot CurrentSnapshot
        {
            get { return this.protocol.CurrentSnapshot; }
        }

        public TreasureC6StreamProtocol Protocol
        {
            get { return this.protocol; }
        }

        public void Connect(CancellationToken cancellationToken)
        {
            this.Disconnect();
            TcpClient candidate = new TcpClient();
            try
            {
                IAsyncResult connectResult = candidate.BeginConnect(this.host, this.port, null, null);
                using (connectResult.AsyncWaitHandle)
                {
                    int elapsed = 0;
                    while (!connectResult.IsCompleted)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int wait = Math.Min(100, this.connectTimeoutMilliseconds - elapsed);
                        if (wait <= 0 || connectResult.AsyncWaitHandle.WaitOne(wait))
                        {
                            break;
                        }
                        elapsed += wait;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!connectResult.IsCompleted)
                {
                    candidate.Close();
                    throw new TreasureC6TransportException(
                        "connect_timeout",
                        "C6 stream connection timed out.");
                }

                candidate.EndConnect(connectResult);
                this.tcpClient = candidate;
                this.networkStream = candidate.GetStream();
                this.protocol.Reset();
                this.SendHello(cancellationToken);
                while (!this.protocol.HandshakeComplete)
                {
                    this.ReadNext(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                candidate.Close();
                this.Disconnect();
                throw;
            }
            catch (TreasureC6TransportException)
            {
                candidate.Close();
                this.Disconnect();
                throw;
            }
            catch (Exception ex)
            {
                candidate.Close();
                this.Disconnect();
                throw new TreasureC6TransportException(
                    "connect_failed",
                    "C6 stream connection failed: " + ex.Message,
                    ex);
            }
        }

        public TreasureC6StreamMessage ReadNext(CancellationToken cancellationToken)
        {
            if (!this.IsConnected)
            {
                throw new TreasureC6TransportException(
                    "not_connected",
                    "C6 stream is not connected.");
            }

            try
            {
                string line = ReadLine(cancellationToken);
                if (line == null)
                {
                    throw new TreasureC6TransportException(
                        "transport_closed",
                        "C6 stream closed the connection.");
                }

                return this.protocol.ProcessLine(line);
            }
            catch (OperationCanceledException)
            {
                this.Disconnect();
                throw;
            }
            catch (TreasureC6ProtocolException)
            {
                this.Disconnect();
                throw;
            }
            catch (TreasureC6TransportException)
            {
                this.Disconnect();
                throw;
            }
            catch (Exception ex)
            {
                this.Disconnect();
                throw new TreasureC6TransportException(
                    "transport_read_failed",
                    "C6 stream read failed: " + ex.Message,
                    ex);
            }
        }

        public void Disconnect()
        {
            NetworkStream stream = this.networkStream;
            TcpClient client = this.tcpClient;
            this.networkStream = null;
            this.tcpClient = null;
            this.protocol.Reset();

            try
            {
                if (stream != null)
                {
                    stream.Close();
                }
            }
            catch (Exception)
            {
            }

            try
            {
                if (client != null)
                {
                    client.Close();
                }
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            this.Disconnect();
        }

        private void SendHello(CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                TreasureC6StreamProtocol.BuildHelloLine());
            cancellationToken.ThrowIfCancellationRequested();
            this.networkStream.Write(bytes, 0, bytes.Length);
            this.networkStream.Flush();
        }

        private string ReadLine(CancellationToken cancellationToken)
        {
            List<byte> bytes = new List<byte>();
            using (cancellationToken.Register(this.CloseTransportForCancellation))
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int value = this.networkStream.ReadByte();
                    if (value < 0)
                    {
                        if (bytes.Count == 0)
                        {
                            return null;
                        }
                        throw new TreasureC6TransportException(
                            "truncated_line",
                            "C6 stream closed in the middle of a JSONL line.");
                    }

                    if (value == '\n')
                    {
                        return new UTF8Encoding(false, true).GetString(bytes.ToArray());
                    }

                    bytes.Add((byte)value);
                    if (bytes.Count > TreasureC6StreamProtocolDefinition.MaxLineBytes)
                    {
                        throw new TreasureC6ProtocolException(
                            "message_too_large",
                            "C6 JSONL message exceeds 1 MiB.");
                    }
                }
            }
        }

        private void CloseTransportForCancellation()
        {
            try
            {
                if (this.networkStream != null)
                {
                    this.networkStream.Close();
                }
            }
            catch (Exception)
            {
            }

            try
            {
                if (this.tcpClient != null)
                {
                    this.tcpClient.Close();
                }
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// Keeps the C6 socket reader running independently from the preset runner.
    /// The reader continuously applies protocol messages to the latest snapshot
    /// while the runner is sending Jump/AutoDig packets or waiting for its next
    /// decision.  Protocol validation still processes every message in order;
    /// the runner-facing notification keeps only the newest message because the
    /// runner consumes the latest snapshot rather than replaying stale events.
    /// </summary>
    public sealed class TreasureC6BufferedInventoryStream : ITreasureC6InventoryStream
    {
        private readonly TreasureC6StreamClient client;
        private readonly object sync = new object();
        private readonly AutoResetEvent messageReady = new AutoResetEvent(false);

        private CancellationTokenSource pumpCts;
        private Task pumpTask;
        private Exception pumpError;
        private TreasureC6StreamMessage pendingMessage;
        private bool hasPendingMessage;
        private bool connected;
        private bool pumpActive;
        private bool disposed;

        public TreasureC6BufferedInventoryStream(TreasureC6StreamClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public bool IsConnected
        {
            get
            {
                lock (this.sync)
                {
                    return this.connected &&
                        this.pumpError == null &&
                        this.client.IsConnected;
                }
            }
        }

        public TreasureC6InventorySnapshot CurrentSnapshot
        {
            get
            {
                lock (this.sync)
                {
                    // A failed reader invalidates the last snapshot.  Returning
                    // null here makes the runner enter its existing fail-closed
                    // resynchronization path instead of processing stale data.
                    return this.connected && this.pumpError == null
                        ? this.client.CurrentSnapshot
                        : null;
                }
            }
        }

        public void Connect(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            this.Disconnect();
            this.client.Connect(cancellationToken);

            CancellationTokenSource nextPumpCts = new CancellationTokenSource();
            lock (this.sync)
            {
                this.pendingMessage = null;
                this.hasPendingMessage = false;
                this.pumpError = null;
                this.pumpCts = nextPumpCts;
                this.connected = true;
                this.pumpActive = true;
                this.pumpTask = Task.Factory.StartNew(
                    () => this.Pump(nextPumpCts.Token),
                    nextPumpCts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }

        public TreasureC6StreamMessage ReadNext(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            while (true)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    this.Disconnect();
                    throw;
                }

                lock (this.sync)
                {
                    if (this.pumpError != null)
                    {
                        throw this.pumpError;
                    }

                    if (this.hasPendingMessage)
                    {
                        TreasureC6StreamMessage message = this.pendingMessage;
                        this.pendingMessage = null;
                        this.hasPendingMessage = false;
                        return message;
                    }

                    if (!this.connected || !this.client.IsConnected)
                    {
                        throw new TreasureC6TransportException(
                            "not_connected",
                            "C6 buffered stream is not connected.");
                    }
                }

                WaitHandle[] waitHandles =
                {
                    this.messageReady,
                    cancellationToken.WaitHandle
                };
                int signaled = WaitHandle.WaitAny(waitHandles);
                if (signaled == 1)
                {
                    this.Disconnect();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        public void Disconnect()
        {
            Task task;
            CancellationTokenSource cts;
            lock (this.sync)
            {
                if (this.disposed)
                {
                    return;
                }

                this.connected = false;
                task = this.pumpTask;
                cts = this.pumpCts;
                this.pumpTask = null;
                this.pumpCts = null;
                this.pendingMessage = null;
                this.hasPendingMessage = false;
                this.pumpError = null;
            }

            if (cts != null)
            {
                cts.Cancel();
            }

            this.client.Disconnect();
            this.messageReady.Set();

            if (task != null &&
                Task.CurrentId != task.Id)
            {
                try
                {
                    task.Wait(1000);
                }
                catch (AggregateException)
                {
                    // The reader failure is delivered through pumpError when it
                    // is unexpected; shutdown itself must remain best effort.
                }
            }

            lock (this.sync)
            {
                if (task == null || task.IsCompleted)
                {
                    this.pumpActive = false;
                }
            }

            if (cts != null)
            {
                cts.Dispose();
            }
        }

        public void Dispose()
        {
            this.Disconnect();
            bool canDisposeSignal;
            lock (this.sync)
            {
                if (this.disposed)
                {
                    return;
                }
                this.disposed = true;
                canDisposeSignal = !this.pumpActive;
            }
            if (canDisposeSignal)
            {
                this.messageReady.Dispose();
            }
            this.client.Dispose();
        }

        private void Pump(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TreasureC6StreamMessage message =
                        this.client.ReadNext(cancellationToken);
                    lock (this.sync)
                    {
                        if (!this.connected || cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        this.pendingMessage = message;
                        this.hasPendingMessage = true;
                    }
                    this.messageReady.Set();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown closes the underlying stream to unblock ReadNext.
            }
            catch (Exception ex)
            {
                lock (this.sync)
                {
                    if (!cancellationToken.IsCancellationRequested && this.connected)
                    {
                        this.pumpError = ex;
                        this.connected = false;
                        this.pendingMessage = null;
                        this.hasPendingMessage = false;
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    this.client.Disconnect();
                }
            }
            finally
            {
                lock (this.sync)
                {
                    this.pumpActive = false;
                }
                this.messageReady.Set();
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(TreasureC6BufferedInventoryStream));
            }
        }
    }
}
