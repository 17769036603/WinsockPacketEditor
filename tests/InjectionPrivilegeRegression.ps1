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

$manifest = Read-SourceFile "WinsockPacketEditor\Properties\app.manifest"
$access = Read-SourceFile "WPELibrary\Lib\NativeMethods\ProcessInjectionAccess.cs"
$project = Read-SourceFile "WPELibrary\WPELibrary.csproj"
$injector = Read-SourceFile "WinsockPacketEditor\Injector_Form.cs"

Assert-Contains $manifest 'requestedExecutionLevel level="asInvoker"' `
    "The ClickOnce injector must keep the compatible asInvoker manifest."
Assert-Contains $access "SeDebugPrivilege" `
    "The injector must enable SeDebugPrivilege before opening the target process."
Assert-Contains $access "ProcessCreateThread |" `
    "The injector preflight must request remote-thread access."
Assert-Contains $injector "ProcessInjectionAccess.EnsureAccess(ProcessID);" `
    "The injector must run the target access preflight before EasyHook."
Assert-Contains (Read-SourceFile "WinsockPacketEditor\Lib\Program.cs") 'startInfo.Verb = "runas";' `
    "The injector must keep its ClickOnce-compatible administrator handoff."
Assert-Contains $project 'Lib\NativeMethods\ProcessInjectionAccess.cs' `
    "The process access helper must be included in the WPELibrary build."

Write-Output "Injection privilege regression checks passed."
