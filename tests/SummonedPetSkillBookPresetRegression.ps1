# SummonedPetSkillBookPresetRegression.ps1
# Offline regression checks for the 15-step preset and its fail-closed entry.

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

function Assert-Contains {
    param([string]$Text, [string]$Expected, [string]$Message)
    if (-not $Text.Contains($Expected)) { throw $Message }
}

function Assert-NotContains {
    param([string]$Text, [string]$Unexpected, [string]$Message)
    if ($Text.Contains($Unexpected)) { throw $Message }
}

Write-Host 'SummonedPetSkillBookPresetRegression starting...'

$planSource = Read-SourceFile 'WPELibrary\Lib\PetSkillBook\SummonedPetSkillBookPresetPlan.cs'
$runnerSource = Read-SourceFile 'WPELibrary\Lib\PetSkillBook\SummonedPetSkillBookRunner.cs'
$cacheSource = Read-SourceFile 'WPELibrary\Lib\Socket_Cache.cs'
$robotSource = Read-SourceFile 'WPELibrary\Lib\Socket_Robot.cs'
$serializerSource = Read-SourceFile 'WPELibrary\Lib\PetSkillBook\SummonedPetSkillBookPresetSerializer.cs'
$libraryPath = Join-Path $BuildDirectory 'WPELibrary.dll'

Assert-True (Test-Path -LiteralPath $libraryPath) "Missing built library: $libraryPath"

# Production entry must remain fail-closed and must not perform a socket operation.
$runtimeStart = $robotSource.IndexOf('private void RunSummonedPetSkillBookPlanInstruction(', [System.StringComparison]::Ordinal)
Assert-True ($runtimeStart -ge 0) 'Summoned-pet runtime guard is missing.'
$runtimeEnd = $robotSource.IndexOf('private void RunTreasureMapInstruction(', $runtimeStart, [System.StringComparison]::Ordinal)
Assert-True ($runtimeEnd -gt $runtimeStart) 'Summoned-pet runtime guard boundary is missing.'
$runtimeMethod = $robotSource.Substring($runtimeStart, $runtimeEnd - $runtimeStart)
Assert-NotContains $runtimeMethod 'SendSocket' 'Summoned-pet entry must not send sockets.'
Assert-NotContains $runtimeMethod 'SendSend' 'Summoned-pet entry must not send packets.'
$noOperation = ([string][char]0x672A) + ([char]0x6267) + ([char]0x884C) + ([char]0x4EFB) + ([char]0x4F55) + ([char]0x6E38) + ([char]0x620F) + ([char]0x64CD) + ([char]0x4F5C)
Assert-Contains $runtimeMethod $noOperation 'Summoned-pet entry must fail closed.'

# The runner must not perform proactive resource or catalog reads.
Assert-NotContains $runnerSource 'ReadResourcesAsync' 'Runner must not precheck inventory or silver.'
Assert-NotContains $runnerSource 'ReadSkillCatalogAsync' 'Runner must not precheck the skill catalog.'
Assert-Contains $runnerSource 'await FailRunAsync' 'Runner must have a terminal failure path.'
Assert-Contains $runnerSource 's.SkillId == book.SkillId' 'Runner must skip an existing skill regardless of lock state.'
Assert-Contains $runnerSource 'changedSlotCount != 1' 'Runner must require exactly one changed slot.'
Assert-Contains $serializerSource 'book.LockAfter = true' 'Loaded books must use immediate locking.'

$expectedPresetName = ([string][char]0x53EC) + ([char]0x5524) + ([char]0x517D) + ([char]0x6280) + ([char]0x80FD)
$expectedInstructionName = $expectedPresetName + ([char]0x6B65) + ([char]0x9AA4)
$expectedPresetConstant = 'SummonedPetSkillBookPresetName = "' + $expectedPresetName + '"'
Assert-Contains $cacheSource $expectedPresetConstant 'Built-in preset name is missing.'
Assert-Contains $cacheSource 'CreateSummonedPetSkillBookPresetInstructions' 'Built-in instruction factory is missing.'
Assert-Contains $cacheSource $expectedInstructionName 'Instruction display name is missing.'

Add-Type -Path $libraryPath

$planType = [WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPresetPlan]
$steps = $planType::GetSteps()
Assert-True ($steps.Count -eq 15) 'V2 template must contain 15 steps.'
Assert-False (($steps | Where-Object { $_.State -eq 'CHECK_MATERIALS' }).Count -gt 0) 'V2 must not contain CHECK_MATERIALS.'

foreach ($step in $steps) {
    $encoded = $planType::EncodeStep($step)
    $decoded = $null
    Assert-True ($planType::TryDecodeStep($encoded, [ref]$decoded)) "Step $($step.Index) failed to decode."
    Assert-True ($decoded.Index -eq $step.Index -and $decoded.State -eq $step.State) "Step $($step.Index) changed during round-trip."
    Assert-True ($step.DisplayText.Contains($step.State) -and $step.DisplayText.Contains($step.Description)) "Step $($step.Index) is not readable."
}

$stepsV1 = $planType::GetSteps(1)
$stepsV2 = $planType::GetSteps(2)
Assert-True ($stepsV1.Count -eq 16) 'V1 template must contain 16 steps.'
Assert-True ($stepsV2.Count -eq 15) 'V2 template must contain 15 steps.'
Assert-True ($stepsV1[3].State -eq 'CHECK_MATERIALS') 'V1 template must retain CHECK_MATERIALS.'

[string[]]$encodedV1 = @()
foreach ($step in $stepsV1) {
    $encodedV1 += "SummonedPetSkillBook|1|$($step.Index)|$($step.State)"
}
Assert-True ($planType::MatchesTemplateVersion($encodedV1, 1)) 'V1 encoded steps must match V1.'
Assert-False ($planType::MatchesTemplateVersion($encodedV1, 2)) 'V1 encoded steps must not match V2.'

[string[]]$encodedV2 = @($stepsV2 | ForEach-Object { $planType::EncodeStep($_) })
Assert-True ($planType::MatchesTemplateVersion($encodedV2, 2)) 'V2 encoded steps must match V2.'

$robotType = [WPELibrary.Lib.Socket_Cache+Robot]
$instructionType = [WPELibrary.Lib.Socket_Cache+Robot+InstructionType]
$petInstructionType = $instructionType::SummonedPetSkillBook
$instructions = $robotType::CreateSummonedPetSkillBookPresetInstructions()
Assert-True ($instructions.Rows.Count -eq 15) 'Built-in instruction list must contain 15 rows.'

for ($index = 0; $index -lt $instructions.Rows.Count; $index++) {
    Assert-True ($instructions.Rows[$index]['Type'] -eq $petInstructionType) "Instruction $index has the wrong type."
    Assert-True ($instructions.Rows[$index]['Content'] -eq $planType::EncodeStep($steps[$index])) "Instruction $index has the wrong content."
}

Assert-True ($robotType::CheckRobotInstruction($instructions, $true) -eq -1) 'Complete V2 instruction list was rejected.'
$mixed = $instructions.Copy()
$delayRow = $mixed.NewRow()
$delayRow['Type'] = $instructionType::Delay
$delayRow['Content'] = '1'
$mixed.Rows.Add($delayRow)
Assert-True ($robotType::CheckRobotInstruction($mixed, $true) -ge 0) 'Mixed instruction list was accepted.'
$invalid = $instructions.Copy()
$invalid.Rows[0]['Content'] = 'invalid'
Assert-True ($robotType::CheckRobotInstruction($invalid, $true) -ge 0) 'Invalid instruction list was accepted.'
Assert-True ($robotType::GetName_ByInstructionType($petInstructionType) -eq $expectedInstructionName) 'Instruction name round-trip failed.'
Assert-True ($robotType::GetInstructionType_ByString('SummonedPetSkillBook') -eq $petInstructionType) 'Instruction string round-trip failed.'
Assert-True ($robotType::GetInstructionType_ByString('10') -eq $petInstructionType) 'Instruction numeric round-trip failed.'

$robotListType = [WPELibrary.Lib.Socket_Cache+RobotList]
$robotListType::lstRobot.Clear()
$created = $robotType::EnsureBuiltInSummonedPetSkillBookPreset()
$builtIn = @($robotListType::lstRobot | Where-Object { $_ -ne $null -and $_.RName -eq $expectedPresetName })
Assert-True ($created -and $builtIn.Count -eq 1) 'Built-in preset was not created.'
Assert-False $builtIn[0].IsEnable 'Built-in preset must be disabled.'
Assert-True ($builtIn[0].RInstruction.Rows.Count -eq 15) 'Built-in preset must contain 15 rows.'

$contextType = [WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookContext]
Assert-True ($contextType.GetProperty('RunStatus') -ne $null) 'Context is missing RunStatus.'
$runStatusType = [WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookRunStatus]
Assert-True ($runStatusType.GetProperty('BookResults') -ne $null) 'RunStatus is missing BookResults.'
$resultType = [WPELibrary.Lib.PetSkillBook.BookExecutionResult]
foreach ($propertyName in @('Status', 'SkillId', 'ItemId', 'Sequence', 'FailureReason', 'ExecutedAt')) {
    Assert-True ($resultType.GetProperty($propertyName) -ne $null) "BookExecutionResult is missing $propertyName."
}

Write-Host 'SummonedPetSkillBookPresetRegression: PASS'
