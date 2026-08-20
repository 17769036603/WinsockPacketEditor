param(
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$source = Get-Content (
    Join-Path $repo "WPELibrary\Lib\Socket_ByteSweepEngine.cs"
) -Raw -Encoding UTF8

if (-not $source.Contains("firstOriginal + firstValueNumber - 1") -or
    -not $source.Contains("secondOriginal + secondValueNumber - 1")) {
    throw "Pair byte sweeps must include each byte's current value as the first value."
}

if (-not [string]::IsNullOrWhiteSpace($BuildDirectory)) {
    Add-Type -Path (Join-Path $BuildDirectory "Be.Windows.Forms.HexBox.dll")
    Add-Type -Path (Join-Path $BuildDirectory "WPELibrary.dll")

    $sentValues = New-Object 'System.Collections.Generic.List[string]'
    $sendDelegateType = [System.Func``2].MakeGenericType([byte[]], [bool])
    $sendDelegate = [System.Management.Automation.LanguagePrimitives]::ConvertTo(
        {
            param([byte[]]$buffer)
            [void]$script:sentValues.Add(("{0:X2}:{1:X2}" -f $buffer[0], $buffer[1]))
            return $true
        },
        $sendDelegateType)

    $result = [WPELibrary.Lib.Socket_ByteSweepEngine]::ExecutePairCombination(
        [byte[]](0x10, 0x20),
        0,
        3,
        0,
        1,
        2,
        0,
        $sendDelegate,
        [System.Threading.CancellationToken]::None,
        $null)

    if ($result.TotalSend -ne 6 -or
        $sentValues[0] -ne "10:20" -or
        $sentValues[-1] -ne "12:21") {
        throw "Pair byte sweeps must start from the original values."
    }
}

Write-Output "Byte-sweep pair start regression checks passed."
