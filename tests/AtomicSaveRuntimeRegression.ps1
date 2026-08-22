param(
    [string]$BuildDirectory = "$(Join-Path (Split-Path -Parent $PSScriptRoot) 'WinsockPacketEditor\work\AtomicSaveFixDebug')"
)

$ErrorActionPreference = "Stop"
$build = [System.IO.Path]::GetFullPath($BuildDirectory)
$hexBox = Join-Path $build "Be.Windows.Forms.HexBox.dll"
$sqlite = Join-Path $build "System.Data.SQLite.dll"
$library = Join-Path $build "WPELibrary.dll"
Add-Type -Path $hexBox
Add-Type -Path $sqlite
Add-Type -Path $library

$tempDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "work\AtomicSaveRuntime"
$sourceDatabase = "C:\WPE64Cache\小黑封包助手.db"
$databaseName = "atomic-source.db"
$databaseFile = Join-Path $tempDirectory $databaseName
$markerName = "AtomicSaveMarker"
$hold = $null

if (-not (Test-Path -LiteralPath $sourceDatabase)) {
    throw "Missing source database: $sourceDatabase"
}

New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceDatabase -Destination $databaseFile -Force

try {
    $databaseType = [WPELibrary.Lib.Socket_Cache+DataBase]
    $flags = [System.Reflection.BindingFlags]::Static -bor
        [System.Reflection.BindingFlags]::NonPublic
    $dbPathField = $databaseType.GetField("dbPath", $flags)
    $dbNameField = $databaseType.GetField("dbName", $flags)
    $connectionField = $databaseType.GetField("connectionString", $flags)
    if ($null -eq $dbPathField -or $null -eq $dbNameField -or $null -eq $connectionField) {
        throw "Database fields were not found."
    }
    $dbPathField.SetValue($null, $tempDirectory)
    $dbNameField.SetValue($null, $databaseName)
    $connectionField.SetValue($null, "Data Source=$databaseFile;Version=3;")

    # Deny delete-sharing so File.Replace cannot commit. SQLite still permits
    # a normal database connection because read/write sharing remains enabled.
    $hold = [System.IO.File]::Open(
        $databaseFile,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::ReadWrite)

    $filterListType = [WPELibrary.Lib.Socket_Cache+FilterList]
    $sendListType = [WPELibrary.Lib.Socket_Cache+SendList]
    $byteSweepListType = [WPELibrary.Lib.Socket_Cache+ByteSweepList]
    $robotListType = [WPELibrary.Lib.Socket_Cache+RobotList]
    if (-not $filterListType::SaveFilterList_ToDB()) {
        throw "Filter list save failed."
    }

    $sendListType::lstFolders.Clear()
    $sendListType::lstFolders.Add("runtime-send")
    if (-not $sendListType::SaveSendList_ToDB()) {
        throw "Send list save failed."
    }

    $byteSweepListType::lstFolders.Clear()
    $byteSweepListType::lstFolders.Add("runtime-sweep")
    if (-not $byteSweepListType::SaveByteSweepList_ToDB()) {
        throw "Byte-sweep list save failed."
    }

    $robotListType::lstFolders.Clear()
    $robotListType::lstFolders.Add("runtime-robot")
    if (-not $robotListType::SaveRobotList_ToDB()) {
        throw "Robot list save failed."
    }

    $saveAction = [Action] {
        $staged = Get-ChildItem -LiteralPath $tempDirectory -Filter "$databaseName.saving-*.db" |
            Select-Object -First 1
        if ($null -eq $staged) {
            throw "Staged database was not created."
        }

        $connection = New-Object System.Data.SQLite.SQLiteConnection(
            "Data Source=$($staged.FullName);Version=3;")
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            try {
                $command.CommandText = "CREATE TABLE IF NOT EXISTS $markerName (Value TEXT NOT NULL); INSERT INTO $markerName (Value) VALUES ('committed');"
                $command.ExecuteNonQuery() | Out-Null
            }
            finally {
                $command.Dispose()
            }
        }
        finally {
            $connection.Dispose()
        }
    }

    $saved = $databaseType::ExecuteAtomicSave($saveAction, "AtomicSaveRuntimeRegression")
    if (-not $saved) {
        throw "ExecuteAtomicSave returned false."
    }
}
finally {
    if ($null -ne $hold) {
        $hold.Dispose()
    }
}

$verify = $null
$count = 0
$verifyError = $null
try {
    $verify = New-Object System.Data.SQLite.SQLiteConnection(
        "Data Source=$databaseFile;Version=3;")
    $verify.Open()
    $command = $verify.CreateCommand()
    try {
        $command.CommandText = "SELECT COUNT(*) FROM $markerName WHERE Value = 'committed';"
        $count = [int]$command.ExecuteScalar()
    }
    finally {
        $command.Dispose()
    }
}
catch {
    $verifyError = $_.Exception
}
finally {
    if ($null -ne $verify) {
        $verify.Dispose()
    }
    if (Test-Path -LiteralPath $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}

if ($null -ne $verifyError) {
    throw $verifyError
}

if ($count -ne 1) {
    throw "SQLite backup commit did not persist the staged mutation."
}

Write-Output "AtomicSaveRuntimeRegression: PASS"
