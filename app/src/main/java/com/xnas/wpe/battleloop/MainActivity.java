package com.xnas.wpe.battleloop;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.io.*;
import java.nio.charset.StandardCharsets;
import com.xnas.wpe.suite.GlassSkin;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;
import com.xnas.wpe.suite.TargetGame;

public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private static final long OLD_TASK_CHECK_THROTTLE_MS=10_000L;
    private final Handler handler=new Handler();
    private TextView status;
    private long lastOldTaskCheckRequestAt;
    private final Runnable refresh=new Runnable(){@Override public void run(){if(status!=null){try{File dir=getFilesDir();String raw=currentStatus(dir);status.setText(TargetGame.supportsModule(TargetGame.BATTLE_LOOP,TargetGame.getSelectedPackage(MainActivity.this))?display(raw,dir):"此功能仅支持 DHXY");}catch(Exception e){status.setText("状态：读取失败");}handler.postDelayed(this,1000);}}};
    @Override public void onCreate(Bundle saved){super.onCreate(saved);ScrollView scroll=new ScrollView(this);LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);int padX=Math.round(GlassSkin.PADDING_H_DP*getResources().getDisplayMetrics().density);int padY=Math.round(GlassSkin.PADDING_V_DP*getResources().getDisplayMetrics().density);root.setPadding(padX,padY,padX,padY);root.setBackgroundColor(GlassSkin.PAGE_BG);status=new TextView(this);status.setTextColor(android.graphics.Color.rgb(224,239,243));status.setTextSize(GlassSkin.TEXT_BODY_SP);root.addView(status);int rowH=Math.round(GlassSkin.BUTTON_HEIGHT_DP*getResources().getDisplayMetrics().density);int compactH=Math.round(GlassSkin.BUTTON_HEIGHT_COMPACT_DP*getResources().getDisplayMetrics().density);Button start=new Button(this);start.setText("启动");start.setTextSize(GlassSkin.TEXT_BUTTON_SP);start.setOnClickListener(v->requestStart());root.addView(start,new LinearLayout.LayoutParams(-1,rowH));Button stop=new Button(this);stop.setText("停止");stop.setTextSize(GlassSkin.TEXT_BUTTON_COMPACT_SP);stop.setOnClickListener(v->dispatchServiceAction(BattleLoopService.STOP));root.addView(stop,new LinearLayout.LayoutParams(-1,compactH));GlassSkin.apply(start,GlassSkin.actionTinted(this,8,GlassSkin.ACTION_START));start.setTextColor(GlassSkin.actionText());GlassSkin.apply(stop,GlassSkin.actionTinted(this,8,GlassSkin.ACTION_STOP));stop.setTextColor(GlassSkin.actionText());scroll.addView(root,new ScrollView.LayoutParams(-1,-2));setContentView(scroll);}
    private void requestStart(){
        if(!TargetGame.supportsModule(TargetGame.BATTLE_LOOP,TargetGame.getSelectedPackage(this))){status.setText("此功能仅支持 DHXY");return;}
        File dir=getFilesDir();
        if(BattleLoopService.isCheckingOldTask()||com.xnas.wpe.battleskip.BattleSkipRecoveryService.isChecking()){
            status.setText("正在准备，请稍候。");return;
        }
        if(!BattleLoopService.isActive()&&com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedLoopActivity(dir.getParentFile())){
            long now=System.currentTimeMillis();File recovery=new File(dir,"recovery-status");long recoveryAge=now-recovery.lastModified();
            boolean recentlyChecked=recovery.isFile()&&recoveryAge>=0&&recoveryAge<OLD_TASK_CHECK_THROTTLE_MS;
            long requestAge=now-lastOldTaskCheckRequestAt;
            if(recentlyChecked||(lastOldTaskCheckRequestAt!=0&&requestAge>=0&&requestAge<OLD_TASK_CHECK_THROTTLE_MS)){
                status.setText("战斗跳过旧任务刚核查过，保护状态仍有效；请查看当前结果，确认后再次点击启动。");return;
            }
            lastOldTaskCheckRequestAt=now;
            try{
                dispatchServiceAction(BattleLoopService.CHECK);
                status.setText("发现未确认的战斗跳过旧任务，正在只读核查；这次不会启动功能。核查完成并确认后，请再次点击启动。");
            }catch(RuntimeException error){
                status.setText("旧任务核查请求未发送；战斗启动保护仍保留，暂未启动。");
            }
            return;
        }
        if(!BattleLoopService.isActive()&&com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedSkipActivity(dir.getParentFile())){
            long now=System.currentTimeMillis();
            if(lastOldTaskCheckRequestAt!=0&&now-lastOldTaskCheckRequestAt<OLD_TASK_CHECK_THROTTLE_MS)return;
            lastOldTaskCheckRequestAt=now;
            Intent check=new Intent(this,com.xnas.wpe.battleskip.BattleSkipRecoveryService.class);
            try{if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(check);else startService(check);
                status.setText("正在确认上次快进是否退出；完成后请再次点击启动。");}
            catch(RuntimeException error){status.setText("无法确认上次快进是否退出，暂未启动。");}
            return;
        }
        try{dispatchServiceAction(BattleLoopService.START);}
        catch(RuntimeException error){status.setText("启动请求未发送；现有战斗保护状态保留。");}
    }
    private void dispatchServiceAction(String action){Intent intent=new Intent(this,BattleLoopService.class).setAction(action);if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(intent);else startService(intent);}
    @Override protected void onResume(){super.onResume();handler.post(refresh);}
    @Override protected void onPause(){handler.removeCallbacks(refresh);super.onPause();}
    private static String currentStatus(File dir)throws Exception{
        File statusFile=new File(dir,"status");String actual=statusFile.isFile()?read(statusFile):"";File modulesRoot=dir.getParentFile();
        if(!BattleLoopService.isActive()&&com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedSkipActivity(modulesRoot)){
            File skipStatus=new File(new File(modulesRoot,"battleskip"),"status");String skip=skipStatus.isFile()?read(skipStatus):"";
            String reason=com.xnas.wpe.suite.BattleWorkerStatus.isRunning(skip)
                ?"战斗快进正在运行，已阻止启动战斗跳过；请先到“战斗快进”页面停止快进，清理确认后再启动。"
                :"战斗快进上次任务未确认退出，已阻止启动战斗跳过；请先到“战斗快进”页面停止快进并等待清理确认。";
            return "state=conflict_wait\nmessage="+reason+"\n";
        }
        if(com.xnas.wpe.suite.BattleConflictGate.isClean(modulesRoot,"battleloop"))return actual;
        File recovery=new File(dir,"recovery-status");if(!BattleLoopService.isActive()&&recovery.isFile())return read(recovery);
        File gate=new File(dir,"gate-status");return gate.isFile()?read(gate):actual;
    }
    private static String read(File f)throws Exception{try(FileInputStream in=new FileInputStream(f);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))>0)out.write(b,0,n);return out.toString("UTF-8");}}
    private static String value(String s,String key){for(String line:s.split("\\r?\\n"))if(line.startsWith(key+"="))return line.substring(key.length()+1).trim();return null;}
    private static String display(String s,File dir){if("stopped".equals(value(s,"state"))&&com.xnas.wpe.suite.BattleConflictGate.isClean(dir.getParentFile(),"battleloop"))return "状态：已停止";if(s.isEmpty())return com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedLoopActivity(dir.getParentFile())?"状态：战斗跳过旧任务未确认；点击启动将触发只读核查":"状态：未启动";String gateMessage=value(s,"message");if(gateMessage!=null&&!gateMessage.isEmpty())return "状态："+gateMessage;String state=value(s,"state");String result="状态：";switch(state==null?"unknown":state){case "stopped":if(!com.xnas.wpe.suite.BattleConflictGate.isClean(dir.getParentFile(),"battleloop"))result+="停止状态未确认";else if("true".equals(value(s,"processEnded")))result+="旧进程已确认结束；未将此结果作为 Hook 或自动状态恢复证明";else result+="已停止，Hook、绑定与自动状态恢复均已确认";break;case "checking":result+="正在只读核查旧任务";break;case "waiting_restart":result+="等待重启游戏后继续核查旧任务";break;case "verified":result+="旧进程核查已通过，但启动保护仍保留；清理状态未确认";break;case "unconfirmed":result+="旧任务未确认；战斗启动保护仍保留";break;case "starting":result+="已保留启动位，等待 worker";break;case "waiting_game":result+="等待游戏启动";break;case "waiting_role":result+="等待角色登录或重新绑定";break;case "waiting_battle":result+="等待战斗";break;case "waiting_config":result+="等待人物/宝宝当前自动技能设置";break;case "waiting_conflict":result+="与其他战斗辅助冲突，已暂停";break;case "conflict_wait":result+="战斗功能互斥，等待另一功能停止或清理确认";break;case "running":result+="运行中";break;case "stopping":result+="正在恢复自动状态并清理 Hooks；清理未确认前启动位保持锁定";break;case "error":result+="运行失败，请查看 worker 日志";break;default:result+="状态未确认";}String player=value(s,"characterSkillId"),pet=value(s,"petSkillId"),petId=value(s,"petId");if(player!=null)result+="\n人物技能 ID："+player;if(pet!=null&&!pet.equals("none"))result+="\n宝宝 ID："+(petId==null?"未确认":petId)+"，技能 ID："+pet;return result;}

    @Override public ModuleStatus getModuleStatus(){
        String raw;
        File dir=getFilesDir();
        try{raw=currentStatus(dir);}
        catch(Exception error){return ModuleStatus.unknown("com.gdoo.dhxy","读取战斗跳过状态失败；游戏与角色绑定未确认。");}
        String selected=TargetGame.getSelectedPackage(this);
        if(!TargetGame.supportsModule(TargetGame.BATTLE_LOOP,selected))return new ModuleStatus(BattleLoopService.isActive()?ModuleStatus.State.UNKNOWN:ModuleStatus.State.STOPPED,BattleLoopService.isActive()?"状态未确认":"不支持",selected,BattleLoopService.isActive()?"战斗跳过状态未确认。":"此功能仅支持 DHXY。","");
        if(raw.isEmpty()){
            if(com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedLoopActivity(dir.getParentFile()))return new ModuleStatus(ModuleStatus.State.UNKNOWN,"旧任务未确认",selected,"存在未确认的战斗跳过旧记录；打开页面不会触发 Root 核查。","点击启动会进行一次只读核查；核查后仍需再次点击启动。");
            return new ModuleStatus(ModuleStatus.State.STOPPED,"未启动",selected,"没有战斗跳过运行记录；打开页面不会触发 Root 核查。","点击启动以运行；本功能仅支持 DHXY。");
        }
        String state=value(raw,"state"), message=value(raw,"message"); if(BattleLoopService.isCheckingOldTask())return new ModuleStatus(ModuleStatus.State.WAITING,"正在准备",selected,"正在只读核对旧任务进程身份。核查不会自动启动功能。","");
        if(message!=null&&!message.isEmpty()&&("conflict_wait".equals(state)||"stopping".equals(state)))return new ModuleStatus("stopping".equals(state)?ModuleStatus.State.STOPPING:ModuleStatus.State.WAITING,
                "stopping".equals(state)?"清理待确认":"启动受阻", "com.gdoo.dhxy", message+"；状态文件来自战斗互斥保护器，worker/游戏绑定未确认。", "等待上方指出的旧任务核查或清理结果；保护状态不代表游戏绑定状态。");
        ModuleStatus.State mapped;
        String label;
        if(state==null){mapped=ModuleStatus.State.UNKNOWN;label="状态未确认";}
        else switch(state){
            case "stopped": if(com.xnas.wpe.suite.BattleConflictGate.isClean(dir.getParentFile(),"battleloop")){mapped=ModuleStatus.State.STOPPED;label="已停止";}else{mapped=ModuleStatus.State.UNKNOWN;label="停止状态未确认";}break;
            case "starting": mapped=ModuleStatus.State.WAITING;label="等待 worker";break;
            case "waiting_game": mapped=ModuleStatus.State.WAITING;label="等待游戏";break;
            case "waiting_role": mapped=ModuleStatus.State.WAITING;label="等待角色";break;
            case "waiting_battle": mapped=ModuleStatus.State.WAITING;label="等待战斗";break;
            case "waiting_config": mapped=ModuleStatus.State.WAITING;label="等待技能设置";break;
            case "waiting_conflict": case "conflict_wait": mapped=ModuleStatus.State.WAITING;label="辅助冲突";break;
            case "running": mapped=ModuleStatus.State.RUNNING;label="循环运行中";break;
            case "stopping": mapped=ModuleStatus.State.STOPPING;label="待确认";break;
            case "checking": mapped=ModuleStatus.State.WAITING;label="正在准备";break;case "waiting_restart": mapped=ModuleStatus.State.WAITING;label="等待重启游戏";break;case "unconfirmed": mapped=ModuleStatus.State.UNKNOWN;label="旧任务未确认";break;case "error": mapped=ModuleStatus.State.ERROR;label="运行失败";break;
            default: mapped=ModuleStatus.State.UNKNOWN;label="状态未确认";
        }
        if(BattleLoopService.isActive()&&mapped==ModuleStatus.State.STOPPED){mapped=ModuleStatus.State.UNKNOWN;label="状态未确认";}
        return new ModuleStatus(mapped,label,"com.gdoo.dhxy",display(raw,dir)+"；目标角色/战斗绑定状态以 worker 报告为准。",
                mapped==ModuleStatus.State.STOPPED?"点击开始以运行；本功能仅支持 DHXY。":"本功能仅支持 DHXY；循环状态不代表每个游戏帧均已确认。");
    }
}
