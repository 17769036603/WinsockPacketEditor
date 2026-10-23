param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\..\.scratch\starhunt-multilevel-20261018\java')
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
    'app/src/main/java/com/xnas/wpe/starhunt/StarHuntSelection.java',
    'app/src/main/java/com/xnas/wpe/starhunt/StarHuntSelectionStore.java',
    'app/src/main/java/com/xnas/wpe/starhunt/StarHuntCommand.java',
    'app/src/main/java/com/xnas/wpe/starhunt/StarHuntHangupRequest.java',
    'app/src/main/java/com/xnas/wpe/starhunt/StarHuntStatus.java',
    'app/src/main/java/com/xnas/wpe/starhunt/StarHuntStopJournal.java',
    'app/src/main/java/com/xnas/wpe/starhunt/StarHuntStopRecovery.java',
    'tests/StarHuntSelectionSelfTest.java',
    'tests/StarHuntSelectionStoreSelfTest.java',
    'tests/StarHuntCommandSelfTest.java',
    'tests/StarHuntHangupRequestSelfTest.java',
    'tests/StarHuntStatusSelfTest.java',
    'tests/StarHuntStopJournalSelfTest.java',
    'tests/StarHuntStopRecoverySelfTest.java'
) | ForEach-Object { Join-Path $suiteRoot $_ }

& $javac --release 8 -encoding UTF-8 -d $classesDir $sources
if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
$tests = @(
    'com.xnas.wpe.starhunt.StarHuntSelectionSelfTest',
    'com.xnas.wpe.starhunt.StarHuntSelectionStoreSelfTest',
    'com.xnas.wpe.starhunt.StarHuntCommandSelfTest',
    'StarHuntHangupRequestSelfTest',
    'com.xnas.wpe.starhunt.StarHuntStatusSelfTest',
    'com.xnas.wpe.starhunt.StarHuntStopJournalSelfTest',
    'com.xnas.wpe.starhunt.StarHuntStopRecoverySelfTest'
)
foreach ($testClass in $tests) {
    & $java -cp $classesDir $testClass
    if ($LASTEXITCODE -ne 0) { throw "Java self-test failed: $testClass (exit code $LASTEXITCODE)" }
}
