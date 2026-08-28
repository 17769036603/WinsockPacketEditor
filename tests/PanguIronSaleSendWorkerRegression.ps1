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

    $sale = New-Object WPELibrary.Lib.Socket_PacketInfo
    $sale.PacketTime = [DateTime]::Now
    $sale.PacketSocket = $socket
    $sale.PacketType = $packetType
    $sale.PacketFrom = $from
    $sale.PacketTo = $to
    $sale.PacketBuffer = [byte[]](
        0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x10, 0x30, 0x44, 0x00, 0x01, 0x5F, 0xAB,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x25, 0x16,
        0x01, 0x00)
    $sale.PacketLen = $sale.PacketBuffer.Length

    $collection = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]'
    $collection.Add($sale)
    $worker = New-Object WPELibrary.Lib.Socket_Send
    $started = $worker.StartSendWithPacketSockets(
        "出售盘古精铁",
        1,
        0,
        $collection)
    Assert-True $started "The protected sale worker must start on a live loopback route"
    Assert-True ($worker.WaitForCompletion(5000)) "The protected sale worker must complete"

    $received = New-Object byte[] $sale.PacketBuffer.Length
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

    Assert-Equal 1 $worker.Send_Success "The protected sale worker success count"
    Assert-Equal 0 $worker.Send_Failure "The protected sale worker failure count"
    Assert-Equal $received.Length $offset "The protected sale worker receive length"
    Assert-Equal "00 00 00 02" (Get-Hex $received[4..7]) `
        "The protected sale worker must send the next current-session sequence"

    Write-Output "PanguIronSaleSendWorkerRegression: PASS"
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
