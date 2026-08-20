param(
    [string]$Configuration = "Debug",
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$resolvedBuildDirectory = if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $null
}
else {
    [System.IO.Path]::GetFullPath($BuildDirectory)
}
$libraryDll = if ($null -eq $resolvedBuildDirectory) {
    Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"
}
else {
    Join-Path $resolvedBuildDirectory "WPELibrary.dll"
}

Add-Type -Path $libraryDll

$socketFormSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repo "WPELibrary\Socket_Form.cs")

function Assert-SourceContains([string]$source, [string]$expected, [string]$message) {
    Assert-True ($source.Contains($expected)) $message
}

function Assert-SourceNotContains([string]$source, [string]$unexpected, [string]$message) {
    Assert-True (-not $source.Contains($unexpected)) $message
}

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) {
        throw "$message expected=$expected actual=$actual"
    }
}

function Assert-True($condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

$bindingFlags = [System.Reflection.BindingFlags]::Static -bor
    [System.Reflection.BindingFlags]::NonPublic -bor
    [System.Reflection.BindingFlags]::Public
$analyzerType = $libraryDll | ForEach-Object {
    [System.Reflection.Assembly]::LoadFrom($_).GetType(
        "WPELibrary.Lib.Socket_PacketReadableAnalyzer",
        $true)
}
$analyze = $analyzerType.GetMethod("Analyze", $bindingFlags)

function Analyze-Bytes([byte[]]$bytes) {
    $arguments = New-Object object[] 1
    $arguments[0] = $bytes
    return $analyze.Invoke($null, $arguments)
}

function Read-ResultProperty($result, [string]$name) {
    return $result.GetType().GetProperty($name).GetValue($result, $null)
}

function Assert-Kind($result, [string]$expected, [string]$message) {
    Assert-Equal $expected (Read-ResultProperty $result "KindKey") $message
}

Assert-SourceNotContains $socketFormSource "this.InitReadableContentUI();" "main form must keep the readable content panel hidden after layout rollback"
Assert-SourceContains $socketFormSource "this.hbPacketData.ReadOnly = false;" "main form must keep the original Hex editor"

Assert-Kind (Analyze-Bytes ([byte[]]@())) "UI_ReadableKind_Empty" "empty packet must be identified"

$chineseText = [string]::Concat([char]0x4F60, [char]0x597D, [char]0xFF0C, [char]0x5C01, [char]0x5305)
$utf8 = [System.Text.Encoding]::UTF8.GetBytes($chineseText)
$utf8Result = Analyze-Bytes $utf8
Assert-Kind $utf8Result "UI_ReadableKind_Text" "UTF-8 Chinese text must be identified as text"
Assert-Equal "UTF-8" (Read-ResultProperty $utf8Result "EncodingName") "UTF-8 encoding must be reported"

$gbk = [System.Text.Encoding]::GetEncoding("GBK").GetBytes($chineseText)
$gbkResult = Analyze-Bytes $gbk
Assert-Kind $gbkResult "UI_ReadableKind_Text" "GBK Chinese text must be identified as text"
Assert-Equal "GBK" (Read-ResultProperty $gbkResult "EncodingName") "GBK encoding must be reported"

$jsonResult = Analyze-Bytes ([System.Text.Encoding]::UTF8.GetBytes('{"name":"demo","count":1}'))
Assert-Kind $jsonResult "UI_ReadableKind_JSON" "JSON must be identified and formatted"
Assert-True ((Read-ResultProperty $jsonResult "Preview") -match '"name"') "formatted JSON must retain its fields"

$xmlResult = Analyze-Bytes ([System.Text.Encoding]::UTF8.GetBytes('<root><item>demo</item></root>'))
Assert-Kind $xmlResult "UI_ReadableKind_XML" "XML must be identified"

$httpResult = Analyze-Bytes ([System.Text.Encoding]::UTF8.GetBytes("GET /demo HTTP/1.1`r`nHost: example.test"))
Assert-Kind $httpResult "UI_ReadableKind_HTTP" "HTTP text must be identified"

$pe = New-Object byte[] 128
$pe[0] = 0x4D
$pe[1] = 0x5A
[Array]::Copy([BitConverter]::GetBytes([int]0x40), 0, $pe, 0x3C, 4)
$pe[0x40] = 0x50
$pe[0x41] = 0x45
$peResult = Analyze-Bytes $pe
Assert-Kind $peResult "UI_ReadableKind_PE" "valid MZ plus PE signature must be identified as PE"

$mzResult = Analyze-Bytes ([byte[]](0x4D, 0x5A, 0x01, 0x02, 0x03))
Assert-Kind $mzResult "UI_ReadableKind_MZ" "MZ without PE signature must not be called a PE"

$pngResult = Analyze-Bytes ([byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
Assert-Kind $pngResult "UI_ReadableKind_PNG" "PNG signature must be identified"

$zipResult = Analyze-Bytes ([byte[]](0x50, 0x4B, 0x03, 0x04, 0x00, 0x00))
Assert-Kind $zipResult "UI_ReadableKind_ZIP" "ZIP signature must be identified"

$binaryResult = Analyze-Bytes ([byte[]](0x00, 0x01, 0x02, 0x41, 0x42, 0x43, 0x44, 0x00))
Assert-Kind $binaryResult "UI_ReadableKind_Binary" "mixed binary data must remain binary"
Assert-True ((Read-ResultProperty $binaryResult "Preview") -match "ABCD") "binary readable strings must be extracted"

$longText = New-Object byte[] 9000
for ($i = 0; $i -lt $longText.Length; $i++) {
    $longText[$i] = 0x41
}
$longResult = Analyze-Bytes $longText
Assert-Equal $true (Read-ResultProperty $longResult "IsTruncated") "long previews must be truncated"

Write-Output "Packet readable analyzer regression passed; homepage panel remains hidden."
