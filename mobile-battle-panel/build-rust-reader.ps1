param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$rustRoot = Join-Path $projectRoot 'rust-reader'

function Find-Tool([string]$name, [string]$fallbackPath) {
    $tool = Get-Command $name -ErrorAction SilentlyContinue
    if ($tool) { return $tool.Source }
    if (Test-Path -LiteralPath $fallbackPath -PathType Leaf) { return $fallbackPath }
    throw "Required tool not found: $name"
}

$userProfilePath = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$cargoExe = Find-Tool 'cargo.exe' (Join-Path $userProfilePath '.cargo/bin/cargo.exe')
$rustupExe = Find-Tool 'rustup.exe' (Join-Path $userProfilePath '.cargo/bin/rustup.exe')

$installedTargets = & $rustupExe target list --installed
if ($LASTEXITCODE -ne 0 -or $installedTargets -notcontains 'x86_64-linux-android') {
    throw 'Rust target x86_64-linux-android is missing. Install it with: rustup target add x86_64-linux-android'
}

$sdkRoots = @($env:ANDROID_NDK_HOME, $env:ANDROID_NDK_ROOT)
if ($env:ANDROID_SDK_ROOT) { $sdkRoots += Join-Path $env:ANDROID_SDK_ROOT 'ndk' }
if ($env:ANDROID_HOME) { $sdkRoots += Join-Path $env:ANDROID_HOME 'ndk' }
$sdkRoots += Join-Path $env:LOCALAPPDATA 'Android/Sdk/ndk'

$ndkRoot = $null
foreach ($root in $sdkRoots | Where-Object { $_ } | Select-Object -Unique) {
    if (Test-Path -LiteralPath $root -PathType Container) {
        if (Test-Path -LiteralPath (Join-Path $root 'toolchains/llvm/prebuilt/windows-x86_64/bin/x86_64-linux-android23-clang.cmd')) {
            $ndkRoot = $root
            break
        }
        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'toolchains/llvm/prebuilt/windows-x86_64/bin/x86_64-linux-android23-clang.cmd') } |
            Select-Object -First 1
        if ($candidate) { $ndkRoot = $candidate.FullName; break }
    }
}
if (-not $ndkRoot) { throw 'Android NDK x86_64 Windows linker was not found.' }

$ndkBin = Join-Path $ndkRoot 'toolchains/llvm/prebuilt/windows-x86_64/bin'
$linkerName = 'x86_64-linux-android23-clang.cmd'
$env:PATH = "$ndkBin;$env:PATH"
$env:CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER = $linkerName

Push-Location $rustRoot
try {
    & $cargoExe build --release --target x86_64-linux-android --bin enemy_reader
    if ($LASTEXITCODE -ne 0) { throw "Cargo Android build failed with exit code $LASTEXITCODE" }
} finally {
    Pop-Location
}

$builtBinary = Join-Path $rustRoot 'target/x86_64-linux-android/release/enemy_reader'
if (-not (Test-Path -LiteralPath $builtBinary -PathType Leaf)) {
    throw 'Cargo succeeded but the expected Android worker binary is missing.'
}

$outputDirectory = Join-Path $projectRoot 'app/build/generated/jniLibs/x86_64'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
Copy-Item -LiteralPath $builtBinary -Destination (Join-Path $outputDirectory 'libenemy_reader.so') -Force
