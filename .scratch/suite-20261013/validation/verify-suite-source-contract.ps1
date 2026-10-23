$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$suite = Join-Path $repo 'mobile-suite'
$copies = Join-Path $PSScriptRoot 'source-copies'
if (Test-Path -LiteralPath $copies) { Remove-Item -LiteralPath $copies -Recurse -Force }
New-Item -ItemType Directory -Path $copies -Force | Out-Null
$assertions = 0

function Assert-True([bool]$Condition, [string]$Message) {
  $script:assertions++
  if (-not $Condition) { throw "FAIL: $Message" }
}
function Copy-Source([string]$RelativePath) {
  $source = Join-Path $suite $RelativePath
  $destination = Join-Path $copies $RelativePath
  $parent = Split-Path -Parent $destination
  New-Item -ItemType Directory -Path $parent -Force | Out-Null
  Copy-Item -LiteralPath $source -Destination $destination
  return [System.IO.File]::ReadAllText($destination, [System.Text.Encoding]::UTF8)
}

$manifestText = Copy-Source 'app\src\main\AndroidManifest.xml'
$manifest = [xml]$manifestText
$launcherCount = 0
$launcherNames = @()
foreach ($activity in $manifest.SelectNodes("//*[local-name()='activity']")) {
  $main = $activity.SelectNodes(".//*[local-name()='action']") | Where-Object { $_.GetAttribute('name', 'http://schemas.android.com/apk/res/android') -eq 'android.intent.action.MAIN' }
  $launch = $activity.SelectNodes(".//*[local-name()='category']") | Where-Object { $_.GetAttribute('name', 'http://schemas.android.com/apk/res/android') -eq 'android.intent.category.LAUNCHER' }
  if ($main -and $launch) {
    $launcherCount++
    $launcherNames += $activity.GetAttribute('name', 'http://schemas.android.com/apk/res/android')
  }
}
Assert-True ($launcherCount -eq 1 -and $launcherNames[0] -eq 'com.xnas.wpe.suite.MainActivity') 'manifest declares exactly one MAIN/LAUNCHER activity, the suite shell'

$ids = @('anlei','mounthold','battleloop','battleskip','jinchan','battlepanel')
$activityDecls = $manifest.SelectNodes("//*[local-name()='activity']")
foreach ($id in $ids) {
  $activityName = "com.xnas.wpe.$id.MainActivity"
  $declaration = $activityDecls | Where-Object { $_.GetAttribute('name', 'http://schemas.android.com/apk/res/android') -eq $activityName }
  Assert-True ($null -ne $declaration -and $declaration.GetAttribute('exported', 'http://schemas.android.com/apk/res/android') -eq 'false') "$id child activity is registered as non-exported"
}

$suiteMain = Copy-Source 'app\src\main\java\com\xnas\wpe\suite\MainActivity.java'
$modulePaths = Copy-Source 'app\src\main\java\com\xnas\wpe\suite\ModulePaths.java'
$moduleActivity = Copy-Source 'app\src\main\java\com\xnas\wpe\suite\ModuleActivity.java'
$moduleService = Copy-Source 'app\src\main\java\com\xnas\wpe\suite\ModuleService.java'
foreach ($id in $ids) {
  Assert-True ($suiteMain.Contains("com.xnas.wpe.$id.MainActivity.class")) "$id is registered in the suite navigation"
  Assert-True ($modulePaths.Contains("case `"com.xnas.wpe.$id`": return `"$id`";")) "$id has a package-to-storage mapping"
  $child = Copy-Source "app\src\main\java\com\xnas\wpe\$id\MainActivity.java"
  Assert-True ($child -match 'extends\s+(com\.xnas\.wpe\.suite\.)?ModuleActivity') "$id activity uses isolated module context"
}
foreach ($base in @($moduleActivity,$moduleService)) {
  foreach ($method in @('getFilesDir','getCacheDir','getCodeCacheDir','getSharedPreferences')) {
    Assert-True ($base.Contains("$method(")) "module base adapts $method"
  }
}

Assert-True ($suiteMain.Contains('extends ActivityGroup') -and $suiteMain.Contains('super(true);')) 'shell uses a single-active-child ActivityGroup'
Assert-True ($suiteMain.Contains('getLocalActivityManager().startActivity(module.id, childIntent)')) 'child activity is started through LocalActivityManager'
Assert-True ($suiteMain.Contains('window.getDecorView()') -and $suiteMain.Contains('moduleContainer.addView(page')) 'child window is transferred into the suite container'
Assert-True ($suiteMain.Contains('onSaveInstanceState') -and $suiteMain.Contains('STATE_SELECTED_MODULE')) 'selected module is saved across recreation'
Assert-True ($suiteMain.Contains('getStringExtra(EXTRA_OPEN_MODULE_ID)') -and $suiteMain.Contains('onNewIntent')) 'selected module can be restored from an explicit suite intent'
Assert-True (-not $suiteMain.Contains('getLaunchIntentForPackage') -and -not $suiteMain.Contains('ProcessBuilder') -and -not $suiteMain.Contains('"su"')) 'suite shell itself does not launch the game, worker, or Root command'

$activityGroup = Copy-Source 'app\src\main\java\com\xnas\wpe\suite\MainActivity.java'
$permissionFindings = @()
foreach ($id in @('mounthold','battlepanel')) {
  $child = [System.IO.File]::ReadAllText((Join-Path $copies "app\src\main\java\com\xnas\wpe\$id\MainActivity.java"), [System.Text.Encoding]::UTF8)
  Assert-True ($child.Contains('requestModulePermissions(') -and $child.Contains('onRequestPermissionsResult(')) "$id child requests through the host and handles its permission result"
  $permissionFindings += $id
}
Assert-True ($activityGroup.Contains('requestPermissionsFromModule(') -and $activityGroup.Contains('getCurrentActivity() != child')) 'suite permission bridge accepts requests only from the active child'
Assert-True ($activityGroup.Contains('pendingPermissionModuleId') -and $activityGroup.Contains('pendingPermissionRequestCode')) 'suite stores the requesting module and permission request code'
Assert-True ($activityGroup.Contains('onRequestPermissionsResult(') -and $activityGroup.Contains('getActivity(moduleId)')) 'suite routes the host permission result back to the requesting child'
Assert-True ($activityGroup.Contains('STATE_PERMISSION_MODULE') -and $activityGroup.Contains('STATE_PERMISSION_CODE')) 'pending permission routing survives activity recreation'

$gradle = Copy-Source 'app\build.gradle'
Assert-True ($gradle -match 'minSdk\s+23' -and $gradle -match 'targetSdk\s+35') 'Gradle declares minSdk 23 and targetSdk 35'
Assert-True ($gradle.Contains("abiFilters 'x86_64'")) 'Gradle restricts native ABI to x86_64'
$nativeBuild = Copy-Source 'build-native.ps1'
$nativeOutputs = @('libanlei_worker.so','libmount_hold_worker.so','libbattle_loop_worker.so','libbattle_skip_worker.so','libjinchan_worker.so','libenemy_reader.so','libresistance_query.so')
foreach ($name in $nativeOutputs) { Assert-True ($nativeBuild.Contains($name)) "native packaging script declares $name" }
Assert-True ($nativeBuild.Contains("Invoke-CargoLibrary 'battleskip-launcher'") -and $nativeBuild.Contains("'battle_skip_launcher'")) 'native packaging script declares the battle skip launcher shared library'

$modules = @('anlei','battleloop','battleskip','jinchan')
$rustCopies = @{}
foreach ($id in $modules) {
  $relative = "native\$id\worker-rust\src\main.rs"
  $text = Copy-Source $relative
  $rustCopies[$id] = $text
  $functionName = if ($id -eq 'jinchan') { 'fn valid_files_dir(p: &Path)' } else { 'fn valid_dir(p: &Path)' }
  $start = $text.IndexOf($functionName, [System.StringComparison]::Ordinal)
  Assert-True ($start -ge 0) "$id native worker has a dedicated exact module path validator"
  $nextFunction = $text.IndexOf("`nfn ", $start + $functionName.Length, [System.StringComparison]::Ordinal)
  if ($nextFunction -lt 0) { $nextFunction = $text.Length }
  $validator = $text.Substring($start, $nextFunction - $start)
  $canonical = "/data/user/0/com.xnas.wpe.suite/files/modules/$id"
  $legacyRoot = "/data/user/0/com.xnas.wpe.$id/files"
  Assert-True ($validator.Contains($canonical) -and $validator.Contains("/data/data/com.xnas.wpe.suite/files/modules/$id")) "$id validator permits only the two package-private suite path forms"
  Assert-True (-not $validator.Contains('starts_with') -and -not $validator.Contains('contains(')) "$id validator does not use prefix matching"
  Assert-True (-not $validator.Contains($legacyRoot)) "$id validator rejects its old standalone-app state path"
  foreach ($peer in $ids | Where-Object { $_ -ne $id }) {
    Assert-True (-not $validator.Contains("/files/modules/$peer")) "$id validator rejects the $peer module path"
  }
  if ($id -eq 'jinchan') {
    Assert-True ($text.Contains('/files/modules/jinchan/..')) 'jinchan native self-test rejects its parent path'
  } else {
    Assert-True ($text.Contains("/files/modules/$id/../x") -and $text.Contains("/files/modules/$id/run")) "$id native self-test rejects traversal and child paths"
  }
}

foreach ($id in @('battleloop','battleskip')) {
  $text = $rustCopies[$id]
  Assert-True ($text.Contains('fn replace_atomic(path: &Path, data: &[u8], mode: u32, owner: Option<u32>)')) "$id status writer supports explicit file ownership"
  Assert-True ($text.Contains('fchown(f.as_raw_fd(), uid, u32::MAX)')) "$id status writer sets status owner before publishing"
  Assert-True ($text.Contains('let owner = fs::metadata(dir).ok().map(') -and $text.Contains('replace_atomic(&p, s.as_bytes(), 0o644, owner)')) "$id status files inherit the owning app directory UID"
}

$loopService = Copy-Source 'app\src\main\java\com\xnas\wpe\battleloop\BattleLoopService.java'
$noWorkerStop = 'if(p==null){writeStatus("state=stopping\nreason=worker_not_started");finishStop(stopGeneration);return;}'
Assert-True ($loopService.Contains($noWorkerStop)) 'battleloop no-worker stop records an unconfirmed stopping state'
$finishStart = $loopService.IndexOf('private synchronized void finishStop(')
$releaseIndex = $loopService.IndexOf('BattleConflictGate.release(', $finishStart, [System.StringComparison]::Ordinal)
$cleanIndex = $loopService.IndexOf('BattleConflictGate.isClean(', $finishStart, [System.StringComparison]::Ordinal)
Assert-True ($finishStart -ge 0 -and $cleanIndex -gt $finishStart -and $releaseIndex -gt $cleanIndex) 'battleloop stop validates full clean evidence before release'
Assert-True ($loopService.Contains('if(stopPending&&worker==null)') -and $loopService.Contains('finishStop(expected)')) 'battleloop monitor retries stop completion while cleanup is pending'
foreach ($id in @('battleloop','battleskip')) {
  $text = $rustCopies[$id]
  Assert-True ($text.Contains('cleanup_last_post.is_some_and') -and $text.Contains('at.elapsed() >= Duration::from_secs(1)')) "$id native worker resends cleanup while stop remains pending"
  Assert-True ($text.Contains('reason=cleanup_unconfirmed') -and $text.Contains('hooksRemoved=true') -and $text.Contains('bindingCleared=true')) "$id native worker publishes stopped only with cleanup evidence"
}

$launcher = Copy-Source 'native\battleskip\launcher-rust\src\lib.rs'
Assert-True ($launcher.Contains('com.xnas.wpe.suite.BattleSkipStart') -and $launcher.Contains('com.xnas.wpe.suite.BattleSkipStop')) 'native fast-forward launcher accepts only its two registered aliases'
Assert-True ($launcher.Contains('data == "/data/user/0/com.xnas.wpe.suite/files"') -and $launcher.Contains('data == "/data/data/com.xnas.wpe.suite/files"')) 'native fast-forward launcher accepts only exact app data roots'
foreach ($peer in $ids | Where-Object { $_ -ne 'battleskip' }) {
  Assert-True (-not $launcher.Contains("/files/modules/$peer")) "fast-forward launcher does not address $peer module files"
}

$mountNative = Copy-Source 'native\mounthold\rust-reader\src\main.rs'
$mountService = Copy-Source 'app\src\main\java\com\xnas\wpe\mounthold\MountHoldService.java'
Assert-True ($mountService.Contains('getFilesDir().getAbsolutePath()') -and $mountService.Contains('--state-dir')) 'mount-hold service passes its module-scoped state directory'
$mountArgsStart = $mountNative.IndexOf('fn args()', [System.StringComparison]::Ordinal)
$mountRun = $mountNative.IndexOf('fn run(', $mountArgsStart, [System.StringComparison]::Ordinal)
$mountArgs = $mountNative.Substring($mountArgsStart, $mountRun - $mountArgsStart)
$mountValidatorStart = $mountNative.IndexOf('fn valid_state_dir(path: &Path)', [System.StringComparison]::Ordinal)
$mountValidatorEnd = $mountNative.IndexOf("`nfn ", $mountValidatorStart + 1, [System.StringComparison]::Ordinal)
$mountValidator = $mountNative.Substring($mountValidatorStart, $mountValidatorEnd - $mountValidatorStart)
Assert-True ($mountArgs.Contains('if !valid_state_dir(&dir)') -and $mountArgs.Contains('--state-dir is not the suite mount-hold module directory')) 'mount-hold args rejects invalid state-dir before accepting parsed arguments'
Assert-True ($mountValidator.Contains('/data/user/0/com.xnas.wpe.suite/files/modules/mounthold') -and $mountValidator.Contains('/data/data/com.xnas.wpe.suite/files/modules/mounthold')) 'mount-hold validator allows exactly the two suite module roots'
Assert-True (-not $mountValidator.Contains('starts_with') -and -not $mountValidator.Contains('canonicalize')) 'mount-hold validator uses exact path equality rather than prefix or normalization'
$mountTestStart = $mountNative.IndexOf('fn state_directory_accepts_only_the_suite_mount_hold_paths()', [System.StringComparison]::Ordinal)
$mountTestEnd = $mountNative.IndexOf("`n    #[test]", $mountTestStart + 1, [System.StringComparison]::Ordinal)
if ($mountTestEnd -lt 0) { $mountTestEnd = $mountNative.Length }
$mountTest = $mountNative.Substring($mountTestStart, $mountTestEnd - $mountTestStart)
foreach ($rejected in @('mounthold/child','mounthold/../battleloop','files"','com.xnas.wpe.battleskip','relative/modules/mounthold')) {
  Assert-True ($mountTest.Contains($rejected)) "mount-hold native test includes rejection case $rejected"
}

Write-Output "SuiteSourceContract: PASS ($assertions assertions; copied sources only)"
Write-Output 'Permission handoff: source routes requests through the host back to the requesting child and saves pending routing state; device acceptance remains unverified.'
Write-Output 'Rust worker integration was not host-run; mount-hold path predicate is additionally exercised by the isolated extracted-function harness.'
