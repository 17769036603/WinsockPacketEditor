package com.xnas.wpe.suite;

import java.text.SimpleDateFormat;
import java.util.Calendar;
import java.util.Date;
import java.util.Locale;

/** Pure local-time occurrence calculations shared by the scheduler and offline tests. */
public final class ScheduleExecutionTime {
    public static final long NO_OCCURRENCE = -1L;

    private ScheduleExecutionTime() { }

    /**
     * Returns the next eligible local wall-clock minute from a sorted daily occurrence list.
     * The current minute remains eligible until a newer occurrence key has been persisted.
     */
    public static long nextAt(long nowMillis, int[] occurrenceMinutes, String lastKey) {
        if (occurrenceMinutes == null || occurrenceMinutes.length == 0) return Long.MAX_VALUE;
        Calendar now = Calendar.getInstance();
        now.setTimeInMillis(nowMillis);
        int currentMinute = now.get(Calendar.HOUR_OF_DAY) * 60 + now.get(Calendar.MINUTE);
        Calendar today = dayStart(now);
        for (int dayOffset = 0; dayOffset <= 366; dayOffset++) {
            Calendar day = (Calendar) today.clone();
            day.add(Calendar.DAY_OF_YEAR, dayOffset);
            long best = Long.MAX_VALUE;
            for (int occurrenceMinute : occurrenceMinutes) {
                if (occurrenceMinute < 0 || occurrenceMinute >= 1440) continue;
                if (dayOffset == 0 && occurrenceMinute < currentMinute) continue;
                Calendar candidate = atMinute(day, occurrenceMinute);
                if (candidate.get(Calendar.HOUR_OF_DAY) * 60 + candidate.get(Calendar.MINUTE)
                        != occurrenceMinute) continue;
                String key = occurrenceKey(candidate.getTimeInMillis());
                if (key.compareTo(safeKey(lastKey)) <= 0) continue;
                best = Math.min(best, candidate.getTimeInMillis());
            }
            if (best != Long.MAX_VALUE) return best;
        }
        return Long.MAX_VALUE;
    }

    /**
     * Finds the newest due occurrence on today or yesterday inside the bounded catch-up window.
     * A persisted key is a monotonic fence: clock rollback cannot make an older key eligible.
     */
    public static long dueOccurrenceAt(long nowMillis, int[] occurrenceMinutes,
                                       int catchUpMinutes, String lastKey) {
        if (occurrenceMinutes == null || occurrenceMinutes.length == 0) return NO_OCCURRENCE;
        Calendar now = Calendar.getInstance();
        now.setTimeInMillis(nowMillis);
        int currentMinute = now.get(Calendar.HOUR_OF_DAY) * 60 + now.get(Calendar.MINUTE);
        Calendar today = dayStart(now);
        long newestDue = NO_OCCURRENCE;
        for (int dayOffset = 0; dayOffset >= -1; dayOffset--) {
            Calendar day = (Calendar) today.clone();
            day.add(Calendar.DAY_OF_YEAR, dayOffset);
            for (int occurrenceMinute : occurrenceMinutes) {
                if (occurrenceMinute < 0 || occurrenceMinute >= 1440) continue;
                int delta = dayOffset == 0
                        ? currentMinute - occurrenceMinute
                        : 1440 + currentMinute - occurrenceMinute;
                if (delta < 0 || delta > Math.max(0, catchUpMinutes)) continue;
                Calendar candidate = atMinute(day, occurrenceMinute);
                if (candidate.get(Calendar.HOUR_OF_DAY) * 60 + candidate.get(Calendar.MINUTE)
                        != occurrenceMinute) continue;
                long candidateMillis = candidate.getTimeInMillis();
                String key = occurrenceKey(candidateMillis);
                if (key.compareTo(safeKey(lastKey)) <= 0) continue;
                if (newestDue == NO_OCCURRENCE || candidateMillis > newestDue) {
                    newestDue = candidateMillis;
                }
            }
        }
        return newestDue;
    }

    /** Stable local occurrence identity, including the date for cross-midnight schedules. */
    public static String occurrenceKey(long occurrenceAtMillis) {
        return new SimpleDateFormat("yyyyMMddHHmm", Locale.US)
                .format(new Date(occurrenceAtMillis));
    }

    /** Legacy helper retained for existing callers and its established fixed-time contract. */
    public static long nextAt(long nowMillis, int hour, int minute, boolean firedToday) {
        Calendar now = Calendar.getInstance();
        now.setTimeInMillis(nowMillis);
        int targetHour = clamp(hour, 0, 23);
        int targetMinute = clamp(minute, 0, 59);
        int currentMinute = now.get(Calendar.HOUR_OF_DAY) * 60 + now.get(Calendar.MINUTE);
        int targetMinuteOfDay = targetHour * 60 + targetMinute;
        Calendar next = (Calendar) now.clone();
        next.set(Calendar.HOUR_OF_DAY, targetHour);
        next.set(Calendar.MINUTE, targetMinute);
        next.set(Calendar.SECOND, 0);
        next.set(Calendar.MILLISECOND, 0);
        if (firedToday || targetMinuteOfDay < currentMinute) {
            next.add(Calendar.DAY_OF_YEAR, 1);
        }
        return next.getTimeInMillis();
    }

    private static Calendar dayStart(Calendar source) {
        Calendar day = (Calendar) source.clone();
        day.set(Calendar.HOUR_OF_DAY, 0);
        day.set(Calendar.MINUTE, 0);
        day.set(Calendar.SECOND, 0);
        day.set(Calendar.MILLISECOND, 0);
        return day;
    }

    private static Calendar atMinute(Calendar day, int minuteOfDay) {
        Calendar candidate = (Calendar) day.clone();
        candidate.set(Calendar.HOUR_OF_DAY, minuteOfDay / 60);
        candidate.set(Calendar.MINUTE, minuteOfDay % 60);
        candidate.set(Calendar.SECOND, 0);
        candidate.set(Calendar.MILLISECOND, 0);
        return candidate;
    }

    private static String safeKey(String key) {
        return key == null ? "" : key;
    }

    private static int clamp(int value, int min, int max) {
        if (value < min) return min;
        return Math.min(value, max);
    }
}
