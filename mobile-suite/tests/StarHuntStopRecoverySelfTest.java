package com.xnas.wpe.starhunt;

/** Recovery policy must only clear a stop journal that no live worker can confirm. */
public final class StarHuntStopRecoverySelfTest {
    private static int checks;

    public static void main(String[] args) {
        StarHuntStopJournal journal = new StarHuntStopJournal(
                StarHuntStopJournal.Phase.UNCONFIRMED, 669L, 1_800_000_000_000L, true);
        StarHuntStatus gameGone = StarHuntStatus.parse(
                "status=stopped\nreason=original_process_ended_cleanup_unconfirmed\n"
                        + "sessionDetached=true\nacceptedSequence=669\n"
                        + "hooksRemoved=false\nbindingCleared=false\n");
        check(StarHuntStopRecovery.shouldRecover(journal, gameGone, false),
                "game-gone status with an accepted journal recovers");
        check(!StarHuntStopRecovery.shouldRecover(journal, gameGone, true),
                "a live worker always keeps the manual retry path");

        StarHuntStopJournal pending = journal.withPhase(StarHuntStopJournal.Phase.PENDING, true);
        check(StarHuntStopRecovery.shouldRecover(pending, gameGone, false),
                "pending journal can also be recovered after the game process ended");

        StarHuntStopJournal complete = journal.withPhase(StarHuntStopJournal.Phase.COMPLETE, false);
        check(!StarHuntStopRecovery.shouldRecover(complete, gameGone, false),
                "completed journal is not a retry state");
        check(!StarHuntStopRecovery.shouldRecover(null, gameGone, false),
                "missing journal is not recoverable");
        check(!StarHuntStopRecovery.shouldRecover(journal, null, false),
                "missing status is not recoverable");

        StarHuntStatus cleaning = StarHuntStatus.parse(
                "status=stopped\nacceptedSequence=669\nhooksRemoved=true\nbindingCleared=true\n");
        check(!StarHuntStopRecovery.shouldRecover(journal, cleaning, false),
                "normal cleanup uses completeReadStop, not the game-gone recovery");

        StarHuntStatus older = StarHuntStatus.parse(
                "status=stopped\nreason=original_process_ended_cleanup_unconfirmed\n"
                        + "sessionDetached=true\nacceptedSequence=668\n");
        check(StarHuntStopRecovery.shouldRecover(journal, older, false),
                "a stop one sequence ahead of the last status still recovers");

        StarHuntStatus reading = StarHuntStatus.parse(
                "status=reading\nreason=original_process_ended_cleanup_unconfirmed\n"
                        + "sessionDetached=true\nacceptedSequence=669\n");
        check(!StarHuntStopRecovery.shouldRecover(journal, reading, false),
                "non-stopped status is not a process-gone cleanup");

        StarHuntStatus explicitGone = StarHuntStatus.parse(
                "status=stopped\ngameProcessGone=true\nacceptedSequence=669\n");
        check(StarHuntStopRecovery.shouldRecover(journal, explicitGone, false),
                "explicit gameProcessGone status recovers");

        System.out.println("StarHuntStopRecoverySelfTest PASS (" + checks + " assertions)");
    }

    private static void check(boolean condition, String message) {
        checks++;
        if (!condition) throw new AssertionError(message);
    }
}
