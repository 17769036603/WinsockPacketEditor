package com.xnas.wpe.mobile;

import android.annotation.SuppressLint;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
import java.util.UUID;

/** Lightweight profile metadata only; complete snapshots live in SnapshotStore. */
public final class SyncStore {
    private static final String PREFS = "wpe_mobile_sync_v2";
    private static final String ENDPOINT = "endpoint";
    private static final String USERNAME = "username";
    private static final String PASSWORD = "passwordEncrypted";
    private static final String KEY_ALIAS = "wpe_mobile_sync_credentials";
    private static final String ACTIVE_PROFILE = "activeProfile";
    private static final String BALL_POS_X = "ballPosX";
    private static final String BALL_POS_Y = "ballPosY";
    private static final String LEGACY_CREDENTIAL_PREFS = "wpe_mobile_credentials_v1";

    private final SharedPreferences preferences;

    @SuppressLint("ApplySharedPref")
    public SyncStore(Context context) {
        preferences = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        // Older builds persisted an encrypted password.  Remove that store so
        // the current contract is true even after an in-place app upgrade.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            context.deleteSharedPreferences(LEGACY_CREDENTIAL_PREFS);
        } else {
            context.getSharedPreferences(LEGACY_CREDENTIAL_PREFS, Context.MODE_PRIVATE)
                    .edit().clear().commit();
        }
    }

    public String getEndpoint() {
        return preferences.getString(ENDPOINT, "");
    }

    public String getUsername() {
        return preferences.getString(USERNAME, "");
    }

    public String getPassword() {
        String encrypted = preferences.getString(PASSWORD, "");
        if (encrypted.isEmpty()) {
            return "";
        }
        try {
            return decrypt(encrypted);
        } catch (Exception ignored) {
            return "";
        }
    }

    public boolean hasCredentials() {
        return !getEndpoint().isEmpty() && !getUsername().isEmpty() && !getPassword().isEmpty();
    }

    public int ballPosX() {
        return preferences.getInt(BALL_POS_X, -1);
    }

    public int ballPosY() {
        return preferences.getInt(BALL_POS_Y, -1);
    }

    public void saveBallPosition(int x, int y) {
        preferences.edit()
                .putInt(BALL_POS_X, x)
                .putInt(BALL_POS_Y, y)
                .apply();
    }

    public void saveConnection(String endpoint, String username, String password) {
        SharedPreferences.Editor editor = preferences.edit()
                .putString(ENDPOINT, endpoint == null ? "" : endpoint)
                .putString(USERNAME, username == null ? "" : username);
        if (password == null || password.isEmpty()) {
            editor.remove(PASSWORD);
        } else {
            try {
                editor.putString(PASSWORD, encrypt(password));
            } catch (Exception ignored) {
                editor.remove(PASSWORD);
            }
        }
        editor.apply();
    }

    public void saveConnection(String endpoint, String username) {
        saveConnection(endpoint, username, getPassword());
    }

    public String activeProfileKey() {
        return preferences.getString(ACTIVE_PROFILE, "");
    }

    public ProfileState activateProfile(String endpoint, String username) {
        String key = SyncModels.profileKey(endpoint, username);
        preferences.edit()
                .putString(ENDPOINT, endpoint == null ? "" : endpoint)
                .putString(USERNAME, username == null ? "" : username)
                .putString(ACTIVE_PROFILE, key)
                .apply();
        // A profile switch must load that profile's own selections.  The
        // profile key already isolates the values; clearing them here would
        // make the UI choose the first preset and overwrite the saved choice.
        return readProfile(key, true);
    }

    public ProfileState readActiveProfile() {
        String key = activeProfileKey();
        return key.isEmpty() ? new ProfileState("") : readProfile(key, true);
    }

    public ProfileState readProfile(String key, boolean preserveSelection) {
        String prefix = prefix(key);
        return new ProfileState(
                key,
                preferences.getString(prefix + "revision", ""),
                preferences.getString(prefix + "payloadSha256", ""),
                preferences.getString(prefix + "snapshotVersion", ""),
                preserveSelection ? parseUuid(preferences.getString(prefix + "selectedSend", "")) : null,
                preserveSelection ? parseUuid(preferences.getString(prefix + "selectedProgression", "")) : null,
                preserveSelection ? parseUuid(preferences.getString(prefix + "selectedAssistant", "")) : null,
                preferences.getLong(prefix + "lastSyncAt", 0L));
    }

    public void saveSnapshot(ProfileState state, String revision, String payloadSha256,
                             String snapshotVersion, long syncedAt) {
        String prefix = prefix(state.profileKey);
        preferences.edit().putString(prefix + "revision", revision == null ? "" : revision)
                .putString(prefix + "payloadSha256", payloadSha256 == null ? "" : payloadSha256)
                .putString(prefix + "snapshotVersion", snapshotVersion == null ? "" : snapshotVersion)
                .putLong(prefix + "lastSyncAt", syncedAt)
                .apply();
    }

    public void saveSelection(String profileKey, SyncModels.Module module, UUID id) {
        if (profileKey == null || profileKey.isEmpty()) {
            return;
        }
        preferences.edit().putString(prefix(profileKey) + selectionKey(module),
                id == null ? "" : id.toString()).apply();
    }

    public void clearSelection(String profileKey, SyncModels.Module module) {
        saveSelection(profileKey, module, null);
    }

    public static final class ProfileState {
        public final String profileKey;
        public final String revision;
        public final String payloadSha256;
        public final String snapshotVersion;
        public final UUID selectedSend;
        public final UUID selectedProgression;
        public final UUID selectedAssistant;
        public final long lastSyncAt;

        ProfileState(String profileKey) {
            this(profileKey, "", "", "", null, null, null, 0L);
        }

        ProfileState(String profileKey, String revision, String payloadSha256,
                     String snapshotVersion, UUID selectedSend, UUID selectedProgression,
                     UUID selectedAssistant, long lastSyncAt) {
            this.profileKey = profileKey;
            this.revision = revision;
            this.payloadSha256 = payloadSha256;
            this.snapshotVersion = snapshotVersion;
            this.selectedSend = selectedSend;
            this.selectedProgression = selectedProgression;
            this.selectedAssistant = selectedAssistant;
            this.lastSyncAt = lastSyncAt;
        }

        public UUID selected(SyncModels.Module module) {
            switch (module) {
                case PROGRESSION:
                    return selectedProgression;
                case ASSISTANT:
                    return selectedAssistant;
                default:
                    return selectedSend;
            }
        }
    }

    private static String prefix(String key) {
        return "profile." + key + ".";
    }

    private String encrypt(String value) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey());
        byte[] iv = cipher.getIV();
        byte[] encrypted = cipher.doFinal(value.getBytes(StandardCharsets.UTF_8));
        byte[] payload = new byte[iv.length + encrypted.length];
        System.arraycopy(iv, 0, payload, 0, iv.length);
        System.arraycopy(encrypted, 0, payload, iv.length, encrypted.length);
        return Base64.encodeToString(payload, Base64.NO_WRAP);
    }

    private String decrypt(String value) throws Exception {
        byte[] payload = Base64.decode(value, Base64.NO_WRAP);
        if (payload.length <= 12) {
            throw new IllegalArgumentException("Invalid encrypted credential.");
        }
        byte[] iv = new byte[12];
        byte[] encrypted = new byte[payload.length - iv.length];
        System.arraycopy(payload, 0, iv, 0, iv.length);
        System.arraycopy(payload, iv.length, encrypted, 0, encrypted.length);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, getOrCreateKey(), new GCMParameterSpec(128, iv));
        return new String(cipher.doFinal(encrypted), StandardCharsets.UTF_8);
    }

    private SecretKey getOrCreateKey() throws Exception {
        KeyStore keyStore = KeyStore.getInstance("AndroidKeyStore");
        keyStore.load(null);
        if (!keyStore.containsAlias(KEY_ALIAS)) {
            KeyGenerator generator = KeyGenerator.getInstance(
                    KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(
                    KEY_ALIAS,
                    KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setRandomizedEncryptionRequired(true)
                    .build());
            generator.generateKey();
        }
        KeyStore.Entry entry = keyStore.getEntry(KEY_ALIAS, null);
        return ((KeyStore.SecretKeyEntry) entry).getSecretKey();
    }

    private static String selectionKey(SyncModels.Module module) {
        switch (module) {
            case PROGRESSION:
                return "selectedProgression";
            case ASSISTANT:
                return "selectedAssistant";
            default:
                return "selectedSend";
        }
    }

    private static UUID parseUuid(String value) {
        try {
            return value == null || value.isEmpty() ? null : UUID.fromString(value);
        } catch (IllegalArgumentException ignored) {
            return null;
        }
    }
}
