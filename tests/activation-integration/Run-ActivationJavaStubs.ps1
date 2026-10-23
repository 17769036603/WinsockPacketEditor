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
        'com\xnas\wpe\battleloop', 'com\xnas\wpe\battlepanel', 'com\xnas\wpe\jinchan'
    )
    foreach ($folder in $folders) { $null = New-Item -ItemType Directory -Path (Join-Path $tempRoot $folder) }

    $sources = @{
        'android\content\Context.java' = @'
package android.content;
import java.io.File;
public class Context {
  public Context getApplicationContext(){return this;}
  public SharedPreferences getSharedPreferences(String n,int m){return MockPrefs.INSTANCE;}
  public File getFilesDir(){return new File(System.getProperty("activation.stub.files"));}
  public Object getMainLooper(){return this;}
  public static int starts;
  public void startService(Intent i){starts++;}
  public void startForegroundService(Intent i){starts++;}
  public static final int MODE_PRIVATE=0;
}
'@
        'android\content\SharedPreferences.java' = @'
package android.content;
public interface SharedPreferences {
  String getString(String k,String d); boolean contains(String k); Editor edit();
  interface Editor { Editor putString(String k,String v); Editor remove(String k); boolean commit(); }
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
  }
}
'@
        'android\content\Intent.java' = 'package android.content; public class Intent { public Intent(Context c,Class<?> k){} public Intent setAction(String a){return this;} }'
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
  public JSONObject(String s){java.util.regex.Matcher x=java.util.regex.Pattern.compile("\\\"code\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"").matcher(s);if(x.find())m.put("code",x.group(1));}
  public JSONObject put(String k,Object v){m.put(k,v);return this;}
  public String getString(String k){return String.valueOf(m.get(k));}
  public String optString(String k,String d){Object v=m.get(k);return v==null?d:String.valueOf(v);}
  public long getLong(String k){return ((Number)m.get(k)).longValue();}
  public String toString(){return "{}";}
}
'@
        'com\xnas\wpe\suite\BuildConfig.java' = 'package com.xnas.wpe.suite; public class BuildConfig { public static String PIAOMIAO_LICENSE_URL=""; }'
        'com\xnas\wpe\anlei\AnleiService.java' = 'package com.xnas.wpe.anlei; public class AnleiService {}'
        'com\xnas\wpe\mounthold\MountHoldService.java' = 'package com.xnas.wpe.mounthold; public class MountHoldService { public static final String ACTION_STOP="stop"; }'
        'com\xnas\wpe\battleloop\BattleLoopService.java' = 'package com.xnas.wpe.battleloop; public class BattleLoopService { public static final String STOP="stop"; }'
        'com\xnas\wpe\battlepanel\BattleOverlayService.java' = 'package com.xnas.wpe.battlepanel; public class BattleOverlayService { public static final String ACTION_STOP="stop"; }'
        'com\xnas\wpe\jinchan\ConnectionService.java' = 'package com.xnas.wpe.jinchan; public class ConnectionService {}'
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
  static final class HttpsHandler extends URLStreamHandler {
    protected URLConnection openConnection(URL u){return new HttpURLConnection(u){
      public void disconnect(){} public boolean usingProxy(){return false;} public void connect(){}
      public OutputStream getOutputStream() throws IOException {if(response.get()==0)throw new IOException("simulated offline");return new ByteArrayOutputStream();}
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
  static void activateRejected(int status,String jsonErrorBody,String expectedMessage)throws Exception {
    seedAuthorization(); Context.starts=0; response.set(status); CountDownLatch done=new CountDownLatch(1);
    errorBody=jsonErrorBody; AtomicReference<String> message=new AtomicReference<>("");
    LicenseManager.activate(context,"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",(m,ok)->{message.set(m);done.countDown();});
    if(!done.await(4,TimeUnit.SECONDS))throw new AssertionError("activate "+status+" timed out");
    Thread.sleep(30);
    if(!LicenseManager.hasSession(context)||!lease().isFile()||Context.starts!=0)
      throw new AssertionError("activate "+status+" must preserve session/lease and avoid STOP");
    if(expectedMessage!=null&&!message.get().contains(expectedMessage))throw new AssertionError("activate "+status+" missing message text: "+message.get());
    System.out.println("activate HTTP "+status+" preserves existing authorization: PASS ("+message.get()+")");
  }
  static void verifyRejected(int status,String body,boolean preserveSession,String label)throws Exception {
    seedAuthorization(); Context.starts=0;
    verify(status,body,preserveSession,false,5,label);
  }
  static void setLeaseActive(boolean active)throws Exception {
    java.lang.reflect.Field deadline=LicenseManager.class.getDeclaredField("deadlineElapsed"); deadline.setAccessible(true);
    deadline.setLong(null,android.os.SystemClock.elapsedRealtime()+(active?60000:-1000));
    java.lang.reflect.Field boot=LicenseManager.class.getDeclaredField("bootId"); boot.setAccessible(true);
    boot.set(null,java.nio.file.Files.readString(java.nio.file.Path.of(System.getProperty("activation.stub.boot"))).trim());
  }
  static void verifyAppScenario(int status,boolean active,boolean expectedLease,int expectedStops,String label)throws Exception {
    seedAuthorization(); setLeaseActive(active); Context.starts=0; response.set(status);
    SuiteApplication app=new SuiteApplication(); app.onCreate();
    var field=SuiteApplication.class.getDeclaredField("supervisor"); field.setAccessible(true);
    ((java.util.concurrent.ScheduledExecutorService)field.get(app)).shutdownNow();
    java.lang.reflect.Field request=LicenseManager.class.getDeclaredField("requestActive"); request.setAccessible(true);
    java.util.concurrent.atomic.AtomicBoolean busy=(java.util.concurrent.atomic.AtomicBoolean)request.get(null);
    long end=System.nanoTime()+TimeUnit.SECONDS.toNanos(4);
    while(busy.get()&&System.nanoTime()<end) Thread.sleep(10);
    if(busy.get())throw new AssertionError(label+" request timed out");
    Thread.sleep(50);
    check(Context.starts==expectedStops,label+" expected STOP requests="+expectedStops+" got="+Context.starts);
    check(lease().isFile()==expectedLease,label+" expected lease="+expectedLease+" got="+lease().isFile());
    check(LicenseManager.hasSession(context)==(status!=401),label+(status==401?" must clear rejected session":" must preserve session for recovery"));
    System.out.println(label+(regressions.isEmpty()?": PASS":": executed"));
  }
  static void check(boolean ok,String failure){if(!ok)regressions.add(failure);}
  static void repeatedExpireDispatchesOnce()throws Exception {
    seedAuthorization(); Context.starts=0; LicenseManager.expire(context); LicenseManager.expire(context);
    if(Context.starts!=5)throw new AssertionError("repeated expire expected one five-service STOP wave, got "+Context.starts);
    System.out.println("repeated expire dispatches exactly one STOP wave: PASS");
  }
  public static void main(String[] args)throws Exception {
    URL.setURLStreamHandlerFactory(p->p.equals("https")?new HttpsHandler():null);
    SuiteApplication app=new SuiteApplication(); app.onCreate();
    var field=SuiteApplication.class.getDeclaredField("supervisor"); field.setAccessible(true);
    ((java.util.concurrent.ScheduledExecutorService)field.get(app)).shutdownNow();
    if(Context.starts!=0)throw new AssertionError("unconfigured cold start requested "+Context.starts+" service stops");
    System.out.println("unconfigured, unactivated cold start starts 0 services: PASS");

    BuildConfig.PIAOMIAO_LICENSE_URL="https://license.test";
    activateRejected(401,"{}",null); activateRejected(403,"{}",null);
    activateRejected(409,"{\"error\":\"激活码已绑定到其他安装\"}","已绑定其他安装");
    verifyRejected(403,"{\"code\":\"LICENSE_EXPIRED\"}",true,"verify LICENSE_EXPIRED retains session, clears lease, and requests STOP");
    verifyRejected(403,"{}",false,"verify HTTP 403 without code clears authorization and requests STOP");
    verifyRejected(403,"{\"code\":\"UNKNOWN\"}",false,"verify HTTP 403 with unknown code clears authorization and requests STOP");
    verifyRejected(401,"{}",false,"verify HTTP 401 clears authorization and requests STOP");
    seedAuthorization(); Context.starts=0;
    verify(500,"{}",true,true,0,"HTTP 500 verify retains session and lease");
    seedAuthorization(); Context.starts=0;
    verify(0,"{}",true,true,0,"connection failure retains session and lease");
    repeatedExpireDispatchesOnce();
    verifyAppScenario(0,true,true,0,"SuiteApplication network failure with active lease sends no STOP");
    verifyAppScenario(0,false,false,5,"SuiteApplication network failure with expired lease clears lease, preserves session, and sends one STOP wave");
    verifyAppScenario(401,false,false,5,"SuiteApplication HTTP 401 invalidation and callback send one STOP wave");
    if(regressions.isEmpty()) { System.out.println("all activation integration checks passed"); System.exit(0); }
    for(String failure:regressions)System.err.println("FAIL: "+failure);
    System.exit(1);
  }
}
'@
    }

    foreach ($entry in $sources.GetEnumerator()) {
        $target = Join-Path $tempRoot $entry.Key
        [IO.File]::WriteAllText($target, $entry.Value, [Text.UTF8Encoding]::new($false))
    }
    Copy-Item (Join-Path $appSource 'SuiteApplication.java') (Join-Path $tempRoot 'com\xnas\wpe\suite\SuiteApplication.java')
    Copy-Item (Join-Path $appSource 'LicenseManager.java') (Join-Path $tempRoot 'com\xnas\wpe\suite\LicenseManager.java')
    Copy-Item (Join-Path $appSource 'LicenseLeaseWindow.java') (Join-Path $tempRoot 'com\xnas\wpe\suite\LicenseLeaseWindow.java')
    Copy-Item (Join-Path $appSource 'InstallationHint.java') (Join-Path $tempRoot 'com\xnas\wpe\suite\InstallationHint.java')
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
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { [IO.Directory]::Delete($tempRoot, $true) }
}
