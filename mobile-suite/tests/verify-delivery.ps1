[CmdletBinding()]
param(
    [string]$RealApkPath,
    [string]$BaselineApk,
    [string]$AndroidSdkPath,
    [string]$JdkPath
)

$ErrorActionPreference = 'Stop'
$suiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $suiteRoot 'tools\SuiteDelivery.Common.psm1') -Force
$script:checks = 0

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Delivery verification failed: $Message" }
    $script:checks++
}

function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $threw = $false
    try { & $Action } catch { $threw = $true }
    if (-not $threw) { throw "Delivery verification expected rejection: $Message" }
    $script:checks++
}

$gradle = [IO.File]::ReadAllText((Join-Path $suiteRoot 'app\build.gradle'))
Assert-Check ($gradle.Contains("providers.gradleProperty('piaomiaoVersionCode').getOrElse('16')")) 'default versionCode is 16 and configurable'
Assert-Check ($gradle.Contains("providers.gradleProperty('piaomiaoVersionName').getOrElse('2026.10.19.1-friends')")) 'stable versionName default is explicit'
Assert-Check ($gradle.Contains("providers.gradleProperty('piaomiaoReleaseChannel').getOrElse('stable')")) 'stable channel is the default'
Assert-Check ($gradle.Contains("getOrElse('false').toBoolean()")) 'license bypass remains opt-in'

Assert-SuiteVersionName -VersionName '2026.10.18.2-friends-open-v1-test' -Channel test
$script:checks++
Assert-SuiteVersionName -VersionName '2026.10.18.2-friends' -Channel stable
$script:checks++
Assert-Throws { Assert-SuiteVersionName -VersionName '2026.10.18.2-friends-test' -Channel stable } 'stable/test suffix mismatch'
Assert-Throws { Assert-SuiteVersionName -VersionName '2026.10.18.2/friends-test' -Channel test } 'path characters in versionName'

$baselineFixture = [pscustomobject]@{
    PackageName = 'com.xnas.wpe.suite'; VersionCode = 14L; VersionName = '2026.10.18.1-friends-open-v1'
    Abis = @('x86_64'); MinSdk = 23; SigningCertificateSha256 = @('ABCDEF')
}
$candidateFixture = [pscustomobject]@{
    PackageName = 'com.xnas.wpe.suite'; VersionCode = 15L; VersionName = '2026.10.18.2-friends-open-v1-test'
    Abis = @('x86_64'); MinSdk = 23; SigningCertificateSha256 = @('ABCDEF')
}
Assert-SuiteApkCompatibility -Candidate $candidateFixture -Baseline $baselineFixture
$script:checks++
$bad = $candidateFixture | Select-Object *
$bad.PackageName = 'com.other.app'
Assert-Throws { Assert-SuiteApkCompatibility -Candidate $bad -Baseline $baselineFixture } 'package mismatch'
$bad = $candidateFixture | Select-Object *
$bad.VersionCode = 14L
Assert-Throws { Assert-SuiteApkCompatibility -Candidate $bad -Baseline $baselineFixture } 'non-increasing versionCode'
$bad = $candidateFixture | Select-Object *
$bad.SigningCertificateSha256 = @('DIFFERENT')
Assert-Throws { Assert-SuiteApkCompatibility -Candidate $bad -Baseline $baselineFixture } 'signing certificate mismatch'
$bad = $candidateFixture | Select-Object *
$bad.Abis = @('arm64-v8a')
Assert-Throws { Assert-SuiteApkCompatibility -Candidate $bad -Baseline $baselineFixture } 'baseline ABI is missing'

if ([string]::IsNullOrWhiteSpace($RealApkPath)) {
    if (-not [string]::IsNullOrWhiteSpace($BaselineApk)) {
        $RealApkPath = $BaselineApk
    }
    else {
        $artifact = Get-ChildItem -LiteralPath (Join-Path $suiteRoot 'artifacts') -Filter '*.apk' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $artifact) { throw 'Pass -RealApkPath or -BaselineApk to analyze a real APK.' }
        $RealApkPath = $artifact.FullName
    }
}
$realApkFullPath = (Resolve-Path -LiteralPath $RealApkPath -ErrorAction Stop).Path
$hashBefore = (Get-FileHash -LiteralPath $realApkFullPath -Algorithm SHA256).Hash
$realMetadata = Get-SuiteApkMetadata -ApkPath $realApkFullPath -AndroidSdkPath $AndroidSdkPath -JdkPath $JdkPath
$hashAfter = (Get-FileHash -LiteralPath $realApkFullPath -Algorithm SHA256).Hash
Assert-Check ($hashBefore -ceq $hashAfter) 'real APK analysis leaves the input byte-identical'
Assert-Check ($realMetadata.PackageName -match '^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$') 'aapt returned an actual package name'
Assert-Check ($realMetadata.VersionCode -gt 0 -and $realMetadata.SigningCertificateSha256.Count -gt 0) 'aapt and apksigner returned version and certificate metadata'
Assert-Check ($realMetadata.Abis.Count -gt 0 -and $null -ne $realMetadata.MinSdk) 'aapt returned ABI and minSdk metadata'
$provenance = Get-SuiteGitProvenance -SourceRoot $suiteRoot
$sourceDigest = Get-SuiteSourceDigest -SourceRoot $suiteRoot
Assert-Check ($provenance.Commit -match '^[0-9a-fA-F]{40,64}$') 'source commit is recorded as a Git object id'
Assert-Check ($provenance.Dirty -is [bool]) 'source dirty state is recorded as a boolean'
Assert-Check ($sourceDigest -match '^[0-9A-F]{64}$') 'source digest is a SHA-256 hex value'

$noBaselineOutput = Join-Path (Split-Path -Parent $realApkFullPath) ('delivery-must-not-exist-' + [guid]::NewGuid().ToString('N'))
$missingBaselineRejected = $false
try {
    & (Join-Path $suiteRoot 'tools\New-SuiteDelivery.ps1') -ApkPath $realApkFullPath -OutputDirectory $noBaselineOutput | Out-Null
}
catch {
    if ($_.Exception.Message -like '*A baseline APK is required*') { $missingBaselineRejected = $true }
    else { throw }
}
Assert-Check $missingBaselineRejected 'delivery generation requires a baseline APK'
Assert-Check (-not (Test-Path -LiteralPath $noBaselineOutput)) 'missing-baseline rejection creates no output directory'

$pathBaseline = $null
if (-not [string]::IsNullOrWhiteSpace($BaselineApk)) {
    $pathBaseline = (Resolve-Path -LiteralPath $BaselineApk -ErrorAction Stop).Path
}
else {
    $otherApk = Get-ChildItem -LiteralPath (Join-Path $suiteRoot 'artifacts') -Filter '*.apk' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ine $realApkFullPath } | Select-Object -First 1
    if ($otherApk) { $pathBaseline = $otherApk.FullName }
}
if ($pathBaseline -and $pathBaseline -ieq $realApkFullPath) {
    $otherApk = Get-ChildItem -LiteralPath (Split-Path -Parent $pathBaseline) -Filter '*.apk' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ine $realApkFullPath } | Select-Object -First 1
    if ($otherApk) { $pathBaseline = $otherApk.FullName }
}
if ($pathBaseline) {
    Assert-Throws { Assert-SuiteDeliveryPaths -ApkPath $realApkFullPath -BaselineApk $pathBaseline -OutputDirectory $realApkFullPath } 'an existing output file path is rejected'
}

Write-Output "verify-delivery.ps1: $script:checks checks passed; real APK metadata read without modifying the input."
