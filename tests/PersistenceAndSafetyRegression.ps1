param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Read-SourceFile {
    param([string]$RelativePath)

    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $RelativePath"
    }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Expected,
        [string]$Message
    )

    if (-not $Text.Contains($Expected)) {
        throw $Message
    }
}

function Assert-NotContains {
    param(
        [string]$Text,
        [string]$Unexpected,
        [string]$Message
    )

    if ($Text.Contains($Unexpected)) {
        throw $Message
    }
}

$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$tcpHost = Read-SourceFile "WPELibrary\Lib\WebAPI\Socket_TcpOwinHost.cs"
$runtime = Read-SourceFile "WPELibrary\Lib\DynamicVariableEngine.cs"
$dynamicUi = Read-SourceFile "WPELibrary\DynamicVariableUi.cs"

Assert-Contains $cache "public static bool ExecuteAtomicSave" `
    "database list saves must keep an isolated atomic-save boundary"
Assert-Contains $cache "SystemListLoadCompleted" `
    "shutdown must know whether startup list loading completed"
Assert-Contains $cache "SystemListLoadFailed" `
    "a failed list load must block destructive shutdown persistence"
Assert-Contains $cache "dtFilter.Columns.Count == 0" `
    "filter load failures must not be mistaken for an empty list"
Assert-Contains $cache "dtSend.Columns.Count == 0" `
    "send load failures must not be mistaken for an empty list"
Assert-Contains $cache "presets.Columns.Count == 0" `
    "progression load failures must not be mistaken for an empty list"
Assert-Contains $cache "loadedAccounts" `
    "proxy accounts must be parsed before replacing the live list"
Assert-Contains $cache "loadedMaps" `
    "proxy mappings must be parsed before replacing the live list"
Assert-Contains $cache "ValidateConfigElements" `
    "configuration imports must validate all scalar fields before mutation"
Assert-Contains $cache "if (SetSystemConfig_FromXML(xeSystemConfig))" `
    "invalid system configuration must not be persisted"
Assert-Contains $cache "if (SetProxyConfig_FromXML(xeProxyConfig))" `
    "invalid proxy configuration must not be persisted"
Assert-Contains $cache "if (SetInjectionConfig_FromXML(xeInjectionConfig))" `
    "invalid injection configuration must not be persisted"
Assert-Contains $cache "TryApplyListChangeAndSave" `
    "preset mutations must use rollback-aware persistence"
Assert-Contains $cache "TryApplyMapLocalChangeAndSave" `
    "local mapping mutations must use rollback-aware persistence"
Assert-Contains $cache "TryApplyMapRemoteChangeAndSave" `
    "remote mapping mutations must use rollback-aware persistence"

Assert-Contains $tcpHost "MaxResponseBodyBytes = 16 * 1024 * 1024" `
    "fallback HTTPS responses must have a hard body limit"
Assert-Contains $tcpHost "LimitedMemoryStream" `
    "fallback HTTPS responses must use bounded buffering"
Assert-Contains $tcpHost "activeClients" `
    "fallback HTTPS shutdown must track active clients"
Assert-Contains $tcpHost "ResponseBodyTooLargeException" `
    "oversized fallback responses must be rejected"
Assert-Contains $tcpHost "this.certificate.Dispose();" `
    "fallback HTTPS certificate lifetime must remain explicit"

Assert-Contains $runtime "public static bool SaveToDatabase()" `
    "dynamic variable persistence must report database failure"
Assert-Contains $runtime "return saved;" `
    "dynamic variable UI callers must be able to detect save failure"
Assert-Contains $dynamicUi "SaveToDatabase())" `
    "dynamic variable UI must surface persistence failures"
Assert-Contains $dynamicUi "bindings.RemoveAt" `
    "binding changes must not be reported successful when persistence fails"

Write-Output "PersistenceAndSafetyRegression: PASS"
