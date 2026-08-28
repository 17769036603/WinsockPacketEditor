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
$petReader = Read-SourceFile "WPELibrary\Lib\PetSkillBook\PetSkillBookAndroidSnapshotReader.cs"
$mountReader = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountStatusAndroidSnapshotReader.cs"
$mountRefineStateMachine = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountRefineStateMachine.cs"
$mountRefineSender = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountRefineA050SocketPacketSender.cs"
$mountRefineLogStore = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountRefineRunLogStore.cs"
$program = Read-SourceFile "WinsockPacketEditor\Lib\Program.cs"
$startupLogStore = Read-SourceFile "WinsockPacketEditor\Lib\StartupDiagnosticLogStore.cs"
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
Assert-Contains $robot "bool isXiangjuChangAnPreset = this.IsXiangjuChangAnTreasureMapPreset()" `
    "Only the Xiangju Chang'an treasure-map preset may opt into the transport protection."
Assert-Contains $robot 'name.StartsWith("相聚长安", StringComparison.Ordinal)' `
    "The Xiangju Chang'an preset selector must not match the unchanged Yizhan Qingcheng preset."
Assert-Contains $robot "JumpUseDelayMilliseconds = isXiangjuChangAnPreset ? 1800 : 300" `
    "Only the Xiangju Chang'an preset must use the conservative Jump-to-Use settling window."
Assert-Contains $robot "NextTargetDelayMilliseconds = isXiangjuChangAnPreset ? 1000 : 0" `
    "Only the Xiangju Chang'an preset must leave a settling gap before the next target."
Assert-Contains $robot "MinimumJumpIntervalMilliseconds = isXiangjuChangAnPreset ? 1800 : 800" `
    "Only the Xiangju Chang'an preset must prevent back-to-back Jump packets."
Assert-Contains $robot "StopContinuousOnTransportFailure = isXiangjuChangAnPreset" `
    "Only the Xiangju Chang'an preset must stop after an ambiguous socket write."
Assert-Contains $robot "JumpRetryDelayMilliseconds = 0" `
    "Production treasure-map execution must not interrupt movement with a mid-route Jump."
Assert-Contains $robot "RequireActionTemplateBeforeJump = isXiangjuChangAnPreset" `
    "Only the Xiangju Chang'an preset must require a complete action path before Jump."
Assert-Contains $robot "new TreasureMapRuntimePacketSender(isXiangjuChangAnPreset)" `
    "Only the Xiangju Chang'an preset may use action-specific cached-socket fallback."
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
Assert-Contains $form "HasVisionSystemInputAction" `
    "The main assistant entry must detect vision actions before starting."
Assert-Contains $form 'VisionAllowSystemInput' `
    "Vision system-input authorization must be scoped to the current assistant run."
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
Assert-Contains $treasureRunner "StopContinuousOnTransportFailure" `
    "Transport-failure stop behavior must be explicitly opt-in per treasure-map preset."
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
Assert-Contains $cache "EnsureBuiltInFirstRideRefinePreset" `
    "The first-mount refine assistant must migrate the complete legacy mount-speed plan."
Assert-Contains $cache "IsCompleteMountSpeedInstructionPlan" `
    "Mount-speed migration must validate the legacy instruction template before copying it."
Assert-Contains $cache "AppendMountSpeedPresetInstructions" `
    "An empty first-mount instruction list must be repaired with the complete mount plan."
Assert-Contains $cache "migratedFirstRideRefinePreset" `
    "First-mount migration must be persisted through the normal atomic assistant save."
Assert-Contains $cache 'sri.VisionProfile.Clone()' `
    "The main assistant entry must run a snapshot of the saved vision profile."
Assert-Contains $cache 'parameters["OwnVisionProfile"] = true' `
    "The main assistant entry must release its private vision profile snapshot."
Assert-Contains $cache 'SummonedPetSkillBookPresetSerializer.DeserializeClone' `
    "The main assistant entry must pass a snapshot of the saved pet-skill preset to the read-only preflight."
Assert-Contains $cache 'MountSpeedPresetSerializer.DeserializeClone' `
    "The main assistant entry must pass a snapshot of the saved mount preset to the read-only preflight."
Assert-Contains $cache 'parameters["MountStatusAndroidSnapshotReader"]' `
    "The main assistant entry must provide a run-scoped Android mount reader."
Assert-Contains $robot "TryPrepareVisionRuntime()" `
    "Robot startup must prepare the vision runtime before accepting a vision flow."
Assert-Contains $robot "new VisionAutoTextRecognizer" `
    "The standard assistant path must provide an OCR recognizer when the editor is not open."
Assert-Contains $robot "if (!VisionFlowRequiresOcr(profile))" `
    "Template/color vision flows must not create an OCR runtime."
$ocrRequirementText = -join ([char[]](35813, 35270, 35273, 27493, 39588, 38656, 35201, 32, 79, 67, 82, 32, 26465, 20214))
Assert-Contains $robot $ocrRequirementText `
    "Only vision steps that actually use OCR may require a recognizer."
Assert-Contains $robot "DisposeOwnedVisionResources" `
    "Vision runtime resources must be released when the assistant exits."
Assert-Contains $robot "TryPrepareSummonedPetSkillBookRuntime()" `
    "Pet-skill assistants must read the current pet skill snapshot before execution."
Assert-Contains $robot "TryPrepareMountSpeedRuntime()" `
    "Mount assistants must refresh the current mount snapshot before execution."
Assert-Contains $robot "MountSkillPresetPreview.TryEvaluate(" `
    "Mount assistants must evaluate configured target skills against the fresh snapshot."
Assert-Contains $robot "TryPrepareMountRefineRuntime(" `
    "Mount assistants must prepare the four-field refine runtime from the fresh startup snapshot."
Assert-Contains $robot "MountRefineA050CaptureBinding.TryCreateFromCurrentCapture(" `
    "Mount assistants must rebuild the A050 outbound route from the current capture after injection."
Assert-Contains $robot 'this._parameters["MountRefineProtocolVerified"] = true' `
    "A valid current A050 capture must complete protocol acceptance for the active mount-refine run."
Assert-Contains $robot "MountRefineLiveSendAuthorization.Create(" `
    "An explicitly started first-mount refine run must receive its scoped live-send authorization after capture validation."
Assert-Contains $robot 'this._parameters["MountRefineA050AutoVerified"] = true' `
    "Automatic A050 acceptance must be marked as run-scoped diagnostic state."
Assert-Contains $cache 'parameters["MountRefineAutoSendRequested"] = true' `
    "The first-mount refine entry must request automatic send without manual protocol fields."
Assert-Contains $robotForm 'parameters["MountRefineAutoSendRequested"] = true' `
    "The dedicated mount assistant execute entry must request automatic send without manual protocol fields."
Assert-Contains $robot "RunMountRefinePlanInstruction(mountPreset)" `
    "Complete mount refine targets must enter the refine state machine from the IDLE plan step."
Assert-Contains $robot "LastMountRefineResultMessage" `
    "Mount refine execution results must be exposed to the assistant UI."
Assert-Contains $robot 'this._parameters.Remove("MountStatusReadOnlySnapshot")' `
    "Mount startup must discard a snapshot from an earlier run."
Assert-Contains $robotForm "MountStatusAndroidSnapshotReader" `
    "Mount assistant startup must provide a run-scoped Android read-only reader."
Assert-Contains $robotForm 'parameters["MountSpeedPreset"]' `
    "Mount assistant startup must pass a run-scoped mount preset snapshot."
Assert-Contains $robotForm "InitMountSpeedLayout()" `
    "Mount assistants must expose a dedicated target-skill editor tab."
Assert-Contains $robotForm "bSaveMountPreset_Click" `
    "Mount target skills must have an explicit local save action."
Assert-Contains $robotForm "bRefreshMountSnapshot_Click" `
    "Mount target skills must expose a read-only current-snapshot preview."
Assert-Contains $form "LastMountRefineResultMessage" `
    "The main assistant completion path must display the mount refine result."
Assert-Contains $robot "new PetSkillBookAndroidSnapshotReader" `
    "Pet-skill startup must use the existing read-only memory snapshot reader."
Assert-Contains $robot "IsReadOnlySummonedPetSkillBookFlow()" `
    "A successful pet-skill snapshot must enter the one-shot read-only observation flow."
Assert-Contains $robot "ReadOnlySnapshotStatus" `
    "The read-only observation result must be available to the assistant UI."
Assert-Contains $form 'UI_AssistantReadOnlySnapshotReady' `
    "A successful read-only pet snapshot must not be shown as an assistant failure."
Assert-Contains $petReader "ShouldRetryWithRootShell(reader)" `
    "A read-only pet snapshot must retry a process-memory permission failure through the root shell."
Assert-Contains $petReader "BuildRootShellCommand(readerCommand)" `
    "The root retry must remain an explicit read-only probe command."
Assert-Contains $petReader "proc_mem_open_failed" `
    "The pet snapshot reader must recognize the probe's process-memory permission failure."
Assert-Contains $mountReader '"XNAS", "WPE", "vision-worker", "python", "python.exe"' `
    "The mount snapshot reader must find the installed WPE Python runtime."
Assert-Contains $mountReader "FindOnPath(candidate)" `
    "The mount snapshot reader must resolve Python executables from PATH instead of assuming python.exe is launchable."
Assert-Contains $mountReader "TransientProbeRetryCount" `
    "The mount snapshot reader must retry a transient LuaJIT discovery miss once before failing startup."
Assert-Contains $mountReader "IsTransientDiscoveryFailure(probe.Output)" `
    "The mount snapshot reader must only retry recognized transient discovery diagnostics."
Assert-Contains $mountReader "cancellationToken.WaitHandle.WaitOne(TransientProbeRetryDelayMilliseconds)" `
    "The mount snapshot retry delay must remain cancellation-aware."
Assert-Contains $mountReader '"local_player_missing"' `
    "The mount snapshot reader must recognize a temporarily missing local-player object."
Assert-Contains $mountReader "IsTransientMountedSnapshot(snapshot)" `
    "A schema-valid but not-yet-bound mounted snapshot must be retried before assistant startup fails."
Assert-Contains $mountReader "TransientMountedSnapshotRetryCount" `
    "Mounted-snapshot stability retries must remain bounded."
Assert-Contains $robot "ReadMountPreflightSnapshot(" `
    "Mount-refine startup must re-read an incomplete 21-card snapshot before failing closed."
Assert-Contains $robot '"a050_binding_retry"' `
    "Automatic mount refine must wait briefly for the current capture queue before rejecting A050 binding."
Assert-Contains $robot '"a050_protocol_accepted"' `
    "Automatic A050 acceptance must be written to the persistent mount-refine log."
Assert-Contains $mountRefineSender "candidate.Packet.PacketSocket" `
    "Duplicate send/WSASend observations on one Socket must be treated as one A050 route."
Assert-NotContains $mountRefineSender "candidate.Route.PacketType," `
    "The A050 ambiguity key must not split one Socket solely by wrapper API."
Assert-Contains $mountRefineStateMachine '"send_result"' `
    "Every mount-refine send attempt must emit a structured result diagnostic."
Assert-Contains $mountRefineStateMachine "IMountRefinePacketSenderDiagnostics" `
    "Mount-refine failures must expose the sender's actual error code and native result."
Assert-Contains $mountRefineLogStore 'FileShare.ReadWrite' `
    "Persistent mount-refine logs must remain readable from CLI while the assistant is running."
Assert-Contains $mountRefineLogStore 'DefaultMaxFileBytes' `
    "Persistent mount-refine logs must use bounded rotation."
Assert-NotContains $mountRefineLogStore 'PacketBuffer' `
    "Persistent mount-refine logs must not record raw packet payloads."
Assert-Contains $program 'WriteStartupLog("process_started"' `
    "Startup diagnostics must begin before database initialization and elevation hand-off."
Assert-Contains $program 'startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory' `
    "The elevated process must start from the installed application directory."
Assert-Contains $program 'elevatedProcess.WaitForExit(1500)' `
    "An elevated child that exits immediately must no longer fail silently."
if ($program.IndexOf('Socket_Cache.DataBase.InitDB()', [System.StringComparison]::Ordinal) -lt
    $program.IndexOf('if (isAdministrator)', [System.StringComparison]::Ordinal)) {
    throw "The non-elevated ClickOnce launcher must not open or initialize the user database."
}
Assert-Contains $startupLogStore 'FileShare.ReadWrite' `
    "Startup diagnostics must be readable from CLI while either launcher process is alive."
Assert-Contains $startupLogStore 'startup.jsonl' `
    "Startup diagnostics must use a stable local log path."

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
    foreach ($key in @("UI_DeleteAssistant", "UI_ConfirmDeleteAssistant", "UI_DeleteAssistantFailed", "UI_AssistantSaveFailed", "UI_TreasureLiveSendConfirmTitle", "UI_TreasureLiveSendConfirm", "UI_AssistantStartingTip", "UI_AssistantRunningTip", "UI_AssistantStartFailed", "UI_AssistantRunFailed", "UI_AssistantReadOnlySnapshotReady", "UI_TreasureC6Unavailable", "UI_TreasureRunFailed")) {
        if ($keys -notcontains $key) {
            throw "Missing assistant management resource: $key"
        }
    }
}

Write-Output "Assistant management regression checks passed."
