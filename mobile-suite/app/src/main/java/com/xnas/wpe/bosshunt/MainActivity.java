package com.xnas.wpe.bosshunt;

import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Typeface;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import com.xnas.wpe.suite.GlassSkin;
import com.xnas.wpe.suite.LicenseManager;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;
import com.xnas.wpe.suite.TargetGame;
import com.xnas.wpe.suite.UiStatusFiles;

import java.io.File;
import java.util.ArrayList;
import java.util.List;

/** Explicit controls for巡回 boss hunting. */
public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity
        implements ModuleStatusProvider {
    private static final int COLOR_BG = GlassSkin.PAGE_BG;
    private static final int COLOR_TEXT = Color.rgb(239, 246, 248);
    private static final int COLOR_MUTED = Color.rgb(156, 179, 189);

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable refreshTask = new Runnable() {
        @Override public void run() {
            refreshPage();
            if (resumed) handler.postDelayed(this, refreshDelayMs);
        }
    };
    private final Runnable requestTimeout = () -> {
        startRequestPending = false;
        refreshPage();
    };

    private boolean resumed;
    private boolean statusReadKnown;
    private long refreshDelayMs = 1000L;
    private boolean startRequestPending;
    private boolean selectionLocked;
    private boolean stopRecoveryRequested;
    private BossHuntSelection selection = BossHuntSelection.empty();
    private BossHuntStatus status = BossHuntStatus.parse("");
    private final List<TextView> mapButtons = new ArrayList<>();
    private TextView currentMapView;
    private TextView nextMapView;
    private TextView countView;
    private TextView stateView;
    private TextView reasonView;
    private Button actionButton;
    private int builtCatalogSize;
    private LinearLayout mapRow;
    private Button mapPickButton;
    private Button teleportButton;

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(COLOR_BG);
        getWindow().setNavigationBarColor(COLOR_BG);
        // 先加载全量地图表，再读选择；否则存了非内置地图的 CSV 会被判成非法并清空。
        BossHuntMapCatalog.load(this);
        selection = BossHuntSelectionStore.load(selectionSettings());
        builtCatalogSize = BossHuntMapCatalog.MAPS.size();
        setContentView(buildContent());
        refreshPage();
    }

    @Override protected void onResume() {
        super.onResume();
        resumed = true;
        stopRecoveryRequested = false;
        handler.removeCallbacks(refreshTask);
        handler.post(refreshTask);
    }

    @Override protected void onPause() {
        resumed = false;
        stopRecoveryRequested = false;
        handler.removeCallbacks(refreshTask);
        super.onPause();
    }

    @Override protected void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        super.onDestroy();
    }

    private View buildContent() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(COLOR_BG);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        int padding = dp(GlassSkin.PADDING_H_DP);
        root.setPadding(padding, dp(GlassSkin.PADDING_V_DP), padding, dp(GlassSkin.PADDING_V_DP));
        scroll.addView(root, new ScrollView.LayoutParams(-1, -2));

        LinearLayout headingRow = new LinearLayout(this);
        headingRow.setOrientation(LinearLayout.HORIZONTAL);
        headingRow.setGravity(android.view.Gravity.CENTER_VERTICAL);
        TextView heading = textView(12, COLOR_TEXT, true);
        heading.setText("巡回地图");
        headingRow.addView(heading, new LinearLayout.LayoutParams(0, -2, 1f));
        mapPickButton = new Button(this);
        mapPickButton.setTextSize(12);
        mapPickButton.setAllCaps(false);
        mapPickButton.setOnClickListener(view -> showMapPicker());
        headingRow.addView(mapPickButton, new LinearLayout.LayoutParams(-2, dp(34)));
        teleportButton = new Button(this);
        teleportButton.setTextSize(12);
        teleportButton.setAllCaps(false);
        teleportButton.setText("直接传送");
        teleportButton.setOnClickListener(view -> showTeleportPicker());
        LinearLayout.LayoutParams teleportParams = new LinearLayout.LayoutParams(-2, dp(34));
        teleportParams.rightMargin = dp(6);
        headingRow.addView(teleportButton, teleportParams);
        addInfo(root, headingRow, 9);

        // 地图表有近百张，只把已选的显示成胶囊，其余在选择弹窗里挑。
        HorizontalScrollView mapScroll = new HorizontalScrollView(this);
        mapScroll.setHorizontalScrollBarEnabled(false);
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        mapButtons.clear();
        mapScroll.addView(row, new HorizontalScrollView.LayoutParams(-2, dp(44)));
        mapRow = row;
        LinearLayout.LayoutParams mapParams = matchWrap();
        mapParams.topMargin = dp(4);
        root.addView(mapScroll, mapParams);

        currentMapView = textView(13, COLOR_TEXT, false);
        nextMapView = textView(12, COLOR_MUTED, false);
        countView = textView(12, COLOR_MUTED, false);
        stateView = textView(14, COLOR_TEXT, true);
        reasonView = textView(11, COLOR_MUTED, false);
        addInfo(root, currentMapView, 15);
        addInfo(root, nextMapView, 4);
        addInfo(root, countView, 4);
        addInfo(root, stateView, 12);
        addInfo(root, reasonView, 4);

        actionButton = new Button(this);
        actionButton.setTextSize(GlassSkin.TEXT_BUTTON_SP);
        actionButton.setAllCaps(false);
        actionButton.setMinHeight(dp(GlassSkin.BUTTON_HEIGHT_DP));
        actionButton.setOnClickListener(view -> toggleHunt());
        LinearLayout.LayoutParams actionParams = matchWrap();
        actionParams.topMargin = dp(14);
        root.addView(actionButton, actionParams);

        TextView help = textView(11, COLOR_MUTED, false);
        help.setText("自动巡回所选地图；当前地图没有 Boss 时前往下一张。开始后返回游戏并保持游戏在前台，结束战斗后继续巡回。");
        addInfo(root, help, 8);
        return scroll;
    }

    private void refreshPage() {
        // worker 首次导出完整地图表后，按钮数量会变多，这里重画一次页面。
        if (BossHuntMapCatalog.MAPS.size() != builtCatalogSize) {
            builtCatalogSize = BossHuntMapCatalog.MAPS.size();
            selection = BossHuntSelectionStore.load(selectionSettings());
            setContentView(buildContent());
        }
        if (!stopRecoveryRequested && BossHuntService.hasRetryableStop(getFilesDir())) {
            stopRecoveryRequested = true;
            dispatch(new Intent(this, BossHuntService.class).setAction(BossHuntService.ACTION_STOP_READ),
                    BossHuntService.ACTION_STOP_READ);
        }
        UiStatusFiles.Snapshot statusSnapshot = readStatusSnapshot();
        statusReadKnown = statusSnapshot.known;
        status = parseStatus(statusSnapshot);
        boolean pending = BossHuntService.isHangupPending();
        boolean active = BossHuntService.isActive();
        boolean stopping = BossHuntService.isStopReadPending();
        boolean retryable = BossHuntService.hasRetryableStop(getFilesDir());
        boolean running = pending || active || stopping || retryable;
        refreshDelayMs = running ? 250L : 1000L;
        setSelectionLocked(running || startRequestPending);

        boolean fresh = status.isFresh(System.currentTimeMillis() / 1000L, 10L);
        String current = fresh ? status.mapName : "";
        if (current.isEmpty() && fresh && status.map > 0) current = BossHuntMapCatalog.nameOf(status.map);
        currentMapView.setText("当前地图：" + (current.isEmpty() ? "等待进入游戏" : current));
        int next = fresh && selection.contains(status.nextMap) ? status.nextMap : 0;
        nextMapView.setText(next == 0 ? "下一地图：等待状态数据" : "下一地图：" + BossHuntMapCatalog.nameOf(next));
        if (fresh && status.map > 0) {
            countView.setText("当前地图 Boss 数：" + status.countForMap(status.map));
        } else {
            countView.setText("当前地图 Boss 数：等待扫描");
        }
        stateView.setText(!running && BossHuntService.hasCompletedStop(getFilesDir())
                ? "已停止" : displayState(pending, active, stopping, retryable));
        String reason = !statusReadKnown ? "状态读取中，巡回状态未确认。"
                : status.reason == null ? "" : status.reason.trim();
        if (stopping || retryable) reason = BossHuntService.getServiceMessage();
        else if (!running) reason = BossHuntService.getServiceMessage();
        reasonView.setText(reason);
        reasonView.setVisibility(reason.isEmpty() ? View.GONE : View.VISIBLE);

        boolean supported = TargetGame.supportsModule(TargetGame.BOSSHUNT, TargetGame.getSelectedPackage(this));
        actionButton.setEnabled(supported && !stopping && !retryable && !startRequestPending
                && (running || (LicenseManager.isActive() && !selection.isEmpty())));
        GlassSkin.apply(actionButton, GlassSkin.actionTinted(this, GlassSkin.RADIUS_CONTROL_DP,
                running ? GlassSkin.ACTION_STOP : GlassSkin.ACTION_START));
        actionButton.setTextColor(GlassSkin.actionText());
        if (stopping) actionButton.setText("正在停止…");
        else if (retryable) actionButton.setText("正在停止…");
        else actionButton.setText(running ? "停止巡回" : "开始巡回");
    }

    private void setSelectionLocked(boolean locked) {
        selectionLocked = locked;
        if (mapPickButton != null) {
            mapPickButton.setEnabled(!locked);
            mapPickButton.setAlpha(locked ? 0.55f : 1f);
            mapPickButton.setText("选择地图（已选 " + selection.size() + "）");
            GlassSkin.apply(mapPickButton, GlassSkin.actionTinted(this, 8, GlassSkin.ACTION_START));
            mapPickButton.setTextColor(GlassSkin.actionText());
        }
        if (teleportButton != null) {
            teleportButton.setEnabled(!locked);
            teleportButton.setAlpha(locked ? 0.55f : 1f);
            GlassSkin.apply(teleportButton, GlassSkin.actionTinted(this, 8, GlassSkin.ACTION_STOP));
            teleportButton.setTextColor(GlassSkin.actionText());
        }
        renderSelectedMaps();
    }

    /** 只把已选地图画成胶囊，地图表近百张，全部平铺点不过来。 */
    private void renderSelectedMaps() {
        if (mapRow == null) return;
        mapRow.removeAllViews();
        mapButtons.clear();
        for (int id : selection.asList()) {
            TextView chip = textView(12, COLOR_TEXT, false);
            chip.setText(BossHuntMapCatalog.nameOf(id));
            chip.setGravity(android.view.Gravity.CENTER);
            chip.setPadding(dp(11), 0, dp(11), 0);
            GlassSkin.apply(chip, GlassSkin.selectedDanger(this, GlassSkin.RADIUS_CONTROL_DP));
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-2, dp(40));
            params.rightMargin = dp(6);
            mapRow.addView(chip, params);
            mapButtons.add(chip);
        }
        if (selection.isEmpty()) {
            TextView empty = textView(11, COLOR_MUTED, false);
            empty.setText("还没有选择地图，点右上角“选择地图”");
            mapRow.addView(empty);
        }
    }

    private TextView pickerTitle(String text) {
        TextView title = textView(15, COLOR_TEXT, true);
        title.setText(text);
        title.setPadding(dp(18), dp(12), dp(18), 0);
        return title;
    }

    /** 直接传送：单选一张地图，确认后派发一次性传送请求。 */
    private void showTeleportPicker() {
        if (configurationLocked()) {
            Toast.makeText(this, "请先停止巡回任务再传送", Toast.LENGTH_SHORT).show();
            return;
        }
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        list.setPadding(dp(10), dp(6), dp(10), dp(6));
        final java.util.List<CheckBox> boxes = new java.util.ArrayList<>();
        for (BossHuntMapCatalog.MapInfo map : BossHuntMapCatalog.MAPS) {
            CheckBox box = new CheckBox(this);
            box.setText(map.name);
            box.setTextSize(13);
            box.setTextColor(COLOR_TEXT);
            box.setTag(map.id);
            box.setOnCheckedChangeListener((button, checked) -> {
                if (!checked) return;
                for (CheckBox other : boxes) if (other != button) other.setChecked(false);
            });
            list.addView(box, new LinearLayout.LayoutParams(-1, dp(34)));
            boxes.add(box);
        }
        ScrollView scroll = new ScrollView(this);
        scroll.addView(list, new ScrollView.LayoutParams(-1, -2));
        android.app.AlertDialog dialog = new android.app.AlertDialog.Builder(this,
                android.R.style.Theme_Material_Dialog_Alert)
                .setCustomTitle(pickerTitle("直接传送到…"))
                .setView(scroll)
                .setPositiveButton("传送", null)
                .setNegativeButton("取消", null)
                .create();
        dialog.setOnShowListener(shown -> {
            android.view.Window window = dialog.getWindow();
            if (window != null) window.setBackgroundDrawable(GlassSkin.surface(this, 14));
            dialog.getButton(android.app.AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
                int target = 0;
                for (CheckBox box : boxes) {
                    if (box.isChecked()) target = (Integer) box.getTag();
                }
                if (target <= 0) {
                    Toast.makeText(this, "请选择一张地图", Toast.LENGTH_SHORT).show();
                    return;
                }
                dispatchTeleport(target);
                dialog.dismiss();
            });
        });
        dialog.show();
    }

    private void dispatchTeleport(int map) {
        Intent intent = new Intent(this, BossHuntService.class)
                .setAction(BossHuntService.ACTION_TELEPORT)
                .putExtra(BossHuntService.EXTRA_MAPS, String.valueOf(map));
        try {
            if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent);
            else startService(intent);
            Toast.makeText(this, "已请求传送到 " + BossHuntMapCatalog.nameOf(map), Toast.LENGTH_SHORT).show();
        } catch (RuntimeException error) {
            Toast.makeText(this, "传送请求未发送", Toast.LENGTH_SHORT).show();
        }
    }

    /** 全量地图多选弹窗：客户端地图表导出的所有地图都在这里。 */
    private void showMapPicker() {
        if (configurationLocked()) { refreshPage(); return; }
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        list.setPadding(dp(10), dp(6), dp(10), dp(6));
        final java.util.List<CheckBox> boxes = new java.util.ArrayList<>();
        for (BossHuntMapCatalog.MapInfo map : BossHuntMapCatalog.MAPS) {
            CheckBox box = new CheckBox(this);
            box.setText(map.name);
            box.setTextSize(13);
            box.setTextColor(COLOR_TEXT);
            box.setTag(map.id);
            box.setChecked(selection.contains(map.id));
            list.addView(box, new LinearLayout.LayoutParams(-1, dp(34)));
            boxes.add(box);
        }
        ScrollView scroll = new ScrollView(this);
        scroll.addView(list, new ScrollView.LayoutParams(-1, -2));
        android.app.AlertDialog dialog = new android.app.AlertDialog.Builder(this,
                android.R.style.Theme_Material_Dialog_Alert)
                .setCustomTitle(pickerTitle("选择巡回地图"))
                .setView(scroll)
                .setPositiveButton("确定", null)
                .setNegativeButton("取消", null)
                .create();
        dialog.setOnShowListener(shown -> {
            android.view.Window window = dialog.getWindow();
            if (window != null) window.setBackgroundDrawable(GlassSkin.surface(this, 14));
            dialog.getButton(android.app.AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
                java.util.List<Integer> picked = new java.util.ArrayList<>();
                for (CheckBox box : boxes) {
                    if (box.isChecked()) picked.add((Integer) box.getTag());
                }
                saveSelection(BossHuntSelection.from(picked));
                dialog.dismiss();
            });
        });
        dialog.show();
    }

    private void saveSelection(BossHuntSelection value) {
        if (value == null || configurationLocked()) return;
        selection = value;
        BossHuntSelectionStore.save(selectionSettings(), selection);
        refreshPage();
    }

    private boolean configurationLocked() {
        return selectionLocked || startRequestPending || BossHuntService.isHangupPending()
                || BossHuntService.isActive() || BossHuntService.isStopReadPending()
                || BossHuntService.hasRetryableStop(getFilesDir());
    }

    private BossHuntSelectionStore.Settings selectionSettings() {
        SharedPreferences preferences = getSharedPreferences("settings", MODE_PRIVATE);
        return new BossHuntSelectionStore.Settings() {
            @Override public boolean contains(String key) { return preferences.contains(key); }
            @Override public String getString(String key, String fallback) { return preferences.getString(key, fallback); }
            @Override public void putString(String key, String value) { preferences.edit().putString(key, value).apply(); }
        };
    }

    private void toggleHunt() {
        if (BossHuntService.isStopReadPending()) return;
        boolean running = BossHuntService.isHangupPending() || BossHuntService.isActive()
                || BossHuntService.hasRetryableStop(getFilesDir());
        if (running) {
            startRequestPending = false;
            handler.removeCallbacks(requestTimeout);
            dispatch(new Intent(this, BossHuntService.class).setAction(BossHuntService.ACTION_STOP_READ),
                    BossHuntService.ACTION_STOP_READ);
            return;
        }
        if (selection.isEmpty()) {
            Toast.makeText(this, "请至少选择一张地图", Toast.LENGTH_SHORT).show();
            return;
        }
        if (!LicenseManager.isActive()) {
            Toast.makeText(this, "请先完成在线授权", Toast.LENGTH_SHORT).show();
            startActivity(new Intent(this, com.xnas.wpe.suite.ActivationActivity.class));
            return;
        }
        startRequestPending = true;
        Intent intent = new Intent(this, BossHuntService.class).setAction(BossHuntService.ACTION_START_HANGUP)
                .putExtra(BossHuntService.EXTRA_MAPS, selection.toCsv());
        try {
            dispatch(intent, BossHuntService.ACTION_START_HANGUP);
        } catch (RuntimeException error) {
            startRequestPending = false;
            Toast.makeText(this, "巡回请求未能发送，请重试", Toast.LENGTH_SHORT).show();
            refreshPage();
            return;
        }
        handler.removeCallbacks(requestTimeout);
        handler.postDelayed(requestTimeout, 1000L);
        returnToGame();
        refreshPage();
    }

    private void dispatch(Intent intent, String action) {
        if (Build.VERSION.SDK_INT >= 26) {
            startForegroundService(intent);
        } else {
            startService(intent);
        }
        refreshPage();
    }

    private void returnToGame() {
        String packageName = TargetGame.getSelectedPackage(this);
        Intent launch = getPackageManager().getLaunchIntentForPackage(packageName);
        if (launch == null) {
            Toast.makeText(this, "找不到游戏启动入口，请先打开游戏", Toast.LENGTH_LONG).show();
            return;
        }
        launch.addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT | Intent.FLAG_ACTIVITY_NEW_TASK);
        startActivity(launch);
    }

    private String displayState(boolean pending, boolean active, boolean stopping, boolean retryable) {
        if (stopping) return "正在停止";
        if (retryable) return "正在停止";
        if (pending || startRequestPending) return "正在读取地图";
        if (!active) return "未开始";
        if (!status.isFresh(System.currentTimeMillis() / 1000L, 10L)) return "状态未确认";
        switch (status.state) {
            case "waiting_role": return "等待角色登录";
            case "waiting_map":
            case "reading": return "正在读取地图";
            case "map_wait": return "正在前往地图";
            case "teleport_wait": return "正在前往 BOSS";
            case "dialog_wait": return "正在确认挑战";
            case "challenge_ready": return "准备挑战";
            case "challenge_wait": return "正在挑战";
            case "battle": return "战斗中";
            case "cooldown": return "等待下一轮巡回";
            case "waiting_targets": return "等待 BOSS 或继续巡图";
            case "paused": return "已暂停";
            case "fatal": return "运行异常";
            case "stopping": return "正在停止";
            case "stopped": return "未开始";
            default: return status.reason.isEmpty() ? "状态未确认" : "状态未确认：" + status.reason;
        }
    }

    @Override public ModuleStatus getModuleStatus() {
        String target = TargetGame.getSelectedPackage(this);
        if (BossHuntService.isStopReadPending()) return new ModuleStatus(ModuleStatus.State.STOPPING,
                "正在停止", target, BossHuntService.getServiceMessage(), "等待清理确认。");
        if (BossHuntService.hasRetryableStop(getFilesDir())) return new ModuleStatus(ModuleStatus.State.STOPPING,
                "正在停止", target, BossHuntService.getServiceMessage(), "正在等待清理。");
        boolean pending = BossHuntService.isHangupPending();
        boolean active = BossHuntService.isActive();
        UiStatusFiles.Snapshot statusSnapshot = readStatusSnapshot(target);
        if (!statusSnapshot.known || !statusSnapshot.targetKey.equals(target)) {
            if (pending || active) return new ModuleStatus(ModuleStatus.State.RUNNING,
                    "状态未确认", target,
                    "本地巡回进程正在运行；巡回状态读取中，游戏端进度未确认。",
                    "点击停止巡回结束 worker 并清理。");
            return ModuleStatus.unknown(target,
                    "本地 Boss 巡回状态读取中或暂不可用；运行状态未确认。");
        }
        BossHuntStatus latest = parseStatus(statusSnapshot);
        if (pending || active) {
            if ("fatal".equals(latest.state)) return new ModuleStatus(ModuleStatus.State.ERROR,
                    "运行异常", target, latest.reason, "查看异常原因后停止或重试。");
            return new ModuleStatus(ModuleStatus.State.RUNNING,
                    displayState(pending, true, false, false), target,
                    latest.reason.isEmpty() ? "本地巡回状态，游戏端结果未确认。" : latest.reason,
                    "点击停止巡回结束 worker 并清理。");
        }
        boolean completed = BossHuntService.hasCompletedStop(getFilesDir());
        return new ModuleStatus(ModuleStatus.State.STOPPED, completed ? "已停止" : "未开始", target,
                completed ? BossHuntService.getServiceMessage() : "巡回当前未运行。",
                "选择地图后显式开始；打开页面不会自动启动。");
    }

    private UiStatusFiles.Snapshot readStatusSnapshot() {
        return readStatusSnapshot(TargetGame.getSelectedPackage(this));
    }

    private UiStatusFiles.Snapshot readStatusSnapshot(String targetKey) {
        File file = new File(getFilesDir(), "status");
        return UiStatusFiles.readForDisplay("bosshunt", file, targetKey);
    }

    private static BossHuntStatus parseStatus(UiStatusFiles.Snapshot snapshot) {
        return BossHuntStatus.parse(snapshot.known && snapshot.exists ? snapshot.text : "");
    }

    private void addInfo(LinearLayout root, View view, int margin) {
        LinearLayout.LayoutParams params = matchWrap();
        params.topMargin = dp(margin);
        root.addView(view, params);
    }

    private TextView textView(int size, int color, boolean bold) {
        TextView view = new TextView(this);
        view.setTextColor(color);
        view.setTextSize(size);
        if (bold) view.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        return view;
    }

    private LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
}
