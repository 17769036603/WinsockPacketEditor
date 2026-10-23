$ErrorActionPreference = 'Stop'
$suiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$suiteActivityPath = Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\suite\MainActivity.java'
$bossActivityPath = Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\bosshunt\MainActivity.java'
$suite = [IO.File]::ReadAllText($suiteActivityPath, [Text.Encoding]::UTF8)
$boss = [IO.File]::ReadAllText($bossActivityPath, [Text.Encoding]::UTF8)

function Require-Text([string]$Source, [string]$Text, [string]$Name) {
    if (-not $Source.Contains($Text)) { throw "Missing UI merge contract: $Name" }
}

function Get-ProductionMethod([string]$Source, [string]$Signature, [string]$Name) {
    $start = $Source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Could not find production $Name signature." }
    $open = $Source.IndexOf('{', $start)
    if ($open -lt 0) { throw "Could not find production $Name body." }
    $depth = 0
    $state = 'code'
    for ($i = $open; $i -lt $Source.Length; $i++) {
        $ch = $Source[$i]
        $next = if ($i + 1 -lt $Source.Length) { $Source[$i + 1] } else { [char]0 }
        if ($state -eq 'line') { if ($ch -eq "`n") { $state = 'code' }; continue }
        if ($state -eq 'block') { if ($ch -eq '*' -and $next -eq '/') { $state = 'code'; $i++ }; continue }
        if ($state -eq 'string' -or $state -eq 'char') {
            if ($ch -eq '\') { $i++; continue }
            if (($state -eq 'string' -and $ch -eq '"') -or ($state -eq 'char' -and $ch -eq "'")) { $state = 'code' }
            continue
        }
        if ($ch -eq '/' -and $next -eq '/') { $state = 'line'; $i++; continue }
        if ($ch -eq '/' -and $next -eq '*') { $state = 'block'; $i++; continue }
        if ($ch -eq '"') { $state = 'string'; continue }
        if ($ch -eq "'") { $state = 'char'; continue }
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -eq 0) { return $Source.Substring($start, $i - $start + 1) }
        }
    }
    throw "Unbalanced production method: $Name"
}

if ($suite.Contains('<<<<<<<') -or $suite.Contains('>>>>>>>') -or $boss.Contains('<<<<<<<') -or $boss.Contains('>>>>>>>')) {
    throw 'A merged activity still contains conflict markers.'
}

$diagnostics = Get-ProductionMethod $suite 'private String diagnosticsText()' 'diagnosticsText'
$copyDiagnostics = Get-ProductionMethod $suite 'private void copyDiagnostics()' 'copyDiagnostics'
$refreshDiagnostics = Get-ProductionMethod $suite 'private void refreshDiagnosticsView()' 'refreshDiagnosticsView'
$refreshHost = Get-ProductionMethod $suite 'private void refreshSelectedStatus()' 'refreshSelectedStatus'
$refreshSnapshots = Get-ProductionMethod $suite 'private void refreshModuleStatusSnapshots()' 'refreshModuleStatusSnapshots'
$navigation = Get-ProductionMethod $suite 'private void renderNavigation()' 'renderNavigation'

Require-Text $diagnostics 'BuildConfig.VERSION_NAME' 'full version name in diagnostics'
Require-Text $diagnostics 'BuildConfig.VERSION_CODE' 'build number in diagnostics'
Require-Text $diagnostics 'Collections.singletonList("x86_64")' 'supported ABI in diagnostics'
Require-Text $diagnostics 'statusSnapshots.diagnosticStates(selectedTargetPackage' 'fixed module status allowlist in diagnostics'
Require-Text $diagnostics 'ScheduleStore.diagnosticSummary(this)' 'scheduler diagnostic summary contract'
Require-Text $diagnostics 'DiagnosticsFormatter.format(diagnostics)' 'sanitized diagnostics formatter'
Require-Text $copyDiagnostics 'ClipData.newPlainText' 'copyable diagnostic payload'
Require-Text $copyDiagnostics 'clipboard.setPrimaryClip' 'clipboard write'
Require-Text $refreshDiagnostics 'content.equals(lastDiagnosticsContent)' 'unchanged diagnostics are not redrawn'
Require-Text $refreshHost 'SchedulePage.refresh(schedulePageView, this)' 'scheduler refresh contract'
Require-Text $refreshHost 'refreshModuleStatusSnapshots()' 'background display snapshots refresh'
Require-Text $refreshSnapshots 'statusSnapshots.publish(module.id, targetKey, state, observedAt)' 'target-bound snapshot publish'
foreach ($token in @(
        'if (!stateText.equals(item.lastStateText))',
        'if (item.lastLabelColor != labelColor)',
        'if (item.lastIconColor != iconColor)',
        'if (item.lastBackgroundSelected != backgroundSelected)',
        'if (item.lastStateColor != stateColor)',
        'if (item.lastStateVisible != stateVisibleValue)',
        'if (item.lastAlpha != alpha)',
        'lastSettingsSelected !=',
        'lastScheduleSelected !=')) {
    Require-Text $navigation $token "sidebar diff rendering: $token"
}
Require-Text $suite 'if (SETTINGS_ID.equals(selectedModuleId)) showSettings();' 'settings refresh after activity return'
Require-Text $suite 'infoRow(page, "版本名", BuildConfig.VERSION_NAME)' 'full version name in settings'
Require-Text $suite 'infoRow(page, "版本码", Integer.toString(BuildConfig.VERSION_CODE))' 'build number in settings'

$bossBuild = Get-ProductionMethod $boss 'private View buildContent()' 'BOSS buildContent'
$bossRefresh = Get-ProductionMethod $boss 'private void refreshPage()' 'BOSS refreshPage'
$bossPicker = Get-ProductionMethod $boss 'private void showMapPicker()' 'BOSS showMapPicker'
$bossToggle = Get-ProductionMethod $boss 'private void toggleHunt()' 'BOSS toggleHunt'
$bossStatus = Get-ProductionMethod $boss '@Override public ModuleStatus getModuleStatus()' 'BOSS getModuleStatus'
$bossRead = Get-ProductionMethod $boss 'private UiStatusFiles.Snapshot readStatusSnapshot(String targetKey)' 'BOSS readStatusSnapshot'
$bossParse = Get-ProductionMethod $boss 'private static BossHuntStatus parseStatus(UiStatusFiles.Snapshot snapshot)' 'BOSS parseStatus'
$bossDisplay = Get-ProductionMethod $boss 'private String displayState(boolean pending, boolean active, boolean stopping, boolean retryable)' 'BOSS displayState'

Require-Text $boss 'BossHuntMapCatalog.load(this)' 'load worker-exported dynamic map catalog'
Require-Text $bossBuild 'setOnClickListener(view -> showMapPicker())' 'dynamic map picker entry'
Require-Text $bossPicker 'for (BossHuntMapCatalog.MapInfo map : BossHuntMapCatalog.MAPS)' 'all exported maps in picker'
Require-Text $bossPicker 'BossHuntSelection.from(picked)' 'persist selected dynamic maps'
Require-Text $boss 'BossHuntService.ACTION_START_HANGUP' 'explicit start action'
Require-Text $boss 'BossHuntService.ACTION_STOP_READ' 'stop and retry recovery action'
Require-Text $boss 'BossHuntService.hasRetryableStop(getFilesDir())' 'retryable stop recovery guard'
Require-Text $bossBuild 'actionButton.setOnClickListener(view -> toggleHunt())' 'explicit stop button click binding'
Require-Text $bossRefresh 'actionButton.setText(running ? "停止巡回" : "开始巡回")' 'stop UI label'
Require-Text $bossToggle 'if (running)' 'stop only when service reports a live or retryable run'
Require-Text $bossToggle 'ACTION_START_HANGUP' 'start is explicit through the action button'
Require-Text $boss 'case "paused": return "已暂停"' 'paused status hint'
Require-Text $bossRead 'UiStatusFiles.readForDisplay("bosshunt", file, targetKey)' 'BOSS status cache is display-only'
Require-Text $bossRefresh 'boolean running = pending || active || stopping || retryable;' 'button state comes from live service state'
Require-Text $boss 'stopRecoveryRequested = false;' 'retry entry resets when the page resumes'
if ($bossStatus.Contains('dispatch(') -or $bossStatus.Contains('ACTION_START') -or $bossStatus.Contains('ACTION_STOP')) {
    throw 'BOSS status observation must not dispatch start, stop, or recovery actions.'
}
$pendingAt = $bossStatus.IndexOf('BossHuntService.isStopReadPending()', [StringComparison]::Ordinal)
$retryAt = $bossStatus.IndexOf('BossHuntService.hasRetryableStop(getFilesDir())', [StringComparison]::Ordinal)
$cacheAt = $bossStatus.IndexOf('readStatusSnapshot(target)', [StringComparison]::Ordinal)
$unknownAt = $bossStatus.IndexOf('if (!statusSnapshot.known', [StringComparison]::Ordinal)
if ($pendingAt -lt 0 -or $retryAt -lt $pendingAt -or $cacheAt -lt $retryAt -or $unknownAt -lt $cacheAt) {
    throw 'Live BOSS stop state must be checked before an unknown display-cache snapshot.'
}
$retryGuardAt = $bossRefresh.IndexOf('if (!stopRecoveryRequested && BossHuntService.hasRetryableStop(getFilesDir()))', [StringComparison]::Ordinal)
$retryActionAt = $bossRefresh.IndexOf('BossHuntService.ACTION_STOP_READ', [StringComparison]::Ordinal)
$pageCacheAt = $bossRefresh.IndexOf('readStatusSnapshot()', [StringComparison]::Ordinal)
if ($retryGuardAt -lt 0 -or $retryActionAt -lt $retryGuardAt -or $pageCacheAt -lt $retryActionAt) {
    throw 'Retry recovery must remain guarded by live service state before display-cache reads.'
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("ui-merge-" + [Guid]::NewGuid().ToString('N'))
$classes = Join-Path $tempRoot 'classes'
$suiteStubs = Join-Path $tempRoot 'src\com\xnas\wpe\suite'
$bossStubs = Join-Path $tempRoot 'src\com\xnas\wpe\bosshunt'
New-Item -ItemType Directory -Path $classes, $suiteStubs, $bossStubs -Force | Out-Null

try {
    $statusHarness = @'
package com.xnas.wpe.bosshunt;
import java.io.File;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.TargetGame;
import com.xnas.wpe.suite.UiStatusFiles;

public final class BossActivityStatusHarness implements com.xnas.wpe.suite.ModuleStatusProvider {
    private final File filesDir;
    private BossHuntStatus status = BossHuntStatus.parse("");
    private boolean startRequestPending;
    public BossActivityStatusHarness(File filesDir) { this.filesDir = filesDir; }
    public File getFilesDir() { return filesDir; }

// BOSS_STATUS_METHOD
// BOSS_READ_METHOD
// BOSS_PARSE_METHOD
// BOSS_DISPLAY_METHOD

    public static void main(String[] args) {
        stopReadPendingWinsUnknownCache();
        retryableStopWinsUnknownCache();
        liveWorkerWinsUnknownCache();
        cachedStoppingTextCannotCreateLiveStopState();
        System.out.println("BossActivityStatusHarness PASS (4 cases; extracted production getModuleStatus/readStatusSnapshot/parseStatus/displayState)");
    }

    private static void reset() {
        UiStatusFiles.readCalls = 0;
        UiStatusFiles.next = UiStatusFiles.Snapshot.unknown(TargetGame.TARGET);
        BossHuntService.stopReadPending = false;
        BossHuntService.retryableStop = false;
        BossHuntService.hangupPending = false;
        BossHuntService.active = false;
        BossHuntService.completedStop = false;
        BossHuntService.actionRequests = 0;
    }

    private static void stopReadPendingWinsUnknownCache() {
        reset();
        BossHuntService.stopReadPending = true;
        ModuleStatus result = new BossActivityStatusHarness(new File("case-stop-pending")).getModuleStatus();
        equal(ModuleStatus.State.STOPPING, result.state, "live stop-read state remains visible while cache is unknown");
        equal(0, UiStatusFiles.readCalls, "pending stop does not wait for a cache read");
        equal(0, BossHuntService.actionRequests, "status observation does not dispatch an action");
    }

    private static void retryableStopWinsUnknownCache() {
        reset();
        BossHuntService.retryableStop = true;
        ModuleStatus result = new BossActivityStatusHarness(new File("case-retryable-stop")).getModuleStatus();
        equal(ModuleStatus.State.STOPPING, result.state, "retryable stop remains visible while cache is unknown");
        equal(0, UiStatusFiles.readCalls, "retryable stop does not wait for a cache read");
        equal(0, BossHuntService.actionRequests, "status observation leaves retry action to its explicit entry");
    }

    private static void liveWorkerWinsUnknownCache() {
        reset();
        BossHuntService.active = true;
        ModuleStatus result = new BossActivityStatusHarness(new File("case-live-worker")).getModuleStatus();
        equal(ModuleStatus.State.RUNNING, result.state, "live worker stays visible while display cache is unknown");
        equal(1, UiStatusFiles.readCalls, "active status still requests display data through the cache");
        equal(0, BossHuntService.actionRequests, "status observation does not start or stop a worker");
    }

    private static void cachedStoppingTextCannotCreateLiveStopState() {
        reset();
        UiStatusFiles.next = UiStatusFiles.Snapshot.present(TargetGame.TARGET, "status=stopping\n");
        ModuleStatus result = new BossActivityStatusHarness(new File("case-cached-stopping")).getModuleStatus();
        equal(ModuleStatus.State.STOPPED, result.state, "cached status text cannot override a stopped live service");
        equal(0, BossHuntService.actionRequests, "cached status text cannot dispatch stop or recovery");
    }

    private static void equal(Object expected, Object actual, String message) {
        if (expected == null ? actual != null : !expected.equals(actual)) {
            throw new AssertionError(message + ": expected=" + expected + " actual=" + actual);
        }
    }
}
'@

    $harness = $statusHarness.Replace('// BOSS_STATUS_METHOD', $bossStatus)
    $harness = $harness.Replace('// BOSS_READ_METHOD', $bossRead)
    $harness = $harness.Replace('// BOSS_PARSE_METHOD', $bossParse)
    $harness = $harness.Replace('// BOSS_DISPLAY_METHOD', $bossDisplay)

    $stubs = @{
        'com\xnas\wpe\suite\TargetGame.java' = @'
package com.xnas.wpe.suite;
public final class TargetGame {
    public static final String TARGET = "com.gdoo.dhxy";
    private TargetGame() { }
    public static String getSelectedPackage(Object ignored) { return TARGET; }
}
'@
        'com\xnas\wpe\suite\UiStatusFiles.java' = @'
package com.xnas.wpe.suite;
import java.io.File;
public final class UiStatusFiles {
    public static int readCalls;
    public static Snapshot next;
    private UiStatusFiles() { }
    public static Snapshot readForDisplay(String moduleId, File file, String targetKey) {
        readCalls++;
        return next == null ? Snapshot.unknown(targetKey) : next;
    }
    public static final class Snapshot {
        public final boolean known, exists;
        public final String targetKey, text;
        private Snapshot(boolean known, boolean exists, String targetKey, String text) {
            this.known = known; this.exists = exists; this.targetKey = targetKey; this.text = text;
        }
        public static Snapshot unknown(String targetKey) { return new Snapshot(false, false, targetKey, ""); }
        public static Snapshot present(String targetKey, String text) { return new Snapshot(true, true, targetKey, text); }
    }
}
'@
        'com\xnas\wpe\bosshunt\BossHuntService.java' = @'
package com.xnas.wpe.bosshunt;
import java.io.File;
public final class BossHuntService {
    public static boolean stopReadPending, retryableStop, hangupPending, active, completedStop;
    public static int actionRequests;
    private BossHuntService() { }
    public static boolean isStopReadPending() { return stopReadPending; }
    public static boolean hasRetryableStop(File ignored) { return retryableStop; }
    public static boolean isHangupPending() { return hangupPending; }
    public static boolean isActive() { return active; }
    public static boolean hasCompletedStop(File ignored) { return completedStop; }
    public static String getServiceMessage() { return "test service message"; }
}
'@
        'com\xnas\wpe\bosshunt\BossHuntStatus.java' = @'
package com.xnas.wpe.bosshunt;
public final class BossHuntStatus {
    public final String state, reason;
    private BossHuntStatus(String state, String reason) { this.state = state; this.reason = reason; }
    public static BossHuntStatus parse(String text) {
        String state = "unknown", reason = "";
        if (text != null) for (String line : text.split("\\r?\\n")) {
            if (line.startsWith("status=")) state = line.substring(7);
            if (line.startsWith("reason=")) reason = line.substring(7);
        }
        return new BossHuntStatus(state, reason);
    }
    public boolean isFresh(long now, long maxAgeSeconds) { return false; }
}
'@
    }

    foreach ($entry in $stubs.GetEnumerator()) {
        $path = Join-Path (Join-Path $tempRoot 'src') $entry.Key
        $parent = Split-Path -Parent $path
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        [IO.File]::WriteAllText($path, $entry.Value, [Text.UTF8Encoding]::new($false))
    }
    $harnessPath = Join-Path $bossStubs 'BossActivityStatusHarness.java'
    [IO.File]::WriteAllText($harnessPath, $harness, [Text.UTF8Encoding]::new($false))

    $javac = $null
    $java = $null
    if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
        $candidateJavac = Join-Path $env:JAVA_HOME 'bin\javac.exe'
        $candidateJava = Join-Path $env:JAVA_HOME 'bin\java.exe'
        if ((Test-Path -LiteralPath $candidateJavac) -and (Test-Path -LiteralPath $candidateJava)) {
            $javac = $candidateJavac; $java = $candidateJava
        }
    }
    if (-not $javac -or -not $java) {
        $javacCommand = Get-Command javac -CommandType Application -ErrorAction SilentlyContinue
        $javaCommand = Get-Command java -CommandType Application -ErrorAction SilentlyContinue
        if ($javacCommand -and $javaCommand) { $javac = $javacCommand.Source; $java = $javaCommand.Source }
    }
    if (-not $javac -or -not $java) {
        $knownJdkRoot = Join-Path $env:LOCALAPPDATA 'Programs\Microsoft'
        $knownJavac = Get-ChildItem -LiteralPath $knownJdkRoot -Filter javac.exe -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($knownJavac) {
            $knownJava = Join-Path $knownJavac.DirectoryName 'java.exe'
            if (Test-Path -LiteralPath $knownJava) { $javac = $knownJavac.FullName; $java = $knownJava }
        }
    }
    if (-not $javac -or -not $java) { throw 'A JDK javac/java pair is required for the extracted production-method harness.' }

    $sources = @(
        (Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\suite\ModuleStatus.java'),
        (Join-Path $suiteRoot 'app\src\main\java\com\xnas\wpe\suite\ModuleStatusProvider.java'),
        (Join-Path $suiteStubs 'TargetGame.java'),
        (Join-Path $suiteStubs 'UiStatusFiles.java'),
        (Join-Path $bossStubs 'BossHuntService.java'),
        (Join-Path $bossStubs 'BossHuntStatus.java'),
        $harnessPath
    )
    & $javac --release 8 -encoding UTF-8 -d $classes @sources
    if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
    & $java -cp $classes com.xnas.wpe.bosshunt.BossActivityStatusHarness
    if ($LASTEXITCODE -ne 0) { throw "BossActivityStatusHarness failed with exit code $LASTEXITCODE" }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}

Write-Output 'UI merge source contracts PASS (diagnostics, schedule contract, sidebar diff rendering, dynamic BOSS maps and explicit stop UI)'
