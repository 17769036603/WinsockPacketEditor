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

$encoder = [WPELibrary.Lib.Vision.TreasurePacketEncoder]
$jumpRequest = New-Object WPELibrary.Lib.Vision.TreasureJumpPacketRequest(1009, 19, 65)
$jumpBytes = $encoder::EncodeJump($jumpRequest)
Assert-Equal 28 $jumpBytes.Length "Jump encoded length"
Assert-Equal "4D 5A 00 00 00 00 00 00 00 12 58 28 00 00 03 F1 00 00 00 13 00 00 00 41 00 00 00 00" (Get-Hex $jumpBytes) "Jump encoded bytes"
Assert-True $jumpRequest.Equals($encoder::DecodeJump($jumpBytes)) "Jump encode/decode round-trip"

$useRequest = New-Object WPELibrary.Lib.Vision.TreasureUsePacketRequest(13, 13, 1, "2")
$useBytes = $encoder::EncodeUse($useRequest)
Assert-Equal 26 $useBytes.Length "Use encoded length"
Assert-Equal "4D 5A 00 00 00 00 00 00 00 10 78 3A 00 00 00 0D 00 00 00 0D 00 00 00 01 01 32" (Get-Hex $useBytes) "Use encoded bytes"
$decodedUse = $encoder::DecodeUse($useBytes)
Assert-Equal 13 $decodedUse.PackageNum "Use pos/packageNum binding"
Assert-Equal 13 $decodedUse.Type "Use type round-trip"
Assert-Equal 1 $decodedUse.Num "Use num round-trip"
Assert-Equal "2" $decodedUse.Param "Use param round-trip"

$autoDigBytes = $encoder::EncodeAutoDig()
Assert-Equal 24 $autoDigBytes.Length "AutoDig encoded length"
Assert-Equal "4D 5A 00 00 00 00 00 00 00 0C B0 F4 00 02 00 00 00 07 D0 00 00 00 07 D1" (Get-Hex $autoDigBytes) "AutoDig encoded bytes"
$encoder::ValidateAutoDig($autoDigBytes)
$legacyAutoDigBytes = [byte[]](
    0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C,
    0xB0, 0xF4, 0x00, 0x02, 0x00, 0x00, 0x07, 0xD0, 0x00, 0x00,
    0x07, 0xD1)
$autoDigContract = [WPELibrary.Lib.Vision.TreasureAutoDigPacketContract]
Assert-True $autoDigContract::IsFrame($legacyAutoDigBytes) `
    "Existing ordinary send-preset AutoDig frame remains accepted"

$badUseLength = [byte[]]$useBytes.Clone()
$badUseLength[24] = 2
$threw = $false
try { $encoder::DecodeUse($badUseLength) | Out-Null } catch { $threw = $true }
Assert-True $threw "Use length mismatch must be rejected"

$packetType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$routePacket = New-Object WPELibrary.Lib.Socket_PacketInfo
$routePacket.PacketSocket = 6168
$routePacket.PacketType = $packetType
$routePacket.PacketFrom = "127.0.0.1:50000"
$routePacket.PacketTo = "127.0.0.1:12345"
$routePacket.PacketBuffer = [byte[]](0x4D, 0x5A, 0, 0, 0, 0, 0, 0, 0, 0, 0x10, 0x01)
$routePacket.PacketLen = $routePacket.PacketBuffer.Length
$captured = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_PacketInfo]'
$captured.Add($routePacket)
$runtime = [WPELibrary.Lib.Vision.TreasurePacketRuntime]
$route = $runtime::DiscoverOutgoingRoute($captured)
Assert-Equal $routePacket.PacketTo $route.PacketTo "Current route destination"

# Existing-connection fallback: a current IPv4 socket and an already captured
# outgoing game frame must be enough to resolve the treasure route without
# starting a new hook capture session.
$listener = [System.Net.Sockets.TcpListener]::new(
    [System.Net.IPAddress]::Parse("127.0.0.1"),
    0)
$listener.Start()
$fallbackClient = [System.Net.Sockets.TcpClient]::new(
    [System.Net.Sockets.AddressFamily]::InterNetwork)
$fallbackClient.Connect(
    [System.Net.IPAddress]::Parse("127.0.0.1"),
    ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port)
$fallbackServer = $listener.AcceptTcpClient()
try {
    $fallbackPacket = New-Object WPELibrary.Lib.Socket_PacketInfo
    $fallbackPacket.PacketTime = [DateTime]::Now
    $fallbackPacket.PacketSocket = $fallbackClient.Client.Handle.ToInt32()
    $fallbackPacket.PacketType = $packetType
    $fallbackPacket.PacketFrom = $fallbackClient.Client.LocalEndPoint.ToString()
    $fallbackPacket.PacketTo = $fallbackClient.Client.RemoteEndPoint.ToString()
    $fallbackPacket.PacketBuffer = $jumpBytes
    $fallbackPacket.PacketLen = $jumpBytes.Length
    $fallbackUsePacket = New-Object WPELibrary.Lib.Socket_PacketInfo
    $fallbackUsePacket.PacketTime = [DateTime]::Now.AddMilliseconds(1)
    $fallbackUsePacket.PacketSocket = $fallbackPacket.PacketSocket
    $fallbackUsePacket.PacketType = $packetType
    $fallbackUsePacket.PacketFrom = $fallbackPacket.PacketFrom
    $fallbackUsePacket.PacketTo = $fallbackPacket.PacketTo
    $fallbackUsePacket.PacketBuffer = $useBytes
    $fallbackUsePacket.PacketLen = $useBytes.Length
    [WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Clear()
    $runtime::BeginSession()
    [WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Add($fallbackPacket)
    [WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Add($fallbackUsePacket)
    $fallbackRoute = $runtime::GetCurrentRoute()
    Assert-Equal $fallbackPacket.PacketTo $fallbackRoute.PacketTo `
        "Existing connection route fallback destination"
    $capturedUse = $runtime::GetCurrentUseRequest(8)
    Assert-Equal 8 $capturedUse.PackageNum "Current capture Use slot binding"
    Assert-Equal 13 $capturedUse.Type "Current capture Use type"
    Assert-Equal 1 $capturedUse.Num "Current capture Use count"
    Assert-Equal "2" $capturedUse.Param "Current capture Use parameter"

    $savedAutoDigPacket = New-Object WPELibrary.Lib.Socket_PacketInfo
    $savedAutoDigPacket.PacketType = $packetType
    $savedAutoDigPacket.PacketFrom = $fallbackPacket.PacketFrom
    $savedAutoDigPacket.PacketTo = $fallbackPacket.PacketTo
    $savedAutoDigPacket.PacketBuffer = $legacyAutoDigBytes
    $savedAutoDigPacket.PacketLen = $legacyAutoDigBytes.Length
    $savedAutoDigCollection = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]'
    $savedAutoDigCollection.Add($savedAutoDigPacket)
    $savedAutoDigPreset = New-Object -TypeName WPELibrary.Lib.Socket_SendInfo -ArgumentList @($false, [Guid]::NewGuid(), "treasure-map", $false, 1, 1000, $savedAutoDigCollection, "")
    [WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Add($savedAutoDigPreset)
    try {
        $currentAutoDig = $runtime::GetCurrentAutoDigPacket($fallbackRoute)
        $legacyAutoDigHex = Get-Hex $legacyAutoDigBytes
        $currentAutoDigHex = Get-Hex $currentAutoDig.PacketBuffer
        Assert-Equal $legacyAutoDigHex $currentAutoDigHex "Saved AutoDig template bytes"
        Assert-Equal $fallbackRoute.PacketTo $currentAutoDig.PacketTo "Saved AutoDig current route destination"
        Assert-Equal 0 $currentAutoDig.PacketSocket "Saved AutoDig template must not retain its old Socket"
    } finally {
        [WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Remove($savedAutoDigPreset)
    }
} finally {
    [WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Clear()
    $runtime::EndSession()
    $fallbackServer.Dispose()
    $fallbackClient.Dispose()
    $listener.Stop()
}

$state = @'
{
  "event": "treasure_inventory_resident_state",
  "available": true,
  "actionAuthorized": false,
  "items": [
    {
      "memberIdentity": "bag-item-13",
      "slot": 13,
      "packageNum": 13,
      "scene": 1009,
      "mapId": 1009,
      "x": 19,
      "y": 65
    }
  ]
}
'@
$prepared = $runtime::PrepareEncodedFromResidentState($state, $route, $useRequest)
Assert-Equal 13 $prepared.Target.PackageNum "Encoded runtime target packageNum"
Assert-Equal (Get-Hex $jumpBytes) (Get-Hex $prepared.JumpPacket.PacketBuffer) "Encoded runtime Jump"
Assert-Equal (Get-Hex $useBytes) (Get-Hex $prepared.UsePacket.PacketBuffer) "Encoded runtime Use"
Assert-Equal (Get-Hex $autoDigBytes) (Get-Hex $prepared.AutoDigPacket.PacketBuffer) "Encoded runtime AutoDig"
Assert-Equal 0 $prepared.JumpPacket.PacketSocket "Encoded preparation must not retain Socket"
Assert-Equal 0 $prepared.AutoDigPacket.PacketSocket "Encoded AutoDig preparation must not retain Socket"
Assert-Equal $false $prepared.PacketSend "Encoded preparation must not send"

$runtime::BeginSession()
$runtime::ObserveCapturedPacket($routePacket)
$sessionRoute = $runtime::GetSessionRoute()
Assert-Equal $routePacket.PacketTo $sessionRoute.PacketTo "Session route destination"
[WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Clear()
Assert-Equal 6168 ($runtime::ResolveCurrentSessionSocket($sessionRoute)) "Session route Socket"
$notAuthorized = $runtime::SendPreparedPacketOnce($prepared.JumpPacket, $null)
Assert-Equal $false $notAuthorized.Success "Live send remains gated"
Assert-Equal "live_send_not_authorized" $notAuthorized.Code "Live send gate code"

Write-Output "TreasurePacketEncoderRegression: PASS"
