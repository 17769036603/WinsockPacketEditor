$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$devkit = Join-Path $workspace '.scratch\frida-core-devkit-17.17.0-android-x86_64'
$sdk = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($sdk)) { $sdk = $env:ANDROID_SDK_ROOT }
if ([string]::IsNullOrWhiteSpace($sdk)) { $sdk = 'C:\Users\Administrator\AppData\Local\Android\Sdk' }
$ndk = $env:ANDROID_NDK_HOME
if ([string]::IsNullOrWhiteSpace($ndk)) { $ndk = $env:ANDROID_NDK_ROOT }
if ([string]::IsNullOrWhiteSpace($ndk)) { $ndk = Join-Path $sdk 'ndk\27.2.12479018' }
$cargo = 'C:\Users\Administrator\.cargo\bin\cargo.exe'
if (-not (Test-Path $devkit)) { throw "Frida devkit missing: $devkit" }
if (-not (Test-Path $cargo)) { throw "Cargo missing: $cargo" }
$env:FRIDA_CORE_DEVKIT = $devkit
$env:CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\x86_64-linux-android23-clang.cmd'
& $cargo build --release --manifest-path (Join-Path $PSScriptRoot 'Cargo.toml') --target x86_64-linux-android
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output (Join-Path $PSScriptRoot 'target\x86_64-linux-android\release\anlei_worker')
