package com.xnas.wpe.suite;

import android.content.Context;

import com.xnas.wpe.bosshunt.BossHuntStatus;
import com.xnas.wpe.starhunt.StarHuntStatus;

import org.json.JSONException;
import org.json.JSONObject;
import org.json.JSONTokener;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/** Read-only, Application-Context observer for existing module status contracts. */
public final class ModuleObservationReader {
    private static final int MAX_STATUS_BYTES = 128 * 1024;
    private static final int MAX_JINCHAN_FILES = 128;
    private static final long STATUS_MAX_AGE_SECONDS = 15L;

    public static final class Baseline {
        public final String marker;
        public final long sequence;
        public final String packageName;

        private Baseline(String marker, long sequence, String packageName) {
            this.marker = marker == null ? "" : marker;
            this.sequence = sequence;
            this.packageName = packageName == null ? "" : packageName;
        }
    }

    public static final class Observation {
        public final ScheduleExecution.ObservationResult result;
        public final ScheduleExecution.ReasonCode reason;
        public final long observedAtMillis;

        private Observation(ScheduleExecution.ObservationResult result,
                            ScheduleExecution.ReasonCode reason, long observedAtMillis) {
            this.result = result;
            this.reason = reason;
            this.observedAtMillis = Math.max(0L, observedAtMillis);
        }
    }

    private static final class JinchanState {
        final File file;
        final String token;
        final String packageName;
        final String state;
        final long heartbeat;

        JinchanState(File file, JSONObject json) {
            this.file = file;
            token = json.optString("token", "");
            packageName = json.optString("package", "");
            state = json.optString("state", "");
            heartbeat = json.optLong("heartbeat", 0L);
        }
    }

    private ModuleObservationReader() { }

    public static Baseline capture(Context context, String moduleId, String action) {
        Context app = applicationContext(context);
        if (app == null || !ModuleActions.isModuleId(moduleId)) return new Baseline("", -1L, "");
        File directory = moduleDirectory(app, moduleId);
        if (TargetGame.STARHUNT.equals(moduleId) || TargetGame.BOSSHUNT.equals(moduleId)) {
            long sequence = commandSequence(new File(directory, "command"));
            return new Baseline("", sequence, "");
        }
        if (TargetGame.JINCHAN.equals(moduleId)) {
            List<JinchanState> states = readJinchanStates(directory);
            Set<String> hashes = new HashSet<>();
            String packageName = "";
            long newest = -1L;
            for (JinchanState state : states) {
                if (!isValidJinchanIdentity(state)) continue;
                hashes.add(hash(state.token));
                if (state.file.lastModified() >= newest) {
                    newest = state.file.lastModified();
                    packageName = state.packageName;
                }
            }
            List<String> ordered = new ArrayList<>(hashes);
            Collections.sort(ordered);
            return new Baseline(join(ordered), -1L,
                    "start".equals(action) ? TargetGame.getSelectedPackage(app) : packageName);
        }
        String packageName = "start".equals(action) && TargetGame.supportsModule(
                moduleId, TargetGame.getSelectedPackage(app))
                ? TargetGame.getSelectedPackage(app) : "";
        return new Baseline("", -1L, packageName);
    }

    /** Reads only module files; it never starts an Activity, service, worker, or game. */
    public static Observation observe(Context context, ScheduleExecution.TargetResult target,
                                      long nowMillis) {
        long now = Math.max(0L, nowMillis);
        if (context == null || target == null || target.dispatchState != ScheduleExecution.DispatchState.ACCEPTED) {
            return observation(ScheduleExecution.ObservationResult.WAIT,
                    ScheduleExecution.ReasonCode.OBSERVATION_MISSING, now);
        }
        Context app = applicationContext(context);
        if (app == null) return observation(ScheduleExecution.ObservationResult.WAIT,
                ScheduleExecution.ReasonCode.OBSERVATION_MISSING, now);
        if (TargetGame.STARHUNT.equals(target.moduleId) || TargetGame.BOSSHUNT.equals(target.moduleId)) {
            return observeSequencedModule(app, target, now);
        }
        if (TargetGame.JINCHAN.equals(target.moduleId)) {
            return observeJinchan(app, target, now);
        }
        return observation(ScheduleExecution.ObservationResult.WAIT,
                ScheduleExecution.ReasonCode.NO_CONFIRMATION_CONTRACT, now);
    }

    private static Observation observeSequencedModule(Context context,
                                                      ScheduleExecution.TargetResult target,
                                                      long nowMillis) {
        File directory = moduleDirectory(context, target.moduleId);
        String commandRaw = read(new File(directory, "command"), 32 * 1024);
        String statusRaw = read(new File(directory, "status"), MAX_STATUS_BYTES);
        if (commandRaw.isEmpty() || statusRaw.isEmpty()) return observation(
                ScheduleExecution.ObservationResult.WAIT,
                ScheduleExecution.ReasonCode.OBSERVATION_MISSING, nowMillis);

        long sequence = longField(commandRaw, "sequence", -1L);
        long accepted;
        long updatedAt;
        String mode = field(commandRaw, "mode");
        String expectedMode = "stop".equals(target.action) ? "scan" : "run";
        if (sequence <= target.baselineSequence || !expectedMode.equals(mode)) return observation(
                ScheduleExecution.ObservationResult.WAIT,
                ScheduleExecution.ReasonCode.IDENTITY_MISMATCH, nowMillis);

        boolean star = TargetGame.STARHUNT.equals(target.moduleId);
        String state;
        if (star) {
            StarHuntStatus parsed = StarHuntStatus.parse(statusRaw);
            accepted = parsed.acceptedSequence;
            updatedAt = parsed.updatedAt;
            state = parsed.state;
        } else {
            BossHuntStatus parsed = BossHuntStatus.parse(statusRaw);
            accepted = parsed.acceptedSequence;
            updatedAt = parsed.updatedAt;
            state = parsed.state;
        }
        if (!isFreshAfter(updatedAt, target.dispatchAtMillis, nowMillis)) return observation(
                ScheduleExecution.ObservationResult.WAIT,
                updatedAt <= 0L ? ScheduleExecution.ReasonCode.OBSERVATION_MISSING
                        : ScheduleExecution.ReasonCode.OBSERVATION_STALE, nowMillis);
        if (accepted < sequence) return observation(ScheduleExecution.ObservationResult.WAIT,
                ScheduleExecution.ReasonCode.OBSERVATION_STALE, nowMillis);
        if ("stop".equals(target.action)) {
            boolean scanOnly = isScanOnlyState(state);
            return scanOnly
                    ? observation(ScheduleExecution.ObservationResult.CONFIRMED,
                            ScheduleExecution.ReasonCode.NONE, nowMillis)
                    : observation(ScheduleExecution.ObservationResult.WAIT,
                            ScheduleExecution.ReasonCode.STATE_NOT_CONFIRMED, nowMillis);
        }
        if (isActiveRunState(state)) return observation(
                ScheduleExecution.ObservationResult.CONFIRMED,
                ScheduleExecution.ReasonCode.NONE, nowMillis);
        if ("stopped".equals(state) || "fatal".equals(state)) return observation(
                ScheduleExecution.ObservationResult.FAILED,
                ScheduleExecution.ReasonCode.STATE_NOT_CONFIRMED, nowMillis);
        return observation(ScheduleExecution.ObservationResult.WAIT,
                ScheduleExecution.ReasonCode.STATE_NOT_CONFIRMED, nowMillis);
    }

    private static boolean isScanOnlyState(String state) {
        return "reading".equals(state) || "waiting_role".equals(state)
                || "waiting_map".equals(state) || "waiting_targets".equals(state);
    }

    private static boolean isActiveRunState(String state) {
        return "waiting_targets".equals(state) || "teleport_wait".equals(state)
                || "challenge_wait".equals(state) || "battle".equals(state)
                || "cooldown".equals(state);
    }

    private static Observation observeJinchan(Context context,
                                              ScheduleExecution.TargetResult target,
                                              long nowMillis) {
        File directory = moduleDirectory(context, TargetGame.JINCHAN);
        List<JinchanState> states = readJinchanStates(directory);
        if (states.isEmpty()) return observation(ScheduleExecution.ObservationResult.WAIT,
                ScheduleExecution.ReasonCode.OBSERVATION_MISSING, nowMillis);
        Set<String> baseline = splitHashes(target.baselineMarker);
        long nowSeconds = nowMillis / 1000L;
        boolean staleCandidate = false;
        boolean identityCandidate = false;
        for (JinchanState state : states) {
            if (!isValidJinchanIdentity(state)) continue;
            boolean wasPresent = baseline.contains(hash(state.token));
            if ("start".equals(target.action)) {
                if (wasPresent) continue;
                if (state.file.lastModified() < target.dispatchAtMillis) continue;
                identityCandidate = true;
                if (!target.packageName.isEmpty() && !target.packageName.equals(state.packageName)) continue;
                if (state.heartbeat <= 0L || state.heartbeat > nowSeconds + 5L
                        || nowSeconds - state.heartbeat > 5L) {
                    staleCandidate = true;
                    continue;
                }
                if ("connected".equals(state.state)) return observation(
                        ScheduleExecution.ObservationResult.CONFIRMED,
                        ScheduleExecution.ReasonCode.NONE, nowMillis);
                if ("blocked".equals(state.state)) return observation(
                        ScheduleExecution.ObservationResult.FAILED,
                        ScheduleExecution.ReasonCode.CLEANUP_UNCONFIRMED, nowMillis);
                if ("error".equals(state.state) || "disconnected".equals(state.state)) return observation(
                        ScheduleExecution.ObservationResult.FAILED,
                        ScheduleExecution.ReasonCode.STATE_NOT_CONFIRMED, nowMillis);
            } else if ("stop".equals(target.action)) {
                if (!wasPresent) continue;
                if (state.file.lastModified() < target.dispatchAtMillis) continue;
                identityCandidate = true;
                if (!target.packageName.isEmpty() && !target.packageName.equals(state.packageName)) continue;
                if ("disconnected".equals(state.state)) return observation(
                        ScheduleExecution.ObservationResult.CONFIRMED,
                        ScheduleExecution.ReasonCode.NONE, nowMillis);
                if ("blocked".equals(state.state)) return observation(
                        ScheduleExecution.ObservationResult.FAILED,
                        ScheduleExecution.ReasonCode.CLEANUP_UNCONFIRMED, nowMillis);
                if ("error".equals(state.state)) return observation(
                        ScheduleExecution.ObservationResult.FAILED,
                        ScheduleExecution.ReasonCode.STATE_NOT_CONFIRMED, nowMillis);
                if (state.heartbeat <= 0L || nowSeconds - state.heartbeat > 5L) staleCandidate = true;
            }
        }
        ScheduleExecution.ReasonCode reason = staleCandidate
                ? ScheduleExecution.ReasonCode.OBSERVATION_STALE
                : identityCandidate ? ScheduleExecution.ReasonCode.STATE_NOT_CONFIRMED
                : ScheduleExecution.ReasonCode.IDENTITY_MISMATCH;
        return observation(ScheduleExecution.ObservationResult.WAIT, reason, nowMillis);
    }

    private static List<JinchanState> readJinchanStates(File directory) {
        File[] files = directory.listFiles();
        if (files == null) return Collections.emptyList();
        Arrays.sort(files, (left, right) -> Long.compare(right.lastModified(), left.lastModified()));
        List<JinchanState> states = new ArrayList<>();
        int count = 0;
        for (File file : files) {
            String name = file.getName();
            if (!file.isFile() || !name.matches("connection-[0-9a-fA-F]{32}\\.json")) continue;
            if (++count > MAX_JINCHAN_FILES) break;
            String raw = read(file, 32 * 1024);
            if (raw.isEmpty()) continue;
            try {
                Object parsed = new JSONTokener(raw).nextValue();
                if (parsed instanceof JSONObject) states.add(new JinchanState(file, (JSONObject) parsed));
            } catch (JSONException | RuntimeException ignored) { }
        }
        return states;
    }

    private static boolean isValidJinchanIdentity(JinchanState state) {
        String fileToken = state.file.getName().substring("connection-".length(),
                state.file.getName().length() - ".json".length());
        return fileToken.equalsIgnoreCase(state.token) && TargetGame.isKnownPackage(state.packageName)
                && !state.state.isEmpty();
    }

    private static boolean isFreshAfter(long updatedAtSeconds, long dispatchAtMillis, long nowMillis) {
        if (updatedAtSeconds <= 0L || updatedAtSeconds > (Long.MAX_VALUE - 999L) / 1000L) return false;
        long updatedAtMillis = updatedAtSeconds * 1000L;
        return updatedAtMillis + 999L >= dispatchAtMillis
                && updatedAtSeconds <= nowMillis / 1000L + 5L
                && nowMillis / 1000L - updatedAtSeconds <= STATUS_MAX_AGE_SECONDS;
    }

    private static long commandSequence(File command) {
        return longField(read(command, 32 * 1024), "sequence", -1L);
    }

    private static String field(String contents, String name) {
        if (contents == null) return "";
        String prefix = name + "=";
        for (String line : contents.split("\\r?\\n")) {
            if (line.startsWith(prefix)) return line.substring(prefix.length()).trim();
        }
        return "";
    }

    private static long longField(String contents, String name, long fallback) {
        try {
            String value = field(contents, name);
            if (value.isEmpty() || !value.matches("-?[0-9]+")) return fallback;
            return Long.parseLong(value);
        } catch (NumberFormatException ignored) { return fallback; }
    }

    private static File moduleDirectory(Context context, String moduleId) {
        return new File(new File(context.getFilesDir(), "modules"), moduleId);
    }

    private static String read(File file, int maxBytes) {
        if (file == null || !file.isFile() || file.length() <= 0L || file.length() > maxBytes) return "";
        try (FileInputStream input = new FileInputStream(file);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int count;
            while ((count = input.read(buffer)) >= 0) {
                if (count == 0) continue;
                if (output.size() + count > maxBytes) return "";
                output.write(buffer, 0, count);
            }
            return new String(output.toByteArray(), StandardCharsets.UTF_8);
        } catch (Exception ignored) { return ""; }
    }

    private static Context applicationContext(Context context) {
        if (context == null) return null;
        Context app = context.getApplicationContext();
        return app == null ? context : app;
    }

    private static Set<String> splitHashes(String marker) {
        Set<String> result = new HashSet<>();
        if (marker == null || marker.isEmpty()) return result;
        for (String part : marker.split(",")) if (part.matches("[0-9a-f]{64}")) result.add(part);
        return result;
    }

    private static String join(List<String> parts) {
        StringBuilder result = new StringBuilder();
        for (String part : parts) {
            if (result.length() > 0) result.append(',');
            result.append(part);
        }
        return result.toString();
    }

    private static String hash(String value) {
        if (value == null) return "";
        try {
            byte[] bytes = MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8));
            StringBuilder result = new StringBuilder(64);
            for (byte item : bytes) result.append(String.format(Locale.US, "%02x", item & 0xff));
            return result.toString();
        } catch (Exception ignored) { return ""; }
    }

    private static Observation observation(ScheduleExecution.ObservationResult result,
                                           ScheduleExecution.ReasonCode reason, long observedAtMillis) {
        return new Observation(result, reason, observedAtMillis);
    }
}
