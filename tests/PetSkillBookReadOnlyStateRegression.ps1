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
{"schema":"pet_skill_snapshot.v1","schemaVersion":1,"readOnly":true,"actionAuthorized":false,"process":{"pid":1853,"startTicks":1466,"exe":"/system/bin/app_process64"},"petId":600000715,"currentPetId":600000715,"skillSlots":[{"slotIndex":1,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":2,"value":{"kind":"i32","raw":"0xFFF900000001445F","integer":83039}},{"slotIndex":3,"value":{"kind":"i32","raw":"0xFFF90000FFFFFFFF","integer":-1}},{"slotIndex":4,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}}],"lockSlots":[{"slotIndex":1,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}},{"slotIndex":2,"value":{"kind":"i32","raw":"0xFFF9000000000003","integer":3}},{"slotIndex":3,"value":{"kind":"i32","raw":"0xFFF9000000000004","integer":4}},{"slotIndex":4,"value":{"kind":"i32","raw":"0xFFF9000000000000","integer":0}}]}
'@

$snapshot = $null
$parseError = ''
$parsed = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlySnapshotProtocol]::TryParse(
    $json,
    [ref]$snapshot,
    [ref]$parseError)
Assert-True $parsed "Valid snapshot rejected: $parseError"

$state = $null
$stateError = ''
$created = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlyStateAdapter]::TryCreate(
    $snapshot,
    [ref]$state,
    [ref]$stateError)
Assert-True $created "Read-only state rejected: $stateError"
Assert-True ($state.IsCurrentPetMatch) 'Current pet match was not preserved.'
Assert-True ($state.Slots.Count -eq 4) 'Expected four normalized slots.'
Assert-True ($state.PopulatedSkillCount -eq 1) 'Expected one positive skill.'
Assert-True ($state.Slots[0].SkillClass.ToString() -eq 'Nil') 'nil classification mismatch.'
Assert-True ($state.Slots[1].SkillClass.ToString() -eq 'PositiveSkill') 'positive skill classification mismatch.'
Assert-True ($state.Slots[2].SkillClass.ToString() -eq 'NegativeSentinel') 'negative sentinel classification mismatch.'
Assert-True ($state.Slots[3].SkillClass.ToString() -eq 'Zero') 'zero classification mismatch.'
Assert-True ($state.Slots[1].SkillId -eq 83039) 'Skill ID was not normalized.'
Assert-True ($state.Slots[1].DecodedSkillId -eq 83039) 'Decoded runtime skill ID was not preserved.'
Assert-True (-not $state.Slots[1].SkillIdIsCatalogKnown) 'Unlisted runtime skill ID was incorrectly catalog-confirmed.'
Assert-True ($state.Slots[1].LockInteger -eq 3) 'Raw lock value was not preserved.'
Assert-True ($state.Slots[1].LockClass.ToString() -eq 'PositiveSkill') 'Raw lock-value class mismatch.'
Assert-True (-not [string]::IsNullOrWhiteSpace($state.StateFingerprint)) 'State fingerprint is empty.'
Assert-True ($state.SlotCount -eq 4) 'Slot count summary mismatch.'
Assert-True ($state.NilSkillCount -eq 1 -and $state.ZeroSkillCount -eq 1 -and
    $state.NegativeSkillCount -eq 1 -and $state.PopulatedSkillCount -eq 1) `
    'Skill raw-value evidence counts mismatch.'
$evidence = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlyStateAdapter]::DescribeSlotEvidence($state)
Assert-True ($evidence.Contains('开槽/锁槽业务语义=未确认')) `
    'Slot evidence must keep open/locked semantics unconfirmed.'
Assert-True ($evidence.Contains('目录已确认=0')) `
    'Slot evidence must distinguish runtime candidates from catalog-confirmed IDs.'

$preset = New-Object WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPreset
$book = New-Object WPELibrary.Lib.PetSkillBook.SkillBookEntry -ArgumentList @(83039, 70001)
[void]$preset.Books.Add($book)
$report = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlyStateAdapter]::DescribePresetTargets(
    $preset,
    $state)
Assert-True ($report.Contains('已存在于槽2')) 'Existing target skill was not detected.'
Assert-True ($report.Contains('目录未收录')) 'Unknown runtime skill IDs must not be presented as known catalog skills.'
Assert-True ($report.Contains('未执行任何游戏操作')) 'Read-only operation boundary was not reported.'

$changedJson = $json.Replace('"integer":83039', '"integer":84002')
$changedSnapshot = $null
$changedError = ''
$changedParsed = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlySnapshotProtocol]::TryParse(
    $changedJson,
    [ref]$changedSnapshot,
    [ref]$changedError)
Assert-True $changedParsed "Changed snapshot rejected: $changedError"
$changedState = $null
$changedStateError = ''
$changedCreated = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlyStateAdapter]::TryCreate(
    $changedSnapshot,
    [ref]$changedState,
    [ref]$changedStateError)
Assert-True $changedCreated "Changed read-only state rejected: $changedStateError"
Assert-True ($state.StateFingerprint -ne $changedState.StateFingerprint) 'State fingerprint did not change.'

Write-Output 'PetSkillBookReadOnlyStateRegression: PASS'
