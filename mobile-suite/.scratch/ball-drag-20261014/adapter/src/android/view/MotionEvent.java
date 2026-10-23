package android.view;
public final class MotionEvent {
 public static final int ACTION_DOWN=0,ACTION_UP=1,ACTION_MOVE=2,ACTION_CANCEL=3,ACTION_POINTER_UP=6,ACTION_POINTER_DOWN=5;
 private final int action,index; private final int[] ids; private final float[] rawX,rawY,x,y;
 public MotionEvent(int action,int index,int[] ids,float[] rawX,float[] rawY,float[] x,float[] y){this.action=action;this.index=index;this.ids=ids;this.rawX=rawX;this.rawY=rawY;this.x=x;this.y=y;}
 public int getActionMasked(){return action;} public int getActionIndex(){return index;} public int getPointerId(int i){return ids[i];} public int findPointerIndex(int id){for(int i=0;i<ids.length;i++)if(ids[i]==id)return i;return -1;}
 public float getRawX(int i){return rawX[i];} public float getRawY(int i){return rawY[i];} public float getRawX(){return rawX[0];} public float getRawY(){return rawY[0];} public float getX(int i){return x[i];} public float getY(int i){return y[i];}
 public int getSource(){return 0;} public int getDeviceId(){return 0;} public float getX(){return x[0];} public float getY(){return y[0];}
 public static MotionEvent one(int action,int id,float rx,float ry){return new MotionEvent(action,0,new int[]{id},new float[]{rx},new float[]{ry},new float[]{rx},new float[]{ry});}
 public static MotionEvent multi(int action,int actionIndex,int[] ids,float[] rx,float[] ry){return new MotionEvent(action,actionIndex,ids,rx,ry,rx,ry);}
}
