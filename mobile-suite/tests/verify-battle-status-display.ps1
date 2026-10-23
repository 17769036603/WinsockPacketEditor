param(
    [string]$OutputDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.scratch\battle-status-display')),
    [string]$SkipActivitySource = (Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\battleskip\MainActivity.java'),
    [string]$LoopActivitySource = (Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\battleloop\MainActivity.java'),
    [string]$GateSource = (Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\suite\BattleConflictGate.java'),
    [string]$WorkerStatusSource = (Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\suite\BattleWorkerStatus.java')
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

$skip = [IO.File]::ReadAllText((Resolve-Path $SkipActivitySource))
$loop = [IO.File]::ReadAllText((Resolve-Path $LoopActivitySource))
$skipStatus = Get-ProductionMethod $skip 'private String currentStatus(File dir, String targetKey)' 'battleskip currentStatus'
$skipFormat = Get-ProductionMethod $skip 'private static String formatStatus(String raw, boolean clean)' 'battleskip formatStatus'
$skipCheckedSuffix = Get-ProductionMethod $skip 'private static String checkedSuffix(String raw)' 'battleskip checkedSuffix'
$loopStatus = Get-ProductionMethod $loop 'private String currentStatus(File dir,String targetKey)' 'battleloop currentStatus'
$loopDisplay = Get-ProductionMethod $loop 'private static String display(String s,File dir)' 'battleloop display'
$loopValue = Get-ProductionMethod $loop 'private static String value(String s,String key)' 'battleloop value'

function Make-Public([string]$Method) { return $Method.Replace('private static', 'public static') }
function Make-HarnessStatic([string]$Method) { return $Method.Replace('private String currentStatus', 'public static String currentStatus') }

$out = [IO.Path]::GetFullPath($OutputDirectory)
$classes = Join-Path $out 'classes'
$skipDir = Join-Path $out 'src\com\xnas\wpe\battleskip'
$loopDir = Join-Path $out 'src\com\xnas\wpe\battleloop'
$suiteDir = Join-Path $out 'src\com\xnas\wpe\suite'
$null = New-Item -ItemType Directory -Path $skipDir -Force
$null = New-Item -ItemType Directory -Path $loopDir -Force
$null = New-Item -ItemType Directory -Path $suiteDir -Force
$null = New-Item -ItemType Directory -Path $classes -Force

$skipTemplate = @'
package com.xnas.wpe.battleskip;

import java.io.File;
import java.nio.charset.StandardCharsets;
import com.xnas.wpe.suite.BattleConflictGate;
import com.xnas.wpe.suite.BattleWorkerStatus;
import com.xnas.wpe.suite.UiStatusFiles;

public final class SkipStatusHarness {
    public static final class BattleSkipRecoveryService {
        public static boolean checking;
        public static boolean isChecking() { return checking; }
    }
    // Source-extracted production methods follow.
SKIP_STATUS
SKIP_FORMAT
SKIP_CHECKED_SUFFIX
}
'@

$loopTemplate = @'
package com.xnas.wpe.battleloop;

import java.io.File;
import com.xnas.wpe.suite.UiStatusFiles;

public final class LoopStatusHarness {
    public static final class BattleLoopService {
        public static boolean active;
        public static boolean isActive() { return active; }
    }
    public static String actualStatusForVisuals;
    // Source-extracted production methods follow.
LOOP_STATUS
LOOP_DISPLAY
LOOP_VALUE
}
'@

$mainTemplate = @'
import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

import com.xnas.wpe.battleskip.SkipStatusHarness;
import com.xnas.wpe.battleloop.LoopStatusHarness;

public final class BattleStatusDisplayHarness {
    private static int assertions;
    private static final java.util.List<String> failures = new java.util.ArrayList<String>();
    private static File scratchRoot;

    public static void main(String[] args) throws Exception {
        if (args.length != 1) throw new IllegalArgumentException("expected absolute taskRoot scratch path");
        scratchRoot = new File(args[0]).getAbsoluteFile();
        Files.createDirectories(scratchRoot.toPath());
        runningWorkerShowsRunning();
        unconfirmedPreviousTaskStillShowsUnconfirmed();
        loopPageShowsRunningSkipBlocker();
        loopPageShowsUnconfirmedSkipBlocker();
        loopPageStaysStoppedWithoutSkipEvidence();
        activeLoopPageKeepsOwnStatus();
        if (failures.isEmpty()) {
            System.out.println("BattleStatusDisplayHarness PASS (" + assertions
                    + " assertions; extracted battleskip.currentStatus/formatStatus and battleloop.currentStatus/display)");
        } else {
            System.out.println("BattleStatusDisplayHarness FAIL (" + failures.size() + " failures / " + assertions + " assertions)");
            for (String failure : failures) System.out.println("FAIL: " + failure);
            throw new AssertionError("battle status display regression assertions failed");
        }
    }

    private static void runningWorkerShowsRunning() throws Exception {
        File root = moduleRoot("running");
        File loop = module(root, "battleloop");
        File skip = module(root, "battleskip");
        write(loop, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        write(skip, "status", "running calls=63\n");
        write(skip, "recovery-status", "state=unconfirmed\nreason=worker_running\nrecoveryModule=battleskip\nmessage=工作进程仍持有运行锁，旧任务状态未确认\n");
        SkipStatusHarness.BattleSkipRecoveryService.checking = false;
        eq("state=running\n", SkipStatusHarness.currentStatus(skip, "com.gdoo.dhxy"), "running worker status is normalized, not unconfirmed");
        eq("状态：运行中", SkipStatusHarness.formatStatus(SkipStatusHarness.currentStatus(skip, "com.gdoo.dhxy"), false), "running page text");
    }

    private static void unconfirmedPreviousTaskStillShowsUnconfirmed() throws Exception {
        File root = moduleRoot("unconfirmed");
        File skip = module(root, "battleskip");
        write(skip, "status", "waiting_game count=2\n");
        write(skip, "recovery-status", "state=unconfirmed\nreason=worker_running\nrecoveryModule=battleskip\nmessage=工作进程仍持有运行锁，旧任务状态未确认\n");
        SkipStatusHarness.BattleSkipRecoveryService.checking = false;
        eq("无法确认上次任务是否退出",
                SkipStatusHarness.formatStatus(SkipStatusHarness.currentStatus(skip, "com.gdoo.dhxy"), false),
                "non-running worker keeps the production unconfirmed explanation");
        File bare = moduleRoot("bare-running");
        File bareSkip = module(bare, "battleskip");
        write(bareSkip, "status", "running\n");
        eq("状态：running", SkipStatusHarness.formatStatus(SkipStatusHarness.currentStatus(bareSkip, "com.gdoo.dhxy"), false),
                "bare running without a call count is not treated as running");
    }

    private static void loopPageShowsRunningSkipBlocker() throws Exception {
        File root = moduleRoot("loop-running-skip");
        File loop = module(root, "battleloop");
        File skip = module(root, "battleskip");
        write(loop, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        write(skip, "status", "running calls=63\n");
        LoopStatusHarness.BattleLoopService.active = false;
        String raw = LoopStatusHarness.currentStatus(loop, "com.gdoo.dhxy");
        check(raw.startsWith("state=conflict_wait"), "loop page reports conflict_wait when skip runs: " + raw);
        String shown = LoopStatusHarness.display(raw, loop);
        check(shown.startsWith("状态：战斗快进正在运行"), "loop page names the running skip module: " + shown);
        check(shown.contains("请先到“战斗快进”页面停止快进"), "loop page tells the user to stop skip first: " + shown);
    }

    private static void loopPageShowsUnconfirmedSkipBlocker() throws Exception {
        File root = moduleRoot("loop-unconfirmed-skip");
        File loop = module(root, "battleloop");
        File skip = module(root, "battleskip");
        write(loop, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        write(skip, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=false\n");
        LoopStatusHarness.BattleLoopService.active = false;
        String raw = LoopStatusHarness.currentStatus(loop, "com.gdoo.dhxy");
        check(raw.startsWith("state=conflict_wait"), "loop page reports conflict_wait for unconfirmed skip: " + raw);
        String shown = LoopStatusHarness.display(raw, loop);
        check(shown.contains("上次任务未确认退出"), "loop page keeps the unconfirmed wording: " + shown);
    }

    private static void loopPageStaysStoppedWithoutSkipEvidence() throws Exception {
        File root = moduleRoot("loop-clean");
        File loop = module(root, "battleloop");
        module(root, "battleskip");
        write(loop, "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        LoopStatusHarness.BattleLoopService.active = false;
        eq("状态：已停止", LoopStatusHarness.display(LoopStatusHarness.currentStatus(loop, "com.gdoo.dhxy"), loop),
                "no skip evidence keeps the normal stopped text");
    }

    private static void activeLoopPageKeepsOwnStatus() throws Exception {
        File root = moduleRoot("loop-active");
        File loop = module(root, "battleloop");
        File skip = module(root, "battleskip");
        write(loop, "status", "state=running\n");
        write(skip, "status", "running calls=63\n");
        LoopStatusHarness.BattleLoopService.active = true;
        eq("状态：运行中", LoopStatusHarness.display(LoopStatusHarness.currentStatus(loop, "com.gdoo.dhxy"), loop),
                "active loop page is not overridden by the skip blocker");
        LoopStatusHarness.BattleLoopService.active = false;
    }

    private static File moduleRoot(String name) throws Exception {
        File root = new File(scratchRoot, "battle-status-" + name);
        Files.createDirectories(root.toPath());
        return root;
    }

    private static File module(File root, String id) {
        File directory = new File(root, id);
        directory.mkdirs();
        return directory;
    }

    private static void write(File directory, String name, String content) throws Exception {
        try (FileOutputStream output = new FileOutputStream(new File(directory, name))) {
            output.write(content.getBytes(StandardCharsets.UTF_8));
        }
    }

    private static void check(boolean ok, String message) {
        assertions++;
        if (!ok) failures.add(message);
    }

    private static void eq(Object expected, Object actual, String message) {
        check(expected == null ? actual == null : expected.equals(actual),
                message + " expected=" + expected + " actual=" + actual);
    }
}
'@

$skipSource = $skipTemplate.Replace('SKIP_STATUS', (Make-HarnessStatic $skipStatus)).Replace('SKIP_FORMAT', (Make-Public $skipFormat)).Replace('SKIP_CHECKED_SUFFIX', (Make-Public $skipCheckedSuffix))
$loopSource = $loopTemplate.Replace('LOOP_STATUS', (Make-HarnessStatic $loopStatus)).Replace('LOOP_DISPLAY', (Make-Public $loopDisplay)).Replace('LOOP_VALUE', (Make-Public $loopValue))
$skipSourcePath = Join-Path $skipDir 'SkipStatusHarness.java'
$loopSourcePath = Join-Path $loopDir 'LoopStatusHarness.java'
$mainSourcePath = Join-Path $out 'src\BattleStatusDisplayHarness.java'
$uiStatusFilesPath = Join-Path $suiteDir 'UiStatusFiles.java'
[IO.File]::WriteAllText($skipSourcePath, $skipSource, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($loopSourcePath, $loopSource, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($mainSourcePath, $mainTemplate, [Text.UTF8Encoding]::new($false))
$uiStatusFilesStub = @'
package com.xnas.wpe.suite;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
public final class UiStatusFiles {
    public static final class Snapshot {
        public final boolean known, exists;
        public final String text;
        Snapshot(boolean known, boolean exists, String text) { this.known=known; this.exists=exists; this.text=text; }
    }
    public static Snapshot readForDisplay(String moduleId, File file, String targetKey) {
        try { return file.isFile() ? new Snapshot(true, true, new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8)) : new Snapshot(true, false, ""); }
        catch (Exception ignored) { return new Snapshot(false, false, ""); }
    }
}
'@
[IO.File]::WriteAllText($uiStatusFilesPath, $uiStatusFilesStub, [Text.UTF8Encoding]::new($false))

& $javac --release 8 -encoding UTF-8 -d $classes $skipSourcePath $loopSourcePath $mainSourcePath $GateSource $WorkerStatusSource $uiStatusFilesPath
if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
& $java -cp $classes BattleStatusDisplayHarness (Join-Path $out 'data')
if ($LASTEXITCODE -ne 0) { throw "battle status display harness failed with exit code $LASTEXITCODE" }
