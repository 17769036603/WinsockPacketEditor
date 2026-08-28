param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BuildDirectory = ''
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param($Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $RepositoryRoot 'WPELibrary\bin\PetSkillSnapshotValidation'
}

$newtonsoft = Join-Path $RepositoryRoot 'packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll'
$library = Join-Path $BuildDirectory 'WPELibrary.dll'
Assert-True (Test-Path -LiteralPath $newtonsoft) 'Newtonsoft.Json.dll is missing.'
Assert-True (Test-Path -LiteralPath $library) 'WPELibrary.dll is missing.'

[void][System.Reflection.Assembly]::LoadFrom($newtonsoft)
[void][System.Reflection.Assembly]::LoadFrom($library)

$json = @'
{"schema":"pet_skill_snapshot.v1","schemaVersion":1,"readOnly":true,"actionAuthorized":false,"process":{"pid":1886,"startTicks":7936,"exe":"/system/bin/app_process64"},"petId":600000591,"currentPetId":600000591,"skillSlots":[{"slotIndex":1,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":2,"value":{"kind":"i32","raw":"0xFFF900000001445C","integer":83036}},{"slotIndex":3,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":4,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":5,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":6,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":7,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":8,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":9,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":10,"value":{"kind":"i32","raw":"0xFFF90000FFFFFFFF","integer":-1}},{"slotIndex":11,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":12,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":13,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":14,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":15,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":16,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":17,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}}],"lockSlots":[{"slotIndex":1,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":2,"value":{"kind":"i32","raw":"0xFFF9000000000003","integer":3}},{"slotIndex":3,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":4,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":5,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":6,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":7,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":8,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":9,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}},{"slotIndex":10,"value":{"kind":"i32","raw":"0xFFF9000000000005","integer":5}},{"slotIndex":11,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":12,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":13,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":14,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":15,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":16,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":17,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}}]}
'@

$snapshot = $null
$parseError = ''
$ok = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlySnapshotProtocol]::TryParse(
    $json,
    [ref]$snapshot,
    [ref]$parseError)
Assert-True $ok "Valid snapshot rejected: $parseError"
Assert-True ($snapshot.ReadOnly -and -not $snapshot.ActionAuthorized) 'Read-only boundary was not preserved.'
Assert-True ($snapshot.PetId -eq 600000591 -and $snapshot.CurrentPetId -eq 600000591) 'Pet identity mismatch.'
Assert-True ($snapshot.Slots.Count -eq 17) 'Expected 17 slots.'
Assert-True ($snapshot.Slots[1].Skill.Integer -eq 83036) 'Skill slot 2 mismatch.'
Assert-True ($snapshot.Slots[1].Lock.Integer -eq 3) 'Lock slot 2 mismatch.'
Assert-True ($snapshot.Slots[9].Skill.Integer -eq -1) 'Skill slot 10 mismatch.'
Assert-True ($snapshot.Slots[9].Lock.Integer -eq 5) 'Lock slot 10 mismatch.'

$tampered = $json.Replace('"actionAuthorized":false', '"actionAuthorized":true')
$tamperedSnapshot = $null
$tamperedError = ''
$tamperedOk = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlySnapshotProtocol]::TryParse(
    $tampered,
    [ref]$tamperedSnapshot,
    [ref]$tamperedError)
Assert-True (-not $tamperedOk) 'Authorized snapshot was accepted.'

Write-Output 'PetSkillBookSnapshotProtocolRegression: PASS'
