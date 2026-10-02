param(
    [Parameter(Mandatory = $true)]
    [string]$BuildDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$build = (Resolve-Path -LiteralPath $BuildDirectory).Path
Set-Location -LiteralPath $build
Add-Type -Path (Join-Path $build "Be.Windows.Forms.HexBox.dll")
Add-Type -Path (Join-Path $build "System.Data.SQLite.dll")
Add-Type -Path (Join-Path $build "WPELibrary.dll")

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (
    "wpe-clean-preset-" + [Guid]::NewGuid().ToString("N"))
$databaseName = "clean-test.db"
$databaseFile = Join-Path $tempDirectory $databaseName
New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null

try {
    $databaseType = [WPELibrary.Lib.Socket_Cache+DataBase]
    $bindingFlags = [System.Reflection.BindingFlags]::Static -bor
        [System.Reflection.BindingFlags]::NonPublic
    $dbPathField = $databaseType.GetField("dbPath", $bindingFlags)
    $dbNameField = $databaseType.GetField("dbName", $bindingFlags)
    $connectionField = $databaseType.GetField("connectionString", $bindingFlags)
    if ($null -eq $dbPathField -or $null -eq $dbNameField -or $null -eq $connectionField) {
        throw "Database fields were not found."
    }

    $dbPathField.SetValue($null, $tempDirectory)
    $dbNameField.SetValue($null, $databaseName)
    $connectionField.SetValue($null, "Data Source=$databaseFile;Version=3;")

    $databaseType::InitDB()
    [WPELibrary.Lib.Socket_Cache+RobotList]::LoadRobotList_FromDB()
    $presetTables = @{
        Send = $databaseType::SelectTable_Send()
        ByteSweep = $databaseType::SelectTable_ByteSweepPreset()
        Robot = $databaseType::SelectTable_Robot()
    }
    foreach ($entry in $presetTables.GetEnumerator()) {
        if ($null -eq $entry.Value -or $entry.Value.Rows.Count -ne 0) {
            $count = if ($null -eq $entry.Value) { -1 } else { $entry.Value.Rows.Count }
            throw "clean package initialized $count $($entry.Key) presets"
        }
    }

    Write-Output "CleanPresetPackageRegression: PASS (send, byte-sweep, robot presets = 0)"
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}
