package com.xnas.wpe.battleloop;

import android.app.*;
import android.content.*;
import android.os.*;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.*;

public final class BattleLoopService extends Service {
    static final String START="start", STOP="stop", OVERLAY="overlay";
    private final ScheduledExecutorService timer=Executors.newSingleThreadScheduledExecutor();
    private volatile java.lang.Process worker;
    private volatile boolean stopping, desiredRunning, stopPending;
    private volatile long lastLaunch, generation;
    private volatile int latestStartId;
    private volatile long stopGeneration;
    private volatile int stopStartId;
    private boolean monitorStarted;
    private File stop;
    private final Handler mainHandler=new Handler(Looper.getMainLooper());
    private WindowManager windowManager;
    private View overlay;
    private TextView overlayStatus;
    private WindowManager.LayoutParams overlayParams;
    private final Runnable overlayRefresh=new Runnable(){@Override public void run(){if(overlayStatus!=null){overlayStatus.setText(overlayText(readStatus()));mainHandler.postDelayed(this,200);}}};

    @Override public void onCreate(){super.onCreate();if(Build.VERSION.SDK_INT>=26){NotificationChannel c=new NotificationChannel("battle_loop","战斗循环助手",NotificationManager.IMPORTANCE_LOW);getSystemService(NotificationManager.class).createNotificationChannel(c);}}
    @Override public synchronized int onStartCommand(Intent i,int flags,int id){
        latestStartId=id;
        if(i!=null&&OVERLAY.equals(i.getAction())){if(desiredRunning)mainHandler.post(this::showOverlay);else if(stopPending)stopStartId=id;else stopSelfResult(id);return desiredRunning?START_STICKY:START_NOT_STICKY;}
        if(i!=null&&STOP.equals(i.getAction())){stopWorker(id);return START_NOT_STICKY;}
        generation++;stopping=false;desiredRunning=true;
        startForeground(41,notification("正在启动并监控游戏进程"));
        mainHandler.post(this::showOverlay);
        stop=new File(getFilesDir(),"stop");
        if(!stopPending){if(worker==null||!running(worker)){stop.delete();startWorker();}}
        ensureMonitor();return START_STICKY;
    }
    private synchronized void startWorker(){
        if(!desiredRunning||stopping||stopPending)return;
        long now=System.currentTimeMillis();if(lastLaunch!=0&&now-lastLaunch<10000)return;lastLaunch=now;
        try{
            File dir=getFilesDir();stop=new File(dir,"stop");stop.delete();writeLease();
            String bin=new File(getApplicationInfo().nativeLibraryDir,"libbattle_loop_worker.so").getAbsolutePath();
            worker=new ProcessBuilder("su","-c","'"+bin.replace("'","'\\''")+"' --dir '"+dir.getAbsolutePath().replace("'","'\\''")+"'").redirectErrorStream(true).redirectOutput(new File(dir,"worker.log")).start();
        }catch(Exception e){worker=null;writeStatus("state=error\ndetail="+e.getMessage());}
    }
    private synchronized void ensureMonitor(){if(monitorStarted)return;monitorStarted=true;timer.scheduleAtFixedRate(this::monitorTick,0,2,TimeUnit.SECONDS);}
    private synchronized void monitorTick(){
        if(desiredRunning&&!stopping)writeLease();
        java.lang.Process p=worker;if(p!=null&&!running(p))worker=null;
        if(stopPending&&worker==null){long expected=stopGeneration;if(desiredRunning&&!stopping){stopPending=false;stop.delete();startWorker();}else if(generation==expected&&stopping)finishStop(expected);}
        else if(desiredRunning&&!stopping&&worker==null)startWorker();
        if(desiredRunning||stopPending)getSystemService(NotificationManager.class).notify(41,notification(displayStatus(readStatus())));
    }
    private synchronized void stopWorker(int startId){
        if(stopping&&stopPending){stopStartId=startId;return;}
        stopping=true;desiredRunning=false;stopPending=true;stopGeneration=++generation;stopStartId=startId;
        stop=new File(getFilesDir(),"stop");
        try(FileOutputStream o=new FileOutputStream(stop)){o.write(1);o.getFD().sync();}catch(Exception e){writeStatus("state=stopping\ndetail=停止请求写入失败");}
        java.lang.Process p=worker;
        if(p==null){writeStatus("state=stopping\nreason=worker_not_started");finishStop(stopGeneration);return;}
        if(!running(p)){worker=null;finishStop(stopGeneration);return;}
        new Thread(()->{try{if(!p.waitFor(15,TimeUnit.SECONDS)){writeStatus("state=stopping\ndetail=等待游戏线程完成Lua清理");return;}synchronized(BattleLoopService.this){if(generation==stopGeneration&&stopping){worker=null;finishStop(stopGeneration);}}}catch(Exception ignored){}}).start();
        ensureMonitor();
    }
    private synchronized void finishStop(long expected){if(generation!=expected||!stopping||!stopPending)return;stopPending=false;String workerStatus=readStatus();String cleanupConfirmed=value(workerStatus,"state").equals("stopped")&&value(workerStatus,"hooksRemoved").equals("true")&&value(workerStatus,"bindingCleared").equals("true")?"\nhooksRemoved=true\nbindingCleared=true":"";writeStatus("state=stopped"+cleanupConfirmed);stopForeground(true);mainHandler.post(this::removeOverlay);stopSelfResult(stopStartId);}
    private void writeLease(){try(FileOutputStream o=new FileOutputStream(new File(getFilesDir(),"lease"))){o.write(Long.toString(System.currentTimeMillis()/1000).getBytes(StandardCharsets.UTF_8));o.getFD().sync();}catch(Exception ignored){}}
    private void writeStatus(String s){try(FileOutputStream o=new FileOutputStream(new File(getFilesDir(),"status"))){o.write(s.getBytes(StandardCharsets.UTF_8));}catch(Exception ignored){}}
    private String readStatus(){try(FileInputStream in=new FileInputStream(new File(getFilesDir(),"status"));ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))>0)out.write(b,0,n);return new String(out.toByteArray(),StandardCharsets.UTF_8);}catch(Exception e){return "state=waiting_game";}}
    private static String value(String s,String key){for(String line:s.split("\\n"))if(line.startsWith(key+"="))return line.substring(key.length()+1);return "";}
    private static String displayStatus(String s){String state=value(s,"state");if(state.equals("waiting_game"))return "等待游戏启动";if(state.equals("waiting_role"))return "等待角色登录";if(state.equals("waiting_battle"))return "等待战斗";if(state.equals("waiting_config"))return "等待当前自动技能设置";if(state.equals("waiting_conflict"))return "与其他战斗辅助冲突，已暂停";if(state.equals("running"))return "战斗循环运行中";if(state.equals("stopped"))return "已停止";if(state.equals("error"))return "运行失败，请查看日志";if(state.equals("stopping"))return "正在清理并恢复自动状态";return "正在监控游戏";}
    private static String overlayText(String s){String state=value(s,"state");if(state.equals("running"))return "战斗循环运行中";if(state.equals("waiting_battle"))return "等待战斗";if(state.equals("waiting_config"))return "等待技能设置";if(state.equals("waiting_conflict"))return "辅助冲突";if(state.equals("stopped"))return "已停止";if(state.equals("error"))return "运行异常";if(state.equals("stopping"))return "正在停止";return "等待游戏";}
    private void showOverlay(){if(overlay!=null)return;if(Build.VERSION.SDK_INT>=23&&!android.provider.Settings.canDrawOverlays(this))return;windowManager=(WindowManager)getSystemService(WINDOW_SERVICE);LinearLayout box=new LinearLayout(this);box.setOrientation(LinearLayout.VERTICAL);box.setPadding(12,8,12,8);box.setBackgroundColor(0xee202832);overlayStatus=new TextView(this);overlayStatus.setTextColor(Color.WHITE);overlayStatus.setTextSize(13);overlayStatus.setText(overlayText(readStatus()));box.addView(overlayStatus);Button stopButton=new Button(this);stopButton.setText("停止");stopButton.setTextSize(12);stopButton.setMinHeight(0);stopButton.setMinimumHeight(0);stopButton.setOnClickListener(v->stopWorker(latestStartId));box.addView(stopButton);int type=Build.VERSION.SDK_INT>=26?WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY:WindowManager.LayoutParams.TYPE_PHONE;overlayParams=new WindowManager.LayoutParams(WindowManager.LayoutParams.WRAP_CONTENT,WindowManager.LayoutParams.WRAP_CONTENT,type,WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE|WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,PixelFormat.TRANSLUCENT);overlayParams.gravity=Gravity.TOP|Gravity.START;overlayParams.x=24;overlayParams.y=120;box.setOnTouchListener((v,event)->{if(event.getAction()==MotionEvent.ACTION_DOWN){v.setTag(new float[]{event.getRawX(),event.getRawY(),overlayParams.x,overlayParams.y});return true;}if(event.getAction()==MotionEvent.ACTION_MOVE){float[] p=(float[])v.getTag();overlayParams.x+=(int)(event.getRawX()-p[0]);overlayParams.y+=(int)(event.getRawY()-p[1]);p[0]=event.getRawX();p[1]=event.getRawY();windowManager.updateViewLayout(v,overlayParams);return true;}return event.getAction()==MotionEvent.ACTION_UP;});try{windowManager.addView(box,overlayParams);overlay=box;mainHandler.removeCallbacks(overlayRefresh);mainHandler.postDelayed(overlayRefresh,200);}catch(Exception e){writeStatus("state=error\ndetail=悬浮窗启动失败");}}
    private void removeOverlay(){mainHandler.removeCallbacks(overlayRefresh);if(overlay!=null&&windowManager!=null){try{windowManager.removeView(overlay);}catch(Exception ignored){}overlay=null;overlayStatus=null;}}
    private Notification notification(String s){Notification.Builder b=Build.VERSION.SDK_INT>=26?new Notification.Builder(this,"battle_loop"):new Notification.Builder(this);return b.setContentTitle("战斗循环助手").setContentText(s.length()>100?s.substring(0,100):s).setSmallIcon(android.R.drawable.stat_notify_sync).setOngoing(true).build();}
    private static boolean running(java.lang.Process p){if(p==null)return false;try{p.exitValue();return false;}catch(IllegalThreadStateException e){return true;}}
    @Override public IBinder onBind(Intent i){return null;}
    @Override public void onDestroy(){desiredRunning=false;stopping=true;timer.shutdownNow();removeOverlay();super.onDestroy();}
}
