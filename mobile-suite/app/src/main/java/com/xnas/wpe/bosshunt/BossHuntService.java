package com.xnas.wpe.bosshunt;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Intent;
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
public final class BossHuntService extends ModuleService {
    public static final String ACTION_START_READ = "com.xnas.wpe.bosshunt.START_READ";
    public static final String ACTION_STOP_READ = "com.xnas.wpe.bosshunt.STOP_READ";
    public static final String ACTION_START_RUN = "com.xnas.wpe.bosshunt.START_RUN";
    public static final String ACTION_START_HANGUP = "com.xnas.wpe.bosshunt.START_HANGUP";
    public static final String ACTION_STOP_RUN = "com.xnas.wpe.bosshunt.STOP_RUN";
    /** 一次性传送：EXTRA_MAPS 里放目标地图 ID，到达或超时后自动结束。 */
    public static final String ACTION_TELEPORT = "com.xnas.wpe.bosshunt.TELEPORT";
    public static final String EXTRA_MAPS = "maps";

    private static final String CHANNEL_ID = "bosshunt_reader";
    private static final int NOTIFICATION_ID = 6111;
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
    private volatile BossHuntHangupRequest hangupRequest;

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
    private volatile long stopAttemptStartedAtMillis;
    private volatile long lastStopDeliveryAtMillis;
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
            BossHuntStopJournal state = BossHuntStopJournal.parse(
                    new String(output.toByteArray(), StandardCharsets.UTF_8));
            if (state.phase == BossHuntStopJournal.Phase.COMPLETE) return false;
            // A journal whose game process ended and whose worker is gone can never be
            // confirmed; do not keep auto-sending STOP_READ (which brings the game forward).
            if (BossHuntStopRecovery.shouldRecover(
                    state, readStatusFile(moduleDirectory), running(worker))) return false;
            return true;
        } catch (Exception ignored) {
            // A malformed journal must leave an explicit retry path rather than hide the stop.
            return true;
        }
    }

    private static BossHuntStatus readStatusFile(File moduleDirectory) {
        File file = new File(moduleDirectory, "status");
        if (!file.isFile()) return BossHuntStatus.parse("");
        try (FileInputStream input = new FileInputStream(file);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[1024];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            return BossHuntStatus.parse(new String(output.toByteArray(), StandardCharsets.UTF_8));
        } catch (Exception ignored) {
            return BossHuntStatus.parse("");
        }
    }

    public static boolean hasCompletedStop(File moduleDirectory) {
        File journal = new File(moduleDirectory, STOP_JOURNAL_FILE);
        if (!journal.isFile()) return false;
        try (FileInputStream input = new FileInputStream(journal);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[1024]; int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            return BossHuntStopJournal.parse(new String(output.toByteArray(), StandardCharsets.UTF_8)).phase
                    == BossHuntStopJournal.Phase.COMPLETE;
        } catch (Exception ignored) { return false; }
    }

    /**
     * A stop journal can never be confirmed after the bound game process ended and the
     * worker is gone. Clear it so opening the BossHunt page does not auto-send STOP_READ
     * and bring the game forward from a stale retry state.
     */
    private synchronized boolean recoverStopJournalIfProcessGone() {
        final BossHuntStopJournal journal;
        try {
            journal = readStopJournal();
        } catch (Exception error) {
            return false;
        }
        if (!BossHuntStopRecovery.shouldRecover(
                journal, readStatusFile(getFilesDir()), running(worker))) return false;
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

        final BossHuntStopJournal state;
        try {
            state = readStopJournal();
        } catch (Exception error) {
            stopReadPending = true;
            active = true;
            stopReadRetryable = true;
            try { BossHuntStopJournal prior = readStopJournal(); if (prior != null) {
                pendingReadStopSequence = prior.sequence;
                stopAttemptStartedAtMillis = prior.requestedAtMillis;
                pendingActionStopSequence = prior.actionSlotHeld ? prior.sequence : 0L;
            }} catch (Exception ignored) { }
            if (!actionSlotHeld) {
                AnleiService.tryAcquireBossHuntAction();
                actionSlotHeld = true;
            }
            setMessage("正在停止，自动清理中");
            ensureMonitor();
            return;
        }
        if (state == null) return;
        if (state.phase == BossHuntStopJournal.Phase.COMPLETE) {
            deleteIfPresent(file);
            stopReadRetryable = false;
            return;
        }
        if (recoverStopJournalIfProcessGone()) return;

        if (state.actionSlotHeld && !actionSlotHeld) {
            AnleiService.tryAcquireBossHuntAction();
            // Keep the BossHunt ownership marker even if another action currently holds the gate.
            actionSlotHeld = true;
        }
        pendingReadStopSequence = state.sequence;
        stopAttemptStartedAtMillis = state.requestedAtMillis;
        if (state.phase == BossHuntStopJournal.Phase.PENDING) {
            stopReadPending = true;
            stopReadRetryable = false;
            active = true;
            setMessage("已恢复停止读取请求，等待 worker 确认清理");
            ensureMonitor();
        } else {
            stopReadPending = true;
            stopReadRetryable = true;
            active = true;
            pendingActionStopSequence = state.actionSlotHeld ? state.sequence : 0L;
            setMessage("正在停止，自动清理中");
            ensureMonitor();
        }
    }

    private BossHuntStopJournal readStopJournal() throws java.io.IOException {
        File file = new File(getFilesDir(), STOP_JOURNAL_FILE);
        if (!file.isFile()) return null;
        try (FileInputStream input = new FileInputStream(file);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[1024];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            return BossHuntStopJournal.parse(new String(output.toByteArray(), StandardCharsets.UTF_8));
        }
    }

    private void writeStopJournal(BossHuntStopJournal state) throws java.io.IOException {
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
        // 服务进程也要先加载全量地图表，否则按 ID 构造选择会判成“不支持的地图”。
        BossHuntMapCatalog.load(this);
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID, "自动找BOSS读取", NotificationManager.IMPORTANCE_LOW);
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
        if (!TargetGame.supportsModule(TargetGame.BOSSHUNT, TargetGame.getSelectedPackage(this))) {
            setMessage("自动找BOSS仅支持 DHXY");
            stopForeground(true);
            stopSelfResult(startId);
            return START_NOT_STICKY;
        }

        if (ACTION_TELEPORT.equals(action)) {
            int map = 0;
            try {
                map = Integer.parseInt(intent == null ? "" : intent.getStringExtra(EXTRA_MAPS));
            } catch (RuntimeException ignored) {
                // 参数缺失或不是数字时按“未指定”处理。
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
            if (intent != null && intent.hasExtra(EXTRA_MAPS)) {
                try {
                    startHangup(BossHuntSelection.parseCsv(intent.getStringExtra(EXTRA_MAPS)));
                } catch (IllegalArgumentException error) {
                    setMessage("地图选择无效，未启动挂机");
                    notifyForeground();
                }
            } else { setMessage("缺少 maps 选择，未启动挂机"); notifyForeground(); }
        } else {
            cancelHangupRequest();
            if (intent != null && intent.hasExtra(EXTRA_MAPS)) {
                try {
                    startAction(BossHuntSelection.parseCsv(intent.getStringExtra(EXTRA_MAPS)),
                            false, 0L);
                } catch (IllegalArgumentException error) {
                    setMessage("地图选择无效，未启动找BOSS动作");
                    notifyForeground();
                }
            } else { setMessage("缺少 maps 选择，未启动BOSS巡回"); notifyForeground(); }
        }
        return START_NOT_STICKY;
    }

    private synchronized void startTeleport(int map, int startId) {
        if (!BossHuntMapCatalog.contains(map)) {
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
        if (!AnleiService.tryAcquireBossHuntAction()) {
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
            setMessage("正在停止，自动清理中");
            notifyForeground();
            return;
        }
        Process current = worker;
        if (active && running(current)) {
            try {
                BossHuntCommand previous = BossHuntCommandFile.read(getFilesDir());
                BossHuntCommand next;
                if (pendingTeleportMap > 0) {
                    // worker 还活着时也要能切到传送：写一条 teleport 命令让它换模式。
                    int target = pendingTeleportMap;
                    next = BossHuntCommandFile.writeNext(getFilesDir(), BossHuntCommand.Mode.TELEPORT,
                            BossHuntSelection.of(target), target);
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
                if (actionSlotHeld || (previous != null && previous.mode == BossHuntCommand.Mode.RUN)) {
                    actionSlotHeld = true;
                    pendingActionStopSequence = next.sequence;
                    setMessage("正在停止找BOSS，扫描继续");
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

        BossHuntStatus oldStatus = readStatus();
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
                throw new IllegalStateException("无法创建自动找BOSS模块目录");
            }
            deleteIfPresent(new File(directory, "stop"));
            BossHuntCommand initial;
            if (pendingTeleportMap > 0) {
                int target = pendingTeleportMap;
                initial = BossHuntCommandFile.writeNext(directory, BossHuntCommand.Mode.TELEPORT,
                        BossHuntSelection.of(target), target);
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

    private synchronized void startHangup(BossHuntSelection maps) {
        if (maps == null || maps.isEmpty()) {
            setMessage("请至少选择一个地图");
            notifyForeground();
            return;
        }
        BossHuntCommand previous;
        try {
            previous = BossHuntCommandFile.read(getFilesDir());
        } catch (Exception error) {
            setMessage("command 文件无效，未启动挂机");
            notifyForeground();
            return;
        }
        boolean retryableStop = stopReadRetryable || hasRetryableStop(getFilesDir());
        if (stopReadPending || retryableStop) {
            stopReadRetryable = retryableStop;
            setMessage("正在停止，自动清理中");
            notifyForeground();
            return;
        }
        if (hangupRequest != null && hangupRequest.isPending()) {
            setMessage("挂机请求正在等待本次扫描，请勿重复点击");
            notifyForeground();
            return;
        }
        if (actionSlotHeld || (active && running(worker) && previous != null
                && previous.mode == BossHuntCommand.Mode.RUN)) {
            setMessage("找BOSS动作正在运行，请先停止后再开始挂机");
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
            BossHuntCommand scan = BossHuntCommandFile.read(getFilesDir());
            if (scan == null || scan.mode != BossHuntCommand.Mode.SCAN
                    || scan.sequence <= previousSequence) {
                setMessage("本次扫描命令未确认写入，未创建挂机请求");
                notifyForeground();
                return;
            }
            hangupRequest = new BossHuntHangupRequest(maps, scan.sequence, System.currentTimeMillis());
            hangupPending = true;
            setMessage("已启动读取并开始挂机扫描，等待当前地图和角色信息");
            ensureMonitor();
        } catch (Exception error) {
            cancelHangupRequest();
            setMessage("读取已启动，但挂机请求创建失败：" + safeMessage(error));
        }
        notifyForeground();
    }

    private synchronized boolean startAction(BossHuntSelection maps, boolean allowEmpty,
                                             long expectedScanSequence) {
        if (maps == null || maps.isEmpty()) {
            setMessage("请至少选择一个地图");
            notifyForeground();
            return false;
        }
        if (!active || stopReadPending || !running(worker)) {
            setMessage("请先开启读取并等待地图数据");
            notifyForeground();
            return false;
        }
        if (!LicenseManager.isActive()) {
            cancelHangupRequest();
            requestReadStop(latestStartId);
            return false;
        }
        BossHuntStatus status = readStatus();
        BossHuntCommand previous;
        try {
            previous = BossHuntCommandFile.read(getFilesDir());
        } catch (Exception error) {
            setMessage("command 文件无效，未启动找BOSS动作");
            notifyForeground();
            return false;
        }
        if (previous == null || status.acceptedSequence < previous.sequence
                || (expectedScanSequence > 0L && (previous.sequence != expectedScanSequence
                || previous.mode != BossHuntCommand.Mode.SCAN
                || status.acceptedSequence != expectedScanSequence))) {
            setMessage("正在等待本次扫描命令被 worker 确认");
            notifyForeground();
            return false;
        }
        long now = System.currentTimeMillis() / 1000L;
        if (!(allowEmpty ? status.canStartHangup(maps, now) : status.canStartAction(maps, now))) {
            setMessage("当前地图或地图数据未确认，请等待扫描更新");
            notifyForeground();
            return false;
        }
        if ("map_wait".equals(status.state) || "teleport_wait".equals(status.state)
                || "dialog_wait".equals(status.state) || "challenge_ready".equals(status.state)
                || "challenge_wait".equals(status.state)
                || "battle".equals(status.state) || "cooldown".equals(status.state)) {
            setMessage("找BOSS动作仍在进行或等待冷却，请先停止找BOSS");
            notifyForeground();
            return false;
        }
        boolean alreadyHeld = actionSlotHeld;
        if (!alreadyHeld && !AnleiService.tryAcquireBossHuntAction()) {
            setMessage("其他移动功能正在运行动作，请先停止对应功能");
            notifyForeground();
            return false;
        }
        try {
            BossHuntCommand command = BossHuntCommandFile.writeNext(getFilesDir(),
                    BossHuntCommand.Mode.RUN, maps, status.map);
            actionSlotHeld = true;
            pendingActionStopSequence = 0L;
            if (!writeLease()) {
                requestActionStop();
                return false;
            }
            setMessage("已提交找BOSS动作，等待 worker 确认");
            recordSelectedMapCounts(maps);
            notifyForeground();
            return true;
        } catch (Exception error) {
            if (!alreadyHeld) AnleiService.releaseBossHuntAction();
            actionSlotHeld = alreadyHeld;
            setMessage("找BOSS命令写入失败，动作未确认");
        }
        notifyForeground();
        return false;
    }

    private synchronized void requestActionStop() {
        cancelHangupRequest();
        if (!active || !running(worker) || stopReadPending) {
            setMessage("读取尚未运行，无法停止找BOSS动作");
            if (!active) {
                stopForeground(true);
                stopSelfResult(latestStartId);
            }
            notifyForeground();
            return;
        }
        try {
            BossHuntCommand command = writeScanCommand();
            pendingActionStopSequence = command.sequence;
            setMessage("正在停止找BOSS，等待 worker 确认；只读扫描会继续");
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
        if (!running(current) && !stopReadPending && !stopReadRetryable
                && !hasPriorWorkerEvidence() && !new File(getFilesDir(), STOP_JOURNAL_FILE).exists()) {
            setMessage("读取未运行");
            stopForeground(true);
            stopSelfResult(startId);
            return;
        }
        if (!running(current)) {
            BossHuntStatus terminal = readStatus();
            if (stopReadPending && terminal.confirmsCleanupFor(pendingReadStopSequence)) {
                BossHuntStopJournal earlier = null;
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
        if (stopReadPending && pendingReadStopSequence > 0L) { deliverStopIntent(); setMessage("正在停止，自动清理中"); ensureMonitor(); notifyForeground(); return; }
        if (pendingReadStopSequence <= 0L) {
            try {
                BossHuntCommand scan = writeScanCommand();
                pendingReadStopSequence = scan.sequence;
                stopAttemptStartedAtMillis = System.currentTimeMillis();
                pendingActionStopSequence = actionSlotHeld ? scan.sequence : 0L;
                writeStopJournal(new BossHuntStopJournal(BossHuntStopJournal.Phase.PENDING,
                        scan.sequence, stopAttemptStartedAtMillis, actionSlotHeld));
            } catch (Exception ignored) {
                // Keep the malformed or unavailable state fail-closed until persistence works.
            }
            stopReadPending = true; stopReadRetryable = true; active = true;
            setMessage("正在停止，自动清理中"); ensureMonitor(); notifyForeground(); return;
        }
        boolean journalWritten = false;
        try {
            long sequence;
            if (running(current) || pendingReadStopSequence <= 0L) {
                sequence = writeScanCommand().sequence;
            } else {
                BossHuntStopJournal previous = readStopJournal();
                if (previous != null && previous.phase != BossHuntStopJournal.Phase.COMPLETE) {
                    sequence = previous.sequence;
                } else {
                    BossHuntCommand previousCommand = BossHuntCommandFile.read(getFilesDir());
                    sequence = previousCommand == null
                            ? readStatus().acceptedSequence : previousCommand.sequence;
                }
                if (sequence <= 0L) {
                    throw new IllegalStateException("没有可复用的停止命令序号");
                }
            }
            long requestedAt = System.currentTimeMillis();
            writeStopJournal(new BossHuntStopJournal(BossHuntStopJournal.Phase.PENDING,
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
                stopReadPending = true; stopReadRetryable = true; active = true;
                setMessage("正在停止，自动清理中"); ensureMonitor();
            } else {
                stopReadRetryable = true; stopReadPending = true; active = true;
                setMessage("正在停止，自动清理中"); ensureMonitor();
            }
        }
        notifyForeground();
    }

    private BossHuntCommand writeScanCommand() throws java.io.IOException {
        BossHuntStatus status = readStatus();
        return BossHuntCommandFile.writeNext(getFilesDir(), BossHuntCommand.Mode.SCAN,
                BossHuntSelection.empty(), Math.max(status.map, 0));
    }

    private void launchWorker(File directory) throws Exception {
        String binary = new File(getApplicationInfo().nativeLibraryDir,
                "libbosshunt_worker.so").getAbsolutePath();
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

        BossHuntStatus status = readStatus();
        // 一次性传送完成后主动收尾：停掉 worker 并让服务退出。必须确认状态是本轮写的，
        // 否则会读到上一次的 stopped 立刻误停，worker 还没绑定就被 stop 掉。
        if (teleportSession && "stopped".equals(status.state)
                && status.acceptedSequence == teleportSequence
                && status.updatedAt >= teleportStartedAtMillis / 1000L) {
            teleportSession = false;
            setMessage("teleported".equals(status.reason) ? "已传送到目标地图" : "传送未确认：" + status.reason);
            requestReadStop(latestStartId);
            return;
        }
        Process current = worker;
        BossHuntHangupRequest request = hangupRequest;
        if (request != null && request.isPending()) {
            BossHuntCommand command = null;
            try {
                command = BossHuntCommandFile.read(getFilesDir());
            } catch (Exception ignored) {
                // A malformed or transient command snapshot cannot authorize a RUN.
            }
            BossHuntHangupRequest.Decision decision = request.evaluate(
                    status, command, System.currentTimeMillis());
            if (decision == BossHuntHangupRequest.Decision.EXPIRED) {
                cancelHangupRequest();
                setMessage("挂机等待扫描超时，未启动找BOSS动作");
            } else if (decision == BossHuntHangupRequest.Decision.CANCEL) {
                cancelHangupRequest();
            } else if (decision == BossHuntHangupRequest.Decision.SUBMIT) {
                long scanSequence = request.expectedScanSequence;
                if (startAction(request.maps, true, scanSequence)
                        && request.markSubmitted(scanSequence)) {
                    hangupRequest = null;
                    hangupPending = false;
                } else {
                    cancelHangupRequest();
                }
            }
        }
        if (!stopReadPending && actionSlotHeld && pendingActionStopSequence > 0L
                && status.confirmsActionStopped(pendingActionStopSequence)
                ) {
            pendingActionStopSequence = 0L;
            releaseActionSlot();
            setMessage("找BOSS动作已停止，只读扫描继续");
        }

        if (stopReadPending) {
            if (current != null && running(current)) deliverStopIntent();
            if (current == null) {
                if (status.confirmsCleanupFor(pendingReadStopSequence)) {
                    completeReadStop(status, -1);
                    return;
                }
                if (status.confirmsGameProcessGoneAfter(stopAttemptStartedAtMillis)) {
                    finishAfterGameGone();
                    return;
                }
                setMessage("请切回游戏，正在等待清理");
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
                    finishAfterGameGone();
                    return;
                }
                setMessage("请切回游戏，正在等待清理");
                notifyForeground();
                return;
            }
            setMessage("正在停止，自动清理中");
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
        } else if ("map_wait".equals(status.state) && actionSlotHeld) {
            setMessage("正在前往地图");
        } else if ("fatal".equals(status.state)) {
            cancelHangupRequest();
            setMessage("worker 报告运行异常，读取状态请查看详情");
        }
        notifyForeground();
    }

    private void deliverStopIntent() {
        long now = System.currentTimeMillis();
        if (now - lastStopDeliveryAtMillis < 2000L) return;
        lastStopDeliveryAtMillis = now;
        try { writeStopRequest(); } catch (Exception ignored) { }
        try {
            BossHuntStopJournal journal = readStopJournal();
            if (journal != null && journal.phase == BossHuntStopJournal.Phase.PENDING) return;
            if (pendingReadStopSequence > 0L) writeStopJournal(new BossHuntStopJournal(
                    BossHuntStopJournal.Phase.PENDING, pendingReadStopSequence,
                    stopAttemptStartedAtMillis > 0 ? stopAttemptStartedAtMillis : now, actionSlotHeld));
        } catch (Exception ignored) { }
    }

    private void finishAfterGameGone() {
        BossHuntStatus status = readStatus();
        if (!status.confirmsGameProcessGoneAfter(stopAttemptStartedAtMillis) || !status.sessionDetached) return;
        // 原游戏进程已结束，钩子随之消失，清理已无对象可清理：必须落一条 COMPLETE，
        // 否则 retryable 标记会一直留着，页面永远卡在“未确认”而无法重新启动。
        try {
            writeStopJournal(new BossHuntStopJournal(BossHuntStopJournal.Phase.COMPLETE,
                    Math.max(1L, pendingReadStopSequence), stopAttemptStartedAtMillis, false));
        } catch (Exception error) {
            // 落盘失败时保持可重试，不能假装清理完成。
            stopReadPending = false;
            stopReadRetryable = true;
            active = false;
            setMessage("已停止（原游戏进程已结束），但停止记录无法保存");
            notifyForeground();
            stopForeground(true);
            stopSelfResult(latestStartId);
            return;
        }
        stopReadPending = false;
        stopReadRetryable = false;
        pendingReadStopSequence = 0L;
        pendingActionStopSequence = 0L;
        active = false;
        if (actionSlotHeld) releaseActionSlot();
        setMessage("已停止（原游戏进程已结束）");
        notifyForeground(); stopForeground(true); stopSelfResult(latestStartId);
    }

    private synchronized void completeReadStop(BossHuntStatus status, int exitCode) {
        if (!status.confirmsCleanupFor(pendingReadStopSequence)) return;
        try {
            writeStopJournal(new BossHuntStopJournal(BossHuntStopJournal.Phase.COMPLETE,
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
            BossHuntStopJournal state = readStopJournal();
            if (state != null && state.phase == BossHuntStopJournal.Phase.PENDING) {
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
            BossHuntStopJournal previous = readStopJournal();
            if (previous != null) {
                if (sequence <= 0L) sequence = previous.sequence;
                if (requestedAt <= 0L) requestedAt = previous.requestedAtMillis;
            }
            if (sequence > 0L && requestedAt > 0L) {
                boolean hold = actionSlotHeld && !releaseIfGameGone;
                writeStopJournal(new BossHuntStopJournal(BossHuntStopJournal.Phase.UNCONFIRMED,
                        sequence, requestedAt, hold));
                if (releaseIfGameGone && actionSlotHeld) releaseActionSlot();
            }
        } catch (Exception error) {
            // A failed journal update must not release a potentially live action slot.
            message = message + "（停止记录保存失败，动作占用保留）";
        }
        stopReadPending = true;
        stopReadRetryable = true;
        active = true;
        pendingReadStopSequence = sequence;
        stopAttemptStartedAtMillis = requestedAt;
        setMessage(message);
    }

    private void stopAfterUnconfirmed() {
        notifyForeground();
        ensureMonitor();
    }

    private void releaseActionSlot() {
        if (!actionSlotHeld) return;
        actionSlotHeld = false;
        AnleiService.releaseBossHuntAction();
    }

    private boolean hasPriorWorkerEvidence() {
        File directory = getFilesDir();
        return new File(directory, "lease").exists()
                || new File(directory, "worker.log").exists()
                || new File(directory, "status").exists();
    }

    private BossHuntStatus readStatus() {
        File file = new File(getFilesDir(), "status");
        if (!file.isFile()) return BossHuntStatus.parse("");
        try (FileInputStream input = new FileInputStream(file);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            BossHuntStatus status = BossHuntStatus.parse(
                    new String(output.toByteArray(), StandardCharsets.UTF_8));
            // worker 会把它从客户端地图表导出的全量地图写进 status，这里顺手落盘。
            if (!status.mapCatalog.isEmpty()) BossHuntMapCatalog.refresh(this, status.mapCatalog);
            return status;
        } catch (Exception ignored) {
            return BossHuntStatus.parse("");
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

    private void recordSelectedMapCounts(BossHuntSelection maps) {
        getSharedPreferences("settings", MODE_PRIVATE).edit()
                .putString(BossHuntSelectionStore.KEY, maps.toCsv()).apply();
    }

    private void setMessage(String message) {
        serviceMessage = message == null ? "状态未确认" : message;
        // 前台通知在部分系统版本上不会因 notify() 而刷新，这里同步打一条日志，
        // 便于从 logcat 直接确认启动被哪一步拦下。
        android.util.Log.i("BossHuntService", "message=" + serviceMessage
                + " active=" + active + " stopPending=" + stopReadPending
                + " retryable=" + stopReadRetryable);
    }

    private void cancelHangupRequest() {
        BossHuntHangupRequest request = hangupRequest;
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
                .putExtra(com.xnas.wpe.suite.MainActivity.EXTRA_OPEN_MODULE_ID, TargetGame.BOSSHUNT);
        PendingIntent pending = PendingIntent.getActivity(this, NOTIFICATION_ID, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(this, CHANNEL_ID) : new Notification.Builder(this);
        return builder.setContentTitle("自动找BOSS")
                .setContentText(message == null || message.length() <= 100
                        ? message : message.substring(0, 100))
                .setSmallIcon(android.R.drawable.stat_notify_sync)
                .setContentIntent(pending)
                .setOngoing(active || stopReadPending)
                .build();
    }

    private String requestText(String action) {
        if (ACTION_STOP_READ.equals(action)) return "正在请求停止读取并清理";
        if (ACTION_STOP_RUN.equals(action)) return "正在请求停止找BOSS动作";
        if (ACTION_START_RUN.equals(action)) return "正在处理找BOSS动作请求";
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
                BossHuntCommand scan = writeScanCommand();
                long requestedAt = System.currentTimeMillis();
                writeStopJournal(new BossHuntStopJournal(BossHuntStopJournal.Phase.PENDING,
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
