package com.xnas.wpe.suite;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

public final class UiStatusFilesSelfTest {
    private static int assertions;

    public static void main(String[] args) throws Exception {
        if (args.length != 1) throw new IllegalArgumentException("expected absolute taskRoot scratch path");
        File root = new File(args[0]).getAbsoluteFile();
        Files.createDirectories(root.toPath());

        coalescesRefreshesAndExpires(root);
        separatesModuleAndFileCaches(root);
        rejectsDamagedAndOversizedFiles(root);
        dropsPreviousTargetRead(root);
        System.out.println("UiStatusFilesSelfTest PASS (" + assertions + " assertions; "
                + UiStatusFiles.REFRESH_INTERVAL_MS + "ms refresh / "
                + UiStatusFiles.STALE_AFTER_MS + "ms hard stale)");
    }

    private static void coalescesRefreshesAndExpires(File root) throws Exception {
        UiStatusFiles.resetForTests();
        File file = new File(root, "coalesce/status");
        write(file, "state=running\n");
        CountDownLatch enteredRead = new CountDownLatch(1);
        CountDownLatch continueRead = new CountDownLatch(1);
        AtomicBoolean blockFirst = new AtomicBoolean(true);
        UiStatusFiles.setBeforeReadHookForTests(() -> {
            if (!blockFirst.getAndSet(false)) return;
            enteredRead.countDown();
            try {
                if (!continueRead.await(5L, TimeUnit.SECONDS)) throw new AssertionError("test read hook timed out");
            } catch (InterruptedException interrupted) {
                Thread.currentThread().interrupt();
                throw new AssertionError("test read hook interrupted", interrupted);
            }
        });
        UiStatusFiles.readForDisplay("anlei", file, "com.gdoo.dhxy");
        check(enteredRead.await(5L, TimeUnit.SECONDS), "initial read enters deterministic block");
        for (int i = 0; i < 100; i++) {
            UiStatusFiles.Snapshot pending = UiStatusFiles.readForDisplay("anlei", file, "com.gdoo.dhxy");
            check(!pending.known, "first display read remains unknown while background I/O is pending");
        }
        continueRead.countDown();
        check(UiStatusFiles.awaitIdleForTests(5000L), "initial coalesced read completes");
        UiStatusFiles.setBeforeReadHookForTests(null);
        UiStatusFiles.Snapshot first = UiStatusFiles.readForDisplay("anlei", file, "com.gdoo.dhxy");
        check(first.known && first.exists, "completed status file is known and present");
        eq("state=running\n", first.text, "status contents are preserved");
        eq(1L, UiStatusFiles.readCountForTests(), "100 same-file requests coalesce to one physical read");
        check(first.isFresh(), "successful snapshot is fresh after the initial read");
        System.out.println("UiStatusFiles metric: 101 same-file requests -> 1 physical read");

        sleep(UiStatusFiles.REFRESH_INTERVAL_MS + 40L);
        UiStatusFiles.Snapshot duringRefresh = UiStatusFiles.readForDisplay("anlei", file, "com.gdoo.dhxy");
        check(duringRefresh.known, "last complete snapshot remains displayable during refresh interval");
        check(UiStatusFiles.awaitIdleForTests(5000L), "interval refresh completes");
        eq(2L, UiStatusFiles.readCountForTests(), "one background refresh runs after the short refresh interval");
        for (int i = 0; i < 50; i++) {
            UiStatusFiles.readForDisplay("anlei", file, "com.gdoo.dhxy");
        }
        eq(2L, UiStatusFiles.readCountForTests(), "fresh snapshot avoids duplicate reads within refresh interval");

        sleep(UiStatusFiles.STALE_AFTER_MS + 40L);
        UiStatusFiles.Snapshot expired = UiStatusFiles.readForDisplay("anlei", file, "com.gdoo.dhxy");
        check(!expired.known && !expired.isFresh(), "hard-expired snapshot returns unknown");
        check(UiStatusFiles.awaitIdleForTests(5000L), "expired status is refreshed in the background");
        UiStatusFiles.Snapshot renewed = UiStatusFiles.readForDisplay("anlei", file, "com.gdoo.dhxy");
        check(renewed.known && renewed.isFresh(), "background refresh replaces the expired snapshot");
        eq(3L, UiStatusFiles.readCountForTests(), "hard expiration triggers exactly one read");
    }

    private static void separatesModuleAndFileCaches(File root) throws Exception {
        UiStatusFiles.resetForTests();
        File firstFile = new File(root, "isolation/first/status");
        File secondFile = new File(root, "isolation/second/status");
        write(firstFile, "state=first\n");
        write(secondFile, "state=second\n");

        UiStatusFiles.readForDisplay("battleloop", firstFile, "com.gdoo.dhxy");
        UiStatusFiles.readForDisplay("battleloop", secondFile, "com.gdoo.dhxy");
        UiStatusFiles.readForDisplay("battleskip", firstFile, "com.gdoo.dhxy");
        check(UiStatusFiles.awaitIdleForTests(5000L), "isolated files complete reads");

        UiStatusFiles.Snapshot first = UiStatusFiles.readForDisplay("battleloop", firstFile, "com.gdoo.dhxy");
        UiStatusFiles.Snapshot second = UiStatusFiles.readForDisplay("battleloop", secondFile, "com.gdoo.dhxy");
        UiStatusFiles.Snapshot otherModule = UiStatusFiles.readForDisplay("battleskip", firstFile, "com.gdoo.dhxy");
        eq("state=first\n", first.text, "same module retains the first file independently");
        eq("state=second\n", second.text, "same module retains the second file independently");
        eq("state=first\n", otherModule.text, "same file in another module has an independent cache entry");
        eq("battleloop", first.moduleId, "snapshot carries module identity");
        eq("com.gdoo.dhxy", first.targetKey, "snapshot carries target identity");
        check(first.readAtElapsedRealtimeMs > 0L, "snapshot exposes monotonic read time");
        eq(3L, UiStatusFiles.readCountForTests(), "module and file isolation causes exactly three reads");
        System.out.println("UiStatusFiles metric: 2 files in one module plus same file in another module -> 3 physical reads");
    }

    private static void rejectsDamagedAndOversizedFiles(File root) throws Exception {
        UiStatusFiles.resetForTests();
        File invalidUtf8 = new File(root, "bad/invalid-utf8");
        write(invalidUtf8, new byte[]{(byte) 0xc3, (byte) 0x28});
        UiStatusFiles.readForDisplay("starhunt", invalidUtf8, "com.gdoo.dhxy");
        check(UiStatusFiles.awaitIdleForTests(5000L), "invalid UTF-8 read completes");
        check(!UiStatusFiles.readForDisplay("starhunt", invalidUtf8, "com.gdoo.dhxy").known,
                "invalid UTF-8 file becomes unknown");

        UiStatusFiles.resetForTests();
        File empty = new File(root, "bad/empty");
        write(empty, new byte[0]);
        UiStatusFiles.readForDisplay("bosshunt", empty, "com.gdoo.dhxy");
        check(UiStatusFiles.awaitIdleForTests(5000L), "empty file read completes");
        check(!UiStatusFiles.readForDisplay("bosshunt", empty, "com.gdoo.dhxy").known,
                "empty present file is treated as damaged and unknown");

        UiStatusFiles.resetForTests();
        File oversized = new File(root, "bad/oversized");
        write(oversized, new byte[UiStatusFiles.MAX_STATUS_FILE_BYTES + 1]);
        UiStatusFiles.readForDisplay("battlepanel", oversized, "com.gdoo.dhxy");
        check(UiStatusFiles.awaitIdleForTests(5000L), "oversized file read completes");
        UiStatusFiles.Snapshot tooLarge = UiStatusFiles.readForDisplay("battlepanel", oversized, "com.gdoo.dhxy");
        check(!tooLarge.known, "over-limit file is unknown");
        eq(64 * 1024, UiStatusFiles.MAX_STATUS_FILE_BYTES, "status reader has a fixed 64 KiB bound");

        UiStatusFiles.resetForTests();
        File missing = new File(root, "missing/status");
        UiStatusFiles.readForDisplay("jinchan", missing, "com.gdoo.dhxy");
        check(UiStatusFiles.awaitIdleForTests(5000L), "missing file probe completes");
        UiStatusFiles.Snapshot absent = UiStatusFiles.readForDisplay("jinchan", missing, "com.gdoo.dhxy");
        check(absent.known && !absent.exists, "confirmed missing file is distinct from pending or damaged state");
    }

    private static void dropsPreviousTargetRead(File root) throws Exception {
        UiStatusFiles.resetForTests();
        File file = new File(root, "target/status");
        write(file, "state=old-target\n");
        CountDownLatch enteredRead = new CountDownLatch(1);
        CountDownLatch continueRead = new CountDownLatch(1);
        CountDownLatch enteredSecondRead = new CountDownLatch(1);
        CountDownLatch continueSecondRead = new CountDownLatch(1);
        AtomicInteger readIndex = new AtomicInteger();
        UiStatusFiles.setBeforeReadHookForTests(() -> {
            int index = readIndex.getAndIncrement();
            if (index > 1) return;
            CountDownLatch entered = index == 0 ? enteredRead : enteredSecondRead;
            CountDownLatch resume = index == 0 ? continueRead : continueSecondRead;
            entered.countDown();
            try {
                if (!resume.await(5L, TimeUnit.SECONDS)) throw new AssertionError("test read hook timed out");
            } catch (InterruptedException interrupted) {
                Thread.currentThread().interrupt();
                throw new AssertionError("test read hook interrupted", interrupted);
            }
        });

        UiStatusFiles.readForDisplay("starhunt", file, "target-A");
        check(enteredRead.await(5L, TimeUnit.SECONDS), "old-target read enters deterministic block");
        write(file, "state=new-target\n");
        UiStatusFiles.Snapshot switched = UiStatusFiles.readForDisplay("starhunt", file, "target-B");
        check(!switched.known && "target-B".equals(switched.targetKey),
                "target switch immediately invalidates old snapshot");
        continueRead.countDown();
        check(enteredSecondRead.await(5L, TimeUnit.SECONDS), "new-target read enters deterministic block");
        UiStatusFiles.Snapshot stillPending = UiStatusFiles.readForDisplay("starhunt", file, "target-B");
        check(!stillPending.known, "old-target result cannot satisfy a pending new-target snapshot");
        continueSecondRead.countDown();
        check(UiStatusFiles.awaitIdleForTests(5000L), "old and new target reads drain");
        UiStatusFiles.setBeforeReadHookForTests(null);

        UiStatusFiles.Snapshot current = UiStatusFiles.readForDisplay("starhunt", file, "target-B");
        check(current.known, "new target read completes successfully");
        eq("target-B", current.targetKey, "snapshot keeps the new target key");
        eq("state=new-target\n", current.text, "late old-target result cannot overwrite the new target");
        eq(2L, UiStatusFiles.readCountForTests(), "old and new target each cause one bounded read");
        System.out.println("UiStatusFiles metric: target switch -> 2 physical reads; old result discarded");
    }

    private static void write(File file, String text) throws Exception {
        write(file, text.getBytes(StandardCharsets.UTF_8));
    }

    private static void write(File file, byte[] bytes) throws Exception {
        Files.createDirectories(file.getParentFile().toPath());
        try (FileOutputStream output = new FileOutputStream(file)) {
            output.write(bytes);
        }
    }

    private static void sleep(long millis) throws InterruptedException {
        Thread.sleep(millis);
    }

    private static void check(boolean condition, String message) {
        assertions++;
        if (!condition) throw new AssertionError(message);
    }

    private static void eq(Object expected, Object actual, String message) {
        check(expected == null ? actual == null : expected.equals(actual),
                message + " expected=" + expected + " actual=" + actual);
    }
}
