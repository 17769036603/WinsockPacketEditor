param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\..\.scratch\starhunt-multilevel-20261018\java'),
    [string]$ServiceSourcePath = (Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\starhunt\StarHuntService.java')
)
$ErrorActionPreference = 'Stop'
if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $javac = Join-Path $env:JAVA_HOME 'bin\javac.exe'
    $java = Join-Path $env:JAVA_HOME 'bin\java.exe'
} else {
    $javac = (Get-Command javac -CommandType Application -ErrorAction Stop).Source
    $java = (Get-Command java -CommandType Application -ErrorAction Stop).Source
}
if (-not (Test-Path -LiteralPath $javac -PathType Leaf) -or -not (Test-Path -LiteralPath $java -PathType Leaf)) {
    throw 'Set JAVA_HOME to a JDK or put javac and java on PATH.'
}

$serviceSource = [IO.File]::ReadAllText((Resolve-Path $ServiceSourcePath))
$signature = 'private synchronized void startHangup(StarHuntSelection levels)'
$start = $serviceSource.IndexOf($signature, [StringComparison]::Ordinal)
if ($start -lt 0) { throw 'Could not find the production startHangup method.' }
$brace = $serviceSource.IndexOf('{', $start)
if ($brace -lt 0) { throw 'Could not find startHangup method body.' }
$depth = 0
$end = -1
for ($i = $brace; $i -lt $serviceSource.Length; $i++) {
    if ($serviceSource[$i] -eq '{') { $depth++ }
    elseif ($serviceSource[$i] -eq '}') {
        $depth--
        if ($depth -eq 0) { $end = $i + 1; break }
    }
}
if ($end -lt 0) { throw 'Could not extract the complete startHangup method.' }
$method = $serviceSource.Substring($start, $end - $start)

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$packageDir = Join-Path $outputRoot 'src\com\xnas\wpe\starhunt'
$classesDir = Join-Path $outputRoot 'classes'
$null = New-Item -ItemType Directory -Path $packageDir -Force
$null = New-Item -ItemType Directory -Path $classesDir -Force
    $harness = @'
package com.xnas.wpe.starhunt;
import java.io.File;
import java.io.IOException;

final class StarHuntCommandFile {
    static StarHuntCommand current;
    static int reads;
    static StarHuntCommand read(File ignored) throws IOException { reads++; return current; }
}

public final class StarHuntHangupStartHarness {
    private static int checks;
    private boolean active;
    private boolean actionSlotHeld;
    private boolean stopReadPending;
    private boolean stopReadRetryable;
    private boolean workerRunning;
    private boolean retryableJournal;
    private boolean scannerFails;
    private boolean hangupPending;
    private int latestStartId = 7;
    private int scansStarted;
    private Process worker;
    private StarHuntHangupRequest hangupRequest;
    private String serviceMessage;
    private final File filesDir = new File("stub-files");

    private File getFilesDir() { return filesDir; }
    private boolean hasRetryableStop(File ignored) { return retryableJournal; }
    private boolean running(Process ignored) { return workerRunning; }
    private void setMessage(String value) { serviceMessage = value; }
    private void notifyForeground() { }
    private String safeMessage(Exception error) { return error.getMessage(); }
    private void ensureMonitor() { }
    private void cancelHangupRequest() {
        if (hangupRequest != null) hangupRequest.cancel();
        hangupRequest = null;
        hangupPending = false;
    }
    private void startReading(int startId) {
        scansStarted++;
        cancelHangupRequest();
        if (scannerFails) { setMessage("scanner-test-failure"); return; }
        active = true;
        workerRunning = true;
        long next = StarHuntCommandFile.current == null
                ? 1L : StarHuntCommandFile.current.sequence + 1L;
        StarHuntCommandFile.current = new StarHuntCommand(
                next, StarHuntCommand.Mode.SCAN, 0, 0);
    }
    private StarHuntCommand writeScanCommand() {
        long next = StarHuntCommandFile.current == null
                ? 1L : StarHuntCommandFile.current.sequence + 1L;
        StarHuntCommandFile.current = new StarHuntCommand(
                next, StarHuntCommand.Mode.SCAN, 0, 0);
        return StarHuntCommandFile.current;
    }
    private boolean writeLease() { return true; }
    private void requestReadStop(int startId) { }

    // Production method extracted verbatim by the PowerShell runner.
METHOD_PLACEHOLDER

    private void begin(StarHuntSelection levels) { startHangup(levels); }
    private static void check(boolean condition, String message) {
        checks++;
        if (!condition) throw new AssertionError(message);
    }
    private static StarHuntHangupStartHarness fresh(StarHuntCommand command) {
        StarHuntCommandFile.current = command;
        StarHuntCommandFile.reads = 0;
        return new StarHuntHangupStartHarness();
    }
    public static void main(String[] args) {
        StarHuntHangupStartHarness cold = fresh(
                new StarHuntCommand(40L, StarHuntCommand.Mode.RUN, 4, 310));
        cold.begin(StarHuntSelection.of(10, 8, 9));
        check(cold.scansStarted == 1, "cold hangup must call the reader startup path once");
        check(cold.active && cold.workerRunning, "successful startup must establish active reader");
        check(cold.hangupRequest != null && cold.hangupPending,
                "pending is created only after the reader starts");
        check(cold.hangupRequest.expectedScanSequence == 41L,
                "pending binds to the new scan sequence, not the stale RUN");
        check("8,9,10".equals(cold.hangupRequest.levels.toCsv()),
                "pending keeps the immutable multi-level request snapshot");
        check(StarHuntCommandFile.current.mode == StarHuntCommand.Mode.SCAN,
                "the new command is SCAN even when an old inactive command was RUN");

        StarHuntHangupStartHarness failed = fresh(null);
        failed.scannerFails = true;
        failed.begin(StarHuntSelection.of(4, 8));
        check(failed.scansStarted == 1, "failed cold start still attempts reader startup");
        check(failed.hangupRequest == null && !failed.hangupPending,
                "failed reader startup must not create pending authorization");

        StarHuntHangupStartHarness duplicate = fresh(null);
        duplicate.begin(StarHuntSelection.of(4, 8));
        duplicate.begin(StarHuntSelection.of(4));
        check(duplicate.scansStarted == 1,
                "repeated request while pending cannot restart the scan cycle");
        check(duplicate.hangupRequest.expectedScanSequence == 1L,
                "repeated request leaves the first sequence bound");
        check("4,8".equals(duplicate.hangupRequest.levels.toCsv()),
                "repeated request cannot replace the first selection snapshot");

        StarHuntHangupStartHarness retryBlocked = fresh(null);
        retryBlocked.stopReadRetryable = true;
        retryBlocked.begin(StarHuntSelection.of(4));
        check(retryBlocked.scansStarted == 0 && retryBlocked.hangupRequest == null,
                "retryable stop gate blocks startup and pending creation");
        StarHuntHangupStartHarness stopping = fresh(null);
        stopping.stopReadPending = true;
        stopping.begin(StarHuntSelection.of(4));
        check(stopping.scansStarted == 0 && stopping.hangupRequest == null,
                "pending cleanup blocks startup and pending creation");
        StarHuntHangupStartHarness journalBlocked = fresh(null);
        journalBlocked.retryableJournal = true;
        journalBlocked.begin(StarHuntSelection.of(4));
        check(journalBlocked.scansStarted == 0 && journalBlocked.hangupRequest == null,
                "persisted retryable stop gate blocks startup");
        StarHuntHangupStartHarness actionBlocked = fresh(null);
        actionBlocked.actionSlotHeld = true;
        actionBlocked.begin(StarHuntSelection.of(4));
        check(actionBlocked.scansStarted == 0 && actionBlocked.hangupRequest == null,
                "held action slot blocks startup");
        StarHuntHangupStartHarness emptySelection = fresh(null);
        emptySelection.begin(StarHuntSelection.empty());
        check(emptySelection.scansStarted == 0 && emptySelection.hangupRequest == null
                        && StarHuntCommandFile.reads == 0,
                "empty levels are rejected before command reads or reader startup");

        check(failed.serviceMessage.contains("scanner-test-failure"),
                "entrypoint must preserve the reader's specific startup failure");
        System.out.println("StarHuntHangupStartHarness PASS (" + checks
                + " assertions; extracted production entrypoint)");
    }
}
'@
    $harness = $harness.Replace('METHOD_PLACEHOLDER', $method)
    $sourcePath = Join-Path $packageDir 'StarHuntHangupStartHarness.java'
    [IO.File]::WriteAllText($sourcePath, $harness, [Text.UTF8Encoding]::new($false))
    $statusPath = Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\starhunt\StarHuntStatus.java'
    $commandPath = Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\starhunt\StarHuntCommand.java'
    $requestPath = Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\starhunt\StarHuntHangupRequest.java'
    $selectionPath = Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\starhunt\StarHuntSelection.java'
    & $javac --release 8 -encoding UTF-8 -d $classesDir $sourcePath $statusPath $commandPath $requestPath $selectionPath
    if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
    & $java -cp $classesDir com.xnas.wpe.starhunt.StarHuntHangupStartHarness
    if ($LASTEXITCODE -ne 0) { throw "entrypoint harness failed with exit code $LASTEXITCODE" }
