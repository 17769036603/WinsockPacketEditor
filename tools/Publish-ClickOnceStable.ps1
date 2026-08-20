[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$Version,
    [string]$Endpoint,
    [string]$ReleaseRoot,
    [string]$StagingRoot,
    [string]$CertificateHash,
    [string]$CertificateSubject = "CN=小黑封包助手 本机 ClickOnce",
    [switch]$SignManifests,
    [switch]$Unsigned,
    [switch]$Preview
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "路径不存在：$Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Read-Utf8Text {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Write-Utf8Text {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($true)))
}

function Get-VersionFromName {
    param([Parameter(Mandatory = $true)][string]$Name)

    $parsed = $null
    if ([System.Version]::TryParse($Name, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Get-NextReleaseVersion {
    param([Parameter(Mandatory = $true)][string]$Root)

    $today = Get-Date
    $prefix = "{0}.{1}.{2}." -f $today.Year, $today.Month, $today.Day
    $versions = @(
        Get-ChildItem -LiteralPath $Root -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                $parsed = Get-VersionFromName $_.Name
                if ($null -ne $parsed) { $parsed }
            }
    )

    $sameDay = @($versions | Where-Object { $_.ToString().StartsWith($prefix, [System.StringComparison]::Ordinal) })
    $nextRevision = 0
    if ($sameDay.Count -gt 0) {
        $nextRevision = (($sameDay | Measure-Object -Property Revision -Maximum).Maximum + 1)
    }

    return "{0}.{1}.{2}.{3}" -f $today.Year, $today.Month, $today.Day, $nextRevision
}

function Update-VersionText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    $updated = $Text
    if ($Path.EndsWith("AssemblyInfo.cs", [System.StringComparison]::OrdinalIgnoreCase)) {
        $updated = [regex]::Replace($updated, '(?m)^(\s*\[assembly:\s+AssemblyVersion\(")[^"]+("\)\])', '${1}' + $NewVersion + '${2}')
        $updated = [regex]::Replace($updated, '(?m)^(\s*\[assembly:\s+AssemblyFileVersion\(")[^"]+("\)\])', '${1}' + $NewVersion + '${2}')
    }
    elseif ($Path.EndsWith("WinsockPacketEditor.csproj", [System.StringComparison]::OrdinalIgnoreCase)) {
        $updated = [regex]::Replace($updated, '(?m)(<ApplicationVersion>)[^<]+(</ApplicationVersion>)', '${1}' + $NewVersion + '${2}')
    }
    elseif ($Path.EndsWith("app.manifest", [System.StringComparison]::OrdinalIgnoreCase)) {
        $updated = [regex]::Replace($updated, '(?m)(<assemblyIdentity\s+version=")[^"]+("\s+name="WinsockPacketEditor"\s*/>)', '${1}' + $NewVersion + '${2}')
    }
    else {
        throw "不支持自动更新版本的文件：$Path"
    }

    if ($updated -eq $Text) {
        throw "未能在文件中找到版本字段：$Path"
    }

    return $updated
}

function Find-MSBuild {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
        (Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
        (Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe")
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "找不到 VS 2022 MSBuild.exe。"
}

function Find-Mage {
    $candidates = @(
        "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\mage.exe",
        "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64\mage.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command mage.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "找不到 mage.exe，无法签名 ClickOnce 固定入口。"
}

function Assert-RequiredFiles {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $required = @(
        "小黑封包助手.exe",
        "小黑封包助手.exe.manifest",
        "小黑封包助手.application",
        "WPELibrary.dll",
        "EasyHook32.dll",
        "EasyHook64.dll",
        "EasyLoad32.dll",
        "EasyLoad64.dll",
        "EasyHook32Svc.exe",
        "EasyHook64Svc.exe",
        "System.Data.SQLite.dll"
    )

    foreach ($name in $required) {
        $path = Join-Path $Directory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "发布目录缺少必要文件：$name"
        }
    }
}

function Get-Sha256Base64 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToBase64String($sha.ComputeHash([System.IO.File]::ReadAllBytes($Path)))
    }
    finally {
        $sha.Dispose()
    }
}

function Ensure-ManifestFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    [xml]$manifest = Read-Utf8Text $ManifestPath
    $fileNodes = @($manifest.SelectNodes("//*[local-name()='file']"))
    foreach ($fileNode in $fileNodes) {
        $relativePath = $fileNode.GetAttribute("name").Replace('/', '\')
        if ([string]::IsNullOrWhiteSpace($relativePath) -or [System.IO.Path]::IsPathRooted($relativePath) -or $relativePath -match '(^|\\)\.\.(\\|$)') {
            throw "应用清单包含不安全文件路径：$relativePath"
        }

        $targetPath = Join-Path $Directory $relativePath
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            $sourcePath = Join-Path $SourceDirectory $relativePath
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                throw "应用清单声明的文件不存在：$relativePath"
            }
            $targetParent = Split-Path -Parent $targetPath
            if (-not (Test-Path -LiteralPath $targetParent)) {
                New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
            }
            Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
        }

        $declaredSize = $fileNode.GetAttribute("size")
        if (-not [string]::IsNullOrWhiteSpace($declaredSize)) {
            $actualSize = (Get-Item -LiteralPath $targetPath).Length
            if ($actualSize -ne [int64]$declaredSize) {
                throw "应用清单文件大小不匹配：$relativePath"
            }
        }

        $digestNode = $fileNode.SelectSingleNode(".//*[local-name()='DigestValue']")
        if ($null -ne $digestNode -and $digestNode.InnerText -ne (Get-Sha256Base64 $targetPath)) {
            throw "应用清单文件 SHA-256 不匹配：$relativePath"
        }
    }
}

function Ensure-DeploymentMappedFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$DeploymentManifestPath,
        [Parameter(Mandatory = $true)][string]$ApplicationManifestPath
    )

    [xml]$deploymentManifest = Read-Utf8Text $DeploymentManifestPath
    $deployment = $deploymentManifest.SelectSingleNode("//*[local-name()='deployment']")
    if ($null -eq $deployment -or $deployment.GetAttribute('mapFileExtensions') -ne 'true') {
        return
    }

    [xml]$applicationManifest = Read-Utf8Text $ApplicationManifestPath
    $relativePaths = @(
        @($applicationManifest.SelectNodes("//*[local-name()='file']")) |
            ForEach-Object { $_.GetAttribute('name') }
        @($applicationManifest.SelectNodes("//*[local-name()='dependentAssembly' and @codebase]")) |
            ForEach-Object { $_.GetAttribute('codebase') }
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($relativePathValue in $relativePaths) {
        $relativePath = $relativePathValue.Replace('/', '\')
        if ([System.IO.Path]::IsPathRooted($relativePath) -or
            $relativePath -eq '..' -or
            $relativePath.StartsWith('..' + '\') -or
            $relativePath.Contains('\' + '..' + '\') -or
            $relativePath.EndsWith('\' + '..')) {
            throw "ClickOnce 清单包含不安全映射路径：$relativePath"
        }

        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }

        $sourcePath = Join-Path $Directory $relativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "无法为 ClickOnce 映射文件准备源文件：$relativePath"
        }

        $mappedPath = $sourcePath + '.deploy'
        if (-not (Test-Path -LiteralPath $mappedPath -PathType Leaf)) {
            Copy-Item -LiteralPath $sourcePath -Destination $mappedPath -Force
        }
    }
}

function Update-DeploymentProvider {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ProviderUrl,
        [switch]$PrepareForSigning
    )

    $content = Read-Utf8Text $Path
    if ($content.Contains("<dsig:Signature")) {
        if (-not $PrepareForSigning) {
            throw "当前清单已签名，不能在未签名模式下改写部署入口。请使用 -SignManifests。"
        }
        $content = [regex]::Replace($content, '(?s)\s*<dsig:Signature.*?</dsig:Signature>\s*', "`r`n")
    }

    $updated = [regex]::Replace(
        $content,
        '(?m)<deploymentProvider\s+codebase="[^"]*"\s*/>',
        '<deploymentProvider codebase="' + $ProviderUrl + '" />',
        1
    )
    if ($updated -eq $content) {
        throw "部署清单中未找到 deploymentProvider：$Path"
    }

    Write-Utf8Text -Path $Path -Text $updated
}

function Update-DeploymentManifestDependency {
    param(
        [Parameter(Mandatory = $true)][string]$DeploymentManifestPath,
        [Parameter(Mandatory = $true)][string]$ApplicationManifestPath
    )

    [xml]$applicationManifest = Read-Utf8Text $ApplicationManifestPath
    $applicationIdentity = $applicationManifest.SelectSingleNode("/*[local-name()='assembly']/*[local-name()='assemblyIdentity']")
    if ($null -eq $applicationIdentity) {
        throw "应用清单缺少根 assemblyIdentity：$ApplicationManifestPath"
    }

    $applicationBytes = [System.IO.File]::ReadAllBytes($ApplicationManifestPath)
    $applicationSize = $applicationBytes.Length
    $applicationDigest = Get-Sha256Base64 $ApplicationManifestPath
    [xml]$deploymentManifest = Read-Utf8Text $DeploymentManifestPath
    $dependency = $deploymentManifest.SelectSingleNode("//*[local-name()='dependentAssembly' and @dependencyType='install']")
    if ($null -eq $dependency) {
        throw "部署清单缺少应用清单依赖：$DeploymentManifestPath"
    }

    $dependencyIdentity = $dependency.SelectSingleNode("./*[local-name()='assemblyIdentity']")
    if ($null -eq $dependencyIdentity) {
        throw "部署清单依赖缺少 assemblyIdentity：$DeploymentManifestPath"
    }

    foreach ($attributeName in @('name', 'version', 'language', 'publicKeyToken', 'processorArchitecture', 'type')) {
        $dependencyIdentity.SetAttribute($attributeName, $applicationIdentity.GetAttribute($attributeName))
    }
    $dependency.SetAttribute('size', [string]$applicationSize)

    $digestNode = $dependency.SelectSingleNode(".//*[local-name()='DigestValue']")
    if ($null -eq $digestNode) {
        throw "部署清单依赖缺少应用清单 SHA-256：$DeploymentManifestPath"
    }
    $digestNode.InnerText = $applicationDigest

    Write-Utf8Text -Path $DeploymentManifestPath -Text $deploymentManifest.OuterXml
}

function New-StableDeploymentManifest {
    param(
        [Parameter(Mandatory = $true)][string]$VersionManifestPath,
        [Parameter(Mandatory = $true)][string]$StableManifestPath,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ProviderUrl,
        [switch]$PrepareForSigning
    )

    $content = Read-Utf8Text $VersionManifestPath
    if ($content.Contains("<dsig:Signature")) {
        if (-not $PrepareForSigning) {
            throw "当前清单已签名，不能在未签名模式下改写固定入口。请使用 -SignManifests。"
        }
        $content = [regex]::Replace($content, '(?s)\s*<dsig:Signature.*?</dsig:Signature>\s*', "`r`n")
    }

    $content = [regex]::Replace(
        $content,
        '(?m)(<assemblyIdentity\s+name="小黑封包助手\.application"\s+version=")[^"]+("[^>]*>)',
        '${1}' + $Version + '${2}',
        1
    )
    $content = [regex]::Replace(
        $content,
        '(?m)(<dependentAssembly\s+dependencyType="install"\s+codebase=")[^"]+("[^>]*>)',
        '${1}' + $Version + '/小黑封包助手.exe.manifest${2}',
        1
    )
    $content = [regex]::Replace(
        $content,
        '(?m)(<assemblyIdentity\s+name="小黑封包助手\.exe"\s+version=")[^"]+("[^>]*>)',
        '${1}' + $Version + '${2}',
        1
    )
    $content = [regex]::Replace(
        $content,
        '(?m)<deploymentProvider\s+codebase="[^"]*"\s*/>',
        '<deploymentProvider codebase="' + $ProviderUrl + '" />',
        1
    )

    Write-Utf8Text -Path $StableManifestPath -Text $content
}

$repo = Resolve-FullPath $RepositoryRoot
$projectPath = Join-Path $repo "WinsockPacketEditor\WinsockPacketEditor.csproj"
$solutionPath = Join-Path $repo "WinSockPacketEditor.sln"
$releaseRootPath = if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) { Join-Path $repo "releases" } else { $ReleaseRoot }
$stagingRootPath = if ([string]::IsNullOrWhiteSpace($StagingRoot)) { Join-Path $repo "work\ClickOncePublish" } else { $StagingRoot }

if (-not (Test-Path -LiteralPath $releaseRootPath)) {
    New-Item -ItemType Directory -Path $releaseRootPath -Force | Out-Null
}
if (-not (Test-Path -LiteralPath $stagingRootPath)) {
    New-Item -ItemType Directory -Path $stagingRootPath -Force | Out-Null
}

$releaseRootPath = (Resolve-Path -LiteralPath $releaseRootPath).Path
$stagingRootPath = (Resolve-Path -LiteralPath $stagingRootPath).Path
$version = if ([string]::IsNullOrWhiteSpace($Version)) { Get-NextReleaseVersion $releaseRootPath } else { $Version }
$parsedVersion = Get-VersionFromName $version
if ($null -eq $parsedVersion -or $parsedVersion.Revision -lt 0) {
    throw "版本号必须是四段数字，例如 2026.8.3.2：$version"
}

$endpointBase = if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    $localRootUri = New-Object -TypeName System.Uri -ArgumentList ($releaseRootPath.TrimEnd('\') + '\')
    $localRootUri.AbsoluteUri
}
else {
    $Endpoint.TrimEnd('/') + '/'
}
$providerUrl = $endpointBase + [Uri]::EscapeDataString("小黑封包助手.application")
$buildInstallUrl = if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    "https://www.wpe64.com/Downloads/Releases/"
}
else {
    $endpointBase
}
$signManifestsEnabled = ($SignManifests.IsPresent -or [string]::IsNullOrWhiteSpace($Endpoint)) -and -not $Unsigned.IsPresent
if ($SignManifests.IsPresent -and $Unsigned.IsPresent) {
    throw "-SignManifests 与 -Unsigned 不能同时使用。"
}
$certificateHashValue = $CertificateHash
if ($signManifestsEnabled) {
    $certificates = @(Get-ChildItem -Path "Cert:\CurrentUser\My" -ErrorAction SilentlyContinue)
    $projectText = Read-Utf8Text $projectPath
    $certificateMatch = [regex]::Match($projectText, '(?m)<ManifestCertificateThumbprint>([^<]+)</ManifestCertificateThumbprint>')
    if ([string]::IsNullOrWhiteSpace($certificateHashValue) -and $certificateMatch.Success) {
        $projectCertificateHash = $certificateMatch.Groups[1].Value.Trim()
        $projectCertificate = $certificates | Where-Object { $_.Thumbprint -eq $projectCertificateHash -and $_.HasPrivateKey } | Select-Object -First 1
        if ($null -ne $projectCertificate) {
            $certificateHashValue = $projectCertificate.Thumbprint
        }
    }
    if ([string]::IsNullOrWhiteSpace($certificateHashValue) -and [string]::IsNullOrWhiteSpace($Endpoint)) {
        $localCertificate = $certificates |
            Where-Object { $_.Subject -eq $CertificateSubject -and $_.HasPrivateKey } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if ($null -ne $localCertificate) {
            $certificateHashValue = $localCertificate.Thumbprint
        }
    }
    if ([string]::IsNullOrWhiteSpace($certificateHashValue)) {
        throw "未找到可用于 ClickOnce 签名的证书。请在当前 Windows 用户的个人证书存储中准备证书，或传入 -CertificateHash；证书本身不要提交到仓库。"
    }
    $selectedCertificate = $certificates | Where-Object { $_.Thumbprint -eq $certificateHashValue -and $_.HasPrivateKey } | Select-Object -First 1
    if ($null -eq $selectedCertificate) {
        throw "ClickOnce 证书不在当前 Windows 用户的个人证书存储中，或缺少私钥：$certificateHashValue"
    }
}
$finalReleasePath = Join-Path $releaseRootPath $version
$stableManifestPath = Join-Path $releaseRootPath "小黑封包助手.application"
$stagePath = Join-Path $stagingRootPath $version
$transactionPath = Join-Path $releaseRootPath (".staging-{0}-{1}" -f $version, $PID)

if (Test-Path -LiteralPath $finalReleasePath) {
    throw "目标版本目录已存在，不覆盖已有发布：$finalReleasePath"
}
if (Test-Path -LiteralPath $stagePath) {
    throw "临时构建目录已存在，请先检查或移除本次任务留下的目录：$stagePath"
}

$versionFiles = @(
    (Join-Path $repo "WinsockPacketEditor\Properties\AssemblyInfo.cs"),
    (Join-Path $repo "WPELibrary\Properties\AssemblyInfo.cs"),
    $projectPath,
    (Join-Path $repo "WinsockPacketEditor\Properties\app.manifest")
)
$originalBytes = @{}
foreach ($path in $versionFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "版本文件不存在：$path"
    }
    $originalBytes[$path] = [System.IO.File]::ReadAllBytes($path)
}

if ($Preview) {
    [pscustomobject]@{
        Version = $version
        ProviderUrl = $providerUrl
        ReleaseDirectory = $finalReleasePath
        StageDirectory = $stagePath
        MSBuild = (Find-MSBuild)
        SignManifests = $signManifestsEnabled
    } | Format-List
    return
}

$published = $false
try {
    foreach ($path in $versionFiles) {
        $text = Read-Utf8Text $path
        Write-Utf8Text -Path $path -Text (Update-VersionText -Path $path -Text $text -NewVersion $version)
    }

    New-Item -ItemType Directory -Path $stagePath -Force | Out-Null
    $msbuild = Find-MSBuild
    # MSBuild 会把 SignManifests 传播到解决方案中的第三方子项目，并尝试打开已失效的旧证书配置。
    # 统一先生成未签名包，再在下方用 Mage 对最终 ClickOnce 清单签名。
    $signValue = "false"
    $msbuildArguments = @(
        $solutionPath,
        "/t:Rebuild",
        "/p:Configuration=Release",
        "/p:Platform=Any CPU",
        "/p:OutDir=$stagePath\",
        "/p:ApplicationVersion=$version",
        "/p:ApplicationRevision=0",
        "/p:InstallUrl=$buildInstallUrl",
        "/p:SignManifests=$signValue",
        "/m",
        "/nologo"
    )
    Write-Host "正在构建 ClickOnce 版本 $version ..."
    & $msbuild @msbuildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild 失败，退出码：$LASTEXITCODE"
    }

    Assert-RequiredFiles $stagePath

    $stageAppManifest = Join-Path $stagePath "小黑封包助手.application"
    $stageExeManifest = Join-Path $stagePath "小黑封包助手.exe.manifest"
    Ensure-ManifestFiles `
        -Directory $stagePath `
        -SourceDirectory (Split-Path -Parent $projectPath) `
        -ManifestPath $stageExeManifest
    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        Update-DeploymentProvider `
            -Path $stageAppManifest `
            -ProviderUrl $providerUrl `
            -PrepareForSigning:$false
    }
    if ($signManifestsEnabled) {
        $mage = Find-Mage
        & $mage -Sign $stageExeManifest -CertHash $certificateHashValue -Algorithm sha256RSA
        if ($LASTEXITCODE -ne 0) {
            throw "mage.exe 签名应用清单失败，退出码：$LASTEXITCODE"
        }
    }
    Update-DeploymentManifestDependency `
        -DeploymentManifestPath $stageAppManifest `
        -ApplicationManifestPath $stageExeManifest
    if ($signManifestsEnabled) {
        & $mage -Sign $stageAppManifest -CertHash $certificateHashValue -Algorithm sha256RSA
        if ($LASTEXITCODE -ne 0) {
            throw "mage.exe 签名部署清单失败，退出码：$LASTEXITCODE"
        }
    }
    $appText = Read-Utf8Text $stageAppManifest
    $exeText = Read-Utf8Text $stageExeManifest
    if (-not $appText.Contains(('version="{0}"' -f $version))) {
        throw "ClickOnce 部署清单版本不一致：$stageAppManifest"
    }
    if (-not $exeText.Contains(('version="{0}"' -f $version))) {
        throw "ClickOnce 应用清单版本不一致：$stageExeManifest"
    }

    New-Item -ItemType Directory -Path $transactionPath -Force | Out-Null
    Copy-Item -Path (Join-Path $stagePath '*') -Destination $transactionPath -Recurse -Force
    Ensure-DeploymentMappedFiles `
        -Directory $transactionPath `
        -DeploymentManifestPath (Join-Path $transactionPath "小黑封包助手.application") `
        -ApplicationManifestPath (Join-Path $transactionPath "小黑封包助手.exe.manifest")
    New-StableDeploymentManifest `
        -VersionManifestPath (Join-Path $transactionPath "小黑封包助手.application") `
        -StableManifestPath (Join-Path $transactionPath "..\小黑封包助手.application") `
        -Version $version `
        -ProviderUrl $providerUrl `
        -PrepareForSigning:$signManifestsEnabled

    $stableManifestForSigning = Join-Path $transactionPath "..\小黑封包助手.application"
    Update-DeploymentManifestDependency `
        -DeploymentManifestPath $stableManifestForSigning `
        -ApplicationManifestPath (Join-Path $transactionPath "小黑封包助手.exe.manifest")
    if ($signManifestsEnabled) {
        $mage = Find-Mage
        & $mage -Sign $stableManifestForSigning -CertHash $certificateHashValue -Algorithm sha256RSA
        if ($LASTEXITCODE -ne 0) {
            throw "mage.exe 签名失败，退出码：$LASTEXITCODE"
        }
    }

    Move-Item -LiteralPath $transactionPath -Destination $finalReleasePath
    $published = $true

    Write-Host "本地 ClickOnce 发布已完成：$finalReleasePath"
    Write-Host "固定入口清单：$stableManifestPath"
    if (-not $signManifestsEnabled) {
        Write-Warning "本次为未签名本地发布。上传公网前必须使用外部证书或证书存储重新签名。"
    }
    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        Write-Host "本机固定入口已更新，可直接点击桌面快捷方式启动：$stableManifestPath"
    }
    else {
        Write-Host "请将 releases\$version\ 下的全部文件以及 releases\小黑封包助手.application 上传到：$endpointBase"
    }
}
catch {
    if (Test-Path -LiteralPath $transactionPath) {
        Remove-Item -LiteralPath $transactionPath -Recurse -Force
    }
    if (-not $published -and (Test-Path -LiteralPath $stagePath)) {
        Remove-Item -LiteralPath $stagePath -Recurse -Force
    }
    foreach ($path in $versionFiles) {
        [System.IO.File]::WriteAllBytes($path, $originalBytes[$path])
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stagePath) {
        Remove-Item -LiteralPath $stagePath -Recurse -Force
    }
}
