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

$resources = Read-SourceFile "WPELibrary\Properties\Resources.resx"
$englishResources = Read-SourceFile "WPELibrary\Properties\Resources.en-US.resx"
$sweepEditor = Read-SourceFile "WPELibrary\Socket_ByteSweepEditorPanel.cs"
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
    "ByteSweep_PairProgress",
    "ByteSweep_PickFirst",
    "ByteSweep_PickSecond",
    "ByteSweep_PickDuplicate"
)) {
    Assert-Contains $resources ('name="' + $resourceKey + '"') `
        "Chinese byte-sweep resources must include $resourceKey."
    Assert-Contains $englishResources ('name="' + $resourceKey + '"') `
        "English byte-sweep resources must include $resourceKey."
}
Assert-Contains $sweepEditor "this.mode.Enabled = !this.busy;" `
    "Concurrent sends must lock the right sweep mode selector."
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
