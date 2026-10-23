package android.view;
public class View {
 public interface OnTouchListener { boolean onTouch(View view, MotionEvent event); }
 private OnTouchListener listener; private float translationX,translationY;
 public int clickCount,hapticCount,unbufferedCount; public boolean pressed;
 public void setOnTouchListener(OnTouchListener value){listener=value;} public boolean dispatch(MotionEvent event){return listener.onTouch(this,event);}
 public void requestUnbufferedDispatch(MotionEvent event){unbufferedCount++;} public void setPressed(boolean value){pressed=value;}
 public boolean performClick(){clickCount++;return true;} public boolean performHapticFeedback(int constant){hapticCount++;return true;}
 public void setTranslationX(float value){translationX=value;} public void setTranslationY(float value){translationY=value;}
 public float getTranslationX(){return translationX;} public float getTranslationY(){return translationY;}
 public void getLocationOnScreen(int[] xy){xy[0]=(int)translationX;xy[1]=(int)translationY;}
}
