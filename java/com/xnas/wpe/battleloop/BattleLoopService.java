package com.xnas.wpe.battleloop;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Intent;
import android.os.Build;
import android.os.IBinder;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

public final class BattleLoopService extends com.xnas.wpe.suite.ModuleService {
    public static final String START = "start";
    public static final String STOP = "stop";
    public static final String CHECK = "check_old_task";

    private final ScheduledExecutorService timer = Executors.newSingleThreadScheduledExecutor();
    private volatile java.lang.Process worker;
    private volatile java.lang.Process recoveryChecker;
    private volatile boolean stopping;
    private volatile boolean desiredRunning;
    private volatile boolean stopPending;
    private volatile boolean recoveryPending;
    private volatile boolean recoveryAttempted;
    private static volatile boolean active;
    private static volatile boolean checkingOldTask;
    private volatile long lastLaunch;
    private volatile long generation;
    private volatile int latestStartId;
    private volatile long stopGeneration;
    private volatile int stopStartId;
    private volatile long recoveryToken;
    private volatile int recoveryStartId;
    private boolean monitorStarted;
    private File stop;

    public static boolean isActive() { return active; }
    public static boolean isCheckingOldTask() { return checkingOldTask; }

    @Override public void onCreate() {
        super.onCreate();
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel = new NotificationChannel(
                    "battle_loop", "战投跳过", NotificationManager.IMPORTANCE_LOW);
            getSystemService(NotificationManager.class).createNotificationChannel(channel);
        }
    }

    @Override public synchronized int onStartCommand(Intent intent, int flags, int startId) {
        latestStartId = startId;
        if (intent != null && CHECK.equals(intent.getAction())) {
            checkOldTask(startId);
            return START_NOT_STICKY;
        }
        if (intent != null && STOP.equals(intent.getAction())) {
            stopWorker(startId);
            return START_NOT_STICKY;
        }
        if (recoveryPending) return START_NOT_STICKY;
        if (desiredRunning && !stopping && !stopPending && running(worker)) return START_STICKY;
        if (!desiredRunning && !stopPending
                && !com.xnas.wpe.suite.TargetGame.supportsModule(
                        com.xnas.wpe.suite.TargetGame.BATTLE_LOOP,
                        com.xnas.wpe.suite.TargetGame.getSelectedPackage(this))) {
            writeStatus("state=stopped\nreason=unsupported_target");
            stopSelfResult(startId);
            return START_NOT_STICKY;
        }

        com.xnas.wpe.suite.BattleConflictGate.Decision gate =
                com.xnas.wpe.suite.BattleConflictGate.reserve(
                        getFilesDir().getParentFile(), "battleloop");
        if (!gate.allowed) {
            stopSelfResult(startId);
            return START_NOT_STICKY;
        }

        generation++;
        stopping = false;
        desiredRunning = true;
        recoveryAttempted = false;
        active = true;
        startForeground(6102, notification("正在启动并监控游戏进程"));
        stop = new File(getFilesDir(), "stop");
        if (!stopPending && (worker == null || !running(worker))) {
            stop.delete();
            startWorker();
        }
        ensureMonitor();
        return START_STICKY;
    }

    private synchronized void startWorker() {
        if (!desiredRunning || stopping || stopPending || recoveryPending) return;
        long now = System.currentTimeMillis();
        if (lastLaunch != 0 && now - lastLaunch < 10000) return;
        lastLaunch = now;
        try {
            File dir = getFilesDir();
            stop = new File(dir, "stop");
            stop.delete();
            File staleRecovery = new File(dir, "recovery-status");
            if (staleRecovery.exists() && !staleRecovery.delete()) {
                writeRecoveryStatus(
                        "state=unconfirmed\nreason=stale_status_remove_failed\nmessage=旧任务检查状态无法清除，未启动工作进程");
                desiredRunning = false;
                stopping = true;
                stopPending = false;
                active = false;
                stopForeground(true);
                stopSelfResult(latestStartId);
                return;
            }
            writeLease();
            String bin = new File(
                    getApplicationInfo().nativeLibraryDir, "libbattle_loop_worker.so").getAbsolutePath();
            String command = shellQuote(bin) + " --dir " + shellQuote(dir.getAbsolutePath());
            worker = new ProcessBuilder("su", "-c", command)
                    .redirectErrorStream(true)
                    .redirectOutput(new File(dir, "worker.log"))
                    .start();
        } catch (Exception error) {
            worker = null;
            desiredRunning = false;
            stopping = true;
            stopPending = false;
            active = false;
            writeStatus("state=error\nreason=worker_launch_failed");
            writeRecoveryStatus(
                    "state=unconfirmed\nreason=worker_launch_failed\nmessage=工作进程启动失败，旧任务保护仍保留");
            stopForeground(true);
            stopSelfResult(latestStartId);
        }
    }

    private synchronized void ensureMonitor() {
        if (monitorStarted) return;
        monitorStarted = true;
        timer.scheduleAtFixedRate(this::monitorTick, 0, 2, TimeUnit.SECONDS);
    }

    private synchronized void monitorTick() {
        if (desiredRunning && !stopping) writeLease();
        if (recoveryPending) {
            if (desiredRunning || stopPending) {
                getSystemService(NotificationManager.class).notify(
                        6102, notification(displayStatus(readStatus())));
            }
            return;
        }

        java.lang.Process currentWorker = worker;
        if (currentWorker != null && !running(currentWorker)) worker = null;
        if (stopPending && worker == null) {
            long expected = stopGeneration;
            if (desiredRunning && !stopping) {
                stopPending = false;
                stop.delete();
                startWorker();
            } else if (generation == expected && stopping) {
                finishStop(expected);
            }
        } else if (desiredRunning && !stopping && worker == null) {
            startWorker();
        }
        if (desiredRunning || stopPending) {
            getSystemService(NotificationManager.class).notify(
                    6102, notification(displayStatus(readStatus())));
        }
    }

    private synchronized void stopWorker(int startId) {
        if (recoveryPending) {
            if (!running(worker)) {
                stopping = true;
                desiredRunning = false;
                stopPending = true;
                stopGeneration = ++generation;
                stopStartId = startId;
                recoveryStartId = startId;
                return;
            }
            cancelRecoveryCheck();
        }
        if (stopping && stopPending) {
            stopStartId = startId;
            return;
        }

        stopping = true;
        desiredRunning = false;
        stopPending = true;
        recoveryAttempted = false;
        stopGeneration = ++generation;
        stopStartId = startId;
        java.lang.Process currentWorker = worker;
        if (currentWorker == null || !running(currentWorker)) {
            worker = null;
            active = false;
            recoveryAttempted = true;
            startOldTaskChecker(startId);
            return;
        }

        stop = new File(getFilesDir(), "stop");
        try (FileOutputStream output = new FileOutputStream(stop)) {
            output.write(1);
            output.getFD().sync();
        } catch (Exception error) {
            writeStatus("state=stopping\ndetail=停止请求写入失败");
        }
        new Thread(() -> {
            try {
                if (!currentWorker.waitFor(15, TimeUnit.SECONDS)) {
                    writeStatus("state=stopping\ndetail=等待游戏线程完成Lua清理");
                    return;
                }
                synchronized (BattleLoopService.this) {
                    if (generation == stopGeneration && stopping) {
                        worker = null;
                        finishStop(stopGeneration);
                    }
                }
            } catch (Exception ignored) { }
        }, "battle-loop-stop-wait").start();
        ensureMonitor();
    }

    private synchronized void finishStop(long expected) {
        if (generation != expected || !stopping || !stopPending || recoveryPending) return;
        File modulesRoot = getFilesDir().getParentFile();
        if (com.xnas.wpe.suite.BattleConflictGate.isClean(modulesRoot, "battleloop")) {
            active = false;
            if (com.xnas.wpe.suite.BattleConflictGate.release(modulesRoot, "battleloop")) {
                stopPending = false;
                new File(getFilesDir(), "gate-status").delete();
                stopForeground(true);
                stopSelfResult(stopStartId);
                return;
            }
        }
        if (!recoveryAttempted) {
            recoveryAttempted = true;
            startOldTaskChecker(stopStartId);
            return;
        }
        leaveProtectedAndStopped(stopStartId);
    }

    private synchronized void checkOldTask(int startId) {
        if (recoveryPending) {
            recoveryStartId = startId;
            return;
        }
        java.lang.Process currentWorker = worker;
        if (currentWorker != null && !running(currentWorker)) worker = null;
        else if (currentWorker != null) {
            writeRecoveryStatus(
                    "state=unconfirmed\nreason=worker_running\nmessage=任务仍在运行或Lua清理中，请停止并等待后再检查");
            getSystemService(NotificationManager.class).notify(
                    6102, notification(displayStatus(readStatus())));
            ensureMonitor();
            return;
        }
        if (!running(worker)) {
            desiredRunning = false;
            stopping = true;
            stopPending = true;
            stopGeneration = ++generation;
            stopStartId = startId;
            active = false;
        }
        recoveryAttempted = true;
        startOldTaskChecker(startId);
    }

    private synchronized void startOldTaskChecker(int startId) {
        if (recoveryPending) return;
        java.lang.Process currentWorker = worker;
        if (currentWorker != null && !running(currentWorker)) {
            worker = null;
            currentWorker = null;
        }
        if (currentWorker != null) {
            writeRecoveryStatus(
                    "state=unconfirmed\nreason=worker_running\nmessage=任务仍在运行或Lua清理中，请停止并等待后再检查");
            getSystemService(NotificationManager.class).notify(
                    6102, notification(displayStatus(readStatus())));
            ensureMonitor();
            return;
        }
        recoveryPending = true;
        recoveryStartId = startId;
        checkingOldTask = true;
        long token = ++recoveryToken;
        startForeground(6102, notification("正在只读核验旧任务状态"));
        writeRecoveryStatus(
                "state=checking\nmessage=正在只读检查旧任务进程身份");
        try {
            File dir = getFilesDir();
            String bin = new File(
                    getApplicationInfo().nativeLibraryDir, "libbattle_loop_worker.so").getAbsolutePath();
            String command = shellQuote(bin) + " --reconcile --dir " + shellQuote(dir.getAbsolutePath());
            java.lang.Process checker = new ProcessBuilder("su", "-c", command)
                    .redirectErrorStream(true)
                    .redirectOutput(new File(dir, "recovery.log"))
                    .start();
            recoveryChecker = checker;
            new Thread(() -> waitForRecoveryChecker(token, checker), "battle-loop-reconcile-wait").start();
        } catch (Exception error) {
            recoveryPending = false;
            checkingOldTask = false;
            recoveryChecker = null;
            writeRecoveryStatus(
                    "state=unconfirmed\nreason=checker_launch_failed\nmessage=无法启动旧任务检查，保护仍保留");
            finishRecoveryAttempt(token);
        }
    }

    private void waitForRecoveryChecker(long token, java.lang.Process checker) {
        boolean timedOut = false;
        try {
            if (!checker.waitFor(30, TimeUnit.SECONDS)) {
                timedOut = true;
                checker.destroy();
                checker.waitFor(1, TimeUnit.SECONDS);
            }
        } catch (Exception error) {
            timedOut = true;
            checker.destroy();
        }
        synchronized (this) {
            if (token != recoveryToken || checker != recoveryChecker) return;
            recoveryChecker = null;
            recoveryPending = false;
            checkingOldTask = false;
            if (timedOut) {
                writeRecoveryStatus(
                        "state=unconfirmed\nreason=checker_timeout\nmessage=旧任务检查超时，保护仍保留");
            } else {
                String recovery = readRecoveryStatus();
                if (recovery.isEmpty() || "checking".equals(value(recovery, "state"))) {
                    writeRecoveryStatus(
                            "state=unconfirmed\nreason=checker_incomplete\nmessage=旧任务检查未返回结果，保护仍保留");
                }
            }
            finishRecoveryAttempt(token);
        }
    }

    private synchronized void finishRecoveryAttempt(long token) {
        if (token != recoveryToken) return;
        boolean workerIsRunning = running(worker);
        if (workerIsRunning) {
            writeRecoveryStatus(
                    "state=unconfirmed\nreason=worker_running\nmessage=任务仍在运行或Lua清理中，请停止并等待后再检查");
            getSystemService(NotificationManager.class).notify(
                    6102, notification(displayStatus(readStatus())));
            ensureMonitor();
            return;
        }

        active = false;
        String recovery = readRecoveryStatus();
        boolean checked = "verified".equals(value(recovery, "state"));
        File modulesRoot = getFilesDir().getParentFile();
        if (checked
                && com.xnas.wpe.suite.BattleConflictGate.isClean(modulesRoot, "battleloop")
                && com.xnas.wpe.suite.BattleConflictGate.release(modulesRoot, "battleloop")) {
            stopPending = false;
            desiredRunning = false;
            stopping = true;
            new File(getFilesDir(), "gate-status").delete();
            stopForeground(true);
            stopSelfResult(recoveryStartId);
            return;
        }

        desiredRunning = false;
        stopping = true;
        stopPending = false;
        leaveProtectedAndStopped(recoveryStartId);
    }

    private synchronized void cancelRecoveryCheck() {
        recoveryToken++;
        java.lang.Process checker = recoveryChecker;
        recoveryChecker = null;
        recoveryPending = false;
        checkingOldTask = false;
        if (checker != null) checker.destroy();
        writeRecoveryStatus(
                "state=unconfirmed\nreason=checker_cancelled\nmessage=旧任务检查已取消，保护仍保留");
    }

    private void leaveProtectedAndStopped(int startId) {
        active = false;
        recoveryPending = false;
        checkingOldTask = false;
        stopForeground(true);
        stopSelfResult(startId);
    }

    private void writeLease() {
        try (FileOutputStream output = new FileOutputStream(new File(getFilesDir(), "lease"))) {
            output.write(Long.toString(System.currentTimeMillis() / 1000)
                    .getBytes(StandardCharsets.UTF_8));
            output.getFD().sync();
        } catch (Exception ignored) { }
    }

    private void writeStatus(String contents) {
        writeFile(new File(getFilesDir(), "status"), contents);
    }

    private void writeRecoveryStatus(String contents) {
        writeFile(new File(getFilesDir(), "recovery-status"), contents);
    }

    private void writeFile(File path, String contents) {
        try (FileOutputStream output = new FileOutputStream(path)) {
            output.write(contents.getBytes(StandardCharsets.UTF_8));
            output.getFD().sync();
        } catch (Exception ignored) { }
    }

    private String readStatus() { return readFile(new File(getFilesDir(), "status")); }
    private String readRecoveryStatus() { return readFile(new File(getFilesDir(), "recovery-status")); }

    private String readFile(File path) {
        try (FileInputStream input = new FileInputStream(path);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int count;
            while ((count = input.read(buffer)) > 0) output.write(buffer, 0, count);
            return new String(output.toByteArray(), StandardCharsets.UTF_8);
        } catch (Exception ignored) {
            return "";
        }
    }

    private static String value(String contents, String key) {
        for (String line : contents.split("\\n")) {
            if (line.startsWith(key + "=")) return line.substring(key.length() + 1);
        }
        return "";
    }

    private String displayStatus(String statusContents) {
        String recovery = readRecoveryStatus();
        String recoveryState = value(recovery, "state");
        if ("checking".equals(recoveryState)) return "正在只读核验旧任务状态";
        if ("waiting_restart".equals(recoveryState) || "unconfirmed".equals(recoveryState)
                || "verified".equals(recoveryState)) {
            String message = value(recovery, "message");
            if (!message.isEmpty()) return message;
        }

        String state = value(statusContents, "state");
        if ("waiting_game".equals(state)) return "等待游戏启动";
        if ("waiting_role".equals(state)) return "等待角色登录";
        if ("waiting_battle".equals(state)) return "等待战斗";
        if ("waiting_config".equals(state)) return "等待当前自动技能设置";
        if ("waiting_conflict".equals(state)) return "与其他战斗辅助冲突，已暂停";
        if ("running".equals(state)) return "战投跳过运行中";
        if ("stopped".equals(state)) return "已停止";
        if ("error".equals(state)) return "运行失败，请查看日志";
        if ("stopping".equals(state)) return "正在清理并恢复自动状态";
        return "正在监控游戏";
    }

    private Notification notification(String text) {
        Intent open = new Intent(this, com.xnas.wpe.suite.MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP)
                .putExtra(com.xnas.wpe.suite.MainActivity.EXTRA_OPEN_MODULE_ID, "battleloop");
        PendingIntent pending = PendingIntent.getActivity(
                this, 6102, open, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(this, "battle_loop")
                : new Notification.Builder(this);
        String display = text.length() > 100 ? text.substring(0, 100) : text;
        return builder.setContentTitle("战投跳过")
                .setContentText(display)
                .setSmallIcon(android.R.drawable.stat_notify_sync)
                .setContentIntent(pending)
                .setOngoing(true)
                .build();
    }

    private static String shellQuote(String value) {
        return "'" + value.replace("'", "'\\''") + "'";
    }

    private static boolean running(java.lang.Process process) {
        if (process == null) return false;
        try {
            process.exitValue();
            return false;
        } catch (IllegalThreadStateException error) {
            return true;
        }
    }

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override public void onDestroy() {
        desiredRunning = false;
        stopping = true;
        active = false;
        checkingOldTask = false;
        timer.shutdownNow();
        super.onDestroy();
    }
}
