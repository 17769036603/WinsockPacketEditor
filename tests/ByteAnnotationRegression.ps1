param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$hexBoxDll = Join-Path $repo "WPELibrary\bin\$Configuration\Be.Windows.Forms.HexBox.dll"
$libraryDll = Join-Path $repo "WPELibrary\bin\$Configuration\WPELibrary.dll"

Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) {
        throw "$message expected=$expected actual=$actual"
    }
}

$annotation = New-Object WPELibrary.Lib.Socket_ByteAnnotationInfo
$annotation.Start = 4
$annotation.Length = 3
$annotation.Note = "opcode"
$annotation.Color = [WPELibrary.Lib.Socket_ByteAnnotationColor]::Blue
$items = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_ByteAnnotationInfo]'
$items.Add($annotation)

[WPELibrary.Lib.Socket_ByteAnnotationEngine]::AdjustForInsert($items, 2, 2)
Assert-Equal 6 $annotation.Start "insert-before must shift start"
Assert-Equal 3 $annotation.Length "insert-before must preserve length"

[WPELibrary.Lib.Socket_ByteAnnotationEngine]::AdjustForInsert($items, 7, 2)
Assert-Equal 6 $annotation.Start "insert-inside must preserve start"
Assert-Equal 5 $annotation.Length "insert-inside must expand range"

[WPELibrary.Lib.Socket_ByteAnnotationEngine]::AdjustForDelete($items, 5, 3)
Assert-Equal 5 $annotation.Start "overlap-delete must collapse start"
Assert-Equal 3 $annotation.Length "overlap-delete must shrink range"

$xml = [WPELibrary.Lib.Socket_ByteAnnotationEngine]::Serialize($items)
$roundTrip = [WPELibrary.Lib.Socket_ByteAnnotationEngine]::Deserialize($xml)
Assert-Equal 1 $roundTrip.Count "annotation XML round-trip count"
Assert-Equal "opcode" $roundTrip[0].Note "annotation XML round-trip note"
Assert-Equal 0 ([WPELibrary.Lib.Socket_ByteAnnotationEngine]::Deserialize("").Count) "old data without annotations must load empty"

$other = New-Object WPELibrary.Lib.Socket_ByteAnnotationInfo
$other.Start = 8
$other.Length = 2
$items.Add($other)
Assert-Equal $true ([WPELibrary.Lib.Socket_ByteAnnotationEngine]::Overlaps($items, 7, 2, $null)) "overlap must be rejected"
Assert-Equal $false ([WPELibrary.Lib.Socket_ByteAnnotationEngine]::Overlaps($items, 10, 1, $null)) "adjacent range must be allowed"

$partial = New-Object WPELibrary.Lib.Socket_ByteAnnotationInfo
$partial.Start = 0
$partial.Length = 4
$partialItems = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_ByteAnnotationInfo]'
$partialItems.Add($partial)
$clipped = [WPELibrary.Lib.Socket_ByteAnnotationEngine]::ForSelection($partialItems, 2, 2)
Assert-Equal 1 $clipped.Count "partially selected annotation must be preserved"
Assert-Equal 0 $clipped[0].Start "partially selected annotation must be rebased"
Assert-Equal 2 $clipped[0].Length "partially selected annotation must be clipped"

$invalidXml = '<Annotations><Annotation Start="0" Length="3" Color="999">first</Annotation><Annotation Start="2" Length="2" Color="Red">overlap</Annotation><Annotation Start="9" Length="2" Color="Blue">outside</Annotation></Annotations>'
$normalized = [WPELibrary.Lib.Socket_ByteAnnotationEngine]::Deserialize($invalidXml, 10)
Assert-Equal 1 $normalized.Count "invalid, overlapping and out-of-bounds annotations must be rejected"
Assert-Equal ([WPELibrary.Lib.Socket_ByteAnnotationColor]::Yellow) $normalized[0].Color "invalid colors must use the safe default"

$providerAnnotation = New-Object WPELibrary.Lib.Socket_ByteAnnotationInfo
$providerAnnotation.Start = 3
$providerAnnotation.Length = 2
$providerItems = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_ByteAnnotationInfo]'
$providerItems.Add($providerAnnotation)
$provider = New-Object WPELibrary.Lib.Socket_AnnotatedByteProvider -ArgumentList ([byte[]](0, 1, 2, 3, 4)), $providerItems
$provider.InsertBytes(1, [byte[]](99))
Assert-Equal 4 $providerAnnotation.Start "provider insert must update annotations after a successful byte edit"
$startBeforeFailure = $providerAnnotation.Start
$lengthBeforeFailure = $provider.Length
try {
    $provider.DeleteBytes(4, 99)
    throw "invalid delete unexpectedly succeeded"
}
catch [System.ArgumentOutOfRangeException] {
}
Assert-Equal $startBeforeFailure $providerAnnotation.Start "failed byte edits must not mutate annotations"
Assert-Equal $lengthBeforeFailure $provider.Length "failed byte edits must not mutate bytes"

$sqliteDll = Join-Path $repo "WPELibrary\bin\$Configuration\System.Data.SQLite.dll"
Add-Type -Path $sqliteDll
$databaseType = [WPELibrary.Lib.Socket_Cache+DataBase]
$bindingFlags = [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::NonPublic
$connectionField = $databaseType.GetField("conStr", $bindingFlags)
$createSend = $databaseType.GetMethod("CreateTable_Send", $bindingFlags)
$createByteSweep = $databaseType.GetMethod("CreateTable_ByteSweep", $bindingFlags)
$originalConnection = $connectionField.GetValue($null)
$tempDatabase = Join-Path ([System.IO.Path]::GetTempPath()) ("wpe-byte-annotation-" + [Guid]::NewGuid().ToString("N") + ".db")
$tempConnection = "Data Source=$tempDatabase;Version=3;"
$connectionField.SetValue($null, $tempConnection)

try {
    $connection = New-Object System.Data.SQLite.SQLiteConnection($tempConnection)
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = @"
CREATE TABLE SendCollection (GUID TEXT NOT NULL, Socket INTEGER NOT NULL, Type INTEGER NOT NULL, IPFrom TEXT NOT NULL, IPTo TEXT NOT NULL, Buffer BLOB);
CREATE TABLE ByteSweepPreset (GUID TEXT NOT NULL PRIMARY KEY, IsEnable BOOLEAN DEFAULT 0, Name TEXT NOT NULL, FolderName TEXT NOT NULL, SortOrder INTEGER NOT NULL DEFAULT 0, Interval INTEGER NOT NULL DEFAULT 1000, StartOffset INTEGER NOT NULL DEFAULT 0, ByteLength INTEGER NOT NULL DEFAULT 1, PacketType INTEGER NOT NULL, IPFrom TEXT, IPTo TEXT, Buffer BLOB);
"@
    [void]$command.ExecuteNonQuery()
    $command.Dispose()
    $connection.Dispose()

    Assert-Equal $true $createSend.Invoke($null, @()) "send schema migration must succeed"
    Assert-Equal $true $createByteSweep.Invoke($null, @()) "byte-sweep schema migration must succeed"

    $connection = New-Object System.Data.SQLite.SQLiteConnection($tempConnection)
    $connection.Open()
    foreach ($table in @("SendCollection", "ByteSweepPreset")) {
        $command = $connection.CreateCommand()
        $command.CommandText = "PRAGMA table_info($table);"
        $reader = $command.ExecuteReader()
        $columns = @()
        while ($reader.Read()) { $columns += $reader["name"].ToString() }
        $reader.Dispose()
        $command.Dispose()
        Assert-Equal $true ($columns -contains "Annotations") "$table migration must add the Annotations column"
    }
    $connection.Dispose()

    $packet = New-Object WPELibrary.Lib.Socket_PacketInfo
    $packet.PacketSocket = 1
    $packet.PacketType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
    $packet.PacketFrom = "127.0.0.1:1"
    $packet.PacketTo = "127.0.0.1:2"
    $packet.PacketBuffer = [byte[]](1, 2, 3)
    $packet.ByteAnnotations.Add($normalized[0])
    $collection = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]'
    $collection.Add($packet)
    $sendId = [Guid]::NewGuid()
    $sendInfo = New-Object WPELibrary.Lib.Socket_SendInfo($true, $sendId, "test", $false, 1, 0, $collection, "")
    [WPELibrary.Lib.Socket_Cache+DataBase]::InsertTable_Send($sendInfo)
    $savedSend = [WPELibrary.Lib.Socket_Cache+DataBase]::SelectTable_SendCollection($sendId)
    $sendRoundTrip = [WPELibrary.Lib.Socket_ByteAnnotationEngine]::Deserialize($savedSend.Rows[0]["Annotations"].ToString(), 3)
    Assert-Equal 1 $sendRoundTrip.Count "send collection annotations must survive SQLite persistence"

    $preset = New-Object WPELibrary.Lib.Socket_ByteSweepPresetInfo
    $preset.BID = [Guid]::NewGuid()
    $preset.BName = "test"
    $preset.BFolder = "folder"
    $preset.BStart = 0
    $preset.BLength = 1
    $preset.BLoopCount = 1
    $preset.PacketType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
    $preset.Buffer = [byte[]](1, 2, 3)
    $preset.ByteAnnotations.Add($normalized[0])
    $folders = New-Object 'System.Collections.Generic.List[string]'
    $folders.Add("folder")
    $presets = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_ByteSweepPresetInfo]'
    $presets.Add($preset)
    Assert-Equal $true ([WPELibrary.Lib.Socket_Cache+DataBase]::ReplaceByteSweepList($folders, $presets)) "byte-sweep persistence must succeed"
    $savedPresets = [WPELibrary.Lib.Socket_Cache+DataBase]::SelectTable_ByteSweepPreset()
    $presetRoundTrip = [WPELibrary.Lib.Socket_ByteAnnotationEngine]::Deserialize($savedPresets.Rows[0]["Annotations"].ToString(), 3)
    Assert-Equal 1 $presetRoundTrip.Count "byte-sweep annotations must survive SQLite persistence"
}
finally {
    $connectionField.SetValue($null, $originalConnection)
    if (Test-Path -LiteralPath $tempDatabase) {
        Remove-Item -LiteralPath $tempDatabase -Force
    }
}

$sendFormSource = Get-Content (Join-Path $repo "WPELibrary\Socket_SendForm.cs") -Raw -Encoding UTF8
$mainFormSource = Get-Content (Join-Path $repo "WPELibrary\Socket_Form.cs") -Raw -Encoding UTF8
$controllerSource = Get-Content (Join-Path $repo "WPELibrary\Socket_ByteAnnotationController.cs") -Raw -Encoding UTF8
$hexBoxSource = Get-Content (Join-Path $repo "ThirdParty\Be.Windows.Forms.HexBox\HexBox.cs") -Raw -Encoding UTF8
$wpePackages = Get-Content (Join-Path $repo "WPELibrary\packages.config") -Raw -Encoding UTF8
$appPackages = Get-Content (Join-Path $repo "WinsockPacketEditor\packages.config") -Raw -Encoding UTF8

Assert-Equal $true ($sendFormSource.Contains("this.workingByteAnnotations = Socket_ByteAnnotationEngine.Clone(this.SPI.ByteAnnotations);")) "send form must edit an annotation working copy"
Assert-Equal $true ($sendFormSource.Contains("this.SPI.ByteAnnotations = Socket_ByteAnnotationEngine.Clone(this.workingByteAnnotations);")) "send form Save must commit the annotation working copy"
Assert-Equal $true ($mainFormSource.Contains("this.byteAnnotationController.Changed += this.ByteAnnotationController_Changed;")) "annotation-only byte-sweep edits must have a commit callback"
Assert-Equal $true ($mainFormSource.Contains("this.CommitPacketDataEdits();`r`n            this.StopByteSweep();") -or $mainFormSource.Contains("this.CommitPacketDataEdits();`n            this.StopByteSweep();")) "form close must commit the active byte-sweep editor before persistence"
Assert-Equal $true ($controllerSource.Contains("layout.ColumnStyles[column].Width = collapsed ? 28F : 220F;")) "annotation panel must support collapse and expand"
Assert-Equal $true ($controllerSource.Contains("internal sealed class Socket_ByteAnnotationController : IDisposable")) "annotation controller must release owned resources"
Assert-Equal $true ($hexBoxSource.Contains("ByteStyleBrushCache styleBrushes")) "HexBox annotation brushes must be cached per paint pass"
Assert-Equal $false ($wpePackages.Contains("Be.Windows.Forms.HexBox")) "WPELibrary must not retain the obsolete HexBox package reference"
Assert-Equal $false ($appPackages.Contains("Be.Windows.Forms.HexBox")) "application must not retain the obsolete HexBox package reference"
[xml](Get-Content (Join-Path $repo "WPELibrary\Properties\Resources.resx") -Raw -Encoding UTF8) | Out-Null
[xml](Get-Content (Join-Path $repo "WPELibrary\Properties\Resources.en-US.resx") -Raw -Encoding UTF8) | Out-Null

Write-Host "Byte annotation regression checks passed."
