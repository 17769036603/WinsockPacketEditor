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
    Join-Path $repo "WPELibrary\Socket_Form.ByteSweep.cs"
) -Raw -Encoding UTF8
[xml](Get-Content (
    Join-Path $repo "WPELibrary\Properties\Resources.resx"
) -Raw -Encoding UTF8) | Out-Null
[xml](Get-Content (
    Join-Path $repo "WPELibrary\Properties\Resources.en-US.resx"
) -Raw -Encoding UTF8) | Out-Null

Assert-Contains $source `
    'private ToolStripButton tsByteSweepParallel;' `
    "Byte-sweep toolbar must expose a send-mode switch."
Assert-Contains $source `
    'this.tsByteSweepParallel.CheckOnClick = true;' `
    "The send-mode switch must be user-toggleable."
Assert-Contains $source `
    'Task<Socket_ByteSweepResult>[] tasks = presets' `
    "Concurrent mode must create one task per selected preset."
Assert-Contains $source `
    'Socket_ByteSweepResult[] results = await Task.WhenAll(tasks);' `
    "Concurrent mode must await all selected preset tasks together."
Assert-Contains $source `
    'if (!this.byteSweepParallelMode)' `
    "Live packet preview must stay disabled for concurrent presets."
Assert-Contains $source `
    'await Task.Delay(preset.BNextInterval, this.byteSweepCts.Token);' `
    "Sequential mode must retain the per-preset next interval."
Assert-Contains $source `
    'this.byteSweepParallelMode = this.tsByteSweepParallel != null' `
    "The selected send mode must be read when a batch starts."
Assert-Contains $source `
    'this.SelectActiveByteSweepPresetRow();' `
    "The sweep list must select the preset that is currently sending after refresh."
Assert-Contains $source `
    'preset.BID != this.activeByteSweepPresetId' `
    "The sweep list selection must follow the active preset identity."

if (-not [string]::IsNullOrWhiteSpace($BuildDirectory)) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -Path (Join-Path $BuildDirectory "Be.Windows.Forms.HexBox.dll")
    Add-Type -Path (Join-Path $BuildDirectory "WPELibrary.dll")

    $form = [WPELibrary.Socket_Form]::new()
    try {
        $flags = [System.Reflection.BindingFlags]::Instance -bor
            [System.Reflection.BindingFlags]::NonPublic
        $modeField = $form.GetType().GetField("tsByteSweepParallel", $flags)
        $mode = $modeField.GetValue($form)
        if ($null -eq $mode -or -not $mode.CheckOnClick -or $mode.Checked) {
            throw "The batch send mode control must start unchecked and toggleable."
        }

        $initialText = $mode.Text
        $mode.PerformClick()
        if (-not $mode.Checked -or $mode.Text -eq $initialText) {
            throw "The batch send mode control must switch to concurrent mode."
        }
        $mode.PerformClick()
        if ($mode.Checked -or $mode.Text -ne $initialText) {
            throw "The batch send mode control must switch back to sequential mode."
        }

        $grid = $form.GetType().GetField("dgvByteSweep", $flags).GetValue($form)
        $runningField = $form.GetType().GetField("byteSweepRunning", $flags)
        $parallelField = $form.GetType().GetField("byteSweepParallelMode", $flags)
        $activeIdField = $form.GetType().GetField("activeByteSweepPresetId", $flags)
        $selectActiveRow = $form.GetType().GetMethod("SelectActiveByteSweepPresetRow", $flags)

        $firstPreset = [WPELibrary.Lib.Socket_ByteSweepPresetInfo]::new()
        $firstPreset.BID = [Guid]::NewGuid()
        $firstPreset.BName = "first"
        $firstPreset.BFolder = "test"
        $firstPreset.Buffer = [byte[]](0x01)
        $firstPreset.BStart = 0
        $firstPreset.BLength = 1

        $secondPreset = [WPELibrary.Lib.Socket_ByteSweepPresetInfo]::new()
        $secondPreset.BID = [Guid]::NewGuid()
        $secondPreset.BName = "second"
        $secondPreset.BFolder = "test"
        $secondPreset.Buffer = [byte[]](0x02)
        $secondPreset.BStart = 0
        $secondPreset.BLength = 1

        $items = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_ByteSweepPresetInfo]'
        [void]$items.Add($firstPreset)
        [void]$items.Add($secondPreset)
        $grid.DataSource = $items
        $runningField.SetValue($form, $true)
        $parallelField.SetValue($form, $false)
        $activeIdField.SetValue($form, $secondPreset.BID)
        [void]$selectActiveRow.Invoke($form, $null)

        if ($grid.SelectedRows.Count -ne 1 -or
            $grid.SelectedRows[0].DataBoundItem.BID -ne $secondPreset.BID) {
            throw "The sweep list must select the preset that is currently sending."
        }
    }
    finally {
        $form.Dispose()
    }
}

Write-Output "Byte-sweep batch mode regression checks passed."
