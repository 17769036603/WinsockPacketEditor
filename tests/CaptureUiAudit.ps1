param(
    [string]$Configuration = "Debug",
    [string]$BuildDirectory = "",
    [switch]$SeedSendPackets,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
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
    Where-Object { $_.Name -notlike "EasyHook*Svc.exe" } |
    Select-Object -First 1 -ExpandProperty FullName

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll
[void][System.Reflection.Assembly]::LoadFrom($applicationExe)

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

function Get-PrivateField($instance, [string]$name) {
    $flags = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $field = $instance.GetType().GetField($name, $flags)
    if ($null -eq $field) {
        throw "Missing private field: $($instance.GetType().FullName).$name"
    }

    return $field.GetValue($instance)
}

function Capture-Form($form, [string]$fileName) {
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object System.Drawing.Point -32000, -32000
    $form.ShowInTaskbar = $false
    $form.Show()
    for ($index = 0; $index -lt 20; $index++) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }

    $form.PerformLayout()
    $bitmap = New-Object System.Drawing.Bitmap $form.Width, $form.Height
    try {
        $bounds = New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height
        $form.DrawToBitmap($bitmap, $bounds)
        $bitmap.Save(
            (Join-Path $resolvedOutput $fileName),
            [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
        $form.Hide()
    }
}

function Select-HexByteWithMouse($form, $hexBox, [long]$index) {
    if (-not $form.Visible) {
        $form.Show()
        [System.Windows.Forms.Application]::DoEvents()
    }

    $instancePrivate = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $getBytePoint = $hexBox.GetType().GetMethod(
        "GetBytePointF",
        $instancePrivate,
        $null,
        [Type[]]@([long]),
        $null)
    $onMouseDown = $hexBox.GetType().GetMethod(
        "OnMouseDown",
        $instancePrivate)
    $onMouseUp = [System.Windows.Forms.Control].GetMethod(
        "OnMouseUp",
        $instancePrivate)
    $point = [System.Drawing.PointF]$getBytePoint.Invoke(
        $hexBox,
        [object[]]@($index))
    $mouseArgs = [System.Windows.Forms.MouseEventArgs]::new(
        [System.Windows.Forms.MouseButtons]::Left,
        1,
        [int][Math]::Floor($point.X + $hexBox.CharSize.Width),
        [int][Math]::Floor($point.Y + ($hexBox.CharSize.Height / 2)),
        0)
    $onMouseDown.Invoke($hexBox, [object[]]@($mouseArgs))
    $onMouseUp.Invoke($hexBox, [object[]]@($mouseArgs))
}

$injector = New-Object WinsockPacketEditor.Injector_Form
try {
    Capture-Form $injector "01-startup.png"
}
finally {
    $injector.Dispose()
}

$processList = New-Object WinsockPacketEditor.ProcessList_Form
try {
    Capture-Form $processList "02-process-list.png"
}
finally {
    $processList.Dispose()
}

$preset = New-Object WPELibrary.Lib.Socket_ByteSweepPresetInfo
$preset.BID = [Guid]::NewGuid()
$preset.BName = "Preview preset"
$preset.BFolder = "Preview group"
$preset.BStart = 0
$preset.BLength = 2
$preset.BLoopCount = 1
$preset.BInterval = 1000
$preset.BNextInterval = 0
$preset.Buffer = [byte[]](0x01, 0x02, 0x03, 0x04)
$presetForm = [WPELibrary.Socket_ByteSweepPresetForm]::new($preset, $false)
try {
    Capture-Form $presetForm "03-byte-sweep-preset-edit.png"
}
finally {
    $presetForm.Dispose()
}

$packet = New-Object WPELibrary.Lib.Socket_PacketInfo
$packet.PacketTime = [DateTime]::Now
$packet.PacketSocket = 1
$packet.PacketType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$packet.PacketFrom = "127.0.0.1:10000"
$packet.PacketTo = "127.0.0.1:20000"
$packet.PacketBuffer = [byte[]](0x01, 0x02, 0x03, 0x04)
$packet.RawBuffer = [byte[]](0x01, 0x02, 0x03, 0x04)
$packet.PacketLen = 4
$sendForm = [WPELibrary.Socket_SendForm]::new($packet)
try {
    Capture-Form $sendForm "05-send-packet.png"
    $sendHexBox = Get-PrivateField $sendForm "hbPacketData"
    Select-HexByteWithMouse $sendForm $sendHexBox 2
    Capture-Form $sendForm "05a-send-single-byte-click.png"
    $setSendRunningState = $sendForm.GetType().GetMethod(
        "SetSendRunningState",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $setSendRunningState.Invoke($sendForm, [object[]]@($true))
    Capture-Form $sendForm "05b-send-running-state.png"
    $setSendRunningState.Invoke($sendForm, [object[]]@($false))
}
finally {
    $sendForm.Close()
    $sendForm.Dispose()
}

$sweepSendForm = [WPELibrary.Socket_SendForm]::new($packet, $preset)
try {
    Capture-Form $sweepSendForm "06-byte-sweep-send-edit.png"
    $setSweepRunningState = $sweepSendForm.GetType().GetMethod(
        "SetSendRunningState",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $beginSweepHighlight = $sweepSendForm.GetType().GetMethod(
        "BeginByteSweepHighlight",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $restoreSweepHighlight = $sweepSendForm.GetType().GetMethod(
        "RestoreByteSweepHighlight",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $beginLiveDisplay = $sweepSendForm.GetType().GetMethod(
        "BeginByteSweepLiveDisplay",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $updateLiveDisplay = $sweepSendForm.GetType().GetMethod(
        "UpdateByteSweepLiveDisplay",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $endLiveDisplay = $sweepSendForm.GetType().GetMethod(
        "EndByteSweepLiveDisplay",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $sweepHexBox = Get-PrivateField $sweepSendForm "hbPacketData"
    $setSweepRunningState.Invoke($sweepSendForm, [object[]]@($true))
    $beginLiveDisplay.Invoke($sweepSendForm, $null)
    $beginSweepHighlight.Invoke($sweepSendForm, $null)
    $sweepHexBox.Select(0, 1)
    $updateLiveDisplay.Invoke(
        $sweepSendForm,
        [object[]]@([long]0, [byte]0x01, [byte]0xA5))
    Capture-Form $sweepSendForm "06b-byte-sweep-live-value.png"
    $endLiveDisplay.Invoke($sweepSendForm, $null)
    $restoreSweepHighlight.Invoke($sweepSendForm, $null)
    $setSweepRunningState.Invoke($sweepSendForm, [object[]]@($false))
}
finally {
    $sweepSendForm.Close()
    $sweepSendForm.Dispose()
}

[WPELibrary.Lib.Socket_Cache+ByteSweepList]::lstFolders.Add("Preview sweep group")
$sendPresetForm = [Activator]::CreateInstance(
    [WPELibrary.Socket_SendPresetForm],
    [object[]]@(
        "Packet name",
        "Target group",
        $null,
        $true))
try {
    $presetType = Get-PrivateField $sendPresetForm "cbbPresetType"
    $presetType.SelectedIndex = 1
    Capture-Form $sendPresetForm "06-send-preset.png"
}
finally {
    $sendPresetForm.Close()
    $sendPresetForm.Dispose()
    [WPELibrary.Lib.Socket_Cache+ByteSweepList]::lstFolders.Remove("Preview sweep group")
}

$databaseType = [WPELibrary.Lib.Socket_Cache+DataBase]
$staticFlags = [System.Reflection.BindingFlags]::Static -bor
    [System.Reflection.BindingFlags]::NonPublic
$dbPathField = $databaseType.GetField("dbPath", $staticFlags)
$dbNameField = $databaseType.GetField("dbName", $staticFlags)
$connectionField = $databaseType.GetField("conStr", $staticFlags)
$originalDbPath = $dbPathField.GetValue($null)
$originalDbName = $dbNameField.GetValue($null)
$originalConnection = $connectionField.GetValue($null)
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wpe-ui-audit-" + [Guid]::NewGuid().ToString("N"))
$temporaryDatabase = Join-Path $temporaryRoot "ui-audit.db"

try {
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $dbPathField.SetValue($null, $temporaryRoot)
    $dbNameField.SetValue($null, "ui-audit.db")
    $connectionField.SetValue($null, "Data Source=$temporaryDatabase;Version=3;")
    [WPELibrary.Lib.Socket_Cache+DataBase]::InitDB()

    if ($SeedSendPackets) {
        [WPELibrary.Lib.Socket_Cache+SendList]::AddFolder("Preview group")
        $singleByteAnnotation = New-Object WPELibrary.Lib.Socket_ByteAnnotationInfo
        $singleByteAnnotation.Start = 2
        $singleByteAnnotation.Length = 1
        $singleByteAnnotation.Note = "Single byte"
        $singleByteAnnotation.Color = [WPELibrary.Lib.Socket_ByteAnnotationColor]::Yellow
        $packet.ByteAnnotations.Add($singleByteAnnotation)
        $createPresetMethod = [WPELibrary.Socket_SendForm].GetMethod(
            "CreateSendPreset",
            [System.Reflection.BindingFlags]::Static -bor
            [System.Reflection.BindingFlags]::NonPublic)
        $previewSend = $createPresetMethod.Invoke(
            $null,
            [object[]]@($packet.PSObject.BaseObject, "Single packet", "Preview group", 1, 1000))
        [WPELibrary.Lib.Socket_Cache+SendList]::SendToList($previewSend)

        $namedSendForm = [WPELibrary.Socket_SendForm]::new($previewSend.SCollection[0])
        try {
            Capture-Form $namedSendForm "07-named-send-packet.png"
        }
        finally {
            $namedSendForm.Close()
            $namedSendForm.Dispose()
        }
    }

    $mainForm = New-Object WPELibrary.Socket_Form
    try {
        Capture-Form $mainForm "04-main-workspace.png"
    }
    finally {
        $mainForm.Close()
        $mainForm.Dispose()
    }
}
finally {
    [WPELibrary.Lib.Socket_Cache+System]::InvokeAction = $null
    $dbPathField.SetValue($null, $originalDbPath)
    $dbNameField.SetValue($null, $originalDbName)
    $connectionField.SetValue($null, $originalConnection)

    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}

Write-Host "UI audit screenshots captured in $resolvedOutput"
