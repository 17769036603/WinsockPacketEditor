package com.xnas.wpe.suite;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import org.json.JSONTokener;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Comparator;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/** Task persistence and the per-task monotonic occurrence fence. */
public final class ScheduleStore {
    private static final String PREFERENCES = "schedule_tasks";
    private static final String KEY_TASKS = "tasks";
    private static final String KEY_FIRED = "fired";
    static final int MAX_TASKS = 20;

    private ScheduleStore() { }

    private static SharedPreferences preferences(Context context) {
        Context application = context.getApplicationContext();
        return (application == null ? context : application)
                .getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE);
    }

    public static List<ScheduleTask> load(Context context) {
        List<ScheduleTask> tasks = new ArrayList<>();
        String raw = preferences(context).getString(KEY_TASKS, "");
        if (raw.isEmpty()) return tasks;
        try {
            Object parsed = new JSONTokener(raw).nextValue();
            if (!(parsed instanceof JSONArray)) return tasks;
            JSONArray array = (JSONArray) parsed;
            for (int i = 0; i < array.length(); i++) {
                ScheduleTask task = ScheduleTask.fromJson(array.optJSONObject(i));
                if (task != null) tasks.add(task);
            }
        } catch (JSONException | RuntimeException ignored) {
            // Corrupt preferences should not prevent the page from adding a new task.
        }
        sort(tasks);
        return tasks;
    }

    public static void save(Context context, List<ScheduleTask> tasks) {
        sort(tasks);
        JSONArray array = new JSONArray();
        for (ScheduleTask task : tasks) {
            try {
                array.put(task.toJson());
            } catch (JSONException ignored) {
                // One malformed row must not discard the remaining tasks.
            }
        }
        preferences(context).edit().putString(KEY_TASKS, array.toString()).apply();
    }

    public static boolean add(Context context, ScheduleTask task) {
        if (task == null || !task.isRunnableAction()) return false;
        List<ScheduleTask> tasks = load(context);
        if (tasks.size() >= MAX_TASKS) return false;
        for (ScheduleTask existing : tasks) {
            if (sameDefinition(existing, task)) return false;
        }
        tasks.add(task);
        save(context, tasks);
        return true;
    }

    /** Replace by id only when the replacement does not duplicate another task. */
    public static boolean update(Context context, ScheduleTask task) {
        if (task == null || !task.isRunnableAction()) return false;
        List<ScheduleTask> tasks = load(context);
        int match = -1;
        for (int i = 0; i < tasks.size(); i++) {
            ScheduleTask existing = tasks.get(i);
            if (existing.id == task.id) {
                match = i;
                continue;
            }
            if (sameDefinition(existing, task)) return false;
        }
        if (match < 0) return false;

        // Editing cannot silently re-enable or disable a task, and a duplicate failure above
        // leaves the original row untouched.
        task.enabled = tasks.get(match).enabled;
        tasks.set(match, task);
        save(context, tasks);
        return true;
    }

    public static void remove(Context context, long id) {
        List<ScheduleTask> tasks = load(context);
        for (int i = 0; i < tasks.size(); i++) {
            if (tasks.get(i).id == id) {
                tasks.remove(i);
                save(context, tasks);
                ScheduleExecutionStore.remove(context, id);
                return;
            }
        }
    }

    public static void setEnabled(Context context, long id, boolean enabled) {
        List<ScheduleTask> tasks = load(context);
        for (ScheduleTask task : tasks) if (task.id == id) task.enabled = enabled;
        save(context, tasks);
    }

    public static boolean hasEnabled(Context context) {
        for (ScheduleTask task : load(context)) if (task.enabled) return true;
        return false;
    }

    /** Notification summary: show the earliest eligible enabled occurrence. */
    public static String summary(Context context) {
        List<ScheduleTask> tasks = load(context);
        int enabled = 0;
        for (ScheduleTask task : tasks) if (task.enabled) enabled++;
        if (enabled == 0) return "没有启用的定时任务";
        String next = nextLabel(context, tasks);
        return next == null ? enabled + " 条定时任务已启用" : next;
    }

    public static String nextLabel(Context context, List<ScheduleTask> tasks) {
        ScheduleTask task = next(context, tasks);
        if (task == null) return null;
        return "下一条 " + nextTimeLabel(context, task) + " "
                + task.actionLabel() + " " + task.targetLabel();
    }

    public static ScheduleTask next(Context context, List<ScheduleTask> tasks) {
        ScheduleTask best = null;
        long bestAt = Long.MAX_VALUE;
        long now = System.currentTimeMillis();
        for (ScheduleTask task : tasks) {
            if (!task.enabled || !task.isRunnableAction()) continue;
            long occurrence = nextAt(context, task, now);
            if (occurrence < bestAt) {
                bestAt = occurrence;
                best = task;
            }
        }
        return bestAt == Long.MAX_VALUE ? null : best;
    }

    public static String nextTimeLabel(Context context, ScheduleTask task) {
        long next = nextAt(context, task, System.currentTimeMillis());
        if (next == Long.MAX_VALUE) return "等待时钟恢复";
        return new SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.US).format(new Date(next));
    }

    static long nextAt(Context context, ScheduleTask task) {
        return nextAt(context, task, System.currentTimeMillis());
    }

    static long nextAt(Context context, ScheduleTask task, long nowMillis) {
        return ScheduleExecutionTime.nextAt(nowMillis, task.triggerMinutes(), lastKey(context, task));
    }

    /** Stable, privacy-limited contract consumed by settings diagnostics. */
    public static String diagnosticSummary(Context context) {
        return ScheduleExecutionStore.diagnosticSummary(context);
    }

    /** Existing format retained for compatibility with callers that know minute and day offset. */
    public static String triggerKeyFor(int triggerMinuteOfDay, boolean previousDay) {
        java.util.Calendar day = java.util.Calendar.getInstance();
        if (previousDay) day.add(java.util.Calendar.DAY_OF_YEAR, -1);
        day.set(java.util.Calendar.HOUR_OF_DAY, triggerMinuteOfDay / 60);
        day.set(java.util.Calendar.MINUTE, triggerMinuteOfDay % 60);
        day.set(java.util.Calendar.SECOND, 0);
        day.set(java.util.Calendar.MILLISECOND, 0);
        return ScheduleExecutionTime.occurrenceKey(day.getTimeInMillis());
    }

    public static String triggerKeyFor(long occurrenceAtMillis) {
        return ScheduleExecutionTime.occurrenceKey(occurrenceAtMillis);
    }

    public static String lastKey(Context context, ScheduleTask task) {
        return fired(context).optString(String.valueOf(task.id), "");
    }

    /** Stores only strictly newer keys, so a delayed tick after clock rollback cannot lower the fence. */
    public static boolean markFired(Context context, ScheduleTask task, String key) {
        if (key == null || key.isEmpty()) return false;
        JSONObject fired = fired(context);
        String old = fired.optString(String.valueOf(task.id), "");
        if (key.compareTo(old) <= 0) return false;
        try {
            fired.put(String.valueOf(task.id), key);
        } catch (JSONException ignored) {
            return false;
        }
        preferences(context).edit().putString(KEY_FIRED, fired.toString()).apply();
        return true;
    }

    /** Remove trigger records for deleted tasks. */
    public static void pruneFired(Context context) {
        List<ScheduleTask> tasks = load(context);
        JSONObject fired = fired(context);
        JSONObject kept = new JSONObject();
        for (ScheduleTask task : tasks) {
            String key = String.valueOf(task.id);
            String value = fired.optString(key, "");
            if (value.isEmpty()) continue;
            try {
                kept.put(key, value);
            } catch (JSONException ignored) {
                // Keep other task fences if one entry cannot be copied.
            }
        }
        preferences(context).edit().putString(KEY_FIRED, kept.toString()).apply();
    }

    private static JSONObject fired(Context context) {
        String raw = preferences(context).getString(KEY_FIRED, "");
        if (raw.isEmpty()) return new JSONObject();
        try {
            Object parsed = new JSONTokener(raw).nextValue();
            return parsed instanceof JSONObject ? (JSONObject) parsed : new JSONObject();
        } catch (JSONException | RuntimeException ignored) {
            return new JSONObject();
        }
    }

    private static boolean sameDefinition(ScheduleTask left, ScheduleTask right) {
        if (!left.type.equals(right.type)
                || !left.action.equals(right.action)
                || !sameTargets(left, right)) return false;
        if (left.isLegacyDaily()) return left.hour == right.hour && left.minute == right.minute;
        return left.cycles == right.cycles && left.leadMinutes == right.leadMinutes;
    }

    private static boolean sameTargets(ScheduleTask left, ScheduleTask right) {
        String[] a = ModuleActions.resolveTargets(left.target, left.action);
        String[] b = ModuleActions.resolveTargets(right.target, right.action);
        if (a.length == 0) a = ModuleActions.targetIds(left.target);
        if (b.length == 0) b = ModuleActions.targetIds(right.target);
        Arrays.sort(a);
        Arrays.sort(b);
        return Arrays.equals(a, b);
    }

    private static void sort(List<ScheduleTask> tasks) {
        Collections.sort(tasks, new Comparator<ScheduleTask>() {
            @Override
            public int compare(ScheduleTask left, ScheduleTask right) {
                int[] leftOccurrences = left.triggerMinutes();
                int[] rightOccurrences = right.triggerMinutes();
                int leftFirst = leftOccurrences.length == 0 ? 0 : leftOccurrences[0];
                int rightFirst = rightOccurrences.length == 0 ? 0 : rightOccurrences[0];
                if (leftFirst != rightFirst) return leftFirst < rightFirst ? -1 : 1;
                return Long.compare(left.id, right.id);
            }
        });
    }
}
