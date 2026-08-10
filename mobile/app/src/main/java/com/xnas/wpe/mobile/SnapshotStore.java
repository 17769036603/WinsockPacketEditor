package com.xnas.wpe.mobile;

import android.content.Context;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.UUID;

/** Versioned private-file storage for complete snapshots. */
public final class SnapshotStore {
    // The canonical payload is limited to 8 MiB; the committed file also
    // carries the revision/hash/size metadata envelope.
    private static final int MAX_STORED_BYTES =
            SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES + 64 * 1024;
    private final File root;

    public SnapshotStore(Context context) {
        root = new File(context.getFilesDir(), "snapshots");
    }

    public JSONObject read(String profileKey, String snapshotVersion) throws IOException {
        if (profileKey == null || snapshotVersion == null || snapshotVersion.isEmpty()) {
            return null;
        }
        File file = snapshotFile(profileKey, snapshotVersion);
        if (!file.isFile()) {
            File backup = backupFile(profileKey, snapshotVersion);
            if (backup.isFile() && backup.renameTo(file)) {
                // Recover a committed version after a process interruption between
                // the two filesystem renames below.
            } else {
                return null;
            }
        }
        byte[] bytes = readBytes(file);
        try {
            return new JSONObject(new String(bytes, StandardCharsets.UTF_8));
        } catch (JSONException ex) {
            throw new IOException("Snapshot JSON is invalid", ex);
        }
    }

    public void commit(String profileKey, String snapshotVersion, JSONObject raw,
                       String expectedSha256, int expectedBytes) throws IOException {
        if (profileKey == null || profileKey.isEmpty() ||
                snapshotVersion == null || snapshotVersion.isEmpty() || raw == null) {
            throw new IOException("Snapshot storage key is invalid");
        }
        String canonical;
        String json;
        JSONObject parsed;
        try {
            canonical = SyncModels.canonicalPayload(raw);
            byte[] canonicalBytes = canonical.getBytes(StandardCharsets.UTF_8);
            if (expectedBytes > 0 && canonicalBytes.length != expectedBytes) {
                throw new IOException("Snapshot byte size does not match the manifest");
            }
            String actualSha = SyncModels.sha256Hex(canonicalBytes);
            if (expectedSha256 != null && !expectedSha256.isEmpty() &&
                    !expectedSha256.equalsIgnoreCase(actualSha)) {
                throw new IOException("Snapshot hash does not match the manifest");
            }
            json = normalizedSnapshotJson(raw, canonical);
            parsed = new JSONObject(json);
        } catch (JSONException ex) {
            throw new IOException("Snapshot JSON is invalid", ex);
        } catch (SyncModels.SyncException ex) {
            throw new IOException("Snapshot canonicalization failed", ex);
        }
        byte[] bytes = json.getBytes(StandardCharsets.UTF_8);
        if (bytes.length > MAX_STORED_BYTES) {
            throw new IOException("Snapshot file is too large");
        }

        if (!root.exists() && !root.mkdirs()) {
            throw new IOException("Unable to create private snapshot directory");
        }
        File profileDirectory = new File(root, safe(profileKey));
        if (!profileDirectory.exists() && !profileDirectory.mkdirs()) {
            throw new IOException("Unable to create profile snapshot directory");
        }
        File target = snapshotFile(profileKey, snapshotVersion);
        File backup = backupFile(profileKey, snapshotVersion);
        File temporary = new File(profileDirectory,
                "." + safe(snapshotVersion) + "." + UUID.randomUUID() + ".tmp");
        try {
            try (FileOutputStream stream = new FileOutputStream(temporary);
                 BufferedOutputStream output = new BufferedOutputStream(stream)) {
                output.write(bytes);
                output.flush();
                stream.getFD().sync();
            }
            if (backup.exists() && !backup.delete()) {
                throw new IOException("Unable to clear the previous snapshot backup");
            }
            if (target.exists() && !target.renameTo(backup)) {
                throw new IOException("Unable to preserve the old snapshot");
            }
            if (!temporary.renameTo(target)) {
                if (backup.exists() && !target.exists()) {
                    backup.renameTo(target);
                }
                throw new IOException("Unable to atomically commit the snapshot");
            }
            if (backup.exists()) {
                backup.delete();
            }
            syncDirectory(profileDirectory);
        } finally {
            if (temporary.exists()) {
                // A failed transaction must leave the last committed version intact.
                temporary.delete();
            }
        }
    }

    private static String normalizedSnapshotJson(JSONObject raw, String canonical)
            throws JSONException {
        String revision = raw.optString("revision", raw.optString("Revision", ""));
        String payloadSha256 = raw.optString("payloadSha256", raw.optString("PayloadSha256", ""));
        int payloadBytes = raw.has("payloadBytes")
                ? raw.optInt("payloadBytes", 0) : raw.optInt("PayloadBytes", 0);
        String generatedAt = raw.optString("generatedAt", raw.optString("GeneratedAt", ""));
        return "{\"revision\":" + SyncModels.canonicalize(revision)
                + ",\"payloadSha256\":" + SyncModels.canonicalize(payloadSha256)
                + ",\"payloadBytes\":" + payloadBytes
                + ",\"generatedAt\":" + SyncModels.canonicalize(generatedAt)
                + "," + canonical.substring(1);
    }

    private File snapshotFile(String profileKey, String snapshotVersion) {
        return new File(new File(root, safe(profileKey)), safe(snapshotVersion) + ".json");
    }

    private File backupFile(String profileKey, String snapshotVersion) {
        return new File(new File(root, safe(profileKey)), "." + safe(snapshotVersion) + ".bak");
    }

    private static byte[] readBytes(File file) throws IOException {
        long length = file.length();
        if (length > MAX_STORED_BYTES) {
            throw new IOException("Snapshot file is too large");
        }
        byte[] result = new byte[(int) length];
        int offset = 0;
        try (BufferedInputStream input = new BufferedInputStream(new FileInputStream(file))) {
            while (offset < result.length) {
                int count = input.read(result, offset, result.length - offset);
                if (count < 0) {
                    break;
                }
                offset += count;
            }
        }
        if (offset != result.length) {
            throw new IOException("Snapshot file ended unexpectedly");
        }
        return result;
    }

    private static void syncDirectory(File directory) {
        // Android does not expose a portable directory fsync API. The file fsync above
        // is the important durability boundary; keeping this helper documents the gate.
        if (directory != null) {
            directory.setLastModified(System.currentTimeMillis());
        }
    }

    private static String safe(String value) {
        // Profile and snapshot keys are hashes.  Do not allow a tampered
        // preference value such as "." or ".." to escape the private root.
        String normalized = value.replaceAll("[^A-Za-z0-9_-]", "_");
        return normalized.isEmpty() ? "_" : normalized;
    }
}
