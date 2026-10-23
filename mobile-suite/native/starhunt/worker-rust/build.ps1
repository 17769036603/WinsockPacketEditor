param(
    [ValidateSet('shared', 'static')][string]$FridaLinkMode = 'shared'
)
$ErrorActionPreference = 'Stop'
$suiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$workspaceRoot = (Resolve-Path (Join-Path $suiteRoot '..\..')).Path
$sdk = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($sdk)) { $sdk = $env:ANDROID_SDK_ROOT }
if ([string]::IsNullOrWhiteSpace($sdk)) { $sdk = 'C:\Users\Administrator\AppData\Local\Android\Sdk' }
$ndk = $env:ANDROID_NDK_HOME
if ([string]::IsNullOrWhiteSpace($ndk)) { $ndk = $env:ANDROID_NDK_ROOT }
if ([string]::IsNullOrWhiteSpace($ndk)) { $ndk = Join-Path $sdk 'ndk\27.2.12479018' }
$devkit = $env:FRIDA_CORE_DEVKIT
if ([string]::IsNullOrWhiteSpace($devkit)) { $devkit = Join-Path $workspaceRoot '.scratch\frida-core-devkit-17.17.0-android-x86_64' }
$cargo = Join-Path $env:USERPROFILE '.cargo\bin\cargo.exe'
$linker = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\x86_64-linux-android23-clang.cmd'
$env:FRIDA_CORE_DEVKIT = $devkit
$env:CARGO_NET_OFFLINE = 'true'
$env:CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER = $linker
$env:SUITE_FRIDA_LINK_MODE = $FridaLinkMode
if ($FridaLinkMode -eq 'shared') {
    $sharedFridaDir = Join-Path $suiteRoot 'build\frida-shared'
    if (-not (Test-Path -LiteralPath (Join-Path $sharedFridaDir 'libfrida-core.so'))) {
        throw "Shared Frida core missing: $sharedFridaDir"
    }
    $env:FRIDA_SHARED_LIBRARY_DIR = $sharedFridaDir
} else {
    Remove-Item Env:FRIDA_SHARED_LIBRARY_DIR -ErrorAction SilentlyContinue
}
foreach ($required in @($cargo, $linker, $devkit)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required offline Android build input missing: $required" }
}
& $cargo build --offline --locked --release --target x86_64-linux-android --manifest-path (Join-Path $PSScriptRoot 'Cargo.toml') --bin starhunt_worker
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output (Join-Path $PSScriptRoot 'target\x86_64-linux-android\release\starhunt_worker')
