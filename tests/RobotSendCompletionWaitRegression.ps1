param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Read-SourceFile {
    param([string]$RelativePath)

    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $RelativePath"
    }

    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Expected,
        [string]$Message
    )

    if (-not $Text.Contains($Expected)) {
        throw $Message
    }
}

function Assert-NotContains {
    param(
        [string]$Text,
        [string]$Unexpected,
        [string]$Message
    )

    if ($Text.Contains($Unexpected)) {
        throw $Message
    }
}

$robot = Read-SourceFile "WPELibrary\Lib\Socket_Robot.cs"
$send = Read-SourceFile "WPELibrary\Lib\Socket_Send.cs"
$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$robotForm = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"

$targetPresetId = "BDEE8CE8-6B2D-485D-9A88-4C9A9261A8DF"
Assert-Contains $robot $targetPresetId `
    "The fast completion path must identify the exact target preset GUID."
Assert-Contains $robot "private static readonly Guid FastCompletionWaitRobotPresetId" `
    "The fast completion path must use a fixed per-preset identity gate."
Assert-Contains $robot "Guid robotPresetId," `
    "Robot startup must accept the persistent preset identity."
Assert-Contains $robot "this.RobotPresetId = robotPresetId;" `
    "Robot execution must retain the persistent preset identity."
Assert-Contains $robot "if (this.ShouldUseFastCompletionWait())" `
    "The fast completion path must be conditional on the target preset."
Assert-Contains $robot "if (!ss.WaitForCompletion(this.cts.Token))" `
    "The target preset must wait on the send completion signal with cancellation."
Assert-Contains $robot "Thread.Sleep(100);" `
    "Non-target robot presets must retain their existing completion polling path."
Assert-Contains $robot "return this.StartRobot(" `
    "Callers without a preset identity must not receive the target-only fast path."
Assert-Contains $robot "Guid.Empty," `
    "Callers without a preset identity must not receive the target-only fast path."

Assert-Contains $send "public bool WaitForCompletion(CancellationToken cancellationToken)" `
    "Send workers must expose a cancellation-aware completion wait."
Assert-Contains $send "this.sendStopped.Wait(cancellationToken);" `
    "Cancellation-aware completion must wait on the send completion signal."

Assert-Contains $cache "srReturn.StartRobot(robotName, RID, instruction, parameters)" `
    "Cached robot starts must propagate the persistent preset identity."
Assert-Contains $robotForm "sr.StartRobot(sriSelect.RName, sriSelect.RID, this.dtRobotInstruction, parameters)" `
    "Interactive robot starts must propagate the persistent preset identity."

$sendListBlock = [regex]::Match(
    $robot,
    '(?s)case Socket_Cache\.Robot\.InstructionType\.SendSendList:.*?case Socket_Cache\.Robot\.InstructionType\.SendSocketList:').Value
if ([string]::IsNullOrWhiteSpace($sendListBlock)) {
    throw "Could not isolate the send-list instruction execution block."
}
Assert-Contains $sendListBlock "WaitForCompletion(this.cts.Token)" `
    "The target send-list path must use signal-based completion."
Assert-Contains $sendListBlock "while (ss.Worker.IsBusy)" `
    "The non-target send-list path must remain available."
Assert-Contains $sendListBlock "Thread.Sleep(100);" `
    "The non-target send-list path must retain its original polling interval."

Assert-NotContains $send "Thread.Sleep(100);" `
    "The shared send worker must not own the robot-only 100ms completion poll."

Write-Output "RobotSendCompletionWaitRegression: PASS"
