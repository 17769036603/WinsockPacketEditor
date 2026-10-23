package com.xnas.wpe.mounthold;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

public final class MountHoldRuntimeLogSelfTest {
    public static void main(String[] args) throws Exception {
        File directory = Files.createTempDirectory("mount-hold-log-test").toFile();
        File metricsDirectory = new File(directory, "metrics");
        try {
            MountHoldRuntimeLog log = new MountHoldRuntimeLog(directory, 180);
            for (int i = 0; i < 12; i++)
                log.append(String.format("event%02d", i), "session", "run", (long) i,
                        null, null, null, null, null, null, null);
            File[] files = directory.listFiles();
            if (files == null || files.length != 4) throw new AssertionError("rotation count " + Arrays.toString(files));
            List<String> lines = new ArrayList<>();
            for (int i = 3; i >= 1; i--) lines.addAll(read(new File(directory, "mount-hold.jsonl." + i)));
            lines.addAll(read(new File(directory, "mount-hold.jsonl")));
            List<String> events = new ArrayList<>();
            for (String line : lines) {
                if (!line.startsWith("{") || !line.endsWith("}") || line.contains("\r"))
                    throw new AssertionError("incomplete JSONL line: " + line);
                events.add(extractEvent(line));
            }
            List<String> expected = Arrays.asList("event08", "event09", "event10", "event11");
            if (!expected.equals(events)) throw new AssertionError("retained event order: " + events);
            if (!"RootDenied".equals(MountHoldRuntimeLog.safeDetail("ERROR", "RootDenied_dynamic message"))
                    || !"unknown_error".equals(MountHoldRuntimeLog.safeDetail("ERROR", "secret"))
                    || !"unknown".equals(MountHoldRuntimeLog.safeDetail("ACTIVE", "secret"))
                    || !"waiting_for_local_player".equals(MountHoldRuntimeLog.safeDetail("LOCATING", "waiting_for_local_player")))
                throw new AssertionError("detail redaction");
            MountHoldRuntimeLog metrics = new MountHoldRuntimeLog(metricsDirectory, 4096);
            metrics.appendRefreshSummary("session", "run", 30_000L,
                    300, 3, 1, 1, 12, 45, 8_000, 12_000, 29_000, 29_000);
            metrics.appendRefreshMetric("cache_fallback", "session", "run", 31_000L,
                    4_321, 0, false, "full_discovery", "mount_binding_changed");
            metrics.appendRefreshMetric("candidate_rebind", "session", "run", 32_500L,
                    1_500, 3, true, null, null);
            List<String> metricLines = read(new File(metricsDirectory, "mount-hold.jsonl"));
            if (metricLines.size() != 3
                    || !metricLines.get(0).contains("\"cacheHitCount\":300")
                    || !metricLines.get(0).contains("\"knownTableScanCount\":3")
                    || !metricLines.get(0).contains("\"fullDiscoveryCount\":1")
                    || !metricLines.get(0).contains("\"averageFullScanUs\":13250")
                    || !metricLines.get(0).contains("\"maxFullScanUs\":29000")
                    || !metricLines.get(1).contains("\"refreshElapsedUs\":4321")
                    || !metricLines.get(1).contains("\"fallbackPath\":\"full_discovery\"")
                    || !metricLines.get(1).contains("\"fallbackReason\":\"mount_binding_changed\"")
                    || !metricLines.get(1).contains("\"candidateFound\":false")
                    || !metricLines.get(2).contains("\"rebindElapsedMs\":1500")
                    || !metricLines.get(2).contains("\"scanAttempts\":3")
                    || !metricLines.get(2).contains("\"candidateFound\":true"))
                throw new AssertionError("refresh metric serialization: " + metricLines);
            metrics.appendRefreshMetric("cache_fallback", "session", "run", 33_000L,
                    1, 0, false, "full_discovery", "untrusted_detail");
            if (read(new File(metricsDirectory, "mount-hold.jsonl")).size() != 3)
                throw new AssertionError("unsafe fallback reason was logged");
            System.out.println("MountHoldRuntimeLog self-check passed");
        } finally {
            File metricsFile = new File(metricsDirectory, "mount-hold.jsonl");
            Files.deleteIfExists(metricsFile.toPath());
            Files.deleteIfExists(metricsDirectory.toPath());
            File[] files = directory.listFiles();
            if (files != null) for (File file : files) Files.deleteIfExists(file.toPath());
            Files.deleteIfExists(directory.toPath());
        }
    }

    private static String extractEvent(String line) {
        String key = "\"event\":\"";
        int start = line.indexOf(key);
        if (start < 0) throw new AssertionError("missing event field: " + line);
        start += key.length();
        int end = line.indexOf('"', start);
        if (end < 0) throw new AssertionError("unterminated event field: " + line);
        return line.substring(start, end);
    }

    private static List<String> read(File file) throws Exception {
        if (!file.isFile()) return new ArrayList<>();
        byte[] bytes = Files.readAllBytes(file.toPath());
        String text = new String(bytes, StandardCharsets.UTF_8);
        if (!text.endsWith("\n")) throw new AssertionError("missing final newline: " + file);
        return Arrays.asList(text.substring(0, text.length() - 1).split("\n"));
    }
}
