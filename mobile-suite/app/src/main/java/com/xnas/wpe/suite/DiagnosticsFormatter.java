package com.xnas.wpe.suite;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Formats a deliberately small, revalidated allowlist of shareable diagnostic fields. */
public final class DiagnosticsFormatter {
    private static final Pattern VERSION_NAME = Pattern.compile("[A-Za-z0-9][A-Za-z0-9._+\\-]{0,63}");
    private static final Pattern CLOCK = Pattern.compile("([0-2][0-9]):([0-5][0-9])");
    private static final Set<String> ALLOWED_ABIS = new HashSet<>(Arrays.asList(
            "armeabi-v7a", "arm64-v8a", "x86", "x86_64"));
    private static final Set<String> SCHEDULE_RESULTS = new HashSet<>(Arrays.asList(
            "none", "pending", "dispatched", "confirmed", "partial", "failed", "unknown"));
    private static final String[] MODULE_ORDER = ModuleStatusSnapshotStore.allowedModuleIds();

    private DiagnosticsFormatter() {
    }

    public static String format(SuiteDiagnostics diagnostics) {
        if (diagnostics == null) diagnostics = new SuiteDiagnostics(
                "", -1L, null, null, null, null);

        StringBuilder text = new StringBuilder(512);
        text.append("飘渺助手诊断\n");
        text.append("版本名: ").append(safeVersionName(diagnostics.versionName)).append('\n');
        text.append("版本码: ").append(diagnostics.versionCode >= 0L
                ? Long.toString(diagnostics.versionCode) : "unknown").append('\n');
        text.append("授权模式: ").append(licenseMode(diagnostics.licenseMode)).append('\n');
        text.append("支持架构: ").append(safeAbis(diagnostics.supportedAbis)).append("\n\n");
        text.append("模块状态（仅固定模块 ID、状态和新鲜度）:\n");

        Map<String, ModuleStatusSnapshotStore.DiagnosticState> byId = new HashMap<>();
        for (ModuleStatusSnapshotStore.DiagnosticState state : diagnostics.moduleStates) {
            if (state == null || !ModuleStatusSnapshotStore.isKnownModule(state.moduleId)) continue;
            if (!byId.containsKey(state.moduleId)) byId.put(state.moduleId, state);
        }
        for (String moduleId : MODULE_ORDER) {
            ModuleStatusSnapshotStore.DiagnosticState state = byId.get(moduleId);
            ModuleStatus.State safeState = state == null || state.freshness == null
                    || state.freshness != ModuleStatusSnapshotStore.Freshness.FRESH
                    ? ModuleStatus.State.UNKNOWN : safeState(state.state);
            ModuleStatusSnapshotStore.Freshness freshness = state == null || state.freshness == null
                    ? ModuleStatusSnapshotStore.Freshness.UNKNOWN : state.freshness;
            text.append(moduleId).append(" | ").append(stateLabel(safeState))
                    .append(" | ").append(freshnessLabel(freshness)).append('\n');
        }
        text.append("最近调度: ").append(safeSchedule(diagnostics.scheduleSummary));
        return text.toString();
    }

    private static String safeVersionName(String value) {
        if (value == null || !VERSION_NAME.matcher(value).matches()) return "unknown";
        return value;
    }

    private static String licenseMode(SuiteDiagnostics.LicenseMode mode) {
        if (mode == SuiteDiagnostics.LicenseMode.BYPASS) return "免激活";
        if (mode == SuiteDiagnostics.LicenseMode.STANDARD) return "普通";
        return "unknown";
    }

    private static String safeAbis(List<String> values) {
        List<String> result = new ArrayList<>();
        if (values != null) {
            for (String value : values) {
                if (ALLOWED_ABIS.contains(value) && !result.contains(value)) result.add(value);
            }
        }
        return result.isEmpty() ? "unknown" : join(result, ", ");
    }

    private static String safeSchedule(String value) {
        if (value == null) return "unknown|--:--";
        int separator = value.indexOf('|');
        if (separator <= 0 || separator != value.lastIndexOf('|')) return "unknown|--:--";
        String result = value.substring(0, separator);
        String time = value.substring(separator + 1);
        if (!SCHEDULE_RESULTS.contains(result)) return "unknown|--:--";
        if ("none".equals(result)) {
            return "--:--".equals(time) ? "none|--:--" : "unknown|--:--";
        }
        Matcher clock = CLOCK.matcher(time);
        if (!clock.matches() || Integer.parseInt(clock.group(1)) > 23) return "unknown|--:--";
        return result.toLowerCase(Locale.ROOT) + "|" + time;
    }

    private static ModuleStatus.State safeState(ModuleStatus.State state) {
        return state == null ? ModuleStatus.State.UNKNOWN : state;
    }

    private static String stateLabel(ModuleStatus.State state) {
        switch (state) {
            case STOPPED: return "已停止";
            case WAITING: return "等待中";
            case RUNNING: return "运行中";
            case PAUSED: return "已暂停";
            case STOPPING: return "清理中";
            case ERROR: return "错误";
            default: return "未确认";
        }
    }

    private static String freshnessLabel(ModuleStatusSnapshotStore.Freshness freshness) {
        switch (freshness) {
            case FRESH: return "新鲜";
            case STALE: return "过期";
            default: return "未读取";
        }
    }

    private static String join(List<String> values, String separator) {
        StringBuilder result = new StringBuilder();
        for (String value : values) {
            if (result.length() > 0) result.append(separator);
            result.append(value);
        }
        return result.toString();
    }
}
