package com.xnas.wpe.bosshunt;

/** A game-process-gone BossHunt journal must not keep auto-sending STOP_READ. */
public final class BossHuntStopRecoverySelfTest {
    private static int checks;

    public static void main(String[] args) {
        BossHuntStopJournal journal = new BossHuntStopJournal(
                BossHuntStopJournal.Phase.PENDING, 34L, 1_800_000_000_000L, true);
        BossHuntStatus gameGone = BossHuntStatus.parse(
                "status=stopped\nreason=original_process_ended_cleanup_unconfirmed\n"
                        + "sessionDetached=true\nacceptedSequence=33\n"
                        + "hooksRemoved=false\nbindingCleared=false\n");
        check(BossHuntStopRecovery.shouldRecover(journal, gameGone, false),
                "pending journal recovers when the stop is one sequence ahead of the status");
        check(!BossHuntStopRecovery.shouldRecover(journal, gameGone, true),
                "a live worker always keeps the manual retry path");

        BossHuntStopJournal unconfirmed = journal.withPhase(BossHuntStopJournal.Phase.UNCONFIRMED, true);
        check(BossHuntStopRecovery.shouldRecover(unconfirmed, gameGone, false),
                "unconfirmed journal also recovers after the game process ended");

        BossHuntStopJournal complete = journal.withPhase(BossHuntStopJournal.Phase.COMPLETE, false);
        check(!BossHuntStopRecovery.shouldRecover(complete, gameGone, false),
                "completed journal is not a retry state");
        check(!BossHuntStopRecovery.shouldRecover(null, gameGone, false),
                "missing journal is not recoverable");
        check(!BossHuntStopRecovery.shouldRecover(journal, null, false),
                "missing status is not recoverable");

        BossHuntStatus cleaning = BossHuntStatus.parse(
                "status=stopped\nacceptedSequence=33\nhooksRemoved=true\nbindingCleared=true\n");
        check(!BossHuntStopRecovery.shouldRecover(journal, cleaning, false),
                "normal cleanup uses completeReadStop, not the game-gone recovery");

        BossHuntStatus reading = BossHuntStatus.parse(
                "status=reading\nreason=original_process_ended_cleanup_unconfirmed\n"
                        + "sessionDetached=true\nacceptedSequence=33\n");
        check(!BossHuntStopRecovery.shouldRecover(journal, reading, false),
                "non-stopped status is not a process-gone cleanup");

        BossHuntStatus explicitGone = BossHuntStatus.parse(
                "status=stopped\ngameProcessGone=true\nacceptedSequence=33\n");
        check(BossHuntStopRecovery.shouldRecover(journal, explicitGone, false),
                "explicit gameProcessGone status recovers");

        System.out.println("BossHuntStopRecoverySelfTest PASS (" + checks + " assertions)");
    }

    private static void check(boolean condition, String message) {
        checks++;
        if (!condition) throw new AssertionError(message);
    }
}
