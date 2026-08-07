param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$libraryDll = Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
$hexBoxDll = Join-Path $repo "WPELibrary\bin\$Configuration\Be.Windows.Forms.HexBox.dll"

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function Get-PrivateField($instance, [string]$name) {
    $flags = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $field = $instance.GetType().GetField($name, $flags)
    if ($null -eq $field) {
        throw "Missing private field: $name"
    }
    return $field.GetValue($instance)
}

function Set-PrivateField($instance, [string]$name, $value) {
    $flags = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $field = $instance.GetType().GetField($name, $flags)
    if ($null -eq $field) {
        throw "Missing private field: $name"
    }
    $field.SetValue($instance, $value)
}

function Invoke-PrivateMethod($instance, [string]$name, [object[]]$arguments) {
    $flags = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $method = $instance.GetType().GetMethod($name, $flags)
    if ($null -eq $method) {
        throw "Missing private method: $name"
    }
    $method.Invoke($instance, $arguments) | Out-Null
}

function Pump-Ui([int]$iterations = 10) {
    for ($index = 0; $index -lt $iterations; $index++) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 30
    }
}

if (-not (Test-Path -LiteralPath $libraryDll)) { throw "WPELibrary.dll not found: $libraryDll" }
if (-not (Test-Path -LiteralPath $hexBoxDll)) { throw "HexBox assembly not found: $hexBoxDll" }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll

$form = $null
$newCancellation = $null
try {
    $robotInfo = [WPELibrary.Lib.Socket_RobotInfo]::new(
        $true,
        [Guid]::NewGuid(),
        "Vision completion race regression",
        [WPELibrary.Lib.Socket_Cache+Robot]::InitInstructions())
    $form = [WPELibrary.Socket_RobotForm]::new($robotInfo)
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = [System.Drawing.Point]::new(0, 0)
    $form.ShowInTaskbar = $false
    $form.Show()
    Pump-Ui

    $resultType = [WPELibrary.Lib.Vision.VisionAssistantRunResult]
    $oldTask = [System.Threading.Tasks.Task]::FromResult($resultType::new())
    $newTaskSource = [System.Threading.Tasks.TaskCompletionSource[WPELibrary.Lib.Vision.VisionAssistantRunResult]]::new()
    $newTask = $newTaskSource.Task
    $oldProfile = [WPELibrary.Lib.Socket_VisionProfile]::new()
    $oldCancellation = [System.Threading.CancellationTokenSource]::new()
    $newCancellation = [System.Threading.CancellationTokenSource]::new()

    $runButton = Get-PrivateField $form "bVisionRunSteps"
    $stopButton = Get-PrivateField $form "bVisionStopSteps"
    Set-PrivateField $form "visionAssistantTask" $newTask
    Set-PrivateField $form "visionAssistantCancellation" $newCancellation
    $runButton.Enabled = $false
    $stopButton.Enabled = $true

    Invoke-PrivateMethod $form "CompleteVisionAssistant" @($oldTask, $oldProfile, $oldCancellation)
    Pump-Ui 15

    Assert-True ([object]::ReferenceEquals((Get-PrivateField $form "visionAssistantTask"), $newTask)) `
        "A stale completion callback must not replace the current vision task."
    Assert-True ([object]::ReferenceEquals((Get-PrivateField $form "visionAssistantCancellation"), $newCancellation)) `
        "A stale completion callback must not dispose the current cancellation source."
    Assert-True ((-not $runButton.Enabled) -and $stopButton.Enabled) `
        "A stale completion callback must not reset the current run buttons."

    Write-Host "Vision assistant completion race regression passed."
}
finally {
    if ($null -ne $newCancellation) {
        $newCancellation.Dispose()
    }
    if ($null -ne $form) {
        $form.Dispose()
    }
}
