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

$hook = Read-SourceFile "WPELibrary\Lib\WinSockHook.cs"
$operation = Read-SourceFile "WPELibrary\Lib\Socket_Operation.cs"
$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$robot = Read-SourceFile "WPELibrary\Lib\Socket_Robot.cs"
$systemMode = Read-SourceFile "WinsockPacketEditor\SystemMode_Form.cs"
$proxyController = Read-SourceFile "WPELibrary\Lib\WebAPI\ProxyAccount_Controller.cs"
$filterForm = Read-SourceFile "WPELibrary\Socket_FilterForm.cs"
$proxyForm = Read-SourceFile "WinsockPacketEditor\SocketProxy_Form.cs"

if (([regex]::Matches($hook, "lpOverlapped != IntPtr.Zero \|\| lpCompletionRoutine != IntPtr.Zero")).Count -lt 4) {
    throw "All four WSA hook variants must bypass unsafe completion-driven I/O."
}
Assert-Contains $hook "pWSABuffers[i].len = copyLength;" `
    "Multi-buffer replacements must zero unused WSABUF tails."
Assert-Contains $hook "finally" `
    "WSABUF lengths must be restored even when the native call fails."
if (([regex]::Matches($hook, "ReturnReceiveInterceptError\(\)")).Count -lt 2) {
    throw "Synchronous recv interception must not report EOF."
}
if (([regex]::Matches($hook, "SetReceiveInterceptError\(lpNumberOfBytesRecvd\)")).Count -lt 4) {
    throw "WSA receive interception must return a Winsock error instead of zero bytes."
}
Assert-Contains $hook "WsaErrorInterrupted = 10004;" `
    "Receive interception must use WSAEINTR semantics."
Assert-Contains (Read-SourceFile "WPELibrary\Lib\NativeMethods\WS2_32.cs") "WSASetLastError" `
    "Winsock receive interception must set the native last-error value."

$normalFilter = [regex]::Match(
    $cache,
    '(?s)public static bool CheckFilter_IsMatch_Normal\(.*?\n            \}')
if (-not $normalFilter.Success) {
    throw "Normal filter matcher was not found."
}
Assert-Contains $normalFilter.Value "bool hasCondition = false;" `
    "Normal filters must require at least one valid condition."
Assert-Contains $normalFilter.Value "return hasCondition;" `
    "Normal filter parsing must not treat malformed-only input as a match."
Assert-NotContains $normalFilter.Value "return true;" `
    "Normal filters must not default malformed input to a match."

Assert-Contains $operation "private static int SendNativeTcp" `
    "Native TCP replay must use a complete-send helper."
Assert-Contains $operation "while (totalSent < length)" `
    "Native TCP replay must handle partial sends."
Assert-Contains $operation "while (iReturn < data.Length)" `
    "Proxy TCP writes must handle partial sends."

$sleepMethod = [regex]::Match(
    $operation,
    '(?s)public static async Task DoSleepAsync\(.*?\n        \}')
if (-not $sleepMethod.Success) {
    throw "Cancellable delay method was not found."
}
Assert-NotContains $sleepMethod.Value "catch (TaskCanceledException)" `
    "Cancellable delays must propagate cancellation."
Assert-Contains $robot "GetAwaiter()" `
    "Robot delays must observe cancellation without blocking on an aggregate exception."

Assert-Contains $cache "public static bool ExecuteAtomicSave" `
    "Configuration saves must use an isolated atomic database replacement."
Assert-Contains $cache "File.Replace(temporaryFile, databaseFile, null);" `
    "Successful configuration saves must replace the live database only after completion."
Assert-Contains $cache "if (atomicSaveInProgress)" `
    "Database row failures must propagate to the atomic save boundary."
Assert-Contains $cache "Socket_Cache.ProxyAccount.LoadProxyAccountList_FromDB();" `
    "Startup proxy-account loading must not synchronously wait on a UI-marshalled task."
Assert-NotContains $cache "LoadProxyAccountList_FromDB().GetAwaiter().GetResult()" `
    "Startup proxy-account loading must not deadlock the UI dispatcher."

Assert-Contains $cache "private const int MaxLogQueueCount = 5000;" `
    "Log queues must have a hard upper bound."
Assert-Contains $cache "DroppedSocketLog_CNT" `
    "Dropped log entries must remain observable."

Assert-Contains $operation "Uri.UriSchemeHttps" `
    "Remote management must reject clear-text HTTP."
Assert-Contains $cache 'Socket_Operation.PassWord_Encrypt(Socket_Cache.System.Remote_PassWord)' `
    "Remote management database persistence must use DPAPI protection."
Assert-Contains $cache "Socket_Operation.PassWord_EncryptForPortableExport(Socket_Cache.System.Remote_PassWord)" `
    "System backup export must use a portable password representation."
Assert-Contains $cache "Socket_Operation.PassWord_EncryptForPortableExport(pai.PassWord)" `
    "Proxy-account backup export must not carry user-bound DPAPI data."
Assert-Contains $operation "PassWord_EncryptForPortableExport" `
    "Portable password export conversion must be centralized."
Assert-Contains $systemMode 'string.Format("https://{0}:{1}"' `
    "Remote management URLs must be generated as HTTPS URLs."
Assert-Contains $proxyController "if (pai == null)" `
    "Proxy-account API endpoints must reject empty request bodies without a server error."

$recordLoginMethod = [regex]::Match(
    $cache,
    '(?s)public static async Task RecordLoginIP_ByAccountID\(.*?\n            \}')
if (-not $recordLoginMethod.Success) {
    throw "Proxy login IP recording must remain awaitable."
}
Assert-Contains $cache "_ = Socket_Cache.ProxyAccount.RecordLoginIP_ByAccountID(AccountID, ClientIP);" `
    "Network callbacks must explicitly fire and forget proxy login IP recording."
Assert-Contains $recordLoginMethod.Value "Socket_Cache.System.InvokeAction" `
    "Proxy login account mutations must be marshalled through the UI owner."

$onlineMethod = [regex]::Match(
    $cache,
    '(?s)public static Task UpdateOnlineStatus\(\).*?\n            \}')
if (-not $onlineMethod.Success) {
    throw "Proxy online-state refresh must remain awaitable."
}
Assert-NotContains $onlineMethod.Value "Task.Run" `
    "Proxy online-state refresh must not mutate UI-bound accounts from a worker thread."

$pasteMethod = [regex]::Match(
    $filterForm,
    '(?s)private void PastePacketData\(.*?\n        \}')
if (-not $pasteMethod.Success) {
    throw "Filter paste handling must remain a UI-thread operation."
}
Assert-NotContains $pasteMethod.Value "Task.Run" `
    "Filter paste handling must not access DataGridView controls from a worker thread."

foreach ($methodName in @("Event_RecProxyData", "Event_RecProxyInfo", "UpdateClientLinks", "UpdateAccountLinksAndDevices")) {
    $method = [regex]::Match(
        $proxyForm,
        "(?s)(private (?:void|Task) $methodName\(.*?\n        \})")
    if (-not $method.Success) {
        throw "Proxy UI method was not found: $methodName"
    }

    Assert-NotContains $method.Value "Task.Run" `
        "$methodName must not touch proxy TreeView or BindingList state from a worker thread."
}

Write-Output "Full code fix regression checks passed."
