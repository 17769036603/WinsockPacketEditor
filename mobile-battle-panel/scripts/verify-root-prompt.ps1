param(
    [string]$Serial = 'emulator-5554'
)

$ErrorActionPreference = 'Stop'
$adbPath = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
$deviceDumpPath = '/sdcard/wpe-battle-panel-root-prompt-check.xml'
$package = 'com.xnas.wpe.battlepanel'

if (-not (Test-Path -LiteralPath $adbPath)) {
    throw "ADB not found: $adbPath"
}

try {
    & $adbPath -s $Serial shell am force-stop $package | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'could not stop the existing app instance'
    }

    & $adbPath -s $Serial shell am start -W -n "$package/.MainActivity" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'could not launch the app'
    }

    & $adbPath -s $Serial shell uiautomator dump $deviceDumpPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'could not inspect the app start screen'
    }
    $startUiLines = & $adbPath -s $Serial shell cat $deviceDumpPath
    if ($LASTEXITCODE -ne 0) {
        throw 'could not read the app start screen hierarchy'
    }
    [xml]$startUi = $startUiLines -join "`n"
    $startButton = $startUi.SelectSingleNode(
        "//node[@package='$package' and @class='android.widget.Button' and @text='启动悬浮面板' and @enabled='true']")
    if ($null -eq $startButton) {
        throw 'the enabled Start Overlay button is not visible'
    }
    $bounds = [regex]::Match($startButton.GetAttribute('bounds'), '\[(\d+),(\d+)\]\[(\d+),(\d+)\]')
    if (-not $bounds.Success) {
        throw 'could not determine the Start Overlay button bounds'
    }
    $tapX = [int](([int]$bounds.Groups[1].Value + [int]$bounds.Groups[3].Value) / 2)
    $tapY = [int](([int]$bounds.Groups[2].Value + [int]$bounds.Groups[4].Value) / 2)
    & $adbPath -s $Serial shell input tap $tapX $tapY
    if ($LASTEXITCODE -ne 0) {
        throw 'could not activate the Start Overlay button'
    }

    $rootUi = ''
    $rootRequestVisible = $false
    $rootWait = [Diagnostics.Stopwatch]::StartNew()
    while ($rootWait.ElapsedMilliseconds -lt 10000) {
        & $adbPath -s $Serial shell uiautomator dump $deviceDumpPath | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw 'uiautomator dump failed after starting the service'
        }
        $rootUiLines = & $adbPath -s $Serial shell cat $deviceDumpPath
        if ($LASTEXITCODE -ne 0) {
            throw 'could not read the Root request hierarchy'
        }
        $rootUi = $rootUiLines -join "`n"
        $rootRequestVisible = ($rootUi -match 'package="com\.android\.settings"') -and
                ($rootUi -match '超级用户请求') -and ($rootUi -match '战斗状态面板')
        if ($rootRequestVisible) {
            break
        }
        Start-Sleep -Milliseconds 250
    }
    $rootWait.Stop()
    if (-not $rootRequestVisible) {
        throw 'Root confirmation for the battle panel did not appear within 10 seconds'
    }

    $windowLines = & $adbPath -s $Serial shell dumpsys window windows
    if ($LASTEXITCODE -ne 0) {
        throw 'could not inspect Android windows'
    }
    $windows = $windowLines -join "`n"
    if ($windows -match 'package=com\.xnas\.wpe\.battlepanel appop=SYSTEM_ALERT_WINDOW') {
        throw 'FAIL: the app overlay is present while its Root confirmation is open'
    }

    'PASS: Root confirmation is visible without the app overlay.'
}
finally {
    & $adbPath -s $Serial shell rm -f $deviceDumpPath | Out-Null
}
