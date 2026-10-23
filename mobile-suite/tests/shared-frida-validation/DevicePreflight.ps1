function Test-SharedFridaDevicePreflight {
    param([string]$Uid,[string]$Abi,[string]$Sdk,[string]$Smaps,[string]$OtherOutput='')
    return ($Uid.Trim() -eq '2000' -and $Abi.Trim() -eq 'x86_64' -and $Sdk.Trim() -eq '34' -and $Smaps.Trim() -eq 'readable')
}
