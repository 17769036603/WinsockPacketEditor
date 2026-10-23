import com.xnas.wpe.suite.BattleWorkerStatus;

public final class BattleWorkerStatusSelfTest {
    public static void main(String[] args) {
        runningLinesAreRecognized();
        otherWorkerStatesAreNotRunning();
        System.out.println("BattleWorkerStatusSelfTest: PASS");
    }

    private static void runningLinesAreRecognized() {
        expect(true, BattleWorkerStatus.isRunning("running calls=63"), "running with count");
        expect(true, BattleWorkerStatus.isRunning("running calls=0\n"), "zero calls");
        expect(true, BattleWorkerStatus.isRunning("running calls=63\r\n"), "crlf line");
        expect(true, BattleWorkerStatus.isRunning("\n running calls=7 \n"), "surrounding whitespace");
        expect("state=running\n", BattleWorkerStatus.normalizedRunning("running calls=63"), "normalized running");
    }

    private static void otherWorkerStatesAreNotRunning() {
        expect(false, BattleWorkerStatus.isRunning(null), "null");
        expect(false, BattleWorkerStatus.isRunning(""), "empty");
        expect(false, BattleWorkerStatus.isRunning("running"), "bare running without count");
        expect(false, BattleWorkerStatus.isRunning("running calls=x"), "non numeric count");
        expect(false, BattleWorkerStatus.isRunning("running calls=63 extra"), "trailing text");
        expect(false, BattleWorkerStatus.isRunning("status=running\ncalls=3"), "lua key value form");
        expect(false, BattleWorkerStatus.isRunning("waiting_game count=2"), "waiting is not running");
        expect(false, BattleWorkerStatus.isRunning("state=stopped\nhooksRemoved=true\nbindingCleared=true"), "stopped");
        expect(null, BattleWorkerStatus.normalizedRunning("stopped"), "normalized stopped");
    }

    private static void expect(Object expected, Object actual, String name) {
        boolean matches = expected == null ? actual == null : expected.equals(actual);
        if (!matches) throw new AssertionError(name + ": expected " + expected + " but was " + actual);
    }
}
