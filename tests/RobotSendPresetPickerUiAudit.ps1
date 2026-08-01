param(
    [Parameter(Mandatory = $true)]
    [string]$BuildDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$build = [System.IO.Path]::GetFullPath($BuildDirectory)
$output = [System.IO.Path]::GetFullPath($OutputDirectory)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $build "Be.Windows.Forms.HexBox.dll")
Add-Type -Path (Join-Path $build "WPELibrary.dll")
[System.IO.Directory]::CreateDirectory($output) | Out-Null

function Get-PrivateField($instance, [string]$name) {
    $flags = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    $field = $instance.GetType().GetField($name, $flags)
    if ($null -eq $field) {
        throw "Missing private field: $name"
    }
    return $field.GetValue($instance)
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function Capture-Form($form, [string]$fileName) {
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object System.Drawing.Point -32000, -32000
    $form.ShowInTaskbar = $false
    $form.Show()
    for ($index = 0; $index -lt 12; $index++) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }

    $bitmap = New-Object System.Drawing.Bitmap $form.Width, $form.Height
    try {
        $bounds = New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height
        $form.DrawToBitmap($bitmap, $bounds)
        $bitmap.Save(
            (Join-Path $output $fileName),
            [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
        $form.Hide()
    }
}

[WPELibrary.Lib.MultiLanguage]::SetDefaultLanguage("zh-CN")
[WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Clear()
[WPELibrary.Lib.Socket_Cache+SendList]::lstFolders.Clear()
[WPELibrary.Lib.Socket_Cache+SendList]::lstFolders.Add("Common")
[WPELibrary.Lib.Socket_Cache+SendList]::lstFolders.Add("Battle")
$collection = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]'
$first = [WPELibrary.Lib.Socket_SendInfo]::new(
    $true,
    [Guid]::NewGuid(),
    "Long preset name for tooltip verification",
    $false,
    1,
    0,
    $collection,
    "",
    "Common",
    0)
$second = [WPELibrary.Lib.Socket_SendInfo]::new(
    $true,
    [Guid]::NewGuid(),
    "Duplicate name",
    $false,
    1,
    0,
    $collection,
    "",
    "Battle",
    1)
[WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Add($first)
[WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Add($second)

$form = [WPELibrary.Socket_SendPresetPickerForm]::new($first.SID)
try {
    $tree = Get-PrivateField $form "tvGroups"
    $search = Get-PrivateField $form "txtSearch"
    $grid = Get-PrivateField $form "dgvPresets"
    $empty = Get-PrivateField $form "lEmptyState"
    $confirm = Get-PrivateField $form "bConfirm"

    Assert-True ($tree.Nodes.Count -eq 3) "Picker group order/count is incorrect."
    Assert-True ($grid.Rows.Count -eq 2) "Picker did not preserve preset order."
    Assert-True ($form.SelectedSendPresetId -eq $first.SID) "Initial preset selection is incorrect."
    Assert-True ($grid.Rows[0].Cells[0].ToolTipText -eq $first.SName) "Long-name tooltip is missing."
    Capture-Form $form "01-send-preset-picker.png"
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()

    $search.Text = "not-found"
    [System.Windows.Forms.Application]::DoEvents()
    Assert-True ($grid.Rows.Count -eq 0) "Search did not filter the current list."
    Assert-True (
        $empty.Visible -and -not $confirm.Enabled) (
            "Empty state or confirm state is incorrect. rows={0}, empty={1}, confirm={2}" -f
            $grid.Rows.Count,
            $empty.Visible,
            $confirm.Enabled)
    $search.Text = ""
    [System.Windows.Forms.Application]::DoEvents()
    $tree.SelectedNode = $tree.Nodes[2]
    [System.Windows.Forms.Application]::DoEvents()
    Assert-True ($grid.Rows.Count -eq 1 -and $grid.Rows[0].Cells[0].Value -eq $second.SName) "Group filtering is incorrect."

    $cellDoubleClick = $form.GetType().GetMethod(
        "dgvPresets_CellDoubleClick",
        [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic)
    $cellDoubleClick.Invoke(
        $form,
        [object[]]@($grid, [System.Windows.Forms.DataGridViewCellEventArgs]::new(0, 0))) | Out-Null
    Assert-True ($form.DialogResult -eq [System.Windows.Forms.DialogResult]::OK) "Double-click confirmation failed."

    $instructionTable = New-Object System.Data.DataTable
    $robotInfo = [WPELibrary.Lib.Socket_RobotInfo]::new(
        $true,
        [Guid]::NewGuid(),
        "Picker preview robot",
        $instructionTable)
    $robotForm = [WPELibrary.Socket_RobotForm]::new($robotInfo)
    try {
        $selectedName = Get-PrivateField $robotForm "lSelectedSendPreset"
        $selectedFolder = Get-PrivateField $robotForm "lSelectedSendFolder"
        $insertButton = Get-PrivateField $robotForm "bSend_SendList"
        Assert-True ($selectedName.Text -like "*Long preset name*") "Robot page did not show the selected preset name."
        Assert-True ($selectedFolder.Text -like "*Common*") "Robot page did not show the selected preset group."
        Assert-True $insertButton.Enabled "Robot insert button should be enabled when presets exist."
        Capture-Form $robotForm "02-robot-send-list-picker.png"
    }
    finally {
        $robotForm.Close()
        $robotForm.Dispose()
    }

    [WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Clear()
    [WPELibrary.Lib.Socket_Cache+SendList]::lstFolders.Clear()
    [WPELibrary.Lib.MultiLanguage]::SetDefaultLanguage("en-US")
    $emptyRobotForm = [WPELibrary.Socket_RobotForm]::new($robotInfo)
    try {
        $emptyName = Get-PrivateField $emptyRobotForm "lSelectedSendPreset"
        $emptyInsert = Get-PrivateField $emptyRobotForm "bSend_SendList"
        Assert-True ($emptyName.Text -like "*No send presets*") "Robot empty state is missing."
        Assert-True (-not $emptyInsert.Enabled) "Robot insert button should be disabled without presets."
    }
    finally {
        $emptyRobotForm.Close()
        $emptyRobotForm.Dispose()
    }
}
finally {
    $form.Close()
    $form.Dispose()
    [WPELibrary.Lib.Socket_Cache+SendList]::lstSend.Clear()
    [WPELibrary.Lib.Socket_Cache+SendList]::lstFolders.Clear()
}

Write-Host "Robot send preset picker UI audit passed. Screenshot: $(Join-Path $output '01-send-preset-picker.png')"
