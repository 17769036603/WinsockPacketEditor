package com.xnas.wpe.suite;

import android.content.Context;
import android.content.SharedPreferences;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 全应用共享的地图表（id + 名称）。
 *
 * <p>数据来自 worker 从客户端 {@code data_MapInfo} 导出的清单，由自动BOSS 首次读取时写入；
 * 自动找星等其它模块共用同一份，避免各自维护。</p>
 */
public final class GameMapCatalog {
    public static final class MapInfo {
        public final int id;
        public final String name;
        private MapInfo(int id, String name) { this.id = id; this.name = name; }
    }

    private static final String PREFERENCES = "suite_map_catalog";
    private static final String KEY = "catalog";

    private static final List<MapInfo> DEFAULTS = Collections.unmodifiableList(Arrays.asList(
            new MapInfo(1010, "东海渔村"), new MapInfo(1002, "地府"),
            new MapInfo(1003, "北俱芦洲"), new MapInfo(1016, "御马监"),
            new MapInfo(1018, "宝象国")));

    private static volatile List<MapInfo> maps = DEFAULTS;

    private GameMapCatalog() {
    }

    public static List<MapInfo> maps() {
        return maps;
    }

    public static String nameOf(int id) {
        for (MapInfo map : maps) if (map.id == id) return map.name;
        return "";
    }

    public static boolean contains(int id) {
        for (MapInfo map : maps) if (map.id == id) return true;
        return false;
    }

    public static void load(Context context) {
        List<MapInfo> parsed = parse(preferences(context).getString(KEY, ""));
        if (parsed.size() > DEFAULTS.size()) maps = Collections.unmodifiableList(parsed);
    }

    /** 其它模块导出全量地图后写入共享存储。 */
    public static void store(Context context, String catalog) {
        List<MapInfo> parsed = parse(catalog);
        if (parsed.size() <= maps.size()) return;
        maps = Collections.unmodifiableList(parsed);
        preferences(context).edit().putString(KEY, catalog).apply();
    }

    private static SharedPreferences preferences(Context context) {
        Context application = context.getApplicationContext();
        return (application == null ? context : application)
                .getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE);
    }

    private static List<MapInfo> parse(String catalog) {
        List<MapInfo> result = new ArrayList<>();
        if (catalog == null || catalog.isEmpty()) return result;
        Map<Integer, String> unique = new LinkedHashMap<>();
        for (String part : catalog.split(",")) {
            int colon = part.indexOf(':');
            if (colon <= 0) continue;
            String name = part.substring(colon + 1).trim();
            if (name.isEmpty()) continue;
            int id;
            try {
                id = Integer.parseInt(part.substring(0, colon).trim());
            } catch (NumberFormatException error) {
                continue;
            }
            if (id <= 0 || unique.containsKey(id)) continue;
            unique.put(id, name);
        }
        for (Map.Entry<Integer, String> entry : unique.entrySet()) {
            result.add(new MapInfo(entry.getKey(), entry.getValue()));
        }
        return result;
    }
}
