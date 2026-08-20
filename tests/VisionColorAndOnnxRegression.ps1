param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$runtimeDirectory = Join-Path $repo "WinsockPacketEditor\bin\$Configuration"
$libraryDll = Join-Path $runtimeDirectory "WPELibrary.dll"

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

if (-not (Test-Path -LiteralPath $libraryDll)) {
    throw "WPELibrary.dll not found at $libraryDll"
}

Add-Type -AssemblyName System.Drawing
[void][System.Reflection.Assembly]::LoadFrom($libraryDll)

$bitmap = [System.Drawing.Bitmap]::new(32, 24)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$onnx = $null
$auto = $null
try {
    $graphics.Clear([System.Drawing.Color]::Black)
    $graphics.FillRectangle([System.Drawing.Brushes]::Red, 4, 5, 12, 8)

    $color = [WPELibrary.Lib.Vision.VisionColorCondition]::new()
    $color.TargetColor = [System.Drawing.Color]::Red
    $color.Tolerance = 0
    $color.MinimumPixelCount = 90
    $color.MinimumMatchRatio = 0.1
    $match = [WPELibrary.Lib.Vision.VisionColorMatcher]::Find(
        $bitmap,
        $color,
        [System.Threading.CancellationToken]::None)
    Assert-True $match.Found "A synthetic red block must satisfy the color matcher."
    Assert-True ($match.MatchCount -ge 90 -and $match.MatchRatio -ge 0.1) `
        "Color match count and ratio must be reported accurately."
    Assert-True (-not $match.Bounds.IsEmpty) "A color match must expose its bounds."

    $appears = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $appears.Type = [WPELibrary.Lib.Vision.VisionConditionType]::ColorAppears
    $appears.Region.Width = 32
    $appears.Region.Height = 24
    $appears.ColorCondition = $color.Clone()
    [string]$reason = ""
    $appearsMatched = [WPELibrary.Lib.Vision.VisionConditionEvaluator]::Matches(
        $appears,
        [WPELibrary.Lib.Vision.VisionObservation]::FromColor($match),
        [ref]$reason)
    Assert-True $appearsMatched "A found color must satisfy ColorAppears."

    $disappears = $appears.Clone()
    $disappears.Type = [WPELibrary.Lib.Vision.VisionConditionType]::ColorDisappears
    $disappearsMatched = [WPELibrary.Lib.Vision.VisionConditionEvaluator]::Matches(
        $disappears,
        [WPELibrary.Lib.Vision.VisionObservation]::FromColor($match),
        [ref]$reason)
    Assert-True (-not $disappearsMatched) "A found color must not satisfy ColorDisappears."

    $graphics.Clear([System.Drawing.Color]::Black)
    $missingMatch = [WPELibrary.Lib.Vision.VisionColorMatcher]::Find(
        $bitmap,
        $color,
        [System.Threading.CancellationToken]::None)
    $disappearedMatched = [WPELibrary.Lib.Vision.VisionConditionEvaluator]::Matches(
        $disappears,
        [WPELibrary.Lib.Vision.VisionObservation]::FromColor($missingMatch),
        [ref]$reason)
    Assert-True $disappearedMatched "A missing color must satisfy ColorDisappears."

    $cancelSource = [System.Threading.CancellationTokenSource]::new()
    try {
        $cancelSource.Cancel()
        $cancelled = [WPELibrary.Lib.Vision.VisionColorMatcher]::Find(
            $bitmap,
            $color,
            $cancelSource.Token)
        Assert-True $cancelled.Cancelled "Color matching must honor cancellation."
    }
    finally {
        $cancelSource.Dispose()
    }

    $onnxOptions = [WPELibrary.Lib.Vision.VisionOcrOptions]::new()
    $onnxOptions.Engine = [WPELibrary.Lib.Vision.VisionOcrEngine]::Onnx
    $onnxOptions.OnnxModelDirectory = "__missing_onnx_ocr_model_set__"
    $onnxStatus = [WPELibrary.Lib.Vision.VisionOnnxTextRecognizer]::DescribeModelDirectory(
        $onnxOptions.OnnxModelDirectory)
    Assert-True ($onnxStatus.Contains("ONNX model directory was not found")) `
        "ONNX OCR must report a missing external model set without inventing a ready state."
    $onnx = [WPELibrary.Lib.Vision.VisionOnnxTextRecognizer]::new()
    $auto = [WPELibrary.Lib.Vision.VisionAutoTextRecognizer]::new(
        $onnx,
        [WPELibrary.Lib.Vision.VisionTesseractRecognizer]::new("Z:\\wpe-missing-tesseract\\tesseract.exe"))
    Assert-True ($null -ne $auto) "Automatic OCR provider must be constructible with external providers."

    Write-Output "Vision color and ONNX regression passed."
}
finally {
    if ($null -ne $auto) { $auto.Dispose() }
    if ($null -ne $onnx) { $onnx.Dispose() }
    $graphics.Dispose()
    $bitmap.Dispose()
}
