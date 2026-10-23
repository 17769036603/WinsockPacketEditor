package com.xnas.wpe.suite;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.Arrays;
import java.util.Locale;

/** One enabled schedule and its action/target contract. */
public final class ScheduleTask {
    public static final String ACTION_START = "start";
    public static final String ACTION_STOP = "stop";
    /** Persisted only for old daily battle-loop tasks; the editor never offers these actions. */
    public static final String ACTION_PAUSE = "pause";
    public static final String ACTION_RESUME = "resume";
    public static final String TARGET_ALL = "all";

    public static final String TYPE_CYCLE = "cycle";
    public static final String TYPE_LEGACY_DAILY = "legacy_daily";

    public static final int CYCLE_HOUR = 1;
    public static final int CYCLE_HALF = 2;
    public static final int DEFAULT_LEAD_MINUTES = 3;

    /** These are the only actions exposed by the current editor. */
    public static final String[] ACTIONS = {ACTION_START, ACTION_STOP};

    public final long id;
    public final String type;
    public int cycles;
    public int leadMinutes;
    /** Used only when type == legacy_daily; cycle tasks store -1 here. */
    public final int hour;
    public final int minute;
    public String action;
    public String target;
    public boolean enabled;

    public ScheduleTask(long id, int cycles, int leadMinutes, String action,
                        String target, boolean enabled) {
        this(id, TYPE_CYCLE, cycles, leadMinutes, -1, -1, action, target, enabled);
    }

    public static ScheduleTask legacyDaily(long id, int hour, int minute, String action,
                                           String target, boolean enabled) {
        return new ScheduleTask(id, TYPE_LEGACY_DAILY, 0, 0,
                clamp(hour, 0, 23), clamp(minute, 0, 59), action, target, enabled);
    }

    private ScheduleTask(long id, String type, int cycles, int leadMinutes, int hour, int minute,
                         String action, String target, boolean enabled) {
        this.id = id;
        this.type = TYPE_LEGACY_DAILY.equals(type) ? TYPE_LEGACY_DAILY : TYPE_CYCLE;
        this.cycles = cycles & (CYCLE_HOUR | CYCLE_HALF);
        if (this.type.equals(TYPE_CYCLE) && this.cycles == 0) {
            this.cycles = CYCLE_HOUR | CYCLE_HALF;
        }
        this.leadMinutes = clamp(leadMinutes, 0, 30);
        this.hour = this.type.equals(TYPE_LEGACY_DAILY) ? clamp(hour, 0, 23) : -1;
        this.minute = this.type.equals(TYPE_LEGACY_DAILY) ? clamp(minute, 0, 59) : -1;
        this.action = preserveAction(action);
        this.target = target == null || target.trim().isEmpty() ? TARGET_ALL : target.trim();
        this.enabled = enabled && isRunnableAction();
    }

    /** Returns null for an unrecognized action. Callers must reject it instead of assuming start. */
    public static String normalizeAction(String action) {
        if (ACTION_START.equals(action) || ACTION_STOP.equals(action)
                || ACTION_PAUSE.equals(action) || ACTION_RESUME.equals(action)) return action;
        return null;
    }

    public static boolean isKnownAction(String action) {
        return normalizeAction(action) != null;
    }

    private static String preserveAction(String action) {
        String normalized = normalizeAction(action);
        if (normalized != null) return normalized;
        if (action == null || action.trim().isEmpty()) return "unknown";
        String raw = action.trim();
        return raw.length() > 64 ? raw.substring(0, 64) : raw;
    }

    public boolean isLegacyDaily() {
        return TYPE_LEGACY_DAILY.equals(type);
    }

    /** Old pause/resume commands are valid only on an existing daily battle-loop task. */
    public boolean isLegacyCompatibilityAction() {
        return isLegacyDaily()
                && TargetGame.BATTLE_LOOP.equals(target)
                && (ACTION_PAUSE.equals(action) || ACTION_RESUME.equals(action));
    }

    public boolean isRunnableAction() {
        if (isLegacyCompatibilityAction()) return true;
        if (!ACTION_START.equals(action) && !ACTION_STOP.equals(action)) return false;
        for (String id : ModuleActions.targetIds(target)) {
            if (TARGET_ALL.equals(id)) {
                if (!ACTION_STOP.equals(action)) return false;
            } else if (!ModuleActions.isModuleId(id)) {
                // Retired targets stay visible for editing/removal, but never restart silently.
                return false;
            }
        }
        return ModuleActions.targetIds(target).length > 0;
    }

    private boolean containsTarget(String value) {
        for (String id : ModuleActions.targetIds(target)) if (value.equals(id)) return true;
        return false;
    }

    public boolean isStop() {
        return ACTION_STOP.equals(action);
    }

    /** Only start brings the selected game to the foreground. */
    public boolean needsGameForeground() {
        return ACTION_START.equals(action) && isRunnableAction();
    }

    public boolean hasCycle(int cycle) {
        return !isLegacyDaily() && (cycles & cycle) != 0;
    }

    public String cycleLabel() {
        if (isLegacyDaily()) return "每日固定时刻";
        boolean hourCycle = hasCycle(CYCLE_HOUR);
        boolean halfCycle = hasCycle(CYCLE_HALF);
        if (hourCycle && halfCycle) return "整点+半点";
        if (halfCycle) return "半点";
        return "整点";
    }

    /** Sorted minute-of-day occurrences for a full local day. */
    public int[] triggerMinutes() {
        if (isLegacyDaily()) return new int[]{hour * 60 + minute};
        int perDay = (hasCycle(CYCLE_HOUR) ? 24 : 0) + (hasCycle(CYCLE_HALF) ? 24 : 0);
        int[] minutes = new int[perDay];
        int index = 0;
        for (int hourOfDay = 0; hourOfDay < 24; hourOfDay++) {
            if (hasCycle(CYCLE_HOUR)) {
                minutes[index++] = Math.floorMod(hourOfDay * 60 - leadMinutes, 1440);
            }
            if (hasCycle(CYCLE_HALF)) {
                minutes[index++] = Math.floorMod(hourOfDay * 60 + 30 - leadMinutes, 1440);
            }
        }
        Arrays.sort(minutes);
        return minutes;
    }

    /** Compact recurring-time hint for the task row. */
    public String triggerLabel() {
        if (isLegacyDaily()) return String.format(Locale.US, "每日 %02d:%02d", hour, minute);
        StringBuilder text = new StringBuilder();
        if (hasCycle(CYCLE_HOUR)) {
            text.append("每小时 :").append(String.format(Locale.US, "%02d",
                    Math.floorMod(-leadMinutes, 60)));
        }
        if (hasCycle(CYCLE_HALF)) {
            if (text.length() > 0) text.append(" / ");
            text.append("每小时 :").append(String.format(Locale.US, "%02d",
                    Math.floorMod(30 - leadMinutes, 60)));
        }
        return text.toString();
    }

    public String scheduleLabel() {
        if (isLegacyDaily()) return triggerLabel();
        String lead = leadMinutes == 0 ? "准点" : "提前" + leadMinutes + "分";
        return cycleLabel() + " · " + lead;
    }

    public String actionLabel() {
        if (ACTION_STOP.equals(action)) return "停止";
        if (ACTION_PAUSE.equals(action)) return "暂停（旧任务兼容）";
        if (ACTION_RESUME.equals(action)) return "继续（旧任务兼容）";
        if (ACTION_START.equals(action)) return "启动";
        return "未知操作“" + action + "”（已停用）";
    }

    public String targetLabel() {
        return ModuleActions.label(target);
    }

    JSONObject toJson() throws JSONException {
        JSONObject object = new JSONObject();
        object.put("id", id);
        object.put("type", type);
        if (isLegacyDaily()) {
            object.put("hour", hour);
            object.put("minute", minute);
        } else {
            object.put("cycles", cycles);
            object.put("lead", leadMinutes);
        }
        object.put("action", action);
        object.put("target", target);
        object.put("enabled", enabled);
        return object;
    }

    static ScheduleTask fromJson(JSONObject object) {
        if (object == null) return null;
        long id = object.optLong("id", 0L);
        if (id <= 0L) return null;
        String target = object.optString("target", TARGET_ALL);
        if (!ModuleActions.knownTarget(target)) return null;

        String type = object.optString("type", "");
        boolean hasLegacyTime = object.has("hour") && object.has("minute");
        boolean legacyDaily = TYPE_LEGACY_DAILY.equals(type)
                || (!TYPE_CYCLE.equals(type) && hasLegacyTime);
        String action = object.optString("action", ACTION_START);
        if (legacyDaily) {
            if (!hasLegacyTime) return null;
            return legacyDaily(id, object.optInt("hour", -1), object.optInt("minute", -1),
                    action, target, object.optBoolean("enabled", true));
        }
        if (!type.isEmpty() && !TYPE_CYCLE.equals(type)) return null;
        return new ScheduleTask(id,
                object.optInt("cycles", CYCLE_HOUR | CYCLE_HALF),
                object.optInt("lead", DEFAULT_LEAD_MINUTES),
                action,
                target,
                object.optBoolean("enabled", true));
    }

    private static int clamp(int value, int lower, int upper) {
        if (value < lower) return lower;
        return Math.min(value, upper);
    }
}
