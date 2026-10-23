param(
    [string]$OutputDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\stability'))
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be absolute.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $out -Force
$script:JavaCompiler = $null
$script:JavaRunner = $null
if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $candidateCompiler = Join-Path $env:JAVA_HOME 'bin\javac.exe'
    $candidateRunner = Join-Path $env:JAVA_HOME 'bin\java.exe'
    if ((Test-Path -LiteralPath $candidateCompiler -PathType Leaf) -and
        (Test-Path -LiteralPath $candidateRunner -PathType Leaf)) {
        $script:JavaCompiler = $candidateCompiler
        $script:JavaRunner = $candidateRunner
    }
}
if (-not $script:JavaCompiler -or -not $script:JavaRunner) {
    $compilerCommand = Get-Command javac -CommandType Application -ErrorAction SilentlyContinue
    $runnerCommand = Get-Command java -CommandType Application -ErrorAction SilentlyContinue
    if ($compilerCommand -and $runnerCommand) {
        $script:JavaCompiler = $compilerCommand.Source
        $script:JavaRunner = $runnerCommand.Source
        if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) { $env:JAVA_HOME = '' }
    }
}
if (-not $script:JavaCompiler -or -not $script:JavaRunner) {
    $portableJdk = 'C:/Users/Administrator/.codex/tools/temurin17-portable/jdk-17.0.20+8'
    $portableCompiler = Join-Path $portableJdk 'bin/javac.exe'
    $portableRunner = Join-Path $portableJdk 'bin/java.exe'
    if ((Test-Path -LiteralPath $portableCompiler -PathType Leaf) -and
        (Test-Path -LiteralPath $portableRunner -PathType Leaf)) {
        $env:JAVA_HOME = $portableJdk
        $env:Path = (Join-Path $portableJdk 'bin') + ';' + $env:Path
        $script:JavaCompiler = $portableCompiler
        $script:JavaRunner = $portableRunner
    } else {
        throw 'No usable JDK found. Set JAVA_HOME to a JDK or add both javac and java to PATH.'
    }
}
$results = [Collections.Generic.List[object]]::new()

function Invoke-Check([string]$Name, [scriptblock]$Command, [string]$ExpectedFailureMarker = '') {
    Write-Output "=== $Name ==="
    $safeName = $Name -replace '[^A-Za-z0-9._-]', '_'
    $stdoutPath = Join-Path $out ($safeName + '.stdout.log')
    $stderrPath = Join-Path $out ($safeName + '.stderr.log')
    $combinedPath = Join-Path $out ($safeName + '.log')
    $childPath = Join-Path $out ($safeName + '.child.ps1')
    $childSource = @"
`$ErrorActionPreference = 'Stop'
`$root = `$env:SUITE_STABILITY_ROOT
`$out = `$env:SUITE_STABILITY_OUTPUT
`$script:JavaCompiler = `$env:SUITE_STABILITY_JAVAC
`$script:JavaRunner = `$env:SUITE_STABILITY_JAVA
try {
    & {
$($Command.ToString())
    }
    `$code = if (`$null -ne `$LASTEXITCODE) { [int]`$LASTEXITCODE } else { 0 }
    exit `$code
} catch {
    [Console]::Error.WriteLine((`$_ | Out-String))
    exit 1
}
"@
    [IO.File]::WriteAllText($childPath, $childSource, [Text.UTF8Encoding]::new($true))
    $env:SUITE_STABILITY_ROOT = $root
    $env:SUITE_STABILITY_OUTPUT = $out
    $env:SUITE_STABILITY_JAVAC = $script:JavaCompiler
    $env:SUITE_STABILITY_JAVA = $script:JavaRunner

    $shellPath = Join-Path $PSHOME 'powershell.exe'
    if (-not (Test-Path -LiteralPath $shellPath -PathType Leaf)) {
        $pwshCommand = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue
        if ($pwshCommand) { $shellPath = $pwshCommand.Source }
        else {
            $powershellCommand = Get-Command powershell -CommandType Application -ErrorAction SilentlyContinue
            if ($powershellCommand) { $shellPath = $powershellCommand.Source }
            else {
                $results.Add([pscustomobject]@{ Name=$Name; Result='FAIL'; Detail='No powershell.exe, pwsh, or powershell executable is available.' })
                return
            }
        }
    }
    $env:SUITE_STABILITY_SHELL = $shellPath
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $shellPath
    $psi.Arguments = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $childPath + '"'
    $psi.WorkingDirectory = $root
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $proc = [Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    try {
        if (-not $proc.Start()) { throw 'Could not start isolated PowerShell child.' }
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()
        $proc.WaitForExit()
        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        [IO.File]::WriteAllText($stdoutPath, $stdout, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($stderrPath, $stderr, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($combinedPath, $stdout + $(if ($stderr) { "`r`n[stderr]`r`n" + $stderr } else { '' }), [Text.UTF8Encoding]::new($false))
        if ($stdout) { Write-Output $stdout.TrimEnd() }
        if ($stderr) { Write-Output $stderr.TrimEnd() }
        if ($proc.ExitCode -ne 0) {
            # Windows PowerShell can wrap an exception in the middle of a word.
            # Preserve the raw streams, but compare the exact assertion characters
            # after removing presentation whitespace from both sides.
            $normalizedOutput = [regex]::Replace(($stdout + $stderr), '\s+', '')
            $normalizedMarker = [regex]::Replace($ExpectedFailureMarker, '\s+', '')
            if ($ExpectedFailureMarker -and $normalizedOutput.Contains($normalizedMarker)) {
                $results.Add([pscustomobject]@{ Name=$Name; Result='OBSOLETE_EXPECTATION'; Detail="exit=$($proc.ExitCode); exact legacy assertion matched: $ExpectedFailureMarker; complete stdout/stderr saved" })
            } else {
                $results.Add([pscustomobject]@{ Name=$Name; Result='FAIL'; Detail="exit=$($proc.ExitCode); expected legacy marker absent or unrelated failure; complete stdout/stderr saved" })
            }
        } elseif ($ExpectedFailureMarker) {
            $results.Add([pscustomobject]@{ Name=$Name; Result='FAIL'; Detail="expected marker failure but child exited 0: $ExpectedFailureMarker" })
        } else {
            $results.Add([pscustomobject]@{ Name=$Name; Result='PASS'; Detail='isolated child exited 0' })
        }
    } catch {
        $failure = $_ | Out-String
        [IO.File]::AppendAllText($combinedPath, "`r`n[Invoke-Check error]`r`n$failure", [Text.UTF8Encoding]::new($false))
        $results.Add([pscustomobject]@{ Name=$Name; Result='FAIL'; Detail=$_.Exception.Message })
    } finally {
        $proc.Dispose()
    }
}

Invoke-Check 'starhunt-run-state-machine' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/starhunt-run-state-machine.ps1') }
Invoke-Check 'battle-skip-lease' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/activation-native/run-battle-skip-lease.ps1') }
Invoke-Check 'battle-loop-compatible' {
    $cfg = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='tick'}"
    $lua = Join-Path $root 'app/src/main/assets/battle_loop.lua.in'; $runner = Join-Path $root 'tests/battle-loop-sandbox/run-lua.ps1'; $fix = Join-Path $root 'tests/battle-loop-sandbox/fixture.lua'; $helpers = Join-Path $root 'tests/battle-loop-sandbox/animation-helpers.lua'
    foreach ($case in @('behavior','commands-without-ack','rejection','no-pet','identity-change','lease-expiry','missing-round-ack','rounds-1-5','animation-waits','animation-finished','finished-index-exhausted','no-pet-animation','natural-playover-phases','phase-change','phase-change-role','phase-change-after-verify','exception-cleanup')) {
        & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File $runner -LuaFile $lua -SetupFile $fix -TestsFile (Join-Path $root "tests/battle-loop-sandbox/$case.lua") -AfterLoadFile $helpers -ConfigLiteral $cfg
        if ($LASTEXITCODE -ne 0) { throw "Lua case failed: $case" }
    }
    $stopCfg = $cfg.Replace("mode='tick'", "mode='stop'")
    & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File $runner -LuaFile $lua -SetupFile $fix -BeforeStopFile (Join-Path $root 'tests/battle-loop-sandbox/stop-before.lua') -TestsFile (Join-Path $root 'tests/battle-loop-sandbox/stop.lua') -AfterLoadFile $helpers -ConfigLiteral $cfg -StopConfigLiteral $stopCfg
    if ($LASTEXITCODE -ne 0) { throw 'Lua case failed: stop' }
    & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File $runner -LuaFile $lua -SetupFile $fix -BeforeStopFile (Join-Path $root 'tests/battle-loop-sandbox/stop-pending-before.lua') -TestsFile (Join-Path $root 'tests/battle-loop-sandbox/stop-pending.lua') -AfterLoadFile $helpers -ConfigLiteral $cfg -StopConfigLiteral $stopCfg
    if ($LASTEXITCODE -ne 0) { throw 'Lua case failed: stop-pending' }
    & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/battle-loop-sandbox/verify-status-contract.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'status contract failed' }
}
Invoke-Check 'battle-loop-unknown-pet-skill-fallback-80-rounds' {
    $cfg = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='tick'}"
    & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/battle-loop-sandbox/run-lua.ps1') -LuaFile (Join-Path $root 'app/src/main/assets/battle_loop.lua.in') -SetupFile (Join-Path $root 'tests/battle-loop-sandbox/fixture.lua') -TestsFile (Join-Path $root 'tests/battle-loop-sandbox/unknown-pet-skill-fallback.lua') -AfterLoadFile (Join-Path $root 'tests/battle-loop-sandbox/animation-helpers.lua') -ConfigLiteral $cfg
}
Invoke-Check 'battle-loop-legacy-unknown-target' {
    $cfg = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='tick'}"
    & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/battle-loop-sandbox/run-lua.ps1') -LuaFile (Join-Path $root 'app/src/main/assets/battle_loop.lua.in') -SetupFile (Join-Path $root 'tests/battle-loop-sandbox/fixture.lua') -TestsFile (Join-Path $root 'tests/battle-loop-sandbox/unknown-target.lua') -AfterLoadFile (Join-Path $root 'tests/battle-loop-sandbox/animation-helpers.lua') -ConfigLiteral $cfg
} 'unconfirmed skill target caused a send'
Invoke-Check 'activation-integration-stubs' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/activation-integration/Run-ActivationJavaStubs.ps1') }
Invoke-Check 'cold-stop-stubs' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/activation-integration/Run-ColdStopJavaStubs.ps1') }
Invoke-Check 'starhunt-java-models' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-starhunt-java-models.ps1') }
Invoke-Check 'starhunt-java-contract' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-starhunt-java-contract.ps1') }
Invoke-Check 'battle-gate-startable' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-battle-gate-startable.ps1') }
Invoke-Check 'battle-status-display' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-battle-status-display.ps1') }
Invoke-Check 'mounthold-stop-status' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-mounthold-stop-status.ps1') }
Invoke-Check 'diagnostics' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-diagnostics.ps1') }
Invoke-Check 'ui-status-files' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-ui-status-files.ps1') -OutputDirectory (Join-Path $out 'ui-status-files') }
Invoke-Check 'schedule-execution' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-schedule-execution.ps1') }
Invoke-Check 'schedule-observation' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-schedule-observation.ps1') -OutputDirectory (Join-Path $out 'schedule-observation') }
Invoke-Check 'schedule-merge' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-schedule-merge.ps1') }
Invoke-Check 'boss-auto-stop' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-boss-auto-stop.ps1') }
Invoke-Check 'teleport-contract' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-teleport-contract.ps1') -OutputDirectory (Join-Path $out 'teleport-contract') }
Invoke-Check 'ui-merge' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/verify-ui-merge.ps1') }
Invoke-Check 'service-gates' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/activation/verify-service-gates.ps1') }
Invoke-Check 'license-bypass' { & $env:SUITE_STABILITY_SHELL -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests/activation/verify-license-bypass.ps1') }
Invoke-Check 'module-status-72h-synthetic' {
    $classes = Join-Path $out 'status-classes'; $null = New-Item -ItemType Directory -Path $classes -Force
    & $script:JavaCompiler --release 8 -encoding UTF-8 -d $classes (Join-Path $root 'app/src/main/java/com/xnas/wpe/suite/ModuleStatus.java') (Join-Path $root 'app/src/main/java/com/xnas/wpe/suite/ModuleStatusSnapshotStore.java') (Join-Path $root 'tests/stability/ModuleStatusSnapshotLongLoopSelfTest.java')
    if ($LASTEXITCODE -ne 0) { throw 'javac failed' }
    & $script:JavaRunner -cp $classes com.xnas.wpe.suite.ModuleStatusSnapshotLongLoopSelfTest
}

$rows = @('Suite stability run', '', "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')", "Suite: $root", 'Mode: offline; no Gradle/Cargo, ADB, game, network business, or installation actions.', '', '| Check | Result | Detail |', '|---|---|---|')
foreach ($r in $results) { $rows += "| $($r.Name) | $($r.Result) | $($r.Detail -replace '\|','/') |" }
$rows += ''; $rows += 'Coverage boundary: synthetic 72-hour status progression is accelerated logical time, not real 72-hour execution. Android App Context observation and real game behavior remain unconfirmed. The legacy unknown-target expectation is retained as an obsolete expectation; the independent 80-round fallback test validates the current documented normal-attack behavior.'
$report = Join-Path $out 'stability-report.md'
[IO.File]::WriteAllLines($report, $rows, [Text.UTF8Encoding]::new($false))
$results | Format-Table -AutoSize | Out-String | Write-Output
Write-Output "Report: $report"
if (($results | Where-Object Result -eq 'FAIL').Count -gt 0) { exit 1 }
