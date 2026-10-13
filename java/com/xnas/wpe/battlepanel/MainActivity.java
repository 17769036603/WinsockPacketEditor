package com.xnas.wpe.battlepanel;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;

/** User-facing entry point for the independent battle status overlay. */
public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private static final int REQUEST_NOTIFICATIONS = 7402;
    private static final long SERVICE_STATE_REFRESH_MS = 500L;
    private static final String STATE_WAITING_FOR_OVERLAY_PERMISSION = "waiting_for_overlay_permission";
    private static final String STATE_WAITING_FOR_NOTIFICATION_PERMISSION = "waiting_for_notification_permission";

    private final Handler handler = new Handler(Looper.getMainLooper());
    private boolean waitingForOverlayPermission;
    private boolean waitingForNotificationPermission;
    private TextView statusView;
    private Button startButton;
    private Button stopButton;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (savedInstanceState != null) {
            waitingForOverlayPermission = savedInstanceState.getBoolean(
                    STATE_WAITING_FOR_OVERLAY_PERMISSION);
            waitingForNotificationPermission = savedInstanceState.getBoolean(
                    STATE_WAITING_FOR_NOTIFICATION_PERMISSION);
        }
        getWindow().setStatusBarColor(Color.rgb(13, 20, 29));
        getWindow().setNavigationBarColor(Color.rgb(13, 20, 29));
        setContentView(buildContent());
        refreshState();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (waitingForOverlayPermission) {
            waitingForOverlayPermission = false;
            if (Settings.canDrawOverlays(this)) {
                requestNotificationsThenStart();
            } else {
                setStatus("尚未授予悬浮窗权限。授权后点“启动悬浮面板”继续。", false);
            }
        } else if (!waitingForNotificationPermission) {
            refreshState();
        }
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        outState.putBoolean(STATE_WAITING_FOR_OVERLAY_PERMISSION, waitingForOverlayPermission);
        outState.putBoolean(STATE_WAITING_FOR_NOTIFICATION_PERMISSION,
                waitingForNotificationPermission);
        super.onSaveInstanceState(outState);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions,
                                           int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQUEST_NOTIFICATIONS && waitingForNotificationPermission) {
            waitingForNotificationPermission = false;
            boolean granted = grantResults.length > 0
                    && grantResults[0] == android.content.pm.PackageManager.PERMISSION_GRANTED;
            if (!granted) {
                setStatus("通知权限未授予；悬浮面板仍会启动，服务状态可在应用和系统任务管理中查看。",
                        false);
            }
            startOverlayService();
        }
    }

    private View buildContent() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(20), dp(20), dp(20));
        root.setBackgroundColor(Color.rgb(13, 20, 29));
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            int top = Math.max(dp(20), insets.getSystemWindowInsetTop() + dp(10));
            int bottom = Math.max(dp(20), insets.getSystemWindowInsetBottom() + dp(10));
            view.setPadding(dp(20), top, dp(20), bottom);
            return insets;
        });

        statusView = text("尚未启动", 13f, Color.rgb(199, 219, 224));
        statusView.setGravity(Gravity.CENTER_VERTICAL);
        statusView.setPadding(dp(12), dp(9), dp(12), dp(9));
        statusView.setBackground(roundBackground(Color.rgb(33, 51, 65),
                Color.rgb(81, 124, 139), dp(10), dp(1)));
        root.addView(statusView, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 16, 0, 0));

        startButton = button("启动悬浮面板", Color.rgb(23, 112, 91));
        startButton.setOnClickListener(view -> requestOverlayPermissionThenStart());
        root.addView(startButton, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                dp(52), 0, 14, 0, 0));

        stopButton = button("停止悬浮面板", Color.rgb(89, 53, 53));
        stopButton.setOnClickListener(view -> stopOverlayService());
        root.addView(stopButton, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                dp(48), 0, 10, 0, 0));

        scroll.addView(root, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return scroll;
    }

    private void requestOverlayPermissionThenStart() {
        if (Settings.canDrawOverlays(this)) {
            requestNotificationsThenStart();
            return;
        }
        waitingForOverlayPermission = true;
        setStatus("请在系统页面开启“允许显示在其他应用上层”，返回后会继续启动。", false);
        try {
            Intent permission = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                    Uri.parse("package:" + getPackageName()));
            startActivity(permission);
        } catch (RuntimeException error) {
            waitingForOverlayPermission = false;
            setStatus("无法打开系统悬浮窗授权页面，请到应用信息中手动开启权限。", false);
        }
    }

    private void requestNotificationsThenStart() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                && checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)
                != android.content.pm.PackageManager.PERMISSION_GRANTED) {
            waitingForNotificationPermission = true;
            requestModulePermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},
                    REQUEST_NOTIFICATIONS);
            return;
        }
        startOverlayService();
    }

    private void startOverlayService() {
        if (!Settings.canDrawOverlays(this)) {
            setStatus("缺少悬浮窗权限，尚未启动服务。", false);
            return;
        }
        Intent service = new Intent(this, BattleOverlayService.class)
                .setAction(BattleOverlayService.ACTION_START)
                .putExtra(BattleOverlayService.EXTRA_PACKAGE,
                        com.xnas.wpe.suite.TargetGame.getSelectedPackage(this));
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(service);
            } else {
                startService(service);
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                    && checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)
                    != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                setStatus("正在启动悬浮面板。通知权限未授予；服务仍会运行，但通知栏状态可能不可见。",
                        false);
            } else {
                setStatus("正在启动前台读取服务与悬浮球…", false);
            }
            handler.postDelayed(this::refreshState, 500L);
            moveTaskToBack(true);
        } catch (RuntimeException error) {
            refreshState();
            setStatus("服务启动失败，请检查通知权限和系统后台运行设置。", false);
        }
    }

    private void stopOverlayService() {
        stopService(new Intent(this, BattleOverlayService.class));
        setStatus("已请求停止悬浮面板和只读读取器。", false);
        handler.postDelayed(this::refreshState, SERVICE_STATE_REFRESH_MS);
    }

    private void refreshState() {
        boolean running = BattleOverlayService.isActive();
        if (startButton != null) {
            startButton.setEnabled(!running);
            startButton.setText(running ? "悬浮面板已启动" : "启动悬浮面板");
        }
        if (stopButton != null) {
            stopButton.setEnabled(running);
        }
        if (running) {
            if (!BattleOverlayService.isOverlayVisible()) {
                setStatus("前台读取服务运行中；等待 Root 授权和首次读取后显示悬浮面板。",
                        true);
            } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                    && checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)
                    != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                setStatus("前台服务运行中；通知权限未授予，系统通知栏状态可能不可见。",
                        true);
            } else {
                setStatus("前台服务运行中；展开悬浮球可查看双方各十槽数据。", true);
            }
        } else if (Settings.canDrawOverlays(this)) {
            setStatus("悬浮窗权限已就绪，点击启动。", false);
        } else {
            setStatus("尚未授予悬浮窗权限。", false);
        }
    }

    private void setStatus(String value, boolean running) {
        if (statusView != null) {
            statusView.setText(value);
            statusView.setTextColor(running
                    ? Color.rgb(179, 244, 210) : Color.rgb(199, 219, 224));
        }
    }

    @Override public ModuleStatus getModuleStatus() {
        String target = BattleOverlayService.isActive()
                ? BattleOverlayService.getActiveGamePackage(this)
                : com.xnas.wpe.suite.TargetGame.getSelectedPackage(this);
        boolean active = BattleOverlayService.isActive();
        boolean visible = active && BattleOverlayService.isOverlayVisible();
        ModuleStatus.State state = active ? ModuleStatus.State.RUNNING : ModuleStatus.State.STOPPED;
        String label = !active ? "未启动" : (visible ? "服务运行中" : "服务运行中，等待面板");
        String detail = !active ? "前台读取服务未运行。" : visible
                ? "读取服务与悬浮面板处于活动状态；目标游戏读取器的绑定/有效战斗帧未由主界面确认。"
                : "前台读取服务处于活动状态，但悬浮面板尚未显示；目标游戏绑定与有效战斗帧未确认。";
        return new ModuleStatus(state, label, target, detail,
                active ? "服务活动不等于已绑定目标游戏或存在有效战斗帧；打开功能页查看读取器提示。"
                        : "授予所需权限后，在功能页手动启动观察面板。");
    }

    private Button button(String label, int fill) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        button.setTextSize(15f);
        button.setTextColor(Color.WHITE);
        button.setBackground(roundBackground(fill, Color.rgb(120, 177, 187), dp(12), dp(1)));
        return button;
    }

    private TextView text(String value, float size, int color) {
        TextView view = new TextView(this);
        view.setText(value);
        view.setTextSize(size);
        view.setTextColor(color);
        view.setIncludeFontPadding(false);
        return view;
    }

    private GradientDrawable roundBackground(int fill, int stroke, int radius, int strokeWidth) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(fill);
        drawable.setCornerRadius(radius);
        drawable.setStroke(strokeWidth, stroke);
        return drawable;
    }

    private LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private LinearLayout.LayoutParams marginParams(int width, int height,
                                                   int left, int top, int right, int bottom) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(width, height);
        params.setMargins(dp(left), dp(top), dp(right), dp(bottom));
        return params;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }
}
