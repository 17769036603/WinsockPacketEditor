param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Read-SourceFile {
    param([string]$RelativePath)

    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $RelativePath"
    }

    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Expected,
        [string]$Message
    )

    if (-not $Text.Contains($Expected)) {
        throw $Message
    }
}

$robotForm = Read-SourceFile 'WPELibrary\Socket_RobotForm.cs'
Assert-Contains $robotForm 'parameters["SummonedPetSkillBookPreset"] = preset;' `
    'The desktop execution path must pass the active summoned-pet preset into the read-only preflight.'
Assert-Contains $robotForm 'AppendPetSkillPreflight' `
    'The snapshot UI must render the per-book read-only preflight report.'
Assert-Contains $robotForm '启动前只读预演完成：未执行游戏操作' `
    'A read-only preview must finish with an explicit no-operation status.'
Assert-Contains $robotForm 'this.bStop.Enabled = false;' `
    'A read-only preview must not leave the stop action enabled.'
Assert-Contains $robotForm '技能书库存：{1} 种/{2} 本' `
    'The UI resource summary must display the parsed skill-book inventory totals.'

$robot = Read-SourceFile 'WPELibrary\Lib\Socket_Robot.cs'
Assert-Contains $robot 'public PetSkillBookReadOnlySnapshot ReadOnlyPetSkillSnapshot' `
    'The robot must expose the snapshot produced by startup preflight to the UI.'
Assert-Contains $robot 'public PetSkillBookReadOnlyState ReadOnlyPetSkillState' `
    'The robot must expose the normalized read-only state to the UI.'
Assert-Contains $robot 'PetSkillBookReadOnlyStateAdapter.DescribePresetTargets' `
    'Startup preflight must evaluate the active preset against the normalized state.'
$state = Read-SourceFile 'WPELibrary\Lib\PetSkillBook\PetSkillBookReadOnlyState.cs'
Assert-Contains $state 'public static string DescribeSlotEvidence' `
    'The normalized state must expose explicit raw slot evidence without guessing open/locked semantics.'

$mainForm = Read-SourceFile 'WPELibrary\Socket_Form.cs'
Assert-Contains $mainForm '未加载召唤兽技能书配置' `
    'The completion flow must recognize a missing summoned-pet skill-book preset.'
Assert-Contains $mainForm 'Socket_Operation.ShowRobotForm_Dialog(completedRobotInfo);' `
    'The completion flow must open the matching assistant editor after the missing-preset notice.'

Write-Output 'PetSkillBookUiPreflightRegression: PASS'
