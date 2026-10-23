package com.xnas.wpe.battlepanel;

import android.content.Context;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.xnas.wpe.suite.CompactUi;

/** Builds and renders the battle overlay using only a service-derived context. */
final class BattleOverlayRenderer {
    interface Listener {
        void onCollapseRequested();
        void onResistancePageSelected();
    }

    private static final String[] RESISTANCE_NAMES = {
            "抗混", "抗毒", "抗风", "抗雷", "抗水", "抗火", "抗物理", "抗遗忘"};
    private static final int[] RESISTANCE_CODES = {
            ResistanceSnapshot.FIRST_CODE,
            ResistanceSnapshot.FIRST_CODE + 1,
            ResistanceSnapshot.FIRST_CODE + 4,
            ResistanceSnapshot.FIRST_CODE + 5,
            ResistanceSnapshot.FIRST_CODE + 6,
            ResistanceSnapshot.FIRST_CODE + 7,
            ResistanceSnapshot.FIRST_CODE + 12,
            ResistanceSnapshot.FIRST_CODE + 11};

    private final Context context;
    private final Listener listener;
    private final boolean landscape;
    private final View root;
    private final TextView[] slotNames = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] slotHp = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] slotMp = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] slotSp = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] resistancePositions = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] resistanceValues = new TextView[BattleFrameProtocol.SLOT_COUNT];
    private final TextView[] resistanceHeaders = new TextView[4];
    private final Button[] resistanceButtons = new Button[RESISTANCE_NAMES.length];

    private TextView statusView;
    private TextView battleView;
    private TextView resistanceDetail;
    private Button attributesTab;
    private Button resistanceTab;
    private View attributesBody;
    private View resistanceBody;
    private boolean showingResistance;
    private int selectedResistance;
    private BattleOverlayService.DisplayState latestState;

    BattleOverlayRenderer(Context serviceContext, Listener listener) {
        this.context = CompactUi.wrap(serviceContext);
        this.listener = listener;
        this.landscape = context.getResources().getConfiguration().orientation
                == Configuration.ORIENTATION_LANDSCAPE;
        this.root = buildPanel();
    }

    View getView() {
        return root;
    }

    boolean isShowingResistance() {
        return showingResistance;
    }

    void render(BattleOverlayService.DisplayState state) {
        latestState = state;
        if (state == null) {
            setStatus("读取未启动", BattleFrameProtocol.STATUS_LOCATING);
            renderBattle(null);
            renderResistance(null);
            return;
        }
        String summary = state.label == null ? "未知状态" : state.label;
        if (state.detail != null && !state.detail.isEmpty()) {
            summary += " · " + state.detail;
        }
        setStatus(compactMessage(summary, 100), state.status);
        renderBattle(state.frame);
        renderResistance(state);
    }

    private View buildPanel() {
        LinearLayout panel = new LinearLayout(context);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(dp(12), dp(10), dp(12), dp(10));
        panel.setBackground(roundBackground(Color.argb(215, 11, 20, 31),
                Color.argb(245, 151, 204, 220), dp(18), dp(1)));
        panel.setElevation(dp(10));

        LinearLayout header = new LinearLayout(context);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setOrientation(LinearLayout.HORIZONTAL);
        TextView title = text("战斗状态", 17f, Color.WHITE);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        title.setGravity(Gravity.CENTER_VERTICAL);
        header.addView(title, new LinearLayout.LayoutParams(0, dp(34), 1f));
        Button collapse = smallButton("收起");
        collapse.setContentDescription("收起到悬浮球");
        collapse.setOnClickListener(view -> listener.onCollapseRequested());
        header.addView(collapse, new LinearLayout.LayoutParams(dp(60), dp(30)));
        panel.addView(header, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(34)));

        statusView = text("正在定位", 11.5f, Color.rgb(211, 226, 236));
        statusView.setGravity(Gravity.CENTER_VERTICAL);
        statusView.setPadding(dp(9), dp(3), dp(9), dp(3));
        statusView.setBackground(roundBackground(Color.argb(228, 27, 45, 63),
                Color.argb(160, 156, 198, 213), dp(9), dp(1)));
        panel.addView(statusView, marginParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(29), 0, 4, 0, 5));

        ScrollView scroll = new ScrollView(context);
        scroll.setFillViewport(false);
        LinearLayout pages = new LinearLayout(context);
        pages.setOrientation(LinearLayout.VERTICAL);
        attributesBody = buildAttributesBody();
        resistanceBody = buildResistanceBody();
        pages.addView(attributesBody);
        pages.addView(resistanceBody);
        scroll.addView(pages, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        panel.addView(scroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        LinearLayout tabs = new LinearLayout(context);
        tabs.setOrientation(LinearLayout.HORIZONTAL);
        attributesTab = smallButton("属性");
        attributesTab.setContentDescription("显示 HP、MP、SP 属性");
        attributesTab.setOnClickListener(view -> showAttributes());
        resistanceTab = smallButton("抗性");
        resistanceTab.setContentDescription("查看抗性快照");
        resistanceTab.setOnClickListener(view -> showResistance());
        LinearLayout.LayoutParams tabParams = new LinearLayout.LayoutParams(0, dp(34), 1f);
        tabParams.setMargins(0, dp(5), dp(3), 0);
        tabs.addView(attributesTab, tabParams);
        LinearLayout.LayoutParams resistanceParams = new LinearLayout.LayoutParams(0, dp(34), 1f);
        resistanceParams.setMargins(dp(3), dp(5), 0, 0);
        tabs.addView(resistanceTab, resistanceParams);
        panel.addView(tabs);

        setPageVisibility();
        return panel;
    }

    private View buildAttributesBody() {
        LinearLayout body = new LinearLayout(context);
        body.setOrientation(LinearLayout.VERTICAL);
        battleView = text("等待有效战斗帧", 11f, Color.rgb(199, 219, 224));
        battleView.setPadding(dp(3), dp(4), dp(3), dp(5));
        body.addView(battleView);
        addSideSections(body, false);
        return body;
    }

    private View buildResistanceBody() {
        LinearLayout body = new LinearLayout(context);
        body.setOrientation(LinearLayout.VERTICAL);
        resistanceDetail = text("", 10.5f, Color.rgb(199, 219, 224));
        resistanceDetail.setVisibility(View.GONE);
        resistanceDetail.setPadding(dp(3), dp(4), dp(3), dp(5));
        body.addView(resistanceDetail);

        LinearLayout options = new LinearLayout(context);
        options.setOrientation(LinearLayout.HORIZONTAL);
        options.setGravity(Gravity.CENTER_VERTICAL);
        for (int index = 0; index < RESISTANCE_NAMES.length; index++) {
            final int selectedIndex = index;
            Button option = smallButton(RESISTANCE_NAMES[index]);
            option.setTextSize(9f);
            option.setSingleLine(true);
            option.setPadding(dp(1), 0, dp(1), 0);
            option.setMinWidth(0);
            option.setMinimumWidth(0);
            option.setContentDescription(RESISTANCE_NAMES[index]);
            option.setOnClickListener(view -> selectResistance(selectedIndex));
            resistanceButtons[index] = option;
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, dp(30), 1f);
            params.setMargins(dp(1), dp(1), dp(1), dp(1));
            options.addView(option, params);
        }
        body.addView(options, marginParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(32), 0, 0, 0, 4));
        addSideSections(body, true);
        updateResistanceButtonStyles();
        return body;
    }

    private void addSideSections(LinearLayout body, boolean resistance) {
        LinearLayout sides = new LinearLayout(context);
        sides.setOrientation(landscape ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        LinearLayout.LayoutParams opponentParams = landscape
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT);
        if (landscape) opponentParams.setMargins(0, 0, dp(5), 0);
        else opponentParams.setMargins(0, 0, 0, dp(5));
        sides.addView(buildSideSection("对方", 10, resistance), opponentParams);

        LinearLayout.LayoutParams ownParams = landscape
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT);
        if (landscape) ownParams.setMargins(dp(5), 0, 0, 0);
        sides.addView(buildSideSection("本方", 0, resistance), ownParams);
        body.addView(sides, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
    }

    private View buildSideSection(String name, int firstIndex, boolean resistance) {
        LinearLayout section = new LinearLayout(context);
        section.setOrientation(LinearLayout.VERTICAL);
        section.setPadding(dp(6), dp(5), dp(6), dp(6));
        section.setBackground(roundBackground(Color.argb(154, 17, 31, 47),
                Color.argb(150, 111, 169, 189), dp(13), dp(1)));

        TextView heading = text(name, 12.5f, Color.WHITE);
        heading.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        heading.setGravity(Gravity.CENTER_VERTICAL);
        section.addView(heading, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(21)));

        LinearLayout character = new LinearLayout(context);
        character.setOrientation(LinearLayout.VERTICAL);
        character.addView(buildFormationSection("人物", firstIndex, resistance));
        section.addView(character, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout pets = new LinearLayout(context);
        pets.setOrientation(LinearLayout.VERTICAL);
        pets.addView(buildFormationSection("宠物", firstIndex + 5, resistance));
        LinearLayout.LayoutParams petsParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        petsParams.setMargins(0, dp(4), 0, 0);
        section.addView(pets, petsParams);
        return section;
    }

    private View buildFormationSection(String name, int firstIndex, boolean resistance) {
        LinearLayout section = new LinearLayout(context);
        section.setOrientation(LinearLayout.VERTICAL);
        section.setPadding(dp(6), dp(4), dp(6), dp(5));
        section.setBackground(roundBackground(Color.argb(170, 21, 38, 55),
                Color.argb(132, 130, 180, 198), dp(12), dp(1)));

        TextView heading = text(name, 11.5f, Color.rgb(226, 240, 247));
        heading.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        heading.setPadding(dp(3), 0, 0, 0);
        section.addView(heading, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(resistance ? 18 : 20)));

        LinearLayout tableHeader = new LinearLayout(context);
        tableHeader.setGravity(Gravity.CENTER_VERTICAL);
        tableHeader.setPadding(dp(2), 0, dp(2), 0);
        float positionWeight = resistance ? 1f : 0.9f;
        tableHeader.addView(cell("槽位", true), weightedCellParams(positionWeight));
        if (resistance) {
            TextView metric = cell(RESISTANCE_NAMES[selectedResistance], true);
            resistanceHeaders[resistanceHeaderIndex(firstIndex)] = metric;
            tableHeader.addView(metric, weightedCellParams(1f));
        } else {
            tableHeader.addView(cell("HP", true), weightedCellParams(1f));
            tableHeader.addView(cell("MP", true), weightedCellParams(1f));
            tableHeader.addView(cell("SP", true), weightedCellParams(1f));
        }
        section.addView(tableHeader, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(resistance ? 18 : 20)));

        for (int rowNumber = 0; rowNumber < 5; rowNumber++) {
            int index = firstIndex + 4 - rowNumber;
            LinearLayout row = new LinearLayout(context);
            row.setGravity(Gravity.CENTER_VERTICAL);
            row.setPadding(dp(2), 0, dp(2), 0);
            row.setBackground(roundBackground(Color.argb(155, 31, 48, 64),
                    Color.argb(82, 185, 215, 228), dp(8), dp(1)));
            LinearLayout.LayoutParams rowParams = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(resistance ? 22 : 28));
            rowParams.setMargins(0, dp(2), 0, 0);

            if (resistance) {
                TextView position = cell(slotLabel(index % BattleFrameProtocol.SIDE_SLOT_COUNT),
                        false);
                TextView value = cell("未提供", false);
                value.setTextColor(Color.rgb(224, 237, 173));
                value.setSingleLine(true);
                resistancePositions[index] = position;
                resistanceValues[index] = value;
                row.addView(position, weightedCellParams(1f));
                row.addView(value, weightedCellParams(1f));
            } else {
                TextView position = cell(slotLabel(index % BattleFrameProtocol.SIDE_SLOT_COUNT),
                        false);
                TextView hp = cell("—", false);
                TextView mp = cell("—", false);
                TextView sp = cell("—", false);
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
        if (firstIndex == 0) return 2;
        return 3;
    }

    private void showAttributes() {
        showingResistance = false;
        setPageVisibility();
    }

    private void showResistance() {
        showingResistance = true;
        setPageVisibility();
        listener.onResistancePageSelected();
    }

    private void setPageVisibility() {
        if (attributesBody != null) attributesBody.setVisibility(
                showingResistance ? View.GONE : View.VISIBLE);
        if (resistanceBody != null) resistanceBody.setVisibility(
                showingResistance ? View.VISIBLE : View.GONE);
        if (attributesTab != null) {
            attributesTab.setSelected(!showingResistance);
            attributesTab.setBackground(roundBackground(
                    showingResistance ? Color.rgb(37, 71, 82) : Color.argb(248, 27, 102, 126),
                    Color.rgb(173, 239, 247), dp(9), dp(1)));
        }
        if (resistanceTab != null) {
            resistanceTab.setSelected(showingResistance);
            resistanceTab.setBackground(roundBackground(
                    showingResistance ? Color.argb(248, 27, 102, 126) : Color.rgb(37, 71, 82),
                    Color.rgb(173, 239, 247), dp(9), dp(1)));
        }
    }

    private void selectResistance(int index) {
        if (index < 0 || index >= RESISTANCE_NAMES.length) return;
        selectedResistance = index;
        for (TextView header : resistanceHeaders) {
            if (header != null) header.setText(RESISTANCE_NAMES[index]);
        }
        updateResistanceButtonStyles();
        renderResistance(latestState);
    }

    private void updateResistanceButtonStyles() {
        for (int index = 0; index < resistanceButtons.length; index++) {
            Button option = resistanceButtons[index];
            if (option == null) continue;
            boolean selected = index == selectedResistance;
            option.setSelected(selected);
            option.setTextColor(selected ? Color.WHITE : Color.rgb(197, 219, 232));
            option.setBackground(roundBackground(
                    selected ? Color.argb(248, 27, 102, 126)
                            : Color.argb(155, 31, 48, 64),
                    selected ? Color.rgb(173, 239, 247)
                            : Color.argb(132, 130, 180, 198),
                    dp(7), selected ? dp(2) : dp(1)));
        }
    }

    private void renderBattle(BattleFrameProtocol.Frame frame) {
        if (battleView != null) {
            battleView.setText(frame == null ? "等待有效战斗帧"
                    : "实时战斗 · 第 " + frame.round + " 回合 · BattleId " + frame.battleId);
        }
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
            String detail = null;
            if (live && state.resistanceMessage != null
                    && isResistanceTransientMessage(state.resistanceMessage)) {
                detail = state.resistanceMessage;
            } else if (state != null && state.detail != null && !state.detail.isEmpty()
                    && (state.status == BattleFrameProtocol.STATUS_ROOT_DENIED
                    || state.status == BattleFrameProtocol.STATUS_READ_ERROR
                    || state.status == BattleFrameProtocol.STATUS_EXPIRED)) {
                detail = state.detail;
            }
            if (detail == null || detail.isEmpty()) {
                resistanceDetail.setVisibility(View.GONE);
            } else {
                resistanceDetail.setText(compactMessage(detail, 72));
                resistanceDetail.setVisibility(View.VISIBLE);
            }
        }
        for (int i = 0; i < BattleFrameProtocol.SLOT_COUNT; i++) {
            if (resistancePositions[i] == null) continue;
            BattleFrameProtocol.Slot slot = live ? frame.slots[i] : null;
            resistancePositions[i].setText(slotLabel(i % BattleFrameProtocol.SIDE_SLOT_COUNT));
            resistanceValues[i].setText(matches && slot != null
                    ? snapshot.value(slot.position, RESISTANCE_CODES[selectedResistance])
                    : "未提供");
        }
    }

    private String slotLabel(int sideIndex) {
        return (sideIndex < 5 ? "人物" : "宠") + (sideIndex < 5 ? sideIndex + 1 : sideIndex - 4);
    }

    private boolean isResistanceTransientMessage(String message) {
        return message.startsWith("正在查询") || message.contains("未取得")
                || message.contains("未采用");
    }

    private void setStatus(String value, int status) {
        if (statusView == null) return;
        statusView.setText(value);
        statusView.setTextColor(statusColor(status));
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

    private Button smallButton(String label) {
        Button button = new Button(context);
        button.setText(label);
        button.setAllCaps(false);
        button.setTextSize(11f);
        button.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        button.setTextColor(Color.WHITE);
        button.setMinHeight(0);
        button.setMinimumHeight(0);
        button.setMinWidth(0);
        button.setMinimumWidth(0);
        button.setPadding(dp(5), 0, dp(5), 0);
        button.setBackground(roundBackground(Color.rgb(37, 71, 82),
                Color.rgb(173, 239, 247), dp(9), dp(1)));
        return button;
    }

    private TextView cell(String value, boolean header) {
        TextView view = text(value, header ? 9.5f : 9.7f,
                header ? Color.rgb(197, 219, 232) : Color.WHITE);
        view.setGravity(Gravity.CENTER);
        view.setSingleLine(true);
        if (!header) view.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        return view;
    }

    private LinearLayout.LayoutParams weightedCellParams(float weight) {
        return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, weight);
    }

    private TextView text(String value, float size, int color) {
        TextView view = new TextView(context);
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

    private String compactMessage(String value, int maximumLength) {
        if (value == null || value.length() <= maximumLength) return value;
        return value.substring(0, Math.max(0, maximumLength - 1)) + "…";
    }

    private int dp(int value) {
        return (int) (value * context.getResources().getDisplayMetrics().density + 0.5f);
    }
}
