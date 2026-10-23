package com.xnas.wpe.starhunt;

/** Persistence rules for the versioned selected-level preference. */
public final class StarHuntSelectionStore {
    public static final String KEY = "selected_levels_v1";
    public static final String LEGACY_KEY = "selected_level";

    public interface Settings {
        boolean contains(String key);
        String getString(String key, String fallback);
        int getInt(String key, int fallback);
        void putString(String key, String value);
    }

    private StarHuntSelectionStore() {
    }

    /** Loads a versioned value, or migrates the one-time scalar/default value and records it. */
    public static StarHuntSelection load(Settings settings) {
        if (settings == null) throw new IllegalArgumentException("settings are required");
        if (settings.contains(KEY)) {
            try {
                return StarHuntSelection.restore(true, settings.getString(KEY, null), false, 0);
            } catch (RuntimeException ignored) {
                return StarHuntSelection.empty();
            }
        }

        boolean hasLegacy = settings.contains(LEGACY_KEY);
        int legacyLevel = 0;
        if (hasLegacy) {
            try {
                legacyLevel = settings.getInt(LEGACY_KEY, 0);
            } catch (RuntimeException ignored) {
                // A malformed legacy value is history, but not a valid selection to migrate.
            }
        }
        StarHuntSelection migrated = StarHuntSelection.restore(false, null, hasLegacy, legacyLevel);
        settings.putString(KEY, migrated.toCsv());
        return migrated;
    }

    public static void save(Settings settings, StarHuntSelection selection) {
        if (settings == null) throw new IllegalArgumentException("settings are required");
        if (selection == null) throw new IllegalArgumentException("selection is required");
        settings.putString(KEY, selection.toCsv());
    }
}
