[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ResultPath
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
    throw "Experiment result JSON missing: $ResultPath"
}
$result = Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8 | ConvertFrom-Json

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Output "PASS: $Message"
}

Require ($result.SchemaVersion -eq 1) 'schema version is recognized'
Require ($result.Experiment -eq 'isolated-frida-core-link-size') 'result identifies the isolated Frida size experiment'
Require ($result.Scope.AndroidDeviceUsed -eq $false -and
    $result.Scope.RootUsed -eq $false -and
    $result.Scope.FridaAttachPerformed -eq $false -and
    $result.Scope.NetworkRequested -eq $false -and
    $result.Scope.GradleRun -eq $false) 'scope records no device, Root, Frida attach, network, or Gradle action'
Require ($result.Scope.LinkerTarget -eq 'x86_64-linux-android23') 'linker target records Android API 23 x86_64'
Require ($result.Scope.CompressionMethod -match 'ZIP Deflate') 'compression measurement records ZIP Deflate'

Require (@($result.WorkerConsumers).Count -eq 7) 'exactly seven worker/query consumers are measured'
$observedUnion = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($consumer in $result.WorkerConsumers) {
    Require (@($consumer.UndefinedDynamicSymbols).Count -gt 0) "$($consumer.Name) has undefined dynamic symbols"
    Require (@($consumer.IntersectionWithCurrentCoreExports).Count -eq $consumer.CurrentCoreIntersectionCount) "$($consumer.Name) intersection count matches its symbol list"
    foreach ($symbol in $consumer.IntersectionWithCurrentCoreExports) { [void]$observedUnion.Add([string]$symbol) }
}
Require ($observedUnion.Count -eq @($result.WorkerRequiredExportUnion).Count) 'worker required export union has exactly the observed intersection count'
foreach ($symbol in $observedUnion) {
    Require (@($result.WorkerRequiredExportUnion) -contains $symbol) "worker export union contains $symbol"
}
foreach ($symbol in $result.WorkerRequiredExportUnion) {
    Require ($observedUnion.Contains([string]$symbol)) "worker export union symbol comes from an observed consumer intersection: $symbol"
}
foreach ($symbol in $result.SelfCheckAndCompatibilityExports) {
    Require (@($result.RequiredExports) -contains $symbol) "required export set preserves $symbol"
}

Require ($result.Baseline.StrippedBytes -gt 0) 'baseline stripped size is present'
Require ($result.Baseline.DeflateBytes -gt 0) 'baseline Deflate size is present'
Require ($result.Baseline.ELF.Class -eq 'ELF64') 'baseline is ELF64'
Require ($result.Baseline.ELF.Type -match 'DYN') 'baseline is a shared ELF object'
Require ($result.Baseline.ELF.Machine -match 'X86-64') 'baseline architecture is x86_64'
Require ($result.Baseline.ELF.Soname -eq 'libfrida-core.so') 'baseline SONAME is correct'

$expectedNames = @('whole-archive-gc-api-map', 'archive-selective-gc', 'archive-selective-gc-api-map')
foreach ($expectedName in $expectedNames) {
    Require (@($result.Variants | Where-Object Name -eq $expectedName).Count -eq 1) "result contains variant $expectedName"
}
foreach ($variant in $result.Variants) {
    if ($variant.Status -eq 'measured_contract_pass' -or $variant.Status -eq 'measured_contract_failure') {
        Require ($variant.Metric.StrippedBytes -gt 0 -and $variant.Metric.DeflateBytes -gt 0) "$($variant.Name) has stripped and Deflate sizes"
        Require ($variant.Metric.ELF.Class -eq 'ELF64' -and $variant.Metric.ELF.Machine -match 'X86-64') "$($variant.Name) architecture is x86_64"
        Require ($variant.Metric.ELF.Type -match 'DYN' -and $variant.Metric.ELF.Soname -eq 'libfrida-core.so') "$($variant.Name) remains a shared core with expected SONAME"
        Require ($variant.HasNoTextRel -eq $true) "$($variant.Name) has no TEXTREL"
        Require ($variant.RequiredExportsMissing.Count -eq 0) "$($variant.Name) retains the required export set"
        Require ($variant.WorkerImportsMissing.Count -eq 0) "$($variant.Name) covers every worker/core import intersection"
        Require ($variant.ExtraNeededDependenciesVsBaseline.Count -eq 0) "$($variant.Name) adds no DT_NEEDED dependency"
        Require ($variant.CompatSourceIncluded -eq $true -and $variant.ReallocarrayDefinedBeforeStrip -eq $true -and $variant.ReallocarrayUndefinedAfterLink -eq $false) "$($variant.Name) includes reallocarray compatibility without leaving it unresolved"
        $expectedStrippedDelta = [int64]$result.Baseline.StrippedBytes - [int64]$variant.Metric.StrippedBytes
        $expectedDeflateDelta = [int64]$result.Baseline.DeflateBytes - [int64]$variant.Metric.DeflateBytes
        Require ([int64]$variant.StrippedBytesSavedVsBaseline -eq $expectedStrippedDelta) "$($variant.Name) stripped-size delta is arithmetically consistent"
        Require ([int64]$variant.DeflateBytesSavedVsBaseline -eq $expectedDeflateDelta) "$($variant.Name) Deflate-size delta is arithmetically consistent"
    } else {
        Require (@($variant.Diagnostics).Count -gt 0) "$($variant.Name) records diagnostics for $($variant.Status)"
    }
}
Require ($result.Conclusion.ProductionIntegrationRecommended -eq $false) 'static measurement does not recommend production integration'
Require ($result.Conclusion.RuntimeCompatibilityVerified -eq $false) 'runtime compatibility remains explicitly unverified'
Write-Output 'Experiment JSON validation complete. This script only reads the result JSON.'
