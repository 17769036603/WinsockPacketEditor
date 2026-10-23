package com.xnas.wpe.suite;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/** Pure-Java evidence checks for the optional hidden-battle cleanup receipt. */
public final class GateHideBattleContract {
    private static int assertions;

    private static File root() throws Exception {
        return Files.createTempDirectory("hide-battle-gate-").toFile();
    }

    private static void status(File root, String fields) throws Exception {
        File dir = new File(root, "battleloop");
        if (!dir.mkdirs() && !dir.isDirectory()) throw new AssertionError("cannot create status directory");
        Files.write(new File(dir, "status").toPath(), fields.getBytes(StandardCharsets.UTF_8));
    }

    private static void expect(boolean expected, File root, String label) {
        boolean actual = BattleConflictGate.isClean(root, "battleloop");
        if (actual != expected) throw new AssertionError(label + ": expected " + expected + ", got " + actual);
        assertions++;
        System.out.println("PASS " + label);
    }

    public static void main(String[] args) throws Exception {
        String clean = "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n";

        File missing = root();
        status(missing, clean + "hideBattleRequested=true\n");
        expect(false, missing, "hide enabled without visibility receipt remains unclean");

        File failed = root();
        status(failed, clean + "hideBattleRequested=true\nvisibilityRestoreConfirmed=false\n");
        expect(false, failed, "hide enabled with failed visibility restore remains unclean");

        File restored = root();
        status(restored, clean + "hideBattleRequested=true\nvisibilityRestoreConfirmed=true\n");
        expect(true, restored, "hide enabled with confirmed visibility restore is clean");

        File legacy = root();
        status(legacy, clean);
        expect(true, legacy, "legacy status without hide request keeps prior clean behavior");

        File disabled = root();
        status(disabled, clean + "hideBattleRequested=false\nvisibilityRestoreConfirmed=false\n");
        expect(true, disabled, "hide disabled keeps prior clean behavior");

        System.out.println("GateHideBattleContract PASS assertions=" + assertions);
    }
}
