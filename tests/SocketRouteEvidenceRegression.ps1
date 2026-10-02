param(
    [string]$Configuration = "Debug",
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$buildRoot = if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    Join-Path $repo "WPELibrary\bin\$Configuration"
}
else {
    [System.IO.Path]::GetFullPath($BuildDirectory)
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $buildRoot "Be.Windows.Forms.HexBox.dll")
Add-Type -Path (Join-Path $buildRoot "WPELibrary.dll")

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function New-TestUdpSocket {
    $socket = [System.Net.Sockets.Socket]::new(
        [System.Net.Sockets.AddressFamily]::InterNetwork,
        [System.Net.Sockets.SocketType]::Dgram,
        [System.Net.Sockets.ProtocolType]::Udp)
    $socket.Bind([System.Net.IPEndPoint]::new(
        [System.Net.IPAddress]::Loopback,
        0))
    return $socket
}

function New-CapturedPacket {
    param(
        [int]$Socket,
        [string]$From,
        [string]$To,
        [DateTime]$CapturedAt,
        [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]$PacketType,
        [byte[]]$Buffer
    )

    $packet = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $packet.PacketSocket = $Socket
    $packet.PacketType = $PacketType
    $packet.PacketFrom = $From
    $packet.PacketTo = $To
    $packet.PacketTime = $CapturedAt
    $packet.PacketBuffer = $Buffer
    return $packet
}

$socketList = [WPELibrary.Lib.Socket_Cache+SocketList]
$packetType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_SendTo
$originalCapturedPackets = @($socketList::lstRecPacket)
$originalCaptureSession = $socketList::CaptureSessionStartedAt
Assert-True ($originalCaptureSession -eq [DateTime]::MinValue) `
    "Route evidence regression must start without an active capture session."

$testSockets = [System.Collections.Generic.List[System.Net.Sockets.Socket]]::new()
try {
    $socketList::lstRecPacket.Clear()
    $socketA = New-TestUdpSocket
    $socketB = New-TestUdpSocket
    $testSockets.Add($socketA)
    $testSockets.Add($socketB)

    $sourceA = $socketA.LocalEndPoint.ToString()
    $sourceB = $socketB.LocalEndPoint.ToString()
    $targetA = "203.0.113.60:443"
    $targetB = "203.0.113.61:443"
    $bufferA = [byte[]](0xA1, 0xA2, 0xA3)
    $bufferA2 = [byte[]](0xB1, 0xB2, 0xB3)
    $bufferB = [byte[]](0xC1, 0xC2, 0xC3)

    $socketList::BeginCaptureSession()
    $baseTime = [DateTime]::Now.AddSeconds(1)
    $observeMethod = [WPELibrary.Lib.Socket_Cache+SocketList].GetMethod(
        "ObserveCurrentSocketRouteEvidence",
        [System.Reflection.BindingFlags]::Static -bor
        [System.Reflection.BindingFlags]::NonPublic)
    Assert-True ($null -ne $observeMethod) `
        "The current-session route evidence observer must remain available."

    $packetA = New-CapturedPacket $socketA.Handle.ToInt32() $sourceA $targetA `
        $baseTime $packetType $bufferA
    $packetA2 = New-CapturedPacket $socketA.Handle.ToInt32() $sourceA $targetA `
        $baseTime.AddMilliseconds(1) $packetType $bufferA2
    $packetB = New-CapturedPacket $socketB.Handle.ToInt32() $sourceB $targetB `
        $baseTime.AddMilliseconds(2) $packetType $bufferB
    [void]$observeMethod.Invoke($null, [object[]]@($packetA))
    [void]$observeMethod.Invoke($null, [object[]]@($packetA2))
    [void]$observeMethod.Invoke($null, [object[]]@($packetB))

    # Simulate AutoClear: the UI list is empty, but the current-session cache
    # must still retain enough metadata to identify an older packet on socket A.
    $socketList::lstRecPacket.Clear()
    $template = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $template.PacketType = $packetType
    $template.PacketBuffer = $bufferA
    $resolution = $socketList::ResolveCurrentRoute($template)
    Assert-True $resolution.Succeeded `
        "A cleared display list must still resolve from current-session evidence."
    Assert-True ($resolution.Route.Socket -eq $socketA.Handle.ToInt32()) `
        "Retained packet evidence must select the socket that carried the packet."
    Assert-True ($resolution.Route.PacketTo -eq $targetA) `
        "Retained route evidence must preserve the current target address."
}
finally {
    $socketList::StopCaptureSessionPreservingRoutes()
    if ($null -ne $testSockets) {
        foreach ($testSocket in $testSockets) {
            $testSocket.Dispose()
        }
    }
    $socketList::lstRecPacket.Clear()
    foreach ($capturedPacket in $originalCapturedPackets) {
        $socketList::lstRecPacket.Add($capturedPacket)
    }
}

Write-Output "Socket route evidence regression checks passed."
