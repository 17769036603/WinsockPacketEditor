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

function Assert-Contains {
    param([string]$Text, [string]$Expected, [string]$Message)
    if (-not $Text.Contains($Expected)) {
        throw $Message
    }
}

function Assert-NotContains {
    param([string]$Text, [string]$Unexpected, [string]$Message)
    if ($Text.Contains($Unexpected)) {
        throw $Message
    }
}

$planSource = Read-SourceFile "WPELibrary\Lib\PetSkillBook\SummonedPetSkillBookPresetPlan.cs"
$cacheSource = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$robotSource = Read-SourceFile "WPELibrary\Lib\Socket_Robot.cs"
$libraryPath = Join-Path $BuildDirectory "WPELibrary.dll"
$expectedPresetName = ([string][char]0x53EC) + ([char]0x5524) + ([char]0x517D) + ([char]0x6280) + ([char]0x80FD)
$expectedInstructionName = $expectedPresetName + ([char]0x6B65) + ([char]0x9AA4)
$expectedNoOperation = ([string][char]0x672A) + ([char]0x6267) + ([char]0x884C) + ([char]0x4EFB) + ([char]0x4F55) + ([char]0x6E38) + ([char]0x620F) + ([char]0x64CD) + ([char]0x4F5C)
$expectedPresetConstant = 'SummonedPetSkillBookPresetName = "' + $expectedPresetName + '"'

if (-not (Test-Path -LiteralPath $libraryPath)) {
    throw "The built WPELibrary.dll is missing: $libraryPath"
}

foreach ($forbiddenApi in @(
        "WriteProcessMemory",
        "NtWriteVirtualMemory",
        "VirtualAllocEx",
        "CreateRemoteThread",
        "ReadProcessMemory")) {
    Assert-NotContains $planSource $forbiddenApi `
        "The preset plan must not contain process-memory APIs: $forbiddenApi"
}

$runtimeMethodStart = $robotSource.IndexOf(
    "private void RunSummonedPetSkillBookPlanInstruction(",
    [System.StringComparison]::Ordinal)
if ($runtimeMethodStart -lt 0) {
    throw "The summoned-pet preset runtime guard was not found."
}
$runtimeMethodEnd = $robotSource.IndexOf(
    "private void RunTreasureMapInstruction(",
    $runtimeMethodStart,
    [System.StringComparison]::Ordinal)
if ($runtimeMethodEnd -lt 0) {
    throw "The summoned-pet preset runtime guard boundary was not found."
}
$runtimeMethod = $robotSource.Substring(
    $runtimeMethodStart,
    $runtimeMethodEnd - $runtimeMethodStart)
Assert-NotContains $runtimeMethod "SendSocket" `
    "The summoned-pet preset guard must not send a socket operation."
Assert-NotContains $runtimeMethod "SendSend" `
    "The summoned-pet preset guard must not send a packet operation."
Assert-Contains $runtimeMethod $expectedNoOperation `
    "The summoned-pet preset guard must fail closed before adapters are connected."
Assert-Contains $cacheSource $expectedPresetConstant `
    "The built-in preset must use the requested name."
Assert-Contains $cacheSource "CreateSummonedPetSkillBookPresetInstructions" `
    "The assistant cache must create the persisted step list."

Add-Type -Path $libraryPath

$planType = [WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPresetPlan]
$steps = $planType::GetSteps()
if ($steps.Count -ne 16) {
    throw "Expected 16 persisted summoned-pet steps, got $($steps.Count)."
}

foreach ($step in $steps) {
    $encoded = $planType::EncodeStep($step)
    $decoded = $null
    if (-not $planType::TryDecodeStep($encoded, [ref]$decoded)) {
        throw "Step $($step.Index) could not be decoded after encoding."
    }
    if ($decoded.Index -ne $step.Index -or $decoded.State -ne $step.State) {
        throw "Step $($step.Index) changed during encode/decode."
    }
    if (-not $step.DisplayText.Contains($step.State) -or
        -not $step.DisplayText.Contains($step.Description)) {
        throw "Step $($step.Index) is not human-readable."
    }
}

$robotType = [WPELibrary.Lib.Socket_Cache+Robot]
$instructionType = [WPELibrary.Lib.Socket_Cache+Robot+InstructionType]
$petInstructionType = $instructionType::SummonedPetSkillBook
$instructions = $robotType::CreateSummonedPetSkillBookPresetInstructions()
if ($instructions.Rows.Count -ne $steps.Count) {
    throw "The persisted assistant instruction count does not match the plan."
}
for ($index = 0; $index -lt $instructions.Rows.Count; $index++) {
    if ($instructions.Rows[$index]["Type"] -ne $petInstructionType) {
        throw "Instruction $index has the wrong instruction type."
    }
    if ($instructions.Rows[$index]["Content"] -ne $planType::EncodeStep($steps[$index])) {
        throw "Instruction $index has the wrong encoded step."
    }
}

if ($robotType::CheckRobotInstruction($instructions, $true) -ne -1) {
    throw "The complete summoned-pet preset was rejected by assistant validation."
}

$mixedInstructions = $instructions.Copy()
$delayRow = $mixedInstructions.NewRow()
$delayRow["Type"] = $instructionType::Delay
$delayRow["Content"] = "1"
$mixedInstructions.Rows.Add($delayRow)
if ($robotType::CheckRobotInstruction($mixedInstructions, $true) -lt 0) {
    throw "A mixed generic/summoned-pet instruction list must be rejected."
}

$invalidInstructions = $instructions.Copy()
$invalidInstructions.Rows[0]["Content"] = "invalid"
if ($robotType::CheckRobotInstruction($invalidInstructions, $true) -lt 0) {
    throw "An invalid summoned-pet step must be rejected."
}

if ($robotType::GetName_ByInstructionType($petInstructionType) -ne $expectedInstructionName) {
    throw "The assistant grid instruction name is incorrect."
}
if (-not $robotType::GetContentString_ByInstructionType(
        $petInstructionType,
        $instructions.Rows[1]["Content"].ToString()).Contains("VERIFY_CURRENT_PET")) {
    throw "The assistant grid did not render the state-machine step."
}
if ($robotType::GetInstructionType_ByString("SummonedPetSkillBook") -ne $petInstructionType) {
    throw "The persisted instruction type did not round-trip by enum name."
}
if ($robotType::GetInstructionType_ByString("10") -ne $petInstructionType) {
    throw "The persisted numeric instruction type did not round-trip."
}

$robotListType = [WPELibrary.Lib.Socket_Cache+RobotList]
$robotListType::lstRobot.Clear()
$created = $robotType::EnsureBuiltInSummonedPetSkillBookPreset()
$builtIn = @($robotListType::lstRobot | Where-Object {
        $_ -ne $null -and $_.RName -eq $expectedPresetName
    })
if (-not $created -or $builtIn.Count -ne 1) {
    throw "The built-in summoned-pet preset was not added to the assistant list."
}
if ($builtIn[0].IsEnable -or $builtIn[0].RInstruction.Rows.Count -ne 16) {
    throw "The built-in summoned-pet preset must be disabled and contain all steps."
}

Write-Output "SummonedPetSkillBookPresetRegression: PASS"
