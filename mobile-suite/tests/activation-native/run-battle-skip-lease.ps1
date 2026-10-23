$ErrorActionPreference = 'Stop'
$suite = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$lua = Join-Path $suite 'app\src\main\assets\battle_skip.lua.in'
$runner = Join-Path $suite 'tests\battle-loop-sandbox\run-lua.ps1'
$fixture = Join-Path $PSScriptRoot 'battle-skip-lease-fixture.lua'
$config = "{resultPath='result',bindingPath='binding',leasePath='lease',stateKey='__codex_apk_battle_skip_v1',runId='lease-test',expectedPlayer='',expectedManagerTostring='',mode='tick'}"
foreach ($case in @('missing','corrupt','expired','cross-boot','send','too-far')) {
    $test = Join-Path $PSScriptRoot ("battle-skip-lease-$case.lua")
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner -LuaFile $lua -SetupFile $fixture -TestsFile $test -ConfigLiteral $config
    if ($LASTEXITCODE -ne 0) { throw "Battle skip lease scenario failed: $case" }
}
Write-Output 'All battle skip activation lease scenarios passed.'
