param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Read-SourceFile {
    param([string]$RelativePath)
    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $RelativePath"
    }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Assert-Contains {
    param([string]$Text, [string]$Expected, [string]$Message)
    if (-not $Text.Contains($Expected)) { throw $Message }
}

function Assert-NotContains {
    param([string]$Text, [string]$Unexpected, [string]$Message)
    if ($Text.Contains($Unexpected)) { throw $Message }
}

$form = Read-SourceFile "WPELibrary\Socket_Form.cs"
$assistantConcurrency = Read-SourceFile "WPELibrary\AssistantRunConcurrencyPolicy.cs"
$robotInfo = Read-SourceFile "WPELibrary\Lib\Socket_RobotInfo.cs"
$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$resources = Read-SourceFile "WPELibrary\Properties\Resources.resx"

Add-Type -Path (Join-Path $RepositoryRoot "WPELibrary\AssistantRunConcurrencyPolicy.cs")

$assistantStartMethodStart = $form.IndexOf("private async void AssistantButton_Click")
$assistantStartMethodEnd = $form.IndexOf(
    "private static bool HasTreasureMapInstruction",
    $assistantStartMethodStart)
$assistantStartMethod = $form.Substring(
    $assistantStartMethodStart,
    $assistantStartMethodEnd - $assistantStartMethodStart)
$batchStartMethodStart = $form.IndexOf("private void tsRobotList_Start_Click")
$batchStartMethodEnd = $form.IndexOf(
    "private void tsRobotList_Stop_Click",
    $batchStartMethodStart)
$batchStartMethod = $form.Substring(
    $batchStartMethodStart,
    $batchStartMethodEnd - $batchStartMethodStart)
$batchCompletedMethodStart = $form.IndexOf("private void bgwRobotList_RunWorkerCompleted")
$batchCompletedMethodEnd = $form.IndexOf("#endregion", $batchCompletedMethodStart)
$batchCompletedMethod = $form.Substring(
    $batchCompletedMethodStart,
    $batchCompletedMethodEnd - $batchCompletedMethodStart)
$assistantCompletedMethodStart = $form.IndexOf("private void AssistantRobot_RunWorkerCompleted")
$assistantCompletedMethodEnd = $form.IndexOf(
    "private void UpdateAssistantButtonState",
    $assistantCompletedMethodStart)
$assistantCompletedMethod = $form.Substring(
    $assistantCompletedMethodStart,
    $assistantCompletedMethodEnd - $assistantCompletedMethodStart)

Assert-Contains $form "private void InitAutomationHomeUI()" `
    "The homepage automation host must be initialized explicitly."
Assert-Contains $form "ColumnCount = 2" `
    "The homepage navigation must use two columns for a responsive 2x2 layout."
Assert-Contains $form "RowCount = 2" `
    "The homepage navigation must use two rows for a responsive 2x2 layout."
Assert-Contains $form "Size = new Size(240, 36)" `
    "Homepage navigation buttons must use the consistent 240x36px size."
Assert-Contains $form "Padding = new Padding(12, 8, 12, 8)" `
    "Homepage navigation must use the balanced fixed outer spacing."
Assert-Contains $form "tlpAutomationNavigation_Resize" `
    "Homepage navigation buttons must adapt to narrow layouts."
Assert-Contains $form "int buttonWidth = Math.Min(240, availableWidth)" `
    "Homepage navigation buttons must keep the consistent maximum while adapting down."
Assert-Contains $form "this.bAutomationSend, 0, 0" `
    "Send must be the first homepage entry."
Assert-Contains $form "this.bAutomationSweep, 1, 0" `
    "Sweep must be the second homepage entry."
Assert-Contains $form "this.bAutomationAssistant, 0, 1" `
    "Assistant must be the third homepage entry."
Assert-Contains $form "this.bAutomationFilter, 1, 1" `
    "Filter must be the fourth homepage entry."
Assert-Contains $form "this.tcAutomation.SelectedTab = this.tpSendList" `
    "The homepage must default to the send area."
Assert-Contains $form "settingsSections.Controls.Add(this.tpFilterList)" `
    "The legacy filter settings page must remain available."
Assert-Contains $form "settingsSections.Controls.Add(this.tpRobotList)" `
    "The legacy assistant settings page must remain available."
Assert-Contains $form "this.tcAutomation.Controls.Add(this.tpFilterList)" `
    "The filter page must return to the homepage after settings close."
Assert-Contains $form "this.tcAutomation.Controls.Add(this.tpRobotList)" `
    "The assistant page must return to the homepage after settings close."

Assert-Contains $form "private void InitAssistantButtonUI()" `
    "Assistant buttons must be initialized separately from the legacy grid."
Assert-Contains $form "ColumnCount = 5" `
    "Assistant buttons must use a fixed five-column grid."
Assert-Contains $form "GrowStyle = TableLayoutPanelGrowStyle.AddRows" `
    "Assistant buttons must grow vertically for more than 16 items."
Assert-Contains $form "Math.Max(1, (robots.Count + 4) / 5)" `
    "Assistant grid rows must be calculated in groups of five."
Assert-Contains $form "Size = new Size(84, 34)" `
    "Assistant buttons must use the compact 84x34px size."
Assert-Contains $form "AutoEllipsis = true" `
    "Long assistant names must remain usable in compact buttons."
Assert-Contains $form "Socket_Cache.Robot.DoRobotAsync(robot.RID, parameters)" `
    "An assistant button must start only its own assistant."
Assert-Contains $form "activeRun.Robot.StopRobot()" `
    "Each active assistant button must stop only its own assistant."
Assert-Contains $form "private readonly Dictionary<Guid, ActiveAssistantRun> activeAssistantRuns" `
    "The assistant page must track concurrent runs by preset identity."
Assert-Contains $form "AssistantRunConcurrencyPolicy.CanStart(" `
    "The assistant page must enforce the explicit concurrency policy before starting."
Assert-Contains $form "HasOnlyMountSpeedHoldInstruction(robot)" `
    "Only a standalone mount-speed-hold assistant may use the mount-speed-hold run slot."
Assert-Contains $form "HasOnlyDelayInstructions(robot)" `
    "Only assistants that contain delays and no game actions may use the delay-only run slot."
Assert-Contains $form "bool visible = active || starting || compatible;" `
    "Only active, starting, or policy-compatible assistant buttons may stay visible."
Assert-Contains $form "pair.Value.Enabled = enabled" `
    "Each active assistant button must remain independently stoppable."
Assert-Contains $form "this.RenderAssistantButtons();" `
    "Completing a cross-folder run must rebuild the current folder button set."
Assert-Contains $assistantConcurrency `
    "left == AssistantRunKind.TreasureMap && right == AssistantRunKind.MountRefine" `
    "Treasure-map and mount-refine must remain an explicitly compatible pair."
Assert-Contains $assistantConcurrency `
    "left == AssistantRunKind.MountRefine && right == AssistantRunKind.TreasureMap" `
    "Treasure-map and mount-refine compatibility must be symmetric."
Assert-Contains $assistantConcurrency `
    "left == AssistantRunKind.MountSpeedHold && right == AssistantRunKind.DelayOnly" `
    "Mount-speed hold may run with a delay-only assistant."
Assert-Contains $assistantConcurrency `
    "left == AssistantRunKind.DelayOnly && right == AssistantRunKind.MountSpeedHold" `
    "Mount-speed hold and delay-only compatibility must be symmetric."
Assert-Contains $assistantConcurrency "hasTreasureMapInstruction == isMountRefinePreset" `
    "Mixed or unclassified presets must remain in the exclusive Other group."

$mountSpeedHold = [WPELibrary.AssistantRunConcurrencyPolicy]::Classify($false, $false, $true, $false)
$delayOnly = [WPELibrary.AssistantRunConcurrencyPolicy]::Classify($false, $false, $false, $true)
$treasureMap = [WPELibrary.AssistantRunConcurrencyPolicy]::Classify($true, $false, $false, $false)
$mountRefine = [WPELibrary.AssistantRunConcurrencyPolicy]::Classify($false, $true, $false, $false)
$other = [WPELibrary.AssistantRunConcurrencyPolicy]::Classify($false, $false, $false, $false)
$mixed = [WPELibrary.AssistantRunConcurrencyPolicy]::Classify($false, $false, $true, $true)
if ($mountSpeedHold -ne [WPELibrary.AssistantRunKind]::MountSpeedHold -or
    $delayOnly -ne [WPELibrary.AssistantRunKind]::DelayOnly -or
    $treasureMap -ne [WPELibrary.AssistantRunKind]::TreasureMap -or
    $mountRefine -ne [WPELibrary.AssistantRunKind]::MountRefine -or
    $other -ne [WPELibrary.AssistantRunKind]::Other -or
    $mixed -ne [WPELibrary.AssistantRunKind]::Other -or
    [WPELibrary.AssistantRunConcurrencyPolicy]::Classify($true, $true, $false, $false) -ne
        [WPELibrary.AssistantRunKind]::Other -or
    [WPELibrary.AssistantRunConcurrencyPolicy]::Classify($true, $false, $true, $false) -ne
        [WPELibrary.AssistantRunKind]::Other -or
    [WPELibrary.AssistantRunConcurrencyPolicy]::Classify($true, $false, $false, $true) -ne
        [WPELibrary.AssistantRunKind]::Other) {
    throw "Standalone hold/delay assistants must be classified strictly."
}
if (-not [WPELibrary.AssistantRunConcurrencyPolicy]::CanRunTogether($treasureMap, $mountRefine) -or
    -not [WPELibrary.AssistantRunConcurrencyPolicy]::CanRunTogether($mountSpeedHold, $delayOnly) -or
    -not [WPELibrary.AssistantRunConcurrencyPolicy]::CanRunTogether($delayOnly, $mountSpeedHold) -or
    [WPELibrary.AssistantRunConcurrencyPolicy]::CanRunTogether($mountSpeedHold, $mountSpeedHold) -or
    [WPELibrary.AssistantRunConcurrencyPolicy]::CanRunTogether($mountSpeedHold, $other) -or
    [WPELibrary.AssistantRunConcurrencyPolicy]::CanRunTogether($mountSpeedHold, $treasureMap) -or
    [WPELibrary.AssistantRunConcurrencyPolicy]::CanRunTogether($delayOnly, $treasureMap) -or
    [WPELibrary.AssistantRunConcurrencyPolicy]::CanRunTogether($delayOnly, $mountRefine) -or
    [WPELibrary.AssistantRunConcurrencyPolicy]::CanRunTogether($mountSpeedHold, $mountRefine)) {
    throw "Mount-speed hold may run only with a delay-only assistant."
}
if ([WPELibrary.AssistantRunConcurrencyPolicy]::CanStart(
        [WPELibrary.AssistantRunKind[]]@($mountSpeedHold, $delayOnly),
        [WPELibrary.AssistantRunKind]::TreasureMap) -or
    -not [WPELibrary.AssistantRunConcurrencyPolicy]::CanStart(
        [WPELibrary.AssistantRunKind[]]@($mountSpeedHold),
        $delayOnly)) {
    throw "The assistant policy must allow exactly the approved pair and reject a third run."
}
Assert-Contains $assistantStartMethod "if (this.bgwRobotList.IsBusy)" `
    "Homepage assistants must not start while the legacy robot batch is running."
Assert-Contains $batchStartMethod "if (this.HasActiveAssistantRuns || this.assistantStartPending)" `
    "Legacy robot batches must not start while a homepage assistant is active or starting."
Assert-Contains $form "bool batchRunning = this.bgwRobotList.IsBusy;" `
    "Homepage assistant buttons must reflect a running legacy robot batch."
Assert-Contains $form "!this.assistantStartPending && !batchRunning && (active || compatible)" `
    "Homepage assistant start buttons must be disabled during a legacy robot batch."
Assert-Contains $batchStartMethod "this.UpdateAssistantButtonState();" `
    "Starting a legacy robot batch must refresh homepage assistant button state."
Assert-Contains $batchCompletedMethod "this.UpdateAssistantButtonState();" `
    "Completing a legacy robot batch must refresh homepage assistant button state."
Assert-Contains $assistantStartMethod "this.UpdateRobotToolbarState();" `
    "Starting or admitting a homepage assistant must refresh legacy batch controls."
Assert-Contains $assistantCompletedMethod "this.UpdateRobotToolbarState();" `
    "Completing a homepage assistant must refresh legacy batch controls."
Assert-Contains $form "UI_AssistantEmpty" `
    "Empty assistant groups must show an explicit empty state."
Assert-Contains $form "MoveAssistantToFolder_Click" `
    "Assistant buttons must support moving one assistant to another group."
Assert-Contains $form "UI_GroupMustBeEmpty" `
    "Non-empty assistant groups must not be deleted silently."
Assert-Contains $form "addedRobot.RFolder = this.selectedRobotFolder" `
    "New assistants must be assigned to the currently selected group."
Assert-Contains $form "this.tsRobotList_Start.Visible = false;" `
    "Assistant batch start must not be visible."
Assert-Contains $form "this.tsRobotList_Stop.Visible = false;" `
    "Assistant batch stop must not be visible."

Assert-Contains $robotInfo "public string RFolder" `
    "Robot folder ownership must be represented independently from instructions."
Assert-Contains $cache "CREATE TABLE IF NOT EXISTS RobotFolder" `
    "Robot folders must have independent SQLite persistence."
Assert-Contains $cache "ALTER TABLE Robot ADD COLUMN Folder" `
    "Old robot databases must receive a default folder column."
Assert-Contains $cache 'new XElement("Folders")' `
    "Robot XML export must preserve folder metadata."
Assert-Contains $cache 'xdoc.Root.Elements("Robot")' `
    "Robot XML import must remain compatible with the folder metadata node."
Assert-Contains $resources 'name="UI_Assistant"' `
    "Chinese resources must expose the Assistant label."
Assert-Contains $resources 'name="UI_AssistantEmpty"' `
    "Chinese resources must expose the empty assistant state."

Write-Output "Automation home regression checks passed."
