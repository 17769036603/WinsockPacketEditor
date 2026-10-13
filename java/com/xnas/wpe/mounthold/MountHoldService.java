package com.xnas.wpe.mounthold;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.SystemClock;

import java.io.BufferedReader;
import java.io.File;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.UUID;

/** Android lifecycle bridge only. The Rust worker owns all preset decisions. */
public final class MountHoldService extends com.xnas.wpe.suite.ModuleService {
    public static final String ACTION_START = "com.xnas.wpe.mounthold.START";
    public static final String ACTION_STOP = "com.xnas.wpe.mounthold.STOP";
    public static final String EXTRA_PACKAGE = "game_package";
    public static final String EXTRA_SPEED = "target_speed";

    private static final String CHANNEL_ID = "mount_speed_hold";
    private static final int NOTIFICATION_ID = 7404;
    private static volatile boolean active;
    private static volatile String statusText = "尚未启动";

    private volatile Process worker;
    private volatile OutputStream workerInput;
    private volatile Thread workerThread;
    private volatile boolean stopping;
    private volatile boolean workerFailed;
    private volatile boolean foregroundStarted;
    private volatile boolean userStopRequested;
    private volatile boolean stopRecoveryAttempted;
    private volatile boolean blockedJournal;
    private volatile boolean destroyed;
    private volatile boolean preserveActiveAfterDestroy;
    private volatile boolean recoveringOldJournal;
    private volatile boolean oldJournalResolved;
    private volatile boolean sawTerminalState;
    private volatile boolean sawRecoveredState;
    private volatile boolean sawWorkerError;
    private volatile long retryDelaySeconds = 1L;
    private MountHoldRuntimeLog runtimeLog;
    private volatile String sessionId;
    private volatile String runId;
    private volatile long workerStartedAt;
    private String lastLoggedState;
    private String lastLoggedDetail;
    private long holdRepeatCount;
    private long lastHoldRepeatLogAt;
    private volatile String gamePackage;
    private static volatile String lastTargetPackage;
    private volatile String targetSpeed;
    private final Object workerLock = new Object();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    public static boolean isActive() { return active; }
    public static String getPackageForStop(String fallback) {
        String captured = lastTargetPackage;
        return captured == null || captured.isEmpty() ? fallback : captured;
    }
    public static String getStatusText() { return statusText; }

    private void ensureForeground(String message) {
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTIFICATION_ID, notification(message),
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(NOTIFICATION_ID, notification(message));
        }
        foregroundStarted = true;
    }

    @Override
    public void onCreate() {
        super.onCreate();
        runtimeLog = new MountHoldRuntimeLog(getFilesDir());
        sessionId = UUID.randomUUID().toString();
        log("service_created", null, null, null, null, null, null, null, null);
        createNotificationChannel();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && ACTION_STOP.equals(intent.getAction())) {
            if (intent.hasExtra(EXTRA_PACKAGE) && !active && !blockedJournal
                    && !(recoveringOldJournal && !oldJournalResolved)) gamePackage = intent.getStringExtra(EXTRA_PACKAGE);
            if (intent.hasExtra(EXTRA_SPEED) && !active) targetSpeed = intent.getStringExtra(EXTRA_SPEED);
            active = true;
            ensureSession();
            log("service_stop_requested", null, null, null, null, null, null, null, null);
            ensureForeground("正在安全恢复原值");
            setStatus("正在安全恢复原值");
            stopRecoveryAttempted = false;
            requestUserStop();
            return START_REDELIVER_INTENT;
        }
        mainHandler.removeCallbacks(retryWorkerAction);
        if (workerThread != null && workerThread.isAlive()) return START_REDELIVER_INTENT;

        gamePackage = intent == null ? null : intent.getStringExtra(EXTRA_PACKAGE);
        if (!com.xnas.wpe.suite.TargetGame.supportsModule(
                com.xnas.wpe.suite.TargetGame.MOUNT_HOLD, gamePackage)) {
            active = false;
            setStatus("目标游戏不支持");
            stopSelf(startId);
            return START_NOT_STICKY;
        }
        lastTargetPackage = gamePackage;
        targetSpeed = intent == null ? null : intent.getStringExtra(EXTRA_SPEED);
        log("service_start", null, null, null, null, gamePackage, targetSpeed, null, null);
        ensureForeground("正在启动 Root worker");
        active = true;
        stopping = false;
        userStopRequested = false;
        stopRecoveryAttempted = false;
        retryDelaySeconds = 1L;
        workerFailed = false;
        setStatus("正在启动 Root worker");
        launchWorker(false);
        return START_REDELIVER_INTENT;
    }

    private final Runnable retryWorkerAction = () -> launchWorker(false);

    private void launchWorker(boolean sendStop) {
        synchronized (workerLock) {
            if (destroyed) return;
            if (workerThread != null && workerThread.isAlive()) return;
            blockedJournal = false;
            recoveringOldJournal = false;
            oldJournalResolved = false;
            sawTerminalState = false;
            sawRecoveredState = false;
            sawWorkerError = false;
            stopping = sendStop || userStopRequested;
            runId = UUID.randomUUID().toString();
            final String workerSessionId = sessionId;
            final String workerRunId = runId;
            final long workerStartedAt = SystemClock.elapsedRealtime();
            this.workerStartedAt = workerStartedAt;
            Thread thread = new Thread(() -> runWorker(gamePackage, targetSpeed, sendStop,
                    workerSessionId, workerRunId, workerStartedAt),
                    sendStop ? "mount-hold-stop-recovery" : "mount-hold-worker");
            thread.setDaemon(true);
            workerThread = thread;
            lastLoggedState = null;
            lastLoggedDetail = null;
            holdRepeatCount = 0L;
            log(sendStop ? "stop_recovery_worker_launch" : "worker_launch",
                    runId, null, null, null, null, null, null, null);
            thread.start();
        }
    }

    private void launchStopRecoveryWorker() {
        synchronized (workerLock) {
            if (destroyed) return;
            if (stopRecoveryAttempted || (workerThread != null && workerThread.isAlive())) return;
            stopRecoveryAttempted = true;
            active = true;
            workerFailed = false;
            setStatus("正在尝试恢复保留状态");
            launchWorker(true);
        }
    }

    private void requestUserStop() {
        synchronized (workerLock) {
            userStopRequested = true;
            stopping = true;
            mainHandler.removeCallbacks(retryWorkerAction);
            requestWorkerStop();
            if (workerThread == null) launchStopRecoveryWorker();
        }
    }

    private void runWorker(String gamePackage, String targetSpeed, boolean sendStop,
                           String workerSessionId, String workerRunId, long workerStartedAt) {
        int exitCode = -1;
        String workerPath = new File(getApplicationInfo().nativeLibraryDir,
                "libmount_hold_worker.so").getAbsolutePath();
        String command = shellQuote(workerPath)
                + " --package " + shellQuote(gamePackage == null ? "" : gamePackage)
                + " --target-speed " + shellQuote(targetSpeed == null ? "" : targetSpeed)
                + " --state-dir " + shellQuote(getFilesDir().getAbsolutePath());
        try {
            File binary = new File(workerPath);
            if (!binary.isFile() || !binary.canExecute()) {
                workerFailed = true;
                log("worker_start_failure_binary_unavailable", workerRunId, 0L, "ERROR", "unknown_error", null, null, null, null);
                setStatus("Rust worker 不存在或不可执行");
                return;
            }
            Process process = new ProcessBuilder("su", "-c", command).start();
            worker = process;
            workerInput = process.getOutputStream();
            if (sendStop || stopping) requestWorkerStop();
            if (!sendStop) setStatus("正在请求 Root 并扫描目标游戏");
            Thread stderr = new Thread(() -> drainStderr(process), "mount-hold-stderr");
            stderr.setDaemon(true);
            stderr.start();

            try (BufferedReader stdout = new BufferedReader(
                    new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8))) {
                String line;
                while ((line = stdout.readLine()) != null) consumeWorkerLine(line);
            }
            exitCode = process.waitFor();
        } catch (InterruptedException error) {
            Thread.currentThread().interrupt();
            workerFailed = true;
            log("worker_start_failure_interrupted", workerRunId, workerElapsed(workerStartedAt), "ERROR", "unknown_error", null, null, null, null);
            setStatus("worker 服务线程被中断");
        } catch (IOException error) {
            workerFailed = true;
            log("worker_start_failure_io", workerRunId, workerElapsed(workerStartedAt), "ERROR", "unknown_error", null, null, null, null);
            setStatus("Root worker 启动失败：" + error.getClass().getSimpleName());
        } finally {
            flushHoldRepeat();
            worker = null;
            closeWorkerInput();
            active = false;
            if (exitCode != 0) workerFailed = true;
            final int result = exitCode;
            final Thread completedThread = Thread.currentThread();
            final Long elapsed = workerElapsed(workerStartedAt);
            mainHandler.post(() -> onWorkerExit(result, completedThread,
                    workerSessionId, workerRunId, elapsed));
        }
    }

    private void onWorkerExit(int exitCode, Thread completedThread, String workerSessionId,
                              String workerRunId, Long elapsedMs) {
        logForSession("worker_exit", workerSessionId, workerRunId, elapsedMs,
                null, null, null, null, exitCode, null);
        if (destroyed) {
            synchronized (workerLock) {
                if (workerThread == completedThread) workerThread = null;
            }
            active = (preserveActiveAfterDestroy && !sawRecoveredState)
                    || blockedJournal || (recoveringOldJournal && !oldJournalResolved);
            return;
        }
        synchronized (workerLock) {
            if (workerThread != completedThread) return;
            workerThread = null;
        }
        if (userStopRequested) {
            if (blockedJournal || (recoveringOldJournal && !oldJournalResolved)) {
                log("blocked", runId, elapsedWorkerMs(), "BLOCKED", "journal_retained", null, null, null, null);
                workerFailed = true;
                active = false;
                setStatus("恢复失败，保留恢复记录");
                return;
            }
            if (!stopRecoveryAttempted && (exitCode != 0 || !sawRecoveredState)) {
                launchStopRecoveryWorker();
                return;
            }
            if (stopRecoveryAttempted && !sawRecoveredState) {
                log("recovery_outcome", runId, elapsedWorkerMs(), "ERROR", "unknown_error", null, null, null, null);
                workerFailed = true;
                active = false;
                setStatus("恢复 worker 未能确认结果");
                return;
            }
            finishService();
            return;
        }
        if (blockedJournal || (recoveringOldJournal && !oldJournalResolved)) {
            log("blocked", runId, elapsedWorkerMs(), "BLOCKED", "journal_retained", null, null, null, null);
            workerFailed = true;
            active = false;
            setStatus("恢复失败，保留恢复记录");
            return;
        }
        if (sawWorkerError && !sawRecoveredState) {
            log("recovery_outcome", runId, elapsedWorkerMs(), "ERROR", "unknown_error", null, null, null, null);
            workerFailed = true;
            active = false;
            setStatus("worker 错误，未确认恢复，已暂停自动重试");
            return;
        }
        if (exitCode == 0 && !workerFailed && sawTerminalState) {
            finishService();
            return;
        }
        long delay = retryDelaySeconds;
        retryDelaySeconds = Math.min(30L, retryDelaySeconds * 2L);
        workerFailed = true;
        active = true;
        setStatus("worker 异常退出，" + delay + " 秒后重试");
        log("retry_schedule", runId, elapsedWorkerMs(), null, null, null, null, exitCode, delay);
        mainHandler.postDelayed(retryWorkerAction, delay * 1000L);
    }

    private void finishService() {
        log("service_stop", runId, elapsedWorkerMs(), null, null, null, null, null, null);
        active = false;
        if (Build.VERSION.SDK_INT >= 24) stopForeground(STOP_FOREGROUND_REMOVE);
        else stopForeground(true);
        foregroundStarted = false;
        lastTargetPackage = null;
        stopSelf();
    }

    private void consumeWorkerLine(String line) {
        if (line.startsWith("METRIC\t")) {
            consumeWorkerMetric(line);
            return;
        }
        if (!line.startsWith("STATE\t")) return;
        String[] fields = line.split("\t", 3);
        if (fields.length < 2) return;
        String detail = fields.length == 3 ? fields[2] : "";
        logState(fields[1], detail);
        String display;
        switch (fields[1]) {
            case "STARTING": display = "Rust worker 已启动"; break;
            case "LOCATING": display = "正在定位自己的角色"; break;
            case "ACTIVE":
                display = "角色速度保持中";
                if ("holding_speed".equals(detail)) {
                    retryDelaySeconds = 1L;
                    workerFailed = false;
                }
                break;
            case "STOPPING": display = "正在安全恢复原值"; sawTerminalState = true; break;
            case "RECOVERED": display = "stopped_without_changes".equals(detail)
                    ? "已停止，未修改速度" : "已停止并确认恢复";
                log("recovery_outcome", runId, elapsedWorkerMs(), "RECOVERED", detail,
                        null, null, null, null);
                sawTerminalState = true;
                sawRecoveredState = true;
                if ("old_journal_restored".equals(detail)) oldJournalResolved = true;
                break;
            case "ARCHIVED":
                display = "旧恢复记录已安全保存，正在查找游戏";
                oldJournalResolved = true;
                break;
            case "BLOCKED":
                log("blocked", runId, elapsedWorkerMs(), "BLOCKED", detail,
                        null, null, null, null);
                if ("journal_retained".equals(detail)) {
                    workerFailed = true;
                    blockedJournal = true;
                    display = "已停止，恢复记录保留";
                } else {
                    if ("recovering_old_journal".equals(detail)) recoveringOldJournal = true;
                    display = "正在核验旧恢复记录";
                }
                break;
            case "ERROR":
                workerFailed = true;
                sawWorkerError = true;
                display = "worker 报错";
                break;
            default: display = "worker 状态更新"; break;
        }
        if (!detail.isEmpty()) display += "：" + detail;
        setStatus(display);
    }

    private void consumeWorkerMetric(String line) {
        String[] fields = line.split("\t", -1);
        if (fields.length < 2) return;
        switch (fields[1]) {
            case "refresh_window": {
                if (fields.length != 12) return;
                Long[] values = new Long[10];
                for (int i = 0; i < values.length; i++) {
                    values[i] = metricNumber(fields[i + 2]);
                    if (values[i] == null) return;
                }
                runtimeLog.appendRefreshSummary(sessionId, runId, elapsedWorkerMs(),
                        values[0], values[1], values[2], values[3], values[4], values[5],
                        values[6], values[7], values[8], values[9]);
                break;
            }
            case "cache_fallback": {
                if (fields.length != 6) return;
                Long elapsedUs = metricNumber(fields[2]);
                Boolean found = metricFlag(fields[3]);
                String path = fields[4];
                String reason = fields[5];
                if (elapsedUs == null || found == null
                        || !MountHoldRuntimeLog.isAllowedFallbackPath(path)
                        || !MountHoldRuntimeLog.isAllowedFallbackReason(reason)) return;
                runtimeLog.appendRefreshMetric("cache_fallback", sessionId, runId,
                        elapsedWorkerMs(), elapsedUs, 0L, found, path, reason);
                break;
            }
            case "candidate_rebind": {
                if (fields.length != 5) return;
                Long elapsedMs = metricNumber(fields[2]);
                Long attempts = metricNumber(fields[3]);
                Boolean found = metricFlag(fields[4]);
                if (elapsedMs == null || attempts == null || found == null) return;
                runtimeLog.appendRefreshMetric("candidate_rebind", sessionId, runId,
                        elapsedWorkerMs(), elapsedMs, attempts, found, null, null);
                break;
            }
            default:
                break;
        }
    }

    private static Long metricNumber(String value) {
        try {
            long number = Long.parseLong(value);
            return number < 0L ? null : number;
        } catch (NumberFormatException error) {
            return null;
        }
    }

    private static Boolean metricFlag(String value) {
        if ("0".equals(value)) return false;
        if ("1".equals(value)) return true;
        return null;
    }

    private void ensureSession() {
        if (sessionId == null) sessionId = UUID.randomUUID().toString();
    }

    private Long elapsedWorkerMs() {
        return workerElapsed(workerStartedAt);
    }

    private Long workerElapsed(long started) {
        return started == 0L ? null : Math.max(0L, SystemClock.elapsedRealtime() - started);
    }

    private void logForSession(String event, String currentSessionId, String currentRunId,
                               Long elapsedMs, String state, String detail, String packageName,
                               String speed, Integer exitCode, Long retrySeconds) {
        runtimeLog.append(event, currentSessionId, currentRunId, elapsedMs, state, detail,
                exitCode, retrySeconds, packageName, speed, null);
    }

    private void logState(String state, String detail) {
        if ("LOCATING".equals(state) && "retrying_hold_write_verification".equals(detail)) {
            long now = SystemClock.elapsedRealtime();
            if (!state.equals(lastLoggedState) || !detail.equals(lastLoggedDetail)) {
                flushHoldRepeat();
                holdRepeatCount = 0L;
                log("state", runId, elapsedWorkerMs(), state, detail, null, null, null, null);
                lastHoldRepeatLogAt = now;
            } else {
                holdRepeatCount++;
                if (now - lastHoldRepeatLogAt >= 30_000L) {
                    logRepeatSummary();
                    lastHoldRepeatLogAt = now;
                }
            }
            lastLoggedState = state;
            lastLoggedDetail = detail;
            return;
        }
        if (!state.equals(lastLoggedState) || !detail.equals(lastLoggedDetail)) {
            flushHoldRepeat();
            log("state", runId, elapsedWorkerMs(), state, detail, null, null, null, null);
            lastLoggedState = state;
            lastLoggedDetail = detail;
        }
    }

    private void flushHoldRepeat() {
        if (holdRepeatCount > 0L) logRepeatSummary();
    }

    private void logRepeatSummary() {
        runtimeLog.append("state_repeat_summary", sessionId, runId, elapsedWorkerMs(), "LOCATING",
                "retrying_hold_write_verification", null, null, null, null, holdRepeatCount);
        holdRepeatCount = 0L;
    }

    private void log(String event, String currentRunId, Long elapsedMs, String state, String detail,
                     String packageName, String speed, Integer exitCode, Long retrySeconds) {
        ensureSession();
        runtimeLog.append(event, sessionId, currentRunId, elapsedMs, state, detail, exitCode,
                retrySeconds, packageName, speed, null);
    }

    private void drainStderr(Process process) {
        try (BufferedReader reader = new BufferedReader(
                new InputStreamReader(process.getErrorStream(), StandardCharsets.UTF_8))) {
            while (reader.readLine() != null) { /* STATE lines carry the sanitized diagnostic. */ }
        } catch (IOException ignored) {
        }
    }

    private void requestWorkerStop() {
        stopping = true;
        OutputStream input = workerInput;
        if (input == null) return;
        try {
            input.write("stop\n".getBytes(StandardCharsets.UTF_8));
            input.flush();
        } catch (IOException ignored) {
        }
    }

    private void closeWorkerInput() {
        OutputStream input = workerInput;
        workerInput = null;
        if (input != null) {
            try { input.close(); } catch (IOException ignored) { }
        }
    }

    private void setStatus(String text) {
        statusText = text;
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager != null && foregroundStarted) manager.notify(NOTIFICATION_ID, notification(text));
    }

    private Notification notification(String text) {
        Intent open = new Intent(this, com.xnas.wpe.suite.MainActivity.class).putExtra(com.xnas.wpe.suite.MainActivity.EXTRA_OPEN_MODULE_ID, "mounthold");
        PendingIntent pending = PendingIntent.getActivity(this, 7404, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(this, CHANNEL_ID)
                : new Notification.Builder(this);
        return builder.setSmallIcon(android.R.drawable.ic_lock_idle_lock)
                .setContentTitle("角色速度保持")
                .setContentText(text)
                .setContentIntent(pending)
                .addAction(android.R.drawable.ic_menu_close_clear_cancel,
                        active ? "停止并恢复" : "关闭提示",
                        PendingIntent.getService(this, 1,
                                new Intent(this, MountHoldService.class)
                                        .setAction(ACTION_STOP)
                                        .putExtra(EXTRA_PACKAGE, gamePackage)
                                        .putExtra(EXTRA_SPEED, targetSpeed),
                                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE))
                .setOngoing(true)
                .build();
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT < 26) return;
        NotificationChannel channel = new NotificationChannel(CHANNEL_ID,
                "角色速度保持", NotificationManager.IMPORTANCE_LOW);
        getSystemService(NotificationManager.class).createNotificationChannel(channel);
    }

    private static String shellQuote(String value) {
        return "'" + value.replace("'", "'\\''") + "'";
    }

    @Override
    public void onDestroy() {
        boolean cleanupUnconfirmed = ((active || stopping || workerFailed || userStopRequested)
                && !sawRecoveredState)
                || (workerThread != null && workerThread.isAlive()) || blockedJournal
                || (recoveringOldJournal && !oldJournalResolved);
        destroyed = true;
        preserveActiveAfterDestroy = cleanupUnconfirmed;
        log("service_destroyed", runId, elapsedWorkerMs(), null, null, null, null, null, null);
        requestWorkerStop();
        closeWorkerInput();
        active = cleanupUnconfirmed;
        mainHandler.removeCallbacksAndMessages(null);
        if (foregroundStarted) {
            if (Build.VERSION.SDK_INT >= 24) stopForeground(STOP_FOREGROUND_REMOVE);
            else stopForeground(true);
            foregroundStarted = false;
        }
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) { return null; }
}
