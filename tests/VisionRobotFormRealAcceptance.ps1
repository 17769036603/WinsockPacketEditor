param(
    [string]$Configuration = "Debug",
    [string]$TesseractPath = "C:\Program Files\Tesseract-OCR\tesseract.exe",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo "WPELibrary\work\Plan2VisionRobotFormAcceptance"
}
else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$libraryDll = Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
$hexBoxDll = Join-Path $repo "WPELibrary\bin\$Configuration\Be.Windows.Forms.HexBox.dll"
$tessdataPath = Join-Path (Split-Path -Parent $TesseractPath) "tessdata"

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

function Invoke-PrivateEvent($instance, [string]$name) {
    $flags = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $method = $instance.GetType().GetMethod($name, $flags)
    if ($null -eq $method) {
        throw "Missing private event method: $name"
    }
    $method.Invoke($instance, [object[]]@($null, [System.EventArgs]::Empty)) | Out-Null
}

function Pump-Ui {
    param([int]$Iterations = 10)

    for ($index = 0; $index -lt $Iterations; $index++) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 40
    }
}

function Wait-RobotVisionIdle($robotForm, [int]$TimeoutMilliseconds = 30000) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        Pump-Ui 2
        $task = Get-PrivateField $robotForm "visionAssistantTask"
        if ($null -eq $task) {
            return
        }
        Start-Sleep -Milliseconds 80
    }
    throw "Robot form vision task did not become idle within $TimeoutMilliseconds ms."
}

if (-not (Test-Path -LiteralPath $libraryDll)) { throw "WPELibrary.dll not found: $libraryDll" }
if (-not (Test-Path -LiteralPath $hexBoxDll)) { throw "HexBox assembly not found: $hexBoxDll" }
if (-not (Test-Path -LiteralPath $TesseractPath)) { throw "Tesseract not found: $TesseractPath" }
if (-not (Test-Path -LiteralPath (Join-Path $tessdataPath "chi_sim.traineddata"))) {
    throw "chi_sim.traineddata not found: $tessdataPath"
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$taskText = [string]::new([char[]](0x4EFB, 0x52A1, 0x5B8C, 0x6210))
$targetTitle = "CodexRobotTarget-" + [Guid]::NewGuid().ToString("N")
$targetScript = Join-Path $PSScriptRoot "VisionAcceptanceTarget.ps1"
$targetCommandFile = Join-Path ([System.IO.Path]::GetTempPath()) ("vision-target-" + [Guid]::NewGuid().ToString("N") + ".cmd")
$targetProcess = $null
$robotInfo = $null
$robotForm = $null
$template = $null

try {
    Assert-True (Test-Path -LiteralPath $targetScript) "The target helper script is missing."
    $targetProcess = Start-Process `
        -FilePath "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -ArgumentList @(
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            $targetScript,
            "-Title",
            $targetTitle,
            "-CommandFile",
            $targetCommandFile) `
        -WindowStyle Hidden `
        -PassThru

    $targetWindow = $null
    $targetWatch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($targetWatch.ElapsedMilliseconds -lt 15000 -and $null -eq $targetWindow) {
        $targetWindow = [WPELibrary.Lib.Vision.VisionWindowService]::EnumerateVisibleWindows(-1) |
            Where-Object { $_.WindowTitle -eq $targetTitle } |
            Select-Object -First 1
        if ($null -eq $targetWindow) {
            Start-Sleep -Milliseconds 100
        }
    }
    Assert-True ($null -ne $targetWindow) "The independent target process did not expose a visible window."

    $options = [WPELibrary.Lib.Vision.VisionOcrOptions]::new()
    $options.ExecutablePath = $TesseractPath
    $options.TessdataPath = $tessdataPath
    $options.TimeoutMilliseconds = 20000
    $region = [WPELibrary.Lib.Vision.VisionRegion]::new()
    $region.Width = $targetWindow.ClientSize.Width
    $region.Height = $targetWindow.ClientSize.Height

    $robotInfo = [WPELibrary.Lib.Socket_RobotInfo]::new(
        $true,
        [Guid]::NewGuid(),
        "Robot form vision acceptance",
        [WPELibrary.Lib.Socket_Cache+Robot]::InitInstructions())
    $profile = $robotInfo.VisionProfile
    $profile.WindowHandle = $targetWindow.Handle.ToInt64()
    $profile.ProcessId = $targetWindow.ProcessId
    $profile.ProcessName = $targetWindow.ProcessName
    $profile.WindowTitle = $targetWindow.WindowTitle
    $profile.Region = $region.Clone()
    $profile.OcrOptions = $options.Clone()
    $profile.OcrCondition.ExpectedText = $taskText
    $profile.OcrCondition.MinimumConfidence = 0.5

    $appearsCondition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $appearsCondition.Name = "ui-text-appears"
    $appearsCondition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TextAppears
    $appearsCondition.Region = $region.Clone()
    $appearsCondition.TextCondition.ExpectedText = $taskText
    $appearsCondition.TextCondition.MinimumConfidence = 0.5
    $appearsCondition.RequiredConfirmations = 2
    $appearsCondition.PollIntervalMilliseconds = 80
    $appearsCondition.TimeoutMilliseconds = 8000
    $appearsStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
    $appearsStep.Name = "ui-text-appears"
    $appearsStep.Condition = $appearsCondition
    $profile.AssistantSteps.Add($appearsStep)

    $robotForm = [WPELibrary.Socket_RobotForm]::new($robotInfo)
    $robotForm.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $robotForm.Location = [System.Drawing.Point]::new(0, 0)
    $robotForm.ShowInTaskbar = $false
    $robotForm.Show()
    Pump-Ui

    $windowsCombo = Get-PrivateField $robotForm "cbbVisionWindows"
    $previewBox = Get-PrivateField $robotForm "pbVisionPreview"
    $templateBox = Get-PrivateField $robotForm "pbVisionTemplate"
    $visionStatus = Get-PrivateField $robotForm "lVisionStatus"
    $matchStatus = Get-PrivateField $robotForm "lVisionMatchStatus"
    $ocrStatus = Get-PrivateField $robotForm "lVisionOcrStatus"
    $assistantStatus = Get-PrivateField $robotForm "lVisionAssistantStatus"
    $assistantLog = Get-PrivateField $robotForm "txtVisionAssistantLog"
    $conditionType = Get-PrivateField $robotForm "cbbVisionConditionType"
    $keyword = Get-PrivateField $robotForm "txtVisionOcrKeyword"
    $stepsCombo = Get-PrivateField $robotForm "cbbVisionSteps"
    $runButton = Get-PrivateField $robotForm "bVisionRunSteps"
    $stopButton = Get-PrivateField $robotForm "bVisionStopSteps"
    $confirmations = Get-PrivateField $robotForm "nudVisionConfirmations"
    $pollInterval = Get-PrivateField $robotForm "nudVisionPollInterval"
    $timeout = Get-PrivateField $robotForm "nudVisionTimeout"

    $targetIndex = -1
    for ($index = 0; $index -lt $windowsCombo.Items.Count; $index++) {
        $window = $windowsCombo.Items[$index]
        if ($window.Handle.ToInt64() -eq $targetWindow.Handle.ToInt64()) {
            $targetIndex = $index
            break
        }
    }
    Assert-True ($targetIndex -ge 0) "The real Robot form must discover the synthetic target window."
    $windowsCombo.SelectedIndex = $targetIndex
    Pump-Ui

    Invoke-PrivateEvent $robotForm "bCaptureVision_Click"
    Pump-Ui
    $visionPreview = Get-PrivateField $robotForm "visionPreview"
    Assert-True ($null -ne $visionPreview -and $previewBox.Image -ne $null) `
        "The Robot form capture action must populate the preview."
    [WPELibrary.Lib.Vision.VisionWindowService]::SavePng(
        $visionPreview,
        (Join-Path $resolvedOutput "01-robot-form-capture.png"))
    Assert-True (-not [string]::IsNullOrWhiteSpace($visionStatus.Text)) `
        "The Robot form capture action must update its status."

    $visionTemplate = Get-PrivateField $robotForm "visionTemplate"
    Assert-True ($null -ne $visionTemplate -and $templateBox.Image -ne $null) `
        "The first capture must automatically populate the image reference preview."

    $keyword.Text = $taskText
    Invoke-PrivateEvent $robotForm "bRecognizeVisionText_Click"
    Pump-Ui 20
    Assert-True ($ocrStatus.Text.Contains($taskText.Substring(0, 2)) -and
        $ocrStatus.Text.Contains("42")) `
        "The Robot form OCR action must show the recognized task text and number."

    $threshold = Get-PrivateField $robotForm "nudVisionThreshold"
    $threshold.Value = 99
    Invoke-PrivateEvent $robotForm "bMatchVisionTemplate_Click"
    Pump-Ui
    Assert-True (-not [string]::IsNullOrWhiteSpace($matchStatus.Text)) `
        "The Robot form template action must update its status."

    Invoke-PrivateEvent $robotForm "bRunVisionAssistant_Click"
    Wait-RobotVisionIdle $robotForm
    Assert-True ($runButton.Enabled -and (-not $stopButton.Enabled)) `
        "The Robot form must restore run/stop buttons after a successful vision run."
    Assert-True ($assistantLog.Text.Contains($taskText.Substring(0, 2)) -and
        $assistantLog.Text.Contains("42")) `
        "The Robot form log must contain OCR evidence from the successful run."
    Write-Host "ROBOT_FORM_APPEARS: capture=True; ocr=True; template=True; run=True"

    $stepsCombo.SelectedIndex = 1
    Invoke-PrivateEvent $robotForm "bRemoveVisionStep_Click"
    Assert-True ($profile.AssistantSteps.Count -eq 0) `
        "The Robot form must remove the completed vision step."
    $conditionType.SelectedIndex = 1
    $keyword.Text = $taskText
    $confirmations.Value = 2
    $pollInterval.Value = 80
    $timeout.Value = 8000
    Invoke-PrivateEvent $robotForm "bConfirmVisionAction_Click"
    Assert-True ($profile.AssistantSteps.Count -eq 1 -and
        $profile.AssistantSteps[0].Condition.Type -eq [WPELibrary.Lib.Vision.VisionConditionType]::TextDisappears) `
        "The Robot form must add a text-disappeared step through its editor."
    [System.IO.File]::WriteAllText($targetCommandFile, "hide")
    Start-Sleep -Milliseconds 300
    Pump-Ui
    Invoke-PrivateEvent $robotForm "bRunVisionAssistant_Click"
    Wait-RobotVisionIdle $robotForm
    Assert-True ($runButton.Enabled -and (-not $stopButton.Enabled)) `
        "The Robot form must complete a text-disappeared run and restore controls."
    Assert-True ($assistantLog.Text.Length -gt 0) `
        "The Robot form must retain logs for a text-disappeared run."
    Write-Host "ROBOT_FORM_DISAPPEARS: editor=True; run=True"

    $stepsCombo.SelectedIndex = 1
    Invoke-PrivateEvent $robotForm "bRemoveVisionStep_Click"
    $conditionType.SelectedIndex = 0
    $keyword.Text = "text-that-does-not-exist"
    $confirmations.Value = 1
    $pollInterval.Value = 80
    $timeout.Value = 20000
    Invoke-PrivateEvent $robotForm "bConfirmVisionAction_Click"
    [System.IO.File]::WriteAllText($targetCommandFile, "show")
    Start-Sleep -Milliseconds 300
    Pump-Ui
    Invoke-PrivateEvent $robotForm "bRunVisionAssistant_Click"
    Start-Sleep -Milliseconds 700
    Pump-Ui 4
    Invoke-PrivateEvent $robotForm "bStopVisionAssistant_Click"
    Wait-RobotVisionIdle $robotForm
    Assert-True ($runButton.Enabled -and (-not $stopButton.Enabled)) `
        "The Robot form stop action must cancel a running vision task and restore controls."
    Write-Host "ROBOT_FORM_STOP: cancelled=True"
}
finally {
    if ($null -ne $robotForm) {
        $activeTask = Get-PrivateField $robotForm "visionAssistantTask"
        if ($null -ne $activeTask) {
            Invoke-PrivateEvent $robotForm "bStopVisionAssistant_Click"
            Pump-Ui 10
        }
        $robotForm.Close()
        $robotForm.Dispose()
    }
    if ($null -ne $template -and $null -ne $robotForm) {
        $template.Dispose()
    }
    if ($null -ne $targetProcess) {
        if (-not $targetProcess.HasExited) {
            try {
                [System.IO.File]::WriteAllText($targetCommandFile, "close")
            }
            catch {
            }
            if (-not $targetProcess.WaitForExit(3000)) {
                Stop-Process -Id $targetProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
        $targetProcess.Dispose()
    }
    if (Test-Path -LiteralPath $targetCommandFile) {
        Remove-Item -LiteralPath $targetCommandFile -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Robot form real acceptance passed. Screenshots are in: $resolvedOutput"
