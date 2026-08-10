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
    param(
        [string]$Text,
        [string]$Expected,
        [string]$Message
    )

    if (-not $Text.Contains($Expected)) {
        throw $Message
    }
}

function Assert-NotContains {
    param(
        [string]$Text,
        [string]$Unexpected,
        [string]$Message
    )

    if ($Text.Contains($Unexpected)) {
        throw $Message
    }
}

$socketForm = Read-SourceFile "WPELibrary\Socket_Form.cs"
$socketCache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
Assert-NotContains $socketForm "Socket_Cache.System.IsRemote = false;" `
    "Startup must not discard the persisted remote-management configuration."
Assert-Contains $socketForm "Socket_Operation.StartRemoteMGT();" `
    "Startup must start the configured HTTPS remote-management service."

$byteSweepForm = Read-SourceFile "WPELibrary\Socket_Form.ByteSweep.cs"
Assert-Contains $byteSweepForm "await Task.Delay(preset.BNextInterval, this.byteSweepCts.Token);" `
    "Byte-sweep next-item delay must be asynchronous and cancellable."
Assert-NotContains $byteSweepForm "Token.WaitHandle.WaitOne(preset.BNextInterval)" `
    "Byte-sweep next-item delay must not block the UI thread."

$sendForm = Read-SourceFile "WPELibrary\Socket_SendForm.cs"
Assert-Contains $sendForm "for (int loop = 0; loop < loopCount; loop++)" `
    "The single-packet sweep page must execute every configured loop."
Assert-Contains $sendForm "progress.TotalSend += completedSend;" `
    "Multi-loop sweep progress must keep cumulative send totals."
Assert-Contains $sendForm "this.hbPacketData.ReadOnly = true;" `
    "Packet bytes must be locked against editing while a send is running."
Assert-Contains $sendForm 'Name = "bAdvancedPairEditorToggle"' `
    "The two-byte editor must reserve a stable advanced-editor header."
Assert-Contains $sendForm "this.tableLayoutPanel1.Visible = false;" `
    "The byte type detail panel must be hidden from the send page."
Assert-Contains $sendForm "private bool byteSweepEditorDirty;" `
    "Sweep editor changes must have an explicit dirty state."
Assert-Contains $sendForm "ConfirmByteSweepUnsavedChanges()" `
    "Closing the sweep editor must check for unsaved changes."
Assert-Contains $sendForm "MessageBoxButtons.YesNoCancel" `
    "Unsaved sweep changes must offer save, discard, and cancel choices."
Assert-Contains $sendForm "ByteAnnotationController_Changed" `
    "Byte annotation edits must participate in unsaved-change tracking."
Assert-Contains $sendForm "if (e.Error != null)" `
    "Unexpected send-worker errors must have a distinct completion state."
Assert-Contains $sendForm "throw;" `
    "The send worker must propagate unexpected exceptions to its completion handler."
Assert-Contains $sendForm "this.byteSweepProviderHadChanges =" `
    "Temporary live display must preserve the provider's original dirty state."
Assert-Contains $sendForm "Socket_Cache.SendList.TryApplyListChangeAndSave(" `
    "Confirmed normal preset saves must persist immediately with rollback on failure."
Assert-Contains $sendForm "this.StartSend(false, false);" `
    "The left Start action must always launch normal sending."
Assert-Contains $sendForm "this.StartSend(true, false);" `
    "The lower Start action must launch only the sequential sweep path."
Assert-Contains $sendForm "this.StartSend(true, true);" `
    "The right editor Start action must launch its selected sweep mode."
Assert-Contains $sendForm "sweepRunning && !this.byteSweepStartedFromEditorPanel" `
    "The lower Stop action must be enabled only for a sweep started there."
Assert-Contains $sendForm "sweepRunning && this.byteSweepStartedFromEditorPanel" `
    "The right Stop action must be enabled only for a sweep started there."
Assert-Contains $sendForm "this.hbPacketData.MouseUp += this.hbPacketData_ByteSweepPickMouseUp;" `
    "Two-byte position pickers must consume the final HexBox click position."
Assert-Contains $sendForm "this.hbPacketData.SelectionLength = 1;" `
    "Picking a combination byte must normalize the HexBox selection to one byte."
Assert-Contains $sendForm "this.byteSweepLiveValueActive ||" `
    "Live-preview cleanup must include byte A state."
Assert-Contains $sendForm "this.byteSweepLiveSecondValueActive;" `
    "Live-preview cleanup must include byte B state independently."
Assert-Contains $sendForm "KeepByteSweepLiveSelection" `
    "The send preview must keep the active byte selected while a sweep is running."

$mainForm = Read-SourceFile "WPELibrary\Socket_Form.cs"
$mainByteSweep = Read-SourceFile "WPELibrary\Socket_Form.ByteSweep.cs"
Assert-Contains $mainForm "SelectionStartChanged += this.hbPacketData_ByteSweepSelectionChanged;" `
    "The main packet preview must observe selection changes during a sweep."
Assert-Contains $mainByteSweep "KeepByteSweepLiveSelection" `
    "The main preview must keep the active byte selected while a sweep is running."
Assert-Contains $mainByteSweep "RestoreByteSweepLiveSelection();" `
    "The main preview must restore the user's original selection after a sweep."
$saveHandler = [regex]::Match(
    $sendForm,
    '(?s)private void bSave_Click\(object sender, EventArgs e\).*?private bool ApplyCurrentPacketEdits').Value
$saveAsSweepIndex = $saveHandler.IndexOf("if (dialog.SaveAsByteSweep)")
$applyEditsIndex = $saveHandler.IndexOf("if (!this.ApplyCurrentPacketEdits())")
if ($saveAsSweepIndex -lt 0 -or $applyEditsIndex -le $saveAsSweepIndex) {
    throw "Cancelling or saving as a sweep must not mutate the original send preset."
}

$sendWorker = Read-SourceFile "WPELibrary\Lib\Socket_Send.cs"
Assert-Contains $sendWorker "catch (OperationCanceledException)" `
    "Stopping during an interval must complete as cancellation."
Assert-Contains $sendWorker "CreateSendSnapshot(SendCollection)" `
    "Send workers must run against an immutable packet-list snapshot."
Assert-Contains $sendWorker "WaitForStop(int millisecondsTimeout)" `
    "Send workers must expose a bounded shutdown wait."
Assert-Contains $sendWorker "throw new InvalidOperationException" `
    "A missing resolved Socket must complete as an explicit worker error."
Assert-Contains $sendWorker "this.sendStopped.Set();" `
    "Send workers must signal completion even when they fail."

$hook = Read-SourceFile "WPELibrary\Lib\WinSockHook.cs"
Assert-Contains $hook "private static Socket_Cache.Filter.FilterAction ApplyFilterSafely" `
    "Hook filter failures must use the pass-through safety path."
Assert-NotContains $hook "Socket_Cache.FilterList.DoFilterList(Socket, bBufferSpan" `
    "Direct hook filter calls must not bypass the pass-through safety path."
Assert-Contains $hook "public HookStartResult StartHook()" `
    "Hook startup must return an explicit result instead of hiding partial failures."
Assert-Contains $hook "HookStartResult.Failed(failedHook, ex.Message)" `
    "Hook startup failures must identify the failed API and roll back created hooks."
Assert-Contains $hook "this.DisposeHook(ref this.lhWS1_Send" `
    "Hook shutdown must dispose every created hook independently."
Assert-Contains $hook "public bool IsRunning { get; private set; }" `
    "The hook implementation must expose its actual running state to the UI."
Assert-Contains $hook "this.IsRunning = true;" `
    "The hook must mark itself running only after all configured hooks are created."
Assert-Contains $hook "this.IsRunning = false;" `
    "The hook must clear its running state after shutdown."

$queue = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
Assert-Contains $queue "public const int MaxQueueCount = 10000;" `
    "Capture queue must have a hard upper bound."
Assert-Contains $queue "Interlocked.Increment(ref Dropped_CNT)" `
    "Capture queue overflow must report dropped packets."
Assert-Contains $queue "lock (QueueSync)" `
    "Capture queue capacity enforcement must be serialized across producer threads."
Assert-Contains $queue "public static async Task SocketToList(int maxItems = 1)" `
    "Capture list draining must support bounded batch consumption."

$operation = Read-SourceFile "WPELibrary\Lib\Socket_Operation.cs"
Assert-Contains $operation "private const int MaxHookResultQueueCount = 20000;" `
    "Hook result processing must have a hard upper bound before packet work is accepted."
Assert-Contains $operation "private static void ProcessHookResults()" `
    "Hook result processing must use a dedicated consumer instead of one task per packet."
Assert-Contains $socketForm "Socket_Operation.StopHookResultProcessing();" `
    "Hook result processing must be stopped during form shutdown."
$hookResultMethod = [regex]::Match(
    $operation,
    '(?s)public static Task ProcessingHookResultAsync\(.*?\n        }').Value
Assert-NotContains $hookResultMethod "Task.Run" `
    "Hook callbacks must not create one ThreadPool task per packet."
$logMethod = [regex]::Match(
    $operation,
    '(?s)public static void DoLog\(.*?\n        }').Value
Assert-NotContains $logMethod "Task.Run" `
    "Logging must not create one ThreadPool task per message."

$mainHook = Read-SourceFile "WPELibrary\Socket_Form.cs"
Assert-Contains $mainHook "UI_HookStatusStarting" `
    "The main workspace must expose an explicit hook-starting state."
Assert-Contains $mainHook "SynchronizeHookUiState" `
    "The main workspace must reconcile the status bar with the actual hook state."
Assert-Contains $mainHook "this.ws.IsRunning" `
    "The status bar must use the hook implementation state as its source of truth."
Assert-Contains $mainHook "await Socket_Cache.SocketList.SocketToList(200);" `
    "The main workspace must drain captured packets in batches."
Assert-Contains $socketCache "List<Socket_PacketInfo> visiblePackets = new List<Socket_PacketInfo>();" `
    "Capture queue drain must collect a batch before scheduling one UI refresh."
Assert-Contains $socketCache "Action appendVisiblePackets = () =>" `
    "Capture queue drain must schedule one UI append action for each visible batch."
Assert-Contains $socketCache "foreach (Socket_PacketInfo packet in visiblePackets)" `
    "Capture queue drain must preserve visible packet arrival order."
Assert-Contains $socketCache "Socket_Cache.SocketList.lstRecPacket.Add(packet);" `
    "Capture queue drain must append every visible packet from the batch."
Assert-NotContains $socketForm "LoadProxyAccountList_FromDB" `
    "Injection-only socket form must not load proxy accounts."
Assert-NotContains $socketForm "LoadProxyMapLocal_FromDB" `
    "Injection-only socket form must not load local proxy mappings."
Assert-NotContains $socketForm "LoadProxyMapRemote_FromDB" `
    "Injection-only socket form must not load remote proxy mappings."
Assert-NotContains $socketForm "SaveProxyAccountList_ToDB" `
    "Injection-only socket form must not save proxy accounts."
Assert-NotContains $socketForm "SaveProxyMapLocal_ToDB" `
    "Injection-only socket form must not save local proxy mappings."
Assert-NotContains $socketForm "SaveProxyMapRemote_ToDB" `
    "Injection-only socket form must not save remote proxy mappings."

$processList = Read-SourceFile "WinsockPacketEditor\ProcessList_Form.cs"
Assert-Contains $processList "private bool ShowEmulatorOnly = true;" `
    "Injection target selection must default to approved emulator processes."
Assert-Contains $processList "IsSupportedInjectionProcess" `
    "Injection target selection must validate the live process name."
Assert-Contains $processList "AppendDirectEmulatorRows" `
    "Injection target selection must recover when the general process snapshot is empty."
Assert-NotContains $processList "OpenFileDialog" `
    "Injection-only process selection must not expose arbitrary EXE selection."
Assert-Contains $processList "PArch" `
    "Process selection must expose target architecture."

$injector = Read-SourceFile "WinsockPacketEditor\Injector_Form.cs"
Assert-Contains $injector "TrySelectSingleEmulator" `
    "A single approved emulator must be selected without opening a redundant process chooser."
Assert-Contains $injector "HasAutoInjectSwitch" `
    "Unattended desktop restart must have an explicit auto-inject switch."
Assert-Contains $injector "this.bInject.PerformClick()" `
    "The explicit auto-inject switch must invoke the existing injection button path."

$program = Read-SourceFile "WinsockPacketEditor\Lib\Program.cs"
Assert-Contains $program "Environment.GetCommandLineArgs()" `
    "The elevated launcher must inspect explicit unattended switches."
Assert-Contains $program 'startInfo.Arguments = string.Join(" ", forwardedArguments.ToArray());' `
    "The elevated launcher must preserve unattended switches across the UAC handoff."

Assert-Contains $sendForm "private enum SendUiMode" `
    "The send editor must expose explicit execution modes."
Assert-Contains $sendForm "UI_SendModeSequential" `
    "The send editor must expose a single-byte sweep mode label."
Assert-Contains $sendForm "this.gbSendType.Visible = normalMode;" `
    "The send editor must show only the active mode panel."
Assert-Contains $sendForm "private void UpdatePairEditorVisibility()" `
    "The two-byte editor must be limited to the two-byte combination mode."
Assert-Contains $sendForm "UI_AdvancedEditorCollapse" `
    "The two-byte editor must expose a collapsible advanced-editor header."

$web = Read-SourceFile "WPELibrary\Lib\WebAPI\Socket_Web.cs"
$accountController = Read-SourceFile "WPELibrary\Lib\WebAPI\ProxyAccount_Controller.cs"
$ccProxyController = Read-SourceFile "WPELibrary\Lib\WebAPI\CCProxy_Controller.cs"
$accountForm = Read-SourceFile "WPELibrary\Proxy_AccountForm.cs"
Assert-Contains $web "TryGetBasicCredentials" `
    "Malformed Basic authentication must be rejected without request exceptions."
Assert-Contains $web 'authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)' `
    "Basic authentication must validate the complete authentication scheme."
Assert-Contains $accountController "IEnumerable<ProxyAccountSummary>" `
    "Proxy account APIs must return a password-free DTO."
Assert-NotContains $accountController "GetPassWordDecrypt" `
    "The remote API must not expose a password decryption endpoint."
Assert-Contains $accountController "existing.PassWord" `
    "Editing an account with a blank password must preserve the existing secret."
Assert-NotContains $ccProxyController "PassWord_Decrypt(pai.PassWord)" `
    "Legacy proxy account pages must not render stored passwords."
Assert-NotContains $accountForm "PassWord_Decrypt(pai.PassWord)" `
    "The desktop proxy account editor must not render stored passwords."
Assert-Contains $accountForm "Leave the field blank to keep the current password unchanged." `
    "The desktop proxy account editor must support password-preserving edits."

$crypto = Read-SourceFile "WPELibrary\Lib\Socket_Operation.cs"
Assert-Contains $crypto "EncryptedXmlMagic" `
    "New encrypted exports must use a versioned envelope."
Assert-Contains $crypto "ProtectedData.Protect" `
    "Stored proxy passwords must use Windows user-scoped protection."
Assert-Contains $crypto "ProtectedPasswordPrefix" `
    "Stored proxy passwords must use an explicit version marker."
Assert-Contains $crypto "Rfc2898DeriveBytes" `
    "New encrypted exports must derive keys with a password KDF."
Assert-Contains $crypto "RandomNumberGenerator.Create()" `
    "New encrypted exports must use random salt and IV material."
Assert-Contains $crypto "HMACSHA256" `
    "New encrypted exports must authenticate their ciphertext."
Assert-Contains $crypto "GetLegacyAESKeyFromString" `
    "Encrypted imports must remain compatible with older export files."

$hexBox = Read-SourceFile "ThirdParty\Be.Windows.Forms.HexBox\HexBox.cs"
Assert-Contains $hexBox "bool replaceSingleByteInPlace = sw && sel == 1;" `
    "Typing into a clicked byte must preserve annotation structure."

$sweepEngine = Read-SourceFile "WPELibrary\Lib\Socket_ByteSweepEngine.cs"
Assert-NotContains $sweepEngine "originalValue, originalValue, byteNumber, length, 0" `
    "Live sweep progress must not report an original value that was never sent."
Assert-Contains $sweepEngine "(now - lastProgress).TotalMilliseconds >= 100" `
    "Pair-combination progress must be throttled so long runs do not flood the UI queue."
$byteSweepMainForm = Read-SourceFile "WPELibrary\Socket_Form.ByteSweep.cs"
Assert-Contains $byteSweepMainForm "RestoreByteSweepLivePreview(preset);" `
    "Batch sweep completion must restore the preset baseline after live preview."
Assert-Contains $byteSweepMainForm "if (this.byteSweepRunning)" `
    "Batch sweep selection changes must not commit temporary live-preview bytes."
$runtime = Read-SourceFile "WPELibrary\Lib\Socket_ByteSweepRuntime.cs"
$byteSweepLog = Read-SourceFile "WPELibrary\Socket_Form.ByteSweepLog.cs"
Assert-Contains $runtime "Socket_ByteSweepRuntimeState.Stopping" `
    "The shared byte-sweep runtime must expose an explicit stopping state."
Assert-Contains $runtime "RequestStop(Guid jobId)" `
    "The shared byte-sweep runtime must cancel the active job by identifier."
Assert-Contains $runtime "ProgressChanged" `
    "The shared byte-sweep runtime must publish progress snapshots to both UI surfaces."
Assert-Contains $byteSweepForm "Socket_ByteSweepRuntime.Current.TryStart(" `
    "Batch byte sweeps must acquire the shared runtime before starting."
Assert-Contains $sendForm "Socket_ByteSweepRuntime.Current.TryStart(" `
    "Single-packet byte sweeps must acquire the shared runtime before starting."
Assert-Contains $sendForm "Socket_ByteSweepRuntime.Current.RequestStop(this.byteSweepJobId);" `
    "The send form stop action must cancel the shared runtime job."
Assert-Contains $byteSweepLog "dgvByteSweepLog" `
    "The main window must provide a dedicated byte-sweep log grid."
Assert-Contains $byteSweepLog "ExportByteSweepLog_Click" `
    "The dedicated byte-sweep log must support CSV export."
$mainRobotUi = $socketForm
Assert-Contains $mainRobotUi "ConfigureRobotToolbarTextButtons" `
    "Robot toolbar actions must use the shared text-button configuration."
Assert-Contains $mainRobotUi 'UiText("Robot_Load")' `
    "Robot load action must have a visible text label."
Assert-Contains $mainRobotUi "tsRobotListMore" `
    "Robot secondary actions must be grouped under a More menu."
Assert-Contains $mainRobotUi 'UiText("UI_Assistant")' `
    "The home automation navigation must expose the Assistant label."
Assert-Contains $mainRobotUi "InitAssistantButtonUI" `
    "The assistant page must use the grouped button layout."
Assert-Contains $mainRobotUi "AssistantButton_Click" `
    "Assistant buttons must run one assistant at a time."
Assert-Contains $mainRobotUi "this.tsRobotList_Start.Visible = false;" `
    "Assistant batch start must not be exposed in the assistant page."
Assert-Contains $mainRobotUi "this.tsRobotList_Stop.Visible = false;" `
    "Assistant batch stop must not be exposed in the assistant page."
Assert-Contains $mainRobotUi "ConfigureFilterToolbarTextButtons" `
    "Filter toolbar actions must use the shared text-button configuration."
Assert-Contains $mainRobotUi 'UiText("Filter_Add")' `
    "Filter add action must have a visible localized text label."
Assert-Contains $mainRobotUi "tsFilterListMore" `
    "Filter secondary actions must be grouped under a More menu."
Assert-Contains $mainRobotUi "this.tsFilterList_CleanUp.Visible = false;" `
    "Filter cleanup must remain available from the More menu instead of the main toolbar."
Assert-Contains $mainRobotUi "tsSendListMore" `
    "Send preset secondary actions must be grouped under a More menu."

$resources = Read-SourceFile "WPELibrary\Properties\Resources.resx"
$englishResources = Read-SourceFile "WPELibrary\Properties\Resources.en-US.resx"
$sweepEditor = Read-SourceFile "WPELibrary\Socket_ByteSweepEditorPanel.cs"
Assert-Contains $sweepEditor "ConfigurePairLayout" `
    "The pair editor must provide a dedicated compact layout."
Assert-Contains $sweepEditor "this.scrollHost.AutoScroll = false;" `
    "The pair editor must not require vertical scrolling."
Assert-Contains $sweepEditor "SetPairOnlyMode" `
    "The right sweep editor must expose a dedicated pair-combination surface."
Assert-Contains $sendForm "this.byteSweepEditorPanel.SetPairOnlyMode(true);" `
    "The send page must configure the lower sweep editor as pair-only."
Assert-Contains $sendForm "this.tlpParameter.Controls.Add(this.pnlByteSweepSide, 0, 1);" `
    "The send page must place the pair editor below the packet data."
Assert-Contains $sendForm "SetUiVisible(false)" `
    "The send page must hide the annotation UI without removing its compatibility controller."
Assert-Contains $socketForm 'UiText("UI_ClearCaptureConfirm")' `
    "Clearing the current capture must require an explicit confirmation."
Assert-Contains $socketForm "MessageBoxButtons.YesNo" `
    "The clear-current-capture confirmation must offer a safe negative choice."
Assert-Contains $resources 'name="UI_ClearCaptureConfirm"' `
    "The Chinese resources must include the clear-current-capture confirmation."
Assert-Contains $englishResources "Clear capture (&amp;C)" `
    "The English clear action must describe its current-capture scope."
foreach ($resourceKey in @(
    "ByteSweep_StartAction",
    "ByteSweep_PauseAction",
    "ByteSweep_ResumeAction",
    "ByteSweep_StopAction",
    "ByteSweep_Paused",
    "ByteSweep_SequentialHeader",
    "ByteSweep_PairMode",
    "ByteSweep_CombinationEstimate",
    "ByteSweep_PairProgress",
    "ByteSweep_PickFirst",
    "ByteSweep_PickSecond",
    "ByteSweep_PickDuplicate",
    "ByteSweep_LogTitle",
    "ByteSweep_LogLive",
    "ByteSweep_RuntimeBusy",
    "ByteSweep_ParameterInvalid",
    "ByteSweep_UnsavedTitle",
    "ByteSweep_UnsavedChanges",
    "Robot_Load",
    "Robot_Start",
    "Robot_Clear",
    "UI_More",
    "Send_Copy",
    "UI_HookStatusReady",
    "UI_HookStatusStarting",
    "UI_HookStatusListening",
    "UI_HookStatusStopping",
    "UI_HookStatusFailed",
    "UI_QueueDropped",
    "UI_SendModeNormal",
    "UI_SendModeSequential",
    "UI_SendModePair",
    "UI_AdvancedEditorCollapse",
    "UI_AdvancedEditorExpand"
)) {
    Assert-Contains $resources ('name="' + $resourceKey + '"') `
        "Chinese byte-sweep resources must include $resourceKey."
    Assert-Contains $englishResources ('name="' + $resourceKey + '"') `
        "English byte-sweep resources must include $resourceKey."
}
Assert-Contains $sweepEditor "this.mode.Enabled = !this.pairOnlyMode && !this.busy;" `
    "Concurrent sends and pair-only mode must lock the right sweep mode selector."
Assert-Contains $sweepEditor "this.loopCount.Enabled = !this.busy;" `
    "Concurrent sends must lock right sweep loop settings."
Assert-Contains $sweepEditor "position == (int)other.Value" `
    "Two-byte position picking must reject duplicate A/B positions."

$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$loadSystemList = [regex]::Match(
    $cache,
    '(?s)public static void LoadSystemList_FromDB\(\).*?#endregion').Value
if ([string]::IsNullOrWhiteSpace($loadSystemList)) {
    throw "Could not locate LoadSystemList_FromDB for startup persistence checks."
}
Assert-NotContains $loadSystemList "Task.Run(" `
    "Startup list loading must complete before shutdown persistence can run."
Assert-Contains $loadSystemList "Socket_Cache.SendList.LoadSendList_FromDB();" `
    "Startup list loading must synchronously populate send presets."

$loaders = @(
    @{
        Name = "filter"
        Marker = "public static void LoadFilterList_FromDB()"
        Clear = "Socket_Cache.FilterList.FilterListClear();"
    },
    @{
        Name = "send"
        Marker = "public static void LoadSendList_FromDB()"
        Clear = "Socket_Cache.SendList.SendListClear();"
    },
    @{
        Name = "byte-sweep"
        Marker = "public static void LoadByteSweepList_FromDB()"
        Clear = "Socket_Cache.ByteSweepList.Clear();"
    },
    @{
        Name = "robot"
        Marker = "public static void LoadRobotList_FromDB()"
        Clear = "Socket_Cache.RobotList.RobotListClear();"
    }
)
foreach ($loader in $loaders) {
    $loaderBody = [regex]::Match(
        $cache,
        '(?s)' + [regex]::Escape($loader.Marker) + '.*?#endregion').Value
    if ([string]::IsNullOrWhiteSpace($loaderBody)) {
        throw "Could not locate $($loader.Name) database loader."
    }
    Assert-Contains $loaderBody $loader.Clear `
        "$($loader.Name) database loader must clear the in-memory list before loading."
}
Assert-Contains $cache "ReplaceByteSweepList(folders, presets)" `
    "Byte-sweep persistence must use the transactional replacement path."
Assert-Contains $cache "conn.BeginTransaction()" `
    "Byte-sweep persistence must begin a database transaction."
Assert-Contains $cache "transaction.Commit();" `
    "Byte-sweep persistence must commit only after all rows are written."
Assert-Contains $cache "transaction.Rollback();" `
    "Byte-sweep persistence must roll back failed replacements."
Assert-Contains $cache 'AddWithValue("@Mode", (int)preset.BMode)' `
    "Byte-sweep mode must be persisted as a stable integer value."
Assert-Contains $cache "Guid.TryParse((string)element.Element(""ID""), out presetId)" `
    "Byte-sweep import must preserve valid preset identifiers."
Assert-Contains $cache "BID = presetId" `
    "Imported byte-sweep presets must use the parsed identifier."
Assert-Contains $cache "_ = DoSendAsync(SID);" `
    "Hotkey sends must not synchronously block the UI thread."
$hotkeySend = [regex]::Match(
    $cache,
    '(?s)public static void DoSend_ByIndex\(int SendListIndex\).*?#endregion').Value
Assert-NotContains $hotkeySend "Task.Run(() => DoSendAsync(SID))" `
    "Hotkey sends must not deadlock while resolving the current Socket."

$processList = Read-SourceFile "WinsockPacketEditor\ProcessList_Form.cs"
Assert-Contains $processList "IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase)" `
    "Process search must compare names and paths without using a DataView expression."
Assert-NotContains $processList "RowFilter =" `
    "Process search must not concatenate user input into a DataView RowFilter."

$mobileController = Read-SourceFile "WPELibrary\Lib\WebAPI\MobileSync_Controller.cs"
Assert-Contains $mobileController "public sealed class MobilePresetDescriptor" `
    "Mobile synchronization must expose a catalog DTO instead of full preset XML."
Assert-NotContains $mobileController "SendXml" `
    "Mobile synchronization must not expose raw send-preset XML."
Assert-NotContains $mobileController "ProgressionXml" `
    "Mobile synchronization must not expose raw progression-preset XML."
Assert-NotContains $mobileController "AssistantXml" `
    "Mobile synchronization must not expose raw assistant XML."
Assert-Contains $mobileController "Socket_Cache.SendList.lstSend" `
    "The mobile revision must change when send preset content changes."
Assert-Contains $mobileController "Socket_Cache.ByteSweepList.lstPresets" `
    "The mobile revision must change when progression preset content changes."
Assert-Contains $mobileController "Socket_Cache.RobotList.lstRobot" `
    "The mobile revision must change when assistant preset content changes."
Assert-Contains $mobileController "string canonical = Canonicalize(payload)" `
    "Preset content used for revision hashing must remain local and deterministic."
Assert-Contains $web 'if (isMobileSync)' `
    "MobileSync must have an explicit passwordless route boundary."
Assert-NotContains $web 'IsValidMobile(username, password)' `
    "MobileSync must not depend on proxy-account credentials."
Assert-Contains $web 'IsValidAdmin(username, password)' `
    "Non-mobile Web API routes must remain protected by administrator credentials."
Assert-Contains $cache 'Socket_Cache.ProxyAccount.LoadProxyAccountList_FromDB();' `
    "Proxy accounts must be loaded before the remote service can authenticate mobile clients."
Assert-NotContains $cache 'LoadProxyAccountList_FromDB().GetAwaiter().GetResult()' `
    "Startup proxy-account loading must not deadlock the UI dispatcher."
Assert-Contains $cache "AtomicSaveGate" `
    "Atomic configuration saves must gate concurrent connection-string readers."
Assert-Contains $byteSweepForm "public sealed class MobileByteSweepStartResult" `
    "The mobile progression API must return a structured start result."
$mobileManifest = Read-SourceFile "mobile\app\src\main\AndroidManifest.xml"
Assert-NotContains $mobileManifest 'android:usesCleartextTraffic="true"' `
    "The Android client must reject clear-text transport."
$mobileClient = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\WpeSyncClient.java"
Assert-Contains $mobileClient "setInstanceFollowRedirects(false)" `
    "The mobile client must not follow credential-bearing redirects."
Assert-Contains $mobileClient '"https://"' `
    "The mobile client must enforce HTTPS endpoints."
$mobileCoordinator = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\SyncCoordinator.java"
Assert-Contains $mobileCoordinator "private void postToMain(Runnable action)" `
    "Mobile network callbacks must be guarded after Activity destruction."
Assert-Contains $mobileCoordinator "if (!destroyed && action != null)" `
    "Mobile coordinator must drop stale UI callbacks after teardown."
$mobileMain = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\MainActivity.java"
Assert-Contains $mobileMain "coordinator.detachListener(this)" `
    "Mobile Activity teardown must detach listeners while the overlay remains alive."
$injector = Read-SourceFile "WinsockPacketEditor\Injector_Form.cs"
Assert-Contains $injector 'bRemoteSettings' `
    "The fixed injector entry must expose remote service settings."
Assert-Contains $injector 'bMobileAccountSettings' `
    "The fixed injector entry must expose the dedicated mobile account manager."

$mainAssembly = Read-SourceFile "WinsockPacketEditor\Properties\AssemblyInfo.cs"
$libraryAssembly = Read-SourceFile "WPELibrary\Properties\AssemblyInfo.cs"
$project = [xml](Read-SourceFile "WinsockPacketEditor\WinsockPacketEditor.csproj")
$manifest = [xml](Read-SourceFile "WinsockPacketEditor\Properties\app.manifest")

$mainVersion = [regex]::Match(
    $mainAssembly,
    '(?m)^\s*\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]').Groups[1].Value
$libraryVersion = [regex]::Match(
    $libraryAssembly,
    '(?m)^\s*\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]').Groups[1].Value
$projectNamespace = New-Object System.Xml.XmlNamespaceManager($project.NameTable)
$projectNamespace.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
$applicationVersion = $project.SelectSingleNode(
    "//msb:ApplicationVersion",
    $projectNamespace).InnerText
$manifestVersion = $manifest.assembly.assemblyIdentity.version

$versions = @($mainVersion, $libraryVersion, $applicationVersion, $manifestVersion)
if ($versions | Where-Object { $_ -ne $mainVersion }) {
    throw "Version mismatch: $($versions -join ', ')"
}

$xmlFiles = @(
    "WinsockPacketEditor\WinsockPacketEditor.csproj",
    "WPELibrary\WPELibrary.csproj",
    "WinsockPacketEditor\Properties\app.manifest",
    "WinsockPacketEditor\Injector_Form.resx",
    "WinsockPacketEditor\ProcessList_Form.resx",
    "WPELibrary\Socket_Form.resx",
    "WPELibrary\Properties\Resources.resx",
    "WPELibrary\Properties\Resources.en-US.resx"
)
foreach ($relativePath in $xmlFiles) {
    $null = [xml](Read-SourceFile $relativePath)
}

Write-Output "Review regression checks passed."
