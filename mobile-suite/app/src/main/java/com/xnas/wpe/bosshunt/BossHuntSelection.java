package com.xnas.wpe.bosshunt;

import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.List;
import java.util.TreeSet;

/** Immutable normalized selection of supported boss-hunt maps. */
public final class BossHuntSelection {
    private static final BossHuntSelection EMPTY = new BossHuntSelection(Collections.<Integer>emptyList());
    private final List<Integer> maps;
    private BossHuntSelection(List<Integer> values) { maps = Collections.unmodifiableList(new ArrayList<>(values)); }
    public static BossHuntSelection empty() { return EMPTY; }
    public static BossHuntSelection of(int... values) {
        if (values == null || values.length == 0) return EMPTY;
        TreeSet<Integer> sorted = new TreeSet<>();
        for (int value : values) { requireMap(value); sorted.add(value); }
        return fromSortedSet(sorted);
    }
    public static BossHuntSelection from(Collection<Integer> values) {
        if (values == null || values.isEmpty()) return EMPTY;
        TreeSet<Integer> sorted = new TreeSet<>();
        for (Integer value : values) { if (value == null) throw new IllegalArgumentException("map is required"); requireMap(value); sorted.add(value); }
        return fromSortedSet(sorted);
    }
    public static BossHuntSelection parseCsv(String csv) {
        if (csv == null) throw new IllegalArgumentException("maps are missing");
        if (csv.isEmpty()) return EMPTY;
        String[] items = csv.split(",", -1); List<Integer> result = new ArrayList<>(items.length); int previous = 0;
        for (String item : items) {
            if (!item.matches("[0-9]+")) throw new IllegalArgumentException("map must use ASCII decimal digits");
            int value; try { value = Integer.parseInt(item); } catch (NumberFormatException e) { throw new IllegalArgumentException("map is outside supported range", e); }
            requireMap(value);
            if (value <= previous || !item.equals(String.valueOf(value))) throw new IllegalArgumentException("maps must be canonical and strictly ascending");
            result.add(value); previous = value;
        }
        return new BossHuntSelection(result);
    }
    /** No prior map is selected by default. */
    public static BossHuntSelection restore(boolean present, String csv) {
        if (!present) return EMPTY;
        try { return parseCsv(csv); } catch (IllegalArgumentException ignored) { return EMPTY; }
    }
    public BossHuntSelection withAdded(int map) { requireMap(map); if (contains(map)) return this; TreeSet<Integer> sorted = new TreeSet<>(maps); sorted.add(map); return fromSortedSet(sorted); }
    public BossHuntSelection without(int map) { if (!contains(map)) return this; List<Integer> copy = new ArrayList<>(maps); copy.remove(Integer.valueOf(map)); return copy.isEmpty() ? EMPTY : new BossHuntSelection(copy); }
    public boolean contains(int map) { return Collections.binarySearch(maps, map) >= 0; }
    public boolean isEmpty() { return maps.isEmpty(); }
    public int size() { return maps.size(); }
    public int firstMap() { return isEmpty() ? 0 : maps.get(0); }
    public List<Integer> asList() { return maps; }
    public String toCsv() { StringBuilder out = new StringBuilder(); for (int i = 0; i < maps.size(); i++) { if (i > 0) out.append(','); out.append(maps.get(i)); } return out.toString(); }
    private static void requireMap(int map) { if (map <= 0 || !BossHuntMapCatalog.contains(map)) throw new IllegalArgumentException("unsupported boss-hunt map"); }
    private static BossHuntSelection fromSortedSet(TreeSet<Integer> sorted) { return sorted.isEmpty() ? EMPTY : new BossHuntSelection(new ArrayList<>(sorted)); }
    @Override public boolean equals(Object other) { return other instanceof BossHuntSelection && maps.equals(((BossHuntSelection) other).maps); }
    @Override public int hashCode() { return maps.hashCode(); }
    @Override public String toString() { return toCsv(); }
}
