$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

function Assert-Contains([string]$text, [string]$expected, [string]$message) {
    if (-not $text.Contains($expected)) {
        throw $message
    }
}

$cacheSource = Get-Content (
    Join-Path $repo "WPELibrary\Lib\Socket_Cache.cs"
) -Raw -Encoding UTF8
$workerSource = Get-Content (
    Join-Path $repo "WPELibrary\Lib\Socket_Send.cs"
) -Raw -Encoding UTF8
$runtimeSource = Get-Content (
    Join-Path $repo "WPELibrary\Lib\Vision\TreasurePacketRuntime.cs"
) -Raw -Encoding UTF8
$diagnosticSource = Get-Content (
    Join-Path $repo "WPELibrary\Lib\Socket_SendDiagnosticLogStore.cs"
) -Raw -Encoding UTF8

Assert-Contains $cacheSource `
    'private const string PanguIronSalePresetName = "出售盘古精铁";' `
    "The protected preset policy must retain the sale preset anchor name."
Assert-Contains $cacheSource `
    'private static readonly HashSet<string> PanguIronSaleProtectedPresetNames' `
    "The protected preset policy must use one explicit preset-name set."
foreach ($name in @(
        "积分一", "积分二", "积分三", "积分四", "积分五", "积分六", "积分七",
        "百亿玉", "百亿银子", "百亿师贡献", "百亿帮贡", "百亿成就", "百亿积分",
        "炼星石", "积分", "嘉嘉的嫁妆", "扭转乾坤", "子虚乌有", "化无", "成仁取义",
        "抗性", "超级宝图")) {
    Assert-Contains $cacheSource `
        ('"' + $name + '"') `
        "The protected preset policy must include $name."
}
Assert-Contains $cacheSource `
    'Math.Max(loopInterval, 1800)' `
    "The sale preset must use the conservative runtime interval without changing the saved preset."
Assert-Contains $cacheSource `
    'usePanguIronSaleTransportProtection,' `
    "The sale preset must opt into the fail-closed send worker policy."
Assert-Contains $workerSource `
    'Socket_Cache.SocketList.ResolveCurrentRoute(packet)' `
    "The protected send path must refresh the current route before each send."
Assert-Contains $workerSource `
    'TreasurePacketRuntime.ResolveCurrentSessionSocket(' `
    "The protected sale preset must borrow the current-session socket fallback used by treasure sending."
Assert-Contains $workerSource `
    'Socket_Cache.Send.IsPanguIronSalePreset(sendName)' `
    "Every low-level send entry point must recognize the protected sale preset."
Assert-Contains $workerSource `
    'refreshCurrentRouteBeforeEachSend || protectPanguIronSale' `
    "The protected sale preset must force route refresh even through the legacy detail window."
Assert-Contains $workerSource `
    'stopOnRouteOrSendFailure || protectPanguIronSale' `
    "The protected sale preset must force fail-closed behavior through the legacy detail window."
Assert-Contains $workerSource `
    'GetEffectiveLoopInterval(SendName, LoopINT)' `
    "Every low-level send entry point must enforce the protected runtime interval."
Assert-Contains $workerSource `
    'out bytesSent,' `
    "The protected send path must retain the native write result details."
Assert-Contains $workerSource `
    'this.LogFailClosedStop(' `
    "The protected send path must stop instead of continuing after an unsafe result."
Assert-Contains $workerSource `
    'public bool StartSendWithPacketSockets(' `
    "The legacy packet-socket entry point must remain available."
Assert-Contains $workerSource `
    'SocketSendDiagnosticLogStore PanguIronSaleDiagnosticLog' `
    "The protected sale preset must have a persistent diagnostic sink."
Assert-Contains $workerSource `
    'private static bool IsProtectedPacket(byte[] buffer)' `
    "The protected presets must validate the supported packet family shapes."
Assert-Contains $workerSource `
    '(((buffer[8] << 8) | buffer[9]) == buffer.Length - 10)' `
    "The protected presets must preserve each saved packet length."
Assert-Contains $workerSource `
    '(buffer[10] == 0x40 && buffer[11] == 0x62)' `
    "The protected presets must support the test-group 0x4062 packet family."
Assert-Contains $workerSource `
    '(buffer[10] == 0x70 && buffer[11] == 0xAB)' `
    "The protected presets must support the resistance 0x70AB packet family."
Assert-Contains $workerSource `
    '(buffer[10] == 0xF9 && buffer[11] == 0x08)' `
    "The protected presets must support the super treasure map 0xF908 packet family."
Assert-Contains $workerSource `
    'TreasurePacketRuntime.TryPrepareCurrentSessionSequence(' `
    "The protected presets must bind the current session sequence before sending."
Assert-Contains $runtimeSource `
    'public static bool TryPrepareCurrentSessionSequence(' `
    "The treasure runtime must expose the narrow current-session sequence bridge."
Assert-Contains $runtimeSource `
    'if (!sessionSequenceAvailable)' `
    "The current-session bridge must reject stale non-zero sequences from saved preset bytes."
Assert-Contains $workerSource `
    'wsaError' `
    "The protected sale preset must retain the native WSA error in diagnostics."
Assert-Contains $diagnosticSource `
    'socket-send.jsonl' `
    "The persistent diagnostic file name must remain CLI-readable."
Assert-Contains $diagnosticSource `
    '["packetLength"]' `
    "Diagnostics must record packet length without recording packet bytes."

Write-Output "Send preset transport protection regression checks passed."
