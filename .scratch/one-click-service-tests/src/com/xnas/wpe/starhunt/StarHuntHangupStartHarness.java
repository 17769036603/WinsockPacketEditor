package com.xnas.wpe.starhunt;
import java.io.File;
import java.io.IOException;

final class StarHuntCommandFile {
    static StarHuntCommand current;
    static StarHuntCommand read(File ignored) throws IOException { return current; }
}

public final class StarHuntHangupStartHarness {
    private boolean active;
    private boolean actionSlotHeld;
    private boolean stopReadPending;
    private boolean stopReadRetryable;
    private boolean workerRunning;
    private boolean retryableJournal;
    private boolean scannerFails;
    private boolean hangupPending;
    private int latestStartId = 7;
    private int scansStarted;
    private Process worker;
    private StarHuntHangupRequest hangupRequest;
    private String message;
    private final File filesDir = new File("stub-files");

    private File getFilesDir() { return filesDir; }
    private boolean hasRetryableStop(File ignored) { return retryableJournal; }
    private boolean running(Process ignored) { return workerRunning; }
    private void setMessage(String value) { message = value; }
    private void notifyForeground() { }
    private String safeMessage(Exception error) { return error.getMessage(); }
    private void ensureMonitor() { }
    private void cancelHangupRequest() {
        if (hangupRequest != null) hangupRequest.cancel();
        hangupRequest = null;
        hangupPending = false;
    }
    private void startReading(int startId) {
        scansStarted++;
        cancelHangupRequest();
        if (scannerFails) return;
        active = true;
        workerRunning = true;
        long next = StarHuntCommandFile.current == null
                ? 1L : StarHuntCommandFile.current.sequence + 1L;
        StarHuntCommandFile.current = new StarHuntCommand(
                next, StarHuntCommand.Mode.SCAN, 0, 0);
    }
    private StarHuntCommand writeScanCommand() {
        long next = StarHuntCommandFile.current == null
                ? 1L : StarHuntCommandFile.current.sequence + 1L;
        StarHuntCommandFile.current = new StarHuntCommand(
                next, StarHuntCommand.Mode.SCAN, 0, 0);
        return StarHuntCommandFile.current;
    }
    private boolean writeLease() { return true; }
    private void requestReadStop(int startId) { }

    // Production method extracted verbatim by the PowerShell runner.
private synchronized void startHangup(int level) {
        if (level < 1 || level > 999) {
            setMessage("请选择 1 到 999 的星级");
            notifyForeground();
            return;
        }
        StarHuntCommand previous;
        try {
            previous = StarHuntCommandFile.read(getFilesDir());
        } catch (Exception error) {
            setMessage("command 文件无效，未启动挂机");
            notifyForeground();
            return;
        }
        boolean retryableStop = stopReadRetryable || hasRetryableStop(getFilesDir());
        if (stopReadPending || retryableStop) {
            stopReadRetryable = retryableStop;
            setMessage("上次停止读取未确认，请先重试停止");
            notifyForeground();
            return;
        }
        if (hangupRequest != null && hangupRequest.isPending()) {
            setMessage("挂机请求正在等待本次扫描，请勿重复点击");
            notifyForeground();
            return;
        }
        if (actionSlotHeld || (active && running(worker) && previous != null
                && previous.mode == StarHuntCommand.Mode.RUN)) {
            setMessage("找星动作正在运行，请先停止后再开始挂机");
            notifyForeground();
            return;
        }
        long previousSequence = previous == null ? 0L : previous.sequence;
        cancelHangupRequest();
        startReading(latestStartId);
        if (!active || stopReadPending || !running(worker) || actionSlotHeld) {
            setMessage("读取未能启动或动作仍未停止，未创建挂机请求");
            notifyForeground();
            return;
        }
        try {
            StarHuntCommand scan = StarHuntCommandFile.read(getFilesDir());
            if (scan == null || scan.mode != StarHuntCommand.Mode.SCAN
                    || scan.sequence <= previousSequence) {
                setMessage("本次扫描命令未确认写入，未创建挂机请求");
                notifyForeground();
                return;
            }
            hangupRequest = new StarHuntHangupRequest(level, scan.sequence, System.currentTimeMillis());
            hangupPending = true;
            setMessage("已启动读取并开始挂机扫描，等待当前地图和角色信息");
            ensureMonitor();
        } catch (Exception error) {
            cancelHangupRequest();
            setMessage("读取已启动，但挂机请求创建失败：" + safeMessage(error));
        }
        notifyForeground();
    }

    private void begin(int level) { startHangup(level); }
    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
    private static StarHuntHangupStartHarness fresh(StarHuntCommand command) {
        StarHuntCommandFile.current = command;
        return new StarHuntHangupStartHarness();
    }
    public static void main(String[] args) {
        StarHuntHangupStartHarness cold = fresh(
                new StarHuntCommand(40L, StarHuntCommand.Mode.RUN, 4, 310));
        cold.begin(4);
        check(cold.scansStarted == 1, "cold hangup must call the reader startup path once");
        check(cold.active && cold.workerRunning, "successful startup must establish active reader");
        check(cold.hangupRequest != null && cold.hangupPending,
                "pending is created only after the reader starts");
        check(cold.hangupRequest.expectedScanSequence == 41L,
                "pending binds to the new scan sequence, not the stale RUN");
        check(StarHuntCommandFile.current.mode == StarHuntCommand.Mode.SCAN,
                "the new command is SCAN even when an old inactive command was RUN");

        StarHuntHangupStartHarness failed = fresh(null);
        failed.scannerFails = true;
        failed.begin(4);
        check(failed.scansStarted == 1, "failed cold start still attempts reader startup");
        check(failed.hangupRequest == null && !failed.hangupPending,
                "failed reader startup must not create pending authorization");

        StarHuntHangupStartHarness duplicate = fresh(null);
        duplicate.begin(4);
        duplicate.begin(4);
        check(duplicate.scansStarted == 1,
                "repeated request while pending cannot restart the scan cycle");
        check(duplicate.hangupRequest.expectedScanSequence == 1L,
                "repeated request leaves the first sequence bound");

        StarHuntHangupStartHarness retryBlocked = fresh(null);
        retryBlocked.stopReadRetryable = true;
        retryBlocked.begin(4);
        check(retryBlocked.scansStarted == 0 && retryBlocked.hangupRequest == null,
                "retryable stop gate blocks startup and pending creation");
        StarHuntHangupStartHarness stopping = fresh(null);
        stopping.stopReadPending = true;
        stopping.begin(4);
        check(stopping.scansStarted == 0 && stopping.hangupRequest == null,
                "pending cleanup blocks startup and pending creation");
        StarHuntHangupStartHarness journalBlocked = fresh(null);
        journalBlocked.retryableJournal = true;
        journalBlocked.begin(4);
        check(journalBlocked.scansStarted == 0 && journalBlocked.hangupRequest == null,
                "persisted retryable stop gate blocks startup");
        StarHuntHangupStartHarness actionBlocked = fresh(null);
        actionBlocked.actionSlotHeld = true;
        actionBlocked.begin(4);
        check(actionBlocked.scansStarted == 0 && actionBlocked.hangupRequest == null,
                "held action slot blocks startup");
        StarHuntHangupStartHarness invalidLevel = fresh(null);
        invalidLevel.begin(1000);
        check(invalidLevel.scansStarted == 0 && invalidLevel.hangupRequest == null,
                "out-of-range level cannot start the reader or create pending");

        System.out.println("StarHuntHangupStartHarness PASS (14 assertions; extracted production entrypoint)");
    }
}