$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionWindowService.cs')
$region = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionRegion.cs')
$provider = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionWindowObservationProvider.cs')
$profile = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Socket_VisionProfile.cs')
$form = Get-Content -Raw (Join-Path $root 'WPELibrary\Socket_RobotForm.cs')
$cache = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Socket_Cache.cs')
$ocrOptions = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionOcrOptions.cs')
$preprocessor = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionImagePreprocessor.cs')
$templateOptions = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionTemplateMatchOptions.cs')
$templateMatcher = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionTemplateMatcher.cs')
$observation = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionObservation.cs')
$onnx = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionOnnxTextRecognizer.cs')
$autoOcr = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionAutoTextRecognizer.cs')
$colorMatcher = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionColorMatcher.cs')
$colorCondition = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionColorCondition.cs')
$mouseAction = Get-Content -Raw (Join-Path $root 'WPELibrary\Lib\Vision\VisionMouseAction.cs')

function Assert-Contains([string]$text, [string]$needle, [string]$message) {
    if (-not $text.Contains($needle)) {
        throw $message
    }
}

Assert-Contains $service 'CaptureClientRegionDetailed' 'Detailed capture API is missing.'
Assert-Contains $service 'PrintWindow' 'Window-render fallback is missing.'
Assert-Contains $service 'ProcessStartTimeUtcTicks' 'Process identity metadata is missing.'
Assert-Contains $service 'MatchesProcessIdentity' 'Window handle identity validation is missing.'
Assert-Contains $service 'TryFindInjectedTargetWindow' 'Injected-process window binding is missing.'
Assert-Contains $service 'CalculateImageStats' 'Capture quality diagnostics are missing.'
Assert-Contains $region 'UseNormalizedCoordinates' 'Normalized region support is missing.'
Assert-Contains $region 'Resolve(Size clientSize)' 'Region scaling resolver is missing.'
Assert-Contains $provider 'MinimumIntervalMilliseconds' 'Capture interval throttling is missing.'
Assert-Contains $provider 'SkipUnchangedFrames' 'Unchanged-frame cache is missing.'
Assert-Contains $provider 'TryResolveWindow' 'Automatic window rebinding is missing.'
Assert-Contains $provider 'lastFrameFingerprint' 'Frame fingerprint caching is missing.'
Assert-Contains $provider 'SaveFailureSnapshot' 'Failure screenshot diagnostics are missing.'
Assert-Contains $provider 'CaptureMilliseconds' 'Capture timing diagnostics are missing.'
Assert-Contains $profile 'CaptureSettings' 'Capture settings are not part of the profile.'
Assert-Contains $form 'pbVisionPreview_MouseDown' 'Preview drag selection is missing.'
Assert-Contains $form 'CreateVisionTemplateFromSelection' 'Template crop is missing.'
Assert-Contains $form 'visionOcrCancellation' 'OCR cancellation is missing.'
Assert-Contains $form 'Vision_TemplateVariantAdded' 'Template variant UI is missing.'
Assert-Contains $form 'Vision_WindowAutoBound' 'Injected-target binding status is missing.'
Assert-Contains $form 'visionHistory' 'Capture history is missing.'
Assert-Contains $form 'bVisionSelectRegion_Click' 'The client-area region picker button is missing.'
Assert-Contains $form 'VisionRegionPickerForm' 'The client-area selection overlay is missing.'
Assert-Contains $form 'this.bCaptureVision_Click(this, EventArgs.Empty);' 'Region selection does not trigger the automatic capture flow.'
Assert-Contains $form 'CalculateVisionSelectionRectangle' 'Reverse-drag selection calculation is missing.'
Assert-Contains $form 'SystemInformation.VirtualScreen.Contains' 'Off-screen target validation is missing.'
Assert-Contains $form 'DialogResult = DialogResult.Cancel' 'Esc cancellation must leave the existing region unchanged.'
Assert-Contains $cache 'TemplateVariants' 'Template variants are not persisted.'
Assert-Contains $cache 'RegionNormalized' 'Normalized regions are not persisted.'
Assert-Contains $cache 'ProcessStartTimeUtcTicks' 'Process identity is not persisted.'
Assert-Contains $region 'ReferenceWidth' 'Normalized region reference dimensions are missing.'
Assert-Contains $cache 'CaptureSaveFailures' 'Failure screenshot settings are not persisted.'
Assert-Contains $ocrOptions 'UseAdaptiveThreshold' 'Adaptive OCR threshold options are missing.'
Assert-Contains $ocrOptions 'CharacterWhitelist' 'OCR character whitelist options are missing.'
Assert-Contains $preprocessor 'ApplyAdaptiveThreshold' 'Adaptive OCR preprocessing is missing.'
Assert-Contains $templateOptions 'AllowScaleVariation' 'Template scale options are missing.'
Assert-Contains $templateMatcher 'NormalizeBrightness' 'Template brightness normalization is missing.'
Assert-Contains $form 'visionMatchCancellation' 'Async cancellable template matching is missing.'
Assert-Contains $observation 'DiagnosticSnapshotPath' 'Observation snapshot diagnostics are missing.'
Assert-Contains $onnx 'InferenceSession' 'ONNX OCR inference provider is missing.'
Assert-Contains $onnx 'dbnet.onnx' 'DBNet model discovery is missing.'
Assert-Contains $onnx 'crnn_lite_lstm.onnx' 'CRNN model discovery is missing.'
Assert-Contains $onnx 'implicitBlankAtZero' 'PaddleOCR CTC blank-class handling is missing.'
Assert-Contains $autoOcr 'OnnxRecognitionThreshold' 'Automatic OCR confidence gate is missing.'
Assert-Contains $autoOcr 'VisionOcrEngine.Tesseract' 'Automatic OCR Tesseract fallback is missing.'
Assert-Contains $autoOcr 'ONNX-LowConfidence' 'Automatic OCR must reject low-confidence ONNX output when fallback is unavailable.'
Assert-Contains $colorMatcher 'LockBits' 'Color matching must use a deterministic pixel scan.'
Assert-Contains $colorCondition 'MinimumMatchRatio' 'Color condition thresholds are missing.'
Assert-Contains $mouseAction 'ColorResult.Bounds' 'Color actions must use the matched color bounds when available.'
Assert-Contains $provider 'ColorAppears' 'Color conditions are not wired into observation capture.'
Assert-Contains $cache 'ColorMinimumRatio' 'Color condition persistence is missing.'
Assert-Contains $cache 'OcrModelDirectory' 'ONNX model configuration persistence is missing.'

Write-Host 'Vision capture enhancements regression passed.'
