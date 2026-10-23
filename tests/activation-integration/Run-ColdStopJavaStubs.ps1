$ErrorActionPreference = 'Stop'
$javac = $null
$java = $null
if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $candidateJavac = Join-Path $env:JAVA_HOME 'bin\javac.exe'
    $candidateJava = Join-Path $env:JAVA_HOME 'bin\java.exe'
    if ((Test-Path -LiteralPath $candidateJavac) -and (Test-Path -LiteralPath $candidateJava)) {
        $javac = $candidateJavac
        $java = $candidateJava
    }
}
if (-not $javac) { $javac = (Get-Command javac -CommandType Application -ErrorAction Stop).Source }
if (-not $java) { $java = (Get-Command java -CommandType Application -ErrorAction Stop).Source }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mountSource = Get-Content -Raw -Encoding utf8 (Join-Path $root 'app\src\main\java\com\xnas\wpe\mounthold\MountHoldService.java')
$connectionSource = Get-Content -Raw -Encoding utf8 (Join-Path $root 'app\src\main\java\com\xnas\wpe\jinchan\ConnectionService.java')

function Get-JavaMethod([string]$Source, [string]$Name) {
    $match = [regex]::Match($Source, "(?m)^\s*(?:private|public|protected)\s+(?:synchronized\s+)?(?:void|boolean|Boolean|int)\s+$Name\s*\([^)]*\)\s*\{")
    if (-not $match.Success) { throw "Could not find production method $Name" }
    $open = $Source.IndexOf('{', $match.Index)
    $depth = 0; $state = 'code'
    for ($i = $open; $i -lt $Source.Length; $i++) {
        $ch = $Source[$i]
        $next = if ($i + 1 -lt $Source.Length) { $Source[$i + 1] } else { [char]0 }
        if ($state -eq 'line') { if ($ch -eq "`n") { $state = 'code' }; continue }
        if ($state -eq 'block') { if ($ch -eq '*' -and $next -eq '/') { $state = 'code'; $i++ }; continue }
        if ($state -eq 'string' -or $state -eq 'char') {
            if ($ch -eq '\') { $i++; continue }
            if (($state -eq 'string' -and $ch -eq '"') -or ($state -eq 'char' -and $ch -eq "'")) { $state = 'code' }
            continue
        }
        if ($ch -eq '/' -and $next -eq '/') { $state = 'line'; $i++; continue }
        if ($ch -eq '/' -and $next -eq '*') { $state = 'block'; $i++; continue }
        if ($ch -eq '"') { $state = 'string'; continue }
        if ($ch -eq "'") { $state = 'char'; continue }
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -eq 0) { return $Source.Substring($match.Index, $i - $match.Index + 1) }
        }
    }
    throw "Unbalanced method body: $Name"
}

$requestUserStop = Get-JavaMethod $mountSource 'requestUserStop'
$launchStopRecoveryWorker = Get-JavaMethod $mountSource 'launchStopRecoveryWorker'
$currentJournalPresent = Get-JavaMethod $mountSource 'currentJournalPresent'
$finishService = Get-JavaMethod $mountSource 'finishService'
$stopConnection = (Get-JavaMethod $connectionSource 'stopConnection').Replace('ConnectionService.this','ConnectionHarness.this')
$stopForegroundSafely = Get-JavaMethod $connectionSource 'stopForegroundSafely'
$stopConnectionCall = if ($stopConnection -match 'stopConnection\s*\(\s*int\s+startId\s*\)') { 'stopConnection(11)' } else { 'stopConnection()' }
$oldStopConnectionCall = if ($stopConnection -match 'stopConnection\s*\(\s*int\s+startId\s*\)') { 'stopConnection(21)' } else { 'stopConnection()' }
$tempRoot = Join-Path $env:TEMP ('cold-stop-java-stubs-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $tempRoot
try {
    $harness = @"
import java.util.*;
import java.util.concurrent.*;
import java.io.File;
public final class ColdStopHarness {
  static final class Build { static final class VERSION { static final int SDK_INT=35; } }
  static final class Handler { void removeCallbacks(Runnable r) {} }
  static final class MountHarness {
    final Object workerLock = new Object(); final Handler mainHandler = new Handler();
    static final int STOP_FOREGROUND_REMOVE=1; static volatile String lastTargetPackage;
    volatile Thread workerThread; volatile boolean destroyed, stopRecoveryAttempted, active, foregroundStarted;
    volatile boolean userStopRequested, stopping, blockedJournal, recoveringOldJournal, oldJournalResolved, workerFailed;
    volatile String runId;
    int recoveryLaunches, workerStopRequests, finishCalls; File filesDir;
    final Runnable retryWorkerAction = () -> {};
    MountHarness(File dir){filesDir=dir;}
    File getFilesDir(){return filesDir;}
    void setStatus(String text) {}
    void requestWorkerStop() {workerStopRequests++;}
    void launchWorker(boolean sendStop) { recoveryLaunches++; }
    void log(Object... values) {}
    Long elapsedWorkerMs(){return 0L;}
    void stopForeground(int flags) {}
    void stopForeground(boolean remove) {}
    void stopSelf() {finishCalls++;}
$requestUserStop
$launchStopRecoveryWorker
$currentJournalPresent
$finishService
  }
  static final class Connection { boolean stop; }
  static final class QueueExecutor {
    final Queue<Runnable> queue = new ArrayDeque<>(); void execute(Runnable r) { queue.add(r); }
    void runNext() { queue.remove().run(); }
  }
  static final class ConnectionHarness {
    static final int STOP_FOREGROUND_REMOVE=1; static final class Version { static final int SDK_INT=35; }
    static final class Build { static final Version VERSION=new Version(); }
    final QueueExecutor control=new QueueExecutor(); volatile Connection current, wanted;
    volatile String state="disconnected", message="已断开"; static boolean targetSwitchLocked;
    int stopSelfResultCalls, stopSelfResultLastId, latestStartId=11, foregroundStops, stopRequests;
    void stopForeground(int flags) { foregroundStops++; }
    void stopForeground(boolean remove) { foregroundStops++; }
    boolean awaitExit(Connection c,int ms) { return true; }
    void requestStop(Connection c) { stopRequests++; c.stop=true; }
    int refreshes; final List<String> events=new ArrayList<>();
    void refreshNotification() {refreshes++;events.add("notify");}
    boolean isRunning(Process p) { return false; }
    boolean stopSelfResult(int id) { stopSelfResultCalls++; stopSelfResultLastId=id; return id==latestStartId; }
$stopForegroundSafely
$stopConnection
  }
  static final List<String> failures=new ArrayList<>();
  static void check(boolean ok,String message) { if(!ok) failures.add(message); }
  public static void main(String[] args) throws Exception {
    File dir=new File(System.getProperty("cold.stop.tempRoot")); dir.mkdirs();
    MountHarness noJournal=new MountHarness(dir); noJournal.requestUserStop();
    check(noJournal.recoveryLaunches==0,"STOP without worker or recovery journal must not spawn su recovery worker; got "+noJournal.recoveryLaunches);
    check(noJournal.finishCalls==1,"cold idle STOP without journal must finish the foreground service");
    MountHarness retainedJournal=new MountHarness(dir); new File(dir,"mount_hold.journal").createNewFile(); retainedJournal.requestUserStop();
    check(retainedJournal.recoveryLaunches==1&&retainedJournal.finishCalls==0,"current journal must preserve one recovery attempt");
    new File(dir,"mount_hold.journal").delete();
    new File(dir,"mount_hold.journal.expired.test").createNewFile();
    MountHarness expiredOnly=new MountHarness(dir); expiredOnly.requestUserStop();
    check(expiredOnly.recoveryLaunches==0&&expiredOnly.finishCalls==1,"expired journal archive without current journal must finish idle service");
    new File(dir,"mount_hold.journal.expired.test").delete();
    MountHarness unresolved=new MountHarness(dir); unresolved.blockedJournal=true; unresolved.requestUserStop();
    check(unresolved.recoveryLaunches==1&&unresolved.finishCalls==0,"blocked/unknown recovery state must preserve recovery");
    File unreadable=new File(dir,"unreadable") { @Override public File[] listFiles(){return null;} };
    MountHarness unknownDir=new MountHarness(unreadable); unknownDir.requestUserStop();
    check(unknownDir.recoveryLaunches==1&&unknownDir.finishCalls==0,"unknown journal directory listing must preserve recovery");
    File denied=new File(dir,"denied") { @Override public File[] listFiles(){throw new SecurityException("denied");} };
    MountHarness deniedDir=new MountHarness(denied); boolean deniedThrew=false;
    try { deniedDir.requestUserStop(); } catch(SecurityException expected) { deniedThrew=true; }
    check(!deniedThrew&&deniedDir.recoveryLaunches==1&&deniedDir.finishCalls==0,"security failure reading journal directory must preserve recovery safely");
    MountHarness existingWorker=new MountHarness(dir); existingWorker.workerThread=new Thread(()->{});
    existingWorker.workerThread.start(); existingWorker.requestUserStop(); existingWorker.workerThread.join();
    check(existingWorker.workerStopRequests==1&&existingWorker.recoveryLaunches==0,"existing worker receives STOP without launching recovery worker");

    ConnectionHarness idle=new ConnectionHarness(); idle.$stopConnectionCall;
    check(idle.stopSelfResultCalls==1&&idle.stopSelfResultLastId==11,"idle STOP must stop the service for current startId 11");
    ConnectionHarness exited=new ConnectionHarness(); exited.current=new Connection(); exited.$stopConnectionCall;
    int notificationsBeforeExit=exited.refreshes; exited.control.runNext();
    check(exited.stopSelfResultCalls==1&&exited.stopSelfResultLastId==11,"confirmed disconnect must stop the service with STOP startId");
    check(exited.refreshes==notificationsBeforeExit,"confirmed disconnect must not republish notification after stop");
    ConnectionHarness oldCallback=new ConnectionHarness(); oldCallback.current=new Connection();
    oldCallback.latestStartId=21; oldCallback.$oldStopConnectionCall; oldCallback.latestStartId=22;
    oldCallback.wanted=new Connection(); oldCallback.control.runNext();
    check(oldCallback.wanted!=null,"late stop callback must preserve a newer start request");
    check(oldCallback.stopSelfResultCalls==0||(oldCallback.stopSelfResultLastId==21),"old callback must pass its old startId 21 so Android preserves newer start 22");
    ConnectionHarness differentCurrent=new ConnectionHarness(); differentCurrent.current=new Connection();
    differentCurrent.$oldStopConnectionCall; Connection different=new Connection(); differentCurrent.current=different;
    differentCurrent.control.runNext();
    check(differentCurrent.current==different&&differentCurrent.stopSelfResultCalls==0,"late callback for replaced connection must preserve current connection");
    if(failures.isEmpty()) System.out.println("cold STOP behavior: PASS");
    else { for(String failure:failures)System.err.println("FAIL: "+failure); throw new AssertionError(failures.size()+" cold STOP regression(s)"); }
  }
}
"@
    $file = Join-Path $tempRoot 'ColdStopHarness.java'
    [IO.File]::WriteAllText($file, $harness, [Text.UTF8Encoding]::new($false))
    & $javac -encoding UTF-8 -d $tempRoot $file
    if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
    & $java "-Dcold.stop.tempRoot=$tempRoot" -cp $tempRoot ColdStopHarness
    if ($LASTEXITCODE -ne 0) { throw "ColdStopHarness failed with exit code $LASTEXITCODE" }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { [IO.Directory]::Delete($tempRoot, $true) }
}
