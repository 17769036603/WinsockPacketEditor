param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}

function Read-Utf8 {
    param([string]$RelativePath)
    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "缺少文件：$RelativePath"
    }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Assert-Contains {
    param([string]$Text, [string]$Expected, [string]$Message)
    if (-not $Text.Contains($Expected)) { throw $Message }
}

function Assert-NotContains {
    param([string]$Text, [string]$Unexpected, [string]$Message)
    if ($Text.Contains($Unexpected)) { throw $Message }
}

function Get-Sha256Base64 {
    param([string]$Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToBase64String($sha.ComputeHash([System.IO.File]::ReadAllBytes($Path)))
    }
    finally {
        $sha.Dispose()
    }
}

function Assert-LocalPackage {
    param([string]$Root)

    $releaseRoot = Join-Path $Root "releases"
    $releaseVersions = @(
        Get-ChildItem -LiteralPath $releaseRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                $parsed = $null
                if ([System.Version]::TryParse($_.Name, [ref]$parsed)) { $parsed }
            }
    )
    $stablePath = Join-Path $releaseRoot "小黑封包助手.application"
    if ($releaseVersions.Count -eq 0 -or -not (Test-Path -LiteralPath $stablePath -PathType Leaf)) {
        Write-Warning "没有本地 ClickOnce 发布目录，跳过发布产物校验。"
        return
    }

    $latestVersion = ($releaseVersions | Sort-Object)[-1].ToString()
    $manifestPath = Join-Path $releaseRoot ("{0}\小黑封包助手.application" -f $latestVersion)
    $manifest = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
    $stableManifest = [System.IO.File]::ReadAllText($stablePath, [System.Text.Encoding]::UTF8)
    $releaseRootAbsolute = (Resolve-Path -LiteralPath $releaseRoot).Path
    $releaseRootUri = (New-Object -TypeName System.Uri -ArgumentList ($releaseRootAbsolute.TrimEnd('\') + '\')).AbsoluteUri
    $expectedProvider = $releaseRootUri + [Uri]::EscapeDataString("小黑封包助手.application")

    if (-not ($manifest.Contains('maximumAge="0"') -or $manifest.Contains('<beforeApplicationStartup />'))) {
        throw "部署清单必须设置为每次启动检查。"
    }
    Assert-Contains $manifest ('deploymentProvider codebase="{0}"' -f $expectedProvider) `
        "本地部署清单必须指向固定 file URI 入口。"
    Assert-Contains $stableManifest ('deploymentProvider codebase="{0}"' -f $expectedProvider) `
        "固定入口清单必须指向本机 releases 目录。"
    Assert-Contains $stableManifest (('version="{0}"' -f $latestVersion)) `
        "固定入口清单必须指向当前最高版本。"
    Assert-Contains $stableManifest (($latestVersion + "/小黑封包助手.exe.manifest")) `
        "固定入口清单必须指向版本目录中的应用清单。"
    Assert-Contains $manifest '<Signature ' `
        "版本部署清单必须包含 ClickOnce 签名。"
    Assert-Contains $stableManifest '<Signature ' `
        "固定入口清单必须包含 ClickOnce 签名。"

    $stableXml = [xml]$stableManifest
    $stableDependency = $stableXml.SelectSingleNode("//*[local-name()='dependentAssembly' and @dependencyType='install']")
    if ($null -eq $stableDependency) {
        throw "固定入口清单缺少应用清单依赖。"
    }
    $relativeDependency = $stableDependency.GetAttribute("codebase").Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $dependencyPath = Join-Path $releaseRoot $relativeDependency
    if (-not (Test-Path -LiteralPath $dependencyPath -PathType Leaf)) {
        throw "固定入口清单依赖文件不存在：$dependencyPath"
    }
    $digestNode = $stableXml.SelectSingleNode("//*[local-name()='dependentAssembly' and @dependencyType='install']//*[local-name()='DigestValue']")
    if ($null -eq $digestNode -or $digestNode.InnerText -ne (Get-Sha256Base64 $dependencyPath)) {
        throw "固定入口清单中的应用清单 SHA-256 不匹配。"
    }

    $requiredFiles = @("小黑封包助手.exe", "WPELibrary.dll", "EasyHook32.dll", "EasyHook64.dll", "EasyLoad32.dll", "EasyLoad64.dll", "System.Data.SQLite.dll")
    $latestDirectory = Join-Path $releaseRoot $latestVersion
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $latestDirectory $file) -PathType Leaf)) {
            throw "最新发布目录缺少依赖文件：$file"
        }
    }

    $exeManifestPath = Join-Path $latestDirectory "小黑封包助手.exe.manifest"
    $exeManifestText = [System.IO.File]::ReadAllText($exeManifestPath, [System.Text.Encoding]::UTF8)
    Assert-Contains $exeManifestText '<Signature ' `
        "应用清单必须包含 ClickOnce 签名。"
    [xml]$exeManifest = $exeManifestText
    $applicationIdentity = $exeManifest.SelectSingleNode("/*[local-name()='assembly']/*[local-name()='assemblyIdentity']")
    if ($null -eq $applicationIdentity) {
        throw "应用清单缺少根 assemblyIdentity。"
    }

    foreach ($deploymentPath in @(
        (Join-Path $latestDirectory "小黑封包助手.application"),
        $stablePath
    )) {
        [xml]$deploymentManifest = [System.IO.File]::ReadAllText($deploymentPath, [System.Text.Encoding]::UTF8)
        $dependency = $deploymentManifest.SelectSingleNode("//*[local-name()='dependentAssembly' and @dependencyType='install']")
        if ($null -eq $dependency) {
            throw "部署清单缺少应用清单依赖：$deploymentPath"
        }
        $dependencyIdentity = $dependency.SelectSingleNode("./*[local-name()='assemblyIdentity']")
        if ($null -eq $dependencyIdentity) {
            throw "部署清单依赖缺少 assemblyIdentity：$deploymentPath"
        }
        foreach ($attributeName in @('name', 'version', 'language', 'publicKeyToken', 'processorArchitecture', 'type')) {
            if ($dependencyIdentity.GetAttribute($attributeName) -ne $applicationIdentity.GetAttribute($attributeName)) {
                throw "部署清单引用标识与应用清单不一致：$deploymentPath / $attributeName"
            }
        }
    }

    foreach ($fileNode in @($exeManifest.SelectNodes("//*[local-name()='file']"))) {
        $relativePath = $fileNode.GetAttribute("name").Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $filePath = Join-Path $latestDirectory $relativePath
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "应用清单声明的文件未随发布：$relativePath"
        }
        $declaredSize = $fileNode.GetAttribute("size")
        if (-not [string]::IsNullOrWhiteSpace($declaredSize) -and (Get-Item -LiteralPath $filePath).Length -ne [int64]$declaredSize) {
            throw "发布文件大小与应用清单不一致：$relativePath"
        }
        $digestNode = $fileNode.SelectSingleNode(".//*[local-name()='DigestValue']")
        if ($null -ne $digestNode -and $digestNode.InnerText -ne (Get-Sha256Base64 $filePath)) {
            throw "发布文件 SHA-256 与应用清单不一致：$relativePath"
        }

        if (-not (Test-Path -LiteralPath ($filePath + '.deploy') -PathType Leaf)) {
            throw "ClickOnce mapFileExtensions=true 时缺少映射文件：$relativePath.deploy"
        }
    }

    $latestDeployment = [xml]$manifest
    $latestDeploymentNode = $latestDeployment.SelectSingleNode("//*[local-name()='deployment']")
    if ($null -ne $latestDeploymentNode -and $latestDeploymentNode.GetAttribute('mapFileExtensions') -eq 'true') {
        $mappedPayloads = @(
            @($exeManifest.SelectNodes("//*[local-name()='file']")) |
                ForEach-Object { $_.GetAttribute('name') }
            @($exeManifest.SelectNodes("//*[local-name()='dependentAssembly' and @codebase]")) |
                ForEach-Object { $_.GetAttribute('codebase') }
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

        foreach ($mappedPayload in $mappedPayloads) {
            $mappedPath = Join-Path $latestDirectory ($mappedPayload.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
            if (-not (Test-Path -LiteralPath ($mappedPath + '.deploy') -PathType Leaf)) {
                throw "ClickOnce mapFileExtensions=true 时缺少映射文件：$mappedPayload.deploy"
            }
        }
    }
    if (Test-Path -LiteralPath (Join-Path $latestDirectory "小黑封包助手.db")) {
        throw "最新发布目录不应包含用户数据库。"
    }
}

$project = Read-Utf8 "WinsockPacketEditor\WinsockPacketEditor.csproj"
$publisher = Read-Utf8 "tools\Publish-ClickOnceStable.ps1"
Assert-Contains $project "<UpdateEnabled>true</UpdateEnabled>" `
    "ClickOnce 必须启用更新。"
Assert-Contains $project "<UpdateMode>Foreground</UpdateMode>" `
    "ClickOnce 必须在启动前检查更新。"
Assert-Contains $publisher '$localRootUri' `
    "发布脚本默认必须生成本地 file URI 入口。"
Assert-Contains $publisher '/p:InstallUrl=$buildInstallUrl' `
    "构建时必须把合法的构建入口传给 ClickOnce。"
Assert-Contains $publisher 'if ([string]::IsNullOrWhiteSpace($Endpoint))' `
    "发布脚本必须支持本地默认入口和显式远程入口。"
Assert-Contains $publisher "Get-NextReleaseVersion" `
    "发布脚本必须自动计算当天递增版本。"
Assert-Contains $publisher "New-StableDeploymentManifest" `
    "发布脚本必须生成固定入口清单。"
Assert-Contains $publisher "Find-Mage" `
    "签名发布必须具备 mage.exe 路径。"
Assert-Contains $publisher '$signManifestsEnabled = ($SignManifests.IsPresent -or [string]::IsNullOrWhiteSpace($Endpoint))' `
    "本地发布默认必须启用 ClickOnce 签名。"
Assert-Contains $publisher '$CertificateSubject' `
    "本地发布必须支持从证书存储按主题自动选择签名证书。"
Assert-Contains $publisher "-CertHash" `
    "签名发布必须使用外部证书指纹。"
Assert-Contains $publisher "Update-DeploymentManifestDependency" `
    "签名发布必须在签名应用清单后更新部署清单引用。"
Assert-Contains $publisher "publicKeyToken" `
    "发布脚本必须同步部署清单与应用清单的完整 assemblyIdentity。"
Assert-Contains $publisher "Assert-RequiredFiles" `
    "发布脚本必须检查旁路运行文件。"
Assert-Contains $publisher "Ensure-DeploymentMappedFiles" `
    "发布脚本必须为 mapFileExtensions=true 准备 .deploy 文件。"
Assert-Contains $publisher 'WriteAllBytes($path, $originalBytes[$path])' `
    "发布失败时必须恢复版本源文件。"
Assert-NotContains $publisher "C:\\WPE64Cache\\小黑封包助手.db" `
    "发布脚本不得操作用户数据库。"

Assert-LocalPackage $RepositoryRoot
Write-Output "ClickOnce 更新配置与发布脚本回归通过。"
