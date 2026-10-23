package com.xnas.wpe.bosshunt;

/** Persistence for the isolated boss-hunt map selection. */
public final class BossHuntSelectionStore {
    public static final String KEY = "selected_maps_v1";
    public interface Settings { boolean contains(String key); String getString(String key, String fallback); void putString(String key, String value); }
    private BossHuntSelectionStore() { }
    public static BossHuntSelection load(Settings settings) {
        if (settings == null) throw new IllegalArgumentException("settings are required");
        boolean present = settings.contains(KEY);
        String raw = present ? settings.getString(KEY, null) : null;
        BossHuntSelection result = BossHuntSelection.restore(present, raw);
        if (!present) settings.putString(KEY, "");
        return result;
    }
    public static void save(Settings settings, BossHuntSelection selection) {
        if (settings == null || selection == null) throw new IllegalArgumentException("settings and selection are required");
        settings.putString(KEY, selection.toCsv());
    }
}
