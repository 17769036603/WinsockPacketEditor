package com.xnas.wpe.suite;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/** Keeps module status by its independent module and selected-target identity. */
public final class ModuleStatusSnapshotStore {
    public static final long FRESH_FOR_MILLIS = 5_000L;
    public static final String TARGET_DHXY = "com.gdoo.dhxy";
    public static final String TARGET_XXXY = "com.gdoo.xxxy2026";

    private static final String[] MODULE_IDS = {
            "mounthold",
            "battleloop",
            "battlepanel",
            "starhunt",
            "bosshunt",
            "battleskip",
            "jinchan",
            "anlei"
    };

    private final Map<Key, StoredSnapshot> snapshots = new HashMap<>();

    public enum Freshness {
        FRESH,
        STALE,
        UNKNOWN
    }

    /** A UI-safe view of one module state. The target key is intentionally absent. */
    public static final class DiagnosticState {
        public final String moduleId;
        public final ModuleStatus.State state;
        public final Freshness freshness;

        DiagnosticState(String moduleId, ModuleStatus.State state, Freshness freshness) {
            this.moduleId = moduleId;
            this.state = state;
            this.freshness = freshness;
        }
    }

    /** A target-bound status for ordinary host rendering. */
    public static final class Snapshot {
        public final String moduleId;
        public final String targetKey;
        public final ModuleStatus.State state;
        public final Freshness freshness;
        public final boolean hasSample;
        public final long observedAtElapsedRealtimeMs;

        private Snapshot(String moduleId, String targetKey, ModuleStatus.State state,
                         Freshness freshness, boolean hasSample, long observedAtElapsedRealtimeMs) {
            this.moduleId = moduleId;
            this.targetKey = targetKey;
            this.state = state;
            this.freshness = freshness;
            this.hasSample = hasSample;
            this.observedAtElapsedRealtimeMs = observedAtElapsedRealtimeMs;
        }
    }

    /**
     * Publish one observation. An older observation for the same module and target cannot
     * replace a newer one. The return value indicates a state change, not a timestamp refresh.
     */
    public synchronized boolean publish(String moduleId, String targetKey,
                                        ModuleStatus.State state,
                                        long observedAtElapsedRealtimeMs) {
        if (!isKnownModule(moduleId) || !isKnownTarget(targetKey)
                || observedAtElapsedRealtimeMs < 0L) {
            return false;
        }

        Key key = new Key(moduleId, targetKey);
        StoredSnapshot previous = snapshots.get(key);
        if (previous != null && observedAtElapsedRealtimeMs < previous.observedAtElapsedRealtimeMs) {
            return false;
        }

        ModuleStatus.State safeState = state == null ? ModuleStatus.State.UNKNOWN : state;
        snapshots.put(key, new StoredSnapshot(safeState, observedAtElapsedRealtimeMs));
        return previous == null || previous.state != safeState;
    }

    /** Returns UNKNOWN before a first read and after the last observation expires. */
    public synchronized Snapshot get(String moduleId, String targetKey,
                                     long nowElapsedRealtimeMs) {
        if (!isKnownModule(moduleId) || !isKnownTarget(targetKey)) {
            return new Snapshot("unknown", "", ModuleStatus.State.UNKNOWN,
                    Freshness.UNKNOWN, false, 0L);
        }

        StoredSnapshot stored = snapshots.get(new Key(moduleId, targetKey));
        if (stored == null) {
            return new Snapshot(moduleId, targetKey, ModuleStatus.State.UNKNOWN,
                    Freshness.UNKNOWN, false, 0L);
        }
        if (!isFresh(stored.observedAtElapsedRealtimeMs, nowElapsedRealtimeMs)) {
            return new Snapshot(moduleId, targetKey, ModuleStatus.State.UNKNOWN,
                    Freshness.STALE, true, stored.observedAtElapsedRealtimeMs);
        }
        return new Snapshot(moduleId, targetKey, stored.state, Freshness.FRESH,
                true, stored.observedAtElapsedRealtimeMs);
    }

    /** Creates only the fixed module-ID allowlist and drops the target key from output. */
    public synchronized List<DiagnosticState> diagnosticStates(String targetKey,
                                                               long nowElapsedRealtimeMs) {
        List<DiagnosticState> result = new ArrayList<>(MODULE_IDS.length);
        for (String moduleId : MODULE_IDS) {
            Snapshot snapshot = get(moduleId, targetKey, nowElapsedRealtimeMs);
            result.add(new DiagnosticState(moduleId, snapshot.state, snapshot.freshness));
        }
        return result;
    }

    public static String[] allowedModuleIds() {
        return MODULE_IDS.clone();
    }

    public static boolean isKnownModule(String moduleId) {
        if (moduleId == null) return false;
        for (String allowed : MODULE_IDS) if (allowed.equals(moduleId)) return true;
        return false;
    }

    public static boolean isKnownTarget(String targetKey) {
        return TARGET_DHXY.equals(targetKey) || TARGET_XXXY.equals(targetKey);
    }

    private static boolean isFresh(long observedAtElapsedRealtimeMs, long nowElapsedRealtimeMs) {
        return nowElapsedRealtimeMs >= observedAtElapsedRealtimeMs
                && nowElapsedRealtimeMs - observedAtElapsedRealtimeMs <= FRESH_FOR_MILLIS;
    }

    private static final class StoredSnapshot {
        final ModuleStatus.State state;
        final long observedAtElapsedRealtimeMs;

        StoredSnapshot(ModuleStatus.State state, long observedAtElapsedRealtimeMs) {
            this.state = state;
            this.observedAtElapsedRealtimeMs = observedAtElapsedRealtimeMs;
        }
    }

    private static final class Key {
        final String moduleId;
        final String targetKey;

        Key(String moduleId, String targetKey) {
            this.moduleId = moduleId;
            this.targetKey = targetKey;
        }

        @Override public boolean equals(Object other) {
            if (this == other) return true;
            if (!(other instanceof Key)) return false;
            Key key = (Key) other;
            return moduleId.equals(key.moduleId) && targetKey.equals(key.targetKey);
        }

        @Override public int hashCode() {
            return 31 * moduleId.hashCode() + targetKey.hashCode();
        }
    }
}
