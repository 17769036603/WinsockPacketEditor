package com.xnas.wpe.starhunt;

import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.List;
import java.util.TreeSet;

/** Immutable, normalized selection of star levels. */
public final class StarHuntSelection {
    private static final StarHuntSelection EMPTY = new StarHuntSelection(
            Collections.<Integer>emptyList());

    private final List<Integer> levels;

    private StarHuntSelection(List<Integer> normalizedLevels) {
        this.levels = Collections.unmodifiableList(new ArrayList<>(normalizedLevels));
    }

    public static StarHuntSelection empty() {
        return EMPTY;
    }

    public static StarHuntSelection of(int... values) {
        if (values == null || values.length == 0) return EMPTY;
        TreeSet<Integer> sorted = new TreeSet<>();
        for (int value : values) {
            requireLevel(value);
            sorted.add(value);
        }
        return fromSortedSet(sorted);
    }

    public static StarHuntSelection from(Collection<Integer> values) {
        if (values == null || values.isEmpty()) return EMPTY;
        TreeSet<Integer> sorted = new TreeSet<>();
        for (Integer value : values) {
            if (value == null) throw new IllegalArgumentException("level is required");
            requireLevel(value);
            sorted.add(value);
        }
        return fromSortedSet(sorted);
    }

    /** Parses only canonical CSV: ASCII decimal, strictly ascending, no duplicates. */
    public static StarHuntSelection parseCsv(String csv) {
        if (csv == null) throw new IllegalArgumentException("levels are missing");
        if (csv.isEmpty()) return EMPTY;
        String[] items = csv.split(",", -1);
        List<Integer> parsed = new ArrayList<>(items.length);
        int previous = 0;
        for (String item : items) {
            if (!item.matches("[0-9]+")) {
                throw new IllegalArgumentException("level must use ASCII decimal digits");
            }
            int value;
            try {
                value = Integer.parseInt(item);
            } catch (NumberFormatException error) {
                throw new IllegalArgumentException("level is outside 1..999", error);
            }
            requireLevel(value);
            if (value <= previous || !item.equals(String.valueOf(value))) {
                throw new IllegalArgumentException("levels must be canonical and strictly ascending");
            }
            parsed.add(value);
            previous = value;
        }
        return new StarHuntSelection(parsed);
    }

    /** Reads persisted selection semantics, including the one-time legacy migration rule. */
    public static StarHuntSelection restore(boolean hasVersionedValue, String versionedCsv,
                                            boolean hasLegacyValue, int legacyLevel) {
        if (hasVersionedValue) {
            try {
                return parseCsv(versionedCsv);
            } catch (IllegalArgumentException ignored) {
                // A present but malformed value is deliberate persisted state; never fall back.
                return EMPTY;
            }
        }
        if (hasLegacyValue) {
            return legacyLevel >= 1 && legacyLevel <= 999
                    ? of(legacyLevel) : EMPTY;
        }
        return of(1);
    }

    public StarHuntSelection withAdded(int level) {
        requireLevel(level);
        if (contains(level)) return this;
        List<Integer> updated = new ArrayList<>(levels.size() + 1);
        boolean inserted = false;
        for (int existing : levels) {
            if (!inserted && level < existing) {
                updated.add(level);
                inserted = true;
            }
            updated.add(existing);
        }
        if (!inserted) updated.add(level);
        return new StarHuntSelection(updated);
    }

    public StarHuntSelection without(int level) {
        if (!contains(level)) return this;
        List<Integer> updated = new ArrayList<>(levels);
        updated.remove(Integer.valueOf(level));
        return updated.isEmpty() ? EMPTY : new StarHuntSelection(updated);
    }

    public boolean contains(int level) {
        return Collections.binarySearch(levels, level) >= 0;
    }

    public boolean isEmpty() {
        return levels.isEmpty();
    }

    public int size() {
        return levels.size();
    }

    public int firstLevel() {
        return isEmpty() ? 0 : levels.get(0);
    }

    public List<Integer> asList() {
        return levels;
    }

    public String toCsv() {
        StringBuilder result = new StringBuilder();
        for (int i = 0; i < levels.size(); i++) {
            if (i > 0) result.append(',');
            result.append(levels.get(i));
        }
        return result.toString();
    }

    private static void requireLevel(int level) {
        if (level < 1 || level > 999) {
            throw new IllegalArgumentException("level is outside 1..999");
        }
    }

    private static StarHuntSelection fromSortedSet(TreeSet<Integer> sorted) {
        if (sorted.isEmpty()) return EMPTY;
        return new StarHuntSelection(new ArrayList<>(sorted));
    }

    @Override public boolean equals(Object other) {
        return other instanceof StarHuntSelection
                && levels.equals(((StarHuntSelection) other).levels);
    }

    @Override public int hashCode() {
        return levels.hashCode();
    }

    @Override public String toString() {
        return toCsv();
    }
}
