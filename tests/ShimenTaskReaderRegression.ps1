param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$CompanionRoot = 'F:\项目目录\飘渺游戏助手',
    [string]$PythonPath = 'C:\Users\Administrator\AppData\Local\Programs\Python\Python314\python.exe'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PythonPath)) {
    throw "Python interpreter not found: $PythonPath"
}

$testRoot = Join-Path $CompanionRoot 'tools\treasure-reader\tests'
$testPath = Join-Path $testRoot 'test_shimen_task_memory_reader.py'
if (-not (Test-Path -LiteralPath $testPath)) {
    throw "Missing shimen reader regression: $testPath"
}

& $PythonPath -m unittest discover -v -s $testRoot -p 'test_shimen_task_memory_reader.py'
if ($LASTEXITCODE -ne 0) {
    throw "Shimen task memory-reader Python regression failed with exit code $LASTEXITCODE."
}

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

$reader = Read-SourceFile 'WPELibrary\Lib\ShimenTaskAndroidSnapshotReader.cs'
Assert-Contains $reader 'AndroidRootShellSessionManager.Acquire(' `
    'Shimen reader must use the shared Android Root shell broker.'
Assert-Contains $reader '--root-broker-pipe' `
    'Shimen reader must pass the shared Root broker pipe to Python.'
Assert-Contains $reader 'StandardOutputEncoding = Encoding.UTF8' `
    'C# bridge must keep an explicit UTF-8 process-output decoder for escaped JSON.'
Assert-Contains $reader 'payload.Value<bool?>("ownerVerified") != true' `
    'C# bridge must reject any snapshot without explicit TaskMgr owner proof.'
Assert-Contains $reader 'TryParseGc64Address' `
    'C# bridge must parse and range-check the Python GC64 owner address.'
Assert-Contains $reader 'NumberStyles.AllowHexSpecifier' `
    'C# bridge must parse ownerAddress as the Python 0x hexadecimal form.'
Assert-Contains $reader '(1UL << 47)' `
    'C# bridge must reject ownerAddress values outside the GC64 range.'

$robot = Read-SourceFile 'WPELibrary\Lib\Socket_Robot.cs'
$preflight = [regex]::Match(
    $robot,
    '(?s)if \(this\.HasShimenTaskInstructionRows\(\).*?TryPrepareShimenTaskRuntime\(\).*?return false;.*?\n                }').Value
if ([string]::IsNullOrWhiteSpace($preflight)) {
    throw 'Shimen task preflight must fail startup before any worker/send path.'
}
$preflightIndex = $robot.IndexOf('TryPrepareShimenTaskRuntime()', [System.StringComparison]::Ordinal)
$workerIndex = $robot.IndexOf('this.Worker.RunWorkerAsync()', [System.StringComparison]::Ordinal)
if ($preflightIndex -lt 0 -or $workerIndex -lt 0 -or $preflightIndex -ge $workerIndex) {
    throw 'Shimen task preflight must run before the robot worker can send.'
}
Assert-Contains $robot 'if (!this.TryPrepareVisionRuntime())' `
    'Non-shimen robot presets must retain their existing vision preparation path.'
Assert-Contains $robot 'if (this.GetParameter("ShimenTaskLiveLoopEnabled", false))' `
    'Only the explicitly prepared shimen live-loop path may enter the shimen sender.'

$probePath = Join-Path $CompanionRoot 'tools\treasure-reader\shimen_task_memory_reader.py'
if (-not (Test-Path -LiteralPath $probePath)) {
    throw "Missing shimen reader source: $probePath"
}
$probe = [System.IO.File]::ReadAllText($probePath, [System.Text.Encoding]::UTF8)
Assert-Contains $probe 'ensure_ascii=True' `
    'Shimen JSONL must use ASCII escapes so C# decoding is encoding-independent.'
Assert-Contains $probe 'DISCOVERY_MAX_CHUNKS = 4' `
    'Shimen discovery must retain the bounded default chunk budget.'
Assert-Contains $probe 'DISCOVERY_MAX_SCOPE_BYTES = 0x800000' `
    'Shimen discovery must cap the selected warm-hint scope.'
Assert-Contains $probe 'scan_taskmgr_owner_tables' `
    'Shimen discovery must use its own bounded TaskMgr owner scan.'
Assert-Contains $probe 'discover_task_list_from_lua_anchor' `
    'Shimen discovery must have a bounded LuaJIT/Cocos cold-start anchor path.'
Assert-Contains $probe 'LUA_GLOBAL_STR_TAB_OFFSET = 0x98' `
    'Lua anchor path must use the validated LuaJIT GC64 global string-table layout.'
Assert-Contains $probe 'discovered_lua_anchor_m_task_list' `
    'Successful Lua anchor discovery must be visible as its own read path.'
Assert-Contains $probe 'task_list_owner_not_verified' `
    'Ownerless task-list candidates must fail closed.'
Assert-Contains $probe 'ownerScan' `
    'Owner-scan diagnostics must be returned to the caller.'
if ($probe.Contains('_find_owner_tables_global')) {
    throw 'Shimen discovery must not call the unbounded shared owner scan.'
}
Assert-Contains $probe 'task_list_discovery_ambiguous' `
    'Ambiguous task-list candidates must fail closed.'
Assert-Contains $probe 'process_identity_changed' `
    'Task snapshots must reject process identity changes.'
Assert-Contains $probe 'identity_after_reader=lambda: process_identity(args.adb, args.pid, broker)' `
    'Main must re-read PID/startTicks/exe before updating the cache.'

Write-Output 'Shimen task reader regression passed.'
