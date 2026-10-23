[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApkPath,
    [ValidateRange(1, 100)][int]$Top = 20,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$resolvedApk = (Resolve-Path -LiteralPath $ApkPath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedApk -PathType Leaf) -or [IO.Path]::GetExtension($resolvedApk) -ine '.apk') {
    throw 'ApkPath must point to an existing .apk file.'
}

$archive = $null
try {
    $archive = [IO.Compression.ZipFile]::OpenRead($resolvedApk)
    $entries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') } | ForEach-Object {
        [pscustomobject]@{
            Path = $_.FullName
            CompressedBytes = [long]$_.CompressedLength
            UncompressedBytes = [long]$_.Length
        }
    })
    $compressedTotal = [long](($entries | Measure-Object -Property CompressedBytes -Sum).Sum)
    $uncompressedTotal = [long](($entries | Measure-Object -Property UncompressedBytes -Sum).Sum)
    $apkBytes = [long](Get-Item -LiteralPath $resolvedApk).Length
    foreach ($entry in $entries) {
        $entry | Add-Member -NotePropertyName CompressionRatio -NotePropertyValue $(if ($entry.UncompressedBytes -gt 0) { [math]::Round(100.0 * $entry.CompressedBytes / $entry.UncompressedBytes, 2) } else { $null })
        $entry | Add-Member -NotePropertyName CompressedSharePercent -NotePropertyValue $(if ($compressedTotal -gt 0) { [math]::Round(100.0 * $entry.CompressedBytes / $compressedTotal, 2) } else { 0.0 })
        $entry | Add-Member -NotePropertyName UncompressedSharePercent -NotePropertyValue $(if ($uncompressedTotal -gt 0) { [math]::Round(100.0 * $entry.UncompressedBytes / $uncompressedTotal, 2) } else { 0.0 })
    }

    $report = [ordered]@{
        apkFileName = [IO.Path]::GetFileName($resolvedApk)
        apkFileBytes = $apkBytes
        archiveEntryCompressedBytes = $compressedTotal
        archiveEntryUncompressedBytes = $uncompressedTotal
        zipMetadataAndSigningOverheadBytes = ($apkBytes - $compressedTotal)
        topByCompressedBytes = @($entries | Sort-Object CompressedBytes -Descending | Select-Object -First $Top)
        topByUncompressedBytes = @($entries | Sort-Object UncompressedBytes -Descending | Select-Object -First $Top)
    }
    if ($Json) {
        $report | ConvertTo-Json -Depth 6
        return
    }

    Write-Output "APK: $($report.apkFileName)"
    Write-Output "File bytes: $($report.apkFileBytes)"
    Write-Output "Compressed ZIP entries: $($report.archiveEntryCompressedBytes)"
    Write-Output "Uncompressed ZIP entries: $($report.archiveEntryUncompressedBytes)"
    Write-Output "ZIP metadata and signing overhead: $($report.zipMetadataAndSigningOverheadBytes)"
    Write-Output ''
    Write-Output "Largest $Top entries by compressed size (with share of compressed entry bytes):"
    $report.topByCompressedBytes | Format-Table -AutoSize Path, CompressedBytes, UncompressedBytes, CompressionRatio, CompressedSharePercent
    Write-Output "Largest $Top entries by uncompressed size (with share of uncompressed entry bytes):"
    $report.topByUncompressedBytes | Format-Table -AutoSize Path, UncompressedBytes, CompressedBytes, CompressionRatio, UncompressedSharePercent
}
finally {
    if ($null -ne $archive) { $archive.Dispose() }
}
