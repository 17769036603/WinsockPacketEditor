param(
    [string]$Configuration = "Release",
    [string]$TesseractPath = "C:\Program Files\Tesseract-OCR\tesseract.exe",
    [string]$DatasetDirectory = "",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$libraryDll = Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
$resolvedOutput = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo "WPELibrary\work\VisionAccuracyReplay"
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
[void][System.Reflection.Assembly]::LoadFrom($libraryDll)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$generatedDataset = [string]::IsNullOrWhiteSpace($DatasetDirectory)
$dataset = if ($generatedDataset) {
    Join-Path $resolvedOutput "synthetic-dataset"
}
else {
    [System.IO.Path]::GetFullPath($DatasetDirectory)
}
[System.IO.Directory]::CreateDirectory($dataset) | Out-Null

$cases = New-Object 'System.Collections.Generic.List[object]'
$generatedFiles = New-Object 'System.Collections.Generic.List[string]'

function Save-Bitmap([System.Drawing.Bitmap]$bitmap, [string]$path) {
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $script:generatedFiles.Add($path)
}

if ($generatedDataset) {
    $template = [System.Drawing.Bitmap]::new(32, 24)
    $templateGraphics = [System.Drawing.Graphics]::FromImage($template)
    try {
        $templateGraphics.Clear([System.Drawing.Color]::FromArgb(35, 55, 85))
        $templateGraphics.FillRectangle([System.Drawing.Brushes]::Gold, 4, 3, 24, 18)
        $templateGraphics.FillRectangle([System.Drawing.Brushes]::Black, 10, 7, 8, 7)
    }
    finally {
        $templateGraphics.Dispose()
    }
    $templatePath = Join-Path $dataset "template.png"
    Save-Bitmap $template $templatePath

    $source = [System.Drawing.Bitmap]::new(160, 100)
    $sourceGraphics = [System.Drawing.Graphics]::FromImage($source)
    try {
        $sourceGraphics.Clear([System.Drawing.Color]::FromArgb(35, 55, 85))
        $sourceGraphics.DrawImage($template, [System.Drawing.Rectangle]::new(48, 34, 32, 24))
    }
    finally {
        $sourceGraphics.Dispose()
    }
    $sourcePath = Join-Path $dataset "template-source.png"
    Save-Bitmap $source $sourcePath
    $cases.Add([pscustomobject]@{
        Name = "synthetic-template"
        Kind = "Template"
        Source = $sourcePath
        Template = $templatePath
        ExpectedFound = $true
        MinimumSimilarity = 0.9
        NormalizeBrightness = $true
        AllowScaleVariation = $false
        MinimumScale = 0.9
        MaximumScale = 1.1
        ScaleStep = 0.05
    })

    $scaledSource = [System.Drawing.Bitmap]::new(190, 120)
    $scaledGraphics = [System.Drawing.Graphics]::FromImage($scaledSource)
    try {
        $scaledGraphics.Clear([System.Drawing.Color]::FromArgb(35, 55, 85))
        $scaledGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scaledGraphics.DrawImage($template, [System.Drawing.Rectangle]::new(70, 40, 48, 36))
    }
    finally {
        $scaledGraphics.Dispose()
    }
    $scaledSourcePath = Join-Path $dataset "template-scale-source.png"
    Save-Bitmap $scaledSource $scaledSourcePath
    $cases.Add([pscustomobject]@{
        Name = "synthetic-template-scale"
        Kind = "Template"
        Source = $scaledSourcePath
        Template = $templatePath
        ExpectedFound = $true
        MinimumSimilarity = 0.8
        NormalizeBrightness = $true
        AllowScaleVariation = $true
        MinimumScale = 1.45
        MaximumScale = 1.55
        ScaleStep = 0.05
    })

    $ocrSource = [System.Drawing.Bitmap]::new(520, 150)
    $ocrGraphics = [System.Drawing.Graphics]::FromImage($ocrSource)
    $ocrFont = [System.Drawing.Font]::new("Arial", 54, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    try {
        $ocrGraphics.Clear([System.Drawing.Color]::White)
        $ocrGraphics.DrawString("TASK 42", $ocrFont, [System.Drawing.Brushes]::Black, 24, 28)
    }
    finally {
        $ocrFont.Dispose()
        $ocrGraphics.Dispose()
    }
    $ocrSourcePath = Join-Path $dataset "ocr-source.png"
    Save-Bitmap $ocrSource $ocrSourcePath
    if (Test-Path -LiteralPath $TesseractPath) {
        $cases.Add([pscustomobject]@{
            Name = "synthetic-ocr"
            Kind = "Ocr"
            Source = $ocrSourcePath
            ExpectedText = "TASK 42"
            MinimumConfidence = 0.5
            TesseractPath = [System.IO.Path]::GetFullPath($TesseractPath)
        })
    }
    $template.Dispose()
    $source.Dispose()
    $scaledSource.Dispose()
    $ocrSource.Dispose()
}
else {
    $manifestPath = Join-Path $dataset "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "DatasetDirectory must contain manifest.json. See tests/VisionAccuracyReplay.ps1 for the supported case fields."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($case in @($manifest.cases)) {
        if ([string]::IsNullOrWhiteSpace($case.Source)) {
            throw "Every replay case needs a Source path."
        }
        $case.Source = [System.IO.Path]::GetFullPath((Join-Path $dataset $case.Source))
        if ($case.Template) {
            $case.Template = [System.IO.Path]::GetFullPath((Join-Path $dataset $case.Template))
        }
        $cases.Add($case)
    }
}

$recognizerCache = @{}
$results = New-Object 'System.Collections.Generic.List[object]'
$matcherCancellation = [System.Threading.CancellationToken]::None
foreach ($case in $cases) {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $passed = $false
    $actualText = ""
    $actualConfidence = 0D
    $actualFound = $false
    $similarity = 0D
    $caseError = ""
    $sourceBitmap = $null
    $templateBitmap = $null
    try {
        $sourceBitmap = [System.Drawing.Bitmap]::new($case.Source)
        if ([string]::Equals($case.Kind, "Template", [System.StringComparison]::OrdinalIgnoreCase)) {
            $templateBitmap = [System.Drawing.Bitmap]::new($case.Template)
            $options = [WPELibrary.Lib.Vision.VisionTemplateMatchOptions]::new()
            $options.NormalizeBrightness = if ($null -eq $case.NormalizeBrightness) { $true } else { [bool]$case.NormalizeBrightness }
            $options.AllowScaleVariation = [bool]$case.AllowScaleVariation
            $options.MinimumScale = if ($case.MinimumScale) { [double]$case.MinimumScale } else { 0.9D }
            $options.MaximumScale = if ($case.MaximumScale) { [double]$case.MaximumScale } else { 1.1D }
            $options.ScaleStep = if ($case.ScaleStep) { [double]$case.ScaleStep } else { 0.05D }
            $match = [WPELibrary.Lib.Vision.VisionTemplateMatcher]::FindBestMatch(
                $sourceBitmap,
                $templateBitmap,
                [double]$case.MinimumSimilarity,
                $options,
                $matcherCancellation)
            $actualFound = $match.Found
            $similarity = $match.Similarity
            $expectedFound = if ($null -eq $case.ExpectedFound) { $true } else { [bool]$case.ExpectedFound }
            $passed = ($actualFound -eq $expectedFound)
        }
        elseif ([string]::Equals($case.Kind, "Ocr", [System.StringComparison]::OrdinalIgnoreCase)) {
            $path = if ($case.TesseractPath) { [System.IO.Path]::GetFullPath($case.TesseractPath) } else { [System.IO.Path]::GetFullPath($TesseractPath) }
            if (-not $recognizerCache.ContainsKey($path)) {
                $recognizerCache[$path] = [WPELibrary.Lib.Vision.VisionTesseractRecognizer]::new($path)
            }
            $ocrOptions = [WPELibrary.Lib.Vision.VisionOcrOptions]::new()
            $ocrOptions.ExecutablePath = $path
            $ocrOptions.Language = if ($case.Language) { [string]$case.Language } else { "eng" }
            $ocrOptions.PageSegmentationMode = if ($case.PageSegmentationMode) { [int]$case.PageSegmentationMode } else { 6 }
            $ocrOptions.TimeoutMilliseconds = 20000
            $ocr = $recognizerCache[$path].Recognize(
                $sourceBitmap,
                $ocrOptions,
                [System.Threading.CancellationToken]::None)
            $actualText = $ocr.Text
            $actualConfidence = $ocr.Confidence
            $normalizedActual = ($actualText -replace "\s+", "").ToUpperInvariant()
            $normalizedExpected = (([string]$case.ExpectedText) -replace "\s+", "").ToUpperInvariant()
            $passed = $ocr.Success -and $normalizedActual.Contains($normalizedExpected) -and
                $actualConfidence -ge [double]$case.MinimumConfidence
        }
        else {
            throw "Unsupported replay case kind: $($case.Kind)"
        }
    }
    catch {
        $caseError = $_.Exception.Message
        $passed = $false
    }
    finally {
        if ($null -ne $templateBitmap) { $templateBitmap.Dispose() }
        if ($null -ne $sourceBitmap) { $sourceBitmap.Dispose() }
        $stopwatch.Stop()
    }
    $results.Add([pscustomobject]@{
        Name = [string]$case.Name
        Kind = [string]$case.Kind
        Passed = $passed
        ElapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        Found = $actualFound
        Similarity = $similarity
        Text = $actualText
        Confidence = $actualConfidence
        Error = $caseError
    })
}

$eligible = @($results.ToArray())
$passedCount = @($eligible | Where-Object { $_.Passed }).Count
$report = [pscustomobject]@{
    GeneratedAt = [DateTime]::Now.ToString("o")
    Dataset = $dataset
    DatasetType = if ($generatedDataset) { "self-owned synthetic" } else { "caller supplied" }
    CaseCount = $eligible.Count
    PassedCount = $passedCount
    FailedCount = $eligible.Count - $passedCount
    Accuracy = if ($eligible.Count -eq 0) { 0D } else { [Math]::Round($passedCount / [double]$eligible.Count, 4) }
    Cases = $eligible
}
$reportPath = Join-Path $resolvedOutput "accuracy-report.json"
[System.IO.File]::WriteAllText(
    $reportPath,
    ($report | ConvertTo-Json -Depth 6),
    [System.Text.Encoding]::UTF8)

Write-Output ("Vision accuracy replay: {0}/{1} passed; accuracy={2:P1}; report={3}" -f
    $passedCount,
    $eligible.Count,
    $report.Accuracy,
    $reportPath)
if ($passedCount -ne $eligible.Count) {
    exit 1
}
