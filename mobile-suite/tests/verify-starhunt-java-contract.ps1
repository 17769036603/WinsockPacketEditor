$ErrorActionPreference = 'Stop'
$suiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:checks = 0

function Read-Source([string]$relativePath) {
    $path = Join-Path $suiteRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required StarHunt integration file is missing: $relativePath"
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Text([string]$source, [string]$expected, [string]$label) {
    if (-not $source.Contains($expected)) { throw "StarHunt contract check failed: $label" }
    $script:checks++
}

$hostActivity = Read-Source 'app/src/main/java/com/xnas/wpe/suite/MainActivity.java'
$paths = Read-Source 'app/src/main/java/com/xnas/wpe/suite/ModulePaths.java'
$target = Read-Source 'app/src/main/java/com/xnas/wpe/suite/TargetGame.java'
$manifest = Read-Source 'app/src/main/AndroidManifest.xml'
$gradle = Read-Source 'app/build.gradle'
$nativeBuild = Read-Source 'build-native.ps1'
$buildManifest = Read-Source 'build/shared-frida-build-files.json'
$command = Read-Source 'app/src/main/java/com/xnas/wpe/starhunt/StarHuntCommand.java'
$selection = Read-Source 'app/src/main/java/com/xnas/wpe/starhunt/StarHuntSelection.java'
$selectionStore = Read-Source 'app/src/main/java/com/xnas/wpe/starhunt/StarHuntSelectionStore.java'
$service = Read-Source 'app/src/main/java/com/xnas/wpe/starhunt/StarHuntService.java'
$status = Read-Source 'app/src/main/java/com/xnas/wpe/starhunt/StarHuntStatus.java'
$stopJournal = Read-Source 'app/src/main/java/com/xnas/wpe/starhunt/StarHuntStopJournal.java'
$anlei = Read-Source 'app/src/main/java/com/xnas/wpe/anlei/AnleiService.java'
$activity = Read-Source 'app/src/main/java/com/xnas/wpe/starhunt/MainActivity.java'

Require-Text $hostActivity 'new Module(TargetGame.STARHUNT, "自动找星", "自动找星", R.drawable.ic_nav_star, com.xnas.wpe.starhunt.MainActivity.class)' 'host navigation registration'
Require-Text $paths 'case "com.xnas.wpe.starhunt": return "starhunt";' 'isolated module directory mapping'
Require-Text $target 'STARHUNT.equals(moduleId)' 'StarHunt participates in the target gate'
Require-Text $target 'BOSSHUNT.equals(moduleId)' 'BossHunt participates in the target gate'
Require-Text $target 'if (STARHUNT.equals(moduleId) || BOSSHUNT.equals(moduleId)) return DHXY_PACKAGE.equals(packageName);' 'StarHunt and BossHunt are both restricted to DHXY'
Require-Text $manifest 'android:name="com.xnas.wpe.starhunt.MainActivity"' 'module activity declaration'
Require-Text $manifest 'android:name="com.xnas.wpe.starhunt.StarHuntService"' 'foreground service declaration'
Require-Text $nativeBuild "Invoke-CargoBinary 'starhunt' 'native\starhunt\worker-rust\Cargo.toml' 'starhunt_worker' 'libstarhunt_worker.so'" 'native worker build entry'
Require-Text $nativeBuild "'libstarhunt_worker.so'" 'native worker packaging entry'
Require-Text $nativeBuild "'native/starhunt/worker-rust/build.rs'" 'shared Frida consumer build metadata'
Require-Text $buildManifest '"native/starhunt/worker-rust/build.rs"' 'generated shared Frida consumer metadata'
Require-Text $command '"Unknown command field: "' 'unknown command fields fail closed'
Require-Text $command '"levels=" + levels.toCsv()' 'new command serialization always uses a level list'
Require-Text $command 'MAX_SAFE_SEQUENCE = 9_007_199_254_740_991L' 'worker-safe sequence bound remains explicit'
Require-Text $selection 'public static StarHuntSelection restore(boolean hasVersionedValue' 'selection restore model handles migration state'
Require-Text $selectionStore 'public static final String KEY = "selected_levels_v1"' 'versioned selection preference key'
Require-Text $selectionStore 'if (settings.contains(KEY))' 'new selection key presence takes precedence'
Require-Text $gradle "def licenseUrl = providers.gradleProperty('piaomiaoLicenseUrl').getOrElse('')" 'license URL remains configured through the existing authorized Gradle property'
Require-Text $gradle "def licenseBypass = providers.gradleProperty('piaomiaoLicenseBypass').getOrElse('false').toBoolean()" 'license bypass remains disabled by default'
Require-Text $gradle "providers.gradleProperty('piaomiaoVersionCode').getOrElse('16')" 'release version code has an explicit parameter and safe default'
Require-Text $gradle "providers.gradleProperty('piaomiaoVersionName').getOrElse('2026.10.19.1-friends')" 'release version name has an explicit parameter and safe default'
Require-Text $gradle "providers.gradleProperty('piaomiaoReleaseChannel').getOrElse('stable')" 'release channel has an explicit stable default'
Require-Text $gradle 'versionCode piaomiaoVersionCode' 'version code is independent of authorization mode'
Require-Text $gradle 'versionName piaomiaoVersionName' 'version name is independent of authorization mode'
Require-Text $gradle "buildConfigField 'String', 'PIAOMIAO_LICENSE_URL'" 'license URL build configuration remains present'
Require-Text $service 'status.acceptedSequence < previous.sequence' 'run waits for current worker command acknowledgement'
Require-Text $service 'status.confirmsActionStopped(pendingActionStopSequence)' 'action stop waits for scan acknowledgement'
Require-Text $service 'status.confirmsCleanup()' 'read stop requires cleanup evidence'
Require-Text $service 'completeReadStop(terminal, -1);' 'terminal cleanup is reconciled without a worker process handle'
Require-Text $service 'sequence = previous.sequence;' 'workerless stop retry reuses its journaled sequence'
Require-Text $service 'if (status.confirmsCleanupFor(pendingReadStopSequence)) {' 'workerless monitor accepts the journaled cleanup acknowledgement'
Require-Text $service 'restoreStopJournal();' 'persisted stop monitoring is restored'
Require-Text $service 'LOST_WORKER_STOP_TIMEOUT_MS' 'lost worker stop wait is bounded'
Require-Text $service 'markReadStopUnconfirmed' 'unconfirmed stop clears pending state and persists retry'
Require-Text $status 'confirmsGameProcessGoneAfter' 'stale process-gone status cannot release the action slot'
Require-Text $status 'public final String selectedLevels' 'status retains normalized multi-level selection state'
Require-Text $status 'canStartAction(StarHuntSelection selected' 'manual RUN gate checks the complete selection'
Require-Text $stopJournal 'UNCONFIRMED("unconfirmed")' 'stop journal has a retryable unconfirmed phase'
Require-Text $service '"lease"' 'worker lease is written in its module directory'
Require-Text $service '"libstarhunt_worker.so"' 'worker launches from app native library directory'
Require-Text $activity '开始挂机' 'one-click start is visible in the page'
Require-Text $activity 'ACTION_START_HANGUP' 'one-click start uses an explicit service request'
Require-Text $activity 'EXTRA_LEVELS' 'selection snapshot is passed to the service'
Require-Text $service 'private synchronized void startHangup(StarHuntSelection levels)' 'service accepts a complete immutable hangup snapshot'
Require-Text $service 'private synchronized void startHangup(int level)' 'legacy single-level service entry point remains available'
Require-Text $service 'private synchronized boolean startAction(int level, boolean allowEmpty, long expectedScanSequence)' 'legacy single-level RUN method remains available'
Require-Text $service 'if (levels == null || levels.isEmpty())' 'empty selection is rejected before command reads'
Require-Text $service 'new StarHuntHangupRequest(levels, scan.sequence, System.currentTimeMillis())' 'pending request preserves the selected snapshot'
Require-Text $activity 'hasRetryableStop(getFilesDir())' 'retryable stop is visible to the module page'
Require-Text $activity '重试停止' 'stop button exposes an explicit retry'
Require-Text $anlei 'tryAcquireStarHuntAction' 'Anlei action gate entry'
Require-Text $anlei 'beginAnleiAction' 'reverse Anlei action gate entry'
Require-Text $anlei 'starHuntActionHeld' 'shared action gate state'
if ($service.Contains('return START_STICKY')) { throw 'StarHunt must not automatically restart an unconfirmed worker' }

Write-Output "StarHunt Java integration contract PASS ($script:checks checks)"
