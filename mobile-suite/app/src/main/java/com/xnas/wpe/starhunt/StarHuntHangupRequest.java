package com.xnas.wpe.starhunt;

/** In-memory, one-shot authorization to turn a confirmed scan into one RUN command. */
public final class StarHuntHangupRequest {
    public enum Phase { WAITING_FOR_SCAN, SUBMITTED, CANCELLED }
    public enum Decision { WAIT, SUBMIT, EXPIRED, CANCEL }

    /** Compatibility view: first selected level. */
    public final int level;
    public final StarHuntSelection levels;
    public final long expectedScanSequence;
    public final long requestedAtMillis;
    private Phase phase = Phase.WAITING_FOR_SCAN;

    public StarHuntHangupRequest(int level, long expectedScanSequence, long requestedAtMillis) {
        this(StarHuntSelection.of(level), expectedScanSequence, requestedAtMillis);
    }

    public StarHuntHangupRequest(StarHuntSelection levels, long expectedScanSequence,
                                 long requestedAtMillis) {
        if (levels == null || levels.isEmpty()) {
            throw new IllegalArgumentException("at least one level is required");
        }
        if (expectedScanSequence <= 0L) throw new IllegalArgumentException("scan sequence must be positive");
        if (requestedAtMillis <= 0L) throw new IllegalArgumentException("request time must be positive");
        this.levels = StarHuntSelection.parseCsv(levels.toCsv());
        this.level = this.levels.firstLevel();
        this.expectedScanSequence = expectedScanSequence;
        this.requestedAtMillis = requestedAtMillis;
    }

    public synchronized Phase getPhase() { return phase; }

    public synchronized boolean isPending() { return phase == Phase.WAITING_FOR_SCAN; }

    public synchronized Decision evaluate(StarHuntStatus status, StarHuntCommand command,
                                          long nowMillis) {
        if (phase != Phase.WAITING_FOR_SCAN) return Decision.CANCEL;
        if (nowMillis < requestedAtMillis || nowMillis - requestedAtMillis >= 60_000L) {
            phase = Phase.CANCELLED;
            return Decision.EXPIRED;
        }
        if (status == null || command == null) return Decision.WAIT;
        if (command.mode != StarHuntCommand.Mode.SCAN
                || command.sequence != expectedScanSequence
                || status.acceptedSequence != expectedScanSequence) return Decision.WAIT;
        if ("fatal".equals(status.state) || "stopped".equals(status.state)) {
            phase = Phase.CANCELLED;
            return Decision.CANCEL;
        }
        long nowSeconds = nowMillis / 1000L;
        return status.canStartHangup(levels, nowSeconds) ? Decision.SUBMIT : Decision.WAIT;
    }

    public synchronized boolean markSubmitted(long scanSequence) {
        if (phase != Phase.WAITING_FOR_SCAN || scanSequence != expectedScanSequence) return false;
        phase = Phase.SUBMITTED;
        return true;
    }

    public synchronized void cancel() {
        if (phase == Phase.WAITING_FOR_SCAN) phase = Phase.CANCELLED;
    }
}
