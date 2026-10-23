package com.xnas.wpe.suite;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.TimeUnit;

public final class BattleConflictGateOfflineTest {
    private static int assertions;

    public static void main(String[] args) throws Exception {
        if (args.length > 0 && "worker".equals(args[0])) {
            runWorker(args);
            return;
        }
        Path root = new File(args[0]).toPath();
        Path classes = new File(args[1]).toPath();
        Path java = new File(args[2]).toPath();
        testModulePaths(root.resolve("paths"));
        testUnconfirmedStops(root.resolve("uncertain"));
        testConcurrentReservations(root.resolve("races"), classes, java);
        System.out.println("BattleConflictGateOfflineTest: PASS (" + assertions + " assertions)");
    }

    private static void testModulePaths(Path root) throws Exception {
        Class<?>[] types = {
            com.xnas.wpe.anlei.ModuleProbe.class,
            com.xnas.wpe.battleloop.ModuleProbe.class,
            com.xnas.wpe.battlepanel.ModuleProbe.class,
            com.xnas.wpe.battleskip.ModuleProbe.class,
            com.xnas.wpe.jinchan.ModuleProbe.class,
            com.xnas.wpe.mounthold.ModuleProbe.class
        };
        String[] ids = {"anlei", "battleloop", "battlepanel", "battleskip", "jinchan", "mounthold"};
        List<String> files = new ArrayList<>();
        List<String> caches = new ArrayList<>();
        for (int i = 0; i < types.length; i++) {
            equal(ids[i], ModulePaths.idFor(types[i]), "module package maps to its own stable id");
            File filesDir = ModulePaths.moduleDir(root.resolve("files").toFile(), ids[i]);
            File cacheDir = ModulePaths.moduleDir(root.resolve("cache").toFile(), ids[i]);
            File codeCache = ModulePaths.cacheChild(root.resolve("cache").toFile(), ids[i], "code_cache");
            files.add(filesDir.getCanonicalPath());
            caches.add(cacheDir.getCanonicalPath());
            equal(new File(cacheDir, "code_cache").getCanonicalPath(), codeCache.getCanonicalPath(), "code cache remains inside module cache");
            write(new File(filesDir, "owner.txt"), ids[i]);
        }
        distinct(files, "each module receives a distinct files directory");
        distinct(caches, "each module receives a distinct cache directory");
        for (int i = 0; i < ids.length; i++) {
            equal(ids[i], read(new File(files.get(i), "owner.txt")), "module file sentinel stays in its own directory");
        }
    }

    private static void testUnconfirmedStops(Path root) throws Exception {
        String[] modules = {"battleloop", "battleskip"};
        for (String module : modules) {
            String other = "battleloop".equals(module) ? "battleskip" : "battleloop";
            for (String missing : new String[]{"hooksRemoved", "bindingCleared"}) {
                File caseRoot = root.resolve(module + "-missing-" + missing).toFile();
                prepareUnconfirmed(caseRoot, module, missing);
                File marker = new File(caseRoot, ".battle-conflict.guard");
                write(marker, module + "\n");
                check(!BattleConflictGate.isClean(caseRoot, module), module + " with missing " + missing + " is not clean");
                check(!BattleConflictGate.release(caseRoot, module), module + " with missing " + missing + " cannot release reservation");
                check(marker.isFile(), module + " with missing " + missing + " keeps reservation marker");
                check(!BattleConflictGate.reserve(caseRoot, other).allowed, other + " is blocked by uncertain " + module + " cleanup");
            }
        }
        File loopRestore = root.resolve("battleloop-missing-restore").toFile();
        write(new File(loopRestore, "battleloop/status"), "state=stopped\nhooksRemoved=true\nbindingCleared=true\n");
        check(!BattleConflictGate.isClean(loopRestore, "battleloop"), "battleloop requires restore confirmation");
        check(!BattleConflictGate.reserve(loopRestore, "battleskip").allowed, "battleskip is blocked until battleloop restore is confirmed");
    }

    private static void prepareUnconfirmed(File root, String module, String missing) throws Exception {
        String hooks = "hooksRemoved=true\n";
        String binding = "bindingCleared=true\n";
        String restore = "battleloop".equals(module) ? "restoreConfirmed=true\n" : "";
        if ("hooksRemoved".equals(missing)) hooks = "";
        if ("bindingCleared".equals(missing)) binding = "";
        write(new File(root, module + "/status"), "state=stopped\n" + hooks + binding + restore);
    }

    private static void testConcurrentReservations(Path root, Path classes, Path java) throws Exception {
        Files.createDirectories(root);
        for (int i = 0; i < 12; i++) {
            race(root.resolve("loop-first-" + i), "battleloop", "battleskip", classes, java);
            race(root.resolve("skip-first-" + i), "battleskip", "battleloop", classes, java);
        }
    }

    private static void race(Path caseRoot, String first, String second, Path classes, Path java) throws Exception {
        Files.createDirectories(caseRoot);
        File root = caseRoot.toFile();
        write(new File(root, "battleloop/status"), "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        write(new File(root, "battleskip/status"), "state=stopped\nhooksRemoved=true\nbindingCleared=true\n");
        File readyA = new File(root, "ready-a");
        File readyB = new File(root, "ready-b");
        File start = new File(root, "start");
        Process a = child(java, classes, root, first, readyA, start);
        Process b = child(java, classes, root, second, readyB, start);
        try {
            await(readyA, "first process ready");
            await(readyB, "second process ready");
            check(start.createNewFile(), "race start barrier created");
            boolean aDone = a.waitFor(15, TimeUnit.SECONDS);
            boolean bDone = b.waitFor(15, TimeUnit.SECONDS);
            check(aDone && bDone, "both racing processes exited");
            String outA = new String(a.getInputStream().readAllBytes(), StandardCharsets.UTF_8).trim();
            String outB = new String(b.getInputStream().readAllBytes(), StandardCharsets.UTF_8).trim();
            int wins = ("ALLOWED".equals(outA) ? 1 : 0) + ("ALLOWED".equals(outB) ? 1 : 0);
            int losses = ("DENIED".equals(outA) ? 1 : 0) + ("DENIED".equals(outB) ? 1 : 0);
            check(a.exitValue() == 0 && b.exitValue() == 0, "race child exit codes are zero: " + outA + "/" + outB);
            check(wins == 1 && losses == 1, "exactly one cross-process reservation wins: " + outA + "/" + outB);
            File marker = new File(root, ".battle-conflict.guard");
            check(marker.isFile(), "winning reservation persists a marker");
            String owner = read(marker).trim();
            check(owner.equals(first) || owner.equals(second), "marker owner is one of the two racing modules");
            File status = new File(new File(root, owner), "status");
            check(read(status).contains("state=starting"), "winning module is recorded as starting");
            assertions += 2;
        } finally {
            if (a.isAlive()) a.destroyForcibly();
            if (b.isAlive()) b.destroyForcibly();
        }
    }

    private static Process child(Path java, Path classes, File root, String module, File ready, File start) throws IOException {
        return new ProcessBuilder(java.toString(), "-cp", classes.toString(), BattleConflictGateOfflineTest.class.getName(),
                "worker", root.getAbsolutePath(), module, ready.getAbsolutePath(), start.getAbsolutePath())
                .redirectErrorStream(true).start();
    }

    private static void runWorker(String[] args) throws Exception {
        File root = new File(args[1]);
        File ready = new File(args[3]);
        File start = new File(args[4]);
        check(ready.createNewFile(), "child ready marker created");
        await(start, "race start signal");
        BattleConflictGate.Decision result = BattleConflictGate.reserve(root, args[2]);
        System.out.println(result.allowed ? "ALLOWED" : "DENIED");
    }

    private static void await(File file, String what) throws Exception {
        long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(10);
        while (!file.exists() && System.nanoTime() < deadline) Thread.sleep(5);
        check(file.exists(), "timed out waiting for " + what);
    }

    private static void distinct(List<String> values, String message) {
        check(values.size() == values.stream().distinct().count(), message + ": " + values);
    }

    private static void equal(String expected, String actual, String message) {
        check(expected.equals(actual), message + " (expected=" + expected + ", actual=" + actual + ")");
    }

    private static void check(boolean condition, String message) {
        assertions++;
        if (!condition) throw new AssertionError(message);
    }

    private static void write(File file, String value) throws IOException {
        File parent = file.getParentFile();
        if (parent != null) Files.createDirectories(parent.toPath());
        try (FileOutputStream out = new FileOutputStream(file)) {
            out.write(value.getBytes(StandardCharsets.UTF_8));
            out.getFD().sync();
        }
    }

    private static String read(File file) throws IOException {
        try (FileInputStream in = new FileInputStream(file)) {
            return new String(in.readAllBytes(), StandardCharsets.UTF_8);
        }
    }
}
