param([string]$OutputDirectory=(Join-Path $PSScriptRoot '..\.scratch\cycle-confirmation'))
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
$null=New-Item -ItemType Directory -Force -Path $out
$source=[IO.File]::ReadAllText((Join-Path $root 'app/src/main/java/com/xnas/wpe/starhunt/StarHuntService.java'))
$a=$source.IndexOf('    private synchronized void maintainMapCycle(')
$b=$source.IndexOf('    private void startNextMap(', $a)
if($a -lt 0 -or $b -lt 0 -or -not $source.Contains('rotateSequence = command.sequence;')){throw 'Cycle confirmation wiring missing.'}
$method=$source.Substring($a,$b-$a)
$harness=@'
public class CycleConfirmationHarness {
 boolean active=true,actionSlotHeld=true,stopReadPending,stopReadRetryable,hangupPending,teleportSession,rotating=true;
 int rotateTarget=2000,cycleCursor,writes,nexts;long rotateSequence=42,rotateStartedAtMillis=System.currentTimeMillis(),waitingSinceMillis;
 static final long CYCLE_ARRIVE_MILLIS=10000,CYCLE_IDLE_MILLIS=5000;
 int[] cycleMaps(){return new int[]{1010,2000};}
 void writeRunForMap(int map){writes++;}
 void startNextMap(int[] maps,long now){nexts++;}
 static class StarHuntStatus{int map=2000;long acceptedSequence=41,updatedAt=System.currentTimeMillis()/1000;String state="stopped",reason="teleported";}
 // METHOD
 static int assertions;
 static void check(boolean ok){assertions++;if(!ok)throw new AssertionError("cycle case "+assertions);}
 public static void main(String[] args){
  CycleConfirmationHarness h=new CycleConfirmationHarness();StarHuntStatus s=new StarHuntStatus();
  h.maintainMapCycle(s);check(h.rotating&&h.writes==0);
  s.acceptedSequence=42;s.updatedAt-=10;h.maintainMapCycle(s);check(h.rotating&&h.writes==0);
  s.updatedAt=System.currentTimeMillis()/1000;s.state="teleport_wait";h.maintainMapCycle(s);check(h.rotating&&h.writes==0);
  s.state="stopped";s.reason="teleport_timeout";h.maintainMapCycle(s);check(h.rotating&&h.writes==0);
  s.reason="teleported";h.maintainMapCycle(s);check(!h.rotating&&h.writes==1);
  h.actionSlotHeld=false;s.state="waiting_targets";h.waitingSinceMillis=System.currentTimeMillis()-6000;
  h.maintainMapCycle(s);check(h.nexts==0);
  System.out.println("Extracted star cycle confirmation: PASS ("+assertions+" assertions)");
 }
}
'@
$file=Join-Path $out 'CycleConfirmationHarness.java'
[IO.File]::WriteAllText($file,$harness.Replace('// METHOD',$method),[Text.UTF8Encoding]::new($false))
$javac=$null;$java=$null
if(-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)){
 $candidateJavac=Join-Path $env:JAVA_HOME 'bin/javac.exe';$candidateJava=Join-Path $env:JAVA_HOME 'bin/java.exe'
 if((Test-Path -LiteralPath $candidateJavac -PathType Leaf) -and (Test-Path -LiteralPath $candidateJava -PathType Leaf)){$javac=$candidateJavac;$java=$candidateJava}
}
if(-not $javac -or -not $java){$javac=(Get-Command javac -CommandType Application -ErrorAction Stop).Source;$java=(Get-Command java -CommandType Application -ErrorAction Stop).Source}
& $javac --release 8 -encoding UTF-8 -d $out $file
if($LASTEXITCODE -ne 0){throw 'Cycle confirmation compile failed.'}
& $java -cp $out CycleConfirmationHarness
if($LASTEXITCODE -ne 0){throw 'Cycle confirmation behavior failed.'}
