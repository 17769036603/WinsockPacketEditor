# SummonedPetSkillBookRunnerRegression.ps1
# Static checks plus the compiled offline Harness.

param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$HarnessExecutablePath = ''
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

Write-Host 'SummonedPetSkillBookRunnerRegression starting...'

$projectPath = Join-Path $RepositoryRoot 'WPELibrary\WPELibrary.csproj'
Assert-True (Test-Path -LiteralPath $projectPath) 'WPELibrary.csproj is missing.'

$petDirectory = Join-Path $RepositoryRoot 'WPELibrary\Lib\PetSkillBook'
foreach ($file in @(
    'PetSkillBookModels.cs',
    'SummonedPetSkillBookPreset.cs',
    'IPetSkillBookAdapter.cs',
    'SummonedPetSkillBookRunner.cs',
    'TestAdapters.cs',
    'SummonedPetSkillBookPresetPlan.cs'
)) {
    Assert-True (Test-Path -LiteralPath (Join-Path $petDirectory $file)) "Missing source file: $file"
}

$model = Read-SourceFile 'WPELibrary\Lib\PetSkillBook\PetSkillBookModels.cs'
$adapter = Read-SourceFile 'WPELibrary\Lib\PetSkillBook\IPetSkillBookAdapter.cs'
$runner = Read-SourceFile 'WPELibrary\Lib\PetSkillBook\SummonedPetSkillBookRunner.cs'
$plan = Read-SourceFile 'WPELibrary\Lib\PetSkillBook\SummonedPetSkillBookPresetPlan.cs'
$testAdapters = Read-SourceFile 'WPELibrary\Lib\PetSkillBook\TestAdapters.cs'
$csproj = Read-SourceFile 'WPELibrary\WPELibrary.csproj'

foreach ($expected in @(
    'public enum OperationResult',
    'public enum PetMode',
    'public class SkillBookEntry',
    'public class BookExecutionResult',
    'public class SummonedPetSkillBookRunStatus'
)) { Assert-True $model.Contains($expected) "Model missing: $expected" }
Assert-True $adapter.Contains('IPetSkillBookReadOnlyAdapter') 'Read-only adapter is missing.'
Assert-True $adapter.Contains('IPetSkillBookOperationAdapter') 'Operation adapter is missing.'
Assert-True $runner.Contains('ExecuteStateMachineAsync') 'Runner state machine is missing.'
Assert-True $runner.Contains('OpenAllSlots = true') 'Runner must force opening all slots.'
Assert-True $runner.Contains('book.LockAfter = true') 'Runner must force immediate locking.'
Assert-True $runner.Contains('await FailRunAsync') 'Runner must have terminal failure handling.'
Assert-True $runner.Contains('changedSlotCount != 1') 'Runner must require one changed slot.'
Assert-True $runner.Contains('s.SkillId == book.SkillId') 'Runner must skip an existing target skill.'
Assert-True (-not $runner.Contains('ReadResourcesAsync')) 'Runner must not read resources proactively.'
Assert-True (-not $runner.Contains('ReadSkillCatalogAsync')) 'Runner must not read the skill catalog proactively.'
Assert-True $testAdapters.Contains('class ScriptableGameState') 'ScriptableGameState is missing.'
Assert-True $testAdapters.Contains('sharedState') 'Shared test state is missing.'
Assert-True $plan.Contains('GetSteps(int version)') 'Version-aware plan lookup is missing.'
Assert-True $plan.Contains('MatchesTemplateVersion') 'Template matching is missing.'
Assert-True $plan.Contains('Version = 2') 'V2 plan declaration is missing.'

foreach ($sourceName in @(
    'PetSkillBookModels.cs',
    'SummonedPetSkillBookRunner.cs',
    'TestAdapters.cs',
    'SummonedPetSkillBookPresetPlan.cs'
)) {
    Assert-True $csproj.Contains($sourceName) "Project does not include $sourceName."
}

Write-Host '  Static checks: PASS'

if ([string]::IsNullOrWhiteSpace($HarnessExecutablePath)) {
    foreach ($candidate in @(
        (Join-Path $RepositoryRoot 'tests\PetSkillBookRunnerHarness\bin\Stage2Validation\PetSkillBookRunnerHarness.exe'),
        (Join-Path $RepositoryRoot 'tests\PetSkillBookRunnerHarness\bin\HarnessStage2Validation\PetSkillBookRunnerHarness.exe'),
        (Join-Path $RepositoryRoot 'tests\PetSkillBookRunnerHarness\bin\FinalValidation\PetSkillBookRunnerHarness.exe'),
        (Join-Path $RepositoryRoot 'tests\PetSkillBookRunnerHarness\bin\Debug\PetSkillBookRunnerHarness.exe')
    )) {
        if (Test-Path -LiteralPath $candidate) {
            $HarnessExecutablePath = $candidate
            break
        }
    }
}

Assert-True (-not [string]::IsNullOrWhiteSpace($HarnessExecutablePath)) 'Compiled Harness executable was not found.'
Assert-True (Test-Path -LiteralPath $HarnessExecutablePath) "Harness executable is missing: $HarnessExecutablePath"

Write-Host "Running Harness: $HarnessExecutablePath"
$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = (Resolve-Path -LiteralPath $HarnessExecutablePath).Path
$processInfo.WorkingDirectory = Split-Path -Parent $processInfo.FileName
$processInfo.UseShellExecute = $false
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.CreateNoWindow = $true
$process = [System.Diagnostics.Process]::Start($processInfo)
$output = $process.StandardOutput.ReadToEnd()
$errorOutput = $process.StandardError.ReadToEnd()
$process.WaitForExit()
Write-Host $output

if ($process.ExitCode -ne 0) {
    if ($errorOutput) { Write-Host $errorOutput }
    throw "Harness failed with exit code $($process.ExitCode)."
}
Assert-True ($output -match 'PASS') 'Harness output did not contain PASS.'

Write-Host 'SummonedPetSkillBookRunnerRegression: PASS'
