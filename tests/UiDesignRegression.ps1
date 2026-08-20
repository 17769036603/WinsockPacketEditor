param(
    [string]$Configuration = "Debug",
    [string]$BuildDirectory = "",
    [string]$AuditScreenshotPath = "",
    [int]$AuditWidth = 1400,
    [int]$AuditHeight = 950
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$resolvedBuildDirectory = if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $null
}
else {
    [System.IO.Path]::GetFullPath($BuildDirectory)
}
$hexBoxDll = if ($null -eq $resolvedBuildDirectory) {
    Join-Path $repo "WPELibrary\bin\$Configuration\Be.Windows.Forms.HexBox.dll"
}
else {
    Join-Path $resolvedBuildDirectory "Be.Windows.Forms.HexBox.dll"
}
$libraryDll = if ($null -eq $resolvedBuildDirectory) {
    Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
}
else {
    Join-Path $resolvedBuildDirectory "WPELibrary.dll"
}
$applicationDirectory = if ($null -eq $resolvedBuildDirectory) {
    Join-Path $repo "WinsockPacketEditor\bin\$Configuration"
}
else {
    $resolvedBuildDirectory
}
$applicationExe = Get-ChildItem -LiteralPath $applicationDirectory -Filter "*.exe" |
    Where-Object { $_.Name -eq "小黑封包助手.exe" } |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrEmpty($applicationExe)) {
    $applicationExe = Get-ChildItem -LiteralPath $applicationDirectory -Filter "*.exe" |
        Where-Object { $_.Name -notlike "EasyHook*Svc.exe" -and $_.Name -notlike "VisionLiveHarness*.exe" } |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrEmpty($applicationExe)) {
    throw "Application executable not found in $applicationDirectory"
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll

$keyInterpreterType = [Be.Windows.Forms.HexBox].GetNestedType(
    "KeyInterpreter",
    [System.Reflection.BindingFlags]::NonPublic)
$inclusiveSelectionMethod = $keyInterpreterType.GetMethod(
    "GetInclusiveMouseSelection",
    [System.Reflection.BindingFlags]::Static -bor
    [System.Reflection.BindingFlags]::NonPublic -bor
    [System.Reflection.BindingFlags]::Public)

function Get-InclusiveMouseSelection(
    [long]$Anchor,
    [long]$Current,
    [long]$ByteCount
) {
    $arguments = [object[]]@($Anchor, $Current, $ByteCount, [long]0, [long]0)
    [void]$inclusiveSelectionMethod.Invoke($null, $arguments)
    return [pscustomobject]@{
        Start = [long]$arguments[3]
        Length = [long]$arguments[4]
    }
}

[void][System.Reflection.Assembly]::LoadFrom($applicationExe)

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

$packetType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$capturedSocketA = New-Object WPELibrary.Lib.Socket_PacketInfo
$capturedSocketA.PacketType = $packetType
$capturedSocketA.PacketTo = "198.51.100.10:1000"
$capturedSocketA.PacketSocket = 101
$capturedSocketB = New-Object WPELibrary.Lib.Socket_PacketInfo
$capturedSocketB.PacketType = $packetType
$capturedSocketB.PacketTo = "198.51.100.20:2000"
$capturedSocketB.PacketSocket = 202
$emptyTargetTemplate = New-Object WPELibrary.Lib.Socket_PacketInfo
$emptyTargetTemplate.PacketType = $packetType
$capturedPackets = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_PacketInfo]'
$capturedPackets.Add($capturedSocketA)
$capturedPackets.Add($capturedSocketB)
$emptyTemplates = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_PacketInfo]'
$emptyTemplates.Add($emptyTargetTemplate)
Assert-True (
    [WPELibrary.Lib.Socket_Cache+SocketList]::FindLatestMatchingSocket(
        $capturedPackets,
        $emptyTemplates) -eq 202
) "A legacy preset without a destination must use the latest captured socket of the same packet type."
$wrongTargetTemplate = New-Object WPELibrary.Lib.Socket_PacketInfo
$wrongTargetTemplate.PacketType = $packetType
$wrongTargetTemplate.PacketTo = "198.51.100.99:9999"
$wrongTargetTemplates = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_PacketInfo]'
$wrongTargetTemplates.Add($wrongTargetTemplate)
Assert-True (
    [WPELibrary.Lib.Socket_Cache+SocketList]::FindLatestMatchingSocket(
        $capturedPackets,
        $wrongTargetTemplates) -eq 0
) "A preset with a destination must still require an exact destination match."

$forwardSelection = Get-InclusiveMouseSelection 3 5 10
Assert-True (
    $forwardSelection.Start -eq 3 -and $forwardSelection.Length -eq 3
) "Forward mouse selection must include its start and end bytes."
$backwardSelection = Get-InclusiveMouseSelection 5 3 10
Assert-True (
    $backwardSelection.Start -eq 3 -and $backwardSelection.Length -eq 3
) "Backward mouse selection must include the byte where dragging started."
$clickSelection = Get-InclusiveMouseSelection 5 5 10
Assert-True (
    $clickSelection.Start -eq 5 -and $clickSelection.Length -eq 1
) "A single click must select one complete byte."

$mouseSelectionHost = [System.Windows.Forms.Form]::new()
$mouseSelectionHexBox = [Be.Windows.Forms.HexBox]::new()
try {
    $mouseSelectionHost.StartPosition =
        [System.Windows.Forms.FormStartPosition]::Manual
    $mouseSelectionHost.Location = [System.Drawing.Point]::new(-32000, -32000)
    $mouseSelectionHost.ShowInTaskbar = $false
    $mouseSelectionHost.ClientSize = [System.Drawing.Size]::new(520, 180)
    $mouseSelectionHexBox.Dock = [System.Windows.Forms.DockStyle]::Fill
    $mouseSelectionHexBox.ColumnInfoVisible = $true
    $mouseSelectionHexBox.LineInfoVisible = $true
    $mouseSelectionHexBox.StringViewVisible = $true
    $mouseSelectionHexBox.ByteProvider =
        [Be.Windows.Forms.DynamicByteProvider]::new(
            [byte[]](0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07))
    $mouseSelectionHost.Controls.Add($mouseSelectionHexBox)
    $mouseSelectionHost.Show()
    [System.Windows.Forms.Application]::DoEvents()

    $hexBoxPrivate = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $getBytePoint = $mouseSelectionHexBox.GetType().GetMethod(
        "GetBytePointF",
        $hexBoxPrivate,
        $null,
        [Type[]]@([long]),
        $null)
    $onMouseDown = $mouseSelectionHexBox.GetType().GetMethod(
        "OnMouseDown",
        $hexBoxPrivate)
    $onMouseMove = [System.Windows.Forms.Control].GetMethod(
        "OnMouseMove",
        $hexBoxPrivate)
    $onMouseUp = [System.Windows.Forms.Control].GetMethod(
        "OnMouseUp",
        $hexBoxPrivate)

    function Get-HexByteMouseArgs(
        [long]$Index,
        [System.Windows.Forms.MouseButtons]$Button
    ) {
        $point = [System.Drawing.PointF]$getBytePoint.Invoke(
            $mouseSelectionHexBox,
            [object[]]@($Index))
        return [System.Windows.Forms.MouseEventArgs]::new(
            $Button,
            1,
            [int][Math]::Floor($point.X + $mouseSelectionHexBox.CharSize.Width),
            [int][Math]::Floor($point.Y + ($mouseSelectionHexBox.CharSize.Height / 2)),
            0)
    }

    $singleByteDown = Get-HexByteMouseArgs 5 `
        ([System.Windows.Forms.MouseButtons]::Left)
    $onMouseDown.Invoke($mouseSelectionHexBox, [object[]]@($singleByteDown))
    $onMouseUp.Invoke($mouseSelectionHexBox, [object[]]@($singleByteDown))
    Assert-True (
        $mouseSelectionHexBox.SelectionStart -eq 5 -and
        $mouseSelectionHexBox.SelectionLength -eq 1
    ) "A real mouse click must immediately select one complete byte."

    $forwardDown = Get-HexByteMouseArgs 3 `
        ([System.Windows.Forms.MouseButtons]::Left)
    $forwardMove = Get-HexByteMouseArgs 5 `
        ([System.Windows.Forms.MouseButtons]::Left)
    $onMouseDown.Invoke($mouseSelectionHexBox, [object[]]@($forwardDown))
    $onMouseMove.Invoke($mouseSelectionHexBox, [object[]]@($forwardMove))
    $onMouseUp.Invoke($mouseSelectionHexBox, [object[]]@($forwardMove))
    Assert-True (
        $mouseSelectionHexBox.SelectionStart -eq 3 -and
        $mouseSelectionHexBox.SelectionLength -eq 3
    ) "A real forward drag must include both endpoint bytes."

    $backwardDown = Get-HexByteMouseArgs 5 `
        ([System.Windows.Forms.MouseButtons]::Left)
    $backwardMove = Get-HexByteMouseArgs 3 `
        ([System.Windows.Forms.MouseButtons]::Left)
    $onMouseDown.Invoke($mouseSelectionHexBox, [object[]]@($backwardDown))
    $onMouseMove.Invoke($mouseSelectionHexBox, [object[]]@($backwardMove))
    $onMouseUp.Invoke($mouseSelectionHexBox, [object[]]@($backwardMove))
    Assert-True (
        $mouseSelectionHexBox.SelectionStart -eq 3 -and
        $mouseSelectionHexBox.SelectionLength -eq 3
    ) "A real backward drag must include both endpoint bytes."

    $outsideClick = [System.Windows.Forms.MouseEventArgs]::new(
        [System.Windows.Forms.MouseButtons]::Left,
        1,
        1,
        1,
        0)
    $onMouseDown.Invoke($mouseSelectionHexBox, [object[]]@($outsideClick))
    $onMouseUp.Invoke($mouseSelectionHexBox, [object[]]@($outsideClick))
    Assert-True (
        $mouseSelectionHexBox.SelectionStart -eq 3 -and
        $mouseSelectionHexBox.SelectionLength -eq 3
    ) "Clicking outside the byte area must not create a false selection."
}
finally {
    $mouseSelectionHost.Close()
    $mouseSelectionHexBox.Dispose()
    $mouseSelectionHost.Dispose()
}

$sendDelegateType = [System.Func``2].MakeGenericType([byte[]], [bool])
$script:byteSweepSentValues = [System.Collections.Generic.List[byte]]::new()
$script:byteSweepProgressValues =
    [System.Collections.Generic.List[WPELibrary.Lib.Socket_ByteSweepProgress]]::new()
$byteSweepSend = [System.Management.Automation.LanguagePrimitives]::ConvertTo(
    {
        param([byte[]]$buffer)
        [void]$script:byteSweepSentValues.Add($buffer[0])
        return $true
    },
    $sendDelegateType)
$progressDelegateType = [System.Action``1].MakeGenericType(
    [WPELibrary.Lib.Socket_ByteSweepProgress])
$byteSweepProgress = [System.Management.Automation.LanguagePrimitives]::ConvertTo(
    {
        param([WPELibrary.Lib.Socket_ByteSweepProgress]$progress)
        $script:byteSweepProgressValues.Add($progress)
    },
    $progressDelegateType)
$byteSweepBaseline = [byte[]](0x10)
$byteSweepResult = [WPELibrary.Lib.Socket_ByteSweepEngine]::Execute(
    $byteSweepBaseline,
    0,
    1,
    0,
    $byteSweepSend,
    [System.Threading.CancellationToken]::None,
    $byteSweepProgress)
Assert-True (
    $byteSweepResult.TotalSend -eq 255 -and
    $script:byteSweepSentValues.Count -eq 255
) "A one-byte sweep must execute all 255 non-original values even with a zero interval."
Assert-True (
    @($script:byteSweepSentValues | Select-Object -Unique).Count -eq 255 -and
    -not $script:byteSweepSentValues.Contains([byte]0x10)
) "A one-byte sweep must send every alternate byte value exactly once."
Assert-True (
    $byteSweepBaseline[0] -eq 0x10
) "The byte-sweep engine must not mutate its caller's baseline buffer."
Assert-True (
    $script:byteSweepProgressValues.Count -ge 2 -and
    $script:byteSweepProgressValues[0].ValueNumber -eq 1 -and
    @($script:byteSweepProgressValues |
        Where-Object { $_.CurrentValue -eq $_.OriginalValue }).Count -eq 0
) "Every displayed sweep value must come from a completed non-original send."

$pairSentValues = [System.Collections.Generic.List[string]]::new()
$pairSend = [System.Management.Automation.LanguagePrimitives]::ConvertTo(
    {
        param([byte[]]$buffer)
        [void]$pairSentValues.Add(("{0:X2}:{1:X2}" -f $buffer[0], $buffer[1]))
        return $true
    },
    $sendDelegateType)
$pairProgressValues = [System.Collections.Generic.List[WPELibrary.Lib.Socket_ByteSweepProgress]]::new()
$pairProgress = [System.Management.Automation.LanguagePrimitives]::ConvertTo(
    {
        param([WPELibrary.Lib.Socket_ByteSweepProgress]$progress)
        [void]$pairProgressValues.Add($progress)
    },
    $progressDelegateType)
$pairBaseline = [byte[]](0x10, 0x20)
$pairResult = [WPELibrary.Lib.Socket_ByteSweepEngine]::ExecutePairCombination(
    $pairBaseline,
    0,
    3,
    0,
    1,
    2,
    0,
    $pairSend,
    [System.Threading.CancellationToken]::None,
    $pairProgress)
Assert-True (
    $pairResult.TotalSend -eq 6 -and
    $pairSentValues.Count -eq 6 -and
    @($pairSentValues | Select-Object -Unique).Count -eq 6
) "A pair combination must send every configured first/second value combination exactly once."
Assert-True (
    $pairSentValues[0] -eq "10:20" -and
    $pairSentValues[-1] -eq "12:21"
) "A pair combination must start from each byte's current value."
Assert-True (
    $pairBaseline[0] -eq 0x10 -and $pairBaseline[1] -eq 0x20
) "A pair combination must preserve its baseline."
Assert-True (
    $pairProgressValues.Count -ge 2 -and
    $pairProgressValues.Count -lt $pairResult.TotalSend -and
    $pairProgressValues[-1].TotalSend -eq $pairResult.TotalSend -and
    $pairProgressValues[-1].IsPairCombination
) "Pair progress must include first/final state without queueing one UI update per send."

$script:byteSweepSentValues.Clear()
$executeSweepLoops = [WPELibrary.Socket_SendForm].GetMethod(
    "ExecuteByteSweepLoops",
    [System.Reflection.BindingFlags]::Static -bor
    [System.Reflection.BindingFlags]::NonPublic)
$twoLoopSweepResult = $executeSweepLoops.Invoke(
    $null,
    [object[]]@(
        $byteSweepBaseline,
        [int]0,
        [int]1,
        [int]0,
        [int]2,
        $byteSweepSend,
        [System.Threading.CancellationToken]::None,
        $null))
Assert-True (
    $twoLoopSweepResult.TotalSend -eq 510 -and
    $twoLoopSweepResult.Success -eq 510 -and
    $script:byteSweepSentValues.Count -eq 510
) "A two-loop byte sweep must execute and accumulate both complete 255-value rounds."
Assert-True (
    $byteSweepBaseline[0] -eq 0x10
) "A multi-loop byte sweep must keep the caller's baseline buffer unchanged."

$script:byteSweepCancellation = [System.Threading.CancellationTokenSource]::new()
$script:cancelledSweepSendCount = 0
$cancelledSweepSend = [System.Management.Automation.LanguagePrimitives]::ConvertTo(
    {
        param([byte[]]$buffer)
        $script:cancelledSweepSendCount++
        if ($script:cancelledSweepSendCount -eq 10) {
            $script:byteSweepCancellation.Cancel()
        }
        return $true
    },
    $sendDelegateType)
try {
    $cancelledSweepResult = [WPELibrary.Lib.Socket_ByteSweepEngine]::Execute(
        [byte[]](0x20),
        0,
        1,
        0,
        $cancelledSweepSend,
        $script:byteSweepCancellation.Token,
        $null)
    Assert-True (
        $cancelledSweepResult.Cancelled -and
        $cancelledSweepResult.TotalSend -eq 10 -and
        $script:cancelledSweepSendCount -eq 10
    ) "Cancellation must stop a byte sweep immediately after the current send result."
}
finally {
    $script:byteSweepCancellation.Dispose()
    Remove-Variable byteSweepCancellation -Scope Script -ErrorAction SilentlyContinue
    Remove-Variable cancelledSweepSendCount -Scope Script -ErrorAction SilentlyContinue
    Remove-Variable byteSweepSentValues -Scope Script -ErrorAction SilentlyContinue
    Remove-Variable byteSweepProgressValues -Scope Script -ErrorAction SilentlyContinue
}

function Get-PrivateField($instance, [string]$name) {
    $flags = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $field = $instance.GetType().GetField($name, $flags)
    if ($null -eq $field) {
        throw "Missing private field: $($instance.GetType().FullName).$name"
    }

    return $field.GetValue($instance)
}

function Read-Source([string]$relativePath) {
    return [System.IO.File]::ReadAllText(
        (Join-Path $repo $relativePath),
        [System.Text.Encoding]::UTF8)
}

function Get-DescendantControls([System.Windows.Forms.Control]$parent) {
    foreach ($control in $parent.Controls) {
        $control
        if ($control.HasChildren) {
            Get-DescendantControls $control
        }
    }
}

$injector = New-Object WinsockPacketEditor.Injector_Form
try {
    $selectProcess = Get-PrivateField $injector "bSelectProcess"
    $inject = Get-PrivateField $injector "bInject"
    $processDisplay = Get-PrivateField $injector "tbProcessID"

    Assert-True ($injector.ClientSize.Width -ge 580) "Injector layout must leave room for visible localized action labels."
    Assert-True (-not [string]::IsNullOrWhiteSpace($selectProcess.Text)) "Process selection must not be icon-only."
    Assert-True (-not [string]::IsNullOrWhiteSpace($selectProcess.AccessibleName)) "Process selection needs an accessible name."
    Assert-True (-not $processDisplay.TabStop) "Read-only process display must not interrupt keyboard navigation."
    Assert-True ([object]::ReferenceEquals($injector.AcceptButton, $inject)) "Enter must target the primary injection action."
    Assert-True ($selectProcess.TabIndex -lt $inject.TabIndex) "Keyboard order must select a process before injection."
}
finally {
    $injector.Dispose()
}

$processList = New-Object WinsockPacketEditor.ProcessList_Form
try {
    $confirm = Get-PrivateField $processList "bSelected"
    $search = Get-PrivateField $processList "txtProcessSearch"
    $grid = Get-PrivateField $processList "dgvProcessList"

    Assert-True ($processList.FormBorderStyle -eq [System.Windows.Forms.FormBorderStyle]::SizableToolWindow) "Process list must support resizing."
    Assert-True ($processList.MinimumSize.Width -ge 680) "Process list needs a usable minimum width."
    Assert-True (-not $confirm.Enabled) "Confirm must remain disabled until a process is selected."
    Assert-True ([object]::ReferenceEquals($processList.AcceptButton, $confirm)) "Enter must confirm the selected process."
    Assert-True (-not [string]::IsNullOrWhiteSpace($search.AccessibleName)) "Process search needs an accessible name."
    Assert-True (-not [string]::IsNullOrWhiteSpace($grid.AccessibleName)) "Process grid needs an accessible name."
}
finally {
    $processList.Dispose()
}

$preset = New-Object WPELibrary.Lib.Socket_ByteSweepPresetInfo
$preset.BID = [Guid]::NewGuid()
$preset.BName = "UI regression preset"
$preset.BFolder = "UI regression group"
$preset.BStart = 0
$preset.BLength = 1
$preset.BLoopCount = 1
$preset.BInterval = 10
$preset.BNextInterval = 0
$preset.Buffer = [byte[]](0x01, 0x02)

$newPresetForm = [WPELibrary.Socket_ByteSweepPresetForm]::new($preset, $true)
$editPresetForm = [WPELibrary.Socket_ByteSweepPresetForm]::new($preset, $false)
try {
    Assert-True ($newPresetForm.Text -ne $editPresetForm.Text) "New and edit preset dialogs must have different titles."
    Assert-True ($newPresetForm.MinimumSize.Width -ge 420) "Preset dialog needs room for localized labels."
    Assert-True ($newPresetForm.AutoScaleMode -eq [System.Windows.Forms.AutoScaleMode]::Dpi) "Preset dialog must scale with DPI."
    Assert-True ($null -ne $newPresetForm.AcceptButton) "Preset dialog must expose a primary keyboard action."
    Assert-True ($null -ne $newPresetForm.CancelButton) "Preset dialog must support Escape/cancel."
    $newPresetInputs = @(Get-DescendantControls $newPresetForm |
        Where-Object { $_ -is [System.Windows.Forms.NumericUpDown] })
    $editPresetInputs = @(Get-DescendantControls $editPresetForm |
        Where-Object { $_ -is [System.Windows.Forms.NumericUpDown] })
    Assert-True ($newPresetInputs.Count -eq 0) "New preset identity dialog must not duplicate sweep parameter inputs."
    Assert-True ($editPresetInputs.Count -eq 0) "Edit preset identity dialog must only edit name and group."
    Assert-True (
        $newPresetForm.Controls.Find("bEditByteSweepDetails", $true).Count -eq 0
    ) "New presets already use the send page and do not need a second navigation action."
    Assert-True (
        $editPresetForm.Controls.Find("bEditByteSweepDetails", $true).Count -eq 1
    ) "Existing preset identity dialog must expose the other-settings action."
}
finally {
    $newPresetForm.Dispose()
    $editPresetForm.Dispose()
}

$packet = New-Object WPELibrary.Lib.Socket_PacketInfo
$packet.PacketTime = [DateTime]::Now
$packet.PacketBuffer = [byte[]](0x01, 0x02)
$packet.RawBuffer = [byte[]](0x01, 0x02)
$packet.PacketLen = 2
$sendForm = [WPELibrary.Socket_SendForm]::new($packet)
try {
    Assert-True ($sendForm.MinimumSize.Width -ge 1200) "Send dialog must preserve usable packet, detail, and sweep columns at scaled DPI."
    Assert-True ($sendForm.MinimumSize.Height -ge 620) "Send dialog must preserve its complete compact lower action area."
    $saveButton = Get-PrivateField $sendForm "bSave"
    $startButton = Get-PrivateField $sendForm "bSend"
    $closeButton = Get-PrivateField $sendForm "bClose"
    $legacyButtonRow = Get-PrivateField $sendForm "tlpButtons"
    $sendRootLayout = Get-PrivateField $sendForm "tlpSendForm"
    $sendParameterLayout = Get-PrivateField $sendForm "tlpParameter"
    $sendStatusStrip = Get-PrivateField $sendForm "ssSocketSend"
    $packetIdentity = Get-PrivateField $sendForm "tlCurrentPacketIdentity"
    Assert-True (-not [string]::IsNullOrWhiteSpace($saveButton.Text)) "The send preset save action must remain visible."
    Assert-True ($null -eq $closeButton.Parent) "The redundant bottom Close button must not remain in the visible layout."
    Assert-True (
        -not $sendRootLayout.Controls.Contains($legacyButtonRow)
    ) "The empty legacy action row must be removed after actions move into the send panel."
    Assert-True ($sendRootLayout.RowCount -eq 4) "The send editor must use packet info, data, parameters, and status rows only."
    Assert-True (
        $sendRootLayout.GetCellPosition($sendStatusStrip).Row -eq 3
    ) "The status strip must sit directly below the parameter panels."
    Assert-True (
        $sendRootLayout.RowStyles[2].Height -ge 144
    ) "The send and progression panels need enough vertical room for aligned controls."
    Assert-True (
        $sendParameterLayout.ColumnStyles[1].Width -gt
        $sendParameterLayout.ColumnStyles[0].Width
    ) "The denser progression panel must receive slightly more horizontal room."
    Assert-True $packetIdentity.Spring "The packet identity must absorb flexible status-bar width before counters."
    $expectedChineseStart = [string]([char]0x5F00) + [char]0x59CB
    Assert-True (
        $startButton.Text.StartsWith($expectedChineseStart) -or
        $startButton.Text.StartsWith("Start")
    ) "The primary send action must use the clearer Start label."

    $stopButton = Get-PrivateField $sendForm "bSendStop"
    $normalSweepStartButton = Get-PrivateField $sendForm "bStartByteSweep"
    $normalSweepStopButton = Get-PrivateField $sendForm "bStopByteSweep"
    $normalSweepEditor = Get-PrivateField $sendForm "byteSweepEditorPanel"
    $normalSweepEditorMode = Get-PrivateField $normalSweepEditor "mode"
    $normalSweepEditorStop = Get-PrivateField $normalSweepEditor "stop"
    $sendGroup = Get-PrivateField $sendForm "gbSendType"
    $sendTimesInput = Get-PrivateField $sendForm "nudSendType_Times"
    $sendHexBox = Get-PrivateField $sendForm "hbPacketData"
    $hexBoxPrivate = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $selectionBoxesVisible = $sendHexBox.GetType().GetField(
        "_selectionByteBoxesVisible",
        $hexBoxPrivate)
    $selectionBoxBorderColor = $sendHexBox.GetType().GetField(
        "_selectionByteBoxBorderColor",
        $hexBoxPrivate)
    Assert-True (
        $sendHexBox.SelectionBackColor -eq
        [System.Drawing.Color]::FromArgb(210, 230, 255)
    ) "Selected packet bytes must use the readable light-blue cell fill."
    Assert-True (
        $sendHexBox.SelectionForeColor -eq [System.Drawing.Color]::Black
    ) "Selected packet byte text must remain readable."
    Assert-True (
        $null -ne $selectionBoxesVisible -and
        [bool]$selectionBoxesVisible.GetValue($sendHexBox)
    ) "The send HexBox must opt into per-byte selection boxes."
    Assert-True (
        $null -ne $selectionBoxBorderColor -and
        $selectionBoxBorderColor.GetValue($sendHexBox) -eq
        [System.Drawing.Color]::FromArgb(0, 90, 180)
    ) "Selected packet bytes must use the approved dark-blue box border."
    $defaultHexBox = [Be.Windows.Forms.HexBox]::new()
    try {
        Assert-True (
            -not [bool]$selectionBoxesVisible.GetValue($defaultHexBox)
        ) "Other HexBox controls must keep the original selection rendering by default."
    }
    finally {
        $defaultHexBox.Dispose()
    }
    $originalPacketEditorReadOnly = $sendHexBox.ReadOnly
    $setSendRunningState = $sendForm.GetType().GetMethod(
        "SetSendRunningState",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $setSendRunningState.Invoke($sendForm, [object[]]@($true))
    Assert-True $sendGroup.Enabled "The send group must remain enabled while running so Stop stays actionable."
    Assert-True $stopButton.Enabled "Starting a send must enable the Stop button."
    Assert-True (-not $normalSweepStartButton.Enabled) "A normal send must disable the separate sweep Start action."
    Assert-True (-not $normalSweepStopButton.Enabled) "A normal send must not enable the lower sweep Stop action."
    Assert-True (-not $normalSweepEditorStop.Enabled) "A normal send must not enable the right sweep Stop action."
    Assert-True (-not $normalSweepEditorMode.Enabled) "A normal send must lock right-editor sweep settings against concurrent edits."
    Assert-True (-not $startButton.Enabled) "Starting a send must disable the Start button."
    Assert-True (-not $sendTimesInput.Enabled) "Running parameters must remain locked while Stop stays enabled."
    Assert-True (-not $saveButton.Enabled) "Preset saving must remain disabled during a send."
    Assert-True $sendHexBox.ReadOnly "Packet bytes must be read-only while a send is running."
    $setSendRunningState.Invoke($sendForm, [object[]]@($false))
    Assert-True $startButton.Enabled "Completing or stopping a send must re-enable Start."
    Assert-True (-not $stopButton.Enabled) "Completing or stopping a send must disable Stop again."
    Assert-True (-not $normalSweepEditorMode.Enabled) "The pair-only right editor must keep its hidden mode selector locked after a send completes."
    Assert-True (
        $sendHexBox.ReadOnly -eq $originalPacketEditorReadOnly
    ) "Completing or stopping a send must restore the packet editor's original read-only state."

    $null = $sendForm.Handle
    $instancePrivate = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $sendCountField = $sendForm.GetType().GetField("Send_CNT", $instancePrivate)
    $sendSuccessField = $sendForm.GetType().GetField("Send_Success", $instancePrivate)
    $sendFailField = $sendForm.GetType().GetField("Send_Fail", $instancePrivate)
    $postSendCounterUpdateMethod = $sendForm.GetType().GetMethod(
        "PostSendCounterUpdate",
        $instancePrivate)
    $sendCountLabel = Get-PrivateField $sendForm "tlSendTimes_Value"
    $sendSuccessLabel = Get-PrivateField $sendForm "tlSend_Success_Value"
    $sendFailLabel = Get-PrivateField $sendForm "tlSend_Fail_Value"
    $sendCountField.SetValue($sendForm, [long]1)
    $sendSuccessField.SetValue($sendForm, [long]1)
    $sendFailField.SetValue($sendForm, [long]0)
    $postSendCounterUpdateMethod.Invoke($sendForm, $null)
    $sendCountField.SetValue($sendForm, [long]9)
    $sendSuccessField.SetValue($sendForm, [long]7)
    $sendFailField.SetValue($sendForm, [long]2)
    $postSendCounterUpdateMethod.Invoke($sendForm, $null)
    for ($messagePump = 0; $messagePump -lt 5; $messagePump++) {
        [System.Windows.Forms.Application]::DoEvents()
    }
    Assert-True ($sendCountLabel.Text -eq "9") "Live send progress must display the latest total before completion."
    Assert-True ($sendSuccessLabel.Text -eq "7") "Live send progress must display the latest success count before completion."
    Assert-True ($sendFailLabel.Text -eq "2") "Live send progress must display the latest failure count before completion."

    $originalSelectionBackColor = $sendHexBox.SelectionBackColor
    $originalSelectionForeColor = $sendHexBox.SelectionForeColor
    $highlightMethod = $sendForm.GetType().GetMethod(
        "BeginByteSweepHighlight",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $restoreHighlightMethod = $sendForm.GetType().GetMethod(
        "RestoreByteSweepHighlight",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $highlightMethod.Invoke($sendForm, $null)
    Assert-True ($sendHexBox.SelectionBackColor -eq [System.Drawing.Color]::Gold) "Byte sweep must use a visible yellow current-byte highlight."
    Assert-True ($sendHexBox.SelectionForeColor -eq [System.Drawing.Color]::Black) "Byte sweep highlight text must remain readable."
    $restoreHighlightMethod.Invoke($sendForm, $null)
    Assert-True ($sendHexBox.SelectionBackColor -eq $originalSelectionBackColor) "Byte sweep completion must restore the original selection background."
    Assert-True ($sendHexBox.SelectionForeColor -eq $originalSelectionForeColor) "Byte sweep completion must restore the original selection text color."
}
finally {
    $sendForm.Dispose()
}

$sweepEditForm = [WPELibrary.Socket_SendForm]::new($packet, $preset)
try {
    $initHexBoxMethod = $sweepEditForm.GetType().GetMethod(
        "InitHexBox",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $initSweepParametersMethod = $sweepEditForm.GetType().GetMethod(
        "InitSendParameters",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $initHexBoxMethod.Invoke($sweepEditForm, $null)
    $initSweepParametersMethod.Invoke($sweepEditForm, $null)
    $sendUiModeType = $sweepEditForm.GetType().GetNestedType(
        "SendUiMode",
        [System.Reflection.BindingFlags]::NonPublic)
    $setSendUiModeMethod = $sweepEditForm.GetType().GetMethod(
        "SetSendUiMode",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $savedSweepMode = if ($preset.BMode -eq [WPELibrary.Lib.Socket_ByteSweepMode]::PairCombination) {
        [Enum]::Parse($sendUiModeType, "PairSweep")
    }
    else {
        [Enum]::Parse($sendUiModeType, "SequentialSweep")
    }
    $setSendUiModeMethod.Invoke($sweepEditForm, [object[]]@($savedSweepMode))

    $sweepRadio = Get-PrivateField $sweepEditForm "rbSendType_ByteSweep"
    $loopCountInput = Get-PrivateField $sweepEditForm "nudByteSweepLoopCount"
    $intervalInput = Get-PrivateField $sweepEditForm "nudByteSweepInterval"
    $nextIntervalInput = Get-PrivateField $sweepEditForm "nudByteSweepNextInterval"
    $legacySweepSettings = Get-PrivateField $sweepEditForm "tlpByteSweepSettings"
    $legacyModeSelector = Get-PrivateField $sweepEditForm "cbbByteSweepMode"
    $legacySecondPosition = Get-PrivateField $sweepEditForm "nudByteSweepSecondPosition"
    $legacySecondLength = Get-PrivateField $sweepEditForm "nudByteSweepSecondLength"
    $sweepHexBox = Get-PrivateField $sweepEditForm "hbPacketData"
    $packetValueInfo = Get-PrivateField $sweepEditForm "tableLayoutPanel1"
    $packetPositionInfo = Get-PrivateField $sweepEditForm "lHexBox_Position"
    $perLineSelector = Get-PrivateField $sweepEditForm "tscbPerLine"
    $sweepSaveButton = Get-PrivateField $sweepEditForm "bSaveByteSweepPreset"
    $sweepStartButton = Get-PrivateField $sweepEditForm "bStartByteSweep"
    $sweepStopButton = Get-PrivateField $sweepEditForm "bStopByteSweep"
    $sweepSidePanel = Get-PrivateField $sweepEditForm "pnlByteSweepSide"
    $sweepEditorPanel = Get-PrivateField $sweepEditForm "byteSweepEditorPanel"
    $sweepEditorScrollHost = Get-PrivateField $sweepEditorPanel "scrollHost"
    $sweepEditorLayout = Get-PrivateField $sweepEditorPanel "layout"
    $sweepEditorMode = Get-PrivateField $sweepEditorPanel "mode"
    $sweepEditorTitle = Get-PrivateField $sweepEditorPanel "title"
    $sweepEditorFooter = Get-PrivateField $sweepEditorPanel "footer"
    $sweepEditorSummary = Get-PrivateField $sweepEditorPanel "selection"
    $sweepEditorInterval = Get-PrivateField $sweepEditorPanel "interval"
    $sweepEditorFirstPosition = Get-PrivateField $sweepEditorPanel "firstPosition"
    $sweepEditorPickFirst = Get-PrivateField $sweepEditorPanel "pickFirst"
    $sweepEditorSecondPosition = Get-PrivateField $sweepEditorPanel "secondPosition"
    $sweepEditorSecondLength = Get-PrivateField $sweepEditorPanel "secondLength"
    $sweepEditorPickSecond = Get-PrivateField $sweepEditorPanel "pickSecond"
    $sweepEditorSave = Get-PrivateField $sweepEditorPanel "save"
    $sweepEditorStart = Get-PrivateField $sweepEditorPanel "send"
    $sweepEditorPause = Get-PrivateField $sweepEditorPanel "pause"
    $sweepEditorStop = Get-PrivateField $sweepEditorPanel "stop"
    $annotationController = Get-PrivateField $sweepEditForm "byteAnnotationController"
    $annotationPanel = Get-PrivateField $annotationController "panel"
    $socketGroup = Get-PrivateField $sweepEditForm "gbSendSocket"
    $sendGroup = Get-PrivateField $sweepEditForm "gbSendType"
    $progressionGroup = Get-PrivateField $sweepEditForm "gbSendStep"
    $parameterLayout = Get-PrivateField $sweepEditForm "tlpParameter"
    $sendActions = Get-PrivateField $sweepEditForm "tlpSendActions"
    $idleSweepStatus = Get-PrivateField $sweepEditForm "tlByteSweepProgress"
    $sendButton = Get-PrivateField $sweepEditForm "bSend"
    Assert-True ($parameterLayout.ColumnCount -eq 2) "The lower editor must contain only send and progression columns."
    $packetDataLayout = $sweepEditForm.GetType().GetField(
        "tlpPacketData",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic).GetValue($sweepEditForm)
    Assert-True (
        -not $packetValueInfo.Visible -and
        -not $packetPositionInfo.Visible -and
        $packetDataLayout.ColumnCount -eq 1 -and
        $sweepSidePanel.Parent -eq $parameterLayout
    ) "The pair editor must move below a full-width packet data area."
    Assert-True (
        $parameterLayout.GetCellPosition($sendGroup).Column -eq 0 -and
        $parameterLayout.GetCellPosition($progressionGroup).Column -eq 0 -and
        $parameterLayout.GetColumnSpan($sendGroup) -eq 2 -and
        $parameterLayout.GetColumnSpan($progressionGroup) -eq 2
    ) "Send and single-byte progression panels must share one full-width lower feature area."
    Assert-True (
        $legacySweepSettings.ColumnCount -eq 6 -and
        $legacySecondPosition.Parent -eq $legacySweepSettings -and
        $legacySecondLength.Parent -eq $legacySweepSettings
    ) "The original progression section must retain its backing fields for synchronized execution."
    Assert-True (
        -not $legacyModeSelector.Visible -and
        $legacySweepSettings.RowStyles[0].Height -gt 0 -and
        $legacySweepSettings.RowStyles[1].Height -eq 0 -and
        $legacySweepSettings.RowStyles[2].Height -eq 0 -and
        -not $legacySecondPosition.Visible -and
        -not $legacySecondLength.Visible
    ) "The lower progression section must remain a single-mode surface; pair selection belongs only to the right editor."
    Assert-True (-not $socketGroup.TabStop) "The hidden Socket panel must not participate in keyboard navigation."
    Assert-True ($sendButton.Parent.Name -eq "tlpSendActions") "Send actions must remain inside the send panel."
    Assert-True (
        $sendActions.ColumnStyles[0].Width -eq 50 -and
        $sendActions.ColumnStyles[1].Width -eq 50 -and
        $sendActions.ColumnStyles[2].Width -eq 0
    ) "Hiding normal preset save in sweep editing must let Start and Stop share the full action row."
    Assert-True ($idleSweepStatus.GetCurrentParent().Name -eq "ssSocketSend") "Sweep status must be hosted by the shared bottom status bar."
    Assert-True (
        $sweepSaveButton.Parent.Name -eq "tlpByteSweepActions" -and
        $sweepStartButton.Parent -eq $sweepSaveButton.Parent -and
        $sweepStopButton.Parent -eq $sweepSaveButton.Parent
    ) "The lower sequential sweep area must provide its own Save, Start, and Stop actions."
    Assert-True (
        $sweepSaveButton.Parent.GetCellPosition($sweepStartButton).Column -eq 0 -and
        $sweepSaveButton.Parent.GetCellPosition($sweepStopButton).Column -eq 1 -and
        $sweepSaveButton.Parent.GetCellPosition($sweepSaveButton).Column -eq 2
    ) "The lower sweep actions must be ordered Start, Stop, then Save."
    Assert-True (
        -not [string]::IsNullOrWhiteSpace($sweepEditorStart.AccessibleName) -and
        -not [string]::IsNullOrWhiteSpace($sweepEditorPause.AccessibleName) -and
        -not [string]::IsNullOrWhiteSpace($sweepEditorStop.AccessibleName) -and
        $sweepEditorStart.Parent.Controls.IndexOf($sweepEditorStart) -eq 0 -and
        $sweepEditorStart.Parent.Controls.IndexOf($sweepEditorPause) -eq 1 -and
        $sweepEditorStart.Parent.Controls.IndexOf($sweepEditorStop) -eq 2 -and
        $sweepEditorStart.Parent.Controls.IndexOf($sweepEditorSave) -eq 3
    ) "The right sweep editor must provide its own accessible Start, Pause, and Stop actions."
    Assert-True (
        $sweepSidePanel.RowCount -eq 3 -and
        $sweepSidePanel.Controls.Count -eq 3 -and
        -not $annotationPanel.Visible -and
        $sweepSidePanel.RowStyles[0].Height -eq 0
    ) "The right packet panel must hide annotations and keep only the sweep section visible."
    Assert-True ($sweepEditorPanel.GetType().Name -eq "Socket_ByteSweepEditorPanel") "The right sweep section must use the reusable editor panel."
    Assert-True (
        $sweepEditorFooter.Dock -eq [System.Windows.Forms.DockStyle]::Fill -and
        $sweepEditorFooter.RowCount -eq 1 -and
        $sweepEditorStart.Parent.Parent.Parent -eq $sweepEditorFooter
    ) "The right sweep editor must keep its action row in a fixed footer while status moves to the shared status bar."
    Assert-True (
        $sweepEditorTitle.Height -ge 24 -and
        $sweepEditorTitle.Bottom -le $sweepEditorScrollHost.Top
    ) "The right sweep editor must keep its title above the scrollable parameter area."
    Assert-True (
        -not $sweepEditorScrollHost.AutoScroll -and
        $sweepEditorScrollHost.AutoScrollMinSize.Height -eq 0
    ) "The pair editor must display its compact layout without vertical scrolling."
    Assert-True ($sweepEditorSecondPosition.Parent -ne $null -and $sweepEditorSecondLength.Parent -ne $null) "The right sweep section must include visible byte B position and length inputs."
    Assert-True (
        $sweepSidePanel.ColumnStyles[0].SizeType -eq
        [System.Windows.Forms.SizeType]::Percent
    ) "The annotation section must not reduce the width of the shared sweep column."
    Assert-True (
        -not $sweepEditorMode.Visible -and
        $sweepEditorPanel.IsPairCombinationSelected -and
        $sweepEditorLayout.ColumnCount -eq 6 -and
        $sweepEditorLayout.RowCount -eq 6 -and
        $sweepEditorLayout.RowStyles[0].Height -eq 0 -and
        $sweepEditorLayout.RowStyles[1].Height -gt 0 -and
        $sweepEditorLayout.RowStyles[5].Height -gt 0
    ) "The pair editor must expose a full six-row compact surface."
    [System.Windows.Forms.Application]::DoEvents()
    Assert-True (
        $sweepEditorLayout.RowStyles[3].Height -gt 0 -and
        $sweepEditorLayout.RowStyles[5].Height -gt 0 -and
        $sweepEditorPickFirst.Parent -ne $null -and
        $sweepEditorPickSecond.Parent -ne $null
    ) "Pair mode must reveal both A and B fields in the compact layout."
    Assert-True (
        $sweepEditorSummary.Text.Contains("A") -and
        $sweepEditorSummary.Text.Contains("B") -and
        $sweepEditorSummary.Text.Contains("65025")
    ) "Pair mode must show the estimated combination count. Actual: $($sweepEditorSummary.Text)"
    foreach ($accessibleControl in @(
        $sweepEditorMode,
        $sweepEditorInterval,
        $sweepEditorFirstPosition,
        $sweepEditorPickFirst,
        $sweepEditorSecondPosition,
        $sweepEditorSecondLength,
        $sweepEditorPickSecond
    )) {
        Assert-True (
            -not [string]::IsNullOrWhiteSpace($accessibleControl.AccessibleName)
    ) "Every sweep input must have an accessible name."
    }
    Assert-True (
        $sweepSidePanel.RowStyles[0].SizeType -eq
        [System.Windows.Forms.SizeType]::Absolute -and
        $sweepSidePanel.RowStyles[0].Height -eq 0 -and
        $sweepSidePanel.RowStyles[1].SizeType -eq
        [System.Windows.Forms.SizeType]::Absolute -and
        $sweepSidePanel.RowStyles[1].Height -eq 0 -and
        $sweepSidePanel.RowStyles[2].SizeType -eq
        [System.Windows.Forms.SizeType]::Percent
    ) "The hidden annotation area must release its row while the sweep area uses the remaining space."
    $collapseAnnotation = $annotationController.GetType().GetMethod(
        "Panel_CollapseRequested",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $collapseAnnotation.Invoke(
        $annotationController,
        [object[]]@($null, [System.EventArgs]::Empty))
    Assert-True (
        $sweepSidePanel.RowStyles[0].SizeType -eq
        [System.Windows.Forms.SizeType]::Absolute -and
        $sweepSidePanel.RowStyles[0].Height -eq 0 -and
        $sweepSidePanel.RowStyles[1].SizeType -eq
        [System.Windows.Forms.SizeType]::Absolute -and
        $sweepSidePanel.RowStyles[1].Height -eq 0 -and
        $sweepSidePanel.RowStyles[2].SizeType -eq
        [System.Windows.Forms.SizeType]::Percent -and
        $sweepSidePanel.ColumnStyles[0].SizeType -eq
        [System.Windows.Forms.SizeType]::Percent
    ) "Hidden annotations must remain hidden and must not reduce the sweep editor width."
    $sweepEditorMode.SelectedIndex = 1
    [System.Windows.Forms.Application]::DoEvents()
    Assert-True ($perLineSelector.Items.Count -eq 1) "Send page must expose only the adaptive packet layout."
    Assert-True ($perLineSelector.SelectedIndex -eq 0) "Send page must select the adaptive packet layout by default."
    Assert-True (-not $sweepHexBox.UseFixedBytesPerLine) "Send page packet data must resize its bytes per line with the window."
    Assert-True $sweepRadio.Checked "Opening other sweep settings must enter byte-sweep mode."
    Assert-True ($loopCountInput.Value -eq $preset.BLoopCount) "Sweep edit page must restore the preset loop count."
    Assert-True ($intervalInput.Value -eq $preset.BInterval) "Sweep edit page must restore the preset interval."
    Assert-True ($nextIntervalInput.Value -eq $preset.BNextInterval) "Sweep edit page must restore the next-preset interval."
    Assert-True ($loopCountInput.Controls.Count -gt 0 -and -not $loopCountInput.Controls[0].Visible) "Sweep loop count must use direct numeric input without spinner buttons."
    Assert-True ($intervalInput.Controls.Count -gt 0 -and -not $intervalInput.Controls[0].Visible) "Sweep interval must use direct numeric input without spinner buttons."
    Assert-True ($nextIntervalInput.Controls.Count -gt 0 -and -not $nextIntervalInput.Controls[0].Visible) "Sweep next interval must use direct numeric input without spinner buttons."
    Assert-True ($sweepHexBox.SelectionStart -eq $preset.BStart) "Sweep edit page must restore the selected start."
    Assert-True ($sweepHexBox.SelectionLength -eq $preset.BLength) "Sweep edit page must restore the selected length."
    Assert-True $sweepSaveButton.Enabled "Sweep edit page must enable direct update for a valid selection."

    if (-not [string]::IsNullOrWhiteSpace($AuditScreenshotPath)) {
        $sweepEditForm.ClientSize = [System.Drawing.Size]::new($AuditWidth, $AuditHeight)
        $sweepEditForm.StartPosition =
            [System.Windows.Forms.FormStartPosition]::Manual
        $sweepEditForm.Location = [System.Drawing.Point]::new(-32000, -32000)
        $sweepEditForm.ShowInTaskbar = $false
        $sweepEditForm.Show()
        $sweepEditorMode.SelectedIndex = 1
        $sweepEditorFirstPosition.Value = 0
        $sweepEditorSecondPosition.Value = 1
        $sweepEditForm.PerformLayout()
        [System.Windows.Forms.Application]::DoEvents()
        if ($AuditWidth -le 1200 -and $AuditHeight -le 620) {
            Assert-True (
                $sweepEditorScrollHost.VerticalScroll.Visible -or
                $sweepEditorScrollHost.AutoScrollMinSize.Height -le $sweepEditorScrollHost.ClientSize.Height
            ) "Compact windows must either scroll the right sweep editor or show all pair fields without clipping."
        }
        $screenshotDirectory = Split-Path -Parent $AuditScreenshotPath
        if (-not [string]::IsNullOrWhiteSpace($screenshotDirectory)) {
            [void][System.IO.Directory]::CreateDirectory($screenshotDirectory)
        }
        $bitmap = [System.Drawing.Bitmap]::new(
            $sweepEditForm.Width,
            $sweepEditForm.Height)
        try {
            $sweepEditForm.DrawToBitmap(
                $bitmap,
                [System.Drawing.Rectangle]::new(
                    [System.Drawing.Point]::Empty,
                    $sweepEditForm.Size))
            $bitmap.Save(
                $AuditScreenshotPath,
                [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
            $sweepEditForm.Hide()
        }
    }

    $buttonOnClick = [System.Windows.Forms.Button].GetMethod(
        "OnClick",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $pickTargetField = $sweepEditForm.GetType().GetField(
        "byteSweepPositionPickTarget",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $pickMouseUp = $sweepEditForm.GetType().GetMethod(
        "hbPacketData_ByteSweepPickMouseUp",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $leftMouseUp = [System.Windows.Forms.MouseEventArgs]::new(
        [System.Windows.Forms.MouseButtons]::Left,
        1,
        0,
        0,
        0)

    # Position pickers are only available in pair mode; restore that mode
    # after the sequential-mode assertions above.
    $sweepEditorMode.SelectedIndex = 1
    $sweepEditorFirstPosition.Value = 0
    $sweepEditorSecondPosition.Value = 1
    [System.Windows.Forms.Application]::DoEvents()
    $buttonOnClick.Invoke(
        $sweepEditorPickSecond,
        [object[]]@([System.EventArgs]::Empty))
    Assert-True (
        $pickTargetField.GetValue($sweepEditForm) -eq 2
    ) "Pick B must arm packet-byte selection for byte B."
    $sweepHexBox.SelectionStart = 0
    $sweepHexBox.SelectionLength = 1
    $pickMouseUp.Invoke(
        $sweepEditForm,
        [object[]]@($sweepHexBox, $leftMouseUp))
    Assert-True (
        $pickTargetField.GetValue($sweepEditForm) -eq 2 -and
        $sweepEditorSecondPosition.Value -eq 1
    ) "Picking the same position as byte A must keep Pick B armed and preserve its old value."
    $sweepHexBox.SelectionStart = 1
    $pickMouseUp.Invoke(
        $sweepEditForm,
        [object[]]@($sweepHexBox, $leftMouseUp))
    Assert-True (
        $pickTargetField.GetValue($sweepEditForm) -eq 0 -and
        $sweepEditorSecondPosition.Value -eq 1 -and
        $sweepHexBox.SelectionLength -eq 1
    ) "Clicking a valid packet byte must fill byte B and finish selection."

    $buttonOnClick.Invoke(
        $sweepEditorPickFirst,
        [object[]]@([System.EventArgs]::Empty))
    $sweepHexBox.SelectionStart = 0
    $pickMouseUp.Invoke(
        $sweepEditForm,
        [object[]]@($sweepHexBox, $leftMouseUp))
    Assert-True (
        $pickTargetField.GetValue($sweepEditForm) -eq 0 -and
        $sweepEditorFirstPosition.Value -eq 0
    ) "Clicking a valid packet byte must fill byte A and finish selection."

    $beginLiveDisplay = $sweepEditForm.GetType().GetMethod(
        "BeginByteSweepLiveDisplay",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $updateLiveDisplay = $sweepEditForm.GetType().GetMethod(
        "UpdateByteSweepLiveDisplay",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $endLiveDisplay = $sweepEditForm.GetType().GetMethod(
        "EndByteSweepLiveDisplay",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $beginLiveDisplay.Invoke($sweepEditForm, $null)
    $updateLiveDisplay.Invoke(
        $sweepEditForm,
        [object[]]@([long]1, [byte]0x02, [byte]0x7F))
    Assert-True (
        $sweepHexBox.ByteProvider.ReadByte(1) -eq 0x7F
    ) "The active byte cell must display the current sweep value in real time."
    Assert-True (
        $packet.PacketBuffer[1] -eq 0x02
    ) "The temporary live value must not mutate the source packet buffer."
    $updateLiveDisplay.Invoke(
        $sweepEditForm,
        [object[]]@([long]0, [byte]0x01, [byte]0x22))
    Assert-True (
        $sweepHexBox.ByteProvider.ReadByte(1) -eq 0x02
    ) "Moving to the next byte must restore the previous byte's original value."
    Assert-True (
        $sweepHexBox.ByteProvider.ReadByte(0) -eq 0x22
    ) "The newly active byte must display its current sweep value."
    $endLiveDisplay.Invoke($sweepEditForm, $null)
    Assert-True (
        $sweepHexBox.ByteProvider.ReadByte(0) -eq 0x01 -and
        $sweepHexBox.ByteProvider.ReadByte(1) -eq 0x02
    ) "Stopping or completing a sweep must restore every displayed byte to its original value."
    Assert-True (
        -not $sweepHexBox.ByteProvider.HasChanges()
    ) "A clean packet provider must remain clean after temporary live display is restored."

    $updatePairLiveDisplay = $sweepEditForm.GetType().GetMethod(
        "UpdateByteSweepLivePairDisplay",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $restoreDisplayedValue = $sweepEditForm.GetType().GetMethod(
        "RestoreByteSweepDisplayedValue",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $firstLiveActive = $sweepEditForm.GetType().GetField(
        "byteSweepLiveValueActive",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $pairLiveProgress = [WPELibrary.Lib.Socket_ByteSweepProgress]::new()
    $pairLiveProgress.PairFirstPosition = 0
    $pairLiveProgress.PairSecondPosition = 1
    $pairLiveProgress.PairFirstValue = 0x33
    $pairLiveProgress.PairSecondValue = 0x44
    $beginLiveDisplay.Invoke($sweepEditForm, $null)
    $updatePairLiveDisplay.Invoke(
        $sweepEditForm,
        [object[]]@($pairLiveProgress))
    Assert-True (
        $sweepHexBox.ByteProvider.ReadByte(0) -eq 0x33 -and
        $sweepHexBox.ByteProvider.ReadByte(1) -eq 0x44
    ) "Pair live preview must display both current byte values."
    $sweepHexBox.ByteProvider.WriteByte(0, 0x01)
    $firstLiveActive.SetValue($sweepEditForm, $false)
    $restoreDisplayedValue.Invoke($sweepEditForm, $null)
    Assert-True (
        $sweepHexBox.ByteProvider.ReadByte(1) -eq 0x02
    ) "Pair live preview cleanup must restore byte B even if byte A is already inactive."
    $endLiveDisplay.Invoke($sweepEditForm, $null)
    Assert-True (
        -not $sweepHexBox.ByteProvider.HasChanges()
    ) "Pair live preview cleanup must preserve a clean packet provider."

    $setSweepRunningState = $sweepEditForm.GetType().GetMethod(
        "SetSendRunningState",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $runWorkerCompleted = $sweepEditForm.GetType().GetMethod(
        "bgwSendPacket_RunWorkerCompleted",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $sweepWasRunningForError = $sweepEditForm.GetType().GetField(
        "byteSweepWasRunning",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $liveSelectionStartField = $sweepEditForm.GetType().GetField(
        "byteSweepLiveSelectionStart",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $liveSelectionLengthField = $sweepEditForm.GetType().GetField(
        "byteSweepLiveSelectionLength",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $sweepStartedFromEditor = $sweepEditForm.GetType().GetField(
        "byteSweepStartedFromEditorPanel",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $beginHighlightForError = $sweepEditForm.GetType().GetMethod(
        "BeginByteSweepHighlight",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $sweepProgressLabel = Get-PrivateField $sweepEditForm "tlByteSweepProgress"
    $sweepLeftStopButton = Get-PrivateField $sweepEditForm "bSendStop"
    $originalSweepReadOnly = $sweepHexBox.ReadOnly
    $sweepWasRunningForError.SetValue($sweepEditForm, $true)
    $liveSelectionStartField.SetValue($sweepEditForm, [long]0)
    $liveSelectionLengthField.SetValue($sweepEditForm, [long]1)
    $sweepHexBox.Select(0, 1)
    $sweepHexBox.Select(1, 1)
    Assert-True (
        $sweepHexBox.SelectionStart -eq 0 -and
        $sweepHexBox.SelectionLength -eq 1
    ) "A running sweep must keep the active byte selected after another byte is clicked."
    $sweepStartedFromEditor.SetValue($sweepEditForm, $false)
    $setSweepRunningState.Invoke($sweepEditForm, [object[]]@($true))
    Assert-True (-not $sweepLeftStopButton.Enabled) "A lower sweep must not enable the normal-send Stop action."
    Assert-True $sweepStopButton.Enabled "A lower sweep must enable only its own Stop action."
    Assert-True (-not $sweepEditorStop.Enabled) "A lower sweep must not enable the right-editor Stop action."
    $setSweepRunningState.Invoke($sweepEditForm, [object[]]@($false))
    $sweepStartedFromEditor.SetValue($sweepEditForm, $true)
    $setSweepRunningState.Invoke($sweepEditForm, [object[]]@($true))
    Assert-True (-not $sweepLeftStopButton.Enabled) "A right-editor sweep must not enable the normal-send Stop action."
    Assert-True (-not $sweepStopButton.Enabled) "A right-editor sweep must not enable the lower Stop action."
    Assert-True $sweepEditorStop.Enabled "A right-editor sweep must enable its own Stop action."
    $setSweepRunningState.Invoke($sweepEditForm, [object[]]@($false))
    $sweepWasRunningForError.SetValue($sweepEditForm, $true)
    $sweepStartedFromEditor.SetValue($sweepEditForm, $true)
    $setSweepRunningState.Invoke($sweepEditForm, [object[]]@($true))
    $beginLiveDisplay.Invoke($sweepEditForm, $null)
    $beginHighlightForError.Invoke($sweepEditForm, $null)
    $updateLiveDisplay.Invoke(
        $sweepEditForm,
        [object[]]@([long]0, [byte]0x01, [byte]0x55))
    $syntheticSweepError = [InvalidOperationException]::new(
        "synthetic byte sweep failure")
    $runWorkerCompleted.Invoke(
        $sweepEditForm,
        [object[]]@(
            $sweepEditForm,
            [System.ComponentModel.RunWorkerCompletedEventArgs]::new(
                $null,
                $syntheticSweepError,
                $false)))
    Assert-True (
        $sweepProgressLabel.Text.Contains($syntheticSweepError.Message)
    ) "An unexpected sweep failure must be displayed as an error instead of completion."
    Assert-True (
        $sweepHexBox.ByteProvider.ReadByte(0) -eq 0x01
    ) "An unexpected sweep failure must restore the temporary byte value."
    Assert-True (
        $sweepHexBox.ReadOnly -eq $originalSweepReadOnly
    ) "An unexpected sweep failure must restore packet editing state."

    $sweepHexBox.SelectionStart = 1
    $sweepHexBox.SelectionLength = 0
    $tryGetSweepSelectionMethod = $sweepEditForm.GetType().GetMethod(
        "TryGetByteSweepSelection",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $selectionArguments = [object[]]@([long]0, [long]0)
    $hasSingleByteSelection = $tryGetSweepSelectionMethod.Invoke(
        $sweepEditForm,
        $selectionArguments)
    Assert-True $hasSingleByteSelection "A caret on a valid byte must count as a one-byte sweep selection."
    Assert-True (
        $selectionArguments[0] -eq 1 -and $selectionArguments[1] -eq 1
    ) "A caret-only sweep selection must normalize to the current byte with length one."
    Assert-True $sweepSaveButton.Enabled "A caret-only byte sweep must enable preset saving."

    $loopCountInput.Value = 2
    $intervalInput.Value = 7
    $createWorkItemMethod = $sweepEditForm.GetType().GetMethod(
        "CreateSendWorkItem",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $singleByteWorkItem = $createWorkItemMethod.Invoke(
        $sweepEditForm,
        [object[]]@($true))
    Assert-True (
        $singleByteWorkItem.SweepStart -eq 1 -and $singleByteWorkItem.SweepLength -eq 1
    ) "A caret-only byte sweep must create a one-byte send work item."
    Assert-True (
        $singleByteWorkItem.Times -eq 2 -and $singleByteWorkItem.Interval -eq 7
    ) "A byte sweep must use the progression panel's loop count and interval."

    $null = $sweepEditForm.Handle
    $postByteSweepProgress = $sweepEditForm.GetType().GetMethod(
        "PostByteSweepProgress",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $beginLiveDisplay.Invoke($sweepEditForm, $null)
    $postByteSweepProgress.Invoke(
        $sweepEditForm,
        [object[]]@(
            [int]0,
            [byte]0x01,
            [byte]0x66,
            [int]1,
            [int]1,
            [int]1,
            $true))
    $endLiveDisplay.Invoke($sweepEditForm, $null)
    for ($messagePump = 0; $messagePump -lt 3; $messagePump++) {
        [System.Windows.Forms.Application]::DoEvents()
    }
    Assert-True (
        $sweepHexBox.ByteProvider.ReadByte(0) -eq 0x01
    ) "A queued progress callback must not overwrite restored bytes after a sweep ends."

    $sweepHexBox.Select(0, 2)
    $originalStartField = $sweepEditForm.GetType().GetField(
        "byteSweepOriginalSelectionStart",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $originalLengthField = $sweepEditForm.GetType().GetField(
        "byteSweepOriginalSelectionLength",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $sweepWasRunningField = $sweepEditForm.GetType().GetField(
        "byteSweepWasRunning",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $originalStartField.SetValue($sweepEditForm, [long]0)
    $originalLengthField.SetValue($sweepEditForm, [long]2)
    $sweepWasRunningField.SetValue($sweepEditForm, $true)
    $beginLiveDisplay.Invoke($sweepEditForm, $null)
    $highlightMethod = $sweepEditForm.GetType().GetMethod(
        "BeginByteSweepHighlight",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $highlightMethod.Invoke($sweepEditForm, $null)
    $sweepHexBox.Select(1, 1)
    $updateLiveDisplay.Invoke(
        $sweepEditForm,
        [object[]]@([long]1, [byte]0x02, [byte]0x44))
    $formClosingMethod = $sweepEditForm.GetType().GetMethod(
        "Socket_SendForm_FormClosing",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $formClosingMethod.Invoke(
        $sweepEditForm,
        [object[]]@(
            $sweepEditForm,
            [System.Windows.Forms.FormClosingEventArgs]::new(
                [System.Windows.Forms.CloseReason]::UserClosing,
                $false)))
    Assert-True (
        $sweepHexBox.ByteProvider.ReadByte(1) -eq 0x02
    ) "Closing a running sweep editor must restore the displayed byte value."
    Assert-True (
        $sweepHexBox.SelectionStart -eq 0 -and
        $sweepHexBox.SelectionLength -eq 2
    ) "Closing a running sweep editor must restore the original selection."
    Assert-True (
        $sweepHexBox.SelectionBackColor -ne [System.Drawing.Color]::Gold
    ) "Closing a running sweep editor must restore the original selection color."
}
finally {
    $sweepEditForm.Dispose()
}

$sendPresetForm = [WPELibrary.Socket_SendPresetForm]::new(
    "UI regression send preset",
    "UI regression send group")
try {
    Assert-True ($sendPresetForm.MinimumSize.Width -ge 400) "Send preset dialog needs room for localized labels."
    Assert-True ($sendPresetForm.AutoScaleMode -eq [System.Windows.Forms.AutoScaleMode]::Dpi) "Send preset dialog must scale with DPI."
    Assert-True ($null -ne $sendPresetForm.AcceptButton) "Send preset dialog must expose a primary keyboard action."
    Assert-True ($null -ne $sendPresetForm.CancelButton) "Send preset dialog must support Escape/cancel."

    $savePresetMethod = $sendPresetForm.GetType().GetMethod(
        "Save_Click",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $savePresetMethod.Invoke(
        $sendPresetForm,
        [object[]]@($sendPresetForm.AcceptButton, [EventArgs]::Empty))
    Assert-True ($sendPresetForm.DialogResult -eq [System.Windows.Forms.DialogResult]::OK) "Valid send preset input must be accepted."
    Assert-True ($sendPresetForm.PresetName -eq "UI regression send preset") "Send preset name must round-trip through the dialog."
    Assert-True ($sendPresetForm.FolderName -eq "UI regression send group") "Send preset group must round-trip through the dialog."
}
finally {
    $sendPresetForm.Dispose()
}

$testSweepFolder = "UI regression sweep group"
[WPELibrary.Lib.Socket_Cache+ByteSweepList]::lstFolders.Add($testSweepFolder)
try {
    $combinedPresetForm = [Activator]::CreateInstance(
        [WPELibrary.Socket_SendPresetForm],
        [object[]]@(
            "UI regression packet",
            "UI regression send group",
            $null,
            $true))
    try {
        $presetType = Get-PrivateField $combinedPresetForm "cbbPresetType"
        $presetFolder = Get-PrivateField $combinedPresetForm "cbbFolder"
        Assert-True ($presetType.Items.Count -eq 2) "Packet save dialog must offer send and byte-sweep preset types."
        $presetType.SelectedIndex = 1
        Assert-True $combinedPresetForm.SaveAsByteSweep "Selecting byte-sweep must change the save destination."
        Assert-True ($presetFolder.Items.Contains($testSweepFolder)) "Byte-sweep destination must list byte-sweep groups."
        Assert-True (-not $presetFolder.Items.Contains("UI regression send group")) "Byte-sweep destination must not mix in send groups."
    }
    finally {
        $combinedPresetForm.Dispose()
    }
}
finally {
    [WPELibrary.Lib.Socket_Cache+ByteSweepList]::lstFolders.Remove($testSweepFolder)
}

$createPresetMethod = [WPELibrary.Socket_SendForm].GetMethod(
    "CreateSendPreset",
    [System.Reflection.BindingFlags]::Static -bor
    [System.Reflection.BindingFlags]::NonPublic)

$folderOrderPrefix = "UI folder order " + [Guid]::NewGuid().ToString("N")
$folderA = $folderOrderPrefix + " A"
$folderB = $folderOrderPrefix + " B"
$folderC = $folderOrderPrefix + " C"
[WPELibrary.Lib.Socket_Cache+SendList]::AddFolder($folderA) | Out-Null
[WPELibrary.Lib.Socket_Cache+SendList]::AddFolder($folderB) | Out-Null
[WPELibrary.Lib.Socket_Cache+SendList]::AddFolder($folderC) | Out-Null
try {
    Assert-True (
        [WPELibrary.Lib.Socket_Cache+SendList]::MoveFolder($folderC, -1)
    ) "A packet group must be movable upward."
    $orderedFolders = [WPELibrary.Lib.Socket_Cache+SendList]::lstFolders
    $indexA = $orderedFolders.IndexOf($folderA)
    Assert-True ($orderedFolders[$indexA + 1] -eq $folderC) "Moving a group upward must change the in-memory order."
    Assert-True ($orderedFolders[$indexA + 2] -eq $folderB) "Moving a group upward must retain the other groups' relative order."
    $firstFolder = $orderedFolders[0]
    Assert-True (
        -not [WPELibrary.Lib.Socket_Cache+SendList]::MoveFolder($firstFolder, -1)
    ) "The first packet group must not move past the top boundary."

    $xmlItems = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_SendInfo]'
    $xmlItems.Add($createPresetMethod.Invoke(
        $null,
        [object[]]@($packet.PSObject.BaseObject, "folder B packet", $folderB, 1, 0)))
    $xmlItems.Add($createPresetMethod.Invoke(
        $null,
        [object[]]@($packet.PSObject.BaseObject, "folder A packet", $folderA, 1, 0)))
    $xmlItems.Add($createPresetMethod.Invoke(
        $null,
        [object[]]@($packet.PSObject.BaseObject, "folder C packet", $folderC, 1, 0)))
    $folderXml = [WPELibrary.Lib.Socket_Cache+SendList]::GetSendList_XML($xmlItems)
    $xmlFolderNames = @($folderXml.Element("Folders").Elements("Folder") |
        ForEach-Object { $_.Attribute("Name").Value })
    Assert-True (
        ($xmlFolderNames -join "|") -eq (@($folderA, $folderC, $folderB) -join "|")
    ) "Send-list XML must retain the custom packet-group order."
}
finally {
    [WPELibrary.Lib.Socket_Cache+SendList]::lstFolders.Remove($folderA)
    [WPELibrary.Lib.Socket_Cache+SendList]::lstFolders.Remove($folderB)
    [WPELibrary.Lib.Socket_Cache+SendList]::lstFolders.Remove($folderC)
}

$createdPreset = $createPresetMethod.Invoke(
    $null,
    [object[]]@($packet.PSObject.BaseObject, "created preset", "created group", 3, 25))
Assert-True ($createdPreset.SCollection.Count -eq 1) "Saving must create one packet in the send preset."
Assert-True ($createdPreset.SLoopCNT -eq 3) "Saving must retain the configured send count."
Assert-True ($createdPreset.SLoopINT -eq 25) "Saving must retain the configured interval."
Assert-True $createdPreset.SSystemSocket "New packet presets must use the current session system Socket."
Assert-True (-not [object]::ReferenceEquals($createdPreset.SCollection[0], $packet)) "Saved presets must own a packet copy."
Assert-True ($createdPreset.SCollection[0].PacketBuffer[1] -eq 0x02) "Saved preset bytes must match the edited packet."
$continuousPreset = $createPresetMethod.Invoke(
    $null,
    [object[]]@($packet.PSObject.BaseObject, "continuous preset", "created group", 0, 10))
Assert-True ($continuousPreset.SLoopCNT -eq 0) "Continuous send presets must retain the zero-count sentinel."
Assert-True ($continuousPreset.SLoopINT -eq 10) "Continuous send presets must retain their configured interval."

$capturedPackets = [System.Collections.Generic.List[WPELibrary.Lib.Socket_PacketInfo]]::new()
$packetTemplates = [System.Collections.Generic.List[WPELibrary.Lib.Socket_PacketInfo]]::new()
$matchingTemplate = [WPELibrary.Lib.Socket_PacketInfo]::new()
$matchingTemplate.PacketType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$matchingTemplate.PacketTo = "202.189.15.112:14567"
$packetTemplates.Add($matchingTemplate)
foreach ($candidate in @(
    @{ Socket = 1201; Type = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send; To = "202.189.15.112:14567" },
    @{ Socket = 1202; Type = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send; To = "203.0.113.8:14567" },
    @{ Socket = 1203; Type = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Recv; To = "202.189.15.112:14567" },
    @{ Socket = 1204; Type = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send; To = "202.189.15.112:14567" }
)) {
    $capturedPacket = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $capturedPacket.PacketSocket = $candidate.Socket
    $capturedPacket.PacketType = $candidate.Type
    $capturedPacket.PacketTo = $candidate.To
    $capturedPackets.Add($capturedPacket)
}
$matchedSocket = [WPELibrary.Lib.Socket_Cache+SocketList]::FindLatestMatchingSocket(
    $capturedPackets,
    $packetTemplates)
Assert-True ($matchedSocket -eq 1204) "Automatic Socket matching must choose the newest packet with the same type and destination."
$matchingTemplate.PacketTo = "198.51.100.9:9999"
$unmatchedSocket = [WPELibrary.Lib.Socket_Cache+SocketList]::FindLatestMatchingSocket(
    $capturedPackets,
    $packetTemplates)
Assert-True ($unmatchedSocket -eq 0) "Automatic Socket matching must not guess when destination and type do not match."

$originalCapturedPackets = @(
    [WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket)
$originalResolvedSocket = [WPELibrary.Lib.Socket_Cache+System]::SystemSocket
try {
    [WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Clear()
    foreach ($capturedPacket in $capturedPackets) {
        [WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Add(
            $capturedPacket)
    }

    $firstTemplate = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $firstTemplate.PacketType =
        [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
    $firstTemplate.PacketTo = "202.189.15.112:14567"
    $secondTemplate = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $secondTemplate.PacketType =
        [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
    $secondTemplate.PacketTo = "203.0.113.8:14567"
    $missingTemplate = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $missingTemplate.PacketType =
        [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
    $missingTemplate.PacketTo = "198.51.100.9:9999"

    [WPELibrary.Lib.Socket_Cache+System]::SystemSocket = 0
    $resolvedFirst =
        [WPELibrary.Lib.Socket_Cache+SocketList]::ResolveCurrentSocket(
            [WPELibrary.Lib.Socket_PacketInfo[]]@($firstTemplate))
    Assert-True ($resolvedFirst -eq 1204) "The first automatic match must resolve its latest exact Socket."
    $resolvedMissing =
        [WPELibrary.Lib.Socket_Cache+SocketList]::ResolveCurrentSocket(
            [WPELibrary.Lib.Socket_PacketInfo[]]@($missingTemplate))
    Assert-True ($resolvedMissing -eq 0) "A previous automatic match must not become the fallback for an unmatched preset."

    [WPELibrary.Lib.Socket_Cache+System]::SystemSocket = 7777
    $manualFallback =
        [WPELibrary.Lib.Socket_Cache+SocketList]::ResolveCurrentSocket(
            [WPELibrary.Lib.Socket_PacketInfo[]]@($missingTemplate))
    Assert-True ($manualFallback -eq 7777) "An explicitly assigned system Socket must remain available as the manual fallback."
    Assert-True (
        [WPELibrary.Lib.Socket_Cache+System]::ManualSystemSocket -eq 7777
    ) "Manual and automatically resolved Socket state must remain distinguishable."

    [WPELibrary.Lib.Socket_Cache+System]::SystemSocket = 0
    $sweepA = [WPELibrary.Lib.Socket_ByteSweepPresetInfo]::new()
    $sweepA.BID = [Guid]::NewGuid()
    $sweepA.BName = "sweep A"
    $sweepA.BFolder = "group"
    $sweepA.PacketType = $firstTemplate.PacketType
    $sweepA.PacketTo = $firstTemplate.PacketTo
    $sweepB = [WPELibrary.Lib.Socket_ByteSweepPresetInfo]::new()
    $sweepB.BID = [Guid]::NewGuid()
    $sweepB.BName = "sweep B"
    $sweepB.BFolder = "group"
    $sweepB.PacketType = $secondTemplate.PacketType
    $sweepB.PacketTo = $secondTemplate.PacketTo
    $sweepPresets =
        [System.Collections.Generic.List[WPELibrary.Lib.Socket_ByteSweepPresetInfo]]::new()
    $sweepPresets.Add($sweepA)
    $sweepPresets.Add($sweepB)
    $resolveSweepSockets = [WPELibrary.Socket_Form].GetMethod(
        "ResolveByteSweepSockets",
        [System.Reflection.BindingFlags]::Static -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $resolveSweepArguments = [object[]]@($sweepPresets, $null)
    $resolvedSweepSockets = $resolveSweepSockets.Invoke(
        $null,
        $resolveSweepArguments)
    Assert-True (
        $resolvedSweepSockets[$sweepA.BID] -eq 1204
    ) "A byte-sweep batch must preserve the first preset's exact Socket."
    Assert-True (
        $resolvedSweepSockets[$sweepB.BID] -eq 1202
    ) "A byte-sweep batch must resolve a separate Socket for each preset."
    Assert-True (
        $null -eq $resolveSweepArguments[1]
    ) "A fully resolved byte-sweep batch must not report an unresolved preset."

    $sweepPacket = [WPELibrary.Lib.Socket_PacketInfo]::new()
    $sweepPacket.PacketType = $firstTemplate.PacketType
    $sweepPacket.PacketTo = $firstTemplate.PacketTo
    $sweepPacket.PacketBuffer = [byte[]](0x01)
    $sweepEditForm = [WPELibrary.Socket_SendForm]::new(
        $sweepPacket,
        $sweepA)
    try {
        $effectiveSweepSocket = $sweepEditForm.GetType().GetMethod(
            "GetEffectiveSendSocket",
            [System.Reflection.BindingFlags]::Instance -bor
            [System.Reflection.BindingFlags]::NonPublic)
        Assert-True (
            $effectiveSweepSocket.Invoke($sweepEditForm, $null) -eq 1204
        ) "Editing an existing byte-sweep preset must use the same exact current-session Socket matcher."
    }
    finally {
        $sweepEditForm.Dispose()
    }
}
finally {
    [WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Clear()
    foreach ($capturedPacket in $originalCapturedPackets) {
        [WPELibrary.Lib.Socket_Cache+SocketList]::lstRecPacket.Add(
            $capturedPacket)
    }
    [WPELibrary.Lib.Socket_Cache+System]::SystemSocket =
        $originalResolvedSocket
}

$sendListCount = [WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Count
[WPELibrary.Lib.Socket_Cache+SendList]::SendToList($createdPreset)
try {
    Assert-True ([WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Count -eq ($sendListCount + 1)) "A saved preset must immediately enter the shared send preset list."
    Assert-True ([object]::ReferenceEquals(
        [WPELibrary.Lib.Socket_Cache+SendList]::lstSend[$sendListCount],
        $createdPreset)) "The send preset list must expose the newly saved preset."

    $originalSystemSocketForGuard = [WPELibrary.Lib.Socket_Cache+System]::SystemSocket
    try {
        $createdPreset.SSystemSocket = $false
        [WPELibrary.Lib.Socket_Cache+System]::SystemSocket = 0
        $blockedSend = [WPELibrary.Lib.Socket_Cache+Send]::DoSendAsync(
            $createdPreset.SID).GetAwaiter().GetResult()
        Assert-True ($null -eq $blockedSend) "Existing presets must be blocked before any send starts when the current session Socket is missing."
    }
    finally {
        $createdPreset.SSystemSocket = $true
        [WPELibrary.Lib.Socket_Cache+System]::SystemSocket = $originalSystemSocketForGuard
    }

    $namedSendForm = [WPELibrary.Socket_SendForm]::new($createdPreset.SCollection[0])
    try {
        $packetIdentity = Get-PrivateField $namedSendForm "tlCurrentPacketIdentity"
        Assert-True ($namedSendForm.Text.Contains($createdPreset.SName)) "Opening an existing packet must show its preset name in the window title."
        Assert-True ($packetIdentity.Text.Contains($createdPreset.SName)) "Opening an existing packet must show its preset name in the status bar."
        Assert-True ($packetIdentity.Text.Contains($createdPreset.SFolder)) "Opening an existing packet must show its group in the status bar."

        $createdPreset.SLoopCNT = 0
        $createdPreset.SLoopINT = 10
        $initSendParametersMethod = $namedSendForm.GetType().GetMethod(
            "InitSendParameters",
            [System.Reflection.BindingFlags]::Instance -bor
            [System.Reflection.BindingFlags]::NonPublic)
        $initSendParametersMethod.Invoke($namedSendForm, $null)
        $continuousRadio = Get-PrivateField $namedSendForm "rbSendType_Continuously"
        $intervalInput = Get-PrivateField $namedSendForm "nudSendType_Interval"
        Assert-True $continuousRadio.Checked "Reopening a continuous preset must restore continuous-send mode."
        Assert-True ($intervalInput.Value -eq 10) "Reopening a preset must restore its saved interval."

        $originalSystemSocket = [WPELibrary.Lib.Socket_Cache+System]::SystemSocket
        try {
            [WPELibrary.Lib.Socket_Cache+System]::SystemSocket = 4567
            $effectiveSocketMethod = $namedSendForm.GetType().GetMethod(
                "GetEffectiveSendSocket",
                [System.Reflection.BindingFlags]::Instance -bor
                [System.Reflection.BindingFlags]::NonPublic)
            Assert-True (
                $effectiveSocketMethod.Invoke($namedSendForm, $null) -eq 4567
            ) "An existing packet preset must resolve the current session system Socket instead of its stored handle."
        }
        finally {
            [WPELibrary.Lib.Socket_Cache+System]::SystemSocket = $originalSystemSocket
        }
    }
    finally {
        $namedSendForm.Dispose()
    }

    $editExistingPresetForm = [WPELibrary.Socket_SendPresetForm]::new(
        $createdPreset.SName,
        $createdPreset.SFolder,
        [Nullable[Guid]]$createdPreset.SID)
    try {
        $savePresetMethod.Invoke(
            $editExistingPresetForm,
            [object[]]@($editExistingPresetForm.AcceptButton, [EventArgs]::Empty))
        Assert-True (
            $editExistingPresetForm.DialogResult -eq
            [System.Windows.Forms.DialogResult]::OK) "An existing preset must be allowed to keep its own name and group."
    }
    finally {
        $editExistingPresetForm.Dispose()
    }
}
finally {
    [WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Remove($createdPreset)
}

$annotationPanel = Read-Source "WPELibrary\Socket_ByteAnnotationPanel.cs"
Assert-True ($annotationPanel.Contains("MinimumSize = value ? Size.Empty : new Size(130, 0);")) "Collapsed annotations must not retain the expanded minimum width."
Assert-True ($annotationPanel.Contains("ByteAnnotation_CollapseButton")) "Annotation collapse control needs an accessible label."

$mainForm = Read-Source "WPELibrary\Socket_Form.cs"
$sendFormSource = Read-Source "WPELibrary\Socket_SendForm.cs"
$sweepPresetFormSource = Read-Source "WPELibrary\Socket_ByteSweepPresetForm.cs"
$sweepMainSource = Read-Source "WPELibrary\Socket_Form.ByteSweep.cs"
$cacheSource = Read-Source "WPELibrary\Lib\Socket_Cache.cs"
$sendWorkerSource = Read-Source "WPELibrary\Lib\Socket_Send.cs"
Assert-True ($mainForm.Contains("this.MinimumSize = new System.Drawing.Size(900, 620);")) "Main workspace needs a usable minimum size."
Assert-True ($mainForm.Contains('UiText("Main_Settings")')) "Dynamic main controls must use localized resources."
Assert-True ($mainForm.Contains('ConfigureSendToolbarTextButton(this.tsSendList_Add, UiText("UI_NewPacket"))')) "The send toolbar must expose direct packet creation."
Assert-True ($mainForm.Contains("private void tsSendList_Add_Click")) "The new-packet action must be wired to a handler."
Assert-True ($mainForm.Contains("byte[] buffer = new byte[] { 0 };")) "A new packet must start with editable packet data instead of an empty legacy list."
Assert-True ($mainForm.Contains("Socket_Operation.ShowSendForm(sendInfo.SCollection[0]);")) "A newly created packet must open the single-packet editor."
Assert-True ($mainForm.Contains("AddPacketPresetToGroup(")) "Adding a captured packet must create a direct packet preset."
Assert-True ($mainForm.Contains("private Socket_SendInfo PromptAndAddPacketPreset(")) "Captured packets must be confirmed before they are saved."
Assert-True ($mainForm.Contains("new Socket_SendPresetForm(defaultName, suggestedFolder)")) "The confirmation dialog must allow packet naming and group selection."
Assert-True ($mainForm.Contains("if (dialog.ShowDialog(this) != DialogResult.OK)")) "Cancelling packet confirmation must prevent saving."
Assert-True ($sendFormSource.Contains("Socket_SendInfo existingPreset = containingPreset ?? this.savedSendPreset;")) "Every send-form save must reopen name and group confirmation, including existing presets."
Assert-True ($sendFormSource.Contains("existingPreset.SFolder = targetFolder;")) "Confirmed edits must be able to move an existing preset to another group."
Assert-True ($sendFormSource.Contains("if (dialog.SaveAsByteSweep)")) "Packet save must route the selected byte-sweep destination to the sweep preset editor."
Assert-True ($sendFormSource.Contains("this.ShowByteSweepPresetDialog(")) "New byte-sweep presets must still capture their range and parameters from the send page."
Assert-True (-not $sweepPresetFormSource.Contains("NumericUpDown")) "The sweep identity dialog must only contain name and group inputs."
Assert-True ($sweepPresetFormSource.Contains("EditDetailsRequested")) "The sweep identity dialog must distinguish navigation to other settings."
Assert-True ($sweepMainSource.Contains("new Socket_SendForm(packet, preset)")) "Other sweep settings must open the existing preset in the send page."
Assert-True ($sendFormSource.Contains("this.UpdateCurrentByteSweepPreset();")) "The sweep edit page save action must update the current preset directly."
Assert-True ($sendFormSource.Contains("Socket_Cache.ByteSweepList.TryApplyListChangeAndSave(")) "Direct sweep saves must persist immediately with rollback on failure."
Assert-True ($sendFormSource.Contains("private void InitializeSendPanelLayout()")) "The send page must define its two-panel layout explicitly."
Assert-True ($sendFormSource.Contains("this.gbSendSocket.Visible = false;")) "The legacy Socket panel must remain hidden without deleting its backing controls."
Assert-True ($sendFormSource.Contains("this.tlpParameter.ColumnCount = 2;")) "The lower editor must reserve columns only for send and progression."
Assert-True ($sendFormSource.Contains("this.UpdateByteSweepLiveDisplay(")) "Byte-sweep progress callbacks must update the active HexBox byte."
Assert-True (
    [regex]::Matches(
        $sendFormSource,
        'this\.RestoreByteSweepVisualState\(\);').Count -ge 3
) "Completion, failure, and form closing must share one byte-sweep visual cleanup path."
Assert-True ($sendFormSource.Contains("this.PostSendCounterUpdate();")) "Every ordinary packet result must request a live counter refresh."
Assert-True ($sendFormSource.Contains("Interlocked.CompareExchange(ref this.sendCounterUpdateScheduled, 1, 0)")) "Live counter refreshes must be coalesced to avoid flooding the UI queue."
Assert-True ($sendFormSource.Contains("private void UpdateSendCounterLabels()")) "Live and final send progress must share one label update path."
Assert-True ($cacheSource.Contains("Socket_ByteAnnotationEngine.Clone(value.ByteAnnotations)")) "Sweep updates must retain edited byte annotations."
Assert-True ($sendFormSource.Contains("Socket_Cache.SocketList.ResolveCurrentSocket(new[] { this.SPI });")) "Saved packet editors must resolve a matching current-session Socket."
Assert-True ($sendFormSource.Contains("this.InitSendParameters();")) "Reopened packet presets must restore their saved send mode and interval."
Assert-True ($sendFormSource.Contains("this.rbSendType_Continuously.Checked")) "Preset saving must preserve continuous-send mode instead of rewriting it as one send."
Assert-True ($sendWorkerSource.Contains("while (this.LoopCNT == 0 || loopIndex < this.LoopCNT)")) "A zero loop count must execute continuously until stopped."
Assert-True ($mainForm.Contains('e.Value = UiText("UI_ContinuousSend");')) "The packet list must label continuous presets accurately."
Assert-True ([regex]::IsMatch(
    $cacheSource,
    'ssReturn\.StartSend\(\s*sendName,\s*(?:useSystemSocket,\s*)?resolvedSocket,')) "Existing send presets must pass an immutable resolved Socket into the worker."
Assert-True ($sendWorkerSource.Contains("this.resolvedSystemSocket = Math.Max(0, ResolvedSystemSocket);")) "A send worker must snapshot its resolved Socket before it starts."
Assert-True ($sendWorkerSource.Contains("Socket = this.resolvedSystemSocket;")) "A running send worker must keep using its own Socket snapshot."
Assert-True (-not $sendWorkerSource.Contains("Socket = Socket_Cache.System.SystemSocket;")) "A running send worker must not reread the mutable global Socket."
Assert-True ($cacheSource.Contains("public static int FindLatestMatchingSocket(")) "Preset sends need a shared exact type-and-destination Socket matcher."
Assert-True ($cacheSource.Contains("Socket_Cache.SocketList.ResolveCurrentSocket(sendCollection)")) "Hotkey and direct preset sends must refresh the current Socket before sending."
Assert-True ($cacheSource.Contains("_ = DoSendAsync(SID);")) "Hotkey sends must start asynchronously without blocking the UI dispatcher."
Assert-True ($cacheSource.Contains("public static int ManualSystemSocket")) "Automatic matching must keep an explicit manual fallback separate from resolved state."
Assert-True ($cacheSource.Contains("internal static int ResolveSystemSocket(int matchedSocket)")) "Manual fallback selection and resolved-state updates must remain atomic."
Assert-True ($mainForm.Contains("private bool EnsureCurrentSystemSocket(IEnumerable<Socket_SendInfo> sendInfos)")) "Single and batch preset sends must auto-match before rejecting a missing current system Socket."
Assert-True ($mainForm.Contains("items.All(item =>")) "A normal send batch must validate every preset instead of accepting one match for the whole batch."
Assert-True ($sweepMainSource.Contains("presetSockets[preset.BID]")) "Byte-sweep batches must use the Socket resolved for each preset."
Assert-True ($mainForm.Contains('UiText("UI_MoveGroupUp")')) "Packet-group context menu must expose move up."
Assert-True ($mainForm.Contains('UiText("UI_MoveGroupDown")')) "Packet-group context menu must expose move down."
Assert-True ($mainForm.Contains("private void MoveSelectedSendFolder(int offset)")) "Packet-group move actions must share one bounded reorder path."
Assert-True ($cacheSource.Contains("public static bool MoveFolder(string folderName, int offset)")) "Packet-group order must be maintained by the shared send-list model."
Assert-True ([regex]::Matches(
    $mainForm,
    'SelectionBackColor = System\.Drawing\.SystemColors\.Window').Count -ge 2) "Send count and interval cells must remain readable when their row is selected."
Assert-True ($mainForm.Contains("this.dgvSendList.CellClick += this.dgvSendList_CellClick;")) "Send count and interval cells must support one-click editing."
Assert-True ($mainForm.Contains("this.BeginInvoke(new Action(textBox.SelectAll));")) "Numeric preset editing must select the existing value for direct replacement."
Assert-True ($mainForm.Contains('UiText("UI_AddToPacketGroup")')) "Packet context menus must target groups directly."
Assert-True ($mainForm.Contains("item.SCollection != null && item.SCollection.Count > 0")) "Empty legacy lists must not appear as packets."
Assert-True ($mainForm.Contains('Name = "cSendNow"')) "Packet rows must retain the per-row send action."
Assert-True ($mainForm.Contains('Name = "cStopNow"')) "Packet rows must retain the per-row stop action."
Assert-True ([regex]::Matches(
    $mainForm,
    'Name = "c(?:Send|Stop)Now",\s*HeaderText = string\.Empty').Count -eq 2) "Per-row action columns must keep blank headers so labels are not mistaken for buttons."
Assert-True ($cacheSource.Contains("packet.PacketBuffer == null ? null : (byte[])packet.PacketBuffer.Clone()")) "Copied packet rows must own independent packet bytes."

$sendFormEnglishResources = Read-Source "WPELibrary\Socket_SendForm.en-US.resx"
$mainEnglishResources = Read-Source "WPELibrary\Properties\Resources.en-US.resx"
Assert-True ($mainEnglishResources.Contains("<value>New packet</value>")) "The new-packet action needs an English resource."
Assert-True ($sendFormEnglishResources.Contains("Save preset")) "The save action must clearly target a send preset."

$processListSource = Read-Source "WinsockPacketEditor\ProcessList_Form.cs"
Assert-True ($processListSource.Contains("finally")) "Process loading must restore the UI after failures."
Assert-True ($processListSource.Contains("private bool ShowEmulatorOnly = true;")) "Injection mode must default to approved emulator processes."
Assert-True ($processListSource.Contains("IsSupportedInjectionProcess")) "Selected processes must be revalidated before injection."
Assert-True (-not $processListSource.Contains("OpenFileDialog")) "Injection mode must not expose arbitrary EXE selection."

$programSource = Read-Source "WinsockPacketEditor\Lib\Program.cs"
Assert-True ($programSource.Contains("MultiLanguage.SetDefaultLanguage(Socket_Cache.System.DefaultLanguage);")) "Startup UI must apply the saved language before creating the first form."

Write-Host "UI design regression checks passed."
