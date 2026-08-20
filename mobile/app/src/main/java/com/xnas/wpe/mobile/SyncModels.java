package com.xnas.wpe.mobile;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.net.URI;
import java.net.URISyntaxException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.UUID;

/** Protocol models and validation for the v1.5 mobile-sync contract. */
public final class SyncModels {
    public static final int SCHEMA_VERSION = 2;
    public static final int DEFAULT_MAX_SNAPSHOT_BYTES = 8 * 1024 * 1024;

    private SyncModels() {
    }

    public enum Module {
        SEND("send"), PROGRESSION("progression"), ASSISTANT("assistant");

        public final String wireName;

        Module(String wireName) {
            this.wireName = wireName;
        }
    }

    public enum SyncState {
        NO_PROFILE, CACHED_OFFLINE, CONNECTING, CONNECTED_CACHED,
        UPDATE_CHECKING, UPDATING, SYNCED, UPDATE_AVAILABLE,
        ACTION_PENDING, ERROR
    }

    public enum RuntimeState {
        IDLE, STARTING, RUNNING, PAUSING, PAUSED, STOPPING,
        COMPLETED, CANCELLED, FAULTED, UNKNOWN;

        public static RuntimeState parse(String value) {
            if (value == null || value.trim().isEmpty()) {
                return UNKNOWN;
            }
            String normalized = value.trim().toUpperCase(Locale.ROOT);
            try {
                return RuntimeState.valueOf(normalized);
            } catch (IllegalArgumentException ignored) {
                return UNKNOWN;
            }
        }
    }

    public static final class SyncException extends Exception {
        public final String code;
        public final boolean retryable;

        public SyncException(String code, String message) {
            this(code, message, false);
        }

        public SyncException(String code, String message, boolean retryable) {
            super(message);
            this.code = code == null ? "sync_error" : code;
            this.retryable = retryable;
        }
    }

    public static final class Manifest {
        public final int schemaVersion;
        public final String revision;
        public final String payloadSha256;
        public final int payloadBytes;
        public final String generatedAt;
        public final int sendCount;
        public final int progressionCount;
        public final int assistantCount;
        public final Set<String> capabilities;

        private Manifest(int schemaVersion, String revision, String payloadSha256,
                         int payloadBytes, String generatedAt, int sendCount,
                         int progressionCount, int assistantCount, Set<String> capabilities) {
            this.schemaVersion = schemaVersion;
            this.revision = revision;
            this.payloadSha256 = payloadSha256;
            this.payloadBytes = payloadBytes;
            this.generatedAt = generatedAt;
            this.sendCount = sendCount;
            this.progressionCount = progressionCount;
            this.assistantCount = assistantCount;
            this.capabilities = capabilities;
        }

        public static Manifest parse(JSONObject value, int maxBytes) throws SyncException {
            if (value == null) {
                throw new SyncException("manifest_invalid", "电脑端返回了空版本清单。");
            }
            int schema = validatedInt(value, "manifest.schemaVersion",
                    "schemaVersion", "SchemaVersion");
            if (schema != SCHEMA_VERSION) {
                throw new SyncException("schema_unsupported", "电脑端版本不兼容，请升级移动端。");
            }
            String revision = firstString(value, "revision", "Revision");
            String payloadSha256 = firstString(value, "payloadSha256", "PayloadSha256");
            int payloadBytes = validatedInt(value, "manifest.payloadBytes",
                    "payloadBytes", "PayloadBytes");
            validateHash(revision, "manifest_invalid", "电脑端版本摘要无效。");
            validateHash(payloadSha256, "manifest_invalid", "电脑端快照校验值无效。");
            if (payloadBytes <= 0 || payloadBytes > maxBytes) {
                throw new SyncException("snapshot_too_large", "电脑端快照大小超出移动端限制。");
            }
            ensureNonNegative(validatedInt(value, "manifest.sendCount",
                    "sendCount", "SendCount"), "manifest.sendCount");
            ensureNonNegative(validatedInt(value, "manifest.progressionCount",
                    "progressionCount", "ProgressionCount"),
                    "manifest.progressionCount");
            ensureNonNegative(validatedInt(value, "manifest.assistantCount",
                    "assistantCount", "AssistantCount"),
                    "manifest.assistantCount");
            Set<String> capabilities = new HashSet<>();
            JSONArray values = value.optJSONArray("capabilities");
            if (values != null) {
                for (int i = 0; i < values.length(); i++) {
                    capabilities.add(values.optString(i, ""));
                }
            }
            if (!capabilities.contains("fullSnapshot") ||
                    !capabilities.contains("expectedRevision")) {
                throw new SyncException("client_incompatible", "电脑端未提供完整移动端同步能力。");
            }
            return new Manifest(schema, revision, payloadSha256, payloadBytes,
                    firstString(value, "generatedAt", "GeneratedAt"),
                    validatedInt(value, "manifest.sendCount", "sendCount", "SendCount"),
                    validatedInt(value, "manifest.progressionCount",
                            "progressionCount", "ProgressionCount"),
                    validatedInt(value, "manifest.assistantCount",
                            "assistantCount", "AssistantCount"),
                    Collections.unmodifiableSet(capabilities));
        }
    }

    public static final class GroupInfo {
        public final UUID id;
        public final String name;
        public final int sortOrder;

        private GroupInfo(UUID id, String name, int sortOrder) {
            this.id = id;
            this.name = name;
            this.sortOrder = sortOrder;
        }

        static GroupInfo parse(JSONObject value) throws SyncException {
            UUID id = validatedUuid(value, "group.id", "id", "Id", "ID");
            int sortOrder = validatedInt(value, "group.sortOrder", "sortOrder", "SortOrder");
            ensurePositive(sortOrder, "group.sortOrder");
            return new GroupInfo(id, firstString(value, "name", "Name"), sortOrder);
        }
    }

    public static final class PacketInfo {
        public final String packetType;
        public final String from;
        public final String to;
        public final int length;
        public final String dataBase64;
        public final String dataHex;
        public final JSONArray byteAnnotations;

        private PacketInfo(String packetType, String from, String to, int length,
                           String dataBase64, String dataHex, JSONArray byteAnnotations) {
            this.packetType = packetType;
            this.from = from;
            this.to = to;
            this.length = length;
            this.dataBase64 = dataBase64;
            this.dataHex = dataHex;
            this.byteAnnotations = byteAnnotations == null ? new JSONArray() : byteAnnotations;
        }

        static PacketInfo parse(JSONObject value) {
            String base64 = firstString(value, "dataBase64", "DataBase64", "bufferBase64", "BufferBase64");
            return new PacketInfo(
                    firstString(value, "packetType", "PacketType"),
                    firstString(value, "from", "From"),
                    firstString(value, "to", "To"),
                    firstInt(value, "length", "Length"),
                    base64,
                    base64ToHex(base64),
                    value.optJSONArray("byteAnnotations"));
        }
    }

    public static final class Preset {
        public final UUID id;
        public final UUID groupId;
        public final String groupName;
        public final String name;
        public final int sortOrder;
        public final boolean enabled;
        public final JSONObject raw;

        private Preset(UUID id, UUID groupId, String groupName, String name,
                        int sortOrder, boolean enabled, JSONObject raw) {
            this.id = id;
            this.groupId = groupId;
            this.groupName = groupName;
            this.name = name;
            this.sortOrder = sortOrder;
            this.enabled = enabled;
            this.raw = raw;
        }

        static Preset parse(JSONObject value, Set<UUID> groupIds) throws SyncException {
            UUID id = validatedUuid(value, "preset.id", "id", "Id", "ID");
            UUID groupId = validatedUuid(value, "preset.groupId", "groupId", "GroupId", "groupID");
            if (!groupIds.contains(groupId)) {
                throw new SyncException("snapshot_invalid", "快照预设 ID 或分组关联无效。");
            }
            return new Preset(id, groupId,
                    firstString(value, "groupName", "GroupName", "folder", "Folder"),
                    firstString(value, "name", "Name"),
                    validatedInt(value, "preset.sortOrder", "sortOrder", "SortOrder"),
                    firstBoolean(value, true, "enabled", "Enabled"), value);
        }

        public String displayName() {
            return name == null || name.trim().isEmpty() ? id.toString() : name;
        }

        public int loopCount() {
            return firstInt(raw, "loopCount", "LoopCount");
        }

        public int intervalMs() {
            return firstInt(raw, "intervalMs", "IntervalMs", "interval");
        }

        public String summaryStatus(RuntimeStatus runtime, String revision) {
            if (runtime != null && id.equals(runtime.presetId)) {
                return runtime.state == RuntimeState.RUNNING ? "发送中"
                        : runtime.state == RuntimeState.PAUSED ? "已暂停"
                        : runtime.state == RuntimeState.STARTING ? "启动中"
                        : runtime.state == RuntimeState.STOPPING ? "停止中"
                        : runtime.state == RuntimeState.COMPLETED ? "已完成"
                        : runtime.state == RuntimeState.CANCELLED ? "已取消"
                        : runtime.state == RuntimeState.FAULTED ? "执行失败" : "待发送";
            }
            return revision == null || revision.isEmpty() ? "待同步" : "已同步·待发送";
        }

        public String readOnlyDetails(Module module) {
            StringBuilder result = new StringBuilder();
            if (module == Module.SEND) {
                JSONArray packets = raw.optJSONArray("packets");
                result.append("启用：").append(enabled ? "是" : "否")
                        .append(" · 系统 Socket：").append(raw.optBoolean("systemSocket", false))
                        .append("\n备注：").append(raw.optString("notes", ""));
                if (packets != null) {
                    for (int i = 0; i < packets.length(); i++) {
                        JSONObject packet = packets.optJSONObject(i);
                        if (packet == null) {
                            continue;
                        }
                        String data = firstString(packet, "dataBase64", "DataBase64",
                                "bufferBase64", "BufferBase64");
                        result.append("\n封包").append(i + 1)
                                .append(" · ").append(firstString(packet, "packetType", "PacketType"))
                                .append(" · ").append(firstString(packet, "from", "From"))
                                .append(" → ").append(firstString(packet, "to", "To"))
                                .append(" · ").append(firstInt(packet, "length", "Length"))
                                .append("B\n").append(base64ToHex(data));
                    }
                }
            } else if (module == Module.PROGRESSION) {
                result.append("启用：").append(enabled ? "是" : "否")
                        .append(" · 模式：").append(firstString(raw, "mode", "Mode"))
                        .append("\n范围：").append(raw.optJSONObject("range"))
                        .append(" · 封包：").append(base64ToHex(firstString(raw,
                                "bufferBase64", "BufferBase64")));
                appendOptional(result, raw, "组合一", "combinationFirstPosition",
                        "combinationFirstLength", "combinationFirstIntervalMs");
                appendOptional(result, raw, "组合二", "combinationSecondPosition",
                        "combinationSecondLength", "combinationSecondIntervalMs");
            } else {
                JSONArray instructions = raw.optJSONArray("instructions");
                result.append("启用：").append(enabled ? "是" : "否")
                        .append(" · 指令数：").append(instructions == null ? 0 : instructions.length());
                if (instructions != null) {
                    for (int i = 0; i < instructions.length(); i++) {
                        result.append("\n指令").append(i + 1).append("：")
                                .append(instructions.optJSONObject(i));
                    }
                }
                if (raw.has("visionProfile") && !raw.isNull("visionProfile")) {
                    result.append("\n视觉参数：").append(raw.opt("visionProfile"));
                }
            }
            return result.length() > 4000 ? result.substring(0, 4000) + "…" : result.toString();
        }

        private static void appendOptional(StringBuilder result, JSONObject value, String label,
                                           String position, String length, String interval) {
            if (value.has(position) || value.has(length) || value.has(interval)) {
                result.append("\n").append(label).append("：")
                        .append(value.optInt(position, 0)).append(" / ")
                        .append(value.optInt(length, 0)).append(" / ")
                        .append(value.optInt(interval, 0)).append("ms");
            }
        }
    }

    public static final class Snapshot {
        public final int schemaVersion;
        public final String revision;
        public final String payloadSha256;
        public final int payloadBytes;
        public final String generatedAt;
        public final List<GroupInfo> sendGroups;
        public final List<GroupInfo> progressionGroups;
        public final List<GroupInfo> assistantGroups;
        public final List<Preset> send;
        public final List<Preset> progression;
        public final List<Preset> assistant;
        public final JSONObject raw;

        private Snapshot(int schemaVersion, String revision, String payloadSha256,
                         int payloadBytes, String generatedAt, List<GroupInfo> sendGroups,
                         List<GroupInfo> progressionGroups, List<GroupInfo> assistantGroups,
                         List<Preset> send, List<Preset> progression, List<Preset> assistant,
                         JSONObject raw) {
            this.schemaVersion = schemaVersion;
            this.revision = revision;
            this.payloadSha256 = payloadSha256;
            this.payloadBytes = payloadBytes;
            this.generatedAt = generatedAt;
            this.sendGroups = Collections.unmodifiableList(sendGroups);
            this.progressionGroups = Collections.unmodifiableList(progressionGroups);
            this.assistantGroups = Collections.unmodifiableList(assistantGroups);
            this.send = Collections.unmodifiableList(send);
            this.progression = Collections.unmodifiableList(progression);
            this.assistant = Collections.unmodifiableList(assistant);
            this.raw = raw;
        }

        public static Snapshot parse(JSONObject value, Manifest manifest, int maxBytes)
                throws SyncException {
            if (value == null || manifest == null) {
                throw new SyncException("snapshot_invalid", "电脑端返回了空快照。");
            }
            int schema = validatedInt(value, "snapshot.schemaVersion",
                    "schemaVersion", "SchemaVersion");
            String revision = firstString(value, "revision", "Revision");
            String payloadSha256 = firstString(value, "payloadSha256", "PayloadSha256");
            int payloadBytes = validatedInt(value, "snapshot.payloadBytes",
                    "payloadBytes", "PayloadBytes");
            if (schema != SCHEMA_VERSION || !manifest.revision.equalsIgnoreCase(revision)) {
                throw new SyncException("snapshot_revision_mismatch", "快照版本在读取期间发生变化。", true);
            }
            validateHash(payloadSha256, "snapshot_invalid", "快照校验值无效。");
            if (payloadBytes <= 0 || payloadBytes > maxBytes) {
                throw new SyncException("snapshot_too_large", "电脑端快照大小超出移动端限制。");
            }
            if (!manifest.payloadSha256.equalsIgnoreCase(payloadSha256)
                    || manifest.payloadBytes != payloadBytes) {
                throw new SyncException("snapshot_revision_mismatch", "快照校验元数据已变化。", true);
            }

            List<GroupInfo> sendGroups = parseGroups(value.optJSONArray("sendGroups"));
            List<GroupInfo> progressionGroups = parseGroups(value.optJSONArray("progressionGroups"));
            List<GroupInfo> assistantGroups = parseGroups(value.optJSONArray("assistantGroups"));
            List<Preset> send = parsePresets(value.optJSONArray("send"), sendGroups, Module.SEND);
            List<Preset> progression = parsePresets(value.optJSONArray("progression"), progressionGroups, Module.PROGRESSION);
            List<Preset> assistant = parsePresets(value.optJSONArray("assistant"), assistantGroups, Module.ASSISTANT);
            if (manifest.sendCount != send.size()
                    || manifest.progressionCount != progression.size()
                    || manifest.assistantCount != assistant.size()) {
                throw new SyncException("snapshot_revision_mismatch", "快照目录数量与版本清单不一致。", true);
            }
            ensureUnique(send, "send");
            ensureUnique(progression, "progression");
            ensureUnique(assistant, "assistant");

            String canonical = canonicalPayload(value);
            String actualSha = sha256Hex(canonical.getBytes(StandardCharsets.UTF_8));
            int actualBytes = canonical.getBytes(StandardCharsets.UTF_8).length;
            if (!actualSha.equalsIgnoreCase(payloadSha256)
                    || !actualSha.equalsIgnoreCase(revision)
                    || actualBytes != payloadBytes) {
                throw new SyncException("snapshot_hash_mismatch", "快照内容校验失败，旧快照已保留。", true);
            }
            return new Snapshot(schema, revision, payloadSha256, payloadBytes,
                    firstString(value, "generatedAt", "GeneratedAt"), sendGroups,
                    progressionGroups, assistantGroups, send, progression, assistant, value);
        }

        public List<Preset> presets(Module module) {
            switch (module) {
                case PROGRESSION:
                    return progression;
                case ASSISTANT:
                    return assistant;
                default:
                    return send;
            }
        }

        public List<GroupInfo> groups(Module module) {
            switch (module) {
                case PROGRESSION:
                    return progressionGroups;
                case ASSISTANT:
                    return assistantGroups;
                default:
                    return sendGroups;
            }
        }

        public Preset find(Module module, UUID id) {
            if (id == null) {
                return null;
            }
            for (Preset value : presets(module)) {
                if (id.equals(value.id)) {
                    return value;
                }
            }
            return null;
        }
    }

    public static final class RuntimeStatus {
        public final RuntimeState state;
        public final boolean starting;
        public final boolean running;
        public final boolean pausing;
        public final boolean paused;
        public final boolean stopping;
        public final UUID presetId;
        public final UUID jobId;
        public final String revision;
        public final Integer completedCount;
        public final Integer totalCount;
        public final String detail;

        private RuntimeStatus(RuntimeState state, boolean starting, boolean running,
                              boolean pausing, boolean paused, boolean stopping,
                              UUID presetId, UUID jobId,
                              String revision, Integer completedCount, Integer totalCount, String detail) {
            this.state = state;
            this.starting = starting;
            this.running = running;
            this.pausing = pausing;
            this.paused = paused;
            this.stopping = stopping;
            this.presetId = presetId;
            this.jobId = jobId;
            this.revision = revision;
            this.completedCount = completedCount;
            this.totalCount = totalCount;
            this.detail = detail;
        }

        static RuntimeStatus parse(JSONObject value) {
            if (value == null) {
                return new RuntimeStatus(RuntimeState.UNKNOWN, false, false, false,
                        false, false, null, null, "", 0, 0, "状态未知");
            }
            RuntimeState state = RuntimeState.parse(firstString(value, "state", "State"));
            boolean starting = firstBoolean(value, false, "starting", "Starting") || state == RuntimeState.STARTING;
            // The desktop DTO also exposes isBusy.  It is true for paused and
            // stopping jobs, so it must never be used as the running flag.
            boolean running = firstBoolean(value, false, "running", "Running")
                    || state == RuntimeState.RUNNING;
            boolean pausing = firstBoolean(value, false, "pausing", "Pausing")
                    || state == RuntimeState.PAUSING;
            boolean paused = firstBoolean(value, false, "paused", "Paused") || state == RuntimeState.PAUSED;
            boolean stopping = firstBoolean(value, false, "stopping", "Stopping") || state == RuntimeState.STOPPING;
            UUID presetId = parseUuid(firstString(value, "presetId", "PresetId"));
            UUID jobId = parseUuid(firstString(value, "jobId", "JobId"));
            return new RuntimeStatus(state, starting, running, pausing, paused, stopping,
                    presetId, jobId,
                    firstString(value, "revision", "Revision"),
                    nullableInt(value, "completedCount", "CompletedCount"),
                    nullableInt(value, "totalCount", "TotalCount"),
                    firstString(value, "detail", "Detail"));
        }

        public boolean isBusy() {
            return starting || running || pausing || paused || stopping ||
                    state == RuntimeState.PAUSING;
        }
    }

    public static final class RuntimeBundle {
        public final RuntimeStatus send;
        public final RuntimeStatus progression;
        public final RuntimeStatus assistant;

        private RuntimeBundle(RuntimeStatus send, RuntimeStatus progression, RuntimeStatus assistant) {
            this.send = send;
            this.progression = progression;
            this.assistant = assistant;
        }

        public static RuntimeBundle parse(JSONObject value) {
            return new RuntimeBundle(
                    RuntimeStatus.parse(object(value, "send", "Send")),
                    RuntimeStatus.parse(object(value, "progression", "Progression")),
                    RuntimeStatus.parse(object(value, "assistant", "Assistant")));
        }

        public static RuntimeBundle parseStrict(JSONObject value) throws SyncException {
            if (value == null) {
                throw new SyncException("runtime_invalid", "电脑端返回了空运行状态。");
            }
            JSONObject send = object(value, "send", "Send");
            JSONObject progression = object(value, "progression", "Progression");
            JSONObject assistant = object(value, "assistant", "Assistant");
            if (send == null || progression == null || assistant == null) {
                throw new SyncException("runtime_invalid", "电脑端运行状态字段不完整。");
            }
            validateRuntimeStatus(send, "send");
            validateRuntimeStatus(progression, "progression");
            validateRuntimeStatus(assistant, "assistant");
            RuntimeBundle result = parse(value);
            if (result.send.state == RuntimeState.UNKNOWN
                    || result.progression.state == RuntimeState.UNKNOWN
                    || result.assistant.state == RuntimeState.UNKNOWN) {
                throw new SyncException("runtime_invalid", "电脑端返回了未知运行状态。");
            }
            return result;
        }

        private static void validateRuntimeStatus(JSONObject value, String module)
                throws SyncException {
            String state = firstString(value, "state", "State");
            RuntimeState parsedState = RuntimeState.parse(state);
            if (parsedState == RuntimeState.UNKNOWN) {
                throw new SyncException("runtime_invalid", module + " 运行状态无效。");
            }
            UUID presetId = runtimeUuid(value, module, "presetId", "PresetId");
            UUID jobId = runtimeUuid(value, module, "jobId", "JobId");
            String revision = runtimeRevision(value, module);
            if (requiresRuntimeIdentity(parsedState)) {
                if (isEmptyUuid(presetId) || isEmptyUuid(jobId)
                        || revision.isEmpty()) {
                    throw new SyncException("runtime_invalid", module + " 忙碌状态缺少任务关联信息。");
                }
            }
            for (String name : new String[] {
                    "starting", "Starting", "running", "Running", "pausing", "Pausing",
                    "paused", "Paused", "stopping", "Stopping", "isBusy", "IsBusy"}) {
                if (value.has(name) && !value.isNull(name) && !(value.opt(name) instanceof Boolean)) {
                    throw new SyncException("runtime_invalid", module + " 运行状态字段类型无效。");
                }
            }
            validateRuntimeCounts(value, module, "completedCount", "CompletedCount");
            validateRuntimeCounts(value, module, "totalCount", "TotalCount");
            validateRuntimeFlags(value, module, parsedState);
        }

        private static void validateRuntimeCounts(JSONObject value, String module,
                                                  String... names) throws SyncException {
            if (value == null) {
                return;
            }
            Integer resolved = null;
            for (String name : names) {
                if (!value.has(name)) {
                    continue;
                }
                if (value.isNull(name)) {
                    continue;
                }
                int count = validatedInt(value, module + "." + name, name);
                ensureNonNegative(count, module + "." + name);
                if (resolved != null && resolved != count) {
                    throw new SyncException("runtime_invalid", module + " 运行进度别名字段不一致。");
                }
                resolved = count;
            }
        }

        private static UUID runtimeUuid(JSONObject value, String module, String... names)
                throws SyncException {
            UUID resolved = null;
            for (String name : names) {
                if (!value.has(name) || value.isNull(name)) {
                    continue;
                }
                Object raw = value.opt(name);
                if (!(raw instanceof String)) {
                    throw new SyncException("runtime_invalid", module + " 任务 ID 类型无效。");
                }
                String rawText = (String) raw;
                String text = rawText.trim();
                if (!text.equals(rawText)) {
                    throw new SyncException("runtime_invalid", module + " 任务 ID 格式无效。");
                }
                if (text.isEmpty()) {
                    continue;
                }
                UUID parsed;
                try {
                    if (!isCanonicalUuid(text)) {
                        throw new IllegalArgumentException("uuid");
                    }
                    parsed = UUID.fromString(text);
                } catch (IllegalArgumentException ex) {
                    throw new SyncException("runtime_invalid", module + " 任务 ID 格式无效。");
                }
                if (isEmptyUuid(parsed)) {
                    continue;
                }
                if (resolved != null && !resolved.equals(parsed)) {
                    throw new SyncException("runtime_invalid", module + " 任务 ID 别名字段不一致。");
                }
                resolved = parsed;
            }
            return resolved;
        }

        private static String runtimeRevision(JSONObject value, String module)
                throws SyncException {
            String resolved = "";
            for (String name : new String[] {"revision", "Revision"}) {
                if (!value.has(name) || value.isNull(name)) {
                    continue;
                }
                Object raw = value.opt(name);
                if (!(raw instanceof String)) {
                    throw new SyncException("runtime_invalid", module + " 版本摘要类型无效。");
                }
                String rawText = (String) raw;
                String text = rawText.trim();
                if (!text.equals(rawText)) {
                    throw new SyncException("runtime_invalid", module + " 版本摘要格式无效。");
                }
                if (text.isEmpty()) {
                    continue;
                }
                if (!text.matches("[0-9a-fA-F]{64}")) {
                    throw new SyncException("runtime_invalid", module + " 版本摘要格式无效。");
                }
                if (!resolved.isEmpty() && !resolved.equalsIgnoreCase(text)) {
                    throw new SyncException("runtime_invalid", module + " 版本摘要别名字段不一致。");
                }
                resolved = text;
            }
            return resolved;
        }

        private static void validateRuntimeFlags(JSONObject value, String module,
                                                 RuntimeState state) throws SyncException {
            if (explicitTrue(value, "starting", "Starting") && state != RuntimeState.STARTING) {
                throw new SyncException("runtime_invalid", module + " 启动状态字段与 state 不一致。");
            }
            if (explicitFalse(value, "starting", "Starting") && state == RuntimeState.STARTING) {
                throw new SyncException("runtime_invalid", module + " 启动状态字段与 state 不一致。");
            }
            if (explicitTrue(value, "running", "Running") && state != RuntimeState.RUNNING) {
                throw new SyncException("runtime_invalid", module + " 运行状态字段与 state 不一致。");
            }
            if (explicitFalse(value, "running", "Running") && state == RuntimeState.RUNNING) {
                throw new SyncException("runtime_invalid", module + " 运行状态字段与 state 不一致。");
            }
            if (explicitTrue(value, "pausing", "Pausing") && state != RuntimeState.PAUSING) {
                throw new SyncException("runtime_invalid", module + " 暂停过渡字段与 state 不一致。");
            }
            if (explicitFalse(value, "pausing", "Pausing") && state == RuntimeState.PAUSING) {
                throw new SyncException("runtime_invalid", module + " 暂停过渡字段与 state 不一致。");
            }
            if (explicitTrue(value, "paused", "Paused") && state != RuntimeState.PAUSED) {
                throw new SyncException("runtime_invalid", module + " 暂停状态字段与 state 不一致。");
            }
            if (explicitFalse(value, "paused", "Paused") && state == RuntimeState.PAUSED) {
                throw new SyncException("runtime_invalid", module + " 暂停状态字段与 state 不一致。");
            }
            if (explicitTrue(value, "stopping", "Stopping") && state != RuntimeState.STOPPING) {
                throw new SyncException("runtime_invalid", module + " 停止状态字段与 state 不一致。");
            }
            if (explicitFalse(value, "stopping", "Stopping") && state == RuntimeState.STOPPING) {
                throw new SyncException("runtime_invalid", module + " 停止状态字段与 state 不一致。");
            }
            if (explicitTrue(value, "isBusy", "IsBusy")
                    && state != RuntimeState.STARTING
                    && state != RuntimeState.RUNNING
                    && state != RuntimeState.PAUSING
                    && state != RuntimeState.PAUSED
                    && state != RuntimeState.STOPPING) {
                throw new SyncException("runtime_invalid", module + " 忙碌状态字段与 state 不一致。");
            }
            if (explicitFalse(value, "isBusy", "IsBusy")
                    && (state == RuntimeState.STARTING
                    || state == RuntimeState.RUNNING
                    || state == RuntimeState.PAUSING
                    || state == RuntimeState.PAUSED
                    || state == RuntimeState.STOPPING)) {
                throw new SyncException("runtime_invalid", module + " 忙碌状态字段与 state 不一致。");
            }
        }

        private static boolean explicitTrue(JSONObject value, String... names) {
            if (value == null) {
                return false;
            }
            for (String name : names) {
                if (value.has(name) && Boolean.TRUE.equals(value.opt(name))) {
                    return true;
                }
            }
            return false;
        }

        private static boolean explicitFalse(JSONObject value, String... names) {
            if (value == null) {
                return false;
            }
            for (String name : names) {
                if (value.has(name) && Boolean.FALSE.equals(value.opt(name))) {
                    return true;
                }
            }
            return false;
        }

        private static boolean requiresRuntimeIdentity(RuntimeState state) {
            return state == RuntimeState.STARTING || state == RuntimeState.RUNNING
                    || state == RuntimeState.PAUSING || state == RuntimeState.PAUSED
                    || state == RuntimeState.STOPPING;
        }

        private static boolean isEmptyUuid(UUID value) {
            return value == null || (value.getMostSignificantBits() == 0L
                    && value.getLeastSignificantBits() == 0L);
        }

        public RuntimeStatus forModule(Module module) {
            switch (module) {
                case PROGRESSION:
                    return progression;
                case ASSISTANT:
                    return assistant;
                default:
                    return send;
            }
        }
    }

    public static final class ActionResult {
        public final boolean accepted;
        public final UUID id;
        public final UUID jobId;
        public final String revision;
        public final String action;

        private ActionResult(boolean accepted, UUID id, UUID jobId, String revision, String action) {
            this.accepted = accepted;
            this.id = id;
            this.jobId = jobId;
            this.revision = revision;
            this.action = action;
        }

        public static ActionResult parse(JSONObject value) {
            return new ActionResult(
                    firstBoolean(value, false, "accepted", "Accepted"),
                    parseUuid(firstString(value, "id", "Id")),
                    parseUuid(firstString(value, "jobId", "JobId")),
                    firstString(value, "revision", "Revision"),
                    firstString(value, "action", "Action"));
        }
    }

    public static String canonicalPayload(JSONObject value) throws SyncException {
        if (value == null) {
            throw new SyncException("snapshot_invalid", "快照为空。");
        }
        JSONObject payload = new JSONObject();
        try {
            payload.put("assistant", value.optJSONArray("assistant") == null ? new JSONArray() : value.optJSONArray("assistant"));
            payload.put("assistantGroups", value.optJSONArray("assistantGroups") == null ? new JSONArray() : value.optJSONArray("assistantGroups"));
            payload.put("progression", value.optJSONArray("progression") == null ? new JSONArray() : value.optJSONArray("progression"));
            payload.put("progressionGroups", value.optJSONArray("progressionGroups") == null ? new JSONArray() : value.optJSONArray("progressionGroups"));
            payload.put("schemaVersion", validatedInt(value, "snapshot.schemaVersion",
                    "schemaVersion", "SchemaVersion"));
            payload.put("send", value.optJSONArray("send") == null ? new JSONArray() : value.optJSONArray("send"));
            payload.put("sendGroups", value.optJSONArray("sendGroups") == null ? new JSONArray() : value.optJSONArray("sendGroups"));
        } catch (JSONException ex) {
            throw new SyncException("snapshot_invalid", "快照规范化失败。");
        }
        return canonicalize(payload);
    }

    public static String canonicalize(Object value) {
        if (value == null || value == JSONObject.NULL) {
            return "null";
        }
        if (value instanceof JSONObject) {
            JSONObject object = (JSONObject) value;
            List<String> keys = new ArrayList<>();
            Iterator<String> iterator = object.keys();
            while (iterator.hasNext()) {
                keys.add(iterator.next());
            }
            Collections.sort(keys);
            StringBuilder result = new StringBuilder("{");
            for (int i = 0; i < keys.size(); i++) {
                if (i > 0) {
                    result.append(',');
                }
                String key = keys.get(i);
                result.append(canonicalString(key)).append(':')
                        .append(canonicalize(object.opt(key)));
            }
            return result.append('}').toString();
        }
        if (value instanceof JSONArray) {
            JSONArray array = (JSONArray) value;
            StringBuilder result = new StringBuilder("[");
            for (int i = 0; i < array.length(); i++) {
                if (i > 0) {
                    result.append(',');
                }
                result.append(canonicalize(array.opt(i)));
            }
            return result.append(']').toString();
        }
        if (value instanceof String || value instanceof Character) {
            return canonicalString(String.valueOf(value));
        }
        if (value instanceof Number) {
            return canonicalNumber((Number) value);
        }
        return String.valueOf(value);
    }

    private static String canonicalNumber(Number value) {
        String text = String.valueOf(value);
        int exponent = Math.max(text.indexOf('e'), text.indexOf('E'));
        if (exponent >= 0 && exponent + 1 < text.length()
                && text.charAt(exponent + 1) != '+' && text.charAt(exponent + 1) != '-') {
            return text.substring(0, exponent + 1) + "+" + text.substring(exponent + 1);
        }
        return text;
    }

    private static String canonicalString(String value) {
        StringBuilder result = new StringBuilder(value.length() + 2);
        result.append('"');
        for (int i = 0; i < value.length(); i++) {
            char item = value.charAt(i);
            switch (item) {
                case '"':
                    result.append("\\\"");
                    break;
                case '\\':
                    result.append("\\\\");
                    break;
                case '\b':
                    result.append("\\b");
                    break;
                case '\f':
                    result.append("\\f");
                    break;
                case '\n':
                    result.append("\\n");
                    break;
                case '\r':
                    result.append("\\r");
                    break;
                case '\t':
                    result.append("\\t");
                    break;
                default:
                    if (item < 0x20) {
                        result.append("\\u");
                        String hex = Integer.toHexString(item);
                        for (int pad = hex.length(); pad < 4; pad++) {
                            result.append('0');
                        }
                        result.append(hex);
                    } else {
                        result.append(item);
                    }
                    break;
            }
        }
        return result.append('"').toString();
    }

    public static String sha256Hex(byte[] value) {
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] result = digest.digest(value == null ? new byte[0] : value);
            StringBuilder builder = new StringBuilder(result.length * 2);
            for (byte item : result) {
                builder.append(String.format(Locale.ROOT, "%02x", item & 0xff));
            }
            return builder.toString();
        } catch (NoSuchAlgorithmException ex) {
            throw new IllegalStateException("SHA-256 is unavailable", ex);
        }
    }

    public static String profileKey(String endpoint, String username) {
        String canonicalEndpoint = canonicalEndpoint(endpoint);
        return sha256Hex((canonicalEndpoint + "\n" + (username == null ? "" : username.trim()))
                .getBytes(StandardCharsets.UTF_8));
    }

    private static String canonicalEndpoint(String endpoint) {
        String value = endpoint == null ? "" : endpoint.trim();
        try {
            URI uri = new URI(value);
            String scheme = uri.getScheme() == null ? "" : uri.getScheme().toLowerCase(Locale.ROOT);
            String host = uri.getHost() == null ? "" : uri.getHost().toLowerCase(Locale.ROOT);
            if (!"https".equals(scheme) || host.isEmpty()) {
                return value;
            }
            int port = uri.getPort();
            if (port == 443) {
                port = -1;
            }
            String path = uri.getPath() == null ? "" : uri.getPath();
            while (path.endsWith("/") && !path.isEmpty()) {
                path = path.substring(0, path.length() - 1);
            }
            return new URI("https", null, host, port, path, null, null).toString();
        } catch (URISyntaxException ignored) {
            while (value.endsWith("/")) {
                value = value.substring(0, value.length() - 1);
            }
            return value;
        }
    }

    private static List<GroupInfo> parseGroups(JSONArray values) throws SyncException {
        if (values == null) {
            throw new SyncException("snapshot_invalid", "快照缺少分组数组。");
        }
        List<GroupInfo> result = new ArrayList<>();
        Set<UUID> ids = new HashSet<>();
        for (int i = 0; i < values.length(); i++) {
            GroupInfo group = GroupInfo.parse(values.optJSONObject(i));
            if (!ids.add(group.id)) {
                throw new SyncException("snapshot_invalid", "快照包含重复分组 ID。");
            }
            result.add(group);
        }
        Collections.sort(result, new Comparator<GroupInfo>() {
            @Override
            public int compare(GroupInfo left, GroupInfo right) {
                int order = Integer.compare(left.sortOrder, right.sortOrder);
                return order != 0 ? order : left.id.toString().compareTo(right.id.toString());
            }
        });
        return result;
    }

    private static List<Preset> parsePresets(JSONArray values, List<GroupInfo> groups, Module module)
            throws SyncException {
        if (values == null) {
            throw new SyncException("snapshot_invalid", "快照缺少预设数组。");
        }
        Set<UUID> groupIds = new HashSet<>();
        for (GroupInfo group : groups) {
            groupIds.add(group.id);
        }
        List<Preset> result = new ArrayList<>();
        for (int i = 0; i < values.length(); i++) {
            JSONObject value = values.optJSONObject(i);
            validatePresetFields(value, module);
            result.add(Preset.parse(value, groupIds));
        }
        Collections.sort(result, new Comparator<Preset>() {
            @Override
            public int compare(Preset left, Preset right) {
                int order = Integer.compare(left.sortOrder, right.sortOrder);
                return order != 0 ? order : left.id.toString().compareTo(right.id.toString());
            }
        });
        return result;
    }

        private static void validatePresetFields(JSONObject value, Module module) throws SyncException {
        if (value == null) {
            throw new SyncException("snapshot_invalid", "快照包含空预设。");
        }
        ensurePositive(validatedInt(value, "sortOrder", "sortOrder", "SortOrder"), "sortOrder");
        if (module == Module.SEND) {
            requireField(value, "enabled", "发送预设缺少启用状态。");
            requireField(value, "loopCount", "发送预设缺少发送次数。");
            requireField(value, "intervalMs", "发送预设缺少发送间隔。");
            requireField(value, "systemSocket", "发送预设缺少系统 Socket 标记。");
            requireField(value, "notes", "发送预设缺少备注字段。");
            requireBoolean(value, "enabled", "发送预设启用状态无效。");
            requireBoolean(value, "systemSocket", "发送预设系统 Socket 标记无效。");
            ensureNonNegative(validatedInt(value, "send.loopCount",
                    "loopCount", "LoopCount"), "send.loopCount");
            ensureNonNegative(validatedInt(value, "send.intervalMs",
                    "intervalMs", "IntervalMs", "interval"), "send.intervalMs");
            JSONArray packets = value.optJSONArray("packets");
            if (packets == null) {
                throw new SyncException("snapshot_invalid", "发送预设缺少封包集合。");
            }
            for (int i = 0; i < packets.length(); i++) {
                JSONObject packet = packets.optJSONObject(i);
                if (packet == null) {
                    throw new SyncException("snapshot_invalid", "发送预设包含空封包。");
                }
                requireField(packet, "packetType", "发送封包缺少类型字段。");
                requireField(packet, "from", "发送封包缺少源地址字段。");
                requireField(packet, "to", "发送封包缺少目标地址字段。");
                requireField(packet, "length", "发送封包缺少长度字段。");
                int length = validatedInt(packet, "send.packet.length", "length", "Length");
                ensureNonNegative(length, "send.packet.length");
                if (!packet.has("dataBase64") && !packet.has("DataBase64")) {
                    throw new SyncException("snapshot_invalid", "发送封包缺少 Base64 数据。");
                }
                byte[] data = decodeBase64Strict(firstString(packet, "dataBase64", "DataBase64"),
                        "send.packet.dataBase64");
                if (length != data.length) {
                    throw new SyncException("snapshot_invalid", "发送封包长度与 Base64 数据不一致。");
                }
                validateAnnotations(packet.optJSONArray("byteAnnotations"), data.length, "send.packet");
            }
        } else if (module == Module.PROGRESSION) {
            requireField(value, "enabled", "递进预设缺少启用状态。");
            requireField(value, "loopCount", "递进预设缺少循环次数。");
            requireField(value, "intervalMs", "递进预设缺少递进间隔。");
            requireField(value, "nextIntervalMs", "递进预设缺少下条间隔。");
            requireBoolean(value, "enabled", "递进预设启用状态无效。");
            ensurePositive(validatedInt(value, "progression.loopCount",
                    "loopCount", "LoopCount"), "progression.loopCount");
            ensureNonNegative(validatedInt(value, "progression.intervalMs",
                    "intervalMs", "IntervalMs", "interval"), "progression.intervalMs");
            ensureNonNegative(validatedInt(value, "progression.nextIntervalMs",
                    "nextIntervalMs", "NextIntervalMs"), "progression.nextIntervalMs");
            requireField(value, "packetType", "递进预设缺少封包类型字段。");
            requireField(value, "from", "递进预设缺少源地址字段。");
            requireField(value, "to", "递进预设缺少目标地址字段。");
            JSONObject range = value.optJSONObject("range");
            if (range == null) {
                throw new SyncException("snapshot_invalid", "递进预设缺少范围。");
            }
            requireField(range, "start", "递进范围缺少起始位置。");
            requireField(range, "length", "递进范围缺少长度。");
            int start = validatedInt(range, "progression.range.start", "start", "Start");
            int length = validatedInt(range, "progression.range.length", "length", "Length");
            ensureNonNegative(start, "progression.range.start");
            ensureNonNegative(length, "progression.range.length");
            if (!value.has("bufferBase64") && !value.has("BufferBase64")) {
                throw new SyncException("snapshot_invalid", "递进预设缺少 Base64 原始封包。");
            }
            String mode = firstString(value, "mode", "Mode").toLowerCase(Locale.ROOT);
            if (!"sequential".equals(mode) && !"paircombination".equals(mode)) {
                throw new SyncException("snapshot_invalid", "递进预设模式无效。");
            }
            byte[] data = decodeBase64Strict(firstString(value, "bufferBase64", "BufferBase64"),
                    "progression.bufferBase64");
            if (length <= 0 || start > data.length || length > data.length - start) {
                throw new SyncException("snapshot_invalid", "递进范围超出原始封包。");
            }
            validateAnnotations(value.optJSONArray("byteAnnotations"), data.length, "progression");
            validateNonNegative(value, "combinationFirstPosition", "combinationFirstLength",
                    "combinationFirstIntervalMs", "combinationSecondPosition", "combinationSecondLength",
                    "combinationSecondIntervalMs");
            if ("paircombination".equals(mode)) {
                requireField(value, "combinationFirstPosition", "递进组合参数不完整。");
                requireField(value, "combinationFirstLength", "递进组合参数不完整。");
                requireField(value, "combinationFirstIntervalMs", "递进组合参数不完整。");
                requireField(value, "combinationSecondPosition", "递进组合参数不完整。");
                requireField(value, "combinationSecondLength", "递进组合参数不完整。");
                requireField(value, "combinationSecondIntervalMs", "递进组合参数不完整。");
                int firstPosition = validatedInt(value, "combinationFirstPosition",
                        "combinationFirstPosition");
                int firstLength = validatedInt(value, "combinationFirstLength",
                        "combinationFirstLength");
                int secondPosition = validatedInt(value, "combinationSecondPosition",
                        "combinationSecondPosition");
                int secondLength = validatedInt(value, "combinationSecondLength",
                        "combinationSecondLength");
                if (firstPosition >= data.length || secondPosition >= data.length
                        || firstLength <= 0 || secondLength <= 0
                        || firstLength > data.length - firstPosition
                        || secondLength > data.length - secondPosition
                        || firstPosition == secondPosition) {
                    throw new SyncException("snapshot_invalid", "递进组合范围超出原始封包。");
                }
            }
        } else {
            validateAssistantFields(value);
        }
    }

    private static void validateAssistantFields(JSONObject value) throws SyncException {
        if (!value.has("enabled") || !value.has("instructions")
                || value.optJSONArray("instructions") == null
                || !value.has("visionProfile")) {
            throw new SyncException("snapshot_invalid", "助手预设参数不完整。");
        }
        requireBoolean(value, "enabled", "助手预设启用状态无效。");
        JSONArray instructions = value.optJSONArray("instructions");
        for (int i = 0; i < instructions.length(); i++) {
            if (instructions.optJSONObject(i) == null) {
                throw new SyncException("snapshot_invalid", "助手预设包含无效指令。");
            }
        }
        if (!value.isNull("visionProfile") && value.optJSONObject("visionProfile") == null) {
            throw new SyncException("snapshot_invalid", "助手预设视觉参数格式无效。");
        }
    }

    private static void requireField(JSONObject value, String name, String message)
            throws SyncException {
        if (value == null || !value.has(name) || value.isNull(name)) {
            throw new SyncException("snapshot_invalid", message);
        }
    }

    private static void validateNonNegative(JSONObject value, String... names) throws SyncException {
        for (String name : names) {
            if (value.has(name)) {
                ensureNonNegative(validatedInt(value, name, name), name);
            }
        }
    }

    private static void validateAnnotations(JSONArray values, int bufferLength, String field)
            throws SyncException {
        if (values == null) {
            return;
        }
        for (int i = 0; i < values.length(); i++) {
            JSONObject annotation = values.optJSONObject(i);
            if (annotation == null) {
                throw new SyncException("snapshot_invalid", field + " 包含空字节注释。");
            }
            int start = validatedInt(annotation, field + ".annotation.start",
                    "start", "Start");
            int length = validatedInt(annotation, field + ".annotation.length",
                    "length", "Length");
            ensureNonNegative(start, field + ".annotation.start");
            ensureNonNegative(length, field + ".annotation.length");
            if (start > bufferLength || length > bufferLength - start) {
                throw new SyncException("snapshot_invalid", field + " 字节注释超出封包范围。");
            }
        }
    }

    private static void ensureNonNegative(int value, String field) throws SyncException {
        if (value < 0) {
            throw new SyncException("snapshot_invalid", "快照字段不能为负数：" + field);
        }
    }

    private static void ensurePositive(int value, String field) throws SyncException {
        if (value <= 0) {
            throw new SyncException("snapshot_invalid", "快照字段必须大于零：" + field);
        }
    }

    private static void ensureUnique(List<Preset> values, String category) throws SyncException {
        Set<UUID> ids = new HashSet<>();
        for (Preset value : values) {
            if (!ids.add(value.id)) {
                throw new SyncException("snapshot_invalid", "快照包含重复的" + category + "预设 ID。");
            }
        }
    }

    private static void validateHash(String value, String code, String message) throws SyncException {
        if (value == null || !value.matches("[0-9a-fA-F]{64}")) {
            throw new SyncException(code, message);
        }
    }

    private static JSONObject object(JSONObject parent, String... names) {
        if (parent == null) {
            return null;
        }
        for (String name : names) {
            JSONObject value = parent.optJSONObject(name);
            if (value != null) {
                return value;
            }
        }
        return null;
    }

    private static String firstString(JSONObject value, String... names) {
        if (value == null) {
            return "";
        }
        for (String name : names) {
            Object item = value.opt(name);
            if (item != null && item != JSONObject.NULL) {
                return String.valueOf(item);
            }
        }
        return "";
    }

    private static int firstInt(JSONObject value, String... names) {
        if (value == null) {
            return 0;
        }
        for (String name : names) {
            if (value.has(name)) {
                Integer parsed = optionalInteger(value.opt(name));
                return parsed == null ? 0 : parsed;
            }
        }
        return 0;
    }

    private static int validatedInt(JSONObject value, String field, String... names)
            throws SyncException {
        if (value == null) {
            throw new SyncException("snapshot_invalid", "快照缺少整数字段：" + field);
        }
        Integer resolved = null;
        for (String name : names) {
            if (!value.has(name) || value.isNull(name)) {
                continue;
            }
            Object raw = value.opt(name);
            int parsed;
            if (raw instanceof Byte || raw instanceof Short || raw instanceof Integer
                    || raw instanceof Long) {
                long number = ((Number) raw).longValue();
                if (number < Integer.MIN_VALUE || number > Integer.MAX_VALUE) {
                    throw new SyncException("snapshot_invalid", "快照整数超出范围：" + field);
                }
                parsed = (int) number;
            } else if (raw instanceof Float || raw instanceof Double) {
                double number = ((Number) raw).doubleValue();
                if (!Double.isFinite(number) || number != Math.rint(number)
                        || number < Integer.MIN_VALUE || number > Integer.MAX_VALUE) {
                    throw new SyncException("snapshot_invalid", "快照整数格式无效：" + field);
                }
                parsed = (int) number;
            } else {
                throw new SyncException("snapshot_invalid", "快照整数格式无效：" + field);
            }
            if (resolved != null && resolved != parsed) {
                throw new SyncException("snapshot_invalid", "快照整数别名字段不一致：" + field);
            }
            resolved = parsed;
        }
        if (resolved != null) {
            return resolved;
        }
        throw new SyncException("snapshot_invalid", "快照缺少整数字段：" + field);
    }

    private static void requireBoolean(JSONObject value, String name, String message)
            throws SyncException {
        if (value == null || !value.has(name) || value.isNull(name)
                || !(value.opt(name) instanceof Boolean)) {
            throw new SyncException("snapshot_invalid", message);
        }
    }

    private static UUID validatedUuid(JSONObject value, String field, String... names)
            throws SyncException {
        if (value == null) {
            throw new SyncException("snapshot_invalid", "快照缺少 GUID 字段：" + field);
        }
        UUID resolved = null;
        for (String name : names) {
            if (!value.has(name) || value.isNull(name)) {
                continue;
            }
            Object raw = value.opt(name);
            if (!(raw instanceof String)) {
                throw new SyncException("snapshot_invalid", "快照 GUID 格式无效：" + field);
            }
            String text = (String) raw;
            if (!isCanonicalUuid(text)) {
                throw new SyncException("snapshot_invalid", "快照 GUID 格式无效：" + field);
            }
            UUID parsed;
            try {
                parsed = UUID.fromString(text);
            } catch (IllegalArgumentException ex) {
                throw new SyncException("snapshot_invalid", "快照 GUID 格式无效：" + field);
            }
            if (isEmptyUuid(parsed)) {
                throw new SyncException("snapshot_invalid", "快照 GUID 不能为空：" + field);
            }
            if (resolved != null && !resolved.equals(parsed)) {
                throw new SyncException("snapshot_invalid", "快照 GUID 别名字段不一致：" + field);
            }
            resolved = parsed;
        }
        if (resolved == null) {
            throw new SyncException("snapshot_invalid", "快照缺少 GUID 字段：" + field);
        }
        return resolved;
    }

    private static Integer nullableInt(JSONObject value, String... names) {
        if (value == null) {
            return null;
        }
        for (String name : names) {
            if (value.has(name)) {
                if (value.isNull(name)) {
                    continue;
                }
                return optionalInteger(value.opt(name));
            }
        }
        return null;
    }

    private static boolean firstBoolean(JSONObject value, boolean fallback, String... names) {
        if (value == null) {
            return fallback;
        }
        for (String name : names) {
            if (value.has(name)) {
                Object raw = value.opt(name);
                return raw instanceof Boolean ? (Boolean) raw : fallback;
            }
        }
        return fallback;
    }

    private static Integer optionalInteger(Object raw) {
        if (!(raw instanceof Number)) {
            return null;
        }
        if (raw instanceof Byte || raw instanceof Short || raw instanceof Integer
                || raw instanceof Long) {
            long value = ((Number) raw).longValue();
            return value < Integer.MIN_VALUE || value > Integer.MAX_VALUE
                    ? null : (int) value;
        }
        double value = ((Number) raw).doubleValue();
        if (!Double.isFinite(value) || value != Math.rint(value)
                || value < Integer.MIN_VALUE || value > Integer.MAX_VALUE) {
            return null;
        }
        return (int) value;
    }

    private static boolean isEmptyUuid(UUID value) {
        return value == null || (value.getMostSignificantBits() == 0L
                && value.getLeastSignificantBits() == 0L);
    }

    private static UUID parseUuid(String value) {
        try {
            if (value == null || value.trim().isEmpty()) {
                return null;
            }
            String text = value.trim();
            if (!text.equals(value) || !isCanonicalUuid(text)) {
                return null;
            }
            UUID result = UUID.fromString(text);
            return result.equals(new UUID(0L, 0L)) ? null : result;
        } catch (IllegalArgumentException ignored) {
            return null;
        }
    }

    private static boolean isCanonicalUuid(String value) {
        return value != null && value.matches(
                "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-"
                        + "[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
    }

    private static String base64ToHex(String value) {
        if (value == null || value.isEmpty()) {
            return "";
        }
        try {
            byte[] bytes = android.util.Base64.decode(value, android.util.Base64.DEFAULT);
            StringBuilder result = new StringBuilder(bytes.length * 2);
            for (byte item : bytes) {
                result.append(String.format(Locale.ROOT, "%02X", item & 0xff));
            }
            return result.toString();
        } catch (IllegalArgumentException ignored) {
            return "";
        }
    }

    private static byte[] decodeBase64Strict(String value, String field) throws SyncException {
        if (value == null) {
            throw new SyncException("snapshot_invalid", "快照缺少 Base64 字段：" + field);
        }
        try {
            if (!value.equals(value.trim()) || value.matches(".*\\s+.*")) {
                throw new IllegalArgumentException("whitespace");
            }
            byte[] decoded = android.util.Base64.decode(value, android.util.Base64.DEFAULT);
            String normalized = android.util.Base64.encodeToString(decoded, android.util.Base64.NO_WRAP);
            if (!normalized.equals(value)) {
                throw new IllegalArgumentException("non-canonical");
            }
            return decoded;
        } catch (IllegalArgumentException ex) {
            throw new SyncException("snapshot_invalid", "快照 Base64 字段无效：" + field);
        }
    }
}
