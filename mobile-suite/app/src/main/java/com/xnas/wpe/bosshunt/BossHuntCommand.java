package com.xnas.wpe.bosshunt;

import java.util.LinkedHashMap;
import java.util.Map;

/** Strict four-field command snapshot shared with the isolated boss-hunt worker. */
public final class BossHuntCommand {
    public static final long MAX_SAFE_SEQUENCE = 9_007_199_254_740_991L;
    public enum Mode { SCAN("scan"), RUN("run"), TELEPORT("teleport"); public final String value; Mode(String value) { this.value = value; }
        static Mode parse(String value) { for (Mode mode : values()) if (mode.value.equals(value)) return mode; throw new IllegalArgumentException("Unknown command mode"); } }
    public final long sequence;
    public final Mode mode;
    public final BossHuntSelection maps;
    /** Current-map snapshot used by RUN; zero for SCAN when unavailable. */
    public final int map;
    public BossHuntCommand(long sequence, Mode mode, BossHuntSelection maps, int map) {
        if (sequence <= 0 || sequence > MAX_SAFE_SEQUENCE) throw new IllegalArgumentException("sequence outside worker-safe range");
        if (mode == null || maps == null) throw new IllegalArgumentException("mode and maps are required");
        if (map < 0 || map > Integer.MAX_VALUE) throw new IllegalArgumentException("invalid current map");
        if ((mode == Mode.RUN || mode == Mode.TELEPORT) && (maps.isEmpty() || map <= 0)) throw new IllegalArgumentException("run requires maps and a current map");
        if (mode == Mode.TELEPORT && (maps.size() != 1 || maps.firstMap() != map)) throw new IllegalArgumentException("teleport requires exactly its target map");
        this.sequence = sequence; this.mode = mode; this.maps = BossHuntSelection.parseCsv(maps.toCsv()); this.map = map;
    }
    public String toFileContents() { return "sequence=" + sequence + "\nmode=" + mode.value + "\nmaps=" + maps.toCsv() + "\nmap=" + map + "\n"; }
    public static long nextSequence(long previous) { if (previous < 0 || previous >= MAX_SAFE_SEQUENCE) throw new IllegalStateException("Bosshunt command sequence exhausted"); return previous + 1; }
    public static BossHuntCommand parse(String contents) {
        if (contents == null) throw new IllegalArgumentException("command is missing");
        Map<String,String> fields = new LinkedHashMap<>();
        for (String line : contents.split("\\r?\\n", -1)) {
            if (line.isEmpty()) continue; int equals = line.indexOf('='); if (equals <= 0) throw new IllegalArgumentException("Malformed command line");
            String key = line.substring(0, equals); if (!"sequence".equals(key) && !"mode".equals(key) && !"maps".equals(key) && !"map".equals(key)) throw new IllegalArgumentException("Unknown command field: " + key);
            if (fields.put(key, line.substring(equals + 1)) != null) throw new IllegalArgumentException("Duplicate command field: " + key);
        }
        if (fields.size() != 4) throw new IllegalArgumentException("command must contain exactly sequence, mode, maps, and map");
        try { String s = required(fields,"sequence"), rawMap = required(fields,"map"); if (!s.matches("[0-9]+") || !rawMap.matches("[0-9]+")) throw new NumberFormatException();
            // SCAN 命令按设计不带地图（maps= 为空），不能把空值当成缺失字段，
            // 否则上一条扫描命令会让后续所有启动都报“command 文件无效”。
            String rawMaps = fields.get("maps"); if (rawMaps == null) throw new IllegalArgumentException("Missing command field: maps");
            return new BossHuntCommand(Long.parseLong(s), Mode.parse(required(fields,"mode")), BossHuntSelection.parseCsv(rawMaps), Integer.parseInt(rawMap));
        } catch (NumberFormatException error) { throw new IllegalArgumentException("Command contains an invalid number", error); }
    }
    private static String required(Map<String,String> fields, String key) { String value = fields.get(key); if (value == null || value.isEmpty()) throw new IllegalArgumentException("Missing command field: " + key); return value; }
}
