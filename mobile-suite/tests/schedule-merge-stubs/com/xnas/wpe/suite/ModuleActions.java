package com.xnas.wpe.suite;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;

public final class ModuleActions {
    private static final String[] IDS = {
            TargetGame.ANLEI, TargetGame.STARHUNT, TargetGame.BOSSHUNT
    };

    private ModuleActions() { }

    public static String[] targetIds(String target) {
        if (target == null || target.trim().isEmpty()) return new String[0];
        String[] parts = target.split(",");
        List<String> result = new ArrayList<>();
        for (String part : parts) if (!part.trim().isEmpty()) result.add(part.trim());
        return result.toArray(new String[0]);
    }

    public static boolean knownTarget(String target) {
        String[] ids = targetIds(target);
        if (ids.length == 0) return false;
        for (String id : ids) {
            if (ScheduleTask.TARGET_ALL.equals(id) || TargetGame.BATTLE_LOOP.equals(id)) continue;
            boolean known = false;
            for (String allowed : IDS) if (allowed.equals(id)) known = true;
            if (!known) return false;
        }
        return true;
    }

    public static String[] resolveTargets(String target, String action) {
        String normalized = ScheduleTask.normalizeAction(action);
        if (normalized == null) return new String[0];
        LinkedHashSet<String> result = new LinkedHashSet<>();
        for (String id : targetIds(target)) {
            if (ScheduleTask.TARGET_ALL.equals(id)) {
                if (ScheduleTask.ACTION_STOP.equals(normalized)) {
                    java.util.Collections.addAll(result, IDS);
                }
            } else {
                result.add(id);
            }
        }
        return result.toArray(new String[0]);
    }

    public static String label(String target) { return target == null ? "" : target; }
}
