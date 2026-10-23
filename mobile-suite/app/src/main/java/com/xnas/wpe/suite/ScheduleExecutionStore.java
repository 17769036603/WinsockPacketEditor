package com.xnas.wpe.suite;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import org.json.JSONTokener;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/** Private persistence for the most recent trigger and its per-target outcomes. */
public final class ScheduleExecutionStore {
    private static final String PREFERENCES = "schedule_execution";
    private static final String KEY_PREFIX = "task_";

    private ScheduleExecutionStore() { }

    private static SharedPreferences preferences(Context context) {
        Context app = context.getApplicationContext();
        return (app == null ? context : app)
                .getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE);
    }

    public static void save(Context context, ScheduleExecution execution) {
        if (context == null || execution == null || execution.taskId <= 0L) return;
        try {
            preferences(context).edit().putString(key(execution.taskId), encode(execution)).apply();
        } catch (JSONException | RuntimeException ignored) {
            // Result persistence is best-effort and must not interrupt module dispatch.
        }
    }

    public static ScheduleExecution latest(Context context, long taskId) {
        if (context == null || taskId <= 0L) return null;
        String raw = preferences(context).getString(key(taskId), "");
        return decode(raw, taskId);
    }

    public static void remove(Context context, long taskId) {
        if (context == null || taskId <= 0L) return;
        preferences(context).edit().remove(key(taskId)).apply();
    }

    public static List<ScheduleExecution> loadAll(Context context) {
        if (context == null) return Collections.emptyList();
        List<ScheduleExecution> executions = new ArrayList<>();
        for (Map.Entry<String, ?> entry : preferences(context).getAll().entrySet()) {
            if (!entry.getKey().startsWith(KEY_PREFIX) || !(entry.getValue() instanceof String)) continue;
            long id = parseId(entry.getKey().substring(KEY_PREFIX.length()));
            ScheduleExecution execution = decode((String) entry.getValue(), id);
            if (execution != null) executions.add(execution);
        }
        Collections.sort(executions, new Comparator<ScheduleExecution>() {
            @Override public int compare(ScheduleExecution left, ScheduleExecution right) {
                int time = Long.compare(left.firedAtMillis, right.firedAtMillis);
                return time != 0 ? time : Long.compare(left.taskId, right.taskId);
            }
        });
        return executions;
    }

    /** Stable read-only format: aggregate state plus the local trigger minute only. */
    public static String diagnosticSummary(Context context) {
        ScheduleExecution latest = null;
        for (ScheduleExecution execution : loadAll(context)) {
            if (latest == null || execution.firedAtMillis >= latest.firedAtMillis) latest = execution;
        }
        if (latest == null || latest.firedAtMillis <= 0L) return "none|--:--";
        String minute = new SimpleDateFormat("HH:mm", Locale.US)
                .format(new Date(latest.firedAtMillis));
        return latest.aggregate().value() + "|" + minute;
    }

    public static String taskSummary(Context context, long taskId) {
        ScheduleExecution execution = latest(context, taskId);
        if (execution == null) return "最近结果：暂无";
        StringBuilder text = new StringBuilder("最近结果：")
                .append(aggregateLabel(execution.aggregate()));
        for (ScheduleExecution.TargetResult target : execution.targets()) {
            if (text.length() > 0) text.append(" · ");
            text.append(ModuleActions.isKnownTargetId(target.moduleId)
                            ? ModuleActions.label(target.moduleId) : "无效目标")
                    .append(' ')
                    .append(targetLabel(target));
        }
        return text.toString();
    }

    private static String targetLabel(ScheduleExecution.TargetResult target) {
        if (target.dispatchState == ScheduleExecution.DispatchState.NOT_ATTEMPTED) {
            return target.confirmationState == ScheduleExecution.ConfirmationState.UNKNOWN
                    ? "未派发，结果未知（" + reasonLabel(target.reasonCode) + "）" : "未派发";
        }
        if (target.dispatchState == ScheduleExecution.DispatchState.FAILED) {
            return "派发失败（" + reasonLabel(target.reasonCode) + "）";
        }
        switch (target.confirmationState) {
            case CONFIRMED: return "已确认";
            case FAILED: return "动作失败（" + reasonLabel(target.reasonCode) + "）";
            case UNKNOWN: return "未知（" + reasonLabel(target.reasonCode) + "）";
            default:
                if (target.reasonCode != ScheduleExecution.ReasonCode.NONE) {
                    return "等待确认（" + reasonLabel(target.reasonCode) + "）";
                }
                return "已派发，等待确认";
        }
    }

    private static String aggregateLabel(ScheduleExecution.AggregateState state) {
        switch (state) {
            case PENDING: return "待处理";
            case DISPATCHED: return "已派发，待确认";
            case CONFIRMED: return "全部确认";
            case PARTIAL: return "部分完成";
            case FAILED: return "失败";
            case UNKNOWN: return "结果未知";
            default: return "暂无";
        }
    }

    private static String reasonLabel(ScheduleExecution.ReasonCode reason) {
        if (reason == null) return "未确认";
        switch (reason) {
            case UNSUPPORTED_TARGET: return "目标不支持";
            case UNSUPPORTED_ACTION: return "动作不支持";
            case INVALID_TARGET: return "目标无效";
            case DISPATCH_REJECTED: return "服务未接受请求";
            case DISPATCH_EXCEPTION: return "派发异常";
            case OBSERVATION_MISSING: return "状态缺失";
            case OBSERVATION_STALE: return "状态陈旧";
            case IDENTITY_MISMATCH: return "身份不匹配";
            case STATE_NOT_CONFIRMED: return "状态未满足";
            case CLEANUP_UNCONFIRMED: return "清理未确认";
            case NO_CONFIRMATION_CONTRACT: return "没有可用确认状态";
            case TIMEOUT: return "等待超时";
            case INTERRUPTED: return "派发被中断";
            default: return "未确认";
        }
    }

    private static String encode(ScheduleExecution execution) throws JSONException {
        JSONObject object = new JSONObject();
        object.put("version", 1);
        object.put("taskId", execution.taskId);
        object.put("action", execution.action);
        object.put("firedAt", execution.firedAtMillis);
        object.put("timeout", execution.timeoutMillis);
        JSONArray targets = new JSONArray();
        for (ScheduleExecution.TargetResult target : execution.targets()) {
            JSONObject item = new JSONObject();
            item.put("module", target.moduleId);
            item.put("action", target.action);
            item.put("dispatch", target.dispatchState.name());
            item.put("confirmation", target.confirmationState.name());
            item.put("reason", target.reasonCode.value());
            item.put("dispatchAt", target.dispatchAtMillis);
            item.put("deadline", target.deadlineMillis);
            item.put("observedAt", target.lastObservedAtMillis);
            item.put("baselineSequence", target.baselineSequence);
            item.put("baselineMarker", target.baselineMarker);
            item.put("packageName", target.packageName);
            targets.put(item);
        }
        object.put("targets", targets);
        return object.toString();
    }

    private static ScheduleExecution decode(String raw, long keyId) {
        if (raw == null || raw.isEmpty() || keyId <= 0L) return null;
        try {
            Object parsed = new JSONTokener(raw).nextValue();
            if (!(parsed instanceof JSONObject)) return null;
            JSONObject object = (JSONObject) parsed;
            if (object.optInt("version", 0) != 1 || object.optLong("taskId", -1L) != keyId) return null;
            JSONArray array = object.optJSONArray("targets");
            List<ScheduleExecution.TargetResult> targets = new ArrayList<>();
            if (array != null) {
                for (int i = 0; i < array.length(); i++) {
                    JSONObject item = array.optJSONObject(i);
                    if (item == null) continue;
                    String moduleId = item.optString("module", "");
                    if (!ModuleActions.isKnownTargetId(moduleId)) continue;
                    targets.add(ScheduleExecution.TargetResult.restore(
                            moduleId,
                            item.optString("action", object.optString("action", "start")),
                            enumValue(ScheduleExecution.DispatchState.class,
                                    item.optString("dispatch", "NOT_ATTEMPTED"),
                                    ScheduleExecution.DispatchState.NOT_ATTEMPTED),
                            enumValue(ScheduleExecution.ConfirmationState.class,
                                    item.optString("confirmation", "PENDING"),
                                    ScheduleExecution.ConfirmationState.PENDING),
                            ScheduleExecution.ReasonCode.fromValue(item.optString("reason", "none")),
                            item.optLong("dispatchAt", 0L), item.optLong("deadline", 0L),
                            item.optLong("observedAt", 0L), item.optLong("baselineSequence", -1L),
                            item.optString("baselineMarker", ""), item.optString("packageName", "")));
                }
            }
            return ScheduleExecution.restore(keyId, object.optString("action", "start"),
                    object.optLong("firedAt", 0L),
                    object.optLong("timeout", ScheduleExecution.DEFAULT_TIMEOUT_MS), targets);
        } catch (JSONException | RuntimeException ignored) {
            return null;
        }
    }

    private static <T extends Enum<T>> T enumValue(Class<T> type, String value, T fallback) {
        try { return Enum.valueOf(type, value); }
        catch (IllegalArgumentException | NullPointerException ignored) { return fallback; }
    }

    private static long parseId(String value) {
        try {
            long id = Long.parseLong(value);
            return id > 0L ? id : -1L;
        } catch (NumberFormatException ignored) {
            return -1L;
        }
    }

    private static String key(long taskId) { return KEY_PREFIX + taskId; }
}
