$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DevicePreflight.ps1')
function Assert([bool]$Value,[string]$Name) { if (-not $Value) { throw "FAIL: $Name" }; Write-Output "PASS: $Name" }
Assert (Test-SharedFridaDevicePreflight '2000' 'x86_64' '34' 'readable') 'accept exact SDK 34 preflight'
Assert (-not (Test-SharedFridaDevicePreflight '2000' 'x86_64' '134' 'readable')) 'reject SDK 134'
Assert (-not (Test-SharedFridaDevicePreflight '2000' 'x86_64' '3' 'readable' 'other property contains 34')) 'reject SDK 3 even if unrelated output contains 34'
Assert (-not (Test-SharedFridaDevicePreflight '2000' 'x86_64' "3`n34" 'readable')) 'reject SDK output with extra line 34'
Assert (-not (Test-SharedFridaDevicePreflight '0' 'x86_64' '34' 'readable')) 'reject unexpected UID'
