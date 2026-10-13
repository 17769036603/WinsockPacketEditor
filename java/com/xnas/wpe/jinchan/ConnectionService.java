package com.xnas.wpe.jinchan;

import android.app.*;
import android.content.*;
import android.os.*;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.*;

public final class ConnectionService extends com.xnas.wpe.suite.ModuleService {
    static final String ACTION_CONNECT="connect", ACTION_STOP="stop", EXTRA_PACKAGE="package";
    private static final String CHANNEL="jinchan_connection";
    private final ExecutorService control=Executors.newSingleThreadExecutor();
    private volatile Connection current;
    private volatile Connection wanted;
    private volatile String state="disconnected", message="已断开";
    private static volatile boolean targetSwitchLocked;
    private static final class Connection {
        final String token, pkg;
        volatile java.lang.Process process;
        volatile boolean stop;
        volatile String state="connecting", message="正在连接";
        volatile long heartbeat;
        volatile Thread monitor;
        Connection(String token,String pkg){this.token=token;this.pkg=pkg;}
    }
    @Override public void onCreate(){super.onCreate();if(Build.VERSION.SDK_INT>=26){NotificationChannel c=new NotificationChannel(CHANNEL,"金蟾连接",NotificationManager.IMPORTANCE_LOW);getSystemService(NotificationManager.class).createNotificationChannel(c);}}
    @Override public int onStartCommand(Intent i,int flags,int id){
        if(i!=null&&ACTION_STOP.equals(i.getAction())){stopConnection();return START_NOT_STICKY;}
        String pkg=i==null?"":i.getStringExtra(EXTRA_PACKAGE);if(pkg==null)pkg="";if(!com.xnas.wpe.suite.TargetGame.supportsModule(com.xnas.wpe.suite.TargetGame.JINCHAN,pkg)){state="error";message="目标游戏不支持";startForeground(6103,notification());if(Build.VERSION.SDK_INT>=24)stopForeground(STOP_FOREGROUND_REMOVE);else stopForeground(true);stopSelfResult(id);return START_NOT_STICKY;}
        Connection c=current;if(c!=null&&pkg.equals(c.pkg)&&!c.stop&&c.process!=null&&isRunning(c.process)&&("connecting".equals(state)||"connected".equals(state)||"busy".equals(state)))return START_STICKY;
        startConnection(pkg);return START_STICKY;
    }
    private synchronized void startConnection(String pkg){
        targetSwitchLocked=true;
        Connection next=new Connection(UUID.randomUUID().toString().replace("-",""),pkg);
        Connection old=current;wanted=next;state="connecting";message="正在连接";startForeground(6103,notification());
        if(old!=null)requestStop(old);
        control.execute(()->{
            if(old!=null&&!awaitExit(old,15000)){if(wanted==next){state="error";message="旧 worker 未确认退出，已阻止叠加连接";refreshNotification();}return;}
            synchronized(ConnectionService.this){if(wanted!=next)return;current=next;}
            launch(next);
        });
    }
    private void launch(Connection c){
        try{
            File dir=getFilesDir();new File(dir,"connection-"+c.token+".json").delete();
            String bin=new File(getApplicationInfo().nativeLibraryDir,"libjinchan_worker.so").getAbsolutePath();
            String cmd=quote(bin)+" --serve "+quote(dir.getAbsolutePath())+" "+quote(c.pkg)+" "+c.token;
            c.process=new ProcessBuilder("su","-c",cmd).redirectErrorStream(true).redirectOutput(new File(dir,"jinchan-daemon-"+c.token+".log")).start();
            c.monitor=new Thread(()->monitor(c),"jinchan-monitor-"+c.token);c.monitor.start();
        }catch(Exception e){c.state="error";c.message=e.getMessage()==null?"连接失败":e.getMessage();publish(c);}
    }
    private void monitor(Connection c){
        try{
            while(!c.stop&&isRunning(c.process)){
                JSONObject s=readState(c.token);
                if(s!=null&&validState(s,c)){
                    c.state=s.optString("state","error");c.message=s.optString("message",c.state);
                    c.heartbeat=s.optLong("heartbeat",0);
                }
                long age=System.currentTimeMillis()/1000-c.heartbeat, limit="busy".equals(c.state)?15:5;
                if(("connected".equals(c.state)||"busy".equals(c.state))&&age>limit){c.state="disconnected";c.message="worker 状态已过期";}
                publish(c);Thread.sleep(1000);
            }
            c.process.waitFor();JSONObject last=readState(c.token);
            if(last!=null&&validState(last,c)){c.state=last.optString("state","error");c.message=last.optString("message",c.state);}
            else if(!c.stop){c.state="error";c.message="worker 已退出，未提供有效最终状态";}
            if(!c.stop&&!"error".equals(c.state)&&!"blocked".equals(c.state)){c.state="disconnected";c.message="worker 已断开";}
            publish(c);
        }catch(Exception e){if(!c.stop){c.state="error";c.message=e.getMessage()==null?"状态监控失败":e.getMessage();publish(c);}}
    }
    private boolean validState(JSONObject s,Connection c){return c.token.equals(s.optString("token"))&&c.pkg.equals(s.optString("package"));}
    private boolean awaitExit(Connection c,long timeout){
        requestStop(c);long end=System.currentTimeMillis()+timeout;
        try{java.lang.Process p=c.process;if(p==null)return c.monitor==null;while(isRunning(p)&&System.currentTimeMillis()<end)Thread.sleep(200);if(isRunning(p))return false;
            JSONObject s=readState(c.token);File statusFile=new File(getFilesDir(),"connection-"+c.token+".json");if(s==null){if(statusFile.exists())return false;}else if(!validState(s,c)||!("disconnected".equals(s.optString("state"))||"error".equals(s.optString("state"))||"blocked".equals(s.optString("state"))))return false;
            Thread m=c.monitor;if(m!=null)m.join(Math.max(1,end-System.currentTimeMillis()));return m==null||!m.isAlive();
        }catch(Exception e){return false;}
    }
    private void requestStop(Connection c){c.stop=true;try(FileOutputStream o=new FileOutputStream(new File(getFilesDir(),"stop-"+c.token))){o.write(1);o.getFD().sync();}catch(Exception ignored){}}
    private synchronized void stopConnection(){wanted=null;Connection c=current;if(c==null){targetSwitchLocked=false;state="disconnected";message="已断开";if(Build.VERSION.SDK_INT>=24)stopForeground(STOP_FOREGROUND_REMOVE);else stopForeground(true);return;}state="disconnected";message="正在断开";refreshNotification();requestStop(c);control.execute(()->{if(awaitExit(c,15000)){if(current==c&&wanted==null)current=null;if(wanted==null){targetSwitchLocked=false;state="disconnected";message="已断开";if(Build.VERSION.SDK_INT>=24)stopForeground(STOP_FOREGROUND_REMOVE);else stopForeground(true);}}else{targetSwitchLocked=true;state="error";message="worker 未确认退出，仍保持连接保护";}refreshNotification();});}
    boolean submit(String action,int pos,int base,int refine,String role,String uid,String preview,String nonce,String expectedPackage,String expectedToken){
        Connection c=current;if(c==null||c.stop||!c.pkg.equals(expectedPackage)||!c.token.equals(expectedToken)||!"connected".equals(c.state))return false;
        JSONObject s=readState(c.token);if(s==null||!validState(s,c)||!"connected".equals(s.optString("state"))||System.currentTimeMillis()/1000-s.optLong("heartbeat",0)>5)return false;
        File f=new File(getFilesDir(),"request-"+c.token+".txt");if(f.exists())return false;
        String data=getFilesDir().getAbsolutePath()+"\n"+c.pkg+"\n"+action+"\n"+pos+"\n"+base+"\n"+refine+"\n"+role+"\n"+uid+"\n"+preview+"\n"+nonce;
        if(data.getBytes(StandardCharsets.UTF_8).length>8192)return false;
        try{File tmp=new File(getFilesDir(),"request-"+c.token+".tmp");try(FileOutputStream o=new FileOutputStream(tmp)){o.write(data.getBytes(StandardCharsets.UTF_8));o.getFD().sync();}return tmp.renameTo(f);}catch(Exception e){return false;}
    }
    private JSONObject readState(String t){File f=new File(getFilesDir(),"connection-"+t+".json");if(!f.isFile()||f.length()>32768)return null;try(FileInputStream in=new FileInputStream(f);ByteArrayOutputStream b=new ByteArrayOutputStream()){byte[] x=new byte[4096];int n;while((n=in.read(x))>0)b.write(x,0,n);return new JSONObject(new String(b.toByteArray(),StandardCharsets.UTF_8));}catch(Exception e){return null;}}
    private static boolean isRunning(java.lang.Process p){try{p.exitValue();return false;}catch(IllegalThreadStateException running){return true;}}
    private void publish(Connection c){if(current==c&&(wanted==null||wanted==c)){state=c.state;message=c.message;refreshNotification();}}
    public static boolean isTargetSwitchLocked(){return targetSwitchLocked;}
    String getState(){return state;}String getMessage(){return message;}String getToken(){Connection c=current;return c==null?"":c.token;}String getPackage(){Connection c=current;return c==null?"":c.pkg;}
    private Notification notification(){Intent open=new Intent(this,com.xnas.wpe.suite.MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP|Intent.FLAG_ACTIVITY_CLEAR_TOP).putExtra(com.xnas.wpe.suite.MainActivity.EXTRA_OPEN_MODULE_ID,"jinchan");PendingIntent pending=PendingIntent.getActivity(this,6103,open,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);Notification.Builder b=Build.VERSION.SDK_INT>=26?new Notification.Builder(this,CHANNEL):new Notification.Builder(this);return b.setContentTitle("金蟾装备助手").setContentText(message).setSmallIcon(android.R.drawable.stat_sys_download_done).setContentIntent(pending).setOngoing(true).build();}
    private void refreshNotification(){if(Build.VERSION.SDK_INT>=26)((NotificationManager)getSystemService(NOTIFICATION_SERVICE)).notify(6103,notification());}
    private static String quote(String s){return "'"+s.replace("'","'\\''")+"'";}
    @Override public IBinder onBind(Intent i){return new LocalBinder();}final class LocalBinder extends Binder{ConnectionService get(){return ConnectionService.this;}}
    @Override public void onDestroy(){wanted=null;Connection c=current;if(c!=null){targetSwitchLocked=true;requestStop(c);}control.execute(()->{boolean exited=c==null||awaitExit(c,15000);if(exited&&current==c)current=null;targetSwitchLocked=!exited;control.shutdown();});super.onDestroy();}
}
