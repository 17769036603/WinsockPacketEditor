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
    Where-Object {
        $_.Name -notlike "EasyHook*Svc.exe" -and
        $_.Name -notlike "VisionLiveHarness*.exe"
    } |
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

function Add-AutoScrollDescendant($control, $results) {
    if ($control -is [System.Windows.Forms.ScrollableControl] -and $control.AutoScroll) {
        $results.Add($control)
    }
    foreach ($child in $control.Controls) {
        Add-AutoScrollDescendant $child $results
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
    $visionTab = $instructionTabs.SelectedTab
    if ($visionTab -eq $null) { throw "Vision tab was not found." }
    $visionSettingsTab = $instructionTabs.TabPages["tpInstruction_VisionSettings"]
    if ($visionSettingsTab -eq $null) { throw "Vision settings tab was not found." }
    if ($visionSettingsTab.Text -eq "Main_Settings") {
        throw "Vision settings tab text was not localized."
    }
    $visionRoot = $visionTab.Controls | Select-Object -First 1
    if ($visionRoot -eq $null) { throw "Vision page root was not created." }
    $requiredVisionFields = @(
        "cbbVisionWindows",
        "bVisionSelectRegion",
        "bVisionRecapture",
        "pbVisionPreview",
        "txtVisionOcrKeyword",
        "cbbVisionOcrEngine",
        "txtVisionOcrModelDirectory",
        "txtVisionPythonExecutable",
        "txtVisionPythonWorkerScript",
        "nudVisionPythonWorkerTimeout",
        "bVisionBrowsePythonExecutable",
        "bVisionBrowsePythonWorkerScript",
        "bVisionResetPythonSettings",
        "bVisionTestPythonWorker",
        "nudVisionOcrDetectionThreshold",
        "nudVisionOcrRecognitionThreshold",
        "nudVisionOcrMaxImageSide",
        "lVisionOcrModelStatus",
        "cbbVisionConditionType",
        "txtVisionColorRgb",
        "nudVisionColorTolerance",
        "nudVisionColorMinimumPixels",
        "nudVisionColorMinimumRatio",
        "bVisionRecognizeText",
        "bVisionMatchTemplate",
        "bVisionAdvancedSettings",
        "bVisionConfirmAction",
        "bVisionCancelAction",
        "bVisionActionVerificationMenu",
        "cbbVisionSteps",
        "chkVisionActionVerification",
        "cbbVisionVerificationType",
        "bVisionSelectVerificationRegion",
        "lVisionVerificationHint",
        "bVisionRunSteps",
        "bVisionStopSteps",
        "bToggleVisionAssistantLog",
        "txtVisionAssistantLog",
        "lVisionStatus",
        "bToggleRobotInstructionPanel",
        "bToggleExecuteLog",
        "txtExecute"
    )
    foreach ($fieldName in $requiredVisionFields) {
        $field = $form.GetType().GetField(
            $fieldName,
            [System.Reflection.BindingFlags]::Instance -bor
            [System.Reflection.BindingFlags]::NonPublic)
        if ($field -eq $null -or $field.GetValue($form) -eq $null) {
            throw "Vision control field is missing: $fieldName"
        }
    }
    $confirmActionButton = $form.GetType().GetField(
        "bVisionConfirmAction",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic).GetValue($form)
    if ($confirmActionButton.Name -ne "bVisionConfirmAction") {
        throw "The confirm-action button was not wired with its stable name."
    }
    $stepPicker = $form.GetType().GetField(
        "cbbVisionSteps",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic).GetValue($form)
    if ($stepPicker.Items.Count -lt 3 -or
        $stepPicker.Items[0].Condition -ne $null -or
        -not ($stepPicker -is [System.Windows.Forms.ListBox])) {
        throw "The assistant steps must be shown in a visible list beginning with a new-step entry."
    }
    $verificationMenuButton = $form.GetType().GetField(
        "bVisionActionVerificationMenu",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic).GetValue($form)
    if ($verificationMenuButton.ContextMenuStrip.Items.Count -ne 3) {
        throw "The action-verification dropdown must expose the three verification modes."
    }
    $verificationCheckbox = $form.GetType().GetField(
        "chkVisionActionVerification",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic).GetValue($form)
    $verificationTypePicker = $form.GetType().GetField(
        "cbbVisionVerificationType",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic).GetValue($form)
    $verificationMenuButton.ContextMenuStrip.Items[1].PerformClick()
    if (-not $verificationCheckbox.Checked -or
        $verificationTypePicker.SelectedItem.Type -ne [WPELibrary.Lib.Vision.VisionConditionType]::TextAppears) {
        throw "The text-wait menu entry did not enable text verification."
    }
    $verificationMenuButton.ContextMenuStrip.Items[2].PerformClick()
    if (-not $verificationCheckbox.Checked -or
        $verificationTypePicker.SelectedItem.Type -ne [WPELibrary.Lib.Vision.VisionConditionType]::TemplateAppears) {
        throw "The image-wait menu entry did not enable image verification."
    }
    $verificationMenuButton.ContextMenuStrip.Items[0].PerformClick()
    if ($verificationCheckbox.Checked) {
        throw "The no-verification menu entry did not clear verification."
    }
    $conditionTypeField = $form.GetType().GetField(
        "cbbVisionConditionType",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $colorField = $form.GetType().GetField(
        "txtVisionColorRgb",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $conditionType = $conditionTypeField.GetValue($form)
    $colorPanel = $colorField.GetValue($form).Parent
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = [System.Drawing.Point]::new(-32000, -32000)
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $conditionType.SelectedIndex = 5
    if (-not $colorPanel.Visible) {
        throw "Color condition controls did not become visible."
    }
    $conditionType.SelectedIndex = 0
    if ($colorPanel.Visible) {
        throw "Color condition controls did not hide for a text condition."
    }
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
    $form.Width = 350
    $form.Height = 820
    Capture-Form $form "01-robot-vision-narrow.png"
    $selectRegionButton = $form.GetType().GetField(
        "bVisionSelectRegion",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $recaptureButton = $form.GetType().GetField(
        "bVisionRecapture",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    if (-not $selectRegionButton.GetValue($form).Visible -or
        -not $recaptureButton.GetValue($form).Visible) {
        throw "The primary region controls are not visible in the narrow layout."
    }
    $coordinateFields = @("nudVisionX", "nudVisionY", "nudVisionWidth", "nudVisionHeight")
    foreach ($fieldName in $coordinateFields) {
        $field = $form.GetType().GetField(
            $fieldName,
            [System.Reflection.BindingFlags]::Instance -bor
            [System.Reflection.BindingFlags]::NonPublic)
        if ($field.GetValue($form).Visible) {
            throw "Manual coordinate editor should be hidden by default: $fieldName"
        }
    }
    $visionWindowField = $form.GetType().GetField(
        "cbbVisionWindows",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    if ($null -eq $visionWindowField -or $visionWindowField.GetValue($form).Visible) {
        throw "The injected target window selector should be hidden."
    }
    $instructionTabs.SelectedTab = $visionSettingsTab
    [System.Windows.Forms.Application]::DoEvents()
    $advancedSettingsField = $form.GetType().GetField(
        "bVisionAdvancedSettings",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    if ($null -eq $advancedSettingsField -or
        $null -eq $advancedSettingsField.GetValue($form) -or
        -not $advancedSettingsField.GetValue($form).Visible) {
        throw "The advanced vision settings entry was not found."
    }
    $advancedStorageField = $form.GetType().GetField(
        "visionAdvancedStorage",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $advancedModulesField = $form.GetType().GetField(
        "visionAdvancedModules",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    if ($null -eq $advancedStorageField -or
        $null -eq $advancedStorageField.GetValue($form) -or
        $advancedModulesField.GetValue($form).Count -lt 5) {
        throw "Advanced vision modules were not moved to the settings host."
    }
    $verificationPanelField = $form.GetType().GetField(
        "visionVerificationAdvancedPanel",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    if ($null -eq $verificationPanelField -or
        $verificationPanelField.GetValue($form).Controls.Count -eq 0 -or
        $verificationPanelField.GetValue($form).Controls[0].Visible) {
        throw "The advanced verification settings entry should be hidden."
    }
    $instructionTabs.SelectedTab = $visionTab
    [System.Windows.Forms.Application]::DoEvents()
    $form.GetType().GetField(
        "cbbVisionWindows",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic).GetValue($form).SelectedIndex = -1
    $selectRegionButton.GetValue($form).PerformClick()
    $statusField = $form.GetType().GetField(
        "lVisionStatus",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $selectionHintText = [string]$statusField.GetValue($form).Text
    if ([string]::IsNullOrWhiteSpace($selectionHintText) -or
        $selectionHintText -eq "Vision_SelectWindowHint") {
        throw "Selecting a region without a target window did not show the selection hint."
    }
    $selectionMethod = $form.GetType().GetMethod(
        "CalculateVisionSelectionRectangle",
        [System.Reflection.BindingFlags]::Static -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $reverseSelection = $selectionMethod.Invoke(
        $null,
        [object[]]@(
            [System.Drawing.Point]::new(120, 90),
            [System.Drawing.Point]::new(20, 30)))
    if ($reverseSelection.X -ne 20 -or $reverseSelection.Y -ne 30 -or
        $reverseSelection.Width -ne 100 -or $reverseSelection.Height -ne 60) {
        throw "Reverse drag did not calculate both selection endpoints."
    }
    $sourceText = Get-Content -LiteralPath (Join-Path $repo "WPELibrary\Socket_RobotForm.cs") -Raw
    if (-not $sourceText.Contains("Keys.Escape") -or
        -not $sourceText.Contains("DialogResult = DialogResult.Cancel")) {
        throw "The region picker does not retain an Esc cancellation path."
    }
    $executeTextField = $form.GetType().GetField(
        "txtExecute",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    if ($executeTextField.GetValue($form).Visible) {
        throw "Execution log should be collapsed by default."
    }
    $executeToggleField = $form.GetType().GetField(
        "bToggleExecuteLog",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $executeToggleField.GetValue($form).PerformClick()
    if (-not $executeTextField.GetValue($form).Visible) {
        throw "Execution log did not expand after clicking the toggle button."
    }
    $executeToggleField.GetValue($form).PerformClick()
    if ($executeTextField.GetValue($form).Visible) {
        throw "Execution log did not collapse after clicking the toggle button."
    }
    $instructionGroupField = $form.GetType().GetField(
        "gbRobotInstruction",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $instructionToggleField = $form.GetType().GetField(
        "bToggleRobotInstructionPanel",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    if (-not $instructionGroupField.GetValue($form).Visible -or
        $instructionToggleField.GetValue($form).Visible) {
        throw "A populated instruction list should be expanded by default."
    }
    [void]$form.UpdateInstruction_ByListAction(
        [WPELibrary.Lib.Socket_Cache+System+ListAction]::CleanUp,
        0)
    [System.Windows.Forms.Application]::DoEvents()
    if ($instructionGroupField.GetValue($form).Visible -or
        -not $instructionToggleField.GetValue($form).Visible) {
        throw "An empty instruction list should collapse into a compact toggle."
    }
    Capture-Form $form "02-robot-vision-empty-instructions.png"
    $instructionToggleField.GetValue($form).PerformClick()
    if (-not $instructionGroupField.GetValue($form).Visible) {
        throw "The empty instruction list toggle did not restore the panel."
    }
    $visionLogField = $form.GetType().GetField(
        "txtVisionAssistantLog",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $visionLogToggleField = $form.GetType().GetField(
        "bToggleVisionAssistantLog",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    if ($visionLogField.GetValue($form).Visible) {
        throw "Vision assistant execution log should be collapsed by default."
    }
    $visionLogToggleField.GetValue($form).PerformClick()
    if (-not $visionLogField.GetValue($form).Visible) {
        throw "Vision assistant execution log did not expand after clicking the toggle button."
    }
    $visionLogToggleField.GetValue($form).PerformClick()
    if ($visionLogField.GetValue($form).Visible) {
        throw "Vision assistant execution log did not collapse after clicking the toggle button."
    }
    $visionRoot.PerformLayout()
    if ($visionRoot.HorizontalScroll.Visible) {
        throw "Vision page unexpectedly exposes a horizontal scrollbar."
    }
    $autoScrollControls = New-Object 'System.Collections.Generic.List[object]'
    Add-AutoScrollDescendant $visionRoot $autoScrollControls
    if ($autoScrollControls.Count -ne 1 -or $autoScrollControls[0] -ne $visionRoot) {
        throw "Vision workflow should use one page-level vertical scrollbar without nested scroll containers."
    }
    $visionSections = @($visionRoot.Controls)
    if ($visionSections.Count -ne 4) {
        throw "Vision page should contain four step sections after settings migration; found $($visionSections.Count)."
    }
    foreach ($section in $visionSections) {
        if ($section.Width -le 0) {
            throw "Vision section has no usable width: $($section.Text)"
        }
    }
    $settingsRoot = $visionSettingsTab.Controls | Select-Object -First 1
    if ($settingsRoot -eq $null -or $settingsRoot.Controls.Count -lt 1) {
        throw "Vision settings page should contain the migrated settings section."
    }
}
finally {
    $form.Close()
    $form.Dispose()
    if ($templateStep.Condition.Template -ne $null) {
        $templateStep.Condition.Template.Dispose()
        $templateStep.Condition.Template = $null
    }
}

Write-Host "Vision UI audit passed. Screenshot: $(Join-Path $resolvedOutput '01-robot-vision-narrow.png')"
