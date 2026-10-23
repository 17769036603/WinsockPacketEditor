package com.xnas.wpe.starhunt;

/**
 * Recovery policy for a stop journal left behind after the bound game process ended.
 *
 * <p>An emulator or game restart can kill the worker after the stop request but before
 * any Lua cleanup. The stop command may be one sequence newer than the last status, so
 * the journal sequence cannot be required to match the status. Recovery is limited to
 * the case where the status explicitly reports the bound game process ended and no worker
 * is alive; a live worker or a live game process always keeps the manual retry path.</p>
 */
public final class StarHuntStopRecovery {
    private StarHuntStopRecovery() {
    }

    public static boolean shouldRecover(StarHuntStopJournal journal, StarHuntStatus status,
                                        boolean workerRunning) {
        if (journal == null || journal.phase == StarHuntStopJournal.Phase.COMPLETE) return false;
        if (workerRunning || status == null) return false;
        return status.confirmsGameProcessGone();
    }
}
