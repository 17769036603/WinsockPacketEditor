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
Assert-Contains $socketForm "Socket_Cache.System.IsRemote = false;" `
    "Injection-only startup must disable persisted remote-management configuration."
Assert-NotContains $socketForm "Socket_Operation.StartRemoteMGT();" `
    "Injection-only startup must not start the remote-management HTTP service."

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
Assert-Contains $sendForm "SizeType.Absolute, 205F" `
    "The annotation area must use a stable height so the right sweep editor is not compressed by percentage layout."
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
Assert-Contains $sendForm "Socket_Cache.SendList.SaveSendList_ToDB();" `
    "Confirmed normal preset saves must persist immediately."
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
Assert-Contains $sweepEditor "AutoScrollMinSize" `
    "The right sweep editor must reserve a scrollable content area when the window is compact."
Assert-Contains $sweepEditor "SetPairOnlyMode" `
    "The right sweep editor must expose a dedicated pair-combination surface."
Assert-Contains $sendForm "this.byteSweepEditorPanel.SetPairOnlyMode(true);" `
    "The send page must configure the right sweep editor as pair-only."
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
    "ByteSweep_StopAction",
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
    "Send_Copy"
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
Assert-Contains $processList "StartsWith(searchText, StringComparison.CurrentCultureIgnoreCase)" `
    "Process search must compare process names directly."
Assert-NotContains $processList "RowFilter =" `
    "Process search must not concatenate user input into a DataView RowFilter."

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
