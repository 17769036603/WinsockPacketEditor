# PetSkillBookCatalogRegression.ps1
# Offline checks for the supplied skill and skill-book item catalog.

param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BuildDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'WPELibrary\bin\PetSkillSnapshotValidation')
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param($Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    if ($Actual -ne $Expected) {
        throw "$Message Actual=$Actual Expected=$Expected"
    }
}

function Find-Skill {
    param([int]$SkillId)
    return @([WPELibrary.Lib.PetSkillBook.PetSkillBookCatalog]::Skills |
        Where-Object { $_.SkillId -eq $SkillId })[0]
}

function Find-Item {
    param([int]$ItemId)
    return @([WPELibrary.Lib.PetSkillBook.PetSkillBookCatalog]::Items |
        Where-Object { $_.ItemId -eq $ItemId })[0]
}

function Assert-Skill {
    param(
        [int]$SkillId,
        [int]$HexId,
        [string]$Name,
        [WPELibrary.Lib.PetSkillBook.PetSkillBookTier]$Tier
    )

    $skill = Find-Skill $SkillId
    Assert-True ($null -ne $skill) "Missing skill catalog entry: $SkillId"
    Assert-Equal $skill.HexId $HexId "Skill hex ID mismatch for $SkillId."
    Assert-Equal $skill.Name $Name "Skill name mismatch for $SkillId."
    Assert-Equal $skill.Tier $Tier "Skill tier mismatch for $SkillId."
}

function Assert-Item {
    param(
        [int]$ItemId,
        [int]$HexId,
        [string]$Name,
        [WPELibrary.Lib.PetSkillBook.PetSkillBookTier]$Tier,
        [bool]$IsBundle
    )

    $item = Find-Item $ItemId
    Assert-True ($null -ne $item) "Missing item catalog entry: $ItemId"
    Assert-Equal $item.HexId $HexId "Item hex ID mismatch for $ItemId."
    Assert-Equal $item.Name $Name "Item name mismatch for $ItemId."
    Assert-Equal $item.Tier $Tier "Item tier mismatch for $ItemId."
    Assert-Equal $item.IsBundle $IsBundle "Item bundle flag mismatch for $ItemId."
}

$libraryPath = Join-Path $BuildDirectory 'WPELibrary.dll'
Assert-True (Test-Path -LiteralPath $libraryPath) "Missing built library: $libraryPath"
Add-Type -Path $libraryPath

$catalogType = [WPELibrary.Lib.PetSkillBook.PetSkillBookCatalog]
$tierType = [WPELibrary.Lib.PetSkillBook.PetSkillBookTier]

Assert-Equal $catalogType::Skills.Count 114 'Unexpected skill catalog count.'
Assert-Equal $catalogType::Items.Count 9 'Unexpected item catalog count.'

Assert-Skill 92001 0x16761 '闪现' $tierType::Ordinary
Assert-Skill 92029 0x1677D '仙风道骨' $tierType::Ordinary
Assert-Skill 92101 0x167C5 '审时度势' $tierType::Advanced
Assert-Skill 92160 0x16800 '进退自如' $tierType::Advanced
Assert-Skill 92201 0x16829 '当头棒喝' $tierType::Advanced
Assert-Skill 92224 0x16840 '超级抗性' $tierType::Advanced
Assert-Skill 92190 0x1681E '超级闪现' $tierType::SourceException

Assert-Equal @($catalogType::Skills | Where-Object { $_.Tier -eq $tierType::Ordinary }).Count 29 'Ordinary skill count mismatch.'
Assert-Equal @($catalogType::Skills | Where-Object { $_.Tier -eq $tierType::Advanced }).Count 84 'Advanced skill count mismatch.'
Assert-Equal @($catalogType::Skills | Where-Object { $_.Tier -eq $tierType::SourceException }).Count 1 'Source exception count mismatch.'

Assert-Item 90027 0x15FAB '普通技能书' $tierType::Ordinary $false
Assert-Item 99002 0x182BA '普通技能书' $tierType::Ordinary $false
Assert-Item 99003 0x182BB '高级技能书' $tierType::Advanced $false
Assert-Item 99004 0x182BC '终级技能书' $tierType::Ultimate $false
Assert-Item 30010 0x0753A '高级技能书礼包' $tierType::Advanced $true
Assert-Item 30011 0x0753B '普通技能书' $tierType::Ordinary $false
Assert-Item 30018 0x0753E '终级技能书' $tierType::Ultimate $false
Assert-Item 30040 0x07570 '高级技能书礼包' $tierType::Advanced $true
Assert-Item 30041 0x07571 '终极技能书礼包' $tierType::Ultimate $true

$errorMessage = $null
Assert-True ($catalogType::TryValidateEntry(92001, 90027, [ref]$errorMessage)) 'Ordinary skill/book pair should be valid.'
Assert-True ($catalogType::TryValidateEntry(92110, 99003, [ref]$errorMessage)) 'Advanced skill/book pair should be valid.'
Assert-True ($catalogType::TryValidateEntry(92224, 30010, [ref]$errorMessage)) 'Advanced skill/bundle pair should be valid.'
Assert-True ($catalogType::TryValidateEntry(92190, 99003, [ref]$errorMessage)) 'Source exception should remain compatible.'
Assert-True ($catalogType::TryValidateEntry(1002, 40002, [ref]$errorMessage)) 'Unknown IDs must remain compatible with offline test adapters.'

Assert-True (-not $catalogType::TryValidateEntry(92001, 99003, [ref]$errorMessage)) 'Ordinary skill with advanced book must be rejected.'
Assert-True ($errorMessage.Contains('等级不匹配')) 'Tier mismatch error should be explicit.'
Assert-True (-not $catalogType::TryValidateEntry(92110, 90027, [ref]$errorMessage)) 'Advanced skill with ordinary book must be rejected.'
Assert-True (-not $catalogType::TryValidateEntry(92201, 99004, [ref]$errorMessage)) 'Advanced skill with ultimate book must be rejected.'

$preset = New-Object WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPreset
[void]$preset.Books.Add((New-Object WPELibrary.Lib.PetSkillBook.SkillBookEntry(92001, 90027)))
$presetError = $null
Assert-True ($preset.IsValid([ref]$presetError)) 'Preset with a known compatible pair should be valid.'
$preset.Books.Clear()
[void]$preset.Books.Add((New-Object WPELibrary.Lib.PetSkillBook.SkillBookEntry(92001, 99003)))
Assert-True (-not $preset.IsValid([ref]$presetError)) 'Preset with a known incompatible pair should be rejected.'
Assert-True ($presetError.Contains('等级不匹配')) 'Preset mismatch error should include the catalog reason.'

$description = $catalogType::DescribeEntry(92001, 90027)
Assert-True ($description.Contains('闪现') -and $description.Contains('普通技能书')) 'Catalog description must include skill and item names.'

Write-Host 'PetSkillBookCatalogRegression: PASS'
