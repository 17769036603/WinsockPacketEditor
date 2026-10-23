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

$program = Read-SourceFile "WinsockPacketEditor\Lib\Program.cs"
$startupStore = Read-SourceFile "WinsockPacketEditor\Lib\StartupDiagnosticLogStore.cs"
$robot = Read-SourceFile "WPELibrary\Lib\Socket_Robot.cs"
$reader = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountStatusAndroidSnapshotReader.cs"
$probe = Read-SourceFile "tools\mount-reader\mount_status_luajit_probe.py"
$sender = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountRefineA050SocketPacketSender.cs"
$packetTemplate = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountRefineA050PacketTemplate.cs"
$shapeResolver = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountRideShapeResolver.cs"
$socketCache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$socketForm = Read-SourceFile "WPELibrary\Socket_Form.cs"
$stateMachine = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountRefineStateMachine.cs"
$harness = Read-SourceFile "tests\MountSpeedRunnerHarness\Program.cs"
$probeRegression = Read-SourceFile "tests\MountStatusLuaJitProbeRegression.py"
$runStore = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountRefineRunLogStore.cs"
$mainProject = Read-SourceFile "WinsockPacketEditor\WinsockPacketEditor.csproj"
$libraryProject = Read-SourceFile "WPELibrary\WPELibrary.csproj"

Assert-Contains $program 'WriteStartupLog("process_started"' `
    "Startup logging must begin before elevation and database initialization."
Assert-Contains $program 'startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory' `
    "Elevation must start from the installed application directory."
Assert-Contains $program 'elevatedProcess.WaitForExit(1500)' `
    "An elevated child that exits immediately must be reported instead of failing silently."
$adminBranch = $program.IndexOf('if (isAdministrator)', [System.StringComparison]::Ordinal)
$databaseInit = $program.IndexOf('Socket_Cache.DataBase.InitDB()', [System.StringComparison]::Ordinal)
if ($adminBranch -lt 0 -or $databaseInit -lt $adminBranch) {
    throw "The non-elevated ClickOnce launcher must not initialize the user database."
}
Assert-Contains $startupStore 'FileShare.ReadWrite' `
    "Startup logs must remain readable from CLI while the process is running."
Assert-Contains $mainProject 'Lib\StartupDiagnosticLogStore.cs' `
    "The startup diagnostic store must be compiled into the main application."

Assert-Contains $reader 'IsTransientMountedSnapshot(snapshot)' `
    "A mounted snapshot whose active ride has not converged must be retried."
Assert-Contains $reader 'TransientMountedSnapshotRetryCount' `
    "Mounted-snapshot retries must remain bounded."
Assert-Contains $reader 'WasCancelled' `
    "Android snapshot cancellation must be distinguishable from a read failure."
Assert-Contains $reader 'while (!process.WaitForExit(100))' `
    "Android bridge process waits must remain cancellation-responsive."
Assert-Contains $reader 'TerminateProcess(process)' `
    "Android bridge must terminate a child process after cancellation or timeout."
Assert-Contains $reader 'requireCompleteRefineCards' `
    "Mount-refine reads must explicitly require the complete 21-card snapshot."
Assert-Contains $reader 'IsCacheDiscoveryFallbackRequired(probe.Output)' `
    "An incomplete cached snapshot must fall back to full discovery."
Assert-Contains $reader 'CachedRefineCardRetryCount' `
    "Transient cached refine-card gaps must get bounded fast retries before discovery."
Assert-Contains $reader 'CachedRefineCardRetryDelayMilliseconds' `
    "Cached refine-card retries must use a short bounded delay."
Assert-Contains $reader 'IsCachedRefineCardsIncomplete(probe.Output)' `
    "Only the transient incomplete-card cache result may use the fast retry path."
Assert-Contains $reader 'HasCompleteRefineCards(snapshot)' `
    "A cached snapshot with incomplete refine cards must not become reusable state."
Assert-Contains $reader 'preferredRefineRideInstanceId' `
    "A preset A050 ride instance must reach every Android snapshot read path."
Assert-Contains $reader '"refine_selected"' `
    "The desktop reader must distinguish the selected refine ride from the mounted ride."
Assert-Contains $reader 'ReadProbeDiagnostics(json)' `
    "Probe read-path and phase timing must reach the desktop reader."
Assert-Contains $reader '!IsProbeWorkerResponseTimeout(probe)' `
    "A timed-out long-lived discovery must not repeat the same scan in one-shot mode."
Assert-Contains $probe 'def stable_snapshot_session_id(' `
    "Android snapshots must use a stable session identity for one process lifetime."
Assert-Contains $probe '"sessionId": stable_snapshot_session_id(pid, identity)' `
    "Android snapshot results must use the stable process session identity."
Assert-NotContains $probe '"sessionId": str(uuid.uuid4())' `
    "A fresh random session ID per snapshot would make refresh validation fail."
Assert-Contains $probe 'cached_refine_cards_incomplete' `
    "Cache-only reads must reject an incomplete 21-card refine snapshot."
Assert-Contains $probe 'has_complete_ride_refine_cards' `
    "The probe must have one explicit complete-card validation."
Assert-Contains $probe 'WARM_HINT_SCHEMA' `
    "Cross-process warm hints must use a separate versioned sidecar schema."
Assert-Contains $probe 'build_probe_warm_hints_from_cache' `
    "Successful plans must persist relative warm-start hints."
Assert-Contains $probe 'warm_cache = build_warm_cache_from_hints' `
    "Cross-process relative hints must be revalidated in the current process before use."
Assert-Contains $probe 'Absolute point addresses stay process-local.' `
    "Point addresses must stay scoped to one verified process identity."
Assert-Contains $probe 'def select_cold_priority_ranges(' `
    "Cold discovery must have a bounded small-mapping priority pass."
Assert-Contains $probe 'readPath": resolved_read_path' `
    "Probe diagnostics must identify the bounded read path."
Assert-Contains $probe 'timingMs' `
    "Probe diagnostics must expose bounded phase timings."
Assert-Contains $robot 'GetParameter("MountRefineIntervalMs", 250)' `
    "Mount-refine refresh polling must default to the optimized 250ms interval."
Assert-Contains $probe 'ROOT_BROKER_BATCH_TIMEOUT_SECONDS' `
    "Root-broker batch reads must not use the short single-read timeout."
Assert-Contains $probe 'max(ROOT_BROKER_BATCH_TIMEOUT_SECONDS, timeout)' `
    "Root-broker batch reads must use a bounded batch timeout."
Assert-Contains $probe 'ROOT_BROKER_MAX_RAW_BATCH_BYTES' `
    "Root-broker chunk batches must cap raw bytes, not only command count."
Assert-Contains $probe 'batch_bytes + size > ROOT_BROKER_MAX_RAW_BATCH_BYTES' `
    "Root-broker batching must enforce the raw-byte budget before adding a chunk."
Assert-Contains $probe 'ROOT_BROKER_CONNECT_RETRY_SECONDS' `
    "The read-only probe must tolerate the broker pipe recreation window."
Assert-Contains $probe 'while self._pipe is None:' `
    "The broker client must retry connection establishment before closing the session."
Assert-Contains $probe 'io.BufferedRWPair(' `
    "The broker client must buffer large JSONL responses instead of reading byte by byte."
Assert-Contains $probe 'ROOT_BROKER_PIPE_BUFFER_BYTES' `
    "The broker pipe buffer size must be explicit and regression protected."
Assert-Contains $probe 'missing_fields: list[str] | None = None' `
    "Field discovery failures must report the missing required fields."
Assert-Contains $probe 'missingFields' `
    "The desktop parser must surface missing probe fields in its diagnostic."
Assert-Contains $probe 'select_warm_hint_priority_ranges' `
    "Cross-process hints must define only a bounded first-pass scan."
Assert-Contains $probe 'luajit_gc64_warm_priority' `
    "Successful priority scans must be visible in probe diagnostics."
Assert-Contains $probe 'requireCompleteRefineCards' `
    "The long-lived probe must receive the strict refine-card requirement."
Assert-Contains $probe 'def bind_refine_ride_instance(' `
    "The read-only probe must support an explicitly selected refine-page ride."
Assert-Contains $probe '"LocalRide.m_RideIns+PresetA050"' `
    "Selected refine-ride binding must retain both live-page and preset-packet evidence."
Assert-Contains $robot 'ReadMountPreflightSnapshot(' `
    "An incomplete 21-card refine snapshot must be re-read before failing."
Assert-Contains $robot '"MountRefinePreferredRideInstanceId"' `
    "The validated preset ride instance must be retained for refresh reads."
Assert-Contains $robot 'GetParameter("MountRefineMaxAttempts", 0)' `
    "Mount-refine must default to no attempt-count auto-stop."
Assert-Contains $robot '"a050_binding_retry"' `
    "A050 binding must wait briefly for the current capture queue."
Assert-Contains $robot '"a050_protocol_accepted"' `
    "Run-scoped automatic A050 acceptance must be persisted."
Assert-Contains $robot 'mount_refine_stop_requested' `
    "A robot stop request must be persisted before cancelling an active mount-refine read."
Assert-Contains $sender 'candidate.Packet.PacketSocket' `
    "One Socket observed through multiple send APIs must remain one route."
Assert-NotContains $sender 'candidate.Route.PacketType,' `
    "Wrapper API must not be part of the A050 route ambiguity key."
Assert-Contains $socketCache 'ObserveMountRefineA050Evidence(spi)' `
    "Validated A050 evidence must be observed before the bounded UI list can auto-clear."
Assert-Contains $socketCache 'ObserveCurrentSocketRouteEvidence(spi)' `
    "Generic preset routes must be retained before SpeedMode bypasses the UI queue."
Assert-Contains $socketCache 'TreasurePacketRuntime.ObserveCapturedPacket(spi)' `
    "Protected preset protocol state must be observed before SpeedMode bypasses the UI queue."
$observeIndex = $socketCache.IndexOf('SocketList.ObserveMountRefineA050Evidence(spi)', [System.StringComparison]::Ordinal)
$routeObserveIndex = $socketCache.IndexOf('SocketList.ObserveCurrentSocketRouteEvidence(spi)', [System.StringComparison]::Ordinal)
$treasureObserveIndex = $socketCache.IndexOf('TreasurePacketRuntime.ObserveCapturedPacket(spi)', [System.StringComparison]::Ordinal)
$speedModeIndex = $socketCache.IndexOf('if (!Socket_Cache.SocketPacket.SpeedMode)', [System.StringComparison]::Ordinal)
if ($observeIndex -lt 0 -or
    $routeObserveIndex -lt 0 -or
    $treasureObserveIndex -lt 0 -or
    $speedModeIndex -lt 0 -or
    $observeIndex -gt $speedModeIndex -or
    $routeObserveIndex -gt $speedModeIndex -or
    $treasureObserveIndex -gt $speedModeIndex) {
    throw "Session route and protocol evidence must be observed before SpeedMode bypasses the UI queue."
}
Assert-Contains $socketCache 'CaptureCurrentSocketRouteEvidence()' `
    "Generic current-session route evidence must be available independently from the visible list."
Assert-Contains $socketCache 'CaptureMountRefineA050Evidence()' `
    "Current-session A050 evidence must be available independently from the visible list."
Assert-Contains $socketCache 'StopCaptureSessionPreservingRoutes()' `
    "Stopping capture must preserve the current-session route for preset sends."
Assert-Contains $socketCache 'CaptureSessionActive' `
    "Capture lifecycle must distinguish an active recording session from retained route evidence."
Assert-Contains $socketCache 'mountRefineA050Evidence.Clear()' `
    "Starting a new injection session must discard previous A050 evidence."
Assert-Contains $socketCache 'capturedPackets.AddRange(CaptureMountRefineA050Evidence())' `
    "Current-route resolution must survive UI-list auto-clear for A050 sends."
Assert-Contains $socketCache 'new[] { "com.gdoo.dhxy", "com.gdoo.xxxy2026" })' `
    "The mount-refine reader must keep both known client packages as fallback candidates, not the environment variable alone."
Assert-Contains $harness 'Mount refine fallback candidates are incorrect' `
    "The mount runner harness must lock the refine package candidate order."
Assert-Contains $sender 'Socket_Cache.SocketList.CaptureMountRefineA050Evidence()' `
    "A050 binding must consume current-session evidence as well as visible rows."
Assert-Contains $sender 'capturedPacket.PacketSocket' `
    "A050 binding must preserve the selected packet's original Socket."
Assert-Contains $sender '_routeTemplate.PacketSocket' `
    "A050 sender must use the Socket from the selected packet route."
Assert-NotContains $sender 'ResolveCurrentRoute(' `
    "A050 sender must not silently switch to another route at send time."
Assert-Contains $packetTemplate 'packet = (byte[])_fixtureBytes.Clone();' `
    "A050 template must build the send copy from a clone of the fixed fixture."
Assert-Contains $packetTemplate 'public string RideInstanceId' `
    "A validated A050 template must expose its immutable ride instance for read binding."
Assert-NotContains $packetTemplate 'Buffer.BlockCopy(encodedId' `
    "A050 template must rewrite the tail with one explicit offset loop, not a bulk copy."
Assert-Contains $packetTemplate 'packet[RideInstanceIdOffset + index] = encodedRideInstanceId[index];' `
    "A050 TryBuild must rewrite only the 19 ride-instance bytes at offsets 20-38."
Assert-Contains $packetTemplate 'TryValidateFrame(packet, out error)' `
    "A050 TryBuild must re-run the full structural validation on the rewritten frame."
Assert-Contains $packetTemplate 'frame[19] != RideInstanceIdLength' `
    "A050 structural validation must keep the offset-19 0x13 selector check."
Assert-Contains $packetTemplate 'mount_refine_a050_active_ride_instance_id_invalid' `
    "A050 TryBuild must reject a missing or illegal current ride instance id instead of reusing the fixture id."
Assert-NotContains $packetTemplate '_fixtureBytes[' `
    "A050 TryBuild must never write into the immutable template fixture bytes."
Assert-Contains $robot 'MountRideShapeResolver.RequiresShapeResolution(' `
    "The startup chain must gate the shape lookup on the optional preset target shape."
Assert-Contains $robot 'preset.TargetRideShapeId' `
    "The startup chain must read the optional target ride shape from the preset."
Assert-Contains $robot 'TryResolvePreferredRideInstanceId(' `
    "The startup chain must use the pure shape resolver for the refine preferred instance id."
Assert-Contains $robot 'preferredRefineRideInstanceId = resolvedRefineRideInstanceId;' `
    "The shape-resolved current instance id must become the refine binding instance."
Assert-Contains $robot 'shapeReadResult = reader.ReadSnapshotAsync(' `
    "Shape lookup must use one read-only snapshot before the strict refine preflight read."
Assert-Contains $robot 'mount_ride_shape_snapshot_invalid' `
    "A failed shape-resolution snapshot must stop the start with a diagnostic."
Assert-Contains $robot 'preferredRefineRideInstanceId = configuredTemplate.RideInstanceId;' `
    "Presets without a target shape must keep the packet ride instance id unchanged."
Assert-Contains $shapeResolver 'public static bool TryResolveRideInstanceId(' `
    "The ride shape resolver must expose a pure single-shape lookup."
Assert-Contains $shapeResolver 'public static bool RequiresShapeResolution(' `
    "The ride shape resolver must state whether a snapshot read is required."
Assert-Contains $shapeResolver 'matchCount > 1' `
    "The ride shape resolver must fail closed on duplicate shapes, even with identical instance ids."
Assert-Contains $shapeResolver 'mount_ride_shape_ambiguous' `
    "Duplicate shapes must produce an explicit ambiguous error code."
Assert-Contains $shapeResolver 'mount_ride_shape_no_match' `
    "A missing shape match must produce an explicit error code."
Assert-Contains $shapeResolver 'mount_ride_shape_id_invalid' `
    "An illegal target shape id must produce an explicit error code."
Assert-Contains $harness 'TestMountRideShapeResolutionAndTailRewrite' `
    "The harness must cover shape resolution and the dynamic A050 tail rewrite."
Assert-Contains $harness 'AssertA050TailRewrite' `
    "The harness must compare the rewritten A050 frame position by position."
Assert-Contains $harness 'mount_refine_a050_active_ride_instance_id_invalid' `
    "The harness must lock the explicit rejection of a missing or illegal ride instance id."
Assert-Contains $sender 'speedMode={3}' `
    "A050 binding diagnostics must expose whether high-speed capture bypassed the UI list."
Assert-Contains $sender 'a050_protocol_not_matched' `
    "A050 binding diagnostics must identify protocol-header rejection categories."
Assert-Contains $sender 'scanTotal={0}' `
    "A050 binding diagnostics must report the scanned packet count."
Assert-Contains $stateMachine '"send_result"' `
    "Every mount-refine send attempt must emit a structured result."
Assert-Contains $stateMachine 'IMountRefinePacketSenderDiagnostics' `
    "Sender-native failure details must reach the execution result and log."
Assert-Contains $stateMachine 'SelectedRefineRideInstanceId' `
    "The state machine must receive an explicit per-preset selected-ride binding."
Assert-Contains $stateMachine '"refine_selected"' `
    "The state machine must recognize the explicitly selected refine-page binding."
Assert-Contains $stateMachine 'snapshot.ActiveRideInstanceId,' `
    "Selected refine-page validation must compare the active ride instance."
Assert-Contains $robot 'Socket_Cache.Robot.FourthRideRefinePresetName' `
    "The exact fourth-ride preset must retain its independent selected-ride state-machine path."
Assert-Contains $robot 'Socket_Cache.Robot.ThirdRideRefinePresetName' `
    "Only the exact third-ride preset may enable its selected-ride state-machine path."
Assert-Contains $robot 'SelectedRefineRideInstanceId = selectedRefineRideInstanceId' `
    "The exact third- or fourth-ride instance must reach state-machine validation."

# 配置了坐骑形态的预设必须与三/四坐骑洗炼共用同一个 refine_selected 绑定入口：
# 预检按 refine_selected 通过后，状态机不得再退回 bound 分支（即不得再要求骑乘状态）。
$refinePlanStart = $robot.IndexOf(
    'private void RunMountRefinePlanInstruction(',
    [System.StringComparison]::Ordinal)
$refinePlanEnd = $robot.IndexOf(
    'private WPELibrary.Lib.MountSpeed.IMountRefinePacketSender',
    $refinePlanStart,
    [System.StringComparison]::Ordinal)
if ($refinePlanStart -lt 0 -or $refinePlanEnd -le $refinePlanStart) {
    throw "Could not isolate the mount-refine plan instruction method."
}
$refinePlanSource = $robot.Substring($refinePlanStart, $refinePlanEnd - $refinePlanStart)
Assert-Contains $refinePlanSource 'Socket_Cache.Robot.ThirdRideRefinePresetName' `
    "The exact third-ride preset name branch must be retained."
Assert-Contains $refinePlanSource 'Socket_Cache.Robot.FourthRideRefinePresetName' `
    "The exact fourth-ride preset name branch must be retained."
Assert-Contains $refinePlanSource 'StringComparison.Ordinal' `
    "The ride-preset name comparison must stay an exact ordinal match."
Assert-Contains $refinePlanSource 'RequiresShapeResolution(' `
    "A preset with a target ride shape must also bind through the selected refine ride."
Assert-Contains $refinePlanSource 'preset.TargetRideShapeId' `
    "The selected refine binding must be keyed on the preset's optional target ride shape."
Assert-Contains $refinePlanSource 'MountRefinePreferredRideInstanceId' `
    "The selected refine binding must reuse the preflight-resolved preferred ride instance id."
Assert-NotContains $refinePlanSource 'StartsWith(' `
    "The ride-preset name gate must not be broadened to a prefix match."
Assert-NotContains $refinePlanSource 'IndexOf(robotName' `
    "The ride-preset name gate must not be broadened to a substring match."
Assert-NotContains $refinePlanSource 'robotName.Contains' `
    "The ride-preset name gate must not be broadened to a substring match."

Assert-Contains $stateMachine 'readResult.WasCancelled' `
    "Android snapshot cancellation must be classified as a user/host stop."
Assert-Contains $stateMachine 'long baselineSequence' `
    "Refresh verification must retain the pre-send sequence baseline."
Assert-Contains $stateMachine 'readResult.Snapshot.Sequence <= baselineSequence' `
    "A non-advancing snapshot sequence must remain outside verification."
Assert-Contains $stateMachine 'mount_refine_snapshot_sequence_not_advanced_waiting' `
    "A non-advancing sequence must be logged as a recoverable wait."
Assert-Contains $stateMachine 'keep waiting without target evaluation or resend.' `
    "A non-advancing sequence must not trigger target evaluation or resend."
Assert-Contains $stateMachine 'if (!HasChangedRefineCards(previousSnapshot, readResult.Snapshot))' `
    "A newer read/request sequence must not replace the refine-card content-change proof."
Assert-Contains $stateMachine 'mount_refine_snapshot_cards_not_changed_waiting' `
    "A newer sequence with unchanged cards must be logged as a recoverable wait."
Assert-Contains $stateMachine 'retryWindow - lastSequenceWaitLogWindow >= 10' `
    "Repeated stale-sequence diagnostics must be rate limited across confirmation windows."
Assert-Contains $stateMachine 'retryWindow - lastContentWaitLogWindow >= 10' `
    "Repeated unchanged-card diagnostics must be rate limited across confirmation windows."
Assert-NotContains $stateMachine 'Sequence++' `
    "The state machine must not manufacture a fresh snapshot sequence."
Assert-NotContains $stateMachine '_snapshotSource is MountAndroidRefineSnapshotSource' `
    "Sequence protection must cover point, fast, and fallback snapshot paths equally."
Assert-NotContains $reader 'mount_refine_snapshot_sequence_not_advanced' `
    "The Android reader must not duplicate the state-machine sequence termination gate."
Assert-NotContains $probe 'mount_refine_snapshot_sequence_not_advanced' `
    "The Python point/fast/fallback reader must not turn sequence comparison into a terminal error."
Assert-Contains $probe 'probe_runtime.clear_point_plan()' `
    "An invalid Python point plan must fall back instead of manufacturing or accepting a stale result."
Assert-Contains $stateMachine 'MountRefineStopReason.UserStopped' `
    "Cancelled snapshot reads must finish with the user/host stop reason."
Assert-Contains $stateMachine '"refresh_wait_retry"' `
    "A single refresh timeout must be logged as a recoverable wait retry."
Assert-Contains $stateMachine 'mount_refine_result_timeout_retrying' `
    "A refresh timeout must not terminate the preset run."
Assert-Contains $stateMachine 'public int SnapshotReadTimeoutMs { get; set; } = 120000;' `
    "A full safe-discovery read must have an independent bounded timeout."
Assert-NotContains $stateMachine 'Math.Min(remainingMs, _configuration.SnapshotReadTimeoutMs)' `
    "The short confirmation window must not cancel a legitimate snapshot discovery."
Assert-Contains $harness 'DelayedChangedSnapshotSource' `
    "The harness must cover a snapshot read that outlives one confirmation window."
Assert-Contains $probeRegression 'selected_binding_candidate' `
    "The offline probe regression must cover a selected ride that is not currently mounted."
Assert-Contains $harness 'delayedReadSource.CancelledReadCount != 0' `
    "The harness must prove the slow snapshot read was not cancelled and restarted."
Assert-Contains $stateMachine 'sendToVerifyStopwatch' `
    "Refresh verification must measure the accepted-send to new-snapshot segment."
Assert-Contains $harness 'TestMountRefineSnapshotSequenceWaitAsync' `
    "The harness must cover repeated non-advancing sequence waits."
Assert-Contains $harness 'TestFourthRideSelectedRefineBindingAsync' `
    "The harness must cover the exact fourth-ride selected binding."
Assert-Contains $harness 'TestThirdRideSelectedRefineBindingAsync' `
    "The harness must cover the exact third-ride selected binding."
Assert-Contains $harness 'Non-target second-ride preset inherited selected binding' `
    "The harness must prove a non-target ride preset is not broadened."
Assert-Contains $harness 'string[] readPaths = { "point", "fast", "fallback" }' `
    "The harness must cover every optimized Android read path."
Assert-Contains $harness 'source.ReadCount < 11' `
    "The harness must prove repeated old-sequence and unchanged-card reads occurred before recovery."
Assert-Contains $harness 'waitingMachine.State != WPELibrary.Lib.MountSpeed.MountRefineState.WaitingForRefresh' `
    "The harness must observe repeated same-sequence reads while the state machine is still waiting."
Assert-Contains $harness 'waitingTask.IsCompleted' `
    "The harness must prove the same-sequence wait has not completed before a changed snapshot is released."
Assert-Contains $harness 'waitingSender.Requests.Count != 1' `
    "The harness must prove the same-sequence wait never resends."
Assert-Contains $harness 'advancedUnchanged' `
    "The harness must cover a newer sequence whose 21-card content has not changed."
Assert-Contains $harness 'mount_refine_snapshot_cards_not_changed_waiting' `
    "The harness must assert the distinct unchanged-card wait diagnostic."
Assert-Contains $harness 'fixture_read_failed' `
    "The harness must prove a real read failure remains fail-closed."
Assert-Contains $harness 'MountRefineStopReason.MountChanged' `
    "The harness must prove a mount-context change remains terminal."
Assert-Contains $harness 'BuildMountedRefineFixtureJson(1.055, 2, 20)' `
    "The harness must prove an incomplete refresh snapshot remains fail-closed."
Assert-Contains $probeRegression 'for read_path in ("point", "fast", "fallback")' `
    "Python snapshot diagnostics must retain all supported read-path labels."
Assert-Contains $socketForm 'StopCaptureSessionPreservingRoutes()' `
    "Stopping the hook must retain the current preset route after capture ends."
$stopStart = $socketForm.IndexOf('private void StopHook_MainForm()', [System.StringComparison]::Ordinal)
$stopEnd = $socketForm.IndexOf('#endregion', $stopStart, [System.StringComparison]::Ordinal)
if ($stopStart -lt 0 -or $stopEnd -le $stopStart) {
    throw "Could not isolate the stop-hook lifecycle method."
}
$stopSource = $socketForm.Substring($stopStart, $stopEnd - $stopStart)
if ($stopSource.Contains('TreasurePacketRuntime.EndSession()')) {
    throw "Stopping capture must not clear the current protected-preset protocol session."
}
Assert-Contains $runStore 'FileShare.ReadWrite' `
    "Mount-refine logs must remain readable from CLI during a run."
Assert-Contains $runStore 'DefaultMaxFileBytes' `
    "Mount-refine logs must use bounded rotation."
Assert-Contains $runStore 'sendToVerifyMs' `
    "Mount-refine logs must persist the accepted-send to verification timing."
Assert-NotContains $runStore 'PacketBuffer' `
    "Mount-refine logs must not record raw packet payloads."
Assert-Contains $libraryProject 'Lib\MountSpeed\MountRefineRunLogStore.cs' `
    "The mount-refine run log store must be compiled into WPELibrary."

Write-Host "Mount-refine complete-fix regression checks passed."
