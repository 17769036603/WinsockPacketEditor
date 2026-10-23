package com.xnas.wpe.anlei;

public final class AnleiStatusSelfTest {
    public static void main(String[] args) {
        check(WorkerStatus.isStopped("stopped"), "clean worker stop");
        check(WorkerStatus.isStopped("state=stopped\nreason=original_process_ended\nluaCleanupConfirmed=false\n"), "ended original process is terminal without inventing Lua cleanup");
        check(!WorkerStatus.isStopped("error stopped after fatal: business error"), "fatal exit must not be classified as a clean stop");
        check(!WorkerStatus.isStopped("stopping:cleanup_unconfirmed"), "pending cleanup remains unconfirmed");
        check(!WorkerStatus.isStopped("game_gone; detaching old Frida session"), "detach in progress is not terminal");
        check(WorkerStatus.isStopped("已停止（worker 未启动）"), "fresh no-worker stop");
        System.out.println("AnleiStatusSelfTest: PASS (6 cases)");
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
