param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) '.scratch\jinchan-binding'),
    [string]$ActivitySourcePath = (Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\jinchan\MainActivity.java')
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
function Get-ProductionRunnable([string]$Source, [string]$Marker, [string]$Name) {
    $start = $Source.IndexOf($Marker, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Could not find production $Name." }
    $brace = $Source.IndexOf('{', $start); if ($brace -lt 0) { throw "Could not find production $Name body." }
    $depth = 0; $end = -1
    for ($i = $brace; $i -lt $Source.Length; $i++) {
        if ($Source[$i] -eq '{') { $depth++ }
        elseif ($Source[$i] -eq '}') { $depth--; if ($depth -eq 0) { $end = $i + 1; break } }
    }
    if ($end -lt 0) { throw "Could not extract complete production $Name." }
    $semi = $Source.IndexOf(';', $end)
    if ($semi -lt 0) { throw "Could not find production $Name terminator." }
    return $Source.Substring($start, $semi + 1 - $start)
}
function Get-ProductionStatement([string]$Source, [string]$Marker, [string]$Name) {
    $start = $Source.IndexOf($Marker, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Could not find production $Name." }
    $semi = $Source.IndexOf(';', $start)
    if ($semi -lt 0) { throw "Could not find production $Name terminator." }
    return $Source.Substring($start, $semi + 1 - $start)
}

$activity = [IO.File]::ReadAllText((Resolve-Path $ActivitySourcePath))
$onCreate = Get-ProductionMethod $activity '@Override public void onCreate(Bundle state)' 'onCreate'
$onResume = Get-ProductionMethod $activity '@Override protected void onResume()' 'onResume'
$onDestroy = Get-ProductionMethod $activity '@Override protected void onDestroy()' 'onDestroy'
$createConnection = Get-ProductionMethod $activity 'private ServiceConnection createServiceConnection()' 'createServiceConnection'
$createConnection = $createConnection.Replace('android.os.SystemClock', 'TestClock')
$ensure = Get-ProductionMethod $activity 'private void ensureServiceBound()' 'ensureServiceBound'
$release = Get-ProductionMethod $activity 'private void releaseServiceBinding()' 'releaseServiceBinding'
$retry = Get-ProductionMethod $activity 'private void scheduleBindRetry()' 'scheduleBindRetry'
$retry = $retry.Replace('android.os.SystemClock', 'TestClock')
$setError = Get-ProductionMethod $activity 'private void setBindingError(String detail)' 'setBindingError'
$lastBindAttempt = (Get-ProductionStatement $activity 'private long lastBindAttempt=' 'lastBindAttempt').Replace('private ', '')
$timeout = Get-ProductionRunnable $activity 'private final Runnable reconnectTimeout =' 'reconnectTimeout'
$refresh = Get-ProductionRunnable $activity 'private final Runnable connectionRefresh =' 'connectionRefresh'
$onCreate = $onCreate.Replace('super.onCreate(state);', 'superOnCreate(state);').Replace('@Override ', '')
$onResume = $onResume.Replace('super.onResume();', 'superOnResume();').Replace('@Override ', '').Replace('MainActivity', 'JinchanActivityHarness')
$onDestroy = $onDestroy.Replace('super.onDestroy();', 'superOnDestroy();').Replace('@Override ', '')
$createConnection = $createConnection.Replace('@Override ', '')
$timeout = $timeout.Replace('android.os.SystemClock', 'TestClock')
$ensure = $ensure.Replace('android.os.SystemClock', 'TestClock')
$ensure = $ensure.Replace('private void ensureServiceBound()', 'void ensureServiceBound()')
$sourceMethods = @($onCreate,$onResume,$onDestroy,$createConnection,$ensure,$release,$retry,$setError,$timeout,$refresh) -join "`r`n"

$out = [IO.Path]::GetFullPath($OutputDirectory)
$src = Join-Path $out 'src'; $classes = Join-Path $out 'classes'
$null = New-Item -ItemType Directory -Path $src -Force; $null = New-Item -ItemType Directory -Path $classes -Force
$template = @'
class Bundle {}
class TestClock { static long now=10000; static long elapsedRealtime(){return now;} }
class ComponentName {}
interface IBinder {}
interface ServiceConnection {
    void onServiceConnected(ComponentName n,IBinder b); void onServiceDisconnected(ComponentName n);
    void onBindingDied(ComponentName n); void onNullBinding(ComponentName n);
}
class Intent { final Object context; Intent(Object c,Class<?> target){context=c;} }
class Context {
    static final int BIND_AUTO_CREATE=1; int bindCalls,unbindCalls; boolean bindResult;
    final java.util.List<ServiceConnection> attempts=new java.util.ArrayList<ServiceConnection>();
    final java.util.List<Object> unboundContexts=new java.util.ArrayList<Object>();
    final java.util.List<Object> boundIntentContexts=new java.util.ArrayList<Object>();
    boolean bindService(Intent i,ServiceConnection c,int flags){bindCalls++;attempts.add(c);boundIntentContexts.add(i.context);return bindResult;}
    void unbindService(ServiceConnection c){unbindCalls++;unboundContexts.add(this);}
}
class Handler {
    static final class Entry { final Runnable task; final long at; Entry(Runnable r,long a){task=r;at=a;} }
    final java.util.List<Entry> queue=new java.util.ArrayList<Entry>();
    void post(Runnable r){queue.add(new Entry(r,TestClock.now));}
    void postDelayed(Runnable r,long d){removeCallbacks(r);queue.add(new Entry(r,TestClock.now+d));}
    void removeCallbacks(Runnable r){queue.removeIf(e->e.task==r);}
    void runDue(){boolean again=true;int guard=0;while(again&&guard++<100){again=false;for(int i=0;i<queue.size();i++){Entry e=queue.get(i);if(e.at<=TestClock.now){queue.remove(i);e.task.run();again=true;break;}}}}
}
class MainThread { static final Handler MAIN=new Handler(); }
class GlassSkin { static final int PAGE_BG=0; }
class Window { void setStatusBarColor(int x){} void setNavigationBarColor(int x){} }
class ConnectionService {
    String state="connected",pkg="game",token="token";
    String getState(){return state;} String getPackage(){return pkg;} String getToken(){return token;}
    static final class LocalBinder implements IBinder { final ConnectionService service; LocalBinder(ConnectionService s){service=s;} ConnectionService get(){return service;} }
}
class Button { void setEnabled(boolean b){} }
class TextView { void setText(String s){} }
class JSONObject { }
class JinchanActivityHarness {
    static final int BIND_AUTO_CREATE=Context.BIND_AUTO_CREATE;
    static final Handler MAIN=MainThread.MAIN;
    static java.lang.ref.WeakReference<JinchanActivityHarness> ACTIVE=new java.lang.ref.WeakReference<JinchanActivityHarness>(null);
    final Context applicationContext=new Context(); Context bindingContext; Window window=new Window();
    ConnectionService service; boolean bound,destroyed,resumed,assetsReady; ServiceConnection serviceConnection;
    @@LAST_BIND_ATTEMPT@@ long reconnectDeadline; String bindingError=""; TextView connectionStatus; int queryCalls,createCalls,returnCalls;
    Context getApplicationContext(){return applicationContext;} Window getWindow(){return window;}
    void bindCounterForbiddenOperation(String op){if("query".equals(op))queryCalls++;if("create".equals(op))createCalls++;if("return".equals(op))returnCalls++;}
    void buildUi(){} void copyAsset(String s) throws Exception{} String message(Exception e){return e.getMessage();} void setStatus(String s){}
    void showPendingResult(){} void updateButtons(){} void updateConnectionUi(){}
    void superOnCreate(Bundle b){} void superOnResume(){} void superOnDestroy(){}
    void superOnPause(){}
    @@PRODUCTION_METHODS@@
}
public final class JinchanBindingHarness {
    static int assertions; static final java.util.List<String> failures=new java.util.ArrayList<String>();
    static void check(boolean ok,String message){assertions++;if(!ok)failures.add(message);}
    static JinchanActivityHarness fresh(boolean accepted){JinchanActivityHarness a=new JinchanActivityHarness();a.applicationContext.bindResult=accepted;a.onCreate(new Bundle());return a;}
    static ServiceConnection attempt(JinchanActivityHarness a,int index){return a.applicationContext.attempts.get(index);}
    static void connected(JinchanActivityHarness a,ServiceConnection c){c.onServiceConnected(new ComponentName(),new ConnectionService.LocalBinder(new ConnectionService()));}
    static void mainChecks(){
        TestClock.now=10000; MAIN_RESET();
        JinchanActivityHarness rejected=fresh(false); rejected.onResume();
        check(rejected.applicationContext.bindCalls==1,"cold start must try binding by onResume even when onCreate does not bind");
        check(!rejected.bound&&rejected.applicationContext.unbindCalls==1,"bindService=false must remain unbound and release the attempted registration");
        check(rejected.applicationContext.attempts.size()==1,"rejected bind attempt has one connection");
        check(rejected.applicationContext.boundIntentContexts.get(0)==rejected.bindingContext,"bind uses the stored ApplicationContext instance");
        TestClock.now+=4999; rejected.ensureServiceBound();
        check(rejected.applicationContext.bindCalls==1,"failed bind must respect five-second retry delay");
        TestClock.now+=1; rejected.ensureServiceBound();
        check(rejected.applicationContext.bindCalls==2,"failed bind must retry after five seconds");

        MAIN_RESET(); TestClock.now=20000; JinchanActivityHarness active=fresh(true); active.onResume();
        check(active.bound,"accepted bind sets bound true"); check(active.bindingContext==active.applicationContext,"binding uses the same ApplicationContext for bind/unbind");
        active.ensureServiceBound(); check(active.applicationContext.bindCalls==1,"successful outstanding bind is not duplicated");
        ServiceConnection first=attempt(active,0); connected(active,first); active.ensureServiceBound();
        check(active.service!=null&&active.applicationContext.bindCalls==1,"connected service is not rebound");
        first.onServiceDisconnected(new ComponentName());
        check(active.bound&&active.service==null,"service disconnect keeps registration while awaiting system reconnect");
        TestClock.now+=4999; MainThread.MAIN.runDue(); active.ensureServiceBound();
        check(active.applicationContext.bindCalls==1&&active.applicationContext.unbindCalls==0,"disconnect does not release/rebind before five seconds");
        TestClock.now+=1; MainThread.MAIN.runDue();
        check(active.applicationContext.bindCalls==2&&active.applicationContext.unbindCalls==1,"disconnect timeout releases old registration and retries");
        ServiceConnection second=attempt(active,1); check(second!=first,"each rebind gets a distinct ServiceConnection");
        first.onServiceConnected(new ComponentName(),new ConnectionService.LocalBinder(new ConnectionService()));
        check(active.service==null,"stale callback from previous attempt cannot revive service");
        connected(active,second); check(active.service!=null,"current attempt can connect");

        MAIN_RESET(); TestClock.now=20000; JinchanActivityHarness pending=fresh(true); pending.onResume();
        check(pending.bound&&pending.service==null,"accepted bind without connected callback waits in bound state");
        TestClock.now+=4999; MainThread.MAIN.runDue(); pending.ensureServiceBound();
        check(pending.applicationContext.bindCalls==1&&pending.applicationContext.unbindCalls==0,"missing initial connected callback waits for five-second deadline");
        TestClock.now+=1; MainThread.MAIN.runDue();
        check(pending.applicationContext.bindCalls==2&&pending.applicationContext.unbindCalls==1,"missing initial connected callback releases and retries after five seconds");
        check(pending.applicationContext.unboundContexts.get(0)==pending.bindingContext,"missing callback timeout releases using original bind context");

        MAIN_RESET(); TestClock.now=0; JinchanActivityHarness uptimeZero=fresh(true); uptimeZero.onResume();
        check(uptimeZero.applicationContext.bindCalls==1&&uptimeZero.bound,"first onResume attempts binding when device uptime is zero");

        MAIN_RESET(); TestClock.now=30000; JinchanActivityHarness died=fresh(true); died.onResume(); ServiceConnection diedConn=attempt(died,0); diedConn.onBindingDied(new ComponentName());
        check(!died.bound&&died.service==null&&died.applicationContext.unbindCalls==1,"onBindingDied releases registration and does not fake connection");
        TestClock.now+=1000; MainThread.MAIN.runDue(); check(died.applicationContext.bindCalls==1,"onBindingDied does not retry on the next one-second refresh");
        TestClock.now+=3999; MainThread.MAIN.runDue(); check(died.applicationContext.bindCalls==1,"onBindingDied waits full five seconds from event time");
        TestClock.now+=1; MainThread.MAIN.runDue(); check(died.applicationContext.bindCalls==2,"onBindingDied retries at event plus five seconds");
        MAIN_RESET(); TestClock.now=40000; JinchanActivityHarness nullBind=fresh(true); nullBind.onResume(); ServiceConnection nullConn=attempt(nullBind,0); nullConn.onNullBinding(new ComponentName());
        check(!nullBind.bound&&nullBind.service==null&&nullBind.applicationContext.unbindCalls==1,"onNullBinding releases registration and does not fake connection");
        TestClock.now+=1000; MainThread.MAIN.runDue(); check(nullBind.applicationContext.bindCalls==1,"onNullBinding does not retry on the next one-second refresh");
        TestClock.now+=3999; MainThread.MAIN.runDue(); check(nullBind.applicationContext.bindCalls==1,"onNullBinding waits full five seconds from event time");
        TestClock.now+=1; MainThread.MAIN.runDue(); check(nullBind.applicationContext.bindCalls==2,"onNullBinding retries at event plus five seconds");

        MAIN_RESET(); TestClock.now=50000; JinchanActivityHarness doomed=fresh(true); doomed.onResume(); ServiceConnection old=attempt(doomed,0); connected(doomed,old); doomed.onDestroy();
        check(doomed.applicationContext.unbindCalls==1,"destroy releases registered connection");
        check(doomed.applicationContext.unboundContexts.get(0)==doomed.bindingContext,"destroy unbind uses the exact context used for bind");
        check(!doomed.bound&&doomed.service==null,"destroy clears binding and service state");
        old.onServiceDisconnected(new ComponentName()); old.onBindingDied(new ComponentName()); old.onNullBinding(new ComponentName());
        old.onServiceConnected(new ComponentName(),new ConnectionService.LocalBinder(new ConnectionService()));
        TestClock.now+=10000; MainThread.MAIN.runDue(); doomed.ensureServiceBound();
        check(doomed.service==null&&!doomed.bound&&doomed.applicationContext.bindCalls==1,"destroyed Activity and late callbacks cannot rebind or revive service");
        check(doomed.queryCalls==0&&doomed.createCalls==0&&doomed.returnCalls==0,"binding failures and callbacks never invoke query/create/return");
    }
    static void MAIN_RESET(){MainThread.MAIN.queue.clear();}
    public static void main(String[] args){mainChecks();
        if(failures.isEmpty())System.out.println("JinchanBindingHarness PASS ("+assertions+" assertions; extracted onCreate/onResume/onDestroy and production bind/release/callback/retry methods)");
        else {System.out.println("JinchanBindingHarness FAIL ("+failures.size()+" failures / "+assertions+" assertions; extracted production lifecycle and binding methods)");for(String f:failures)System.out.println("FAIL: "+f);throw new AssertionError("production binding lifecycle assertions failed");}
    }
}
'@
$source = $template.Replace('@@PRODUCTION_METHODS@@', $sourceMethods).Replace('@@LAST_BIND_ATTEMPT@@', $lastBindAttempt)
$sourcePath = Join-Path $src 'JinchanBindingHarness.java'
[IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($false))
& $javac --release 8 -encoding UTF-8 -d $classes $sourcePath
if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
& $java -cp $classes JinchanBindingHarness
if ($LASTEXITCODE -ne 0) { throw "binding lifecycle harness failed with exit code $LASTEXITCODE" }
