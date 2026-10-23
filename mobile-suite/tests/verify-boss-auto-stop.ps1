$ErrorActionPreference = 'Stop'
$suiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskRoot = (Resolve-Path (Join-Path $suiteRoot '..\..')).Path
$outputRoot = Join-Path $taskRoot 'boss-stop'
$null = New-Item -ItemType Directory -Force -Path $outputRoot
$javac = $null; $java = $null
if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $candidateJavac = Join-Path $env:JAVA_HOME 'bin\javac.exe'
    $candidateJava = Join-Path $env:JAVA_HOME 'bin\java.exe'
    if ((Test-Path -LiteralPath $candidateJavac) -and (Test-Path -LiteralPath $candidateJava)) { $javac=$candidateJavac; $java=$candidateJava }
}
if (-not $javac) { $javac=(Get-Command javac -CommandType Application -ErrorAction Stop).Source }
if (-not $java) { $java=(Get-Command java -CommandType Application -ErrorAction Stop).Source }

function Get-JavaMethod([string]$Source,[string]$Name) {
    $match=[regex]::Match($Source,"(?m)^\s*(?:@Override\s+)?(?:(?:private|public|protected)\s+)?(?:static\s+)?(?:synchronized\s+)?(?:void|boolean|String|Intent)\s+$Name\s*\([^)]*\)\s*\{")
    if(-not $match.Success){throw "Could not find production method $Name"}
    $open=$Source.IndexOf('{',$match.Index);$depth=0;$state='code'
    for($i=$open;$i -lt $Source.Length;$i++){
        $ch=$Source[$i];$next=if($i+1 -lt $Source.Length){$Source[$i+1]}else{[char]0}
        if($state -eq 'line'){if($ch -eq "`n"){$state='code'};continue}
        if($state -eq 'block'){if($ch -eq '*' -and $next -eq '/'){$state='code';$i++};continue}
        if($state -eq 'string' -or $state -eq 'char'){if($ch -eq '\'){$i++;continue};if(($state -eq 'string' -and $ch -eq '"') -or ($state -eq 'char' -and $ch -eq "'")){$state='code'};continue}
        if($ch -eq '/' -and $next -eq '/'){$state='line';$i++;continue};if($ch -eq '/' -and $next -eq '*'){$state='block';$i++;continue}
        if($ch -eq '"'){$state='string';continue};if($ch -eq "'"){$state='char';continue}
        if($ch -eq '{'){$depth++}elseif($ch -eq '}'){$depth--;if($depth -eq 0){return $Source.Substring($match.Index,$i-$match.Index+1)}}
    }
    throw "Unbalanced method body: $Name"
}

$servicePath=Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\bosshunt\BossHuntService.java'
$service=[IO.File]::ReadAllText($servicePath,[Text.Encoding]::UTF8)
$methods=@(
    (Get-JavaMethod $service 'hasRetryableStop'),
    (Get-JavaMethod $service 'hasCompletedStop'),
    (Get-JavaMethod $service 'restoreStopJournal'),
    (Get-JavaMethod $service 'cancelHangupRequest'),
    (Get-JavaMethod $service 'markReadStopUnconfirmed'),
    (Get-JavaMethod $service 'completeReadStop')
) -join "`r`n"
$template=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'boss-stop-offline\BossStopHarness.java.in'),[Text.Encoding]::UTF8)
$harness=$template.Replace('// PRODUCTION_METHODS',$methods)
$temp=Join-Path $outputRoot 'compile'
if(Test-Path -LiteralPath $temp){[IO.Directory]::Delete($temp,$true)}
$null=New-Item -ItemType Directory -Path $temp
$sources=@(
    (Join-Path $PSScriptRoot 'boss-stop-offline\BossStopSupport.java'),
    (Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\bosshunt\BossHuntStopJournal.java'),
    (Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\bosshunt\BossHuntHangupRequest.java')
)
$harnessPath=Join-Path $temp 'BossStopHarness.java'
[IO.File]::WriteAllText($harnessPath,$harness,[Text.UTF8Encoding]::new($false))
& $javac -encoding UTF-8 -d $temp @sources $harnessPath
if($LASTEXITCODE -ne 0){throw "javac failed with exit code $LASTEXITCODE"}
& $java "-Dboss.stop.tempRoot=$outputRoot\runtime" -cp $temp com.xnas.wpe.bosshunt.BossStopHarness
if($LASTEXITCODE -ne 0){throw "BossStopHarness failed with exit code $LASTEXITCODE"}

$moduleSource=[IO.File]::ReadAllText((Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\suite\ModuleActions.java'),[Text.Encoding]::UTF8)
$buildIntent=Get-JavaMethod $moduleSource 'buildIntent'
$intentMethod=Get-JavaMethod $moduleSource 'intent'
$preferenceMethod=Get-JavaMethod $moduleSource 'modulePreference'
$moduleTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'boss-stop-offline\ModuleActionHarness.java.in'),[Text.Encoding]::UTF8)
$moduleHarness=$moduleTemplate.Replace('// MODULE_ACTION_METHODS',($buildIntent+"`r`n"+$intentMethod+"`r`n"+$preferenceMethod))
$moduleTemp=Join-Path $temp 'module-actions'
$null=New-Item -ItemType Directory -Force -Path $moduleTemp
$modulePath=Join-Path $moduleTemp 'ModuleActionHarness.java'
[IO.File]::WriteAllText($modulePath,$moduleHarness,[Text.UTF8Encoding]::new($false))
$stubFiles=@{
  'android\content\Context.java'='package android.content; public class Context { public static final int MODE_PRIVATE=0; public final java.util.Map<String,String> preferences=new java.util.HashMap<>(); public SharedPreferences getSharedPreferences(String name,int mode){ return new SharedPreferences(){ public String getString(String key,String fallback){return preferences.getOrDefault(key,fallback);} public int getInt(String key,int fallback){return fallback;} }; } }'
  'android\content\SharedPreferences.java'='package android.content; public interface SharedPreferences { String getString(String key,String fallback); int getInt(String key,int fallback); }'
  'android\content\Intent.java'='package android.content; public class Intent { private String action; private final java.util.Map<String,Object> extras=new java.util.HashMap<>(); public Intent(Context c,Class<?> cls){} public Intent setAction(String a){action=a;return this;} public Intent putExtra(String k,String v){extras.put(k,v);return this;} public Intent putExtra(String k,int v){extras.put(k,v);return this;} public String getAction(){return action;} public String getStringExtra(String k){Object v=extras.get(k);return v instanceof String?(String)v:null;} }'
  'com\xnas\wpe\suite\TargetGame.java'='package com.xnas.wpe.suite; public final class TargetGame { public static final String MOUNT_HOLD="mounthold",BATTLE_LOOP="battleloop",BATTLE_PANEL="battlepanel",STARHUNT="starhunt",BOSSHUNT="bosshunt",JINCHAN="jinchan",ANLEI="anlei"; public static String getSelectedPackage(android.content.Context c){return "test";} }'
  'com\xnas\wpe\suite\ScheduleTask.java'='package com.xnas.wpe.suite; public final class ScheduleTask { public static final String ACTION_START="start",ACTION_STOP="stop",ACTION_PAUSE="pause",ACTION_RESUME="resume"; public static String normalizeAction(String a){return ACTION_START.equals(a)||ACTION_STOP.equals(a)||ACTION_PAUSE.equals(a)||ACTION_RESUME.equals(a)?a:null;} }'
  'com\xnas\wpe\mounthold\MountHoldService.java'='package com.xnas.wpe.mounthold; public class MountHoldService { public static final String ACTION_STOP="m-stop",ACTION_START="m-start"; }'
  'com\xnas\wpe\battleloop\BattleLoopService.java'='package com.xnas.wpe.battleloop; public class BattleLoopService { public static final String STOP="b-stop",START="b-start",PAUSE="b-pause",RESUME="b-resume"; }'
  'com\xnas\wpe\battlepanel\BattleOverlayService.java'='package com.xnas.wpe.battlepanel; public class BattleOverlayService { public static final String ACTION_STOP="p-stop",ACTION_START="p-start"; }'
  'com\xnas\wpe\starhunt\StarHuntService.java'='package com.xnas.wpe.starhunt; public class StarHuntService { public static final String ACTION_STOP_RUN="s-stop",ACTION_START_HANGUP="s-start",EXTRA_LEVELS="levels",EXTRA_LEVEL="level"; }'
  'com\xnas\wpe\bosshunt\BossHuntService.java'='package com.xnas.wpe.bosshunt; public class BossHuntService { public static final String ACTION_STOP_RUN="com.xnas.wpe.bosshunt.STOP_RUN",ACTION_START_HANGUP="com.xnas.wpe.bosshunt.START_HANGUP",EXTRA_MAPS="maps"; }'
  'com\xnas\wpe\jinchan\ConnectionService.java'='package com.xnas.wpe.jinchan; public class ConnectionService { public static final String ACTION_STOP="j-stop",ACTION_CONNECT="j-connect",EXTRA_PACKAGE="package"; }'
  'com\xnas\wpe\anlei\AnleiService.java'='package com.xnas.wpe.anlei; public class AnleiService { public static final String STOP="a-stop",START="a-start"; }'
}
foreach($entry in $stubFiles.GetEnumerator()){
  $path=Join-Path $moduleTemp $entry.Key; $parent=Split-Path $path -Parent
  $null=New-Item -ItemType Directory -Force -Path $parent
  [IO.File]::WriteAllText($path,$entry.Value,[Text.UTF8Encoding]::new($false))
}
$moduleSources=@(Get-ChildItem -LiteralPath $moduleTemp -Filter '*.java' -Recurse | Where-Object FullName -NotMatch 'ModuleActionHarness.java$' | ForEach-Object FullName)
& $javac -encoding UTF-8 -d $moduleTemp @moduleSources $modulePath
if($LASTEXITCODE -ne 0){throw "ModuleActions javac failed with exit code $LASTEXITCODE"}
& $java -cp $moduleTemp com.xnas.wpe.suite.ModuleActionHarness
if($LASTEXITCODE -ne 0){throw "ModuleActionHarness failed with exit code $LASTEXITCODE"}

$catalogTemp=Join-Path $temp 'catalog-cold-load'
$null=New-Item -ItemType Directory -Force -Path $catalogTemp
$catalogStubs=@{
  'android\content\SharedPreferences.java'='package android.content; public interface SharedPreferences { String getString(String key,String fallback); Editor edit(); interface Editor { Editor putString(String key,String value); void apply(); } }'
  'android\content\Context.java'='package android.content; public class Context { public static final int MODE_PRIVATE=0; private static final java.util.Map<String,Prefs> ALL=new java.util.HashMap<>(); public Context getApplicationContext(){return null;} public SharedPreferences getSharedPreferences(String name,int mode){return ALL.computeIfAbsent(name,k->new Prefs());} private static final class Prefs implements SharedPreferences { final java.util.Map<String,String> values=new java.util.HashMap<>(); public String getString(String k,String d){return values.getOrDefault(k,d);} public Editor edit(){return new Editor(){final java.util.Map<String,String> pending=new java.util.HashMap<>(); public Editor putString(String k,String v){pending.put(k,v);return this;} public void apply(){values.putAll(pending);} };} } }'
}
foreach($entry in $catalogStubs.GetEnumerator()){
  $path=Join-Path $catalogTemp $entry.Key; $parent=Split-Path $path -Parent
  $null=New-Item -ItemType Directory -Force -Path $parent
  [IO.File]::WriteAllText($path,$entry.Value,[Text.UTF8Encoding]::new($false))
}
$catalogHarnessPath=Join-Path $catalogTemp 'BossMapCatalogColdStart.java'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'boss-stop-offline\BossMapCatalogColdStart.java') -Destination $catalogHarnessPath
$catalogSources=@(
  (Join-Path $catalogTemp 'android\content\Context.java'),
  (Join-Path $catalogTemp 'android\content\SharedPreferences.java'),
  (Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\bosshunt\BossHuntMapCatalog.java'),
  (Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\suite\GameMapCatalog.java'),
  (Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\bosshunt\BossHuntSelection.java'),
  $catalogHarnessPath
)
& $javac -encoding UTF-8 -d $catalogTemp @catalogSources
if($LASTEXITCODE -ne 0){throw "Boss map catalog javac failed with exit code $LASTEXITCODE"}
& $java -cp $catalogTemp com.xnas.wpe.bosshunt.BossMapCatalogColdStart
if($LASTEXITCODE -ne 0){throw "BossMapCatalogColdStart failed with exit code $LASTEXITCODE"}

$onCreate=Get-JavaMethod $service 'onCreate'
$superAt=$onCreate.IndexOf('super.onCreate()', [StringComparison]::Ordinal)
$loadAt=$onCreate.IndexOf('BossHuntMapCatalog.load(this)', [StringComparison]::Ordinal)
if($superAt -lt 0 -or $loadAt -le $superAt){throw 'BossHuntService.onCreate must load cached map catalog after super.onCreate'}
Write-Output 'Boss cold-start service wiring: PASS (1 assertion)'
