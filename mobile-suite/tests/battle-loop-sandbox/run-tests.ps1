$ErrorActionPreference = 'Stop'
$config = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='tick'}"
$stopConfig = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='stop'}"
$moduleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$lua = Join-Path $moduleRoot 'app\src\main\assets\battle_loop.lua.in'
$runner = Join-Path $PSScriptRoot 'run-lua.ps1'
$fixture = Join-Path $PSScriptRoot 'fixture.lua'
$helpers = Join-Path $PSScriptRoot 'animation-helpers.lua'
$cases = @('behavior','commands-without-ack','rejection','unknown-target','no-pet','player-pos-fallback','already-committed-playover','stale-no-playover','identity-change','lease-expiry','missing-round-ack','low-hp-target-priority','low-hp-target-fallback','low-hp-target-invalidated','low-hp-target-reverse-side','low-hp-target-no-replacement','low-hp-target-retry-fixed')
foreach ($case in $cases) {
    Write-Output "=== legacy-compatible: $case ==="
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner -LuaFile $lua -SetupFile $fixture -TestsFile (Join-Path $PSScriptRoot ($case + '.lua')) -AfterLoadFile $helpers -ConfigLiteral $config
    if ($LASTEXITCODE -ne 0) { throw "Legacy-compatible Lua scenario failed: $case" }
}
Write-Output '=== legacy-compatible: stop ==='
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner -LuaFile $lua -SetupFile $fixture -BeforeStopFile (Join-Path $PSScriptRoot 'stop-before.lua') -TestsFile (Join-Path $PSScriptRoot 'stop.lua') -AfterLoadFile $helpers -ConfigLiteral $config -StopConfigLiteral $stopConfig
if ($LASTEXITCODE -ne 0) { throw 'Legacy-compatible Lua scenario failed: stop' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify-status-contract.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Static status contract failed.' }
foreach ($case in @('rounds-1-5','animation-waits','animation-finished','finished-index-exhausted','no-pet-animation','natural-playover-phases','phase-change','phase-change-role','phase-change-after-verify','exception-cleanup')) {
    Write-Output "=== animation: $case ==="
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner -LuaFile $lua -SetupFile $fixture -TestsFile (Join-Path $PSScriptRoot ($case + '.lua')) -AfterLoadFile $helpers -ConfigLiteral $config
    if ($LASTEXITCODE -ne 0) { throw "Animation scenario failed: $case" }
}
Write-Output '=== animation: stop while pending ==='
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner -LuaFile $lua -SetupFile $fixture -BeforeStopFile (Join-Path $PSScriptRoot 'stop-pending-before.lua') -TestsFile (Join-Path $PSScriptRoot 'stop-pending.lua') -AfterLoadFile $helpers -ConfigLiteral $config -StopConfigLiteral $stopConfig
if ($LASTEXITCODE -ne 0) { throw 'Animation scenario failed: stop-pending' }
Write-Output 'All compatible and animation scenarios passed.'
