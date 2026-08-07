param([string]$Configuration = "Debug")

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$python = Join-Path $env:LOCALAPPDATA "XNAS\WPE\vision-worker\python\python.exe"
$worker = Join-Path $repo "vision_worker\worker.py"
$library = Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
$hexBox = Join-Path $repo "ThirdParty\Be.Windows.Forms.HexBox\bin\$Configuration\Be.Windows.Forms.HexBox.dll"

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

if (-not (Test-Path -LiteralPath $python)) { throw "Deployed Python runtime was not found: $python" }
if (-not (Test-Path -LiteralPath $worker)) { throw "Worker script was not found: $worker" }
if (-not (Test-Path -LiteralPath $library)) { throw "WPELibrary.dll was not found: $library" }
if (-not (Test-Path -LiteralPath $hexBox)) { throw "HexBox assembly was not found: $hexBox" }

Add-Type -AssemblyName System.Drawing
Add-Type -Path $hexBox
Add-Type -Path $library

$bitmap = [System.Drawing.Bitmap]::new(640, 180)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$font = [System.Drawing.Font]::new("Arial", 42, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
try {
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.DrawString("TASK 42", $font, [System.Drawing.Brushes]::Black, 30, 50)
}
finally {
    $graphics.Dispose()
    $font.Dispose()
}

$options = [WPELibrary.Lib.Vision.VisionOcrOptions]::new()
$options.Engine = [WPELibrary.Lib.Vision.VisionOcrEngine]::PythonWorker
$options.PythonExecutablePath = $python
$options.PythonWorkerScriptPath = $worker
$options.PythonWorkerTimeoutMilliseconds = 30000
$recognizer = [WPELibrary.Lib.Vision.VisionPythonWorkerTextRecognizer]::new()
try {
    $result = $recognizer.Recognize(
        $bitmap,
        $options,
        [System.Threading.CancellationToken]::None)
    Write-Host ("Python bridge OCR: success={0}; available={1}; confidence={2:N3}; text={3}" -f
        $result.Success, $result.Available, $result.Confidence, $result.Text)
    Assert-True $result.Available "The Python worker bridge must be available."
    Assert-True $result.Success "The Python worker bridge OCR failed: $($result.Error)"
    Assert-True $result.Text.Contains("TASK") "The Python worker bridge did not return the recognized text."
}
finally {
    $recognizer.Dispose()
    $bitmap.Dispose()
}

Write-Host "Vision Python worker runtime regression passed."
