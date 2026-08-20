param(
    [string]$BuildDirectory = "$(Join-Path (Split-Path -Parent $PSScriptRoot) 'WinsockPacketEditor\bin\Release')"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $BuildDirectory "Be.Windows.Forms.HexBox.dll")
Add-Type -Path (Join-Path $BuildDirectory "WPELibrary.dll")

$form = [WPELibrary.Socket_Form]::new()
try {
    $flags = [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic
    function Get-PrivateField([string]$name) {
        $field = $form.GetType().GetField($name, $flags)
        if ($null -eq $field) { throw "Missing field: $name" }
        return $field.GetValue($form)
    }

    $navigation = Get-PrivateField "tlpAutomationNavigation"
    $automationHome = Get-PrivateField "tlpAutomationHome"
    $automation = Get-PrivateField "tcAutomation"
    $assistantGrid = Get-PrivateField "tlpAssistantButtons"
    $send = Get-PrivateField "bAutomationSend"
    $sweep = Get-PrivateField "bAutomationSweep"
    $assistant = Get-PrivateField "bAutomationAssistant"
    $filter = Get-PrivateField "bAutomationFilter"

    $form.ClientSize = [System.Drawing.Size]::new(1200, 800)
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = [System.Drawing.Point]::new(-32000, -32000)
    $form.ShowInTaskbar = $false
    $form.Show()
    $form.PerformLayout()
    [System.Windows.Forms.Application]::DoEvents()

    if ($navigation.Controls.Count -ne 4) { throw "Expected four homepage buttons." }
    if ($automationHome.Parent.Name -ne "tlpInformation") { throw "Homepage host is not in the information area." }
    if ($automation.SelectedTab.Name -ne "tpSendList") { throw "Homepage default tab is not Send." }
    if ($navigation.GetCellPosition($send).Column -ne 0 -or
        $navigation.GetCellPosition($send).Row -ne 0) { throw "Send position is invalid." }
    if ($navigation.GetCellPosition($sweep).Column -ne 1 -or
        $navigation.GetCellPosition($sweep).Row -ne 0) { throw "Sweep position is invalid." }
    if ($navigation.GetCellPosition($assistant).Column -ne 0 -or
        $navigation.GetCellPosition($assistant).Row -ne 1) { throw "Assistant position is invalid." }
    if ($navigation.GetCellPosition($filter).Column -ne 1 -or
        $navigation.GetCellPosition($filter).Row -ne 1) { throw "Filter position is invalid." }
    if ($assistantGrid.ColumnCount -ne 5) { throw "Assistant grid must have five columns." }
    if ($send.Height -le 0 -or $send.Height -gt 40 -or
        $assistant.Height -le 0 -or $assistant.Height -gt 40 -or
        $send.Width -le 0 -or $send.Width -gt 240 -or
        $assistant.Width -le 0 -or $assistant.Width -gt 240) {
        throw "Homepage button size is invalid. send=$($send.Width)x$($send.Height), assistant=$($assistant.Width)x$($assistant.Height), nav=$($navigation.Width)x$($navigation.Height)"
    }

    $navigation.Dock = [System.Windows.Forms.DockStyle]::None
    $navigation.Width = 300
    $navigation.PerformLayout()
    [System.Windows.Forms.Application]::DoEvents()
    if ($send.Width -le 0 -or $send.Width -ge 240 -or
        $assistant.Width -le 0 -or $assistant.Width -ge 240) {
        throw "Homepage buttons did not adapt to the narrow layout. send=$($send.Width), assistant=$($assistant.Width)"
    }

    Write-Output "Automation home runtime regression checks passed."
}
finally {
    $form.Dispose()
}
