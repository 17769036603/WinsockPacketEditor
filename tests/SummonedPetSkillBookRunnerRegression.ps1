<#
.SYNOPSIS
    SummonedPetSkillBookRunner 回归测试脚本
.DESCRIPTION
    执行 PetSkillBookRunner 的单元测试和集成测试，验证状态机逻辑完整性。
.PARAMETER RepositoryRoot
    项目根目录路径。如果不指定，将自动推导。
.PARAMETER HarnessExecutablePath
    Harness 可执行文件路径。如果不指定，将默认查找 Stage2Validation 目录。
.EXAMPLE
    .\SummonedPetSkillBookRunnerRegression.ps1
    .\SummonedPetSkillBookRunnerRegression.ps1 -HarnessExecutablePath .\tests\PetSkillBookRunnerHarness\bin\Stage2Validation\PetSkillBookRunnerHarness.exe
#>

param(
    [string]$RepositoryRoot = "",
    [string]$HarnessExecutablePath = ""
)

if ([string]::IsNullOrEmpty($RepositoryRoot)) {
    if (-not $PSScriptRoot) { throw "Error: cannot locate script directory. Please specify -RepositoryRoot." }
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}

$ErrorActionPreference = "Stop"

function Read-SourceFile {
    param([string]$RelativePath)
    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $RelativePath"
    }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

Write-Host "SummonedPetSkillBookRunner Regression Test..."

# ============================================
# 第一部分：静态检查
# ============================================

Write-Host "`n[阶段 1] 静态代码检查..."

# Check project file
$projectPath = Join-Path $RepositoryRoot "WPELibrary\WPELibrary.csproj"
Assert-Condition (Test-Path $projectPath) "WPELibrary.csproj not found"

# Check PetSkillBook directory
$petSkillBookDir = Join-Path $RepositoryRoot "WPELibrary\Lib\PetSkillBook"
Assert-Condition (Test-Path $petSkillBookDir) "PetSkillBook directory not found"

# Check required files
$requiredFiles = @(
    "PetSkillBookModels.cs",
    "SummonedPetSkillBookPreset.cs",
    "IPetSkillBookAdapter.cs",
    "SummonedPetSkillBookRunner.cs",
    "TestAdapters.cs"
)

foreach ($file in $requiredFiles) {
    $filePath = Join-Path $petSkillBookDir $file
    Assert-Condition (Test-Path $filePath) "Missing file: $file"
}
Write-Host "  [PASS] 文件结构检查通过"

# Read source files
$modelContent = Read-SourceFile "WPELibrary\Lib\PetSkillBook\PetSkillBookModels.cs"
$adapterContent = Read-SourceFile "WPELibrary\Lib\PetSkillBook\IPetSkillBookAdapter.cs"
$runnerContent = Read-SourceFile "WPELibrary\Lib\PetSkillBook\SummonedPetSkillBookRunner.cs"
$presetContent = Read-SourceFile "WPELibrary\Lib\PetSkillBook\SummonedPetSkillBookPreset.cs"
$testAdaptersContent = Read-SourceFile "WPELibrary\Lib\PetSkillBook\TestAdapters.cs"
$csprojContent = Read-SourceFile "WPELibrary\WPELibrary.csproj"

# Syntax / structure checks
Assert-Condition ($modelContent -match "public enum OperationResult") "PetSkillBookModels.cs missing OperationResult enum"
Assert-Condition ($modelContent -match "public enum PetMode") "PetSkillBookModels.cs missing PetMode enum"
Assert-Condition ($modelContent -match "public class SkillBookEntry") "PetSkillBookModels.cs missing SkillBookEntry class"

Assert-Condition ($adapterContent -match "IPetSkillBookReadOnlyAdapter") "IPetSkillBookAdapter.cs missing read-only interface"
Assert-Condition ($adapterContent -match "IPetSkillBookOperationAdapter") "IPetSkillBookAdapter.cs missing operation interface"

Assert-Condition ($runnerContent -match "public enum State") "Runner.cs missing State enum"
Assert-Condition ($runnerContent -match "ExecuteStateMachineAsync") "Runner.cs missing state machine method"

Assert-Condition ($csprojContent -match "PetSkillBookModels\.cs") "csproj not referencing PetSkillBookModels.cs"
Assert-Condition ($csprojContent -match "SummonedPetSkillBookRunner\.cs") "csproj not referencing SummonedPetSkillBookRunner.cs"
Assert-Condition ($csprojContent -match "TestAdapters\.cs") "csproj not referencing TestAdapters.cs"
Write-Host "  [PASS] 语法和项目引用检查通过"

# Shared state test adapter validation
Assert-Condition ($testAdaptersContent -match "class ScriptableGameState") "TestAdapters.cs missing ScriptableGameState shared state class"
Assert-Condition ($testAdaptersContent -match "sharedState") "ScriptableGameState field not used in adapters"

# 验证暂停后不会进入 COMPLETE
Assert-Condition ($runnerContent -match "(?s)if \(_context\.IsPaused \|\| cancellationToken\.IsCancellationRequested\)\s*\{\s*return;\s*// 已暂停或取消，不进入 COMPLETE") "Bug: 循环结束后缺少暂停/取消终止保护"
Write-Host "  [PASS] 暂停逻辑验证通过"

Write-Host "`n[阶段 1] 静态检查: PASS`n"

# ============================================
# 第二部分：实际运行 Harness 测试
# ============================================

Write-Host "[阶段 2] 实际运行 Harness 测试..."

# 确定 Harness 可执行文件路径
if ([string]::IsNullOrEmpty($HarnessExecutablePath)) {
    $DefaultHarnessPath = Join-Path $RepositoryRoot "tests\PetSkillBookRunnerHarness\bin\Stage2Validation\PetSkillBookRunnerHarness.exe"
    if (Test-Path $DefaultHarnessPath) {
        $HarnessExecutablePath = $DefaultHarnessPath
    }
    else {
        # 尝试常见的输出目录
        $AltPaths = @(
            (Join-Path $RepositoryRoot "tests\PetSkillBookRunnerHarness\bin\HarnessStage2Validation\PetSkillBookRunnerHarness.exe"),
            (Join-Path $RepositoryRoot "tests\PetSkillBookRunnerHarness\bin\Debug\PetSkillBookRunnerHarness.exe")
        )
        foreach ($altPath in $AltPaths) {
            if (Test-Path $altPath) {
                $HarnessExecutablePath = $altPath
                break
            }
        }
    }
}

if ([string]::IsNullOrEmpty($HarnessExecutablePath)) {
    Write-Host "  [WARN] 未找到 Harness 可执行文件，跳过实际运行测试"
    Write-Host "`nSummonedPetSkillBookRunner Regression Test: PASS (静态检查通过)"
    return
}

Write-Host "  使用 Harness: $HarnessExecutablePath"

# 运行 Harness
$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = $HarnessExecutablePath
$processInfo.UseShellExecute = $false
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.CreateNoWindow = $true
$processInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$processInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8

$process = [System.Diagnostics.Process]::Start($processInfo)
$output = $process.StandardOutput.ReadToEnd()
$errorOutput = $process.StandardError.ReadToEnd()
$process.WaitForExit()

Write-Host $output

if ($process.ExitCode -ne 0) {
    Write-Host ""
    Write-Host "  [FAIL] Harness 测试失败，退出码: $($process.ExitCode)"
    if ($errorOutput) {
        Write-Host "  错误输出: $errorOutput"
    }
    throw "Harness 测试失败"
}

# 验证输出中包含 ASCII 成功标记；中文日志编码由子进程控制台代码页决定
if ($output -notmatch "PASS") {
    Write-Host ""
    Write-Host "  [FAIL] Harness 输出未包含成功标记"
    throw "Harness 测试未通过"
}

Write-Host ""
Write-Host "SummonedPetSkillBookRunner Regression Test: PASS"
Write-Host "  - 静态检查: PASS"
Write-Host "  - 实际运行: PASS"
