package com.xnas.wpe.battleloop;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;

public final class LoopStatusHarness {
    public static final class BattleLoopService {
        public static boolean active;
        public static boolean isActive() { return active; }
    }
    // Source-extracted production methods follow.
public static String read(File f)throws Exception{try(FileInputStream in=new FileInputStream(f);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))>0)out.write(b,0,n);return out.toString("UTF-8");}}
public static String currentStatus(File dir)throws Exception{
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
public static String display(String s,File dir){if("stopped".equals(value(s,"state"))&&com.xnas.wpe.suite.BattleConflictGate.isClean(dir.getParentFile(),"battleloop"))return "状态：已停止";if(s.isEmpty())return com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedLoopActivity(dir.getParentFile())?"状态：战斗跳过旧任务未确认；点击启动将触发只读核查":"状态：未启动";String gateMessage=value(s,"message");if(gateMessage!=null&&!gateMessage.isEmpty())return "状态："+gateMessage;String state=value(s,"state");String result="状态：";switch(state==null?"unknown":state){case "stopped":if(!com.xnas.wpe.suite.BattleConflictGate.isClean(dir.getParentFile(),"battleloop"))result+="停止状态未确认";else if("true".equals(value(s,"processEnded")))result+="旧进程已确认结束；未将此结果作为 Hook 或自动状态恢复证明";else result+="已停止，Hook、绑定与自动状态恢复均已确认";break;case "checking":result+="正在只读核查旧任务";break;case "waiting_restart":result+="等待重启游戏后继续核查旧任务";break;case "verified":result+="旧进程核查已通过，但启动保护仍保留；清理状态未确认";break;case "unconfirmed":result+="旧任务未确认；战斗启动保护仍保留";break;case "starting":result+="已保留启动位，等待 worker";break;case "waiting_game":result+="等待游戏启动";break;case "waiting_role":result+="等待角色登录或重新绑定";break;case "waiting_battle":result+="等待战斗";break;case "waiting_config":result+="等待人物/宝宝当前自动技能设置";break;case "waiting_conflict":result+="与其他战斗辅助冲突，已暂停";break;case "conflict_wait":result+="战斗功能互斥，等待另一功能停止或清理确认";break;case "running":result+="运行中";break;case "stopping":result+="正在恢复自动状态并清理 Hooks；清理未确认前启动位保持锁定";break;case "error":result+="运行失败，请查看 worker 日志";break;default:result+="状态未确认";}String player=value(s,"characterSkillId"),pet=value(s,"petSkillId"),petId=value(s,"petId");if(player!=null)result+="\n人物技能 ID："+player;if(pet!=null&&!pet.equals("none"))result+="\n宝宝 ID："+(petId==null?"未确认":petId)+"，技能 ID："+pet;return result;}
public static String value(String s,String key){for(String line:s.split("\\r?\\n"))if(line.startsWith(key+"="))return line.substring(key.length()+1).trim();return null;}
}