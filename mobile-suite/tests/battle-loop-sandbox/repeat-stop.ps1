param([string]$Scenario = 'completed-run')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$module = (Resolve-Path (Join-Path $root '..\..')).Path
$lua = Join-Path $module 'app\src\main\assets\battle_loop.lua.in'
$runner = Join-Path $root 'run-lua.ps1'
$fixture = Join-Path $root 'fixture.lua'
$stopMode = $Scenario -ne 'completed-run'
$mode = if ($stopMode) { 'stop' } else { 'tick' }
$config = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='$mode'}"
$stop = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='stop'}"
$before = Join-Path $root ("repeat-stop-before-" + $Scenario + '.lua')
$tests = Join-Path $root 'repeat-stop.lua'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner -LuaFile $lua -SetupFile $fixture -TestsFile $tests -BeforeStopFile $before -ConfigLiteral $config -StopConfigLiteral $stop
if ($LASTEXITCODE -ne 0) { throw "Repeat-stop scenario failed: $Scenario" }
