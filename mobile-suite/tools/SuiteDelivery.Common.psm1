Set-StrictMode -Version Latest

function Assert-SuiteVersionName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$VersionName,
        [Parameter(Mandatory = $true)][ValidateSet('stable', 'test')][string]$Channel
    )

    if ($VersionName -notmatch '^\d{4}\.\d{2}\.\d{2}\.\d+-friends(?:-open-v\d+)?(?:-test)?$') {
        throw 'versionName format must be YYYY.MM.DD.N-friends[-open-vN][-test].'
    }
    $hasTestSuffix = $VersionName.EndsWith('-test', [StringComparison]::Ordinal)
    if (($Channel -eq 'test') -ne $hasTestSuffix) {
        throw 'The test channel requires a -test versionName suffix; stable names must not use it.'
    }
}

function Get-SuiteReleaseChannel {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$VersionName)

    if ($VersionName.EndsWith('-test', [StringComparison]::Ordinal)) { return 'test' }
    return 'stable'
}

function Resolve-SuiteAndroidTools {
    [CmdletBinding()]
    param(
        [string]$AndroidSdkPath,
        [string]$JdkPath
    )

    $sdkCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($AndroidSdkPath)) { $sdkCandidates += $AndroidSdkPath }
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_SDK_ROOT)) { $sdkCandidates += $env:ANDROID_SDK_ROOT }
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) { $sdkCandidates += $env:ANDROID_HOME }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $sdkCandidates += Join-Path $env:LOCALAPPDATA 'Android\Sdk'
    }

    $sdkRoot = $null
    foreach ($candidate in ($sdkCandidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'build-tools') -PathType Container) {
            $sdkRoot = [IO.Path]::GetFullPath($candidate)
            break
        }
    }
    if (-not $sdkRoot) { throw 'Android SDK build-tools were not found. Set ANDROID_SDK_ROOT or pass -AndroidSdkPath.' }

    $buildToolsRoot = Join-Path $sdkRoot 'build-tools'
    $buildToolDirs = @(Get-ChildItem -LiteralPath $buildToolsRoot -Directory -ErrorAction Stop | Sort-Object {
        try { [version]$_.Name } catch { [version]'0.0' }
    } -Descending)
    $selectedBuildTools = $null
    foreach ($dir in $buildToolDirs) {
        $aapt = Join-Path $dir.FullName 'aapt.exe'
        $apksignerJar = Join-Path $dir.FullName 'lib\apksigner.jar'
        if ((Test-Path -LiteralPath $aapt -PathType Leaf) -and (Test-Path -LiteralPath $apksignerJar -PathType Leaf)) {
            $selectedBuildTools = $dir.FullName
            break
        }
    }
    if (-not $selectedBuildTools) { throw 'Android build-tools must contain aapt.exe and lib\apksigner.jar.' }

    $javaCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($JdkPath)) {
        $javaCandidates += (Join-Path $JdkPath 'bin\java.exe')
        if ((Split-Path -Leaf $JdkPath) -ieq 'java.exe') { $javaCandidates += $JdkPath }
    }
    if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) { $javaCandidates += (Join-Path $env:JAVA_HOME 'bin\java.exe') }
    $javaCommand = Get-Command java.exe -ErrorAction SilentlyContinue
    if ($javaCommand) { $javaCandidates += $javaCommand.Source }

    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $codexToolsRoot = Join-Path $env:USERPROFILE '.codex\tools'
        if (Test-Path -LiteralPath $codexToolsRoot -PathType Container) {
            foreach ($toolDir in (Get-ChildItem -LiteralPath $codexToolsRoot -Directory -ErrorAction SilentlyContinue)) {
                if ($toolDir.Name -match 'jdk|java|temurin|portable') {
                    $javaCandidates += Join-Path $toolDir.FullName 'bin\java.exe'
                    foreach ($child in (Get-ChildItem -LiteralPath $toolDir.FullName -Directory -ErrorAction SilentlyContinue)) {
                        $javaCandidates += Join-Path $child.FullName 'bin\java.exe'
                    }
                }
            }
        }
    }

    $javaExe = $null
    foreach ($candidate in ($javaCandidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $javaExe = [IO.Path]::GetFullPath($candidate)
            break
        }
    }
    if (-not $javaExe) { throw 'A JDK was not found. Set JAVA_HOME or pass -JdkPath.' }

    return [pscustomobject]@{
        SdkRoot = $sdkRoot
        BuildToolsRoot = $selectedBuildTools
        AaptPath = Join-Path $selectedBuildTools 'aapt.exe'
        ApksignerJar = Join-Path $selectedBuildTools 'lib\apksigner.jar'
        JavaPath = $javaExe
    }
}

function Get-SuiteApkMetadata {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ApkPath,
        [string]$AndroidSdkPath,
        [string]$JdkPath
    )

    $apk = (Resolve-Path -LiteralPath $ApkPath -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) { throw 'APK path must point to a file.' }
    if ([IO.Path]::GetExtension($apk) -ine '.apk') { throw 'APK input must have the .apk extension.' }
    $tools = Resolve-SuiteAndroidTools -AndroidSdkPath $AndroidSdkPath -JdkPath $JdkPath

    # aapt.exe handles some Windows Unicode paths poorly. Stage a temporary ASCII-named copy,
    # then remove it in finally; the selected APK is only read and is never rewritten.
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $tempDir = Join-Path $tempBase ('suite-apk-inspect-' + [guid]::NewGuid().ToString('N'))
    $resolvedTempDir = [IO.Path]::GetFullPath($tempDir)
    if (-not $resolvedTempDir.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Temporary inspection path escaped the system temporary directory.'
    }
    New-Item -ItemType Directory -Path $resolvedTempDir -ErrorAction Stop | Out-Null
    $stagedApk = Join-Path $resolvedTempDir 'input.apk'
    try {
        [IO.File]::Copy($apk, $stagedApk, $false)

        $aaptOutput = @(& $tools.AaptPath dump badging $stagedApk 2>&1)
        $aaptExitCode = $LASTEXITCODE
        $aaptText = (@($aaptOutput | ForEach-Object { $_.ToString() }) -join "`n")
        if ($aaptExitCode -ne 0) { throw 'aapt failed to read APK package metadata.' }

        $packageMatch = [regex]::Match($aaptText, "(?m)^package:\s+name='([^']+)'\s+versionCode='(\d+)'\s+versionName='([^']*)'")
        if (-not $packageMatch.Success) { throw 'aapt output did not contain package name, versionCode, and versionName.' }
        $versionCode = [long]0
        if (-not [long]::TryParse($packageMatch.Groups[2].Value, [ref]$versionCode) -or $versionCode -le 0) {
            throw 'APK versionCode is missing or invalid.'
        }

        $minSdkMatch = [regex]::Match($aaptText, "(?m)^sdkVersion:'([^']+)'")
        $targetSdkMatch = [regex]::Match($aaptText, "(?m)^targetSdkVersion:'([^']+)'")
        $abis = @()
        foreach ($nativeLine in [regex]::Matches($aaptText, "(?m)^native-code:\s*(.*)$")) {
            foreach ($abiMatch in [regex]::Matches($nativeLine.Groups[1].Value, "'([^']+)'")) {
                $abis += $abiMatch.Groups[1].Value
            }
        }
        $abis = @($abis | Sort-Object -Unique)

        $signerOutput = @(& $tools.JavaPath -jar $tools.ApksignerJar verify --print-certs $stagedApk 2>&1)
        $signerExitCode = $LASTEXITCODE
        $signerText = (@($signerOutput | ForEach-Object { $_.ToString() }) -join "`n")
        if ($signerExitCode -ne 0) { throw 'apksigner failed to verify the APK.' }
        $certificateDigests = @([regex]::Matches($signerText, '(?m)^Signer #\d+ certificate SHA-256 digest:\s*([0-9a-fA-F]+)') | ForEach-Object {
            $_.Groups[1].Value.ToUpperInvariant()
        } | Sort-Object -Unique)
        if ($certificateDigests.Count -eq 0) { throw 'apksigner did not report a signer SHA-256 certificate digest.' }

        $fileInfo = Get-Item -LiteralPath $apk -ErrorAction Stop
        $sha256 = (Get-FileHash -LiteralPath $apk -Algorithm SHA256 -ErrorAction Stop).Hash.ToUpperInvariant()
        return [pscustomobject]@{
            FileName = [IO.Path]::GetFileName($apk)
            FileBytes = [long]$fileInfo.Length
            Sha256 = $sha256
            PackageName = $packageMatch.Groups[1].Value
            VersionCode = $versionCode
            VersionName = $packageMatch.Groups[3].Value
            Abis = $abis
            MinSdk = if ($minSdkMatch.Success -and $minSdkMatch.Groups[1].Value -match '^\d+$') { [int]$minSdkMatch.Groups[1].Value } else { $null }
            TargetSdk = if ($targetSdkMatch.Success -and $targetSdkMatch.Groups[1].Value -match '^\d+$') { [int]$targetSdkMatch.Groups[1].Value } else { $null }
            SigningCertificateSha256 = $certificateDigests
        }
    }
    finally {
        if (Test-Path -LiteralPath $resolvedTempDir -PathType Container) {
            $checkPath = [IO.Path]::GetFullPath($resolvedTempDir)
            if ($checkPath.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and
                (Split-Path -Leaf $checkPath) -like 'suite-apk-inspect-*') {
                Remove-Item -LiteralPath $checkPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

function Assert-SuiteApkCompatibility {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Candidate,
        [Parameter(Mandatory = $true)]$Baseline
    )

    if ($Candidate.PackageName -cne $Baseline.PackageName) {
        throw 'Candidate applicationId does not match the baseline APK.'
    }
    if ([long]$Candidate.VersionCode -le [long]$Baseline.VersionCode) {
        throw 'Candidate versionCode must be greater than the baseline versionCode.'
    }
    $candidateSigners = @($Candidate.SigningCertificateSha256 | Sort-Object -Unique) -join ','
    $baselineSigners = @($Baseline.SigningCertificateSha256 | Sort-Object -Unique) -join ','
    if ($candidateSigners -cne $baselineSigners) {
        throw 'Candidate signing certificate set does not match the baseline APK.'
    }
    if ($null -eq $Candidate.MinSdk -or $null -eq $Baseline.MinSdk) {
        throw 'Both APKs must report minSdk before delivery compatibility can be checked.'
    }
    if ([int]$Candidate.MinSdk -gt [int]$Baseline.MinSdk) {
        throw 'Candidate minSdk is higher than the baseline and may block an in-place update.'
    }

    $baselineAbis = @($Baseline.Abis | Sort-Object -Unique)
    $candidateAbis = @($Candidate.Abis | Sort-Object -Unique)
    if ($baselineAbis.Count -eq 0 -or $candidateAbis.Count -eq 0) {
        throw 'Both APKs must report at least one native ABI.'
    }
    foreach ($abi in $baselineAbis) {
        if ($candidateAbis -cnotcontains $abi) { throw "Candidate APK no longer includes baseline ABI '$abi'." }
    }

    $channel = Get-SuiteReleaseChannel -VersionName $Candidate.VersionName
    Assert-SuiteVersionName -VersionName $Candidate.VersionName -Channel $channel
}

function Assert-SuiteDeliveryPaths {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ApkPath,
        [Parameter(Mandatory = $true)][string]$BaselineApk,
        [Parameter(Mandatory = $true)][string]$OutputDirectory
    )

    $apk = (Resolve-Path -LiteralPath $ApkPath -ErrorAction Stop).Path
    $baseline = (Resolve-Path -LiteralPath $BaselineApk -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $apk -PathType Leaf) -or [IO.Path]::GetExtension($apk) -ine '.apk') {
        throw 'Candidate path must be an existing .apk file.'
    }
    if (-not (Test-Path -LiteralPath $baseline -PathType Leaf) -or [IO.Path]::GetExtension($baseline) -ine '.apk') {
        throw 'Baseline path must be an existing .apk file.'
    }
    if ([IO.Path]::GetFullPath($apk) -ieq [IO.Path]::GetFullPath($baseline)) {
        throw 'Candidate and baseline must be different files.'
    }

    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $output) { throw 'OutputDirectory already exists; refusing to overwrite it.' }
    $parent = Split-Path -Parent $output
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw 'OutputDirectory parent must already exist.'
    }
    return [pscustomobject]@{ ApkPath = [IO.Path]::GetFullPath($apk); BaselineApk = [IO.Path]::GetFullPath($baseline); OutputDirectory = $output }
}

function Get-SuiteGitProvenance {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$SourceRoot)

    $gitCommand = Get-Command git.exe -ErrorAction SilentlyContinue
    if (-not $gitCommand) { throw 'Git is required to record the source commit and dirty state.' }
    $commitLines = @(& $gitCommand.Source -C $SourceRoot rev-parse --verify HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $commitLines.Count -eq 0) { throw 'Could not determine the source Git commit.' }
    $statusLines = @(& $gitCommand.Source -C $SourceRoot status --porcelain --untracked-files=all -- . 2>$null)
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine whether source files are dirty.' }
    return [pscustomobject]@{
        Commit = ([string]$commitLines[0]).Trim()
        Dirty = ($statusLines.Count -gt 0)
    }
}

function Get-SuiteSourceDigest {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$SourceRoot)

    $root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourceRoot -ErrorAction Stop).Path).TrimEnd('\')
    $allowedExtensions = @('.gradle', '.java', '.kt', '.rs', '.toml', '.json', '.xml', '.lua', '.ps1', '.psm1', '.md', '.txt', '.c', '.h', '.cpp', '.cmake', '.properties', '.lock', '.yml', '.yaml')
    $excludedSegments = @('.git', '.gradle', '.scratch', 'artifacts', 'build', 'target', 'bin', 'obj', 'node_modules', '.externalNativeBuild')
    $files = @(Get-ChildItem -LiteralPath $root -File -Recurse -Force -ErrorAction Stop | Where-Object {
        if ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }
        if ($allowedExtensions -notcontains $_.Extension.ToLowerInvariant()) { return $false }
        $relative = $_.FullName.Substring($root.Length).TrimStart('\', '/')
        $segments = $relative -split '[\\/]'
        foreach ($segment in $segments) {
            if ($excludedSegments -contains $segment) { return $false }
        }
        if ($_.Name -in @('local.properties', '.env', '.env.local', 'keystore.properties')) { return $false }
        return $true
    } | Sort-Object { $_.FullName.Substring($root.Length).Replace('\', '/') })
    if ($files.Count -eq 0) { throw 'No source files were found for the source digest.' }

    $digestLines = foreach ($file in $files) {
        $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
        $fileHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256 -ErrorAction Stop).Hash.ToUpperInvariant()
        "$relative`t$fileHash"
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($digestLines -join "`n"))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '') }
    finally { $sha.Dispose() }
}

Export-ModuleMember -Function Assert-SuiteVersionName, Get-SuiteReleaseChannel, Resolve-SuiteAndroidTools, Get-SuiteApkMetadata, Assert-SuiteApkCompatibility, Assert-SuiteDeliveryPaths, Get-SuiteGitProvenance, Get-SuiteSourceDigest
