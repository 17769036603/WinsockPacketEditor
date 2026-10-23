package com.xnas.wpe.starhunt;

public final class StarHuntStopJournalSelfTest {
    private static int checks;

    public static void main(String[] args) {
        StarHuntStopJournal pending = new StarHuntStopJournal(
                StarHuntStopJournal.Phase.PENDING, 41L, 1_800_000_000_000L, true);
        StarHuntStopJournal restored = StarHuntStopJournal.parse(pending.toFileContents());
        eq(StarHuntStopJournal.Phase.PENDING, restored.phase, "pending phase round trip");
        eq(41L, restored.sequence, "pending sequence round trip");
        eq(1_800_000_000_000L, restored.requestedAtMillis, "request time round trip");
        check(restored.actionSlotHeld, "action slot remains held across service restart");
        check(!restored.isExpired(1_800_000_014_999L, 15_000L),
                "stop monitor waits within its bounded retry window");
        check(restored.isExpired(1_800_000_015_000L, 15_000L),
                "lost worker transitions after bounded wait");
        check(restored.isExpired(1_799_999_999_999L, 15_000L),
                "clock rollback cannot leave a lost worker pending forever");

        StarHuntStopJournal unconfirmed = restored.withPhase(
                StarHuntStopJournal.Phase.UNCONFIRMED, true);
        StarHuntStopJournal retry = new StarHuntStopJournal(
                StarHuntStopJournal.Phase.PENDING, unconfirmed.sequence, 1_800_000_020_000L,
                unconfirmed.actionSlotHeld);
        eq(StarHuntStopJournal.Phase.PENDING,
                StarHuntStopJournal.parse(retry.toFileContents()).phase,
                "explicit retry returns unconfirmed state to pending");
        eq(unconfirmed.sequence, StarHuntStopJournal.parse(retry.toFileContents()).sequence,
                "retry preserves the journaled stop sequence");
        check(StarHuntStopJournal.parse(retry.toFileContents()).actionSlotHeld,
                "retry does not release an unconfirmed action slot");

        StarHuntStopJournal complete = unconfirmed.withPhase(
                StarHuntStopJournal.Phase.COMPLETE, false);
        check(!StarHuntStopJournal.parse(complete.toFileContents()).actionSlotHeld,
                "confirmed completion records a released action slot");
        rejects(() -> StarHuntStopJournal.parse(
                "phase=pending\nsequence=1\nrequestedAtMillis=5\nactionSlotHeld=false\nextra=x\n"),
                "unknown journal fields fail closed");
        System.out.println("StarHuntStopJournalSelfTest PASS (" + checks + " assertions)");
    }

    private static void rejects(Runnable action, String message) {
        checks++;
        try {
            action.run();
        } catch (IllegalArgumentException expected) {
            return;
        }
        throw new AssertionError(message);
    }

    private static void check(boolean condition, String message) {
        checks++;
        if (!condition) throw new AssertionError(message);
    }

    private static void eq(Object expected, Object actual, String message) {
        checks++;
        if (expected == null ? actual != null : !expected.equals(actual)) {
            throw new AssertionError(message + ": expected=" + expected + " actual=" + actual);
        }
    }
}
