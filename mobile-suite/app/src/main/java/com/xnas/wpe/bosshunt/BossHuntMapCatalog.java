package com.xnas.wpe.bosshunt;

import android.content.Context;
import android.content.SharedPreferences;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 可巡回地图列表。
 *
 * <p>默认只有内置的五张；首次读取时由 worker 从客户端地图表（{@code data_MapInfo}）
 * 导出全量列表写进 status 的 {@code mapCatalog} 字段，这里解析后落盘，
 * 页面上就能列出游戏里所有地图。</p>
 */
public final class BossHuntMapCatalog {
    public static final class MapInfo {
        public final int id;
        public final String name;
        private MapInfo(int id, String name) { this.id = id; this.name = name; }
    }

    private static final String PREFERENCES = "settings";
    private static final String KEY = "map_catalog_v1";

    private static final List<MapInfo> DEFAULTS = Collections.unmodifiableList(Arrays.asList(
            new MapInfo(1010, "东海渔村"), new MapInfo(1002, "地府"),
            new MapInfo(1003, "北俱芦洲"), new MapInfo(1016, "御马监"),
            new MapInfo(1018, "宝象国")));

    /** 当前可用地图；进程重启后由 {@link #load(Context)} 恢复成上次导出的全量列表。 */
    public static volatile List<MapInfo> MAPS = DEFAULTS;

    private BossHuntMapCatalog() {
    }

    public static void load(Context context) {
        String raw = preferences(context).getString(KEY, "");
        List<MapInfo> parsed = parse(raw);
        if (parsed.size() > DEFAULTS.size()) {
            MAPS = Collections.unmodifiableList(parsed);
            com.xnas.wpe.suite.GameMapCatalog.store(context, raw);
        }
    }

    /** worker 导出全量地图表后调用；只在变多时覆盖，避免把内置列表缩回去。 */
    public static void refresh(Context context, String catalog) {
        List<MapInfo> parsed = parse(catalog);
        if (parsed.size() <= MAPS.size()) return;
        MAPS = Collections.unmodifiableList(parsed);
        preferences(context).edit().putString(KEY, catalog).apply();
        // 同一份地图表也给其它模块（自动找星）用。
        com.xnas.wpe.suite.GameMapCatalog.store(context, catalog);
    }

    public static boolean contains(int id) {
        for (MapInfo map : MAPS) if (map.id == id) return true;
        return false;
    }

    public static String nameOf(int id) {
        for (MapInfo map : MAPS) if (map.id == id) return map.name;
        return "";
    }

    private static SharedPreferences preferences(Context context) {
        Context application = context.getApplicationContext();
        return (application == null ? context : application)
                .getSharedPreferences("module.bosshunt." + PREFERENCES, Context.MODE_PRIVATE);
    }

    private static List<MapInfo> parse(String catalog) {
        List<MapInfo> maps = new ArrayList<>();
        if (catalog == null || catalog.isEmpty()) return maps;
        Map<Integer, String> unique = new LinkedHashMap<>();
        for (String part : catalog.split(",")) {
            int colon = part.indexOf(':');
            if (colon <= 0) continue;
            String idText = part.substring(0, colon).trim();
            String name = part.substring(colon + 1).trim();
            if (name.isEmpty()) continue;
            int id;
            try {
                id = Integer.parseInt(idText);
            } catch (NumberFormatException error) {
                continue;
            }
            if (id <= 0 || unique.containsKey(id)) continue;
            unique.put(id, name);
        }
        for (Map.Entry<Integer, String> entry : unique.entrySet()) {
            maps.add(new MapInfo(entry.getKey(), entry.getValue()));
        }
        return maps;
    }
}
