package android.content;

import java.util.HashMap;
import java.util.Map;

public class Context {
    public static final int MODE_PRIVATE = 0;
    private final Map<String, SharedPreferences> preferences = new HashMap<>();

    public Context getApplicationContext() { return this; }

    public SharedPreferences getSharedPreferences(String name, int mode) {
        SharedPreferences found = preferences.get(name);
        if (found == null) {
            found = new SharedPreferences.Memory();
            preferences.put(name, found);
        }
        return found;
    }
}
