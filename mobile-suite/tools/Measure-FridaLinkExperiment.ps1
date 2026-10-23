[CmdletBinding()]
param(
    [string]$Ndk = 'C:\Users\Administrator\AppData\Local\Android\Sdk\ndk\27.2.12479018',
    [string]$Devkit,
    [string]$SuiteRoot,
    [string]$WorkspaceRoot,
    [string]$ReferenceSuiteRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SuiteRoot)) {
    $SuiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
$suiteRoot = (Resolve-Path -LiteralPath $SuiteRoot).Path
if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = (Resolve-Path (Join-Path $suiteRoot '..\..\..\..')).Path
}
$workspaceRoot = (Resolve-Path -LiteralPath $WorkspaceRoot).Path
if ([string]::IsNullOrWhiteSpace($ReferenceSuiteRoot)) {
    $ReferenceSuiteRoot = Join-Path $workspaceRoot 'WinsockPacketEditor\mobile-suite'
}
$referenceSuiteRoot = (Resolve-Path -LiteralPath $ReferenceSuiteRoot).Path
if ([string]::IsNullOrWhiteSpace($Devkit)) {
    $Devkit = Join-Path $workspaceRoot '.scratch\frida-core-devkit-17.17.0-android-x86_64'
}
$devkit = (Resolve-Path -LiteralPath $Devkit).Path
$jniRoot = Join-Path $referenceSuiteRoot 'app\build\generated\jniLibs\x86_64'
$archive = Join-Path $devkit 'libfrida-core.a'
$compatSource = Join-Path $referenceSuiteRoot 'native\frida-shared\reallocarray_compat.c'
$referenceCore = Join-Path $jniRoot 'libfrida-core.so'
$toolBin = Join-Path $Ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin'
$linker = Join-Path $toolBin 'x86_64-linux-android23-clang.cmd'
$strip = Join-Path $toolBin 'llvm-strip.exe'
$nm = Join-Path $toolBin 'llvm-nm.exe'
$readelf = Join-Path $toolBin 'llvm-readelf.exe'
$outputRoot = Join-Path $workspaceRoot '.scratch\apk-optimization-20261018-01\size-lab'

$consumers = @(
    [pscustomobject]@{ Name = 'anlei'; File = 'libanlei_worker.so' },
    [pscustomobject]@{ Name = 'battleloop'; File = 'libbattle_loop_worker.so' },
    [pscustomobject]@{ Name = 'battleskip'; File = 'libbattle_skip_worker.so' },
    [pscustomobject]@{ Name = 'jinchan'; File = 'libjinchan_worker.so' },
    [pscustomobject]@{ Name = 'battlepanel-resistance-query'; File = 'libresistance_query.so' },
    [pscustomobject]@{ Name = 'starhunt'; File = 'libstarhunt_worker.so' },
    [pscustomobject]@{ Name = 'bosshunt'; File = 'libbosshunt_worker.so' }
)
$selfCheckExports = @(
    'frida_init',
    'frida_deinit',
    'frida_version',
    'frida_version_string',
    'frida_device_manager_new',
    'reallocarray'
)

function Assert-InputFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required experiment input missing: $Path"
    }
}

foreach ($path in @($archive, $compatSource, $referenceCore, $linker, $strip, $nm, $readelf)) {
    Assert-InputFile $path
}
foreach ($consumer in $consumers) {
    Assert-InputFile (Join-Path $jniRoot $consumer.File)
}

function Invoke-Tool([string]$Tool, [string[]]$Arguments, [string]$Label) {
    $output = @(& $Tool @Arguments 2>&1 | ForEach-Object { [string]$_ })
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Label failed (exit=$exitCode): $($output -join ' ')"
    }
    return ,$output
}

function Sort-Ordinal([string[]]$Values) {
    $items = [string[]]@($Values)
    [Array]::Sort($items, [StringComparer]::Ordinal)
    return ,$items
}

function Get-NmSymbols([string]$Path, [switch]$Dynamic, [switch]$UndefinedOnly) {
    $arguments = @()
    if ($Dynamic) { $arguments += '-D' }
    if ($UndefinedOnly) {
        $arguments += '-u'
    } else {
        $arguments += '--defined-only'
    }
    $arguments += $Path
    $lines = Invoke-Tool -Tool $nm -Arguments $arguments -Label "llvm-nm $Path"
    $symbols = [Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        $parts = ([string]$line).Trim() -split '\s+'
        if ($parts.Count -eq 0) { continue }
        $symbol = [string]$parts[-1]
        if ([string]::IsNullOrWhiteSpace($symbol) -or $symbol -match '^(\?|U|w|v)$') { continue }
        $symbol = ($symbol -split '@', 2)[0]
        if (-not [string]::IsNullOrWhiteSpace($symbol)) { $symbols.Add($symbol) }
    }
    return ,(Sort-Ordinal $symbols.ToArray())
}

function Get-ELFInfo([string]$Path) {
    $header = Invoke-Tool -Tool $readelf -Arguments @('-h', $Path) -Label "llvm-readelf -h $Path"
    $dynamic = Invoke-Tool -Tool $readelf -Arguments @('-d', $Path) -Label "llvm-readelf -d $Path"
    $headerText = [string]::Join([Environment]::NewLine, $header)
    $dynamicText = [string]::Join([Environment]::NewLine, $dynamic)
    $class = if ($headerText -match 'Class:\s+(\S+)') { $Matches[1] } else { $null }
    $type = if ($headerText -match '(?m)Type:\s+([^\r\n]+)') { $Matches[1].Trim() } else { $null }
    $machine = if ($headerText -match '(?m)Machine:\s+([^\r\n]+)') { $Matches[1].Trim() } else { $null }
    $needed = @([regex]::Matches($dynamicText, 'NEEDED\).*?\[([^\]]+)\]') | ForEach-Object { $_.Groups[1].Value })
    $soname = if ($dynamicText -match 'SONAME\).*?\[([^\]]+)\]') { $Matches[1] } else { $null }
    [pscustomobject]@{
        Class = $class
        Type = $type
        Machine = $machine
        Needed = $needed
        Soname = $soname
        HasTextRel = ($dynamicText -match '\bTEXTREL\b')
    }
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Measure-DeflateBytes([string]$Path, [string]$RunDirectory) {
    Add-Type -AssemblyName System.IO.Compression
    $tempZip = Join-Path $RunDirectory ('.deflate-' + [Guid]::NewGuid().ToString('N') + '.tmp.zip')
    $fileStream = $null
    $zip = $null
    $source = $null
    $entryStream = $null
    $readerStream = $null
    $readerZip = $null
    try {
        $fileStream = [IO.File]::Open($tempZip, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $zip = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Create, $true)
        $entry = $zip.CreateEntry('lib/x86_64/libfrida-core.so', [IO.Compression.CompressionLevel]::Optimal)
        $entryStream = $entry.Open()
        $source = [IO.File]::OpenRead($Path)
        $source.CopyTo($entryStream)
        $entryStream.Dispose()
        $entryStream = $null
        $zip.Dispose()
        $zip = $null
        $fileStream.Dispose()
        $fileStream = $null
        $readerStream = [IO.File]::OpenRead($tempZip)
        $readerZip = [IO.Compression.ZipArchive]::new($readerStream, [IO.Compression.ZipArchiveMode]::Read, $true)
        $measuredEntry = $readerZip.GetEntry('lib/x86_64/libfrida-core.so')
        if ($null -eq $measuredEntry) { throw 'Temporary Deflate ZIP is missing its native entry.' }
        return [int64]$measuredEntry.CompressedLength
    } finally {
        if ($null -ne $source) { $source.Dispose() }
        if ($null -ne $entryStream) { $entryStream.Dispose() }
        if ($null -ne $readerZip) { $readerZip.Dispose() }
        if ($null -ne $readerStream) { $readerStream.Dispose() }
        if ($null -ne $zip) { $zip.Dispose() }
        if ($null -ne $fileStream) { $fileStream.Dispose() }
        if (Test-Path -LiteralPath $tempZip) { Remove-Item -LiteralPath $tempZip -Force }
    }
}

function Get-MetricRecord([string]$Name, [string]$Path, [string]$RunDirectory) {
    $file = Get-Item -LiteralPath $Path
    $elf = Get-ELFInfo $Path
    $exports = Get-NmSymbols $Path -Dynamic
    $undefined = Get-NmSymbols $Path -Dynamic -UndefinedOnly
    [pscustomobject]@{
        Name = $Name
        Path = $Path
        Sha256 = Get-Sha256 $Path
        StrippedBytes = [int64]$file.Length
        DeflateBytes = Measure-DeflateBytes $Path $RunDirectory
        ExportCount = $exports.Count
        Exports = $exports
        UndefinedDynamicSymbols = $undefined
        ELF = $elf
    }
}

function New-VersionScript([string]$Path, [string[]]$Symbols) {
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('{')
    $lines.Add('  global:')
    foreach ($symbol in $Symbols) { $lines.Add("    $symbol;") }
    $lines.Add('  local:')
    $lines.Add('    *;')
    $lines.Add('};')
    $content = [string]::Join([Environment]::NewLine, $lines.ToArray())
    [IO.File]::WriteAllText($Path, $content, [Text.UTF8Encoding]::new($false))
}

$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path $outputRoot ("run-" + $runId)
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$versionScript = Join-Path $runDirectory 'required.exports.map'

Write-Output 'Reading seven worker/query ELF dynamic symbol tables...'
$currentExports = Get-NmSymbols $referenceCore -Dynamic
$currentExportSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($symbol in $currentExports) { [void]$currentExportSet.Add($symbol) }

$consumerEvidence = [Collections.Generic.List[object]]::new()
$workerRequiredList = [Collections.Generic.List[string]]::new()
foreach ($consumer in $consumers) {
    $path = Join-Path $jniRoot $consumer.File
    $undefined = Get-NmSymbols $path -Dynamic -UndefinedOnly
    $intersection = Sort-Ordinal @($undefined | Where-Object { $currentExportSet.Contains($_) })
    $elf = Get-ELFInfo $path
    if ($elf.Needed -notcontains 'libfrida-core.so') {
        throw "$($consumer.File) does not declare DT_NEEDED libfrida-core.so"
    }
    foreach ($symbol in $intersection) {
        if (-not $workerRequiredList.Contains($symbol)) { $workerRequiredList.Add($symbol) }
    }
    $consumerEvidence.Add([pscustomobject]@{
        Name = $consumer.Name
        File = $consumer.File
        Sha256 = Get-Sha256 $path
        UndefinedDynamicSymbols = $undefined
        IntersectionWithCurrentCoreExports = $intersection
        CurrentCoreIntersectionCount = $intersection.Count
        ELF = $elf
    })
}
$workerRequired = Sort-Ordinal $workerRequiredList.ToArray()
$requiredExportsList = [Collections.Generic.List[string]]::new()
foreach ($symbol in $workerRequired) {
    if (-not $requiredExportsList.Contains($symbol)) { $requiredExportsList.Add($symbol) }
}
foreach ($symbol in $selfCheckExports) {
    if (-not $requiredExportsList.Contains($symbol)) { $requiredExportsList.Add($symbol) }
}
$requiredExports = Sort-Ordinal $requiredExportsList.ToArray()
$missingFromCurrent = @($requiredExports | Where-Object { -not $currentExportSet.Contains($_) })
if ($missingFromCurrent.Count -gt 0) {
    throw "Required worker/self-check exports are absent from current core: $($missingFromCurrent -join ', ')"
}
New-VersionScript $versionScript $requiredExports

Write-Output "Worker/core intersection has $($workerRequired.Count) symbols; constrained export set has $($requiredExports.Count) symbols."
Write-Output 'Measuring current stripped shared core...'
$baseline = Get-MetricRecord 'current-stripped-shared-core' $referenceCore $runDirectory
$baselineExportSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($symbol in $baseline.Exports) { [void]$baselineExportSet.Add($symbol) }
$baselineMissing = @($requiredExports | Where-Object { -not $baselineExportSet.Contains($_) })
if ($baselineMissing.Count -gt 0) { throw "Current core misses required exports: $($baselineMissing -join ', ')" }

$variantDefinitions = @(
    [pscustomobject]@{ Name = 'whole-archive-gc-api-map'; WholeArchive = $true; ExportControl = $true },
    [pscustomobject]@{ Name = 'archive-selective-gc'; WholeArchive = $false; ExportControl = $false },
    [pscustomobject]@{ Name = 'archive-selective-gc-api-map'; WholeArchive = $false; ExportControl = $true }
)
$variantResults = [Collections.Generic.List[object]]::new()
foreach ($variant in $variantDefinitions) {
    $outputPath = Join-Path $runDirectory (Join-Path $variant.Name 'libfrida-core.so')
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
    $linkArgs = [Collections.Generic.List[string]]::new()
    $linkArgs.Add('-shared')
    $linkArgs.Add('-pthread')
    $linkArgs.Add('-Wl,-soname,libfrida-core.so')
    if ($variant.WholeArchive) {
        $linkArgs.Add('-Wl,--whole-archive')
        $linkArgs.Add($archive)
        $linkArgs.Add('-Wl,--no-whole-archive')
    } else {
        foreach ($symbol in $requiredExports) { $linkArgs.Add("-Wl,-u,$symbol") }
        $linkArgs.Add($archive)
    }
    $linkArgs.Add('-Wl,--gc-sections')
    if ($variant.ExportControl) { $linkArgs.Add("-Wl,--version-script=$versionScript") }
    $linkArgs.Add($compatSource)
    $linkArgs.Add('-llog')
    $linkArgs.Add('-ldl')
    $linkArgs.Add('-lm')
    $linkArgs.Add('-o')
    $linkArgs.Add($outputPath)

    $forcedSymbols = @()
    if (-not $variant.WholeArchive) { $forcedSymbols = $requiredExports }
    Write-Output "Linking $($variant.Name)..."
    $nativeArgs = $linkArgs.ToArray()
    $linkOutput = @(& $linker @nativeArgs 2>&1 | ForEach-Object { [string]$_ })
    $linkExit = $LASTEXITCODE
    if ($linkExit -ne 0 -or -not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        $variantResults.Add([pscustomobject]@{
            Name = $variant.Name
            Status = 'link_failed'
            FailureStage = 'link'
            LinkExitCode = $linkExit
            Diagnostics = @($linkOutput | Select-Object -First 30)
            WholeArchive = $variant.WholeArchive
            GarbageCollection = $true
            ExportControl = $variant.ExportControl
            ForcedUndefinedSymbols = $forcedSymbols
        })
        Write-Warning "Link failed for $($variant.Name), exit=$linkExit."
        continue
    }

    try {
        $preStripDefined = Get-NmSymbols $outputPath
        $compatDefinedBeforeStrip = ($preStripDefined -contains 'reallocarray')
        $null = Invoke-Tool -Tool $strip -Arguments @('--strip-unneeded', $outputPath) -Label "llvm-strip $($variant.Name)"
        $metric = Get-MetricRecord $variant.Name $outputPath $runDirectory
        $exportSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($symbol in $metric.Exports) { [void]$exportSet.Add($symbol) }
        $missingRequired = @($requiredExports | Where-Object { -not $exportSet.Contains($_) })
        $missingWorkerImports = [Collections.Generic.List[object]]::new()
        foreach ($consumer in $consumerEvidence) {
            $missing = @($consumer.IntersectionWithCurrentCoreExports | Where-Object { -not $exportSet.Contains($_) })
            if ($missing.Count -gt 0) {
                $missingWorkerImports.Add([pscustomobject]@{ Consumer = $consumer.Name; Missing = $missing })
            }
        }
        $baselineNeededSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($dependency in $baseline.ELF.Needed) { [void]$baselineNeededSet.Add($dependency) }
        $extraNeeded = @($metric.ELF.Needed | Where-Object { -not $baselineNeededSet.Contains($_) })
        $undefinedReallocarray = ($metric.UndefinedDynamicSymbols -contains 'reallocarray')
        $sameArchitecture = ($metric.ELF.Class -eq 'ELF64' -and $metric.ELF.Machine -match 'X86-64')
        $expectedSoname = ($metric.ELF.Soname -eq 'libfrida-core.so')
        $noTextRel = (-not $metric.ELF.HasTextRel)
        $contractPass = ($sameArchitecture -and $metric.ELF.Type -match 'DYN' -and $expectedSoname -and
            $noTextRel -and $extraNeeded.Count -eq 0 -and $missingRequired.Count -eq 0 -and
            $missingWorkerImports.Count -eq 0 -and $compatDefinedBeforeStrip -and -not $undefinedReallocarray)
        $sizeDelta = [int64]$baseline.StrippedBytes - [int64]$metric.StrippedBytes
        $deflateDelta = [int64]$baseline.DeflateBytes - [int64]$metric.DeflateBytes
        $variantStatus = if ($contractPass) { 'measured_contract_pass' } else { 'measured_contract_failure' }
        $variantResults.Add([pscustomobject]@{
            Name = $variant.Name
            Status = $variantStatus
            FailureStage = $null
            LinkExitCode = $linkExit
            WholeArchive = $variant.WholeArchive
            GarbageCollection = $true
            ExportControl = $variant.ExportControl
            ForcedUndefinedSymbols = $forcedSymbols
            Metric = $metric
            RequiredExportsMissing = $missingRequired
            WorkerImportsMissing = @($missingWorkerImports)
            ExtraNeededDependenciesVsBaseline = $extraNeeded
            SameArchitectureAsX86_64Worker = $sameArchitecture
            HasExpectedSoname = $expectedSoname
            HasNoTextRel = $noTextRel
            CompatSourceIncluded = $true
            ReallocarrayDefinedBeforeStrip = $compatDefinedBeforeStrip
            ReallocarrayUndefinedAfterLink = $undefinedReallocarray
            StrippedBytesSavedVsBaseline = $sizeDelta
            StrippedPercentSavedVsBaseline = [Math]::Round(100.0 * $sizeDelta / $baseline.StrippedBytes, 3)
            DeflateBytesSavedVsBaseline = $deflateDelta
            DeflatePercentSavedVsBaseline = [Math]::Round(100.0 * $deflateDelta / $baseline.DeflateBytes, 3)
            Diagnostics = @()
        })
        Write-Output ("Measured {0}: stripped={1:N0} bytes, deflate={2:N0} bytes, exports={3}, contract={4}" -f
            $variant.Name, $metric.StrippedBytes, $metric.DeflateBytes, $metric.ExportCount, $contractPass)
    } catch {
        $variantResults.Add([pscustomobject]@{
            Name = $variant.Name
            Status = 'analysis_failed'
            FailureStage = 'strip-or-measure'
            LinkExitCode = $linkExit
            WholeArchive = $variant.WholeArchive
            GarbageCollection = $true
            ExportControl = $variant.ExportControl
            ForcedUndefinedSymbols = $forcedSymbols
            Diagnostics = @($_.Exception.Message)
        })
        Write-Warning "Could not fully measure $($variant.Name): $($_.Exception.Message)"
    }
}

$successfulCandidates = @($variantResults | Where-Object { $_.Status -eq 'measured_contract_pass' })
$best = $null
if ($successfulCandidates.Count -gt 0) {
    $best = $successfulCandidates | Sort-Object { $_.Metric.DeflateBytes } | Select-Object -First 1
}
$bestName = $null
$conclusionText = 'No candidate completed link and static ELF contract checks; the production shared core remains the only measured baseline.'
if ($null -ne $best) {
    $bestName = $best.Name
    $strippedSavedMb = [Math]::Round($best.StrippedBytesSavedVsBaseline / 1000000.0, 3)
    $deflateSavedMb = [Math]::Round($best.DeflateBytesSavedVsBaseline / 1000000.0, 3)
    $conclusionText = "Best statically measured candidate is $($best.Name): stripped size saves $strippedSavedMb decimal MB ($($best.StrippedPercentSavedVsBaseline)%); Deflate saves $deflateSavedMb decimal MB ($($best.DeflatePercentSavedVsBaseline)%). No Android/runtime test was performed, so this result is not sufficient for production integration."
}

$inputFiles = @($archive, $compatSource, $referenceCore) + @($consumers | ForEach-Object { Join-Path $jniRoot $_.File })
$inputEvidence = @($inputFiles | ForEach-Object {
    $file = Get-Item -LiteralPath $_
    [pscustomobject]@{
        Path = $_
        LengthBytes = [int64]$file.Length
        Sha256 = Get-Sha256 $_
    }
})
$result = [ordered]@{
    SchemaVersion = 1
    Experiment = 'isolated-frida-core-link-size'
    CreatedAtUtc = [DateTime]::UtcNow.ToString('o')
    RunId = $runId
    OutputDirectory = $runDirectory
    Scope = [ordered]@{
        AndroidDeviceUsed = $false
        RootUsed = $false
        FridaAttachPerformed = $false
        NetworkRequested = $false
        GradleRun = $false
        ProductionFilesModified = $false
        CompressionMethod = 'ZIP Deflate, Optimal, one lib/x86_64/libfrida-core.so entry'
        LinkerTarget = 'x86_64-linux-android23'
        ReallocarrayCompatibilitySource = $compatSource
    }
    Inputs = $inputEvidence
    CurrentCoreExportCount = $currentExports.Count
    WorkerConsumers = @($consumerEvidence)
    WorkerRequiredExportUnion = $workerRequired
    SelfCheckAndCompatibilityExports = $selfCheckExports
    RequiredExports = $requiredExports
    ExportVersionScript = $versionScript
    Baseline = $baseline
    Variants = @($variantResults)
    Conclusion = [ordered]@{
        BestStaticCandidate = $bestName
        Text = $conclusionText
        ProductionIntegrationRecommended = $false
        RuntimeCompatibilityVerified = $false
        Reason = 'Static ELF linkage and compression measurements do not establish Android loader behavior or Frida attach/runtime compatibility.'
    }
}
$resultPath = Join-Path $runDirectory 'experiment-result.json'
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultPath -Encoding UTF8
Write-Output "RESULT_JSON=$resultPath"
Write-Output $conclusionText
Write-Output 'No Android, Root, Frida attach, network, or Gradle operation was performed.'
