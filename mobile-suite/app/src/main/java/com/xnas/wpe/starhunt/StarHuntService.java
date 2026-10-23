package com.xnas.wpe.starhunt;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Build;
import android.os.IBinder;
import android.system.ErrnoException;
import android.system.Os;

import com.xnas.wpe.anlei.AnleiService;
import com.xnas.wpe.suite.LicenseManager;
import com.xnas.wpe.suite.ModuleService;
import com.xnas.wpe.suite.NativeWorkerCommand;
import com.xnas.wpe.suite.TargetGame;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/** Owns the read-only worker lifecycle; action changes are explicit command-file writes. */
public final class StarHuntService extends ModuleService {
    public static final String ACTION_START_READ = "com.xnas.wpe.starhunt.START_READ";
    public static final String ACTION_STOP_READ = "com.xnas.wpe.starhunt.STOP_READ";
    public static final String ACTION_START_RUN = "com.xnas.wpe.starhunt.START_RUN";
    public static final String ACTION_START_HANGUP = "com.xnas.wpe.starhunt.START_HANGUP";
    public static final String ACTION_STOP_RUN = "com.xnas.wpe.starhunt.STOP_RUN";
    /** 一次性传送：EXTRA_MAPS 里放目标地图 ID。 */
    public static final String ACTION_TELEPORT = "com.xnas.wpe.starhunt.TELEPORT";
    public static final String EXTRA_MAPS = "maps";
    public static final String EXTRA_LEVEL = "level";
    public static final String EXTRA_LEVELS = "levels";

    private static final String CHANNEL_ID = "starhunt_reader";
    private static final int NOTIFICATION_ID = 6110;
    private static final long MONITOR_INTERVAL_MS = 250L;
    private static final long REGULAR_MONITOR_INTERVAL_NANOS = TimeUnit.SECONDS.toNanos(1L);
    private static final long LOST_WORKER_STOP_TIMEOUT_MS = 15_000L;
    private static final String STOP_JOURNAL_FILE = "stop_read.state";
    private static final String STOP_JOURNAL_TEMP_FILE = "stop_read.state.tmp";

    private static volatile boolean active;
    private static volatile boolean actionSlotHeld;
    private static volatile boolean stopReadPending;
    private static volatile boolean stopReadRetryable;
    private static volatile boolean hangupPending;
    private static volatile String serviceMessage = "尚未开启读取";
    private static volatile Process worker;
    private volatile StarHuntHangupRequest hangupRequest;

    private final ScheduledExecutorService timer = Executors.newSingleThreadScheduledExecutor();
    private volatile boolean destroyed;
    private volatile boolean monitorStarted;
    private long lastMonitorAtNanos;
    private volatile long pendingActionStopSequence;
    private volatile long pendingReadStopSequence;
    private volatile int pendingTeleportMap;
    private volatile boolean teleportSession;
    private volatile long teleportSequence;
    private volatile long teleportStartedAtMillis;
    private volatile long waitingSinceMillis;
    private volatile boolean rotating;
    private volatile int rotateTarget;
    private volatile long rotateStartedAtMillis;
    private volatile long rotateSequence;
    private volatile int cycleCursor;
    private volatile int cycleEmptyHops;
    private volatile long cycleCooldownUntilMillis;
    private volatile long waitingRoleSinceMillis;
    private volatile boolean waitingRoleRetried;
    private volatile long recoverPausedAtMillis;
    private volatile long rotateSettledAtMillis;
    private volatile long unknownSinceMillis;
    private volatile long readingSinceMillis;
    private volatile long stopAttemptStartedAtMillis;
    private volatile int latestStartId;

    public static boolean isActive() {
        return active;
    }

    public static boolean isActionActive() {
        return actionSlotHeld;
    }

    public static boolean isHangupPending() {
        return hangupPending;
    }

    public static boolean isStopReadPending() {
        return stopReadPending;
    }

    public static boolean hasRetryableStop(File moduleDirectory) {
        if (stopReadRetryable) return true;
        File journal = new File(moduleDirectory, STOP_JOURNAL_FILE);
        if (!journal.isFile()) return false;
        try (FileInputStream input = new FileInputStream(journal);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[1024];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            StarHuntStopJournal state = StarHuntStopJournal.parse(
                    new String(output.toByteArray(), StandardCharsets.UTF_8));
            if (state.phase == StarHuntStopJournal.Phase.COMPLETE) return false;
            // A journal whose game process ended and whose worker is gone can never be
            // confirmed; do not keep the page on "retry stop" for it.
            if (StarHuntStopRecovery.shouldRecover(
                    state, readStatusFile(moduleDirectory), running(worker))) return false;
            return true;
        } catch (Exception ignored) {
            // A malformed journal must leave an explicit retry path rather than hide the stop.
            return true;
        }
    }

    private static StarHuntStatus readStatusFile(File moduleDirectory) {
        File file = new File(moduleDirectory, "status");
        if (!file.isFile()) return StarHuntStatus.parse("");
        try (FileInputStream input = new FileInputStream(file);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[1024];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            return StarHuntStatus.parse(new String(output.toByteArray(), StandardCharsets.UTF_8));
        } catch (Exception ignored) {
            return StarHuntStatus.parse("");
        }
    }

    /**
     * A stop journal can never be confirmed after the bound game process ended and the
     * worker is gone. Clear it so a restart does not leave the page stuck on "retry stop".
     */
    private synchronized boolean recoverStopJournalIfProcessGone() {
        final StarHuntStopJournal journal;
        try {
            journal = readStopJournal();
        } catch (Exception error) {
            return false;
        }
        if (!StarHuntStopRecovery.shouldRecover(journal, readStatus(), running(worker))) return false;
        deleteIfPresent(new File(getFilesDir(), STOP_JOURNAL_FILE));
        deleteIfPresent(new File(getFilesDir(), "stop"));
        stopReadPending = false;
        stopReadRetryable = false;
        active = false;
        pendingReadStopSequence = 0L;
        pendingActionStopSequence = 0L;
        if (actionSlotHeld) releaseActionSlot();
        setMessage("原游戏进程已结束，已自动完成停止并释放动作位，可重新读取");
        notifyForeground();
        return true;
    }

    private synchronized void restoreStopJournal() {
        File file = new File(getFilesDir(), STOP_JOURNAL_FILE);
        if (!file.isFile()) return;

        final StarHuntStopJournal state;
        try {
            state = readStopJournal();
        } catch (Exception error) {
            stopReadPending = false;
            active = false;
            stopReadRetryable = true;
            pendingReadStopSequence = 0L;
            pendingActionStopSequence = 0L;
            if (!actionSlotHeld) {
                AnleiService.tryAcquireStarHuntAction();
                actionSlotHeld = true;
            }
            setMessage("上次停止记录无法确认，停止状态未确认，可重试");
            return;
        }
        if (state == null) return;
        if (state.phase == StarHuntStopJournal.Phase.COMPLETE) {
            deleteIfPresent(file);
            stopReadRetryable = false;
            return;
        }
        if (recoverStopJournalIfProcessGone()) return;

        if (state.actionSlotHeld && !actionSlotHeld) {
            AnleiService.tryAcquireStarHuntAction();
            // Keep the StarHunt ownership marker even if another action currently holds the gate.
            actionSlotHeld = true;
        }
        pendingReadStopSequence = state.sequence;
        stopAttemptStartedAtMillis = state.requestedAtMillis;
        if (state.phase == StarHuntStopJournal.Phase.PENDING) {
            stopReadPending = true;
            stopReadRetryable = false;
            active = true;
            setMessage("已恢复停止读取请求，等待 worker 确认清理");
            ensureMonitor();
        } else {
            stopReadPending = false;
            stopReadRetryable = true;
            active = false;
            pendingActionStopSequence = 0L;
            setMessage("上次停止读取未确认，可重试停止");
        }
    }

    private StarHuntStopJournal readStopJournal() throws java.io.IOException {
        File file = new File(getFilesDir(), STOP_JOURNAL_FILE);
        if (!file.isFile()) return null;
        try (FileInputStream input = new FileInputStream(file);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[1024];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            return StarHuntStopJournal.parse(new String(output.toByteArray(), StandardCharsets.UTF_8));
        }
    }

    private void writeStopJournal(StarHuntStopJournal state) throws java.io.IOException {
        File directory = getFilesDir();
        if (!directory.isDirectory() && !directory.mkdirs()) {
            throw new java.io.IOException("无法创建停止状态目录");
        }
        File temporary = new File(directory, STOP_JOURNAL_TEMP_FILE);
        File destination = new File(directory, STOP_JOURNAL_FILE);
        boolean renamed = false;
        try {
            try (FileOutputStream output = new FileOutputStream(temporary, false)) {
                output.write(state.toFileContents().getBytes(StandardCharsets.UTF_8));
                output.getFD().sync();
            }
            Os.rename(temporary.getAbsolutePath(), destination.getAbsolutePath());
            renamed = true;
        } catch (ErrnoException error) {
            throw new java.io.IOException("停止状态保存失败", error);
        } finally {
            if (!renamed) deleteIfPresent(temporary);
        }
    }

    public static String getServiceMessage() {
        return serviceMessage;
    }

    @Override public void onCreate() {
        super.onCreate();
        com.xnas.wpe.suite.GameMapCatalog.load(this);
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID, "自动找星读取", NotificationManager.IMPORTANCE_LOW);
            getSystemService(NotificationManager.class).createNotificationChannel(channel);
        }
    }

    @Override public synchronized int onStartCommand(Intent intent, int flags, int startId) {
        latestStartId = startId;
        String action = intent == null ? "" : intent.getAction();
        startForeground(NOTIFICATION_ID, notification(requestText(action)));
        restoreStopJournal();

        if (ACTION_STOP_RUN.equals(action) || ACTION_STOP_READ.equals(action)) com.xnas.wpe.suite.GameForeground.bringToFront(this);
        if (ACTION_STOP_RUN.equals(action)) {
            requestActionStop();
            return START_NOT_STICKY;
        }
        if (ACTION_STOP_READ.equals(action)) {
            requestReadStop(startId);
            return START_NOT_STICKY;
        }
        if (!ACTION_START_READ.equals(action) && !ACTION_START_RUN.equals(action)
                && !ACTION_START_HANGUP.equals(action) && !ACTION_TELEPORT.equals(action)) {
            setMessage("等待明确的读取操作");
            stopForeground(true);
            stopSelfResult(startId);
            return START_NOT_STICKY;
        }
        if (ACTION_START_RUN.equals(action)) cancelHangupRequest();
        if (!LicenseManager.isActive()) {
            setMessage("在线授权未生效，请先完成激活");
            stopForeground(true);
            stopSelfResult(startId);
            return START_NOT_STICKY;
        }
        if (!TargetGame.supportsModule(TargetGame.STARHUNT, TargetGame.getSelectedPackage(this))) {
            setMessage("自动找星仅支持 DHXY");
            stopForeground(true);
            stopSelfResult(startId);
            return START_NOT_STICKY;
        }

        if (ACTION_TELEPORT.equals(action)) {
            int map = 0;
            try {
                map = Integer.parseInt(intent == null ? "" : intent.getStringExtra(EXTRA_MAPS));
            } catch (RuntimeException ignored) {
                // 参数缺失按未指定处理。
            }
            if (map <= 0) {
                setMessage("缺少目标地图，未开始传送");
                notifyForeground();
                return START_NOT_STICKY;
            }
            startTeleport(map, startId);
        } else if (ACTION_START_READ.equals(action)) {
            startReading(startId);
        } else if (ACTION_START_HANGUP.equals(action)) {
            if (intent != null && intent.hasExtra(EXTRA_LEVELS)) {
                try {
                    startHangup(StarHuntSelection.parseCsv(intent.getStringExtra(EXTRA_LEVELS)));
                } catch (IllegalArgumentException error) {
                    setMessage("星级选择无效，未启动挂机");
                    notifyForeground();
                }
            } else {
                startHangup(intent == null ? 0 : intent.getIntExtra(EXTRA_LEVEL, 0));
            }
        } else {
            cancelHangupRequest();
            if (intent != null && intent.hasExtra(EXTRA_LEVELS)) {
                try {
                    startAction(StarHuntSelection.parseCsv(intent.getStringExtra(EXTRA_LEVELS)),
                            false, 0L);
                } catch (IllegalArgumentException error) {
                    setMessage("星级选择无效，未启动找星动作");
                    notifyForeground();
                }
            } else {
                startAction(intent == null ? 0 : intent.getIntExtra(EXTRA_LEVEL, 0));
            }
        }
        return START_NOT_STICKY;
    }

    private synchronized void startTeleport(int map, int startId) {
        if (!com.xnas.wpe.suite.GameMapCatalog.contains(map)) {
            setMessage("目标地图不在已加载清单中，未开始传送");
            notifyForeground();
            return;
        }
        if (active || running(worker) || actionSlotHeld || stopReadPending
                || stopReadRetryable || hasRetryableStop(getFilesDir())) {
            setMessage("请先停止当前任务并确认清理，再开始传送");
            notifyForeground();
            return;
        }
        if (!AnleiService.tryAcquireStarHuntAction()) {
            setMessage("其他移动任务尚未停止，未开始传送");
            notifyForeground();
            return;
        }
        actionSlotHeld = true;
        pendingTeleportMap = map;
        try {
            startReading(startId);
        } finally {
            // An early read-start rejection must not leave a queued teleport for a later scan.
            if (pendingTeleportMap > 0) {
                pendingTeleportMap = 0;
                releaseActionSlot();
            }
        }
    }

    private synchronized void startReading(int startId) {
        cancelHangupRequest();
        recoverStopJournalIfProcessGone();
        if (stopReadPending) {
            setMessage("正在等待 worker 确认清理，暂不能重新读取");
            notifyForeground();
            return;
        }
        if (stopReadRetryable || hasRetryableStop(getFilesDir())) {
            stopReadRetryable = true;
            setMessage("上次停止读取未确认，请先重试停止");
            notifyForeground();
            return;
        }
        Process current = worker;
        if (active && running(current)) {
            try {
                StarHuntCommand previous = StarHuntCommandFile.read(getFilesDir());
                StarHuntCommand next;
                if (pendingTeleportMap > 0) {
                    int target = pendingTeleportMap;
                    next = StarHuntCommandFile.writeNext(getFilesDir(), StarHuntCommand.Mode.TELEPORT,
                            StarHuntSelection.empty(), target);
                    pendingTeleportMap = 0;
                    teleportSession = true;
                    teleportSequence = next.sequence;
                    teleportStartedAtMillis = System.currentTimeMillis();
                    actionSlotHeld = true;
                    pendingActionStopSequence = 0L;
                    setMessage("正在传送到地图 " + target);
                    if (!writeLease()) {
                        requestReadStop(latestStartId);
                        return;
                    }
                    ensureMonitor();
                    notifyForeground();
                    return;
                }
                next = writeScanCommand();
                if (actionSlotHeld || (previous != null && previous.mode == StarHuntCommand.Mode.RUN)) {
                    actionSlotHeld = true;
                    pendingActionStopSequence = next.sequence;
                    setMessage("正在停止找星，扫描继续");
                } else {
                    setMessage("读取已开启，正在扫描当前地图");
                }
                if (!writeLease()) {
                    requestReadStop(latestStartId);
                    return;
                }
                ensureMonitor();
            } catch (Exception error) {
                setMessage("扫描命令写入失败，动作状态未确认");
            }
            notifyForeground();
            return;
        }

        StarHuntStatus oldStatus = readStatus();
        if (active && !oldStatus.permitsFreshRead()) {
            setMessage("上次 worker 的清理状态未确认，未自动重新启动");
            notifyForeground();
            return;
        }
        if (hasPriorWorkerEvidence() && !oldStatus.permitsFreshRead()) {
            active = true;
            setMessage("上次 worker 清理未确认，请检查状态后再启动读取");
            ensureMonitor();
            notifyForeground();
            return;
        }

        try {
            File directory = getFilesDir();
            if (!directory.isDirectory() && !directory.mkdirs()) {
                throw new IllegalStateException("无法创建自动找星模块目录");
            }
            deleteIfPresent(new File(directory, "stop"));
            StarHuntCommand initial;
            if (pendingTeleportMap > 0) {
                int target = pendingTeleportMap;
                initial = StarHuntCommandFile.writeNext(directory, StarHuntCommand.Mode.TELEPORT,
                        StarHuntSelection.empty(), target);
                setMessage("正在传送到地图 " + target);
            } else {
                initial = writeScanCommand();
            }
            teleportSession = pendingTeleportMap > 0;
            teleportSequence = teleportSession ? initial.sequence : 0L;
            teleportStartedAtMillis = System.currentTimeMillis();
            pendingTeleportMap = 0;
            pendingReadStopSequence = 0L;
            pendingActionStopSequence = 0L;
            stopReadPending = false;
            if (actionSlotHeld && !teleportSession) {
                // A terminal cleanup or detached, ended process permits a new scan only.
                releaseActionSlot();
            }
            if (!writeLease()) throw new java.io.IOException("运行租约写入失败");
            launchWorker(directory);
            active = true;
            setMessage(teleportSession ? "传送任务已启动，等待地图确认" : "只读扫描已启动，等待游戏数据");
            ensureMonitor();
        } catch (Exception error) {
            active = false;
            if (teleportSession) {
                requestReadStop(startId);
                return;
            }
            setMessage("worker 启动失败：" + safeMessage(error));
            stopForeground(true);
            stopSelfResult(startId);
        }
        notifyForeground();
    }

    /** Compatibility entry point for callers that still send one level. */
    private synchronized void startHangup(int level) {
        if (level < 1 || level > 999) {
            setMessage("请选择 1 到 999 的星级");
            notifyForeground();
            return;
        }
        startHangup(StarHuntSelection.of(level));
    }

    private synchronized void startHangup(StarHuntSelection levels) {
        if (levels == null || levels.isEmpty()) {
            setMessage("请至少选择一个星级");
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
            setMessage("未创建挂机请求：" + serviceMessage);
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
            hangupRequest = new StarHuntHangupRequest(levels, scan.sequence, System.currentTimeMillis());
            hangupPending = true;
            setMessage("已启动读取并开始挂机扫描，等待当前地图和角色信息");
            ensureMonitor();
        } catch (Exception error) {
            cancelHangupRequest();
            setMessage("读取已启动，但挂机请求创建失败：" + safeMessage(error));
        }
        notifyForeground();
    }

    private synchronized boolean startAction(int level) {
        if (level < 1 || level > 999) {
            setMessage("请选择 1 到 999 的星级");
            notifyForeground();
            return false;
        }
        return startAction(StarHuntSelection.of(level), false, 0L);
    }

    /** Compatibility overload for legacy one-level service calls. */
    private synchronized boolean startAction(int level, boolean allowEmpty, long expectedScanSequence) {
        if (level < 1 || level > 999) return false;
        return startAction(StarHuntSelection.of(level), allowEmpty, expectedScanSequence);
    }

    private synchronized boolean startAction(StarHuntSelection levels, boolean allowEmpty,
                                             long expectedScanSequence) {
        if (levels == null || levels.isEmpty()) {
            setMessage("请至少选择一个星级");
            notifyForeground();
            return false;
        }
        if (!active || stopReadPending || !running(worker)) {
            setMessage("请先开启读取并等待星级数据");
            notifyForeground();
            return false;
        }
        if (!LicenseManager.isActive()) {
            cancelHangupRequest();
            requestReadStop(latestStartId);
            return false;
        }
        StarHuntStatus status = readStatus();
        StarHuntCommand previous;
        try {
            previous = StarHuntCommandFile.read(getFilesDir());
        } catch (Exception error) {
            setMessage("command 文件无效，未启动找星动作");
            notifyForeground();
            return false;
        }
        if (previous == null || status.acceptedSequence < previous.sequence
                || (expectedScanSequence > 0L && (previous.sequence != expectedScanSequence
                || previous.mode != StarHuntCommand.Mode.SCAN
                || status.acceptedSequence != expectedScanSequence))) {
            setMessage("正在等待本次扫描命令被 worker 确认");
            notifyForeground();
            return false;
        }
        long now = System.currentTimeMillis() / 1000L;
        if (!(allowEmpty ? status.canStartHangup(levels, now) : status.canStartAction(levels, now))) {
            setMessage("当前星级或地图数据未确认，请等待扫描更新");
            notifyForeground();
            return false;
        }
        if ("teleport_wait".equals(status.state) || "challenge_wait".equals(status.state)
                || "battle".equals(status.state) || "cooldown".equals(status.state)) {
            setMessage("找星动作仍在进行或等待冷却，请先停止找星");
            notifyForeground();
            return false;
        }
        boolean alreadyHeld = actionSlotHeld;
        if (!alreadyHeld && !AnleiService.tryAcquireStarHuntAction()) {
            setMessage("其他移动功能正在运行动作，请先停止对应功能");
            notifyForeground();
            return false;
        }
        try {
            StarHuntCommand command = StarHuntCommandFile.writeNext(getFilesDir(),
                    StarHuntCommand.Mode.RUN, levels, status.map);
            actionSlotHeld = true;
            pendingActionStopSequence = 0L;
            if (!writeLease()) {
                requestActionStop();
                return false;
            }
            setMessage("已提交找星动作，等待 worker 确认");
            recordSelectedLevels(levels);
            notifyForeground();
            return true;
        } catch (Exception error) {
            if (!alreadyHeld) AnleiService.releaseStarHuntAction();
            actionSlotHeld = alreadyHeld;
            setMessage("找星命令写入失败，动作未确认");
        }
        notifyForeground();
        return false;
    }

    private synchronized void requestActionStop() {
        cancelHangupRequest();
        if (!active || !running(worker) || stopReadPending) {
            setMessage("读取尚未运行，无法停止找星动作");
            if (!active) {
                stopForeground(true);
                stopSelfResult(latestStartId);
            }
            notifyForeground();
            return;
        }
        try {
            StarHuntCommand command = writeScanCommand();
            pendingActionStopSequence = command.sequence;
            setMessage("正在停止找星，等待 worker 确认；只读扫描会继续");
            writeLease();
        } catch (Exception error) {
            setMessage("停止命令写入失败，动作状态仍未确认");
        }
        notifyForeground();
    }

    private synchronized void requestReadStop(int startId) {
        cancelHangupRequest();
        latestStartId = startId;
        Process current = worker;
        if (!running(current)) {
            StarHuntStatus terminal = readStatus();
            if (terminal.confirmsCleanup() && terminal.acceptedSequence > 0L) {
                pendingReadStopSequence = terminal.acceptedSequence;
                StarHuntStopJournal earlier = null;
                try {
                    earlier = readStopJournal();
                } catch (Exception ignored) {
                    // The terminal status itself carries the sequence used below.
                }
                stopAttemptStartedAtMillis = earlier == null
                        ? System.currentTimeMillis() : earlier.requestedAtMillis;
                stopReadPending = true;
                active = true;
                completeReadStop(terminal, -1);
                return;
            }
        }
        if (!active && !running(current) && !stopReadPending && !stopReadRetryable
                && !hasRetryableStop(getFilesDir())) {
            setMessage("读取未运行");
            stopForeground(true);
            stopSelfResult(startId);
            return;
        }
        if (stopReadPending) {
            if (current != null && running(current)) {
                setMessage("正在等待 worker 确认清理");
                notifyForeground();
                return;
            }
            if (!stopAttemptExpired()) {
                setMessage("正在等待 worker 确认清理");
                ensureMonitor();
                notifyForeground();
                return;
            }
            boolean gameGone = readStatus().confirmsGameProcessGoneAfter(
                    stopAttemptStartedAtMillis);
            markReadStopUnconfirmed(gameGone
                    ? "游戏进程已结束，但 worker 清理未确认；已释放动作占用，可重试"
                    : "worker 状态未确认，已开放重试",
                    gameGone);
        }
        boolean journalWritten = false;
        try {
            long sequence;
            if (running(current)) {
                sequence = writeScanCommand().sequence;
            } else {
                StarHuntStopJournal previous = readStopJournal();
                if (previous != null && previous.phase != StarHuntStopJournal.Phase.COMPLETE) {
                    sequence = previous.sequence;
                } else {
                    StarHuntCommand previousCommand = StarHuntCommandFile.read(getFilesDir());
                    sequence = previousCommand == null
                            ? readStatus().acceptedSequence : previousCommand.sequence;
                }
                if (sequence <= 0L) {
                    throw new IllegalStateException("没有可复用的停止命令序号");
                }
            }
            long requestedAt = System.currentTimeMillis();
            writeStopJournal(new StarHuntStopJournal(StarHuntStopJournal.Phase.PENDING,
                    sequence, requestedAt, actionSlotHeld));
            journalWritten = true;
            pendingReadStopSequence = sequence;
            stopAttemptStartedAtMillis = requestedAt;
            pendingActionStopSequence = actionSlotHeld ? sequence : 0L;
            writeStopRequest();
            active = true;
            stopReadPending = true;
            stopReadRetryable = false;
            setMessage("正在停止读取，等待 worker 清理确认");
            ensureMonitor();
        } catch (Exception error) {
            if (journalWritten) {
                markReadStopUnconfirmed("停止请求未能完整写入，状态未确认，可重试", false);
            } else {
                stopReadRetryable = true;
                if (!running(current)) {
                    active = false;
                    stopReadPending = false;
                }
                setMessage("停止读取请求未写入，状态未确认，可重试");
                if (!active) stopAfterUnconfirmed();
            }
        }
        notifyForeground();
    }

    private StarHuntCommand writeScanCommand() throws java.io.IOException {
        StarHuntStatus status = readStatus();
        return StarHuntCommandFile.writeNext(getFilesDir(), StarHuntCommand.Mode.SCAN,
                0, Math.max(status.map, 0));
    }

    private void launchWorker(File directory) throws Exception {
        String binary = new File(getApplicationInfo().nativeLibraryDir,
                "libstarhunt_worker.so").getAbsolutePath();
        String command = NativeWorkerCommand.withLibraryPath(getApplicationInfo().nativeLibraryDir,
                NativeWorkerCommand.shellQuote(binary) + " --dir "
                        + NativeWorkerCommand.shellQuote(directory.getAbsolutePath()));
        worker = new ProcessBuilder("su", "-c", command)
                .redirectErrorStream(true)
                .redirectOutput(new File(directory, "worker.log"))
                .start();
    }

    private synchronized void ensureMonitor() {
        if (monitorStarted || destroyed) return;
        monitorStarted = true;
        timer.scheduleAtFixedRate(this::monitorTick, 0L, MONITOR_INTERVAL_MS,
                TimeUnit.MILLISECONDS);
    }

    /** 多图巡回：当前地图完全没有候选 1.5 秒、有候选但都忙 4 秒就换下一张，到达后 0.8 秒重新下发运行命令。 */
    private static final long CYCLE_EMPTY_IDLE_MILLIS = 1500L;
    private static final long CYCLE_IDLE_MILLIS = 4000L;
    private static final long CYCLE_ARRIVE_MILLIS = 10_000L;
    private static final long CYCLE_SETTLE_MILLIS = 800L;
    /** 一圈地图都没有目标时原地等这么久，避免每几秒传送一次的来回跳。 */
    private static final long CYCLE_EMPTY_COOLDOWN_MILLIS = 20_000L;

    private int[] cycleMaps() {
        String raw = getSharedPreferences("settings", MODE_PRIVATE)
                .getString("cycle_maps_v1", "");
        if (raw.isEmpty()) return new int[0];
        String[] parts = raw.split(",");
        int[] maps = new int[parts.length];
        int count = 0;
        for (String part : parts) {
            try {
                int map = Integer.parseInt(part.trim());
                if (map > 0) maps[count++] = map;
            } catch (NumberFormatException ignored) {
                // 非法项直接跳过。
            }
        }
        return java.util.Arrays.copyOf(maps, count);
    }

    private synchronized void maintainMapCycle(StarHuntStatus status) {
        int[] probe = cycleMaps();
        android.util.Log.i("StarHuntService", "cycle active=" + active
                + " stopPending=" + stopReadPending + " hangupPending=" + hangupPending
                + " teleport=" + teleportSession + " maps=" + probe.length
                + " state=" + status.state + " map=" + status.map
                + " waitSince=" + waitingSinceMillis + " rotating=" + rotating
                + " emptyHops=" + cycleEmptyHops
                + " cooldown=" + Math.max(0L, cycleCooldownUntilMillis - System.currentTimeMillis()));
        if (!active || !actionSlotHeld || stopReadPending || stopReadRetryable
                || hangupPending || teleportSession) return;
        int[] maps = cycleMaps();
        if (maps.length < 2) {
            rotating = false;
            waitingSinceMillis = 0;
            return;
        }
        long now = System.currentTimeMillis();
        if ("battle".equals(status.state)) cycleEmptyHops = 0;
        if (rotating) {
            // 只要地图已经是目标图、且不在传送中，就补发运行命令把它从“只读扫描”切回“找星”。
            boolean arrived = status.map == rotateTarget
                    && !"teleport_wait".equals(status.state)
                    && status.updatedAt >= rotateStartedAtMillis / 1000L;
            if (arrived) {
                if (rotateSettledAtMillis == 0L) {
                    rotateSettledAtMillis = now;
                } else if (now - rotateSettledAtMillis >= CYCLE_SETTLE_MILLIS) {
                    writeRunForMap(rotateTarget);
                    rotating = false;
                    rotateSettledAtMillis = 0;
                    waitingSinceMillis = 0;
                }
            } else {
                rotateSettledAtMillis = 0;
                if (now - rotateStartedAtMillis >= CYCLE_ARRIVE_MILLIS) startNextMap(maps, now);
            }
            return;
        }
        if (now < cycleCooldownUntilMillis) {
            // 整圈地图都跑过且没有目标：原地等，避免来回传送。
            waitingSinceMillis = 0;
            return;
        }
        // 人物死亡/复活会换图，Lua 会 paused（map_changed_after_battle）；这里自动重新锁定继续找。
        if ("paused".equals(status.state)) {
            String pauseReason = status.reason == null ? "" : status.reason;
            if (pauseReason.startsWith("map_changed") || pauseReason.startsWith("start_map_changed")) {
                if (recoverPausedAtMillis == 0L) {
                    recoverPausedAtMillis = now;
                    return;
                }
                if (now - recoverPausedAtMillis >= 3000L) {
                    recoverPausedAtMillis = 0;
                    writeRunForMap(status.map);
                }
            } else {
                recoverPausedAtMillis = 0;
            }
            return;
        }
        recoverPausedAtMillis = 0;
        // 战斗回执对不上会进 unknown 并永久停住；持续 30 秒就重发运行命令把它拉回正轨。
        if ("unknown".equals(status.state)) {
            if (unknownSinceMillis == 0L) {
                unknownSinceMillis = now;
                return;
            }
            if (now - unknownSinceMillis >= 30_000L) {
                unknownSinceMillis = 0;
                writeRunForMap(status.map);
            }
            return;
        }
        unknownSinceMillis = 0;
        // 卡在“只读扫描”不挑战：有星也不打。换图后如果 RUN 没接上会一直停在这里，
        // 因此 4 秒就重发一次运行命令（换图到开打的实际耗时主要就卡在这段）。
        if ("reading".equals(status.state)) {
            long nowR = System.currentTimeMillis();
            if (readingSinceMillis == 0L) {
                readingSinceMillis = nowR;
                return;
            }
            if (nowR - readingSinceMillis >= 4_000L) {
                readingSinceMillis = 0;
                writeRunForMap(status.map);
            }
            return;
        }
        readingSinceMillis = 0;
        if (!"waiting_targets".equals(status.state)) {
            waitingSinceMillis = 0;
            // 换图后偶发绑定不上（waiting_role）：先重发一次运行命令，再不行就换下一张。
            // 落在限制地图（如"一战到底"）里会一直 waiting_role，这种要尽快换图，不能干等半分钟。
            if ("waiting_role".equals(status.state)) {
                long now0 = System.currentTimeMillis();
                if (waitingRoleSinceMillis == 0L) {
                    waitingRoleSinceMillis = now0;
                    waitingRoleRetried = false;
                    return;
                }
                long stuck = now0 - waitingRoleSinceMillis;
                String why = status.reason == null ? "" : status.reason;
                if (why.startsWith("team_operation_restricted") && stuck >= 6_000L) {
                    waitingRoleSinceMillis = 0;
                    startNextMap(maps, now0);
                    return;
                }
                if (stuck >= 5_000L && !waitingRoleRetried) {
                    waitingRoleRetried = true;
                    writeRunForMap(status.map);
                    return;
                }
                if (stuck >= 12_000L) {
                    waitingRoleSinceMillis = 0;
                    startNextMap(maps, now0);
                }
            } else {
                waitingRoleSinceMillis = 0;
                waitingRoleRetried = false;
            }
            return;
        }
        waitingRoleSinceMillis = 0;
        if (waitingSinceMillis == 0L) {
            waitingSinceMillis = now;
            return;
        }
        if (now - waitingSinceMillis >= ("no_targets_for_selected_level".equals(status.reason)
                ? CYCLE_EMPTY_IDLE_MILLIS : CYCLE_IDLE_MILLIS)) startNextMap(maps, now);
    }

    private void startNextMap(int[] maps, long now) {
        cycleCursor = (cycleCursor + 1) % maps.length;
        rotateTarget = maps[cycleCursor];
        rotating = true;
        rotateStartedAtMillis = now;
        waitingSinceMillis = 0;
        cycleEmptyHops++;
        boolean fullRoundEmpty = cycleEmptyHops >= maps.length;
        if (fullRoundEmpty) {
            cycleEmptyHops = 0;
            cycleCooldownUntilMillis = now + CYCLE_EMPTY_COOLDOWN_MILLIS;
        }
        try {
            StarHuntCommand command = StarHuntCommandFile.writeNext(getFilesDir(), StarHuntCommand.Mode.TELEPORT,
                    StarHuntSelection.empty(), rotateTarget);
            rotateSequence = command.sequence;
            setMessage(fullRoundEmpty
                    ? "这一圈地图都没有目标，等待 " + (CYCLE_EMPTY_COOLDOWN_MILLIS / 1000) + " 秒后继续巡回"
                    : "当前地图没有目标，正在前往另一张地图");
        } catch (Exception error) {
            rotating = false;
            setMessage("换图命令写入失败：" + safeMessage(error));
        }
        notifyForeground();
    }

    private void writeRunForMap(int map) {
        try {
            StarHuntSelection levels = levelsForCycle();
            if (levels.isEmpty()) {
                rotating = false;
                return;
            }
            StarHuntCommandFile.writeNext(getFilesDir(), StarHuntCommand.Mode.RUN, levels, map);
            setMessage("已到达目标地图，继续找星");
        } catch (Exception error) {
            setMessage("换图后运行命令写入失败：" + safeMessage(error));
        }
        notifyForeground();
    }

    /** 巡回换图后重新下发运行时沿用页面里保存的星级。 */
    private StarHuntSelection levelsForCycle() {
        SharedPreferences prefs = getSharedPreferences("settings", MODE_PRIVATE);
        String csv = prefs.getString("selected_levels_v1", "");
        if (!csv.isEmpty()) {
            try {
                return StarHuntSelection.parseCsv(csv);
            } catch (RuntimeException ignored) {
                // 落到单星级回退。
            }
        }
        int level = prefs.getInt("selected_level", 0);
        return level > 0 ? StarHuntSelection.of(level) : StarHuntSelection.empty();
    }

    private synchronized void monitorTick() {
        if (!active) return;
        long nowNanos = System.nanoTime();
        // Only pending one-click startup needs fast polling. Keep steady-state I/O at 1 Hz.
        if (!hangupPending && lastMonitorAtNanos != 0L
                && nowNanos - lastMonitorAtNanos < REGULAR_MONITOR_INTERVAL_NANOS) return;
        lastMonitorAtNanos = nowNanos;
        if (!stopReadPending && !LicenseManager.isActive()) {
            requestReadStop(latestStartId);
            return;
        }

        if (active && !stopReadPending && running(worker) && !writeLease()) {
            requestReadStop(latestStartId);
            return;
        }

        StarHuntStatus status = readStatus();
        // 多图巡回：当前图连续 5 秒没目标就换下一张；到达后重新下发运行命令。
        maintainMapCycle(status);
        // 一次性传送完成后主动收尾；必须确认状态是本轮写的，否则会读到最后一次的 stopped 立刻误停。
        if (teleportSession && "stopped".equals(status.state)
                && status.acceptedSequence == teleportSequence
                && status.updatedAt >= teleportStartedAtMillis / 1000L) {
            teleportSession = false;
            setMessage("teleported".equals(status.reason) ? "已传送到目标地图" : "传送未确认：" + status.reason);
            requestReadStop(latestStartId);
            return;
        }
        Process current = worker;
        StarHuntHangupRequest request = hangupRequest;
        if (request != null && request.isPending()) {
            StarHuntCommand command = null;
            try {
                command = StarHuntCommandFile.read(getFilesDir());
            } catch (Exception ignored) {
                // A malformed or transient command snapshot cannot authorize a RUN.
            }
            StarHuntHangupRequest.Decision decision = request.evaluate(
                    status, command, System.currentTimeMillis());
            if (decision == StarHuntHangupRequest.Decision.EXPIRED) {
                cancelHangupRequest();
                setMessage("挂机等待扫描超时，未启动找星动作");
            } else if (decision == StarHuntHangupRequest.Decision.CANCEL) {
                cancelHangupRequest();
            } else if (decision == StarHuntHangupRequest.Decision.SUBMIT) {
                long scanSequence = request.expectedScanSequence;
                if (startAction(request.levels, true, scanSequence)
                        && request.markSubmitted(scanSequence)) {
                    hangupRequest = null;
                    hangupPending = false;
                } else {
                    cancelHangupRequest();
                }
            }
        }
        if (actionSlotHeld && pendingActionStopSequence > 0L
                && status.confirmsActionStopped(pendingActionStopSequence)
                && (!stopReadPending || running(current))) {
            pendingActionStopSequence = 0L;
            releaseActionSlot();
            setMessage("找星动作已停止，只读扫描继续");
        }

        if (stopReadPending) {
            if (current == null) {
                if (status.confirmsCleanupFor(pendingReadStopSequence)) {
                    completeReadStop(status, -1);
                    return;
                }
                if (status.confirmsGameProcessGoneAfter(stopAttemptStartedAtMillis)) {
                    markReadStopUnconfirmed(
                            "游戏进程已结束，但 worker 清理未确认；已释放动作占用，可重试", true);
                    stopAfterUnconfirmed();
                    return;
                }
                if (stopAttemptExpired()) {
                    markReadStopUnconfirmed(
                            "worker 未连接或状态未确认，停止读取可重试；动作占用仍保留", false);
                    stopAfterUnconfirmed();
                    return;
                }
                setMessage("正在停止读取，等待 worker 状态确认");
                notifyForeground();
                return;
            }
            if (!running(current)) {
                worker = null;
                int exitCode = exitCode(current);
                if (status.confirmsCleanupFor(pendingReadStopSequence)) {
                    completeReadStop(status, exitCode);
                    return;
                }
                if (status.confirmsGameProcessGoneAfter(stopAttemptStartedAtMillis)) {
                    markReadStopUnconfirmed(
                            "游戏进程已结束，但 hook 或绑定清理未确认；已释放动作占用，可重试", true);
                    stopAfterUnconfirmed();
                    return;
                }
                if (stopAttemptExpired()) {
                    markReadStopUnconfirmed(
                            "worker 已退出，但清理状态未确认；停止读取可重试", false);
                    stopAfterUnconfirmed();
                    return;
                }
                setMessage("worker 已退出，等待最终清理状态写入");
                notifyForeground();
                return;
            }
            if (stopAttemptExpired()) {
                markReadStopUnconfirmed(
                        "worker 未确认停止读取，状态可重试；动作占用仍保留", false);
                stopAfterUnconfirmed();
                return;
            }
            setMessage("正在停止读取，等待 worker 清理确认");
        } else if (current != null && !running(current)) {
            cancelHangupRequest();
            worker = null;
            int exitCode = exitCode(current);
            if (status.confirmsCleanup()) {
                if (actionSlotHeld) releaseActionSlot();
                active = false;
                setMessage("worker 已结束，清理状态已确认；需要时请重新开启读取");
                stopForeground(true);
                stopSelfResult(latestStartId);
                return;
            }
            // A dead worker without explicit cleanup evidence remains unconfirmed.
            setMessage("worker 已退出，但 hook 或绑定清理未确认；未自动重启");
        } else if ("fatal".equals(status.state)) {
            cancelHangupRequest();
            setMessage("worker 报告运行异常，读取状态请查看详情");
        }
        notifyForeground();
    }

    private synchronized void completeReadStop(StarHuntStatus status, int exitCode) {
        if (!status.confirmsCleanupFor(pendingReadStopSequence)) return;
        try {
            writeStopJournal(new StarHuntStopJournal(StarHuntStopJournal.Phase.COMPLETE,
                    pendingReadStopSequence, stopAttemptStartedAtMillis, false));
        } catch (Exception error) {
            markReadStopUnconfirmed(
                    "worker 已确认清理，但停止结果无法保存；动作占用仍保留，可重试", false);
            stopAfterUnconfirmed();
            return;
        }
        stopReadPending = false;
        active = false;
        stopReadRetryable = false;
        pendingActionStopSequence = 0L;
        pendingReadStopSequence = 0L;
        if (actionSlotHeld) releaseActionSlot();
        setMessage(exitCode <= 0 ? "读取已停止，worker 清理已确认"
                : "读取已停止，worker 清理已确认（退出码 " + exitCode + "）");
        stopForeground(true);
        stopSelfResult(latestStartId);
    }

    private boolean stopAttemptExpired() {
        long requestedAt = stopAttemptStartedAtMillis;
        try {
            StarHuntStopJournal state = readStopJournal();
            if (state != null && state.phase == StarHuntStopJournal.Phase.PENDING) {
                requestedAt = state.requestedAtMillis;
            }
        } catch (Exception ignored) {
            return true;
        }
        if (requestedAt <= 0L) return true;
        long now = System.currentTimeMillis();
        return now < requestedAt || now - requestedAt >= LOST_WORKER_STOP_TIMEOUT_MS;
    }

    private void markReadStopUnconfirmed(String message, boolean releaseIfGameGone) {
        long sequence = pendingReadStopSequence;
        long requestedAt = stopAttemptStartedAtMillis;
        try {
            StarHuntStopJournal previous = readStopJournal();
            if (previous != null) {
                if (sequence <= 0L) sequence = previous.sequence;
                if (requestedAt <= 0L) requestedAt = previous.requestedAtMillis;
            }
            if (sequence > 0L && requestedAt > 0L) {
                boolean hold = actionSlotHeld && !releaseIfGameGone;
                writeStopJournal(new StarHuntStopJournal(StarHuntStopJournal.Phase.UNCONFIRMED,
                        sequence, requestedAt, hold));
                if (releaseIfGameGone && actionSlotHeld) releaseActionSlot();
            }
        } catch (Exception error) {
            // A failed journal update must not release a potentially live action slot.
            message = message + "（停止记录保存失败，动作占用保留）";
        }
        stopReadPending = false;
        stopReadRetryable = true;
        active = false;
        pendingActionStopSequence = 0L;
        pendingReadStopSequence = 0L;
        setMessage(message);
    }

    private void stopAfterUnconfirmed() {
        notifyForeground();
        stopForeground(true);
        stopSelfResult(latestStartId);
    }

    private void releaseActionSlot() {
        if (!actionSlotHeld) return;
        actionSlotHeld = false;
        AnleiService.releaseStarHuntAction();
    }

    private boolean hasPriorWorkerEvidence() {
        File directory = getFilesDir();
        return new File(directory, "lease").exists()
                || new File(directory, "worker.log").exists()
                || new File(directory, "status").exists();
    }

    private StarHuntStatus readStatus() {
        File file = new File(getFilesDir(), "status");
        if (!file.isFile()) return StarHuntStatus.parse("");
        try (FileInputStream input = new FileInputStream(file);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            StarHuntStatus status = StarHuntStatus.parse(
                    new String(output.toByteArray(), StandardCharsets.UTF_8));
            if (!status.mapCatalog.isEmpty()) {
                com.xnas.wpe.suite.GameMapCatalog.store(this, status.mapCatalog);
            }
            return status;
        } catch (Exception ignored) {
            return StarHuntStatus.parse("");
        }
    }

    private boolean writeLease() {
        File lease = new File(getFilesDir(), "lease");
        try (FileOutputStream output = new FileOutputStream(lease, false)) {
            output.write(Long.toString(System.currentTimeMillis() / 1000L)
                    .getBytes(StandardCharsets.UTF_8));
            output.getFD().sync();
            return true;
        } catch (Exception error) {
            setMessage("运行租约写入失败，worker 将按租约停止");
            return false;
        }
    }

    private void writeStopRequest() throws java.io.IOException {
        File stop = new File(getFilesDir(), "stop");
        try (FileOutputStream output = new FileOutputStream(stop, false)) {
            output.write(1);
            output.getFD().sync();
        }
    }

    private void recordSelectedLevels(StarHuntSelection levels) {
        getSharedPreferences("settings", MODE_PRIVATE).edit()
                .putString(StarHuntSelectionStore.KEY, levels.toCsv())
                .putInt(StarHuntSelectionStore.LEGACY_KEY, levels.firstLevel()).apply();
    }

    private void setMessage(String message) {
        serviceMessage = message == null ? "状态未确认" : message;
    }

    private void cancelHangupRequest() {
        StarHuntHangupRequest request = hangupRequest;
        hangupRequest = null;
        hangupPending = false;
        if (request != null) request.cancel();
    }

    private void notifyForeground() {
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager != null && active) manager.notify(NOTIFICATION_ID, notification(serviceMessage));
    }

    private Notification notification(String message) {
        Intent open = new Intent(this, com.xnas.wpe.suite.MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP)
                .putExtra(com.xnas.wpe.suite.MainActivity.EXTRA_OPEN_MODULE_ID, TargetGame.STARHUNT);
        PendingIntent pending = PendingIntent.getActivity(this, NOTIFICATION_ID, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(this, CHANNEL_ID) : new Notification.Builder(this);
        return builder.setContentTitle("自动找星")
                .setContentText(message == null || message.length() <= 100
                        ? message : message.substring(0, 100))
                .setSmallIcon(android.R.drawable.stat_notify_sync)
                .setContentIntent(pending)
                .setOngoing(active || stopReadPending)
                .build();
    }

    private String requestText(String action) {
        if (ACTION_STOP_READ.equals(action)) return "正在请求停止读取并清理";
        if (ACTION_STOP_RUN.equals(action)) return "正在请求停止找星动作";
        if (ACTION_START_RUN.equals(action)) return "正在处理找星动作请求";
        if (ACTION_START_HANGUP.equals(action)) return "正在处理挂机请求";
        return "正在处理读取请求";
    }

    private static boolean running(Process process) {
        if (process == null) return false;
        try {
            process.exitValue();
            return false;
        } catch (IllegalThreadStateException error) {
            return true;
        }
    }

    private int exitCode(Process process) {
        try {
            return process.exitValue();
        } catch (IllegalThreadStateException ignored) {
            return -1;
        }
    }

    private static void deleteIfPresent(File file) {
        if (file.exists()) file.delete();
    }

    private static String safeMessage(Exception error) {
        String message = error.getMessage();
        return message == null || message.isEmpty() ? error.getClass().getSimpleName() : message;
    }

    @Override public IBinder onBind(Intent intent) {
        return null;
    }

    @Override public synchronized void onDestroy() {
        destroyed = true;
        cancelHangupRequest();
        if (active && running(worker) && !stopReadPending) {
            try {
                StarHuntCommand scan = writeScanCommand();
                long requestedAt = System.currentTimeMillis();
                writeStopJournal(new StarHuntStopJournal(StarHuntStopJournal.Phase.PENDING,
                        scan.sequence, requestedAt, actionSlotHeld));
                pendingReadStopSequence = scan.sequence;
                stopAttemptStartedAtMillis = requestedAt;
                pendingActionStopSequence = actionSlotHeld ? scan.sequence : 0L;
                stopReadPending = true;
                stopReadRetryable = false;
                writeStopRequest();
            } catch (Exception ignored) {
                if (pendingReadStopSequence > 0L) {
                    markReadStopUnconfirmed(
                            "服务关闭时停止状态未能确认，动作占用仍保留，可重试", false);
                }
                // The existing lease still expires; do not release action ownership without status proof.
            }
        }
        timer.shutdownNow();
        super.onDestroy();
    }
}
