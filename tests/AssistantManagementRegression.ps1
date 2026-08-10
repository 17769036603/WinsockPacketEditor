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
    if (-not $Text.Contains($Expected)) {
        throw $Message
    }
}

function Assert-NotContains {
    param([string]$Text, [string]$Unexpected, [string]$Message)
    if ($Text.Contains($Unexpected)) {
        throw $Message
    }
}

$form = Read-SourceFile "WPELibrary\Socket_Form.cs"
$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$robotForm = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"
$zh = [xml](Read-SourceFile "WPELibrary\Properties\Resources.resx")
$en = [xml](Read-SourceFile "WPELibrary\Properties\Resources.en-US.resx")

Assert-Contains $form "cmsAssistantButtonMoveToGroup" `
    "Assistant menu population must use a stable menu reference instead of localized display text."
Assert-Contains $form 'this.cmsAssistantButton.Items.Add(UiText("UI_DeleteAssistant")' `
    "Assistant buttons must expose a dedicated delete command."
Assert-Contains $form "private bool TryCommitAssistantChange(Action mutation, string failureTextKey)" `
    "Assistant UI changes must share one save-and-recover path."
Assert-Contains $form "RobotList.TryApplyListChangeAndSave(mutation)" `
    "Assistant UI changes must use atomic persistence with rollback."
Assert-Contains $form "int originalCount = Socket_Cache.RobotList.lstRobot.Count" `
    "Adding an assistant must verify that a new record was actually created."
Assert-Contains $form "lstRobot.Count <= originalCount" `
    "A failed assistant creation must not modify the previous last assistant."
Assert-Contains $form "robot.IsEnable = newValue" `
    "Changing an assistant's enabled state must go through the persistent mutation path."
Assert-Contains $form "this.activeAssistantRobot != null" `
    "Assistant edits and group changes must be blocked while an assistant is running."
Assert-NotContains $form "private void SaveRobotFolderData()" `
    "Folder changes must not use a save helper that discards the result."

Assert-Contains $cache "public static bool TryApplyListChangeAndSave(Action mutation)" `
    "Robot list changes must have a shared atomic save wrapper."
Assert-Contains $cache "pair.Key.RFolder = pair.Value" `
    "Failed folder changes must restore each original assistant folder."
Assert-Contains $cache "lstFolders.Clear();" `
    "Failed folder changes must restore the original folder collection."
Assert-Contains $cache "Socket_Cache.RobotList.TryApplyListChangeAndSave" `
    "Legacy assistant delete/cleanup operations must persist and recover failures."
Assert-Contains $cache "orderedSelection = sriList.ToList()" `
    "Assistant list operations must not reverse the caller's selected list in place."
Assert-Contains $cache 'TryApplyListChangeAndSave(' `
    "Full configuration import must keep the original assistant list when persistence fails."

$saveStart = $cache.IndexOf('public static bool SaveRobotList_ToDB()', [System.StringComparison]::Ordinal)
if ($saveStart -lt 0) {
    throw "Robot save method was not found."
}
$saveBody = $cache.Substring($saveStart, [Math]::Min(1800, $cache.Length - $saveStart))
Assert-NotContains $saveBody "DeleteTable_Send" `
    "Deleting or saving assistants must not delete send presets."

Assert-Contains $robotForm 'if (!Socket_Cache.RobotList.SaveRobotList_ToDB())' `
    "Vision profile save must report persistence failures instead of showing success."
Assert-Contains $robotForm 'UiText("UI_AssistantSaveFailed")' `
    "Vision profile save failure must use the localized assistant error."
Assert-Contains $robotForm 'private void RestoreRobotEditorState(' `
    "Assistant editor save failures must restore the in-memory editor state."
Assert-Contains $robotForm 'Socket_Cache.Robot.UpdateRobot(sriSelect, RName_New, this.dtRobotInstruction);' `
    "Assistant editor changes must be committed before the persistent save."

foreach ($resource in @($zh, $en)) {
    $keys = @($resource.root.data | ForEach-Object { $_.name })
    foreach ($key in @("UI_DeleteAssistant", "UI_ConfirmDeleteAssistant", "UI_DeleteAssistantFailed", "UI_AssistantSaveFailed")) {
        if ($keys -notcontains $key) {
            throw "Missing assistant management resource: $key"
        }
    }
}

Write-Output "Assistant management regression checks passed."
