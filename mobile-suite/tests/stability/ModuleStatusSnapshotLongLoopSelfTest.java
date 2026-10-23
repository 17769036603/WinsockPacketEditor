package com.xnas.wpe.suite;

import java.util.Arrays;

/** Drives the production snapshot store through three simulated days without sleeping. */
public final class ModuleStatusSnapshotLongLoopSelfTest {
    private static final long BASE = 4_000_000L;
    private static final long STEP = 20L * 60L * 1000L;
    private static final String[] MODULES = ModuleStatusSnapshotStore.allowedModuleIds();
    private static final String[] TARGETS = {
            ModuleStatusSnapshotStore.TARGET_DHXY,
            ModuleStatusSnapshotStore.TARGET_XXXY
    };
    private static final ModuleStatus.State[] STATES = {
            ModuleStatus.State.STOPPED,
            ModuleStatus.State.WAITING,
            ModuleStatus.State.RUNNING,
            ModuleStatus.State.PAUSED,
            ModuleStatus.State.STOPPING,
            ModuleStatus.State.ERROR
    };
    private static int assertions;

    public static void main(String[] args) {
        ModuleStatusSnapshotStore store = new ModuleStatusSnapshotStore();
        int transitions = 0;

        // 216 twenty-minute advances exercise 72 hours of state publication and expiry.
        for (int tick = 0; tick < 216; tick++) {
            long now = BASE + (tick * STEP);
            for (int module = 0; module < MODULES.length; module++) {
                for (int target = 0; target < TARGETS.length; target++) {
                    ModuleStatus.State state = STATES[(tick + module * 2 + target * 3) % STATES.length];
                    check(store.publish(MODULES[module], TARGETS[target], state, now),
                            "transition accepted for " + MODULES[module] + "/" + target);
                    transitions++;
                    ModuleStatusSnapshotStore.Snapshot current = store.get(
                            MODULES[module], TARGETS[target], now);
                    equal(state, current.state, "fresh state matches its own module/target");
                    equal(ModuleStatusSnapshotStore.Freshness.FRESH, current.freshness,
                            "new transition remains fresh");
                }
            }

            // A delayed result from the previous logical tick must not roll back either target.
            if (tick > 0) {
                int module = tick % MODULES.length;
                String targetKey = TARGETS[tick % TARGETS.length];
                ModuleStatus.State before = store.get(MODULES[module], targetKey, now).state;
                check(!store.publish(MODULES[module], targetKey,
                                STATES[(tick + module + 1) % STATES.length], now - STEP),
                        "older observation rejected after logical time advances");
                equal(before, store.get(MODULES[module], targetKey, now).state,
                        "late observation cannot replace current state");
            }

            // One module-target pair is intentionally left unrefreshed across a 20-minute jump.
            if (tick % 24 == 0) {
                int module = tick % MODULES.length;
                String targetKey = TARGETS[(tick / 24) % TARGETS.length];
                ModuleStatusSnapshotStore.Snapshot stale = store.get(
                        MODULES[module], targetKey, now + STEP);
                equal(ModuleStatus.State.UNKNOWN, stale.state,
                        "stale state is hidden during extended inactivity");
                equal(ModuleStatusSnapshotStore.Freshness.STALE, stale.freshness,
                        "stale state carries explicit stale freshness");
                check(stale.hasSample, "stale state retains only its prior-read marker");
            }
        }

        String unknownModuleId = "not-a-module";
        check(!Arrays.asList(MODULES).contains(unknownModuleId),
                "unknown module fixture is not in the allowed module set");
        check(!store.publish(unknownModuleId, TARGETS[0], ModuleStatus.State.ERROR,
                        BASE + (216L * STEP)),
                "unlisted module identity cannot enter the snapshot store");
        check(!store.publish(MODULES[0], "com.example.injected", ModuleStatus.State.ERROR,
                        BASE + (216L * STEP)),
                "unknown target identity cannot enter the snapshot store");
        System.out.println("ModuleStatusSnapshotLongLoopSelfTest PASS (" + assertions
                + " assertions, " + transitions + " module-target transitions, 72 simulated hours)");
    }

    private static void check(boolean condition, String message) {
        assertions++;
        if (!condition) throw new AssertionError(message);
    }

    private static void equal(Object expected, Object actual, String message) {
        check(expected == null ? actual == null : expected.equals(actual),
                message + " expected=" + expected + " actual=" + actual);
    }
}
