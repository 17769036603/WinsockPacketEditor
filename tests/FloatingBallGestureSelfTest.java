package com.xnas.wpe.battlepanel;

public final class FloatingBallGestureSelfTest {
    private static int checks;

    public static void main(String[] args) {
        upWithoutFinalMoveUsesFullDeltaAndDoesNotClick();
        tapWithSmallJitterClicks();
        cancelNeverClicks();
        clampAtEachBoundary();
        multipleMovesUseTotalDeltaFromDown();
        System.out.println("FloatingBallGestureSelfTest: PASS (" + checks + " assertions)");
    }

    private static void upWithoutFinalMoveUsesFullDeltaAndDoesNotClick() {
        FloatingBallGesture gesture = new FloatingBallGesture();
        gesture.begin(7, 100, 100, 40, 60, 8);
        FloatingBallGesture.Result result = gesture.release(7, 360, 220, 0, 900, 16, 1600);
        require(result != null, "UP result exists");
        require(result.x == 300 && result.y == 180, "UP applies complete delta without final MOVE");
        require(result.dragging && !result.shouldClick && !result.active, "drag release cannot click and ends gesture");
    }

    private static void tapWithSmallJitterClicks() {
        FloatingBallGesture gesture = new FloatingBallGesture();
        gesture.begin(1, 10, 20, 30, 40, 8);
        FloatingBallGesture.Result moved = gesture.move(1, 15, 24, 0, 500, 16, 800);
        require(moved != null && !moved.dragging, "sub-slop jitter stays a tap");
        FloatingBallGesture.Result up = gesture.release(1, 15, 24, 0, 500, 16, 800);
        require(up.shouldClick && !up.dragging, "tap with jitter clicks");
        require(up.x == 30 && up.y == 40, "tap jitter does not move ball");
    }

    private static void cancelNeverClicks() {
        FloatingBallGesture gesture = new FloatingBallGesture();
        gesture.begin(2, 100, 100, 50, 50, 8);
        gesture.move(2, 150, 100, 0, 500, 16, 800);
        FloatingBallGesture.Result canceled = gesture.cancel();
        require(canceled != null && canceled.dragging && !canceled.shouldClick && !canceled.active,
                "cancel after drag never clicks and ends gesture");
    }

    private static void clampAtEachBoundary() {
        assertClamped(0, 25, 25, -200, -200);
        assertClamped(1, 25, 25, 200, -200);
        assertClamped(2, 25, 25, -200, 200);
        assertClamped(3, 25, 25, 200, 200);
    }

    private static void assertClamped(int id, int x, int y, int dx, int dy) {
        FloatingBallGesture gesture = new FloatingBallGesture();
        gesture.begin(id, 0, 0, x, y, 1);
        FloatingBallGesture.Result result = gesture.release(id, dx, dy, 0, 100, 16, 120);
        require(result.x >= 0 && result.x <= 100, "x clamped to horizontal bounds");
        require(result.y >= 16 && result.y <= 120, "y clamped to vertical bounds");
    }

    private static void multipleMovesUseTotalDeltaFromDown() {
        FloatingBallGesture gesture = new FloatingBallGesture();
        gesture.begin(4, 100, 100, 100, 100, 5);
        FloatingBallGesture.Result first = gesture.move(4, 130, 120, 0, 500, 16, 800);
        FloatingBallGesture.Result second = gesture.move(4, 160, 145, 0, 500, 16, 800);
        FloatingBallGesture.Result third = gesture.release(4, 200, 180, 0, 500, 16, 800);
        require(first.x == 130 && first.y == 120, "first move uses total delta");
        require(second.x == 160 && second.y == 145, "second move does not double-count first delta");
        require(third.x == 200 && third.y == 180, "release uses total delta rather than incremental accumulation");
    }

    private static void require(boolean condition, String message) {
        checks++;
        if (!condition) throw new AssertionError(message);
    }
}


