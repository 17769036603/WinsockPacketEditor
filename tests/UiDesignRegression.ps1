param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$hexBoxDll = Join-Path $repo "WPELibrary\bin\$Configuration\Be.Windows.Forms.HexBox.dll"
$libraryDll = Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
$applicationDirectory = Join-Path $repo "WinsockPacketEditor\bin\$Configuration"
$applicationExe = Get-ChildItem -LiteralPath $applicationDirectory -Filter "*.exe" |
    Where-Object { $_.Name -notlike "EasyHook*Svc.exe" } |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrEmpty($applicationExe)) {
    throw "Application executable not found in $applicationDirectory"
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll
[void][System.Reflection.Assembly]::LoadFrom($applicationExe)

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
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
    Assert-True ($sendForm.MinimumSize.Width -ge 950) "Send dialog must not shrink until its controls are clipped."
}
finally {
    $sendForm.Dispose()
}

$annotationPanel = Read-Source "WPELibrary\Socket_ByteAnnotationPanel.cs"
Assert-True ($annotationPanel.Contains("MinimumSize = value ? Size.Empty : new Size(180, 0);")) "Collapsed annotations must not retain the expanded minimum width."
Assert-True ($annotationPanel.Contains("ByteAnnotation_CollapseButton")) "Annotation collapse control needs an accessible label."

$mainForm = Read-Source "WPELibrary\Socket_Form.cs"
Assert-True ($mainForm.Contains("this.MinimumSize = new System.Drawing.Size(900, 620);")) "Main workspace needs a usable minimum size."
Assert-True ($mainForm.Contains('UiText("Main_Settings")')) "Dynamic main controls must use localized resources."

$processListSource = Read-Source "WinsockPacketEditor\ProcessList_Form.cs"
Assert-True ($processListSource.Contains("finally")) "Process loading must restore the UI after failures."
Assert-True ($processListSource.Contains("ofdCreate.ShowDialog(this) != DialogResult.OK")) "Canceling file selection must leave the current choice unchanged."
Assert-True ($processListSource.Contains("this.ShowEmulatorOnly = !this.ShowEmulatorOnly;")) "Emulator filtering must provide a path back to all processes."

$programSource = Read-Source "WinsockPacketEditor\Lib\Program.cs"
Assert-True ($programSource.Contains("MultiLanguage.SetDefaultLanguage(Socket_Cache.System.DefaultLanguage);")) "Startup UI must apply the saved language before creating the first form."

Write-Host "UI design regression checks passed."
