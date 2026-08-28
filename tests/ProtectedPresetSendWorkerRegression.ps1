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

function Convert-HexToBytes([string]$hex) {
    $normalized = ($hex -replace '\s', '')
    if (($normalized.Length % 2) -ne 0) {
        throw "Hex snapshot has an odd number of digits: $hex"
    }

    $bytes = New-Object byte[] ($normalized.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte(
            $normalized.Substring($index * 2, 2),
            16)
    }
    return $bytes
}

function Get-Hex([byte[]]$bytes) {
    return (($bytes | ForEach-Object { $_.ToString("X2") }) -join "")
}

function Set-Sequence([byte[]]$bytes, [uint32]$sequence) {
    $bytes[4] = [byte](($sequence -shr 24) -band 0xFF)
    $bytes[5] = [byte](($sequence -shr 16) -band 0xFF)
    $bytes[6] = [byte](($sequence -shr 8) -band 0xFF)
    $bytes[7] = [byte]($sequence -band 0xFF)
}

$presets = @(
    [pscustomobject]@{
        Name = "积分一"
        Hex = "4D5A00000009D96C0023304400015FAB0000000A00012690011332303932313436373039393635373235363936"
    },
    [pscustomobject]@{
        Name = "积分二"
        Hex = "4D5A0000000000000010304400015FAB00000001000125110100"
    },
    [pscustomobject]@{
        Name = "积分三"
        Hex = "4D5A0000000000000010304400015FAB00000001000125100100"
    },
    [pscustomobject]@{
        Name = "积分四"
        Hex = "4D5A0000000000000010304400015FAB000000010001250B0100"
    },
    [pscustomobject]@{
        Name = "积分五"
        Hex = "4D5A0000000000000010304400015FAB000000010001252C0100"
    },
    [pscustomobject]@{
        Name = "积分六"
        Hex = "4D5A0000000000000010304400015FAB00000001000125270100"
    },
    [pscustomobject]@{
        Name = "积分七"
        Hex = "4D5A0000000000000010304400015FAB000000010001250B0100"
    },
    [pscustomobject]@{
        Name = "百亿玉"
        Hex = "4D5A0000000000000023406200000001184063757272656E63793D3D313A313030303030303030303000000000"
    },
    [pscustomobject]@{
        Name = "百亿银子"
        Hex = "4D5A0000000000000023406200000001184063757272656E63793D3D323A313030303030303030303000000000"
    },
    [pscustomobject]@{
        Name = "百亿师贡献"
        Hex = "4D5A0000000000000023406200000001184063757272656E63793D3D333A313030303030303030303000000000"
    },
    [pscustomobject]@{
        Name = "百亿帮贡"
        Hex = "4D5A0000000000000024406200000001194063757272656E63793D3D31333A313030303030303030303000000000"
    },
    [pscustomobject]@{
        Name = "百亿成就"
        Hex = "4D5A0000000000000024406200000001194063757272656E63793D3D31373A313030303030303030303000000000"
    },
    [pscustomobject]@{
        Name = "百亿积分"
        Hex = "4D5A0000000000000024406200000001194063757272656E63793D3D31353A313030303030303030303000000000"
    },
    [pscustomobject]@{
        Name = "炼星石"
        Hex = "4D5A000000000000001B40620000000110406974656D3D3D39393033312C39393900000000"
    },
    [pscustomobject]@{
        Name = "积分"
        Hex = "4D5A000000000000001B40620000000110406974656D3D3D39393030382C39393900000000"
    },
    [pscustomobject]@{
        Name = "嘉嘉的嫁妆"
        Hex = "4D5A00000000000000194062000000010E406974656D3D3D39393035312C3100000000"
    },
    [pscustomobject]@{
        Name = "扭转乾坤"
        Hex = "4D5A000000000000001A4062000000010F406974656D3D3D39323230332C393900000000"
    },
    [pscustomobject]@{
        Name = "子虚乌有"
        Hex = "4D5A000000000000001A4062000000010F406974656D3D3D39323230342C393900000000"
    },
    [pscustomobject]@{
        Name = "化无"
        Hex = "4D5A000000000000001A4062000000010F406974656D3D3D39323230382C393900000000"
    },
    [pscustomobject]@{
        Name = "成仁取义"
        Hex = "4D5A000000000000001A4062000000010F406974656D3D3D39323231302C393900000000"
    },
    [pscustomobject]@{
        Name = "抗性"
        Hex = "4D5A0000000A2623000E70AB0B7B273230323037273A307D"
    },
    [pscustomobject]@{
        Name = "超级宝图"
        Hex = "4D5A0000000000000009F9080000002B013400"
    }
)

$packetType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$socketList = [WPELibrary.Lib.Socket_Cache+SocketList]
$runtime = [WPELibrary.Lib.Vision.TreasurePacketRuntime]
$listener = $null
$client = $null
$server = $null
$worker = $null

try {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Parse("127.0.0.1"),
        0)
    $listener.Start()
    $endpoint = [System.Net.IPEndPoint]$listener.LocalEndpoint
    $client = [System.Net.Sockets.TcpClient]::new(
        [System.Net.Sockets.AddressFamily]::InterNetwork)
    $client.Connect("127.0.0.1", $endpoint.Port)
    $server = $listener.AcceptTcpClient()
    $server.ReceiveTimeout = 5000

    $from = $client.Client.LocalEndPoint.ToString()
    $to = $client.Client.RemoteEndPoint.ToString()
    $socket = $client.Client.Handle.ToInt32()

    $socketList::lstRecPacket.Clear()
    $socketList::BeginCaptureSession()
    $runtime::BeginSession()

    $observed = New-Object WPELibrary.Lib.Socket_PacketInfo
    $observed.PacketTime = [DateTime]::Now
    $observed.PacketSocket = $socket
    $observed.PacketType = $packetType
    $observed.PacketFrom = $from
    $observed.PacketTo = $to
    $observed.PacketBuffer = [byte[]](
        0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
        0x00, 0x12, 0x58, 0x28,
        0x00, 0x00, 0x03, 0xF1,
        0x00, 0x00, 0x00, 0x35,
        0x00, 0x00, 0x00, 0x31,
        0x00, 0x00, 0x00, 0x00)
    $observed.PacketLen = $observed.PacketBuffer.Length
    $socketList::lstRecPacket.Add($observed)
    $runtime::ObserveCapturedPacket($observed)

    [uint32]$expectedSequence = 2
    foreach ($preset in $presets) {
        $source = Convert-HexToBytes $preset.Hex
        $sourceHex = Get-Hex $source
        $packet = New-Object WPELibrary.Lib.Socket_PacketInfo
        $packet.PacketTime = [DateTime]::Now
        $packet.PacketSocket = $socket
        $packet.PacketType = $packetType
        $packet.PacketFrom = $from
        $packet.PacketTo = $to
        $packet.PacketBuffer = $source
        $packet.PacketLen = $source.Length

        $collection = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]'
        $collection.Add($packet)
        $worker = New-Object WPELibrary.Lib.Socket_Send
        $started = $worker.StartSendWithPacketSockets(
            $preset.Name,
            1,
            0,
            $collection)
        Assert-True $started "$($preset.Name) protected worker must start on a live loopback route"
        Assert-True ($worker.WaitForCompletion(5000)) "$($preset.Name) protected worker must complete"

        $received = New-Object byte[] $source.Length
        $offset = 0
        while ($offset -lt $received.Length) {
            $count = $server.GetStream().Read(
                $received,
                $offset,
                $received.Length - $offset)
            if ($count -le 0) {
                break
            }
            $offset += $count
        }

        Assert-Equal 1 $worker.Send_Success "$($preset.Name) protected worker success count"
        Assert-Equal 0 $worker.Send_Failure "$($preset.Name) protected worker failure count"
        Assert-Equal $source.Length $offset "$($preset.Name) protected worker receive length"

        $expected = [byte[]]$source.Clone()
        Set-Sequence $expected $expectedSequence
        Assert-Equal (Get-Hex $expected) (Get-Hex $received) `
            "$($preset.Name) must preserve its saved bytes except for the live session sequence"
        Assert-Equal $sourceHex (Get-Hex $packet.PacketBuffer) `
            "$($preset.Name) source packet bytes must not be modified in memory"

        $worker.StopSend()
        $worker = $null
        $expectedSequence++
    }

    Write-Output "ProtectedPresetSendWorkerRegression: PASS"
}
finally {
    if ($worker -ne $null) {
        $worker.StopSend()
    }
    $runtime::EndSession()
    $socketList::lstRecPacket.Clear()
    if ($server -ne $null) { $server.Dispose() }
    if ($client -ne $null) { $client.Dispose() }
    if ($listener -ne $null) { $listener.Stop() }
}
