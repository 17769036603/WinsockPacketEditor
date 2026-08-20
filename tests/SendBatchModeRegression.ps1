param(
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

function Assert-Contains([string]$text, [string]$expected, [string]$message) {
    if (-not $text.Contains($expected)) {
        throw $message
    }
}

$source = Get-Content (
    Join-Path $repo "WPELibrary\Socket_Form.cs"
) -Raw -Encoding UTF8

Assert-Contains $source `
    'private ToolStripButton tsSendListParallel;' `
    "The send-list toolbar must expose a send-mode switch."
Assert-Contains $source `
    'CheckOnClick = true,' `
    "The send-list mode switch must be user-toggleable."
Assert-Contains $source `
    'Task[] tasks = queue' `
    "Concurrent mode must create one task per selected send item."
Assert-Contains $source `
    'Task.WaitAll(tasks);' `
    "Concurrent mode must wait for all selected send items together."
Assert-Contains $source `
    'this.sendListParallelMode = this.tsSendListParallel != null' `
    "The selected send mode must be read when a batch starts."
Assert-Contains $source `
    'this.StopActiveBatchSends();' `
    "Stopping a batch must request cancellation for all active send items."
Assert-Contains $source `
    'private readonly Dictionary<Guid, Socket_Send> activeBatchSendOperations' `
    "The batch must track more than one active send operation."

if (-not [string]::IsNullOrWhiteSpace($BuildDirectory)) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -Path (Join-Path $BuildDirectory "Be.Windows.Forms.HexBox.dll")
    Add-Type -Path (Join-Path $BuildDirectory "WPELibrary.dll")

    $form = [WPELibrary.Socket_Form]::new()
    try {
        $flags = [System.Reflection.BindingFlags]::Instance -bor
            [System.Reflection.BindingFlags]::NonPublic
        $modeField = $form.GetType().GetField("tsSendListParallel", $flags)
        $mode = $modeField.GetValue($form)
        if ($null -eq $mode -or -not $mode.CheckOnClick -or $mode.Checked) {
            throw "The send-list mode control must start unchecked and toggleable."
        }

        $initialText = $mode.Text
        $mode.PerformClick()
        if (-not $mode.Checked -or $mode.Text -eq $initialText) {
            throw "The send-list mode control must switch to concurrent mode."
        }
        $mode.PerformClick()
        if ($mode.Checked -or $mode.Text -ne $initialText) {
            throw "The send-list mode control must switch back to sequential mode."
        }
    }
    finally {
        $form.Dispose()
    }
}

Write-Output "Send-list batch mode regression checks passed."
