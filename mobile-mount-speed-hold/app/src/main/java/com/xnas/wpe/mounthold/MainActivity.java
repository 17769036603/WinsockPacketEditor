package com.xnas.wpe.mounthold;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.ArrayAdapter;
import android.widget.Spinner;
import android.widget.TextView;

/** Small configuration screen; all preset and memory behavior lives in Rust. */
public final class MainActivity extends Activity {
    private static final int REQUEST_NOTIFICATIONS = 7403;
    private static final String PREFS = "mount_hold";
    private static final String KEY_PACKAGE = "package";
    private static final String KEY_SPEED = "speed";
    private static final String[] GAME_PACKAGES = {"com.gdoo.dhxy", "com.gdoo.xxxy2026"};

    private Spinner packageInput;
    private EditText speedInput;
    private TextView statusView;
    private Button startButton;
    private Button stopButton;
    private boolean waitingForOverlayPermission;
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
        if (waitingForOverlayPermission) {
            waitingForOverlayPermission = false;
            if (Settings.canDrawOverlays(this)) {
                requestNotificationThenStart();
            } else if (statusView != null) {
                setInactiveStatus("悬浮窗权限未授予，点“启动保持”重试。");
            }
        } else {
            refreshState();
        }
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

        TextView eyebrow = text("独立模拟器工具 · Rust worker", 13, Color.rgb(118, 190, 190));
        eyebrow.setGravity(Gravity.CENTER);
        root.addView(eyebrow, matchWrap());

        TextView title = text("角色速度保持", 28, Color.WHITE);
        title.setGravity(Gravity.CENTER);
        title.setTypeface(android.graphics.Typeface.DEFAULT, android.graphics.Typeface.BOLD);
        root.addView(title, params(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, 0, 10));

        TextView intro = text("按输入的目标值保持自己角色的移动速度；无需骑乘，未找到角色时继续等待。", 15, Color.rgb(188, 204, 215));
        intro.setGravity(Gravity.CENTER);
        intro.setLineSpacing(dp(5), 1f);
        root.addView(intro, params(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, 0, 12));

        root.addView(label("游戏包名（精确匹配）"), params(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, 0, 28));
        packageInput = new Spinner(this);
        ArrayAdapter<String> packageAdapter = new ArrayAdapter<String>(this,
                android.R.layout.simple_spinner_item, GAME_PACKAGES) {
            @Override public View getView(int position, View recycled, ViewGroup parent) {
                TextView item = (TextView) super.getView(position, recycled, parent);
                item.setTextColor(Color.WHITE);
                return item;
            }
        };
        packageAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        packageInput.setAdapter(packageAdapter);
        String savedPackage = getPreferences(0).getString(KEY_PACKAGE, GAME_PACKAGES[0]);
        packageInput.setSelection(savedPackage.equals(GAME_PACKAGES[1]) ? 1 : 0);
        root.addView(packageInput, matchWrap());

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

        TextView note = text("需要模拟器提供 su Root。停止时 Rust 会先尝试恢复原值；如果目标值已被外部改动，会保留恢复记录并停止。", 12, Color.rgb(137, 157, 171));
        note.setGravity(Gravity.CENTER);
        note.setLineSpacing(dp(4), 1f);
        root.addView(note, params(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, 0, 20));

        scroll.addView(root, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return scroll;
    }

    private void requestNotificationThenStart() {
        if (!Settings.canDrawOverlays(this)) {
            waitingForOverlayPermission = true;
            setInactiveStatus("请开启“允许显示在其他应用上层”，返回后会继续启动。");
            try {
                startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                        Uri.parse("package:" + getPackageName())));
            } catch (RuntimeException error) {
                waitingForOverlayPermission = false;
                setInactiveStatus("无法打开悬浮窗授权页面，请到应用信息中手动开启。");
            }
            return;
        }
        if (Build.VERSION.SDK_INT >= 33
                && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, REQUEST_NOTIFICATIONS);
        } else {
            startWorker();
        }
    }

    private void startWorker() {
        if (!Settings.canDrawOverlays(this)) {
            setInactiveStatus("缺少悬浮窗权限，尚未启动保持服务。");
            return;
        }
        inactiveStatusOverride = null;
        String packageName = String.valueOf(packageInput.getSelectedItem());
        String targetSpeed = speedInput.getText().toString().trim();
        getPreferences(0).edit().putString(KEY_PACKAGE, packageName).putString(KEY_SPEED, targetSpeed).apply();
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
        String packageName = getPreferences(0).getString(KEY_PACKAGE, GAME_PACKAGES[0]);
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
