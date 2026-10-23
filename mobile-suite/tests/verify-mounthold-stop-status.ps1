param(
    [string]$OutputDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.scratch\mounthold-stop-status')),
    [string]$ServiceSourcePath = (Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\mounthold\MountHoldService.java'),
    [string]$ActivitySourcePath = (Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\mounthold\MainActivity.java')
)
$ErrorActionPreference = 'Stop'
if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $javac = Join-Path $env:JAVA_HOME 'bin\javac.exe'; $java = Join-Path $env:JAVA_HOME 'bin\java.exe'
} else {
    $javac = (Get-Command javac -CommandType Application -ErrorAction Stop).Source
    $java = (Get-Command java -CommandType Application -ErrorAction Stop).Source
}
if (-not (Test-Path -LiteralPath $javac -PathType Leaf) -or -not (Test-Path -LiteralPath $java -PathType Leaf)) { throw 'Set JAVA_HOME to a JDK or put javac and java on PATH.' }

function Get-ProductionMethod([string]$Source, [string]$Signature, [string]$Name) {
    $start = $Source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Could not find production $Name signature." }
    $brace = $Source.IndexOf('{', $start); if ($brace -lt 0) { throw "Could not find production $Name body." }
    $depth = 0; $end = -1
    for ($i = $brace; $i -lt $Source.Length; $i++) {
        if ($Source[$i] -eq '{') { $depth++ }
        elseif ($Source[$i] -eq '}') { $depth--; if ($depth -eq 0) { $end = $i + 1; break } }
    }
    if ($end -lt 0) { throw "Could not extract complete production $Name." }
    return $Source.Substring($start, $end - $start)
}

# ReadAllText preserves UTF-8 BOM handling consistently on Windows PowerShell and pwsh.
$service = [IO.File]::ReadAllText((Resolve-Path $ServiceSourcePath))
$activity = [IO.File]::ReadAllText((Resolve-Path $ActivitySourcePath))
$requestStop = Get-ProductionMethod $service 'private void requestUserStop()' 'requestUserStop'
$journalProbe = Get-ProductionMethod $service 'private Boolean currentJournalPresent()' 'currentJournalPresent'
$finish = Get-ProductionMethod $service 'private void finishService()' 'finishService'
$destroy = Get-ProductionMethod $service 'public void onDestroy()' 'onDestroy'
$consume = Get-ProductionMethod $service 'private void consumeWorkerLine(String line)' 'consumeWorkerLine'
$status = Get-ProductionMethod $activity '@Override public ModuleStatus getModuleStatus()' 'getModuleStatus'
$completionSummary = Get-ProductionMethod $activity 'private String completionSummary(String detail)' 'completionSummary'
$out = [IO.Path]::GetFullPath($OutputDirectory)
$src = Join-Path $out 'src\com\xnas\wpe\mounthold'; $classes = Join-Path $out 'classes'
$null = New-Item -ItemType Directory -Path $src -Force; $null = New-Item -ItemType Directory -Path $classes -Force

$template = @'
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
REQUEST
JOURNAL
FINISH
DESTROY
CONSUME
}
final class ActivityHarness {
    Object statusView; String inactiveStatusOverride; String selectedPackage(){return "pkg";}
    static final class Context { }
    // Source-extracted production method follows.
STATUS
COMPLETION
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
'@
$destroy = $destroy.Replace('super.onDestroy();', 'superDestroy();').Replace('@Override ', '')
$consume = $consume.Replace('private void consumeWorkerLine(String line)', 'public void consumeWorkerLine(String line)')
$status = $status.Replace('@Override ', '')
$requestStop = $requestStop.Replace('private void requestUserStop()', 'public void requestUserStop()')
$source = $template.Replace('REQUEST', $requestStop).Replace('JOURNAL', $journalProbe).Replace('FINISH', $finish).Replace('DESTROY', $destroy).Replace('CONSUME', $consume).Replace('STATUS', $status).Replace('COMPLETION', $completionSummary)
$sourcePath = Join-Path $src 'MountHoldStopStatusHarness.java'
[IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($false))
$targetDir = Join-Path $out 'src\com\xnas\wpe\suite'; $null = New-Item -ItemType Directory -Path $targetDir -Force
$targetStub = Join-Path $targetDir 'TargetGame.java'
[IO.File]::WriteAllText($targetStub, 'package com.xnas.wpe.suite; public final class TargetGame { public static String getSelectedPackage(Object ignored) { return "pkg"; } }', [Text.UTF8Encoding]::new($false))
& $javac --release 8 -encoding UTF-8 -d $classes $sourcePath $targetStub
if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
& $java -cp $classes com.xnas.wpe.mounthold.MountHoldStopStatusHarness
if ($LASTEXITCODE -ne 0) { throw "lifecycle/status harness failed with exit code $LASTEXITCODE" }
