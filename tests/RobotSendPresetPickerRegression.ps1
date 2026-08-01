param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Assert-Contains([string]$path, [string]$text) {
    $content = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if ($content.IndexOf($text, [System.StringComparison]::Ordinal) -lt 0) {
        throw "Missing '$text' in $path"
    }
}

function Assert-Resource([string]$path, [string]$key) {
    [xml]$resource = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $match = $resource.root.data | Where-Object { $_.name -eq $key }
    if ($null -eq $match) {
        throw "Missing resource '$key' in $path"
    }
}

$robotForm = Join-Path $RepoRoot "WPELibrary\Socket_RobotForm.cs"
$pickerForm = Join-Path $RepoRoot "WPELibrary\Socket_SendPresetPickerForm.cs"
$project = Join-Path $RepoRoot "WPELibrary\WPELibrary.csproj"
$cache = Join-Path $RepoRoot "WPELibrary\Lib\Socket_Cache.cs"
$zhResource = Join-Path $RepoRoot "WPELibrary\Socket_SendPresetPickerForm.resx"
$enResource = Join-Path $RepoRoot "WPELibrary\Socket_SendPresetPickerForm.en-US.resx"

Assert-Contains $robotForm "selectedSendPresetId"
Assert-Contains $robotForm "InitSendPresetPickerLayout"
Assert-Contains $robotForm "Socket_SendPresetPickerForm"
Assert-Contains $robotForm "InstructionType.SendSendList"
Assert-Contains $robotForm "selectedSendPresetId.ToString().ToUpper()"
Assert-Contains $robotForm "DialogResult.OK"
Assert-Contains $pickerForm "Socket_Cache.SendList.lstFolders"
Assert-Contains $pickerForm "this.sendPresets = Socket_Cache.SendList.lstSend.ToList()"
Assert-Contains $pickerForm "StringComparison.OrdinalIgnoreCase"
Assert-Contains $pickerForm "dgvPresets_CellDoubleClick"
Assert-Contains $pickerForm "txtSearch_TextChanged"
Assert-Contains $pickerForm "Picker_EmptyState"
Assert-Contains $pickerForm "ToolTipText"
Assert-Contains $project "Socket_SendPresetPickerForm.cs"
Assert-Contains $project "Socket_SendPresetPickerForm.resx"
Assert-Contains $project "Socket_SendPresetPickerForm.en-US.resx"
Assert-Contains $cache 'return string.IsNullOrEmpty(SFolder) ? SName : SFolder + " / " + SName;'

foreach ($key in @(
        "Picker_Title",
        "Picker_Search",
        "Picker_All",
        "Picker_EmptyState",
        "UI_OK",
        "UI_Cancel",
        "Robot_SelectList",
        "Robot_SelectedNameFormat",
        "Robot_SelectedFolderFormat")) {
    Assert-Resource $zhResource $key
    Assert-Resource $enResource $key
}

Write-Host "Robot send preset picker regression checks passed."
