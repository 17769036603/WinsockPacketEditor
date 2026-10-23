package com.xnas.wpe.mounthold;
import java.io.File;

enum ModuleState { STOPPED, WAITING, UNKNOWN, STOPPING, RUNNING }
final class ModuleStatus {
    enum State { STOPPED, WAITING, UNKNOWN, STOPPING, RUNNING }
    final State state; final String label, detail, action;
    ModuleStatus(State s, String l, String t, String d, String a) { state=s; label=l; detail=d; action=a; }
}
final class TargetGame { static String getSelectedPackage(Object ignored) { return "pkg"; } }
final class MountHoldService {
    static boolean active; static String detail;
    static boolean isActive(){return active;} static String getStatusText(){return detail;}
    static String getPackageForStop(String value){return value;}
}
final class StateView { void setText(String ignored){} void setVisibility(int ignored){} }
final class View { static final int VISIBLE=0; }
final class ActivityContext { }
final class ProbeDirectory extends File {
    static boolean inspectionUnknown, journalExists;
    ProbeDirectory(){super("stub-files");}
    @Override public File[] listFiles(){ if(inspectionUnknown)return null; return journalExists ? new File[]{new File("mount_hold.journal")} : new File[0]; }
}

final class MountHoldHarness {
    static final class Handler { void removeCallbacks(Object o){} void removeCallbacksAndMessages(Object o){} }
    static final class Worker { boolean alive; boolean isAlive(){return alive;} }
    final Object workerLock=new Object(); final Handler mainHandler=new Handler(); final Object retryWorkerAction=new Object();
    Worker workerThread; boolean userStopRequested, stopping, blockedJournal, recoveringOldJournal, oldJournalResolved;
    boolean active=true, workerFailed, sawRecoveredState, sawTerminalState, sawWorkerError, destroyed, preserveActiveAfterDestroy, foregroundStarted;
    String status, runId, lastTargetPackage; int launches, stops; long retryDelaySeconds=1L;
    File filesDir=new ProbeDirectory(); File getFilesDir(){return filesDir;}
    void requestWorkerStop(){} void launchStopRecoveryWorker(){launches++;} void setStatus(String s){status=s; MountHoldService.detail=s;}
    void consumeWorkerMetric(String line){} void logState(String state,String detail){}
    void log(String a,String b,Long c,String d,String e,String f,String g,Integer h,Long i){}
    Long elapsedWorkerMs(){return 0L;} void stopForeground(int x){stops++;} void stopForeground(boolean x){stops++;}
    void stopSelf(){} void closeWorkerInput(){} void superDestroy(){}
    static final int STOP_FOREGROUND_REMOVE=1;
    // Source-extracted production methods follow.
public void requestUserStop() {
        synchronized (workerLock) {
            userStopRequested = true;
            stopping = true;
            mainHandler.removeCallbacks(retryWorkerAction);
            requestWorkerStop();
            if (workerThread == null) {
                boolean recoveryStateUnresolved = blockedJournal
                        || (recoveringOldJournal && !oldJournalResolved);
                Boolean journalPresent = currentJournalPresent();
                if (Boolean.FALSE.equals(journalPresent) && !recoveryStateUnresolved) {
                    // No journal means this stop confirmed that there was nothing to restore.
                    // Keep onDestroy from treating the stop request itself as unresolved cleanup.
                    sawTerminalState = true;
                    sawRecoveredState = true;
                    setStatus("已停止，未修改速度");
                    finishService();
                } else {
                    launchStopRecoveryWorker();
                }
            }
        }
    }
private Boolean currentJournalPresent() {
        final File[] files;
        try {
            files = getFilesDir().listFiles();
        } catch (SecurityException denied) {
            return null;
        }
        if (files == null) return null;
        for (File file : files) {
            if ("mount_hold.journal".equals(file.getName())) return true;
        }
        return false;
    }
private void finishService() {
        log("service_stop", runId, elapsedWorkerMs(), null, null, null, null, null, null);
        active = false;
        if (Build.VERSION.SDK_INT >= 24) stopForeground(STOP_FOREGROUND_REMOVE);
        else stopForeground(true);
        foregroundStarted = false;
        lastTargetPackage = null;
        stopSelf();
    }
public void onDestroy() {
        boolean cleanupUnconfirmed = ((active || stopping || workerFailed || userStopRequested)
                && !sawRecoveredState)
                || (workerThread != null && workerThread.isAlive()) || blockedJournal
                || (recoveringOldJournal && !oldJournalResolved);
        destroyed = true;
        preserveActiveAfterDestroy = cleanupUnconfirmed;
        log("service_destroyed", runId, elapsedWorkerMs(), null, null, null, null, null, null);
        requestWorkerStop();
        closeWorkerInput();
        active = cleanupUnconfirmed;
        mainHandler.removeCallbacksAndMessages(null);
        if (foregroundStarted) {
            if (Build.VERSION.SDK_INT >= 24) stopForeground(STOP_FOREGROUND_REMOVE);
            else stopForeground(true);
            foregroundStarted = false;
        }
        superDestroy();
    }
public void consumeWorkerLine(String line) {
        if (line.startsWith("METRIC\t")) {
            consumeWorkerMetric(line);
            return;
        }
        if (!line.startsWith("STATE\t")) return;
        String[] fields = line.split("\t", 3);
        if (fields.length < 2) return;
        String detail = fields.length == 3 ? fields[2] : "";
        logState(fields[1], detail);
        String display;
        switch (fields[1]) {
            case "STARTING": display = "Rust worker 已启动"; break;
            case "LOCATING": display = "正在定位自己的角色"; break;
            case "ACTIVE":
                display = "角色速度保持中";
                if ("holding_speed".equals(detail)) {
                    retryDelaySeconds = 1L;
                    workerFailed = false;
                }
                break;
            case "STOPPING": display = "正在安全恢复原值"; sawTerminalState = true; break;
            case "RECOVERED": display = "stopped_without_changes".equals(detail)
                    ? "已停止，未修改速度" : "已停止并确认恢复";
                log("recovery_outcome", runId, elapsedWorkerMs(), "RECOVERED", detail,
                        null, null, null, null);
                sawTerminalState = true;
                sawRecoveredState = true;
                if ("old_journal_restored".equals(detail)) oldJournalResolved = true;
                break;
            case "ARCHIVED":
                display = "旧恢复记录已安全保存，正在查找游戏";
                oldJournalResolved = true;
                break;
            case "BLOCKED":
                log("blocked", runId, elapsedWorkerMs(), "BLOCKED", detail,
                        null, null, null, null);
                if ("journal_retained".equals(detail)) {
                    workerFailed = true;
                    blockedJournal = true;
                    display = "已停止，恢复记录保留";
                } else {
                    if ("recovering_old_journal".equals(detail)) recoveringOldJournal = true;
                    display = "正在核验旧恢复记录";
                }
                break;
            case "ERROR":
                workerFailed = true;
                sawWorkerError = true;
                display = "worker 报错";
                break;
            default: display = "worker 状态更新"; break;
        }
        if (!detail.isEmpty()) display += "：" + detail;
        setStatus(display);
    }
}
final class ActivityHarness {
    Object statusView; String inactiveStatusOverride; String selectedPackage(){return "pkg";}
    static final class Context { }
    // Source-extracted production method follows.
public ModuleStatus getModuleStatus() {
        String target = MountHoldService.getPackageForStop(com.xnas.wpe.suite.TargetGame.getSelectedPackage(this));
        String detail = MountHoldService.getStatusText();
        boolean active = MountHoldService.isActive();
        String safeDetail = detail == null || detail.trim().isEmpty() ? "服务状态文本未提供。" : detail.trim();
        ModuleStatus.State state;
        String label;
        boolean unresolved = safeDetail.contains("恢复失败") || safeDetail.contains("未确认恢复")
                || safeDetail.contains("未确认") || safeDetail.contains("状态未确认")
                || safeDetail.contains("未能确认") || safeDetail.contains("保留恢复记录")
                || safeDetail.contains("恢复记录保留");
        if (unresolved) {
            state = ModuleStatus.State.UNKNOWN;
            label = "状态未确认";
        } else if (!active) {
            state = ModuleStatus.State.STOPPED;
            label = "未运行";
        } else if ("已停止，未修改速度".equals(completionSummary(safeDetail))
                || "已停止并确认恢复".equals(completionSummary(safeDetail))) {
            state = ModuleStatus.State.WAITING;
            label = "等待任务退出";
        } else if (safeDetail.contains("等待") || safeDetail.contains("定位") || safeDetail.contains("启动")) {
            state = ModuleStatus.State.WAITING;
            label = "等待目标";
        } else if (safeDetail.contains("恢复") || safeDetail.contains("停止")) {
            state = ModuleStatus.State.STOPPING;
            label = "恢复中";
        } else {
            state = ModuleStatus.State.RUNNING;
            label = "服务运行中";
        }
        return new ModuleStatus(state, label, target,
                safeDetail + "；服务活动状态=" + (active ? "是" : "否") + "，目标绑定未确认。",
                active || state == ModuleStatus.State.UNKNOWN ? "服务运行不等同于已绑定游戏/角色；如需停止，请在功能页点击“停止并恢复”。"
                        : "选择目标游戏和速度后，点击“启动保持”；运行需要模拟器 Root 权限。");
    }
private String completionSummary(String detail) {
        int separator = detail.indexOf('：');
        int asciiSeparator = detail.indexOf(':');
        if (separator < 0 || (asciiSeparator >= 0 && asciiSeparator < separator)) {
            separator = asciiSeparator;
        }
        return separator < 0 ? detail : detail.substring(0, separator).trim();
    }
    ModuleStatus invoke(){return getModuleStatus();}
}
final class Build { static final class VERSION { static final int SDK_INT=35; } }
public final class MountHoldStopStatusHarness {
    static int assertions; static final java.util.List<String> failures=new java.util.ArrayList<String>();
    static void check(boolean ok,String message){assertions++; if(!ok) failures.add(message);}
    static void eq(Object expected,Object actual,String message){check(java.util.Objects.equals(expected,actual),message+" expected="+expected+" actual="+actual);}
    static MountHoldHarness fresh(){ MountHoldHarness h=new MountHoldHarness(); MountHoldService.active=true; MountHoldService.detail="运行中"; ProbeDirectory.inspectionUnknown=false; ProbeDirectory.journalExists=false; return h; }
    static void status(boolean active,String detail,ModuleStatus.State expected,String caseName){
        MountHoldService.active=active; MountHoldService.detail=detail;
        ModuleStatus got=new ActivityHarness().invoke(); eq(expected,got.state,caseName);
    }
    public static void main(String[] args){
        MountHoldHarness h=fresh(); h.requestUserStop();
        eq("已停止，未修改速度",h.status,"confirmed no-journal stop message");
        check(!h.active,"no journal and no worker should finish inactive before destroy");
        h.onDestroy(); check(!h.active,"no journal and no worker must remain inactive after onDestroy");

        MountHoldHarness journal=fresh(); ProbeDirectory.journalExists=true; journal.requestUserStop(); check(journal.launches==1,"present journal must launch recovery");
        MountHoldHarness unknown=fresh(); ProbeDirectory.inspectionUnknown=true; unknown.requestUserStop(); check(unknown.launches==1,"unknown journal state must launch recovery");
        MountHoldHarness blocked=fresh(); blocked.blockedJournal=true; blocked.requestUserStop(); check(blocked.launches==1,"blocked journal must launch recovery");
        MountHoldHarness unresolved=fresh(); unresolved.recoveringOldJournal=true; unresolved.oldJournalResolved=false; unresolved.requestUserStop(); check(unresolved.launches==1,"unresolved old journal must launch recovery");
        MountHoldHarness worker=fresh(); worker.workerThread=new MountHoldHarness.Worker(); worker.workerThread.alive=true; worker.requestUserStop(); check(worker.launches==0,"live worker must not bypass worker stop/recovery"); check(worker.stops==0,"live worker stop request cannot finish foreground service synchronously");

        status(false,"worker 已退出",ModuleStatus.State.STOPPED,"inactive confirmed is STOPPED");
        status(true,"已停止，未修改速度",ModuleStatus.State.WAITING,"confirmed terminal while service active waits for worker exit");
        status(false,"恢复失败，保留恢复记录",ModuleStatus.State.UNKNOWN,"inactive recovery failure is UNKNOWN");
        status(false,"未确认恢复",ModuleStatus.State.UNKNOWN,"inactive unconfirmed recovery is UNKNOWN");
        status(false,"worker 未能确认结果",ModuleStatus.State.UNKNOWN,"inactive unable-to-confirm is UNKNOWN");
        status(true,"恢复失败，保留恢复记录",ModuleStatus.State.UNKNOWN,"active recovery failure is UNKNOWN");
        status(true,"未确认恢复",ModuleStatus.State.UNKNOWN,"active unconfirmed recovery is UNKNOWN");
        status(true,"worker 未能确认结果",ModuleStatus.State.UNKNOWN,"active unable-to-confirm is UNKNOWN");
        status(true,"正在安全恢复原值",ModuleStatus.State.STOPPING,"actual recovery is STOPPING");
        status(true,"正在定位目标",ModuleStatus.State.WAITING,"target location is WAITING");
        status(true,"运行中",ModuleStatus.State.RUNNING,"normal active is RUNNING");
        MountHoldHarness noChange=fresh(); noChange.consumeWorkerLine("STATE\tRECOVERED\tstopped_without_changes");
        eq("已停止，未修改速度：stopped_without_changes",noChange.status,"RECOVERED no-change output includes worker detail suffix");
        status(true,noChange.status,ModuleStatus.State.WAITING,"RECOVERED no-change worker output waits for task exit");
        MountHoldHarness restored=fresh(); restored.consumeWorkerLine("STATE\tRECOVERED\told_journal_restored");
        eq("已停止并确认恢复：old_journal_restored",restored.status,"RECOVERED restored output includes worker detail suffix");
        status(true,restored.status,ModuleStatus.State.WAITING,"RECOVERED restored worker output waits for task exit");
        status(true,"恢复失败，保留恢复记录",ModuleStatus.State.UNKNOWN,"recovery failure stays UNKNOWN before terminal text mapping");
        if(failures.isEmpty()) System.out.println("MountHoldStopStatusHarness PASS ("+assertions+" assertions; extracted production requestUserStop/finishService/onDestroy/getModuleStatus)");
        else { System.out.println("MountHoldStopStatusHarness FAIL ("+failures.size()+" failures / "+assertions+" assertions; extracted production methods)"); for(String failure:failures)System.out.println("FAIL: "+failure); throw new AssertionError("production regression assertions failed"); }
    }
}