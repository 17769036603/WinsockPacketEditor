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

$target = New-Object WPELibrary.Lib.Vision.TreasureInventoryTarget(13, 1009, 53, 49)
$patcher = [WPELibrary.Lib.Vision.TreasurePacketTemplatePatcher]

$jump = $patcher::BuildJump($target)
Assert-Equal 28 $jump.Length "Jump frame length"
Assert-Equal "4D 5A 00 00 00 00 00 00 00 12 58 28 00 00 03 F1 00 00 00 35 00 00 00 31 00 00 00 00" (Get-Hex $jump) "Jump target bytes"

$jumpTemplate = [byte[]](
    0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x12,
    0x58, 0x28, 0x00, 0x00, 0x03, 0xF3, 0x00, 0x00, 0x01, 0x0E,
    0x00, 0x00, 0x00, 0x87, 0x00, 0x00, 0x00, 0x00
)
$jumpBefore = Get-Hex $jumpTemplate
$patchedJump = $patcher::PatchJump($jumpTemplate, $target)
Assert-Equal $jumpBefore (Get-Hex $jumpTemplate) "PatchJump must not mutate the input template"
Assert-Equal "4D 5A 00 00 00 00 00 00 00 12 58 28 00 00 03 F1 00 00 00 35 00 00 00 31 00 00 00 00" (Get-Hex $patchedJump) "PatchJump offsets"

$dynamicJumpTemplate = [byte[]]$jumpTemplate.Clone()
$dynamicJumpTemplate[4] = 0x00
$dynamicJumpTemplate[5] = 0x09
$dynamicJumpTemplate[6] = 0xF9
$dynamicJumpTemplate[7] = 0x31
$dynamicPatchedJump = $patcher::PatchJump($dynamicJumpTemplate, $target)
Assert-Equal "4D 5A 00 00 00 09 F9 31 00 12 58 28 00 00 03 F1 00 00 00 35 00 00 00 31 00 00 00 00" `
    (Get-Hex $dynamicPatchedJump) "PatchJump must preserve the captured session sequence"

$useTemplate = [byte[]](
    0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x10, 0x78, 0x3A,
    0x00, 0x00, 0x00, 0x10,
    0x00, 0x00, 0x00, 0x07,
    0x00, 0x00, 0x00, 0x01,
    0x01, 0x32
)
$useBefore = Get-Hex $useTemplate
$patchedUse = $patcher::PatchUse($useTemplate, $target)
Assert-Equal $useBefore (Get-Hex $useTemplate) "PatchUse must not mutate the input template"
Assert-Equal "4D 5A 00 00 00 00 00 00 00 10 78 3A 00 00 00 0D 00 00 00 07 00 00 00 01 01 32" (Get-Hex $patchedUse) "PatchUse only changes pos"

$dynamicUseTemplate = [byte[]]$useTemplate.Clone()
$dynamicUseTemplate[4] = 0x00
$dynamicUseTemplate[5] = 0x09
$dynamicUseTemplate[6] = 0xFB
$dynamicUseTemplate[7] = 0x18
$dynamicPatchedUse = $patcher::PatchUse($dynamicUseTemplate, $target)
Assert-Equal "4D 5A 00 00 00 09 FB 18 00 10 78 3A 00 00 00 0D 00 00 00 07 00 00 00 01 01 32" `
    (Get-Hex $dynamicPatchedUse) "PatchUse must preserve the captured session sequence"

$capturedClientUseTemplate = [byte[]](
    0x4D, 0x5A, 0x00, 0x00, 0x00, 0x09, 0xFB, 0x18,
    0x00, 0x34, 0x78, 0x3A,
    0x00, 0x00, 0x00, 0x0E,
    0x00, 0x00, 0x00, 0x07,
    0x00, 0x00, 0x00, 0x01,
    0x00, 0x24, 0x49, 0x74, 0x32, 0x56, 0x57, 0x38,
    0x44, 0x6C, 0x64, 0x59, 0x6E, 0x5A, 0x70, 0x65,
    0x75, 0x35, 0x59, 0x70, 0x49, 0x41, 0x77, 0x6B,
    0x55, 0x46, 0x69, 0x39, 0x6C, 0x53, 0x30, 0x6E,
    0x41, 0x58, 0x75, 0x76, 0x42, 0x32)
$capturedClientUsePatched = $patcher::PatchUse($capturedClientUseTemplate, $target)
Assert-Equal "4D 5A 00 00 00 09 FB 18 00 34 78 3A 00 00 00 0D 00 00 00 07 00 00 00 01 00 24 49 74 32 56 57 38 44 6C 64 59 6E 5A 70 65 75 35 59 70 49 41 77 6B 55 46 69 39 6C 53 30 6E 41 58 75 76 42 32" `
    (Get-Hex $capturedClientUsePatched) "PatchUse must accept the captured zero-marker parameter format"

$jumpTaskWalkInvalid = [byte[]]$jumpTemplate.Clone()
$jumpTaskWalkInvalid[27] = 1
$threw = $false
try { $patcher::PatchJump($jumpTaskWalkInvalid, $target) | Out-Null } catch { $threw = $true }
Assert-True $threw "non-zero isTaskWalk template must be rejected"

$useProtocolInvalid = [byte[]]$useTemplate.Clone()
$useProtocolInvalid[11] = 0x3B
$threw = $false
try { $patcher::PatchUse($useProtocolInvalid, $target) | Out-Null } catch { $threw = $true }
Assert-True $threw "wrong Use protocol template must be rejected"

$packetType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$jumpPacket = New-Object WPELibrary.Lib.Socket_PacketInfo
$jumpPacket.PacketType = $packetType
$jumpPacket.PacketFrom = "127.0.0.1:50000"
$jumpPacket.PacketTo = "127.0.0.1:12345"
$jumpPacket.PacketBuffer = $jumpTemplate
$jumpPacket.PacketLen = $jumpTemplate.Length

$usePacket = New-Object WPELibrary.Lib.Socket_PacketInfo
$usePacket.PacketType = $packetType
$usePacket.PacketFrom = "127.0.0.1:50000"
$usePacket.PacketTo = "127.0.0.1:12345"
$usePacket.PacketBuffer = $useTemplate
$usePacket.PacketLen = $useTemplate.Length

$captured = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_PacketInfo]'
$jumpPacket.PacketSocket = 6168
$usePacket.PacketSocket = 6168
$captured.Add($jumpPacket)
$captured.Add($usePacket)
$runtime = [WPELibrary.Lib.Vision.TreasurePacketRuntime]
$templates = $runtime::DiscoverTemplates($captured)
$runtimeRoute = New-Object WPELibrary.Lib.Vision.TreasurePacketRoute(
    $packetType,
    "127.0.0.1:50000",
    "127.0.0.1:12345")
$state = @"
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
      "x": 53,
      "y": 49
    }
  ]
}
"@
$prepared = $runtime::PrepareFromResidentState($state, $templates)
Assert-Equal 13 $prepared.Target.PackageNum "runtime target packageNum"
Assert-Equal 1009 $prepared.Target.MapId "runtime target mapId"
Assert-Equal "4D 5A 00 00 00 00 00 00 00 12 58 28 00 00 03 F1 00 00 00 35 00 00 00 31 00 00 00 00" (Get-Hex $prepared.JumpPacket.PacketBuffer) "runtime Jump replacement"
Assert-Equal "4D 5A 00 00 00 00 00 00 00 10 78 3A 00 00 00 0D 00 00 00 07 00 00 00 01 01 32" (Get-Hex $prepared.UsePacket.PacketBuffer) "runtime Use replacement"
Assert-Equal 0 $prepared.JumpPacket.PacketSocket "runtime must not reuse template socket"
Assert-Equal $false $prepared.PacketSend "runtime preparation must not send"
Assert-Equal $false $prepared.ActionAuthorized "resident read-only state must not authorize action"

$runtime::BeginSession()
$runtime::ObserveCapturedPackets($captured)
$currentJumpPacket = $runtime::GetCurrentJumpPacket($target, $runtimeRoute)
Assert-Equal "4D 5A 00 00 00 00 00 00 00 12 58 28 00 00 03 F1 00 00 00 35 00 00 00 31 00 00 00 00" (Get-Hex $currentJumpPacket.PacketBuffer) "current-session Jump template patch"
Assert-Equal 0 $currentJumpPacket.PacketSocket "current-session Jump template must not retain Socket"
$currentUsePacket = $runtime::GetCurrentUsePacket($target, $runtimeRoute)
Assert-Equal "4D 5A 00 00 00 00 00 00 00 10 78 3A 00 00 00 0D 00 00 00 07 00 00 00 01 01 32" (Get-Hex $currentUsePacket.PacketBuffer) "current-session Use template patch"
Assert-Equal 0 $currentUsePacket.PacketSocket "current-session Use template must not retain Socket"
$sessionSnapshot = $runtime::GetSessionSnapshot()
Assert-True $sessionSnapshot.Ready "session preset must become ready from validated captures"
[WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Clear()
$resolvedAfterUiClear = $runtime::ResolveCurrentSessionSocket($packetType, "127.0.0.1:12345")
Assert-Equal 6168 $resolvedAfterUiClear "session Socket must survive UI list auto-clear"

$runtime::BeginSession()
$missingJumpRejected = $false
try {
    $runtime::GetCurrentJumpPacket($target, $runtimeRoute) | Out-Null
} catch [System.Management.Automation.MethodInvocationException] {
    if ($_.Exception.InnerException -is [WPELibrary.Lib.Vision.TreasurePacketRuntimeException] -and
        $_.Exception.InnerException.Code -eq "jump_template_not_found") {
        $missingJumpRejected = $true
    } else {
        throw
    }
}
Assert-True $missingJumpRejected "missing current Jump template must fail closed"
$missingUseRejected = $false
try {
    $runtime::GetCurrentUsePacket($target, $runtimeRoute) | Out-Null
} catch [System.Management.Automation.MethodInvocationException] {
    if ($_.Exception.InnerException -is [WPELibrary.Lib.Vision.TreasurePacketRuntimeException] -and
        $_.Exception.InnerException.Code -eq "use_template_not_found") {
        $missingUseRejected = $true
    } else {
        throw
    }
}
Assert-True $missingUseRejected "missing current Use template must fail closed"
Assert-True (-not $runtime::HasCurrentUseTemplate()) "missing current Use template availability must fail closed"
$crossSessionRejected = $false
try { $runtime::GetSessionSnapshot() | Out-Null } catch { $crossSessionRejected = $true }
Assert-True $crossSessionRejected "session preset must not cross BeginSession"

$notAuthorized = $runtime::SendPreparedPacketOnce($prepared.JumpPacket, $null)
Assert-Equal $false $notAuthorized.Success "live send must be gated"
Assert-Equal "live_send_not_authorized" $notAuthorized.Code "live send gate code"

Write-Output "TreasurePacketTemplatePatcherRegression: PASS"
