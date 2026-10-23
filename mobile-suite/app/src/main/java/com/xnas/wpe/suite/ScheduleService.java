package com.xnas.wpe.suite;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;

import java.io.File;
import java.io.FileOutputStream;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/**
 * 定时任务执行器。
 *
 * <p>用常驻前台服务每 5 秒对一次系统时间，到点就派发对应模块的启动/停止请求。
 * 不用 AlarmManager：精确闹钟在 Android 14 需要额外授权、还会被 Doze 打折，
 * 而助手本身就需要常驻前台服务，自己计时更稳定。</p>
 *
 * <p>启动动作会先把目标游戏切回前台，否则模块的 worker 起来后也连不上游戏。</p>
 */
public final class ScheduleService extends Service {
    private static final String CHANNEL = "schedule";
    private static final int NOTIFICATION_ID = 6103;
    private static final long TICK_MS = 5000L;
    /** 错过触发后最多补发几分钟；再久就等下个周期，避免半夜起床补跑。 */
    private static final int CATCH_UP_MINUTES = 2;
    private static final long LOG_LIMIT = 64 * 1024L;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private boolean ticking;
    private String lastNotificationSummary = "";

    /** 有启用的任务时确保执行器在跑；没有任务时什么都不做。 */
    public static void startIfNeeded(Context context) {
        if (context == null || !ScheduleStore.hasEnabled(context)) return;
        Intent intent = new Intent(context, ScheduleService.class);
        try {
            if (Build.VERSION.SDK_INT >= 26) context.startForegroundService(intent);
            else context.startService(intent);
        } catch (RuntimeException ignored) {
            // 后台限制等原因启动失败时，页面上的状态会照常显示任务内容。
        }
    }

    private final Runnable tick = new Runnable() {
        @Override
        public void run() {
            try {
                fireDue();
                pollExecutions();
                updateNotification();
            } catch (RuntimeException ignored) {
                // 单次触发异常不能让执行器退出，下一个 tick 继续。
            }
            handler.postDelayed(this, TICK_MS);
        }
    };

    @Override
    public void onCreate() {
        super.onCreate();
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel =
                    new NotificationChannel(CHANNEL, "定时任务", NotificationManager.IMPORTANCE_LOW);
            NotificationManager manager = getSystemService(NotificationManager.class);
            if (manager != null) manager.createNotificationChannel(channel);
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        lastNotificationSummary = ScheduleStore.summary(this);
        startForeground(NOTIFICATION_ID, notification());
        if (!ticking) {
            ticking = true;
            handler.post(tick);
        }
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        ticking = false;
        handler.removeCallbacks(tick);
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    /** 到点检查：允许补发几分钟内错过的触发（休眠或卡顿会把 tick 推后）。 */
    private void fireDue() {
        List<ScheduleTask> tasks = ScheduleStore.load(this);
        long nowMillis = System.currentTimeMillis();
        boolean firedAny = false;
        for (ScheduleTask task : tasks) {
            if (!task.enabled || !task.isRunnableAction()) continue;
            String lastKey = ScheduleStore.lastKey(this, task);
            long occurrenceAt = ScheduleExecutionTime.dueOccurrenceAt(
                    nowMillis, task.triggerMinutes(), CATCH_UP_MINUTES, lastKey);
            if (occurrenceAt == ScheduleExecutionTime.NO_OCCURRENCE) continue;
            String key = ScheduleStore.triggerKeyFor(occurrenceAt);
            // Persist the exact occurrence before any module request; the monotonic key also
            // prevents a wall-clock rollback from dispatching an earlier occurrence again.
            if (!ScheduleStore.markFired(this, task, key)) continue;
            String[] targets = ModuleActions.resolveTargets(task.target, task.action, task.isLegacyDaily());
            if (targets.length == 0) targets = new String[]{"invalid_target"};
            ScheduleExecution execution = ScheduleExecution.create(task.id, task.action, nowMillis,
                    targets, ScheduleExecution.DEFAULT_TIMEOUT_MS);
            ScheduleExecutionStore.save(this, execution);
            for (String targetId : targets) {
                if (!ModuleActions.isScheduledTarget(targetId, task.action, task.isLegacyDaily())) continue;
                try {
                    ModuleObservationReader.Baseline baseline =
                            ModuleObservationReader.capture(this, targetId, task.action);
                    execution.setBaseline(targetId, baseline.marker, baseline.sequence, baseline.packageName);
                } catch (RuntimeException ignored) {
                    // Dispatch remains available even if a read-only baseline cannot be captured.
                }
            }
            ScheduleExecutionStore.save(this, execution);
            if (task.needsGameForeground()) GameForeground.bringToFront(this);
            for (String targetId : targets) {
                long dispatchAt = System.currentTimeMillis();
                if (!ModuleActions.isScheduledTarget(targetId, task.action, task.isLegacyDaily())) {
                    ScheduleExecution.ReasonCode reason = ModuleActions.isModuleId(targetId)
                            ? ScheduleExecution.ReasonCode.UNSUPPORTED_ACTION
                            : ScheduleExecution.ReasonCode.INVALID_TARGET;
                    execution.recordDispatch(targetId, false,
                            reason, dispatchAt);
                } else {
                    ModuleActions.DispatchResult result =
                            ModuleActions.dispatchTarget(this, targetId, task.action,
                                    task.isLegacyDaily());
                    execution.setPackageName(targetId, result.packageName);
                    execution.recordDispatch(targetId, result.accepted, result.reasonCode,
                            dispatchAt);
                }
                // Save each target immediately; a process death must not trigger a retry.
                ScheduleExecutionStore.save(this, execution);
            }
            writeLog(task, execution, key);
            firedAny = true;
        }
        if (firedAny) {
            ScheduleStore.pruneFired(this);
            updateNotification();
        }
        if (!ScheduleStore.hasEnabled(this)) {
            stopSelf();
        }
    }

    private void pollExecutions() {
        long now = System.currentTimeMillis();
        for (ScheduleExecution execution : ScheduleExecutionStore.loadAll(this)) {
            boolean changed = false;
            for (ScheduleExecution.TargetResult target : execution.targets()) {
                if (target.dispatchState != ScheduleExecution.DispatchState.ACCEPTED
                        || target.confirmationState != ScheduleExecution.ConfirmationState.PENDING) continue;
                try {
                    ModuleObservationReader.Observation observed =
                            ModuleObservationReader.observe(this, target, now);
                    changed |= execution.recordObservation(target.moduleId, observed.result,
                            observed.reason, observed.observedAtMillis);
                } catch (RuntimeException ignored) {
                    changed |= execution.recordObservation(target.moduleId,
                            ScheduleExecution.ObservationResult.WAIT,
                            ScheduleExecution.ReasonCode.OBSERVATION_MISSING, now);
                }
            }
            changed |= execution.expire(now);
            if (changed) ScheduleExecutionStore.save(this, execution);
        }
    }

    private void updateNotification() {
        String summary = ScheduleStore.summary(this);
        if (summary.equals(lastNotificationSummary)) return;
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager == null) return;
        try {
            lastNotificationSummary = summary;
            manager.notify(NOTIFICATION_ID, notification());
        } catch (RuntimeException ignored) {
            // 通知被系统限制时不影响任务执行。
        }
    }

    private Notification notification() {
        Intent open = new Intent(this, MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP)
                .putExtra(MainActivity.EXTRA_OPEN_MODULE_ID, MainActivity.SCHEDULE_ID);
        PendingIntent pending = PendingIntent.getActivity(this, NOTIFICATION_ID, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(this, CHANNEL)
                : new Notification.Builder(this);
        return builder
                .setContentTitle("定时任务")
                .setContentText(ScheduleStore.summary(this))
                .setSmallIcon(android.R.drawable.ic_menu_recent_history)
                .setContentIntent(pending)
                .setOngoing(true)
                .build();
    }

    /** 触发记录写到应用私有目录，便于事后核对是“没到点”还是“派发失败”。 */
    private void writeLog(ScheduleTask task, ScheduleExecution execution, String key) {
        File file = new File(getFilesDir(), "schedule.log");
        try {
            if (file.length() > LOG_LIMIT) {
                File old = new File(getFilesDir(), "schedule.log.1");
                if (old.exists() && !old.delete()) return;
                if (!file.renameTo(old)) return;
            }
            String line = key
                    + " " + task.scheduleLabel()
                    + " " + task.action
                    + " " + task.target
                    + " " + execution.aggregate().value()
                    + "\n";
            try (FileOutputStream output = new FileOutputStream(file, true)) {
                output.write(line.getBytes("UTF-8"));
                output.getFD().sync();
            }
        } catch (Exception ignored) {
            // 写日志失败不阻塞任务本身。
        }
    }
}
