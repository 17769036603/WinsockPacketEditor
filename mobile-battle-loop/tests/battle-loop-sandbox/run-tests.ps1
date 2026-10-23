param()
$ErrorActionPreference = 'Stop'
$moduleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$lua = Join-Path $moduleRoot 'app\src\main\assets\battle_loop.lua.in'
$fixture = Join-Path $PSScriptRoot 'fixture.lua'
$luaRunner = Join-Path $PSScriptRoot 'run-lua.ps1'
$config = "{resultPath='result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='tick'}"
$stopConfig = "{resultPath='result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='stop'}"
$cases = @('behavior','commands-without-ack','rejection','unknown-target','no-pet','identity-change','lease-expiry','missing-round-ack')
foreach ($case in $cases) {
    Write-Output "=== $case ==="
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $luaRunner -LuaFile $lua -SetupFile $fixture -TestsFile (Join-Path $PSScriptRoot ($case + '.lua')) -ConfigLiteral $config
    if ($LASTEXITCODE -ne 0) { throw "Lua scenario failed: $case" }
}
Write-Output '=== stop ==='
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $luaRunner -LuaFile $lua -SetupFile $fixture -BeforeStopFile (Join-Path $PSScriptRoot 'stop-before.lua') -TestsFile (Join-Path $PSScriptRoot 'stop.lua') -ConfigLiteral $config -StopConfigLiteral $stopConfig
if ($LASTEXITCODE -ne 0) { throw 'Lua scenario failed: stop' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify-status-contract.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Static status contract failed.' }
Write-Output 'All battle-loop sandbox scenarios passed.'
