import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

import com.xnas.wpe.battleskip.SkipStatusHarness;
import com.xnas.wpe.battleloop.LoopStatusHarness;

public final class BattleStatusDisplayHarness {
    private static int assertions;
    private static final java.util.List<String> failures = new java.util.ArrayList<String>();

    public static void main(String[] args) throws Exception {
        runningWorkerShowsRunning();
        unconfirmedPreviousTaskStillShowsUnconfirmed();
        loopPageShowsRunningSkipBlocker();
        loopPageShowsUnconfirmedSkipBlocker();
        loopPageStaysStoppedWithoutSkipEvidence();
        activeLoopPageKeepsOwnStatus();
        if (failures.isEmpty()) {
            System.out.println("BattleStatusDisplayHarness PASS (" + assertions
                    + " assertions; extracted battleskip.currentStatus/formatStatus and battleloop.currentStatus/display)");
        } else {
            System.out.println("BattleStatusDisplayHarness FAIL (" + failures.size() + " failures / " + assertions + " assertions)");
            for (String failure : failures) System.out.println("FAIL: " + failure);
            throw new AssertionError("battle status display regression assertions failed");
        }
    }

    private static void runningWorkerShowsRunning() throws Exception {
        File root = moduleRoot("running");
        File loop = module(root, "battleloop");
        File skip = module(root, "battleskip");
        write(loop, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        write(skip, "status", "running calls=63\n");
        write(skip, "recovery-status", "state=unconfirmed\nreason=worker_running\nrecoveryModule=battleskip\nmessage=工作进程仍持有运行锁，旧任务状态未确认\n");
        SkipStatusHarness.BattleSkipRecoveryService.checking = false;
        eq("state=running\n", SkipStatusHarness.currentStatus(skip), "running worker status is normalized, not unconfirmed");
        eq("状态：运行中", SkipStatusHarness.formatStatus(SkipStatusHarness.currentStatus(skip), false), "running page text");
    }

    private static void unconfirmedPreviousTaskStillShowsUnconfirmed() throws Exception {
        File root = moduleRoot("unconfirmed");
        File skip = module(root, "battleskip");
        write(skip, "status", "waiting_game count=2\n");
        write(skip, "recovery-status", "state=unconfirmed\nreason=worker_running\nrecoveryModule=battleskip\nmessage=工作进程仍持有运行锁，旧任务状态未确认\n");
        SkipStatusHarness.BattleSkipRecoveryService.checking = false;
        eq("无法确认上次任务是否退出，暂未启动。",
                SkipStatusHarness.formatStatus(SkipStatusHarness.currentStatus(skip), false),
                "non-running worker keeps the unconfirmed explanation");
        File bare = moduleRoot("bare-running");
        File bareSkip = module(bare, "battleskip");
        write(bareSkip, "status", "running\n");
        eq("状态：running", SkipStatusHarness.formatStatus(SkipStatusHarness.currentStatus(bareSkip), false),
                "bare running without a call count is not treated as running");
    }

    private static void loopPageShowsRunningSkipBlocker() throws Exception {
        File root = moduleRoot("loop-running-skip");
        File loop = module(root, "battleloop");
        File skip = module(root, "battleskip");
        write(loop, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        write(skip, "status", "running calls=63\n");
        LoopStatusHarness.BattleLoopService.active = false;
        String raw = LoopStatusHarness.currentStatus(loop);
        check(raw.startsWith("state=conflict_wait"), "loop page reports conflict_wait when skip runs: " + raw);
        String shown = LoopStatusHarness.display(raw, loop);
        check(shown.startsWith("状态：战斗快进正在运行"), "loop page names the running skip module: " + shown);
        check(shown.contains("请先到“战斗快进”页面停止快进"), "loop page tells the user to stop skip first: " + shown);
    }

    private static void loopPageShowsUnconfirmedSkipBlocker() throws Exception {
        File root = moduleRoot("loop-unconfirmed-skip");
        File loop = module(root, "battleloop");
        File skip = module(root, "battleskip");
        write(loop, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        write(skip, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=false\n");
        LoopStatusHarness.BattleLoopService.active = false;
        String raw = LoopStatusHarness.currentStatus(loop);
        check(raw.startsWith("state=conflict_wait"), "loop page reports conflict_wait for unconfirmed skip: " + raw);
        String shown = LoopStatusHarness.display(raw, loop);
        check(shown.contains("上次任务未确认退出"), "loop page keeps the unconfirmed wording: " + shown);
    }

    private static void loopPageStaysStoppedWithoutSkipEvidence() throws Exception {
        File root = moduleRoot("loop-clean");
        File loop = module(root, "battleloop");
        module(root, "battleskip");
        write(loop, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        LoopStatusHarness.BattleLoopService.active = false;
        eq("状态：已停止", LoopStatusHarness.display(LoopStatusHarness.currentStatus(loop), loop),
                "no skip evidence keeps the normal stopped text");
    }

    private static void activeLoopPageKeepsOwnStatus() throws Exception {
        File root = moduleRoot("loop-active");
        File loop = module(root, "battleloop");
        File skip = module(root, "battleskip");
        write(loop, "status", "state=running\n");
        write(skip, "status", "running calls=63\n");
        LoopStatusHarness.BattleLoopService.active = true;
        eq("状态：运行中", LoopStatusHarness.display(LoopStatusHarness.currentStatus(loop), loop),
                "active loop page is not overridden by the skip blocker");
        LoopStatusHarness.BattleLoopService.active = false;
    }

    private static File moduleRoot(String name) throws Exception {
        return Files.createTempDirectory("battle-status-" + name).toFile();
    }

    private static File module(File root, String id) {
        File directory = new File(root, id);
        directory.mkdirs();
        return directory;
    }

    private static void write(File directory, String name, String content) throws Exception {
        try (FileOutputStream output = new FileOutputStream(new File(directory, name))) {
            output.write(content.getBytes(StandardCharsets.UTF_8));
        }
    }

    private static void check(boolean ok, String message) {
        assertions++;
        if (!ok) failures.add(message);
    }

    private static void eq(Object expected, Object actual, String message) {
        check(expected == null ? actual == null : expected.equals(actual),
                message + " expected=" + expected + " actual=" + actual);
    }
}