package com.xnas.wpe.suite;

import java.util.regex.Pattern;

/**
 * 识别战斗 worker 直接写出的运行状态行（`running calls=N`）。
 *
 * <p>战斗快进 worker 在绑定游戏后把状态文件写成不带键名的单行
 * {@code running calls=N}，而战斗跳过 worker 写的是 {@code state=...}。
 * 只按 {@code state=} 解析会把正在运行的快进当成“旧任务未确认”，
 * 于是页面显示“暂未启动”、侧栏显示“未确认”。这里只做文本识别，
 * 不参与互斥门或清理判定。</p>
 */
public final class BattleWorkerStatus {
    /** 只接受 worker 实际写出的完整格式，避免把半截或其它文本当成运行中。 */
    private static final Pattern RUNNING = Pattern.compile("running calls=\\d+");

    private BattleWorkerStatus() { }

    public static boolean isRunning(String raw) {
        String line = firstLine(raw);
        return line != null && RUNNING.matcher(line).matches();
    }

    /** 把运行行归一成页面通用的 key=value 形式；不是运行行时返回 null。 */
    public static String normalizedRunning(String raw) {
        return isRunning(raw) ? "state=running\n" : null;
    }

    private static String firstLine(String raw) {
        if (raw == null) return null;
        for (String line : raw.split("\\r?\\n")) {
            String trimmed = line.trim();
            if (!trimmed.isEmpty()) return trimmed;
        }
        return null;
    }
}
