param(
    [string]$BuildDirectory = "$(Join-Path (Split-Path -Parent $PSScriptRoot) 'WPELibrary\bin\ReviewFixRelease')"
)

$ErrorActionPreference = "Stop"
Add-Type -Path (Join-Path $BuildDirectory "WPELibrary.dll")

$root = Join-Path ([System.IO.Path]::GetTempPath()) ("WpeCryptoRegression-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $root | Out-Null
$path = Join-Path $root "payload.xml"
$password = "review-password-2026"

try {
    $storedPassword = [WPELibrary.Lib.Socket_Operation]::PassWord_Encrypt("proxy-secret")
    if (-not $storedPassword.StartsWith("DPAPI1:")) {
        throw "Proxy password storage is missing the DPAPI version marker."
    }
    if ([WPELibrary.Lib.Socket_Operation]::PassWord_Decrypt($storedPassword) -ne "proxy-secret") {
        throw "Proxy password protection round-trip failed."
    }
    if ([WPELibrary.Lib.Socket_Operation]::PassWord_Decrypt("legacy-plain-password") -ne "legacy-plain-password") {
        throw "Plain-text remote password migration compatibility failed."
    }

    $legacyMethod = [WPELibrary.Lib.Socket_Operation].GetMethod(
        "GetLegacyAESKeyFromString",
        [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Static)
    $legacyKey = $legacyMethod.Invoke($null, [object[]]@($password))
    $legacyPlain = [System.Text.Encoding]::UTF8.GetBytes('<Root><Value>legacy</Value></Root>')
    $legacyAes = [System.Security.Cryptography.Aes]::Create()
    $legacyAes.Key = $legacyKey
    $legacyAes.IV = $legacyKey
    $legacyStream = New-Object System.IO.MemoryStream
    $legacyCrypto = New-Object System.Security.Cryptography.CryptoStream(
        $legacyStream,
        $legacyAes.CreateEncryptor(),
        [System.Security.Cryptography.CryptoStreamMode]::Write)
    $legacyCrypto.Write($legacyPlain, 0, $legacyPlain.Length)
    $legacyCrypto.FlushFinalBlock()
    $legacyCrypto.Dispose()
    $legacyAes.Dispose()
    [System.IO.File]::WriteAllBytes($path, $legacyStream.ToArray())
    $legacyRoundTrip = [WPELibrary.Lib.Socket_Operation]::DecryptXMLFile($path, $password)
    $legacyStream.Dispose()
    if ($null -eq $legacyRoundTrip -or $legacyRoundTrip.Root.Value -ne "legacy") {
        throw "Legacy encrypted XML import compatibility failed."
    }

    [System.IO.File]::WriteAllText($path, '<Root><Value>confidential</Value></Root>', [System.Text.Encoding]::UTF8)
    [WPELibrary.Lib.Socket_Operation]::EncryptXMLFile($path, $password)
    $first = [System.IO.File]::ReadAllBytes($path)
    $magic = [System.Text.Encoding]::ASCII.GetBytes("WPEXML2")
    for ($i = 0; $i -lt $magic.Length; $i++) {
        if ($first[$i] -ne $magic[$i]) { throw "The encrypted XML envelope header is invalid." }
    }

    $roundTrip = [WPELibrary.Lib.Socket_Operation]::DecryptXMLFile($path, $password)
    if ($null -eq $roundTrip -or $roundTrip.Root.Value -ne "confidential") {
        throw "Encrypted XML round-trip failed."
    }

    [System.IO.File]::WriteAllText($path, '<Root><Value>confidential</Value></Root>', [System.Text.Encoding]::UTF8)
    [WPELibrary.Lib.Socket_Operation]::EncryptXMLFile($path, $password)
    $second = [System.IO.File]::ReadAllBytes($path)
    if ([Convert]::ToBase64String($first) -eq [Convert]::ToBase64String($second)) {
        throw "Encrypted XML output did not change its random salt/IV."
    }

    $second[$second.Length - 1] = $second[$second.Length - 1] -bxor 1
    [System.IO.File]::WriteAllBytes($path, $second)
    if ($null -ne [WPELibrary.Lib.Socket_Operation]::DecryptXMLFile($path, $password)) {
        throw "Tampered encrypted XML was accepted."
    }

    Write-Output "Crypto regression checks passed."
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
