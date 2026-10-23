$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'app/src/main/java/com/xnas/wpe/mounthold/MountHoldRuntimeLog.java'
$test = Join-Path $PSScriptRoot 'MountHoldRuntimeLogSelfTest.java'
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$tempFull = [System.IO.Path]::GetFullPath($temp)
if (-not $tempFull.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Temporary output path is outside the system temp directory.'
}
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    & javac -d $temp $source $test
    if ($LASTEXITCODE -ne 0) { throw "javac failed: $LASTEXITCODE" }
    & java -cp $temp com.xnas.wpe.mounthold.MountHoldRuntimeLogSelfTest
    if ($LASTEXITCODE -ne 0) { throw "self-check failed: $LASTEXITCODE" }
}
finally {
    $cleanupFull = [System.IO.Path]::GetFullPath($temp)
    if ($cleanupFull.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        $cleanupFull -ne $tempRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar)) {
        Remove-Item -LiteralPath $cleanupFull -Recurse -Force -ErrorAction SilentlyContinue
    }
}
