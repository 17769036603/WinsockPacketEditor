package com.xnas.wpe.battlepanel;

import android.content.ActivityNotFoundException;
import android.content.ComponentName;
import android.content.Intent;
import android.content.ServiceConnection;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.xnas.wpe.suite.GlassSkin;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;
import com.xnas.wpe.suite.TargetGame;

/** Two-button control page for the service-owned battle overlay. */
public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private static final int REQUEST_OVERLAY_PERMISSION = 7401;
    private static final int REQUEST_NOTIFICATIONS = 7402;
    private static final long SERVICE_STATE_REFRESH_MS = 500L;
    private static final long IDLE_STATE_REFRESH_MS = 2000L;
    private static final String STATE_WAITING_FOR_OVERLAY_PERMISSION =
            "waiting_for_overlay_permission";
    private static final String STATE_WAITING_FOR_NOTIFICATION_PERMISSION =
            "waiting_for_notification_permission";

    private final Handler handler = new Handler(Looper.getMainLooper());
    private long refreshDelayMs = IDLE_STATE_REFRESH_MS;
    private final Runnable refreshTask = new Runnable() {
        @Override public void run() {
            refreshState();
            if (resumed) handler.postDelayed(this, refreshDelayMs);
        }
    };
    private final ServiceConnection serviceConnection = new ServiceConnection() {
        @Override public void onServiceConnected(ComponentName name, IBinder binder) {
            if (binder instanceof BattleOverlayService.LocalBinder) {
                service = ((BattleOverlayService.LocalBinder) binder).getService();
            }
            refreshState();
        }

        @Override public void onServiceDisconnected(ComponentName name) {
            service = null;
            refreshState();
        }
    };

    private boolean waitingForOverlayPermission;
    private boolean waitingForNotificationPermission;
    private boolean bound;
    private boolean resumed;
    private BattleOverlayService service;
    private TextView statusView;
    private Button startButton;
    private Button stopButton;
    private String gateMessage;

    @Override protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (savedInstanceState != null) {
            waitingForOverlayPermission = savedInstanceState.getBoolean(
                    STATE_WAITING_FOR_OVERLAY_PERMISSION);
            waitingForNotificationPermission = savedInstanceState.getBoolean(
                    STATE_WAITING_FOR_NOTIFICATION_PERMISSION);
        }
        getWindow().setStatusBarColor(GlassSkin.PAGE_BG);
        getWindow().setNavigationBarColor(GlassSkin.PAGE_BG);
        setContentView(buildContent());
        refreshState();
    }

    @Override protected void onStart() {
        super.onStart();
        if (BattleOverlayService.isActive()) bindToStartedService();
    }

    @Override protected void onResume() {
        super.onResume();
        resumed = true;
        handler.removeCallbacks(refreshTask);
        handler.post(refreshTask);
    }

    @Override protected void onPause() {
        resumed = false;
        handler.removeCallbacks(refreshTask);
        super.onPause();
    }

    @Override protected void onStop() {
        handler.removeCallbacks(refreshTask);
        if (bound) {
            unbindService(serviceConnection);
            bound = false;
            service = null;
        }
        super.onStop();
    }

    @Override protected void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        super.onDestroy();
    }

    @Override protected void onSaveInstanceState(Bundle outState) {
        outState.putBoolean(STATE_WAITING_FOR_OVERLAY_PERMISSION, waitingForOverlayPermission);
        outState.putBoolean(STATE_WAITING_FOR_NOTIFICATION_PERMISSION,
                waitingForNotificationPermission);
        super.onSaveInstanceState(outState);
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQUEST_OVERLAY_PERMISSION && waitingForOverlayPermission) {
            waitingForOverlayPermission = false;
            gateMessage = Build.VERSION.SDK_INT < Build.VERSION_CODES.M
                    || Settings.canDrawOverlays(this)
                    ? "悬浮窗权限已授予，请再次点击启动读取。"
                    : "未授予悬浮窗权限，读取未启动。";
            refreshState();
        }
    }

    @Override public void onRequestPermissionsResult(int requestCode, String[] permissions,
                                                      int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQUEST_NOTIFICATIONS && waitingForNotificationPermission) {
            waitingForNotificationPermission = false;
            if (grantResults.length == 0
                    || grantResults[0] != PackageManager.PERMISSION_GRANTED) {
                gateMessage = "通知权限未授予，正在尝试启动读取。";
            }
            startReaderService();
        }
    }

    private View buildContent() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER_HORIZONTAL | Gravity.CENTER_VERTICAL);
        root.setPadding(dp(GlassSkin.PADDING_H_DP), dp(GlassSkin.PADDING_V_DP),
                dp(GlassSkin.PADDING_H_DP), dp(GlassSkin.PADDING_V_DP));
        root.setBackground(GlassSkin.page(this));

        statusView = new TextView(this);
        statusView.setTextSize(GlassSkin.TEXT_BODY_SP);
        statusView.setTextColor(Color.rgb(211, 226, 236));
        statusView.setGravity(Gravity.CENTER_VERTICAL);
        statusView.setIncludeFontPadding(false);
        statusView.setPadding(dp(10), dp(7), dp(10), dp(7));
        statusView.setMinHeight(dp(36));
        GlassSkin.apply(statusView, GlassSkin.surface(this, GlassSkin.RADIUS_CONTAINER_DP));
        LinearLayout.LayoutParams statusParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        statusParams.setMargins(0, dp(10), 0, dp(10));


        LinearLayout controls = new LinearLayout(this);
        controls.setOrientation(LinearLayout.VERTICAL);
        controls.setGravity(Gravity.CENTER_VERTICAL);
        startButton = button("启动读取", GlassSkin.ACTION_START);
        startButton.setContentDescription("启动读取");
        startButton.setOnClickListener(view -> requestOverlayPermissionThenStart());
        stopButton = button("停止读取", GlassSkin.ACTION_STOP);
        stopButton.setTextSize(GlassSkin.TEXT_BUTTON_COMPACT_SP);
        stopButton.setContentDescription("停止读取");
        stopButton.setOnClickListener(view -> stopReaderService());
        LinearLayout.LayoutParams buttonParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(GlassSkin.BUTTON_HEIGHT_DP));
        buttonParams.setMargins(0, 0, 0, dp(8));
        controls.addView(startButton, buttonParams);
        LinearLayout.LayoutParams stopParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(GlassSkin.BUTTON_HEIGHT_DP));
        
        controls.addView(stopButton, stopParams);
        root.addView(controls, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return root;
    }

    private void requestOverlayPermissionThenStart() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !Settings.canDrawOverlays(this)) {
            waitingForOverlayPermission = true;
            gateMessage = "请授予悬浮窗权限，返回后再次点击启动读取。";
            Intent settings = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                    Uri.parse("package:" + getPackageName()));
            try {
                startActivityForResult(settings, REQUEST_OVERLAY_PERMISSION);
            } catch (ActivityNotFoundException error) {
                waitingForOverlayPermission = false;
                gateMessage = "系统无法打开悬浮窗授权页面，读取未启动。";
                refreshState();
            }
            return;
        }
        gateMessage = null;
        requestNotificationsThenStart();
    }

    private void requestNotificationsThenStart() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                && checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)
                != PackageManager.PERMISSION_GRANTED) {
            waitingForNotificationPermission = true;
            gateMessage = "等待通知权限";
            requestModulePermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},
                    REQUEST_NOTIFICATIONS);
            return;
        }
        startReaderService();
    }

    private void startReaderService() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !Settings.canDrawOverlays(this)) {
            gateMessage = "未授予悬浮窗权限，读取未启动。";
            refreshState();
            return;
        }
        gateMessage = null;
        Intent intent = new Intent(this, BattleOverlayService.class)
                .setAction(BattleOverlayService.ACTION_START)
                .putExtra(BattleOverlayService.EXTRA_PACKAGE, TargetGame.getSelectedPackage(this));
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) startForegroundService(intent);
            else startService(intent);
            bindToStartedService();
            setStatus("正在启动读取", BattleFrameProtocol.STATUS_LOCATING);
            handler.postDelayed(this::refreshState, SERVICE_STATE_REFRESH_MS);
        } catch (RuntimeException error) {
            gateMessage = "读取服务启动失败。";
            setStatus(gateMessage, BattleFrameProtocol.STATUS_READ_ERROR);
        }
    }

    private void bindToStartedService() {
        if (bound) return;
        Intent intent = new Intent(this, BattleOverlayService.class);
        bound = bindService(intent, serviceConnection, 0);
    }

    private void stopReaderService() {
        gateMessage = null;
        try {
            if (service != null) {
                service.requestStop();
            } else if (BattleOverlayService.isActive()) {
                Intent intent = new Intent(this, BattleOverlayService.class)
                        .setAction(BattleOverlayService.ACTION_STOP);
                startService(intent);
            } else {
                stopService(new Intent(this, BattleOverlayService.class));
            }
            setStatus("已请求停止读取", BattleFrameProtocol.STATUS_LOCATING);
            handler.postDelayed(this::refreshState, SERVICE_STATE_REFRESH_MS);
        } catch (RuntimeException error) {
            gateMessage = "停止读取请求失败。";
            setStatus(gateMessage, BattleFrameProtocol.STATUS_READ_ERROR);
        }
    }

    private void refreshState() {
        boolean running = BattleOverlayService.isActive();
        if (service == null && running) bindToStartedService();
        BattleOverlayService.DisplayState state = service == null ? null : service.getDisplayState();
        if (state == null) {
            running = BattleOverlayService.isActive();
            refreshDelayMs = running ? SERVICE_STATE_REFRESH_MS : IDLE_STATE_REFRESH_MS;
            setStatus(running ? "正在连接读取服务" :
                    gateMessage == null ? "读取未启动" : gateMessage,
                    running ? BattleFrameProtocol.STATUS_LOCATING
                            : gateMessage == null ? BattleFrameProtocol.STATUS_LOCATING
                            : BattleFrameProtocol.STATUS_READ_ERROR);
            if (startButton != null) startButton.setEnabled(!running);
            if (stopButton != null) stopButton.setEnabled(running);
            return;
        }

        running = state.active;
        refreshDelayMs = running ? SERVICE_STATE_REFRESH_MS : IDLE_STATE_REFRESH_MS;
        String status;
        if (!running && state.status != BattleFrameProtocol.STATUS_READ_ERROR) {
            status = gateMessage == null ? "读取未启动" : gateMessage;
        } else {
            status = state.label == null ? "未知状态" : state.label;
            if (state.detail != null && !state.detail.isEmpty()) status += " · " + state.detail;
        }
        setStatus(compactMessage(status, 96), state.status);
        if (startButton != null) startButton.setEnabled(!running);
        if (stopButton != null) stopButton.setEnabled(running);
    }

    private void setStatus(String value, int status) {
        if (statusView == null) return;
        statusView.setText(value);
        statusView.setTextColor(statusColor(status));
    }

    private int statusColor(int status) {
        switch (status) {
            case BattleFrameProtocol.STATUS_LIVE:
                return Color.rgb(180, 244, 212);
            case BattleFrameProtocol.STATUS_ROOT_DENIED:
            case BattleFrameProtocol.STATUS_READ_ERROR:
                return Color.rgb(255, 190, 178);
            case BattleFrameProtocol.STATUS_EXPIRED:
                return Color.rgb(255, 218, 155);
            default:
                return Color.rgb(211, 226, 236);
        }
    }

    @Override public ModuleStatus getModuleStatus() {
        BattleOverlayService.DisplayState state = service == null ? null : service.getDisplayState();
        boolean active = state == null ? BattleOverlayService.isActive() : state.active;
        String target = state != null && state.gamePackage != null ? state.gamePackage
                : active ? BattleOverlayService.getActiveGamePackage(this)
                : TargetGame.getSelectedPackage(this);
        String detail = state != null ? state.detail : active
                ? "读取服务运行中；页面状态尚未连接确认。" : "读取服务未运行。";
        return new ModuleStatus(active ? ModuleStatus.State.RUNNING : ModuleStatus.State.STOPPED,
                active ? "读取中" : "未启动", target, detail,
                active ? "服务活动不等于已绑定目标游戏或存在有效战斗帧。"
                        : "在功能页手动启动读取。");
    }

    private Button button(String label, int fill) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        button.setTextSize(GlassSkin.TEXT_BUTTON_SP);
        button.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        button.setTextColor(GlassSkin.actionText());
        button.setMinHeight(0);
        button.setMinimumHeight(0);
        button.setMinWidth(0);
        button.setMinimumWidth(0);
        button.setPadding(dp(8), 0, dp(8), 0);
        GlassSkin.apply(button, GlassSkin.actionTinted(this, GlassSkin.RADIUS_CONTROL_DP, fill));
        return button;
    }

    private String compactMessage(String value, int maximumLength) {
        if (value == null || value.length() <= maximumLength) return value;
        return value.substring(0, Math.max(0, maximumLength - 1)) + "…";
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }
}
