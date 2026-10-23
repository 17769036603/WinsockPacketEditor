$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$suite = Join-Path $repo 'mobile-suite'
$manifestPath = Join-Path $suite 'app\src\main\AndroidManifest.xml'
$manifest = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
$script:assertions = 0

function Assert-True([bool]$Condition, [string]$Message) {
  $script:assertions++
  if (-not $Condition) { throw "FAIL: $Message" }
}

function Read-Source([string]$RelativePath) {
  $path = Join-Path $suite $RelativePath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "FAIL: required source is missing: $RelativePath"
  }
  return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Get-MethodBody([string]$Text, [string]$MethodName) {
  $pattern = '(?:public|protected|private)\s+(?:static\s+)?(?:final\s+)?void\s+' + [regex]::Escape($MethodName) + '\s*\([^)]*\)\s*\{'
  $match = [regex]::Match($Text, $pattern)
  if (-not $match.Success) { return $null }
  $open = $Text.IndexOf('{', $match.Index + $match.Length - 1)
  $depth = 0
  for ($i = $open; $i -lt $Text.Length; $i++) {
    if ($Text[$i] -eq '{') { $depth++ }
    elseif ($Text[$i] -eq '}') {
      $depth--
      if ($depth -eq 0) { return $Text.Substring($open + 1, $i - $open - 1) }
    }
  }
  throw "FAIL: unterminated $MethodName method"
}

Assert-True ($manifest -match 'android\.permission\.SYSTEM_ALERT_WINDOW') 'suite manifest declares the user-requested battle-panel overlay permission'

$modules = @(
  @{ Id = 'anlei'; Activity = 'anlei\MainActivity.java'; Service = 'anlei\AnleiService.java'; Class = 'AnleiService' },
  @{ Id = 'mounthold'; Activity = 'mounthold\MainActivity.java'; Service = 'mounthold\MountHoldService.java'; Class = 'MountHoldService' },
  @{ Id = 'battleloop'; Activity = 'battleloop\MainActivity.java'; Service = 'battleloop\BattleLoopService.java'; Class = 'BattleLoopService' },
  @{ Id = 'battlepanel'; Activity = 'battlepanel\MainActivity.java'; Service = 'battlepanel\BattleOverlayService.java'; Class = 'BattleOverlayService' }
)

foreach ($module in $modules) {
  $activity = Read-Source "app\src\main\java\com\xnas\wpe\$($module.Activity)"
  $service = Read-Source "app\src\main\java\com\xnas\wpe\$($module.Service)"

  Assert-True ($activity -match 'extends\s+com\.xnas\.wpe\.suite\.ModuleActivity') "$($module.Id) page remains an app-internal suite module"
  Assert-True ($activity.Contains('setContentView(')) "$($module.Id) page retains its in-app content view"
  Assert-True ($activity.Contains($module.Class) -and ($activity.Contains('startService(') -or $activity.Contains('startForegroundService('))) "$($module.Id) page can start its background service"
  if ($module.Id -eq 'battlepanel') {
    Assert-True ($activity -match 'canDrawOverlays' -and $activity -match 'ACTION_MANAGE_OVERLAY_PERMISSION') 'battlepanel requests overlay permission before starting its reader'
  } else {
    Assert-True (-not ($activity -match 'canDrawOverlays|ACTION_MANAGE_OVERLAY_PERMISSION|SYSTEM_ALERT_WINDOW')) "$($module.Id) start path has no system-overlay permission prerequisite"
  }

  foreach ($lifecycle in @('onPause', 'onStop', 'onDestroy')) {
    $body = Get-MethodBody $activity $lifecycle
    if ($null -ne $body) {
      Assert-True (-not ($body -match 'stopService\s*\(|\.setAction\s*\([^)]*(?:STOP|stop)')) "$($module.Id) $lifecycle does not stop its background service"
    }
  }

  if ($module.Id -eq 'battlepanel') {
    Assert-True ($service -match 'TYPE_APPLICATION_OVERLAY' -and $service.Contains('removeOverlayWindows()')) 'only battlepanel owns the requested overlay and removes its windows'
  } else {
    Assert-True (-not ($service -match 'WindowManager|TYPE_APPLICATION_OVERLAY|TYPE_PHONE|canDrawOverlays|ACTION_MANAGE_OVERLAY_PERMISSION|WINDOW_SERVICE|\baddView\s*\(')) "$($module.Id) service does not attach system windows"
  }
  Assert-True ($service.Contains('startForeground(')) "$($module.Id) background service retains foreground notification startup"
  Assert-True ($service -match 'setOngoing\s*\(\s*true\s*\)') "$($module.Id) foreground notification remains ongoing"
}

$suiteMain = Read-Source 'app\src\main\java\com\xnas\wpe\suite\MainActivity.java'
Assert-True ($suiteMain.Contains('getLocalActivityManager().startActivity(module.id, childIntent)')) 'suite shell still launches module pages internally'
Assert-True ($suiteMain.Contains('window.getDecorView()') -and $suiteMain.Contains('moduleContainer.addView(page')) 'suite shell still hosts module content inside its app panel'

Write-Output "BackgroundWindowContract: PASS ($script:assertions assertions; production sources read directly)"
Write-Output 'Static source checks only; Android service survival and notification display were not runtime-tested.'
