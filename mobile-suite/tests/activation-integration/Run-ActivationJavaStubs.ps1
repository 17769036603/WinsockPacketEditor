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
$tempRoot = Join-Path $env:TEMP ('activation-java-stubs-' + [guid]::NewGuid().ToString('N'))
$appSource = Resolve-Path (Join-Path $PSScriptRoot '..\..\app\src\main\java\com\xnas\wpe\suite')
$null = New-Item -ItemType Directory -Path $tempRoot

try {
    $folders = @(
        'android\app', 'android\content', 'android\os', 'android\util', 'org\json',
        'com\xnas\wpe\suite', 'com\xnas\wpe\anlei', 'com\xnas\wpe\mounthold',
        'com\xnas\wpe\battleloop', 'com\xnas\wpe\battlepanel', 'com\xnas\wpe\jinchan',
        'com\xnas\wpe\starhunt', 'com\xnas\wpe\bosshunt'
    )
    foreach ($folder in $folders) { $null = New-Item -ItemType Directory -Path (Join-Path $tempRoot $folder) }

    $sources = @{
        'android\content\Context.java' = @'
package android.content;
import java.io.File;
import java.util.*;
import android.content.pm.PackageManager;
public class Context {
  public Context getApplicationContext(){return this;}
  public SharedPreferences getSharedPreferences(String n,int m){return MockPrefs.INSTANCE;}
  public File getFilesDir(){return new File(System.getProperty("activation.stub.files"));}
  public PackageManager getPackageManager(){return PackageManager.INSTANCE;}
  public Object getMainLooper(){return this;}
  public static int starts;
  public static final List<String> startActions=new ArrayList<>();
  public void startService(Intent i){starts++;startActions.add(i.target.getName()+":"+i.action);}
  public void startForegroundService(Intent i){starts++;startActions.add(i.target.getName()+":"+i.action);}
  public static void resetStarts(){starts=0;startActions.clear();}
  public static final int MODE_PRIVATE=0;
}
'@
        'android\content\pm\PackageInfo.java' = 'package android.content.pm; public class PackageInfo { public int versionCode; public String versionName; }'
        'android\content\pm\PackageManager.java' = @'
package android.content.pm;
import java.util.*;
public class PackageManager {
  public static final PackageManager INSTANCE=new PackageManager();
  public final Map<String,PackageInfo> packages=new HashMap<>();
  public PackageInfo getPackageInfo(String name,int flags)throws NameNotFoundException {
    PackageInfo info=packages.get(name); if(info==null)throw new NameNotFoundException(name); return info;
  }
  public static class NameNotFoundException extends Exception { public NameNotFoundException(String n){super(n);} }
}
'@
        'android\content\SharedPreferences.java' = @'
package android.content;
public interface SharedPreferences {
  String getString(String k,String d); boolean contains(String k); Editor edit();
  interface Editor { Editor putString(String k,String v); Editor remove(String k); boolean commit(); void apply(); }
}
'@
        'android\content\MockPrefs.java' = @'
package android.content;
import java.util.*;
public class MockPrefs implements SharedPreferences {
  public static final MockPrefs INSTANCE=new MockPrefs(); final Map<String,String> m=new HashMap<>();
  public String getString(String k,String d){return m.getOrDefault(k,d);}
  public boolean contains(String k){return m.containsKey(k);}
  public Editor edit(){return new E();}
  class E implements Editor { Map<String,String> put=new HashMap<>(); Set<String> remove=new HashSet<>();
    public Editor putString(String k,String v){put.put(k,v);return this;}
    public Editor remove(String k){remove.add(k);return this;}
    public boolean commit(){remove.forEach(m::remove);m.putAll(put);return true;}
    public void apply(){commit();}
  }
}
'@
        'android\content\Intent.java' = 'package android.content; public class Intent { public final Class<?> target; public String action; public Intent(Context c,Class<?> k){target=k;} public Intent setAction(String a){action=a;return this;} }'
        'android\app\Application.java' = 'package android.app; import android.content.*; public class Application extends Context { public void onCreate(){} }'
        'android\os\SystemClock.java' = 'package android.os; public class SystemClock { public static long elapsedRealtime(){return System.nanoTime()/1000000L;} }'
        'android\os\Build.java' = 'package android.os; public class Build { public static class VERSION { public static int SDK_INT=35; } }'
        'android\os\Handler.java' = 'package android.os; public class Handler { public Handler(Object l){} public boolean post(Runnable r){r.run();return true;} }'
        'android\util\Base64.java' = 'package android.util; public class Base64 { public static final int URL_SAFE=1,NO_WRAP=2,NO_PADDING=4; public static String encodeToString(byte[] b,int f){return java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(b);} }'
        'org\json\JSONObject.java' = @'
package org.json;
import java.util.*;
public class JSONObject {
  final Map<String,Object> m=new HashMap<>(); public JSONObject(){}
  public JSONObject(String s){
    if(s==null)throw new IllegalArgumentException("null JSON");
    String t=s.trim(); if(!t.startsWith("{")||!t.endsWith("}"))throw new IllegalArgumentException("invalid JSON object");
    java.util.regex.Matcher x=java.util.regex.Pattern.compile("\\\"([^\\\"]+)\\\"\\s*:\\s*(?:\\\"([^\\\"]*)\\\"|(-?[0-9]+)|null)").matcher(t);
    while(x.find())m.put(x.group(1),x.group(2)!=null?x.group(2):x.group(3)!=null?Long.valueOf(x.group(3)):null);
    String residue=t.replaceAll("\\\"[^\\\"]+\\\"\\s*:\\s*(?:\\\"[^\\\"]*\\\"|-?[0-9]+|null)","").replaceAll("[{},\\s]","");
    if(!residue.isEmpty())throw new IllegalArgumentException("invalid JSON object");
  }
  public JSONObject put(String k,Object v){m.put(k,v);return this;}
  public String getString(String k){return String.valueOf(m.get(k));}
  public String optString(String k,String d){Object v=m.get(k);return v==null?d:String.valueOf(v);}
  public Object opt(String k){return m.get(k);}
  public long getLong(String k){return ((Number)m.get(k)).longValue();}
  public String toString(){return "{}";}
}
'@
        'com\xnas\wpe\suite\BuildConfig.java' = 'package com.xnas.wpe.suite; public class BuildConfig { public static String PIAOMIAO_LICENSE_URL=""; public static boolean PIAOMIAO_LICENSE_BYPASS=false; public static String PIAOMIAO_BYPASS_GAME_PACKAGES="com.gdoo.dhxy,com.gdoo.xxxy2026"; public static int PIAOMIAO_BYPASS_GAME_VERSION_CODE=1; public static String PIAOMIAO_BYPASS_GAME_VERSION_NAME="1.0"; public static String VERSION_NAME="test-version"; }'
        'com\xnas\wpe\anlei\AnleiService.java' = 'package com.xnas.wpe.anlei; public class AnleiService {}'
        'com\xnas\wpe\mounthold\MountHoldService.java' = 'package com.xnas.wpe.mounthold; public class MountHoldService { public static final String ACTION_STOP="stop"; }'
        'com\xnas\wpe\battleloop\BattleLoopService.java' = 'package com.xnas.wpe.battleloop; public class BattleLoopService { public static final String STOP="stop"; }'
        'com\xnas\wpe\battlepanel\BattleOverlayService.java' = 'package com.xnas.wpe.battlepanel; public class BattleOverlayService { public static final String ACTION_STOP="stop"; }'
        'com\xnas\wpe\jinchan\ConnectionService.java' = 'package com.xnas.wpe.jinchan; public class ConnectionService {}'
        'com\xnas\wpe\starhunt\StarHuntService.java' = 'package com.xnas.wpe.starhunt; public class StarHuntService { public static final String ACTION_STOP_RUN="stop-run", ACTION_STOP_READ="stop-read"; }'
        'com\xnas\wpe\bosshunt\BossHuntService.java' = 'package com.xnas.wpe.bosshunt; public class BossHuntService { public static final String ACTION_STOP_READ="stop-read"; }'
        'com\xnas\wpe\suite\ScheduleService.java' = 'package com.xnas.wpe.suite; public class ScheduleService { public static void startIfNeeded(android.content.Context context){} }'
        'com\xnas\wpe\suite\ActivationStubHarness.java' = @'
package com.xnas.wpe.suite;
import android.app.Application;
import android.content.*;
import java.io.*;
import java.net.*;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
public final class ActivationStubHarness {
  static final Context context=new Context();
  static final String TOKEN="session-token-preserved-1234567890";
  static final AtomicInteger response=new AtomicInteger(500);
  static final java.util.List<String> regressions=new java.util.ArrayList<>();
  static volatile String errorBody="{}";
  static volatile String requestUserAgent="";
  static final class URLCalls { static int count; }
  static final class HttpsHandler extends URLStreamHandler {
    protected URLConnection openConnection(URL u){URLCalls.count++;return new HttpURLConnection(u){
      public void disconnect(){} public boolean usingProxy(){return false;} public void connect(){}
      public OutputStream getOutputStream() throws IOException {if(response.get()==0)throw new IOException("simulated offline");return new ByteArrayOutputStream();}
      public void setRequestProperty(String k,String v){if("User-Agent".equalsIgnoreCase(k))requestUserAgent=v;}
      public int getResponseCode(){return response.get();}
      public InputStream getErrorStream(){return new ByteArrayInputStream(errorBody.getBytes(java.nio.charset.StandardCharsets.UTF_8));}
    };}
  }
  static File lease(){return new File(context.getFilesDir(),"license-lease.json");}
  static void seedAuthorization()throws Exception {
    context.getSharedPreferences("suite_license_private_v1",Context.MODE_PRIVATE).edit().putString("session_token",TOKEN).commit();
    lease().getParentFile().mkdirs(); try(FileOutputStream out=new FileOutputStream(lease())){out.write("old-lease".getBytes());}
  }
  static String verify(int status,String jsonErrorBody)throws Exception {
    response.set(status); errorBody=jsonErrorBody; CountDownLatch done=new CountDownLatch(1); AtomicReference<String> message=new AtomicReference<>("");
    LicenseManager.verify(context,(m,ok)->{message.set(m);done.countDown();});
    if(!done.await(4,TimeUnit.SECONDS))throw new AssertionError("verify "+status+" timed out");
    Thread.sleep(30);
    return message.get();
  }
  static void verify(int status,String jsonErrorBody,boolean expectedSession,boolean expectedLease,int expectedStops,String label)throws Exception {
    String message=verify(status,jsonErrorBody);
    boolean actual=LicenseManager.hasSession(context);
    if(actual!=expectedSession)throw new AssertionError(label+" expected session="+expectedSession+" got="+actual);
    if(lease().isFile()!=expectedLease)throw new AssertionError(label+" expected lease="+expectedLease+" got="+lease().isFile());
    if(Context.starts!=expectedStops)throw new AssertionError(label+" expected STOP requests="+expectedStops+" got="+Context.starts);
    System.out.println(label+": PASS"+(message.isEmpty()?"":" ("+message+")"));
  }
  static java.util.Set<String> stopSet(String... actions){return new java.util.HashSet<>(java.util.Arrays.asList(actions));}
  static java.util.Set<String> completeStopWave(){return stopSet(
    "com.xnas.wpe.bosshunt.BossHuntService:stop-read",
    "com.xnas.wpe.anlei.AnleiService:stop",
    "com.xnas.wpe.mounthold.MountHoldService:stop",
    "com.xnas.wpe.battleloop.BattleLoopService:stop",
    "com.xnas.wpe.battlepanel.BattleOverlayService:stop",
    "com.xnas.wpe.jinchan.ConnectionService:stop",
    "com.xnas.wpe.starhunt.StarHuntService:stop-run",
    "com.xnas.wpe.starhunt.StarHuntService:stop-read");}
  static boolean exactStopWave(java.util.Set<String> expected){
    return Context.starts==expected.size()&&Context.startActions.size()==expected.size()
      &&new java.util.HashSet<>(Context.startActions).equals(expected);
  }
  static void assertStopWave(String label,int expectedStops){
    java.util.Set<String> expected=expectedStops==0?java.util.Collections.emptySet():completeStopWave();
    if(expectedStops!=expected.size()||!exactStopWave(expected))
      throw new AssertionError(label+" expected exact STOP actions="+expected+" got="+Context.startActions);
  }
  static void activateRejected(int status,String jsonErrorBody,String expectedMessage)throws Exception {
    seedAuthorization(); Context.resetStarts(); response.set(status); CountDownLatch done=new CountDownLatch(1);
    errorBody=jsonErrorBody; AtomicReference<String> message=new AtomicReference<>("");
    LicenseManager.activate(context,"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",(m,ok)->{message.set(m);done.countDown();});
    if(!done.await(4,TimeUnit.SECONDS))throw new AssertionError("activate "+status+" timed out");
    Thread.sleep(30);
    if(!LicenseManager.hasSession(context)||!lease().isFile()||Context.starts!=0)
      throw new AssertionError("activate "+status+" must preserve session/lease and avoid STOP");
    if(expectedMessage!=null&&!message.get().contains(expectedMessage))throw new AssertionError("activate "+status+" missing message text: "+message.get());
    if(status==403&&!message.get().contains("访问被拒绝")&&!message.get().contains("网络")&&!message.get().contains("激活请求被拒绝")&&!message.get().contains("授权已到期"))
      throw new AssertionError("activate 403 must report access/network/business rejection, not invalid or used code: "+message.get());
    System.out.println("activate HTTP "+status+" preserves existing authorization: PASS ("+message.get()+")");
  }
  static void verifyRejected(int status,String body,boolean preserveSession,boolean preserveLease,int expectedStops,String label)throws Exception {
    seedAuthorization(); if(preserveLease)setLeaseActive(true); Context.resetStarts();
    String message=verify(status,body);
    if(LicenseManager.hasSession(context)!=preserveSession)throw new AssertionError(label+" session preservation mismatch");
    if(lease().isFile()!=preserveLease)throw new AssertionError(label+" lease preservation mismatch");
    assertStopWave(label,expectedStops);
    if(status==403&&!body.contains("LICENSE_EXPIRED")&&!message.contains("暂不可用")&&!message.contains("访问被拒绝"))throw new AssertionError(label+" should report temporary unavailability or service access rejection: "+message);
    if(status==403&&body.contains("LICENSE_EXPIRED")&&!message.contains("授权已到期"))throw new AssertionError(label+" should report expiry: "+message);
    System.out.println(label+": PASS ("+message+")");
  }
  static void setLeaseActive(boolean active)throws Exception {
    java.lang.reflect.Field deadline=LicenseManager.class.getDeclaredField("deadlineElapsed"); deadline.setAccessible(true);
    deadline.setLong(null,android.os.SystemClock.elapsedRealtime()+(active?60000:-1000));
    java.lang.reflect.Field boot=LicenseManager.class.getDeclaredField("bootId"); boot.setAccessible(true);
    boot.set(null,java.nio.file.Files.readString(java.nio.file.Path.of(System.getProperty("activation.stub.boot"))).trim());
  }
  static void verifyAppScenario(int status,boolean active,boolean expectedLease,int expectedStops,String label)throws Exception {
    seedAuthorization(); setLeaseActive(active); Context.resetStarts(); response.set(status);
    SuiteApplication app=new SuiteApplication(); app.onCreate();
    var field=SuiteApplication.class.getDeclaredField("supervisor"); field.setAccessible(true);
    ((java.util.concurrent.ScheduledExecutorService)field.get(app)).shutdownNow();
    java.lang.reflect.Field request=LicenseManager.class.getDeclaredField("requestActive"); request.setAccessible(true);
    java.util.concurrent.atomic.AtomicBoolean busy=(java.util.concurrent.atomic.AtomicBoolean)request.get(null);
    long end=System.nanoTime()+TimeUnit.SECONDS.toNanos(4);
    while(busy.get()&&System.nanoTime()<end) Thread.sleep(10);
    if(busy.get())throw new AssertionError(label+" request timed out");
    Thread.sleep(50);
    assertStopWave(label,expectedStops);
    check(lease().isFile()==expectedLease,label+" expected lease="+expectedLease+" got="+lease().isFile());
    check(LicenseManager.hasSession(context)==(status!=401),label+(status==401?" must clear rejected session":" must preserve session for recovery"));
    System.out.println(label+(regressions.isEmpty()?": PASS":": executed"));
  }
  static void check(boolean ok,String failure){if(!ok)regressions.add(failure);}
  static void tick(SuiteApplication app)throws Exception {
    var method=SuiteApplication.class.getDeclaredMethod("tick"); method.setAccessible(true); method.invoke(app);
  }
  static void bypassTests()throws Exception {
    BuildConfig.PIAOMIAO_LICENSE_BYPASS=true;
    android.content.pm.PackageManager pm=context.getPackageManager();
    android.content.pm.PackageInfo good=new android.content.pm.PackageInfo(); good.versionCode=1; good.versionName="1.0";
    pm.packages.put("com.gdoo.dhxy",good); pm.packages.put("com.gdoo.xxxy2026",good);
    SuiteApplication app=new SuiteApplication(); app.onCreate();
    var field=SuiteApplication.class.getDeclaredField("supervisor"); field.setAccessible(true);
    ((java.util.concurrent.ScheduledExecutorService)field.get(app)).shutdownNow();
    check(LicenseManager.BYPASS,"explicit bypass build switch should be enabled");
    check(LicenseManager.isActive(),"supported selected game version should be allowed");
    check(LicenseManager.displayStatus(context).contains("限指定游戏版本"),"bypass status should describe the game version limit");
    pm.packages.get("com.gdoo.dhxy").versionCode=2;
    check(!LicenseManager.isActive(),"wrong game versionCode must be denied");
    Context.resetStarts(); tick(app); check(exactStopWave(completeStopWave()),"wrong version should request exactly one STOP action for every affected service");
    tick(app); check(Context.starts==8,"repeated version checks must not duplicate stop requests");
    pm.packages.get("com.gdoo.dhxy").versionCode=1; tick(app);
    pm.packages.get("com.gdoo.dhxy").versionName="1.1";
    check(!LicenseManager.isActive(),"wrong game versionName must be denied");
    Context.resetStarts(); tick(app); check(exactStopWave(completeStopWave()),"restoring then changing the version must dispatch one fresh STOP action per affected service");
    tick(app); check(Context.starts==8,"a second mismatch check must not duplicate stop requests");
    pm.packages.get("com.gdoo.dhxy").versionName="1.0"; tick(app);
    pm.packages.remove("com.gdoo.dhxy"); pm.packages.get("com.gdoo.xxxy2026").versionName="1.0";
    TargetGame.setSelectedPackage(context,"com.gdoo.dhxy");
    check(!LicenseManager.isActive(),"missing selected game package must be denied");
    pm.packages.put("com.gdoo.dhxy",good);
    context.getSharedPreferences("suite_target",Context.MODE_PRIVATE).edit().putString("game_package","com.gdoo.unknown").commit();
    check(!LicenseManager.isActive(),"unknown selected package must be denied");
    TargetGame.setSelectedPackage(context,"com.gdoo.dhxy");
    java.util.concurrent.atomic.AtomicInteger callbacks=new java.util.concurrent.atomic.AtomicInteger();
    int before=URLCalls.count;
    LicenseManager.activate(context,"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",(m,ok)->{if(!ok)throw new AssertionError(m);callbacks.incrementAndGet();});
    LicenseManager.verify(context,(m,ok)->{if(!ok)throw new AssertionError(m);callbacks.incrementAndGet();});
    check(callbacks.get()==2,"bypass activation and verify should callback successfully without network");
    check(URLCalls.count==before,"bypass activation and verify must not send network requests");
    pm.packages.get("com.gdoo.dhxy").versionCode=3;
    LicenseManager.activate(context,"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",(m,ok)->{if(ok)throw new AssertionError("wrong version accepted");callbacks.incrementAndGet();});
    check(!LicenseManager.isActive(),"wrong selected game version must keep features blocked");
    check(URLCalls.count==before,"wrong-version bypass attempt must not send network requests");
    if(!regressions.isEmpty()){for(String failure:regressions)System.err.println("FAIL: "+failure);System.exit(1);}
    System.out.println("friend bypass version gate, missing package rejection, and no-network checks: PASS");
  }
  static void repeatedExpireDispatchesOnce()throws Exception {
    seedAuthorization(); Context.resetStarts(); LicenseManager.expire(context); LicenseManager.expire(context);
    if(!exactStopWave(completeStopWave()))throw new AssertionError("repeated expire expected exactly one STOP action per affected service, got "+Context.startActions);
    System.out.println("repeated expire dispatches one exact STOP wave: PASS");
  }
  static void shutdownLicenseRequests()throws Exception {
    java.lang.reflect.Field field=LicenseManager.class.getDeclaredField("requests"); field.setAccessible(true);
    ((java.util.concurrent.ExecutorService)field.get(null)).shutdownNow();
  }
  public static void main(String[] args)throws Exception {
    try {
    if(args.length>0&&args[0].equals("bypass")){ bypassTests(); return; }
    URLCalls.count=0;
    URL.setURLStreamHandlerFactory(p->p.equals("https")?new HttpsHandler():null);
    if(LicenseManager.BYPASS)throw new AssertionError("normal build must keep license bypass disabled by default");
    SuiteApplication app=new SuiteApplication(); app.onCreate();
    var field=SuiteApplication.class.getDeclaredField("supervisor"); field.setAccessible(true);
    ((java.util.concurrent.ScheduledExecutorService)field.get(app)).shutdownNow();
    if(Context.starts!=0)throw new AssertionError("unconfigured cold start requested "+Context.starts+" service stops");
    System.out.println("unconfigured, unactivated cold start starts 0 services: PASS");

    BuildConfig.PIAOMIAO_LICENSE_URL="https://license.test";
    activateRejected(401,"{}",null);
    activateRejected(403,"error code: 1010\n","访问被拒绝");
    activateRejected(403,"<html>Forbidden</html>","访问被拒绝");
    activateRejected(403,"","访问被拒绝");
    activateRejected(403,"{\"error\":\"授权已失效\"}","激活请求被拒绝");
    activateRejected(409,"{\"error\":\"激活码已绑定到其他安装\"}","已绑定其他安装");
    verifyRejected(403,"{\"code\":\"LICENSE_EXPIRED\"}",true,false,8,"verify LICENSE_EXPIRED retains session, clears lease, and requests STOP");
    verifyRejected(403,"{\"code\":\"1010\"}",true,true,0,"verify HTTP 403 text-code error retains active authorization");
    verifyRejected(403,"<html>Forbidden</html>",true,true,0,"verify HTTP 403 HTML retains active authorization");
    verifyRejected(403,"",true,true,0,"verify HTTP 403 empty body retains active authorization");
    verifyRejected(403,"{\"error\":\"revoked\"}",true,true,0,"verify HTTP 403 business error retains active authorization");
    verifyRejected(401,"{}",false,false,8,"verify HTTP 401 clears authorization and requests STOP");
    seedAuthorization(); Context.starts=0;
    verify(500,"{}",true,true,0,"HTTP 500 verify retains session and lease");
    seedAuthorization(); Context.starts=0;
    verify(0,"{}",true,true,0,"connection failure retains session and lease");
    repeatedExpireDispatchesOnce();
    if(!requestUserAgent.equals("PiaomiaoHelper/"+BuildConfig.VERSION_NAME+" (Android)"))throw new AssertionError("unexpected request User-Agent: "+requestUserAgent);
    System.out.println("request User-Agent matches product/version contract: PASS");
    verifyAppScenario(0,true,true,0,"SuiteApplication network failure with active lease sends no STOP");
    verifyAppScenario(0,false,false,8,"SuiteApplication network failure with expired lease clears lease, preserves session, and sends one STOP wave");
    verifyAppScenario(401,false,false,8,"SuiteApplication HTTP 401 invalidation and callback send one STOP wave");
    verifyAppScenario(403,false,false,8,"SuiteApplication verification failure with exhausted lease sends one safety STOP wave");
    if(regressions.isEmpty()) { System.out.println("all activation integration checks passed"); System.exit(0); }
    for(String failure:regressions)System.err.println("FAIL: "+failure);
    System.exit(1);
    } finally {
      shutdownLicenseRequests();
    }
  }
}
'@
    }

    foreach ($entry in $sources.GetEnumerator()) {
        $target = Join-Path $tempRoot $entry.Key
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        [IO.File]::WriteAllText($target, $entry.Value, [Text.UTF8Encoding]::new($false))
    }
    Copy-Item (Join-Path $appSource 'SuiteApplication.java') (Join-Path $tempRoot 'com\xnas\wpe\suite\SuiteApplication.java')
    Copy-Item (Join-Path $appSource 'LicenseManager.java') (Join-Path $tempRoot 'com\xnas\wpe\suite\LicenseManager.java')
    Copy-Item (Join-Path $appSource 'LicenseLeaseWindow.java') (Join-Path $tempRoot 'com\xnas\wpe\suite\LicenseLeaseWindow.java')
    Copy-Item (Join-Path $appSource 'InstallationHint.java') (Join-Path $tempRoot 'com\xnas\wpe\suite\InstallationHint.java')
    Copy-Item (Join-Path $appSource 'TargetGame.java') (Join-Path $tempRoot 'com\xnas\wpe\suite\TargetGame.java')
    $bootIdFile = Join-Path $tempRoot 'boot-id'
    [IO.File]::WriteAllText($bootIdFile, 'test-boot-id', [Text.UTF8Encoding]::new($false))
    $licenseManagerCopy = Join-Path $tempRoot 'com\xnas\wpe\suite\LicenseManager.java'
    $bootIdJavaPath = $bootIdFile.Replace('\', '/')
    $licenseManagerText = [IO.File]::ReadAllText($licenseManagerCopy).Replace('/proc/sys/kernel/random/boot_id', $bootIdJavaPath)
    [IO.File]::WriteAllText($licenseManagerCopy, $licenseManagerText, [Text.UTF8Encoding]::new($false))

    $javaFiles = Get-ChildItem $tempRoot -Filter '*.java' -Recurse | ForEach-Object FullName
    & $javac -encoding UTF-8 -d $tempRoot $javaFiles
    if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
    & $java "-Dactivation.stub.files=$(Join-Path $tempRoot 'files')" "-Dactivation.stub.boot=$bootIdFile" -cp $tempRoot com.xnas.wpe.suite.ActivationStubHarness
    if ($LASTEXITCODE -ne 0) { throw "ActivationStubHarness failed with exit code $LASTEXITCODE" }

    $buildConfig = Join-Path $tempRoot 'com\xnas\wpe\suite\BuildConfig.java'
    $buildConfigText = [IO.File]::ReadAllText($buildConfig).Replace('PIAOMIAO_LICENSE_BYPASS=false', 'PIAOMIAO_LICENSE_BYPASS=true')
    [IO.File]::WriteAllText($buildConfig, $buildConfigText, [Text.UTF8Encoding]::new($false))
    & $javac -encoding UTF-8 -d $tempRoot $javaFiles
    if ($LASTEXITCODE -ne 0) { throw "javac bypass variant failed with exit code $LASTEXITCODE" }
    & $java "-Dactivation.stub.files=$(Join-Path $tempRoot 'bypass-files')" "-Dactivation.stub.boot=$bootIdFile" -cp $tempRoot com.xnas.wpe.suite.ActivationStubHarness bypass
    if ($LASTEXITCODE -ne 0) { throw "ActivationStubHarness bypass checks failed with exit code $LASTEXITCODE" }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { [IO.Directory]::Delete($tempRoot, $true) }
}
