param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\..\.scratch\bosshunt-stop-recovery')
)
$ErrorActionPreference = 'Stop'
if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $javac = Join-Path $env:JAVA_HOME 'bin\javac.exe'
    $java = Join-Path $env:JAVA_HOME 'bin\java.exe'
} else {
    $javac = (Get-Command javac -CommandType Application -ErrorAction Stop).Source
    $java = (Get-Command java -CommandType Application -ErrorAction Stop).Source
}
if (-not (Test-Path -LiteralPath $javac -PathType Leaf) -or -not (Test-Path -LiteralPath $java -PathType Leaf)) {
    throw 'Set JAVA_HOME to a JDK or put javac and java on PATH.'
}

$suiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$classesDir = Join-Path $outputRoot 'model-classes'
$null = New-Item -ItemType Directory -Path $classesDir -Force
$sources = @(
    'app/src/main/java/com/xnas/wpe/bosshunt/BossHuntSelection.java',
    'app/src/main/java/com/xnas/wpe/bosshunt/BossHuntStatus.java',
    'app/src/main/java/com/xnas/wpe/bosshunt/BossHuntStopJournal.java',
    'app/src/main/java/com/xnas/wpe/bosshunt/BossHuntStopRecovery.java',
    'tests/model-stubs/com/xnas/wpe/bosshunt/BossHuntMapCatalog.java',
    'tests/BossHuntStopRecoverySelfTest.java'
) | ForEach-Object { Join-Path $suiteRoot $_ }

& $javac --release 8 -encoding UTF-8 -d $classesDir $sources
if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
& $java -cp $classesDir com.xnas.wpe.bosshunt.BossHuntStopRecoverySelfTest
if ($LASTEXITCODE -ne 0) { throw "Java self-test failed with exit code $LASTEXITCODE" }
