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

$mouseAction = Read-SourceFile "WPELibrary\Lib\Vision\VisionMouseAction.cs"
Assert-Contains $mouseAction "AllowSystemInput" `
    "Real mouse actions must require an explicit run-level authorization."
Assert-Contains $mouseAction "context.Condition.Region.Resolve(clientSize)" `
    "Mouse actions must resolve the observed condition region against the target client size."
Assert-Contains $mouseAction "observationRegion.Left + textBounds.Left" `
    "OCR action coordinates must be translated from observation-relative to client-relative coordinates."
Assert-Contains $mouseAction "observationRegion.Left + bounds.Left" `
    "Template and color action coordinates must include the observation-region origin."
Assert-Contains $mouseAction "IsForegroundWindow" `
    "System input must verify that the target still owns the foreground immediately before the event."

$condition = Read-SourceFile "WPELibrary\Lib\Vision\VisionConditionDefinition.cs"
Assert-Contains $condition "bool FitsWithin(Size clientSize, out string error)" `
    "Vision conditions must expose validation against the actual client area."

$runner = Read-SourceFile "WPELibrary\Lib\Vision\VisionAssistantRunner.cs"
Assert-Contains $runner "step.Action = null" `
    "The runner must clear stale action instances before rebuilding a step."
Assert-Contains $runner "current run is explicitly confirmed" `
    "The runner must fail closed when real system input was not authorized."
Assert-Contains $runner "TryResolveWindow" `
    "The assistant runner must resolve the live target before preflight validation."
Assert-Contains $runner "step.Condition.FitsWithin(clientSize" `
    "The assistant runner must validate every step condition against the live client area."
Assert-Contains $runner "step.Verification.FitsWithin(clientSize" `
    "The assistant runner must validate every verification region against the live client area."

$form = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"
Assert-Contains $form 'Name = "bVisionConfirmAction"' `
    "The vision assistant must expose an explicit confirm-action button."
Assert-Contains $form 'Name = "bVisionCancelAction"' `
    "The vision assistant must expose a cancel-action button."
Assert-Contains $form "bCancelVisionAction_Click" `
    "Canceling an action must reset the unsaved vision editor state."
Assert-Contains $form "bConfirmVisionAction_Click" `
    "Confirming an action must create or update the selected assistant step."
Assert-Contains $form "BuildVisionAssistantStepFromEditors" `
    "Vision assistant creation and update must use one shared editor path."
Assert-Contains $form "Vision_NewStep" `
    "The assistant step picker must expose a new-step mode."
Assert-Contains $form "private ListBox cbbVisionSteps" `
    "All assistant steps must be visible in a list instead of a collapsed dropdown."
Assert-Contains $form "bVisionActionVerificationMenu" `
    "Action verification must expose a dropdown menu."
Assert-Contains $form "assistantLayout.Controls.Add(confirmSettings, 0, 11)" `
    "The final confirm-action row must be placed after the recognition-flow test row."
Assert-Contains $form "assistantLayout.Controls.Add(assistantActions, 0, 10)" `
    "The recognition-flow test row must appear before final confirmation."
Assert-Contains $form "dgvRobotInstruction_CellToolTipTextNeeded" `
    "The right command list must expose full instruction content when a cell is truncated."
Assert-Contains $form 'AccessibleName = UiText("Vision_OcrKeyword")' `
    "The OCR keyword input must expose a localized accessible name."
Assert-Contains $form 'AccessibleName = UiText("Vision_Condition")' `
    "The vision condition picker must expose a localized accessible name."
Assert-Contains $form 'AccessibleName = UiText("Vision_VerificationKeyword")' `
    "The verification keyword input must expose a localized accessible name."
Assert-Contains $form 'BackColor = Color.FromArgb(0, 120, 215)' `
    "The final confirm action must have a distinct primary-action treatment."
Assert-Contains $form 'UiText("Vision_AdvancedSettingsHint")' `
    "The settings page must explain what advanced settings contain."
Assert-Contains $form 'this.lVisionOcrStatus = CreateVisionStatusLabel(string.Empty)' `
    "The recognition page should hide the default OCR explanation note."
Assert-Contains $form 'this.lVisionOcrStatus.Visible = !string.IsNullOrWhiteSpace(text)' `
    "Runtime OCR status messages must remain visible after the default note is hidden."
Assert-Contains $form "modeTabs.SelectedIndex == 0 ? 130 : 225" `
    "The recognition-mode section must stay compact for OCR and preserve image-preview space for templates."
Assert-Contains $form "Height = 130," `
    "The recognition-mode section must use the compact OCR height by default."
Assert-Contains $form 'Name = "bToggleVisionAssistantLog"' `
    "The assistant execution log must expose an explicit expand/collapse control."
Assert-Contains $form "SetVisionAssistantLogExpanded(true)" `
    "Starting the assistant test must reveal the execution log."
Assert-Contains $form "this.visionAssistantLogHost.Controls.Remove(this.txtVisionAssistantLog)" `
    "The assistant execution log must collapse without leaving an empty reserved row."
Assert-Contains $form "assistantLayout.Controls.Add(verificationSettings, 0, 5)" `
    "Action verification must appear before the final confirm-action row."
Assert-NotContains $form "bAddVisionOcrStep_Click" `
    "The obsolete text-wait add handler must be removed."
Assert-NotContains $form "bAddVisionTemplateStep_Click" `
    "The obsolete image-step add handler must be removed."
Assert-NotContains $form "bConfirmVisionStep_Click" `
    "The duplicate confirm-step handler must be removed."
Assert-Contains $form "ApplyVisionConditionEditors" `
    "Selecting or saving an assistant step must apply its editor values to that step."
Assert-Contains $form "LoadVisionTemplateForStep" `
    "Selecting an assistant step must restore the corresponding template preview."
Assert-Contains $form "DisposeVisionConditionTemplates(step.Verification)" `
    "Verification templates and variants must be released with the assistant profile."
Assert-Contains $form "SetVisionStatusLabelWidths" `
    "Vision status text must be constrained and wrapped in narrow layouts."
Assert-Contains $form "this.MinimumSize = new Size(600, 511)" `
    "The robot editor must not shrink below the designed two-pane layout width."
Assert-NotContains $form "InitVisionLayoutLegacy" `
    "The obsolete duplicate vision layout must not remain in the form."
Assert-Contains $form "IVisionTextRecognizer recognizer = this.visionTextRecognizer" `
    "Background OCR tasks must capture a stable recognizer reference before dispatch."

$resources = Read-SourceFile "WPELibrary\Properties\Resources.resx"
$englishResources = Read-SourceFile "WPELibrary\Properties\Resources.en-US.resx"
Assert-Contains $resources 'name="Vision_ActionCancelled"' `
    "The Chinese reset status resource must be present."
Assert-Contains $resources 'name="Vision_VerificationIncomplete"' `
    "The Chinese incomplete-verification status resource must be present."
Assert-Contains $resources 'name="Vision_FailurePolicy"' `
    "The Chinese failure-policy label resource must be present."
Assert-Contains $englishResources 'name="Vision_ActionCancelled"' `
    "The English reset status resource must be present."
Assert-Contains $englishResources 'name="Vision_VerificationIncomplete"' `
    "The English incomplete-verification status resource must be present."
Assert-Contains $englishResources 'name="Vision_FailurePolicy"' `
    "The English failure-policy label resource must be present."

$observationProvider = Read-SourceFile "WPELibrary\Lib\Vision\VisionWindowObservationProvider.cs"
Assert-Contains $observationProvider "this.captureSettings.RequireExactClientSize" `
    "Exact-size observation failures must stop instead of retrying a hidden or minimized target."
Assert-Contains $observationProvider "VisionObservation.TerminalFailure(resolveReason)" `
    "Exact-size resolution failures must be represented as terminal observations."
Assert-Contains $observationProvider "if (captureResult.IsBlank)" `
    "Blank captures must skip recognition instead of evaluating stale pixels."
Assert-Contains $observationProvider "!captureResult.IsBlank" `
    "Blank captures must not reuse the unchanged-frame recognition cache."
Assert-Contains $observationProvider "wpe_vision_*.png" `
    "Failure snapshot cleanup must be limited to files generated by this feature."
Assert-Contains $observationProvider "CleanupFailureSnapshots(directory, 200)" `
    "Failure snapshots must have a bounded retention count."

$windowService = Read-SourceFile "WPELibrary\Lib\Vision\VisionWindowService.cs"
Assert-Contains $windowService "AttachThreadInput" `
    "Window activation must handle Windows foreground-thread restrictions."
Assert-Contains $windowService "BringWindowToTop" `
    "Window activation must bring the target window to the top before verification."
Assert-Contains $windowService "for (int attempt = 0; attempt < 4; attempt++)" `
    "Window activation must retry the asynchronous foreground transition."
Assert-Contains $windowService "GetForegroundWindow() != hWnd" `
    "Automatic screen capture must not trust pixels from a background target window."
Assert-Contains $windowService "CaptureWindowRender" `
    "Automatic capture must have a safe window-render fallback."
Assert-Contains $windowService "!screenResult.IsBlank" `
    "Automatic capture must reject blank screen pixels before falling back."
Assert-Contains $windowService "multiple windows match the configured process identity" `
    "Window rebinding must fail closed when identity matching is ambiguous."
Assert-Contains $windowService "public static bool IsForegroundWindow" `
    "The vision window service must expose a final foreground-window safety check."

$fingerprint = Read-SourceFile "WPELibrary\Lib\Vision\VisionBitmapFingerprint.cs"
Assert-Contains $fingerprint "LockBits" `
    "Vision cache fingerprints must cover the complete bitmap instead of sparse samples."
Assert-Contains $windowService "VisionBitmapFingerprint.Compute(bitmap)" `
    "Capture diagnostics must use the full-pixel fingerprint."

$onnx = Read-SourceFile "WPELibrary\Lib\Vision\VisionOnnxTextRecognizer.cs"
Assert-Contains $onnx "modelSignature" `
    "ONNX sessions must be invalidated when the model package changes."
Assert-Contains $onnx "BuildModelSignature" `
    "ONNX cache validation must include model and preprocessing sidecars."
Assert-Contains $onnx "dimensions.Length != 4" `
    "Unsupported ONNX image input ranks must fail explicitly."
Assert-Contains $onnx "current.DetectionPreprocess.OutputIsLogits" `
    "Detection output probability/logit interpretation must be configurable."
Assert-Contains $onnx "current.RecognitionPreprocess.OutputIsLogits" `
    "Recognition output probability/logit interpretation must be configurable."
Assert-Contains $onnx "preprocess.json" `
    "ONNX preprocessing must support the documented sidecar."
Assert-Contains $onnx "lock (this.modelSync)" `
    "ONNX sessions must not be disposed concurrently with inference."

$hook = Read-SourceFile "WPELibrary\Lib\WinSockHook.cs"
Assert-Contains $hook "CopyFilteredReceiveBuffer" `
    "Filtered receive buffers must use a bounded copy into the caller buffer."
Assert-NotContains $hook "new Span<byte>(bNewBuffer).CopyTo" `
    "Receive hooks must not copy a larger source span into a shorter destination span."

$cache = Read-SourceFile "WPELibrary\Lib\Socket_Cache.cs"
Assert-Contains $cache "QueueDeferredExecution" `
    "Filter-triggered sends and robots must leave the Winsock callback through a bounded queue."
Assert-Contains $cache "StopDeferredExecution" `
    "Stopping the hook must prevent new deferred filter actions from starting."
Assert-Contains $cache "WaitForCompletion(100)" `
    "Deferred filter actions must remain bounded until the actual send or robot completes."

$robot = Read-SourceFile "WPELibrary\Lib\Socket_Robot.cs"
Assert-Contains $robot "throw;" `
    "Unexpected robot instruction errors must reach BackgroundWorker completion as failures."
Assert-Contains $robot "WaitForCompletion" `
    "Deferred filter actions must be able to wait for the real robot worker."
Assert-Contains $form "this.sr.StopRobot();" `
    "Closing the robot editor must stop an active robot."
Assert-Contains $form "this.visionRobotClosing" `
    "Robot editor shutdown must defer recognizer disposal until the worker completes."

$preprocessor = Read-SourceFile "WPELibrary\Lib\Vision\VisionImagePreprocessor.cs"
Assert-Contains $preprocessor "output.UnlockBits(outputData)" `
    "Image preprocessing must unlock bitmap data before disposal."

$worker = Read-SourceFile "vision_worker\worker.py"
Assert-Contains $worker "self._ocr_by_key" `
    "Python OCR engines must be cached by model and language instead of one global instance."
Assert-Contains $worker "_validate_model_directory" `
    "Python OCR must fail closed when bundled offline models are missing."
Assert-Contains $worker "scale_factor" `
    "Python OCR must receive the configured preprocessing scale."
Assert-Contains $worker "x_scale = original_width" `
    "Python OCR boxes must be mapped back to the source image coordinates."
Assert-Contains $worker "character_whitelist" `
    "Python OCR must apply the configured character whitelist."
Assert-Contains $worker "begin_input_session" `
    "Airtest input must require a worker-side authorization session."
Assert-Contains $worker "authorization_token" `
    "Airtest actions must validate a per-run authorization token."

$pythonBridge = Read-SourceFile "WPELibrary\Lib\Vision\VisionPythonWorkerTextRecognizer.cs"
Assert-Contains $pythonBridge "ResolveWorkerModelDirectory" `
    "Python Worker relative model paths must resolve beside worker.py."
Assert-Contains $pythonBridge "public VisionCaptureResult Capture" `
    "Airtest capture must be exposed through the C# JSONL bridge."
Assert-Contains $pythonBridge "ExecuteAirtestAction" `
    "Airtest input actions must be exposed through the C# JSONL bridge."
Assert-Contains $pythonBridge "PythonWorkerTimeoutMilliseconds" `
    "Python Worker timeout configuration must be wired to the bridge."

$captureMode = Read-SourceFile "WPELibrary\Lib\Vision\VisionCaptureSourceMode.cs"
Assert-Contains $captureMode "Airtest = 3" `
    "Airtest must be a selectable capture source."
$form = Read-SourceFile "WPELibrary\Socket_RobotForm.cs"
Assert-Contains $form "VisionCaptureSourceMode.Airtest" `
    "The UI must expose the Airtest capture source."
Assert-Contains $form "txtVisionPythonExecutable" `
    "The UI must expose the Python runtime path."
Assert-Contains $form "txtVisionPythonWorkerScript" `
    "The UI must expose the Worker script path."
Assert-Contains $form "nudVisionPythonWorkerTimeout" `
    "The UI must expose the Worker timeout."

$applicationProject = Read-SourceFile "WinsockPacketEditor\WinsockPacketEditor.csproj"
foreach ($model in @(
    "ch_ppocr_mobile_v2.0_cls_mobile.onnx",
    "PP-OCRv6_det_small.onnx",
    "PP-OCRv6_rec_small.onnx"
)) {
    Assert-Contains $applicationProject $model `
        "ClickOnce output must include the bundled RapidOCR model: $model"
}

$tesseract = Read-SourceFile "WPELibrary\Lib\Vision\VisionTesseractRecognizer.cs"
Assert-Contains $tesseract "IDisposable" `
    "The Tesseract recognizer must release its worker gate."
Assert-Contains $tesseract "DrainProcessOutput" `
    "Killed Tesseract processes must drain redirected output before cleanup."
Assert-Contains $tesseract "WriteAsync" `
    "Tesseract image input must be written asynchronously so cancellation can be observed."
Assert-Contains $tesseract "while (!writeTask.IsCompleted)" `
    "Tesseract input writing must obey the configured deadline."
Assert-NotContains $tesseract "BaseStream.Write(pngBytes" `
    "Tesseract recognition must not synchronously block on the redirected input pipe."
Assert-Contains $tesseract "TaskContinuationOptions.ExecuteSynchronously" `
    "Tesseract timeout cleanup must observe unfinished pipe tasks without blocking the OCR worker."

$matcher = Read-SourceFile "WPELibrary\Lib\Vision\VisionTemplateMatcher.cs"
Assert-Contains $matcher "MaxTemplateComparisons" `
    "Template matching must reject unbounded source-template workloads."
Assert-Contains $matcher "VisionTemplateWorkloadException" `
    "Oversized template workloads must fail as a terminal recognition condition."
Assert-Contains $matcher "cancellationToken.IsCancellationRequested" `
    "Template matching must check cancellation inside the pixel comparison loop."

$stateMachine = Read-SourceFile "WPELibrary\Lib\Vision\VisionAssistantStateMachine.cs"
Assert-NotContains $stateMachine "Step '{0}' action verification confirmed." `
    "Vision state-machine logs must use localized resources."
Assert-Contains $stateMachine "Vision_LogVerificationConfirmed" `
    "Action verification success logs must have a localized resource key."

foreach ($relativePath in @(
    "WPELibrary\Properties\Resources.resx",
    "WPELibrary\Properties\Resources.en-US.resx"
)) {
    $resource = Read-SourceFile $relativePath
    Assert-Contains $resource "Vision_LogVerificationConfirmed" `
        "$relativePath must define the action verification log resource."
    $null = [xml]$resource
}

Assert-Contains (Read-SourceFile "ARCHITECTURE.md") "preprocess.json" "Architecture documentation must describe the ONNX preprocessing sidecar."
Assert-Contains (Read-SourceFile "PROJECT_STATUS.md") "preprocess.json" "Project status must record the ONNX preprocessing sidecar."

Write-Output "Vision review fixes regression passed."
