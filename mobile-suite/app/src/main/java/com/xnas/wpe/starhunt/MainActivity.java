package com.xnas.wpe.starhunt;

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

/** One-button StarHunt hangup controls. */
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
    private boolean hasSelectionLockState;
    private boolean lastSelectionLocked;
    private StarHuntSelection selectedLevels = StarHuntSelection.empty();
    private StarHuntStatus status = StarHuntStatus.parse("");
    private TextView mapView;
    private TextView countView;
    private TextView stateView;
    private TextView reasonView;
    private Button toggleButton;
    private Button teleportButton;
    private Button cycleMapButton;
    private LinearLayout levelGrid;
    private LinearLayout otherSelectedPanel;
    private LinearLayout chipsRow;

    @Override protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().setStatusBarColor(COLOR_BG);
        getWindow().setNavigationBarColor(COLOR_BG);
        selectedLevels = StarHuntSelectionStore.load(selectionSettings());
        com.xnas.wpe.suite.GameMapCatalog.load(this);
        setContentView(buildContent());
        updateCycleLabel();
        refreshPage();
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
        scroll.addView(root, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout headingRow = new LinearLayout(this);
        headingRow.setOrientation(LinearLayout.HORIZONTAL);
        headingRow.setGravity(android.view.Gravity.CENTER_VERTICAL);
        TextView heading = textView(12, COLOR_TEXT, true);
        heading.setText("挂机设置");
        headingRow.addView(heading, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        teleportButton = new Button(this);
        teleportButton.setText("直接传送");
        teleportButton.setTextSize(12);
        teleportButton.setAllCaps(false);
        teleportButton.setOnClickListener(view -> showTeleportPicker());
        headingRow.addView(teleportButton, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(34)));
        cycleMapButton = new Button(this);
        cycleMapButton.setTextSize(12);
        cycleMapButton.setAllCaps(false);
        cycleMapButton.setOnClickListener(view -> showCycleMapPicker());
        LinearLayout.LayoutParams cycleParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(34));
        cycleParams.rightMargin = dp(6);
        headingRow.addView(cycleMapButton, cycleParams);
        LinearLayout.LayoutParams headingParams = matchWrap();
        headingParams.topMargin = dp(9);
        headingParams.bottomMargin = dp(7);
        root.addView(headingRow, headingParams);

        LinearLayout levelPanel = new LinearLayout(this);
        levelPanel.setOrientation(LinearLayout.VERTICAL);
        levelPanel.setPadding(dp(12), dp(7), dp(12), dp(7));
        GlassSkin.apply(levelPanel, GlassSkin.surface(this, GlassSkin.RADIUS_CONTAINER_DP));
        TextView levelLabel = textView(13, COLOR_TEXT, false);
        levelLabel.setText("星级预设（1–30）");
        levelLabel.setGravity(android.view.Gravity.CENTER_VERTICAL);
        levelPanel.addView(levelLabel, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(32)));

        levelGrid = new LinearLayout(this);
        levelGrid.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams gridParams = matchWrap();
        gridParams.topMargin = dp(4);
        levelPanel.addView(levelGrid, gridParams);
        for (int rowIndex = 0; rowIndex < 3; rowIndex++) {
            LinearLayout row = new LinearLayout(this);
            LinearLayout.LayoutParams rowParams = matchWrap();
            if (rowIndex > 0) rowParams.topMargin = dp(4);
            levelGrid.addView(row, rowParams);
            for (int column = 0; column < 10; column++) {
                final int level = rowIndex * 10 + column + 1;
                TextView item = textView(10, COLOR_TEXT, false);
                item.setText(level + "星");
                item.setGravity(android.view.Gravity.CENTER);
                item.setContentDescription((selectedLevels.contains(level) ? "已选 " : "选择 ")
                        + level + " 星");
                item.setTag(Integer.valueOf(level));
                item.setFocusable(true);
                item.setClickable(true);
                LinearLayout.LayoutParams itemParams = new LinearLayout.LayoutParams(0, dp(34), 1f);
                if (column > 0) itemParams.leftMargin = dp(3);
                row.addView(item, itemParams);
                item.setOnClickListener(view -> {
                    if (configurationLocked()) {
                        refreshPage();
                        return;
                    }
                    saveSelection(selectedLevels.contains(level)
                            ? selectedLevels.without(level) : selectedLevels.withAdded(level));
                });
            }
        }

        otherSelectedPanel = new LinearLayout(this);
        otherSelectedPanel.setOrientation(LinearLayout.VERTICAL);
        TextView otherSelectedLabel = textView(11, COLOR_MUTED, false);
        otherSelectedLabel.setText("其他已选：");
        otherSelectedPanel.addView(otherSelectedLabel, matchWrap());
        HorizontalScrollView chipsScroll = new HorizontalScrollView(this);
        chipsScroll.setHorizontalScrollBarEnabled(false);
        chipsScroll.setFillViewport(false);
        chipsScroll.setContentDescription("其他已选星级，可左右滑动逐项移除");
        chipsRow = new LinearLayout(this);
        chipsRow.setGravity(android.view.Gravity.CENTER_VERTICAL);
        chipsScroll.addView(chipsRow, new HorizontalScrollView.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(38)));
        LinearLayout.LayoutParams chipsParams = matchWrap();
        chipsParams.topMargin = dp(2);
        otherSelectedPanel.addView(chipsScroll, chipsParams);
        LinearLayout.LayoutParams otherParams = matchWrap();
        otherParams.topMargin = dp(5);
        levelPanel.addView(otherSelectedPanel, otherParams);
        root.addView(levelPanel, matchWrap());
        renderSelectionButtons();
        renderOtherSelectedChips();

        mapView = textView(12, COLOR_MUTED, false);
        countView = textView(12, COLOR_MUTED, false);
        stateView = textView(14, COLOR_TEXT, true);
        reasonView = textView(11, COLOR_MUTED, false);
        addInfo(root, mapView, 13);
        addInfo(root, countView, 3);



        toggleButton = new Button(this);
        toggleButton.setText("开始挂机");
        toggleButton.setTextColor(COLOR_TEXT);
        toggleButton.setTextSize(GlassSkin.TEXT_BUTTON_SP);
        toggleButton.setAllCaps(false);
        toggleButton.setMinHeight(dp(GlassSkin.BUTTON_HEIGHT_DP));
        GlassSkin.apply(toggleButton, GlassSkin.actionTinted(this,
                GlassSkin.RADIUS_CONTROL_DP, GlassSkin.ACTION_START));
        toggleButton.setTextColor(GlassSkin.actionText());
        toggleButton.setOnClickListener(view -> toggleHangup());
        LinearLayout.LayoutParams buttonParams = matchWrap();
        buttonParams.topMargin = dp(14);
        root.addView(toggleButton, buttonParams);

        TextView help = textView(11, COLOR_MUTED, false);
        help.setText("开始后返回游戏；没有目标时等待刷新；请让游戏保持前台");
        addInfo(root, help, 8);
        return scroll;
    }

    private void addInfo(LinearLayout root, View view, int topMarginDp) {
        LinearLayout.LayoutParams params = matchWrap();
        params.topMargin = dp(topMarginDp);
        root.addView(view, params);
    }

    private void refreshPage() {
        UiStatusFiles.Snapshot statusSnapshot = readStatusSnapshot();
        statusReadKnown = statusSnapshot.known;
        status = parseStatus(statusSnapshot);
        boolean pending = StarHuntService.isHangupPending();
        boolean active = StarHuntService.isActive();
        boolean stopping = StarHuntService.isStopReadPending();
        boolean retryable = StarHuntService.hasRetryableStop(getFilesDir());
        boolean running = pending || active || stopping || retryable;
        refreshDelayMs = running ? 250L : 1000L;
        boolean selectionLocked = running || startRequestPending;
        updateSelectionLock(selectionLocked);
        boolean freshStatus = status.isFresh(System.currentTimeMillis() / 1000L, 10L);
        String map = freshStatus && !status.mapName.isEmpty() ? status.mapName : "";
        if (map.isEmpty() && freshStatus && status.map > 0) map = "地图 " + status.map;
        mapView.setText("当前地图：" + (map.isEmpty() ? "等待进入游戏" : map));
        if (status.isFresh(System.currentTimeMillis() / 1000L, 5L)) {
            countView.setText("已选 " + selectedLevels.size() + " 个星级 · 当前候选 "
                    + countForSelectedLevels() + " 个");
        } else {
            countView.setText("已选 " + selectedLevels.size() + " 个星级 · 当前候选尚未扫描");
        }
        stateView.setText(displayState(pending, active, stopping, retryable));
        String reason = !statusReadKnown ? "状态读取中，运行数据未确认。"
                : status.reason == null ? "" : status.reason.trim();
        if (retryable) reason = "清理尚未确认，请重试停止。" + (reason.isEmpty() ? "" : " " + reason);
        reasonView.setText(reason);
        reasonView.setVisibility(reason.isEmpty() ? View.GONE : View.VISIBLE);
        boolean supported = TargetGame.supportsModule(TargetGame.STARHUNT,
                TargetGame.getSelectedPackage(this));
        toggleButton.setEnabled(supported && !StarHuntService.isStopReadPending()
                && !startRequestPending && (running
                || (LicenseManager.isActive() && !selectedLevels.isEmpty())));
        GlassSkin.apply(toggleButton, GlassSkin.actionTinted(this,
                GlassSkin.RADIUS_CONTROL_DP, running ? GlassSkin.ACTION_STOP : GlassSkin.ACTION_START));
        toggleButton.setTextColor(GlassSkin.actionText());
        if (stopping) toggleButton.setText("正在停止…");
        else if (retryable) toggleButton.setText("重试停止");
        else toggleButton.setText(running ? "停止挂机" : "开始挂机");
        if (teleportButton != null) {
            // 运行中也能传送：服务会把命令切成 teleport，到达后自动恢复找星。
            boolean usable = !stopping && !retryable && !startRequestPending;
            teleportButton.setEnabled(usable);
            teleportButton.setAlpha(usable ? 1f : 0.55f);
            GlassSkin.apply(teleportButton, GlassSkin.actionTinted(this, 8, GlassSkin.ACTION_STOP));
            teleportButton.setTextColor(GlassSkin.actionText());
            cycleMapButton.setEnabled(true);
            GlassSkin.apply(cycleMapButton, GlassSkin.actionTinted(this, 8, GlassSkin.ACTION_START));
            cycleMapButton.setTextColor(GlassSkin.actionText());
        }
    }

    /** 直接传送：单选一张地图，派发一次性传送请求。同名地图带 ID 以便区分。 */
    private void showTeleportPicker() {
        java.util.List<com.xnas.wpe.suite.GameMapCatalog.MapInfo> maps =
                com.xnas.wpe.suite.GameMapCatalog.maps();
        java.util.Map<String, Integer> nameCounts = new java.util.HashMap<>();
        for (com.xnas.wpe.suite.GameMapCatalog.MapInfo map : maps) {
            Integer count = nameCounts.get(map.name);
            nameCounts.put(map.name, count == null ? 1 : count + 1);
        }
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        list.setPadding(dp(10), dp(6), dp(10), dp(6));
        final java.util.List<android.widget.CheckBox> boxes = new java.util.ArrayList<>();
        for (com.xnas.wpe.suite.GameMapCatalog.MapInfo map : maps) {
            android.widget.CheckBox box = new android.widget.CheckBox(this);
            int sameName = nameCounts.get(map.name);
            box.setText(sameName > 1 ? map.name + " (" + map.id + ")" : map.name);
            box.setTextSize(13);
            box.setTextColor(COLOR_TEXT);
            box.setTag(map.id);
            box.setOnCheckedChangeListener((button, checked) -> {
                if (!checked) return;
                for (android.widget.CheckBox other : boxes) if (other != button) other.setChecked(false);
            });
            list.addView(box, new LinearLayout.LayoutParams(-1, dp(34)));
            boxes.add(box);
        }
        ScrollView scroll = new ScrollView(this);
        scroll.addView(list, new ScrollView.LayoutParams(-1, -2));
        android.app.AlertDialog dialog = new android.app.AlertDialog.Builder(this,
                android.R.style.Theme_Material_Dialog_Alert)
                .setTitle("直接传送到…")
                .setView(scroll)
                .setPositiveButton("传送", null)
                .setNegativeButton("取消", null)
                .create();
        dialog.setOnShowListener(shown -> dialog.getButton(android.app.AlertDialog.BUTTON_POSITIVE)
                .setOnClickListener(v -> {
                    int target = 0;
                    for (android.widget.CheckBox box : boxes) {
                        if (box.isChecked()) target = (Integer) box.getTag();
                    }
                    if (target <= 0) {
                        Toast.makeText(this, "请选择一张地图", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    dispatchTeleport(target);
                    dialog.dismiss();
                }));
        dialog.show();
    }

    private void dispatchTeleport(int map) {
        // 运行中直接改命令实测不生效（命令已同步但 Lua 不执行），先停挂机、清理完再传送。
        if (StarHuntService.isActive() || StarHuntService.isStopReadPending()) {
            Toast.makeText(this, "正在停止挂机，清理完成后自动传送…", Toast.LENGTH_SHORT).show();
            try {
                startService(new Intent(this, StarHuntService.class)
                        .setAction(StarHuntService.ACTION_STOP_READ));
            } catch (RuntimeException ignored) {
                // 停止请求发不出去就直接尝试传送。
            }
            waitThenTeleport(map, 0);
            return;
        }
        sendTeleport(map);
    }

    private void waitThenTeleport(final int map, final int attempt) {
        handler.postDelayed(() -> {
            if (StarHuntService.isActive() || StarHuntService.isStopReadPending()) {
                if (attempt < 20) waitThenTeleport(map, attempt + 1);
                else Toast.makeText(this, "挂机未能停止，未传送", Toast.LENGTH_SHORT).show();
            } else {
                sendTeleport(map);
            }
        }, 800L);
    }

    private void sendTeleport(int map) {
        Intent intent = new Intent(this, StarHuntService.class)
                .setAction(StarHuntService.ACTION_TELEPORT)
                .putExtra(StarHuntService.EXTRA_MAPS, String.valueOf(map));
        try {
            if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent);
            else startService(intent);
            Toast.makeText(this, "已请求传送到 " + com.xnas.wpe.suite.GameMapCatalog.nameOf(map),
                    Toast.LENGTH_SHORT).show();
        } catch (RuntimeException error) {
            Toast.makeText(this, "传送请求未发送", Toast.LENGTH_SHORT).show();
        }
    }

    /** 巡回地图：多选，只勾一张时保持原来的单图行为；同名地图带 ID 以便区分。 */
    private void showCycleMapPicker() {
        final java.util.Set<Integer> selected = new java.util.HashSet<>();
        for (int id : loadCycleMaps()) selected.add(id);
        java.util.List<com.xnas.wpe.suite.GameMapCatalog.MapInfo> maps =
                com.xnas.wpe.suite.GameMapCatalog.maps();
        java.util.Map<String, Integer> nameCounts = new java.util.HashMap<>();
        for (com.xnas.wpe.suite.GameMapCatalog.MapInfo map : maps) {
            Integer count = nameCounts.get(map.name);
            nameCounts.put(map.name, count == null ? 1 : count + 1);
        }
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        list.setPadding(dp(10), dp(6), dp(10), dp(6));
        final java.util.List<android.widget.CheckBox> boxes = new java.util.ArrayList<>();
        for (com.xnas.wpe.suite.GameMapCatalog.MapInfo map : maps) {
            android.widget.CheckBox box = new android.widget.CheckBox(this);
            int sameName = nameCounts.get(map.name);
            box.setText(sameName > 1 ? map.name + " (" + map.id + ")" : map.name);
            box.setTextSize(13);
            box.setTextColor(COLOR_TEXT);
            box.setTag(map.id);
            box.setChecked(selected.contains(map.id));
            list.addView(box, new LinearLayout.LayoutParams(-1, dp(34)));
            boxes.add(box);
        }
        ScrollView scroll = new ScrollView(this);
        scroll.addView(list, new ScrollView.LayoutParams(-1, -2));
        android.app.AlertDialog dialog = new android.app.AlertDialog.Builder(this,
                android.R.style.Theme_Material_Dialog_Alert)
                .setTitle("巡回地图（多选）")
                .setView(scroll)
                .setPositiveButton("保存", null)
                .setNegativeButton("取消", null)
                .create();
        dialog.setOnShowListener(shown -> dialog.getButton(android.app.AlertDialog.BUTTON_POSITIVE)
                .setOnClickListener(v -> {
                    java.util.List<Integer> picked = new java.util.ArrayList<>();
                    for (android.widget.CheckBox box : boxes) {
                        if (box.isChecked()) picked.add((Integer) box.getTag());
                    }
                    java.util.Collections.sort(picked);
                    StringBuilder csv = new StringBuilder();
                    for (int id : picked) {
                        if (csv.length() > 0) csv.append(',');
                        csv.append(id);
                    }
                    getSharedPreferences("settings", MODE_PRIVATE).edit()
                            .putString("cycle_maps_v1", csv.toString()).apply();
                    updateCycleLabel();
                    dialog.dismiss();
                }));
        dialog.show();
    }

    private int[] loadCycleMaps() {
        String csv = getSharedPreferences("settings", MODE_PRIVATE)
                .getString("cycle_maps_v1", "");
        if (csv.isEmpty()) return new int[0];
        String[] parts = csv.split(",");
        int[] maps = new int[parts.length];
        int count = 0;
        for (String part : parts) {
            try {
                int id = Integer.parseInt(part.trim());
                if (id > 0) maps[count++] = id;
            } catch (NumberFormatException ignored) {
                // 忽略非法项。
            }
        }
        return java.util.Arrays.copyOf(maps, count);
    }

    private void updateCycleLabel() {
        if (cycleMapButton == null) return;
        int count = loadCycleMaps().length;
        cycleMapButton.setText(count > 1 ? "巡回地图（" + count + "）" : "巡回地图");
    }

    private void updateSelectionLock(boolean locked) {
        if (hasSelectionLockState && lastSelectionLocked == locked) return;
        hasSelectionLockState = true;
        lastSelectionLocked = locked;
        for (int rowIndex = 0; rowIndex < levelGrid.getChildCount(); rowIndex++) {
            View row = levelGrid.getChildAt(rowIndex);
            if (!(row instanceof ViewGroup)) continue;
            ViewGroup buttons = (ViewGroup) row;
            for (int column = 0; column < buttons.getChildCount(); column++) {
                View button = buttons.getChildAt(column);
                button.setEnabled(!locked);
                button.setAlpha(locked ? 0.55f : 1f);
            }
        }
        for (int index = 0; index < chipsRow.getChildCount(); index++) {
            View chip = chipsRow.getChildAt(index);
            chip.setEnabled(!locked);
            chip.setAlpha(locked ? 0.55f : 1f);
        }
    }

    private void renderSelectionButtons() {
        if (levelGrid == null) return;
        for (int rowIndex = 0; rowIndex < levelGrid.getChildCount(); rowIndex++) {
            View row = levelGrid.getChildAt(rowIndex);
            if (!(row instanceof ViewGroup)) continue;
            ViewGroup buttons = (ViewGroup) row;
            for (int column = 0; column < buttons.getChildCount(); column++) {
                View child = buttons.getChildAt(column);
                if (!(child instanceof TextView)) continue;
                TextView item = (TextView) child;
                Object tag = item.getTag();
                if (!(tag instanceof Integer)) continue;
                int level = (Integer) tag;
                boolean selected = selectedLevels.contains(level);
                item.setTextColor(COLOR_TEXT);
                item.setContentDescription((selected ? "已选 " : "选择 ") + level + " 星");
                GlassSkin.apply(item, selected
                        ? GlassSkin.selectedDanger(this, GlassSkin.RADIUS_CONTROL_DP)
                        : GlassSkin.input(this, GlassSkin.RADIUS_CONTROL_DP));
            }
        }
    }

    private void renderOtherSelectedChips() {
        if (chipsRow == null || otherSelectedPanel == null) return;
        java.util.List<Integer> values = selectedLevels.asList();
        java.util.HashSet<Integer> remaining = new java.util.HashSet<>();
        for (int level : values) {
            if (level > 30) remaining.add(level);
        }
        for (int index = chipsRow.getChildCount() - 1; index >= 0; index--) {
            View chip = chipsRow.getChildAt(index);
            Object tag = chip.getTag();
            if (!(tag instanceof Integer) || !remaining.remove((Integer) tag)) {
                chipsRow.removeViewAt(index);
            }
        }
        for (int level : values) {
            if (remaining.remove(level)) addOtherSelectedChip(level);
        }
        otherSelectedPanel.setVisibility(chipsRow.getChildCount() > 0 ? View.VISIBLE : View.GONE);
    }

    private void addOtherSelectedChip(int level) {
        TextView chip = textView(11, COLOR_TEXT, false);
        chip.setText(level + "星  ×");
        chip.setGravity(android.view.Gravity.CENTER);
        chip.setPadding(dp(9), 0, dp(9), 0);
        chip.setContentDescription("移除 " + level + " 星");
        chip.setTag(Integer.valueOf(level));
        chip.setFocusable(true);
        chip.setClickable(true);
        chip.setEnabled(!lastSelectionLocked);
        chip.setAlpha(lastSelectionLocked ? 0.55f : 1f);
        GlassSkin.apply(chip, GlassSkin.selectedDanger(this, GlassSkin.RADIUS_PILL_DP));
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(32));
        params.rightMargin = dp(6);
        chipsRow.addView(chip, params);
        chip.setOnClickListener(view -> {
            if (configurationLocked()) {
                refreshPage();
                return;
            }
            saveSelection(selectedLevels.without(level));
        });
    }

    private boolean saveSelection(StarHuntSelection selection) {
        if (selection == null || configurationLocked()) return false;
        StarHuntSelection previous = selectedLevels;
        selectedLevels = StarHuntSelection.parseCsv(selection.toCsv());
        StarHuntSelectionStore.save(selectionSettings(), selectedLevels);
        renderSelectionButtons();
        if (!sameOtherSelections(previous, selectedLevels)) renderOtherSelectedChips();
        refreshPage();
        return true;
    }

    private boolean sameOtherSelections(StarHuntSelection first, StarHuntSelection second) {
        java.util.List<Integer> firstValues = first.asList();
        java.util.List<Integer> secondValues = second.asList();
        int firstIndex = 0;
        int secondIndex = 0;
        while (true) {
            while (firstIndex < firstValues.size() && firstValues.get(firstIndex) <= 30) firstIndex++;
            while (secondIndex < secondValues.size() && secondValues.get(secondIndex) <= 30) secondIndex++;
            if (firstIndex == firstValues.size() || secondIndex == secondValues.size()) {
                return firstIndex == firstValues.size() && secondIndex == secondValues.size();
            }
            if (!firstValues.get(firstIndex).equals(secondValues.get(secondIndex))) return false;
            firstIndex++;
            secondIndex++;
        }
    }

    private boolean configurationLocked() {
        return startRequestPending || StarHuntService.isHangupPending()
                || StarHuntService.isActive() || StarHuntService.isStopReadPending()
                || StarHuntService.hasRetryableStop(getFilesDir());
    }

    private StarHuntSelectionStore.Settings selectionSettings() {
        final SharedPreferences preferences = getSharedPreferences("settings", MODE_PRIVATE);
        return new StarHuntSelectionStore.Settings() {
            @Override public boolean contains(String key) { return preferences.contains(key); }
            @Override public String getString(String key, String fallback) {
                return preferences.getString(key, fallback);
            }
            @Override public int getInt(String key, int fallback) {
                return preferences.getInt(key, fallback);
            }
            @Override public void putString(String key, String value) {
                preferences.edit().putString(key, value).apply();
            }
        };
    }

    private long countForSelectedLevels() {
        long total = 0L;
        for (int level : selectedLevels.asList()) total += status.countForLevel(level);
        return total;
    }

    private String selectionSummary() {
        if (selectedLevels.isEmpty()) return "已选星级";
        if (selectedLevels.size() > 3) return selectedLevels.size() + " 个星级";
        StringBuilder summary = new StringBuilder();
        for (int level : selectedLevels.asList()) {
            if (summary.length() > 0) summary.append("、");
            summary.append(level);
        }
        return summary.append("星").toString();
    }

    private String displayState(boolean pending, boolean active, boolean stopping, boolean retryable) {
        if (stopping) return "正在停止";
        if (retryable) return "重试停止：清理未确认";
        if (pending || startRequestPending) return "正在读取地图";
        if (!active) return status.confirmsCleanup() ? "未开始" : "未开始";
        if (status.updatedAt <= 0L || !status.isFresh(System.currentTimeMillis() / 1000L, 10L)) {
            return "正在读取地图";
        }
        switch (status.state) {
            case "waiting_role": return "等待角色登录";
            case "waiting_map": return "正在读取地图";
            case "reading": return "正在读取地图";
            case "waiting_targets":
                if ("round_cooldown".equals(status.reason)) return "等待下一轮刷新";
                if (status.reason.contains("观战") || status.reason.contains("spectator")) {
                    return "观战状态，暂不寻找";
                }
                return "等待" + selectionSummary() + "刷新";
            case "teleport_wait": return "正在寻找" + selectionSummary();
            case "challenge_wait": return "正在挑战";
            case "battle": return "战斗中";
            case "cooldown": return "等待下一轮刷新";
            case "paused": return "已暂停";
            case "fatal": return "运行异常";
            case "stopping": return "正在停止";
            case "stopped": return "未开始";
            default:
                return status.reason.isEmpty() ? "状态未确认" : "状态未确认：" + status.reason;
        }
    }

    private void toggleHangup() {
        if (StarHuntService.isStopReadPending()) return;
        boolean running = StarHuntService.isHangupPending() || StarHuntService.isActive()
                || StarHuntService.hasRetryableStop(getFilesDir());
        if (running) {
            startRequestPending = false;
            handler.removeCallbacks(requestTimeout);
            dispatch(StarHuntService.ACTION_STOP_READ, 0);
            return;
        }
        if (selectedLevels.isEmpty()) {
            Toast.makeText(this, "请至少选择一个星级", Toast.LENGTH_SHORT).show();
            return;
        }
        if (!LicenseManager.isActive()) {
            Toast.makeText(this, "请先完成在线授权", Toast.LENGTH_SHORT).show();
            startActivity(new Intent(this, com.xnas.wpe.suite.ActivationActivity.class));
            return;
        }
        startRequestPending = true;
        try {
            dispatch(StarHuntService.ACTION_START_HANGUP,
                    StarHuntSelection.parseCsv(selectedLevels.toCsv()));
        } catch (RuntimeException error) {
            startRequestPending = false;
            Toast.makeText(this, "挂机请求未能发送，请重试", Toast.LENGTH_SHORT).show();
            refreshPage();
            return;
        }
        handler.removeCallbacks(requestTimeout);
        handler.postDelayed(requestTimeout, 1000L);
        returnToGame();
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

    private void dispatch(String action, int level) {
        Intent intent = new Intent(this, StarHuntService.class).setAction(action);
        if (level > 0) intent.putExtra(StarHuntService.EXTRA_LEVEL, level);
        dispatch(intent, action);
    }

    private void dispatch(String action, StarHuntSelection levels) {
        Intent intent = new Intent(this, StarHuntService.class).setAction(action);
        intent.putExtra(StarHuntService.EXTRA_LEVELS, levels.toCsv());
        dispatch(intent, action);
    }

    private void dispatch(Intent intent, String action) {
        if (Build.VERSION.SDK_INT >= 26
                && !StarHuntService.ACTION_STOP_READ.equals(action)
                && !StarHuntService.ACTION_STOP_RUN.equals(action)) {
            startForegroundService(intent);
        } else {
            startService(intent);
        }
        refreshPage();
    }

    @Override public ModuleStatus getModuleStatus() {
        String target = TargetGame.DHXY_PACKAGE;
        String selected = TargetGame.getSelectedPackage(this);
        if (!TargetGame.supportsModule(TargetGame.STARHUNT, selected)) {
            return new ModuleStatus(ModuleStatus.State.STOPPED, "不支持", target,
                    "此功能仅支持 DHXY。", "切换到 DHXY 后再使用自动找星。");
        }
        UiStatusFiles.Snapshot statusSnapshot = readStatusSnapshot(selected);
        if (!statusSnapshot.known || !statusSnapshot.targetKey.equals(selected)) return ModuleStatus.unknown(target,
                "本地找星状态读取中或暂不可用；运行状态未确认。");
        StarHuntStatus latest = parseStatus(statusSnapshot);
        if (StarHuntService.isStopReadPending()) {
            return new ModuleStatus(ModuleStatus.State.STOPPING, "正在停止", target,
                    StarHuntService.getServiceMessage(), "等待 worker 确认清理。");
        }
        if (StarHuntService.hasRetryableStop(getFilesDir())) {
            return new ModuleStatus(ModuleStatus.State.UNKNOWN, "重试停止", target,
                    StarHuntService.getServiceMessage(), "清理未确认，可重试停止。");
        }
        if (StarHuntService.isHangupPending() || StarHuntService.isActive()) {
            String label = displayState(StarHuntService.isHangupPending(), true, false, false);
            if ("paused".equals(latest.state)) {
                return new ModuleStatus(ModuleStatus.State.STOPPED, label, target,
                        "挂机已暂停。" + latest.reason, "恢复前请检查游戏状态。");
            }
            if ("fatal".equals(latest.state)) {
                return new ModuleStatus(ModuleStatus.State.ERROR, label, target,
                        latest.reason, "查看异常原因后重试。");
            }
            return new ModuleStatus(ModuleStatus.State.RUNNING, label, target,
                    latest.reason.isEmpty() ? "本地挂机状态，服务端结果未确认。"
                            : "本地状态：" + latest.reason,
                    "点击停止挂机结束 worker 并清理。");
        }
        if (latest.confirmsCleanup() || !statusSnapshot.exists) {
            return new ModuleStatus(ModuleStatus.State.STOPPED, "未开始", target,
                    "挂机当前未运行。", "选择星级后开始；打开页面不会自动启动。");
        }
        return new ModuleStatus(ModuleStatus.State.STOPPED, "未开始", target,
                "最近状态文件未证明 worker 仍在运行。", "选择星级后显式开始挂机。");
    }

    private UiStatusFiles.Snapshot readStatusSnapshot() {
        return readStatusSnapshot(TargetGame.getSelectedPackage(this));
    }

    private UiStatusFiles.Snapshot readStatusSnapshot(String targetKey) {
        File file = new File(getFilesDir(), "status");
        return UiStatusFiles.readForDisplay("starhunt", file, targetKey);
    }

    private static StarHuntStatus parseStatus(UiStatusFiles.Snapshot snapshot) {
        return StarHuntStatus.parse(snapshot.known && snapshot.exists ? snapshot.text : "");
    }

    private TextView textView(int size, int color, boolean bold) {
        TextView view = new TextView(this);
        view.setTextColor(color);
        view.setTextSize(size);
        if (bold) view.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        return view;
    }

    private LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
