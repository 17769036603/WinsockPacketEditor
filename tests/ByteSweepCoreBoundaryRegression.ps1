param(
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $repo "WPELibrary\bin\ReviewFixRelease"
}

Add-Type -Path (Join-Path $BuildDirectory "WPELibrary.dll")

function Assert-Throws([scriptblock]$action, [Type]$exceptionType, [string]$message) {
    try {
        & $action
    }
    catch {
        $actual = $_.Exception
        if ($actual -is [System.Management.Automation.MethodInvocationException]) {
            $actual = $actual.InnerException
        }
        if ($actual -is $exceptionType) {
            return
        }
    }
    throw $message
}

$sendDelegateType = [System.Func``2].MakeGenericType([byte[]], [bool])
$sent = 0
$send = [System.Management.Automation.LanguagePrimitives]::ConvertTo(
    {
        param([byte[]]$buffer)
        $script:sent++
        return $true
    },
    $sendDelegateType)

$baseline = [byte[]](0x7F, 0x20)
$result = [WPELibrary.Lib.Socket_ByteSweepEngine]::Execute(
    $baseline, 0, 1, 0, $send,
    [System.Threading.CancellationToken]::None, $null)

if ($result.TotalSend -ne 255 -or $result.Success -ne 255 -or $result.Failure -ne 0 -or
    $baseline[0] -ne 0x7F -or $sent -ne 255) {
    throw "Sequential byte sweep must complete all values and restore the source buffer."
}

$cancelSource = New-Object System.Threading.CancellationTokenSource
$cancelledSendCount = 0
$cancelledSend = [System.Management.Automation.LanguagePrimitives]::ConvertTo(
    {
        param([byte[]]$buffer)
        $script:cancelledSendCount++
        if ($script:cancelledSendCount -eq 3) {
            $script:cancelSource.Cancel()
        }
        return $true
    },
    $sendDelegateType)
$cancelled = [WPELibrary.Lib.Socket_ByteSweepEngine]::Execute(
    [byte[]](0x01), 0, 1, 0, $cancelledSend,
    $cancelSource.Token, $null)
$cancelSource.Dispose()

if (-not $cancelled.Cancelled -or $cancelled.TotalSend -ne 3) {
    throw "Sequential byte sweep cancellation must stop before the next send."
}

Assert-Throws {
    [WPELibrary.Lib.Socket_ByteSweepEngine]::Execute($null, 0, 1, 0, $send, [System.Threading.CancellationToken]::None, $null)
} ([ArgumentNullException]) "Null sweep buffers must be rejected."
Assert-Throws {
    [WPELibrary.Lib.Socket_ByteSweepEngine]::Execute([byte[]](0x01), 1, 1, 0, $send, [System.Threading.CancellationToken]::None, $null)
} ([ArgumentOutOfRangeException]) "Out-of-range sweep positions must be rejected."
Assert-Throws {
    [WPELibrary.Lib.Socket_ByteSweepEngine]::Execute([byte[]](0x01), 0, 1, -1, $send, [System.Threading.CancellationToken]::None, $null)
} ([ArgumentOutOfRangeException]) "Negative sweep intervals must be rejected."
Assert-Throws {
    [WPELibrary.Lib.Socket_ByteSweepEngine]::ExecutePairCombination([byte[]](0x01, 0x02), 0, 1, 0, 0, 1, 0, $send, [System.Threading.CancellationToken]::None, $null)
} ([ArgumentOutOfRangeException]) "Pair sweeps must reject duplicate byte positions."

$preset = [WPELibrary.Lib.Socket_ByteSweepPresetInfo]::new()
$preset.BID = [Guid]::NewGuid()
$preset.BName = "core-boundary"
$preset.BFolder = "regression"
$preset.Buffer = [byte[]](0x10, 0x20, 0x30)
$preset.BStart = 0
$preset.BLength = 2
$preset.BLoopCount = 2
$preset.BInterval = 7
$preset.BNextInterval = 11
$preset.BCombinationFirstPosition = 0
$preset.BCombinationFirstLength = 3
$preset.BCombinationFirstInterval = 13
$preset.BCombinationSecondPosition = 1
$preset.BCombinationSecondLength = 4
$preset.BCombinationSecondInterval = 17

if (-not $preset.IsValid -or $preset.TotalSend -ne 1020) {
    throw "A complete sequential preset must be valid and calculate its send total."
}

$clone = $preset.Clone()
$clone.Buffer[0] = 0xFF
$clone.BName = "clone"
if ($preset.Buffer[0] -ne 0x10 -or $preset.BName -ne "core-boundary") {
    throw "Preset cloning must not alias the source buffer or metadata."
}

$runtime = [WPELibrary.Lib.Socket_ByteSweepRuntime]::new()
$jobId = [Guid]::Empty
$jobCancellation = $null
if (-not $runtime.TryStart($preset.BID, $preset.BName, "Sequential", 0, -1, [ref]$jobId, [ref]$jobCancellation) -or
    $jobId -eq [Guid]::Empty -or -not $runtime.IsBusy) {
    throw "Runtime must accept the first job and enter a busy state."
}

$wrongId = [Guid]::NewGuid()
if ($runtime.RequestStop($wrongId)) {
    throw "Runtime must ignore stop requests for another job."
}
if (-not $runtime.MarkRunning($jobId) -or -not $runtime.RequestStop($jobId) -or -not $runtime.IsBusy) {
    throw "Runtime must transition through running and stopping states."
}
$runtime.Finish($jobId, $true, $null, "regression-cancel")
if ($runtime.IsBusy) {
    throw "Runtime must leave the busy state after finishing a cancelled job."
}

$nextJobId = [Guid]::Empty
$nextCancellation = $null
if (-not $runtime.TryStart([Guid]::NewGuid(), "next", "PairCombination", 1, 1, [ref]$nextJobId, [ref]$nextCancellation)) {
    throw "Runtime must allow a new job after the previous job finishes."
}
$runtime.Finish($nextJobId, $false, $null, "regression-complete")

Write-Output "Byte-sweep core boundary regression checks passed."
