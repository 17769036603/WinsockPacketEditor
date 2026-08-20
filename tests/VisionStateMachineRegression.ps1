param(
    [string]$Configuration = "Debug",
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$resolvedBuildDirectory = if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    Join-Path $repo "WPELibrary\bin\$Configuration"
}
else {
    [System.IO.Path]::GetFullPath($BuildDirectory)
}
$libraryDll = Join-Path $resolvedBuildDirectory "WPELibrary.dll"
if (-not (Test-Path -LiteralPath $libraryDll)) {
    throw "WPELibrary.dll not found at $libraryDll"
}

Add-Type -AssemblyName System.Drawing
[void][System.Reflection.Assembly]::LoadFrom($libraryDll)

$fakeTypes = @"
using System.Collections.Generic;
using System.Threading;
using WPELibrary.Lib.Vision;

namespace VisionStateMachineRegression
{
    public sealed class QueueProvider : IVisionObservationProvider
    {
        private readonly Queue<VisionObservation> observations = new Queue<VisionObservation>();
        private VisionObservation lastObservation;

        public int Calls { get; private set; }

        public void Add(VisionObservation observation)
        {
            this.observations.Enqueue(observation);
        }

        public VisionObservation Observe(
            VisionConditionDefinition condition,
            CancellationToken cancellationToken)
        {
            this.Calls++;
            if (this.observations.Count > 0)
            {
                this.lastObservation = this.observations.Dequeue();
            }
            return this.lastObservation ?? VisionObservation.Failed("No synthetic observation.");
        }
    }

    public sealed class CountingAction : IVisionAssistantAction
    {
        public int Executions { get; private set; }

        public VisionAssistantActionResult Execute(
            VisionAssistantActionContext context,
            CancellationToken cancellationToken)
        {
            this.Executions++;
            return VisionAssistantActionResult.Succeeded();
        }
    }
}
"@
$framework = @(
    $libraryDll,
    [System.Object].Assembly.Location,
    [System.Linq.Enumerable].Assembly.Location
)
Add-Type -TypeDefinition $fakeTypes -ReferencedAssemblies $framework

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function New-TextCondition {
    param(
        [int]$Confirmations = 3,
        [int]$Timeout = 200,
        [int]$Retries = 0,
        [WPELibrary.Lib.Vision.VisionFailurePolicy]$FailurePolicy =
            [WPELibrary.Lib.Vision.VisionFailurePolicy]::Stop
    )

    $condition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
    $condition.Region.X = 0
    $condition.Region.Y = 0
    $condition.Region.Width = 10
    $condition.Region.Height = 10
    $condition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TextAppears
    $condition.TextCondition.ExpectedText = "任务完成"
    $condition.TextCondition.MinimumConfidence = 0
    $condition.RequiredConfirmations = $Confirmations
    $condition.PollIntervalMilliseconds = 10
    $condition.TimeoutMilliseconds = $Timeout
    $condition.MaxRetries = $Retries
    $condition.FailurePolicy = $FailurePolicy
    return $condition
}

$waiting = [WPELibrary.Lib.Vision.VisionObservation]::FromOcr(
    [WPELibrary.Lib.Vision.VisionOcrResult]::Succeeded("等待", 0.95))
$completed = [WPELibrary.Lib.Vision.VisionObservation]::FromOcr(
    [WPELibrary.Lib.Vision.VisionOcrResult]::Succeeded("任务完成", 0.95))

$provider = [VisionStateMachineRegression.QueueProvider]::new()
$provider.Add($waiting)
$provider.Add($waiting)
$provider.Add($completed)
$provider.Add($completed)
$provider.Add($completed)
$action = [VisionStateMachineRegression.CountingAction]::new()
$step = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
$step.Name = "complete"
$step.Condition = New-TextCondition
$step.Action = $action
$steps = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Vision.VisionAssistantStep]'
$steps.Add($step)
$machine = [WPELibrary.Lib.Vision.VisionAssistantStateMachine]::new($provider)
$run = $machine.Run($steps, [System.Threading.CancellationToken]::None)
Assert-True $run.Succeeded "The state machine must advance after consecutive confirmations."
Assert-True ($run.CompletedSteps -eq 1 -and $action.Executions -eq 1) `
    "A confirmed condition must execute its action exactly once."
Assert-True ($provider.Calls -eq 5) `
    "A false observation must reset consecutive confirmation counting."

$verificationProvider = [VisionStateMachineRegression.QueueProvider]::new()
$verificationProvider.Add($completed)
$verificationProvider.Add($waiting)
$verificationProvider.Add($completed)
$verificationProvider.Add($completed)
$verificationProvider.Add($completed)
$verificationAction = [VisionStateMachineRegression.CountingAction]::new()
$verificationStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
$verificationStep.Name = "verify-after-action"
$verificationStep.Condition = New-TextCondition -Confirmations 1
$verificationStep.Action = $verificationAction
$verificationStep.VerificationEnabled = $true
$verificationStep.Verification = New-TextCondition -Confirmations 3
$verificationSteps = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Vision.VisionAssistantStep]'
$verificationSteps.Add($verificationStep)
$verificationRun = [WPELibrary.Lib.Vision.VisionAssistantStateMachine]::new($verificationProvider).Run(
    $verificationSteps,
    [System.Threading.CancellationToken]::None)
Assert-True ($verificationRun.Succeeded -and $verificationAction.Executions -eq 1) `
    "Action verification must complete after the action and execute the action only once."

$skipProvider = [VisionStateMachineRegression.QueueProvider]::new()
$skipStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
$skipStep.Name = "skip-on-timeout"
$skipStep.Condition = New-TextCondition -Confirmations 1 -Timeout 30 -Retries 1 `
    -FailurePolicy ([WPELibrary.Lib.Vision.VisionFailurePolicy]::Skip)
$skipSteps = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Vision.VisionAssistantStep]'
$skipSteps.Add($skipStep)
$skipRun = [WPELibrary.Lib.Vision.VisionAssistantStateMachine]::new($skipProvider).Run(
    $skipSteps,
    [System.Threading.CancellationToken]::None)
Assert-True ($skipRun.Succeeded -and $skipRun.SkippedStep) `
    "A configured timeout skip must complete without executing the step."

$stopProvider = [VisionStateMachineRegression.QueueProvider]::new()
$stopStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
$stopStep.Name = "stop-on-timeout"
$stopStep.Condition = New-TextCondition -Confirmations 1 -Timeout 30
$stopSteps = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Vision.VisionAssistantStep]'
$stopSteps.Add($stopStep)
$stopRun = [WPELibrary.Lib.Vision.VisionAssistantStateMachine]::new($stopProvider).Run(
    $stopSteps,
    [System.Threading.CancellationToken]::None)
Assert-True ((-not $stopRun.Succeeded) -and $stopRun.FailedStepIndex -eq 0) `
    "A configured timeout stop must fail the run at the waiting step."

$cancelProvider = [VisionStateMachineRegression.QueueProvider]::new()
$cancelStep = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
$cancelStep.Name = "cancelled"
$cancelStep.Condition = New-TextCondition -Confirmations 1 -Timeout 100
$cancelSteps = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Vision.VisionAssistantStep]'
$cancelSteps.Add($cancelStep)
$cancelSource = [System.Threading.CancellationTokenSource]::new()
$cancelSource.Cancel()
try {
    $cancelRun = [WPELibrary.Lib.Vision.VisionAssistantStateMachine]::new($cancelProvider).Run(
        $cancelSteps,
        $cancelSource.Token)
    Assert-True $cancelRun.Cancelled "A cancelled token must stop the state machine immediately."
}
finally {
    $cancelSource.Dispose()
}

Write-Output "Vision state machine regression passed."
