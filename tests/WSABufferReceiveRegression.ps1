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

$hook = Read-SourceFile "WPELibrary\Lib\WinSockHook.cs"
Assert-Contains $hook "CopyFilteredReceiveBufferToWsabufs" `
    "WSA receive filtering must copy the filtered stream through a shared WSABUF helper."
Assert-Contains $hook "int sourceOffset = 0" `
    "WSA receive WSABUF copying must track the filtered source offset independently."
Assert-Contains $hook "filteredBuffer.AsSpan(sourceOffset, copyLength)" `
    "Each WSABUF must receive the next contiguous portion of the filtered stream."
Assert-NotContains $hook "bNewBuffer.AsSpan(BytesRecvd - remainingBytes" `
    "WSA receive filtering must not restart the source offset from the remaining tail."

$helperCalls = [regex]::Matches($hook, "CopyFilteredReceiveBufferToWsabufs\(").Count
if ($helperCalls -lt 3) {
    throw "The WSABUF receive helper must be used by both WSA receive hooks."
}

$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
Assert-Contains $cache "SnapshotForExecution" `
    "Filter execution must use an immutable snapshot instead of enumerating the UI BindingList."
Assert-Contains $cache "FilterRuntimeStates" `
    "Filter counters must be isolated from the UI-bound filter objects."
Assert-Contains $cache "Local\\WPELibrary-SqliteAtomicSave" `
    "Database saves must coordinate across injected processes."

$socketForm = Read-SourceFile "WPELibrary\Socket_Form.cs"
Assert-Contains $socketForm "if (this.dgvFilterList.Rows.Count > 0)" `
    "Creating the first filter must not index a non-existent DataGridView row."

Write-Output "WSA buffer and filter concurrency regression checks passed."
