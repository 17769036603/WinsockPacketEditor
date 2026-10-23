package com.xnas.wpe.battleloop;

import android.content.Context;
import android.system.ErrnoException;
import android.system.Os;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.Map;

/** Private settings and the startup snapshot shared by the battle-loop UI and service. */
final class BattleVisualOptions {
    static final String PREFERENCES_NAME = "settings";
    static final String SKIP_BATTLE_KEY = "skip_battle_enabled";
    static final String HIDE_BATTLE_KEY = "hide_battle_enabled";
    static final String CONFIG_FILE_NAME = "battle-options";
    static final String LEGACY_CONFIG_FILE_NAME = "hide-battle";
    static final String VISUAL_LOG_FILE_NAME = "visual.log";
    static final int MAX_VISUAL_LOG_BYTES = 64 * 1024;

    static final class Snapshot {
        final boolean skipBattle;
        final boolean hideBattle;

        Snapshot(boolean skipBattle, boolean hideBattle) {
            this.skipBattle = skipBattle;
            this.hideBattle = hideBattle;
        }

        boolean hasAnyModeEnabled() {
            return skipBattle || hideBattle;
        }
    }

    private BattleVisualOptions() {
    }

    static Snapshot readSettings(Context context) {
        Map<String, ?> values = context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)
                .getAll();
        Object skipValue = values.get(SKIP_BATTLE_KEY);
        Object hideValue = values.get(HIDE_BATTLE_KEY);
        boolean skipBattle = skipValue instanceof Boolean ? (Boolean) skipValue : true;
        boolean hideBattle = hideValue instanceof Boolean ? (Boolean) hideValue : false;
        return new Snapshot(skipBattle, hideBattle);
    }

    static boolean canEdit(Context context) {
        if (!com.xnas.wpe.suite.TargetGame.supportsModule(
                com.xnas.wpe.suite.TargetGame.BATTLE_LOOP,
                com.xnas.wpe.suite.TargetGame.getSelectedPackage(context))) return false;
        if (BattleLoopService.isActive() || BattleLoopService.isCheckingOldTask()) return false;
        File moduleDir = context.getFilesDir();
        File modulesRoot = moduleDir == null ? null : moduleDir.getParentFile();
        return !com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedLoopActivity(modulesRoot);
    }

    /** Atomically publish both values captured for the next worker run. */
    static void writeConfigAtomically(File moduleDir, Snapshot options) throws IOException {
        if (moduleDir == null) throw new IOException("BattleLoop module directory is unavailable");
        if (options == null) throw new IOException("BattleLoop options are unavailable");
        if (!moduleDir.isDirectory() && !moduleDir.mkdirs()) {
            throw new IOException("Could not create BattleLoop module directory");
        }

        File temporary = new File(moduleDir, CONFIG_FILE_NAME + ".tmp");
        File destination = new File(moduleDir, CONFIG_FILE_NAME);
        boolean renamed = false;
        try {
            try (FileOutputStream output = new FileOutputStream(temporary, false)) {
                String contents = "version=1\nskipBattle=" + (options.skipBattle ? "1" : "0")
                        + "\nhideBattle=" + (options.hideBattle ? "1" : "0") + "\n";
                output.write(contents.getBytes(StandardCharsets.UTF_8));
                output.getFD().sync();
            }
            try {
                Os.chmod(temporary.getAbsolutePath(), 0600);
                Os.rename(temporary.getAbsolutePath(), destination.getAbsolutePath());
                renamed = true;
            } catch (ErrnoException error) {
                throw new IOException("Could not atomically publish BattleLoop options", error);
            }
        } finally {
            if (!renamed && temporary.exists()) temporary.delete();
        }
    }
}
