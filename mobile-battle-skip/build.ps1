param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$rustRoot = Join-Path $projectRoot 'launcher-rust'
$sdkRoot = 'C:\Users\Administrator\AppData\Local\Android\Sdk'
$ndkRoot = Join-Path $sdkRoot 'ndk\27.2.12479018'
$jdkRoot = 'C:\Users\Administrator\.codex\tools\temurin17-portable\jdk-17.0.20+8'
$cargoExe = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.cargo\bin\cargo.exe'
$rustupExe = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.cargo\bin\rustup.exe'
$linker = Join-Path $ndkRoot 'toolchains\llvm\prebuilt\windows-x86_64\bin\x86_64-linux-android23-clang.cmd'

foreach ($required in @($cargoExe, $rustupExe, $linker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required build tool is missing: $required" }
}
$targets = & $rustupExe target list --installed
if ($LASTEXITCODE -ne 0 -or $targets -notcontains 'x86_64-linux-android') {
    throw 'Rust target x86_64-linux-android is missing. Install it with: rustup target add x86_64-linux-android'
}

$env:JAVA_HOME = $jdkRoot
$env:ANDROID_HOME = $sdkRoot
$env:ANDROID_SDK_ROOT = $sdkRoot
$env:ANDROID_NDK_HOME = $ndkRoot
$env:ANDROID_NDK_ROOT = $ndkRoot
$env:PATH = "$(Join-Path $ndkRoot 'toolchains\llvm\prebuilt\windows-x86_64\bin');$env:PATH"
$env:CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER = 'x86_64-linux-android23-clang.cmd'

& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $projectRoot 'worker-rust/build.ps1')
if ($LASTEXITCODE -ne 0) { throw "Rust worker build failed with exit code $LASTEXITCODE" }
$jniLibs = Join-Path $projectRoot 'app/build/generated/jniLibs/x86_64'
New-Item -ItemType Directory -Force -Path $jniLibs | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'worker-rust/target/x86_64-linux-android/release/battle_skip_worker') -Destination (Join-Path $jniLibs 'libbattle_skip_worker.so') -Force

Push-Location $rustRoot
try {
    & $cargoExe build --release --target x86_64-linux-android --lib
    if ($LASTEXITCODE -ne 0) { throw "Rust launcher build failed with exit code $LASTEXITCODE" }
} finally {
    Pop-Location
}

$launcher = Join-Path $rustRoot 'target\x86_64-linux-android\release\libbattle_skip_launcher.so'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) { throw 'Rust build succeeded but libbattle_skip_launcher.so is missing.' }
$jniLibs = Join-Path $projectRoot 'app\build\generated\jniLibs\x86_64'
New-Item -ItemType Directory -Force -Path $jniLibs | Out-Null
Copy-Item -LiteralPath $launcher -Destination (Join-Path $jniLibs 'libbattle_skip_launcher.so') -Force

$worker = Join-Path $jniLibs 'libbattle_skip_worker.so'
if (-not (Test-Path -LiteralPath $worker -PathType Leaf)) {
    throw 'Rust launcher compiled, but required worker is missing: app/build/generated/jniLibs/x86_64/libbattle_skip_worker.so'
}
