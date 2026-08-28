# PetSkillBookPersistenceRegression.ps1
# Offline persistence and model checks for the summoned-pet preset.

param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BuildDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'WPELibrary\bin\SummonedPetSkillBookValidation')
)

$ErrorActionPreference = 'Stop'

function Read-SourceFile {
    param([string]$RelativePath)
    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing source file: $RelativePath" }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Assert-True {
    param($Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-False {
    param($Condition, [string]$Message)
    if ($Condition) { throw $Message }
}

Write-Host 'PetSkillBookPersistenceRegression starting...'

$cacheSource = Read-SourceFile 'WPELibrary\Lib\Socket_Cache.cs'
Assert-True $cacheSource.Contains('CREATE TABLE IF NOT EXISTS RobotSummonedPetPreset') 'Preset table CREATE statement is missing.'
Assert-True $cacheSource.Contains('PresetJson') 'PresetJson column is missing.'
Assert-True $cacheSource.Contains('SelectTable_RobotSummonedPetPreset') 'Preset SELECT method is missing.'
Assert-True $cacheSource.Contains('INSERT OR REPLACE INTO RobotSummonedPetPreset') 'Preset INSERT statement is missing.'
Assert-True $cacheSource.Contains('DELETE FROM RobotSummonedPetPreset') 'Preset DELETE statement is missing.'

$libraryPath = Join-Path $BuildDirectory 'WPELibrary.dll'
Assert-True (Test-Path -LiteralPath $libraryPath) "Missing built library: $libraryPath"
Add-Type -Path $libraryPath

$presetType = [WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPreset]
$entryType = [WPELibrary.Lib.PetSkillBook.SkillBookEntry]
$serializerType = [WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPresetSerializer]

$preset = $presetType::new()
$preset.Name = 'PersistenceTest'
$preset.OpenAllSlots = $false
$preset.LockAfter = $false
$preset.OpenItemId = 1001
$book1 = $entryType::new(101, 2001, $false)
$book2 = $entryType::new(102, 2002, $true)
$preset.Books.Add($book1)
$preset.Books.Add($book2)

$json = $serializerType::Serialize($preset)
Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'Serialize returned an empty string.'
Assert-True $json.Contains('PersistenceTest') 'Serialized JSON lost the preset name.'
Assert-False $json.Contains('BookResults') 'Runtime BookResults must not be embedded in preset JSON.'

$loaded = $null
$deserializeError = ''
Assert-True ($serializerType::TryDeserialize($json, [ref]$loaded, [ref]$deserializeError)) "Deserialize failed: $deserializeError"
Assert-True ($loaded.Name -eq 'PersistenceTest') 'Preset name did not round-trip.'
Assert-True ($loaded.Books.Count -eq 2) 'Book count did not round-trip.'
Assert-True $loaded.OpenAllSlots 'Loaded preset must force OpenAllSlots=true.'
Assert-True $loaded.LockAfter 'Loaded preset must force LockAfter=true.'
$unlockedBookCount = @($loaded.Books | Where-Object { -not $_.LockAfter }).Count
Assert-True ($unlockedBookCount -eq 0) 'Loaded books must force LockAfter=true.'

$legacyJson = '{"SchemaVersionProperty":1,"Name":"Legacy","OpenAllSlots":false,"LockAfter":false,"Books":[{"SkillId":301,"ItemId":6001,"LockAfter":false}]}'
$legacy = $null
$legacyError = ''
Assert-True ($serializerType::TryDeserialize($legacyJson, [ref]$legacy, [ref]$legacyError)) "Legacy deserialize failed: $legacyError"
Assert-True ($legacy.SchemaVersionProperty -eq 2) 'Legacy schema was not migrated to V2.'
Assert-True $legacy.OpenAllSlots 'Legacy preset did not force OpenAllSlots=true.'
Assert-True $legacy.Books[0].LockAfter 'Legacy book did not force LockAfter=true.'

$clone = $serializerType::DeserializeClone($loaded)
Assert-True ($null -ne $clone) 'DeserializeClone returned null.'
Assert-True ($clone.Name -eq $loaded.Name) 'Clone name mismatch.'
Assert-True $clone.Books[0].LockAfter 'Clone lost immediate-lock rule.'

$empty = $null
$emptyError = ''
Assert-False ($serializerType::TryDeserialize('', [ref]$empty, [ref]$emptyError)) 'Empty JSON must fail.'
$invalid = $null
$invalidError = ''
Assert-False ($serializerType::TryDeserialize('not json', [ref]$invalid, [ref]$invalidError)) 'Invalid JSON must fail.'

$resultType = [WPELibrary.Lib.PetSkillBook.BookExecutionResult]
foreach ($propertyName in @('Status', 'SkillId', 'ItemId', 'Sequence', 'SkillSlotIndex', 'FailureReason', 'StateVersionBefore', 'StateVersionAfter', 'ExecutedAt')) {
    Assert-True ($resultType.GetProperty($propertyName) -ne $null) "BookExecutionResult is missing $propertyName."
}
$runStatusType = [WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookRunStatus]
foreach ($propertyName in @('IsCompleted', 'IsFailed', 'IsCancelled', 'BookResults', 'FailureReason', 'CompletedAt')) {
    Assert-True ($runStatusType.GetProperty($propertyName) -ne $null) "RunStatus is missing $propertyName."
}

Write-Host 'PetSkillBookPersistenceRegression: PASS'
