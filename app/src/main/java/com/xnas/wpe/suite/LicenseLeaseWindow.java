package com.xnas.wpe.suite;

/** Pure validation for server lease arithmetic; elapsed time is captured before each request. */
public final class LicenseLeaseWindow {
    private LicenseLeaseWindow() {}

    public static long deadline(long requestStartedElapsedMs, long nowElapsedMs,
                                long expiresAtEpochMs, long serverTimeEpochMs,
                                long leaseSeconds) {
        if (leaseSeconds <= 0 || leaseSeconds > 120) throw new IllegalArgumentException("leaseSeconds");
        if (serverTimeEpochMs <= 0 || expiresAtEpochMs <= serverTimeEpochMs) throw new IllegalArgumentException("expiry");
        long remainingMs = expiresAtEpochMs - serverTimeEpochMs;
        if (leaseSeconds * 1000L > remainingMs) throw new IllegalArgumentException("lease exceeds expiry");
        if (requestStartedElapsedMs < 0 || nowElapsedMs < requestStartedElapsedMs) {
            throw new IllegalArgumentException("lease elapsed");
        }
        long durationMs = leaseSeconds * 1000L;
        if (requestStartedElapsedMs > Long.MAX_VALUE - durationMs) throw new IllegalArgumentException("deadline overflow");
        long deadline = requestStartedElapsedMs + durationMs;
        if (deadline <= nowElapsedMs) throw new IllegalArgumentException("lease elapsed");
        return deadline;
    }
}
