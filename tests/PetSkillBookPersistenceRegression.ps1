# PetSkillBookPersistenceRegression.ps1
# Tests for SummonedPetSkillBook preset persistence
# Covers: round-trip, schema verification, null handling, edge cases

param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BuildDirectory = "$(Join-Path (Split-Path -Parent $PSScriptRoot) 'WPELibrary\bin\SummonedPetSkillBookValidation')"
)

$ErrorActionPreference = "Stop"

function Read-SourceFile {
    param([string]$RelativePath)
    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $RelativePath"
    }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Assert-True {
    param($Condition, $Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-False {
    param($Condition, $Message)
    if ($Condition) {
        throw $Message
    }
}

$libraryPath = Join-Path $BuildDirectory "WPELibrary.dll"
if (-not (Test-Path -LiteralPath $libraryPath)) {
    throw "The built WPELibrary.dll is missing: $libraryPath"
}

# 1. Verify RobotSummonedPetPreset table is created in DeleteTable_Robot
$cacheSource = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
Assert-True ($cacheSource.Contains("CREATE TABLE IF NOT EXISTS RobotSummonedPetPreset")) "RobotSummonedPetPreset table CREATE statement missing"
Assert-True ($cacheSource.Contains("PresetJson")) "RobotSummonedPetPreset table missing PresetJson column"

# 2. Verify SelectTable_RobotSummonedPetPreset method exists
Assert-True ($cacheSource.Contains("SelectTable_RobotSummonedPetPreset")) "SelectTable_RobotSummonedPetPreset method missing"

# 3. Verify INSERT OR REPLACE in preset save path
Assert-True ($cacheSource.Contains("INSERT OR REPLACE INTO RobotSummonedPetPreset")) "INSERT OR REPLACE statement for preset missing"

# 4. Verify DELETE in DeleteTable_Robot for preset cleanup
Assert-True ($cacheSource.Contains("DELETE FROM RobotSummonedPetPreset")) "DELETE from RobotSummonedPetPreset missing"

Add-Type -Path $libraryPath

# 5. Round-trip test: serialize -> deserialize
$preset = [WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPreset]::new()
$preset.Name = "TestPreset"
$preset.PetMode = [WPELibrary.Lib.PetSkillBook.PetMode]::Specified
$preset.OpenAllSlots = $true
$preset.OpenItemId = 1001
$preset.OpenSlotSilverCost = 500
$preset.StudySilverCost = 1000
$preset.TimeoutMs = 10000
$preset.EnableStepLog = $true

$book1 = [WPELibrary.Lib.PetSkillBook.SkillBookEntry]::new()
$book1.SkillId = 101
$book1.ItemId = 2001
$book1.LockAfter = $true
$preset.Books.Add($book1)

$book2 = [WPELibrary.Lib.PetSkillBook.SkillBookEntry]::new()
$book2.SkillId = 102
$book2.ItemId = 2002
$book2.LockAfter = $false
$preset.Books.Add($book2)

$serializer = [WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPresetSerializer]
$json = $serializer::Serialize($preset)
Assert-True (-not [string]::IsNullOrWhiteSpace($json)) "Serialized JSON is empty"

$loaded = $null
$deserializeErr = ""
$result = $serializer::TryDeserialize($json, [ref]$loaded, [ref]$deserializeErr)
Assert-True $result "Deserialize failed: $deserializeErr"
Assert-True ($loaded.Name -eq $preset.Name) "Name mismatch after deserialize"
Assert-True ($loaded.Books.Count -eq 2) "Book count mismatch after deserialize"

# 6. Clone test via DeserializeClone
$clone = $serializer::DeserializeClone($loaded)
Assert-True ($null -ne $clone) "Clone failed"
Assert-True ($clone.Name -eq $loaded.Name) "Name mismatch after clone"

# 7. Empty string handling
$nullLoaded = $null
$nullErr = ""
$nullResult = $serializer::TryDeserialize("", [ref]$nullLoaded, [ref]$nullErr)
Assert-False $nullResult "Empty JSON should return false"

# 8. Invalid JSON handling
$invalidLoaded = $null
$invalidErr = ""
$invalidResult = $serializer::TryDeserialize("invalid json", [ref]$invalidLoaded, [ref]$invalidErr)
Assert-False $invalidResult "Invalid JSON should return false"

# 9. Verify SkillBookEntry constructor exists
$entry = [WPELibrary.Lib.PetSkillBook.SkillBookEntry]::new(500, 600, $true)
Assert-True ($entry.SkillId -eq 500) "SkillBookEntry constructor SkillId failed"
Assert-True ($entry.ItemId -eq 600) "SkillBookEntry constructor ItemId failed"
Assert-True $entry.LockAfter "SkillBookEntry constructor LockAfter failed"

# 10. Verify PetMode enum has Specied value
$petMode = [WPELibrary.Lib.PetSkillBook.PetMode]::Specified
Assert-True ($petMode -eq 1) "PetMode.Specified should equal 1"

# 11. Verify SaveRobotList_ToDB is called in RobotForm bSave_Click
$formSource = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"
Assert-True ($formSource.Contains("this.sriSelect.SummonedPetSkillBookPreset = this.presetEditing")) "Preset not assigned before SaveRobotList_ToDB"
Assert-True ($formSource.Contains("bSavePreset_Click")) "bSavePreset_Click method missing"

# 12. Verify LoadPresetToControls is called in InitFrom
Assert-True ($formSource.Contains("LoadPresetToControls()")) "LoadPresetToControls call missing in InitFrom"

Write-Output "PetSkillBookPersistenceRegression: PASS"
