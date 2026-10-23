package com.xnas.wpe.starhunt;

import java.util.LinkedHashMap;
import java.util.Map;

/** Immutable command snapshot shared by the Android service and Rust worker. */
public final class StarHuntCommand {
    public static final long MAX_SAFE_SEQUENCE = 9_007_199_254_740_991L;
    public enum Mode {
        SCAN("scan"),
        RUN("run"),
        /** 一次性传送：只用 map 字段，levels 为空。 */
        TELEPORT("teleport");

        public final String value;

        Mode(String value) {
            this.value = value;
        }

        static Mode parse(String value) {
            for (Mode mode : values()) if (mode.value.equals(value)) return mode;
            throw new IllegalArgumentException("Unknown command mode");
        }
    }

    public final long sequence;
    public final Mode mode;
    /** Compatibility view: the first selected level, or zero for an empty selection. */
    public final int level;
    public final StarHuntSelection levels;
    public final int map;

    /** Compatibility constructor for legacy single-level callers. */
    public StarHuntCommand(long sequence, Mode mode, int level, int map) {
        this(sequence, mode, legacySelection(mode, level), map);
    }

    public StarHuntCommand(long sequence, Mode mode, StarHuntSelection levels, int map) {
        if (sequence <= 0 || sequence > MAX_SAFE_SEQUENCE) {
            throw new IllegalArgumentException("sequence is outside the worker-safe integer range");
        }
        if (mode == null) throw new IllegalArgumentException("mode is required");
        if (levels == null) throw new IllegalArgumentException("levels are required");
        if (map < 0) throw new IllegalArgumentException("map must not be negative");
        if (mode == Mode.RUN && (levels.isEmpty() || map == 0)) {
            throw new IllegalArgumentException("run mode requires selected levels and current map");
        }
        if (mode == Mode.TELEPORT && (map == 0 || !levels.isEmpty())) {
            throw new IllegalArgumentException("teleport mode requires a target map");
        }
        this.sequence = sequence;
        this.mode = mode;
        this.levels = StarHuntSelection.parseCsv(levels.toCsv());
        this.level = this.levels.firstLevel();
        this.map = map;
    }

    public String toFileContents() {
        return "sequence=" + sequence + "\n"
                + "mode=" + mode.value + "\n"
                + "levels=" + levels.toCsv() + "\n"
                + "map=" + map + "\n";
    }

    public static long nextSequence(long previousSequence) {
        if (previousSequence < 0L || previousSequence >= MAX_SAFE_SEQUENCE) {
            throw new IllegalStateException("Starhunt command sequence exhausted");
        }
        return previousSequence + 1L;
    }

    public static StarHuntCommand parse(String contents) {
        if (contents == null) throw new IllegalArgumentException("command is missing");
        Map<String, String> fields = new LinkedHashMap<>();
        String[] lines = contents.split("\\r?\\n", -1);
        for (String line : lines) {
            if (line.isEmpty()) continue;
            int equals = line.indexOf('=');
            if (equals <= 0) throw new IllegalArgumentException("Malformed command line");
            String key = line.substring(0, equals);
            if (!"sequence".equals(key) && !"mode".equals(key)
                    && !"level".equals(key) && !"levels".equals(key) && !"map".equals(key)) {
                throw new IllegalArgumentException("Unknown command field: " + key);
            }
            String value = line.substring(equals + 1);
            if (fields.put(key, value) != null) {
                throw new IllegalArgumentException("Duplicate command field: " + key);
            }
        }
        try {
            long sequence = parseLong(required(fields, "sequence"));
            Mode mode = Mode.parse(required(fields, "mode"));
            boolean hasLegacyLevel = fields.containsKey("level");
            boolean hasLevels = fields.containsKey("levels");
            if (hasLegacyLevel == hasLevels) {
                throw new IllegalArgumentException("command must contain exactly one of level or levels");
            }
            StarHuntSelection levels;
            if (hasLevels) {
                levels = StarHuntSelection.parseCsv(fields.get("levels"));
            } else {
                int legacyLevel = parseInt(required(fields, "level"));
                levels = legacySelection(mode, legacyLevel);
            }
            int map = parseInt(required(fields, "map"));
            return new StarHuntCommand(sequence, mode, levels, map);
        } catch (NumberFormatException error) {
            throw new IllegalArgumentException("Command contains an invalid number", error);
        }
    }

    private static StarHuntSelection legacySelection(Mode mode, int level) {
        if (mode == null) throw new IllegalArgumentException("mode is required");
        if (mode == Mode.SCAN && level == 0) return StarHuntSelection.empty();
        return StarHuntSelection.of(level);
    }

    private static String required(Map<String, String> fields, String key) {
        String value = fields.get(key);
        if (value == null || value.isEmpty()) throw new IllegalArgumentException("Missing command field: " + key);
        return value;
    }

    private static long parseLong(String value) {
        if (!value.matches("[0-9]+")) throw new NumberFormatException(value);
        return Long.parseLong(value);
    }

    private static int parseInt(String value) {
        if (!value.matches("[0-9]+")) throw new NumberFormatException(value);
        return Integer.parseInt(value);
    }
}
