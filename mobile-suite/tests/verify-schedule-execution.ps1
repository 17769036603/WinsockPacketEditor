$ErrorActionPreference = 'Stop'
$suiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("schedule-execution-" + [guid]::NewGuid().ToString('N'))
$model = Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/ScheduleExecution.java'
$time = Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/ScheduleExecutionTime.java'
$test = Join-Path $PSScriptRoot 'ScheduleExecutionSelfTest.java'
$service = [System.IO.File]::ReadAllText(
    (Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/ScheduleService.java'))
$actions = [System.IO.File]::ReadAllText(
    (Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/ModuleActions.java'))

if (-not $service.Contains('ScheduleStore.markFired(this, task, key)')) {
    throw 'Occurrence-key fence is missing before per-target dispatch.'
}
$fence = $service.IndexOf('ScheduleStore.markFired(this, task, key)', [StringComparison]::Ordinal)
$dispatch = $service.IndexOf('ModuleActions.dispatchTarget(this, targetId, task.action,', [StringComparison]::Ordinal)
if ($dispatch -lt 0 -or $fence -ge $dispatch) {
    throw 'The exact occurrence key must be persisted before any target dispatch.'
}
$allAcceptedContract = $actions.Contains('boolean allAccepted = true;')
$allAcceptedReturn = $actions.Contains('return found && allAccepted;')
if (-not $allAcceptedContract -or -not $allAcceptedReturn) {
    throw 'The compatibility multi-target dispatcher must not report success when any target dispatch fails.'
}

$compiler = $null
$runner = $null
if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $candidateCompiler = Join-Path $env:JAVA_HOME 'bin/javac.exe'
    $candidateRunner = Join-Path $env:JAVA_HOME 'bin/java.exe'
    if ((Test-Path -LiteralPath $candidateCompiler -PathType Leaf) -and
        (Test-Path -LiteralPath $candidateRunner -PathType Leaf)) {
        $compiler = $candidateCompiler
        $runner = $candidateRunner
    }
}
if (-not $compiler -or -not $runner) {
    $compilerCommand = Get-Command javac -CommandType Application -ErrorAction SilentlyContinue
    $runnerCommand = Get-Command java -CommandType Application -ErrorAction SilentlyContinue
    if ($compilerCommand -and $runnerCommand) {
        $compiler = $compilerCommand.Source
        $runner = $runnerCommand.Source
    }
}
if (-not $compiler -or -not $runner) {
    throw 'No usable JDK found. Set JAVA_HOME to a JDK or add both javac and java to PATH.'
}
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    & $compiler -encoding UTF-8 -d $temp $model $time $test
    if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
    & $runner -cp $temp com.xnas.wpe.suite.ScheduleExecutionSelfTest
    if ($LASTEXITCODE -ne 0) { throw "Java self-test failed with exit code $LASTEXITCODE" }
    Write-Output 'Schedule integration contract PASS (occurrence fence ordering and all-target dispatch result)'
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
