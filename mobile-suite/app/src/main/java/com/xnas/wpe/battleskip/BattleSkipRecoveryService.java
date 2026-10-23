package com.xnas.wpe.battleskip;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Intent;
import android.os.Build;
import android.os.IBinder;

import com.xnas.wpe.suite.BattleConflictGate;
import com.xnas.wpe.suite.ModuleService;

import java.io.File;
import java.util.concurrent.TimeUnit;

/** User-initiated, process-identity-only reconciliation; never launches battle assistance. */
public final class BattleSkipRecoveryService extends ModuleService {
    private static final String CHANNEL = "battle_skip_recovery";
    private static final int NOTIFICATION_ID = 6104;
    private static volatile boolean checking;
    private volatile Process checker;
    private volatile boolean destroyed;
    private Thread checkThread;

    public static boolean isChecking() { return checking; }

    @Override public void onCreate() {
        super.onCreate();
        if (Build.VERSION.SDK_INT >= 26) {
            getSystemService(NotificationManager.class).createNotificationChannel(
                    new NotificationChannel(CHANNEL, "战斗快进", NotificationManager.IMPORTANCE_LOW));
        }
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        startForeground(NOTIFICATION_ID, notification());
        if (checking) return START_NOT_STICKY;
        if (BattleConflictGate.isClean(getFilesDir().getParentFile(), "battleskip")) {
            BattleConflictGate.release(getFilesDir().getParentFile(), "battleskip");
            finishCheck();
            return START_NOT_STICKY;
        }
        checking = true;
        File directory = getFilesDir();
        String binary = new File(getApplicationInfo().nativeLibraryDir,
                "libbattle_skip_worker.so").getAbsolutePath();
        checkThread = new Thread(() -> {
            try {
                String command = com.xnas.wpe.suite.NativeWorkerCommand.withLibraryPath(
                        getApplicationInfo().nativeLibraryDir, quote(binary)
                                + " --reconcile --dir " + quote(directory.getAbsolutePath()));
                Process process = new ProcessBuilder("su", "-c", command)
                        .redirectErrorStream(true)
                        .redirectOutput(new File(directory, "recovery-worker.log")).start();
                checker = process;
                if (destroyed) process.destroy();
                if (!process.waitFor(45, TimeUnit.SECONDS)) process.destroy();
                if (!destroyed && !process.isAlive()) {
                    BattleConflictGate.release(directory.getParentFile(), "battleskip");
                }
            } catch (Exception ignored) {
                // Missing permission, launch failure or timeout never supplies cleanup evidence.
            } finally {
                checker = null;
                if (!destroyed) new android.os.Handler(getMainLooper()).post(this::finishCheck);
            }
        }, "battle-skip-identity-check");
        checkThread.start();
        return START_NOT_STICKY;
    }

    private void finishCheck() {
        checking = false;
        stopForeground(true);
        stopSelf();
    }

    private Notification notification() {
        Intent open = new Intent(this, com.xnas.wpe.suite.MainActivity.class)
                .putExtra(com.xnas.wpe.suite.MainActivity.EXTRA_OPEN_MODULE_ID, "battleskip");
        PendingIntent pending = PendingIntent.getActivity(this, NOTIFICATION_ID, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(this, CHANNEL) : new Notification.Builder(this);
        return builder.setSmallIcon(android.R.drawable.ic_menu_info_details)
                .setContentTitle("飘渺助手 · 战斗快进")
                .setContentText("正在确认上次任务是否退出")
                .setContentIntent(pending).setOngoing(true).build();
    }

    private static String quote(String value) { return "'" + value.replace("'", "'\\''") + "'"; }

    @Override public void onDestroy() {
        destroyed = true;
        checking = false;
        Process process = checker;
        if (process != null) process.destroy();
        if (checkThread != null) checkThread.interrupt();
        stopForeground(true);
        super.onDestroy();
    }

    @Override public IBinder onBind(Intent intent) { return null; }
}
