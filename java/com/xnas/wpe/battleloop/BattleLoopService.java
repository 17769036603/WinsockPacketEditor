package com.xnas.wpe.battleloop;

import android.app.*;
import android.content.*;
import android.os.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.*;

public final class BattleLoopService extends com.xnas.wpe.suite.ModuleService {
    static final String START="start", STOP="stop";
    private final ScheduledExecutorService timer=Executors.newSingleThreadScheduledExecutor();
    private volatile java.lang.Process worker;
    private volatile boolean stopping, desiredRunning, stopPending;
    private static volatile boolean active;
    private volatile long lastLaunch, generation;
    private volatile int latestStartId;
    private volatile long stopGeneration;
    private volatile int stopStartId;
    private boolean monitorStarted;
    private File stop;
    public static boolean isActive(){return active;}

    @Override public void onCreate(){super.onCreate();if(Build.VERSION.SDK_INT>=26){NotificationChannel c=new NotificationChannel("battle_loop","战斗循环助手",NotificationManager.IMPORTANCE_LOW);getSystemService(NotificationManager.class).createNotificationChannel(c);}}
    @Override public synchronized int onStartCommand(Intent i,int flags,int id){
        latestStartId=id;
        if(i!=null&&STOP.equals(i.getAction())){stopWorker(id);return START_NOT_STICKY;}
        if(desiredRunning&&!stopping&&!stopPending&&running(worker))return START_STICKY;
        if(!desiredRunning&&!stopPending&&!com.xnas.wpe.suite.TargetGame.supportsModule(com.xnas.wpe.suite.TargetGame.BATTLE_LOOP,
                com.xnas.wpe.suite.TargetGame.getSelectedPackage(this))){writeStatus("state=stopped\nreason=unsupported_target");stopSelfResult(id);return START_NOT_STICKY;}
        com.xnas.wpe.suite.BattleConflictGate.Decision gate=com.xnas.wpe.suite.BattleConflictGate.reserve(
                getFilesDir().getParentFile(),"battleloop");
        if(!gate.allowed){stopSelfResult(id);return START_NOT_STICKY;}
        generation++;stopping=false;desiredRunning=true;
        active=true;
        startForeground(6102,notification("正在启动并监控游戏进程"));
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
        }catch(Exception e){worker=null;desiredRunning=false;stopping=true;stopPending=false;writeStatus("state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\nreason=worker_launch_failed");com.xnas.wpe.suite.BattleConflictGate.release(getFilesDir().getParentFile(),"battleloop");stopForeground(true);stopSelfResult(latestStartId);}
    }
    private synchronized void ensureMonitor(){if(monitorStarted)return;monitorStarted=true;timer.scheduleAtFixedRate(this::monitorTick,0,2,TimeUnit.SECONDS);}
    private synchronized void monitorTick(){
        if(desiredRunning&&!stopping)writeLease();
        java.lang.Process p=worker;if(p!=null&&!running(p))worker=null;
        if(stopPending&&worker==null){long expected=stopGeneration;if(desiredRunning&&!stopping){stopPending=false;stop.delete();startWorker();}else if(generation==expected&&stopping)finishStop(expected);}
        else if(desiredRunning&&!stopping&&worker==null)startWorker();
        if(desiredRunning||stopPending)getSystemService(NotificationManager.class).notify(6102,notification(displayStatus(readStatus())));
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
    private synchronized void finishStop(long expected){if(generation!=expected||!stopping||!stopPending)return;if(!com.xnas.wpe.suite.BattleConflictGate.isClean(getFilesDir().getParentFile(),"battleloop")){writeGateStatus("state=stopping\nmessage=清理尚未确认；请保持游戏运行并等待 hooks、绑定和自动状态恢复确认");return;}stopPending=false;active=false;com.xnas.wpe.suite.BattleConflictGate.release(getFilesDir().getParentFile(),"battleloop");new File(getFilesDir(),"gate-status").delete();stopForeground(true);stopSelfResult(stopStartId);}
    private void writeLease(){try(FileOutputStream o=new FileOutputStream(new File(getFilesDir(),"lease"))){o.write(Long.toString(System.currentTimeMillis()/1000).getBytes(StandardCharsets.UTF_8));o.getFD().sync();}catch(Exception ignored){}}
    private void writeStatus(String s){try(FileOutputStream o=new FileOutputStream(new File(getFilesDir(),"status"))){o.write(s.getBytes(StandardCharsets.UTF_8));}catch(Exception ignored){}}
    private void writeGateStatus(String s){try(FileOutputStream o=new FileOutputStream(new File(getFilesDir(),"gate-status"))){o.write(s.getBytes(StandardCharsets.UTF_8));}catch(Exception ignored){}}
    private String readStatus(){try(FileInputStream in=new FileInputStream(new File(getFilesDir(),"status"));ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))>0)out.write(b,0,n);return new String(out.toByteArray(),StandardCharsets.UTF_8);}catch(Exception e){return "state=waiting_game";}}
    private static String value(String s,String key){for(String line:s.split("\\n"))if(line.startsWith(key+"="))return line.substring(key.length()+1);return "";}
    private static String displayStatus(String s){String state=value(s,"state");if(state.equals("waiting_game"))return "等待游戏启动";if(state.equals("waiting_role"))return "等待角色登录";if(state.equals("waiting_battle"))return "等待战斗";if(state.equals("waiting_config"))return "等待当前自动技能设置";if(state.equals("waiting_conflict"))return "与其他战斗辅助冲突，已暂停";if(state.equals("running"))return "战斗循环运行中";if(state.equals("stopped"))return "已停止";if(state.equals("error"))return "运行失败，请查看日志";if(state.equals("stopping"))return "正在清理并恢复自动状态";return "正在监控游戏";}
    private Notification notification(String s){Intent open=new Intent(this,com.xnas.wpe.suite.MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP|Intent.FLAG_ACTIVITY_CLEAR_TOP).putExtra(com.xnas.wpe.suite.MainActivity.EXTRA_OPEN_MODULE_ID,"battleloop");PendingIntent pending=PendingIntent.getActivity(this,6102,open,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);Notification.Builder b=Build.VERSION.SDK_INT>=26?new Notification.Builder(this,"battle_loop"):new Notification.Builder(this);return b.setContentTitle("战斗循环助手").setContentText(s.length()>100?s.substring(0,100):s).setSmallIcon(android.R.drawable.stat_notify_sync).setContentIntent(pending).setOngoing(true).build();}
    private static boolean running(java.lang.Process p){if(p==null)return false;try{p.exitValue();return false;}catch(IllegalThreadStateException e){return true;}}
    @Override public IBinder onBind(Intent i){return null;}
    @Override public void onDestroy(){desiredRunning=false;stopping=true;active=false;timer.shutdownNow();super.onDestroy();}
}
