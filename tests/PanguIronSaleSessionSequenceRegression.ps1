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

try {
    $runtime::BeginSession()

    $observed = New-Object WPELibrary.Lib.Socket_PacketInfo
    $observed.PacketType = $packetType
    $observed.PacketFrom = "198.18.0.1:24001"
    $observed.PacketTo = "198.18.0.2:24002"
    $observed.PacketBuffer = [byte[]](
        0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x00, 0x12, 0x58, 0x28,
        0x00, 0x00, 0x03, 0xF1,
        0x00, 0x00, 0x00, 0x35,
        0x00, 0x00, 0x00, 0x31,
        0x00, 0x00, 0x00, 0x00)
    $runtime::ObserveCapturedPacket($observed)

    $saleBuffer = [byte[]](
        0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x10, 0x30, 0x44, 0x00, 0x01, 0x5F, 0xAB,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x25, 0x16,
        0x01, 0x00)
    $saleError = ""
    $salePrepared = $runtime::TryPrepareCurrentSessionSequence(
        $saleBuffer,
        [ref]$saleError)
    Assert-True $salePrepared "The sale packet must bind the current session sequence"
    Assert-Equal "00 00 01 01" (Get-Hex $saleBuffer[4..7]) `
        "The sale packet must receive the next current-session sequence"

    $runtime::EndSession()
    $zeroSaleBuffer = [byte[]](
        0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x10, 0x30, 0x44, 0x00, 0x01, 0x5F, 0xAB,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x25, 0x16,
        0x01, 0x00)
    $zeroSaleError = ""
    $zeroSalePrepared = $runtime::TryPrepareCurrentSessionSequence(
        $zeroSaleBuffer,
        [ref]$zeroSaleError)
    Assert-True (-not $zeroSalePrepared) `
        "The sale packet must fail closed when no current session sequence exists"
    Assert-Equal "session_sequence_unavailable" $zeroSaleError `
        "The sale packet must expose the missing current session sequence reason"

    Write-Output "PanguIronSaleSessionSequenceRegression: PASS"
}
finally {
    $runtime::EndSession()
}
