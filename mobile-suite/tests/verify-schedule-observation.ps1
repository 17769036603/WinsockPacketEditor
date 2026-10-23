param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\.scratch\schedule-observation'))
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
$null=New-Item -ItemType Directory -Force -Path $out
$stubRoot=Join-Path $PSScriptRoot 'schedule-merge-stubs'
$context=[IO.File]::ReadAllText((Join-Path $stubRoot 'android/content/Context.java'))
$context=[regex]::Replace($context,'}\s*$',' public java.io.File getFilesDir(){return new java.io.File(System.getProperty("observation.files"));} }')
$target=[IO.File]::ReadAllText((Join-Path $stubRoot 'com/xnas/wpe/suite/TargetGame.java'))
$target=[regex]::Replace($target,'}\s*$',' public static String getSelectedPackage(android.content.Context c){return "com.gdoo.dhxy";} public static boolean supportsModule(String m,String p){return true;} public static boolean isKnownPackage(String p){return "com.gdoo.dhxy".equals(p);} }')
$actions=[IO.File]::ReadAllText((Join-Path $root 'app/src/main/java/com/xnas/wpe/suite/ModuleActions.java'))
$prefix=$actions.Substring(0,$actions.IndexOf('    public static final class DispatchResult'))
$prefix=[regex]::Replace($prefix,'(?m)^import android\.[^;]+;\s*','')
$a=$actions.IndexOf('    public static String[] targetIds(');$b=$actions.IndexOf('    static Intent buildIntent(',$a)
$prefix+=$actions.Substring($a,$b-$a)+"`n}`n"
$files=@{'Context.java'=$context;'TargetGame.java'=$target;'ModuleActions.java'=$prefix}
foreach($name in $files.Keys){[IO.File]::WriteAllText((Join-Path $out $name),$files[$name],[Text.UTF8Encoding]::new($false))}
$harness=@'
package com.xnas.wpe.suite;
import android.content.Context;
import java.io.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
public class ScheduleObservationHarness {
 static int checks;
 static void check(boolean ok,String message){checks++;if(!ok)throw new AssertionError(message);}
 static void write(File file,String data)throws Exception{file.getParentFile().mkdirs();Files.write(file.toPath(),data.getBytes(StandardCharsets.UTF_8));}
 static ScheduleExecution execution(String module,String action){
  ScheduleExecution e=ScheduleExecution.create(11,action,100500,new String[]{module},10000);
  e.setBaseline(module,"",10,"");e.recordDispatch(module,true,ScheduleExecution.ReasonCode.NONE,100500);return e;
 }
 public static void main(String[] args)throws Exception {
  Context c=new Context();
  for(String module:new String[]{TargetGame.STARHUNT,TargetGame.BOSSHUNT}){
   File dir=new File(c.getFilesDir(),"modules/"+module);
   ScheduleExecution e=execution(module,"start");
   write(new File(dir,"command"),"sequence=11\nmode=run\n");
   write(new File(dir,"status"),"status=waiting_targets\nacceptedSequence=11\nupdatedAt=100\n");
   check(ModuleObservationReader.observe(c,e.target(module),100800).result==ScheduleExecution.ObservationResult.CONFIRMED,"lowercase run confirms "+module);
   write(new File(dir,"command"),"sequence=10\nmode=run\n");
   check(ModuleObservationReader.observe(c,e.target(module),100800).result==ScheduleExecution.ObservationResult.WAIT,"old sequence cannot confirm "+module);
   e=execution(module,"stop");write(new File(dir,"command"),"sequence=12\nmode=scan\n");
   write(new File(dir,"status"),"status=reading\nacceptedSequence=12\nupdatedAt=100\n");
   check(ModuleObservationReader.observe(c,e.target(module),100800).result==ScheduleExecution.ObservationResult.CONFIRMED,"lowercase scan confirms "+module);
   write(new File(dir,"status"),"status=reading\nacceptedSequence=12\nupdatedAt=80\n");
   check(ModuleObservationReader.observe(c,e.target(module),100800).result==ScheduleExecution.ObservationResult.WAIT,"stale result cannot confirm "+module);
  }
  ScheduleExecution old=execution(TargetGame.BATTLE_LOOP,"pause");ScheduleExecutionStore.save(c,old);
  ScheduleExecution loaded=ScheduleExecutionStore.latest(c,11);
  check(loaded!=null&&loaded.target(TargetGame.BATTLE_LOOP)!=null,"legacy target persists in execution store");
  check(!ScheduleExecutionStore.taskSummary(c,11).contains("无效目标"),"legacy target has truthful summary");
  System.out.println("Production schedule observation/persistence: PASS ("+checks+" assertions)");
 }
}
'@
$harnessPath=Join-Path $out 'ScheduleObservationHarness.java'
[IO.File]::WriteAllText($harnessPath,$harness,[Text.UTF8Encoding]::new($false))
$sources=@(Get-ChildItem -LiteralPath $out -File -Filter '*.java' | ForEach-Object FullName)
$sources+=@(Get-ChildItem -LiteralPath (Join-Path $stubRoot 'org/json') -File -Filter '*.java' | ForEach-Object FullName)
$sources+=Join-Path $stubRoot 'android/content/SharedPreferences.java'
foreach($rel in @('suite/ScheduleTask','suite/ScheduleExecution','suite/ScheduleExecutionStore','suite/ModuleObservationReader','suite/GameMapCatalog','bosshunt/BossHuntMapCatalog','bosshunt/BossHuntStatus','bosshunt/BossHuntSelection','starhunt/StarHuntStatus','starhunt/StarHuntSelection')){
 $sources+=Join-Path $root "app/src/main/java/com/xnas/wpe/$rel.java"
}
$javac=$null;$java=$null
if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
 $candidateJavac=Join-Path $env:JAVA_HOME 'bin/javac.exe';$candidateJava=Join-Path $env:JAVA_HOME 'bin/java.exe'
 if((Test-Path -LiteralPath $candidateJavac -PathType Leaf) -and (Test-Path -LiteralPath $candidateJava -PathType Leaf)){$javac=$candidateJavac;$java=$candidateJava}
}
if(-not $javac -or -not $java){$javac=(Get-Command javac -CommandType Application -ErrorAction Stop).Source;$java=(Get-Command java -CommandType Application -ErrorAction Stop).Source}
& $javac --release 8 -encoding UTF-8 -d $out @sources
if($LASTEXITCODE -ne 0){throw 'Schedule observation compile failed.'}
& $java "-Dobservation.files=$(Join-Path $out 'files')" -cp $out com.xnas.wpe.suite.ScheduleObservationHarness
if($LASTEXITCODE -ne 0){throw 'Schedule observation behavior failed.'}
