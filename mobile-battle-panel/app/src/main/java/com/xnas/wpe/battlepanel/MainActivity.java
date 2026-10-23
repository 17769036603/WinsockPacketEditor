package com.xnas.wpe.battlepanel;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
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
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

/** User-facing entry point for the independent battle status overlay. */
public final class MainActivity extends Activity {
    private static final int REQUEST_NOTIFICATIONS = 7402;
    private static final long SERVICE_STATE_REFRESH_MS = 500L;
    private static final String STATE_WAITING_FOR_OVERLAY_PERMISSION = "waiting_for_overlay_permission";
    private static final String STATE_WAITING_FOR_NOTIFICATION_PERMISSION = "waiting_for_notification_permission";

    private final Handler handler = new Handler(Looper.getMainLooper());
    private boolean waitingForOverlayPermission;
    private boolean waitingForNotificationPermission;
    private TextView statusView;
    private TextView gameSelectorHint;
    private Spinner gameSelector;
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
        root.setGravity(Gravity.CENTER_HORIZONTAL);
        root.setPadding(dp(24), dp(34), dp(24), dp(28));
        root.setBackgroundColor(Color.rgb(13, 20, 29));
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            int top = Math.max(dp(34), insets.getSystemWindowInsetTop() + dp(10));
            int bottom = Math.max(dp(28), insets.getSystemWindowInsetBottom() + dp(10));
            view.setPadding(dp(24), top, dp(24), bottom);
            return insets;
        });

        TextView eyebrow = text("WPE · 战斗观察", 12f, Color.rgb(136, 188, 197));
        eyebrow.setGravity(Gravity.CENTER);
        eyebrow.setLetterSpacing(0.08f);
        root.addView(eyebrow, matchWrap());

        TextView title = text(getString(R.string.app_name), 28f, Color.WHITE);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        title.setGravity(Gravity.CENTER);
        root.addView(title, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 8, 0, 0));

        TextView subtitle = text("查看本方与对方 HP、MP、SP；SP 含客户端属性值，战斗增减速未确认", 14f,
                Color.rgb(181, 199, 210));
        subtitle.setGravity(Gravity.CENTER);
        subtitle.setLineSpacing(dp(4), 1f);
        root.addView(subtitle, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 12, 0, 0));

        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(16), dp(16), dp(16), dp(16));
        card.setBackground(roundBackground(Color.rgb(25, 37, 49),
                Color.rgb(74, 112, 126), dp(16), dp(1)));
        root.addView(card, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 30, 0, 0));

        TextView sectionTitle = text("启动前", 16f, Color.WHITE);
        sectionTitle.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        card.addView(sectionTitle, matchWrap());

        TextView requirements = text(
                "• 悬浮窗权限：显示和拖动面板\n"
                        + "• Root 授权：读取战斗状态；抗性页临时连接游戏查询\n"
                        + "• 抗性页：当前 dhxy 版本每场查询一次，显示查询时快照\n"
                        + "• 通知权限：显示前台服务状态；拒绝后仍可启动",
                13f, Color.rgb(193, 208, 216));
        requirements.setLineSpacing(dp(7), 1f);
        card.addView(requirements, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 12, 0, 0));

        TextView targetTitle = text("读取目标游戏", 14f, Color.WHITE);
        targetTitle.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        card.addView(targetTitle, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 18, 0, 0));

        gameSelector = new Spinner(this);
        ArrayAdapter<String> gameAdapter = new ArrayAdapter<>(this,
                android.R.layout.simple_spinner_item,
                new String[]{"XXXY · " + BattleOverlayService.GAME_PACKAGE_XXXY2026,
                        "DHXY · " + BattleOverlayService.GAME_PACKAGE_DHXY});
        gameAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        gameSelector.setAdapter(gameAdapter);
        gameSelector.setSelection(BattleOverlayService.GAME_PACKAGE_DHXY.equals(
                BattleOverlayService.getSelectedGamePackage(this)) ? 1 : 0);
        gameSelector.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                String gamePackage = position == 1
                        ? BattleOverlayService.GAME_PACKAGE_DHXY
                        : BattleOverlayService.GAME_PACKAGE_XXXY2026;
                BattleOverlayService.saveSelectedGamePackage(MainActivity.this, gamePackage);
            }

            @Override
            public void onNothingSelected(AdapterView<?> parent) {
            }
        });
        card.addView(gameSelector, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 4, 0, 0));

        gameSelectorHint = text("服务运行中不能切换；先停止悬浮面板后选择。", 12f,
                Color.rgb(153, 174, 186));
        card.addView(gameSelectorHint, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 4, 0, 0));

        statusView = text("尚未启动", 13f, Color.rgb(199, 219, 224));
        statusView.setGravity(Gravity.CENTER_VERTICAL);
        statusView.setPadding(dp(12), dp(9), dp(12), dp(9));
        statusView.setBackground(roundBackground(Color.rgb(33, 51, 65),
                Color.rgb(81, 124, 139), dp(10), dp(1)));
        card.addView(statusView, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 18, 0, 0));

        startButton = button("启动悬浮面板", Color.rgb(23, 112, 91));
        startButton.setOnClickListener(view -> requestOverlayPermissionThenStart());
        root.addView(startButton, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                dp(52), 0, 24, 0, 0));

        stopButton = button("停止悬浮面板", Color.rgb(89, 53, 53));
        stopButton.setOnClickListener(view -> stopOverlayService());
        root.addView(stopButton, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                dp(48), 0, 10, 0, 0));

        TextView footer = text("双方各十槽（人物 5 + 宠物 5）· 死亡、未进战、读取错误与过期状态分开展示",
                11f, Color.rgb(134, 153, 166));
        footer.setGravity(Gravity.CENTER);
        root.addView(footer, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, 0, 22, 0, 0));

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
            requestPermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},
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
        updateGameSelectorState(true);
        Intent service = new Intent(this, BattleOverlayService.class)
                .setAction(BattleOverlayService.ACTION_START);
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
        updateGameSelectorState(running);
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

    private void updateGameSelectorState(boolean running) {
        if (gameSelector != null) {
            gameSelector.setEnabled(!running);
        }
        if (gameSelectorHint != null) {
            gameSelectorHint.setText(running
                    ? "悬浮面板运行中，需先停止后切换目标游戏。"
                    : "服务运行时不能切换；先停止悬浮面板后选择。此前选择会自动保存。");
        }
    }

    private void setStatus(String value, boolean running) {
        if (statusView != null) {
            statusView.setText(value);
            statusView.setTextColor(running
                    ? Color.rgb(179, 244, 210) : Color.rgb(199, 219, 224));
        }
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
