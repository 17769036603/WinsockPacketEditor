$ErrorActionPreference = 'Stop'
# 行为级回归：把 BattleConflictGate / BattleWorkerStatus 编译成纯 Java 并在临时目录上跑。
# 覆盖：清理证据、锁空闲可启动（startable）及其时效、仍在运行必须拒绝、互斥放行。
$suite = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..'))
$source = Join-Path $suite 'app\src\main\java\com\xnas\wpe\suite'
$javac = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\javac.exe' } else { 'javac' }
$java = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\java.exe' } else { 'java' }
if (-not (Test-Path $javac)) { throw "javac not found: $javac" }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('battle-gate-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $temp 'com\xnas\wpe\suite'
$classes = Join-Path $temp 'classes'
New-Item -ItemType Directory -Force -Path $package, $classes | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'BattleConflictGate.java') -Destination $package
Copy-Item -LiteralPath (Join-Path $source 'BattleWorkerStatus.java') -Destination $package

$probe = @'
package com.xnas.wpe.suite;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.FileTime;
import java.time.Instant;

public final class GateProbe {
    private static Path root;

    private static Path freshRoot() throws Exception {
        root = Files.createTempDirectory("gate-probe");
        return root;
    }

    private static void write(String module, String name, String text) throws Exception {
        Path dir = root.resolve(module);
        Files.createDirectories(dir);
        Files.write(dir.resolve(name), text.getBytes(StandardCharsets.UTF_8));
    }

    private static void backdate(String module, String name, long seconds) throws Exception {
        Path file = root.resolve(module).resolve(name);
        Files.setLastModifiedTime(file, FileTime.from(Instant.now().minusSeconds(seconds)));
    }

    private static long now() { return Instant.now().getEpochSecond(); }

    private static void startableVerdict(String module, long checkedAt) throws Exception {
        write(module, "recovery-status",
                "state=startable\nreason=no_prior_record\nlockProbe=free\ncheckedAt=" + checkedAt + "\n");
    }

    private static void check(boolean condition, String label) {
        if (!condition) throw new AssertionError(label);
        System.out.println("PASS " + label);
    }

    public static void main(String[] args) throws Exception {
        File modules = freshRoot().toFile();
        write("battleskip", "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\n");
        check(BattleConflictGate.isClean(modules, "battleskip"), "confirmed stop is clean");
        check(BattleConflictGate.settled(modules, "battleskip"), "confirmed stop is settled");

        modules = freshRoot().toFile();
        write("battleskip", "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nreason=activation_lease_invalid\n");
        backdate("battleskip", "status", 60);
        startableVerdict("battleskip", now());
        check(BattleConflictGate.isStartable(modules, "battleskip"), "fresh lock-free verdict is startable");
        check(BattleConflictGate.settled(modules, "battleskip"), "fresh lock-free verdict is settled");

        modules = freshRoot().toFile();
        write("battleskip", "status", "state=stopped\n");
        write("battleskip", "recovery-status", "state=startable\nreason=no_prior_record\ncheckedAt=" + now() + "\n");
        check(!BattleConflictGate.isStartable(modules, "battleskip"), "verdict without lockProbe=free is rejected");

        modules = freshRoot().toFile();
        write("battleskip", "status", "state=stopped\n");
        backdate("battleskip", "status", 60);
        startableVerdict("battleskip", now());
        check(BattleConflictGate.isStartable(modules, "battleskip"), "verdict newer than the status file is trusted");

        modules = freshRoot().toFile();
        write("battleskip", "status", "state=stopped\n");
        startableVerdict("battleskip", now() - 120);
        check(!BattleConflictGate.isStartable(modules, "battleskip"), "status newer than the verdict invalidates it");

        modules = freshRoot().toFile();
        write("battleskip", "status", "running calls=3\n");
        backdate("battleskip", "status", 60);
        startableVerdict("battleskip", now());
        check(!BattleConflictGate.isStartable(modules, "battleskip"), "live worker status beats a fresh verdict");

        modules = freshRoot().toFile();
        write("battleskip", "status", "state=starting\n");
        backdate("battleskip", "status", 60);
        startableVerdict("battleskip", now());
        check(BattleConflictGate.isStartable(modules, "battleskip"), "interrupted start becomes startable after a lock-free probe");

        modules = freshRoot().toFile();
        write("battleskip", "status", "state=stopping\n");
        startableVerdict("battleskip", now() - 60);
        check(!BattleConflictGate.isStartable(modules, "battleskip"), "status written after the probe blocks the verdict");

        modules = freshRoot().toFile();
        write("battleskip", "status", "state=stopped\n");
        write("battleskip", "recovery-status", "state=unconfirmed\nreason=worker_running\nlockProbe=busy\n");
        check(!BattleConflictGate.isStartable(modules, "battleskip"), "unconfirmed verdict stays blocked");
        check(BattleConflictGate.hasUnconfirmedSkipActivity(modules), "unconfirmed verdict still reports unconfirmed activity");
        check(!BattleConflictGate.reserve(modules, "battleloop").allowed, "unconfirmed skip still blocks the loop start");

        modules = freshRoot().toFile();
        write("battleskip", "status", "state=stopped\n");
        backdate("battleskip", "status", 60);
        startableVerdict("battleskip", now());
        check(!BattleConflictGate.hasUnconfirmedSkipActivity(modules), "startable verdict is not unconfirmed activity");
        check(BattleConflictGate.reserve(modules, "battleloop").allowed, "startable skip no longer blocks the loop start");

        modules = freshRoot().toFile();
        write("battleloop", "status", "running calls=7\n");
        BattleConflictGate.Decision liveLoop = BattleConflictGate.reserve(modules, "battleskip");
        check(!liveLoop.allowed, "live loop still blocks the skip start");
        check(liveLoop.message.contains("正在运行或清理"), "live blocker names the running module: " + liveLoop.message);

        modules = freshRoot().toFile();
        write("battleskip", "status", "state=starting\n");
        BattleConflictGate.Decision ownStart = BattleConflictGate.reserve(modules, "battleskip");
        check(!ownStart.allowed, "interrupted own start without a verdict blocks a second start");
        check(ownStart.message.contains("战斗快进"), "own interrupted start keeps its module wording: " + ownStart.message);

        modules = freshRoot().toFile();
        write("battleloop", "status", "state=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n");
        check(BattleConflictGate.settled(modules, "battleloop"), "loop cleanup evidence is settled");
        check(BattleConflictGate.reserve(modules, "battleskip").allowed, "clean loop allows the skip start");

        System.out.println("BattleGateStartableContract PASS");
    }
}
'@
[IO.File]::WriteAllText((Join-Path $package 'GateProbe.java'), $probe, [Text.UTF8Encoding]::new($false))

& $javac -encoding UTF-8 -d $classes (Join-Path $package 'BattleConflictGate.java') `
    (Join-Path $package 'BattleWorkerStatus.java') (Join-Path $package 'GateProbe.java')
if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
& $java -cp $classes com.xnas.wpe.suite.GateProbe
if ($LASTEXITCODE -ne 0) { throw "GateProbe failed with exit code $LASTEXITCODE" }
Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
