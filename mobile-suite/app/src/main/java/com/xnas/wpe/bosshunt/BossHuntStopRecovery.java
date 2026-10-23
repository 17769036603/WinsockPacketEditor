package com.xnas.wpe.bosshunt;

/**
 * Recovery policy for a BossHunt stop journal left behind after the bound game process ended.
 *
 * <p>The stop request can be written one sequence after the last acknowledged status and then
 * the worker can die with the emulator/game process. No worker exists to confirm cleanup, so
 * the journal must not keep the page auto-sending STOP_READ (which brings the game forward).
 * A live worker or a live game process always keeps the manual retry path.</p>
 */
public final class BossHuntStopRecovery {
    private BossHuntStopRecovery() {
    }

    public static boolean shouldRecover(BossHuntStopJournal journal, BossHuntStatus status,
                                        boolean workerRunning) {
        if (journal == null || journal.phase == BossHuntStopJournal.Phase.COMPLETE) return false;
        if (workerRunning || status == null) return false;
        return status.confirmsGameProcessGone();
    }
}
