package com.xnas.wpe.suite;

import android.app.Activity;
import android.app.ActivityGroup;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.content.res.Configuration;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@SuppressWarnings("deprecation")
public final class MainActivity extends ActivityGroup {
    public static final String EXTRA_OPEN_MODULE_ID = "com.xnas.wpe.suite.extra.OPEN_MODULE_ID";
    private static final String STATE_SELECTED_MODULE = "selected_module";
    private static final String STATE_PERMISSION_MODULE = "permission_module";
    private static final String STATE_PERMISSION_CODE = "permission_code";
    private static final int COLOR_BG = Color.rgb(12, 22, 31);
    private static final int COLOR_NAV = Color.rgb(15, 29, 40);
    private static final int COLOR_ACCENT = Color.rgb(74, 201, 176);
    private static final int COLOR_TEXT = Color.rgb(239, 246, 248);
    private static final int COLOR_MUTED = Color.rgb(156, 179, 189);
    private static final int COLOR_SURFACE = Color.rgb(21, 39, 51);
    private static final int COLOR_GREEN = Color.rgb(84, 210, 157);
    private static final int COLOR_AMBER = Color.rgb(255, 190, 92);
    private static final int COLOR_RED = Color.rgb(255, 112, 112);
    private static final String APP_VERSION = "2026.10.13.1";
    private static final String[] TARGET_LABELS = {
            "DHXY",
            "XXXY 2026"
    };

    private static final Module[] MODULES = {
            new Module(TargetGame.MOUNT_HOLD, "角色速度", "角色速度保持", com.xnas.wpe.mounthold.MainActivity.class),
            new Module(TargetGame.BATTLE_SKIP, "战斗快进", "战斗快进", com.xnas.wpe.battleskip.MainActivity.class),
            new Module(TargetGame.BATTLE_LOOP, "战投跳过", "战投跳过", com.xnas.wpe.battleloop.MainActivity.class),
            new Module(TargetGame.BATTLE_PANEL, "战斗面板", "战斗面板", com.xnas.wpe.battlepanel.MainActivity.class),
            new Module(TargetGame.JINCHAN, "金蝉装备", "金蝉装备", com.xnas.wpe.jinchan.MainActivity.class),
            new Module(TargetGame.ANLEI, "暗雷遇敌", "暗雷遇敌助手", com.xnas.wpe.anlei.MainActivity.class)
    };

    private final Handler statusHandler = new Handler(Looper.getMainLooper());
    private final List<NavigationItem> navigationItems = new ArrayList<>();
    private final Map<String, ModuleStatus> lastStatuses = new HashMap<>();
    private final Map<String, Long> statusTimes = new HashMap<>();
    private String selectedModuleId = "battlepanel";
    private String selectedTargetPackage = TargetGame.DHXY_PACKAGE;
    private TextView moduleTitle;
    private TextView statusLabel;
    private TextView targetButton;
    private FrameLayout moduleContainer;
    private LinearLayout sidebarView;
    private String pendingPermissionModuleId;
    private int pendingPermissionRequestCode = -1;
    private final Runnable statusRefresh = new Runnable() {
        @Override public void run() {
            refreshSelectedStatus();
            statusHandler.postDelayed(this, 1000L);
        }
    };

    @Override protected void attachBaseContext(android.content.Context base) {
        super.attachBaseContext(CompactUi.wrap(base));
    }

    public MainActivity() {
        super(true);
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (savedInstanceState != null) {
            selectedModuleId = savedInstanceState.getString(STATE_SELECTED_MODULE, selectedModuleId);
            pendingPermissionModuleId = savedInstanceState.getString(STATE_PERMISSION_MODULE);
            pendingPermissionRequestCode = savedInstanceState.getInt(STATE_PERMISSION_CODE, -1);
        }
        selectedTargetPackage = TargetGame.getSelectedPackage(this);
        String requestedModule = getIntent() == null ? null : getIntent().getStringExtra(EXTRA_OPEN_MODULE_ID);
        if (findModule(requestedModule) != null) selectedModuleId = requestedModule;
        getWindow().setStatusBarColor(COLOR_BG);
        getWindow().setNavigationBarColor(COLOR_BG);
        setContentView(buildShell());
        showModule(selectedModuleId);
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String requestedModule = intent == null ? null : intent.getStringExtra(EXTRA_OPEN_MODULE_ID);
        if (findModule(requestedModule) != null) showModule(requestedModule);
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        outState.putString(STATE_SELECTED_MODULE, selectedModuleId);
        if (pendingPermissionModuleId != null) {
            outState.putString(STATE_PERMISSION_MODULE, pendingPermissionModuleId);
            outState.putInt(STATE_PERMISSION_CODE, pendingPermissionRequestCode);
        }
        super.onSaveInstanceState(outState);
    }

    @Override
    protected void onResume() {
        super.onResume();
        statusHandler.removeCallbacks(statusRefresh);
        statusRefresh.run();
    }

    @Override
    protected void onPause() {
        statusHandler.removeCallbacks(statusRefresh);
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        statusHandler.removeCallbacksAndMessages(null);
        super.onDestroy();
    }

    @Override
    public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        if (sidebarView != null) {
            ViewGroup.LayoutParams params = sidebarView.getLayoutParams();
            params.width = dp(sidebarWidthDp());
            sidebarView.setLayoutParams(params);
        }
        renderNavigation();
    }

    void requestPermissionsFromModule(ModuleActivity child, String[] permissions, int requestCode) {
        if (child == null || getLocalActivityManager().getCurrentActivity() != child) {
            throw new IllegalStateException("Only the active module page may request permissions");
        }
        if (pendingPermissionModuleId != null) {
            throw new IllegalStateException("A module permission request is already pending");
        }
        pendingPermissionModuleId = child.moduleIdForHost();
        pendingPermissionRequestCode = requestCode;
        try {
            // ActivityGroup's child permission result is dispatched to this host Activity.
            requestPermissions(permissions, requestCode);
        } catch (RuntimeException error) {
            pendingPermissionModuleId = null;
            pendingPermissionRequestCode = -1;
            throw error;
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        String moduleId = pendingPermissionModuleId;
        int expectedCode = pendingPermissionRequestCode;
        pendingPermissionModuleId = null;
        pendingPermissionRequestCode = -1;
        if (moduleId == null || requestCode != expectedCode) return;
        Activity child = getLocalActivityManager().getActivity(moduleId);
        if (child instanceof ModuleActivity) {
            ((ModuleActivity) child).onRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }

    private View buildShell() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.HORIZONTAL);
        root.setBackgroundColor(COLOR_BG);

        LinearLayout sidebar = new LinearLayout(this);
        sidebarView = sidebar;
        sidebar.setOrientation(LinearLayout.VERTICAL);
        sidebar.setBackgroundColor(COLOR_NAV);
        sidebar.setPadding(dp(10), dp(18), dp(10), dp(14));
        root.addView(sidebar, new LinearLayout.LayoutParams(dp(sidebarWidthDp()), ViewGroup.LayoutParams.MATCH_PARENT));

        ScrollView navScroll = new ScrollView(this);
        navScroll.setFillViewport(true);
        sidebar.addView(navScroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        LinearLayout navList = new LinearLayout(this);
        navList.setOrientation(LinearLayout.VERTICAL);
        navScroll.addView(navList, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        for (Module module : MODULES) {
            LinearLayout item = new LinearLayout(this);
            item.setOrientation(LinearLayout.VERTICAL);
            item.setGravity(Gravity.CENTER);
            item.setPadding(dp(4), dp(5), dp(4), dp(4));
            item.setFocusable(true);
            item.setClickable(true);
            item.setOnClickListener(view -> showModule(module.id));

            TextView label = new TextView(this);
            label.setText(module.navLabel);
            label.setTextSize(12);
            label.setTextColor(COLOR_MUTED);
            label.setGravity(Gravity.CENTER);
            label.setMaxLines(1);
            label.setEllipsize(android.text.TextUtils.TruncateAt.END);
            item.addView(label, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

            TextView state = new TextView(this);
            state.setText("未查看");
            state.setTextSize(9);
            state.setTextColor(COLOR_MUTED);
            state.setGravity(Gravity.CENTER);
            state.setSingleLine(true);
            item.addView(state, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(17)));

            item.setContentDescription(module.title + "，状态尚未读取");
            navigationItems.add(new NavigationItem(module, item, label, state));
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(58));
            params.bottomMargin = dp(5);
            navList.addView(item, params);
        }

        LinearLayout detail = new LinearLayout(this);
        detail.setOrientation(LinearLayout.VERTICAL);
        root.addView(detail, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));

        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.VERTICAL);
        header.setPadding(dp(12), dp(8), dp(12), dp(8));
        header.setBackgroundColor(Color.rgb(16, 31, 42));
        detail.addView(header, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout titleRow = new LinearLayout(this);
        titleRow.setOrientation(LinearLayout.HORIZONTAL);
        titleRow.setGravity(Gravity.CENTER_VERTICAL);
        header.addView(titleRow, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        moduleTitle = new TextView(this);
        moduleTitle.setTextColor(COLOR_TEXT);
        moduleTitle.setTextSize(16);
        moduleTitle.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        titleRow.addView(moduleTitle, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        moduleTitle.setSingleLine(true);
        moduleTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);
        statusLabel = new TextView(this);
        statusLabel.setText("状态未确认");
        statusLabel.setTextSize(10);
        statusLabel.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        statusLabel.setPadding(dp(7), dp(3), dp(7), dp(3));
        titleRow.addView(statusLabel, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        targetButton = new TextView(this);
        targetButton.setText(TARGET_LABELS[targetIndex(selectedTargetPackage)] + " ▾");
        targetButton.setTextSize(11);
        targetButton.setTextColor(COLOR_TEXT);
        targetButton.setGravity(Gravity.CENTER);
        targetButton.setMinHeight(dp(32));
        targetButton.setMinWidth(dp(76));
        targetButton.setPadding(dp(8), dp(4), dp(8), dp(4));
        targetButton.setBackground(background(COLOR_SURFACE, COLOR_ACCENT, dp(8), dp(1)));
        targetButton.setContentDescription("目标游戏，点击切换");
        targetButton.setFocusable(true);
        targetButton.setOnClickListener(view -> showTargetPicker());
        LinearLayout.LayoutParams targetParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        targetParams.leftMargin = dp(8);
        titleRow.addView(targetButton, targetParams);

        TextView version = new TextView(this);
        version.setText(APP_VERSION);
        version.setTextColor(COLOR_MUTED);
        version.setTextSize(9);
        version.setPadding(dp(8), 0, 0, 0);
        version.setGravity(Gravity.CENTER_VERTICAL | Gravity.RIGHT);
        version.setContentDescription("应用版本 " + APP_VERSION);
        titleRow.addView(version, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        moduleContainer = new FrameLayout(this);
        detail.addView(moduleContainer, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        return root;
    }

    private void showModule(String moduleId) {
        Module module = findModule(moduleId);
        if (module == null) module = findModule("battlepanel");
        if (module == null || moduleContainer == null) return;

        selectedModuleId = module.id;
        moduleTitle.setText(module.title);
        renderNavigation();

        if (!TargetGame.supportsModule(module.id, selectedTargetPackage)) {
            moduleContainer.removeAllViews();
            TextView unsupported = new TextView(this);
            unsupported.setText("此功能仅支持 DHXY");
            unsupported.setTextColor(COLOR_MUTED);
            unsupported.setTextSize(15);
            unsupported.setPadding(dp(20), dp(20), dp(20), dp(20));
            moduleContainer.addView(unsupported, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            refreshSelectedStatus();
            return;
        }

        try {
            Intent childIntent = new Intent(this, module.activity);
            Window window = getLocalActivityManager().startActivity(module.id, childIntent);
            if (window == null) throw new IllegalStateException("Feature page could not be created");
            View page = window.getDecorView();
            ViewParentRemover.detach(page);
            moduleContainer.removeAllViews();
            moduleContainer.addView(page, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            refreshSelectedStatus();
        } catch (RuntimeException error) {
            moduleContainer.removeAllViews();
            TextView failure = new TextView(this);
            failure.setText("页面打开失败：" + (error.getMessage() == null ? "未知错误" : error.getMessage()));
            failure.setTextColor(COLOR_TEXT);
            failure.setPadding(dp(18), dp(18), dp(18), dp(18));
            moduleContainer.addView(failure, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        }
    }

    private static Module findModule(String id) {
        if (id == null) return null;
        for (Module module : MODULES) if (module.id.equals(id)) return module;
        return null;
    }

    private void refreshSelectedStatus() {
        Module module = findModule(selectedModuleId);
        if (module == null) return;
        selectedTargetPackage = TargetGame.getSelectedPackage(this);
        Activity child;
        ModuleStatus status;
        if (!TargetGame.supportsModule(module.id, selectedTargetPackage)) {
            status = new ModuleStatus(ModuleStatus.State.ERROR, "不支持", selectedTargetPackage, "", "");
        } else try {
            child = getLocalActivityManager().getActivity(module.id);
            if (child instanceof ModuleStatusProvider) {
                status = ((ModuleStatusProvider) child).getModuleStatus();
            } else {
                status = ModuleStatus.unknown(selectedTargetPackage, "");
            }
        } catch (RuntimeException error) {
            status = ModuleStatus.unknown(selectedTargetPackage, "");
        }
        if (status == null) status = ModuleStatus.unknown(selectedTargetPackage, "");
        lastStatuses.put(module.id, status);
        statusTimes.put(module.id, System.currentTimeMillis());
        renderStatus(status);
        renderNavigation();
    }

    private void renderStatus(ModuleStatus status) {
        if (statusLabel == null) return;
        int color = statusColor(status.state);
        statusLabel.setText(status.label);
        statusLabel.setTextColor(color);
        statusLabel.setBackground(background(withAlpha(color, 42), withAlpha(color, 110), dp(20), dp(1)));
    }

    private void renderNavigation() {
        for (NavigationItem item : navigationItems) {
            boolean selected = item.module.id.equals(selectedModuleId);
            ModuleStatus status = lastStatuses.get(item.module.id);
            Long sampledAt = statusTimes.get(item.module.id);
            item.label.setTextColor(selected ? COLOR_TEXT : COLOR_MUTED);
            item.label.setTypeface(Typeface.DEFAULT, selected ? Typeface.BOLD : Typeface.NORMAL);
            item.root.setBackground(background(selected ? Color.rgb(29, 77, 78) : Color.TRANSPARENT,
                    selected ? COLOR_ACCENT : Color.TRANSPARENT, dp(12), selected ? dp(1) : 0));
            item.root.setSelected(selected);
            String stateText;
            int stateTextColor;
            boolean supported = TargetGame.supportsModule(item.module.id, selectedTargetPackage);
            if (!supported) {
                stateText = "不支持";
                stateTextColor = COLOR_AMBER;
            } else if (status == null) {
                stateText = "未查看";
                stateTextColor = COLOR_MUTED;
            } else {
                String prefix = selected || sampledAt == null ? "" : "最近·";
                stateText = prefix + navState(status.state);
                stateTextColor = selected ? statusColor(status.state) : COLOR_MUTED;
            }
            item.state.setText(stateText);
            item.state.setTextColor(stateTextColor);
            item.root.setAlpha(supported ? 1f : 0.65f);
            item.root.setContentDescription(item.module.title + "，"
                    + (selected ? "当前页面，" : "")
                    + (!supported ? "当前目标不支持" : status == null ? "状态尚未读取" : status.label));
        }
    }

    private boolean isTargetSelectionLocked() {
        if (com.xnas.wpe.anlei.AnleiService.isActive()
                || com.xnas.wpe.mounthold.MountHoldService.isActive()
                || com.xnas.wpe.battleloop.BattleLoopService.isActive()
                || com.xnas.wpe.battlepanel.BattleOverlayService.isActive()
                || com.xnas.wpe.jinchan.ConnectionService.isTargetSwitchLocked()
                || BattleConflictGate.hasUnconfirmedActivity(new java.io.File(getFilesDir(), "modules"))) {
            return true;
        }
        for (Module module : MODULES) {
            Activity child = getLocalActivityManager().getActivity(module.id);
            if (!(child instanceof ModuleStatusProvider)) continue;
            try {
                ModuleStatus status = ((ModuleStatusProvider) child).getModuleStatus();
                if (status == null || status.state != ModuleStatus.State.STOPPED) return true;
            } catch (RuntimeException error) {
                return true;
            }
        }
        return false;
    }

    private void showTargetPicker() {
        new android.app.AlertDialog.Builder(this)
                .setTitle("目标游戏")
                .setSingleChoiceItems(TARGET_LABELS, targetIndex(selectedTargetPackage), (dialog, index) -> {
                    String target = packageForTargetIndex(index);
                    dialog.dismiss();
                    if (target.equals(selectedTargetPackage)) return;
                    if (isTargetSelectionLocked()) {
                        Toast.makeText(this, "请先停止运行中的功能并完成清理", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    if (!TargetGame.setSelectedPackage(this, target)) return;
                    selectedTargetPackage = target;
                    targetButton.setText(TARGET_LABELS[index] + " ▾");
                    renderNavigation();
                    showModule(selectedModuleId);
                })
                .setNegativeButton("取消", null)
                .show();
    }

    private static int targetIndex(String packageName) {
        return TargetGame.XXXY_PACKAGE.equals(packageName) ? 1 : 0;
    }

    private static String packageForTargetIndex(int index) {
        return index == 1 ? TargetGame.XXXY_PACKAGE : TargetGame.DHXY_PACKAGE;
    }

    private String navState(ModuleStatus.State state) {
        switch (state) {
            case STOPPED: return "已停止";
            case WAITING: return "等待中";
            case RUNNING: return "运行中";
            case STOPPING: return "清理中";
            case ERROR: return "错误";
            default: return "未确认";
        }
    }

    private int statusColor(ModuleStatus.State state) {
        switch (state) {
            case RUNNING: return COLOR_GREEN;
            case WAITING:
            case STOPPING: return COLOR_AMBER;
            case ERROR: return COLOR_RED;
            case STOPPED: return COLOR_ACCENT;
            default: return COLOR_MUTED;
        }
    }

    private static int withAlpha(int color, int alpha) {
        return Color.argb(alpha, Color.red(color), Color.green(color), Color.blue(color));
    }

    private GradientDrawable background(int fill, int stroke, int radius, int strokeWidth) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(fill);
        drawable.setCornerRadius(radius);
        if (strokeWidth > 0) drawable.setStroke(strokeWidth, stroke);
        return drawable;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private int sidebarWidthDp() {
        int widthDp = Math.round(getResources().getDisplayMetrics().widthPixels
                / getResources().getDisplayMetrics().density);
        return widthDp < 420 ? 92 : (widthDp < 600 ? 104 : 116);
    }

    private static final class Module {
        final String id;
        final String navLabel;
        final String title;
        final Class<? extends Activity> activity;

        Module(String id, String navLabel, String title, Class<? extends Activity> activity) {
            this.id = id;
            this.navLabel = navLabel;
            this.title = title;
            this.activity = activity;
        }
    }

    private static final class NavigationItem {
        final Module module;
        final LinearLayout root;
        final TextView label;
        final TextView state;

        NavigationItem(Module module, LinearLayout root, TextView label, TextView state) {
            this.module = module;
            this.root = root;
            this.label = label;
            this.state = state;
        }
    }

    private static final class ViewParentRemover {
        static void detach(View view) {
            if (view.getParent() instanceof ViewGroup) {
                ((ViewGroup) view.getParent()).removeView(view);
            }
        }
    }
}
