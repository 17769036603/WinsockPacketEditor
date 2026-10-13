package com.xnas.wpe.anlei;

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

public final class AnleiService extends com.xnas.wpe.suite.ModuleService {
    static final String START="start", STOP="stop", OVERLAY="overlay";
    private final ScheduledExecutorService timer=Executors.newSingleThreadScheduledExecutor();
    private static volatile java.lang.Process worker;
    private volatile boolean stopping, desiredRunning, stopPending, destroyed;
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
    private static volatile boolean active;

    public static boolean isActive(){return active;}
    private final Runnable overlayRefresh=new Runnable(){@Override public void run(){if(overlayStatus!=null){overlayStatus.setText(overlayText(readStatus()));mainHandler.postDelayed(this,100);}}};

    @Override public void onCreate(){super.onCreate();if(Build.VERSION.SDK_INT>=26){NotificationChannel c=new NotificationChannel("anlei","暗雷遇敌助手",NotificationManager.IMPORTANCE_LOW);getSystemService(NotificationManager.class).createNotificationChannel(c);}}
    @Override public synchronized int onStartCommand(Intent i,int flags,int id){
        latestStartId=id;
        if(i!=null&&OVERLAY.equals(i.getAction())){if(desiredRunning)mainHandler.post(this::showOverlay);else if(stopPending)stopStartId=id;else stopSelfResult(id);return desiredRunning?START_STICKY:START_NOT_STICKY;}
        if(i!=null&&STOP.equals(i.getAction())){stopWorker(id);return START_NOT_STICKY;}
        if(desiredRunning&&!stopping&&running(worker))return START_STICKY;
        if(!desiredRunning&&!stopPending&&!com.xnas.wpe.suite.TargetGame.supportsModule(com.xnas.wpe.suite.TargetGame.ANLEI,
                com.xnas.wpe.suite.TargetGame.getSelectedPackage(this))){writeStatus("stopped:unsupported_target");stopSelfResult(id);return START_NOT_STICKY;}
        generation++;stopping=false;desiredRunning=true;
        active=true;
        startForeground(6101,notification("正在启动并监控游戏进程"));
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
            String bin=new File(getApplicationInfo().nativeLibraryDir,"libanlei_worker.so").getAbsolutePath();
            worker=new ProcessBuilder("su","-c","'"+bin.replace("'","'\\''")+"' --dir '"+dir.getAbsolutePath().replace("'","'\\''")+"'").redirectErrorStream(true).redirectOutput(new File(dir,"worker.log")).start();
        }catch(Exception e){worker=null;writeStatus("启动失败："+e.getMessage());}
    }
    private synchronized void ensureMonitor(){
        if(monitorStarted)return;monitorStarted=true;
        timer.scheduleAtFixedRate(this::monitorTick,0,2,TimeUnit.SECONDS);
    }
    private synchronized void monitorTick(){
        if(desiredRunning&&!stopping)writeLease();
        java.lang.Process p=worker;if(p!=null&&!running(p))worker=null;
        if(stopPending&&worker==null){
            long expected=stopGeneration;
            if(desiredRunning&&!stopping){stopPending=false;stop.delete();startWorker();}
            else if(generation==expected&&stopping){finishStop(expected);}
        }else if(desiredRunning&&!stopping&&worker==null){startWorker();}
        if(desiredRunning||stopPending)getSystemService(NotificationManager.class).notify(6101,notification(displayStatus(readStatus())));
    }
    private synchronized void stopWorker(int startId){
        if(stopping&&stopPending){stopStartId=startId;return;}
        stopping=true;desiredRunning=false;stopPending=true;stopGeneration=++generation;stopStartId=startId;
        stop=new File(getFilesDir(),"stop");
        writeStopRequest();
        java.lang.Process p=worker;
        if(p==null||!running(p)){worker=null;finishStop(stopGeneration);return;}
        Thread waiter=new Thread(()->{try{if(!p.waitFor(15,TimeUnit.SECONDS))writeStatus("停止待确认：清理仍在等待游戏线程");p.waitFor();synchronized(AnleiService.this){if(generation==stopGeneration&&stopping){worker=null;finishStop(stopGeneration);}}}catch(InterruptedException ignored){Thread.currentThread().interrupt();}},"anlei-stop-waiter");
        waiter.setDaemon(true);waiter.start();
        if(!destroyed)ensureMonitor();
    }
    private synchronized void finishStop(long expected){
        if(generation!=expected||!stopping||!stopPending)return;
        stopPending=false;active=false;
        File dir=getFilesDir();
        if(!new File(dir,"status").isFile()){
            boolean hasRunEvidence=new File(dir,"lease").exists()||new File(dir,"worker.log").exists();
            writeStatus(hasRunEvidence?"停止待确认：缺少 worker 终态":"已停止（worker 未启动）");
        }
        // Preserve the worker's terminal evidence; process exit alone must not
        // replace an error or unconfirmed cleanup with a generic stopped label.
        if(destroyed)return;stopForeground(true);mainHandler.post(this::removeOverlay);stopSelfResult(stopStartId);
    }
    private void writeStopRequest(){stop=new File(getFilesDir(),"stop");try(FileOutputStream o=new FileOutputStream(stop)){o.write(1);o.getFD().sync();}catch(Exception e){writeStatus("停止请求失败");}}
    private void writeLease(){try(FileOutputStream o=new FileOutputStream(new File(getFilesDir(),"lease"))){o.write(Long.toString(System.currentTimeMillis()/1000).getBytes(StandardCharsets.UTF_8));o.getFD().sync();}catch(Exception ignored){}}
    private void writeStatus(String s){try(FileOutputStream o=new FileOutputStream(new File(getFilesDir(),"status"))){o.write(s.getBytes(StandardCharsets.UTF_8));}catch(Exception ignored){}}
    private String readStatus(){try(FileInputStream in=new FileInputStream(new File(getFilesDir(),"status"));ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))>0)out.write(b,0,n);return new String(out.toByteArray(),StandardCharsets.UTF_8);}catch(Exception e){return "等待游戏进程";}}
    private static String displayStatus(String s){if(s.contains("overlay_permission_required"))return "请开启悬浮窗权限";if(s.contains("waiting_game"))return "等待游戏启动";if(s.contains("waiting_manager_or_local_role")||s.contains("waiting_role"))return "等待角色登录";if(s.contains("waiting_map"))return "等待地图准备";if(s.contains("running calls="))return "持续运行";if(s.contains("battle"))return "战斗暂停";if(s.contains("game_gone")||s.contains("identity_changed"))return "正在重新连接游戏";if(WorkerStatus.isStopped(s))return "已停止";if(s.contains("error")||s.contains("fatal"))return "运行失败，请查看日志";return "正在监控游戏";}
    private static String overlayText(String s){if(s.contains("battle"))return "战斗暂停";if(s.contains("running calls="))return "持续运行";if(s.contains("waiting_game"))return "等待游戏";if(s.contains("waiting_role")||s.contains("waiting_manager_or_local_role"))return "等待角色";if(s.contains("waiting_map"))return "等待地图";if(WorkerStatus.isStopped(s))return "已停止";if(s.contains("error")||s.contains("fatal"))return "运行异常";return "正在连接";}
    private void showOverlay(){
        if(overlay!=null)return;
        if(Build.VERSION.SDK_INT>=23&&!android.provider.Settings.canDrawOverlays(this))return;
        windowManager=(WindowManager)getSystemService(WINDOW_SERVICE);
        LinearLayout box=new LinearLayout(this);box.setOrientation(LinearLayout.VERTICAL);box.setPadding(12,8,12,8);box.setBackgroundColor(0xee202832);
        overlayStatus=new TextView(this);overlayStatus.setTextColor(Color.WHITE);overlayStatus.setTextSize(13);overlayStatus.setText(overlayText(readStatus()));box.addView(overlayStatus);
        Button stopButton=new Button(this);stopButton.setText("停止");stopButton.setTextSize(12);stopButton.setMinHeight(0);stopButton.setMinimumHeight(0);stopButton.setOnClickListener(v->stopWorker(latestStartId));box.addView(stopButton);
        int type=Build.VERSION.SDK_INT>=26?WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY:WindowManager.LayoutParams.TYPE_PHONE;
        overlayParams=new WindowManager.LayoutParams(WindowManager.LayoutParams.WRAP_CONTENT,WindowManager.LayoutParams.WRAP_CONTENT,type,WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE|WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,PixelFormat.TRANSLUCENT);
        overlayParams.gravity=Gravity.TOP|Gravity.START;overlayParams.x=24;overlayParams.y=120;
        box.setOnTouchListener((v,event)->{if(event.getAction()==MotionEvent.ACTION_DOWN){v.setTag(new float[]{event.getRawX(),event.getRawY(),overlayParams.x,overlayParams.y});return true;}if(event.getAction()==MotionEvent.ACTION_MOVE){float[] p=(float[])v.getTag();overlayParams.x+=(int)(event.getRawX()-p[0]);overlayParams.y+=(int)(event.getRawY()-p[1]);p[0]=event.getRawX();p[1]=event.getRawY();windowManager.updateViewLayout(v,overlayParams);return true;}return event.getAction()==MotionEvent.ACTION_UP;});
        try{windowManager.addView(box,overlayParams);overlay=box;mainHandler.removeCallbacks(overlayRefresh);mainHandler.postDelayed(overlayRefresh,100);}catch(Exception e){writeStatus("悬浮窗启动失败："+e.getMessage());}
    }
    private void removeOverlay(){mainHandler.removeCallbacks(overlayRefresh);if(overlay!=null&&windowManager!=null){try{windowManager.removeView(overlay);}catch(Exception ignored){}overlay=null;overlayStatus=null;}}
    private Notification notification(String s){Intent open=new Intent(this,com.xnas.wpe.suite.MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP|Intent.FLAG_ACTIVITY_CLEAR_TOP).putExtra(com.xnas.wpe.suite.MainActivity.EXTRA_OPEN_MODULE_ID,"anlei");PendingIntent pending=PendingIntent.getActivity(this,6101,open,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);Notification.Builder b=Build.VERSION.SDK_INT>=26?new Notification.Builder(this,"anlei"):new Notification.Builder(this);return b.setContentTitle("暗雷遇敌助手").setContentText(s.length()>100?s.substring(0,100):s).setSmallIcon(android.R.drawable.stat_notify_sync).setContentIntent(pending).setOngoing(true).build();}
    private static boolean running(java.lang.Process p){if(p==null)return false;try{p.exitValue();return false;}catch(IllegalThreadStateException e){return true;}}
    @Override public IBinder onBind(Intent i){return null;}
    @Override public void onDestroy(){destroyed=true;if(active||desiredRunning||stopPending||running(worker))stopWorker(latestStartId);timer.shutdownNow();removeOverlay();super.onDestroy();}
}
