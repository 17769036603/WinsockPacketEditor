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
$form = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"
$process = Read-SourceFile "WPELibrary\Lib\MountSpeed\MountGrowthRateHoldProcess.cs"
$python = Read-SourceFile "tools\mount-reader\mount_growth_rate_memory_test.py"
$appProject = Read-SourceFile "WinsockPacketEditor\WinsockPacketEditor.csproj"

Assert-Contains $robot "TryPrepareMountGrowthRateHoldRuntime()" "Growth-rate preset must preflight before robot start."
Assert-Contains $robot "snapshot.CandidateCount != 1" "Growth-rate preflight must require one player candidate."
Assert-Contains $robot "snapshot.MountId.Value <= 0" "Growth-rate preflight must reject an invalid mount ID."
Assert-Contains $robot 'RideBindingStatus, "bound"' "Growth-rate preflight must require a bound current ride."
Assert-Contains $robot "GrowthRateMemoryAddress.Value == 0" "Growth-rate preflight must require a valid field address."
Assert-Contains $robot "case Socket_Cache.Robot.InstructionType.MountGrowthRateHold" "Growth-rate preset needs its own instruction branch."
Assert-Contains $robot "RunMountGrowthRateHoldInstruction(sContent)" "Growth-rate hold must run its lifecycle handler."
Assert-Contains $robot "MountGrowthRateHoldProcess.SetAndRestore" "Robot must use guarded set-and-restore process."
Assert-Contains $robot "this.cts == null ? CancellationToken.None : this.cts.Token" "Robot stop must cancel the hold and trigger restoration."
Assert-Contains $robot 'restoreStatus, "restored"' "A run must surface an unconfirmed restore."
Assert-Contains $robot '"not_needed"' "An already-100 startup value may skip restoration only after a verified no-op."
Assert-Contains $robot "!writeAttemptedForSuccess" "A no-op restore status must not be accepted after a write attempt."
Assert-Contains $robot "this.mountGrowthRateHoldCleanupFailed = true" "Ambiguous restore must not be hidden as a normal cancellation."
Assert-Contains $form "this.HasMountGrowthRateHoldInstructionRows()" "The robot form must create a per-run snapshot reader for this preset."
Assert-Contains $process "cancellationToken.WaitHandle.WaitOne()" "The memory value must stay held until the robot is stopped."
Assert-Contains $process "finally" "Restore must run after cancellation or an operation error."
Assert-Contains $process '"restore"' "The hold process must invoke the dedicated restore action."
Assert-Contains $python 'saved.get("rideInstanceId") != str(args.ride_instance_id)' "Restore must be tied to the original ride instance."
Assert-Contains $python '"current ride growth differs from the journaled target"' "Restore must skip if current growth is no longer the tool target."
Assert-Contains $python "float(after_growth) != old_number" "Restore must verify the original value on a fresh bound ride after writing."
Assert-Contains $appProject "mount_growth_rate_memory_test.py" "The app package must include the growth-rate script used at runtime."

Write-Output "MountGrowthRateHoldRobotRegression: PASS"
