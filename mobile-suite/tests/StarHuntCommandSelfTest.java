package com.xnas.wpe.starhunt;

public final class StarHuntCommandSelfTest {
    private static int checks;

    public static void main(String[] args) {
        StarHuntCommand scan = new StarHuntCommand(
                1L, StarHuntCommand.Mode.SCAN, 0, 0);
        eq("sequence=1\nmode=scan\nlevels=\nmap=0\n", scan.toFileContents(),
                "new scan command contains only the normalized levels field");
        StarHuntCommand parsedScan = StarHuntCommand.parse(scan.toFileContents());
        eq(1L, parsedScan.sequence, "scan sequence round trip");
        eq(StarHuntCommand.Mode.SCAN, parsedScan.mode, "scan mode round trip");
        eq("", parsedScan.levels.toCsv(), "empty scan selection round trip");

        StarHuntSelection selected = StarHuntSelection.of(10, 8, 9);
        StarHuntCommand run = new StarHuntCommand(
                14L, StarHuntCommand.Mode.RUN, selected, 310);
        eq("sequence=14\nmode=run\nlevels=8,9,10\nmap=310\n", run.toFileContents(),
                "RUN serializes sorted levels without a legacy level mirror");
        StarHuntCommand parsedRun = StarHuntCommand.parse(run.toFileContents());
        eq(14L, parsedRun.sequence, "run sequence parse");
        eq("8,9,10", parsedRun.levels.toCsv(), "run levels parse");
        eq(8, parsedRun.level, "compatibility level exposes the first selection");
        eq(310, parsedRun.map, "run map parse");
        eq(1L, StarHuntCommand.nextSequence(0L), "first worker sequence starts at one");
        eq(StarHuntCommand.MAX_SAFE_SEQUENCE,
                StarHuntCommand.nextSequence(StarHuntCommand.MAX_SAFE_SEQUENCE - 1L),
                "next sequence can reach the worker-safe maximum");
        stateRejects(() -> StarHuntCommand.nextSequence(StarHuntCommand.MAX_SAFE_SEQUENCE),
                "sequence allocator fails before exceeding worker-safe maximum");

        StarHuntCommand legacyRun = StarHuntCommand.parse(
                "sequence=14\nmode=run\nlevel=21\nmap=310\n");
        eq("21", legacyRun.levels.toCsv(), "legacy RUN maps to a singleton selection");
        eq("", StarHuntCommand.parse(
                "sequence=14\nmode=scan\nlevel=0\nmap=0\n").levels.toCsv(),
                "legacy scan level zero maps to the empty selection");
        eq("21", StarHuntCommand.parse(
                "sequence=14\nmode=scan\nlevel=21\nmap=0\n").levels.toCsv(),
                "legacy nonzero SCAN level remains readable");
        eq("8", StarHuntCommand.parse(
                "sequence=14\nmode=run\nlevel=0008\nmap=310\n").levels.toCsv(),
                "legacy scalar level continues accepting decimal leading zeros");

        rejects(() -> StarHuntCommand.parse(
                "sequence=1\nmode=scan\nlevels=\nlevel=0\nmap=0\n"),
                "legacy and new level fields cannot be combined");
        rejects(() -> StarHuntCommand.parse("sequence=1\nmode=scan\nmap=0\n"),
                "a command must include exactly one levels field");
        rejects(() -> StarHuntCommand.parse("sequence=1\nmode=scan\nlevels=\nmap=0\nlevels=\n"),
                "duplicate command fields must fail closed");
        rejects(() -> new StarHuntCommand(2L, StarHuntCommand.Mode.RUN,
                        StarHuntSelection.empty(), 310),
                "run command must include at least one level");
        rejects(() -> new StarHuntCommand(2L, StarHuntCommand.Mode.RUN,
                        StarHuntSelection.of(4), 0),
                "run command must include the current map");
        rejects(() -> StarHuntCommand.parse(
                "sequence=1\nmode=run\nlevels=1000\nmap=9\n"),
                "level must be in range");
        rejects(() -> StarHuntCommand.parse(
                "sequence=1\nmode=run\nlevels=9,8\nmap=9\n"),
                "levels must be ascending");
        rejects(() -> StarHuntCommand.parse(
                "sequence=1\nmode=run\nlevels=8,8\nmap=9\n"),
                "duplicate levels must fail closed");
        rejects(() -> StarHuntCommand.parse(
                "sequence=1\nmode=run\nlevels=8\nmap=2147483648\n"),
                "map must fit the signed 32-bit worker field");
        rejects(() -> StarHuntCommand.parse(
                "sequence=1\nmode=scan\nlevels=８\nmap=0\n"),
                "levels must use ASCII decimal digits");
        rejects(() -> StarHuntCommand.parse("sequence=0\nmode=scan\nlevels=\nmap=0\n"),
                "sequence must be positive");
        rejects(() -> StarHuntCommand.parse(
                "sequence=9007199254740992\nmode=scan\nlevels=\nmap=0\n"),
                "sequence must fit the worker-safe integer range");
        rejects(() -> StarHuntCommand.parse("sequence=+1\nmode=scan\nlevels=\nmap=0\n"),
                "sequence must use decimal digits");
        rejects(() -> StarHuntCommand.parse(
                "sequence=1\nmode=scan\nlevels=\nmap=0\nextra=ignored\n"),
                "unknown command fields must fail closed");
        rejects(() -> StarHuntCommand.parse(
                "sequence=1\nmode=run\nlevel=4\nmap=2\nlevels=4\n"),
                "legacy and new level fields cannot be combined");
        System.out.println("StarHuntCommandSelfTest PASS (" + checks + " assertions)");
    }

    private static void rejects(Runnable action, String message) {
        checks++;
        try {
            action.run();
        } catch (IllegalArgumentException expected) {
            return;
        }
        throw new AssertionError(message);
    }

    private static void stateRejects(Runnable action, String message) {
        checks++;
        try {
            action.run();
        } catch (IllegalStateException expected) {
            return;
        }
        throw new AssertionError(message);
    }

    private static void eq(Object expected, Object actual, String message) {
        checks++;
        if (expected == null ? actual != null : !expected.equals(actual)) {
            throw new AssertionError(message + ": expected=" + expected + " actual=" + actual);
        }
    }
}
