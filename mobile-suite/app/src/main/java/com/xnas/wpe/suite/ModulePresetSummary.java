package com.xnas.wpe.suite;

import android.content.Context;
import android.content.SharedPreferences;

import java.util.ArrayList;
import java.util.List;

/**
 * Human-readable summary of the saved module preset a scheduled START will use.
 *
 * <p>The schedule task stores only action and target ids; the star levels, cycle maps and
 * boss maps live in each module's own settings. Showing them here makes it explicit what a
 * timer will actually do without opening every module page.</p>
 */
public final class ModulePresetSummary {
    private ModulePresetSummary() {
    }

    public static String describe(Context context, String action, String targetsCsv) {
        if (!ScheduleTask.ACTION_START.equals(action)) return "停止目标，不读取预设";
        GameMapCatalog.load(context);
        List<String> parts = new ArrayList<>();
        for (String id : ModuleActions.targetIds(targetsCsv)) {
            if (ScheduleTask.TARGET_ALL.equals(id)) {
                for (String target : ModuleActions.startTargets()) {
                    add(parts, one(context, target));
                }
            } else {
                add(parts, one(context, id));
            }
        }
        return parts.isEmpty() ? "使用各模块当前设置" : joinModules(parts);
    }

    private static void add(List<String> parts, String summary) {
        if (summary != null && !summary.isEmpty() && !parts.contains(summary)) parts.add(summary);
    }

    private static String one(Context context, String id) {
        if (TargetGame.STARHUNT.equals(id)) {
            SharedPreferences prefs = prefs(context, id);
            String levels = prefs.getString("selected_levels_v1", "");
            if (levels.isEmpty()) {
                int legacy = prefs.getInt("selected_level", 0);
                levels = legacy > 0 ? String.valueOf(legacy) : "";
            }
            return "自动找星：" + formatLevels(levels) + " · "
                    + formatMaps(prefs.getString("cycle_maps_v1", ""), "巡回");
        }
        if (TargetGame.BOSSHUNT.equals(id)) {
            return "自动BOSS："
                    + formatMaps(prefs(context, id).getString("selected_maps_v1", ""), "地图");
        }
        return ModuleActions.label(id) + "：使用模块当前设置";
    }

    private static String formatLevels(String csv) {
        if (csv == null || csv.trim().isEmpty()) return "未选星级（定时启动会失败）";
        List<String> parts = new ArrayList<>();
        for (String token : csv.split(",")) {
            String value = token.trim();
            if (value.isEmpty()) continue;
            parts.add(value + "星");
        }
        return parts.isEmpty() ? "未选星级（定时启动会失败）" : joinItems(parts);
    }

    private static String formatMaps(String csv, String prefix) {
        if (csv == null || csv.trim().isEmpty()) return "未选地图";
        List<String> parts = new ArrayList<>();
        for (String token : csv.split(",")) {
            String value = token.trim();
            if (value.isEmpty()) continue;
            int id;
            try {
                id = Integer.parseInt(value);
            } catch (NumberFormatException ignored) {
                continue;
            }
            String name = GameMapCatalog.nameOf(id);
            parts.add(name.isEmpty() ? value : name);
        }
        return parts.isEmpty() ? "未选地图" : prefix + " " + joinItems(parts);
    }

    private static String joinItems(List<String> values) {
        StringBuilder out = new StringBuilder();
        for (String value : values) {
            if (out.length() > 0) out.append("、");
            out.append(value);
        }
        return out.toString();
    }

    private static String joinModules(List<String> values) {
        StringBuilder out = new StringBuilder();
        for (String value : values) {
            if (out.length() > 0) out.append("；");
            out.append(value);
        }
        return out.toString();
    }

    private static SharedPreferences prefs(Context context, String moduleId) {
        Context application = context.getApplicationContext();
        return (application == null ? context : application)
                .getSharedPreferences("module." + moduleId + ".settings", Context.MODE_PRIVATE);
    }
}
