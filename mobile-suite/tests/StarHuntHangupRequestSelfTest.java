import com.xnas.wpe.starhunt.StarHuntCommand;
import com.xnas.wpe.starhunt.StarHuntHangupRequest;
import com.xnas.wpe.starhunt.StarHuntSelection;
import com.xnas.wpe.starhunt.StarHuntStatus;

public final class StarHuntHangupRequestSelfTest {
    private static int checks;

    public static void main(String[] args) {
        long now = 2_000_000L;
        StarHuntCommand scan = new StarHuntCommand(9L, StarHuntCommand.Mode.SCAN, 0, 0);
        StarHuntStatus noCandidate = StarHuntStatus.parse(
                "status=reading\nmap=310\nrole=r\nlevels=2:0\nupdatedAt=2000\nacceptedSequence=9\n");
        StarHuntSelection launchSelection = StarHuntSelection.of(10, 8, 9);
        StarHuntHangupRequest request = new StarHuntHangupRequest(
                launchSelection, 9L, now - 1_000L);
        launchSelection = launchSelection.withAdded(11);
        eq("8,9,10", request.levels.toCsv(), "request keeps an immutable launch snapshot");
        eq(StarHuntHangupRequest.Decision.SUBMIT, request.evaluate(noCandidate, scan, now),
                "confirmed fresh map and role authorize a wait-style run without a star candidate");
        check(request.markSubmitted(9L), "matching scan can be submitted once");
        check(!request.isPending(), "submitted request is no longer pending");
        check(!request.markSubmitted(9L), "repeated submission is rejected");
        eq(StarHuntHangupRequest.Decision.CANCEL, request.evaluate(noCandidate, scan, now),
                "submitted request cannot authorize another run");

        StarHuntHangupRequest stale = new StarHuntHangupRequest(
                StarHuntSelection.of(4, 8, 10), 9L, now - 1_000L);
        StarHuntStatus oldStatus = StarHuntStatus.parse(
                "status=reading\nmap=310\nrole=r\nupdatedAt=2000\nacceptedSequence=8\n");
        eq(StarHuntHangupRequest.Decision.WAIT, stale.evaluate(oldStatus, scan, now),
                "older status sequence is ignored");
        StarHuntCommand wrongCommand = new StarHuntCommand(10L, StarHuntCommand.Mode.SCAN, 0, 0);
        eq(StarHuntHangupRequest.Decision.WAIT, stale.evaluate(noCandidate, wrongCommand, now),
                "different command sequence is ignored");
        check(stale.isPending(), "sequence mismatch does not consume pending request");

        StarHuntStatus noMap = StarHuntStatus.parse(
                "status=reading\nrole=r\nupdatedAt=2000\nacceptedSequence=9\n");
        eq(StarHuntHangupRequest.Decision.WAIT, stale.evaluate(noMap, scan, now),
                "missing map waits for usable current context");
        StarHuntStatus noRole = StarHuntStatus.parse(
                "status=reading\nmap=310\nupdatedAt=2000\nacceptedSequence=9\n");
        eq(StarHuntHangupRequest.Decision.WAIT, stale.evaluate(noRole, scan, now),
                "missing role waits for usable current context");
        eq(StarHuntHangupRequest.Decision.WAIT, stale.evaluate(null, scan, now),
                "status read exception or unavailable status cannot authorize run");

        StarHuntHangupRequest expired = new StarHuntHangupRequest(
                StarHuntSelection.of(4, 8), 9L, now - 60_000L);
        eq(StarHuntHangupRequest.Decision.EXPIRED, expired.evaluate(noCandidate, scan, now),
                "request expires at sixty seconds");
        check(!expired.isPending(), "expired request is cleared");

        StarHuntHangupRequest cancelled = new StarHuntHangupRequest(
                StarHuntSelection.of(4, 8), 9L, now - 1_000L);
        cancelled.cancel();
        check(!cancelled.isPending(), "explicit cancellation clears request");
        check(!cancelled.markSubmitted(9L), "cancelled request cannot be submitted");
        StarHuntStatus fatal = StarHuntStatus.parse("status=fatal\nacceptedSequence=9\n");
        eq(StarHuntHangupRequest.Decision.CANCEL,
                new StarHuntHangupRequest(StarHuntSelection.of(4, 8), 9L, now - 1_000L)
                        .evaluate(fatal, scan, now),
                "fatal status cancels pending request");
        rejects(() -> new StarHuntHangupRequest(
                        StarHuntSelection.empty(), 9L, now - 1_000L),
                "empty selections cannot create a hangup request");
        System.out.println("StarHuntHangupRequestSelfTest PASS (" + checks + " assertions)");
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

    private static void rejects(Runnable action, String message) {
        checks++;
        try {
            action.run();
        } catch (IllegalArgumentException expected) {
            return;
        }
        throw new AssertionError(message);
    }
}
