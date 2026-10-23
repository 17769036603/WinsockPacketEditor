param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\.scratch\teleport-contract'))
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $out
$rustc = Join-Path $env:USERPROFILE '.cargo\bin\rustc.exe'
if (-not (Test-Path -LiteralPath $rustc -PathType Leaf)) { $rustc = (Get-Command rustc -CommandType Application -ErrorAction Stop).Source }
$rustParts = @()
foreach ($module in @('bosshunt','starhunt')) {
    $title = if ($module -eq 'bosshunt') { 'BossHunt' } else { 'StarHunt' }
    $source = [IO.File]::ReadAllText((Join-Path $root "native/$module/worker-rust/src/main.rs"))
    $start = $source.IndexOf("struct ${title}Command {")
    $end = $source.IndexOf('fn game_module_dir(', $start)
    if ($start -lt 0 -or $end -lt 0) { throw "Native command extraction failed: $module" }
    $rustParts += $source.Substring($start, $end-$start)
}
$rustParts += @'
fn main() {
    assert!(parse_bosshunt_command("sequence=11\nmode=teleport\nmaps=2000\nmap=2000\n").is_some());
    assert!(parse_bosshunt_command("sequence=11\nmode=teleport\nmaps=1010,2000\nmap=2000\n").is_none());
    assert!(parse_bosshunt_command("sequence=11\nmode=teleport\nmaps=1010\nmap=2000\n").is_none());
    assert!(parse_bosshunt_command("sequence=11\nmode=run\nmaps=2000\nmap=1010\n").is_some());
    assert!(parse_bosshunt_command("sequence=11\nmode=teleport\nmaps=02000\nmap=2000\n").is_none());
    assert!(parse_bosshunt_command("sequence=11\nmode=teleport\nmaps=2000\nmap=0\n").is_none());
    assert!(parse_starhunt_command("sequence=11\nmode=teleport\nlevels=\nmap=2000\n").is_some());
    assert!(parse_starhunt_command("sequence=11\nmode=teleport\nlevels=8\nmap=2000\n").is_none());
    assert!(parse_starhunt_command("sequence=11\nmode=teleport\nlevels=\nmap=0\n").is_none());
    assert!(parse_starhunt_command("sequence=11\nmode=run\nlevels=8\nmap=1010\n").is_some());
    println!("Native extracted teleport parsers: PASS (10 assertions)");
}
'@
$rustFile = Join-Path $out 'parsers.rs'
[IO.File]::WriteAllText($rustFile, ($rustParts -join "`n"), [Text.UTF8Encoding]::new($false))
$rustExe = Join-Path $out 'parsers.exe'
& $rustc --edition=2021 $rustFile -o $rustExe
if ($LASTEXITCODE -ne 0) { throw 'Native parser compilation failed.' }
& $rustExe
if ($LASTEXITCODE -ne 0) { throw 'Native parser behavior failed.' }

$gateMethods = @()
foreach ($module in @('bosshunt','starhunt')) {
    $title = if ($module -eq 'bosshunt') { 'BossHunt' } else { 'StarHunt' }
    $source = [IO.File]::ReadAllText((Join-Path $root "app/src/main/java/com/xnas/wpe/$module/${title}Service.java"))
    $start = $source.IndexOf('    private synchronized void startTeleport(')
    $end = $source.IndexOf('    private synchronized void startReading(', $start)
    if ($start -lt 0 -or $end -lt 0) { throw "Service gate extraction failed: $module" }
    $method = $source.Substring($start,$end-$start).Replace('com.xnas.wpe.suite.GameMapCatalog','StubCatalog').Replace('BossHuntMapCatalog','StubCatalog')
    $gateMethods += "static class $title extends Base { $method }"
}
$gateSource = @'
import java.io.File;
public class TeleportGateHarness {
 static int assertions;
 static class StubCatalog { static boolean contains(int id) { return id==2000; } }
 static class AnleiService {
  static boolean permit=true; static int attempts;
  static boolean tryAcquireBossHuntAction(){ attempts++;return permit; }
  static boolean tryAcquireStarHuntAction(){ attempts++;return permit; }
 }
 static class Base {
  boolean active,actionSlotHeld,stopReadPending,stopReadRetryable,readAccept=true;
  Object worker; int pendingTeleportMap,started;
  boolean running(Object p){return p!=null;}
  boolean hasRetryableStop(File f){return false;}
  File getFilesDir(){return null;}
  void notifyForeground(){} void setMessage(String s){}
  void releaseActionSlot(){actionSlotHeld=false;}
  void startReading(int id){started++;if(readAccept)pendingTeleportMap=0;}
 }
 // METHODS
 static void check(boolean ok){assertions++;if(!ok)throw new AssertionError("gate case "+assertions);}
 static void run(boolean boss) {
  Base s=boss?new BossHunt():new StarHunt();
  AnleiService.permit=true;AnleiService.attempts=0;
  call(s,boss,999);check(s.started==0&&AnleiService.attempts==0);
  s.active=true;call(s,boss,2000);check(s.started==0&&AnleiService.attempts==0);s.active=false;
  s.stopReadRetryable=true;call(s,boss,2000);check(s.started==0);s.stopReadRetryable=false;
  AnleiService.permit=false;call(s,boss,2000);check(s.started==0&&!s.actionSlotHeld);
  AnleiService.permit=true;call(s,boss,2000);check(s.started==1&&s.actionSlotHeld&&s.pendingTeleportMap==0);
  s.actionSlotHeld=false;s.readAccept=false;call(s,boss,2000);
  check(s.started==2&&!s.actionSlotHeld&&s.pendingTeleportMap==0);
 }
 static void call(Base s,boolean boss,int map){if(boss)((BossHunt)s).startTeleport(map,1);else((StarHunt)s).startTeleport(map,1);}
 public static void main(String[] args){run(true);run(false);System.out.println("Extracted Java teleport gates: PASS ("+assertions+" assertions)");}
}
'@
$gateSource = $gateSource.Replace('// METHODS', ($gateMethods -join "`n"))
$javaFile = Join-Path $out 'TeleportGateHarness.java'
[IO.File]::WriteAllText($javaFile,$gateSource,[Text.UTF8Encoding]::new($false))
$javac = $null; $java = $null
if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $candidateJavac = Join-Path $env:JAVA_HOME 'bin/javac.exe'
    $candidateJava = Join-Path $env:JAVA_HOME 'bin/java.exe'
    if ((Test-Path -LiteralPath $candidateJavac -PathType Leaf) -and (Test-Path -LiteralPath $candidateJava -PathType Leaf)) {
        $javac=$candidateJavac; $java=$candidateJava
    }
}
if (-not $javac -or -not $java) {
    $javac=(Get-Command javac -CommandType Application -ErrorAction Stop).Source
    $java=(Get-Command java -CommandType Application -ErrorAction Stop).Source
}
& $javac --release 8 -encoding UTF-8 -d $out $javaFile
if ($LASTEXITCODE -ne 0) { throw 'Java service gate compilation failed.' }
& $java -cp $out TeleportGateHarness
if ($LASTEXITCODE -ne 0) { throw 'Java service gate behavior failed.' }

foreach ($module in @('bosshunt','starhunt')) {
    $source = [IO.File]::ReadAllText((Join-Path $root "app/src/main/assets/$module.lua.in"))
    $prefixEnd = $source.IndexOf('local function sameCommand(')
    $stepStart = $source.IndexOf('local function mapStartPositionAny(')
    $stepEnd = $source.IndexOf('local function tick(', $stepStart)
    if ($prefixEnd -lt 0 -or $stepStart -lt 0 -or $stepEnd -lt 0) { throw "Lua extraction failed: $module" }
    $prefix = $source.Substring(0, $prefixEnd)
    $steps = $source.Substring($stepStart, $stepEnd-$stepStart)
    if ($module -eq 'bosshunt') {
        $selectionStart=$source.IndexOf('local function applySelection(')
        $selectionEnd=$source.IndexOf('local function acceptCommand(', $selectionStart)
        $steps=$source.Substring($selectionStart,$selectionEnd-$selectionStart)+"`n"+$steps
    }
    $stub = @'
local stopped,leased,other,battle=true,true,false,false
local sends=0
local function writeStatus(s,state,reason) s.state=state;s.reason=reason end
local function isStopRequested() return stopped end
local function leaseAlive() return leased,'lease_unconfirmed' end
local function anleiActionActive() return other end
local function writeBinding() return true end
local function readProperty(o,k) return o[k] end
local function finish(s,reason) s.finished=true;s.reason=reason end
local function enterUnknown(s,reason) s.unknown=true;s.reason=reason end
JudgeIsInWar=function() return battle end
C2S_JumpMap=22568
netconn={m_ConnectState=1,send=function(self,protocol,map,x,y,flag)
 assert(protocol==22568 and map==2000 and x==3 and y==5 and flag==0)
 sends=sends+1
end}
data_MapInfo={[2000]={startPos={3,5}}}
local function state()
 local s={elapsed=0,calls=0,teleports=0,teleportTarget=2000,role='unknown',manager='unknown'}
 rawset(_G,key,s)
 return s
end
local context={role='role-a',map=1010,loading=false,manager={canNotOperationTeam=false}}
'@
    $selection = if ($module -eq 'bosshunt') { 'maps=2000' } else { 'levels=' }
    $tests = @'
local s=state()
teleportStep(s,context);assert(sends==0 and s.state=='stopping')
stopped=false;leased=false;s=state()
teleportStep(s,context);assert(sends==0 and s.state=='stopping')
leased=true;other=true;s=state()
teleportStep(s,context);assert(sends==0 and s.finished and s.reason=='other_movement_active')
other=false;battle=true;s=state()
teleportStep(s,context);assert(sends==0)
battle=false;context.manager.canNotOperationTeam=true;s=state()
teleportStep(s,context);assert(sends==0)
context.manager.canNotOperationTeam=false;s=state();s.lockedRole='another-role'
teleportStep(s,context);assert(sends==0 and s.finished and s.reason=='role_changed')
s=state();teleportStep(s,context);assert(sends==1 and not s.finished)
teleportStep(s,context);assert(sends==1 and s.state=='teleport_wait')
context.map=2000;teleportStep(s,context);assert(sends==1 and s.finished and s.reason=='teleported')
context.map=1010;s=state();teleportStep(s,context);s.elapsed=20
teleportStep(s,context);assert(s.finished and s.reason=='teleport_timeout')
data_MapInfo[2000].startPos={3.5,5};s=state();local before=sends
teleportStep(s,context);assert(sends==before and s.finished and s.reason=='map_start_position_unavailable')
print('Extracted teleport Lua guards/one-shot/confirmation: PASS (11 scenarios)')
'@
    $parserTest = "assert(parseCommand('sequence=11\nmode=teleport\n$selection\nmap=2000\n'))`n"
    if ($module -eq 'bosshunt') {
        $parserTest+="local selected={};applySelection(selected,{1002,2000});assert(#selected.mapOrder==2 and selected.mapOrder[1]==1002 and selected.mapOrder[2]==2000)`n"
    }
    $luaFile = Join-Path $out "$module.lua"
    [IO.File]::WriteAllText($luaFile, ($prefix + "`n" + $stub + "`n" + $steps + "`n" + $parserTest + $tests), [Text.UTF8Encoding]::new($false))
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/battle-loop-sandbox/run-lua.ps1') -LuaFile $luaFile -ConfigLiteral "{stateKey='__teleport_offline_test'}"
    if ($LASTEXITCODE -ne 0) { throw "Extracted Lua teleport behavior failed: $module" }
}
