$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$java = Join-Path $root 'app/src/main/java/com/xnas/wpe/battlepanel/BattleOverlayService.java'
$helper = Join-Path $root 'app/src/main/java/com/xnas/wpe/battlepanel/FloatingBallGesture.java'
$source = Get-Content -Raw $java
$methodNames = @('installBallDragListener','cancelBallGesture','finishBallGesture','expandBallHostForDrag','restoreBallHostAfterGesture','applyBallGesture','rawPointerX','rawPointerY','ballMinX','ballMaxX','ballMinY','ballMaxY','isPointerOverBall','rememberBallCenter')
$methods = foreach ($name in $methodNames) {
    $match = [regex]::Match($source, "(?m)^    private [\w<>]+ $name\(")
    if (-not $match.Success) { throw "Could not find production method $name" }
    $open = $source.IndexOf('{', $match.Index)
    $depth = 0; $end = -1
    for ($i = $open; $i -lt $source.Length; $i++) {
        if ($source[$i] -eq '{') { $depth++ }
        elseif ($source[$i] -eq '}') { $depth--; if ($depth -eq 0) { $end = $i + 1; break } }
    }
    if ($end -lt 0) { throw "Unbalanced production method $name" }
    $source.Substring($match.Index, $end - $match.Index)
}
$out = Join-Path $PSScriptRoot 'src/com/xnas/wpe/battlepanel/BattleOverlayAdapterHarness.java'
$prefix = @'
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
    public static void main(String[] args){BattleOverlayAdapterHarness h=new BattleOverlayAdapterHarness();h.testUpWithoutMove();h.testMoveThenUpTail();h.testTapAndCancel();h.testFixedPointerIdentity();h.testOutsideDownAfterCancelIgnored();System.out.println("BattleOverlayAdapterHarness: PASS (production host-window adapter extracted)");}
'@
Set-Content -Encoding utf8 $out ($prefix + ($methods -join "`r`n") + "`r`n}`r`n")
$jdk = 'C:/Users/Administrator/AppData/Local/Programs/Microsoft/jdk-17.0.20+8'
$classes = Join-Path $PSScriptRoot 'classes'
New-Item -ItemType Directory -Force $classes | Out-Null
$stubFiles = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.java' -Recurse | ForEach-Object FullName
& "$jdk/bin/javac.exe" -encoding UTF-8 -d $classes $stubFiles $helper $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$jdk/bin/java.exe" -cp $classes com.xnas.wpe.battlepanel.BattleOverlayAdapterHarness
exit $LASTEXITCODE
