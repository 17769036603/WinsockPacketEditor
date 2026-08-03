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
$createRobot = $databaseType.GetMethod("CreateTable_Robot", $flags)
$originalConnection = $connectionField.GetValue($null)
$tempDatabase = Join-Path ([System.IO.Path]::GetTempPath()) (
    "wpe-vision-" + [Guid]::NewGuid().ToString("N") + ".db")
$tempConnection = "Data Source=$tempDatabase;Version=3;"
$sourceTemplate = $null
$sourceVariant = $null
$loadedTemplate = $null

try {
    $connectionField.SetValue($null, $tempConnection)
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
    $robot.VisionProfile.OcrOptions.ScaleFactor = 3
    $robot.VisionProfile.OcrOptions.UseBinaryThreshold = $true
    $robot.VisionProfile.OcrOptions.UseAdaptiveThreshold = $true
    $robot.VisionProfile.OcrOptions.AdaptiveThresholdWindowSize = 21
    $robot.VisionProfile.OcrOptions.AdaptiveThresholdOffset = 6
    $robot.VisionProfile.OcrOptions.Invert = $true
    $robot.VisionProfile.OcrOptions.UseDenoise = $true
    $robot.VisionProfile.OcrOptions.UseSharpen = $true
    $robot.VisionProfile.OcrOptions.CharacterWhitelist = "0123456789"
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
    $robot.VisionProfile.AssistantSteps.Add($step)

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
        [bool]$profileRows.Rows[0]["OcrAdaptive"] -and
        [int]$profileRows.Rows[0]["OcrAdaptiveWindow"] -eq 21 -and
        $profileRows.Rows[0]["OcrWhitelist"].ToString() -eq "0123456789") `
        "Window identity, normalized region, and capture settings must survive SQLite persistence."

    $conditionRows = [WPELibrary.Lib.Socket_Cache+DataBase]::SelectTable_RobotVisionCondition($robot.RID)
    Assert-True ($conditionRows.Rows.Count -eq 1) `
        "Assistant vision conditions must be stored separately from RobotInstruction."
    Assert-True (($conditionRows.Rows[0]["TemplatePng"] -as [byte[]]).Length -gt 0) `
        "Assistant template bytes must survive SQLite persistence."
    Assert-True ($conditionRows.Rows[0]["TemplateVariants"].ToString().Length -gt 0 -and
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
    Assert-True ($loadedRobot.VisionProfile.ProcessPath -eq "C:\vision\vision-target.exe" -and
        $loadedRobot.VisionProfile.ProcessStartTimeUtcTicks -eq 638900000000000000 -and
        $loadedRobot.VisionProfile.Region.UseNormalizedCoordinates -and
        $loadedRobot.VisionProfile.CaptureSettings.MinimumIntervalMilliseconds -eq 80 -and
        -not $loadedRobot.VisionProfile.CaptureSettings.SkipUnchangedFrames -and
        $loadedRobot.VisionProfile.CaptureSettings.SaveFailureSnapshots -and
        $loadedRobot.VisionProfile.CaptureSettings.FailureSnapshotDirectory -eq "C:\vision\diagnostics" -and
        $loadedRobot.VisionProfile.OcrOptions.UseAdaptiveThreshold -and
        $loadedRobot.VisionProfile.OcrOptions.AdaptiveThresholdWindowSize -eq 21 -and
        $loadedRobot.VisionProfile.OcrOptions.CharacterWhitelist -eq "0123456789") `
        "SQLite load must restore window identity, normalized region, and capture settings."
    Assert-True ($loadedRobot.VisionProfile.AssistantSteps.Count -eq 1) `
        "SQLite load must restore assistant conditions."
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
    if ($null -ne $loadedTemplate) { $loadedTemplate.Dispose() }
    if ($null -ne $sourceTemplate) { $sourceTemplate.Dispose() }
    if ($null -ne $sourceVariant) { $sourceVariant.Dispose() }
    [WPELibrary.Lib.Socket_Cache+RobotList]::RobotListClear()
    $connectionField.SetValue($null, $originalConnection)
    if (Test-Path -LiteralPath $tempDatabase) {
        Remove-Item -LiteralPath $tempDatabase -Force
    }
}

Write-Output "Vision persistence regression passed."
