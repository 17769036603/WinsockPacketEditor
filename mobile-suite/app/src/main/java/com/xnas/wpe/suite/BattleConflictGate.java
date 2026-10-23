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
            if (!isManagedId(owner) || !settled(root, owner)) {
                return denied(root, moduleId, blockerMessage(root, owner, moduleId));
            }
            if (!marker.delete()) return denied(root, moduleId, blockerMessage(root, owner, moduleId));
        }

        for (String candidate : new String[]{LOOP, SKIP}) {
            File module = new File(root, candidate);
            File status = new File(module, "status");
            File launcherStatus = new File(module, "launcher-status");
            boolean hasRunEvidence = status.exists() || (SKIP.equals(candidate) && launcherStatus.exists());
            if (hasRunEvidence && !settled(root, candidate)) {
                return denied(root, moduleId, blockerMessage(root, candidate, moduleId));
            }
        }

        try {
            if (!marker.createNewFile()) {
                String owner = read(marker).trim();
                return denied(root, moduleId, isManagedId(owner) ? guidance(owner, moduleId)
                        : "另一项战斗功能正在启动或清理，已阻止启动");
            }
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
        if (!marker.isFile()) {
            boolean ready = settled(modulesRoot, moduleId);
            if (ready) clearResolvedDenials(modulesRoot);
            return ready;
        }
        if (!moduleId.equals(read(marker).trim()) || !settled(modulesRoot, moduleId)) return false;
        boolean released = marker.delete();
        if (released) clearResolvedDenials(modulesRoot);
        return released;
    }

    private static void clearResolvedDenials(File modulesRoot) {
        if (!hasUnconfirmedActivity(modulesRoot)) {
            // These are denial messages, not worker state or cleanup evidence.
            new File(new File(modulesRoot, LOOP), "gate-status").delete();
            new File(new File(modulesRoot, SKIP), "gate-status").delete();
        }
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

    /**
     * 可以启动的两种结论：有清理证据（isClean），或核对确认运行锁空闲（isStartable）。
     * 后者不主张 hook 已移除，只表示没有 worker 在跑；游戏内脚本遇到旧 runId 会先还原。
     */
    public static synchronized boolean settled(File modulesRoot, String moduleId) {
        return isClean(modulesRoot, moduleId) || isStartable(modulesRoot, moduleId);
    }

    /**
     * 上一轮没有已确认的清理证据，但 worker 运行锁已核对为空闲：
     * 允许按当前游戏进程作为新会话启动。要求核对结论本身有效，且状态文件在核对之后
     * 没有再次变化（变了说明又有 worker 动过，旧结论作废）。
     */
    public static synchronized boolean isStartable(File modulesRoot, String moduleId) {
        if (!isManagedId(moduleId) || modulesRoot == null) return false;
        File module = new File(modulesRoot, moduleId);
        File statusFile = new File(module, "status");
        String rawStatus = read(statusFile);
        if (BattleWorkerStatus.isRunning(rawStatus)) return false;
        if ("running".equals(parse(rawStatus).get("state"))) return false;
        Map<String, String> verdict = parse(read(new File(module, "recovery-status")));
        if (!"startable".equals(verdict.get("state")) || !"free".equals(verdict.get("lockProbe"))) {
            return false;
        }
        long checkedAt = epochSeconds(verdict.get("checkedAt"));
        if (checkedAt <= 0) return false;
        return !statusFile.isFile() || statusFile.lastModified() / 1000L <= checkedAt;
    }

    private static long epochSeconds(String value) {
        if (value == null) return -1;
        try {
            return Long.parseLong(value.trim());
        } catch (NumberFormatException invalid) {
            return -1;
        }
    }

    public static synchronized boolean isClean(File modulesRoot, String moduleId) {
        if (!isManagedId(moduleId) || modulesRoot == null) return false;
        File status = new File(new File(modulesRoot, moduleId), "status");
        if (!status.isFile()) return false;
        Map<String, String> fields = parse(read(status));
        if (LOOP.equals(moduleId) && (fields.get("recoveryModule") == null
                || LOOP.equals(fields.get("recoveryModule"))) && verifiedProcessEnded(fields)) return true;
        if (SKIP.equals(moduleId) && SKIP.equals(fields.get("recoveryModule"))
                && verifiedProcessEnded(fields)) return true;
        if (!"stopped".equals(fields.get("state"))
                || !"true".equals(fields.get("hooksRemoved"))
                || !"true".equals(fields.get("bindingCleared"))) return false;
        return !LOOP.equals(moduleId) || ("true".equals(fields.get("restoreConfirmed"))
                && (!"true".equals(fields.get("hideBattleRequested"))
                    || "true".equals(fields.get("visibilityRestoreConfirmed"))));
    }

    /** True only when a persisted loop reservation or loop status still lacks terminal proof. */
    public static synchronized boolean hasUnconfirmedLoopActivity(File modulesRoot) {
        if (modulesRoot == null) return false;
        File loop = new File(modulesRoot, LOOP);
        File marker = new File(modulesRoot, RESERVATION);
        boolean hasRunEvidence = new File(loop, "status").exists()
                || (marker.isFile() && LOOP.equals(read(marker).trim()));
        return hasRunEvidence && !settled(modulesRoot, LOOP);
    }

    public static synchronized boolean hasUnconfirmedSkipActivity(File modulesRoot) {
        if (modulesRoot == null) return false;
        File skip = new File(modulesRoot, SKIP);
        File marker = new File(modulesRoot, RESERVATION);
        boolean hasRunEvidence = new File(skip, "status").exists()
                || new File(skip, "launcher-status").exists()
                || (marker.isFile() && SKIP.equals(read(marker).trim()));
        return hasRunEvidence && !settled(modulesRoot, SKIP);
    }

    private static boolean verifiedProcessEnded(Map<String, String> fields) {
        if (!"stopped".equals(fields.get("state"))
                || !"true".equals(fields.get("processEnded"))
                || !"true".equals(fields.get("recoveryVerified"))
                || !"com.gdoo.dhxy".equals(fields.get("package"))) return false;
        String proof = fields.get("recoveryProof");
        if ("no_target_process".equals(proof)) return true;
        if (!"original_process_ended".equals(proof)) return false;
        try {
            return Long.parseLong(fields.get("pid")) > 0
                    && Long.parseLong(fields.get("startTicks")) > 0;
        } catch (RuntimeException invalidIdentity) { return false; }
    }

    /** True while a battle worker has run evidence without a confirmed clean terminal state. */
    public static synchronized boolean hasUnconfirmedActivity(File modulesRoot) {
        if (modulesRoot == null) return true;
        File marker = new File(modulesRoot, RESERVATION);
        if (marker.exists()) {
            String owner = read(marker).trim();
            if (!isManagedId(owner) || !settled(modulesRoot, owner)) return true;
        }
        for (String candidate : new String[]{LOOP, SKIP}) {
            File module = new File(modulesRoot, candidate);
            boolean hasRunEvidence = new File(module, "status").exists()
                    || (SKIP.equals(candidate) && new File(module, "launcher-status").exists());
            if (hasRunEvidence && !settled(modulesRoot, candidate)) return true;
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

    /**
     * 被谁挡住就说清是谁：对方真的在运行/清理时不要提示用户去点“核查”，
     * 只有对方状态不明时才引导去做只读核查。
     */
    private static String blockerMessage(File root, String blocker, String requester) {
        if (!isManagedId(blocker)) return guidance(blocker, requester);
        String raw = read(new File(new File(root, blocker), "status"));
        String state = parse(raw).get("state");
        boolean busy = BattleWorkerStatus.isRunning(raw)
                || "running".equals(state) || "starting".equals(state) || "stopping".equals(state);
        if (busy && !blocker.equals(requester)) {
            return "「" + displayName(blocker) + "」正在运行或清理，请先停止它再启动「"
                    + displayName(requester) + "」；两项功能仍保持互斥。";
        }
        return guidance(blocker, requester);
    }

    private static String displayName(String moduleId) {
        return LOOP.equals(moduleId) ? "战斗跳过" : "战斗快进";
    }

    private static String guidance(String owner, String requester) {
        if (LOOP.equals(owner)) {
            if (SKIP.equals(requester)) {
                return "战斗跳过旧任务状态未确认，已阻止启动战斗快进；请到战斗跳过页面点击“启动”触发只读核查，核查完成后再启动战斗快进。两项功能仍保持互斥。";
            }
            return "战斗跳过旧任务状态未确认；再次点击“启动战斗跳过”会触发只读核查，核查完成并确认后再启动。战斗跳过与战斗快进仍保持互斥。";
        }
        if (SKIP.equals(owner)) {
            if (SKIP.equals(requester)) {
                return "战斗快进旧状态未确认退出，已阻止重新启动战斗快进；请停止战斗快进并等待清理确认后重试。战斗跳过仍保持互斥。";
            }
            return "战斗快进未确认退出，已阻止启动战斗跳过；请停止战斗快进并等待清理确认后重试。两项功能仍保持互斥。";
        }
        return "战斗跳过与战斗快进的状态未确认，已阻止当前启动请求；请等待状态明确后重试。";
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
