package com.xnas.wpe.battleskip;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.nio.charset.StandardCharsets;
import com.xnas.wpe.suite.BattleConflictGate;
import com.xnas.wpe.suite.BattleWorkerStatus;

public final class SkipStatusHarness {
    public static final class BattleSkipRecoveryService {
        public static boolean checking;
        public static boolean isChecking() { return checking; }
    }
    // Source-extracted production methods follow.
public static String read(File file) {
        try (FileInputStream input = new FileInputStream(file);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[2048];
            int count;
            while ((count = input.read(buffer)) > 0) output.write(buffer, 0, count);
            return new String(output.toByteArray(), StandardCharsets.UTF_8);
        } catch (Exception ignored) { return ""; }
    }
public static String currentStatus(File dir) {
        String actual = read(new File(dir, "status"));
        if (BattleSkipRecoveryService.isChecking()) return "state=checking";
        String running = BattleWorkerStatus.normalizedRunning(actual);
        if (running != null) return running;
        if (BattleConflictGate.isClean(dir.getParentFile(), "battleskip")) return actual;
        String recovery = read(new File(dir, "recovery-status"));
        if (!recovery.isEmpty()) return recovery;
        String gate = read(new File(dir, "gate-status"));
        return gate.isEmpty() ? actual : gate;
    }
public static String formatStatus(String raw, boolean clean) {
        if (raw.isEmpty()) return "状态：未启动";
        if (raw.contains("state=conflict_wait")) {
            for (String line : raw.split("\\r?\\n")) {
                if (line.startsWith("message=")) return "启动受阻：" + line.substring(8);
            }
            return "启动受阻：战斗快进与战斗跳过的运行或清理状态尚未确认";
        }
        for (String line : raw.split("\\r?\\n")) {
            if (line.startsWith("message=")) return "状态：" + line.substring("message=".length());
            if (line.startsWith("state=")) {
                String state = line.substring("state=".length());
                if ("running".equals(state)) return "状态：运行中";
                if ("starting".equals(state)) return "状态：已预留启动位，等待 worker";
                if ("stopping".equals(state)) return "状态：正在清理；未确认前不会释放战斗功能启动位";
                if ("stopped".equals(state)) return clean ? "状态：已停止" : "状态：停止未确认";
                if ("checking".equals(state)) return "正在准备，请稍候。";
                if ("waiting_restart".equals(state)) return "请重启游戏后再点击启动。";
                if ("unconfirmed".equals(state)) return "无法确认上次任务是否退出，暂未启动。";
                return "状态：" + state;
            }
        }
        return "状态：" + raw.trim();
    }
}