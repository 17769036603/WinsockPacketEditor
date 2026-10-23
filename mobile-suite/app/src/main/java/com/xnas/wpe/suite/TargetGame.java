package com.xnas.wpe.suite;

import android.content.Context;
import android.content.SharedPreferences;

/** One app-wide game package choice shared by every feature page and service. */
public final class TargetGame {
    public static final String DHXY_PACKAGE = "com.gdoo.dhxy";
    public static final String XXXY_PACKAGE = "com.gdoo.xxxy2026";

    public static final String ANLEI = "anlei";
    public static final String MOUNT_HOLD = "mounthold";
    public static final String BATTLE_LOOP = "battleloop";
    public static final String BATTLE_SKIP = "battleskip";
    public static final String JINCHAN = "jinchan";
    public static final String BATTLE_PANEL = "battlepanel";
    public static final String STARHUNT = "starhunt";
    public static final String BOSSHUNT = "bosshunt";

    private static final String PREFERENCES = "suite_target";
    private static final String KEY_PACKAGE = "game_package";

    private TargetGame() {
    }

    public static String getSelectedPackage(Context context) {
        if (isLocked()) return BuildConfig.PIAOMIAO_LOCKED_TARGET;
        String selected = preferences(context).getString(KEY_PACKAGE, DHXY_PACKAGE);
        return isKnownPackage(selected) ? selected : DHXY_PACKAGE;
    }

    public static boolean setSelectedPackage(Context context, String packageName) {
        if (isLocked()) return BuildConfig.PIAOMIAO_LOCKED_TARGET.equals(packageName);
        if (!isKnownPackage(packageName)) return false;
        preferences(context).edit().putString(KEY_PACKAGE, packageName).apply();
        return true;
    }

    /** 朋友版构建只允许一个目标游戏；普通构建返回 false。 */
    public static boolean isLocked() {
        return !BuildConfig.PIAOMIAO_LOCKED_TARGET.isEmpty();
    }

    public static String lockedPackage() {
        return BuildConfig.PIAOMIAO_LOCKED_TARGET;
    }

    public static boolean isKnownPackage(String packageName) {
        if (isLocked()) return BuildConfig.PIAOMIAO_LOCKED_TARGET.equals(packageName);
        return DHXY_PACKAGE.equals(packageName) || XXXY_PACKAGE.equals(packageName);
    }

    public static boolean supportsModule(String moduleId, String packageName) {
        if (!isKnownPackage(packageName) || moduleId == null) return false;
        if (ANLEI.equals(moduleId) || BATTLE_LOOP.equals(moduleId)
                || BATTLE_SKIP.equals(moduleId)) {
            return DHXY_PACKAGE.equals(packageName);
        }
        if (STARHUNT.equals(moduleId) || BOSSHUNT.equals(moduleId)) return DHXY_PACKAGE.equals(packageName);
        return MOUNT_HOLD.equals(moduleId) || JINCHAN.equals(moduleId)
                || BATTLE_PANEL.equals(moduleId);
    }

    private static SharedPreferences preferences(Context context) {
        Context appContext = context.getApplicationContext();
        return (appContext == null ? context : appContext)
                .getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE);
    }
}
