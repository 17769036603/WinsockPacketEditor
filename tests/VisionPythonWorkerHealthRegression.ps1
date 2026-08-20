param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$python = Join-Path $env:LOCALAPPDATA "XNAS\WPE\vision-worker\python\python.exe"
$worker = Join-Path $repo "vision_worker\worker.py"
$modelDirectory = Join-Path $repo "vision_worker\models\ocr"
$library = Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
$hexBox = Join-Path $repo "ThirdParty\Be.Windows.Forms.HexBox\bin\$Configuration\Be.Windows.Forms.HexBox.dll"

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True (Test-Path -LiteralPath $python) "Deployed Python runtime was not found: $python"
Assert-True (Test-Path -LiteralPath $worker) "Worker script was not found: $worker"
Assert-True (Test-Path -LiteralPath (Join-Path $modelDirectory "models.manifest.json")) "Model manifest was not found."
Assert-True (Test-Path -LiteralPath $library) "WPELibrary.dll was not found: $library"
Assert-True (Test-Path -LiteralPath $hexBox) "HexBox assembly was not found: $hexBox"

Add-Type -Path $hexBox
Add-Type -Path $library

$options = [WPELibrary.Lib.Vision.VisionOcrOptions]::new()
$options.Engine = [WPELibrary.Lib.Vision.VisionOcrEngine]::PythonWorker
$options.PythonExecutablePath = $python
$options.PythonWorkerScriptPath = $worker
$options.OnnxModelDirectory = $modelDirectory
$options.PythonWorkerTimeoutMilliseconds = 30000
$recognizer = [WPELibrary.Lib.Vision.VisionPythonWorkerTextRecognizer]::new()
try {
    $health = $recognizer.CheckHealth($options, $true, [System.Threading.CancellationToken]::None)
    Write-Host ("Worker health: ready={0}; manifest={1}; responded={2}; warmup_ms={3}; summary={4}" -f
        $health.IsReady, $health.ManifestVerified, $health.WorkerResponded, $health.WarmupMilliseconds, $health.Summary)
    Assert-True $health.IsReady "Python Worker health check failed: $($health.Summary)"
    Assert-True $health.ManifestVerified "Python Worker did not verify the model manifest."
    Assert-True $health.WorkerResponded "Python Worker did not respond to ping/warmup."
    Assert-True $recognizer.LastDiagnostic.Contains("warmup") "Python Worker diagnostics did not record warmup."
}
finally {
    $recognizer.Dispose()
}

Write-Host "Vision Python worker health regression passed."
