param(
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $repo "WPELibrary\bin\PauseFeatureRelease"
}

Add-Type -Path (Join-Path $BuildDirectory "WPELibrary.dll")

Add-Type -ReferencedAssemblies (Join-Path $BuildDirectory "WPELibrary.dll") -TypeDefinition @'
using System;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib;

public static class ByteSweepPauseProbe
{
    public static int SendCount;
    private static ManualResetEventSlim PauseGate;

    public static Task<Socket_ByteSweepResult> Start(ManualResetEventSlim pauseGate)
    {
        PauseGate = pauseGate;
        return Task.Factory.StartNew(() => Socket_ByteSweepEngine.Execute(
            new byte[] { 0x10 },
            0,
            1,
            0,
            Send,
            CancellationToken.None,
            null,
            PauseGate.WaitHandle));
    }

    private static bool Send(byte[] buffer)
    {
        Interlocked.Increment(ref SendCount);
        if (SendCount == 5)
        {
            PauseGate.Reset();
        }
        return true;
    }
}
'@

$pauseGate = New-Object System.Threading.ManualResetEventSlim($true)
$task = [ByteSweepPauseProbe]::Start($pauseGate)
$deadline = [DateTime]::UtcNow.AddSeconds(5)
while ([ByteSweepPauseProbe]::SendCount -lt 5 -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 10
}
if ([ByteSweepPauseProbe]::SendCount -ne 5) {
    throw "The pause probe did not reach its mid-sweep pause point."
}

Start-Sleep -Milliseconds 150
if ([ByteSweepPauseProbe]::SendCount -ne 5) {
    throw "A paused byte sweep must not send while the gate is closed."
}

$pauseGate.Set()
$completed = $false
try {
    $completed = $task.Wait(5000)
}
catch {
    throw $task.Exception.InnerException
}
if (-not $completed) {
    throw "The paused byte sweep did not resume after the gate was opened."
}

$result = $task.Result
$pauseGate.Dispose()
if ($result.TotalSend -ne 255 -or [ByteSweepPauseProbe]::SendCount -ne 255 -or $result.Cancelled) {
    throw "A resumed byte sweep must continue from the paused worker and complete once."
}

Write-Output "Byte-sweep pause/resume regression checks passed."
