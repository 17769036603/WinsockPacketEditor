package com.xnas.wpe.suite;

import android.content.Context;
import org.json.JSONObject;

import java.util.Calendar;
import java.util.List;
import java.util.TimeZone;

/** Offline behavioral checks against the production task, store, and time calculation classes. */
public final class ScheduleMergeSelfTest {
    private static int checks;

    public static void main(String[] args) throws Exception {
        TimeZone.setDefault(TimeZone.getTimeZone("UTC"));
        legacyTargetPoliciesRemainVisibleAndBounded();
        recurringCountsAndLeadOffsets();
        dailyJsonAndUnknownActionsStaySafe();
        editDuplicateAndEnabledStateArePreserved();
        occurrenceCatchUpAndClockRollbackAreMonotonic();
        System.out.println("ScheduleMergeSelfTest PASS (" + checks + " assertions)");
    }

    private static void legacyTargetPoliciesRemainVisibleAndBounded() throws Exception {
        for (String target : new String[]{TargetGame.MOUNT_HOLD, TargetGame.BATTLE_PANEL,
                TargetGame.JINCHAN, TargetGame.BATTLE_SKIP}) {
            JSONObject raw=new JSONObject().put("id",99).put("hour",8).put("minute",5)
                    .put("action","start").put("target",target).put("enabled",true);
            ScheduleTask task=ScheduleTask.fromJson(raw);
            check(task!=null && !task.enabled && !task.isRunnableAction(), "retired target remains visible and disabled: "+target);
            eq(target,task.toJson().optString("target",""),"retired target round trip");
        }
        String[] legacy=ModuleActions.resolveTargets("all","stop",true);
        eq(7,legacy.length,"old all-stop retains all seven original modules");
        check(java.util.Arrays.asList(legacy).contains(TargetGame.MOUNT_HOLD),"legacy all-stop includes speed module");
        check(java.util.Arrays.asList(legacy).contains(TargetGame.BATTLE_LOOP),"legacy all-stop includes loop module");
        eq(3,ModuleActions.resolveTargets("all","stop",false).length,"new all-stop remains current three-module scope");
        check(ModuleActions.isScheduledTarget(TargetGame.BATTLE_LOOP,"stop",true),"legacy stop can dispatch loop");
        check(!ModuleActions.isScheduledTarget(TargetGame.BATTLE_LOOP,"stop",false),"new stop cannot widen to loop");
    }

    private static void recurringCountsAndLeadOffsets() {
        ScheduleTask hourly = new ScheduleTask(1, ScheduleTask.CYCLE_HOUR, 3,
                ScheduleTask.ACTION_START, TargetGame.ANLEI, true);
        ScheduleTask halfHourly = new ScheduleTask(2, ScheduleTask.CYCLE_HALF, 3,
                ScheduleTask.ACTION_START, TargetGame.ANLEI, true);
        ScheduleTask both = new ScheduleTask(3, ScheduleTask.CYCLE_HOUR | ScheduleTask.CYCLE_HALF, 3,
                ScheduleTask.ACTION_START, TargetGame.ANLEI, true);
        eq(24, hourly.triggerMinutes().length, "hourly-only tasks create 24 daily occurrences");
        eq(24, halfHourly.triggerMinutes().length, "half-hour-only tasks create 24 daily occurrences");
        eq(48, both.triggerMinutes().length, "combined tasks create 48 daily occurrences");
        check(contains(hourly.triggerMinutes(), 23 * 60 + 57),
                "hourly lead-three wraps its midnight occurrence to the prior day");
        eq(27, halfHourly.triggerMinutes()[0], "half-hour lead-three includes 00:27");
        eq(27, both.triggerMinutes()[0], "combined lead-three sequence is sorted from 00:27");
        check(strictlyIncreasing(hourly.triggerMinutes()), "hourly occurrence order is strict");
        check(strictlyIncreasing(halfHourly.triggerMinutes()), "half-hour occurrence order is strict");
        check(strictlyIncreasing(both.triggerMinutes()), "combined occurrence order is strict");

        for (int lead : new int[]{0, 3, 30}) {
            ScheduleTask all = new ScheduleTask(10 + lead,
                    ScheduleTask.CYCLE_HOUR | ScheduleTask.CYCLE_HALF, lead,
                    ScheduleTask.ACTION_START, TargetGame.ANLEI, true);
            eq(48, all.triggerMinutes().length, "lead " + lead + " retains all 48 occurrences");
            check(strictlyIncreasing(all.triggerMinutes()), "lead " + lead + " remains sorted");
        }
        eq(0, new ScheduleTask(50, ScheduleTask.CYCLE_HOUR | ScheduleTask.CYCLE_HALF, 30,
                ScheduleTask.ACTION_START, TargetGame.ANLEI, true).triggerMinutes()[0],
                "lead thirty includes the midnight half-hour occurrence");
    }

    private static void dailyJsonAndUnknownActionsStaySafe() throws Exception {
        JSONObject old = new JSONObject()
                .put("id", 41L)
                .put("hour", 9)
                .put("minute", 12)
                .put("cycles", ScheduleTask.CYCLE_HOUR | ScheduleTask.CYCLE_HALF)
                .put("lead", 3)
                .put("action", ScheduleTask.ACTION_START)
                .put("target", TargetGame.STARHUNT)
                .put("enabled", true);
        ScheduleTask migrated = ScheduleTask.fromJson(old);
        check(migrated != null && migrated.isLegacyDaily(),
                "legacy hour/minute JSON is interpreted as a daily fixed time");
        eq(9 * 60 + 12, migrated.triggerMinutes()[0], "old daily time is unchanged");
        ScheduleTask roundTrip = ScheduleTask.fromJson(migrated.toJson());
        check(roundTrip.isLegacyDaily(), "serialized legacy task keeps explicit legacy_daily type");
        eq(migrated.id, roundTrip.id, "daily roundtrip preserves task id");
        eq(migrated.hour, roundTrip.hour, "daily roundtrip preserves hour");
        eq(migrated.minute, roundTrip.minute, "daily roundtrip preserves minute");
        check(roundTrip.enabled, "daily roundtrip preserves enabled state");

        JSONObject unknownObject = new JSONObject()
                .put("id", 42L)
                .put("cycles", ScheduleTask.CYCLE_HOUR)
                .put("lead", 0)
                .put("action", "launch_whatever")
                .put("target", TargetGame.ANLEI)
                .put("enabled", true);
        ScheduleTask unknown = ScheduleTask.fromJson(unknownObject);
        check(unknown != null, "unknown action remains visible as a task row");
        check(!unknown.enabled && !unknown.isRunnableAction(),
                "unknown action is disabled and cannot execute");
        eq(null, ScheduleTask.normalizeAction("launch_whatever"),
                "unknown action never normalizes to start");
        eq(2, ScheduleTask.ACTIONS.length, "the new editor exposes only start and stop");

        ScheduleTask legacyPause = ScheduleTask.legacyDaily(43, 8, 0,
                ScheduleTask.ACTION_PAUSE, TargetGame.BATTLE_LOOP, true);
        check(legacyPause.isRunnableAction(), "old daily battle-loop pause remains compatible");
        ScheduleTask newPause = new ScheduleTask(44, ScheduleTask.CYCLE_HOUR, 0,
                ScheduleTask.ACTION_PAUSE, TargetGame.BATTLE_LOOP, true);
        check(!newPause.isRunnableAction(), "new cycle task cannot create a legacy pause action");
    }

    private static void editDuplicateAndEnabledStateArePreserved() {
        Context context = new Context();
        ScheduleTask first = new ScheduleTask(100, ScheduleTask.CYCLE_HOUR, 0,
                ScheduleTask.ACTION_START, TargetGame.ANLEI, true);
        ScheduleTask second = new ScheduleTask(200, ScheduleTask.CYCLE_HALF, 0,
                ScheduleTask.ACTION_START, TargetGame.STARHUNT, true);
        check(ScheduleStore.add(context, first), "first schedule can be added");
        check(ScheduleStore.add(context, second), "a distinct schedule can be added");
        ScheduleStore.setEnabled(context, first.id, false);

        ScheduleTask duplicateEdit = new ScheduleTask(first.id, ScheduleTask.CYCLE_HALF, 0,
                ScheduleTask.ACTION_START, TargetGame.STARHUNT, true);
        check(!ScheduleStore.update(context, duplicateEdit),
                "editing to another task's definition is rejected");
        List<ScheduleTask> afterRejectedEdit = ScheduleStore.load(context);
        eq(2, afterRejectedEdit.size(), "duplicate rejection leaves both original rows present");
        ScheduleTask original = byId(afterRejectedEdit, first.id);
        eq(ScheduleTask.CYCLE_HOUR, original.cycles,
                "duplicate rejection leaves the original schedule unchanged");
        eq(TargetGame.ANLEI, original.target,
                "duplicate rejection does not delete the original target");
        check(!original.enabled, "duplicate rejection preserves the original enabled state");

        ScheduleTask edited = new ScheduleTask(first.id,
                ScheduleTask.CYCLE_HOUR | ScheduleTask.CYCLE_HALF, 3,
                ScheduleTask.ACTION_START, TargetGame.ANLEI, true);
        check(ScheduleStore.update(context, edited), "a distinct replacement can be updated");
        ScheduleTask saved = byId(ScheduleStore.load(context), first.id);
        eq(first.id, saved.id, "update preserves the edited task id");
        eq(ScheduleTask.CYCLE_HOUR | ScheduleTask.CYCLE_HALF, saved.cycles,
                "update applies the requested cycle change");
        check(!saved.enabled, "editing cannot silently re-enable a disabled task");
    }

    private static void occurrenceCatchUpAndClockRollbackAreMonotonic() {
        int[] everyHalfHour = new int[48];
        for (int i = 0; i < everyHalfHour.length; i++) everyHalfHour[i] = i * 30;
        long beforeMidnightOccurrence = epoch(2026, Calendar.OCTOBER, 18, 23, 59, 0);
        long afterMidnight = epoch(2026, Calendar.OCTOBER, 19, 0, 1, 59);
        eq(beforeMidnightOccurrence, ScheduleExecutionTime.dueOccurrenceAt(afterMidnight,
                new int[]{23 * 60 + 59}, 2, ""),
                "the two-minute catch-up window crosses local midnight");
        long tooLate = epoch(2026, Calendar.OCTOBER, 19, 0, 2, 0);
        eq(ScheduleExecutionTime.NO_OCCURRENCE, ScheduleExecutionTime.dueOccurrenceAt(tooLate,
                new int[]{23 * 60 + 59}, 2, ""),
                "a previous-day occurrence outside the catch-up bound is skipped");

        long occurrence = epoch(2026, Calendar.OCTOBER, 19, 11, 0, 0);
        long delayedTick = epoch(2026, Calendar.OCTOBER, 19, 11, 2, 59);
        String key = ScheduleExecutionTime.occurrenceKey(occurrence);
        eq(occurrence, ScheduleExecutionTime.dueOccurrenceAt(delayedTick,
                new int[]{11 * 60}, 2, ""), "a tick up to two minutes late is caught");
        eq(ScheduleExecutionTime.NO_OCCURRENCE,
                ScheduleExecutionTime.dueOccurrenceAt(delayedTick,
                        new int[]{11 * 60}, 2, key), "lastKey prevents duplicate delivery");

        Context context = new Context();
        ScheduleTask task = new ScheduleTask(501, ScheduleTask.CYCLE_HOUR, 0,
                ScheduleTask.ACTION_START, TargetGame.ANLEI, true);
        String nextKey = "202610191200";
        String priorKey = "202610191130";
        check(ScheduleStore.markFired(context, task, nextKey),
                "a new occurrence key advances the durable task fence");
        check(!ScheduleStore.markFired(context, task, priorKey),
                "clock rollback cannot lower the durable task fence");
        eq(nextKey, ScheduleStore.lastKey(context, task),
                "durable lastKey remains at the latest fired occurrence");

        long rolledBack = epoch(2026, Calendar.OCTOBER, 19, 10, 59, 0);
        eq(ScheduleExecutionTime.NO_OCCURRENCE,
                ScheduleExecutionTime.dueOccurrenceAt(rolledBack, everyHalfHour, 2, key),
                "a backward clock cannot make an older occurrence eligible again");
        eq(epoch(2026, Calendar.OCTOBER, 19, 11, 30, 0),
                ScheduleExecutionTime.nextAt(rolledBack, everyHalfHour, key),
                "next-time display respects the monotonic fence after rollback");
    }

    private static ScheduleTask byId(List<ScheduleTask> tasks, long id) {
        for (ScheduleTask task : tasks) if (task.id == id) return task;
        throw new AssertionError("missing task id " + id);
    }

    private static boolean strictlyIncreasing(int[] values) {
        for (int i = 1; i < values.length; i++) if (values[i - 1] >= values[i]) return false;
        return true;
    }

    private static boolean contains(int[] values, int target) {
        for (int value : values) if (value == target) return true;
        return false;
    }

    private static long epoch(int year, int month, int day, int hour, int minute, int second) {
        Calendar calendar = Calendar.getInstance(TimeZone.getTimeZone("UTC"));
        calendar.clear();
        calendar.set(year, month, day, hour, minute, second);
        return calendar.getTimeInMillis();
    }

    private static void check(boolean condition, String message) {
        checks++;
        if (!condition) throw new AssertionError(message);
    }

    private static void eq(Object expected, Object actual, String message) {
        checks++;
        if (expected == null ? actual != null : !expected.equals(actual)) {
            throw new AssertionError(message + ": expected=" + expected + " actual=" + actual);
        }
    }
}
