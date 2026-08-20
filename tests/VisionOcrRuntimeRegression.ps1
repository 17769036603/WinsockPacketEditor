param(
    [string]$Configuration = "Debug",
    [string]$TesseractPath = "C:\Program Files\Tesseract-OCR\tesseract.exe",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo "WPELibrary\work\Plan2VisionOcrRuntime"
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

function New-TextBitmap {
    param(
        [string[]]$Lines,
        [int]$Width = 900,
        [int]$Height = 240
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $font = [System.Drawing.Font]::new(
        "Microsoft YaHei",
        42,
        [System.Drawing.FontStyle]::Bold,
        [System.Drawing.GraphicsUnit]::Pixel)
    try {
        $graphics.Clear([System.Drawing.Color]::White)
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $y = [single]22
        foreach ($line in $Lines) {
            $graphics.DrawString(
                $line,
                $font,
                [System.Drawing.Brushes]::Black,
                [System.Drawing.PointF]::new([single]24, [single]$y))
            $y += [single]82
        }
    }
    finally {
        $font.Dispose()
        $graphics.Dispose()
    }
    return $bitmap
}

function Invoke-ProjectOcr {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [string]$Name
    )

    $options = [WPELibrary.Lib.Vision.VisionOcrOptions]::new()
    $options.ExecutablePath = $TesseractPath
    $options.TessdataPath = $tessdataPath
    $options.TimeoutMilliseconds = 20000
    $recognizer = [WPELibrary.Lib.Vision.VisionTesseractRecognizer]::new($TesseractPath)
    $defaultRecognizer = [WPELibrary.Lib.Vision.VisionTesseractRecognizer]::new("tesseract.exe")
    Assert-True $defaultRecognizer.IsAvailable "The default tesseract.exe path must discover the standard installation directory."
    $result = $recognizer.Recognize(
        $Bitmap,
        $options,
        [System.Threading.CancellationToken]::None)
    Write-Host ("{0}: success={1}; available={2}; confidence={3:N3}; text={4}; error={5}" -f
        $Name,
        $result.Success,
        $result.Available,
        $result.Confidence,
        $result.Text,
        $result.Error)
    Assert-True $result.Available "$Name must find the installed Tesseract runtime."
    Assert-True $result.Success "$Name OCR failed: $($result.Error)"
    Assert-True ($result.Confidence -gt 0.1D) "$Name confidence is unexpectedly low."
    return $result
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
Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$expectedTask = [string]::new([char[]](0x4EFB, 0x52A1, 0x5B8C, 0x6210))
$expectedCountLabel = [string]::new([char[]](0x6570, 0x91CF))
$bitmap = New-TextBitmap @($expectedTask, "$expectedCountLabel 42")
try {
    $imagePath = Join-Path $resolvedOutput "01-chinese-number.png"
    $bitmap.Save($imagePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $result = Invoke-ProjectOcr -Bitmap $bitmap -Name "ChineseNumber"
    Assert-True ($result.Text.Contains($expectedTask.Substring(0, 2))) "OCR output must contain the Chinese keyword for task."
    Assert-True ($result.Text.Contains($expectedTask.Substring(2, 2))) "OCR output must contain the Chinese keyword for completion."
    Assert-True ($result.Text.Contains("42")) "OCR output must contain the numeric value 42."
}
finally {
    $bitmap.Dispose()
}

Write-Host "Vision OCR runtime regression passed. Screenshot: $imagePath"
