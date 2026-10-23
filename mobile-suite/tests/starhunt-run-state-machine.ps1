$ErrorActionPreference = 'Stop'
$moduleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lua = Join-Path $moduleRoot 'app\src\main\assets\starhunt.lua.in'
$runner = Join-Path $moduleRoot 'tests\battle-loop-sandbox\run-lua.ps1'
$fixture = Join-Path $moduleRoot 'tests\starhunt-fixture.lua'
$cleanup = Join-Path $moduleRoot 'tests\starhunt-cleanup-check.lua'
$config = "{resultPath='result',bindingPath='binding',commandPath='command',stopPath='stop',leasePath='lease',workerPath='worker',baselineSequence=10,stateKey='__codex_starhunt_test_v1',runId='starhunt-test',mode='tick'}"
$stopConfig = $config.Replace("mode='tick'", "mode='stop'")

foreach ($case in @('pipeline', 'timeout-and-guards', 'anlei-conflict', 'lease-expiry', 'scene-userdata', 'challenge-rebind', 'round-robin', 'wait-refresh', 'scan-cadence', 'scan-filter', 'multilevel')) {
    Write-Output "=== starhunt Lua state machine: $case ==="
    $flow = Join-Path $moduleRoot "tests\starhunt-$case.lua"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner `
        -LuaFile $lua -SetupFile $fixture -BeforeStopFile $flow `
        -StopConfigLiteral $stopConfig -AfterLoadFile $cleanup -ConfigLiteral $config
    if ($LASTEXITCODE -ne 0) { throw "Starhunt Lua scenario failed: $case" }
}

# Teleport baseline: the service stops the scan first, then starts a new worker
# whose initial command is TELEPORT.
Write-Output '=== starhunt Lua state machine: teleport-baseline ==='
$scratch = Join-Path $moduleRoot '.scratch'
$null = New-Item -ItemType Directory -Force -Path $scratch
$teleportSetup = Join-Path $scratch 'starhunt-teleport-baseline-setup.lua'
$fixtureSource = [System.IO.File]::ReadAllText($fixture, [System.Text.Encoding]::UTF8)
$prelude = "initialCommand='sequence=10\nmode=teleport\nlevels=\nmap=1007\n'`n"
$postlude = "data_MapInfo={[1007]={name='TestMap',startPos={x=50,y=60}}}`nmapManager.canNotOperationTeam=false`n"
[System.IO.File]::WriteAllText($teleportSetup, $prelude + $fixtureSource + "`n" + $postlude,
    [System.Text.UTF8Encoding]::new($false))
$teleportFlow = Join-Path $moduleRoot 'tests\starhunt-teleport-baseline.lua'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner `
    -LuaFile $lua -SetupFile $teleportSetup -BeforeStopFile $teleportFlow `
    -StopConfigLiteral $stopConfig -AfterLoadFile $cleanup -ConfigLiteral $config
if ($LASTEXITCODE -ne 0) { throw 'Starhunt Lua scenario failed: teleport-baseline' }

# Center landing: 4154 must use the arena center, not the map startPos at the edge.
Write-Output '=== starhunt Lua state machine: teleport-center ==='
$centerSetup = Join-Path $scratch 'starhunt-teleport-center-setup.lua'
$centerPrelude = "initialCommand='sequence=10\nmode=teleport\nlevels=\nmap=4154\n'`n"
$centerPostlude = "data_MapInfo={[4154]={name='TestArena',startPos={20,15},gridSize={20,20},gridNum={121,90},width=2412,height=1791}}`nmapManager.canNotOperationTeam=false`nmapView.m_ColCount=121`nmapView.m_RowCount=90`nmapView.getPosCanGo=function(self,x,y) return x==60 and y==45 end`n"
[System.IO.File]::WriteAllText($centerSetup, $centerPrelude + $fixtureSource + "`n" + $centerPostlude,
    [System.Text.UTF8Encoding]::new($false))
$centerFlow = Join-Path $moduleRoot 'tests\starhunt-teleport-center.lua'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner `
    -LuaFile $lua -SetupFile $centerSetup -BeforeStopFile $centerFlow `
    -StopConfigLiteral $stopConfig -AfterLoadFile $cleanup -ConfigLiteral $config
if ($LASTEXITCODE -ne 0) { throw 'Starhunt Lua scenario failed: teleport-center' }

# Same-map center move: already in 4154 but at the edge must send a center jump.
Write-Output '=== starhunt Lua state machine: teleport-center-same ==='
$sameSetup = Join-Path $scratch 'starhunt-teleport-center-same-setup.lua'
[System.IO.File]::WriteAllText($sameSetup, $centerPrelude + $fixtureSource + "`n" + $centerPostlude,
    [System.Text.UTF8Encoding]::new($false))
$sameFlow = Join-Path $moduleRoot 'tests\starhunt-teleport-center-same.lua'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner `
    -LuaFile $lua -SetupFile $sameSetup -BeforeStopFile $sameFlow `
    -StopConfigLiteral $stopConfig -AfterLoadFile $cleanup -ConfigLiteral $config
if ($LASTEXITCODE -ne 0) { throw 'Starhunt Lua scenario failed: teleport-center-same' }
Write-Output 'All starhunt Lua state machine scenarios passed.'
