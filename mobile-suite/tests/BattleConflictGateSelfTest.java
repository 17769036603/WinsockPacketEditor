import com.xnas.wpe.suite.BattleConflictGate;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicInteger;

public final class BattleConflictGateSelfTest {
    public static void main(String[] args) throws Exception {
        concurrentStartsHaveOneWinner();
        unpublishedReservationBlocksPreviousCleanStates();
        targetSwitchRequiresCleanTerminalState();
        unconfirmedCleanupKeepsReservation();
        persistedReservationSurvivesAnotherGateCall();
        failedDispatchCanClearOnlyItsStartingReservation();
        verifiedEndedProcessReleasesOnlyTheOldLoop();
        invalidEndedProofKeepsProtection();
        skipRecoveryRequiresItsOwnVerifiedProof();
        System.out.println("BattleConflictGateSelfTest: PASS");
    }

    private static String endedProof(String proof) {
        return "state=stopped\nprocessEnded=true\nrecoveryVerified=true\n"
                + "package=com.gdoo.dhxy\nrecoveryProof=" + proof
                + "\npid=4810\nstartTicks=123456\n";
    }

    private static void skipRecoveryRequiresItsOwnVerifiedProof() throws Exception {
        File modules = Files.createTempDirectory("suite-skip-recovery").toFile();
        require(BattleConflictGate.reserve(modules, "battleskip").allowed, "skip reserves");
        require(BattleConflictGate.hasUnconfirmedSkipActivity(modules), "starting skip needs proof");
        String valid = endedProof("original_process_ended") + "recoveryModule=battleskip\n";
        writeStatus(modules, "battleloop", valid);
        require(!BattleConflictGate.isClean(modules, "battleloop"), "skip proof cannot clean loop");
        for (String invalid : new String[]{valid.replace("battleskip", "battleloop"),
                valid.replace("pid=4810", "pid=0"),
                valid.replace("recoveryVerified=true", "recoveryVerified=false"),
                valid.replace("original_process_ended", "process_unreadable")}) {
            writeStatus(modules, "battleskip", invalid);
            require(!BattleConflictGate.release(modules, "battleskip"), "invalid skip proof keeps guard");
        }
        writeStatus(modules, "battleskip", valid);
        writeStatus(modules, "battleloop", "state=running\n");
        require(BattleConflictGate.release(modules, "battleskip"), "verified skip process releases only its guard");
        require(!BattleConflictGate.hasUnconfirmedSkipActivity(modules), "verified skip is clean");
        require(!BattleConflictGate.reserve(modules, "battleskip").allowed, "unconfirmed loop still blocks skip");
    }

    private static void verifiedEndedProcessReleasesOnlyTheOldLoop() throws Exception {
        File modules = Files.createTempDirectory("suite-gate-recovery").toFile();
        require(BattleConflictGate.reserve(modules, "battleloop").allowed, "loop reserves first");
        writeStatus(modules, "battleloop", "state=stopping\nreason=cleanup_unconfirmed\n");
        require(!BattleConflictGate.reserve(modules, "battleskip").allowed, "old loop blocks skip");
        String proof = endedProof("original_process_ended");
        require(!proof.contains("hooksRemoved=true"), "ended proof must not invent hook cleanup");
        writeStatus(modules, "battleloop", proof);
        require(BattleConflictGate.release(modules, "battleloop"), "verified exit releases old loop");
        require(!new File(new File(modules, "battleskip"), "gate-status").exists(),
                "resolved denial no longer shows skip as blocked");
        require(!BattleConflictGate.hasUnconfirmedActivity(modules), "target switch is unlocked");
        require(BattleConflictGate.reserve(modules, "battleskip").allowed, "skip can then reserve");
        writeStatus(modules, "battleloop", endedProof("no_target_process"));
        require(BattleConflictGate.isClean(modules, "battleloop"), "strict no-target proof is terminal");
        require(BattleConflictGate.hasUnconfirmedActivity(modules), "skip reservation is not cleared");
    }

    private static void invalidEndedProofKeepsProtection() throws Exception {
        File modules = Files.createTempDirectory("suite-gate-invalid-proof").toFile();
        String proof = endedProof("original_process_ended");
        for (String bad : new String[] {
                proof.replace("recoveryVerified=true", "recoveryVerified=false"),
                proof.replace("processEnded=true", "processEnded=false"),
                proof.replace("com.gdoo.dhxy", "com.gdoo.xxxy2026"),
                proof.replace("pid=4810", "pid=0"),
                proof.replace("startTicks=123456", "startTicks=unknown"),
                proof.replace("original_process_ended", "process_unreadable")}) {
            writeStatus(modules, "battleloop", bad);
            require(!BattleConflictGate.isClean(modules, "battleloop"), "invalid proof stays blocked");
        }
        writeStatus(modules, "battleskip", proof);
        require(!BattleConflictGate.isClean(modules, "battleskip"), "loop proof cannot clean skip");
    }

    private static void targetSwitchRequiresCleanTerminalState() throws Exception {
        File modules = Files.createTempDirectory("suite-target-lock").toFile();
        require(!BattleConflictGate.hasUnconfirmedActivity(modules),
                "target switch should be allowed before either battle module has run");
        require(BattleConflictGate.reserve(modules, "battleloop").allowed,
                "loop should reserve before target lock assertion");
        require(BattleConflictGate.hasUnconfirmedActivity(modules),
                "target switch must remain locked while a battle reservation is active");
        writeStatus(modules, "battleloop", cleanLoopStatus());
        require(!BattleConflictGate.hasUnconfirmedActivity(modules),
                "confirmed clean battle cleanup should unlock target switching");
        writeStatus(modules, "battleskip", "state=error\n");
        require(BattleConflictGate.hasUnconfirmedActivity(modules),
                "unconfirmed worker status must keep target switching locked");
    }

    private static void unpublishedReservationBlocksPreviousCleanStates() throws Exception {
        File modules = Files.createTempDirectory("suite-gate-unpublished").toFile();
        writeStatus(modules, "battleloop", cleanLoopStatus());
        writeStatus(modules, "battleskip", cleanSkipStatus());
        File marker = new File(modules, ".battle-conflict.guard");
        require(marker.createNewFile(), "reservation marker should be created empty");
        require(!BattleConflictGate.reserve(modules, "battleloop").allowed,
                "an unpublished reservation must block loop despite old clean states");
        require(!BattleConflictGate.reserve(modules, "battleskip").allowed,
                "an unpublished reservation must block skip despite old clean states");
        require(BattleConflictGate.hasUnconfirmedActivity(modules),
                "target switching must remain locked during reservation publication");
        writeStatus(modules, "battleloop", "state=starting\n");
        Files.write(marker.toPath(), "battleloop\n".getBytes(StandardCharsets.UTF_8));
        require(!BattleConflictGate.reserve(modules, "battleskip").allowed,
                "published starting owner must continue excluding skip");
        writeStatus(modules, "battleloop", cleanLoopStatus());
        require(BattleConflictGate.release(modules, "battleloop"),
                "only confirmed cleanup may release the published owner");
    }

    private static void concurrentStartsHaveOneWinner() throws Exception {
        File modules = Files.createTempDirectory("suite-gate-race").toFile();
        CountDownLatch ready = new CountDownLatch(2);
        CountDownLatch go = new CountDownLatch(1);
        AtomicInteger winners = new AtomicInteger();
        Thread loop = contender(modules, "battleloop", ready, go, winners);
        Thread skip = contender(modules, "battleskip", ready, go, winners);
        loop.start();
        skip.start();
        ready.await();
        go.countDown();
        loop.join();
        skip.join();
        require(winners.get() == 1, "concurrent reservation must have exactly one winner");
    }

    private static Thread contender(File modules, String id, CountDownLatch ready,
                                    CountDownLatch go, AtomicInteger winners) {
        return new Thread(() -> {
            ready.countDown();
            try { go.await(); } catch (InterruptedException error) { throw new AssertionError(error); }
            if (BattleConflictGate.reserve(modules, id).allowed) winners.incrementAndGet();
        });
    }

    private static void unconfirmedCleanupKeepsReservation() throws Exception {
        File modules = Files.createTempDirectory("suite-gate-cleanup").toFile();
        require(BattleConflictGate.reserve(modules, "battleloop").allowed, "loop should reserve fresh state");
        writeStatus(modules, "battleloop", "state=stopped\nhooksRemoved=true\nbindingCleared=true\n");
        require(!BattleConflictGate.release(modules, "battleloop"), "loop must require restore confirmation");
        require(!BattleConflictGate.reserve(modules, "battleskip").allowed,
                "unconfirmed loop cleanup must block skip");
        writeStatus(modules, "battleloop", cleanLoopStatus());
        require(BattleConflictGate.release(modules, "battleloop"), "confirmed loop cleanup should release");
        require(BattleConflictGate.reserve(modules, "battleskip").allowed,
                "skip should reserve after confirmed loop cleanup");
        writeStatus(modules, "battleskip", cleanSkipStatus());
        require(BattleConflictGate.release(modules, "battleskip"), "confirmed skip cleanup should release");
    }

    private static void persistedReservationSurvivesAnotherGateCall() throws Exception {
        File modules = Files.createTempDirectory("suite-gate-persist").toFile();
        require(BattleConflictGate.reserve(modules, "battleskip").allowed, "skip should reserve fresh state");
        require(!BattleConflictGate.reserve(modules, "battleloop").allowed,
                "a persisted non-terminal marker must remain locked");
        require(new File(modules, ".battle-conflict.guard").isFile(), "reservation marker must persist");
        writeStatus(modules, "battleskip", cleanSkipStatus());
        require(BattleConflictGate.release(modules, "battleskip"), "terminal status should clear marker");
    }

    private static void failedDispatchCanClearOnlyItsStartingReservation() throws Exception {
        File modules = Files.createTempDirectory("suite-gate-abort").toFile();
        require(BattleConflictGate.reserve(modules, "battleskip").allowed,
                "skip should reserve before dispatch");
        require(BattleConflictGate.abortBeforeDispatch(modules, "battleskip"),
                "synchronous dispatch failure may clear its untouched starting reservation");
        require(!new File(modules, ".battle-conflict.guard").exists(),
                "aborted dispatch should remove its reservation");
        require(BattleConflictGate.isClean(modules, "battleskip"),
                "aborted dispatch must leave a proven no-worker terminal state");

        require(BattleConflictGate.reserve(modules, "battleskip").allowed,
                "skip may reserve again after proven abort");
        writeStatus(modules, "battleskip", "state=stopping\n");
        require(!BattleConflictGate.abortBeforeDispatch(modules, "battleskip"),
                "abort must not release a state that left starting");
        require(new File(modules, ".battle-conflict.guard").isFile(),
                "uncertain worker state must remain reserved");
    }

    private static String cleanLoopStatus() {
        return "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n";
    }

    private static String cleanSkipStatus() {
        return "state=stopped\nhooksRemoved=true\nbindingCleared=true\n";
    }

    private static void writeStatus(File modules, String id, String content) throws Exception {
        File directory = new File(modules, id);
        Files.createDirectories(directory.toPath());
        Files.write(new File(directory, "status").toPath(), content.getBytes(StandardCharsets.UTF_8));
    }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
