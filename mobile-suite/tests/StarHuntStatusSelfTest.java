package com.xnas.wpe.starhunt;

public final class StarHuntStatusSelfTest {
    private static int checks;

    public static void main(String[] args) {
        StarHuntStatus status = StarHuntStatus.parse(
                "status=reading\n"
                        + "map=310\nmapName=长寿郊外\nrole=测试角色\n"
                        + "levels=21:10,2:3,21:99,1000:4,4:-1\n"
                        + "levelNames=21:地祖星|2:地狗星|999:未发现\n"
                        + "selectedLevels=8,9,10\nselectedLevel=2\ntargetId=778\ntargetName=地狗星\n"
                        + "targetX=91\ntargetY=42\nreason=扫描中\n"
                        + "updatedAt=2000\nacceptedSequence=7\n"
                        + "hooksRemoved=false\nbindingCleared=false\n");
        eq("reading", status.state, "status value");
        eq(310, status.map, "map value");
        eq("长寿郊外", status.mapName, "map name");
        eq(2, status.levels.size(), "invalid and duplicate levels are excluded");
        eq(2, status.levels.get(0).number, "levels sort numerically");
        eq("8,9,10", status.selectedLevels, "selected level list is parsed as normalized CSV");
        eq(8, status.selectedLevel, "compatibility selected level is the first selected level");
        eq("地狗星", status.levels.get(0).name, "name follows numeric level");
        eq(3, status.levels.get(0).count, "first duplicate count is retained");
        eq(21, status.levels.get(1).number, "larger level follows smaller level");
        eq(10, status.countForLevel(21), "level count lookup");
        eq(7L, status.acceptedSequence, "accepted command sequence");
        check(status.confirmsActionStopped(7L), "reading acknowledges stopped action");
        check(!status.confirmsActionStopped(8L), "older sequence cannot confirm action stop");
        check(status.isFresh(2004L, 5L), "recent status is fresh");
        check(!status.isFresh(2006L, 5L), "old status is stale");
        check(status.canStartAction(2, 2004L), "fresh level and role allow explicit action");
        check(status.canStartAction(StarHuntSelection.of(4, 21), 2004L),
                "manual multi-level action starts when any selected level has a candidate");
        check(!status.canStartAction(4, 2004L), "missing level blocks action");
        check(status.canStartHangup(4, 2004L),
                "hangup can wait even when the chosen star is absent from the scanned levels");
        check(status.canStartHangup(StarHuntSelection.of(8, 9, 10), 2004L),
                "multi-level hangup can wait when none of its selected levels has a candidate");
        check(!status.canStartHangup(StarHuntSelection.empty(), 2004L),
                "empty multi-level selection cannot start a hangup");
        StarHuntStatus noCandidate = StarHuntStatus.parse(
                "status=reading\nmap=310\nrole=r\nlevels=2:0\nupdatedAt=2000\n");
        check(noCandidate.canStartHangup(4, 2000L),
                "hangup may wait when its chosen star is absent from the current scan");
        check(!noCandidate.canStartAction(4, 2000L),
                "manual start remains blocked when its star is absent");
        check(!noCandidate.canStartAction(StarHuntSelection.of(4, 9), 2000L),
                "manual multi-level start requires at least one candidate");
        StarHuntStatus explicitEmptySelection = StarHuntStatus.parse(
                "status=reading\nselectedLevels=\nselectedLevel=4\n");
        eq("", explicitEmptySelection.selectedLevels,
                "an explicit empty status selection remains empty");
        StarHuntStatus legacySelection = StarHuntStatus.parse("status=reading\nselectedLevel=7\n");
        eq("7", legacySelection.selectedLevels, "legacy selectedLevel maps to a singleton selection");
        check(!StarHuntStatus.parse("status=reading\nrole=r\nupdatedAt=2000\n")
                        .canStartHangup(4, 2000L), "missing map blocks hangup");
        check(!StarHuntStatus.parse("status=reading\nmap=310\nupdatedAt=2000\n")
                        .canStartHangup(4, 2000L), "missing role blocks hangup");
        check(!StarHuntStatus.parse(
                        "status=reading\nmap=310\nrole=unknown\nupdatedAt=2000\n")
                        .canStartHangup(4, 2000L), "unknown role identity blocks hangup");
        StarHuntStatus invalidHangupState = StarHuntStatus.parse(
                "status=not_a_worker_state\nmap=310\nrole=r\nlevels=4:1\nupdatedAt=2000\n");
        check(!invalidHangupState.canStartHangup(4, 2000L),
                "unrecognized state cannot authorize automatic hangup");
        StarHuntStatus goneHangupContext = StarHuntStatus.parse(
                "status=reading\nmap=310\nrole=r\ngameProcessGone=true\nupdatedAt=2000\n");
        check(!goneHangupContext.canStartHangup(4, 2000L),
                "ended game process cannot authorize automatic hangup");
        StarHuntStatus unknownManualContext = StarHuntStatus.parse(
                "status=unknown\nmap=310\nrole=r\nlevels=4:1\nupdatedAt=2000\n");
        check(unknownManualContext.canStartAction(4, 2000L),
                "existing manual-start gate still accepts its legacy unknown state");
        check(!unknownManualContext.canStartHangup(4, 2000L),
                "automatic hangup rejects unknown state");

        StarHuntStatus challenge = StarHuntStatus.parse(
                "status=challenge_wait\nacceptedSequence=8\n");
        check(!challenge.confirmsActionStopped(8L), "challenge wait is not a stopped action");
        StarHuntStatus unknown = StarHuntStatus.parse(
                "status=unknown\nacceptedSequence=8\n");
        check(unknown.confirmsActionStopped(8L), "unknown suspends automatic actions after ack");

        StarHuntStatus missing = StarHuntStatus.parse("status=stopped\n");
        eq(-1L, missing.acceptedSequence, "missing sequence stays unavailable");
        check(!missing.confirmsActionStopped(1L), "missing sequence is not an acknowledgement");
        check(!missing.confirmsCleanup(), "missing cleanup flags are not confirmation");
        StarHuntStatus incomplete = StarHuntStatus.parse(
                "status=stopped\nhooksRemoved=true\nbindingCleared=false\n");
        check(!incomplete.confirmsCleanup(), "both cleanup facts are required");
        StarHuntStatus clean = StarHuntStatus.parse(
                "status=stopped\nhooksRemoved=true\nbindingCleared=true\nacceptedSequence=9\n");
        check(clean.confirmsCleanup(), "explicit stopped cleanup is confirmed");
        check(clean.confirmsCleanupFor(9L), "cleanup acknowledges the requested stop sequence");
        check(!clean.confirmsCleanupFor(10L), "older cleanup cannot confirm a newer stop command");
        StarHuntStatus unknownCleanup = StarHuntStatus.parse(
                "status=unknown\nhooksRemoved=true\nbindingCleared=true\n");
        check(!unknownCleanup.confirmsCleanup(), "unknown state is not terminal cleanup");
        StarHuntStatus gameGone = StarHuntStatus.parse(
                "status=stopped\nreason=original_process_ended_cleanup_unconfirmed\n"
                        + "sessionDetached=true\nhooksRemoved=false\nbindingCleared=false\n"
                        + "updatedAt=1800000000\n");
        check(gameGone.confirmsGameProcessGone(),
                "explicit detached game process can release a stale action lock");
        check(gameGone.confirmsGameProcessGoneAfter(1_799_999_999_500L),
                "fresh process-gone status confirms the current stop attempt");
        check(!gameGone.confirmsGameProcessGoneAfter(1_800_000_001_000L),
                "stale process-gone status cannot release the current action lock");
        check(!gameGone.confirmsCleanup(),
                "game process exit does not masquerade as hook and binding cleanup");
        check(gameGone.permitsFreshRead(),
                "ended detached process permits a new read without claiming Lua cleanup");
        check(!gameGone.canStartAction(1, 1_800_000_000L),
                "old ended process does not authorize a new action");
        check(clean.permitsFreshRead(), "confirmed cleanup still permits a fresh read");
        check(!incomplete.permitsFreshRead(), "unconfirmed live process blocks a fresh read");
        StarHuntStatus notDetached = StarHuntStatus.parse(
                "status=stopped\ngameProcessGone=true\nsessionDetached=false\n");
        check(!notDetached.permitsFreshRead(), "process gone without detach confirmation blocks rebind");
        StarHuntStatus nonTerminalGone = StarHuntStatus.parse(
                "status=reading\ngameProcessGone=true\nsessionDetached=true\n");
        check(!nonTerminalGone.permitsFreshRead(), "non-terminal evidence cannot rebind a read");
        StarHuntStatus noGameGoneProof = StarHuntStatus.parse(
                "status=stopped\nhooksRemoved=false\nbindingCleared=false\n");
        check(!noGameGoneProof.confirmsGameProcessGone(),
                "stopped status without process-gone proof is insufficient");

        StarHuntStatus wrongState = StarHuntStatus.parse(
                "status=reading\nmap=310\nrole=r\nlevels=2:3\nupdatedAt=2000\n");
        check(wrongState.canStartAction(2, 2000L),
                "exact update time is usable for an explicit start");
        StarHuntStatus noRole = StarHuntStatus.parse(
                "status=reading\nmap=310\nlevels=2:3\nupdatedAt=2000\n");
        check(!noRole.canStartAction(2, 2000L), "missing role blocks action");
        StarHuntStatus battle = StarHuntStatus.parse(
                "status=battle\nmap=310\nrole=r\nlevels=2:3\nupdatedAt=2000\n");
        check(!battle.canStartAction(2, 2000L), "battle state blocks a new action");
        System.out.println("StarHuntStatusSelfTest PASS (" + checks + " assertions)");
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
