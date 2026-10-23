package com.xnas.wpe.battlepanel;
import android.os.Build;
import android.view.MotionEvent;
import android.view.View;
import android.view.FrameLayout;
import android.view.Gravity;
import android.view.ViewConfiguration;
import android.view.WindowManager;

public final class BattleOverlayAdapterHarness {
    WindowManager.LayoutParams ballParams = new WindowManager.LayoutParams(40,60,76,76);
    WindowManager.LayoutParams ballHostParams = new WindowManager.LayoutParams(40,60,76,76);
    View overlayBallView; FrameLayout overlayBallHostView;
    final WindowManager windowManager = new WindowManager();
    int lastBallCenterX,lastBallCenterY; boolean hasLastBallCenter;
    int displayWidth(){return 500;} int displayHeight(){return 900;} int overlayDp(int value){return value;}
    void removeOverlay(View view){}
    private void require(boolean ok,String message){if(!ok)throw new AssertionError(message);}
    private void setup(){windowManager.updates=0;ballParams.x=40;ballParams.y=60;ballParams.width=76;ballParams.height=76;ballHostParams.x=40;ballHostParams.y=60;ballHostParams.width=76;ballHostParams.height=76;overlayBallHostView=new FrameLayout();overlayBallView=new View();installBallDragListener(overlayBallHostView,overlayBallView);}
    private void down(int id,float x,float y){overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_DOWN,id,x,y));}
    private void smallWindow(String label){require(ballHostParams.width==76&&ballHostParams.height==76,label+" restores small window size");require(ballHostParams.x==ballParams.x&&ballHostParams.y==ballParams.y,label+" restores host to virtual position");require(overlayBallView.getTranslationX()==0&&overlayBallView.getTranslationY()==0,label+" clears child translation");}
    private void testUpWithoutMove(){setup();down(7,100,100);require(ballHostParams.width==500&&ballHostParams.height==900,"DOWN expands host to full display");overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_UP,7,360,220));require(ballParams.x==300&&ballParams.y==180,"UP without MOVE applies complete delta");require(overlayBallHostView.clickCount==0,"drag release does not click");smallWindow("UP without MOVE");require(overlayBallHostView.unbufferedCount==1,"DOWN requests unbuffered dispatch");}
    private void testMoveThenUpTail(){setup();down(7,100,100);overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_MOVE,7,200,150));require(ballParams.x==140&&ballParams.y==110,"MOVE updates virtual position");require(windowManager.updates==1,"MOVE does not update full-screen window geometry");overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_UP,7,360,220));require(ballParams.x==300&&ballParams.y==180,"UP tail applies complete delta from DOWN");require(overlayBallHostView.clickCount==0,"drag does not click");smallWindow("drag UP");}
    private void testTapAndCancel(){setup();down(7,100,100);overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_UP,7,102,102));require(overlayBallHostView.clickCount==1,"tap clicks once after restore");require(ballParams.x==40&&ballParams.y==60,"tap leaves position fixed");smallWindow("tap");setup();down(7,100,100);overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_MOVE,7,160,130));overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_CANCEL,7,160,130));require(overlayBallHostView.clickCount==0,"CANCEL never clicks");smallWindow("CANCEL");}
    private void testFixedPointerIdentity(){setup();down(17,100,100);overlayBallHostView.dispatch(MotionEvent.multi(MotionEvent.ACTION_POINTER_DOWN,1,new int[]{17,99},new float[]{110,300},new float[]{105,300}));overlayBallHostView.dispatch(MotionEvent.multi(MotionEvent.ACTION_MOVE,0,new int[]{99,17},new float[]{400,180},new float[]{400,140}));require(ballParams.x==120&&ballParams.y==100,"reordered pointer array follows original pointer id");overlayBallHostView.dispatch(MotionEvent.multi(MotionEvent.ACTION_POINTER_UP,0,new int[]{99,17},new float[]{400,180},new float[]{400,140}));require(ballHostParams.width==500&&ballHostParams.height==900,"secondary pointer up keeps primary gesture active");overlayBallHostView.dispatch(MotionEvent.multi(MotionEvent.ACTION_MOVE,0,new int[]{17,99},new float[]{200,450},new float[]{160,450}));require(ballParams.x==140&&ballParams.y==120,"continued movement follows fixed primary pointer");overlayBallHostView.dispatch(MotionEvent.multi(MotionEvent.ACTION_POINTER_UP,0,new int[]{17,99},new float[]{200,450},new float[]{160,450}));require(overlayBallHostView.clickCount==0,"primary pointer up cancels without click");smallWindow("primary pointer up");}
    private void testOutsideDownAfterCancelIgnored(){setup();down(7,100,100);overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_MOVE,7,160,130));overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_CANCEL,7,160,130));int x=ballParams.x,y=ballParams.y;float tx=overlayBallView.getTranslationX(),ty=overlayBallView.getTranslationY();smallWindow("CANCEL before outside DOWN");require(!overlayBallView.pressed,"CANCEL clears child pressed state");overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_DOWN,8,430,600));require(ballParams.x==x&&ballParams.y==y,"outside DOWN cannot change virtual position");require(overlayBallView.getTranslationX()==tx&&overlayBallView.getTranslationY()==ty,"outside DOWN preserves child translation");require(!overlayBallView.pressed,"outside DOWN clears pressed state");smallWindow("outside DOWN");overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_MOVE,8,470,650));overlayBallHostView.dispatch(MotionEvent.one(MotionEvent.ACTION_UP,8,470,650));require(ballParams.x==x&&ballParams.y==y,"inactive MOVE and UP cannot move ball");require(overlayBallView.getTranslationX()==tx&&overlayBallView.getTranslationY()==ty,"inactive MOVE and UP preserve translation");smallWindow("inactive MOVE and UP");}
    public static void main(String[] args){BattleOverlayAdapterHarness h=new BattleOverlayAdapterHarness();h.testUpWithoutMove();h.testMoveThenUpTail();h.testTapAndCancel();h.testFixedPointerIdentity();h.testOutsideDownAfterCancelIgnored();System.out.println("BattleOverlayAdapterHarness: PASS (production host-window adapter extracted)");}    private void installBallDragListener(FrameLayout host, View ball) {
        final FloatingBallGesture gesture = new FloatingBallGesture();
        final int[] trackedPointerId = new int[] { -1 };
        final int touchSlop = ViewConfiguration.get(this).getScaledTouchSlop();
        host.setOnTouchListener((view, event) -> {
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    int downIndex = event.getActionIndex();
                    float downRawX = rawPointerX(event, downIndex);
                    float downRawY = rawPointerY(event, downIndex);
                    if (!isPointerOverBall(downRawX, downRawY)) {
                        gesture.cancel();
                        trackedPointerId[0] = -1;
                        ball.setPressed(false);
                        restoreBallHostAfterGesture();
                        return true;
                    }
                    trackedPointerId[0] = event.getPointerId(downIndex);
                    gesture.begin(trackedPointerId[0], downRawX, downRawY,
                            ballParams == null ? 0 : ballParams.x,
                            ballParams == null ? 0 : ballParams.y, touchSlop);
                    view.requestUnbufferedDispatch(event);
                    ball.setPressed(true);
                    if (!expandBallHostForDrag()) {
                        gesture.cancel();
                        trackedPointerId[0] = -1;
                        ball.setPressed(false);
                    }
                    return true;
                case MotionEvent.ACTION_MOVE:
                    int moveIndex = event.findPointerIndex(trackedPointerId[0]);
                    if (moveIndex < 0) {
                        cancelBallGesture(gesture, trackedPointerId, ball);
                        return true;
                    }
                    FloatingBallGesture.Result moved = gesture.move(trackedPointerId[0],
                            rawPointerX(event, moveIndex), rawPointerY(event, moveIndex),
                            ballMinX(), ballMaxX(), ballMinY(), ballMaxY());
                    applyBallGesture(view, ball, moved);
                    if (moved != null && moved.dragging) ball.setPressed(false);
                    return true;
                case MotionEvent.ACTION_UP:
                    int upIndex = event.findPointerIndex(trackedPointerId[0]);
                    if (upIndex >= 0) {
                        finishBallGesture(view, ball, gesture.release(trackedPointerId[0],
                                rawPointerX(event, upIndex), rawPointerY(event, upIndex),
                                ballMinX(), ballMaxX(), ballMinY(), ballMaxY()));
                    } else {
                        cancelBallGesture(gesture, trackedPointerId, ball);
                    }
                    trackedPointerId[0] = -1;
                    return true;
                case MotionEvent.ACTION_POINTER_UP:
                    int liftedIndex = event.getActionIndex();
                    if (event.getPointerId(liftedIndex) == trackedPointerId[0]) {
                        cancelBallGesture(gesture, trackedPointerId, ball);
                    }
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    cancelBallGesture(gesture, trackedPointerId, ball);
                    return true;
                default:
                    return true;
            }
        });
    }
    private void cancelBallGesture(FloatingBallGesture gesture, int[] trackedPointerId,
                                   View ball) {
        gesture.cancel();
        trackedPointerId[0] = -1;
        ball.setPressed(false);
        restoreBallHostAfterGesture();
    }
    private void finishBallGesture(View host, View ball,
                                   FloatingBallGesture.Result result) {
        applyBallGesture(host, ball, result);
        boolean restored = restoreBallHostAfterGesture();
        ball.setPressed(false);
        if (!restored) return;
        if (result == null) return;
        if (result.dragging) {
            host.performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY);
        } else if (result.shouldClick) {
            host.performClick();
        }
    }
    private boolean expandBallHostForDrag() {
        FrameLayout host = overlayBallHostView;
        if (host == null || overlayBallView == null || ballHostParams == null
                || ballParams == null || windowManager == null) {
            return false;
        }
        overlayBallView.setTranslationX(ballParams.x);
        overlayBallView.setTranslationY(ballParams.y);
        ballHostParams.gravity = Gravity.TOP | Gravity.START;
        ballHostParams.x = 0;
        ballHostParams.y = 0;
        ballHostParams.width = displayWidth();
        ballHostParams.height = displayHeight();
        try {
            windowManager.updateViewLayout(host, ballHostParams);
            return true;
        } catch (RuntimeException ignored) {
            overlayBallView.setTranslationX(0f);
            overlayBallView.setTranslationY(0f);
            ballHostParams.x = ballParams.x;
            ballHostParams.y = ballParams.y;
            ballHostParams.width = ballParams.width;
            ballHostParams.height = ballParams.height;
            restoreBallHostAfterGesture();
            return false;
        }
    }
    private boolean restoreBallHostAfterGesture() {
        FrameLayout host = overlayBallHostView;
        if (host == null || overlayBallView == null || ballHostParams == null
                || ballParams == null || windowManager == null) {
            return false;
        }
        overlayBallView.setTranslationX(0f);
        overlayBallView.setTranslationY(0f);
        ballHostParams.gravity = Gravity.TOP | Gravity.START;
        ballHostParams.x = ballParams.x;
        ballHostParams.y = ballParams.y;
        ballHostParams.width = ballParams.width;
        ballHostParams.height = ballParams.height;
        try {
            windowManager.updateViewLayout(host, ballHostParams);
            return true;
        } catch (RuntimeException ignored) {
            removeOverlay(host);
            overlayBallHostView = null;
            overlayBallView = null;
            ballHostParams = null;
            ballParams = null;
            return false;
        }
    }
    private void applyBallGesture(View host, View ball,
                                  FloatingBallGesture.Result result) {
        if (result == null || ballParams == null || overlayBallHostView != host
                || overlayBallView != ball) {
            return;
        }
        ballParams.x = result.x;
        ballParams.y = result.y;
        rememberBallCenter(ballParams.x + ballParams.width / 2,
                ballParams.y + ballParams.height / 2);
        ball.setTranslationX(ballParams.x);
        ball.setTranslationY(ballParams.y);
    }
    private float rawPointerX(MotionEvent event, int pointerIndex) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) return event.getRawX(pointerIndex);
        return event.getRawX() + event.getX(pointerIndex) - event.getX(0);
    }
    private float rawPointerY(MotionEvent event, int pointerIndex) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) return event.getRawY(pointerIndex);
        return event.getRawY() + event.getY(pointerIndex) - event.getY(0);
    }
    private int ballMinX() {
        return 0;
    }
    private int ballMaxX() {
        return Math.max(0, displayWidth() - (ballParams == null ? 0 : ballParams.width));
    }
    private int ballMinY() {
        return overlayDp(16);
    }
    private int ballMaxY() {
        return Math.max(ballMinY(), displayHeight()
                - (ballParams == null ? 0 : ballParams.height) - overlayDp(16));
    }
    private boolean isPointerOverBall(float rawX, float rawY) {
        return ballParams != null
                && rawX >= ballParams.x && rawX < (double) ballParams.x + ballParams.width
                && rawY >= ballParams.y && rawY < (double) ballParams.y + ballParams.height;
    }
    private void rememberBallCenter(int centerX, int centerY) {
        lastBallCenterX = centerX;
        lastBallCenterY = centerY;
        hasLastBallCenter = true;
    }
}

