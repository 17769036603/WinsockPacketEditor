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
        [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]$PacketType
    )

    $packet = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $packet.PacketSocket = $Socket
    $packet.PacketType = $PacketType
    $packet.PacketFrom = $From
    $packet.PacketTo = $To
    $packet.PacketTime = $CapturedAt
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

    # Multiple current candidates must fail closed instead of guessing.
    $ambiguousSocket = New-TestUdpSocket
    $testSockets.Add($ambiguousSocket)
    $ambiguousSource = $ambiguousSocket.LocalEndPoint.ToString()
    $socketList::lstRecPacket.Add((New-CapturedPacket $ambiguousSocket.Handle.ToInt32() $ambiguousSource "203.0.113.41:443" $baseTime.AddSeconds(4) $packetType))
    $ambiguousResolution = $socketList::ResolveCurrentRoute($changedTargetTemplate)
    Assert-True ($ambiguousResolution.Status.ToString() -eq "Ambiguous") `
        "Multiple changed-target candidates must return Ambiguous."
    Assert-True ($ambiguousResolution.ErrorCode -eq "runtime_route_ambiguous") `
        "Ambiguous routes must expose the mobile runtime error code."
    Assert-True ($ambiguousResolution.Candidates.Count -eq 2) `
        "Ambiguous resolution must report both distinct endpoint candidates."

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
    $socketList::lstRecPacket.Clear()
    foreach ($capturedPacket in $originalCapturedPackets) {
        $socketList::lstRecPacket.Add($capturedPacket)
    }
}

Write-Output "Socket route resolution regression checks passed."
