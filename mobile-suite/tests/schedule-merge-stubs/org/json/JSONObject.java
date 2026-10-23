package org.json;

import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;

public class JSONObject {
    private static final AtomicInteger NEXT = new AtomicInteger();
    private static final Map<String, Object> SNAPSHOTS = new HashMap<>();
    private final Map<String, Object> values = new LinkedHashMap<>();

    public JSONObject() { }

    public JSONObject put(String key, Object value) throws JSONException {
        if (key == null) throw new JSONException("null key");
        values.put(key, value);
        return this;
    }

    public JSONArray optJSONArray(String key) { Object value=values.get(key); return value instanceof JSONArray ? (JSONArray)value : null; }

    public boolean has(String key) { return values.containsKey(key); }

    public String optString(String key, String fallback) {
        Object value = values.get(key);
        return value == null ? fallback : String.valueOf(value);
    }

    public long optLong(String key, long fallback) {
        Object value = values.get(key);
        if (value instanceof Number) return ((Number) value).longValue();
        try { return value == null ? fallback : Long.parseLong(String.valueOf(value)); }
        catch (NumberFormatException ignored) { return fallback; }
    }

    public int optInt(String key, int fallback) {
        Object value = values.get(key);
        if (value instanceof Number) return ((Number) value).intValue();
        try { return value == null ? fallback : Integer.parseInt(String.valueOf(value)); }
        catch (NumberFormatException ignored) { return fallback; }
    }

    public boolean optBoolean(String key, boolean fallback) {
        Object value = values.get(key);
        return value instanceof Boolean ? (Boolean) value : fallback;
    }

    static Object snapshot(String token) { return SNAPSHOTS.get(token); }

    @Override public String toString() {
        String token = "@JSON_OBJECT_" + NEXT.incrementAndGet();
        SNAPSHOTS.put(token, this);
        return token;
    }
}
