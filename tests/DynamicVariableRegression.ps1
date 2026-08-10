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
$hexBoxDll = if ($null -eq $resolvedBuildDirectory) {
    Join-Path $repo "WPELibrary\bin\$Configuration\Be.Windows.Forms.HexBox.dll"
}
else {
    Join-Path $resolvedBuildDirectory "Be.Windows.Forms.HexBox.dll"
}
$sqliteDll = if ($null -eq $resolvedBuildDirectory) {
    Join-Path $repo "WPELibrary\bin\$Configuration\System.Data.SQLite.dll"
}
else {
    Join-Path $resolvedBuildDirectory "System.Data.SQLite.dll"
}

foreach ($path in @($hexBoxDll, $libraryDll, $sqliteDll)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing build artifact: $path"
    }
}

Add-Type -Path $hexBoxDll
Add-Type -Path $libraryDll
Add-Type -Path $sqliteDll

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

function Get-Hex([byte[]]$value) {
    if ($null -eq $value) {
        return ""
    }
    return ([BitConverter]::ToString($value)).Replace("-", " ")
}

function New-Definition([string]$symbol, [int]$length) {
    $definition = New-Object WPELibrary.Lib.DynamicVariableDefinition
    $definition.VariableId = [Guid]::NewGuid()
    $definition.Symbol = $symbol
    $definition.DisplayName = $symbol
    $definition.Length = $length
    $definition.IsAutoUpdateEnabled = $true
    return $definition
}

function New-Field([Guid]$variableId, [int]$offset, [int]$length) {
    $field = New-Object WPELibrary.Lib.DynamicField
    $field.FieldId = [Guid]::NewGuid()
    $field.VariableId = $variableId
    $field.Offset = $offset
    $field.Length = $length
    return $field
}

$rangeOffset = 0
$rangeLength = 0
Assert-True ([WPELibrary.Lib.DynamicVariableRange]::TryFromSelection(3, 0, 8, [ref]$rangeOffset, [ref]$rangeLength)) "caret selection must normalize to one byte"
Assert-Equal 3 $rangeOffset "caret selection offset"
Assert-Equal 1 $rangeLength "caret selection length"
Assert-True ([WPELibrary.Lib.DynamicVariableRange]::TryFromEndpoints(5, 2, 8, [ref]$rangeOffset, [ref]$rangeLength)) "reverse selection must normalize"
Assert-Equal 2 $rangeOffset "reverse selection offset"
Assert-Equal 4 $rangeLength "reverse selection length"
Assert-Equal "PET_ID" ([WPELibrary.Lib.DynamicVariableNames]::NormalizeSymbol(" pet_id ") ) "symbol must normalize to uppercase"
Assert-Equal $null ([WPELibrary.Lib.DynamicVariableNames]::NormalizeSymbol("bad-name")) "invalid symbol must be rejected"

$variableA = New-Definition "PET_ID" 2
$variableB = New-Definition "ROLE_ID" 1
$definitions = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.DynamicVariableDefinition]'
$definitions.Add($variableA)
$definitions.Add($variableB)

$rule = New-Object WPELibrary.Lib.ExtractionRule
$rule.RuleId = [Guid]::NewGuid()
$rule.Name = "packet-rule"
$rule.IsEnabled = $true
$rule.PacketType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
$rule.PatternBytes = [byte[]](0xAA, 0x12, 0x34, 0xCC, 0x55)
$rule.WildcardMask = [byte[]](0, 1, 1, 1, 0)
$rule.Fields.Add((New-Field $variableA.VariableId 1 2))
$rule.Fields.Add((New-Field $variableB.VariableId 3 1))
$definitionMap = New-Object 'System.Collections.Generic.Dictionary[Guid, WPELibrary.Lib.DynamicVariableDefinition]'
$definitionMap.Add($variableA.VariableId, $variableA)
$definitionMap.Add($variableB.VariableId, $variableB)
$validationError = ""
Assert-True ([WPELibrary.Lib.PatternMatcher]::ValidateRule($rule, $definitionMap, [ref]$validationError)) "valid extraction rule must pass validation"

$allWildcard = $rule.Clone()
$allWildcard.WildcardMask = [byte[]](1, 1, 1, 1, 1)
Assert-Equal $false ([WPELibrary.Lib.PatternMatcher]::ValidateRule($allWildcard, $definitionMap, [ref]$validationError)) "all-wildcard rule must be rejected"
$overlap = $rule.Clone()
$overlap.Fields.Add((New-Field $variableA.VariableId 2 1))
Assert-Equal $false ([WPELibrary.Lib.PatternMatcher]::ValidateRule($overlap, $definitionMap, [ref]$validationError)) "overlapping fields must be rejected"
$outOfBounds = $rule.Clone()
$outOfBounds.Fields.Clear()
$outOfBounds.Fields.Add((New-Field $variableA.VariableId 4 2))
Assert-Equal $false ([WPELibrary.Lib.PatternMatcher]::ValidateRule($outOfBounds, $definitionMap, [ref]$validationError)) "out-of-bounds fields must be rejected"

[WPELibrary.Lib.DynamicVariableRuntime]::Variables.Load($definitions, (New-Object 'System.Collections.Generic.List[WPELibrary.Lib.ExtractionRule]'))
$ruleError = ""
Assert-True ([WPELibrary.Lib.DynamicVariableRuntime]::Variables.AddOrUpdateRule($rule, [ref]$ruleError)) "valid rule must be stored"
[WPELibrary.Lib.DynamicVariableRuntime]::KnownValues.Load((New-Object 'System.Collections.Generic.List[WPELibrary.Lib.KnownVariableValue]'))
[WPELibrary.Lib.DynamicVariableRuntime]::ProcessPacket(
    [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send,
    [byte[]](0xAA, 0x99, 0x88, 0x07, 0x55))
$current = [WPELibrary.Lib.DynamicVariableRuntime]::Variables.GetCurrentSnapshot()
$currentA = $current | Where-Object { $_.VariableId -eq $variableA.VariableId }
$currentB = $current | Where-Object { $_.VariableId -eq $variableB.VariableId }
Assert-Equal "99 88" (Get-Hex $currentA.Value) "first dynamic field must update"
Assert-Equal "07" (Get-Hex $currentB.Value) "same-packet second dynamic field must update atomically"

$wrongLength = [byte[]](0xAA, 0x99, 0x88, 0x07)
[WPELibrary.Lib.DynamicVariableRuntime]::ProcessPacket(
    [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send,
    $wrongLength)
Assert-Equal "99 88" (Get-Hex (([WPELibrary.Lib.DynamicVariableRuntime]::Variables.GetCurrentSnapshot() | Where-Object { $_.VariableId -eq $variableA.VariableId }).Value)) "length mismatch must not update"
[WPELibrary.Lib.DynamicVariableRuntime]::ProcessPacket(
    [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Recv,
    [byte[]](0xAA, 0x01, 0x02, 0x03, 0x55))
Assert-Equal "99 88" (Get-Hex (([WPELibrary.Lib.DynamicVariableRuntime]::Variables.GetCurrentSnapshot() | Where-Object { $_.VariableId -eq $variableA.VariableId }).Value)) "packet type mismatch must not update"

$snapshot = [WPELibrary.Lib.DynamicVariableRuntime]::CaptureSnapshot()
[WPELibrary.Lib.DynamicVariableRuntime]::ProcessPacket(
    [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send,
    [byte[]](0xAA, 0x01, 0x02, 0x08, 0x55))
$snapValue = $null
Assert-True ($snapshot.TryGetValue($variableA.VariableId, [ref]$snapValue)) "snapshot must contain current variable"
Assert-Equal ([byte[]](0x99, 0x88)).Length $snapValue.Length "snapshot value length"
Assert-Equal 0x99 $snapValue[0] "send task snapshot must remain immutable after current value changes"

$bindingA = New-Object WPELibrary.Lib.PresetVariableBinding
$bindingA.VariableId = $variableA.VariableId
$bindingA.Offset = 1
$bindingA.Length = 2
$bindingB = New-Object WPELibrary.Lib.PresetVariableBinding
$bindingB.VariableId = $variableB.VariableId
$bindingB.Offset = 3
$bindingB.Length = 1
$packet = New-Object WPELibrary.Lib.Socket_PacketInfo
$packet.PacketBuffer = [byte[]](0xAA, 0, 0, 0, 0x55)
$packet.VariableBindings.Add($bindingA)
$packet.VariableBindings.Add($bindingB)
$resolved = $null
$resolveError = ""
$resolver = [Func[Guid, WPELibrary.Lib.DynamicVariableDefinition]]{
    param($id)
    $found = [WPELibrary.Lib.DynamicVariableRuntime]::Variables.GetDefinitionsSnapshot() | Where-Object { $_.VariableId -eq $id }
    return $found
}
Assert-True ([WPELibrary.Lib.VariableResolver]::TryResolveBuffer($packet.PacketBuffer, $packet.VariableBindings, $snapshot, $resolver, [ref]$resolved, [ref]$resolveError)) "complete variable snapshot must resolve all bindings"
Assert-Equal 0x99 $resolved[1] "resolved first variable byte"
Assert-Equal 0x07 $resolved[3] "resolved second variable byte"

[WPELibrary.Lib.DynamicVariableRuntime]::Variables.ClearCurrent($variableA.VariableId)
$missingSnapshot = [WPELibrary.Lib.DynamicVariableRuntime]::CaptureSnapshot()
$missingResolved = $null
$missingError = ""
Assert-Equal $false ([WPELibrary.Lib.VariableResolver]::TryResolveBuffer($packet.PacketBuffer, $packet.VariableBindings, $missingSnapshot, $resolver, [ref]$missingResolved, [ref]$missingError)) "missing variable must reject the whole send task"
$failedVariableId = [Guid]::Empty
$missingResolvedExact = $null
$missingErrorExact = ""
Assert-Equal $false ([WPELibrary.Lib.VariableResolver]::TryResolveBuffer(
    $packet.PacketBuffer,
    $packet.VariableBindings,
    $missingSnapshot,
    $resolver,
    [ref]$missingResolvedExact,
    [ref]$missingErrorExact,
    [ref]$failedVariableId)) "resolver overload must reject the missing variable"
Assert-Equal $variableA.VariableId $failedVariableId "resolver must report the actual failed variable"

$known = [WPELibrary.Lib.DynamicVariableRuntime]::KnownValues.GetSnapshot() | Where-Object { $_.VariableId -eq $variableA.VariableId }
Assert-True ($known.Count -ge 2) "different observed values must be retained as history"
$knownValue = $known | Where-Object { $_.ValueHex -eq "01 02" } | Select-Object -First 1
Assert-True ($null -ne $knownValue) "history must contain the later value"
Assert-True ([WPELibrary.Lib.DynamicVariableRuntime]::KnownValues.SetLabel($variableA.VariableId, $knownValue.Value, "角色一")) "history label must be editable"

$xml = [WPELibrary.Lib.DynamicVariableRuntime]::ExportToXElement()
Assert-True ($xml.Name.LocalName -eq "DynamicVariables") "dynamic backup XML root"
$importError = ""
Assert-True ([WPELibrary.Lib.DynamicVariableRuntime]::ImportFromXElement($xml, [ref]$importError)) "dynamic backup XML round-trip"
Assert-Equal 1 ([WPELibrary.Lib.DynamicVariableRuntime]::Variables.GetRulesSnapshot().Count) "rule must survive XML round-trip"
$importedKnown = [WPELibrary.Lib.DynamicVariableRuntime]::KnownValues.GetSnapshot() |
    Where-Object { $_.VariableId -eq $variableA.VariableId -and $_.ValueHex -eq "01 02" } |
    Select-Object -First 1
Assert-Equal "角色一" $importedKnown.Label "history label must survive XML round-trip"
$invalidBackup = [System.Xml.Linq.XElement]::Parse($xml.ToString())
$invalidBackup.Element("Definitions").Element("Definition").SetAttributeValue("Symbol", "BAD-NAME")
$invalidImportError = ""
Assert-Equal $false ([WPELibrary.Lib.DynamicVariableRuntime]::ImportFromXElement($invalidBackup, [ref]$invalidImportError)) "invalid backup symbol must be rejected"
Assert-Equal 1 ([WPELibrary.Lib.DynamicVariableRuntime]::Variables.GetRulesSnapshot().Count) "rejected backup must not replace current rules"

$legacyPath = Join-Path ([System.IO.Path]::GetTempPath()) ("wpe-dynamic-legacy-" + [Guid]::NewGuid().ToString("N") + ".sc")
$roundTripPath = Join-Path ([System.IO.Path]::GetTempPath()) ("wpe-dynamic-roundtrip-" + [Guid]::NewGuid().ToString("N") + ".sc")
try {
    [System.IO.File]::WriteAllText(
        $legacyPath,
        "<SendCollection><Collection><Socket>1</Socket><Type>WS2_Send</Type><IPFrom></IPFrom><IPTo></IPTo><Buffer>AA BB CC</Buffer></Collection></SendCollection>",
        [System.Text.Encoding]::UTF8)
    $legacyCollection = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]'
    [WPELibrary.Lib.Socket_Cache+Send]::LoadSendCollection($legacyPath, $legacyCollection, $false)
    Assert-Equal 1 $legacyCollection.Count "legacy preset must load"
    Assert-Equal 0 $legacyCollection[0].VariableBindings.Count "legacy preset must have no variable bindings"

    $newCollection = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]'
    $newPacket = New-Object WPELibrary.Lib.Socket_PacketInfo
    $newPacket.PacketSocket = 1
    $newPacket.PacketType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
    $newPacket.PacketBuffer = [byte[]](0xAA, 0x00, 0x00, 0x55)
    $newPacket.SortOrder = 9
    $newPacket.VariableBindings.Add((New-Object WPELibrary.Lib.PresetVariableBinding -Property @{
        VariableId = $variableA.VariableId
        Offset = 1
        Length = 2
    }))
    $newCollection.Add($newPacket)
    [WPELibrary.Lib.Socket_Cache+Send]::SaveSendCollection($roundTripPath, $newCollection, $false)
    $loadedNewCollection = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]'
    [WPELibrary.Lib.Socket_Cache+Send]::LoadSendCollection($roundTripPath, $loadedNewCollection, $false)
    Assert-Equal 1 $loadedNewCollection.Count "new preset must load"
    Assert-Equal 1 $loadedNewCollection[0].VariableBindings.Count "new preset bindings must survive XML round-trip"
    Assert-Equal 9 $loadedNewCollection[0].SortOrder "preset packet sort order must survive XML round-trip"
}
finally {
    foreach ($path in @($legacyPath, $roundTripPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

$bindingList = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.PresetVariableBinding]'
$bindingList.Add($bindingA)
$providerAnnotations = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.Socket_ByteAnnotationInfo]'
$provider = New-Object WPELibrary.Lib.Socket_AnnotatedByteProvider -ArgumentList ([byte[]](0, 1, 2, 3, 4)), $providerAnnotations, $bindingList
try {
    $provider.InsertBytes(2, [byte[]](0xFF))
    throw "insert inside a variable binding unexpectedly succeeded"
}
catch [System.InvalidOperationException] {
}
Assert-Equal 5 $provider.Length "rejected structural edit must preserve bytes"
$provider.InsertBytes(0, [byte[]](0xEE))
Assert-Equal 2 $bindingList[0].Offset "insert before a binding must shift binding offset"

$historyQueue = New-Object WPELibrary.Lib.KnownValueStore
$queuedObservations = New-Object 'System.Collections.Generic.List[WPELibrary.Lib.KnownVariableValue]'
for ($index = 0; $index -lt 5000; $index++) {
    $observation = New-Object WPELibrary.Lib.KnownVariableValue
    $observation.VariableId = $variableA.VariableId
    $observation.Value = [byte[]](0x01, 0x02)
    $observation.FirstSeenUtc = [DateTime]::UtcNow
    $observation.LastSeenUtc = [DateTime]::UtcNow
    $observation.SeenCount = 1
    $queuedObservations.Add($observation)
}
$historyQueue.Enqueue($queuedObservations)
Assert-True ($historyQueue.PendingCount -le 4096) "history queue must remain bounded"

$hookSource = [System.IO.File]::ReadAllText((Join-Path $repo "WPELibrary\Lib\WinSockHook.cs"), [System.Text.Encoding]::UTF8)
$processIndex = $hookSource.IndexOf("DynamicVariableRuntime.ProcessPacket", [System.StringComparison]::Ordinal)
$commitIndex = $hookSource.IndexOf("CommitPacketDeferredExecution", [System.StringComparison]::Ordinal)
Assert-True ($processIndex -ge 0 -and $commitIndex -gt $processIndex) "same-packet variable update must precede deferred filter actions"
$cacheSource = [System.IO.File]::ReadAllText((Join-Path $repo "WPELibrary\Lib\Socket_Cache.cs"), [System.Text.Encoding]::UTF8)
Assert-True ($cacheSource.Contains("DynamicVariableRuntime.CaptureSnapshot()") -and $cacheSource.Contains("VariableResolver.TryResolveBuffer")) "preset send path must resolve one variable snapshot"

$databaseType = [WPELibrary.Lib.Socket_Cache+DataBase]
$bindingFlags = [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::NonPublic
$connectionField = $databaseType.GetField("conStr", $bindingFlags)
$connectionProperty = $databaseType.GetProperty("conStr", $bindingFlags)
$originalConnection = if ($null -ne $connectionField) { $connectionField.GetValue($null) } else { $connectionProperty.GetValue($null, $null) }
$tempDatabase = Join-Path ([System.IO.Path]::GetTempPath()) ("wpe-dynamic-variable-" + [Guid]::NewGuid().ToString("N") + ".db")
$tempConnection = "Data Source=$tempDatabase;Version=3;"
if ($null -ne $connectionField) { $connectionField.SetValue($null, $tempConnection) } else { $connectionProperty.SetValue($null, $tempConnection, $null) }
try {
    Assert-True ([WPELibrary.Lib.Socket_Cache+DataBase]::CreateTable_DynamicVariables()) "dynamic SQLite schema creation"
    $saveDefinitions = [WPELibrary.Lib.DynamicVariableRuntime]::Variables.GetDefinitionsSnapshot()
    $saveRules = [WPELibrary.Lib.DynamicVariableRuntime]::Variables.GetRulesSnapshot()
    $saveValues = [WPELibrary.Lib.DynamicVariableRuntime]::KnownValues.GetSnapshot()
    Assert-True ([WPELibrary.Lib.Socket_Cache+DataBase]::SaveDynamicVariableData($saveDefinitions, $saveRules, $saveValues)) "dynamic SQLite save"
    $loadedDefinitions = $null
    $loadedRules = $null
    $loadedValues = $null
    Assert-True ([WPELibrary.Lib.Socket_Cache+DataBase]::LoadDynamicVariableData([ref]$loadedDefinitions, [ref]$loadedRules, [ref]$loadedValues)) "dynamic SQLite load"
    Assert-Equal $saveDefinitions.Count $loadedDefinitions.Count "definition count after SQLite round-trip"
    Assert-Equal $saveRules.Count $loadedRules.Count "rule count after SQLite round-trip"
    Assert-True ($loadedValues.Count -gt 0) "history after SQLite round-trip"
    $savedHistory = $saveValues | Where-Object { $_.VariableId -eq $variableA.VariableId -and $_.ValueHex -eq "01 02" } | Select-Object -First 1
    $loadedHistory = $loadedValues | Where-Object { $_.VariableId -eq $variableA.VariableId -and $_.ValueHex -eq "01 02" } | Select-Object -First 1
    Assert-Equal $savedHistory.SeenCount $loadedHistory.SeenCount "history count must not double during SQLite round-trip"
    [WPELibrary.Lib.DynamicVariableRuntime]::Variables.Load($loadedDefinitions, $loadedRules)
    Assert-True ($null -eq (([WPELibrary.Lib.DynamicVariableRuntime]::Variables.GetCurrentSnapshot() | Where-Object { $_.VariableId -eq $variableA.VariableId }).Value)) "current values must reset after restart"

    $connection = New-Object System.Data.SQLite.SQLiteConnection($tempConnection)
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = @"
CREATE TABLE Send (GUID TEXT NOT NULL PRIMARY KEY, IsEnable BOOLEAN DEFAULT 0, Name TEXT NOT NULL, SystemSocket BOOLEAN DEFAULT 0, LoopCNT INTEGER NOT NULL DEFAULT 1, LoopINT INTEGER NOT NULL DEFAULT 1000, Notes TEXT);
CREATE TABLE SendCollection (GUID TEXT NOT NULL, Socket INTEGER NOT NULL, Type INTEGER NOT NULL, IPFrom TEXT NOT NULL, IPTo TEXT NOT NULL, Buffer BLOB);
"@
    [void]$command.ExecuteNonQuery()
    $command.Dispose()
    $connection.Dispose()
    $createSend = $databaseType.GetMethod("CreateTable_Send", $bindingFlags)
    Assert-Equal $true $createSend.Invoke($null, @()) "legacy send schema migration"
    $connection = New-Object System.Data.SQLite.SQLiteConnection($tempConnection)
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = "PRAGMA table_info(SendCollection);"
    $reader = $command.ExecuteReader()
    $sendCollectionColumns = @()
    while ($reader.Read()) { $sendCollectionColumns += $reader["name"].ToString() }
    $reader.Dispose()
    $command.Dispose()
    $connection.Dispose()
    foreach ($column in @("Annotations", "VariableBindings", "SortOrder")) {
        Assert-True ($sendCollectionColumns -contains $column) "legacy send schema must add $column"
    }
    $sendPacket = New-Object WPELibrary.Lib.Socket_PacketInfo
    $sendPacket.PacketSocket = 1
    $sendPacket.PacketType = [WPELibrary.Lib.Socket_Cache+SocketPacket+PacketType]::WS2_Send
    $sendPacket.PacketFrom = "127.0.0.1:1"
    $sendPacket.PacketTo = "127.0.0.1:2"
    $sendPacket.PacketBuffer = [byte[]](0xAA, 0x00, 0x00, 0x55)
    $sendPacket.SortOrder = 7
    $sendPacket.VariableBindings.Add($bindingA)
    $sendCollection = New-Object 'System.ComponentModel.BindingList[WPELibrary.Lib.Socket_PacketInfo]'
    $sendCollection.Add($sendPacket)
    $sendInfo = New-Object WPELibrary.Lib.Socket_SendInfo -ArgumentList $true, ([Guid]::NewGuid()), "dynamic", $true, 1, 25, $sendCollection, ""
    [WPELibrary.Lib.Socket_Cache+DataBase]::InsertTable_Send($sendInfo)
    $savedSendCollection = [WPELibrary.Lib.Socket_Cache+DataBase]::SelectTable_SendCollection($sendInfo.SID)
    Assert-Equal 7 ([int]$savedSendCollection.Rows[0]["SortOrder"]) "packet sort order must survive SQLite persistence"
    Assert-True ($savedSendCollection.Rows[0]["VariableBindings"].ToString().Contains($variableA.VariableId.ToString("N"))) "variable bindings must survive SQLite persistence"
}
finally {
    if ($null -ne $connectionField) { $connectionField.SetValue($null, $originalConnection) } else { $connectionProperty.SetValue($null, $originalConnection, $null) }
    if (Test-Path -LiteralPath $tempDatabase) { Remove-Item -LiteralPath $tempDatabase -Force }
}

"DynamicVariableRegression: PASS"
