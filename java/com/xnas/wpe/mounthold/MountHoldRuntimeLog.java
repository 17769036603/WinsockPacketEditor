package com.xnas.wpe.mounthold;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

/** Best-effort, bounded diagnostic log. Callers must never depend on logging succeeding. */
final class MountHoldRuntimeLog {
    static final long DEFAULT_MAX_BYTES = 1024L * 1024L;
    private static final String[] DETAILS = {
            "worker_start", "waiting_for_exact_package", "waiting_for_recovery_candidate",
            "recovering_old_journal", "old_journal_restored", "expired_journal_preserved",
            "exact_package_not_running", "retrying_transient_memory_read",
            "waiting_for_mounted_player", "waiting_for_local_player", "holding_speed", "retrying_hold_write_verification",
            "attempting_safe_restore", "speed_restored", "stopped_without_changes", "journal_retained"
    };
    private static final String[] CACHE_FALLBACK_REASONS = {
            "table_unavailable", "cached_key_unreadable", "cached_node_unreadable",
            "node_out_of_bounds",
            "hash_chain_corrupt", "key_missing_or_duplicate", "key_changed",
            "field_value_invalid", "local_player_binding_changed", "player_binding_changed",
            "mount_binding_changed", "coordinates_invalid", "speed_value_invalid"
    };

    private final File directory;
    private final long maxBytes;

    MountHoldRuntimeLog(File filesDir) { this(new File(filesDir, "logs"), DEFAULT_MAX_BYTES); }

    MountHoldRuntimeLog(File directory, long maxBytes) {
        this.directory = directory;
        this.maxBytes = Math.max(1L, maxBytes);
    }

    synchronized void append(String event, String sessionId, String runId, Long elapsedMs,
                             String state, String detail, Integer exitCode, Long retrySeconds,
                             String gamePackage, String targetSpeed, Long repeatCount) {
        try {
            StringBuilder json = recordHeader(event, sessionId, runId, elapsedMs);
            field(json, "state", state);
            field(json, "detail", safeDetail(state, detail));
            if (exitCode != null) json.append(",\"exitCode\":").append(exitCode);
            if (retrySeconds != null) json.append(",\"retrySeconds\":").append(retrySeconds);
            field(json, "gamePackage", gamePackage);
            field(json, "targetSpeed", targetSpeed);
            if (repeatCount != null) json.append(",\"repeatCount\":").append(repeatCount);
            appendJson(json);
        } catch (IOException | RuntimeException ignored) { }
    }

    synchronized void appendRefreshSummary(String sessionId, String runId, Long elapsedMs,
                                           long cacheHitCount, long knownTableScanCount,
                                           long fullDiscoveryCount, long cacheFallbackCount,
                                           long averageCacheHitUs, long maxCacheHitUs,
                                           long averageKnownTableScanUs, long maxKnownTableScanUs,
                                           long averageFullDiscoveryUs, long maxFullDiscoveryUs) {
        try {
            StringBuilder json = recordHeader("refresh_window_summary", sessionId, runId, elapsedMs);
            long knownScans = Math.max(0L, knownTableScanCount);
            long discoveries = Math.max(0L, fullDiscoveryCount);
            long fullScans = saturatingAdd(knownScans, discoveries);
            long totalKnownUs = saturatingMultiply(knownScans, Math.max(0L, averageKnownTableScanUs));
            long totalDiscoveryUs = saturatingMultiply(discoveries, Math.max(0L, averageFullDiscoveryUs));
            long averageFullScanUs = fullScans == 0L ? 0L
                    : saturatingAdd(totalKnownUs, totalDiscoveryUs) / fullScans;
            json.append(",\"cacheHitCount\":").append(Math.max(0L, cacheHitCount));
            json.append(",\"knownTableScanCount\":").append(knownScans);
            json.append(",\"fullDiscoveryCount\":").append(discoveries);
            json.append(",\"fullScanCount\":").append(fullScans);
            json.append(",\"cacheFallbackCount\":").append(Math.max(0L, cacheFallbackCount));
            json.append(",\"averageCacheHitUs\":").append(Math.max(0L, averageCacheHitUs));
            json.append(",\"maxCacheHitUs\":").append(Math.max(0L, maxCacheHitUs));
            json.append(",\"averageKnownTableScanUs\":").append(Math.max(0L, averageKnownTableScanUs));
            json.append(",\"maxKnownTableScanUs\":").append(Math.max(0L, maxKnownTableScanUs));
            json.append(",\"averageFullDiscoveryUs\":").append(Math.max(0L, averageFullDiscoveryUs));
            json.append(",\"maxFullDiscoveryUs\":").append(Math.max(0L, maxFullDiscoveryUs));
            json.append(",\"averageFullScanUs\":").append(averageFullScanUs);
            json.append(",\"maxFullScanUs\":").append(Math.max(
                    Math.max(0L, maxKnownTableScanUs), Math.max(0L, maxFullDiscoveryUs)));
            appendJson(json);
        } catch (IOException | RuntimeException ignored) { }
    }

    synchronized void appendRefreshMetric(String event, String sessionId, String runId,
                                          Long elapsedMs, long duration, long attempts,
                                          boolean candidateFound, String fallbackPath,
                                          String fallbackReason) {
        if (!"cache_fallback".equals(event) && !"candidate_rebind".equals(event)) return;
        if ("cache_fallback".equals(event)
                && (!isAllowedFallbackPath(fallbackPath) || !isAllowedFallbackReason(fallbackReason))) return;
        try {
            StringBuilder json = recordHeader(event, sessionId, runId, elapsedMs);
            if ("cache_fallback".equals(event)) {
                json.append(",\"refreshElapsedUs\":").append(Math.max(0L, duration));
                field(json, "fallbackPath", fallbackPath);
                field(json, "fallbackReason", fallbackReason);
            } else {
                json.append(",\"rebindElapsedMs\":").append(Math.max(0L, duration));
                json.append(",\"scanAttempts\":").append(Math.max(0L, attempts));
            }
            json.append(",\"candidateFound\":").append(candidateFound);
            appendJson(json);
        } catch (IOException | RuntimeException ignored) { }
    }

    static boolean isAllowedFallbackPath(String value) {
        return "known_table".equals(value) || "full_discovery".equals(value);
    }

    static boolean isAllowedFallbackReason(String value) {
        return contains(CACHE_FALLBACK_REASONS, value);
    }

    private static boolean contains(String[] values, String value) {
        for (String allowed : values) if (allowed.equals(value)) return true;
        return false;
    }

    private static long saturatingAdd(long left, long right) {
        if (right > 0L && left > Long.MAX_VALUE - right) return Long.MAX_VALUE;
        return left + right;
    }

    private static long saturatingMultiply(long left, long right) {
        if (left == 0L || right == 0L) return 0L;
        return left > Long.MAX_VALUE / right ? Long.MAX_VALUE : left * right;
    }

    private static StringBuilder recordHeader(String event, String sessionId, String runId,
                                              Long elapsedMs) {
        StringBuilder json = new StringBuilder("{\"schemaVersion\":1,\"timestampUtc\":\"")
                .append(utcNow()).append("\",\"event\":\"").append(escape(event)).append('"');
        field(json, "sessionId", sessionId);
        field(json, "runId", runId);
        if (elapsedMs != null) json.append(",\"workerElapsedMs\":").append(Math.max(0L, elapsedMs));
        return json;
    }

    private void appendJson(StringBuilder json) throws IOException {
        json.append("}\n");
        byte[] bytes = json.toString().getBytes(StandardCharsets.UTF_8);
        if (bytes.length > maxBytes) return;
        if (!directory.exists() && !directory.mkdirs() && !directory.isDirectory()) return;
        File current = new File(directory, "mount-hold.jsonl");
        if (current.exists() && current.length() + bytes.length > maxBytes) rotate(current);
        try (FileOutputStream output = new FileOutputStream(current, true)) { output.write(bytes); }
    }

    static String safeDetail(String state, String detail) {
        if ("ERROR".equals(state)) {
            if (detail == null) return "unknown_error";
            for (String kind : new String[]{"RootDenied", "ReadError", "RetryableMemoryRead", "Expired"})
                if (detail.startsWith(kind + "_")) return kind;
            return "unknown_error";
        }
        if (detail != null) for (String allowed : DETAILS) if (allowed.equals(detail)) return detail;
        return detail == null || detail.isEmpty() ? null : "unknown";
    }

    private void rotate(File current) throws IOException {
        File oldest = new File(directory, "mount-hold.jsonl.3");
        if (oldest.exists() && !oldest.delete()) throw new IOException("rotate delete failed");
        for (int i = 2; i >= 1; i--) {
            File from = new File(directory, "mount-hold.jsonl." + i);
            if (from.exists() && !from.renameTo(new File(directory, "mount-hold.jsonl." + (i + 1))))
                throw new IOException("rotate rename failed");
        }
        if (!current.renameTo(new File(directory, "mount-hold.jsonl.1")))
            throw new IOException("rotate current failed");
    }

    private static void field(StringBuilder json, String name, String value) {
        if (value != null) json.append(",\"").append(name).append("\":\"").append(escape(value)).append('"');
    }

    private static String escape(String value) {
        StringBuilder escaped = new StringBuilder();
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            if (c == '"' || c == '\\') escaped.append('\\').append(c);
            else if (c < 0x20) escaped.append(String.format(Locale.ROOT, "\\u%04x", (int) c));
            else escaped.append(c);
        }
        return escaped.toString();
    }

    private static String utcNow() {
        SimpleDateFormat format = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT);
        format.setTimeZone(TimeZone.getTimeZone("UTC"));
        return format.format(new Date());
    }
}
