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
$robotInfo = Read-SourceFile "WPELibrary\Lib\Socket_RobotInfo.cs"
$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$resources = Read-SourceFile "WPELibrary\Properties\Resources.resx"

Assert-Contains $form "private void InitAutomationHomeUI()" `
    "The homepage automation host must be initialized explicitly."
Assert-Contains $form "ColumnCount = 2" `
    "The homepage navigation must use two columns."
Assert-Contains $form "RowCount = 2" `
    "The homepage navigation must use two rows."
Assert-Contains $form "Height = 44" `
    "Homepage and assistant buttons must use the fixed 44px height."
Assert-Contains $form "Padding = new Padding(4)" `
    "Homepage button spacing must preserve the 8px visual outer spacing."
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
Assert-Contains $form "ColumnCount = 4" `
    "Assistant buttons must use a fixed four-column grid."
Assert-Contains $form "GrowStyle = TableLayoutPanelGrowStyle.AddRows" `
    "Assistant buttons must grow vertically for more than 16 items."
Assert-Contains $form "Math.Max(1, (robots.Count + 3) / 4)" `
    "Assistant grid rows must be calculated in groups of four."
Assert-Contains $form "Socket_Cache.Robot.DoRobot(robot.RID, null)" `
    "An assistant button must start only its own assistant."
Assert-Contains $form "this.activeAssistantRobot.StopRobot()" `
    "The active assistant button must stop its own assistant."
Assert-Contains $form "pair.Value.Enabled = enabled" `
    "Other assistant buttons must be disabled while one assistant runs."
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
