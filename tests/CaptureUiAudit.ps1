param(
    [string]$Configuration = "Debug",
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$hexBoxDll = Join-Path $repo "WPELibrary\bin\$Configuration\Be.Windows.Forms.HexBox.dll"
$libraryDll = Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
$applicationDirectory = Join-Path $repo "WinsockPacketEditor\bin\$Configuration"
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
$presetForm = [WPELibrary.Socket_ByteSweepPresetForm]::new($preset, $true)
try {
    Capture-Form $presetForm "03-byte-sweep-preset.png"
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
}
finally {
    $sendForm.Close()
    $sendForm.Dispose()
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
