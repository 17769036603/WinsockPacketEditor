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

$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
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
    "WPELibrary\Socket_Form.resx"
)
foreach ($relativePath in $xmlFiles) {
    $null = [xml](Read-SourceFile $relativePath)
}

Write-Output "Review regression checks passed."
