package com.xnas.wpe.battlepanel;
import android.view.MotionEvent;
public final class LegacyListenerHarness {
    WindowManager.LayoutParams ballParams=new WindowManager.LayoutParams(); View overlayBallView; final WindowManager windowManager=new WindowManager(); int clicks;
    int displayWidth(){return 500;} int displayHeight(){return 900;} int overlayDp(int n){return n;} int clamp(int v,int a,int b){return Math.max(a,Math.min(v,b));}
    static final class ViewConfiguration {static ViewConfiguration get(LegacyListenerHarness h){return new ViewConfiguration();} int getScaledTouchSlop(){return 8;}}
    static final class WindowManager {static final class LayoutParams {int x=40,y=60,width=56,height=56;} void updateViewLayout(View v,LayoutParams p){}}
    static final class View {interface OnTouchListener {boolean onTouch(View v,MotionEvent e);} OnTouchListener listener; Runnable click; boolean setPressed; void setOnTouchListener(OnTouchListener l){listener=l;} void setPressed(boolean p){setPressed=p;} boolean performClick(){if(click!=null)click.run();return true;} boolean performHapticFeedback(int i){return true;}}    private void installBallDragListener(View ball) {
        final float[] downRawX = new float[1];
        final float[] downRawY = new float[1];
        final int[] startX = new int[1];
        final int[] startY = new int[1];
        final boolean[] dragging = new boolean[1];
        final int touchSlop = ViewConfiguration.get(this).getScaledTouchSlop();
        ball.setOnTouchListener((view, event) -> {
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    downRawX[0] = event.getRawX();
                    downRawY[0] = event.getRawY();
                    startX[0] = ballParams == null ? 0 : ballParams.x;
                    startY[0] = ballParams == null ? 0 : ballParams.y;
                    dragging[0] = false;
                    view.setPressed(true);
                    return true;
                case MotionEvent.ACTION_MOVE:
                    float dx = event.getRawX() - downRawX[0];
                    float dy = event.getRawY() - downRawY[0];
                    if (!dragging[0]
                            && (Math.abs(dx) > touchSlop || Math.abs(dy) > touchSlop)) {
                        dragging[0] = true;
                    }
                    if (dragging[0] && ballParams != null && overlayBallView == view) {
                        ballParams.x = clamp(startX[0] + Math.round(dx), 0,
                                Math.max(0, displayWidth() - ballParams.width));
                        ballParams.y = clamp(startY[0] + Math.round(dy), overlayDp(16),
                                Math.max(overlayDp(16), displayHeight() - ballParams.height
                                        - overlayDp(16)));
                        try {
                            windowManager.updateViewLayout(view, ballParams);
                        } catch (RuntimeException ignored) {
                            // Window permission can be revoked while dragging.
                        }
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                    view.setPressed(false);
                    if (dragging[0]) {
                        view.performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY);
                    } else {
                        view.performClick();
                    }
                    dragging[0] = false;
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    view.setPressed(false);
                    dragging[0] = false;
                    return true;
                default:
                    return false;
            }
        });
    }    public static void main(String[] args){LegacyListenerHarness h=new LegacyListenerHarness();View v=new View();v.click=()->h.clicks++;h.overlayBallView=v;h.installBallDragListener(v);v.listener.onTouch(v,new MotionEvent(MotionEvent.ACTION_DOWN,100,100));v.listener.onTouch(v,new MotionEvent(MotionEvent.ACTION_UP,360,220));System.out.println("legacy exact listener: x="+h.ballParams.x+" y="+h.ballParams.y+" clicks="+h.clicks+" expected x=300 y=180 clicks=0");if(h.ballParams.x!=300||h.ballParams.y!=180||h.clicks!=0){System.out.println("LegacyListenerHarness: RED (expected regression failure reproduced)");System.exit(1);}System.out.println("LegacyListenerHarness: unexpected green");}
}
