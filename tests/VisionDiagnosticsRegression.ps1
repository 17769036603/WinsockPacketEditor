param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [string]$LibraryPath = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$libraryDll = if ([string]::IsNullOrWhiteSpace($LibraryPath)) {
    Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
}
else {
    [System.IO.Path]::GetFullPath($LibraryPath)
}
$resolvedOutput = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo ("WPELibrary\work\VisionDiagnosticsRegression\" + [Guid]::NewGuid().ToString("N"))
}
else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

if (-not (Test-Path -LiteralPath $libraryDll)) {
    throw "WPELibrary.dll not found at $libraryDll"
}
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][System.Reflection.Assembly]::LoadFrom($libraryDll)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$form = [System.Windows.Forms.Form]::new()
$marker = [System.Windows.Forms.Panel]::new()
$capture = $null
$successTemplate = $null
$failureTemplate = $null
try {
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = [System.Drawing.Point]::new(120, 120)
    $form.ClientSize = [System.Drawing.Size]::new(320, 180)
    $form.ShowInTaskbar = $false
    $form.TopMost = $true
    $form.Text = "Vision diagnostics synthetic target"
    $form.BackColor = [System.Drawing.Color]::FromArgb(30, 90, 140)
    $marker.Location = [System.Drawing.Point]::new(20, 20)
    $marker.Size = [System.Drawing.Size]::new(40, 30)
    $marker.BackColor = [System.Drawing.Color]::Yellow
    $form.Controls.Add($marker)
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()

    $windows = [WPELibrary.Lib.Vision.VisionWindowService]::EnumerateVisibleWindows(-1)
    $windowInfo = $windows | Where-Object { $_.Handle -eq $form.Handle } | Select-Object -First 1
    Assert-True ($null -ne $windowInfo) "The diagnostics target must be discoverable."

    $profile = [WPELibrary.Lib.Socket_VisionProfile]::new()
    $profile.WindowHandle = $windowInfo.Handle.ToInt64()
    $profile.ProcessId = $windowInfo.ProcessId
    $profile.ProcessName = $windowInfo.ProcessName
    $profile.ProcessPath = $windowInfo.ProcessPath
    $profile.ProcessStartTimeUtcTicks = $windowInfo.ProcessStartTimeUtcTicks
    $profile.WindowTitle = $windowInfo.WindowTitle
    $profile.Region.X = 0
    $profile.Region.Y = 0
    $profile.Region.Width = 320
    $profile.Region.Height = 180
    $profile.CaptureSettings.MinimumIntervalMilliseconds = 1000
    $profile.CaptureSettings.SkipUnchangedFrames = $true

    $form.Activate()
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    [System.Windows.Forms.Application]::DoEvents()
    $capture = [WPELibrary.Lib.Vision.VisionWindowService]::CaptureClientRegion(
        $form.Handle,
        $profile.Region)
    $successTemplate = $capture.Clone(
        [System.Drawing.Rectangle]::new(20, 20, 40, 30),
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $successCondition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $successCondition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TemplateAppears
    $successCondition.Region = $profile.Region.Clone()
    $successCondition.Template = $successTemplate
    $successCondition.MinimumSimilarity = 0.95
    $provider = [WPELibrary.Lib.Vision.VisionWindowObservationProvider]::new($profile, $null)
    $first = $provider.Observe($successCondition, [System.Threading.CancellationToken]::None)
    $second = $provider.Observe($successCondition, [System.Threading.CancellationToken]::None)
    Assert-True ($first.TemplateResult.Found -and $first.CaptureSource.Length -gt 0 -and
        $first.FrameFingerprint -ne 0) `
        "A successful observation must expose a match and capture diagnostics."
    Assert-True $second.CacheHit "The capture interval cache must report a cache hit."

    $profile.CaptureSettings.SaveFailureSnapshots = $true
    $profile.CaptureSettings.FailureSnapshotDirectory = $resolvedOutput
    $failureTemplate = [System.Drawing.Bitmap]::new(41, 31)
    $failureGraphics = [System.Drawing.Graphics]::FromImage($failureTemplate)
    try {
        $failureGraphics.Clear([System.Drawing.Color]::Magenta)
        $failureGraphics.FillRectangle([System.Drawing.Brushes]::Black, 7, 8, 12, 9)
    }
    finally {
        $failureGraphics.Dispose()
    }
    $failureCondition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $failureCondition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TemplateAppears
    $failureCondition.Region = $profile.Region.Clone()
    $failureCondition.Template = $failureTemplate
    $failureCondition.MinimumSimilarity = 0.99
    $failureProvider = [WPELibrary.Lib.Vision.VisionWindowObservationProvider]::new($profile, $null)
    $failure = $failureProvider.Observe($failureCondition, [System.Threading.CancellationToken]::None)
    Assert-True ($null -ne $failure.TemplateResult -and -not $failure.TemplateResult.Found) `
        "A missing template must produce a failed observation."
    Assert-True (-not [string]::IsNullOrWhiteSpace($failure.DiagnosticSnapshotPath) -and
        (Test-Path -LiteralPath $failure.DiagnosticSnapshotPath)) `
        "A failed observation must save an opt-in diagnostic screenshot."
    Assert-True ((Get-ChildItem -LiteralPath $resolvedOutput -Filter *.png).Count -ge 1) `
        "The diagnostic output directory must contain a PNG snapshot."
    Write-Output ("Vision diagnostics regression passed: cacheHit={0}; snapshot={1}" -f
        $second.CacheHit,
        $failure.DiagnosticSnapshotPath)
}
finally {
    if ($null -ne $failureTemplate) { $failureTemplate.Dispose() }
    if ($null -ne $successTemplate) { $successTemplate.Dispose() }
    if ($null -ne $capture) { $capture.Dispose() }
    $form.Close()
    $marker.Dispose()
    $form.Dispose()
}
