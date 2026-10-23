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
    long lastBindAttempt=-5000L; long reconnectDeadline; String bindingError=""; TextView connectionStatus; int queryCalls,createCalls,returnCalls;
    Context getApplicationContext(){return applicationContext;} Window getWindow(){return window;}
    void bindCounterForbiddenOperation(String op){if("query".equals(op))queryCalls++;if("create".equals(op))createCalls++;if("return".equals(op))returnCalls++;}
    void buildUi(){} void copyAsset(String s) throws Exception{} String message(Exception e){return e.getMessage();} void setStatus(String s){}
    void showPendingResult(){} void updateButtons(){} void updateConnectionUi(){}
    void superOnCreate(Bundle b){} void superOnResume(){} void superOnDestroy(){}
    void superOnPause(){}
    public void onCreate(Bundle state) {
        superOnCreate(state);
        bindingContext=getApplicationContext();
        getWindow().setStatusBarColor(GlassSkin.PAGE_BG);
        getWindow().setNavigationBarColor(GlassSkin.PAGE_BG);
        buildUi();
        try { copyAsset("jinchan_agent.js"); copyAsset("jinchan.lua.in"); assetsReady=true; }
        catch (Exception e) { setStatus("资源准备失败：" + message(e)); }
        showPendingResult();
        updateButtons();
    }
protected void onResume() { superOnResume(); resumed=true; ACTIVE = new java.lang.ref.WeakReference<>(this); ensureServiceBound(); showPendingResult(); updateButtons(); updateConnectionUi(); MAIN.removeCallbacks(connectionRefresh); MAIN.post(connectionRefresh); }
protected void onDestroy(){ destroyed=true; resumed=false; MAIN.removeCallbacks(connectionRefresh); MAIN.removeCallbacks(reconnectTimeout); releaseServiceBinding(); service=null; superOnDestroy(); }
private ServiceConnection createServiceConnection() {
        return new ServiceConnection() {
            public void onServiceConnected(ComponentName name, IBinder binder) {
                if (destroyed || serviceConnection != this) return;
                reconnectDeadline=0; MAIN.removeCallbacks(reconnectTimeout);
                bindingError=""; service=((ConnectionService.LocalBinder)binder).get(); showPendingResult(); updateConnectionUi();
            }
            public void onServiceDisconnected(ComponentName name) {
                if (destroyed || serviceConnection != this) return;
                service=null; reconnectDeadline=TestClock.elapsedRealtime()+5000;
                MAIN.removeCallbacks(reconnectTimeout); MAIN.postDelayed(reconnectTimeout,5000); updateConnectionUi();
            }
            public void onBindingDied(ComponentName name) {
                if (destroyed || serviceConnection != this) return;
                releaseServiceBinding(); setBindingError("连接服务绑定已失效，5 秒后重试"); scheduleBindRetry();
            }
            public void onNullBinding(ComponentName name) {
                if (destroyed || serviceConnection != this) return;
                releaseServiceBinding(); setBindingError("连接服务未提供绑定接口，5 秒后重试"); scheduleBindRetry();
            }
        };
    }
void ensureServiceBound() {
        if (destroyed || !resumed) return;
        if (bound && service == null && reconnectDeadline != 0 && TestClock.elapsedRealtime() >= reconnectDeadline) { releaseServiceBinding(); lastBindAttempt=TestClock.elapsedRealtime()-5000; }
        if (bound || TestClock.elapsedRealtime()-lastBindAttempt<5000) return;
        lastBindAttempt=TestClock.elapsedRealtime();
        ServiceConnection attempt=createServiceConnection(); serviceConnection=attempt;
        setBindingError("正在绑定连接服务");
        try {
            boolean accepted=bindingContext.bindService(new Intent(bindingContext,ConnectionService.class),attempt,Context.BIND_AUTO_CREATE);
            bound=accepted;
            if (accepted) {
                reconnectDeadline=TestClock.elapsedRealtime()+5000;
                MAIN.removeCallbacks(reconnectTimeout); MAIN.postDelayed(reconnectTimeout,5000);
            } else {
                try { bindingContext.unbindService(attempt); } catch (IllegalArgumentException ignored) { }
                if(serviceConnection==attempt)serviceConnection=null;
                service=null; setBindingError("连接服务绑定失败，5 秒后重试");
            }
        } catch (RuntimeException failure) {
            if(serviceConnection==attempt)serviceConnection=null; bound=false; service=null;
            setBindingError("连接服务绑定异常："+(failure.getMessage()==null?failure.getClass().getSimpleName():failure.getMessage()));
        }
    }
private void releaseServiceBinding() {
        MAIN.removeCallbacks(reconnectTimeout);
        ServiceConnection old=serviceConnection; boolean wasBound=bound;
        serviceConnection=null; bound=false; service=null; reconnectDeadline=0;
        if(wasBound&&old!=null) try { bindingContext.unbindService(old); } catch (IllegalArgumentException ignored) { }
    }
private void scheduleBindRetry() { lastBindAttempt=TestClock.elapsedRealtime(); MAIN.postDelayed(() -> { if(resumed&&!destroyed)ensureServiceBound(); },5000); }
private void setBindingError(String detail) { bindingError=detail; if(connectionStatus!=null)connectionStatus.setText("连接："+detail); updateButtons(); }
private final Runnable reconnectTimeout = () -> {
        if (destroyed || !resumed || !bound || service != null || reconnectDeadline == 0 || TestClock.elapsedRealtime() < reconnectDeadline) return;
        releaseServiceBinding();
        lastBindAttempt=TestClock.elapsedRealtime()-5000;
        ensureServiceBound();
    };
private final Runnable connectionRefresh = new Runnable() { @Override public void run() { if (!resumed || destroyed) return; ensureServiceBound(); updateConnectionUi(); MAIN.postDelayed(this, 1000); } };
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