package com.xnas.wpe.starhunt;

import java.util.Collections;

public final class StarHuntSelectionSelfTest {
    private static int checks;

    public static void main(String[] args) {
        StarHuntSelection normalized = StarHuntSelection.of(10, 8, 9, 8);
        eq("8,9,10", normalized.toCsv(), "factory sorts and deduplicates UI selections");
        eq(8, normalized.firstLevel(), "first level is the compatibility display level");
        eq(3, normalized.size(), "selection exposes the unique level count");
        check(normalized.contains(9), "selection membership");
        eq("8,9,10,999", normalized.withAdded(999).toCsv(), "added levels remain sorted");
        eq("8,10", normalized.without(9).toCsv(), "removed levels leave a new selection");
        eq("8,9,10", normalized.toCsv(), "selection operations do not mutate their source");
        eq("", StarHuntSelection.empty().toCsv(), "empty selection serializes explicitly");
        eq("1", StarHuntSelection.restore(false, null, false, 0).toCsv(),
                "no history defaults to level one");
        eq("", StarHuntSelection.restore(true, "", true, 7).toCsv(),
                "present empty v1 value does not fall back to legacy selection");
        eq("", StarHuntSelection.restore(true, "9,8", true, 7).toCsv(),
                "corrupt v1 value does not fall back to legacy selection");
        eq("7", StarHuntSelection.restore(false, null, true, 7).toCsv(),
                "valid legacy selection migrates as a singleton");
        eq("", StarHuntSelection.restore(false, null, true, 1000).toCsv(),
                "invalid legacy history does not invent a default selection");
        eq("", StarHuntSelection.parseCsv("").toCsv(), "empty command CSV remains empty");
        eq("1,999", StarHuntSelection.parseCsv("1,999").toCsv(),
                "canonical minimum and maximum levels parse");
        rejects(() -> StarHuntSelection.parseCsv(null), "missing CSV is invalid");
        rejects(() -> StarHuntSelection.parseCsv("08"), "leading zero is not canonical CSV");
        rejects(() -> StarHuntSelection.parseCsv("8,08"), "leading-zero duplicate is invalid");
        rejects(() -> StarHuntSelection.parseCsv("9,8"), "CSV must be ascending");
        rejects(() -> StarHuntSelection.parseCsv("8,8"), "CSV cannot contain duplicates");
        rejects(() -> StarHuntSelection.parseCsv("1,,2"), "CSV cannot contain empty entries");
        rejects(() -> StarHuntSelection.parseCsv("０８"), "CSV accepts ASCII digits only");
        rejects(() -> StarHuntSelection.parseCsv("0"), "level zero is invalid");
        rejects(() -> StarHuntSelection.parseCsv("1000"), "level above 999 is invalid");
        rejects(() -> StarHuntSelection.of(0), "factory rejects level zero");
        rejects(() -> normalized.asList().add(11), "selection list is immutable");
        eq("", StarHuntSelection.from(Collections.<Integer>emptyList()).toCsv(),
                "empty collection creates an empty selection");
        System.out.println("StarHuntSelectionSelfTest PASS (" + checks + " assertions)");
    }

    private static void rejects(Runnable action, String message) {
        checks++;
        try {
            action.run();
        } catch (IllegalArgumentException expected) {
            return;
        } catch (UnsupportedOperationException expected) {
            return;
        }
        throw new AssertionError(message);
    }

    private static void check(boolean condition, String message) {
        checks++;
        if (!condition) throw new AssertionError(message);
    }

    private static void eq(Object expected, Object actual, String message) {
        checks++;
        if (expected == null ? actual != null : !expected.equals(actual)) {
            throw new AssertionError(message + ": expected=" + expected + " actual=" + actual);
        }
    }
}
