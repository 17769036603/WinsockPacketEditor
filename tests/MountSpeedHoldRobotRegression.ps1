param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Read-SourceFile {
    param([string]$RelativePath)
    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing source file: $RelativePath" }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Assert-Contains {
    param([string]$Text, [string]$Expected, [string]$Message)
    if (-not $Text.Contains($Expected)) { throw $Message }
}

$robot = Read-SourceFile "WPELibrary\Lib\Socket_Robot.cs"
$reader = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountStatusAndroidSnapshotReader.cs"
$robotForm = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"
$runner = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountSpeedHoldProcess.cs"
$supervisor = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountSpeedHoldRestartSupervisor.cs"
$snapshot = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountStatusReadOnlySnapshot.cs"
Assert-Contains $runner '"0.1"' "The persistent speed hold must use the supported 100 ms recheck interval."
$quoting = Read-SourceFile "tests\MountSpeedHoldArgumentQuotingRegression.cs"
$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$python = Read-SourceFile "tools\mount-reader\mount_speed_memory_test.py"
$probe = Read-SourceFile "tools\mount-reader\mount_status_luajit_probe.py"
$project = Read-SourceFile "WPELibrary\WPELibrary.csproj"
$appProject = Read-SourceFile "WinsockPacketEditor\WinsockPacketEditor.csproj"

Assert-Contains $robot "TryParseMountSpeedHoldTarget" "Hold target must be validated before startup."
Assert-Contains $robot "result.Snapshot.IsMounted != true" "Hold requires a fresh mounted snapshot."
Assert-Contains $robot '启动前读取失败：' "Reader and Android infrastructure failures must not be described as mount-state failures."
Assert-Contains $robot '坐骑速度保持启动前快照无效。' "An invalid or missing snapshot must have its own diagnostic."
Assert-Contains $robot '启动前检查失败：请先骑乘并确认已绑定坐骑。' "Only an unmounted snapshot should prompt the user to ride."
Assert-Contains $robot '启动前未能确认当前坐骑 ID。' "A missing mount ID must be reported as unconfirmed."
Assert-Contains $robot '启动前进程绑定不符：' "A process binding mismatch must have its own diagnostic."
Assert-Contains $robot 'string.IsNullOrWhiteSpace(result.Error)' "Hold preflight failures must preserve the reader error when present."
Assert-Contains $robot "TryPrepareMountSpeedHoldRuntime()" "Hold must preflight before worker startup."
Assert-Contains $robot 'MountSpeedHoldPreflightSnapshot' "The verified startup snapshot must be passed to the hold child."
Assert-Contains $robot 'reader.ReadSpeedOnlySnapshotAsync(cancellationToken)' "Speed hold preflight must use the minimal speed-only snapshot and observe cancellation."
Assert-Contains $robot "case Socket_Cache.Robot.InstructionType.MountSpeedHold" "Hold needs its own robot instruction branch."
Assert-Contains $robot "RunMountSpeedHoldInstruction(sContent)" "Hold must not run through the ordinary MountSpeed plan."
Assert-Contains $robot "string mountSpeedHoldPreflightStatus = DescribeCurrentMountSnapshot(result.Snapshot)" "Mount preflight diagnostics must be logged locally, not stored as a pet read-only popup status."
Assert-Contains $robot "Socket_Operation.DoLog(" "Mount preflight status must be available in the system log."
Assert-Contains $robot '"MountSpeedHold",' "Mount preflight status must be logged under its own subsystem."
if ($robot.Contains('ReadOnlySnapshotStatus = DescribeCurrentMountSnapshot(result.Snapshot)')) { throw "Mount hold preflight must not trigger the summoned-pet read-only popup." }
Assert-Contains (Read-SourceFile "WPELibrary\Socket_Form.cs") "completedRobot.LastRunFailureReason" "Mount hold worker failures must continue to reach the normal failure presentation."
Assert-Contains $reader "RootBrokerPipe = rootBrokerPipe" "Hold must reuse the reader's root broker pipe."
Assert-Contains $reader "LastSuccessfulLaunchContext" "Reader must expose its last successful launch context."
Assert-Contains $reader "ProbeCachePath = this.lastProbeCachePath" "A successful preflight must retain the base probe cache path for the hold helper."
Assert-Contains $reader "ReleaseProbeWorkerForExternalClient" "Preflight must release the single root broker pipe client for the hold child."
Assert-Contains $reader 'probeRequest["speedOnly"] = speedOnly' "The desktop probe bridge must forward speed-only requests."
Assert-Contains $reader 'probeArguments.Add("--speed-only")' "The one-shot probe fallback must preserve speed-only mode."
Assert-Contains $reader 'if (!speedOnly && IsTransientMountedSnapshot(snapshot)' "Speed-only snapshots must not retry for skipped ride-skill data."
Assert-Contains $robot "reader.ReleaseProbeWorkerForExternalClient()" "Hold startup must release the preflight root broker client."
Assert-Contains $robot '"com.gdoo.dhxy"' "The hold preset must find the active game package when no package override is configured."
Assert-Contains $robot 'new[] { "com.gdoo.xxxy2026" }' "Only the mount speed hold reader may use the exact xxxy2026 package fallback."
Assert-Contains $robot "MountSpeedHoldRestartSupervisor.Run(" "The robot runtime must use the tested mount hold restart supervisor."
Assert-Contains $robot "MountSpeedHoldRestartSupervisor.IsEligible(" "Restart must be limited by the exact hold preset gate."
Assert-Contains $robot 'this.RobotName,' "Restart eligibility must use this robot's exact preset name."
Assert-Contains $robot 'this.RobotPresetId,' "Restart eligibility must use this robot's persistent GUID."
Assert-Contains $robot 'instructionCount,' "Restart eligibility must require one instruction row."
Assert-Contains $robot 'TryPrepareMountSpeedHoldRuntime(cancellationToken)' "Every restart must repeat cancellable mount preflight."
Assert-Contains $robot 'hold.SafeToRetry && restartEligible)' "A safe exit remains a safe outcome when cancellation races with it; the supervisor cancellation gate prevents restart."
Assert-Contains $robot 'retryNumber = retryNumber' "Restart logs must include the retry number."
Assert-Contains $robot 'delayMilliseconds = retryDelayMilliseconds' "Restart logs must include the backoff delay."
Assert-Contains $robot 'previousRunId = retryPreviousRunId.ToString("D")' "Restart logs must link the prior child run."
Assert-Contains $robot '"retryPreflightCompleted" : "retryPreflightFailed"' "Each retry must log the new preflight outcome under its new run ID."
if ($supervisor.Contains("MaximumAutomaticRestarts")) { throw "Safe recovered failures must not stop at a configured restart count." }
Assert-Contains $supervisor 'Math.Min(5000, 250L << Math.Min(retryCount - 1, 5))' "Backoff must reach and cap at 5000 ms."
Assert-Contains $supervisor 'archiveIndex <= 20' "Safe runEnd lookup must include all retained JSONL archives."
Assert-Contains $reader 'this.fallbackPackages = fallbackPackages == null' "Fallback package candidates must be copied into each reader instance."
Assert-Contains $reader 'configuredPackage,' "Configured package candidates must retain priority over fallback candidates."
Assert-Contains $reader 'Environment.GetEnvironmentVariable("WPE_MOUNT_STATUS_GAME_PACKAGE")' "Environment package override must retain priority over fallback candidates."
if ($robotForm.Contains("InstructionType.MountSpeedHold")) { throw "The robot editor must let Socket_Robot create the hold-specific reader." }
Assert-Contains $runner "--persistent" "Child must run until cooperative stop or EOF."
Assert-Contains $runner '"--preflight-speed-address"' "A valid preflight binding must avoid the child's duplicate initial snapshot."
Assert-Contains $runner '"--probe-cache-path"' "Hold must pass the preflight cache path to its fresh snapshot probe."
Assert-Contains $snapshot "RoleMoveSpeedMemoryAddress" "The preflight snapshot must retain the speed field address."
Assert-Contains $runner "RedirectStandardOutput = true" "Child stdout must be drained asynchronously."
Assert-Contains $runner "RedirectStandardError = true" "Child stderr must be drained asynchronously."
Assert-Contains $runner 'WriteLine("stop")' "Robot stop must send the cooperative stop command."
Assert-Contains $runner "StandardInput.Close()" "Robot stop must follow the stop command with EOF."
Assert-Contains $runner "this.process.WaitForExit()" "Robot completion must wait for normal child cleanup."
Assert-Contains $runner "File.Exists(this.JournalPath)" "Nonzero child exit must check whether the recovery journal exists."
Assert-Contains $runner "恢复日志已保留于" "An existing recovery journal must be reported as retained."
Assert-Contains $runner "未找到恢复日志" "A missing recovery journal must be reported without implying restoration."
Assert-Contains $runner "backslashes * 2 + 1" "Windows argv quoting must escape backslashes only before embedded quotes."
Assert-Contains $runner "backslashes * 2)" "Windows argv quoting must escape trailing backslashes before the closing quote."
Assert-Contains $runner "WPE_MOUNT_SPEED_MEMORY_TEST_PY" "A custom probe without a colocated writer must support an explicit writer path."
Assert-Contains $runner "OutputDataReceived += this.OnOutput" "Child output must be drained asynchronously."
Assert-Contains $runner "ErrorDataReceived += this.OnError" "Child stderr must be captured separately from stdout."
Assert-Contains $runner "MaxExitStderrCharacters = 4096" "Exit diagnostics must retain only a bounded stderr tail."
Assert-Contains $runner 'exitData.Add("stderrDiagnostic", stderrDiagnostic)' "Only nonzero helper exits must persist the bounded stderr diagnostic."
Assert-Contains $runner "this.stderrOutput.Remove(0, this.stderrOutput.Length - MaxExitStderrCharacters)" "Stderr capture must retain the newest diagnostic tail."
Assert-Contains $robot "hold.Dispose()" "Startup or wait exceptions must release the child through cooperative cleanup."
Assert-Contains $robot "this.cts.Cancel()" "Robot stop must wake the hold worker."
Assert-Contains $python "persistent hold accepts only stop or EOF on stdin" "Python hold must accept the documented cooperative stop protocol."
Assert-Contains $probe 'payload.get("speedOnly", getattr(args, "speed_only", False))' "The JSONL probe server must apply the requested speed-only mode."
Assert-Contains $python "line = sys.stdin.readline()" "Python hold must read stop and EOF from stdin."
Assert-Contains $python "--root-broker-pipe" "Python hold must reuse the root broker pipe."
Assert-Contains $python 'parser.add_argument("--probe-cache-path", dest="probe_cache_path", default=""' "The helper cache path option must default empty for legacy invocations."
Assert-Contains $python 'cache_path=getattr(args, "probe_cache_path", "")' "Hold snapshots must pass the base cache path through to the probe."
Assert-Contains $project "Lib\MountSpeed\MountSpeedHoldProcess.cs" "The hold process implementation must be compiled into WPELibrary."
Assert-Contains $project "Lib\MountSpeed\MountSpeedHoldRestartSupervisor.cs" "The restart supervisor must be compiled into WPELibrary."
Assert-Contains $appProject "mount_speed_memory_test.py" "The hold writer must ship next to the Android read probe."
Assert-Contains $appProject "mount_growth_rate_memory_test.py" "The one-shot growth writer must ship next to the Android read probe."
Assert-Contains $cache "MountSpeedHold = 16" "Hold must use its isolated instruction type."
Assert-Contains $quoting "Windows argv round-trip failed" "Windows command line quoting must have a runtime round-trip regression."

Write-Output "MountSpeedHoldRobotRegression: PASS"
