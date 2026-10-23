param()
$ErrorActionPreference = 'Stop'
$moduleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$lua = Get-Content -Raw (Join-Path $moduleRoot 'app\src\main\assets\battle_loop.lua.in')
$worker = Get-Content -Raw (Join-Path $moduleRoot 'native\battleloop\worker-rust\src\main.rs')
$activity = Get-Content -Raw (Join-Path $moduleRoot 'app\src\main\java\com\xnas\wpe\battleloop\MainActivity.java')
foreach ($field in @('calls=s.actions', 'characterSkillId=s.characterSkillId', 'petId=s.petId', 'petSkillId=s.petSkillId', 'round=s.target')) {
    if (-not $lua.Contains($field)) { throw "Lua status does not publish $field" }
}
foreach ($field in @('calls={calls}', 'characterSkillId=', 'petId=', 'petSkillId=', 'round=')) {
    if (-not $worker.Contains($field)) { throw "Worker status does not forward $field" }
}
foreach ($field in @('characterSkillId', 'petSkillId', 'petId')) {
    if (-not $activity.Contains('value(s,"' + $field + '")')) { throw "MainActivity does not read $field" }
}
Write-Output 'PASS status contract: Lua -> Rust worker -> Android activity'

