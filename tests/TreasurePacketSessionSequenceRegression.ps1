param(
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $PSScriptRoot "..\WPELibrary\bin\Debug"
}

$libraryDll = Join-Path $BuildDirectory "WPELibrary.dll"
if (-not (Test-Path -LiteralPath $libraryDll)) {
    throw "WPELibrary.dll was not found: $libraryDll"
}

Add-Type -Path $libraryDll

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw "ASSERT FAILED: $message"
    }
}

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) {
        throw "ASSERT FAILED: $message (expected=$expected actual=$actual)"
    }
}

function Get-Hex([byte[]]$bytes) {
    return (($bytes | ForEach-Object { $_.ToString("X2") }) -join " ")
}

$packetType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$runtime = [WPELibrary.Lib.Vision.TreasurePacketRuntime]
$socketList = [WPELibrary.Lib.Socket_Cache+SocketList]
$listener = [System.Net.Sockets.TcpListener]::new(
    [System.Net.IPAddress]::Loopback,
    0)
$client = [System.Net.Sockets.TcpClient]::new()
$server = $null

try {
    $listener.Start()
    $endpoint = [System.Net.IPEndPoint]$listener.LocalEndpoint
    $client.Connect($endpoint.Address, $endpoint.Port)
    $server = $listener.AcceptTcpClient()
    $server.ReceiveTimeout = 3000

    $template = New-Object WPELibrary.Lib.Socket_PacketInfo
    $template.PacketTime = [DateTime]::Now
    $template.PacketSocket = $client.Client.Handle.ToInt32()
    $template.PacketType = $packetType
    $template.PacketFrom = $client.Client.LocalEndPoint.ToString()
    $template.PacketTo = $client.Client.RemoteEndPoint.ToString()
    $template.PacketBuffer = [byte[]](
        0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x00, 0x12, 0x58, 0x28,
        0x00, 0x00, 0x03, 0xF1,
        0x00, 0x00, 0x00, 0x35,
        0x00, 0x00, 0x00, 0x31,
        0x00, 0x00, 0x00, 0x00)
    $template.PacketLen = $template.PacketBuffer.Length

    $socketList::lstRecPacket.Clear()
    $socketList::BeginCaptureSession()
    $runtime::BeginSession()
    $socketList::lstRecPacket.Add($template)
    $runtime::ObserveCapturedPacket($template)

    $route = $runtime::GetCurrentRoute()
    $target = New-Object WPELibrary.Lib.Vision.TreasureInventoryTarget(13, 1009, 53, 49)
    $prepared = $runtime::GetCurrentJumpPacket($target, $route)
    Assert-Equal "00 00 01 00" (Get-Hex $prepared.PacketBuffer[4..7]) `
        "Preparation must retain the captured sequence before the send boundary"

    $authorization = [WPELibrary.Lib.Vision.TreasureLiveSendAuthorization]::Create(
        "TREASURE-LIVE-SEND")
    $result = $runtime::SendPreparedPacketOnce($prepared, $authorization)
    Assert-True $result.Success "Offline loopback send must succeed"
    Assert-Equal $prepared.PacketBuffer.Length $result.BytesSent `
        "Offline loopback send byte count"

    $received = New-Object byte[] $prepared.PacketBuffer.Length
    $offset = 0
    while ($offset -lt $received.Length) {
        $count = $server.GetStream().Read($received, $offset, $received.Length - $offset)
        if ($count -le 0) {
            break
        }
        $offset += $count
    }

    Assert-Equal $received.Length $offset "Offline loopback receive length"
    Assert-Equal "4D 5A 00 00 00 00 01 01 00 12 58 28 00 00 03 F1 00 00 00 35 00 00 00 31 00 00 00 00" `
        (Get-Hex $received) "Send boundary must advance the current session sequence"

    Write-Output "TreasurePacketSessionSequenceRegression: PASS"
}
finally {
    $runtime::EndSession()
    $socketList::lstRecPacket.Clear()
    if ($server -ne $null) { $server.Dispose() }
    $client.Dispose()
    $listener.Stop()
}
