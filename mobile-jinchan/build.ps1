$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$workspace = (Resolve-Path (Join-Path $projectRoot '..\..')).Path
$devkit = Join-Path $workspace '.scratch\frida-core-devkit-17.17.0-android-x86_64'
$ndk = $env:ANDROID_NDK_HOME
if ([string]::IsNullOrWhiteSpace($ndk)) {
    $sdkRoot = $env:ANDROID_HOME
    if ([string]::IsNullOrWhiteSpace($sdkRoot)) { $sdkRoot = $env:ANDROID_SDK_ROOT }
    if ([string]::IsNullOrWhiteSpace($sdkRoot)) { $sdkRoot = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
    $ndk = Join-Path $sdkRoot 'ndk\27.2.12479018'
}
$cargo = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.cargo\bin\cargo.exe'
$linker = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\x86_64-linux-android23-clang.cmd'
foreach ($path in @($devkit, $cargo, $linker)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required build input is missing: $path" }
}
$env:FRIDA_CORE_DEVKIT = $devkit
$env:CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER = 'x86_64-linux-android23-clang.cmd'
$env:PATH = "$(Split-Path -Parent $linker);$env:PATH"
$env:ANDROID_NDK_HOME = $ndk
$rustRoot = Join-Path $projectRoot 'worker-rust'
Push-Location $rustRoot
try {
    & $cargo build --release --target x86_64-linux-android
    if ($LASTEXITCODE -ne 0) { throw "Jinchan Rust build failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }
$binary = Join-Path $rustRoot 'target\x86_64-linux-android\release\jinchan_worker'
if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { throw 'Rust build succeeded but jinchan_worker is missing.' }
$jniLibs = Join-Path $projectRoot 'app\build\generated\jniLibs\x86_64'
New-Item -ItemType Directory -Force -Path $jniLibs | Out-Null
$output = Join-Path $jniLibs 'libjinchan_worker.so'
Copy-Item -LiteralPath $binary -Destination $output -Force
Write-Output $output
