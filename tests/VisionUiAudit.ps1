param(
    [string]$Configuration = "Debug",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo "WPELibrary\work\VisionUiAudit"
}
else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$libraryDll = Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
$hexBoxDll = Join-Path $repo "WPELibrary\bin\$Configuration\Be.Windows.Forms.HexBox.dll"
$applicationDirectory = Join-Path $repo "WinsockPacketEditor\bin\$Configuration"
$applicationExe = Get-ChildItem -LiteralPath $applicationDirectory -Filter "*.exe" |
    Where-Object { $_.Name -notlike "EasyHook*Svc.exe" } |
    Select-Object -First 1 -ExpandProperty FullName

if (-not (Test-Path -LiteralPath $libraryDll)) { throw "WPELibrary.dll not found: $libraryDll" }
if (-not (Test-Path -LiteralPath $hexBoxDll)) { throw "HexBox assembly not found: $hexBoxDll" }
if ([string]::IsNullOrWhiteSpace($applicationExe)) { throw "Application assembly was not found." }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll
[void][System.Reflection.Assembly]::LoadFrom($applicationExe)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

function Capture-Form($form, [string]$fileName) {
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object System.Drawing.Point -32000, -32000
    $form.ShowInTaskbar = $false
    $form.Show()
    for ($index = 0; $index -lt 20; $index++) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }
    $form.PerformLayout()
    $bitmap = New-Object System.Drawing.Bitmap $form.Width, $form.Height
    try {
        $form.DrawToBitmap(
            $bitmap,
            [System.Drawing.Rectangle]::new(0, 0, $form.Width, $form.Height))
        $bitmap.Save(
            (Join-Path $resolvedOutput $fileName),
            [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

$robot = [WPELibrary.Lib.Socket_RobotInfo]::new(
    $true,
    [Guid]::NewGuid(),
    "Vision UI audit",
    [WPELibrary.Lib.Socket_Cache+Robot]::InitInstructions())
$robot.VisionProfile.WindowHandle = 12345
$robot.VisionProfile.ProcessId = 678
$robot.VisionProfile.ProcessName = "synthetic-target"
$robot.VisionProfile.WindowTitle = "Synthetic target"
$robot.VisionProfile.Region.X = 10
$robot.VisionProfile.Region.Y = 12
$robot.VisionProfile.Region.Width = 180
$robot.VisionProfile.Region.Height = 90

$textStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
$textStep.Name = "等待任务完成"
$textStep.Condition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
$textStep.Condition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TextAppears
$textStep.Condition.Region = $robot.VisionProfile.Region.Clone()
$textStep.Condition.TextCondition.ExpectedText = "任务完成"
$robot.VisionProfile.AssistantSteps.Add($textStep)

$template = [System.Drawing.Bitmap]::new(48, 24)
$graphics = [System.Drawing.Graphics]::FromImage($template)
try {
    $graphics.Clear([System.Drawing.Color]::SteelBlue)
    $graphics.FillRectangle([System.Drawing.Brushes]::White, 10, 6, 20, 8)
}
finally {
    $graphics.Dispose()
}
$templateStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
$templateStep.Name = "等待模板消失"
$templateStep.Condition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
$templateStep.Condition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TemplateDisappears
$templateStep.Condition.Region = $robot.VisionProfile.Region.Clone()
$templateStep.Condition.Template = $template
$robot.VisionProfile.AssistantSteps.Add($templateStep)

$form = [WPELibrary.Socket_RobotForm]::new($robot)
try {
    $instructionTabsField = $form.GetType().GetField(
        "tcRobotInstruction",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $instructionTabs = $instructionTabsField.GetValue($form)
    $instructionTabs.SelectedTab = $instructionTabs.TabPages["tpInstruction_Vision"]
    $templateField = $form.GetType().GetField(
        "visionTemplate",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $templatePictureField = $form.GetType().GetField(
        "pbVisionTemplate",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $previewTemplate = [System.Drawing.Bitmap]::new($template)
    $templateField.SetValue($form, $previewTemplate)
    $templatePictureField.GetValue($form).Image = $previewTemplate
    Capture-Form $form "01-robot-vision.png"
}
finally {
    $form.Close()
    $form.Dispose()
    if ($templateStep.Condition.Template -ne $null) {
        $templateStep.Condition.Template.Dispose()
        $templateStep.Condition.Template = $null
    }
}

Write-Host "Vision UI audit passed. Screenshot: $(Join-Path $resolvedOutput '01-robot-vision.png')"
