param(
    [Parameter(Mandatory = $true)]
    [string]$Title,
    [Parameter(Mandatory = $true)]
    [string]$CommandFile
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$taskText = [string]::new([char[]](0x4EFB, 0x52A1, 0x5B8C, 0x6210))
$countText = [string]::new([char[]](0x6570, 0x91CF))
$form = [System.Windows.Forms.Form]::new()
$taskLabel = [System.Windows.Forms.Label]::new()
$countLabel = [System.Windows.Forms.Label]::new()
$timer = [System.Windows.Forms.Timer]::new()

try {
    $form.Text = $Title
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = [System.Drawing.Point]::new(950, 120)
    $form.ClientSize = [System.Drawing.Size]::new(920, 280)
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedToolWindow
    $form.ShowInTaskbar = $false
    $form.TopMost = $true
    $form.BackColor = [System.Drawing.Color]::White

    foreach ($label in @($taskLabel, $countLabel)) {
        $label.AutoSize = $true
        $label.BackColor = [System.Drawing.Color]::White
        $label.ForeColor = [System.Drawing.Color]::Black
        $label.Font = [System.Drawing.Font]::new(
            "Microsoft YaHei",
            42,
            [System.Drawing.FontStyle]::Bold,
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    $taskLabel.Text = $taskText
    $taskLabel.Location = [System.Drawing.Point]::new(24, 20)
    $countLabel.Text = "$countText 42"
    $countLabel.Location = [System.Drawing.Point]::new(24, 112)
    $form.Controls.Add($taskLabel)
    $form.Controls.Add($countLabel)

    $timer.Interval = 80
    $timer.Add_Tick({
        if (-not (Test-Path -LiteralPath $CommandFile)) {
            return
        }
        $command = [System.IO.File]::ReadAllText($CommandFile).Trim().ToLowerInvariant()
        Remove-Item -LiteralPath $CommandFile -Force -ErrorAction SilentlyContinue
        if ($command -eq "hide") {
            $taskLabel.Visible = $false
            $countLabel.Visible = $false
        }
        elseif ($command -eq "show") {
            $taskLabel.Visible = $true
            $countLabel.Visible = $true
        }
        elseif ($command -eq "close") {
            $form.Close()
        }
    })
    $form.Add_Shown({ $timer.Start() })
    [System.Windows.Forms.Application]::Run($form)
}
finally {
    $timer.Stop()
    $timer.Dispose()
    if ($null -ne $taskLabel.Font) { $taskLabel.Font.Dispose() }
    if ($null -ne $countLabel.Font) { $countLabel.Font.Dispose() }
    $form.Dispose()
}
