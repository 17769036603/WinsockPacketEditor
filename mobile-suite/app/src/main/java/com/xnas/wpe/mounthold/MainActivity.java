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
import com.xnas.wpe.suite.GlassSkin;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;

/** Small configuration screen; all preset and memory behavior lives in Rust. */
public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private static final int REQUEST_NOTIFICATIONS = 7403;
    private static final String PREFS = "mount_hold";
    private static final String KEY_SPEED = "speed";
    /** 语义配色沿用自动找星/战斗面板的取值：启动为绿、停止为红。 */
    /** 语义配色沿用统一的动作色变量：启动为绿、停止为红。 */
    private static final int COLOR_START = GlassSkin.ACTION_START;
    private static final int COLOR_STOP = GlassSkin.ACTION_STOP;
    private EditText speedInput;
    private TextView statusView;
    private Button startButton;
    private Button stopButton;
    private String inactiveStatusOverride;
    private long refreshDelayMs = 500L;
    private final android.os.Handler handler = new android.os.Handler();

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(GlassSkin.PAGE_BG);
        getWindow().setNavigationBarColor(GlassSkin.PAGE_BG);
        setContentView(buildContent());
    }

    @Override
    protected void onResume() {
        super.onResume();
        refreshState();
        handler.postDelayed(refreshTask, refreshDelayMs);
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
        root.setGravity(Gravity.CENTER_HORIZONTAL | Gravity.CENTER_VERTICAL);
        root.setPadding(dp(GlassSkin.PADDING_H_DP), dp(GlassSkin.PADDING_V_DP),
                dp(GlassSkin.PADDING_H_DP), dp(GlassSkin.PADDING_V_DP));
        root.setBackground(GlassSkin.page(this));

        int contentW = dp(520);
        TextView speedLabel = text("目标速度", 16, Color.rgb(190, 205, 215));
        speedLabel.setGravity(Gravity.CENTER);
        root.addView(speedLabel, params(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, 0, 14));
        speedInput = input(getPreferences(0).getString(KEY_SPEED, "5000"), "5000");
        speedInput.setInputType(android.text.InputType.TYPE_CLASS_NUMBER | android.text.InputType.TYPE_NUMBER_FLAG_DECIMAL);
        speedInput.setGravity(Gravity.CENTER);
        root.addView(speedInput, params(dp(240), ViewGroup.LayoutParams.WRAP_CONTENT, 0, 0));

        statusView = text("尚未启动", GlassSkin.TEXT_BODY_SP, Color.rgb(180, 213, 208));
        statusView.setGravity(Gravity.CENTER);
        statusView.setPadding(dp(12), dp(12), dp(12), dp(12));
        statusView.setVisibility(View.GONE);


        startButton = button("启动保持", COLOR_START, GlassSkin.TEXT_BUTTON_SP);
        startButton.setOnClickListener(view -> requestNotificationThenStart());


        stopButton = button("停止并恢复", COLOR_STOP, GlassSkin.TEXT_BUTTON_COMPACT_SP);
        stopButton.setOnClickListener(view -> requestStop());
                root.addView(startButton, params(dp(240), dp(GlassSkin.BUTTON_HEIGHT_DP), 0, 12));
        root.addView(stopButton, params(dp(240), dp(GlassSkin.BUTTON_HEIGHT_DP), 0, 8));

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
            handler.postDelayed(this, refreshDelayMs);
        }
    };

    private void refreshState() {
        boolean active = MountHoldService.isActive();
        refreshDelayMs = active ? 500L : 1000L;
        if (statusView != null) {
            statusView.setText(!active && inactiveStatusOverride != null
                    ? inactiveStatusOverride : MountHoldService.getStatusText());
            // 空闲且没有异常时不占用页面高度；只有启动中、运行中、清理中或失败时才显示状态行。
            statusView.setVisibility(active || inactiveStatusOverride != null ? View.VISIBLE : View.GONE);
        }
        if (startButton != null) startButton.setEnabled(!active);
        if (stopButton != null) stopButton.setEnabled(active);
    }

    private void setInactiveStatus(String value) {
        inactiveStatusOverride = value;
        if (statusView != null) {
            statusView.setText(value);
            // 失败或"不支持"这类需要用户看到的信息必须立刻显示，不能因为空闲而被收起。
            statusView.setVisibility(View.VISIBLE);
        }
    }

    @Override public ModuleStatus getModuleStatus() {
        String target = MountHoldService.getPackageForStop(com.xnas.wpe.suite.TargetGame.getSelectedPackage(this));
        String detail = MountHoldService.getStatusText();
        boolean active = MountHoldService.isActive();
        String safeDetail = detail == null || detail.trim().isEmpty() ? "服务状态文本未提供。" : detail.trim();
        ModuleStatus.State state;
        String label;
        boolean unresolved = safeDetail.contains("恢复失败") || safeDetail.contains("未确认恢复")
                || safeDetail.contains("未确认") || safeDetail.contains("状态未确认")
                || safeDetail.contains("未能确认") || safeDetail.contains("保留恢复记录")
                || safeDetail.contains("恢复记录保留");
        if (unresolved) {
            state = ModuleStatus.State.UNKNOWN;
            label = "状态未确认";
        } else if (!active) {
            state = ModuleStatus.State.STOPPED;
            label = "未运行";
        } else if ("已停止，未修改速度".equals(completionSummary(safeDetail))
                || "已停止并确认恢复".equals(completionSummary(safeDetail))) {
            state = ModuleStatus.State.WAITING;
            label = "等待任务退出";
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

    private String completionSummary(String detail) {
        int separator = detail.indexOf('：');
        int asciiSeparator = detail.indexOf(':');
        if (separator < 0 || (asciiSeparator >= 0 && asciiSeparator < separator)) {
            separator = asciiSeparator;
        }
        return separator < 0 ? detail : detail.substring(0, separator).trim();
    }

    private EditText input(String value, String hint) {
        EditText field = new EditText(this);
        field.setSingleLine(true);
        field.setText(value);
        field.setHint(hint);
        field.setTextColor(Color.WHITE);
        field.setHintTextColor(Color.rgb(125, 145, 158));
        field.setTypeface(GlassSkin.NUMERIC);
        field.setTextSize(GlassSkin.TEXT_BUTTON_SP);
        field.setPadding(dp(12), dp(8), dp(12), dp(8));
        GlassSkin.apply(field, GlassSkin.input(this, GlassSkin.RADIUS_CONTROL_DP));
        return field;
    }

    private TextView label(String value) {
        return text(value, GlassSkin.TEXT_SECTION_SP, Color.rgb(190, 205, 215));
    }

    private TextView text(String value, float size, int color) {
        TextView view = new TextView(this);
        view.setText(value);
        view.setTextSize(size);
        view.setTextColor(color);
        return view;
    }

    private Button button(String value, int tintColor, float textSize) {
        Button button = new Button(this);
        button.setText(value);
        button.setTextSize(textSize);
        button.setAllCaps(false);
        GlassSkin.apply(button, GlassSkin.actionTinted(this, 8, tintColor));
        button.setTextColor(GlassSkin.actionText());
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
