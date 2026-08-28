param(
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$buildRoot = if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    Join-Path $repo "WPELibrary\bin\Debug"
}
else {
    [System.IO.Path]::GetFullPath($BuildDirectory)
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $buildRoot "Be.Windows.Forms.HexBox.dll")
Add-Type -Path (Join-Path $buildRoot "WPELibrary.dll")

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw "ASSERT FAILED: $message"
    }
}

$socketList = [WPELibrary.Lib.Socket_Cache+SocketList]
$socketQueue = [WPELibrary.Lib.Socket_Cache+SocketQueue]
$socketPacket = [WPELibrary.Lib.Socket_Cache+SocketPacket]
$runtime = [WPELibrary.Lib.Vision.TreasurePacketRuntime]
$packetType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$filterAction = [WPELibrary.Lib.Socket_Cache+Filter+FilterAction]::None
$originalSpeedMode = $socketPacket::SpeedMode
$listener = $null
$client = $null
$server = $null

try {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0)
    $listener.Start()
    $endpoint = [System.Net.IPEndPoint]$listener.LocalEndpoint
    $client = [System.Net.Sockets.TcpClient]::new()
    $client.Connect([System.Net.IPAddress]::Loopback, $endpoint.Port)
    $server = $listener.AcceptTcpClient()

    $socket = $client.Client.Handle.ToInt32()
    $payload = [byte[]](
        0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x00, 0x12, 0x58, 0x28,
        0x00, 0x00, 0x03, 0xF1,
        0x00, 0x00, 0x00, 0x35,
        0x00, 0x00, 0x00, 0x31,
        0x00, 0x00, 0x00, 0x00)

    $socketList::lstRecPacket.Clear()
    $socketQueue::ResetSocketQueue()
    $runtime::BeginSession()
    $socketList::BeginCaptureSession()
    $socketPacket::SpeedMode = $true

    # Feed one captured outbound frame through the same queue entry point used
    # by the hook. SpeedMode must bypass only the UI queue, not session state.
    $socketQueue::SocketPacket_ToQueue(
        $socket,
        $payload,
        $payload,
        $packetType,
        [WPELibrary.Lib.Socket_Cache+SocketPacket+SockAddr]::new(),
        $filterAction,
        [DateTime]::Now)

    Assert-True ($socketQueue::qSocket_PacketInfo.Count -eq 0) `
        "SpeedMode must keep the captured frame out of the display queue"

    $socketList::StopCaptureSessionPreservingRoutes()
    $socketList::lstRecPacket.Clear()

    $routeTemplate = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $routeTemplate.PacketType = $packetType
    $routeTemplate.PacketTo = $client.Client.RemoteEndPoint.ToString()
    $routeResolution = $socketList::ResolveCurrentRoute($routeTemplate)
    Assert-True $routeResolution.Succeeded `
        "A preset route must remain resolvable after capture stops and the UI list is cleared"
    Assert-True ($routeResolution.Route.Socket -eq $socket) `
        "The retained route must point to the live injected-session socket"

    $saleBuffer = [byte[]](
        0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x10, 0x30, 0x44, 0x00, 0x01, 0x5F, 0xAB,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x25, 0x16,
        0x01, 0x00)
    $sequenceError = ""
    $prepared = $runtime::TryPrepareCurrentSessionSequence(
        $saleBuffer,
        [ref]$sequenceError)
    Assert-True $prepared `
        "A protected preset must retain the current protocol sequence after capture stops"

    Write-Output "CaptureStopPresetRegression: PASS"
}
finally {
    $socketPacket::SpeedMode = $originalSpeedMode
    $socketList::StopCaptureSessionPreservingRoutes()
    $socketQueue::ResetSocketQueue()
    $socketList::lstRecPacket.Clear()
    $runtime::EndSession()
    if ($server -ne $null) { $server.Dispose() }
    if ($client -ne $null) { $client.Dispose() }
    if ($listener -ne $null) { $listener.Stop() }
}
