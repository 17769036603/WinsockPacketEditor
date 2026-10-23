package com.xnas.wpe.starhunt;

import java.util.LinkedHashMap;
import java.util.Map;

/** Durable stop attempt state used to recover safely after service recreation. */
public final class StarHuntStopJournal {
    public enum Phase {
        PENDING("pending"),
        UNCONFIRMED("unconfirmed"),
        COMPLETE("complete");

        public final String value;

        Phase(String value) {
            this.value = value;
        }

        private static Phase parse(String value) {
            for (Phase phase : values()) if (phase.value.equals(value)) return phase;
            throw new IllegalArgumentException("Unknown stop phase");
        }
    }

    public final Phase phase;
    public final long sequence;
    public final long requestedAtMillis;
    public final boolean actionSlotHeld;

    public StarHuntStopJournal(Phase phase, long sequence, long requestedAtMillis,
                               boolean actionSlotHeld) {
        if (phase == null) throw new IllegalArgumentException("phase is required");
        if (sequence <= 0L) throw new IllegalArgumentException("sequence must be positive");
        if (requestedAtMillis <= 0L) throw new IllegalArgumentException("request time is required");
        this.phase = phase;
        this.sequence = sequence;
        this.requestedAtMillis = requestedAtMillis;
        this.actionSlotHeld = actionSlotHeld;
    }

    public static StarHuntStopJournal parse(String contents) {
        if (contents == null) throw new IllegalArgumentException("stop journal is missing");
        Map<String, String> fields = new LinkedHashMap<>();
        for (String line : contents.split("\\r?\\n", -1)) {
            if (line.isEmpty()) continue;
            int equals = line.indexOf('=');
            if (equals <= 0) throw new IllegalArgumentException("Malformed stop journal line");
            String key = line.substring(0, equals);
            if (!"phase".equals(key) && !"sequence".equals(key)
                    && !"requestedAtMillis".equals(key) && !"actionSlotHeld".equals(key)) {
                throw new IllegalArgumentException("Unknown stop journal field: " + key);
            }
            if (fields.put(key, line.substring(equals + 1)) != null) {
                throw new IllegalArgumentException("Duplicate stop journal field: " + key);
            }
        }
        try {
            return new StarHuntStopJournal(
                    Phase.parse(required(fields, "phase")),
                    parsePositive(required(fields, "sequence")),
                    parsePositive(required(fields, "requestedAtMillis")),
                    parseBoolean(required(fields, "actionSlotHeld")));
        } catch (NumberFormatException error) {
            throw new IllegalArgumentException("Invalid stop journal number", error);
        }
    }

    public boolean isExpired(long nowMillis, long timeoutMillis) {
        return phase == Phase.PENDING && timeoutMillis >= 0L
                && (nowMillis < requestedAtMillis
                || nowMillis - requestedAtMillis >= timeoutMillis);
    }

    public StarHuntStopJournal withPhase(Phase nextPhase, boolean actionHeld) {
        return new StarHuntStopJournal(nextPhase, sequence, requestedAtMillis, actionHeld);
    }

    public String toFileContents() {
        return "phase=" + phase.value + "\n"
                + "sequence=" + sequence + "\n"
                + "requestedAtMillis=" + requestedAtMillis + "\n"
                + "actionSlotHeld=" + actionSlotHeld + "\n";
    }

    private static String required(Map<String, String> fields, String key) {
        String value = fields.get(key);
        if (value == null || value.isEmpty()) throw new IllegalArgumentException("Missing " + key);
        return value;
    }

    private static long parsePositive(String value) {
        if (!value.matches("[0-9]+")) throw new NumberFormatException(value);
        long parsed = Long.parseLong(value);
        if (parsed <= 0L) throw new NumberFormatException(value);
        return parsed;
    }

    private static boolean parseBoolean(String value) {
        if ("true".equals(value)) return true;
        if ("false".equals(value)) return false;
        throw new IllegalArgumentException("Invalid actionSlotHeld value");
    }
}
