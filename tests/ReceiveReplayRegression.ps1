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

$hook = Read-SourceFile "WPELibrary\Lib\WinSockHook.cs"
$operation = Read-SourceFile "WPELibrary\Lib\Socket_Operation.cs"

Assert-Contains $operation "IsReceivePacketType(packetType)" `
    "Receive packets must use the client-side replay path."
Assert-Contains $operation "WinSockHook.EnqueueReceiveReplay(" `
    "Receive packets must be queued for the injected client."
Assert-Contains $operation "return queued;" `
    "Receive packets must not fall through to native send after queueing."

foreach ($name in @(
        "WS1.recv", "WS1.recvfrom", "WS2.recv", "WS2.recvfrom",
        "WSA.recv", "WSA.recvfrom", "Mswsock.WSARecvEx")) {
    Assert-Contains $hook ('failedHook = "' + $name + '"') `
        "Receive replay must remain covered by $name."
}

Assert-Contains $hook "TryTakeReceiveReplay(" `
    "Receive hooks must consume queued replay data before native receive."
Assert-Contains $hook "EnableReceiveReplay();" `
    "Receive replay must be enabled only after receive hooks start."
Assert-Contains $hook "DisableReceiveReplay();" `
    "Receive replay must be cleared when a hook session stops."
Assert-Contains $hook "MaxReceiveReplayQueuedBytes" `
    "Receive replay must have a bounded queue."
Assert-Contains $hook "CopyReceiveReplayToWsabufs(" `
    "WSA receive replay must support scatter-gather buffers."
Assert-Contains $hook "Select_HookCore(" `
    "Receive replay must make queued sockets readable to select callers."
Assert-Contains $hook "lhWS2_Select" `
    "WS2 select must be hooked when receive replay is enabled."
Assert-Contains $hook "MaxSelectFds" `
    "select fd_set parsing must remain bounded."
Assert-Contains $hook "SocketFlags.Peek" `
    "MSG_PEEK must not consume queued replay data."

Write-Output "Receive-side replay regression checks passed."
