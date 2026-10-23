package org.json;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

public class JSONArray {
    private static final AtomicInteger NEXT = new AtomicInteger();
    private static final java.util.Map<String, Object> SNAPSHOTS = new java.util.HashMap<>();
    private final List<Object> values = new ArrayList<>();

    public JSONArray put(Object value) { values.add(value); return this; }
    public int length() { return values.size(); }
    public JSONObject optJSONObject(int index) {
        if (index < 0 || index >= values.size()) return null;
        Object value = values.get(index);
        return value instanceof JSONObject ? (JSONObject) value : null;
    }

    static Object snapshot(String token) { return SNAPSHOTS.get(token); }

    @Override public String toString() {
        String token = "@JSON_ARRAY_" + NEXT.incrementAndGet();
        SNAPSHOTS.put(token, this);
        return token;
    }
}
