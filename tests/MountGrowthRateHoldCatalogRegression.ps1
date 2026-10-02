param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$cachePath = Join-Path $RepositoryRoot "WPELibrary\Lib\Socket_Cache.cs"
if (-not (Test-Path -LiteralPath $cachePath)) { throw "Missing source file: $cachePath" }
$cache = [System.IO.File]::ReadAllText($cachePath, [System.Text.Encoding]::UTF8)

foreach ($required in @(
    "MountGrowthRateHoldPresetName =",
    "MountGrowthRateHold = 17",
    "TryParseMountGrowthRateHoldTarget",
    "target == 100D",
    'row["Type"] = InstructionType.MountGrowthRateHold;',
    'row["Content"] = "100.0";',
    "EnsureBuiltInMountGrowthRateHoldPreset",
    "MountGrowthRateHoldPresetName,",
    "InstructionType.MountGrowthRateHold:",
    "listMountGrowthRateHold.Count != 1 || dtRInstruction.Rows.Count != 1",
    "createdMountGrowthRateHoldPreset =",
    "createdMountGrowthRateHoldPreset)"
)) {
    if (-not $cache.Contains($required)) { throw "Missing growth-rate preset catalog contract: $required" }
}

Write-Output "MountGrowthRateHoldCatalogRegression: PASS"
