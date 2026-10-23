package com.xnas.wpe.suite;

import android.app.Activity;
import android.app.ActivityGroup;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.content.res.Configuration;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

@SuppressWarnings("deprecation")
public final class MainActivity extends ActivityGroup {
    public static final String EXTRA_OPEN_MODULE_ID = "com.xnas.wpe.suite.extra.OPEN_MODULE_ID";
    private static final String STATE_SELECTED_MODULE = "selected_module";
    private static final String STATE_PERMISSION_MODULE = "permission_module";
    private static final String STATE_PERMISSION_CODE = "permission_code";
    private static final int COLOR_BG = Color.rgb(12, 22, 31);
    private static final int COLOR_NAV = Color.rgb(15, 29, 40);
    private static final int COLOR_TEXT = Color.rgb(239, 246, 248);
    private static final int COLOR_MUTED = Color.rgb(156, 179, 189);
    private static final int COLOR_GREEN = Color.rgb(84, 210, 157);
    private static final int COLOR_AMBER = Color.rgb(255, 190, 92);
    private static final int COLOR_RED = Color.rgb(255, 112, 112);
    private static final String[] TARGET_LABELS = {
            "DHXY",
            "XXXY 2026"
    };

    private static final String SETTINGS_ID = "settings";
    /** 定时任务页的模块 ID，供通知与外部 intent 直接打开。 */
    public static final String SCHEDULE_ID = "schedule";

    private static final Module[] MODULES = {
            new Module(TargetGame.MOUNT_HOLD, "角色速度", "角色速度保持", R.drawable.ic_nav_speed, com.xnas.wpe.mounthold.MainActivity.class),
            new Module(TargetGame.BATTLE_LOOP, "战斗跳过", "战斗跳过", R.drawable.ic_nav_skip, com.xnas.wpe.battleloop.MainActivity.class),
            new Module(TargetGame.BATTLE_PANEL, "战斗面板", "战斗面板", R.drawable.ic_nav_panel, com.xnas.wpe.battlepanel.MainActivity.class),
            new Module(TargetGame.STARHUNT, "自动找星", "自动找星", R.drawable.ic_nav_star, com.xnas.wpe.starhunt.MainActivity.class),
            new Module(TargetGame.BOSSHUNT, "自动BOSS", "自动BOSS", R.drawable.ic_nav_boss, com.xnas.wpe.bosshunt.MainActivity.class),
            new Module(TargetGame.BATTLE_SKIP, "战斗快进", "战斗快进", R.drawable.ic_nav_fast, com.xnas.wpe.battleskip.MainActivity.class),
            new Module(TargetGame.JINCHAN, "金蝉装备", "金蝉装备", R.drawable.ic_nav_equip, com.xnas.wpe.jinchan.MainActivity.class),
            new Module(TargetGame.ANLEI, "暗雷遇敌", "暗雷遇敌助手", R.drawable.ic_nav_thunder, com.xnas.wpe.anlei.MainActivity.class)
    };

    private final Handler statusHandler = new Handler(Looper.getMainLooper());
    private final List<NavigationItem> navigationItems = new ArrayList<>();
    private final ModuleStatusSnapshotStore statusSnapshots = new ModuleStatusSnapshotStore();
    private String selectedModuleId = "battlepanel";
    private String selectedTargetPackage = TargetGame.DHXY_PACKAGE;
    private TextView moduleTitle;
    private TextView targetButton;
    private TextView licenseButton;
    private TextView settingsLabel;
    private ImageView settingsIcon;
    private TextView scheduleLabel;
    private ImageView scheduleIcon;
    private FrameLayout moduleContainer;
    private LinearLayout sidebarView;
    private View schedulePageView;
    private TextView diagnosticsView;
    private String lastDiagnosticsContent;
    private int lastSettingsSelected = -1;
    private int lastScheduleSelected = -1;
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
        if (SETTINGS_ID.equals(requestedModule)) showSettings();
        else if (SCHEDULE_ID.equals(requestedModule)) showSchedule();
        else showModule(selectedModuleId);
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String requestedModule = intent == null ? null : intent.getStringExtra(EXTRA_OPEN_MODULE_ID);
        if (findModule(requestedModule) != null) showModule(requestedModule);
        else if (SETTINGS_ID.equals(requestedModule)) showSettings();
        else if (SCHEDULE_ID.equals(requestedModule)) showSchedule();
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
        // 从授权页返回时，设置页的「授权状态」要按当前状态重画一次。
        if (SETTINGS_ID.equals(selectedModuleId)) showSettings();
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
        for (NavigationItem item : navigationItems) {
            if (item.pulse != null) {
                item.pulse.cancel();
                item.pulse = null;
            }
        }
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

    /** 侧栏底部的设置入口：图标与文字跟导航项同规格，选中时同样高亮。 */
    private View settingsEntry() {
        LinearLayout entry = new LinearLayout(this);
        entry.setOrientation(LinearLayout.HORIZONTAL);
        entry.setGravity(Gravity.CENTER_VERTICAL);
        entry.setPadding(dp(8), 0, dp(8), 0);
        entry.setClickable(true);
        entry.setFocusable(true);
        entry.setOnClickListener(v -> showSettings());
        entry.setContentDescription("系统设置");

        settingsIcon = new ImageView(this);
        settingsIcon.setImageResource(R.drawable.ic_nav_settings);
        settingsIcon.setColorFilter(COLOR_MUTED);
        entry.addView(settingsIcon, new LinearLayout.LayoutParams(dp(14), dp(14)));

        settingsLabel = new TextView(this);
        settingsLabel.setText("系统设置");
        settingsLabel.setTextSize(10);
        settingsLabel.setTextColor(COLOR_MUTED);
        settingsLabel.setSingleLine(true);
        LinearLayout.LayoutParams labelParams = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        labelParams.leftMargin = dp(5);
        entry.addView(settingsLabel, labelParams);
        entry.setBackground(background(Color.TRANSPARENT, Color.TRANSPARENT, dp(10), 0));
        return entry;
    }

    /** 侧栏底部的定时任务入口：与设置入口同规格，排在设置之上。 */
    private View scheduleEntry() {
        LinearLayout entry = new LinearLayout(this);
        entry.setOrientation(LinearLayout.HORIZONTAL);
        entry.setGravity(Gravity.CENTER_VERTICAL);
        entry.setPadding(dp(8), 0, dp(8), 0);
        entry.setClickable(true);
        entry.setFocusable(true);
        entry.setOnClickListener(v -> showSchedule());
        entry.setContentDescription("定时任务");

        scheduleIcon = new ImageView(this);
        scheduleIcon.setImageResource(R.drawable.ic_nav_timer);
        scheduleIcon.setColorFilter(COLOR_MUTED);
        entry.addView(scheduleIcon, new LinearLayout.LayoutParams(dp(14), dp(14)));

        scheduleLabel = new TextView(this);
        scheduleLabel.setText("定时任务");
        scheduleLabel.setTextSize(10);
        scheduleLabel.setTextColor(COLOR_MUTED);
        scheduleLabel.setSingleLine(true);
        LinearLayout.LayoutParams labelParams = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        labelParams.leftMargin = dp(5);
        entry.addView(scheduleLabel, labelParams);
        entry.setBackground(background(Color.TRANSPARENT, Color.TRANSPARENT, dp(10), 0));
        return entry;
    }

    private View buildShell() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.HORIZONTAL);
        root.setBackgroundColor(COLOR_BG);

        LinearLayout sidebar = new LinearLayout(this);
        sidebarView = sidebar;
        sidebar.setOrientation(LinearLayout.VERTICAL);
        sidebar.setBackgroundColor(COLOR_NAV);
        sidebar.setPadding(dp(10), dp(10), dp(10), dp(12));
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

            LinearLayout labelRow = new LinearLayout(this);
            labelRow.setOrientation(LinearLayout.HORIZONTAL);
            labelRow.setGravity(Gravity.CENTER_VERTICAL);

            ImageView icon = new ImageView(this);
            icon.setImageResource(module.iconRes);
            icon.setColorFilter(COLOR_MUTED);
            labelRow.addView(icon, new LinearLayout.LayoutParams(dp(15), dp(15)));

            TextView label = new TextView(this);
            label.setText(module.navLabel);
            label.setTextSize(11);
            label.setTextColor(COLOR_MUTED);
            label.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
            label.setMaxLines(1);
            label.setEllipsize(android.text.TextUtils.TruncateAt.END);
            LinearLayout.LayoutParams labelParams = new LinearLayout.LayoutParams(
                    0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
            labelParams.leftMargin = dp(5);
            labelRow.addView(label, labelParams);
            item.addView(labelRow, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

            TextView state = new TextView(this);
            state.setText("");
            state.setTextSize(8);
            state.setTextColor(COLOR_MUTED);
            state.setVisibility(View.GONE);
            state.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
            state.setSingleLine(true);
            state.setPadding(dp(20), 0, 0, 0);
            item.addView(state, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(17)));

            item.setContentDescription(module.title + "，状态尚未读取");
            navigationItems.add(new NavigationItem(module, item, icon, label, state));
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(42));
            params.bottomMargin = dp(3);
            navList.addView(item, params);
        }

        // 设置入口固定贴在侧栏底部：导航可滚动，分隔线与设置项不参与滚动，避免悬在中间留一大片空白。
        LinearLayout.LayoutParams scheduleParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(34));
        sidebar.addView(scheduleEntry(), scheduleParams);
        View navDivider = new View(this);
        navDivider.setBackgroundColor(0x24FFFFFF);
        LinearLayout.LayoutParams navDividerParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Math.max(1, dp(1)));
        navDividerParams.topMargin = dp(8);
        sidebar.addView(navDivider, navDividerParams);
        LinearLayout.LayoutParams settingsParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(34));
        settingsParams.topMargin = dp(2);
        sidebar.addView(settingsEntry(), settingsParams);

        LinearLayout detail = new LinearLayout(this);
        detail.setOrientation(LinearLayout.VERTICAL);
        root.addView(detail, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));

        // 顶栏整条去掉：模块名已由侧栏选中项表达，这里把高度让给内容。
        moduleTitle = new TextView(this);
        moduleTitle.setVisibility(View.GONE);

        moduleContainer = new FrameLayout(this);
        detail.addView(moduleContainer, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        return root;
    }

    private void showModule(String moduleId) {
        Module module = findModule(moduleId);
        if (module == null) module = findModule("battlepanel");
        if (module == null || moduleContainer == null) return;

        schedulePageView = null;
        diagnosticsView = null;
        lastDiagnosticsContent = null;
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
            fadeIn(unsupported);
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
            fadeIn(page);
            refreshSelectedStatus();
        } catch (RuntimeException error) {
            moduleContainer.removeAllViews();
            TextView failure = new TextView(this);
            failure.setText("页面打开失败：" + (error.getMessage() == null ? "未知错误" : error.getMessage()));
            failure.setTextColor(COLOR_TEXT);
            failure.setPadding(dp(18), dp(18), dp(18), dp(18));
            moduleContainer.addView(failure, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            fadeIn(failure);
        }
    }

    /** 系统设置页：目标游戏、授权与版本信息集中在这里，顶栏不再放胶囊。 */
    private void showSettings() {
        if (moduleContainer == null) return;
        selectedModuleId = SETTINGS_ID;
        moduleTitle.setText("系统设置");
        selectedTargetPackage = TargetGame.getSelectedPackage(this);
        renderNavigation();
        // showSettings() can be reopened after changing the selected target; the new TextView
        // must receive its initial contents even when the diagnostic text itself is unchanged.
        lastDiagnosticsContent = null;

        LinearLayout page = new LinearLayout(this);
        page.setOrientation(LinearLayout.VERTICAL);
        page.setPadding(dp(GlassSkin.PADDING_H_DP), dp(GlassSkin.PADDING_V_DP),
                dp(GlassSkin.PADDING_H_DP), dp(GlassSkin.PADDING_V_DP));
        page.setBackground(GlassSkin.page(this));

        if (TargetGame.isLocked()) {
            infoRow(page, "目标游戏",
                    TARGET_LABELS[targetIndex(selectedTargetPackage)] + "（朋友版锁定，不可切换）");
        } else {
            actionRow(page, "目标游戏",
                    TARGET_LABELS[targetIndex(selectedTargetPackage)] + " ▾",
                    this::showTargetPicker);
        }
        actionRow(page, "授权状态", licenseLabel(),
                () -> startActivity(new Intent(this, ActivationActivity.class)));

        page.addView(sectionLabel("版本"));
        infoRow(page, "版本名", BuildConfig.VERSION_NAME);
        infoRow(page, "版本码", Integer.toString(BuildConfig.VERSION_CODE));
        infoRow(page, "授权模式", LicenseManager.BYPASS ? "免激活" : "普通");
        infoRow(page, "支持架构", "x86_64");

        page.addView(sectionLabel("诊断信息"));
        actionRow(page, "复制诊断", "一键复制", this::copyDiagnostics);
        diagnosticsView = new TextView(this);
        diagnosticsView.setTextColor(GlassSkin.TEXT_MUTED);
        diagnosticsView.setTextSize(10);
        diagnosticsView.setTextIsSelectable(true);
        diagnosticsView.setPadding(dp(12), dp(10), dp(12), dp(10));
        GlassSkin.apply(diagnosticsView, GlassSkin.surface(this, GlassSkin.RADIUS_CONTAINER_DP));
        LinearLayout.LayoutParams diagnosticsParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        diagnosticsParams.topMargin = dp(6);
        page.addView(diagnosticsView, diagnosticsParams);

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.addView(page, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        schedulePageView = null;
        moduleContainer.removeAllViews();
        moduleContainer.addView(scroll, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        refreshDiagnosticsView();
        fadeIn(scroll);
    }

    /** 定时任务页：列表与新增都在 SchedulePage 里，这里只负责挂载与刷新。 */
    private void showSchedule() {
        if (moduleContainer == null) return;
        selectedModuleId = SCHEDULE_ID;
        renderNavigation();
        diagnosticsView = null;
        lastDiagnosticsContent = null;
        moduleContainer.removeAllViews();
        View page = SchedulePage.build(this, this::showSchedule);
        schedulePageView = page;
        moduleContainer.addView(page, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        fadeIn(page);
    }

    private TextView sectionLabel(String label) {
        TextView section = new TextView(this);
        section.setText(label);
        section.setTextColor(GlassSkin.TEXT_MUTED);
        section.setTextSize(GlassSkin.TEXT_SECTION_SP);
        section.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        section.setPadding(0, dp(18), 0, dp(8));
        return section;
    }

    private void infoRow(LinearLayout parent, String label, String value) {
        LinearLayout row = new LinearLayout(this);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(12), dp(10), dp(12), dp(10));
        GlassSkin.apply(row, GlassSkin.surface(this, GlassSkin.RADIUS_CONTAINER_DP));

        TextView name = new TextView(this);
        name.setText(label);
        name.setTextColor(GlassSkin.TEXT_MUTED);
        name.setTextSize(GlassSkin.TEXT_BODY_SP);
        row.addView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView text = new TextView(this);
        text.setText(value);
        text.setTextColor(GlassSkin.TEXT);
        text.setTextSize(GlassSkin.TEXT_BODY_SP);
        text.setGravity(Gravity.END);
        row.addView(text, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        params.topMargin = dp(6);
        parent.addView(row, params);
    }

    private void actionRow(LinearLayout parent, String label, String value, Runnable onClick) {
        LinearLayout row = new LinearLayout(this);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(12), dp(10), dp(12), dp(10));
        row.setFocusable(true);
        row.setClickable(true);
        row.setOnClickListener(v -> onClick.run());
        row.setContentDescription(label + "：" + value);
        GlassSkin.apply(row, GlassSkin.surface(this, GlassSkin.RADIUS_CONTAINER_DP));

        TextView name = new TextView(this);
        name.setText(label);
        name.setTextColor(GlassSkin.TEXT);
        name.setTextSize(GlassSkin.TEXT_BODY_SP);
        row.addView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView text = new TextView(this);
        text.setText(value + "  ›");
        text.setTextColor(GlassSkin.ACCENT);
        text.setTextSize(GlassSkin.TEXT_BODY_SP);
        row.addView(text, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        params.topMargin = dp(6);
        parent.addView(row, params);
    }

    /** 切页时 120ms 淡入：只改透明度，不改变任何控件的位置、尺寸或层级。 */
    private void fadeIn(View view) {
        view.setAlpha(0f);
        view.animate().alpha(1f).setDuration(120L).start();
    }

    private static Module findModule(String id) {
        if (id == null) return null;
        for (Module module : MODULES) if (module.id.equals(id)) return module;
        return null;
    }

    private void refreshSelectedStatus() {
        if (licenseButton != null) {
            // 入口文案、颜色与无障碍描述同步当前授权状态。
            boolean licensed = LicenseManager.isActive();
            licenseButton.setText(licenseLabel());
            licenseButton.setTextColor(licensed ? COLOR_TEXT : COLOR_AMBER);
            GlassSkin.apply(licenseButton, licensed
                    ? GlassSkin.surface(this, 13)
                    : GlassSkin.attention(this, 13, COLOR_AMBER));
            licenseButton.setContentDescription("授权状态：" + LicenseManager.displayStatus(this));
        }
        refreshModuleStatusSnapshots();
        renderNavigation();
        if (SETTINGS_ID.equals(selectedModuleId)) refreshDiagnosticsView();
        if (SCHEDULE_ID.equals(selectedModuleId) && schedulePageView != null) {
            SchedulePage.refresh(schedulePageView, this);
        }
    }

    private String licenseLabel() {
        if (LicenseManager.BYPASS) return LicenseManager.isActive() ? "免激活" : "版本受限";
        return LicenseManager.isActive() ? "已授权" : "授权";
    }

    /** Activity/provider state remains on the UI thread; only immutable, fixed-ID state is cached. */
    private void refreshModuleStatusSnapshots() {
        String targetKey = TargetGame.getSelectedPackage(this);
        selectedTargetPackage = targetKey;
        long observedAt = SystemClock.elapsedRealtime();
        for (Module module : MODULES) {
            if (!TargetGame.supportsModule(module.id, targetKey)) continue;
            Activity child = getLocalActivityManager().getActivity(module.id);
            if (!(child instanceof ModuleStatusProvider)) continue;

            ModuleStatus status;
            try {
                status = ((ModuleStatusProvider) child).getModuleStatus();
            } catch (RuntimeException error) {
                status = null;
            }
            if (!targetKey.equals(TargetGame.getSelectedPackage(this))) return;

            ModuleStatus.State state = status == null ? ModuleStatus.State.UNKNOWN : status.state;
            if (status != null && !targetKey.equals(status.target)) state = ModuleStatus.State.UNKNOWN;
            statusSnapshots.publish(module.id, targetKey, state, observedAt);
        }
    }

    private String diagnosticsText() {
        SuiteDiagnostics diagnostics = new SuiteDiagnostics(
                BuildConfig.VERSION_NAME,
                BuildConfig.VERSION_CODE,
                LicenseManager.BYPASS ? SuiteDiagnostics.LicenseMode.BYPASS
                        : SuiteDiagnostics.LicenseMode.STANDARD,
                Collections.singletonList("x86_64"),
                statusSnapshots.diagnosticStates(selectedTargetPackage, SystemClock.elapsedRealtime()),
                ScheduleStore.diagnosticSummary(this));
        return DiagnosticsFormatter.format(diagnostics);
    }

    private void refreshDiagnosticsView() {
        if (diagnosticsView == null) return;
        String content = diagnosticsText();
        if (content.equals(lastDiagnosticsContent)) return;
        diagnosticsView.setText(content);
        lastDiagnosticsContent = content;
    }

    private void copyDiagnostics() {
        String content = diagnosticsText();
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        if (clipboard == null) {
            Toast.makeText(this, "无法复制诊断信息", Toast.LENGTH_SHORT).show();
            return;
        }
        clipboard.setPrimaryClip(ClipData.newPlainText("飘渺助手诊断", content));
        if (diagnosticsView != null && !content.equals(lastDiagnosticsContent)) {
            diagnosticsView.setText(content);
            lastDiagnosticsContent = content;
        }
        Toast.makeText(this, "诊断信息已复制", Toast.LENGTH_SHORT).show();
    }

    /** 等待中的模块让状态文字极慢呼吸：只改透明度，不动颜色、亮度或尺寸。 */
    private void setPulse(NavigationItem item, boolean pulsing) {
        if (pulsing && item.pulse == null) {
            android.animation.ObjectAnimator animator =
                    android.animation.ObjectAnimator.ofFloat(item.state, "alpha", 1f, 0.55f);
            animator.setDuration(900L);
            animator.setRepeatMode(android.animation.ValueAnimator.REVERSE);
            animator.setRepeatCount(android.animation.ValueAnimator.INFINITE);
            item.pulse = animator;
            animator.start();
        } else if (!pulsing && item.pulse != null) {
            item.pulse.cancel();
            item.pulse = null;
            item.state.setAlpha(1f);
        }
    }

    private void renderNavigation() {
        long now = SystemClock.elapsedRealtime();
        for (NavigationItem item : navigationItems) {
            boolean selected = item.module.id.equals(selectedModuleId);
            boolean supported = TargetGame.supportsModule(item.module.id, selectedTargetPackage);
            ModuleStatusSnapshotStore.Snapshot snapshot = statusSnapshots.get(
                    item.module.id, selectedTargetPackage, now);
            ModuleStatus.State state = snapshot.state;
            int labelColor = selected ? COLOR_TEXT : COLOR_MUTED;
            int labelStyle = selected ? Typeface.BOLD : Typeface.NORMAL;
            if (item.lastLabelColor != labelColor) {
                item.label.setTextColor(labelColor);
                item.lastLabelColor = labelColor;
            }
            if (item.lastLabelStyle != labelStyle) {
                item.label.setTypeface(Typeface.DEFAULT, labelStyle);
                item.lastLabelStyle = labelStyle;
            }
            int iconColor = selected ? COLOR_TEXT : COLOR_MUTED;
            if (item.lastIconColor != iconColor) {
                item.icon.setColorFilter(iconColor);
                item.lastIconColor = iconColor;
            }
            int backgroundSelected = selected ? 1 : 0;
            if (item.lastBackgroundSelected != backgroundSelected) {
                if (selected) GlassSkin.apply(item.root, GlassSkin.selectedNav(this, 12));
                else item.root.setBackground(background(Color.TRANSPARENT,
                        Color.TRANSPARENT, dp(12), 0));
                item.lastBackgroundSelected = backgroundSelected;
            }
            if (item.lastSelected != backgroundSelected) {
                item.root.setSelected(selected);
                item.lastSelected = backgroundSelected;
            }

            String stateText = "● " + navStateText(state);
            if (!stateText.equals(item.lastStateText)) {
                item.state.setText(stateText);
                item.lastStateText = stateText;
            }
            int stateColor = navStateColor(state);
            if (item.lastStateColor != stateColor) {
                item.state.setTextColor(stateColor);
                item.lastStateColor = stateColor;
            }
            boolean stateVisible = supported && snapshot.hasSample && state != ModuleStatus.State.STOPPED;
            int stateVisibleValue = stateVisible ? 1 : 0;
            if (item.lastStateVisible != stateVisibleValue) {
                item.state.setVisibility(stateVisible ? View.VISIBLE : View.GONE);
                item.lastStateVisible = stateVisibleValue;
            }
            setPulse(item, state == ModuleStatus.State.WAITING);

            float alpha = supported ? 1f : 0.65f;
            if (item.lastAlpha != alpha) {
                item.root.setAlpha(alpha);
                item.lastAlpha = alpha;
            }
            String contentDescription = item.module.title + "，"
                    + (selected ? "当前页面，" : "")
                    + (!supported ? "当前目标不支持" : navStateText(state));
            if (!contentDescription.equals(item.lastContentDescription)) {
                item.root.setContentDescription(contentDescription);
                item.lastContentDescription = contentDescription;
            }
        }
        boolean settingsSelected = SETTINGS_ID.equals(selectedModuleId);
        if (settingsLabel != null && settingsIcon != null
                && lastSettingsSelected != (settingsSelected ? 1 : 0)) {
            settingsLabel.setTextColor(settingsSelected ? COLOR_TEXT : COLOR_MUTED);
            settingsLabel.setTypeface(Typeface.DEFAULT, settingsSelected ? Typeface.BOLD : Typeface.NORMAL);
            settingsIcon.setColorFilter(settingsSelected ? COLOR_TEXT : COLOR_MUTED);
            View parent = (View) settingsLabel.getParent();
            if (parent != null) {
                if (settingsSelected) {
                    GlassSkin.apply(parent, GlassSkin.selected(this, 8));
                } else {
                    parent.setBackground(background(Color.TRANSPARENT, Color.TRANSPARENT, dp(10), 0));
                }
                parent.setSelected(settingsSelected);
            }
            lastSettingsSelected = settingsSelected ? 1 : 0;
        }

        boolean scheduleSelected = SCHEDULE_ID.equals(selectedModuleId);
        if (scheduleLabel != null && scheduleIcon != null
                && lastScheduleSelected != (scheduleSelected ? 1 : 0)) {
            scheduleLabel.setTextColor(scheduleSelected ? COLOR_TEXT : COLOR_MUTED);
            scheduleLabel.setTypeface(Typeface.DEFAULT, scheduleSelected ? Typeface.BOLD : Typeface.NORMAL);
            scheduleIcon.setColorFilter(scheduleSelected ? COLOR_TEXT : COLOR_MUTED);
            View scheduleParent = (View) scheduleLabel.getParent();
            if (scheduleParent != null) {
                if (scheduleSelected) {
                    GlassSkin.apply(scheduleParent, GlassSkin.selected(this, 8));
                } else {
                    scheduleParent.setBackground(background(Color.TRANSPARENT, Color.TRANSPARENT, dp(10), 0));
                }
                scheduleParent.setSelected(scheduleSelected);
            }
            lastScheduleSelected = scheduleSelected ? 1 : 0;
        }
    }

    /** 侧栏状态文字：等待、运行、清理、错误各有说法，空闲时不显示。 */
    private String navStateText(ModuleStatus.State state) {
        if (state == null) return "未确认";
        switch (state) {
            case RUNNING: return "运行中";
            case PAUSED: return "已暂停";
            case WAITING: return "等待中";
            case STOPPING: return "清理中";
            case ERROR: return "错误";
            default: return "未确认";
        }
    }

    /** 侧栏状态配色沿用应用语义：绿为运行中、琥珀为等待或清理、红为错误、灰为未确认。 */
    private int navStateColor(ModuleStatus.State state) {
        if (state == null) return COLOR_MUTED;
        switch (state) {
            case RUNNING: return COLOR_GREEN;
            case WAITING:
            case PAUSED:
            case STOPPING: return COLOR_AMBER;
            case ERROR: return COLOR_RED;
            default: return COLOR_MUTED;
        }
    }

    private boolean isTargetSelectionLocked() {
        if (com.xnas.wpe.anlei.AnleiService.isActive()
                || com.xnas.wpe.mounthold.MountHoldService.isActive()
                || com.xnas.wpe.battleloop.BattleLoopService.isActive()
                || com.xnas.wpe.battlepanel.BattleOverlayService.isActive()
                || com.xnas.wpe.starhunt.StarHuntService.isActive()
                || com.xnas.wpe.bosshunt.BossHuntService.isActive()
                || com.xnas.wpe.bosshunt.BossHuntService.isHangupPending()
                || com.xnas.wpe.bosshunt.BossHuntService.isStopReadPending()
                || com.xnas.wpe.bosshunt.BossHuntService.hasRetryableStop(
                        new java.io.File(getFilesDir(), "modules/bosshunt"))
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
        // 应用主题是浅色 Material，这里显式用深色对话框主题，再把窗口背景换成玻璃表面，
        // 标题与选项用小一号字、当前目标行加玻璃高亮底；选项与取消的行为不变。
        android.app.AlertDialog picker = new android.app.AlertDialog.Builder(this,
                android.R.style.Theme_Material_Dialog_Alert)
                .setCustomTitle(targetDialogTitle())
                .setAdapter(targetAdapter(), (dialog, index) -> {
                    String target = packageForTargetIndex(index);
                    dialog.dismiss();
                    if (target.equals(selectedTargetPackage)) return;
                    if (isTargetSelectionLocked()) {
                        Toast.makeText(this, "请先停止运行中的功能并完成清理", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    if (!TargetGame.setSelectedPackage(this, target)) return;
                    selectedTargetPackage = target;
                    renderNavigation();
                    if (SETTINGS_ID.equals(selectedModuleId)) {
                        showSettings();
                    } else {
                        showModule(selectedModuleId);
                    }
                })
                .setNegativeButton("取消", null)
                .create();
        picker.setOnShowListener(shown -> {
            Window window = picker.getWindow();
            if (window == null) return;
            window.setBackgroundDrawable(GlassSkin.surface(this, 14));
            // 默认对话框背景的留白随背景一起被替换，这里补回内容与窗口边缘的间距。
            android.view.View content = window.findViewById(android.R.id.content);
            if (content != null) content.setPadding(dp(6), dp(4), dp(6), dp(4));
        });
        picker.show();
    }

    /** 选择框标题：15sp 浅色加粗，比主题默认标题小一档。 */
    private TextView targetDialogTitle() {
        TextView title = new TextView(this);
        title.setText("目标游戏");
        title.setTextSize(15);
        title.setTextColor(COLOR_TEXT);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        title.setPadding(dp(10), dp(2), dp(10), 0);
        return title;
    }

    /** 选择框每行：左侧青绿单选圆点 + 13sp 浅色文字，当前目标带玻璃高亮底。 */
    private android.widget.BaseAdapter targetAdapter() {
        return new android.widget.BaseAdapter() {
            @Override public int getCount() { return TARGET_LABELS.length; }
            @Override public Object getItem(int position) { return TARGET_LABELS[position]; }
            @Override public long getItemId(int position) { return position; }
            @Override public android.view.View getView(int position, android.view.View convertView,
                                                       android.view.ViewGroup parent) {
                LinearLayout row;
                android.widget.RadioButton mark;
                TextView label;
                if (convertView instanceof LinearLayout) {
                    row = (LinearLayout) convertView;
                    mark = (android.widget.RadioButton) row.getChildAt(0);
                    label = (TextView) row.getChildAt(1);
                } else {
                    row = new LinearLayout(MainActivity.this);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setGravity(Gravity.CENTER_VERTICAL);
                    mark = new android.widget.RadioButton(MainActivity.this);
                    mark.setClickable(false);
                    mark.setFocusable(false);
                    mark.setButtonTintList(android.content.res.ColorStateList.valueOf(GlassSkin.ACCENT));
                    row.addView(mark, new LinearLayout.LayoutParams(dp(22), dp(22)));
                    label = new TextView(MainActivity.this);
                    label.setTextSize(13);
                    label.setTextColor(COLOR_TEXT);
                    label.setPadding(dp(8), 0, 0, 0);
                    row.addView(label, new LinearLayout.LayoutParams(
                            0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
                }
                label.setText(TARGET_LABELS[position]);
                mark.setChecked(position == targetIndex(selectedTargetPackage));
                row.setPadding(dp(10), dp(8), dp(10), dp(8));
                row.setBackground(position == targetIndex(selectedTargetPackage)
                        ? GlassSkin.selected(MainActivity.this, 8)
                        : null);
                return row;
            }
        };
    }

    private static int targetIndex(String packageName) {
        return TargetGame.XXXY_PACKAGE.equals(packageName) ? 1 : 0;
    }

    private static String packageForTargetIndex(int index) {
        return index == 1 ? TargetGame.XXXY_PACKAGE : TargetGame.DHXY_PACKAGE;
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
        final int iconRes;
        final Class<? extends Activity> activity;

        Module(String id, String navLabel, String title, int iconRes,
               Class<? extends Activity> activity) {
            this.id = id;
            this.navLabel = navLabel;
            this.title = title;
            this.iconRes = iconRes;
            this.activity = activity;
        }
    }

    private static final class NavigationItem {
        final Module module;
        final LinearLayout root;
        final ImageView icon;
        final TextView label;
        final TextView state;
        int lastLabelColor = Integer.MIN_VALUE;
        int lastLabelStyle = Integer.MIN_VALUE;
        int lastIconColor = Integer.MIN_VALUE;
        int lastBackgroundSelected = -1;
        int lastSelected = -1;
        String lastStateText;
        int lastStateColor = Integer.MIN_VALUE;
        int lastStateVisible = -1;
        float lastAlpha = Float.NaN;
        String lastContentDescription;
        /** 等待中的呼吸动画；非等待状态必须为 null，避免每秒重复启动。 */
        android.animation.ObjectAnimator pulse;

        NavigationItem(Module module, LinearLayout root, ImageView icon, TextView label, TextView state) {
            this.module = module;
            this.root = root;
            this.icon = icon;
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
