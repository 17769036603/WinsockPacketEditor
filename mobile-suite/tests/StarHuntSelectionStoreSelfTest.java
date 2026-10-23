package com.xnas.wpe.starhunt;

import java.util.HashMap;
import java.util.Map;

public final class StarHuntSelectionStoreSelfTest {
    private static int checks;

    public static void main(String[] args) {
        FakeSettings migratedSettings = new FakeSettings();
        migratedSettings.values.put(StarHuntSelectionStore.LEGACY_KEY, 9);
        StarHuntSelection migrated = StarHuntSelectionStore.load(migratedSettings);
        eq("9", migrated.toCsv(), "valid legacy setting is migrated");
        eq("9", migratedSettings.values.get(StarHuntSelectionStore.KEY),
                "migration writes selected_levels_v1");
        migratedSettings.values.put(StarHuntSelectionStore.LEGACY_KEY, 12);
        eq("9", StarHuntSelectionStore.load(migratedSettings).toCsv(),
                "existing v1 value prevents a second legacy migration");

        FakeSettings noHistory = new FakeSettings();
        eq("1", StarHuntSelectionStore.load(noHistory).toCsv(),
                "fresh settings default to level one");
        eq("1", noHistory.values.get(StarHuntSelectionStore.KEY),
                "fresh default is persisted as the migration marker");

        FakeSettings explicitlyEmpty = new FakeSettings();
        explicitlyEmpty.values.put(StarHuntSelectionStore.KEY, "");
        explicitlyEmpty.values.put(StarHuntSelectionStore.LEGACY_KEY, 4);
        eq("", StarHuntSelectionStore.load(explicitlyEmpty).toCsv(),
                "explicit empty v1 selection does not use legacy data");
        eq(0, explicitlyEmpty.stringWrites, "valid v1 load is read-only");

        FakeSettings corrupt = new FakeSettings();
        corrupt.values.put(StarHuntSelectionStore.KEY, "9,8");
        corrupt.values.put(StarHuntSelectionStore.LEGACY_KEY, 5);
        eq("", StarHuntSelectionStore.load(corrupt).toCsv(),
                "corrupt v1 selection does not fall back to legacy data");
        eq("9,8", corrupt.values.get(StarHuntSelectionStore.KEY),
                "corrupt v1 data is not silently rewritten during read");
        eq(0, corrupt.stringWrites, "corrupt v1 load does not trigger migration writes");

        FakeSettings malformedLegacy = new FakeSettings();
        malformedLegacy.values.put(StarHuntSelectionStore.LEGACY_KEY, "wrong-type");
        eq("", StarHuntSelectionStore.load(malformedLegacy).toCsv(),
                "invalid legacy history becomes empty rather than a default");
        eq("", malformedLegacy.values.get(StarHuntSelectionStore.KEY),
                "invalid legacy history is marked migrated as explicit empty");

        FakeSettings saved = new FakeSettings();
        saved.values.put(StarHuntSelectionStore.LEGACY_KEY, 7);
        StarHuntSelectionStore.load(saved);
        StarHuntSelectionStore.save(saved, StarHuntSelection.of(10, 8, 10));
        eq("8,10", saved.values.get(StarHuntSelectionStore.KEY),
                "saving writes normalized ascending CSV");
        StarHuntSelectionStore.save(saved, StarHuntSelection.empty());
        eq("", saved.values.get(StarHuntSelectionStore.KEY),
                "saving an explicit clear persists the empty string");
        eq("", StarHuntSelectionStore.load(saved).toCsv(),
                "reopening after clear preserves the empty selection");

        FakeSettings wrongType = new FakeSettings();
        wrongType.values.put(StarHuntSelectionStore.KEY, 5);
        wrongType.values.put(StarHuntSelectionStore.LEGACY_KEY, 3);
        eq("", StarHuntSelectionStore.load(wrongType).toCsv(),
                "wrong type in present v1 preference fails closed");
        System.out.println("StarHuntSelectionStoreSelfTest PASS (" + checks + " assertions)");
    }

    private static final class FakeSettings implements StarHuntSelectionStore.Settings {
        final Map<String, Object> values = new HashMap<>();
        int stringWrites;

        @Override public boolean contains(String key) { return values.containsKey(key); }

        @Override public String getString(String key, String fallback) {
            Object value = values.get(key);
            if (value == null) return fallback;
            if (!(value instanceof String)) throw new ClassCastException("not a string");
            return (String) value;
        }

        @Override public int getInt(String key, int fallback) {
            Object value = values.get(key);
            if (value == null) return fallback;
            if (!(value instanceof Integer)) throw new ClassCastException("not an integer");
            return (Integer) value;
        }

        @Override public void putString(String key, String value) {
            stringWrites++;
            values.put(key, value);
        }
    }

    private static void eq(Object expected, Object actual, String message) {
        checks++;
        if (expected == null ? actual != null : !expected.equals(actual)) {
            throw new AssertionError(message + ": expected=" + expected + " actual=" + actual);
        }
    }
}
