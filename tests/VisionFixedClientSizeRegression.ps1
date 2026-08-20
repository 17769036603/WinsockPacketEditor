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
$hexBoxDll = Join-Path $resolvedBuildDirectory "Be.Windows.Forms.HexBox.dll"
if (-not (Test-Path -LiteralPath $libraryDll)) {
    throw "WPELibrary.dll not found at $libraryDll"
}
if (-not (Test-Path -LiteralPath $hexBoxDll)) {
    throw "HexBox assembly not found at $hexBoxDll"
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function Assert-Contains([string]$text, [string]$expected, [string]$message) {
    if (-not $text.Contains($expected)) {
        throw $message
    }
}

function Read-SourceFile([string]$relativePath) {
    return [System.IO.File]::ReadAllText(
        (Join-Path $repo $relativePath),
        [System.Text.Encoding]::UTF8)
}

$settingsSource = Read-SourceFile "WPELibrary\Lib\Vision\VisionCaptureSettings.cs"
$serviceSource = Read-SourceFile "WPELibrary\Lib\Vision\VisionWindowService.cs"
$runnerSource = Read-SourceFile "WPELibrary\Lib\Vision\VisionAssistantRunner.cs"
$mouseActionSource = Read-SourceFile "WPELibrary\Lib\Vision\VisionMouseAction.cs"
$formSource = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"
$cacheSource = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
Assert-Contains $settingsSource "RequireExactClientSize" `
    "Capture settings must expose exact client-size mode."
Assert-Contains $serviceSource "TryValidateClientSize" `
    "The window service must validate the current client size."
Assert-Contains $runnerSource "must use fixed pixel regions" `
    "The runner must reject normalized regions in exact-size mode."
Assert-Contains $mouseActionSource "TryValidateClientSize" `
    "Mouse actions must validate the live client size before input."
Assert-Contains $formSource "bVisionApplyFixedSize_Click" `
    "The robot form must expose the fixed 1280x720 setup action."
Assert-Contains $formSource "captureSettings.RequireExactClientSize && region.UseNormalizedCoordinates" `
    "The step confirmation UI must reject normalized regions in exact-size mode."
Assert-Contains $cacheSource "CaptureRequiredClientWidth" `
    "SQLite persistence must include the required client width."

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $hexBoxDll
[void][System.Reflection.Assembly]::LoadFrom($libraryDll)

$fakeTypes = @"
using System.Collections.Generic;
using System.Threading;
using WPELibrary.Lib.Vision;

namespace VisionFixedClientSizeRegression
{
    public sealed class TerminalProvider : IVisionObservationProvider
    {
        public int Calls { get; private set; }

        public VisionObservation Observe(
            VisionConditionDefinition condition,
            CancellationToken cancellationToken)
        {
            this.Calls++;
            return VisionObservation.TerminalFailure("fixed client size changed");
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
    [System.Object].Assembly.Location
)
Add-Type -TypeDefinition $fakeTypes -ReferencedAssemblies $framework

$legacySettings = [WPELibrary.Lib.Vision.VisionCaptureSettings]::new()
Assert-True (-not $legacySettings.RequireExactClientSize) `
    "Legacy capture settings must keep exact-size mode disabled by default."

$fixedSettings = [WPELibrary.Lib.Vision.VisionCaptureSettings]::new()
$fixedSettings.RequireExactClientSize = $true
$fixedSettings.RequiredClientWidth = 1280
$fixedSettings.RequiredClientHeight = 720
$fixedSettings.Validate()
$fixedClone = $fixedSettings.Clone()
Assert-True ($fixedClone.RequireExactClientSize -and
    $fixedClone.RequiredClientWidth -eq 1280 -and
    $fixedClone.RequiredClientHeight -eq 720) `
    "Exact client-size settings must survive cloning."
Assert-True $fixedSettings.IsClientSizeMatch([System.Drawing.Size]::new(1280, 720)) `
    "1280x720 must be accepted by exact client-size settings."
Assert-True (-not $fixedSettings.IsClientSizeMatch([System.Drawing.Size]::new(1279, 720))) `
    "1279x720 must be rejected by exact client-size settings."
Assert-True (-not $fixedSettings.IsClientSizeMatch([System.Drawing.Size]::new(1280, 719))) `
    "1280x719 must be rejected by exact client-size settings."
Assert-True (-not $fixedSettings.IsClientSizeMatch([System.Drawing.Size]::new(1024, 576))) `
    "1024x576 must be rejected by exact client-size settings."

$form = [System.Windows.Forms.Form]::new()
try {
    $form.Text = "Fixed client-size regression target"
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = [System.Drawing.Point]::new(120, 80)
    $form.ClientSize = [System.Drawing.Size]::new(1280, 720)
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedToolWindow
    $form.ShowInTaskbar = $false
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()

    $actualSize = [System.Drawing.Size]::Empty
    [string]$reason = ""
    $valid = [WPELibrary.Lib.Vision.VisionWindowService]::TryValidateClientSize(
        $form.Handle,
        $fixedSettings,
        [ref]$actualSize,
        [ref]$reason)
    Assert-True ($valid -and $actualSize.Width -eq 1280 -and $actualSize.Height -eq 720) `
        "A visible 1280x720 target must pass fixed client-size validation."

    $form.ClientSize = [System.Drawing.Size]::new(1279, 720)
    [System.Windows.Forms.Application]::DoEvents()
    $valid = [WPELibrary.Lib.Vision.VisionWindowService]::TryValidateClientSize(
        $form.Handle,
        $fixedSettings,
        [ref]$actualSize,
        [ref]$reason)
    Assert-True ((-not $valid) -and $reason.Contains("1280") -and $reason.Contains("1279") ) `
        "A resized target must fail with both required and actual client sizes."

    $form.ClientSize = [System.Drawing.Size]::new(1280, 720)
    $form.Hide()
    [System.Windows.Forms.Application]::DoEvents()
    $valid = [WPELibrary.Lib.Vision.VisionWindowService]::TryValidateClientSize(
        $form.Handle,
        $fixedSettings,
        [ref]$actualSize,
        [ref]$reason)
    Assert-True (-not $valid) "A hidden target must fail fixed client-size validation."

    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Minimized
    [System.Windows.Forms.Application]::DoEvents()
    $valid = [WPELibrary.Lib.Vision.VisionWindowService]::TryValidateClientSize(
        $form.Handle,
        $fixedSettings,
        [ref]$actualSize,
        [ref]$reason)
    Assert-True (-not $valid) "A minimized target must fail fixed client-size validation."
}
finally {
    if ($null -ne $form) {
        $form.Close()
        $form.Dispose()
    }
}

$provider = [VisionFixedClientSizeRegression.TerminalProvider]::new()
$action = [VisionFixedClientSizeRegression.CountingAction]::new()
$step = [WPELibrary.Lib.Vision.VisionAssistantStep]::new()
$step.Name = "terminal-size-change"
$step.Condition = [WPELibrary.Lib.Vision.VisionConditionDefinition]::new()
$step.Condition.Type = [WPELibrary.Lib.Vision.VisionConditionType]::TextAppears
$step.Condition.Region.X = 0
$step.Condition.Region.Y = 0
$step.Condition.Region.Width = 10
$step.Condition.Region.Height = 10
$step.Condition.TextCondition.ExpectedText = "ready"
$step.Condition.TimeoutMilliseconds = 1000
$step.Condition.PollIntervalMilliseconds = 10
$step.Condition.RequiredConfirmations = 1
$step.Action = $action
$steps = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Vision.VisionAssistantStep]'
$steps.Add($step)
$run = [WPELibrary.Lib.Vision.VisionAssistantStateMachine]::new($provider).Run(
    $steps,
    [System.Threading.CancellationToken]::None)
Assert-True ((-not $run.Succeeded) -and $run.Error -eq "fixed client size changed") `
    "A terminal size failure must stop the state machine immediately."
Assert-True ($provider.Calls -eq 1 -and $action.Executions -eq 0) `
    "A terminal size failure must not retry or execute an action."

Write-Output "Vision fixed client-size regression passed."
