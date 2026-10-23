$ErrorActionPreference = 'Stop'
$suiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("schedule-merge-" + [guid]::NewGuid().ToString('N'))
$stubRoot = Join-Path $PSScriptRoot 'schedule-merge-stubs'
$task = Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/ScheduleTask.java'
$store = Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/ScheduleStore.java'
$time = Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/ScheduleExecutionTime.java'
$test = Join-Path $PSScriptRoot 'ScheduleMergeSelfTest.java'
$service = [System.IO.File]::ReadAllText(
    (Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/ScheduleService.java'))
$page = [System.IO.File]::ReadAllText(
    (Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/SchedulePage.java'))
$actions = [System.IO.File]::ReadAllText(
    (Join-Path $suiteRoot 'app/src/main/java/com/xnas/wpe/suite/ModuleActions.java'))

if (-not $service.Contains('ScheduleExecutionTime.dueOccurrenceAt(') -or
    -not $service.Contains('ScheduleStore.markFired(this, task, key)')) {
    throw 'Service must pick and persist a stable occurrence key before dispatch.'
}
$fence = $service.IndexOf('ScheduleStore.markFired(this, task, key)', [StringComparison]::Ordinal)
$dispatch = $service.IndexOf('ModuleActions.dispatchTarget(this, targetId, task.action,', [StringComparison]::Ordinal)
if ($dispatch -lt 0 -or $fence -ge $dispatch) {
    throw 'The occurrence fence must be persisted before target dispatch.'
}
if (-not $page.Contains('明确转换为整点/半点周期') -or
    -not $page.Contains('ScheduleStore.update(host, updated)') -or
    -not $page.Contains('editing.enabled')) {
    throw 'Editor must explicitly convert legacy times and update the existing task state.'
}
$keepDaily = $page.IndexOf('if (keepDaily)', [StringComparison]::Ordinal)
$cycleRequired = $page.IndexOf('if (cycles == 0)', [StringComparison]::Ordinal)
if ($keepDaily -lt 0 -or $cycleRequired -lt $keepDaily) {
    throw 'Legacy daily editing must be saveable while hidden cycle controls are unchecked.'
}
if (-not $actions.Contains('if (normalized == null) return false;') -or
    -not $actions.Contains('ACTION_START_HANGUP')) {
    throw 'Unknown operations must fail closed while saved module start actions remain wired.'
}
if (-not $actions.Contains('boolean legacyDaily)') -or
    -not $service.Contains('task.isLegacyDaily()')) {
    throw 'Pause/resume dispatch must require a legacy-daily task from the scheduler.'
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
    $fallbackJdk = 'C:/Users/Administrator/.codex/tools/temurin17-portable/jdk-17.0.20+8'
    $candidateCompiler = Join-Path $fallbackJdk 'bin/javac.exe'
    $candidateRunner = Join-Path $fallbackJdk 'bin/java.exe'
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

$stubFiles = @(Get-ChildItem -LiteralPath $stubRoot -Recurse -File -Filter '*.java' |
    Where-Object { $_.FullName -notlike '*com\xnas\wpe\suite\ModuleActions.java' } |
    ForEach-Object { $_.FullName })
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    # Compile the actual pure target-policy portion, not a duplicate policy stub.
    $policyEnd = $actions.IndexOf('    public static final class DispatchResult')
    $targetStart = $actions.IndexOf('    public static String[] targetIds(')
    $targetEnd = $actions.IndexOf('    static Intent buildIntent(', $targetStart)
    if ($policyEnd -lt 0 -or $targetStart -lt 0 -or $targetEnd -lt 0) { throw 'ModuleActions policy extraction failed.' }
    $policy = [regex]::Replace($actions.Substring(0,$policyEnd), '(?m)^import android\.[^;]+;\s*', '')
    $policy += $actions.Substring($targetStart,$targetEnd-$targetStart) + "`n}`n"
    $policyFile = Join-Path $temp 'ModuleActions.java'
    [IO.File]::WriteAllText($policyFile,$policy,[Text.UTF8Encoding]::new($false))
    $sources = @($task, $store, $time, $test, $policyFile) + $stubFiles
    & $compiler -encoding UTF-8 -d $temp @sources
    if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
    & $runner -cp $temp com.xnas.wpe.suite.ScheduleMergeSelfTest
    if ($LASTEXITCODE -ne 0) { throw "Schedule merge self-test failed with exit code $LASTEXITCODE" }
    Write-Output 'Schedule merge integration contract PASS (occurrence order, persistence, compatibility, and editor wiring)'
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
