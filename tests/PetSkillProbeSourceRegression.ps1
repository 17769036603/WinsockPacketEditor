$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repositoryRoot 'work\PetSkillProbe\pet_skill_probe.cpp'

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Pet skill probe source is missing: $sourcePath"
}

$source = [System.IO.File]::ReadAllText($sourcePath, [System.Text.Encoding]::UTF8)

function Assert-Contains([string]$text, [string]$fragment, [string]$message) {
    if (-not $text.Contains($fragment)) {
        throw $message
    }
}

function Assert-NotContains([string]$text, [string]$fragment, [string]$message) {
    if ($text.Contains($fragment)) {
        throw $message
    }
}

$jsonStart = $source.IndexOf('bool print_json_snapshot', [System.StringComparison]::Ordinal)
$managerStart = $source.IndexOf('bool find_pet_manager', $jsonStart, [System.StringComparison]::Ordinal)
if ($jsonStart -lt 0 -or $managerStart -le $jsonStart) {
    throw 'Could not isolate JSON snapshot implementation.'
}
$jsonBlock = $source.Substring($jsonStart, $managerStart - $jsonStart)

Assert-Contains $jsonBlock 'read_first_named_value_of_kind(' `
    'JSON mode must read the current participant ID.'
Assert-Contains $jsonBlock 'decoder.find_string_objects("m_Pets", pets_strings)' `
    'JSON mode must enumerate the PetMgr pet container instead of selecting the first global object.'
Assert-Contains $jsonBlock 'add_candidate(entry.value)' `
    'JSON mode must collect pet objects from the m_Pets container.'
Assert-Contains $jsonBlock 'candidate_pet_id->value.integer != current_pet_id.integer' `
    'JSON mode must reject pet objects that are not the current participant.'
Assert-Contains $jsonBlock 'matched_pet_count != 1U' `
    'JSON mode must fail closed when the current pet is absent or ambiguous.'
Assert-Contains $jsonBlock 'try_read_skill_book_inventory(decoder, skill_book_inventory)' `
    'JSON mode must attempt to read the optional skill-book inventory from the live bag.'
Assert-Contains $jsonBlock ',\"skillBookInventory\":' `
    'JSON mode must emit the optional skill-book inventory field when the read is available.'
Assert-NotContains $jsonBlock 'read_first_named_array(decoder, "m_Petskills"' `
    'JSON mode must not select the first global skill array.'
Assert-NotContains $jsonBlock 'read_first_named_value_of_kind(decoder, "m_PetId"' `
    'JSON mode must not select the first global pet ID.'

Write-Output 'PetSkillProbeSourceRegression: PASS'
