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
    param([string]$Text, [string]$Expected, [string]$Message)
    if (-not $Text.Contains($Expected)) {
        throw $Message
    }
}

$robot = Read-SourceFile "WPELibrary\Lib\Socket_Robot.cs"
$robotForm = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"
$sender = Read-SourceFile "WPELibrary\Lib\EquipmentRefine\EquipmentRefineSocketPacketSender.cs"
$stateMachine = Read-SourceFile "WPELibrary\Lib\EquipmentRefine\EquipmentRefineStateMachine.cs"

Assert-Contains $robot '"WPE_EQUIPMENT_REFINE_TEMPLATE_FILE"' `
    "The robot must support an explicitly configured verified template file."
Assert-Contains $robot 'this.GetParameter("EquipmentRefineMemoryResultSource")' `
    "The robot must accept an explicitly injected memory result source."
Assert-Contains $robot 'new ResidentJsonlRefineMemoryResultSource(' `
    "The robot must use the strict read-only JSON/JSONL result adapter for an explicit state path."
Assert-Contains $robot 'BagTargetMode = runPreset.BagTargetMode' `
    "The robot must pass the saved bag target mode into the state machine."
Assert-Contains $robot 'BagTarget = runPreset.BagTarget' `
    "The robot must pass the saved bag target identity into the state machine."
Assert-Contains $robot 'MemoryResultMode = runPreset.MemoryResultMode' `
    "The robot must pass the saved typed-result mode into the state machine."
Assert-Contains $robot 'machine.ConfigureMemoryResultSource(memoryResultSource)' `
    "The robot must bind the resolved memory result source before running."
Assert-Contains $robotForm 'parameters["EquipmentRefineSocketRouteTemplate"] = routeTemplate' `
    "The assistant form must pass only the captured route metadata to the robot."
Assert-Contains $robotForm 'SetEquipmentRefineBagOptions' `
    "The assistant form must expose an explicit bag-equipment injection seam."
Assert-Contains $robotForm 'SetEquipmentRefineBagOptionsFromInventory' `
    "The assistant form must be able to populate bag equipment from a verified read-only snapshot."
Assert-Contains $robotForm 'SetEquipmentRefineBagInventoryProvider' `
    "The assistant form must expose a read-only bag inventory provider seam."
Assert-Contains $robotForm 'ResolveEquipmentRefineBagInventoryProvider' `
    "The assistant form must resolve an explicitly configured state-file fallback for bag reads."
Assert-Contains $robotForm 'EquipmentRefineInventoryProvider' `
    "The assistant form must carry the resolved read-only bag provider into the run parameters."
Assert-Contains $sender 'EquipmentRefineLiveSendAuthorization' `
    "The socket sender must require the explicit live-send authorization type."
Assert-Contains $sender 'ResolveCurrentRoute(' `
    "The socket sender must refresh the current route before each send."
Assert-Contains $stateMachine 'target_reached_before_send' `
    "The state machine must stop before sending when current attributes already match."

Write-Output "EquipmentRefineRuntimeWiringRegression: PASS (source-only)"
