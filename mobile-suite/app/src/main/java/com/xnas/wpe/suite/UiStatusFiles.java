package com.xnas.wpe.suite;

import android.os.SystemClock;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ThreadFactory;
import java.util.concurrent.ThreadPoolExecutor;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicLong;

/**
 * Bounded, display-only cache for small module status files.
 *
 * <p>Callers must resolve the file and target key on the UI thread first. This class never performs
 * file I/O on the calling thread. Its snapshots must not be used for start/stop, mutual exclusion,
 * cleanup, authorization, or worker lifecycle decisions.</p>
 */
public final class UiStatusFiles {
    public static final long REFRESH_INTERVAL_MS = 500L;
    public static final long STALE_AFTER_MS = 5000L;
    public static final int MAX_STATUS_FILE_BYTES = 64 * 1024;

    private static final int MAX_MODULES = 8;
    private static final int MAX_FILES_PER_MODULE = 8;
    private static final int IO_QUEUE_CAPACITY = 32;
    private static final Object LOCK = new Object();
    private static final Map<String, ModuleCache> CACHES = new HashMap<>();
    private static final AtomicLong PHYSICAL_READ_COUNT = new AtomicLong();
    private static volatile Runnable beforeReadHookForTests;
    private static final Set<String> SUPPORTED_MODULES = supportedModules();
    private static final ThreadPoolExecutor IO = new ThreadPoolExecutor(
            1, 1, 0L, TimeUnit.MILLISECONDS, new ArrayBlockingQueue<>(IO_QUEUE_CAPACITY),
            new ThreadFactory() {
                @Override public Thread newThread(Runnable task) {
                    Thread thread = new Thread(task, "ui-status-files");
                    thread.setDaemon(true);
                    return thread;
                }
            }, new ThreadPoolExecutor.AbortPolicy());

    private UiStatusFiles() { }

    /**
     * Returns a fresh display snapshot, or an unknown snapshot while a background read is pending,
     * stale, rejected, or invalid. Repeated requests for the same module and file are coalesced.
     */
    public static Snapshot readForDisplay(String moduleId, File file, String targetKey) {
        if (moduleId == null || !SUPPORTED_MODULES.contains(moduleId)) {
            throw new IllegalArgumentException("Unsupported UI status module: " + moduleId);
        }
        if (file == null) throw new IllegalArgumentException("file is required");

        String normalizedTarget = targetKey == null ? "" : targetKey;
        String filePath = file.getAbsolutePath();
        final ModuleCache cache;
        final FileCache fileCache;
        final long generation;
        final long now = elapsedRealtimeNowMs();

        synchronized (LOCK) {
            ModuleCache existing = CACHES.get(moduleId);
            if (existing == null) {
                if (CACHES.size() >= MAX_MODULES) return Snapshot.unknown(moduleId, normalizedTarget, filePath);
                existing = new ModuleCache(normalizedTarget);
                CACHES.put(moduleId, existing);
            } else if (!existing.targetKey.equals(normalizedTarget)) {
                existing.targetKey = normalizedTarget;
                existing.generation++;
                existing.files.clear();
            }
            cache = existing;
            generation = cache.generation;

            FileCache current = cache.files.get(filePath);
            if (current == null) {
                if (cache.files.size() >= MAX_FILES_PER_MODULE) {
                    return Snapshot.unknown(moduleId, normalizedTarget, filePath);
                }
                current = new FileCache();
                cache.files.put(filePath, current);
            }
            fileCache = current;
            if (fileCache.snapshot != null) {
                long age = now - fileCache.snapshot.readAtElapsedRealtimeMs;
                if (fileCache.snapshot.known && isFresh(fileCache.snapshot, now)) {
                    if (age >= REFRESH_INTERVAL_MS && fileCache.pendingGeneration != generation) {
                        fileCache.pendingGeneration = generation;
                        scheduleRead(moduleId, normalizedTarget, filePath, file, cache, fileCache, generation);
                    }
                    return fileCache.snapshot;
                }
                if (!fileCache.snapshot.known && age >= 0L && age < REFRESH_INTERVAL_MS) {
                    return fileCache.snapshot;
                }
            }
            if (fileCache.pendingGeneration != generation) {
                fileCache.pendingGeneration = generation;
                scheduleRead(moduleId, normalizedTarget, filePath, file, cache, fileCache, generation);
            }
        }
        return Snapshot.unknown(moduleId, normalizedTarget, filePath);
    }

    private static void scheduleRead(String moduleId, String targetKey, String filePath, File file,
                                     ModuleCache cache, FileCache fileCache, long generation) {
        try {
            IO.execute(() -> {
                Snapshot result = readFile(moduleId, targetKey, filePath, file);
                synchronized (LOCK) {
                    ModuleCache current = CACHES.get(moduleId);
                    if (current != cache || current.generation != generation
                            || !current.targetKey.equals(targetKey)
                            || current.files.get(filePath) != fileCache) {
                        return;
                    }
                    fileCache.pendingGeneration = -1L;
                    fileCache.snapshot = result;
                }
            });
        } catch (RuntimeException rejected) {
            fileCache.pendingGeneration = -1L;
        }
    }

    private static Snapshot readFile(String moduleId, String targetKey, String filePath, File file) {
        PHYSICAL_READ_COUNT.incrementAndGet();
        try {
            Runnable testHook = beforeReadHookForTests;
            if (testHook != null) testHook.run();
            if (!file.isFile()) {
                return Snapshot.absent(moduleId, targetKey, filePath, elapsedRealtimeNowMs());
            }
            try (InputStream input = new FileInputStream(file);
                 ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                byte[] buffer = new byte[4096];
                int total = 0;
                int count;
                while ((count = input.read(buffer)) != -1) {
                    total += count;
                    if (total > MAX_STATUS_FILE_BYTES) {
                        return Snapshot.unknown(moduleId, targetKey, filePath, elapsedRealtimeNowMs());
                    }
                    output.write(buffer, 0, count);
                }
                if (total == 0) return Snapshot.unknown(moduleId, targetKey, filePath, elapsedRealtimeNowMs());
                String text = decodeUtf8(output.toByteArray());
                if (text.indexOf('\0') >= 0) return Snapshot.unknown(moduleId, targetKey, filePath, elapsedRealtimeNowMs());
                return Snapshot.present(moduleId, targetKey, filePath, text, elapsedRealtimeNowMs());
            }
        } catch (Exception ignored) {
            return Snapshot.unknown(moduleId, targetKey, filePath, elapsedRealtimeNowMs());
        }
    }

    private static String decodeUtf8(byte[] bytes) throws CharacterCodingException {
        return StandardCharsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(bytes)).toString();
    }

    private static boolean isFresh(Snapshot snapshot, long now) {
        return snapshot.readAtElapsedRealtimeMs > 0L
                && now >= snapshot.readAtElapsedRealtimeMs
                && now - snapshot.readAtElapsedRealtimeMs < STALE_AFTER_MS;
    }

    private static long elapsedRealtimeNowMs() {
        return SystemClock.elapsedRealtime();
    }

    private static Set<String> supportedModules() {
        Set<String> modules = new HashSet<>();
        modules.add("anlei");
        modules.add("battleloop");
        modules.add("battleskip");
        modules.add("mounthold");
        modules.add("starhunt");
        modules.add("bosshunt");
        modules.add("battlepanel");
        modules.add("jinchan");
        return modules;
    }

    private static final class ModuleCache {
        private String targetKey;
        private long generation;
        private final Map<String, FileCache> files = new HashMap<>();

        private ModuleCache(String targetKey) {
            this.targetKey = targetKey;
        }
    }

    private static final class FileCache {
        private Snapshot snapshot;
        private long pendingGeneration = -1L;
    }

    public static final class Snapshot {
        public final String moduleId;
        public final String targetKey;
        public final String filePath;
        public final String text;
        public final boolean known;
        public final boolean exists;
        public final long readAtElapsedRealtimeMs;

        private Snapshot(String moduleId, String targetKey, String filePath, String text,
                         boolean known, boolean exists, long readAtElapsedRealtimeMs) {
            this.moduleId = moduleId;
            this.targetKey = targetKey;
            this.filePath = filePath;
            this.text = text;
            this.known = known;
            this.exists = exists;
            this.readAtElapsedRealtimeMs = readAtElapsedRealtimeMs;
        }

        public boolean isFresh() {
            return known && UiStatusFiles.isFresh(this, elapsedRealtimeNowMs());
        }

        private static Snapshot present(String moduleId, String targetKey, String filePath,
                                        String text, long readAt) {
            return new Snapshot(moduleId, targetKey, filePath, text, true, true, readAt);
        }

        private static Snapshot absent(String moduleId, String targetKey, String filePath, long readAt) {
            return new Snapshot(moduleId, targetKey, filePath, "", true, false, readAt);
        }

        private static Snapshot unknown(String moduleId, String targetKey, String filePath) {
            return unknown(moduleId, targetKey, filePath, 0L);
        }

        private static Snapshot unknown(String moduleId, String targetKey, String filePath, long readAt) {
            return new Snapshot(moduleId, targetKey, filePath, "", false, false, readAt);
        }
    }

    // Package-private test hooks keep read-count assertions out of the app's public API.
    static long readCountForTests() {
        return PHYSICAL_READ_COUNT.get();
    }

    static void resetForTests() {
        awaitIdleForTests(5000L);
        beforeReadHookForTests = null;
        synchronized (LOCK) {
            CACHES.clear();
        }
        PHYSICAL_READ_COUNT.set(0L);
    }

    static void setBeforeReadHookForTests(Runnable hook) {
        beforeReadHookForTests = hook;
    }

    static boolean awaitIdleForTests(long timeoutMs) {
        CountDownLatch barrier = new CountDownLatch(1);
        try {
            IO.execute(barrier::countDown);
            return barrier.await(timeoutMs, TimeUnit.MILLISECONDS);
        } catch (Exception ignored) {
            return false;
        }
    }
}
