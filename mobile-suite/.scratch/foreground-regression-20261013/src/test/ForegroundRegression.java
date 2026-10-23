package test;
import android.content.Intent;
import com.xnas.wpe.battleloop.BattleLoopService;
import com.xnas.wpe.suite.BattleConflictGate;
import com.xnas.wpe.suite.TargetGame;
import java.io.*;
import java.lang.reflect.Field;
import java.nio.file.Files;

public final class ForegroundRegression {
  public static void main(String[] args) throws Exception {
    if (args.length==0 || "check".equals(args[0])) checkWhileWorkerRunning();
    if (args.length==0 || "unsupported".equals(args[0])) unsupportedStart();
    if (args.length==0 || "denied".equals(args[0])) deniedStart();
    stopPathDoesNotThrow();
    System.out.println("ForegroundRegression: PASS");
  }
  static void checkWhileWorkerRunning() throws Exception {
    BattleLoopService s=new BattleLoopService(); s.onCreate();
    Field w=BattleLoopService.class.getDeclaredField("worker"); w.setAccessible(true); w.set(s,new LiveProcess());
    s.onStartCommand(new Intent().setAction(BattleLoopService.CHECK),0,1);
    require(s.foregroundCount>0,"CHECK with running worker must register foreground before returning");
    s.onDestroy();
  }
  static void unsupportedStart() {
    TargetGame.supported=false; BattleLoopService s=new BattleLoopService(); s.onCreate();
    s.onStartCommand(new Intent().setAction(BattleLoopService.START),0,2);
    require(s.foregroundCount>0,"unsupported START must register foreground before returning");
    s.onDestroy(); TargetGame.supported=true;
  }
  static void deniedStart() throws Exception {
    BattleLoopService s=new BattleLoopService(); s.onCreate();
    File modules=s.getFilesDir().getParentFile();
    require(BattleConflictGate.reserve(modules,"battleskip").allowed,"fixture skip reservation");
    s.onStartCommand(new Intent().setAction(BattleLoopService.START),0,3);
    require(s.foregroundCount>0,"gate-denied START must register foreground before returning");
    s.onDestroy();
  }
  static void stopPathDoesNotThrow() {
    BattleLoopService s=new BattleLoopService(); s.onCreate();
    int result=s.onStartCommand(new Intent().setAction(BattleLoopService.STOP),0,4);
    require(result==2,"STOP returns START_NOT_STICKY"); s.onDestroy();
  }
  static void require(boolean v,String m){if(!v)throw new AssertionError(m);}
  static final class LiveProcess extends Process {
    public OutputStream getOutputStream(){return new ByteArrayOutputStream();}
    public InputStream getInputStream(){return new ByteArrayInputStream(new byte[0]);}
    public InputStream getErrorStream(){return new ByteArrayInputStream(new byte[0]);}
    public int waitFor(){return 0;} public int exitValue(){throw new IllegalThreadStateException();} public void destroy(){}
  }
}




