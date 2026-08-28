param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BuildDirectory = ''
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param($Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-False {
    param($Condition, [string]$Message)
    if ($Condition) { throw $Message }
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
{"schema":"pet_skill_snapshot.v1","schemaVersion":1,"readOnly":true,"actionAuthorized":false,"process":{"pid":1886,"startTicks":7936,"exe":"/system/bin/app_process64"},"petId":600000591,"currentPetId":600000591,"skillSlots":[{"slotIndex":1,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}}],"lockSlots":[{"slotIndex":1,"value":{"kind":"other","raw":"0xFFFFFFFFFFFFFFFF"}}],"inventoryItems":[{"itemId":90027,"count":2},{"itemId":99003,"count":0}],"skillBookInventory":[{"skillId":92133,"count":3},{"skillId":92211,"count":2}]}
'@

$snapshot = $null
$parseError = ''
$parsed = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlySnapshotProtocol]::TryParse(
    $json,
    [ref]$snapshot,
    [ref]$parseError)
Assert-True $parsed "Inventory snapshot rejected: $parseError"
Assert-True $snapshot.HasInventorySnapshot 'Inventory availability was not preserved.'
Assert-True ($snapshot.InventoryItems.Count -eq 2) 'Expected two inventory entries.'
Assert-True ($snapshot.InventoryItems[0].ItemId -eq 90027) 'Item ID was not parsed.'
Assert-True ($snapshot.InventoryItems[0].Count -eq 2) 'Item count was not parsed.'
Assert-True $snapshot.HasSkillBookInventorySnapshot 'Skill-keyed inventory availability was not preserved.'
Assert-True ($snapshot.SkillBookInventory.Count -eq 2) 'Expected two skill-book inventory entries.'
Assert-True ($snapshot.SkillBookInventory[0].SkillId -eq 92133) 'Skill ID was not parsed.'
Assert-True ($snapshot.SkillBookInventory[0].Count -eq 3) 'Skill-book count was not parsed.'

$state = $null
$stateError = ''
$created = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlyStateAdapter]::TryCreate(
    $snapshot,
    [ref]$state,
    [ref]$stateError)
Assert-True $created "Read-only state rejected: $stateError"
$count = 0
Assert-True ($state.TryGetInventoryItemCount(90027, [ref]$count)) 'Known inventory lookup failed.'
Assert-True ($count -eq 2) 'Known inventory count mismatch.'
Assert-True ($state.TryGetInventoryItemCount(99004, [ref]$count)) 'Missing inventory lookup should still be available.'
Assert-True ($count -eq 0) 'Missing inventory item should have count zero.'
Assert-True ($state.TryGetSkillBookCount(92133, [ref]$count)) 'Skill-book inventory lookup failed.'
Assert-True ($count -eq 3) 'Skill-book inventory count mismatch.'

$preset = New-Object WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPreset
[void]$preset.Books.Add((New-Object WPELibrary.Lib.PetSkillBook.SkillBookEntry -ArgumentList @(92001, 90027)))
[void]$preset.Books.Add((New-Object WPELibrary.Lib.PetSkillBook.SkillBookEntry -ArgumentList @(92133, 99003)))
$report = [WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlyStateAdapter]::DescribePresetTargets(
    $preset,
    $state)
Assert-True ($report.Contains('背包数量=2')) 'Inventory count was not included in preflight.'
Assert-True ($report.Contains('可进入后续预演')) 'Available inventory was not classified.'
Assert-True ($report.Contains('背包数量=3')) 'Skill-keyed inventory count was not used in preflight.'

$negativeCountJson = $json.Replace('"count":2', '"count":-1')
$invalidSnapshot = $null
$invalidError = ''
Assert-False ([WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlySnapshotProtocol]::TryParse(
        $negativeCountJson,
        [ref]$invalidSnapshot,
        [ref]$invalidError)) 'Negative inventory count was accepted.'

$duplicateJson = $json.Replace('{"itemId":99003,"count":0}', '{"itemId":90027,"count":0}')
$duplicateSnapshot = $null
$duplicateError = ''
Assert-False ([WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlySnapshotProtocol]::TryParse(
        $duplicateJson,
        [ref]$duplicateSnapshot,
        [ref]$duplicateError)) 'Duplicate inventory item ID was accepted.'

$duplicateSkillJson = $json.Replace('{"skillId":92211,"count":2}', '{"skillId":92133,"count":2}')
$duplicateSkillSnapshot = $null
$duplicateSkillError = ''
Assert-False ([WPELibrary.Lib.PetSkillBook.PetSkillBookReadOnlySnapshotProtocol]::TryParse(
        $duplicateSkillJson,
        [ref]$duplicateSkillSnapshot,
        [ref]$duplicateSkillError)) 'Duplicate skill-book inventory ID was accepted.'

$operationAdapter = New-Object WPELibrary.Lib.PetSkillBook.FailClosedPetSkillBookOperationAdapter
$none = [System.Threading.CancellationToken]::None
$openResult = $operationAdapter.SubmitOpenSlotAsync(2, $none).Result
$studyResult = $operationAdapter.SubmitStudyBookAsync(90027, 92001, 2, $none).Result
$lockResult = $operationAdapter.SubmitLockSkillSlotAsync(2, $none).Result
Assert-True ($openResult.ToString() -eq 'Unavailable') 'Open-slot adapter did not fail closed.'
Assert-True ($studyResult.ToString() -eq 'Unavailable') 'Study-book adapter did not fail closed.'
Assert-True ($lockResult.ToString() -eq 'Unavailable') 'Lock-slot adapter did not fail closed.'
Assert-True ($operationAdapter.RejectedCallCount -eq 3) 'Fail-closed adapter call count mismatch.'
Assert-True ($operationAdapter.LastOperation.Contains('锁格')) 'Last rejected operation was not recorded.'
Assert-True (-not [string]::IsNullOrWhiteSpace($operationAdapter.LastRejectionReason)) 'Rejection reason was not recorded.'

Write-Output 'PetSkillBookInventoryPreflightRegression: PASS'
