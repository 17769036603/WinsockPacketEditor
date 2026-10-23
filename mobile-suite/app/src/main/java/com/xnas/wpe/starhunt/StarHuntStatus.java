package com.xnas.wpe.starhunt;

import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.LinkedHashMap;

/** Parsed Lua/worker status. Missing values stay explicitly unavailable. */
public final class StarHuntStatus {
    private static final Set<String> STATES;

    static {
        Set<String> states = new HashSet<>();
        Collections.addAll(states, "reading", "waiting_role", "waiting_map", "waiting_targets",
                "teleport_wait", "challenge_wait", "battle", "cooldown", "paused",
                "unknown", "stopping", "stopped", "fatal");
        STATES = Collections.unmodifiableSet(states);
    }

    public static final class Level {
        public final int number;
        public final int count;
        public final String name;

        private Level(int number, int count, String name) {
            this.number = number;
            this.count = count;
            this.name = name == null ? "" : name;
        }
    }

    public final String state;
    public final int map;
    public final String mapName;
    public final String role;
    public final List<Level> levels;
    public final String selectedLevels;
    public final int selectedLevel;
    public final int targetId;
    public final String targetName;
    public final int targetX;
    public final int targetY;
    public final String reason;
    public final String mapCatalog;
    public final long updatedAt;
    public final long acceptedSequence;
    public final boolean hooksRemoved;
    public final boolean bindingCleared;
    public final boolean sessionDetached;
    public final boolean gameProcessGone;

    private StarHuntStatus(Map<String, String> fields) {
        String rawState = fields.get("status");
        state = STATES.contains(rawState) ? rawState : "unknown";
        map = number(fields.get("map"), 0);
        mapName = value(fields.get("mapName"));
        role = value(fields.get("role"));
        levels = Collections.unmodifiableList(parseLevels(fields.get("levels"), fields.get("levelNames")));
        StarHuntSelection selected = readSelectedLevels(fields);
        selectedLevels = selected.toCsv();
        selectedLevel = selected.isEmpty() ? number(fields.get("selectedLevel"), 0)
                : selected.firstLevel();
        targetId = number(fields.get("targetId"), 0);
        targetName = value(fields.get("targetName"));
        targetX = number(fields.get("targetX"), 0);
        targetY = number(fields.get("targetY"), 0);
        reason = value(fields.get("reason"));
        mapCatalog = value(fields.get("mapCatalog"));
        updatedAt = longNumber(fields.get("updatedAt"), 0L);
        acceptedSequence = longNumber(fields.get("acceptedSequence"), -1L);
        hooksRemoved = "true".equals(fields.get("hooksRemoved"));
        bindingCleared = "true".equals(fields.get("bindingCleared"));
        sessionDetached = "true".equals(fields.get("sessionDetached"));
        gameProcessGone = "true".equals(fields.get("gameProcessGone"))
                || (sessionDetached
                && "original_process_ended_cleanup_unconfirmed".equals(reason));
    }

    public static StarHuntStatus parse(String contents) {
        Map<String, String> fields = new LinkedHashMap<>();
        if (contents != null) {
            String[] lines = contents.split("\\r?\\n", -1);
            for (String line : lines) {
                if (line.isEmpty()) continue;
                int equals = line.indexOf('=');
                if (equals <= 0) continue;
                fields.put(line.substring(0, equals), line.substring(equals + 1));
            }
        }
        return new StarHuntStatus(fields);
    }

    public boolean hasLevel(int number) {
        for (Level level : levels) if (level.number == number) return true;
        return false;
    }

    public int countForLevel(int number) {
        for (Level level : levels) if (level.number == number) return level.count;
        return 0;
    }

    public boolean isFresh(long nowEpochSeconds, long maxAgeSeconds) {
        return updatedAt > 0L && updatedAt <= nowEpochSeconds + 5L
                && nowEpochSeconds - updatedAt <= maxAgeSeconds;
    }

    /** Unknown disables automatic actions by contract, but active request phases do not. */
    public boolean confirmsActionStopped(long sequence) {
        if (sequence <= 0L || acceptedSequence < sequence) return false;
        switch (state) {
            case "reading":
            case "waiting_role":
            case "waiting_map":
            case "waiting_targets":
            case "battle":
            case "cooldown":
            case "paused":
            case "unknown":
                return true;
            default:
                return false;
        }
    }

    /** A stopped process is safe only after both cleanup facts are present. */
    public boolean confirmsCleanup() {
        return ("stopped".equals(state) || "fatal".equals(state))
                && hooksRemoved && bindingCleared;
    }

    /** A cleanup result is usable only when it acknowledges this read-stop command. */
    public boolean confirmsCleanupFor(long sequence) {
        return sequence > 0L && acceptedSequence >= sequence && confirmsCleanup();
    }

    /** Explicit worker evidence that the bound game process ended and the session detached. */
    public boolean confirmsGameProcessGone() {
        return "stopped".equals(state) && gameProcessGone;
    }

    /** A new read session must never reuse the previous action authorization. */
    public boolean permitsFreshRead() {
        return confirmsCleanup() || (confirmsGameProcessGone() && sessionDetached);
    }

    /** Rejects a stale process-gone status left by an earlier read session. */
    public boolean confirmsGameProcessGoneAfter(long requestedAtMillis) {
        return requestedAtMillis > 0L && updatedAt > 0L
                && updatedAt <= (Long.MAX_VALUE - 999L) / 1000L
                && updatedAt * 1000L + 999L >= requestedAtMillis
                && confirmsGameProcessGone();
    }

    public boolean canStartAction(int level, long nowEpochSeconds) {
        if (level < 1 || level > 999) return false;
        return canStartAction(StarHuntSelection.of(level), nowEpochSeconds);
    }

    /** Manual RUN can start when any selected level currently has a candidate. */
    public boolean canStartAction(StarHuntSelection selected, long nowEpochSeconds) {
        if (selected == null || selected.isEmpty() || !isFresh(nowEpochSeconds, 5L)
                || map <= 0 || role.isEmpty() || !hasAnyCandidate(selected)) return false;
        return "reading".equals(state) || "waiting_targets".equals(state)
                || "paused".equals(state) || "unknown".equals(state);
    }

    /** Hangup may wait for a star that is not present in the current scan. */
    public boolean canStartHangup(int level, long nowEpochSeconds) {
        if (level < 1 || level > 999) return false;
        return canStartHangup(StarHuntSelection.of(level), nowEpochSeconds);
    }

    /** The selected snapshot must be nonempty, but none of its levels needs a current candidate. */
    public boolean canStartHangup(StarHuntSelection selected, long nowEpochSeconds) {
        if (selected == null || selected.isEmpty() || !isFresh(nowEpochSeconds, 5L)
                || map <= 0 || role.isEmpty() || "unknown".equalsIgnoreCase(role.trim())
                || gameProcessGone) return false;
        return "reading".equals(state) || "waiting_targets".equals(state)
                || "paused".equals(state);
    }

    private boolean hasAnyCandidate(StarHuntSelection selected) {
        for (int level : selected.asList()) {
            if (hasLevel(level) && countForLevel(level) > 0) return true;
        }
        return false;
    }

    private static StarHuntSelection readSelectedLevels(Map<String, String> fields) {
        if (fields.containsKey("selectedLevels")) {
            try {
                return StarHuntSelection.parseCsv(fields.get("selectedLevels"));
            } catch (IllegalArgumentException ignored) {
                return StarHuntSelection.empty();
            }
        }
        int legacy = number(fields.get("selectedLevel"), 0);
        return legacy >= 1 && legacy <= 999 ? StarHuntSelection.of(legacy)
                : StarHuntSelection.empty();
    }

    private static List<Level> parseLevels(String rawLevels, String rawNames) {
        Map<Integer, Integer> counts = new TreeMap<>();
        if (rawLevels != null && !rawLevels.isEmpty()) {
            for (String item : rawLevels.split(",", -1)) {
                int colon = item.indexOf(':');
                if (colon <= 0) continue;
                int number = number(item.substring(0, colon), -1);
                int count = number(item.substring(colon + 1), -1);
                if (number < 1 || number > 999 || count < 0) continue;
                // A duplicate is malformed input; keep its first count rather than double-counting it.
                if (!counts.containsKey(number)) counts.put(number, count);
            }
        }
        Map<Integer, String> names = new LinkedHashMap<>();
        if (rawNames != null && !rawNames.isEmpty()) {
            for (String item : rawNames.split("\\|", -1)) {
                int colon = item.indexOf(':');
                if (colon <= 0 || colon == item.length() - 1) continue;
                int number = number(item.substring(0, colon), -1);
                if (number >= 1 && number <= 999 && counts.containsKey(number)
                        && !names.containsKey(number)) {
                    names.put(number, item.substring(colon + 1));
                }
            }
        }
        List<Level> result = new ArrayList<>();
        for (Map.Entry<Integer, Integer> entry : counts.entrySet()) {
            result.add(new Level(entry.getKey(), entry.getValue(), names.get(entry.getKey())));
        }
        return result;
    }

    private static String value(String value) {
        return value == null ? "" : value;
    }

    private static int number(String value, int fallback) {
        try {
            if (value == null || !value.matches("[0-9]+")) return fallback;
            return Integer.parseInt(value);
        } catch (NumberFormatException error) {
            return fallback;
        }
    }

    private static long longNumber(String value, long fallback) {
        try {
            if (value == null || !value.matches("[0-9]+")) return fallback;
            return Long.parseLong(value);
        } catch (NumberFormatException error) {
            return fallback;
        }
    }
}
