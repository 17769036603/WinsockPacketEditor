[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Readelf,
    [string]$Core = (Join-Path $PSScriptRoot '..\..\build\size-lab\artifacts\libfrida-core.so'),
    [string]$Worker = (Join-Path $PSScriptRoot '..\..\build\size-lab\artifacts\libanlei_worker.so'),
    [string]$StaticApk,
    [string]$SharedApk
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
foreach ($path in @($Readelf, $Core, $Worker)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required input missing: $path" }
}

function Invoke-Readelf([string[]]$ReadelfArgs) {
    $output = & $Readelf @ReadelfArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "llvm-readelf $($ReadelfArgs -join ' ') failed: $output" }
    return ($output -join "`n")
}
function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Output "PASS: $Message"
}

$coreDyn = Invoke-Readelf @('-d', $Core)
$coreDynSym = Invoke-Readelf @('--dyn-syms', $Core)
$coreHeader = Invoke-Readelf @('-h', $Core)
$workerDyn = Invoke-Readelf @('-d', $Worker)
$workerHeader = Invoke-Readelf @('-h', $Worker)
$coreProg = Invoke-Readelf @('-l', $Core)
$workerProg = Invoke-Readelf @('-l', $Worker)

Require ($coreHeader -match 'Type:\s+DYN') 'shared core is ELF DYN'
Require ($workerHeader -match 'Type:\s+DYN') 'worker is ELF DYN'
Require ($coreDyn -match 'SONAME.*libfrida-core\.so') 'shared core declares libfrida-core.so SONAME'
Require ($workerDyn -match 'NEEDED.*libfrida-core\.so') 'worker requires libfrida-core.so'
Require ($workerDyn -match 'RUNPATH.*\$ORIGIN') 'worker RUNPATH contains $ORIGIN'
Require ($coreDynSym -match '\bfrida_init\b' -and $coreDynSym -match '\bfrida_deinit\b') 'core exports Frida init/deinit'
Require ($coreDynSym -match '\bfrida_version\b') 'core exports Frida version entry point'
Require ($coreDynSym -match '\bfrida_device_manager_new\b') 'core exports device manager symbol (link metadata only)'
Require ($coreProg -notmatch 'TEXTREL' -and $workerProg -notmatch 'TEXTREL' -and $coreDyn -notmatch 'TEXTREL' -and $workerDyn -notmatch 'TEXTREL') 'core and worker have no TEXTREL'

$manifest = Join-Path $root 'app\src\main\java\com\xnas\wpe\suite\MainActivity.java'
$readme = Join-Path $root 'README.md'
$license = Join-Path $root 'ACTIVATION_VALIDATION.md'
$manifestText = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifest
$readmeText = Get-Content -Raw -Encoding UTF8 -LiteralPath $readme
$licenseText = Get-Content -Raw -Encoding UTF8 -LiteralPath $license
foreach ($feature in @('角色速度','战斗快进','战投跳过','战斗面板','金蝉装备','暗雷遇敌')) {
    Require (($manifestText + $readmeText).Contains($feature)) "six-feature inventory includes $feature"
}
Require ($readmeText -match '功能启动和原生后台工作均受授权门禁') 'README documents authorization gate'
Require ($readmeText -match '首次无授权记录时只展示界面，不启动功能服务或请求 Root') 'README documents no-auth STOP behavior'
Require ($licenseText -match 'clean stopped|STOP|已停止') 'activation validation documents stop/cleanup contract'

if (($StaticApk -and -not $SharedApk) -or ($SharedApk -and -not $StaticApk)) { throw 'Provide both -StaticApk and -SharedApk, or neither.' }
if ($StaticApk) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $staticZip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $StaticApk))
    $sharedZip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $SharedApk))
    try {
        $staticNative = @($staticZip.Entries | Where-Object FullName -Match '^lib/x86_64/.*\.so$')
        $sharedNative = @($sharedZip.Entries | Where-Object FullName -Match '^lib/x86_64/.*\.so$')
        Require ($staticNative.Count -eq 8) "static APK contains expected 8 x86_64 native libraries (found $($staticNative.Count))"
        Require ($sharedNative.Count -eq 9) "shared APK contains expected 8 workers plus one shared core (found $($sharedNative.Count))"
        Require (@($sharedNative | Where-Object FullName -eq 'lib/x86_64/libfrida-core.so').Count -eq 1) 'shared APK packages exactly one shared core'
    } finally { $staticZip.Dispose(); $sharedZip.Dispose() }
}

Write-Output 'Static validation complete. No device was contacted.'
