package com.xnas.wpe.suite;

import java.util.Arrays;
import java.util.Calendar;
import java.util.TimeZone;

public final class ScheduleExecutionSelfTest {
    private static int checks;

    public static void main(String[] args) {
        TimeZone.setDefault(TimeZone.getTimeZone("UTC"));
        partialDispatchIsVisible();
        dispatchIsNotConfirmation();
        staleMissingAndTimeoutStayUnknown();
        dailyOccurrenceRespectsFiredFenceAndMidnight();
        recurringOccurrencesCatchUpAndFenceClockRollback();
        System.out.println("ScheduleExecutionSelfTest PASS (" + checks + " assertions)");
    }

    private static void partialDispatchIsVisible() {
        ScheduleExecution execution = ScheduleExecution.create(1L, "stop", 1_000L,
                new String[]{"starhunt", "jinchan"}, 45_000L);
        execution.recordDispatch("starhunt", true, ScheduleExecution.ReasonCode.NONE, 1_100L);
        execution.recordDispatch("jinchan", false, ScheduleExecution.ReasonCode.DISPATCH_REJECTED, 1_101L);
        eq(ScheduleExecution.AggregateState.PARTIAL, execution.aggregate(),
                "one rejected target is not hidden by another accepted dispatch");
        eq(ScheduleExecution.DispatchState.ACCEPTED, execution.target("starhunt").dispatchState,
                "first target dispatch is recorded independently");
        eq(ScheduleExecution.DispatchState.FAILED, execution.target("jinchan").dispatchState,
                "second target dispatch failure is recorded independently");
        eq(ScheduleExecution.ReasonCode.DISPATCH_REJECTED, execution.target("jinchan").reasonCode,
                "safe per-target dispatch reason is retained");
        execution.recordObservation("starhunt", ScheduleExecution.ObservationResult.CONFIRMED,
                ScheduleExecution.ReasonCode.NONE, 1_500L);
        eq(ScheduleExecution.AggregateState.PARTIAL, execution.aggregate(),
                "a confirmed target does not erase another target failure");
    }

    private static void dispatchIsNotConfirmation() {
        ScheduleExecution execution = ScheduleExecution.create(2L, "start", 2_000L,
                new String[]{"starhunt"}, 45_000L);
        execution.recordDispatch("starhunt", true, ScheduleExecution.ReasonCode.NONE, 2_100L);
        eq(ScheduleExecution.AggregateState.DISPATCHED, execution.aggregate(),
                "accepted service request remains dispatched until evidence arrives");
        eq(ScheduleExecution.ConfirmationState.PENDING, execution.target("starhunt").confirmationState,
                "dispatch acceptance does not mark action confirmed");
        execution.recordObservation("starhunt", ScheduleExecution.ObservationResult.CONFIRMED,
                ScheduleExecution.ReasonCode.NONE, 2_500L);
        eq(ScheduleExecution.AggregateState.CONFIRMED, execution.aggregate(),
                "fresh observer evidence can confirm the target");
    }

    private static void staleMissingAndTimeoutStayUnknown() {
        ScheduleExecution stale = ScheduleExecution.create(3L, "start", 10_000L,
                new String[]{"battlepanel"}, 45_000L);
        stale.recordDispatch("battlepanel", true, ScheduleExecution.ReasonCode.NONE, 10_000L);
        stale.recordObservation("battlepanel", ScheduleExecution.ObservationResult.WAIT,
                ScheduleExecution.ReasonCode.OBSERVATION_STALE, 11_000L);
        eq(ScheduleExecution.AggregateState.DISPATCHED, stale.aggregate(),
                "stale evidence remains pending while bounded observation time remains");
        check(stale.expire(55_001L), "accepted request expires at its bounded deadline");
        eq(ScheduleExecution.AggregateState.UNKNOWN, stale.aggregate(),
                "stale evidence becomes unknown rather than confirmed");
        eq(ScheduleExecution.ReasonCode.TIMEOUT, stale.target("battlepanel").reasonCode,
                "timeout is explicit");

        ScheduleExecution missing = ScheduleExecution.create(4L, "stop", 20_000L,
                new String[]{"anlei"}, 45_000L);
        missing.recordDispatch("anlei", true, ScheduleExecution.ReasonCode.NONE, 20_000L);
        missing.recordObservation("anlei", ScheduleExecution.ObservationResult.WAIT,
                ScheduleExecution.ReasonCode.OBSERVATION_MISSING, 21_000L);
        missing.expire(65_000L);
        eq(ScheduleExecution.AggregateState.UNKNOWN, missing.aggregate(),
                "missing observation cannot be promoted from dispatch alone");

        ScheduleExecution interrupted = ScheduleExecution.create(5L, "start", 30_000L,
                new String[]{"jinchan"}, 45_000L);
        interrupted.expire(30_001L);
        eq(ScheduleExecution.AggregateState.UNKNOWN, interrupted.aggregate(),
                "restart with an unattempted persisted target becomes unknown without redispatch");
        eq(ScheduleExecution.ReasonCode.INTERRUPTED, interrupted.target("jinchan").reasonCode,
                "interrupted target has a safe reason");
    }

    private static void dailyOccurrenceRespectsFiredFenceAndMidnight() {
        long before = epoch(2026, Calendar.OCTOBER, 18, 10, 59, 59);
        eq(epoch(2026, Calendar.OCTOBER, 18, 11, 0, 0),
                ScheduleExecutionTime.nextAt(before, 11, 0, false),
                "future occurrence remains on the current day");
        long sameMinute = epoch(2026, Calendar.OCTOBER, 18, 11, 0, 30);
        eq(epoch(2026, Calendar.OCTOBER, 18, 11, 0, 0),
                ScheduleExecutionTime.nextAt(sameMinute, 11, 0, false),
                "current minute remains eligible until today's trigger fence is set");
        eq(epoch(2026, Calendar.OCTOBER, 19, 11, 0, 0),
                ScheduleExecutionTime.nextAt(sameMinute, 11, 0, true),
                "same-day fired task rolls to tomorrow");
        eq(epoch(2026, Calendar.OCTOBER, 19, 14, 0, 0),
                ScheduleExecutionTime.nextAt(sameMinute, 14, 0, true),
                "a fired task stays fenced for the whole local day after clock rollback");
        eq(epoch(2026, Calendar.OCTOBER, 19, 11, 0, 0),
                ScheduleExecutionTime.nextAt(epoch(2026, Calendar.OCTOBER, 18, 11, 1, 0),
                        11, 0, false),
                "past wall-clock minute rolls to tomorrow");
        eq(epoch(2026, Calendar.OCTOBER, 19, 0, 0, 0),
                ScheduleExecutionTime.nextAt(epoch(2026, Calendar.OCTOBER, 18, 23, 59, 0),
                        0, 0, false),
                "midnight is represented on the next local date");
    }

    private static void recurringOccurrencesCatchUpAndFenceClockRollback() {
        int[] recurring = new int[48];
        for (int i = 0; i < recurring.length; i++) recurring[i] = i * 30;
        long midnightPlusOne = epoch(2026, Calendar.OCTOBER, 19, 0, 1, 0);
        long priorDay = epoch(2026, Calendar.OCTOBER, 18, 23, 59, 0);
        eq(priorDay, ScheduleExecutionTime.dueOccurrenceAt(midnightPlusOne,
                new int[]{23 * 60 + 59}, 2, ""),
                "a one-minute-old occurrence before midnight remains catch-up eligible");
        eq(ScheduleExecutionTime.NO_OCCURRENCE, ScheduleExecutionTime.dueOccurrenceAt(
                epoch(2026, Calendar.OCTOBER, 19, 0, 2, 59),
                new int[]{23 * 60 + 59}, 2, ""),
                "cross-midnight occurrence is not replayed after its two-minute bound");

        long at110259 = epoch(2026, Calendar.OCTOBER, 19, 11, 2, 59);
        long at110000 = epoch(2026, Calendar.OCTOBER, 19, 11, 0, 0);
        eq(at110000, ScheduleExecutionTime.dueOccurrenceAt(at110259,
                new int[]{11 * 60}, 2, ""),
                "the current wall-clock minute includes a two-minute-delayed tick");
        String firedKey = ScheduleExecutionTime.occurrenceKey(at110000);
        eq(ScheduleExecutionTime.NO_OCCURRENCE, ScheduleExecutionTime.dueOccurrenceAt(
                at110259, new int[]{11 * 60}, 2, firedKey),
                "the persisted occurrence key blocks a duplicate tick");

        long rolledBack = epoch(2026, Calendar.OCTOBER, 19, 10, 59, 0);
        eq(ScheduleExecutionTime.NO_OCCURRENCE, ScheduleExecutionTime.dueOccurrenceAt(
                rolledBack, recurring, 2, firedKey),
                "rolling the clock behind a fired key cannot replay an older occurrence");
        long nextAfterRollback = ScheduleExecutionTime.nextAt(rolledBack, recurring, firedKey);
        eq(epoch(2026, Calendar.OCTOBER, 19, 11, 30, 0), nextAfterRollback,
                "next-occurrence display also observes the monotonic rollback fence");

        check(recurring.length == 48, "two cycles represent all 48 daily occurrences");
        check(isStrictlyIncreasing(recurring), "recurring occurrence minutes remain sorted");
    }

    private static boolean isStrictlyIncreasing(int[] values) {
        for (int i = 1; i < values.length; i++) if (values[i - 1] >= values[i]) return false;
        return true;
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
