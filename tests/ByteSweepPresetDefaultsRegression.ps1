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

$editorSource = Get-Content (
    Join-Path $repo "WPELibrary\Socket_ByteSweepEditorPanel.cs"
) -Raw -Encoding UTF8
$sendSource = Get-Content (
    Join-Path $repo "WPELibrary\Socket_SendForm.cs"
) -Raw -Encoding UTF8

Assert-Contains $editorSource `
    'CreateByteValueDisplay(' `
    "The dual-byte editor must display byte values instead of visible position inputs."
Assert-Contains $editorSource `
    'ResourceText("ByteSweep_CurrentByteFormat")' `
    "The dual-byte editor must format the selected bytes as hexadecimal values."
Assert-Contains $sendSource `
    'this.byteSweepEditorPanel.SetCurrentByteValues(first, second);' `
    "The send form must refresh A/B display values from the current packet."
Assert-Contains $sendSource `
    'provider.ReadByte(position)' `
    "Picking A/B must read the selected packet byte for display."

Assert-Contains $editorSource `
    'CreateNumber("nudSweepEditorFirstInterval", ResourceText("ByteSweep_FirstInterval"), 0, 999999999, 1000, 10)' `
    "New dual-byte presets must default byte A to 1000ms."
Assert-Contains $editorSource `
    'CreateNumber("nudSweepEditorSecondInterval", ResourceText("ByteSweep_SecondInterval"), 0, 999999999, 100, 10)' `
    "New dual-byte presets must default byte B to 100ms."
Assert-Contains $editorSource `
    'value == null ? 100 : value.BCombinationSecondInterval' `
    "A new editor session must use 100ms for byte B."
Assert-Contains $sendSource `
    'CreateByteSweepNumber("nudByteSweepFirstInterval", 0, 999999999, 1000)' `
    "The send-page dual-byte editor must default byte A to 1000ms."
Assert-Contains $sendSource `
    'CreateByteSweepNumber("nudByteSweepSecondInterval", 0, 999999999, 100)' `
    "The send-page dual-byte editor must default byte B to 100ms."
Assert-Contains $sendSource `
    'value.BCombinationSecondInterval = (int)this.nudByteSweepSecondInterval.Value;' `
    "Manual byte B interval edits must still be written back to the preset."
Assert-Contains $editorSource `
    'BCombinationSecondInterval = (int)this.secondInterval.Value' `
    "Manual byte B interval edits must still be readable from the preset editor."

if (-not [string]::IsNullOrWhiteSpace($BuildDirectory)) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -Path (Join-Path $BuildDirectory "Be.Windows.Forms.HexBox.dll")
    $libraryAssembly = [System.Reflection.Assembly]::LoadFrom(
        (Join-Path $BuildDirectory "WPELibrary.dll"))
    $panelType = $libraryAssembly.GetType("WPELibrary.Socket_ByteSweepEditorPanel", $true)
    $panel = [Activator]::CreateInstance($panelType, $true)
    $flags = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $secondIntervalField = $panelType.GetField("secondInterval", $flags)
    $firstByteValueField = $panelType.GetField("firstByteValue", $flags)
    $secondByteValueField = $panelType.GetField("secondByteValue", $flags)
    $secondInterval = $secondIntervalField.GetValue($panel)
    $firstByteValue = $firstByteValueField.GetValue($panel)
    $secondByteValue = $secondByteValueField.GetValue($panel)
    if ($secondInterval.Value -ne 100) {
        throw "New dual-byte editor session must expose B=100ms, actual=$($secondInterval.Value)."
    }

    $setByteValues = $panelType.GetMethod("SetCurrentByteValues")
    $setByteValues.Invoke(
        $panel,
        [object[]]@([System.Nullable[byte]]([byte]0xA5), [System.Nullable[byte]]([byte]0x02)))
    if ($firstByteValue.Text -ne "0xA5" -or $secondByteValue.Text -ne "0x02") {
        throw "The A/B editor must display the selected byte values instead of positions."
    }

    try {
        $loadSettings = $panelType.GetMethod("LoadSettings")
        $loadSettings.Invoke($panel, [object[]]@($null, 4, 0, 1))
        if ($secondInterval.Value -ne 100) {
            throw "A new dual-byte editor load must retain B=100ms."
        }

        $stored = [WPELibrary.Lib.Socket_ByteSweepPresetInfo]::new()
        $stored.BCombinationSecondInterval = 37
        $loadSettings.Invoke($panel, [object[]]@($stored, 4, 0, 1))
        if ($secondInterval.Value -ne 37) {
            throw "Loading a saved preset must restore the manually edited B interval."
        }

        $readSettings = $panelType.GetMethod("ReadSettings")
        $roundTrip = $readSettings.Invoke($panel, [object[]]@(0, 1))
        if ($roundTrip.BCombinationSecondInterval -ne 37) {
            throw "Reading a manually edited preset must preserve the B interval."
        }
    }
    finally {
        $panel.Dispose()
    }
}

Write-Output "Byte-sweep preset default regression checks passed."
