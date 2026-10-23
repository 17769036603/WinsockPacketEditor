$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$clang = $env:CC
if ([string]::IsNullOrWhiteSpace($clang)) { $clang = 'clang' }
$binary = Join-Path $env:TEMP 'suite-reallocarray-test.exe'
& $clang (Join-Path $root 'native\frida-shared\reallocarray_compat.c') (Join-Path $PSScriptRoot 'reallocarray_test.c') '-o' $binary
if ($LASTEXITCODE -ne 0) { throw "clang failed with exit code $LASTEXITCODE" }
try {
    & $binary
    if ($LASTEXITCODE -ne 0) { throw "reallocarray test failed with exit code $LASTEXITCODE" }
} finally {
    Remove-Item -LiteralPath $binary -Force -ErrorAction SilentlyContinue
}
