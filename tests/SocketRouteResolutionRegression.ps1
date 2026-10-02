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

function New-CapturedPacket {
    param(
        [int]$Socket,
        [string]$From,
        [string]$To,
        [DateTime]$CapturedAt,
        [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]$PacketType,
        [byte[]]$Buffer = $null
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

function New-Template {
    param(
        [string]$From,
        [string]$To,
        [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]$PacketType
    )

    $packet = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $packet.PacketFrom = $From
    $packet.PacketTo = $To
    $packet.PacketType = $PacketType
    return $packet
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

$socketList = [WPELibrary.Lib.Socket_Cache+SocketList]
$packetType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_SendTo
$originalCapturedPackets = @($socketList::lstRecPacket)
$originalCaptureSession = $socketList::CaptureSessionStartedAt
Assert-True ($originalCaptureSession -eq [DateTime]::MinValue) `
    "Route regression must start without an active capture-session filter."

$testTcpDisposables = [System.Collections.Generic.List[object]]::new()
try {
    $socketList::lstRecPacket.Clear()
    $baseTime = [DateTime]::Now.AddMinutes(-5)
    $testSockets = [System.Collections.Generic.List[System.Net.Sockets.Socket]]::new()

    $oldSocket = New-TestUdpSocket
    $newSocket = New-TestUdpSocket
    $testSockets.Add($oldSocket)
    $testSockets.Add($newSocket)
    $oldSource = $oldSocket.LocalEndPoint.ToString()
    $newSource = $newSocket.LocalEndPoint.ToString()

    # Reconnection with the same endpoint must select the newest socket handle.
    $socketList::lstRecPacket.Add((New-CapturedPacket $oldSocket.Handle.ToInt32() $oldSource "198.51.100.20:443" $baseTime.AddSeconds(1) $packetType))
    $socketList::lstRecPacket.Add((New-CapturedPacket $newSocket.Handle.ToInt32() $newSource "198.51.100.20:443" $baseTime.AddSeconds(2) $packetType))
    $oldSocket.Dispose()
    $sameTargetTemplate = New-Template $oldSource "198.51.100.20:443" $packetType
    $sameTargetResolution = $socketList::ResolveCurrentRoute($sameTargetTemplate)
    Assert-True $sameTargetResolution.Succeeded `
        "A same-target reconnection must resolve successfully."
    Assert-True ($sameTargetResolution.Route.Socket -eq $newSocket.Handle.ToInt32()) `
        "A same-target reconnection must use the newest socket handle."

    # The screenshot reports WS2_Send. Exercise the native local/peer endpoint
    # refresh with real loopback TCP sockets, not only synthetic route fields.
    $tcpListenerA = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0)
    $tcpListenerB = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0)
    $tcpListenerA.Start()
    $tcpListenerB.Start()
    $tcpClientA = [System.Net.Sockets.TcpClient]::new(
        [System.Net.Sockets.AddressFamily]::InterNetwork)
    $tcpClientB = [System.Net.Sockets.TcpClient]::new(
        [System.Net.Sockets.AddressFamily]::InterNetwork)
    $testTcpDisposables.Add($tcpClientA)
    $testTcpDisposables.Add($tcpClientB)
    $testTcpDisposables.Add($tcpListenerA)
    $testTcpDisposables.Add($tcpListenerB)
    $tcpClientA.Connect(
        [System.Net.IPAddress]::Loopback,
        ([System.Net.IPEndPoint]$tcpListenerA.LocalEndpoint).Port)
    $tcpClientB.Connect(
        [System.Net.IPAddress]::Loopback,
        ([System.Net.IPEndPoint]$tcpListenerB.LocalEndpoint).Port)
    $tcpAcceptedA = $tcpListenerA.AcceptTcpClient()
    $tcpAcceptedB = $tcpListenerB.AcceptTcpClient()
    $testTcpDisposables.Add($tcpAcceptedA)
    $testTcpDisposables.Add($tcpAcceptedB)
    $ws2SendType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
    $tcpSourceA = $tcpClientA.Client.LocalEndPoint.ToString()
    $tcpTargetA = $tcpClientA.Client.RemoteEndPoint.ToString()
    $tcpSourceB = $tcpClientB.Client.LocalEndPoint.ToString()
    $tcpTargetB = $tcpClientB.Client.RemoteEndPoint.ToString()
    $socketList::lstRecPacket.Clear()
    $socketList::lstRecPacket.Add((New-CapturedPacket $tcpClientA.Client.Handle.ToInt32() $tcpSourceA $tcpTargetA $baseTime.AddSeconds(2.1) $ws2SendType))
    $socketList::lstRecPacket.Add((New-CapturedPacket $tcpClientB.Client.Handle.ToInt32() $tcpSourceB $tcpTargetB $baseTime.AddSeconds(2.2) $ws2SendType))
    $tcpTargetTemplate = New-Template "" $tcpTargetA $ws2SendType
    $tcpTargetResolution = $socketList::ResolveCurrentRoute($tcpTargetTemplate)
    Assert-True $tcpTargetResolution.Succeeded `
        "WS2_Send must resolve from the current native peer endpoint."
    Assert-True ($tcpTargetResolution.Route.Socket -eq $tcpClientA.Client.Handle.ToInt32()) `
        "WS2_Send target matching must select the matching live TCP socket."
    Assert-True ($tcpTargetResolution.Route.PacketFrom -eq $tcpSourceA) `
        "WS2_Send route resolution must refresh the current local endpoint."
    Assert-True ($tcpTargetResolution.Route.PacketTo -eq $tcpTargetA) `
        "WS2_Send route resolution must refresh the current peer endpoint."

    # A changed target is allowed only when the current capture has one candidate.
    $socketList::lstRecPacket.Clear()
    $changedSocket = New-TestUdpSocket
    $testSockets.Add($changedSocket)
    $changedSource = $changedSocket.LocalEndPoint.ToString()
    $socketList::lstRecPacket.Add((New-CapturedPacket $changedSocket.Handle.ToInt32() $changedSource "203.0.113.40:443" $baseTime.AddSeconds(3) $packetType))
    $changedTargetTemplate = New-Template $changedSource "198.51.100.20:443" $packetType
    $changedTargetResolution = $socketList::ResolveCurrentRoute($changedTargetTemplate)
    Assert-True $changedTargetResolution.Succeeded `
        "A changed target with one current candidate must resolve successfully."
    Assert-True ($changedTargetResolution.Route.Socket -eq $changedSocket.Handle.ToInt32()) `
        "A changed target must use the only current candidate socket."
    Assert-True ($changedTargetResolution.Route.PacketTo -eq "203.0.113.40:443") `
        "A changed target must return the current target address."
    Assert-True ($changedTargetTemplate.PacketTo -eq "198.51.100.20:443") `
        "Runtime route resolution must not mutate the saved packet template."

    # A legacy template without a destination can still be bound safely when
    # its captured source endpoint identifies one of several current routes.
    $socketList::lstRecPacket.Clear()
    $sourceFirstSocket = New-TestUdpSocket
    $sourceSecondSocket = New-TestUdpSocket
    $testSockets.Add($sourceFirstSocket)
    $testSockets.Add($sourceSecondSocket)
    $sourceFirst = $sourceFirstSocket.LocalEndPoint.ToString()
    $sourceSecond = $sourceSecondSocket.LocalEndPoint.ToString()
    $socketList::lstRecPacket.Add((New-CapturedPacket $sourceFirstSocket.Handle.ToInt32() $sourceFirst "203.0.113.42:443" $baseTime.AddSeconds(3.5) $packetType))
    $socketList::lstRecPacket.Add((New-CapturedPacket $sourceSecondSocket.Handle.ToInt32() $sourceSecond "203.0.113.43:443" $baseTime.AddSeconds(3.6) $packetType))
    $sourceOnlyTemplate = New-Template $sourceFirst "" $packetType
    $sourceOnlyResolution = $socketList::ResolveCurrentRoute($sourceOnlyTemplate)
    Assert-True $sourceOnlyResolution.Succeeded `
        "A missing destination with a matching source endpoint must resolve safely."
    Assert-True ($sourceOnlyResolution.Route.Socket -eq $sourceFirstSocket.Handle.ToInt32()) `
        "A source endpoint match must select its own current connection."

    # A legacy template without either endpoint can use an exact current
    # packet match, while a packet-type-only match must remain fail-closed.
    $socketList::lstRecPacket.Clear()
    $payloadFirstSocket = New-TestUdpSocket
    $payloadSecondSocket = New-TestUdpSocket
    $testSockets.Add($payloadFirstSocket)
    $testSockets.Add($payloadSecondSocket)
    $payloadFirst = $payloadFirstSocket.LocalEndPoint.ToString()
    $payloadSecond = $payloadSecondSocket.LocalEndPoint.ToString()
    $firstBuffer = [byte[]](0x10, 0x20, 0x30)
    $secondBuffer = [byte[]](0x40, 0x50, 0x60)
    $socketList::lstRecPacket.Add((New-CapturedPacket $payloadFirstSocket.Handle.ToInt32() $payloadFirst "203.0.113.44:443" $baseTime.AddSeconds(3.7) $packetType -Buffer $firstBuffer))
    $socketList::lstRecPacket.Add((New-CapturedPacket $payloadSecondSocket.Handle.ToInt32() $payloadSecond "203.0.113.45:443" $baseTime.AddSeconds(3.8) $packetType -Buffer $secondBuffer))
    $payloadOnlyTemplate = New-Template "" "" $packetType
    $payloadOnlyTemplate.PacketBuffer = $firstBuffer
    $payloadOnlyResolution = $socketList::ResolveCurrentRoute($payloadOnlyTemplate)
    Assert-True $payloadOnlyResolution.Succeeded `
        "An exact current packet match must bind an endpoint-free legacy template."
    Assert-True ($payloadOnlyResolution.Route.Socket -eq $payloadFirstSocket.Handle.ToInt32()) `
        "An exact packet match must select the route that carried the packet."

    # Multiple current candidates must fail closed instead of guessing.
    $socketList::lstRecPacket.Clear()
    $socketList::lstRecPacket.Add((New-CapturedPacket $changedSocket.Handle.ToInt32() $changedSource "203.0.113.40:443" $baseTime.AddSeconds(4) $packetType))
    $ambiguousSocket = New-TestUdpSocket
    $testSockets.Add($ambiguousSocket)
    $ambiguousSource = $ambiguousSocket.LocalEndPoint.ToString()
    $socketList::lstRecPacket.Add((New-CapturedPacket $ambiguousSocket.Handle.ToInt32() $ambiguousSource "203.0.113.41:443" $baseTime.AddSeconds(4) $packetType))
    $ambiguousTemplate = New-Template "" "198.51.100.20:443" $packetType
    $ambiguousResolution = $socketList::ResolveCurrentRoute($ambiguousTemplate)
    Assert-True ($ambiguousResolution.Status.ToString() -eq "Ambiguous") `
        "Multiple changed-target candidates must return Ambiguous."
    Assert-True ($ambiguousResolution.ErrorCode -eq "runtime_route_ambiguous") `
        "Ambiguous routes must expose the mobile runtime error code."
    Assert-True ($ambiguousResolution.Candidates.Count -eq 2) `
        "Ambiguous resolution must report both distinct endpoint candidates."

    # An explicitly selected current system Socket may disambiguate a legacy
    # endpoint-free preset, but an unrelated/stale handle must not do so.
    $preferredResolution = $socketList::ResolveCurrentRoute(
        $ambiguousTemplate,
        $ambiguousSocket.Handle.ToInt32())
    Assert-True $preferredResolution.Succeeded `
        "An explicitly selected live Socket must disambiguate a legacy preset."
    Assert-True ($preferredResolution.Route.Socket -eq $ambiguousSocket.Handle.ToInt32()) `
        "A preferred Socket must select only its own current route."
    $stalePreferredResolution = $socketList::ResolveCurrentRoute(
        $ambiguousTemplate,
        2147483647)
    Assert-True ($stalePreferredResolution.Status.ToString() -eq "Ambiguous") `
        "An unrelated preferred Socket must not bypass route ambiguity."

    # A preset containing packets from different connections must resolve each packet independently.
    $socketList::lstRecPacket.Clear()
    $firstSocket = New-TestUdpSocket
    $secondSocket = New-TestUdpSocket
    $testSockets.Add($firstSocket)
    $testSockets.Add($secondSocket)
    $firstSource = $firstSocket.LocalEndPoint.ToString()
    $secondSource = $secondSocket.LocalEndPoint.ToString()
    $socketList::lstRecPacket.Add((New-CapturedPacket $firstSocket.Handle.ToInt32() $firstSource "203.0.113.50:443" $baseTime.AddSeconds(5) $packetType))
    $socketList::lstRecPacket.Add((New-CapturedPacket $secondSocket.Handle.ToInt32() $secondSource "203.0.113.51:443" $baseTime.AddSeconds(6) $packetType))
    $firstTemplate = New-Template $firstSource "203.0.113.50:443" $packetType
    $secondTemplate = New-Template $secondSource "203.0.113.51:443" $packetType
    $templates = [WPELibrary.Lib.Socket_PacketInfo[]]@($firstTemplate, $secondTemplate)
    $batchResolution = $socketList::ResolveCurrentRoutes($templates)
    Assert-True $batchResolution.Succeeded `
        "A multi-connection preset must pass route preflight."
    Assert-True ($batchResolution.Items[0].Route.Socket -eq $firstSocket.Handle.ToInt32()) `
        "The first packet must use its own connection socket."
    Assert-True ($batchResolution.Items[1].Route.Socket -eq $secondSocket.Handle.ToInt32()) `
        "The second packet must use its own connection socket."
    Assert-True ($batchResolution.Items[0].Route.PacketFrom -eq $firstSource) `
        "The first packet must carry its current source address."
    Assert-True ($batchResolution.Items[1].Route.PacketFrom -eq $secondSource) `
        "The second packet must carry its current source address."

    # No current candidate must prevent the send preflight from starting.
    $socketList::lstRecPacket.Clear()
    $missingTemplate = New-Template "10.0.0.2:5000" "198.51.100.99:443" $packetType
    $missingResolution = $socketList::ResolveCurrentRoute($missingTemplate)
    Assert-True ($missingResolution.Status.ToString() -eq "NotConnected") `
        "A missing current connection must return NotConnected."
    Assert-True ($missingResolution.ErrorCode -eq "runtime_not_connected") `
        "A missing current connection must expose runtime_not_connected."

    $missingPresetId = [Guid]::NewGuid()
    $missingPreset = [WPELibrary.Lib.Socket_SendInfo]::new(
        $true,
        $missingPresetId,
        "route preflight",
        $true,
        1,
        0,
        [System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]]::new(
            [WPELibrary.Lib.Socket_PacketInfo[]]@($missingTemplate)),
        "",
        "route test",
        1)
    [WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Add($missingPreset)
    try {
        $sendResult = [WPELibrary.Lib.Socket_Cache+Send]::DoSendWithResult($missingPresetId)
        Assert-True ($null -eq $sendResult.Send) `
            "A missing route must prevent the send worker from starting."
        Assert-True ($sendResult.ErrorCode -eq "runtime_not_connected") `
            "A missing route preflight must return runtime_not_connected."
    }
    finally {
        [WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Remove($missingPreset)
    }

    $socketSource = [System.IO.File]::ReadAllText(
        (Join-Path $repo "WPELibrary\Lib\Socket_Send.cs"),
        [System.Text.Encoding]::UTF8)
    Assert-True ($socketSource.Contains("spi.PacketFrom, spi.PacketTo")) `
        "Route-aware sends must pass both current addresses to the send operation."
}
finally {
    if ($null -ne $testSockets) {
        foreach ($testSocket in $testSockets) {
            $testSocket.Dispose()
        }
    }
    foreach ($tcpDisposable in $testTcpDisposables) {
        if ($tcpDisposable -is [System.Net.Sockets.TcpListener]) {
            $tcpDisposable.Stop()
        }
        else {
            $tcpDisposable.Dispose()
        }
    }
    $socketList::lstRecPacket.Clear()
    foreach ($capturedPacket in $originalCapturedPackets) {
        $socketList::lstRecPacket.Add($capturedPacket)
    }
}

Write-Output "Socket route resolution regression checks passed."
