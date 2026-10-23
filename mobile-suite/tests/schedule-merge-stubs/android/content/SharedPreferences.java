package android.content;

import java.util.HashMap;
import java.util.Map;

public interface SharedPreferences {
    String getString(String key, String fallback);
    Map<String, ?> getAll();
    Editor edit();

    interface Editor {
        Editor putString(String key, String value);
        Editor remove(String key);
        void apply();
    }

    final class Memory implements SharedPreferences {
        private final Map<String, String> values = new HashMap<>();

        @Override public String getString(String key, String fallback) {
            return values.containsKey(key) ? values.get(key) : fallback;
        }

        @Override public Map<String, ?> getAll() { return new HashMap<>(values); }

        @Override public Editor edit() {
            return new Editor() {
                private final Map<String, String> updates = new HashMap<>();
                @Override public Editor putString(String key, String value) {
                    updates.put(key, value);
                    return this;
                }
                @Override public Editor remove(String key) { updates.put(key,null); return this; }
                @Override public void apply() { for (Map.Entry<String,String> e:updates.entrySet()) { if(e.getValue()==null) values.remove(e.getKey()); else values.put(e.getKey(),e.getValue()); } }
            };
        }
    }
}
