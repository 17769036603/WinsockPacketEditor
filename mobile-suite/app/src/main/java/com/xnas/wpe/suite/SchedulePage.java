package com.xnas.wpe.suite;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.text.TextUtils;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;
import android.os.Handler;
import android.os.Looper;

import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * 定时任务页：列表 + 新增对话框。
 *
 * <p>页面由壳直接渲染，不新增 Activity，也不改动各模块页面；所有操作最终都落到
 * {@link ScheduleStore} 和 {@link ScheduleService}。</p>
 */
public final class SchedulePage {
    private static final String[] ACTION_LABELS = {"启动", "停止"};
    private static final String[] LEAD_LABELS = {"0 分钟", "1 分钟", "2 分钟", "3 分钟", "5 分钟", "10 分钟"};
    private static final int[] LEAD_VALUES = {0, 1, 2, 3, 5, 10};
    private static final String[] HOURS = numberLabels(24);
    private static final String[] MINUTES = numberLabels(60);

    private SchedulePage() {
    }

    public static View build(final Activity host, final Runnable onChange) {
        final LinearLayout root = new LinearLayout(host);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(host, GlassSkin.PADDING_H_DP), dp(host, GlassSkin.PADDING_V_DP),
                dp(host, GlassSkin.PADDING_H_DP), dp(host, GlassSkin.PADDING_V_DP));
        root.setBackground(GlassSkin.page(host));

        TextView hint = new TextView(host);
        hint.setText("周期任务按小时整点或半点重复触发。旧的每日固定时刻任务会保留原计划，需明确勾选后才转换为周期。");
        hint.setTextColor(GlassSkin.TEXT_MUTED);
        hint.setTextSize(12);
        hint.setPadding(0, 0, 0, dp(host, 10));
        root.addView(hint, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        ScrollView scroll = new ScrollView(host);
        scroll.setFillViewport(true);
        root.addView(scroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        LinearLayout list = new LinearLayout(host);
        list.setOrientation(LinearLayout.VERTICAL);
        scroll.addView(list, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        List<ScheduleTask> tasks = ScheduleStore.load(host);
        if (tasks.isEmpty()) {
            TextView empty = new TextView(host);
            empty.setText("还没有定时任务。");
            empty.setTextColor(GlassSkin.TEXT_MUTED);
            empty.setTextSize(13);
            empty.setGravity(Gravity.CENTER);
            empty.setPadding(0, dp(host, 28), 0, dp(host, 28));
            list.addView(empty, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        } else {
            for (ScheduleTask task : tasks) list.addView(row(host, task, onChange));
        }

        Button add = new Button(host);
        add.setText("新增定时任务");
        add.setTextSize(GlassSkin.TEXT_BUTTON_SP);
        add.setAllCaps(false);
        GlassSkin.apply(add, GlassSkin.actionTinted(host, 8, GlassSkin.ACTION_START));
        add.setTextColor(GlassSkin.actionText());
        add.setOnClickListener(v -> showEditor(host, null, onChange));
        LinearLayout.LayoutParams addParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(host, 48));
        addParams.topMargin = dp(host, 12);
        root.addView(add, addParams);
        RefreshController refreshController = new RefreshController(root, host);
        root.setTag(refreshController);
        root.addOnAttachStateChangeListener(refreshController);
        refresh(root, host);
        return root;
    }

    /** Refresh labels in-place; edit controls and click listeners are left intact. */
    public static void refresh(View root, Context context) {
        if (root == null || context == null) return;
        Context app = context.getApplicationContext();
        refreshTree(root, app == null ? context : app);
    }

    private static void refreshTree(View view, Context context) {
        Object tag = view.getTag();
        if (tag instanceof RowRefresh) {
            RowRefresh row = (RowRefresh) tag;
            row.next.setText(row.task.enabled
                    ? "下次：" + ScheduleStore.nextTimeLabel(context, row.task)
                    : "下次：已停用");
            row.result.setText(ScheduleExecutionStore.taskSummary(context, row.task.id));
        }
        if (view instanceof ViewGroup) {
            ViewGroup group = (ViewGroup) view;
            for (int i = 0; i < group.getChildCount(); i++) refreshTree(group.getChildAt(i), context);
        }
    }

    private static View row(final Activity host, final ScheduleTask task, final Runnable onChange) {
        LinearLayout card = new LinearLayout(host);
        card.setOrientation(LinearLayout.HORIZONTAL);
        card.setGravity(Gravity.CENTER_VERTICAL);
        card.setPadding(dp(host, 14), dp(host, 10), dp(host, 14), dp(host, 10));
        GlassSkin.apply(card, GlassSkin.surface(host, GlassSkin.RADIUS_CONTAINER_DP));

        TextView schedule = new TextView(host);
        schedule.setText(task.scheduleLabel());
        schedule.setTextSize(15);
        schedule.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        schedule.setTextColor(task.enabled ? GlassSkin.TEXT : GlassSkin.TEXT_DISABLED);
        card.addView(schedule, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout labels = new LinearLayout(host);
        labels.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams labelsParams = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        labelsParams.leftMargin = dp(host, 16);
        card.addView(labels, labelsParams);

        TextView action = new TextView(host);
        action.setText(task.actionLabel() + " " + task.targetLabel());
        action.setTextSize(14);
        action.setTextColor(task.enabled ? GlassSkin.TEXT : GlassSkin.TEXT_DISABLED);
        labels.addView(action);

        if (ScheduleTask.ACTION_START.equals(task.action)) {
            TextView preset = new TextView(host);
            preset.setText(ModulePresetSummary.describe(host, task.action, task.target));
            preset.setTextSize(11);
            preset.setTextColor(task.enabled ? GlassSkin.TEXT_MUTED : GlassSkin.TEXT_DISABLED);
            preset.setMaxLines(3);
            labels.addView(preset);
        }

        TextView state = new TextView(host);
        state.setText("触发 " + task.triggerLabel() + (task.enabled ? " · 已启用" : " · 已停用"));
        state.setTextSize(11);
        state.setTextColor(task.enabled ? GlassSkin.ACCENT : GlassSkin.TEXT_MUTED);
        labels.addView(state);

        TextView next = new TextView(host);
        next.setTextSize(11);
        next.setTextColor(GlassSkin.TEXT_MUTED);
        labels.addView(next);

        TextView result = new TextView(host);
        result.setTextSize(11);
        result.setTextColor(GlassSkin.TEXT_MUTED);
        result.setMaxLines(3);
        labels.addView(result);
        card.setTag(new RowRefresh(task, next, result));

        TextView toggle = new TextView(host);
        toggle.setText(task.enabled ? "停用" : "启用");
        if (!task.isRunnableAction()) {
            toggle.setText("不可用");
            toggle.setEnabled(false);
        }
        toggle.setTextSize(13);
        toggle.setTextColor(GlassSkin.TEXT);
        toggle.setPadding(dp(host, 12), dp(host, 8), dp(host, 12), dp(host, 8));
        toggle.setClickable(true);
        toggle.setFocusable(true);
        toggle.setOnClickListener(v -> {
            ScheduleStore.setEnabled(host, task.id, !task.enabled);
            if (ScheduleStore.hasEnabled(host)) ScheduleService.startIfNeeded(host);
            onChange.run();
        });
        card.addView(toggle);

        TextView delete = new TextView(host);
        delete.setText("删除");
        delete.setTextSize(13);
        delete.setTextColor(Color.rgb(214, 96, 96));
        delete.setPadding(dp(host, 12), dp(host, 8), dp(host, 4), dp(host, 8));
        delete.setClickable(true);
        delete.setFocusable(true);
        delete.setOnClickListener(v -> {
            ScheduleStore.remove(host, task.id);
            ScheduleStore.pruneFired(host);
            onChange.run();
        });
        card.addView(delete);

        card.setClickable(true);
        card.setFocusable(true);
        card.setOnClickListener(v -> {
            if (!task.isRunnableAction()) {
                Toast.makeText(host, "未知或不兼容的动作已停用，请删除后重新创建。", Toast.LENGTH_SHORT).show();
                return;
            }
            showEditor(host, task, onChange);
        });
        card.setContentDescription(task.scheduleLabel() + " " + task.actionLabel() + " "
                + task.targetLabel() + "，点击编辑");

        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        params.bottomMargin = dp(host, 8);
        card.setLayoutParams(params);
        return card;
    }

    private static final class RowRefresh {
        final ScheduleTask task;
        final TextView next;
        final TextView result;
        RowRefresh(ScheduleTask task, TextView next, TextView result) {
            this.task = task;
            this.next = next;
            this.result = result;
        }
    }

    private static final class RefreshController implements View.OnAttachStateChangeListener {
        private static final long REFRESH_MS = 15_000L;
        private final View root;
        private final Context appContext;
        private final Handler handler = new Handler(Looper.getMainLooper());
        private boolean running;
        private final Runnable refresh = new Runnable() {
            @Override public void run() {
                if (!running || !root.isAttachedToWindow()) return;
                SchedulePage.refresh(root, appContext);
                handler.postDelayed(this, REFRESH_MS);
            }
        };

        RefreshController(View root, Context context) {
            this.root = root;
            Context app = context.getApplicationContext();
            this.appContext = app == null ? context : app;
        }

        @Override public void onViewAttachedToWindow(View view) {
            if (running) return;
            running = true;
            handler.removeCallbacks(refresh);
            handler.post(refresh);
        }

        @Override public void onViewDetachedFromWindow(View view) {
            running = false;
            handler.removeCallbacks(refresh);
        }
    }

    /** Add or edit a recurring task; legacy daily times stay daily until explicitly converted. */
    private static void showEditor(final Activity host, final ScheduleTask editing, final Runnable onChange) {
        final LinearLayout content = new LinearLayout(host);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(host, 18), dp(host, 10), dp(host, 18), dp(host, 2));

        final boolean legacyDaily = editing != null && editing.isLegacyDaily();
        final boolean compatibilityAction = editing != null && editing.isLegacyCompatibilityAction();
        final CheckBox convertLegacy = legacyDaily && !compatibilityAction
                ? checkBox(host, "明确转换为整点/半点周期") : null;
        if (convertLegacy != null) {
            convertLegacy.setChecked(false);
            content.addView(convertLegacy);
            TextView conversionHint = label(host, "未勾选时保留原每日时刻，不改变触发时间。");
            content.addView(conversionHint);
        }

        LinearLayout cycleRow = new LinearLayout(host);
        cycleRow.setOrientation(LinearLayout.HORIZONTAL);
        cycleRow.setGravity(Gravity.CENTER_VERTICAL);
        cycleRow.setVisibility(legacyDaily ? View.GONE : View.VISIBLE);
        content.addView(cycleRow);

        final CheckBox hourBox = checkBox(host, "整点");
        final CheckBox halfBox = checkBox(host, "半点");
        cycleRow.addView(hourBox, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(host, 38)));
        cycleRow.addView(halfBox, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(host, 38)));

        TextView leadLabel = new TextView(host);
        leadLabel.setText("提前");
        leadLabel.setTextColor(GlassSkin.TEXT_MUTED);
        leadLabel.setTextSize(12);
        leadLabel.setPadding(dp(host, 12), 0, dp(host, 6), 0);
        cycleRow.addView(leadLabel);

        final Spinner leadSpinner = spinner(host, LEAD_LABELS);
        cycleRow.addView(leadSpinner, new LinearLayout.LayoutParams(0, dp(host, 38), 1f));

        final LinearLayout dailyRow = new LinearLayout(host);
        dailyRow.setOrientation(LinearLayout.HORIZONTAL);
        dailyRow.setGravity(Gravity.CENTER_VERTICAL);
        dailyRow.setVisibility(legacyDaily ? View.VISIBLE : View.GONE);
        TextView dailyLabel = label(host, "每日固定时刻");
        dailyRow.addView(dailyLabel);
        final Spinner dailyHour = spinner(host, HOURS);
        dailyRow.addView(dailyHour, new LinearLayout.LayoutParams(0, dp(host, 38), 1f));
        TextView colon = label(host, ":");
        dailyRow.addView(colon);
        final Spinner dailyMinute = spinner(host, MINUTES);
        dailyRow.addView(dailyMinute, new LinearLayout.LayoutParams(0, dp(host, 38), 1f));
        content.addView(dailyRow);

        final Spinner actionSpinner = spinner(host, ACTION_LABELS);
        content.addView(label(host, "动作"));
        if (compatibilityAction) {
            actionSpinner.setVisibility(View.GONE);
            TextView legacyAction = label(host, editing.actionLabel() + "（仅保留此旧任务）");
            content.addView(legacyAction);
        }
        content.addView(actionSpinner, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(host, 40)));

        content.addView(label(host, "目标（可多选）"));
        final LinearLayout targetBox = new LinearLayout(host);
        targetBox.setOrientation(LinearLayout.VERTICAL);
        ScrollView targetScroll = new ScrollView(host);
        targetScroll.addView(targetBox, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        content.addView(targetScroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(host, 84)));

        final Set<String> selected = new LinkedHashSet<>();
        if (editing != null) {
            for (String id : ModuleActions.targetIds(editing.target)) selected.add(id);
            hourBox.setChecked(editing.hasCycle(ScheduleTask.CYCLE_HOUR));
            halfBox.setChecked(editing.hasCycle(ScheduleTask.CYCLE_HALF));
            leadSpinner.setSelection(leadIndex(editing.leadMinutes));
            actionSpinner.setSelection(editing.isStop() ? 1 : 0);
            if (editing.isLegacyDaily()) {
                dailyHour.setSelection(editing.hour);
                dailyMinute.setSelection(editing.minute);
            }
        } else {
            hourBox.setChecked(true);
            halfBox.setChecked(true);
            leadSpinner.setSelection(leadIndex(ScheduleTask.DEFAULT_LEAD_MINUTES));
        }

        final TextView presetSummary = new TextView(host);
        presetSummary.setTextSize(12);
        presetSummary.setTextColor(GlassSkin.TEXT_MUTED);
        presetSummary.setPadding(0, dp(host, 6), 0, 0);
        content.addView(presetSummary);
        content.addView(label(host, "预设以各模块页面保存的选择为准；启动时按这里显示的星级/地图执行。"));

        final Runnable updatePresetSummary = () -> {
            String action = compatibilityAction ? editing.action : actionAt(actionSpinner);
            presetSummary.setText(ModulePresetSummary.describe(host, action, TextUtils.join(",", selected)));
        };

        final Runnable rebuildTargets = () -> {
            String action = compatibilityAction ? editing.action : actionAt(actionSpinner);
            String[] available = compatibilityAction
                    ? new String[]{TargetGame.BATTLE_LOOP}
                    : ModuleActions.targetsFor(action);
            fillTargets(host, targetBox, available, selected, !compatibilityAction, updatePresetSummary);
            updatePresetSummary.run();
        };
        rebuildTargets.run();
        if (compatibilityAction) {
            for (int i = 0; i < targetBox.getChildCount(); i++) {
                targetBox.getChildAt(i).setEnabled(false);
            }
        }
        if (convertLegacy != null) {
            convertLegacy.setOnCheckedChangeListener((button, checked) -> {
                cycleRow.setVisibility(checked ? View.VISIBLE : View.GONE);
                dailyRow.setVisibility(checked ? View.GONE : View.VISIBLE);
            });
        }
        actionSpinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                rebuildTargets.run();
            }

            @Override
            public void onNothingSelected(AdapterView<?> parent) {
            }
        });

        AlertDialog dialog = new AlertDialog.Builder(host, android.R.style.Theme_Material_Dialog_Alert)
                .setCustomTitle(title(host, editing == null ? "新增定时任务" : "修改定时任务"))
                .setView(content)
                .setPositiveButton("保存", null)
                .setNegativeButton("取消", null)
                .create();
        dialog.setOnShowListener(shown -> {
            android.view.Window window = dialog.getWindow();
            if (window != null) window.setBackgroundDrawable(GlassSkin.surface(host, 14));
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
                int cycles = 0;
                if (hourBox.isChecked()) cycles |= ScheduleTask.CYCLE_HOUR;
                if (halfBox.isChecked()) cycles |= ScheduleTask.CYCLE_HALF;
                StringBuilder target = new StringBuilder();
                for (int i = 0; i < targetBox.getChildCount(); i++) {
                    View child = targetBox.getChildAt(i);
                    if (!(child instanceof CheckBox) || !((CheckBox) child).isChecked()) continue;
                    String id = (String) child.getTag();
                    if (target.length() > 0) target.append(",");
                    target.append(id);
                }
                if (target.length() == 0) {
                    Toast.makeText(host, "请至少选择一个目标", Toast.LENGTH_SHORT).show();
                    return;
                }
                String action = compatibilityAction ? editing.action : actionAt(actionSpinner);
                if (editing != null) {
                    boolean keepDaily = legacyDaily
                            && (compatibilityAction || (convertLegacy != null && !convertLegacy.isChecked()));
                    ScheduleTask updated;
                    if (keepDaily) {
                        int hour = dailyHour.getSelectedItemPosition();
                        int minute = dailyMinute.getSelectedItemPosition();
                        String selectedTarget = compatibilityAction ? editing.target : target.toString();
                        updated = ScheduleTask.legacyDaily(editing.id, hour, minute,
                                action, selectedTarget, editing.enabled);
                    } else {
                        if (cycles == 0) {
                            Toast.makeText(host, "请至少选择整点或半点；旧每日任务需明确转换", Toast.LENGTH_SHORT).show();
                            return;
                        }
                        int lead = LEAD_VALUES[Math.max(0, Math.min(LEAD_VALUES.length - 1,
                                leadSpinner.getSelectedItemPosition()))];
                        updated = new ScheduleTask(editing.id, cycles, lead, action,
                                target.toString(), editing.enabled);
                    }
                    if (!ScheduleStore.update(host, updated)) {
                        Toast.makeText(host, "保存失败：任务已不存在或与另一条任务重复", Toast.LENGTH_SHORT).show();
                        return;
                    }
                } else {
                    if (cycles == 0) {
                        Toast.makeText(host, "请至少选择整点或半点；旧每日任务需明确转换", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    int lead = LEAD_VALUES[Math.max(0, Math.min(LEAD_VALUES.length - 1,
                            leadSpinner.getSelectedItemPosition()))];
                    ScheduleTask task = new ScheduleTask(System.currentTimeMillis(), cycles, lead,
                            action, target.toString(), true);
                    if (!ScheduleStore.add(host, task)) {
                        Toast.makeText(host, "保存失败：已有相同任务或数量已达上限", Toast.LENGTH_SHORT).show();
                        return;
                    }
                }
                ScheduleStore.pruneFired(host);
                ScheduleService.startIfNeeded(host);
                dialog.dismiss();
                onChange.run();
            });
        });
        dialog.show();
    }

    /** 目标复选框：选“全部停止”时清掉其他项，选其他项时清掉“全部停止”。 */
    private static void fillTargets(Activity host, LinearLayout box, String[] targets,
                                    Set<String> selected, boolean allowChanges, Runnable onChanged) {
        box.removeAllViews();
        for (String id : targets) {
            final CheckBox checkBox = new CheckBox(host);
            checkBox.setText(ModuleActions.label(id));
            checkBox.setTextSize(14);
            checkBox.setTextColor(GlassSkin.TEXT);
            checkBox.setTag(id);
            checkBox.setChecked(selected.contains(id));
            checkBox.setEnabled(allowChanges);
            if (allowChanges) {
                checkBox.setOnCheckedChangeListener((button, checked) -> {
                    if (checked) {
                        if (ScheduleTask.TARGET_ALL.equals(id)) {
                            selected.clear();
                            selected.add(id);
                            for (int i = 0; i < box.getChildCount(); i++) {
                                View child = box.getChildAt(i);
                                if (child instanceof CheckBox && child != button) ((CheckBox) child).setChecked(false);
                            }
                        } else {
                            selected.remove(ScheduleTask.TARGET_ALL);
                            selected.add(id);
                        }
                    } else {
                        selected.remove(id);
                    }
                    if (onChanged != null) onChanged.run();
                });
            }
            box.addView(checkBox, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(host, 30)));
        }
    }

    private static String actionAt(Spinner spinner) {
        int index = Math.max(0, Math.min(ScheduleTask.ACTIONS.length - 1,
                spinner.getSelectedItemPosition()));
        return ScheduleTask.ACTIONS[index];
    }

    private static String[] numberLabels(int count) {
        String[] labels = new String[count];
        for (int i = 0; i < count; i++) labels[i] = String.format(Locale.US, "%02d", i);
        return labels;
    }

    private static int leadIndex(int minutes) {
        for (int i = 0; i < LEAD_VALUES.length; i++) {
            if (LEAD_VALUES[i] == minutes) return i;
        }
        return 3;
    }

    private static CheckBox checkBox(Activity host, String text) {
        CheckBox box = new CheckBox(host);
        box.setText(text);
        box.setTextSize(14);
        box.setTextColor(GlassSkin.TEXT);
        return box;
    }

    private static Spinner spinner(Activity host, String[] values) {
        Spinner spinner = new Spinner(host);
        ArrayAdapter<String> adapter = new ArrayAdapter<>(host,
                android.R.layout.simple_spinner_dropdown_item, values);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spinner.setAdapter(adapter);
        spinner.setBackground(GlassSkin.input(host, 8));
        return spinner;
    }

    private static TextView label(Activity host, String text) {
        TextView view = new TextView(host);
        view.setText(text);
        view.setTextColor(GlassSkin.TEXT_MUTED);
        view.setTextSize(12);
        view.setPadding(0, dp(host, 8), 0, dp(host, 4));
        return view;
    }

    private static TextView title(Activity host, String text) {
        TextView view = new TextView(host);
        view.setText(text);
        view.setTextColor(GlassSkin.TEXT);
        view.setTextSize(15);
        view.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        view.setPadding(dp(host, 18), dp(host, 14), dp(host, 18), 0);
        view.setMaxLines(1);
        view.setEllipsize(TextUtils.TruncateAt.END);
        return view;
    }

    private static int dp(Activity host, float value) {
        return Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value,
                host.getResources().getDisplayMetrics()));
    }
}
