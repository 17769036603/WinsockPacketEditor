param(
    [string]$Configuration = "Debug",
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$resolvedBuildDirectory = if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    Join-Path $repo "WPELibrary\bin\$Configuration"
}
else {
    [System.IO.Path]::GetFullPath($BuildDirectory)
}
$libraryDll = Join-Path $resolvedBuildDirectory "WPELibrary.dll"
$sqliteDll = Join-Path $resolvedBuildDirectory "System.Data.SQLite.dll"
Add-Type -Path $sqliteDll
[void][System.Reflection.Assembly]::LoadFrom($libraryDll)

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

$databaseType = [WPELibrary.Lib.Socket_Cache+DataBase]
$flags = [System.Reflection.BindingFlags]::Static -bor
    [System.Reflection.BindingFlags]::NonPublic
$connectionField = $databaseType.GetField("conStr", $flags)
$connectionProperty = $databaseType.GetProperty("conStr", $flags)
$createRobot = $databaseType.GetMethod("CreateTable_Robot", $flags)
if ($null -eq $connectionField -and $null -eq $connectionProperty) {
    throw "The database connection member was not found."
}
$originalConnection = if ($null -ne $connectionField) {
    $connectionField.GetValue($null)
}
else {
    $connectionProperty.GetValue($null, $null)
}
$tempDatabase = Join-Path ([System.IO.Path]::GetTempPath()) (
    "wpe-vision-" + [Guid]::NewGuid().ToString("N") + ".db")
$tempConnection = "Data Source=$tempDatabase;Version=3;"
$sourceTemplate = $null
$sourceVariant = $null
$loadedTemplate = $null
$loadedXmlProfile = $null

try {
    if ($null -ne $connectionField) {
        $connectionField.SetValue($null, $tempConnection)
    }
    else {
        $connectionProperty.SetValue($null, $tempConnection, $null)
    }
    Assert-True ([bool]$createRobot.Invoke($null, @())) `
        "Vision database schema creation must succeed."

    $robot = [WPELibrary.Lib.Socket_RobotInfo]::new(
        $true,
        [Guid]::NewGuid(),
        "Vision persistence",
        [WPELibrary.Lib.Socket_Cache+Robot]::InitInstructions())
    $robot.VisionProfile.WindowHandle = 12345
    $robot.VisionProfile.ProcessId = 678
    $robot.VisionProfile.ProcessName = "vision-target"
    $robot.VisionProfile.ProcessPath = "C:\vision\vision-target.exe"
    $robot.VisionProfile.ProcessStartTimeUtcTicks = 638900000000000000
    $robot.VisionProfile.WindowTitle = "Vision target"
    $robot.VisionProfile.Region.X = 10
    $robot.VisionProfile.Region.Y = 20
    $robot.VisionProfile.Region.Width = 40
    $robot.VisionProfile.Region.Height = 30
    $robot.VisionProfile.Region.UseNormalizedCoordinates = $true
    $robot.VisionProfile.Region.ReferenceWidth = 1920
    $robot.VisionProfile.Region.ReferenceHeight = 1080
    $robot.VisionProfile.CaptureSettings.SourceMode = [WPELibrary.Lib.Vision.VisionCaptureSourceMode]::WindowRender
    $robot.VisionProfile.CaptureSettings.MinimumIntervalMilliseconds = 80
    $robot.VisionProfile.CaptureSettings.SkipUnchangedFrames = $false
    $robot.VisionProfile.CaptureSettings.HistoryLimit = 12
    $robot.VisionProfile.CaptureSettings.SaveFailureSnapshots = $true
    $robot.VisionProfile.CaptureSettings.FailureSnapshotDirectory = "C:\vision\diagnostics"
    $robot.VisionProfile.CaptureSettings.RequireExactClientSize = $true
    $robot.VisionProfile.CaptureSettings.RequiredClientWidth = 1280
    $robot.VisionProfile.CaptureSettings.RequiredClientHeight = 720
    $robot.VisionProfile.OcrOptions.ScaleFactor = 3
    $robot.VisionProfile.OcrOptions.UseBinaryThreshold = $true
    $robot.VisionProfile.OcrOptions.UseAdaptiveThreshold = $true
    $robot.VisionProfile.OcrOptions.AdaptiveThresholdWindowSize = 21
    $robot.VisionProfile.OcrOptions.AdaptiveThresholdOffset = 6
    $robot.VisionProfile.OcrOptions.Invert = $true
    $robot.VisionProfile.OcrOptions.UseDenoise = $true
    $robot.VisionProfile.OcrOptions.UseSharpen = $true
    $robot.VisionProfile.OcrOptions.CharacterWhitelist = "0123456789"
    $robot.VisionProfile.OcrOptions.Engine = [WPELibrary.Lib.Vision.VisionOcrEngine]::Onnx
    $robot.VisionProfile.OcrOptions.OnnxModelDirectory = "models\ocr-test"
    $robot.VisionProfile.OcrOptions.OnnxDetectionThreshold = 0.35
    $robot.VisionProfile.OcrOptions.OnnxRecognitionThreshold = 0.72
    $robot.VisionProfile.OcrOptions.OnnxMaxImageSide = 1280
    $robot.VisionProfile.OcrOptions.PythonExecutablePath = "C:\vision\python.exe"
    $robot.VisionProfile.OcrOptions.PythonWorkerScriptPath = "C:\vision\worker.py"
    $robot.VisionProfile.OcrOptions.PythonWorkerTimeoutMilliseconds = 24000
    $robot.VisionProfile.OcrCondition.ExpectedText = "任务完成"
    $robot.VisionProfile.OcrCondition.MinimumConfidence = 0.8

    $step = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
    $step.Name = "wait-template"
    $step.Condition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $step.Condition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TemplateAppears
    $step.Condition.NormalizeTemplateBrightness = $false
    $step.Condition.AllowTemplateScaleVariation = $true
    $step.Condition.TemplateMinimumScale = 0.8
    $step.Condition.TemplateMaximumScale = 1.2
    $step.Condition.TemplateScaleStep = 0.1
    $step.Condition.ColorCondition.Red = 24
    $step.Condition.ColorCondition.Green = 128
    $step.Condition.ColorCondition.Blue = 240
    $step.Condition.ColorCondition.Tolerance = 9
    $step.Condition.ColorCondition.MinimumPixelCount = 18
    $step.Condition.ColorCondition.MinimumMatchRatio = 0.25
    $step.Condition.Region.X = 1
    $step.Condition.Region.Y = 2
    $step.Condition.Region.Width = 8
    $step.Condition.Region.Height = 9
    $step.Condition.Region.UseNormalizedCoordinates = $true
    $step.Condition.Region.ReferenceWidth = 1920
    $step.Condition.Region.ReferenceHeight = 1080
    $sourceTemplate = [System.Drawing.Bitmap]::new(5, 4)
    $templateGraphics = [System.Drawing.Graphics]::FromImage($sourceTemplate)
    try {
        $templateGraphics.Clear([System.Drawing.Color]::Orange)
    }
    finally {
        $templateGraphics.Dispose()
    }
    $step.Condition.Template = $sourceTemplate
    $sourceVariant = [System.Drawing.Bitmap]::new(5, 4)
    $variantGraphics = [System.Drawing.Graphics]::FromImage($sourceVariant)
    try {
        $variantGraphics.Clear([System.Drawing.Color]::Purple)
    }
    finally {
        $variantGraphics.Dispose()
    }
    $step.Condition.TemplateVariants.Add($sourceVariant)
    $step.VerificationEnabled = $true
    $step.Verification = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $step.Verification.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TextAppears
    $step.Verification.Region = $step.Condition.Region.Clone()
    $step.Verification.Region.X = 3
    $step.Verification.Region.Y = 4
    $step.Verification.Region.Width = 5
    $step.Verification.Region.Height = 6
    $step.VerificationUsesSeparateRegion = $true
    $step.Verification.TextCondition.ExpectedText = "成功"
    $robot.VisionProfile.AssistantSteps.Add($step)

    $databaseFlags = [System.Reflection.BindingFlags]::Static -bor
        [System.Reflection.BindingFlags]::NonPublic
    $robotListType = [WPELibrary.Lib.Socket_Cache+RobotList]
    $buildProfileXml = $robotListType.GetMethod("BuildVisionProfileXml", $databaseFlags)
    $parseProfileXml = $robotListType.GetMethod("ParseVisionProfile", $databaseFlags)
    $xmlProfile = $buildProfileXml.Invoke($null, @($robot.VisionProfile))
    $loadedXmlProfile = $parseProfileXml.Invoke($null, @($xmlProfile))
    Assert-True ($loadedXmlProfile.OcrOptions.Engine -eq [WPELibrary.Lib.Vision.VisionOcrEngine]::Onnx -and
        $loadedXmlProfile.OcrOptions.OnnxModelDirectory -eq "models\ocr-test" -and
        [double]$loadedXmlProfile.OcrOptions.OnnxDetectionThreshold -eq 0.35 -and
        [double]$loadedXmlProfile.OcrOptions.OnnxRecognitionThreshold -eq 0.72 -and
        $loadedXmlProfile.OcrOptions.OnnxMaxImageSide -eq 1280 -and
        $loadedXmlProfile.OcrOptions.PythonExecutablePath -eq "C:\vision\python.exe" -and
        $loadedXmlProfile.OcrOptions.PythonWorkerScriptPath -eq "C:\vision\worker.py" -and
        $loadedXmlProfile.OcrOptions.PythonWorkerTimeoutMilliseconds -eq 24000) `
        "XML load must restore ONNX and Python Worker OCR settings."
    Assert-True ($loadedXmlProfile.AssistantSteps[0].Condition.ColorCondition.Red -eq 24 -and
        $loadedXmlProfile.AssistantSteps[0].Condition.ColorCondition.Green -eq 128 -and
        $loadedXmlProfile.AssistantSteps[0].Condition.ColorCondition.Blue -eq 240 -and
        $loadedXmlProfile.AssistantSteps[0].Condition.ColorCondition.Tolerance -eq 9 -and
        $loadedXmlProfile.AssistantSteps[0].Condition.ColorCondition.MinimumPixelCount -eq 18 -and
        [double]$loadedXmlProfile.AssistantSteps[0].Condition.ColorCondition.MinimumMatchRatio -eq 0.25) `
        "XML load must restore color condition settings."
    Assert-True ($loadedXmlProfile.CaptureSettings.RequireExactClientSize -and
        $loadedXmlProfile.CaptureSettings.RequiredClientWidth -eq 1280 -and
        $loadedXmlProfile.CaptureSettings.RequiredClientHeight -eq 720) `
        "XML load must restore the exact 1280x720 client-size settings."

    $legacyXmlProfile = [System.Xml.Linq.XElement]::new($xmlProfile)
    $legacyCaptureSettings = $legacyXmlProfile.Element("CaptureSettings")
    $legacyCaptureSettings.Element("RequireExactClientSize").Remove()
    $legacyCaptureSettings.Element("RequiredClientWidth").Remove()
    $legacyCaptureSettings.Element("RequiredClientHeight").Remove()
    $legacyLoadedProfile = $parseProfileXml.Invoke($null, @($legacyXmlProfile))
    Assert-True (-not $legacyLoadedProfile.CaptureSettings.RequireExactClientSize -and
        $legacyLoadedProfile.CaptureSettings.RequiredClientWidth -eq 0 -and
        $legacyLoadedProfile.CaptureSettings.RequiredClientHeight -eq 0) `
        "XML profiles without exact client-size fields must retain legacy behavior."

    [WPELibrary.Lib.Socket_Cache+DataBase]::InsertTable_Robot($robot)
    $profileRows = [WPELibrary.Lib.Socket_Cache+DataBase]::SelectTable_RobotVisionProfile($robot.RID)
    Assert-True ($profileRows.Rows.Count -eq 1) `
        "Robot vision profile must be stored in SQLite."
    Assert-True ([int]$profileRows.Rows[0]["OcrScale"] -eq 3 -and
        $profileRows.Rows[0]["OcrKeyword"].ToString() -eq "任务完成") `
        "OCR profile settings must survive SQLite persistence."
    Assert-True ($profileRows.Rows[0]["ProcessPath"].ToString() -eq "C:\vision\vision-target.exe" -and
        [long]$profileRows.Rows[0]["ProcessStartTimeUtcTicks"] -eq 638900000000000000 -and
        [bool]$profileRows.Rows[0]["RegionNormalized"] -and
        [int]$profileRows.Rows[0]["CaptureInterval"] -eq 80 -and
        [bool]$profileRows.Rows[0]["CaptureSaveFailures"] -and
        $profileRows.Rows[0]["CaptureFailureDirectory"].ToString() -eq "C:\vision\diagnostics" -and
        [bool]$profileRows.Rows[0]["CaptureRequireExactClientSize"] -and
        [int]$profileRows.Rows[0]["CaptureRequiredClientWidth"] -eq 1280 -and
        [int]$profileRows.Rows[0]["CaptureRequiredClientHeight"] -eq 720 -and
        [bool]$profileRows.Rows[0]["OcrAdaptive"] -and
        [int]$profileRows.Rows[0]["OcrAdaptiveWindow"] -eq 21 -and
        $profileRows.Rows[0]["OcrWhitelist"].ToString() -eq "0123456789") `
        "Window identity, normalized region, and capture settings must survive SQLite persistence."

    Assert-True ([int]$profileRows.Rows[0]["OcrEngine"] -eq 1 -and
        $profileRows.Rows[0]["OcrModelDirectory"].ToString() -eq "models\ocr-test" -and
        [double]$profileRows.Rows[0]["OcrDetectionThreshold"] -eq 0.35 -and
        [double]$profileRows.Rows[0]["OcrRecognitionThreshold"] -eq 0.72 -and
        [int]$profileRows.Rows[0]["OcrMaxImageSide"] -eq 1280 -and
        $profileRows.Rows[0]["OcrPythonExecutable"].ToString() -eq "C:\vision\python.exe" -and
        $profileRows.Rows[0]["OcrPythonWorkerScript"].ToString() -eq "C:\vision\worker.py" -and
        [int]$profileRows.Rows[0]["OcrPythonWorkerTimeout"] -eq 24000) `
        "ONNX and Python Worker OCR settings must survive SQLite persistence."

    $conditionRows = [WPELibrary.Lib.Socket_Cache+DataBase]::SelectTable_RobotVisionCondition($robot.RID)
    Assert-True ($conditionRows.Rows.Count -eq 1) `
        "Assistant vision conditions must be stored separately from RobotInstruction."
    Assert-True (($conditionRows.Rows[0]["TemplatePng"] -as [byte[]]).Length -gt 0) `
        "Assistant template bytes must survive SQLite persistence."
    Assert-True ([int]$conditionRows.Rows[0]["ColorR"] -eq 24 -and
        [int]$conditionRows.Rows[0]["ColorG"] -eq 128 -and
        [int]$conditionRows.Rows[0]["ColorB"] -eq 240 -and
        [int]$conditionRows.Rows[0]["ColorTolerance"] -eq 9 -and
        [int]$conditionRows.Rows[0]["ColorMinimumPixels"] -eq 18 -and
        [double]$conditionRows.Rows[0]["ColorMinimumRatio"] -eq 0.25) `
        "Color condition settings must survive SQLite persistence."
    Assert-True ($conditionRows.Rows[0]["TemplateVariants"].ToString().Length -gt 0 -and
        [bool]$conditionRows.Rows[0]["VerificationEnabled"] -and
        $conditionRows.Rows[0]["VerificationKeyword"].ToString() -eq "成功" -and
        [bool]$conditionRows.Rows[0]["VerificationSeparateRegion"] -and
        [int]$conditionRows.Rows[0]["VerificationRegionX"] -eq 3 -and
        [int]$conditionRows.Rows[0]["VerificationRegionY"] -eq 4 -and
        [int]$conditionRows.Rows[0]["VerificationRegionWidth"] -eq 5 -and
        [int]$conditionRows.Rows[0]["VerificationRegionHeight"] -eq 6 -and
        [bool]$conditionRows.Rows[0]["RegionNormalized"] -and
        -not [bool]$conditionRows.Rows[0]["TemplateNormalize"] -and
        [bool]$conditionRows.Rows[0]["TemplateScaleVariation"] -and
        [double]$conditionRows.Rows[0]["TemplateMinScale"] -eq 0.8 -and
        [double]$conditionRows.Rows[0]["TemplateMaxScale"] -eq 1.2) `
        "Template variants and normalized condition regions must survive SQLite persistence."

    [WPELibrary.Lib.Socket_Cache+RobotList]::LoadRobotList_FromDB()
    $loadedRobot = [WPELibrary.Lib.Socket_Cache+RobotList]::lstRobot[0]
    Assert-True ($loadedRobot.VisionProfile.OcrOptions.ScaleFactor -eq 3 -and
        $loadedRobot.VisionProfile.OcrCondition.ExpectedText -eq "任务完成") `
        "SQLite load must restore OCR settings."
    Assert-True ($loadedRobot.VisionProfile.OcrOptions.Engine -eq [WPELibrary.Lib.Vision.VisionOcrEngine]::Onnx -and
        $loadedRobot.VisionProfile.OcrOptions.OnnxModelDirectory -eq "models\ocr-test" -and
        [double]$loadedRobot.VisionProfile.OcrOptions.OnnxDetectionThreshold -eq 0.35 -and
        [double]$loadedRobot.VisionProfile.OcrOptions.OnnxRecognitionThreshold -eq 0.72 -and
        $loadedRobot.VisionProfile.OcrOptions.OnnxMaxImageSide -eq 1280 -and
        $loadedRobot.VisionProfile.OcrOptions.PythonExecutablePath -eq "C:\vision\python.exe" -and
        $loadedRobot.VisionProfile.OcrOptions.PythonWorkerScriptPath -eq "C:\vision\worker.py" -and
        $loadedRobot.VisionProfile.OcrOptions.PythonWorkerTimeoutMilliseconds -eq 24000) `
        "SQLite load must restore ONNX and Python Worker OCR settings."
    Assert-True ($loadedRobot.VisionProfile.ProcessPath -eq "C:\vision\vision-target.exe" -and
        $loadedRobot.VisionProfile.ProcessStartTimeUtcTicks -eq 638900000000000000 -and
        $loadedRobot.VisionProfile.Region.UseNormalizedCoordinates -and
        $loadedRobot.VisionProfile.CaptureSettings.MinimumIntervalMilliseconds -eq 80 -and
        -not $loadedRobot.VisionProfile.CaptureSettings.SkipUnchangedFrames -and
        $loadedRobot.VisionProfile.CaptureSettings.SaveFailureSnapshots -and
        $loadedRobot.VisionProfile.CaptureSettings.FailureSnapshotDirectory -eq "C:\vision\diagnostics" -and
        $loadedRobot.VisionProfile.CaptureSettings.RequireExactClientSize -and
        $loadedRobot.VisionProfile.CaptureSettings.RequiredClientWidth -eq 1280 -and
        $loadedRobot.VisionProfile.CaptureSettings.RequiredClientHeight -eq 720 -and
        $loadedRobot.VisionProfile.OcrOptions.UseAdaptiveThreshold -and
        $loadedRobot.VisionProfile.OcrOptions.AdaptiveThresholdWindowSize -eq 21 -and
        $loadedRobot.VisionProfile.OcrOptions.CharacterWhitelist -eq "0123456789") `
        "SQLite load must restore window identity, normalized region, and capture settings."
    Assert-True ($loadedRobot.VisionProfile.AssistantSteps.Count -eq 1) `
        "SQLite load must restore assistant conditions."
    Assert-True ($loadedRobot.VisionProfile.AssistantSteps[0].VerificationEnabled -and
        $loadedRobot.VisionProfile.AssistantSteps[0].Verification.TextCondition.ExpectedText -eq "成功" -and
        $loadedRobot.VisionProfile.AssistantSteps[0].VerificationUsesSeparateRegion -and
        $loadedRobot.VisionProfile.AssistantSteps[0].Verification.Region.X -eq 3 -and
        $loadedRobot.VisionProfile.AssistantSteps[0].Verification.Region.Height -eq 6) `
        "SQLite load must restore optional action verification."
    Assert-True ($loadedRobot.VisionProfile.AssistantSteps[0].Condition.ColorCondition.Red -eq 24 -and
        $loadedRobot.VisionProfile.AssistantSteps[0].Condition.ColorCondition.Green -eq 128 -and
        $loadedRobot.VisionProfile.AssistantSteps[0].Condition.ColorCondition.Blue -eq 240 -and
        $loadedRobot.VisionProfile.AssistantSteps[0].Condition.ColorCondition.Tolerance -eq 9 -and
        $loadedRobot.VisionProfile.AssistantSteps[0].Condition.ColorCondition.MinimumPixelCount -eq 18 -and
        [double]$loadedRobot.VisionProfile.AssistantSteps[0].Condition.ColorCondition.MinimumMatchRatio -eq 0.25) `
        "SQLite load must restore color condition settings."
    $loadedTemplate = $loadedRobot.VisionProfile.AssistantSteps[0].Condition.Template
    Assert-True ($null -ne $loadedTemplate -and $loadedTemplate.Width -eq 5) `
        "SQLite load must restore template resources."
    Assert-True ($loadedRobot.VisionProfile.AssistantSteps[0].Condition.Region.UseNormalizedCoordinates -and
        $loadedRobot.VisionProfile.AssistantSteps[0].Condition.TemplateVariants.Count -eq 1 -and
        -not $loadedRobot.VisionProfile.AssistantSteps[0].Condition.NormalizeTemplateBrightness -and
        $loadedRobot.VisionProfile.AssistantSteps[0].Condition.AllowTemplateScaleVariation -and
        [double]$loadedRobot.VisionProfile.AssistantSteps[0].Condition.TemplateScaleStep -eq 0.1) `
        "SQLite load must restore normalized condition regions and template variants."
}
finally {
    if ($null -ne $loadedXmlProfile) {
        foreach ($xmlStep in $loadedXmlProfile.AssistantSteps) {
            if ($null -ne $xmlStep.Condition) {
                if ($null -ne $xmlStep.Condition.Template) {
                    $xmlStep.Condition.Template.Dispose()
                }
                $xmlStep.Condition.DisposeTemplateVariants()
            }
            if ($null -ne $xmlStep.Verification -and $null -ne $xmlStep.Verification.Template) {
                $xmlStep.Verification.Template.Dispose()
            }
        }
    }
    if ($null -ne $loadedTemplate) { $loadedTemplate.Dispose() }
    if ($null -ne $sourceTemplate) { $sourceTemplate.Dispose() }
    if ($null -ne $sourceVariant) { $sourceVariant.Dispose() }
    [WPELibrary.Lib.Socket_Cache+RobotList]::RobotListClear()
    if ($null -ne $connectionField) {
        $connectionField.SetValue($null, $originalConnection)
    }
    else {
        $connectionProperty.SetValue($null, $originalConnection, $null)
    }
    if (Test-Path -LiteralPath $tempDatabase) {
        Remove-Item -LiteralPath $tempDatabase -Force
    }
}

Write-Output "Vision persistence regression passed."
