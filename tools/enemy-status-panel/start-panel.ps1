param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$PanelArgs
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$userProfile = $env:USERPROFILE
if ([string]::IsNullOrWhiteSpace($userProfile)) { $userProfile = [Environment]::GetFolderPath('UserProfile') }
$pythonwCandidates = @(
    (Join-Path $userProfile 'AppData\Local\Programs\Python\Python313\pythonw.exe'),
    (Join-Path $userProfile 'AppData\Local\Programs\Python\Python312\pythonw.exe'),
    (Join-Path $userProfile 'AppData\Local\Programs\Python\Python311\pythonw.exe'),
    (Join-Path $userProfile '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\pythonw.exe')
)
$pythonw = $pythonwCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $pythonw) { $pythonw = (Get-Command pythonw.exe -ErrorAction SilentlyContinue).Source }
if (-not $pythonw) {
    $pyw = Get-Command pyw.exe -ErrorAction SilentlyContinue
    if ($pyw) { $pythonw = $pyw.Source }
}
if (-not $pythonw) { throw '找不到 pythonw.exe 或 pyw.exe；请安装带 Tkinter 的 Python 或放置 bundled Python。' }
$panelMain = Join-Path $scriptDir 'main.py'
$launchArguments = @(('"' + $panelMain + '"'))
if ($PanelArgs) { $launchArguments += $PanelArgs }
Start-Process -FilePath $pythonw -ArgumentList $launchArguments -WorkingDirectory $scriptDir
