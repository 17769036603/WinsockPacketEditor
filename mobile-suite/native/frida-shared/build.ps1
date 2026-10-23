param(
    [Parameter(Mandatory = $true)][string]$Ndk,
    [Parameter(Mandatory = $true)][string]$Devkit,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$linker = Join-Path $Ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\x86_64-linux-android23-clang.cmd'
$archive = Join-Path $Devkit 'libfrida-core.a'
$source = Join-Path $PSScriptRoot 'reallocarray_compat.c'
foreach ($input in @($linker, $archive, $source)) {
    if (-not (Test-Path -LiteralPath $input -PathType Leaf)) { throw "Required shared Frida input missing: $input" }
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = Join-Path $OutputDirectory 'libfrida-core.so'
& $linker '-shared' '-pthread' '-Wl,-soname,libfrida-core.so' '-Wl,--whole-archive' $archive '-Wl,--no-whole-archive' $source '-llog' '-ldl' '-lm' '-o' $output
if ($LASTEXITCODE -ne 0) { throw "NDK shared Frida link failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $output -PathType Leaf)) { throw "Shared Frida output missing: $output" }
