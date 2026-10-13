package com.xnas.wpe.suite;

/** A truthful, module-local status snapshot for the host sidebar. */
public final class ModuleStatus {
    public enum State {
        STOPPED,
        WAITING,
        RUNNING,
        STOPPING,
        ERROR,
        UNKNOWN
    }

    public final State state;
    public final String label;
    public final String target;
    public final String detail;
    public final String guidance;

    public ModuleStatus(State state, String label, String target, String detail, String guidance) {
        this.state = state == null ? State.UNKNOWN : state;
        this.label = safe(label, "状态未确认");
        this.target = safe(target, "目标未确认");
        this.detail = safe(detail, "");
        this.guidance = safe(guidance, "");
    }

    public static ModuleStatus unknown(String target, String detail) {
        return new ModuleStatus(State.UNKNOWN, "状态未确认", target, detail,
                "打开本功能页查看实时状态；不要据此判断游戏已连接。");
    }

    private static String safe(String value, String fallback) {
        return value == null ? fallback : value.trim();
    }
}
