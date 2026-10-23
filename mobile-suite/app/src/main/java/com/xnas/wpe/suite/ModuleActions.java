package com.xnas.wpe.suite;

import android.content.Context;
import android.content.ComponentName;
import android.content.Intent;
import android.os.Build;

/**
 * 把“定时任务的目标”翻译成各功能自己的启动/停止请求。
 *
 * <p>这里只复用各模块已有的公开入口，不绕过模块内部的启动保护、冲突检测和清理流程：
 * 定时器发的是和页面上按钮完全相同的 Intent。</p>
 */
public final class ModuleActions {
    /** 可被定时的功能：只保留需要按时间开工/收工的挂机类功能。 */
    public static final String[] IDS = {
            TargetGame.ANLEI,
            TargetGame.STARHUNT,
            TargetGame.BOSSHUNT
    };

    /** The exact set used by persisted daily "all stop" tasks before the editor narrowed. */
    private static final String[] LEGACY_ALL_TARGETS = {
            TargetGame.MOUNT_HOLD, TargetGame.BATTLE_LOOP, TargetGame.BATTLE_PANEL,
            TargetGame.STARHUNT, TargetGame.BOSSHUNT, TargetGame.JINCHAN, TargetGame.ANLEI
    };

    public static boolean isKnownTargetId(String id) {
        if (isModuleId(id) || TargetGame.BATTLE_SKIP.equals(id)) return true;
        for (String legacy : LEGACY_ALL_TARGETS) if (legacy.equals(id)) return true;
        return false;
    }

    private static boolean isLegacyStopTarget(String id) {
        for (String legacy : LEGACY_ALL_TARGETS) if (legacy.equals(id)) return true;
        return false;
    }

    private static final String[] LABELS = {
            "暗雷遇敌",
            "自动找星",
            "自动BOSS"
    };

    private ModuleActions() {
    }

    public static String label(String target) {
        String[] ids = targetIds(target);
        if (ids.length == 0) return "未选择";
        StringBuilder text = new StringBuilder();
        for (String id : ids) {
            if (text.length() > 0) text.append("+");
            text.append(singleLabel(id));
        }
        return text.toString();
    }

    private static String singleLabel(String id) {
        if (ScheduleTask.TARGET_ALL.equals(id)) return "全部停止";
        if (TargetGame.BATTLE_LOOP.equals(id)) return "战斗跳过（旧任务）";
        if (TargetGame.MOUNT_HOLD.equals(id)) return "角色速度（旧任务）";
        if (TargetGame.BATTLE_PANEL.equals(id)) return "战斗面板（旧任务）";
        if (TargetGame.JINCHAN.equals(id)) return "金蝉装备（旧任务）";
        if (TargetGame.BATTLE_SKIP.equals(id)) return "战斗快进（旧任务）";
        for (int i = 0; i < IDS.length; i++) {
            if (IDS[i].equals(id)) return LABELS[i];
        }
        return id;
    }

    public static boolean knownTarget(String target) {
        String[] ids = targetIds(target);
        if (ids.length == 0) return false;
        for (String id : ids) {
            if (ScheduleTask.TARGET_ALL.equals(id)) continue;
            if (!isKnownTargetId(id)) return false;
        }
        return true;
    }

    public static boolean isModuleId(String moduleId) {
        if (moduleId == null) return false;
        for (String id : IDS) if (id.equals(moduleId)) return true;
        return false;
    }

    /**
     * The current editor dispatches only its current module list. The old pause/resume
     * compatibility path is narrower and is accepted only for a persisted daily task.
     */
    public static boolean isScheduledTarget(String moduleId, String action, boolean legacyDaily) {
        String normalized = ScheduleTask.normalizeAction(action);
        if (normalized == null) return false;
        if (isModuleId(moduleId)) {
            return ScheduleTask.ACTION_START.equals(normalized)
                    || ScheduleTask.ACTION_STOP.equals(normalized);
        }
        if (legacyDaily && ScheduleTask.ACTION_STOP.equals(normalized)
                && isLegacyStopTarget(moduleId)) return true;
        return legacyDaily && TargetGame.BATTLE_LOOP.equals(moduleId)
                && (ScheduleTask.ACTION_PAUSE.equals(normalized)
                || ScheduleTask.ACTION_RESUME.equals(normalized));
    }

    /** Expand "all" before execution so every stop target receives its own outcome. */
    public static String[] resolveTargets(String target, String action) {
        return resolveTargets(target, action, false);
    }

    public static String[] resolveTargets(String target, String action, boolean legacyDaily) {
        String normalized = ScheduleTask.normalizeAction(action);
        if (normalized == null) return new String[0];
        java.util.LinkedHashSet<String> resolved = new java.util.LinkedHashSet<>();
        for (String id : targetIds(target)) {
            if (ScheduleTask.TARGET_ALL.equals(id)) {
                if (ScheduleTask.ACTION_STOP.equals(normalized)) {
                    java.util.Collections.addAll(resolved, legacyDaily ? LEGACY_ALL_TARGETS : IDS);
                }
            } else {
                resolved.add(id);
            }
        }
        return resolved.toArray(new String[0]);
    }

    public static final class DispatchResult {
        public final boolean accepted;
        public final ScheduleExecution.ReasonCode reasonCode;
        public final String packageName;

        private DispatchResult(boolean accepted, ScheduleExecution.ReasonCode reasonCode,
                               String packageName) {
            this.accepted = accepted;
            this.reasonCode = reasonCode;
            this.packageName = packageName == null ? "" : packageName;
        }
    }

    /** Dispatch one target and return a safe reason code; service acceptance is not confirmation. */
    public static DispatchResult dispatchTarget(Context context, String moduleId, String action) {
        return dispatchTarget(context, moduleId, action, false);
    }

    /** Compatibility dispatch is gated by the persisted task type, not just the action string. */
    static DispatchResult dispatchTarget(Context context, String moduleId, String action,
                                         boolean legacyDaily) {
        if (context == null) {
            return new DispatchResult(false, ScheduleExecution.ReasonCode.INVALID_TARGET, "");
        }
        String normalized = ScheduleTask.normalizeAction(action);
        if (normalized == null) {
            return new DispatchResult(false, ScheduleExecution.ReasonCode.UNSUPPORTED_ACTION, "");
        }
        boolean legacyPauseResume = legacyDaily && TargetGame.BATTLE_LOOP.equals(moduleId)
                && (ScheduleTask.ACTION_PAUSE.equals(normalized)
                || ScheduleTask.ACTION_RESUME.equals(normalized));
        boolean legacyStop = legacyDaily && ScheduleTask.ACTION_STOP.equals(normalized)
                && isLegacyStopTarget(moduleId);
        if (!isModuleId(moduleId) && !legacyPauseResume && !legacyStop) {
            return new DispatchResult(false, ScheduleExecution.ReasonCode.INVALID_TARGET, "");
        }
        if ((ScheduleTask.ACTION_PAUSE.equals(normalized)
                || ScheduleTask.ACTION_RESUME.equals(normalized)) && !legacyPauseResume) {
            return new DispatchResult(false, ScheduleExecution.ReasonCode.UNSUPPORTED_ACTION, "");
        }
        String selectedPackage = TargetGame.getSelectedPackage(context);
        if (ScheduleTask.ACTION_START.equals(normalized)
                && !TargetGame.supportsModule(moduleId, selectedPackage)) {
            return new DispatchResult(false, ScheduleExecution.ReasonCode.UNSUPPORTED_TARGET,
                    selectedPackage);
        }
        Intent request = intent(context, moduleId, normalized);
        if (request == null) {
            return new DispatchResult(false, ScheduleExecution.ReasonCode.UNSUPPORTED_ACTION,
                    selectedPackage);
        }
        try {
            ComponentName component = Build.VERSION.SDK_INT >= 26
                    ? context.startForegroundService(request) : context.startService(request);
            if (component == null) return new DispatchResult(false,
                    ScheduleExecution.ReasonCode.DISPATCH_REJECTED, selectedPackage);
            return new DispatchResult(true, ScheduleExecution.ReasonCode.NONE,
                    ScheduleTask.ACTION_START.equals(normalized) ? selectedPackage : "");
        } catch (RuntimeException ignored) {
            return new DispatchResult(false, ScheduleExecution.ReasonCode.DISPATCH_EXCEPTION,
                    selectedPackage);
        }
    }

    /** 定时启动时可选的目标（“全部停止”只能用于停止动作）。 */
    public static String[] startTargets() {
        return IDS.clone();
    }

    public static String[] stopTargets() {
        String[] targets = new String[IDS.length + 1];
        targets[0] = ScheduleTask.TARGET_ALL;
        System.arraycopy(IDS, 0, targets, 1, IDS.length);
        return targets;
    }

    /** 每个动作可用的目标。 */
    public static String[] targetsFor(String action) {
        String normalized = ScheduleTask.normalizeAction(action);
        if (ScheduleTask.ACTION_STOP.equals(normalized)) return stopTargets();
        if (ScheduleTask.ACTION_START.equals(normalized)) return startTargets();
        return new String[0];
    }

    /** Compatibility dispatcher; for multiple targets, true means every request was accepted. */
    public static boolean dispatch(Context context, String target, String action) {
        if (context == null) return false;
        boolean found = false;
        boolean allAccepted = true;
        String normalized = ScheduleTask.normalizeAction(action);
        if (normalized == null) return false;
        for (String id : targetIds(target)) {
            if (ScheduleTask.TARGET_ALL.equals(id)) {
                if (!ScheduleTask.ACTION_STOP.equals(normalized)) return false;
                for (String moduleId : IDS) {
                    found = true;
                    allAccepted &= fire(context, moduleId, ScheduleTask.ACTION_STOP);
                }
            } else {
                found = true;
                allAccepted &= fire(context, id, normalized);
            }
        }
        return found && allAccepted;
    }

    /** 把 “mounthold,starhunt” 或 “all” 拆成目标数组。 */
    public static String[] targetIds(String target) {
        if (target == null || target.isEmpty()) return new String[0];
        String[] parts = target.split(",");
        java.util.List<String> ids = new java.util.ArrayList<>();
        for (String part : parts) {
            String id = part.trim();
            if (!id.isEmpty()) ids.add(id);
        }
        return ids.toArray(new String[0]);
    }

    static Intent buildIntent(Context context, String moduleId, String action) {
        String normalized = ScheduleTask.normalizeAction(action);
        if (context == null || normalized == null) return null;
        return intent(context, moduleId, normalized);
    }

    static Intent intent(Context context, String moduleId, String action) {
        String normalized = ScheduleTask.normalizeAction(action);
        if (context == null || normalized == null) return null;
        if ((ScheduleTask.ACTION_PAUSE.equals(normalized)
                || ScheduleTask.ACTION_RESUME.equals(normalized))
                && !TargetGame.BATTLE_LOOP.equals(moduleId)) return null;
        boolean stop = ScheduleTask.ACTION_STOP.equals(normalized);
        switch (moduleId) {
            case TargetGame.MOUNT_HOLD:
                return new Intent(context, com.xnas.wpe.mounthold.MountHoldService.class)
                        .setAction(stop
                                ? com.xnas.wpe.mounthold.MountHoldService.ACTION_STOP
                                : com.xnas.wpe.mounthold.MountHoldService.ACTION_START);
            case TargetGame.BATTLE_LOOP:
                return new Intent(context, com.xnas.wpe.battleloop.BattleLoopService.class)
                        .setAction(ScheduleTask.ACTION_PAUSE.equals(normalized)
                                ? com.xnas.wpe.battleloop.BattleLoopService.PAUSE
                                : ScheduleTask.ACTION_RESUME.equals(normalized)
                                ? com.xnas.wpe.battleloop.BattleLoopService.RESUME
                                : stop ? com.xnas.wpe.battleloop.BattleLoopService.STOP
                                : com.xnas.wpe.battleloop.BattleLoopService.START);
            case TargetGame.BATTLE_PANEL:
                return new Intent(context, com.xnas.wpe.battlepanel.BattleOverlayService.class)
                        .setAction(stop
                                ? com.xnas.wpe.battlepanel.BattleOverlayService.ACTION_STOP
                                : com.xnas.wpe.battlepanel.BattleOverlayService.ACTION_START);
            case TargetGame.STARHUNT:
                if (stop) {
                    return new Intent(context, com.xnas.wpe.starhunt.StarHuntService.class)
                            .setAction(com.xnas.wpe.starhunt.StarHuntService.ACTION_STOP_RUN);
                }
                // 与页面「开始挂机」一致：带上已保存的星级选择，服务端才知道要找什么。
                Intent star = new Intent(context, com.xnas.wpe.starhunt.StarHuntService.class)
                        .setAction(com.xnas.wpe.starhunt.StarHuntService.ACTION_START_HANGUP)
                        .putExtra(com.xnas.wpe.starhunt.StarHuntService.EXTRA_LEVELS,
                                modulePreference(context, TargetGame.STARHUNT, "selected_levels_v1"));
                int starLevel = moduleIntPreference(context, TargetGame.STARHUNT, "selected_level", 0);
                if (starLevel > 0) star.putExtra(com.xnas.wpe.starhunt.StarHuntService.EXTRA_LEVEL, starLevel);
                return star;
            case TargetGame.BOSSHUNT:
                if (stop) {
                    return new Intent(context, com.xnas.wpe.bosshunt.BossHuntService.class)
                            .setAction(com.xnas.wpe.bosshunt.BossHuntService.ACTION_STOP_RUN);
                }
                // 与页面「开始巡回」一致：带上已保存的地图选择。
                return new Intent(context, com.xnas.wpe.bosshunt.BossHuntService.class)
                        .setAction(com.xnas.wpe.bosshunt.BossHuntService.ACTION_START_HANGUP)
                        .putExtra(com.xnas.wpe.bosshunt.BossHuntService.EXTRA_MAPS,
                                modulePreference(context, TargetGame.BOSSHUNT, "selected_maps_v1"));
            case TargetGame.JINCHAN:
                if (stop) {
                    return new Intent(context, com.xnas.wpe.jinchan.ConnectionService.class)
                            .setAction(com.xnas.wpe.jinchan.ConnectionService.ACTION_STOP);
                }
                return new Intent(context, com.xnas.wpe.jinchan.ConnectionService.class)
                        .setAction(com.xnas.wpe.jinchan.ConnectionService.ACTION_CONNECT)
                        .putExtra(com.xnas.wpe.jinchan.ConnectionService.EXTRA_PACKAGE,
                                TargetGame.getSelectedPackage(context));
            case TargetGame.ANLEI:
                return new Intent(context, com.xnas.wpe.anlei.AnleiService.class)
                        .setAction(stop
                                ? com.xnas.wpe.anlei.AnleiService.STOP
                                : com.xnas.wpe.anlei.AnleiService.START);
            default:
                return null;
        }
    }

    /** 读取某个模块自己保存的选择（各模块的 SharedPreferences 会被加上 module.<id>. 前缀）。 */
    private static String modulePreference(Context context, String moduleId, String key) {
        try {
            return context.getSharedPreferences("module." + moduleId + ".settings",
                    Context.MODE_PRIVATE).getString(key, "");
        } catch (RuntimeException ignored) {
            return "";
        }
    }

    /** 读取某个模块自己保存的整数选择（星级等）。 */
    private static int moduleIntPreference(Context context, String moduleId, String key, int fallback) {
        try {
            return context.getSharedPreferences("module." + moduleId + ".settings",
                    Context.MODE_PRIVATE).getInt(key, fallback);
        } catch (RuntimeException ignored) {
            return fallback;
        }
    }

    private static boolean fire(Context context, String moduleId, String action) {
        return dispatchTarget(context, moduleId, action).accepted;
    }
}
