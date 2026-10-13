package com.xnas.wpe.anlei;

final class WorkerStatus {
    private WorkerStatus() { }

    static boolean isStopped(String raw) {
        if (raw == null) return false;
        String value = raw.trim();
        if (value.contains("error") || value.contains("fatal")
                || value.contains("cleanup_unconfirmed")) return false;
        return value.equals("stopped") || value.startsWith("stopped:")
                || value.startsWith("stopped ") || value.startsWith("已停止")
                || value.equals("state=stopped") || value.startsWith("state=stopped\n")
                || value.startsWith("state=stopped\r\n")
                || value.equals("app lease expired; worker stopped");
    }
}
