$ErrorActionPreference = 'Stop'

$suiteRoot = (Resolve-Path $PSScriptRoot).Path
$workspaceRoot = (Resolve-Path (Join-Path $suiteRoot '..\..')).Path
$sdk = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($sdk)) { $sdk = $env:ANDROID_SDK_ROOT }
if ([string]::IsNullOrWhiteSpace($sdk)) { $sdk = 'C:\Users\Administrator\AppData\Local\Android\Sdk' }
$ndk = $env:ANDROID_NDK_HOME
if ([string]::IsNullOrWhiteSpace($ndk)) { $ndk = $env:ANDROID_NDK_ROOT }
if ([string]::IsNullOrWhiteSpace($ndk)) { $ndk = Join-Path $sdk 'ndk\27.2.12479018' }
$cargo = Join-Path $env:USERPROFILE '.cargo\bin\cargo.exe'
$devkit = $env:FRIDA_CORE_DEVKIT
if ([string]::IsNullOrWhiteSpace($devkit)) {
    $devkit = Join-Path $workspaceRoot '.scratch\frida-core-devkit-17.17.0-android-x86_64'
}
$target = 'x86_64-linux-android'
$linker = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\x86_64-linux-android23-clang.cmd'
$strip = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-strip.exe'
$targetRoot = Join-Path $suiteRoot 'build\cargo-target'
$jniRoot = Join-Path $suiteRoot 'app\build\generated\jniLibs\x86_64'

foreach ($required in @($cargo, $linker, $strip, $devkit)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required offline Android build input missing: $required" }
}
New-Item -ItemType Directory -Force -Path $jniRoot | Out-Null
$env:FRIDA_CORE_DEVKIT = $devkit
$env:CARGO_NET_OFFLINE = 'true'
$env:CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER = $linker

function Invoke-CargoBinary([string]$name, [string]$relativeManifest, [string]$binaryName, [string]$outputName) {
    $manifest = Join-Path $suiteRoot $relativeManifest
    $env:CARGO_TARGET_DIR = Join-Path $targetRoot $name
    & $cargo build --offline --locked --release --target $target --manifest-path $manifest --bin $binaryName
    if ($LASTEXITCODE -ne 0) { throw "Cargo failed for $name with exit code $LASTEXITCODE" }
    $built = Join-Path $env:CARGO_TARGET_DIR "$target\release\$binaryName"
    $destination = Join-Path $jniRoot $outputName
    if (-not (Test-Path -LiteralPath $built -PathType Leaf)) { throw "Cargo output missing: $built" }
    Copy-Item -LiteralPath $built -Destination $destination -Force
    Write-Output "Built $name -> $destination"
}

function Invoke-CargoLibrary([string]$name, [string]$relativeManifest, [string]$libraryName) {
    $manifest = Join-Path $suiteRoot $relativeManifest
    $env:CARGO_TARGET_DIR = Join-Path $targetRoot $name
    & $cargo build --offline --locked --release --target $target --manifest-path $manifest --lib
    if ($LASTEXITCODE -ne 0) { throw "Cargo failed for $name with exit code $LASTEXITCODE" }
    $built = Join-Path $env:CARGO_TARGET_DIR "$target\release\lib$libraryName.so"
    $destination = Join-Path $jniRoot "lib$libraryName.so"
    if (-not (Test-Path -LiteralPath $built -PathType Leaf)) { throw "Cargo output missing: $built" }
    Copy-Item -LiteralPath $built -Destination $destination -Force
    Write-Output "Built $name -> $destination"
}

Invoke-CargoBinary 'anlei' 'native\anlei\worker-rust\Cargo.toml' 'anlei_worker' 'libanlei_worker.so'
Invoke-CargoBinary 'mounthold' 'native\mounthold\rust-reader\Cargo.toml' 'mount_hold_worker' 'libmount_hold_worker.so'
Invoke-CargoBinary 'battleloop' 'native\battleloop\worker-rust\Cargo.toml' 'battle_loop_worker' 'libbattle_loop_worker.so'
Invoke-CargoBinary 'battleskip' 'native\battleskip\worker-rust\Cargo.toml' 'battle_skip_worker' 'libbattle_skip_worker.so'
Invoke-CargoLibrary 'battleskip-launcher' 'native\battleskip\launcher-rust\Cargo.toml' 'battle_skip_launcher'
Invoke-CargoBinary 'jinchan' 'native\jinchan\worker-rust\Cargo.toml' 'jinchan_worker' 'libjinchan_worker.so'
Invoke-CargoBinary 'battlepanel-reader' 'native\battlepanel\rust-reader\Cargo.toml' 'enemy_reader' 'libenemy_reader.so'
Invoke-CargoBinary 'battlepanel-resistance' 'native\battlepanel\resistance-query-rust\Cargo.toml' 'resistance-query' 'libresistance_query.so'

# Strip only the suite's generated JNI copies. Keep the Cargo outputs and all
# standalone modules untouched; the package still retains each ELF's dynamic
# symbols and dependencies while dropping unused static symbols.
$packageLibraries = @(
    'libanlei_worker.so',
    'libmount_hold_worker.so',
    'libbattle_loop_worker.so',
    'libbattle_skip_worker.so',
    'libbattle_skip_launcher.so',
    'libjinchan_worker.so',
    'libenemy_reader.so',
    'libresistance_query.so'
)
foreach ($library in $packageLibraries) {
    $path = Join-Path $jniRoot $library
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Generated suite library missing before strip: $path" }
    & $strip --strip-unneeded $path
    if ($LASTEXITCODE -ne 0) { throw "llvm-strip failed for $library with exit code $LASTEXITCODE" }
    Write-Output "Stripped suite package copy -> $path"
}
