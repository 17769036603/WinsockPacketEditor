package com.xnas.wpe.suite;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/** Persistable, per-task execution outcome. This class has no Android dependencies. */
public final class ScheduleExecution {
    public enum AggregateState {
        NONE("none"), PENDING("pending"), DISPATCHED("dispatched"), CONFIRMED("confirmed"),
        PARTIAL("partial"), FAILED("failed"), UNKNOWN("unknown");

        private final String value;
        AggregateState(String value) { this.value = value; }
        public String value() { return value; }

        public static AggregateState fromValue(String value) {
            for (AggregateState state : values()) if (state.value.equals(value)) return state;
            return UNKNOWN;
        }
    }

    public enum DispatchState { NOT_ATTEMPTED, ACCEPTED, FAILED }
    public enum ConfirmationState { PENDING, CONFIRMED, FAILED, UNKNOWN }
    public enum ObservationResult { WAIT, CONFIRMED, FAILED, UNKNOWN }

    /** Stable safe codes only; never persist module-provided text as a reason. */
    public enum ReasonCode {
        NONE("none"),
        UNSUPPORTED_TARGET("unsupported_target"),
        UNSUPPORTED_ACTION("unsupported_action"),
        INVALID_TARGET("invalid_target"),
        DISPATCH_REJECTED("dispatch_rejected"),
        DISPATCH_EXCEPTION("dispatch_exception"),
        OBSERVATION_MISSING("observation_missing"),
        OBSERVATION_STALE("observation_stale"),
        IDENTITY_MISMATCH("identity_mismatch"),
        STATE_NOT_CONFIRMED("state_not_confirmed"),
        CLEANUP_UNCONFIRMED("cleanup_unconfirmed"),
        NO_CONFIRMATION_CONTRACT("no_confirmation_contract"),
        TIMEOUT("timeout"),
        INTERRUPTED("interrupted");

        private final String value;
        ReasonCode(String value) { this.value = value; }
        public String value() { return value; }

        public static ReasonCode fromValue(String value) {
            for (ReasonCode reason : values()) if (reason.value.equals(value)) return reason;
            return STATE_NOT_CONFIRMED;
        }
    }

    public static final long DEFAULT_TIMEOUT_MS = 45_000L;

    public final long taskId;
    public final String action;
    public final long firedAtMillis;
    public final long timeoutMillis;
    private final List<TargetResult> targets;

    private ScheduleExecution(long taskId, String action, long firedAtMillis,
                              long timeoutMillis, List<TargetResult> targets) {
        this.taskId = taskId;
        this.action = safeAction(action);
        this.firedAtMillis = Math.max(0L, firedAtMillis);
        this.timeoutMillis = Math.max(1L, timeoutMillis);
        this.targets = new ArrayList<>(targets);
    }

    public static ScheduleExecution create(long taskId, String action, long firedAtMillis,
                                           String[] targetIds, long timeoutMillis) {
        List<TargetResult> targets = new ArrayList<>();
        if (targetIds != null) {
            for (String targetId : targetIds) {
                String id = safeTargetId(targetId);
                if (id.isEmpty() || contains(targets, id)) continue;
                targets.add(new TargetResult(id, safeAction(action)));
            }
        }
        return new ScheduleExecution(taskId, action, firedAtMillis, timeoutMillis, targets);
    }

    static ScheduleExecution restore(long taskId, String action, long firedAtMillis,
                                     long timeoutMillis, List<TargetResult> targets) {
        return new ScheduleExecution(taskId, action, firedAtMillis, timeoutMillis,
                targets == null ? Collections.<TargetResult>emptyList() : targets);
    }

    public List<TargetResult> targets() {
        return Collections.unmodifiableList(targets);
    }

    public TargetResult target(String moduleId) {
        if (moduleId == null) return null;
        for (TargetResult target : targets) if (moduleId.equals(target.moduleId)) return target;
        return null;
    }

    public void setBaseline(String moduleId, String marker, long sequence) {
        setBaseline(moduleId, marker, sequence, "");
    }

    public void setBaseline(String moduleId, String marker, long sequence, String packageName) {
        TargetResult target = target(moduleId);
        if (target == null || target.dispatchState != DispatchState.NOT_ATTEMPTED) return;
        target.baselineMarker = bounded(marker, 16384);
        target.baselineSequence = Math.max(-1L, sequence);
        target.packageName = bounded(packageName, 128);
    }

    public void setPackageName(String moduleId, String packageName) {
        TargetResult target = target(moduleId);
        if (target != null && packageName != null && !packageName.isEmpty()) {
            target.packageName = bounded(packageName, 128);
        }
    }

    public void recordDispatch(String moduleId, boolean accepted, ReasonCode reason,
                               long dispatchAtMillis) {
        TargetResult target = target(moduleId);
        if (target == null || target.dispatchState != DispatchState.NOT_ATTEMPTED) return;
        target.dispatchAtMillis = Math.max(0L, dispatchAtMillis);
        target.deadlineMillis = saturatingAdd(target.dispatchAtMillis, timeoutMillis);
        if (accepted) {
            target.dispatchState = DispatchState.ACCEPTED;
            target.confirmationState = ConfirmationState.PENDING;
            target.reasonCode = ReasonCode.NONE;
        } else {
            target.dispatchState = DispatchState.FAILED;
            target.confirmationState = ConfirmationState.FAILED;
            target.reasonCode = reason == null || reason == ReasonCode.NONE
                    ? ReasonCode.DISPATCH_REJECTED : reason;
        }
    }

    public boolean recordObservation(String moduleId, ObservationResult result,
                                     ReasonCode reason, long observedAtMillis) {
        TargetResult target = target(moduleId);
        if (target == null || target.dispatchState != DispatchState.ACCEPTED
                || target.confirmationState != ConfirmationState.PENDING || result == null) return false;
        long observedAt = Math.max(0L, observedAtMillis);
        ReasonCode safeReason = reason == null ? ReasonCode.NONE : reason;
        if (result == ObservationResult.WAIT) {
            boolean changed = target.reasonCode != safeReason;
            target.reasonCode = safeReason;
            if (changed || target.lastObservedAtMillis == 0L) target.lastObservedAtMillis = observedAt;
            return changed;
        }
        target.lastObservedAtMillis = observedAt;
        target.reasonCode = safeReason;
        switch (result) {
            case CONFIRMED:
                target.confirmationState = ConfirmationState.CONFIRMED;
                target.reasonCode = ReasonCode.NONE;
                return true;
            case FAILED:
                target.confirmationState = ConfirmationState.FAILED;
                if (target.reasonCode == ReasonCode.NONE) target.reasonCode = ReasonCode.STATE_NOT_CONFIRMED;
                return true;
            case UNKNOWN:
                target.confirmationState = ConfirmationState.UNKNOWN;
                if (target.reasonCode == ReasonCode.NONE) target.reasonCode = ReasonCode.NO_CONFIRMATION_CONTRACT;
                return true;
            default:
                return false;
        }
    }

    /** Resolve interrupted dispatches and unresolved confirmations without retrying. */
    public boolean expire(long nowMillis) {
        boolean changed = false;
        for (TargetResult target : targets) {
            if (target.dispatchState == DispatchState.NOT_ATTEMPTED) {
                if (target.confirmationState != ConfirmationState.UNKNOWN
                        || target.reasonCode != ReasonCode.INTERRUPTED) {
                    target.confirmationState = ConfirmationState.UNKNOWN;
                    target.reasonCode = ReasonCode.INTERRUPTED;
                    target.lastObservedAtMillis = Math.max(0L, nowMillis);
                    changed = true;
                }
            } else if (target.dispatchState == DispatchState.ACCEPTED
                    && target.confirmationState == ConfirmationState.PENDING
                    && nowMillis >= target.deadlineMillis) {
                target.confirmationState = ConfirmationState.UNKNOWN;
                target.reasonCode = ReasonCode.TIMEOUT;
                target.lastObservedAtMillis = Math.max(0L, nowMillis);
                changed = true;
            }
        }
        return changed;
    }

    public AggregateState aggregate() {
        if (targets.isEmpty()) return AggregateState.FAILED;
        int notAttempted = 0;
        int pending = 0;
        int confirmed = 0;
        int failed = 0;
        int unknown = 0;
        for (TargetResult target : targets) {
            if (target.dispatchState == DispatchState.NOT_ATTEMPTED) {
                if (target.confirmationState == ConfirmationState.UNKNOWN) unknown++;
                else notAttempted++;
                continue;
            }
            switch (target.confirmationState) {
                case PENDING: pending++; break;
                case CONFIRMED: confirmed++; break;
                case FAILED: failed++; break;
                case UNKNOWN: unknown++; break;
            }
        }
        if (notAttempted > 0) return AggregateState.PENDING;
        if (pending > 0) {
            if (failed > 0 || unknown > 0) return AggregateState.PARTIAL;
            return AggregateState.DISPATCHED;
        }
        if (confirmed == targets.size()) return AggregateState.CONFIRMED;
        if (failed == targets.size()) return AggregateState.FAILED;
        if (unknown == targets.size()) return AggregateState.UNKNOWN;
        return AggregateState.PARTIAL;
    }

    public static final class TargetResult {
        public final String moduleId;
        public final String action;
        public DispatchState dispatchState;
        public ConfirmationState confirmationState;
        public ReasonCode reasonCode;
        public long dispatchAtMillis;
        public long deadlineMillis;
        public long lastObservedAtMillis;
        public long baselineSequence;
        /** Opaque compact baseline; observers store hashes/sequences, never display it. */
        public String baselineMarker;
        public String packageName;

        TargetResult(String moduleId, String action) {
            this.moduleId = safeTargetId(moduleId);
            this.action = safeAction(action);
            dispatchState = DispatchState.NOT_ATTEMPTED;
            confirmationState = ConfirmationState.PENDING;
            reasonCode = ReasonCode.NONE;
            baselineSequence = -1L;
            baselineMarker = "";
            packageName = "";
        }

        static TargetResult restore(String moduleId, String action, DispatchState dispatchState,
                                    ConfirmationState confirmationState, ReasonCode reasonCode,
                                    long dispatchAtMillis, long deadlineMillis,
                                    long lastObservedAtMillis, long baselineSequence,
                                    String baselineMarker, String packageName) {
            TargetResult target = new TargetResult(moduleId, action);
            target.dispatchState = dispatchState == null ? DispatchState.NOT_ATTEMPTED : dispatchState;
            target.confirmationState = confirmationState == null ? ConfirmationState.PENDING : confirmationState;
            target.reasonCode = reasonCode == null ? ReasonCode.NONE : reasonCode;
            target.dispatchAtMillis = Math.max(0L, dispatchAtMillis);
            target.deadlineMillis = Math.max(0L, deadlineMillis);
            target.lastObservedAtMillis = Math.max(0L, lastObservedAtMillis);
            target.baselineSequence = baselineSequence;
            target.baselineMarker = bounded(baselineMarker, 16384);
            target.packageName = bounded(packageName, 128);
            return target;
        }
    }

    static String safeTargetId(String value) {
        if (value == null || value.length() > 64) return "";
        String id = value.trim();
        if (!id.matches("[a-z][a-z0-9_]*")) return "";
        return id;
    }

    private static String safeAction(String value) {
        return "start".equals(value) || "stop".equals(value)
                || "pause".equals(value) || "resume".equals(value)
                ? value : "unknown";
    }

    private static boolean contains(List<TargetResult> values, String id) {
        for (TargetResult value : values) if (value.moduleId.equals(id)) return true;
        return false;
    }

    private static String bounded(String value, int max) {
        if (value == null) return "";
        return value.length() <= max ? value : value.substring(0, max);
    }

    private static long saturatingAdd(long left, long right) {
        if (right > 0L && left > Long.MAX_VALUE - right) return Long.MAX_VALUE;
        return left + right;
    }
}
