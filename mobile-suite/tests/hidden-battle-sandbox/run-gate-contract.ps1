$ErrorActionPreference = 'Stop'
$suite = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$source = Join-Path $suite 'app\src\main\java\com\xnas\wpe\suite'
$javac = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\javac.exe' } else { 'javac' }
$java = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\java.exe' } else { 'java' }
if (-not (Test-Path -LiteralPath $javac)) { throw "javac not found: $javac" }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('hide-battle-gate-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $temp 'com\xnas\wpe\suite'
$classes = Join-Path $temp 'classes'
New-Item -ItemType Directory -Force -Path $package, $classes | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $source 'BattleConflictGate.java') -Destination $package
    Copy-Item -LiteralPath (Join-Path $source 'BattleWorkerStatus.java') -Destination $package
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GateHideBattleContract.java') -Destination $package
    & $javac -encoding UTF-8 -d $classes `
        (Join-Path $package 'BattleConflictGate.java') `
        (Join-Path $package 'BattleWorkerStatus.java') `
        (Join-Path $package 'GateHideBattleContract.java')
    if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
    & $java -cp $classes com.xnas.wpe.suite.GateHideBattleContract
    if ($LASTEXITCODE -ne 0) { throw "GateHideBattleContract failed with exit code $LASTEXITCODE" }
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
