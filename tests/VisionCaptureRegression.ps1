param(
    [string]$Configuration = "Debug",
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$resolvedBuildDirectory = if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $null
}
else {
    [System.IO.Path]::GetFullPath($BuildDirectory)
}
$libraryDll = if ($null -eq $resolvedBuildDirectory) {
    Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
}
else {
    Join-Path $resolvedBuildDirectory "WPELibrary.dll"
}
if (-not (Test-Path -LiteralPath $libraryDll)) {
    throw "WPELibrary.dll not found at $libraryDll"
}

function Read-SourceFile {
    param([string]$RelativePath)

    return [System.IO.File]::ReadAllText(
        (Join-Path $repo $RelativePath),
        [System.Text.Encoding]::UTF8)
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function Assert-Contains([string]$text, [string]$expected, [string]$message) {
    if (-not $text.Contains($expected)) {
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

$visionServiceSource = Read-SourceFile "WPELibrary\Lib\Vision\VisionWindowService.cs"
$templateMatcherSource = Read-SourceFile "WPELibrary\Lib\Vision\VisionTemplateMatcher.cs"
$ocrSource = Read-SourceFile "WPELibrary\Lib\Vision\VisionTesseractRecognizer.cs"
$preprocessorSource = Read-SourceFile "WPELibrary\Lib\Vision\VisionImagePreprocessor.cs"
$conditionSource = Read-SourceFile "WPELibrary\Lib\Vision\VisionTextCondition.cs"
$robotCacheSource = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$robotFormSource = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"
Assert-Contains $visionServiceSource "region.FitsWithin(clientSize)" `
    "Vision capture must reject a region outside the current client area."
Assert-Contains $visionServiceSource "CopyFromScreen" `
    "Vision capture must capture only the resolved client region."
Assert-Contains $templateMatcherSource "FindBestMatch" `
    "The vision stage must expose an offline template matching entry point."
Assert-Contains $ocrSource "IVisionTextRecognizer" `
    "The OCR implementation must use a replaceable recognizer interface."
Assert-Contains $preprocessorSource "UseBinaryThreshold" `
    "OCR preprocessing must support binary thresholding."
Assert-Contains $conditionSource "NumberRange" `
    "OCR conditions must support numeric range matching."
Assert-Contains $robotCacheSource "RobotVisionProfile" `
    "Vision profile persistence must use a separate robot profile table."
Assert-Contains $robotFormSource "VisionWindowService.CaptureClientRegion" `
    "The robot editor must use the shared vision capture service."
Assert-Contains $robotFormSource "VisionAssistantRunner.Run" `
    "The robot editor must expose an executable vision assistant runner path."
Assert-Contains $robotFormSource "bStopVisionAssistant_Click" `
    "The robot editor must expose a cancellable vision assistant stop path."

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][System.Reflection.Assembly]::LoadFrom($libraryDll)

$robotInfo = [WPELibrary.Lib.Socket_RobotInfo]::new(
    $true,
    [Guid]::NewGuid(),
    "Vision phase one regression",
    [WPELibrary.Lib.Socket_Cache+Robot]::InitInstructions())
$robotInfo.VisionProfile.WindowHandle = 12345
$robotInfo.VisionProfile.ProcessId = 678
$robotInfo.VisionProfile.ProcessName = "synthetic-target"
$robotInfo.VisionProfile.ProcessPath = "C:\synthetic\synthetic-target.exe"
$robotInfo.VisionProfile.ProcessStartTimeUtcTicks = 638900000000000000
$robotInfo.VisionProfile.WindowTitle = "Vision synthetic target"
$robotInfo.VisionProfile.Region.X = 20
$robotInfo.VisionProfile.Region.Y = 30
$robotInfo.VisionProfile.Region.Width = 40
$robotInfo.VisionProfile.Region.Height = 25
$robotInfo.VisionProfile.Region.UseNormalizedCoordinates = $true
$robotInfo.VisionProfile.Region.ReferenceWidth = 1280
$robotInfo.VisionProfile.Region.ReferenceHeight = 720
$robotInfo.VisionProfile.CaptureSettings.MinimumIntervalMilliseconds = 75
$robotInfo.VisionProfile.CaptureSettings.SkipUnchangedFrames = $false
$robotInfo.VisionProfile.CaptureSettings.HistoryLimit = 9
$robotInfo.VisionProfile.CaptureSettings.SaveFailureSnapshots = $true
$robotInfo.VisionProfile.CaptureSettings.FailureSnapshotDirectory = "C:\synthetic\vision-diagnostics"
$robotInfo.VisionProfile.OcrOptions.ScaleFactor = 3
$robotInfo.VisionProfile.OcrOptions.UseBinaryThreshold = $true
$robotInfo.VisionProfile.OcrOptions.UseAdaptiveThreshold = $true
$robotInfo.VisionProfile.OcrOptions.AdaptiveThresholdWindowSize = 21
$robotInfo.VisionProfile.OcrOptions.AdaptiveThresholdOffset = 6
$robotInfo.VisionProfile.OcrOptions.Invert = $true
$robotInfo.VisionProfile.OcrOptions.UseDenoise = $true
$robotInfo.VisionProfile.OcrOptions.UseSharpen = $true
$robotInfo.VisionProfile.OcrOptions.CharacterWhitelist = "0123456789"
$robotInfo.VisionProfile.OcrCondition.ExpectedText = "任务完成"
$robotInfo.VisionProfile.OcrCondition.MinimumConfidence = 0.8
$visionStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
$visionStep.Name = "等待任务完成"
$visionStep.Condition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
$visionStep.Condition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TemplateAppears
$visionStep.Condition.Region.X = 5
$visionStep.Condition.Region.Y = 6
$visionStep.Condition.Region.Width = 12
$visionStep.Condition.Region.Height = 8
$visionStep.Condition.Region.UseNormalizedCoordinates = $true
$visionStep.Condition.Region.ReferenceWidth = 1280
$visionStep.Condition.Region.ReferenceHeight = 720
$visionStep.Condition.RequiredConfirmations = 2
$visionStep.Condition.Template = [System.Drawing.Bitmap]::new(4, 3)
$visionVariant = [System.Drawing.Bitmap]::new(4, 3)
$visionVariantGraphics = [System.Drawing.Graphics]::FromImage($visionVariant)
try {
    $visionVariantGraphics.Clear([System.Drawing.Color]::Purple)
}
finally {
    $visionVariantGraphics.Dispose()
}
$visionStep.Condition.TemplateVariants.Add($visionVariant)
$stepGraphics = [System.Drawing.Graphics]::FromImage($visionStep.Condition.Template)
try {
    $stepGraphics.Clear([System.Drawing.Color]::Lime)
}
finally {
    $stepGraphics.Dispose()
}
$robotInfo.VisionProfile.AssistantSteps.Add($visionStep)
$robotList = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_RobotInfo]'
$robotList.Add($robotInfo)
$robotXml = [WPELibrary.Lib.Socket_Cache+RobotList]::GetRobotList_XML($robotList)
$visionXml = $robotXml.Element("Robot").Element("VisionProfile")
Assert-True ($null -ne $visionXml) "Robot XML must include the optional vision profile."
Assert-True ($visionXml.Element("Region").Element("Width").Value -eq "40") `
    "Robot XML must preserve the relative vision region."
Assert-True ($visionXml.Element("OcrOptions").Element("ScaleFactor").Value -eq "3") `
    "Robot XML must preserve OCR preprocessing settings."
Assert-True ($visionXml.Element("OcrCondition").Element("ExpectedText").Value -eq "任务完成") `
    "Robot XML must preserve the OCR keyword condition."
Assert-True ($visionXml.Element("AssistantSteps").Element("Step").Element("Condition").Element("TemplatePng").Value.Length -gt 0) `
    "Robot XML must embed assistant template resources."
Assert-True ($visionXml.Element("ProcessPath").Value -eq "C:\synthetic\synthetic-target.exe" -and
    [long]$visionXml.Element("ProcessStartTimeUtcTicks").Value -eq 638900000000000000 -and
    $visionXml.Element("Region").Element("UseNormalizedCoordinates").Value -eq "true" -and
    [int]$visionXml.Element("CaptureSettings").Element("MinimumIntervalMilliseconds").Value -eq 75 -and
    [bool]::Parse($visionXml.Element("CaptureSettings").Element("SaveFailureSnapshots").Value) -and
    $visionXml.Element("CaptureSettings").Element("FailureSnapshotDirectory").Value -eq "C:\synthetic\vision-diagnostics" -and
    [bool]::Parse($visionXml.Element("OcrOptions").Element("UseAdaptiveThreshold").Value) -and
    $visionXml.Element("OcrOptions").Element("CharacterWhitelist").Value -eq "0123456789") `
    "Robot XML must preserve window identity, normalized region, and capture settings."
Assert-True ($visionXml.Element("AssistantSteps").Element("Step").Element("Condition").Element("TemplateVariants").Value.Length -gt 0) `
    "Robot XML must embed template variants."
[WPELibrary.Lib.Socket_Cache+RobotList]::RobotListClear()
$robotDocument = [System.Xml.Linq.XDocument]::new($robotXml)
[WPELibrary.Lib.Socket_Cache+RobotList]::LoadRobotList_FromXDocument($robotDocument)
$importedRobot = [WPELibrary.Lib.Socket_Cache+RobotList]::lstRobot[0]
try {
    Assert-True ($importedRobot.VisionProfile.Region.Height -eq 25) `
        "Robot XML import must restore the vision region."
    Assert-True ($importedRobot.VisionProfile.OcrOptions.ScaleFactor -eq 3) `
        "Robot XML import must restore OCR preprocessing settings."
    Assert-True ($importedRobot.VisionProfile.OcrCondition.ExpectedText -eq "任务完成") `
        "Robot XML import must restore the OCR keyword condition."
    Assert-True ($importedRobot.VisionProfile.AssistantSteps.Count -eq 1 -and
        $importedRobot.VisionProfile.AssistantSteps[0].Condition.Template.Width -eq 4) `
        "Robot XML import must restore assistant steps and template resources."
    Assert-True ($importedRobot.VisionProfile.ProcessPath -eq "C:\synthetic\synthetic-target.exe" -and
        $importedRobot.VisionProfile.ProcessStartTimeUtcTicks -eq 638900000000000000 -and
        $importedRobot.VisionProfile.Region.UseNormalizedCoordinates -and
        $importedRobot.VisionProfile.CaptureSettings.MinimumIntervalMilliseconds -eq 75 -and
        -not $importedRobot.VisionProfile.CaptureSettings.SkipUnchangedFrames -and
        $importedRobot.VisionProfile.CaptureSettings.SaveFailureSnapshots -and
        $importedRobot.VisionProfile.CaptureSettings.FailureSnapshotDirectory -eq "C:\synthetic\vision-diagnostics" -and
        $importedRobot.VisionProfile.OcrOptions.UseAdaptiveThreshold -and
        $importedRobot.VisionProfile.OcrOptions.CharacterWhitelist -eq "0123456789") `
        "Robot XML import must restore window identity, normalized region, and capture settings."
    Assert-True ($importedRobot.VisionProfile.AssistantSteps[0].Condition.Region.UseNormalizedCoordinates -and
        $importedRobot.VisionProfile.AssistantSteps[0].Condition.TemplateVariants.Count -eq 1) `
        "Robot XML import must restore normalized condition regions and template variants."
}
finally {
    if ($importedRobot.VisionProfile.AssistantSteps.Count -gt 0 -and
        $null -ne $importedRobot.VisionProfile.AssistantSteps[0].Condition.Template) {
        $importedRobot.VisionProfile.AssistantSteps[0].Condition.Template.Dispose()
    }
    [WPELibrary.Lib.Socket_Cache+RobotList]::RobotListClear()
}
$robotForm = [WPELibrary.Socket_RobotForm]::new($robotInfo)
try {
    $instructionTabs = Get-PrivateField $robotForm "tcRobotInstruction"
    $visionTab = $instructionTabs.TabPages["tpInstruction_Vision"]
    Assert-True ($null -ne $visionTab) "The robot editor must expose a vision tab."
    Assert-True ($visionTab.Text -ne "Vision_Tab") "The vision tab must resolve localized text."
    $visionWindows = Get-PrivateField $robotForm "cbbVisionWindows"
    $visionPreview = Get-PrivateField $robotForm "pbVisionPreview"
    $visionTemplate = Get-PrivateField $robotForm "pbVisionTemplate"
    $visionThreshold = Get-PrivateField $robotForm "nudVisionThreshold"
    $visionMatchStatus = Get-PrivateField $robotForm "lVisionMatchStatus"
    $visionOcrScale = Get-PrivateField $robotForm "nudVisionOcrScale"
    $visionOcrKeyword = Get-PrivateField $robotForm "txtVisionOcrKeyword"
    $visionOcrStatus = Get-PrivateField $robotForm "lVisionOcrStatus"
    $visionAssistantStatus = Get-PrivateField $robotForm "lVisionAssistantStatus"
    $visionRunSteps = Get-PrivateField $robotForm "bVisionRunSteps"
    $visionStopSteps = Get-PrivateField $robotForm "bVisionStopSteps"
    $visionConditionType = Get-PrivateField $robotForm "cbbVisionConditionType"
    $visionConfirmations = Get-PrivateField $robotForm "nudVisionConfirmations"
    $visionPollInterval = Get-PrivateField $robotForm "nudVisionPollInterval"
    $visionTimeout = Get-PrivateField $robotForm "nudVisionTimeout"
    $visionRetries = Get-PrivateField $robotForm "nudVisionRetries"
    $visionFailurePolicy = Get-PrivateField $robotForm "cbbVisionFailurePolicy"
    Assert-True ($null -ne $visionWindows -and $null -ne $visionPreview -and
        $null -ne $visionTemplate -and $null -ne $visionThreshold -and
        $null -ne $visionMatchStatus -and $null -ne $visionOcrScale -and
        $null -ne $visionOcrKeyword -and $null -ne $visionOcrStatus -and
        $null -ne $visionAssistantStatus -and $null -ne $visionRunSteps -and
        $null -ne $visionStopSteps -and $visionStopSteps.Enabled -eq $false -and
        $null -ne $visionConditionType -and $visionConditionType.Items.Count -eq 7 -and
        $null -ne $visionConfirmations -and $null -ne $visionPollInterval -and
        $null -ne $visionTimeout -and $null -ne $visionRetries -and
        $null -ne $visionFailurePolicy) `
        "The vision tab must contain target, condition, timing, runner, and stop controls."
}
finally {
    $robotForm.Close()
    $robotForm.Dispose()
}
if ($robotInfo.VisionProfile.AssistantSteps.Count -gt 0 -and
    $null -ne $robotInfo.VisionProfile.AssistantSteps[0].Condition.Template) {
    $robotInfo.VisionProfile.AssistantSteps[0].Condition.Template.Dispose()
}
if ($robotInfo.VisionProfile.AssistantSteps.Count -gt 0 -and
    $robotInfo.VisionProfile.AssistantSteps[0].Condition.TemplateVariants.Count -gt 0) {
    $robotInfo.VisionProfile.AssistantSteps[0].Condition.DisposeTemplateVariants()
}

$validRegion = [WPELibrary.Lib.Vision.VisionRegion]::new()
$validRegion.X = 20
$validRegion.Y = 30
$validRegion.Width = 40
$validRegion.Height = 25
Assert-True $validRegion.IsValid "A positive region must be valid."
Assert-True (
    $validRegion.FitsWithin([System.Drawing.Size]::new(320, 180))
) "A region inside the client area must be accepted."
Assert-True (-not $validRegion.FitsWithin([System.Drawing.Size]::new(50, 50))) `
    "A region outside the client area must be rejected."
$normalizedRegion = [WPELibrary.Lib.Vision.VisionRegion]::new()
$normalizedRegion.X = 10
$normalizedRegion.Y = 15
$normalizedRegion.Width = 20
$normalizedRegion.Height = 12
$normalizedRegion.UseNormalizedCoordinates = $true
$normalizedRegion.ReferenceWidth = 160
$normalizedRegion.ReferenceHeight = 90
$resolvedNormalizedRegion = $normalizedRegion.Resolve([System.Drawing.Size]::new(320, 180))
Assert-True ($resolvedNormalizedRegion.X -eq 20 -and
    $resolvedNormalizedRegion.Y -eq 30 -and
    $resolvedNormalizedRegion.Width -eq 40 -and
    $resolvedNormalizedRegion.Height -eq 24) `
    "A normalized region must scale from its reference client size."

$source = [System.Drawing.Bitmap]::new(100, 80)
$template = $null
$variantTemplates = $null
$negativeSource = $null
$cancelSource = $null
$scaledSource = $null
$cancellation = $null
try {
    $sourceGraphics = [System.Drawing.Graphics]::FromImage($source)
    try {
        $sourceGraphics.Clear([System.Drawing.Color]::FromArgb(30, 50, 80))
        $sourceGraphics.FillRectangle([System.Drawing.Brushes]::Yellow, 20, 30, 20, 15)
        $sourceGraphics.FillRectangle([System.Drawing.Brushes]::Black, 26, 34, 6, 5)
    }
    finally {
        $sourceGraphics.Dispose()
    }

    $template = $source.Clone(
        [System.Drawing.Rectangle]::new(20, 30, 20, 15),
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $match = [WPELibrary.Lib.Vision.VisionTemplateMatcher]::FindBestMatch(
        $source,
        $template,
        0.99,
        [System.Threading.CancellationToken]::None)
    Assert-True $match.Found "An exact synthetic template must be found."
    Assert-True ($match.Location.X -eq 20 -and $match.Location.Y -eq 30) `
        "The template matcher must return the expected relative location."
    Assert-True ($match.Size.Width -eq 20 -and $match.Size.Height -eq 15) `
        "The template matcher must return the template size."
    Assert-True ($match.Similarity -ge 0.99) `
        "An exact synthetic template must have a high similarity score."
    $negativeSource = [System.Drawing.Bitmap]::new(100, 80)
    $negativeGraphics = [System.Drawing.Graphics]::FromImage($negativeSource)
    try {
        $negativeGraphics.Clear([System.Drawing.Color]::FromArgb(30, 50, 80))
    }
    finally {
        $negativeGraphics.Dispose()
    }
    $variantTemplates = New-Object 'System.Collections.Generic.List[System.Drawing.Bitmap]'
    $variantTemplates.Add($negativeSource)
    $variantTemplates.Add($template)
    $variantMatch = [WPELibrary.Lib.Vision.VisionTemplateMatcher]::FindBestMatch(
        $source,
        $variantTemplates,
        0.99,
        [System.Threading.CancellationToken]::None)
    Assert-True ($variantMatch.Found -and $variantMatch.Location.X -eq 20 -and
        $variantMatch.Location.Y -eq 30) `
        "A template variant set must match the best available variant."

    $scaledSource = [System.Drawing.Bitmap]::new(140, 100)
    $scaledGraphics = [System.Drawing.Graphics]::FromImage($scaledSource)
    try {
        $scaledGraphics.Clear([System.Drawing.Color]::FromArgb(30, 50, 80))
        $scaledGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scaledGraphics.DrawImage($template, [System.Drawing.Rectangle]::new(55, 25, 30, 23))
    }
    finally {
        $scaledGraphics.Dispose()
    }
    $scaleOptions = [WPELibrary.Lib.Vision.VisionTemplateMatchOptions]::new()
    $scaleOptions.NormalizeBrightness = $true
    $scaleOptions.AllowScaleVariation = $true
    $scaleOptions.MinimumScale = 1.45
    $scaleOptions.MaximumScale = 1.55
    $scaleOptions.ScaleStep = 0.05
    $scaledMatch = [WPELibrary.Lib.Vision.VisionTemplateMatcher]::FindBestMatch(
        $scaledSource,
        $template,
        0.8,
        $scaleOptions,
        [System.Threading.CancellationToken]::None)
    Assert-True ($scaledMatch.Found -and $scaledMatch.Location.X -ge 53 -and
        $scaledMatch.Location.X -le 57 -and $scaledMatch.Location.Y -ge 23 -and
        $scaledMatch.Location.Y -le 27) `
        "Template scale variation must find a synthetic resized marker."
    $negativeMatch = [WPELibrary.Lib.Vision.VisionTemplateMatcher]::FindBestMatch(
        $negativeSource,
        $template,
        0.99,
        [System.Threading.CancellationToken]::None)
    Assert-True (-not $negativeMatch.Found) `
        "A synthetic negative image must not pass the high template threshold."

    $cancellation = [System.Threading.CancellationTokenSource]::new()
    $cancellation.Cancel()
    $cancelSource = [WPELibrary.Lib.Vision.VisionTemplateMatcher]::FindBestMatch(
        $source,
        $template,
        0.99,
        $cancellation.Token)
    Assert-True $cancelSource.Cancelled "A cancelled template match must report cancellation."

    $ocrOptions = [WPELibrary.Lib.Vision.VisionOcrOptions]::new()
    $ocrOptions.ScaleFactor = 2
    $ocrOptions.UseBinaryThreshold = $true
    $ocrOptions.BinaryThreshold = 100
    $ocrOptions.UseAdaptiveThreshold = $true
    $ocrOptions.AdaptiveThresholdWindowSize = 15
    $ocrOptions.AdaptiveThresholdOffset = 8
    $ocrOptions.Invert = $true
    $ocrOptions.UseDenoise = $true
    $ocrOptions.UseSharpen = $true
    $prepared = [WPELibrary.Lib.Vision.VisionImagePreprocessor]::Preprocess(
        $source,
        $ocrOptions,
        [System.Threading.CancellationToken]::None)
    try {
        Assert-True ($prepared.Width -eq 200 -and $prepared.Height -eq 160) `
            "OCR preprocessing must apply the configured scale factor."
        $preparedPixel = $prepared.GetPixel(45, 65)
        Assert-True ($preparedPixel.R -eq $preparedPixel.G -and $preparedPixel.G -eq $preparedPixel.B) `
            "OCR preprocessing must produce grayscale pixels when configured."
    }
    finally {
        $prepared.Dispose()
    }

    $advancedPrepared = [WPELibrary.Lib.Vision.VisionImagePreprocessor]::Preprocess(
        $source,
        $ocrOptions,
        [System.Threading.CancellationToken]::None)
    try {
        Assert-True ($advancedPrepared.Width -eq 200 -and $advancedPrepared.Height -eq 160) `
            "Advanced OCR preprocessing must preserve configured output dimensions."
        $advancedPixel = $advancedPrepared.GetPixel(0, 0)
        Assert-True ($advancedPixel.R -eq $advancedPixel.G -and $advancedPixel.G -eq $advancedPixel.B) `
            "Advanced OCR preprocessing must keep the output grayscale."
    }
    finally {
        $advancedPrepared.Dispose()
    }

    $ocrResult = [WPELibrary.Lib.Vision.VisionOcrResult]::Succeeded("任务完成 42", 0.95)
    $textCondition = [WPELibrary.Lib.Vision.VisionTextCondition]::new()
    $textCondition.ExpectedText = "任务完成"
    [string]$conditionReason = ""
    Assert-True ($textCondition.Matches($ocrResult, [ref]$conditionReason)) `
        "An OCR contains condition must match a recognized keyword."
    $spacedOcrResult = [WPELibrary.Lib.Vision.VisionOcrResult]::Succeeded("任务 完成", 0.95)
    Assert-True ($textCondition.Matches($spacedOcrResult, [ref]$conditionReason)) `
        "OCR keyword matching must ignore whitespace inserted between recognized words."
    $numberCondition = [WPELibrary.Lib.Vision.VisionTextCondition]::new()
    $numberCondition.MatchMode = [WPELibrary.Lib.Vision.VisionTextMatchMode]::NumberRange
    $numberCondition.MinimumNumber = 40
    $numberCondition.MaximumNumber = 45
    Assert-True ($numberCondition.Matches($ocrResult, [ref]$conditionReason)) `
        "An OCR numeric condition must match a number in range."

    $disappearanceCondition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $disappearanceCondition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TextDisappears
    $disappearanceCondition.TextCondition.ExpectedText = "expected text"
    $noTextOcrResult = [WPELibrary.Lib.Vision.VisionOcrResult]::Failed(
        "Tesseract returned no text.",
        "",
        0,
        0)
    $disappearanceMatched = [WPELibrary.Lib.Vision.VisionConditionEvaluator]::Matches(
        $disappearanceCondition,
        [WPELibrary.Lib.Vision.VisionObservation]::FromOcr($noTextOcrResult),
        [ref]$conditionReason)
    Assert-True $disappearanceMatched `
        "A usable OCR no-text result must satisfy a text-disappeared condition."

    $missingTesseract = [WPELibrary.Lib.Vision.VisionTesseractRecognizer]::new(
        "__missing_tesseract_for_regression__.exe")
    $missingOptions = $ocrOptions.Clone()
    $missingOptions.ExecutablePath = "C:\__missing_tesseract_for_regression__\tesseract.exe"
    $unavailableOcr = $missingTesseract.Recognize(
        $source,
        $missingOptions,
        [System.Threading.CancellationToken]::None)
    Assert-True (-not $unavailableOcr.Available -and -not $unavailableOcr.Success) `
        "Missing Tesseract must produce an explicit unavailable OCR result."
}
finally {
    if ($null -ne $cancellation) { $cancellation.Dispose() }
    if ($null -ne $negativeSource) { $negativeSource.Dispose() }
    if ($null -ne $scaledSource) { $scaledSource.Dispose() }
    if ($null -ne $template) { $template.Dispose() }
    $source.Dispose()
}

$form = [System.Windows.Forms.Form]::new()
$marker = [System.Windows.Forms.Panel]::new()
try {
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = [System.Drawing.Point]::new(80, 80)
    $form.ClientSize = [System.Drawing.Size]::new(320, 180)
    $form.ShowInTaskbar = $false
    $form.TopMost = $true
    $form.Text = "Vision synthetic target"
    $form.BackColor = [System.Drawing.Color]::FromArgb(30, 90, 140)

    $marker.Location = [System.Drawing.Point]::new(20, 30)
    $marker.Size = [System.Drawing.Size]::new(40, 25)
    $marker.BackColor = [System.Drawing.Color]::Yellow
    $form.Controls.Add($marker)
    $form.Show()
    $form.BringToFront()
    $form.Activate()
    [System.Windows.Forms.Application]::DoEvents()

    $clientBounds = [System.Drawing.Rectangle]::Empty
    $clientSize = [System.Drawing.Size]::Empty
    $resolved = [WPELibrary.Lib.Vision.VisionWindowService]::TryGetClientBounds(
        $form.Handle,
        [ref]$clientBounds,
        [ref]$clientSize)
    Assert-True $resolved "A visible synthetic window must expose its client bounds."
    Assert-True ($clientSize.Width -eq 320 -and $clientSize.Height -eq 180) `
        "The resolved client size must match the synthetic window."

        $firstCaptureSettings = [WPELibrary.Lib.Vision.VisionCaptureSettings]::new()
        $firstCaptureSettings.SourceMode = [WPELibrary.Lib.Vision.VisionCaptureSourceMode]::WindowRender
        $firstCaptureResult = [WPELibrary.Lib.Vision.VisionWindowService]::CaptureClientRegionDetailed(
            $form.Handle,
            $validRegion,
            $firstCaptureSettings)
    try {
        Assert-True ($firstCaptureResult.Image.Width -eq 40 -and $firstCaptureResult.Image.Height -eq 25) `
            "The capture must preserve the configured region size."
        $firstPixel = $firstCaptureResult.Image.GetPixel(20, 12)
        Assert-True ($firstPixel.R -gt 200 -and $firstPixel.G -gt 200) `
            "The capture must contain the marker inside the client region."
    }
    finally {
        $firstCaptureResult.Dispose()
    }

    $firstLocation = $clientBounds.Location
    $form.Location = [System.Drawing.Point]::new(180, 140)
    [System.Windows.Forms.Application]::DoEvents()
    $movedBounds = [System.Drawing.Rectangle]::Empty
    $movedSize = [System.Drawing.Size]::Empty
    $moved = [WPELibrary.Lib.Vision.VisionWindowService]::TryGetClientBounds(
        $form.Handle,
        [ref]$movedBounds,
        [ref]$movedSize)
    Assert-True $moved "The moved synthetic window must still expose its client bounds."
    Assert-True ($movedBounds.Location -ne $firstLocation) `
        "Moving the window must change the resolved screen origin."
    $movedCaptureResult = [WPELibrary.Lib.Vision.VisionWindowService]::CaptureClientRegionDetailed(
        $form.Handle,
        $validRegion,
        $firstCaptureSettings)
    try {
        $movedPixel = $movedCaptureResult.Image.GetPixel(20, 12)
        Assert-True ($movedPixel.R -gt 200 -and $movedPixel.G -gt 200) `
            "A moved window must still capture the same relative client region."
    }
    finally {
        $movedCaptureResult.Dispose()
    }

    $captureSettings = [WPELibrary.Lib.Vision.VisionCaptureSettings]::new()
    $form.BringToFront()
    $form.Activate()
    [System.Windows.Forms.Application]::DoEvents()
    $captureSettings.SourceMode = [WPELibrary.Lib.Vision.VisionCaptureSourceMode]::Screen
    $detailedCapture = $null
    $detailedCaptureAvailable = $false
    try {
        $detailedCapture = [WPELibrary.Lib.Vision.VisionWindowService]::CaptureClientRegionDetailed(
            $form.Handle,
            $normalizedRegion,
            $captureSettings)
    }
    catch [System.InvalidOperationException] {
        if ($_.Exception.Message -notlike '*could not be captured*') {
            throw
        }
        Write-Warning 'Detailed screen capture was unavailable in this desktop test environment; skipping live-screen diagnostics.'
    }
    if ($null -ne $detailedCapture) {
        try {
            Assert-True ($detailedCapture.Image.Width -eq 40 -and $detailedCapture.Image.Height -eq 24) `
                "Detailed capture must apply normalized region dimensions."
            Assert-True ($detailedCapture.MeanBrightness -gt 0 -and $detailedCapture.Contrast -gt 0 -and
                $detailedCapture.Fingerprint -ne 0) `
                "Detailed capture must expose non-empty quality diagnostics and a frame fingerprint."
            $detailedCaptureAvailable = $true
        }
        finally {
            $detailedCapture.Dispose()
        }
    }

    if ($detailedCaptureAvailable) {
        $form.ClientSize = [System.Drawing.Size]::new(640, 360)
        [System.Windows.Forms.Application]::DoEvents()
        $scaledDetailedCapture = [WPELibrary.Lib.Vision.VisionWindowService]::CaptureClientRegionDetailed(
            $form.Handle,
            $normalizedRegion,
            $captureSettings)
        try {
            Assert-True ($scaledDetailedCapture.Image.Width -eq 80 -and
                $scaledDetailedCapture.Image.Height -eq 48) `
                "A normalized capture region must follow client-area resizing for DPI/scale changes."
        }
        finally {
            $scaledDetailedCapture.Dispose()
        }
    }
}
finally {
    $form.Close()
    $marker.Dispose()
    $form.Dispose()
}

Write-Output "Vision capture regression passed."
