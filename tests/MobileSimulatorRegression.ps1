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

function Assert-NotContains {
    param([string]$Text, [string]$Unexpected, [string]$Message)
    if ($Text.Contains($Unexpected)) {
        throw $Message
    }
}

$quote = [string][char]34

$plan = Read-SourceFile "mobile\IMPLEMENTATION_PLAN.md"
Assert-Contains $plan "v1.6" "The passwordless mobile implementation plan must be present."

$models = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\SyncModels.java"
foreach ($needle in @(
    "SCHEMA_VERSION = 2", "canonicalPayload", "payloadSha256", "actualSha.equalsIgnoreCase(revision)",
    "ensureUnique", "decodeBase64Strict", "validatedInt", "requireBoolean", "optionalInteger",
    "raw instanceof Boolean", "RuntimeState.FAULTED", "requiresRuntimeIdentity", "isEmptyUuid",
    "manifest.sendCount != send.size()", "equalsIgnoreCase(revision)",
    "validateRuntimeFlags", "validateRuntimeCounts", "explicitTrue", "explicitFalse", "pausing",
    "validateAssistantFields", "optJSONObject(i)", "visionProfile",
    "Integer resolved = null", "snapshot_invalid", "validatedUuid", "isCanonicalUuid", "!text.equals(value)"
)) {
    Assert-Contains $models $needle "Android model validation is missing: $needle"
}
foreach ($needle in @('runtimeUuid', 'runtimeRevision', 'isEmptyUuid(parsed)', 'resolved != count')) {
    Assert-Contains $models $needle "Runtime identity validation is missing: $needle"
}
$packetTypeNeedle = "requireField(packet, " + $quote + "packetType" + $quote
$progressionPacketTypeNeedle = "requireField(value, " + $quote + "packetType" + $quote
Assert-Contains $models $packetTypeNeedle "Send packet type must be required."
Assert-Contains $models $progressionPacketTypeNeedle "Progression packet type must be required."
$loopNeedle = "requireField(value, " + $quote + "loopCount" + $quote
$schemaNeedle = "validatedInt(value, " + $quote + "manifest.schemaVersion" + $quote
Assert-Contains $models $loopNeedle "Missing loop counts must be rejected."
Assert-Contains $models $schemaNeedle "Manifest schema must be strict."
Assert-Contains $models "firstLength > data.length - firstPosition" "Progression ranges must be bounded."
Assert-NotContains $models 'firstBoolean(value, false, "running", "Running", "isBusy", "IsBusy")' "isBusy must not masquerade as running."

$snapshotStore = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\SnapshotStore.java"
foreach ($needle in @("getFD().sync()", "renameTo(backup)", "renameTo(target)", "backupFile", "MAX_STORED_BYTES", "Do not allow a tampered")) {
    Assert-Contains $snapshotStore $needle "Snapshot durability guard is missing: $needle"
}

$store = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\SyncStore.java"
foreach ($needle in @("wpe_mobile_sync_v2", "snapshotVersion", "selectedProgression", "return readProfile(key, true)", "LEGACY_CREDENTIAL_PREFS", "deleteSharedPreferences", ".edit().clear().commit()")) {
    Assert-Contains $store $needle "SyncStore guard is missing: $needle"
}
$activateProfileMethod = [regex]::Match($store, '(?s)public ProfileState activateProfile\(.*?\n    \}')
if (-not $activateProfileMethod.Success) {
    throw "Profile activation method was not found."
}
Assert-Contains $activateProfileMethod.Value "putString(ENDPOINT" `
    "Profile activation must persist endpoint and active profile in one editor transaction."
Assert-Contains $activateProfileMethod.Value "putString(ACTIVE_PROFILE" `
    "Profile activation must persist the active profile in the same editor transaction."
Assert-NotContains $activateProfileMethod.Value "saveConnection(" `
    "Profile activation must not split connection metadata across two async writes."
Assert-NotContains $store "CredentialStore" "The current store must not persist credentials."
Assert-NotContains $store "getPassword" "The current store must not expose persisted passwords."
$snapshotPreferenceNeedle = "putString(prefix + " + $quote + "snapshot" + $quote
Assert-NotContains $store $snapshotPreferenceNeedle "The full snapshot must not be stored in preferences."
$credentialStorePath = Join-Path $RepositoryRoot "mobile\app\src\main\java\com\xnas\wpe\mobile\CredentialStore.java"
if (Test-Path -LiteralPath $credentialStorePath) {
    throw "The obsolete credential store must not remain."
}

$coordinator = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\SyncCoordinator.java"
foreach ($needle in @(
    "newSingleThreadExecutor", "public static SyncCoordinator obtain", "public static SyncCoordinator current()",
    "actionPendingValue", "RuntimeBundle.parseStrict", "publishStateForSession", "publishRuntimeForSession",
    "publishSnapshotForSession", "canControl", "expectedRevision", "requestId", "jobId",
    "snapshotStore.commit", "finalManifest.payloadSha256", "snapshotResponseLimit(initial.payloadBytes)",
    "SessionChangedException", "sessionGeneration", "activateProfile(endpoint, profileName)",
    "currentClient.postJsonObjectWithRetry(path, body)", "isRuntimeRevisionCurrent",
    "hasRuntimeRevisionMismatch", "catch (RuntimeException ex)", "actionInFlight.set(false)",
    "syncInFlight.set(false)", "runtimeInFlight.set(false)",
    "profile = syncStore.readProfile(profile.profileKey, true)", "synchronized (sessionLock)"
)) {
    Assert-Contains $coordinator $needle "Coordinator guard is missing: $needle"
}
$actionPostNeedle = "ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);" +
    [char]10 + "                    JSONObject response"
Assert-Contains $coordinator $actionPostNeedle `
    "Action submission must re-check the session before posting."
Assert-Contains $coordinator "OverlayService.resetProfileState();" `
    "Coordinator shutdown must clear stale overlay state."
Assert-NotContains $coordinator "syncStore.getPassword" "Profile changes must not restore a password."
Assert-Contains $coordinator "DEFAULT_ENDPOINT" "Passwordless startup must have a default desktop endpoint."
Assert-Contains $coordinator "configure(endpoint, syncStore.getUsername())" `
    "A recreated process must reconnect automatically without credentials."
Assert-NotContains $coordinator "effectivePassword" "The coordinator must not retain a mobile password."
Assert-NotContains $coordinator "currentClient.postJsonObject(path, body)" "Start must retry with the same requestId."

$client = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\WpeSyncClient.java"
Assert-Contains $client ($quote + "https://" + $quote) "The mobile transport must enforce HTTPS."
foreach ($needle in @("setInstanceFollowRedirects(false)", "MAX_RESPONSE_BYTES", "postJsonObject", "postJsonObjectWithRetry", "ByteArrayOutputStream", "businessCode", "businessMessage", "getJson(String path, int maxResponseBytes)", "runtime_revision_mismatch", "runtime_faulted", "relative.indexOf('?')", "relative.indexOf('#')", "relative.indexOf('%')")) {
    Assert-Contains $client $needle "Transport guard is missing: $needle"
}
Assert-NotContains $client 'setRequestProperty("Authorization"' `
    "The passwordless mobile transport must not send an Authorization header."
Assert-NotContains $client "android.util.Base64" `
    "The passwordless mobile transport must not retain Basic Auth encoding."

$overlay = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\OverlayService.java"
foreach ($needle in @(
    "ACTION_START", "ACTION_PAUSE", "ACTION_STOP", "ACTION_SELECT_MODULE", "ACTION_SELECT_PRESET", "ACTION_REFRESH",
    "primaryButton", "presetLabel", "presetButton.setSingleLine(false)", "presetButton.setMaxLines(2)",
    "presetRow.setWeightSum(3f)", "groupRow.setOnClickListener", "getActiveModule()", "getSelectedPreset()",
    "groupAccessibilityLabel", "codePoints", "installPressFeedback", "setDuration(180L)",
    "START_STICKY", "START_NOT_STICKY", "drainPendingCommands", "HANDLED_COMMAND_IDS", "ensureCoordinator",
    "coordinator.refreshRuntime()", "RUNTIME_POLL_INTERVAL_MS", "selectionLocked", "expandedGroups", "restoreGroupScroll",
    "overlayPanelHeight()", "updateModuleButtons", "updateActionPending", "updateErrorStatus", "clearErrorStatus",
    "INTERNAL_COMMAND_PERMISSION",
    "runtime.state != SyncModels.RuntimeState.STOPPING", "RuntimeState.FAULTED",
    "resumeSelectionMatches",
    "restorePanelState(coordinator)", "updatePanelData(currentSnapshot", 'errorStatus = "";',
    "A successful runtime/snapshot replay must not leave an older",
    "resetProfileState();"
)) {
    Assert-Contains $overlay $needle "Overlay guard is missing: $needle"
}
Assert-NotContains $overlay "presetMetaText" `
    "Individual preset buttons must not render send-count, interval, or sync metadata."
Assert-Contains $overlay "dispatch(action, activeModule.wireName,`n                selectedPresetIdForModule(activeModule))" `
    "Primary overlay actions must carry the module and preset selected at click time."
Assert-Contains $overlay "Never replace it with the runtime's last completed preset." `
    "START must not reuse a completed task's preset."
Assert-Contains $overlay "UUID requestedPresetId = parseUuid(intent.getStringExtra(EXTRA_PRESET_ID));" `
    "START must resolve the preset id carried by the button command."
Assert-NotContains $overlay "selectedPreset = findPreset(snapshot, commandModule,`n                    coordinator.getRuntime().forModule(commandModule).presetId);" `
    "START must not overwrite the selected preset from the runtime snapshot."
Assert-Contains $overlay "SyncModels.Module targetModule = moduleFrom(intent.getStringExtra(EXTRA_MODULE));" `
    "Preset selection must resolve its target module before checking runtime state."
Assert-Contains $overlay "coordinator.getRuntime().forModule(targetModule)" `
    "Preset selection must check the target module runtime."
Assert-Contains $overlay "SyncModels.Module commandModule = moduleFrom(intent.getStringExtra(EXTRA_MODULE));" `
    "Delayed overlay actions must resolve their command module explicitly."
Assert-Contains $overlay "if (activeService != this)" `
    "A stale overlay service must not execute queued actions."
Assert-Contains $overlay "enqueuePendingCommand(intent)" `
    "Stale overlay actions must return to the pending command queue."
Assert-Contains $overlay "Only the owner may clear the process-wide" `
    "A stale overlay service must not clear a replacement service state."
Assert-Contains $overlay ($quote + "send" + $quote + ")") "The overlay must expose the send entry."
Assert-Contains $overlay ($quote + "progression" + $quote + ")") "The overlay must expose the progression entry."
Assert-Contains $overlay ($quote + "assistant" + $quote + ")") "The overlay must expose the assistant entry."
Assert-NotContains $overlay "summaryView" "The separate selected summary must stay removed."
Assert-NotContains $overlay "detailsView" "The separate details panel must stay removed."
Assert-NotContains $overlay "WpeSyncClient" "The overlay must not create a second network client."

$main = Read-SourceFile "mobile\app\src\main\java\com\xnas\wpe\mobile\MainActivity.java"
foreach ($needle in @("SyncCoordinator", "ACTION_SELECT_PRESET", "maybeAutoStartOverlay", "moveTaskToBack(true)", "hideSetupPage()", "setupPageHidden = true", "View.GONE", "protected void onNewIntent", "alignSelectionToRuntime", "status.state != SyncModels.RuntimeState.STOPPING", "resumeSelectionMatches", "automaticStartup = true", "OverlayService.isRunning()", "overlayUiReady", "deferredOverlayCommands", "drainOverlayCommands()", "handleOverlayCommand(intent)", "while (deferredOverlayCommands.size() >= 8)", "if (values != null)")) {
    Assert-Contains $main $needle "Activity guard is missing: $needle"
}
Assert-Contains $main "Do not claim a command until the Activity can execute it." `
    "Activity command deferral must keep the service queue claimable."
Assert-Contains $main "command.getStringExtra(OverlayService.EXTRA_COMMAND_ID)))" `
    "Deferred overlay commands must be deduplicated at execution time."
Assert-Contains $main "activeOverlayModule = moduleFrom(intent.getStringExtra(OverlayService.EXTRA_MODULE));" `
    "Activity action commands must follow the module carried by the command."
Assert-Contains $main "SyncModels.Module overlayModule = OverlayService.getActiveModule();" `
    "Overlay refreshes must use the service-owned active module."
Assert-Contains $main "SyncModels.Preset overlayPreset = OverlayService.getSelectedPreset();" `
    "Overlay refreshes must use the service-owned selected preset."
Assert-Contains $main "contentEquals(syncStatus.getText())" `
    "A completed overlay start must clear the transient startup status."
Assert-NotContains $main "new WpeSyncClient" "The Activity must not create another network client."
Assert-NotContains $main "getWindow().getDecorView().setVisibility" "Activity visibility must be controlled by the content root."
Assert-NotContains $main "passwordInput" "The hidden Activity must not retain a password field."
Assert-NotContains $main "usernameInput" "The hidden Activity must not retain an account field."
Assert-NotContains $main "showSetupPage" "No Activity path may reveal the retired setup page."
$emptyStatusNeedle = "setSyncStatus(" + $quote + $quote + ", false)"
Assert-Contains $main $emptyStatusNeedle "Stopping the overlay must clear stale startup status."

$manifest = Read-SourceFile "mobile\app\src\main\AndroidManifest.xml"
foreach ($needle in @("android.permission.INTERNET", "android.permission.FOREGROUND_SERVICE", "android.permission.RECEIVE_BOOT_COMPLETED", ".BootReceiver", "INTERNAL_OVERLAY_COMMAND", "protectionLevel=" + $quote + "signature" + $quote, "networkSecurityConfig", "dataExtractionRules", "excludeFromRecents")) {
    Assert-Contains $manifest $needle "Manifest guard is missing: $needle"
}
$backupFalseNeedle = "android:allowBackup=" + $quote + "false" + $quote
Assert-Contains $manifest $backupFalseNeedle "Android backup must be disabled."
$clearTextNeedle = "android:usesCleartextTraffic=" + $quote + "true" + $quote
Assert-NotContains $manifest $clearTextNeedle "Clear-text Android transport must remain disabled."

$networkConfig = Read-SourceFile "mobile\app\src\main\res\xml\network_security_config.xml"
Assert-Contains $networkConfig "@raw/wpe_mobile_local" "The local HTTPS certificate policy must be present."

$web = Read-SourceFile "WPELibrary\Lib\WebAPI\Socket_Web.cs"
Assert-Contains $web "if (isMobileSync)" "MobileSync must have an explicit passwordless route boundary."
Assert-Contains $web "await next.Invoke();" "Passwordless MobileSync requests must reach Web API."
Assert-NotContains $web "IsValidMobile(username, password)" "MobileSync must not validate account credentials."
Assert-Contains $web "IsValidAdmin(username, password)" "Non-mobile routes must use admin credentials."
Assert-Contains $web "/MobileSync/" "Mobile route matching must include subroutes."
Assert-Contains $web "IsLocalNetworkAddress" "Passwordless MobileSync must be limited to local-network clients."
Assert-Contains $web "context.Request.RemoteIpAddress" "MobileSync local-network enforcement must use the actual peer address."
Assert-NotContains $web "bytes[0] == 100" "CGNAT shared-address space must not be treated as a private LAN boundary."

$injectorForm = Read-SourceFile "WinsockPacketEditor\Injector_Form.cs"
$socketForm = Read-SourceFile "WPELibrary\Socket_Form.cs"
Assert-NotContains $injectorForm "StartRemoteMGT(" `
    "The injector process must not own the MobileSync listener."
Assert-NotContains $injectorForm "StopRemoteMGT(" `
    "The injector process must not stop a target-owned MobileSync listener."
Assert-Contains $socketForm "Socket_Cache.System.LoadSystemList_FromDB();" `
    "The target form must restore the remote-management configuration."
Assert-Contains $socketForm "Socket_Operation.StartRemoteMGT();" `
    "The injected target form must own the MobileSync listener."
Assert-Contains $socketForm "Socket_Operation.StopRemoteMGT(this.RunMode);" `
    "The injected target form must stop its MobileSync listener on exit."

$socketOperation = Read-SourceFile "WPELibrary\Lib\Socket_Operation.cs"
$tcpOwinHost = Read-SourceFile "WPELibrary\Lib\WebAPI\Socket_TcpOwinHost.cs"
Assert-Contains $socketOperation "Socket_TcpOwinHost.Start(remoteUri)" `
    "A non-elevated injected target must have an HTTPS fallback that does not require a URL ACL."
Assert-Contains $tcpOwinHost "new TcpListener(" `
    "The MobileSync fallback must bind a user-mode TCP listener."
Assert-Contains $tcpOwinHost "new Socket_Web().Configuration(builder);" `
    "The HTTPS fallback must reuse the existing OWIN route and authentication boundaries."
Assert-Contains $tcpOwinHost "SslProtocols.Tls12" `
    "The HTTPS fallback must keep encrypted transport."
Assert-Contains $tcpOwinHost "CancelAfter(TimeSpan.FromSeconds(15))" `
    "The HTTPS fallback must bound slow client connections."
Assert-Contains $tcpOwinHost "MaxConcurrentClients = 64" `
    "The HTTPS fallback must cap concurrent clients before starting TLS/request work."
Assert-Contains $tcpOwinHost "this.clientSlots.Wait(0)" `
    "The HTTPS fallback must reject excess clients without an unbounded worker fan-out."

$controller = Read-SourceFile "WPELibrary\Lib\WebAPI\MobileSync_Controller.cs"
foreach ($needle in @("schemaVersion", "payloadSha256", "payloadBytes", "bufferBase64", "combinationFirstPosition", "MobileRequestDeduplicator", "request.RequestId.Length > 128", "RequestCacheKey", "runtime_faulted", "Cache a safe, non-accepted result")) {
    Assert-Contains $controller $needle "Desktop MobileSync guard is missing: $needle"
}
foreach ($needle in @(
    "if (!string.Equals(activeRevision, revision, StringComparison.OrdinalIgnoreCase))",
    "if (!string.Equals(current.Revision, revision, StringComparison.OrdinalIgnoreCase))",
    "if (lastStoppedJobId == jobId && jobId != Guid.Empty)",
    "bool jobScopedStop = requiresJobId && presetId == Guid.Empty",
    "MobileSendRuntime.Stop(request.JobId, request.ExpectedRevision)",
    "MobileProgressionRuntime.Stop(request.JobId, request.ExpectedRevision)",
    "MobileAssistantRuntime.Stop(request.JobId, request.ExpectedRevision)"
)) {
    Assert-Contains $controller $needle "Runtime action contract guard is missing: $needle"
}
foreach ($needle in @('HashSet<Guid> usedGroupIds', 'usedGroupIds.Contains(id)', 'ISet<Guid> usedGroupIds')) {
    Assert-Contains $controller $needle "Stable group identity collision guard is missing: $needle"
}
Assert-Contains $controller "Starting || Running || Pausing || Paused || Stopping" `
    "The server runtime busy flag must include pausing."
Assert-Contains $controller '[JsonProperty("pausing")] public bool Pausing' `
    "The server runtime DTO must expose pausing explicitly."
Assert-Contains $models 'boolean pausing = firstBoolean(value, false, "pausing", "Pausing")' `
    "The Android runtime model must parse pausing explicitly."
Assert-NotContains $controller "&& current.JobId == jobId" "Progression stop idempotence must not depend on the completed job remaining visible."

Assert-Contains $main "coordinator.loadSavedConnection();" "Activity restart must reconnect without credentials."
Assert-Contains $main "executeAction(moduleUi(activeOverlayModule), SyncCoordinator.Action.START,`n                    parseUuid(intent.getStringExtra(OverlayService.EXTRA_PRESET_ID)));" `
    "The Activity fallback path must preserve the preset id carried by START."
Assert-Contains $main "if (action != SyncCoordinator.Action.START" `
    "The Activity must only prefer runtime preset ids for pause/stop."
Assert-Contains $overlay "coordinator.loadSavedConnection();" "A recreated service must reconnect without exposing the Activity."
Assert-Contains $coordinator "currentRuntime.presetId == null" "Pause must stay bound to the running preset."
Assert-Contains $coordinator "currentRuntime.state == SyncModels.RuntimeState.PAUSED" "Resume must explicitly identify the paused task."
Assert-Contains $coordinator "currentRuntime.state != SyncModels.RuntimeState.RUNNING" "Pause must reject transition states."

$byteSweep = Read-SourceFile "WPELibrary\Socket_Form.ByteSweep.cs"
foreach ($needle in @("WaitIfPaused", "string ErrorCode", "runtime_not_connected", "SetRevision(jobId, revision)")) {
    Assert-Contains $byteSweep $needle "Progression guard is missing: $needle"
}
$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
$controller = Read-SourceFile "WPELibrary\Lib\WebAPI\MobileSync_Controller.cs"
Assert-Contains $cache "int resolvedSocket = Socket_Cache.SocketList.ResolveCurrentSocket(sendCollection);" `
    "Every mobile send preset must resolve the current matching socket instead of reusing a stale PacketSocket."
Assert-Contains $controller 'errorCode = "send_failed";' `
    "A mobile send that starts but sends zero successful packets must be reported as a fault."
Assert-Contains $controller "send.Send_Success <= 0" `
    "Mobile send runtime must inspect actual send success rather than treating worker completion as delivery."

$apk = Join-Path $RepositoryRoot "mobile\app\build\outputs\apk\debug\app-debug.apk"
if (-not (Test-Path -LiteralPath $apk) -or (Get-Item -LiteralPath $apk).Length -le 0) {
    throw "Debug APK was not produced."
}

Write-Output "Mobile simulator v1.6 regression checks passed."
