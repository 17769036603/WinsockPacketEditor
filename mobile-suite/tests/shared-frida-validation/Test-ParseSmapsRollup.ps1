$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ParseSmapsRollup.ps1')
function Assert([bool]$Value,[string]$Name) { if (-not $Value) { throw "FAIL: $Name" }; Write-Output "PASS: $Name" }
$lf = "Rss: 100 kB`nPss: 60 kB`nPrivate_Dirty: 20 kB`n"
$crlf = "Rss: 100 kB`r`nPss: 60 kB`r`nPrivate_Dirty: 20 kB`r`n"
$a = ConvertFrom-SharedFridaSmapsRollup $lf
$b = ConvertFrom-SharedFridaSmapsRollup $crlf
Assert ($a.PssKiB -eq 60 -and $a.RssKiB -eq 100 -and $a.PrivateDirtyKiB -eq 20) 'parse LF metrics in KiB'
Assert ($b.PssKiB -eq 60 -and $b.RssKiB -eq 100 -and $b.PrivateDirtyKiB -eq 20) 'parse CRLF metrics in KiB'
Assert ($b.RawMetricLines.Count -eq 3) 'retain only the three requested smaps metric lines'
$missing = ConvertFrom-SharedFridaSmapsRollup "Rss: 100 kB`nPss: 60 kB`n"
Assert (-not $missing.PrivateDirtyConfirmed -and $null -eq $missing.PrivateDirtyKiB) 'missing metric stays explicitly unconfirmed'
