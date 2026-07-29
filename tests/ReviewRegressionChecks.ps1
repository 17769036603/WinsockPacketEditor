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

$resources = Read-SourceFile "WPELibrary\Properties\Resources.resx"
$englishResources = Read-SourceFile "WPELibrary\Properties\Resources.en-US.resx"
Assert-Contains $socketForm 'UiText("UI_ClearCaptureConfirm")' `
    "Clearing the current capture must require an explicit confirmation."
Assert-Contains $socketForm "MessageBoxButtons.YesNo" `
    "The clear-current-capture confirmation must offer a safe negative choice."
Assert-Contains $resources 'name="UI_ClearCaptureConfirm"' `
    "The Chinese resources must include the clear-current-capture confirmation."
Assert-Contains $englishResources "Clear capture (&amp;C)" `
    "The English clear action must describe its current-capture scope."

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
