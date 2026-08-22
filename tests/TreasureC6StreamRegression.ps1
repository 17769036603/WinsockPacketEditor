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

function Get-ExceptionCode($errorRecord) {
    $current = $errorRecord.Exception
    while ($null -ne $current) {
        if ($current.PSObject.Properties.Name -contains "Code") {
            return $current.Code
        }
        $current = $current.InnerException
    }
    return ""
}

function New-CommonMessage(
    [string]$messageType,
    [int]$sequence,
    [string]$session,
    [string]$state,
    [hashtable]$extra) {
    $message = [ordered]@{
        protocolName = "piaomiao.treasure.stream"
        protocolVersion = 1
        messageType = $messageType
        schemaVersion = 2
        cacheKind = "treasure_inventory"
        streamSessionId = $session
        sequence = $sequence
        state = $state
        validationScope = "current_inventory_membership"
        actionAuthorized = $false
        processIdentity = [ordered]@{ pid = 0; startTicks = 0; exe = "fixture" }
        containerIdentity = $null
        monotonicNs = [long]$sequence
    }
    if ($null -ne $extra) {
        foreach ($key in $extra.Keys) {
            $message[$key] = $extra[$key]
        }
    }
    return ($message | ConvertTo-Json -Compress -Depth 12)
}

function New-ReadyIdentity() {
    return [ordered]@{ pid = 31531; startTicks = 7373672; exe = "/system/bin/app_process64" }
}

function New-Item([string]$member, [int]$package, [int]$scene, [int]$x, [int]$y) {
    return [ordered]@{
        memberIdentity = $member
        packageNum = $package
        scene = $scene
        x = $x
        y = $y
    }
}

$session = "c6-fixture-session"
$protocol = New-Object WPELibrary.Lib.Vision.TreasureC6StreamProtocol

$ack = New-CommonMessage "hello_ack" 0 $session "starting" @{
    selectedProtocolVersion = 1
    selectedSchemaVersion = 2
    reasonCode = "ready"
}
$protocol.ProcessLine($ack) | Out-Null
Assert-True $protocol.HandshakeComplete "hello_ack completes handshake"

$items = @(
    (New-Item "member-a" 13 1009 19 65),
    (New-Item "member-b" 14 1018 81 48)
)
$snapshot = New-CommonMessage "snapshot" 1 $session "ready" @{
    processIdentity = New-ReadyIdentity
    containerIdentity = "bagmgr:fixture/m_ItemDict:fixture"
    snapshotId = "snapshot-1"
    items = $items
    formalCount = 2
    rejectedMemberCount = 0
    events = @()
}
$protocol.ProcessLine($snapshot) | Out-Null
Assert-Equal 2 $protocol.CurrentSnapshot.Items.Count "snapshot materializes all C6 items"
Assert-Equal 13 $protocol.CurrentSnapshot.Items[0].PackageNum "packageNum is the consumable slot"

$added = New-CommonMessage "event" 2 $session "ready" @{
    processIdentity = New-ReadyIdentity
    containerIdentity = "bagmgr:fixture/m_ItemDict:fixture"
    snapshotId = "snapshot-1"
    formalCount = 3
    eventType = "added"
    item = New-Item "member-c" 15 1020 22 33
}
$protocol.ProcessLine($added) | Out-Null
Assert-Equal 3 $protocol.CurrentSnapshot.Items.Count "added event updates current membership"
Assert-Equal 15 $protocol.CurrentSnapshot.Items[2].PackageNum "added event packageNum"

$removedForSlotReuse = New-CommonMessage "event" 3 $session "ready" @{
    processIdentity = New-ReadyIdentity
    containerIdentity = "bagmgr:fixture/m_ItemDict:fixture"
    snapshotId = "snapshot-1"
    formalCount = 2
    eventType = "removed"
    item = New-Item "member-a" 13 1009 19 65
}
$protocol.ProcessLine($removedForSlotReuse) | Out-Null
$addedForSlotReuse = New-CommonMessage "event" 4 $session "ready" @{
    processIdentity = New-ReadyIdentity
    containerIdentity = "bagmgr:fixture/m_ItemDict:fixture"
    snapshotId = "snapshot-1"
    formalCount = 3
    eventType = "added"
    item = New-Item "member-replacement" 13 1018 88 34
}
$protocol.ProcessLine($addedForSlotReuse) | Out-Null
$replacement = @($protocol.CurrentSnapshot.Items | Where-Object { $_.PackageNum -eq 13 })
Assert-Equal 1 $replacement.Count "slot reuse keeps one current package member"
Assert-Equal "member-replacement" $replacement[0].MemberIdentity "slot reuse replaces the member after removal"
Assert-Equal 88 $replacement[0].X "slot reuse applies the replacement coordinates"

$invalidProtocol = New-Object WPELibrary.Lib.Vision.TreasureC6StreamProtocol
$invalidAuth = New-CommonMessage "hello_ack" 0 "invalid-auth" "starting" @{
    selectedProtocolVersion = 1
    selectedSchemaVersion = 2
    reasonCode = "ready"
    actionAuthorized = $true
}
$invalidCode = ""
try {
    $invalidProtocol.ProcessLine($invalidAuth) | Out-Null
} catch {
    $invalidCode = Get-ExceptionCode $_
}
Assert-Equal "action_authorized" $invalidCode "actionAuthorized=true is rejected"
Assert-True ($null -eq $invalidProtocol.CurrentSnapshot) "invalid auth clears consumable state"

$gapProtocol = New-Object WPELibrary.Lib.Vision.TreasureC6StreamProtocol
$gapProtocol.ProcessLine($ack) | Out-Null
$gap = New-CommonMessage "snapshot" 2 $session "ready" @{
    processIdentity = New-ReadyIdentity
    containerIdentity = "bagmgr:fixture/m_ItemDict:fixture"
    snapshotId = "snapshot-gap"
    items = @()
    formalCount = 0
    rejectedMemberCount = 0
    events = @()
}
$gapCode = ""
try {
    $gapProtocol.ProcessLine($gap) | Out-Null
} catch {
    $gapCode = Get-ExceptionCode $_
}
Assert-Equal "sequence_gap" $gapCode "sequence gaps are rejected"

$boundaryProtocol = New-Object WPELibrary.Lib.Vision.TreasureC6StreamProtocol
$boundaryProtocol.ProcessLine($ack) | Out-Null
$crossSessionEvent = New-CommonMessage "event" 0 "new-session" "ready" @{
    processIdentity = New-ReadyIdentity
    containerIdentity = "bagmgr:fixture/m_ItemDict:fixture"
    snapshotId = "snapshot-1"
    formalCount = 3
    eventType = "added"
    item = New-Item "member-d" 16 1020 22 34
}
$boundaryCode = ""
try {
    $boundaryProtocol.ProcessLine($crossSessionEvent) | Out-Null
} catch {
    $boundaryCode = Get-ExceptionCode $_
}
Assert-Equal "session_boundary_invalid" $boundaryCode "a new session cannot start with an event"

$target = New-Object WPELibrary.Lib.Vision.TreasureInventoryTarget(13, 1009, 19, 65)
$packetType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$route = New-Object WPELibrary.Lib.Vision.TreasurePacketRoute(
    $packetType,
    "127.0.0.1:50000",
    "127.0.0.1:12345")
$prepared = [WPELibrary.Lib.Vision.TreasurePacketRuntime]::PrepareEncodedFromTarget(
    $target,
    $route,
    13,
    1,
    "2")
Assert-Equal 13 $prepared.Target.PackageNum "direct C6 target binds Use pos"
Assert-Equal 13 ([WPELibrary.Lib.Vision.TreasurePacketEncoder]::DecodeUse($prepared.UsePacket.PacketBuffer)).PackageNum "direct target Use packet"
Assert-Equal 19 ([WPELibrary.Lib.Vision.TreasurePacketEncoder]::DecodeJump($prepared.JumpPacket.PacketBuffer)).X "direct target Jump x"

Write-Output "TreasureC6StreamRegression: PASS"
