$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\app\src\main\java\com\xnas\wpe')
$checks = @(
    @{ Path = 'anlei\AnleiService.java'; Stop = 'if(i!=null&&STOP.equals(i.getAction()))'; Gate = 'LicenseManager.isActive()' },
    @{ Path = 'mounthold\MountHoldService.java'; Stop = 'intent != null && ACTION_STOP.equals(intent.getAction())'; Gate = 'LicenseManager.isActive()' },
    @{ Path = 'battleloop\BattleLoopService.java'; Stop = 'STOP.equals(intent.getAction())'; Gate = 'LicenseManager.isActive()' },
    @{ Path = 'battlepanel\BattleOverlayService.java'; Stop = 'ACTION_STOP.equals(intent.getAction())'; Gate = 'LicenseManager.isActive()' },
    @{ Path = 'jinchan\ConnectionService.java'; Stop = 'ACTION_STOP.equals(i.getAction())'; Gate = 'LicenseManager.isActive()' }
)
foreach ($check in $checks) {
    $source = [IO.File]::ReadAllText((Join-Path $root $check.Path))
    $stopAt = $source.IndexOf($check.Stop, [StringComparison]::Ordinal)
    $gateAt = $source.IndexOf($check.Gate, [StringComparison]::Ordinal)
    if ($stopAt -lt 0 -or $gateAt -lt 0 -or $stopAt -gt $gateAt) {
        throw "Authorization gate/stop ordering failed: $($check.Path)"
    }
}
$restartChecks = @(
    @{ Path = 'anlei\AnleiService.java'; Monitor = 'private synchronized void monitorTick()'; Stop = 'stopWorker(latestStartId);return;'; Restart = 'startWorker();' },
    @{ Path = 'battleloop\BattleLoopService.java'; Monitor = 'private synchronized void monitorTick()'; Stop = 'stopWorker(latestStartId);'; Restart = 'startWorker();' }
)
foreach ($check in $restartChecks) {
    $source = [IO.File]::ReadAllText((Join-Path $root $check.Path))
    $monitorAt = $source.IndexOf($check.Monitor, [StringComparison]::Ordinal)
    $stopAt = $source.IndexOf($check.Stop, $monitorAt, [StringComparison]::Ordinal)
    $restartAt = $source.IndexOf($check.Restart, $stopAt, [StringComparison]::Ordinal)
    if ($monitorAt -lt 0 -or $stopAt -lt 0 -or $restartAt -lt 0) {
        throw "Expired-lease worker restart is not routed through STOP: $($check.Path)"
    }
}
$manager = [IO.File]::ReadAllText((Join-Path $root 'suite\LicenseManager.java'))
$window = [IO.File]::ReadAllText((Join-Path $root 'suite\LicenseLeaseWindow.java'))
foreach ($required in @('setInstanceFollowRedirects(false)', 'setConnectTimeout(5000)', 'setReadTimeout(5000)', 'MAX_RESPONSE = 8192', 'license-lease.json', 'deadlineElapsedMs')) {
    if (-not $manager.Contains($required)) { throw "Missing license request/lease contract: $required" }
}
if (-not $window.Contains('leaseSeconds > 120')) { throw 'Missing 120 second lease cap' }
$mount = [IO.File]::ReadAllText((Join-Path $root 'mounthold\MountHoldService.java'))
if (-not $mount.Contains('(sendStop ? " --recover-only true" : "")')) {
    throw 'Mount hold STOP recovery launch must pass --recover-only true while regular START omits it'
}
$anlei = [IO.File]::ReadAllText((Join-Path $root 'anlei\AnleiService.java'))
$inactiveStart = $anlei.IndexOf('if(!com.xnas.wpe.suite.LicenseManager.isActive())', [StringComparison]::Ordinal)
if ($inactiveStart -lt 0) { throw 'Missing Anlei authorization gate' }
$normalStop = $anlei.IndexOf('if(desiredRunning||stopPending||running(worker)){stopWorker(id);return START_NOT_STICKY;}', $inactiveStart, [StringComparison]::Ordinal)
if ($normalStop -lt 0) { throw 'Anlei expired-lease START with a live worker must dispatch the safe STOP path' }
$manager = [IO.File]::ReadAllText((Join-Path $root 'suite\LicenseManager.java'))
foreach ($required in @('LICENSE_EXPIRED', 'conn.disconnect()', 'MAX_RESPONSE = 8192')) {
    if (-not $manager.Contains($required)) { throw "Missing activation error/transport contract: $required" }
}
Write-Output 'ActivationServiceGateContract PASS (5 service gates, STOP bypass, worker restart/recovery gates, activation expiry and transport bounds)'
