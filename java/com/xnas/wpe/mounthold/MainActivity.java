package com.xnas.wpe.mounthold;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.os.Build;
import android.os.Bundle;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;

/** Small configuration screen; all preset and memory behavior lives in Rust. */
public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private static final int REQUEST_NOTIFICATIONS = 7403;
    private static final String PREFS = "mount_hold";
    private static final String KEY_SPEED = "speed";
    private EditText speedInput;
    private TextView statusView;
    private Button startButton;
    private Button stopButton;
    private String inactiveStatusOverride;
    private final android.os.Handler handler = new android.os.Handler();

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(Color.rgb(19, 26, 34));
        getWindow().setNavigationBarColor(Color.rgb(19, 26, 34));
        setContentView(buildContent());
    }

    @Override
    protected void onResume() {
        super.onResume();
        refreshState();
        handler.postDelayed(refreshTask, 500L);
    }

    @Override
    protected void onPause() {
        handler.removeCallbacks(refreshTask);
        super.onPause();
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(requestCode, permissions, results);
        if (requestCode == REQUEST_NOTIFICATIONS) {
            startWorker();
        }
    }

    private View buildContent() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER_HORIZONTAL);
        root.setPadding(dp(24), dp(36), dp(24), dp(28));
        root.setBackgroundColor(Color.rgb(19, 26, 34));

        root.addView(label("目标速度"), params(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, 0, 18));
        speedInput = input(getPreferences(0).getString(KEY_SPEED, "5000"), "5000");
        speedInput.setInputType(android.text.InputType.TYPE_CLASS_NUMBER | android.text.InputType.TYPE_NUMBER_FLAG_DECIMAL);
        root.addView(speedInput, matchWrap());

        statusView = text("尚未启动", 14, Color.rgb(180, 213, 208));
        statusView.setGravity(Gravity.CENTER);
        statusView.setPadding(dp(12), dp(12), dp(12), dp(12));
        root.addView(statusView, params(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, 0, 22));

        startButton = button("启动保持");
        startButton.setOnClickListener(view -> requestNotificationThenStart());
        root.addView(startButton, params(ViewGroup.LayoutParams.MATCH_PARENT, dp(52), 0, 12));

        stopButton = button("停止并恢复");
        stopButton.setOnClickListener(view -> requestStop());
        root.addView(stopButton, params(ViewGroup.LayoutParams.MATCH_PARENT, dp(48), 0, 8));

        scroll.addView(root, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return scroll;
    }

    private void requestNotificationThenStart() {
        if (Build.VERSION.SDK_INT >= 33
                && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestModulePermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, REQUEST_NOTIFICATIONS);
        } else {
            startWorker();
        }
    }

    private void startWorker() {
        String packageName = com.xnas.wpe.suite.TargetGame.getSelectedPackage(this);
        if (!com.xnas.wpe.suite.TargetGame.supportsModule(com.xnas.wpe.suite.TargetGame.MOUNT_HOLD, packageName)) {
            setInactiveStatus("目标游戏不支持");
            return;
        }
        inactiveStatusOverride = null;
        String targetSpeed = speedInput.getText().toString().trim();
        getPreferences(0).edit().putString(KEY_SPEED, targetSpeed).apply();
        Intent intent = new Intent(this, MountHoldService.class)
                .setAction(MountHoldService.ACTION_START)
                .putExtra(MountHoldService.EXTRA_PACKAGE, packageName)
                .putExtra(MountHoldService.EXTRA_SPEED, targetSpeed);
        try {
            if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent);
            else startService(intent);
            statusView.setText("正在启动 Root worker…");
            moveTaskToBack(true);
        } catch (RuntimeException error) {
            setInactiveStatus("服务启动失败：" + error.getClass().getSimpleName());
        }
        refreshState();
    }

    private void requestStop() {
        String packageName = MountHoldService.getPackageForStop(com.xnas.wpe.suite.TargetGame.getSelectedPackage(this));
        String targetSpeed = getPreferences(0).getString(KEY_SPEED, "5000");
        startService(new Intent(this, MountHoldService.class)
                .setAction(MountHoldService.ACTION_STOP)
                .putExtra(MountHoldService.EXTRA_PACKAGE, packageName)
                .putExtra(MountHoldService.EXTRA_SPEED, targetSpeed));
        statusView.setText("正在安全恢复…");
    }

    private final Runnable refreshTask = new Runnable() {
        @Override public void run() {
            refreshState();
            handler.postDelayed(this, 500L);
        }
    };

    private void refreshState() {
        boolean active = MountHoldService.isActive();
        if (statusView != null) {
            statusView.setText(!active && inactiveStatusOverride != null
                    ? inactiveStatusOverride : MountHoldService.getStatusText());
        }
        if (startButton != null) startButton.setEnabled(!active);
        if (stopButton != null) stopButton.setEnabled(active);
    }

    private void setInactiveStatus(String value) {
        inactiveStatusOverride = value;
        if (statusView != null) statusView.setText(value);
    }

    @Override public ModuleStatus getModuleStatus() {
        String target = MountHoldService.getPackageForStop(com.xnas.wpe.suite.TargetGame.getSelectedPackage(this));
        String detail = MountHoldService.getStatusText();
        boolean active = MountHoldService.isActive();
        String safeDetail = detail == null || detail.trim().isEmpty() ? "服务状态文本未提供。" : detail.trim();
        ModuleStatus.State state;
        String label;
        if (!active) {
            boolean unresolved = safeDetail.contains("恢复失败") || safeDetail.contains("未确认恢复")
                    || safeDetail.contains("未确认") || safeDetail.contains("状态未确认");
            state = unresolved ? ModuleStatus.State.UNKNOWN : ModuleStatus.State.STOPPED;
            label = unresolved ? "状态未确认" : "未运行";
        } else if (safeDetail.contains("等待") || safeDetail.contains("定位") || safeDetail.contains("启动")) {
            state = ModuleStatus.State.WAITING;
            label = "等待目标";
        } else if (safeDetail.contains("恢复") || safeDetail.contains("停止")) {
            state = ModuleStatus.State.STOPPING;
            label = "恢复中";
        } else {
            state = ModuleStatus.State.RUNNING;
            label = "服务运行中";
        }
        return new ModuleStatus(state, label, target,
                safeDetail + "；服务活动状态=" + (active ? "是" : "否") + "，目标绑定未确认。",
                active || state == ModuleStatus.State.UNKNOWN ? "服务运行不等同于已绑定游戏/角色；如需停止，请在功能页点击“停止并恢复”。"
                        : "选择目标游戏和速度后，点击“启动保持”；运行需要模拟器 Root 权限。");
    }

    private EditText input(String value, String hint) {
        EditText field = new EditText(this);
        field.setSingleLine(true);
        field.setText(value);
        field.setHint(hint);
        field.setTextColor(Color.WHITE);
        field.setHintTextColor(Color.rgb(125, 145, 158));
        field.setPadding(dp(12), dp(8), dp(12), dp(8));
        field.setBackgroundColor(Color.rgb(37, 49, 60));
        return field;
    }

    private TextView label(String value) { return text(value, 14, Color.rgb(190, 205, 215)); }

    private TextView text(String value, int size, int color) {
        TextView view = new TextView(this);
        view.setText(value);
        view.setTextSize(size);
        view.setTextColor(color);
        return view;
    }

    private Button button(String value) {
        Button button = new Button(this);
        button.setText(value);
        button.setAllCaps(false);
        return button;
    }

    private LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private LinearLayout.LayoutParams params(int width, int height, int left, int top) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(width, height);
        params.setMargins(left, top, 0, 0);
        return params;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
}
