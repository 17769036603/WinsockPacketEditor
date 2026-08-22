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
$robot = Read-SourceFile "WPELibrary\Lib\Socket_Robot.cs"
$c6Controller = Read-SourceFile "WPELibrary\Lib\Vision\TreasureC6ServiceController.cs"
$runtime = Read-SourceFile "WPELibrary\Lib\Vision\TreasurePacketRuntime.cs"
$treasureRunner = Read-SourceFile "WPELibrary\Lib\Vision\TreasureMapPresetRunner.cs"
$treasureLogStore = Read-SourceFile "WPELibrary\Lib\Vision\TreasureMapRunLogStore.cs"
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
Assert-Contains $form "private static bool HasTreasureMapInstruction(Socket_RobotInfo robot)" `
    "The main assistant entry must identify treasure-map instructions before starting."
Assert-Contains $form "robot.TreasureLiveSendAuthorized = true" `
    "The first treasure-map confirmation must persist live-send authorization."
Assert-NotContains $form "TreasureC6ServiceController.EnsureReadyAsync" `
    "The main assistant entry must not block ordinary robot startup on C6."
Assert-Contains $robot "EnsureTreasureC6Ready()" `
    "The treasure-map instruction must prepare C6 lazily at execution time."
Assert-Contains $robot "TreasureC6ServiceController.EnsureReadyAsync" `
    "Treasure-map execution must still prepare C6 before reading dynamic targets."
Assert-Contains $robot ": TreasureEvidenceMode.Shadow" `
    "Legacy treasure-map instructions must keep the verified Jump-to-Use path when arrival frames are unavailable."
Assert-Contains $robot "ArrivalEvidenceTimeoutMilliseconds = 1000" `
    "Production treasure-map execution must keep arrival evidence diagnostic instead of blocking Use."
Assert-Contains $robot "JumpRetryDelayMilliseconds = 0" `
    "Production treasure-map execution must not interrupt movement with a mid-route Jump."
Assert-Contains $c6Controller "topResumedActivity" `
    "C6 startup must inspect the current foreground Android application."
Assert-Contains $c6Controller "FindForegroundPackage" `
    "C6 startup must support game package changes without only relying on hardcoded names."
Assert-Contains $c6Controller "com.gdoo.yzqcxy" `
    "The current game package must remain a compatibility candidate."
Assert-Contains $form 'UiText("ByteSweep_LogStarting")' `
    "The assistant button must expose a concise starting state."
Assert-Contains $form 'UiText("ByteSweep_LogRunning")' `
    "The assistant button must expose a concise running state."
Assert-Contains $form 'UiText("UI_AssistantStartFailed")' `
    "A failed assistant start must report a visible reason."
Assert-Contains $form 'UiText("UI_TreasureC6Unavailable")' `
    "A missing treasure-map C6 reader must report a visible prerequisite."
Assert-Contains $robot '() => TreasurePacketRuntime.GetCurrentRoute()' `
    "Treasure-map execution must reuse an established current route when the hook session cache is empty."
Assert-Contains $robot 'TreasureMapRunLogStore.CreateDefault()' `
    "Treasure-map execution must initialize the persistent run log store."
Assert-Contains $robot 'persistentLog.TryAppend(entry)' `
    "Every structured treasure-map runtime entry must be persisted."
Assert-Contains $treasureLogStore 'FileShare.ReadWrite' `
    "Persistent treasure-map logs must remain readable while the assistant is running."
Assert-Contains $treasureLogStore 'DefaultMaxFileBytes' `
    "Persistent treasure-map logs must use bounded file rotation."
Assert-NotContains $treasureLogStore 'MemberIdentity' `
    "Persistent treasure-map logs must not expose C6 member identities."
Assert-Contains $robot 'bool isPacketSend =' `
    "Treasure-map runtime counters must distinguish packet sends from confirmation and retry diagnostics."
Assert-Contains $robot 'string.Equals(entry.Step, "jump", StringComparison.Ordinal)' `
    "Treasure-map send counters must include Jump results."
Assert-Contains $robot 'string.Equals(entry.Step, "auto_dig", StringComparison.Ordinal)' `
    "Treasure-map send counters must include AutoDig results."
Assert-Contains $robot 'string.Equals(entry.Step, "use", StringComparison.Ordinal)' `
    "Treasure-map send counters must retain compatibility with legacy Use results."
Assert-Contains $robot 'if (!isPacketSend)' `
    "Treasure-map confirmation and cooldown diagnostics must not inflate send-failure counters."
Assert-Contains $robot 'var route = TreasurePacketRuntime.GetCurrentRoute();' `
    "Treasure-map preflight must use the same established-route fallback as execution."
Assert-Contains $runtime "public static TreasurePacketRoute GetCurrentRoute()" `
    "Treasure-map runtime must expose an established-connection route resolver."
Assert-Contains $runtime "Socket_Cache.SocketList.ResolveCurrentRoute(candidate)" `
    "Established-route fallback must validate the current socket before sending."
Assert-Contains $treasureRunner "ReadUntilSnapshotWithRecovery" `
    "Treasure-map execution must recover a complete C6 snapshot after stream desynchronization."
Assert-Contains $treasureRunner "stale_snapshot_rejected" `
    "Treasure-map execution must classify stale C6 events for resynchronization."
Assert-Contains $treasureRunner "c6_resync_exhausted" `
    "Treasure-map execution must bound consecutive C6 recovery attempts."
Assert-Contains $treasureRunner "target_failed_skipped" `
    "Continuous treasure-map execution must skip a failed target instead of exiting."
Assert-Contains $treasureRunner "EnsureC6ConnectedWithRecovery" `
    "Continuous treasure-map execution must keep reconnecting until manually stopped."
Assert-Contains $treasureRunner "PruneTargetLedgers" `
    "Continuous treasure-map execution must release completed slots after maps leave inventory."
Assert-Contains $treasureRunner "NextTargetDelayMilliseconds" `
    "Treasure-map execution must keep a fixed inter-target cadence."
Assert-NotContains $treasureRunner "RetryBackoffFactors" `
    "Treasure-map send failures must not use long exponential backoff."
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
Assert-Contains $cache "EnsureBuiltInTreasureMapPreset" `
    "The built-in treasure-map assistant must repair an empty instruction list."
Assert-Contains $cache "CreateTreasureMapPresetInstructions" `
    "The built-in treasure-map repair must create the continuous preset instruction."

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
Assert-Contains $robotForm "TreasureLiveSendAuthorized" `
    "Assistant editor must persist the treasure-map live-send setting."

foreach ($resource in @($zh, $en)) {
    $keys = @($resource.root.data | ForEach-Object { $_.name })
    foreach ($key in @("UI_DeleteAssistant", "UI_ConfirmDeleteAssistant", "UI_DeleteAssistantFailed", "UI_AssistantSaveFailed", "UI_TreasureLiveSendConfirmTitle", "UI_TreasureLiveSendConfirm", "UI_AssistantStartingTip", "UI_AssistantRunningTip", "UI_AssistantStartFailed", "UI_TreasureC6Unavailable", "UI_TreasureRunFailed")) {
        if ($keys -notcontains $key) {
            throw "Missing assistant management resource: $key"
        }
    }
}

Write-Output "Assistant management regression checks passed."
