package com.xnas.wpe.suite;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

/** Durable, fail-closed exclusion gate for the two Lua features that hook netconn.send. */
public final class BattleConflictGate {
    private static final String LOOP = "battleloop";
    private static final String SKIP = "battleskip";
    private static final String RESERVATION = ".battle-conflict.guard";

    private BattleConflictGate() { }

    public static final class Decision {
        public final boolean allowed;
        public final String blockedModule;
        public final String message;

        private Decision(boolean allowed, String blockedModule, String message) {
            this.allowed = allowed;
            this.blockedModule = blockedModule;
            this.message = message;
        }
    }

    public static synchronized Decision reserve(File modulesRoot, String moduleId) {
        if (!isManagedId(moduleId)) return denied(modulesRoot, moduleId, "未知功能，已阻止启动");
        File root = ensureDirectory(modulesRoot);
        if (root == null) return denied(modulesRoot, moduleId, "状态目录不可用，已安全阻止启动");
        File marker = new File(root, RESERVATION);

        if (marker.exists()) {
            String owner = read(marker).trim();
            if (!isManagedId(owner) || !isClean(root, owner)) {
                return denied(root, moduleId, guidance(owner));
            }
            if (!marker.delete()) return denied(root, moduleId, guidance(owner));
        }

        for (String candidate : new String[]{LOOP, SKIP}) {
            File module = new File(root, candidate);
            File status = new File(module, "status");
            File launcherStatus = new File(module, "launcher-status");
            boolean hasRunEvidence = status.exists() || (SKIP.equals(candidate) && launcherStatus.exists());
            if (hasRunEvidence && !isClean(root, candidate)) {
                return denied(root, moduleId, guidance(candidate));
            }
        }

        try {
            if (!marker.createNewFile()) return denied(root, moduleId, "另一项战斗功能正在启动或清理，已阻止启动");
            // The empty marker already excludes contenders. Publish its owner only
            // after starting replaces any clean status left by a previous run.
            File own = ensureDirectory(new File(root, moduleId));
            if (own == null) return denied(root, moduleId, "功能状态目录不可用，已保留启动保护");
            write(new File(own, "status"), "state=starting\n");
            write(marker, moduleId + "\n");
            new File(own, "gate-status").delete();
            if (SKIP.equals(moduleId)) new File(own, "launcher-status").delete();
            return new Decision(true, "", "已保留启动位");
        } catch (IOException error) {
            // Keep any created marker: uncertainty must continue to block the other feature.
            return denied(root, moduleId, "启动保护状态无法写入，已安全阻止启动");
        }
    }

    /** Releases a persisted reservation only after module-specific cleanup is proven. */
    public static synchronized boolean release(File modulesRoot, String moduleId) {
        if (!isManagedId(moduleId) || modulesRoot == null) return false;
        File marker = new File(modulesRoot, RESERVATION);
        if (!marker.isFile()) return isClean(modulesRoot, moduleId);
        if (!moduleId.equals(read(marker).trim()) || !isClean(modulesRoot, moduleId)) return false;
        return marker.delete();
    }

    /** Clears a reservation only when the launcher Activity itself failed before dispatch. */
    public static synchronized boolean abortBeforeDispatch(File modulesRoot, String moduleId) {
        if (!SKIP.equals(moduleId) || modulesRoot == null) return false;
        File marker = new File(modulesRoot, RESERVATION);
        File module = new File(modulesRoot, moduleId);
        if (!marker.isFile() || !module.isDirectory()
                || !moduleId.equals(read(marker).trim())
                || !"state=starting\n".equals(read(new File(module, "status")))) return false;
        try {
            write(new File(module, "status"),
                    "state=stopped\nhooksRemoved=true\nbindingCleared=true\nreason=activity_dispatch_failed\n");
            return marker.delete();
        } catch (IOException ignored) {
            return false;
        }
    }

    public static synchronized boolean isClean(File modulesRoot, String moduleId) {
        if (!isManagedId(moduleId) || modulesRoot == null) return false;
        File status = new File(new File(modulesRoot, moduleId), "status");
        if (!status.isFile()) return false;
        Map<String, String> fields = parse(read(status));
        if (!"stopped".equals(fields.get("state"))
                || !"true".equals(fields.get("hooksRemoved"))
                || !"true".equals(fields.get("bindingCleared"))) return false;
        return !LOOP.equals(moduleId) || "true".equals(fields.get("restoreConfirmed"));
    }

    /** True while a battle worker has run evidence without a confirmed clean terminal state. */
    public static synchronized boolean hasUnconfirmedActivity(File modulesRoot) {
        if (modulesRoot == null) return true;
        File marker = new File(modulesRoot, RESERVATION);
        if (marker.exists()) {
            String owner = read(marker).trim();
            if (!isManagedId(owner) || !isClean(modulesRoot, owner)) return true;
        }
        for (String candidate : new String[]{LOOP, SKIP}) {
            File module = new File(modulesRoot, candidate);
            boolean hasRunEvidence = new File(module, "status").exists()
                    || (SKIP.equals(candidate) && new File(module, "launcher-status").exists());
            if (hasRunEvidence && !isClean(modulesRoot, candidate)) return true;
        }
        return false;
    }

    private static Decision denied(File modulesRoot, String requester, String message) {
        if (isManagedId(requester)) {
            if (modulesRoot != null) {
                File own = ensureDirectory(new File(modulesRoot, requester));
                if (own != null) {
                    try { write(new File(own, "gate-status"), "state=conflict_wait\nmessage=" + message + "\n"); }
                    catch (IOException ignored) { }
                }
            }
        }
        return new Decision(false, "", message);
    }

    private static String guidance(String moduleId) {
        if (LOOP.equals(moduleId)) return "先停止战投跳过并等待清理完成；清理未确认时不能启动战斗快进。";
        if (SKIP.equals(moduleId)) return "先停止战斗快进并等待清理完成；清理未确认时不能启动战投跳过。";
        return "战投跳过/快进状态未确认；请先停止战投跳过/快进并等待清理完成。";
    }

    private static boolean isManagedId(String id) { return LOOP.equals(id) || SKIP.equals(id); }

    private static File ensureDirectory(File directory) {
        if (directory == null) return null;
        return directory.isDirectory() || directory.mkdirs() ? directory : null;
    }

    private static String read(File file) {
        try (FileInputStream input = new FileInputStream(file)) {
            byte[] bytes = new byte[(int) Math.min(file.length(), 65536)];
            int length = input.read(bytes);
            return length <= 0 ? "" : new String(bytes, 0, length, StandardCharsets.UTF_8);
        } catch (IOException ignored) { return ""; }
    }

    private static void write(File file, String value) throws IOException {
        try (FileOutputStream output = new FileOutputStream(file)) {
            output.write(value.getBytes(StandardCharsets.UTF_8));
            output.getFD().sync();
        }
    }

    private static Map<String, String> parse(String contents) {
        Map<String, String> values = new HashMap<>();
        for (String line : contents.split("\\r?\\n")) {
            int separator = line.indexOf('=');
            if (separator > 0) values.put(line.substring(0, separator), line.substring(separator + 1));
        }
        return values;
    }
}
