[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApkPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$BaselineApk,
    [switch]$AnalysisOnly,
    [ValidateSet('unverified', 'pending_schedule_merge', 'merged')][string]$IntegrationStatus = 'unverified',
    [string]$AndroidSdkPath,
    [string]$JdkPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SuiteDelivery.Common.psm1') -Force

if (-not $AnalysisOnly -and [string]::IsNullOrWhiteSpace($BaselineApk)) {
    throw 'A baseline APK is required to create a delivery. Use -AnalysisOnly for read-only inspection without a baseline.'
}

$candidate = Get-SuiteApkMetadata -ApkPath $ApkPath -AndroidSdkPath $AndroidSdkPath -JdkPath $JdkPath
if ($AnalysisOnly) {
    $report = [ordered]@{
        analysisOnly = $true
        candidate = $candidate
    }
    if (-not [string]::IsNullOrWhiteSpace($BaselineApk)) {
        $baseline = Get-SuiteApkMetadata -ApkPath $BaselineApk -AndroidSdkPath $AndroidSdkPath -JdkPath $JdkPath
        $compatibility = [ordered]@{ compatible = $false; reason = $null }
        try {
            Assert-SuiteApkCompatibility -Candidate $candidate -Baseline $baseline
            $compatibility.compatible = $true
        }
        catch {
            $compatibility.reason = $_.Exception.Message
        }
        $report.baseline = $baseline
        $report.compatibility = $compatibility
    }
    $report | ConvertTo-Json -Depth 8
    return
}

$paths = Assert-SuiteDeliveryPaths -ApkPath $ApkPath -BaselineApk $BaselineApk -OutputDirectory $OutputDirectory
$baseline = Get-SuiteApkMetadata -ApkPath $paths.BaselineApk -AndroidSdkPath $AndroidSdkPath -JdkPath $JdkPath
Assert-SuiteApkCompatibility -Candidate $candidate -Baseline $baseline
$releaseChannel = Get-SuiteReleaseChannel -VersionName $candidate.VersionName

$sourceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..') -ErrorAction Stop).Path
$git = Get-SuiteGitProvenance -SourceRoot $sourceRoot
$sourceDigest = Get-SuiteSourceDigest -SourceRoot $sourceRoot
$outputPath = $paths.OutputDirectory
$outputParent = Split-Path -Parent $outputPath
$stagePath = Join-Path $outputParent ('.suite-delivery-stage-' + [guid]::NewGuid().ToString('N'))
$resolvedStagePath = [IO.Path]::GetFullPath($stagePath)
$resolvedParent = [IO.Path]::GetFullPath($outputParent).TrimEnd('\') + '\'
if (-not $resolvedStagePath.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path -Leaf $resolvedStagePath) -notlike '.suite-delivery-stage-*') {
    throw 'Temporary delivery staging path failed its path-safety check.'
}

$apkName = "PiaomiaoHelper-$($candidate.VersionName)-code$($candidate.VersionCode).apk"
$shaName = "$apkName.sha256"
$resolvedApkName = Join-Path $resolvedStagePath $apkName
$resolvedManifestName = Join-Path $resolvedStagePath 'manifest.json'
$resolvedShaName = Join-Path $resolvedStagePath $shaName
$resolvedReadmeName = Join-Path $resolvedStagePath '安装说明.txt'
New-Item -ItemType Directory -Path $resolvedStagePath -ErrorAction Stop | Out-Null

try {
    [IO.File]::Copy($paths.ApkPath, $resolvedApkName, $false)
    $copiedHash = (Get-FileHash -LiteralPath $resolvedApkName -Algorithm SHA256 -ErrorAction Stop).Hash.ToUpperInvariant()
    if ($copiedHash -cne $candidate.Sha256) { throw 'APK changed while it was being copied; no delivery was created.' }

    $manifest = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        deliveryState = 'prepared_not_installed_not_published'
        application = [ordered]@{
            packageName = $candidate.PackageName
            versionCode = $candidate.VersionCode
            versionName = $candidate.VersionName
            releaseChannel = $releaseChannel
            abis = @($candidate.Abis)
            minSdk = $candidate.MinSdk
            targetSdk = $candidate.TargetSdk
            apkFileName = $apkName
            sizeBytes = $candidate.FileBytes
            sha256 = $candidate.Sha256
            signingCertificateSha256 = @($candidate.SigningCertificateSha256)
        }
        baseline = [ordered]@{
            apkFileName = $baseline.FileName
            sizeBytes = $baseline.FileBytes
            sha256 = $baseline.Sha256
            packageName = $baseline.PackageName
            versionCode = $baseline.VersionCode
            versionName = $baseline.VersionName
            abis = @($baseline.Abis)
            minSdk = $baseline.MinSdk
            signingCertificateSha256 = @($baseline.SigningCertificateSha256)
        }
        source = [ordered]@{
            gitCommit = $git.Commit
            dirty = $git.Dirty
            sourceDigestAlgorithm = 'SHA-256'
            sourceDigestSha256 = $sourceDigest
            sourceDigestScope = 'Source and project documentation files; excludes build outputs, APK artifacts, local.properties, and secret-store files.'
        }
        acceptance = [ordered]@{
            integrationStatus = $IntegrationStatus
            deviceAcceptance = 'not_run'
            installed = $false
            published = $false
            updatePackageConditions = 'package, signing certificates, minSdk, ABI coverage, and increasing versionCode passed against the supplied baseline; feature and app-data compatibility are not established.'
        }
    }

    $json = $manifest | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($resolvedManifestName, $json + "`n", (New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText($resolvedShaName, "$($candidate.Sha256) *$apkName`n", (New-Object Text.UTF8Encoding($false)))
    $readme = @(
        '飘渺助手 APK 交付说明',
        '',
        "APK：$apkName",
        "包名：$($candidate.PackageName)",
        "版本：$($candidate.VersionName)（versionCode $($candidate.VersionCode)）",
        "SHA-256：$($candidate.Sha256)",
        '',
        "集成状态：$IntegrationStatus；设备验收：未执行。当前检查仅确认与提供的 baseline 在 Android 包名、签名证书、minSdk、ABI 覆盖和版本递增方面满足更新包条件；功能及应用数据兼容性尚未确认，因此当前不建议据此覆盖安装。",
        '',
        '待集成和设备验收完成后，如确认可安装：',
        '1. 将 APK 复制到 Android 设备。',
        '2. 打开 APK，按系统提示允许本次来源安装应用。',
        '3. 确认系统显示的是更新现有应用，再继续安装；不要先卸载旧应用。',
        '4. 安装后按设备验收记录检查启动、授权和目标功能。',
        '',
        '本交付尚未安装、未发布；请用同目录 manifest.json 与 SHA-256 文件核对文件。'
    ) -join "`r`n"
    [IO.File]::WriteAllText($resolvedReadmeName, $readme + "`r`n", (New-Object Text.UTF8Encoding($false)))

    if (Test-Path -LiteralPath $outputPath) { throw 'OutputDirectory appeared during delivery; refusing to overwrite it.' }
    [IO.Directory]::Move($resolvedStagePath, $outputPath)
    Write-Output "Delivery created: $outputPath"
    Write-Output "APK: $(Join-Path $outputPath $apkName)"
    Write-Output "SHA-256: $($candidate.Sha256)"
    Write-Output 'Installed: false; published: false; deviceAcceptance: not_run'
}
finally {
    if (Test-Path -LiteralPath $resolvedStagePath -PathType Container) {
        $checkStage = [IO.Path]::GetFullPath($resolvedStagePath)
        if ($checkStage.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $checkStage) -like '.suite-delivery-stage-*') {
            Remove-Item -LiteralPath $checkStage -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
