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
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;
import com.xnas.wpe.suite.TargetGame;

public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private final Handler handler=new Handler();
    private TextView status;
    private final Runnable refresh=new Runnable(){@Override public void run(){if(status!=null){try{File dir=getFilesDir();String raw=currentStatus(dir);status.setText(TargetGame.supportsModule(TargetGame.BATTLE_LOOP,TargetGame.getSelectedPackage(MainActivity.this))?display(raw):"此功能仅支持 DHXY");}catch(Exception e){status.setText("状态：读取失败");}handler.postDelayed(this,1000);}}};
    @Override public void onCreate(Bundle saved){super.onCreate(saved);ScrollView scroll=new ScrollView(this);LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);int pad=Math.round(20*getResources().getDisplayMetrics().density);root.setPadding(pad,pad,pad,pad);status=new TextView(this);status.setTextColor(android.graphics.Color.rgb(224,239,243));root.addView(status);Button start=new Button(this);start.setText("启动");start.setOnClickListener(v->{if(!TargetGame.supportsModule(TargetGame.BATTLE_LOOP,TargetGame.getSelectedPackage(this))){status.setText("此功能仅支持 DHXY");return;}Intent i=new Intent(this,BattleLoopService.class).setAction(BattleLoopService.START);if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(i);else startService(i);});root.addView(start);Button stop=new Button(this);stop.setText("停止");stop.setOnClickListener(v->startService(new Intent(this,BattleLoopService.class).setAction(BattleLoopService.STOP)));root.addView(stop);Button check=new Button(this);check.setText("检查旧任务");check.setOnClickListener(v->{Intent i=new Intent(this,BattleLoopService.class).setAction(BattleLoopService.CHECK);if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(i);else startService(i);});root.addView(check);scroll.addView(root,new ScrollView.LayoutParams(-1,-2));setContentView(scroll);}
    @Override protected void onResume(){super.onResume();handler.post(refresh);}
    @Override protected void onPause(){handler.removeCallbacks(refresh);super.onPause();}
    private static String currentStatus(File dir)throws Exception{File statusFile=new File(dir,"status");String actual=statusFile.isFile()?read(statusFile):"state=stopped";if(com.xnas.wpe.suite.BattleConflictGate.isClean(dir.getParentFile(),"battleloop"))return actual;File recovery=new File(dir,"recovery-status");if(!BattleLoopService.isActive()&&recovery.isFile())return read(recovery);File gate=new File(dir,"gate-status");return gate.isFile()?read(gate):actual;}
    private static String read(File f)throws Exception{try(FileInputStream in=new FileInputStream(f);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))>0)out.write(b,0,n);return out.toString("UTF-8");}}
    private static String value(String s,String key){for(String line:s.split("\\r?\\n"))if(line.startsWith(key+"="))return line.substring(key.length()+1).trim();return null;}
    private static String display(String s){String gateMessage=value(s,"message");if(gateMessage!=null&&!gateMessage.isEmpty())return "状态："+gateMessage;String state=value(s,"state");String result="状态：";switch(state==null?"unknown":state){case "stopped":result+="已停止";break;case "starting":result+="已保留启动位，等待 worker";break;case "waiting_game":result+="等待游戏启动";break;case "waiting_role":result+="等待角色登录或重新绑定";break;case "waiting_battle":result+="等待战斗";break;case "waiting_config":result+="等待人物/宝宝当前自动技能设置";break;case "waiting_conflict":result+="检测到桌面辅助冲突，已暂停";break;case "conflict_wait":result+="先停止战投跳过/快进并等待清理完成";break;case "running":result+="运行中";break;case "stopping":result+="正在恢复自动状态并清理 Hooks；清理未确认前启动位保持锁定";break;case "error":result+="运行失败，请查看 worker 日志";break;default:result+="状态未确认";}String player=value(s,"characterSkillId"),pet=value(s,"petSkillId"),petId=value(s,"petId");if(player!=null)result+="\n人物技能 ID："+player;if(pet!=null&&!pet.equals("none"))result+="\n宝宝 ID："+(petId==null?"未确认":petId)+"，技能 ID："+pet;return result;}

    @Override public ModuleStatus getModuleStatus(){
        String raw;
        try{File dir=getFilesDir();raw=currentStatus(dir);}
        catch(Exception error){return ModuleStatus.unknown("com.gdoo.dhxy","读取战投跳过状态失败；游戏与角色绑定未确认。");}
        String selected=TargetGame.getSelectedPackage(this);
        if(!TargetGame.supportsModule(TargetGame.BATTLE_LOOP,selected))return new ModuleStatus(BattleLoopService.isActive()?ModuleStatus.State.UNKNOWN:ModuleStatus.State.STOPPED,BattleLoopService.isActive()?"状态未确认":"不支持",selected,BattleLoopService.isActive()?"战投跳过状态未确认。":"此功能仅支持 DHXY。","");
        String state=value(raw,"state"), message=value(raw,"message"); if(BattleLoopService.isCheckingOldTask())return new ModuleStatus(ModuleStatus.State.WAITING,"检查旧任务中",selected,"正在只读核对旧任务进程身份。","");
        if(message!=null&&!message.isEmpty()&&("conflict_wait".equals(state)||"stopping".equals(state)))return new ModuleStatus("stopping".equals(state)?ModuleStatus.State.STOPPING:ModuleStatus.State.WAITING,
                "stopping".equals(state)?"清理待确认":"启动受阻", "com.gdoo.dhxy", message+"；状态文件来自战斗互斥保护器，worker/游戏绑定未确认。", "请打开战投跳过页查看详情；不要把保护位状态当成游戏绑定状态。");
        ModuleStatus.State mapped;
        String label;
        if(state==null){mapped=ModuleStatus.State.UNKNOWN;label="状态未确认";}
        else switch(state){
            case "stopped": mapped=ModuleStatus.State.STOPPED;label="已停止";break;
            case "starting": mapped=ModuleStatus.State.WAITING;label="等待 worker";break;
            case "waiting_game": mapped=ModuleStatus.State.WAITING;label="等待游戏";break;
            case "waiting_role": mapped=ModuleStatus.State.WAITING;label="等待角色";break;
            case "waiting_battle": mapped=ModuleStatus.State.WAITING;label="等待战斗";break;
            case "waiting_config": mapped=ModuleStatus.State.WAITING;label="等待技能设置";break;
            case "waiting_conflict": case "conflict_wait": mapped=ModuleStatus.State.WAITING;label="辅助冲突";break;
            case "running": mapped=ModuleStatus.State.RUNNING;label="循环运行中";break;
            case "stopping": mapped=ModuleStatus.State.STOPPING;label="旧任务待检查";break;
            case "checking": mapped=ModuleStatus.State.WAITING;label="检查旧任务中";break;case "waiting_restart": mapped=ModuleStatus.State.WAITING;label="等待重启游戏";break;case "unconfirmed": mapped=ModuleStatus.State.UNKNOWN;label="旧任务未确认";break;case "error": mapped=ModuleStatus.State.ERROR;label="运行失败";break;
            default: mapped=ModuleStatus.State.UNKNOWN;label="状态未确认";
        }
        if(BattleLoopService.isActive()&&mapped==ModuleStatus.State.STOPPED){mapped=ModuleStatus.State.UNKNOWN;label="状态未确认";}
        return new ModuleStatus(mapped,label,"com.gdoo.dhxy",display(raw)+"；目标角色/战斗绑定状态以 worker 报告为准。",
                mapped==ModuleStatus.State.STOPPED?"点击开始以运行；本功能仅支持 DHXY。":"本功能仅支持 DHXY；循环状态不代表每个游戏帧均已确认。");
    }
}
