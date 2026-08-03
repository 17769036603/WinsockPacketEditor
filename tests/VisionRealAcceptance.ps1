param(
    [string]$Configuration = "Debug",
    [string]$TesseractPath = "C:\Program Files\Tesseract-OCR\tesseract.exe",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo "WPELibrary\work\Plan2VisionRealAcceptance"
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

function Pump-Ui {
    param([int]$Iterations = 12)

    for ($index = 0; $index -lt $Iterations; $index++) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 40
    }
}

if (-not (Test-Path -LiteralPath $libraryDll)) {
    throw "WPELibrary.dll not found at $libraryDll"
}
if (-not (Test-Path -LiteralPath $hexBoxDll)) {
    throw "HexBox assembly not found at $hexBoxDll"
}
if (-not (Test-Path -LiteralPath $TesseractPath)) {
    throw "Tesseract executable not found at $TesseractPath"
}
if (-not (Test-Path -LiteralPath (Join-Path $tessdataPath "chi_sim.traineddata"))) {
    throw "chi_sim.traineddata not found at $tessdataPath"
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$taskText = [string]::new([char[]](0x4EFB, 0x52A1, 0x5B8C, 0x6210))
$countText = [string]::new([char[]](0x6570, 0x91CF))
$form = [System.Windows.Forms.Form]::new()
$taskLabel = [System.Windows.Forms.Label]::new()
$countLabel = [System.Windows.Forms.Label]::new()
$capture = $null
$movedCapture = $null
$disappearedCapture = $null
$template = $null

try {
    $form.Text = "Codex Vision Real Acceptance"
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = [System.Drawing.Point]::new(350, 150)
    $form.ClientSize = [System.Drawing.Size]::new(960, 300)
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedToolWindow
    $form.ShowInTaskbar = $false
    $form.TopMost = $true
    $form.BackColor = [System.Drawing.Color]::White

    foreach ($label in @($taskLabel, $countLabel)) {
        $label.AutoSize = $true
        $label.BackColor = [System.Drawing.Color]::White
        $label.ForeColor = [System.Drawing.Color]::Black
        $label.Font = [System.Drawing.Font]::new(
            "Microsoft YaHei",
            42,
            [System.Drawing.FontStyle]::Bold,
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    $taskLabel.Text = $taskText
    $taskLabel.Location = [System.Drawing.Point]::new(24, 20)
    $countLabel.Text = "$countText 42"
    $countLabel.Location = [System.Drawing.Point]::new(24, 112)
    $form.Controls.Add($taskLabel)
    $form.Controls.Add($countLabel)

    $form.Show()
    $form.Activate()
    Pump-Ui

    $windows = [WPELibrary.Lib.Vision.VisionWindowService]::EnumerateVisibleWindows(-1)
    $windowInfo = $windows | Where-Object { $_.Handle -eq $form.Handle } | Select-Object -First 1
    Assert-True ($null -ne $windowInfo) "The real acceptance window must be discoverable by window enumeration."

    $clientBounds = [System.Drawing.Rectangle]::Empty
    $clientSize = [System.Drawing.Size]::Empty
    $hasClientBounds = [WPELibrary.Lib.Vision.VisionWindowService]::TryGetClientBounds(
        $form.Handle,
        [ref]$clientBounds,
        [ref]$clientSize)
    Assert-True $hasClientBounds "The real acceptance window must expose a valid client area."
    $region = [WPELibrary.Lib.Vision.VisionRegion]::new()
    $region.X = 0
    $region.Y = 0
    $region.Width = $clientSize.Width
    $region.Height = $clientSize.Height

    $options = [WPELibrary.Lib.Vision.VisionOcrOptions]::new()
    $options.ExecutablePath = $TesseractPath
    $options.TessdataPath = $tessdataPath
    $options.TimeoutMilliseconds = 20000
    $recognizer = [WPELibrary.Lib.Vision.VisionTesseractRecognizer]::new("tesseract.exe")

    $capture = [WPELibrary.Lib.Vision.VisionWindowService]::CaptureClientRegion(
        $form.Handle,
        $region)
    $firstImagePath = Join-Path $resolvedOutput "01-real-window-before-move.png"
    $capture.Save($firstImagePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $ocr = $recognizer.Recognize(
        $capture,
        $options,
        [System.Threading.CancellationToken]::None)
    Write-Host ("BEFORE_MOVE: success={0}; confidence={1:N3}; text={2}" -f
        $ocr.Success,
        $ocr.Confidence,
        $ocr.Text)
    Assert-True $ocr.Success "Real window OCR before movement must succeed: $($ocr.Error)"
    Assert-True ($ocr.Text.Contains($taskText.Substring(0, 2))) "Real OCR must recognize the task keyword."
    Assert-True ($ocr.Text.Contains($taskText.Substring(2, 2))) "Real OCR must recognize the completion keyword."
    Assert-True ($ocr.Text.Contains("42")) "Real OCR must recognize the numeric value."

    $textCondition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $textCondition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TextAppears
    $textCondition.Region = $region.Clone()
    $textCondition.TextCondition.ExpectedText = $taskText
    $textCondition.TextCondition.MinimumConfidence = 0.5
    [string]$conditionReason = ""
    $textMatched = [WPELibrary.Lib.Vision.VisionConditionEvaluator]::Matches(
        $textCondition,
        [WPELibrary.Lib.Vision.VisionObservation]::FromOcr($ocr),
        [ref]$conditionReason)
    Assert-True $textMatched "Real OCR text condition must match: $conditionReason"

    $numberCondition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $numberCondition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::NumberInRange
    $numberCondition.Region = $region.Clone()
    $numberCondition.TextCondition.MatchMode = [WPELibrary.Lib.Vision.VisionTextMatchMode]::NumberRange
    $numberCondition.TextCondition.MinimumNumber = 40
    $numberCondition.TextCondition.MaximumNumber = 45
    $numberMatched = [WPELibrary.Lib.Vision.VisionConditionEvaluator]::Matches(
        $numberCondition,
        [WPELibrary.Lib.Vision.VisionObservation]::FromOcr($ocr),
        [ref]$conditionReason)
    Assert-True $numberMatched "Real OCR number condition must match 42: $conditionReason"

    $template = $capture.Clone(
        [System.Drawing.Rectangle]::new(0, 0, [Math]::Min(340, $capture.Width), [Math]::Min(210, $capture.Height)),
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $templateResult = [WPELibrary.Lib.Vision.VisionTemplateMatcher]::FindBestMatch(
        $capture,
        $template,
        0.99,
        [System.Threading.CancellationToken]::None)
    Assert-True $templateResult.Found "Real captured pixels must match the extracted template."

    $form.Location = [System.Drawing.Point]::new(560, 190)
    Pump-Ui
    $movedCapture = [WPELibrary.Lib.Vision.VisionWindowService]::CaptureClientRegion(
        $form.Handle,
        $region)
    $movedImagePath = Join-Path $resolvedOutput "02-real-window-after-move.png"
    $movedCapture.Save($movedImagePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $movedOcr = $recognizer.Recognize(
        $movedCapture,
        $options,
        [System.Threading.CancellationToken]::None)
    Write-Host ("AFTER_MOVE: success={0}; confidence={1:N3}; text={2}" -f
        $movedOcr.Success,
        $movedOcr.Confidence,
        $movedOcr.Text)
    Assert-True $movedOcr.Success "OCR after window movement must succeed: $($movedOcr.Error)"
    Assert-True ($movedOcr.Text.Contains("42")) "OCR after window movement must retain the numeric value."

    $form.WindowState = [System.Windows.Forms.FormWindowState]::Minimized
    Pump-Ui
    $minimizedCaptureRejected = $false
    try {
        $minimizedCapture = [WPELibrary.Lib.Vision.VisionWindowService]::CaptureClientRegion(
            $form.Handle,
            $region)
        if ($null -ne $minimizedCapture) { $minimizedCapture.Dispose() }
    }
    catch [System.InvalidOperationException] {
        $minimizedCaptureRejected = $true
    }
    Assert-True $minimizedCaptureRejected "A minimized target window must reject vision capture."
    Write-Host "MINIMIZED_CAPTURE: rejected=True"
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
    $form.Show()
    $form.Activate()
    Pump-Ui

    $profile = [WPELibrary.Lib.Socket_VisionProfile]::new()
    $profile.WindowHandle = $form.Handle.ToInt64()
    $profile.Region = $region.Clone()
    $profile.OcrOptions = $options.Clone()
    $stateStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
    $stateStep.Name = "real-window-text"
    $stateStep.Condition = $textCondition.Clone()
    $stateStep.Condition.RequiredConfirmations = 2
    $stateStep.Condition.PollIntervalMilliseconds = 80
    $stateStep.Condition.TimeoutMilliseconds = 8000
    $profile.AssistantSteps.Add($stateStep)
    $stateResult = [WPELibrary.Lib.Vision.VisionAssistantRunner]::Run(
        $profile,
        $recognizer,
        [System.Threading.CancellationToken]::None,
        $null)
    Assert-True ($stateResult.Succeeded -and $stateResult.CompletedSteps -eq 1) `
        "The real window must pass through the vision state machine."
    Write-Host ("STATE_MACHINE: succeeded={0}; completed_steps={1}" -f
        $stateResult.Succeeded,
        $stateResult.CompletedSteps)

    $restartStateResult = [WPELibrary.Lib.Vision.VisionAssistantRunner]::Run(
        $profile,
        $recognizer,
        [System.Threading.CancellationToken]::None,
        $null)
    Assert-True ($restartStateResult.Succeeded -and $restartStateResult.CompletedSteps -eq 1) `
        "The vision state machine must be restartable on the same visible target."
    Write-Host ("RESTART_STATE_MACHINE: succeeded={0}; completed_steps={1}" -f
        $restartStateResult.Succeeded,
        $restartStateResult.CompletedSteps)

    $cancelSource = [System.Threading.CancellationTokenSource]::new()
    $cancelSource.Cancel()
    try {
        $cancelledRun = [WPELibrary.Lib.Vision.VisionAssistantRunner]::Run(
            $profile,
            $recognizer,
            $cancelSource.Token,
            $null)
    }
    finally {
        $cancelSource.Dispose()
    }
    Assert-True $cancelledRun.Cancelled "A cancelled vision run must stop before capture."
    Write-Host "CANCELLED_RUN: cancelled=True"

    $timeoutProfile = [WPELibrary.Lib.Socket_VisionProfile]::new()
    $timeoutProfile.WindowHandle = $form.Handle.ToInt64()
    $timeoutProfile.Region = $region.Clone()
    $timeoutProfile.OcrOptions = $options.Clone()
    $timeoutStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
    $timeoutStep.Name = "real-window-timeout"
    $timeoutStep.Condition = $textCondition.Clone()
    $timeoutStep.Condition.TextCondition.ExpectedText = "text-that-does-not-exist"
    $timeoutStep.Condition.RequiredConfirmations = 1
    $timeoutStep.Condition.PollIntervalMilliseconds = 80
    $timeoutStep.Condition.TimeoutMilliseconds = 1200
    $timeoutStep.Condition.MaxRetries = 0
    $timeoutProfile.AssistantSteps.Add($timeoutStep)
    $timeoutRun = [WPELibrary.Lib.Vision.VisionAssistantRunner]::Run(
        $timeoutProfile,
        $recognizer,
        [System.Threading.CancellationToken]::None,
        $null)
    Assert-True ((-not $timeoutRun.Succeeded) -and (-not $timeoutRun.Cancelled)) `
        "A real OCR condition that never appears must time out instead of succeeding."
    Write-Host ("TIMEOUT_RUN: succeeded={0}; cancelled={1}; error={2}" -f
        $timeoutRun.Succeeded,
        $timeoutRun.Cancelled,
        $timeoutRun.Error)

    $capture.Dispose()
    $capture = $null
    $movedCapture.Dispose()
    $movedCapture = $null
    $template.Dispose()
    $template = $null
    $taskLabel.Visible = $false
    $countLabel.Visible = $false
    Pump-Ui

    $disappearedCapture = [WPELibrary.Lib.Vision.VisionWindowService]::CaptureClientRegion(
        $form.Handle,
        $region)
    $disappearedImagePath = Join-Path $resolvedOutput "03-real-window-text-disappeared.png"
    $disappearedCapture.Save($disappearedImagePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $disappearedOcr = $recognizer.Recognize(
        $disappearedCapture,
        $options,
        [System.Threading.CancellationToken]::None)
    Write-Host ("DISAPPEARED: success={0}; available={1}; error={2}" -f
        $disappearedOcr.Success,
        $disappearedOcr.Available,
        $disappearedOcr.Error)
    Assert-True ($disappearedOcr.Available -and -not $disappearedOcr.Success) `
        "Real OCR must report an available no-text result after the labels disappear."
    $disappearanceCondition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $disappearanceCondition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TextDisappears
    $disappearanceCondition.Region = $region.Clone()
    $disappearanceCondition.TextCondition.ExpectedText = $taskText
    $disappearanceCondition.TextCondition.MinimumConfidence = 0.5
    $disappearanceMatched = [WPELibrary.Lib.Vision.VisionConditionEvaluator]::Matches(
        $disappearanceCondition,
        [WPELibrary.Lib.Vision.VisionObservation]::FromOcr($disappearedOcr),
        [ref]$conditionReason)
    Assert-True $disappearanceMatched "Text disappearance must match an available OCR no-text result: $conditionReason"

    $disappearanceProfile = [WPELibrary.Lib.Socket_VisionProfile]::new()
    $disappearanceProfile.WindowHandle = $form.Handle.ToInt64()
    $disappearanceProfile.Region = $region.Clone()
    $disappearanceProfile.OcrOptions = $options.Clone()
    $disappearanceStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
    $disappearanceStep.Name = "real-window-text-disappears"
    $disappearanceStep.Condition = $disappearanceCondition.Clone()
    $disappearanceStep.Condition.RequiredConfirmations = 2
    $disappearanceStep.Condition.PollIntervalMilliseconds = 80
    $disappearanceStep.Condition.TimeoutMilliseconds = 8000
    $disappearanceProfile.AssistantSteps.Add($disappearanceStep)
    $disappearanceStateResult = [WPELibrary.Lib.Vision.VisionAssistantRunner]::Run(
        $disappearanceProfile,
        $recognizer,
        [System.Threading.CancellationToken]::None,
        $null)
    Assert-True ($disappearanceStateResult.Succeeded -and $disappearanceStateResult.CompletedSteps -eq 1) `
        "The real window text-disappeared condition must pass through the state machine."
    Write-Host ("DISAPPEARANCE_STATE_MACHINE: succeeded={0}; completed_steps={1}" -f
        $disappearanceStateResult.Succeeded,
        $disappearanceStateResult.CompletedSteps)
}
finally {
    if ($null -ne $template) { $template.Dispose() }
    if ($null -ne $capture) { $capture.Dispose() }
    if ($null -ne $movedCapture) { $movedCapture.Dispose() }
    if ($null -ne $disappearedCapture) { $disappearedCapture.Dispose() }
    if ($null -ne $taskLabel.Font) { $taskLabel.Font.Dispose() }
    if ($null -ne $countLabel.Font) { $countLabel.Font.Dispose() }
    if ($null -ne $form) {
        $form.Close()
        $form.Dispose()
    }
}

Write-Host "Vision real acceptance passed. Screenshots are in: $(Join-Path $resolvedOutput '')"
