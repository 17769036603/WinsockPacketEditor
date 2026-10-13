package com.xnas.wpe.battlepanel;

import android.content.ComponentName;
import android.content.Intent;
import android.content.ServiceConnection;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;
import com.xnas.wpe.suite.TargetGame;

/** In-app view for the background battle reader. */
public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private static final int REQUEST_NOTIFICATIONS = 7402;
    private static final long SERVICE_STATE_REFRESH_MS = 500L;
    private static final String STATE_WAITING_FOR_NOTIFICATION_PERMISSION =
            "waiting_for_notification_permission";
    private static final String[] RESISTANCE_NAMES = {
            "抗混乱", "抗毒", "抗昏睡", "抗封印", "抗风", "抗雷", "抗水",
            "抗火", "抗震慑", "抗三尸", "抗鬼火", "抗遗忘", "物理吸收"};

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable refreshTask = new Runnable() {
        @Override public void run() {
            refreshState();
            if (resumed) handler.postDelayed(this, SERVICE_STATE_REFRESH_MS);
        }
    };
    private final ServiceConnection serviceConnection = new ServiceConnection() {
        @Override public void onServiceConnected(ComponentName name, IBinder binder) {
            if (binder instanceof BattleOverlayService.LocalBinder) {
                service = ((BattleOverlayService.LocalBinder) binder).getService();
            }
            if (showingResistance && service != null) service.requestResistanceIfNeeded();
            refreshState();
        }

        @Override public void onServiceDisconnected(ComponentName name) {
            service = null;
            refreshState();
        }
    };

    private boolean waitingForNotificationPermission;
    private boolean bound;
    private boolean resumed;
    private boolean showingResistance;
    private int selectedResistance;
    private BattleOverlayService service;
    private TextView statusView;
    private TextView battleView;
    private TextView resistanceDetail;
    private Button startButton;
    private Button stopButton;
    private Button attributesTab;
    private Button resistanceTab;
    private Spinner resistanceSelector;
    private View attributesBody;
    private View resistanceBody;
    private final TextView[] slotNames = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] slotHp = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] slotMp = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] slotSp = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] resistancePositions = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] resistanceValues = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] resistanceHeaders = new TextView[4];

    @Override protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (savedInstanceState != null) {
            waitingForNotificationPermission = savedInstanceState.getBoolean(
                    STATE_WAITING_FOR_NOTIFICATION_PERMISSION);
        }
        getWindow().setStatusBarColor(Color.rgb(13, 20, 29));
        getWindow().setNavigationBarColor(Color.rgb(13, 20, 29));
        setContentView(buildContent());
        refreshState();
    }

    @Override protected void onStart() {
        super.onStart();
        bindToStartedService();
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

    @Override protected void onSaveInstanceState(Bundle outState) {
        outState.putBoolean(STATE_WAITING_FOR_NOTIFICATION_PERMISSION,
                waitingForNotificationPermission);
        super.onSaveInstanceState(outState);
    }

    @Override public void onRequestPermissionsResult(int requestCode, String[] permissions,
                                                      int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQUEST_NOTIFICATIONS && waitingForNotificationPermission) {
            waitingForNotificationPermission = false;
            if (grantResults.length == 0
                    || grantResults[0] != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                setStatus("通知权限未授予；仍会启动后台读取，系统任务管理中可查看前台服务。", false);
            }
            startReaderService();
        }
    }

    private View buildContent() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(12), dp(10), dp(12), dp(11));
        root.setBackground(roundBackground(Color.argb(215, 11, 20, 31),
                Color.argb(245, 151, 204, 220), dp(18), dp(1)));
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            view.setPadding(dp(12), Math.max(dp(10), insets.getSystemWindowInsetTop() + dp(8)),
                    dp(12), Math.max(dp(11), insets.getSystemWindowInsetBottom() + dp(8)));
            return insets;
        });

        statusView = text("后台读取服务未启动", 12.7f, Color.rgb(211, 226, 236));
        statusView.setGravity(Gravity.CENTER_VERTICAL);
        statusView.setPadding(dp(10), 0, dp(10), 0);
        statusView.setBackground(roundBackground(Color.argb(228, 27, 45, 63),
                Color.argb(160, 156, 198, 213), dp(9), dp(1)));
        root.addView(statusView, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                dp(34), 0, 0, 0, 6));

        LinearLayout controls = new LinearLayout(this);
        controls.setOrientation(LinearLayout.HORIZONTAL);
        startButton = button("启动读取", Color.argb(248, 27, 102, 126));
        startButton.setContentDescription("启动后台读取服务");
        startButton.setOnClickListener(view -> requestNotificationsThenStart());
        stopButton = button("停止读取", Color.argb(224, 89, 53, 53));
        stopButton.setContentDescription("停止后台读取服务");
        stopButton.setOnClickListener(view -> stopReaderService());
        LinearLayout.LayoutParams controlParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(38));
        controlParams.setMargins(0, 0, dp(5), 0);
        controls.addView(startButton, controlParams);
        LinearLayout.LayoutParams stopParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(38));
        stopParams.setMargins(dp(5), 0, 0, 0);
        controls.addView(stopButton, stopParams);
        root.addView(controls, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                dp(38), 0, 0, 0, 7));

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(false);
        LinearLayout pages = new LinearLayout(this);
        pages.setOrientation(LinearLayout.VERTICAL);
        attributesBody = buildAttributesBody();
        resistanceBody = buildResistanceBody();
        pages.addView(attributesBody);
        pages.addView(resistanceBody);
        scroll.addView(pages, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        root.addView(scroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        LinearLayout tabs = new LinearLayout(this);
        tabs.setOrientation(LinearLayout.HORIZONTAL);
        attributesTab = button("属性", Color.rgb(37, 71, 82));
        attributesTab.setOnClickListener(view -> showAttributes());
        resistanceTab = button("抗性", Color.rgb(37, 71, 82));
        resistanceTab.setOnClickListener(view -> showResistance());
        LinearLayout.LayoutParams tabParams = new LinearLayout.LayoutParams(0, dp(44), 1f);
        tabParams.setMargins(0, dp(8), dp(4), 0);
        tabs.addView(attributesTab, tabParams);
        LinearLayout.LayoutParams resistanceParams = new LinearLayout.LayoutParams(0, dp(44), 1f);
        resistanceParams.setMargins(dp(4), dp(8), 0, 0);
        tabs.addView(resistanceTab, resistanceParams);
        root.addView(tabs);

        setPageVisibility();
        return root;
    }

    private View buildAttributesBody() {
        LinearLayout body = new LinearLayout(this);
        body.setOrientation(LinearLayout.VERTICAL);
        battleView = text("等待有效战斗帧", 12f, Color.rgb(199, 219, 224));
        battleView.setPadding(dp(3), dp(6), dp(3), dp(8));
        body.addView(battleView);
        addSideSections(body, false);
        return body;
    }

    private View buildResistanceBody() {
        LinearLayout body = new LinearLayout(this);
        body.setOrientation(LinearLayout.VERTICAL);
        resistanceDetail = text("进入战斗后打开抗性页查询", 12f, Color.rgb(199, 219, 224));
        resistanceDetail.setPadding(dp(3), dp(6), dp(3), dp(7));
        body.addView(resistanceDetail);

        LinearLayout selectorRow = new LinearLayout(this);
        selectorRow.setGravity(Gravity.CENTER_VERTICAL);
        TextView selectorLabel = text("抗性项目", 13f, Color.rgb(197, 219, 232));
        selectorRow.addView(selectorLabel, marginParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(34), 0, 0, 8, 0));
        resistanceSelector = new Spinner(this);
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this,
                android.R.layout.simple_spinner_dropdown_item, RESISTANCE_NAMES);
        resistanceSelector.setAdapter(adapter);
        resistanceSelector.setSelection(selectedResistance);
        resistanceSelector.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(android.widget.AdapterView<?> parent,
                                                View view, int position, long id) {
                selectedResistance = position;
                for (TextView header : resistanceHeaders) {
                    if (header != null) header.setText(RESISTANCE_NAMES[position]);
                }
                renderResistance(service == null ? null : service.getDisplayState());
            }

            @Override public void onNothingSelected(android.widget.AdapterView<?> parent) { }
        });
        selectorRow.addView(resistanceSelector, new LinearLayout.LayoutParams(0, dp(36), 1f));
        body.addView(selectorRow, marginParams(ViewGroup.LayoutParams.MATCH_PARENT,
                dp(36), 0, 0, 0, 5));
        addSideSections(body, true);
        return body;
    }

    private void addSideSections(LinearLayout body, boolean resistance) {
        LinearLayout sides = new LinearLayout(this);
        sides.setOrientation(isLandscape() ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        LinearLayout.LayoutParams opponentParams = isLandscape()
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT);
        if (isLandscape()) opponentParams.setMargins(0, 0, dp(5), 0);
        else opponentParams.setMargins(0, 0, 0, dp(5));
        sides.addView(buildSideSection("对方", 10, resistance), opponentParams);

        LinearLayout.LayoutParams ownParams = isLandscape()
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT);
        if (isLandscape()) ownParams.setMargins(dp(5), 0, 0, 0);
        sides.addView(buildSideSection("本方", 0, resistance), ownParams);
        body.addView(sides, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
    }

    private View buildSideSection(String name, int firstIndex, boolean resistance) {
        LinearLayout section = new LinearLayout(this);
        section.setOrientation(LinearLayout.VERTICAL);
        section.setPadding(dp(6), dp(5), dp(6), dp(6));
        section.setBackground(roundBackground(Color.argb(154, 17, 31, 47),
                Color.argb(150, 111, 169, 189), dp(13), dp(1)));

        TextView heading = text(name, 14f, Color.WHITE);
        heading.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        heading.setGravity(Gravity.CENTER_VERTICAL);
        section.addView(heading, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(24)));

        LinearLayout formations = new LinearLayout(this);
        formations.setOrientation(LinearLayout.HORIZONTAL);
        boolean ownSide = firstIndex == 0;
        int firstFormationIndex = ownSide ? firstIndex + 5 : firstIndex;
        int secondFormationIndex = ownSide ? firstIndex : firstIndex + 5;
        String firstFormationName = ownSide ? "宠物" : "人物";
        String secondFormationName = ownSide ? "人物" : "宠物";
        LinearLayout.LayoutParams firstFormationParams = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        firstFormationParams.setMargins(0, 0, dp(4), 0);
        formations.addView(buildFormationSection(firstFormationName, firstFormationIndex,
                resistance), firstFormationParams);
        formations.addView(buildFormationSection(secondFormationName, secondFormationIndex,
                resistance), new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        section.addView(formations, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return section;
    }

    private View buildFormationSection(String name, int firstIndex, boolean resistance) {
        LinearLayout section = new LinearLayout(this);
        section.setOrientation(LinearLayout.VERTICAL);
        section.setPadding(dp(7), dp(6), dp(7), dp(7));
        section.setBackground(roundBackground(Color.argb(170, 21, 38, 55),
                Color.argb(132, 130, 180, 198), dp(12), dp(1)));

        TextView heading = text(name, 13f, Color.rgb(226, 240, 247));
        heading.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        heading.setPadding(dp(3), 0, 0, 0);
        section.addView(heading, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(resistance ? 20 : 22)));

        LinearLayout tableHeader = new LinearLayout(this);
        tableHeader.setGravity(Gravity.CENTER_VERTICAL);
        tableHeader.setPadding(dp(2), 0, dp(2), 0);
        float positionWeight = resistance ? 1f : 0.9f;
        tableHeader.addView(cell("槽位", positionWeight, true),
                weightedCellParams(positionWeight));
        if (resistance) {
            TextView metric = cell(RESISTANCE_NAMES[selectedResistance], 1f, true);
            resistanceHeaders[resistanceHeaderIndex(firstIndex)] = metric;
            tableHeader.addView(metric, weightedCellParams(1f));
        } else {
            tableHeader.addView(cell("HP", 1f, true), weightedCellParams(1f));
            tableHeader.addView(cell("MP", 1f, true), weightedCellParams(1f));
            tableHeader.addView(cell("SP", 1f, true), weightedCellParams(1f));
        }
        section.addView(tableHeader, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(resistance ? 20 : 23)));

        for (int rowNumber = 0; rowNumber < 5; rowNumber++) {
            int index = firstIndex + 4 - rowNumber;
            LinearLayout row = new LinearLayout(this);
            row.setGravity(Gravity.CENTER_VERTICAL);
            row.setPadding(dp(2), 0, dp(2), 0);
            row.setBackground(roundBackground(Color.argb(155, 31, 48, 64),
                    Color.argb(82, 185, 215, 228), dp(8), dp(1)));
            LinearLayout.LayoutParams rowParams = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(resistance ? 24 : 32));
            rowParams.setMargins(0, dp(2), 0, 0);

            if (resistance) {
                TextView position = cell(slotLabel(index % BattleFrameProtocol.SIDE_SLOT_COUNT),
                        1f, false);
                TextView value = cell("未提供", 1f, false);
                value.setTextColor(Color.rgb(224, 237, 173));
                value.setSingleLine(true);
                resistancePositions[index] = position;
                resistanceValues[index] = value;
                row.addView(position, weightedCellParams(1f));
                row.addView(value, weightedCellParams(1f));
            } else {
                TextView position = cell(slotLabel(index % BattleFrameProtocol.SIDE_SLOT_COUNT),
                        0.9f, false);
                TextView hp = cell("—", 1f, false);
                TextView mp = cell("—", 1f, false);
                TextView sp = cell("—", 1f, false);
                slotNames[index] = position;
                slotHp[index] = hp;
                slotMp[index] = mp;
                slotSp[index] = sp;
                row.addView(position, weightedCellParams(0.9f));
                row.addView(hp, weightedCellParams(1f));
                row.addView(mp, weightedCellParams(1f));
                row.addView(sp, weightedCellParams(1f));
            }
            section.addView(row, rowParams);
        }
        return section;
    }

    private int resistanceHeaderIndex(int firstIndex) {
        if (firstIndex == 10) return 0;
        if (firstIndex == 15) return 1;
        if (firstIndex == 5) return 2;
        return 3;
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
        startReaderService();
    }

    private void startReaderService() {
        Intent intent = new Intent(this, BattleOverlayService.class)
                .setAction(BattleOverlayService.ACTION_START)
                .putExtra(BattleOverlayService.EXTRA_PACKAGE, TargetGame.getSelectedPackage(this));
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) startForegroundService(intent);
            else startService(intent);
            bindToStartedService();
            setStatus("正在启动前台读取服务…", true);
            handler.postDelayed(this::refreshState, 500L);
        } catch (RuntimeException error) {
            setStatus("后台读取服务启动失败，请检查系统前台服务设置。", false);
        }
    }

    private void bindToStartedService() {
        if (bound) return;
        Intent intent = new Intent(this, BattleOverlayService.class);
        bound = bindService(intent, serviceConnection, 0);
    }

    private void stopReaderService() {
        Intent intent = new Intent(this, BattleOverlayService.class)
                .setAction(BattleOverlayService.ACTION_STOP);
        try {
            startService(intent);
            setStatus("已请求停止后台读取。", false);
            handler.postDelayed(this::refreshState, SERVICE_STATE_REFRESH_MS);
        } catch (RuntimeException error) {
            if (service != null) service.requestStop();
            else stopService(new Intent(this, BattleOverlayService.class));
            setStatus("已请求停止后台读取。", false);
        }
    }

    private void showAttributes() {
        showingResistance = false;
        setPageVisibility();
    }

    private void showResistance() {
        showingResistance = true;
        setPageVisibility();
        if (service != null) service.requestResistanceIfNeeded();
        refreshState();
    }

    private void setPageVisibility() {
        if (attributesBody != null) attributesBody.setVisibility(
                showingResistance ? View.GONE : View.VISIBLE);
        if (resistanceBody != null) resistanceBody.setVisibility(
                showingResistance ? View.VISIBLE : View.GONE);
        if (attributesTab != null) attributesTab.setSelected(!showingResistance);
        if (resistanceTab != null) resistanceTab.setSelected(showingResistance);
    }

    private void refreshState() {
        if (service == null && BattleOverlayService.isActive()) bindToStartedService();
        BattleOverlayService.DisplayState state = service == null ? null : service.getDisplayState();
        if (state == null) {
            boolean running = BattleOverlayService.isActive();
            setStatus(running ? "前台读取服务运行中 · 正在连接…" : "后台读取服务未启动", running);
            if (startButton != null) startButton.setEnabled(!running);
            if (stopButton != null) stopButton.setEnabled(running);
            renderBattle(null);
            renderResistance(null);
            return;
        }
        if (showingResistance && state.active
                && state.status == BattleFrameProtocol.STATUS_LIVE) {
            service.requestResistanceIfNeeded();
            state = service.getDisplayState();
        }
        String status = state.label;
        if (state.detail != null && !state.detail.isEmpty()) status += " · " + state.detail;
        setStatus(status, state.status);
        if (startButton != null) {
            startButton.setEnabled(!state.active);
            startButton.setText(state.active ? "正在读取" : "启动读取");
        }
        if (stopButton != null) stopButton.setEnabled(state.active);
        renderBattle(state.frame);
        renderResistance(state);
    }

    private void renderBattle(BattleFrameProtocol.Frame frame) {
        if (battleView != null) battleView.setText(frame == null
                ? "等待有效战斗帧"
                : "实时战斗 · 第 " + frame.round + " 回合 · BattleId " + frame.battleId);
        for (int i = 0; i < BattleFrameProtocol.SLOT_COUNT; i++) {
            if (slotNames[i] == null) continue;
            int state = frame == null ? BattleFrameProtocol.SLOT_UNKNOWN : frame.slots[i].state;
            String name = slotLabel(i % BattleFrameProtocol.SIDE_SLOT_COUNT);
            String hp = "—";
            String mp = "—";
            String sp = "—";
            if (frame != null) {
                BattleFrameProtocol.Slot slot = frame.slots[i];
                if (slot.state == BattleFrameProtocol.SLOT_ALIVE
                        || slot.state == BattleFrameProtocol.SLOT_ALIVE_SP_UNKNOWN) {
                    hp = Long.toString(slot.hp);
                    mp = Long.toString(slot.mp);
                    sp = slot.state == BattleFrameProtocol.SLOT_ALIVE
                            ? Long.toString(slot.sp) : "—";
                } else if (slot.state != BattleFrameProtocol.SLOT_EMPTY
                        && slot.state != BattleFrameProtocol.SLOT_DEAD) {
                    name += "\n未确认";
                }
            }
            slotNames[i].setText(name);
            slotHp[i].setText(hp);
            slotMp[i].setText(mp);
            slotSp[i].setText(sp);
            boolean alive = state == BattleFrameProtocol.SLOT_ALIVE
                    || state == BattleFrameProtocol.SLOT_ALIVE_SP_UNKNOWN;
            int fill = alive ? Color.argb(166, 31, 92, 75)
                    : state == BattleFrameProtocol.SLOT_EMPTY ? Color.argb(112, 35, 50, 65)
                    : state == BattleFrameProtocol.SLOT_DEAD ? Color.argb(174, 67, 49, 57)
                    : Color.argb(145, 35, 50, 65);
            View row = slotNames[i].getParent() instanceof View
                    ? (View) slotNames[i].getParent() : null;
            if (row != null) row.setBackground(roundBackground(fill,
                    Color.argb(82, 190, 220, 232), dp(8), dp(1)));
            int inactiveColor = state == BattleFrameProtocol.SLOT_EMPTY
                    ? Color.rgb(161, 179, 190)
                    : state == BattleFrameProtocol.SLOT_DEAD
                    ? Color.rgb(225, 188, 186) : Color.rgb(221, 231, 238);
            slotNames[i].setTextColor(alive ? Color.rgb(245, 250, 249) : inactiveColor);
            slotHp[i].setTextColor(alive ? Color.rgb(255, 217, 205) : inactiveColor);
            slotMp[i].setTextColor(alive ? Color.rgb(205, 226, 255) : inactiveColor);
            slotSp[i].setTextColor(alive ? Color.rgb(224, 237, 173) : inactiveColor);
        }
    }

    private void renderResistance(BattleOverlayService.DisplayState state) {
        BattleFrameProtocol.Frame frame = state == null ? null : state.frame;
        ResistanceSnapshot snapshot = state == null ? null : state.resistance;
        boolean live = frame != null && state.active
                && state.status == BattleFrameProtocol.STATUS_LIVE;
        boolean matches = live && snapshot != null && snapshot.matches(frame);
        if (resistanceDetail != null) {
            String detail = !live ? "进入战斗后打开抗性页查询"
                    : state.resistanceMessage == null ? "正在等待抗性查询" : state.resistanceMessage;
            resistanceDetail.setText(detail);
        }
        for (int i = 0; i < BattleFrameProtocol.SLOT_COUNT; i++) {
            if (resistancePositions[i] == null) continue;
            BattleFrameProtocol.Slot slot = live ? frame.slots[i] : null;
            resistancePositions[i].setText(slotLabel(i % BattleFrameProtocol.SIDE_SLOT_COUNT));
            resistanceValues[i].setText(matches && slot != null
                    ? snapshot.value(slot.position, ResistanceSnapshot.FIRST_CODE + selectedResistance)
                    : "未提供");
        }
    }

    private String slotLabel(int sideIndex) {
        return (sideIndex < 5 ? "人物" : "宠") + (sideIndex < 5 ? sideIndex + 1 : sideIndex - 4);
    }

    private void setStatus(String value, boolean running) {
        setStatus(value, running ? BattleFrameProtocol.STATUS_LIVE
                : BattleFrameProtocol.STATUS_LOCATING);
    }

    private void setStatus(String value, int status) {
        if (statusView != null) {
            statusView.setText(value);
            statusView.setTextColor(statusColor(status));
        }
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
                ? "前台读取服务运行中；页面状态尚未连接确认。" : "前台读取服务未运行。";
        return new ModuleStatus(active ? ModuleStatus.State.RUNNING : ModuleStatus.State.STOPPED,
                active ? "后台读取中" : "未启动", target, detail,
                active ? "读取服务活动不等于已绑定目标游戏或存在有效战斗帧。"
                        : "在功能页手动启动后台读取。 ");
    }

    private TextView cell(String value, float sizeWeight, boolean header) {
        TextView view = text(value, header ? 11.5f : 11.2f,
                header ? Color.rgb(197, 219, 232) : Color.WHITE);
        view.setGravity(Gravity.CENTER);
        if (!header) view.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        return view;
    }

    private LinearLayout.LayoutParams weightedCellParams(float weight) {
        return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, weight);
    }

    private Button button(String label, int fill) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        button.setTextSize(13f);
        button.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        button.setTextColor(Color.WHITE);
        button.setMinHeight(0);
        button.setMinimumHeight(0);
        button.setMinWidth(0);
        button.setPadding(dp(8), 0, dp(8), 0);
        button.setBackground(roundBackground(fill, Color.rgb(173, 239, 247), dp(11), dp(1)));
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

    private LinearLayout.LayoutParams marginParams(int width, int height,
                                                   int left, int top, int right, int bottom) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(width, height);
        params.setMargins(dp(left), dp(top), dp(right), dp(bottom));
        return params;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private boolean isLandscape() {
        return getResources().getConfiguration().orientation == Configuration.ORIENTATION_LANDSCAPE;
    }
}
