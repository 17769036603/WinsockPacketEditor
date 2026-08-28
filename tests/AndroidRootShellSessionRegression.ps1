param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$CompanionRepositoryRoot = ""
)

$ErrorActionPreference = "Stop"

function Read-SourceFile {
    param(
        [string]$Root,
        [string]$RelativePath
    )

    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $path"
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

if ([string]::IsNullOrWhiteSpace($CompanionRepositoryRoot)) {
    $workspaceParent = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    $companionCandidate = Get-ChildItem -LiteralPath $workspaceParent -Directory |
        Where-Object {
            Test-Path -LiteralPath (Join-Path $_.FullName "tools\equipment-reader\equipment_inventory_resident.py")
        } |
        Select-Object -First 1
    if ($null -eq $companionCandidate) {
        throw "Could not locate the companion equipment reader repository."
    }
    $CompanionRepositoryRoot = $companionCandidate.FullName
}

$session = Read-SourceFile $RepositoryRoot "WPELibrary\Lib\Android\AndroidRootShellSession.cs"
$c6 = Read-SourceFile $RepositoryRoot "WPELibrary\Lib\Vision\TreasureC6ServiceController.cs"
$mountReader = Read-SourceFile $RepositoryRoot "WPELibrary\Lib\MountSpeed\MountStatusAndroidSnapshotReader.cs"
$petReader = Read-SourceFile $RepositoryRoot "WPELibrary\Lib\PetSkillBook\PetSkillBookAndroidSnapshotReader.cs"
$equipmentReader = Read-SourceFile $RepositoryRoot "WPELibrary\Lib\EquipmentRefine\EquipmentInventoryAndroidSnapshotReader.cs"
$mountProbe = Read-SourceFile $RepositoryRoot "tools\mount-reader\mount_status_luajit_probe.py"
$equipmentProbe = Read-SourceFile $CompanionRepositoryRoot "tools\equipment-reader\equipment_inventory_resident.py"
$architecture = Read-SourceFile $RepositoryRoot "ARCHITECTURE.md"

Assert-Contains $session "public static class AndroidRootShellSessionManager" `
    "Android Root access must have one process-wide manager."
Assert-Contains $session "adb.exe, device serial" `
    "The manager must key a cached session by ADB path and device serial."
Assert-Contains $session "NamedPipeServerStream" `
    "External read-only Python workers must have a broker owned by the shared session."
Assert-Contains $session "this.commandGate" `
    "Shared Root commands must be serialized before reading the marker."
Assert-Contains $session "AppDomain.CurrentDomain.ProcessExit" `
    "The process-wide Root manager must clean up on desktop process exit."
Assert-Contains $session "this.session = null;" `
    "A lease must release its caller reference without closing the global session."

Assert-Contains $c6 "AndroidRootShellSessionManager.Acquire" `
    "Treasure C6 must acquire the shared Root session."
Assert-Contains $c6 "rootLease.Execute(" `
    "Treasure C6 Root commands must execute through the shared session."
Assert-NotContains $c6 '"shell", shellCommand' `
    "Treasure C6 must not recreate a per-command su shell."

Assert-Contains $mountReader "AndroidRootShellSessionManager.Acquire" `
    "Mount status reads must acquire the shared Root session."
Assert-Contains $mountReader '"--root-broker-pipe"' `
    "Mount readers must receive the shared Root broker path."
Assert-Contains $mountProbe "class RootBrokerClient" `
    "The mount probe must expose the Root broker client."
Assert-Contains $mountProbe '"--root-broker-pipe"' `
    "The mount probe must accept the Root broker path."

Assert-Contains $petReader "AndroidRootShellSessionManager.Acquire" `
    "Pet property reads must reuse the shared Root session on permission fallback."
Assert-Contains $petReader "BuildRootShellCommand(readerCommand)" `
    "Pet property fallback must remain an explicit shared Root reader boundary."

Assert-Contains $equipmentReader "AndroidRootShellSessionManager.Acquire" `
    "Equipment property reads must acquire the shared Root session."
Assert-Contains $equipmentReader '"--root-broker-pipe"' `
    "Equipment readers must receive the shared Root broker path."
Assert-Contains $equipmentProbe "class RootBrokerClient" `
    "The equipment reader must use the desktop Root broker."
Assert-Contains $equipmentProbe '"--root-broker-pipe"' `
    "The equipment reader must accept the Root broker path."

Assert-Contains $architecture "Android Root/ADB" `
    "The project architecture must document the global Root-session contract."
Assert-Contains $architecture "AndroidRootShellSessionManager" `
    "Future Android presets must be required to use the shared Root session."

Write-Output "AndroidRootShellSessionRegression: PASS (source-only)"
