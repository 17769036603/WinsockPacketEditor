package org.json;

public class JSONTokener {
    private final String raw;
    public JSONTokener(String raw) { this.raw = raw; }

    public Object nextValue() throws JSONException {
        if (raw == null) throw new JSONException("null JSON");
        if (raw.startsWith("@JSON_ARRAY_")) {
            Object value = JSONArray.snapshot(raw);
            if (value != null) return value;
        }
        if (raw.startsWith("@JSON_OBJECT_")) {
            Object value = JSONObject.snapshot(raw);
            if (value != null) return value;
        }
        throw new JSONException("invalid test JSON token");
    }
}
