using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Threading;
using WPELibrary.Lib;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionWindowObservationProvider : IVisionObservationProvider
    {
        private readonly IntPtr windowHandle;
        private readonly IVisionTextRecognizer textRecognizer;
        private readonly VisionOcrOptions ocrOptions;
        private readonly Socket_VisionProfile profile;
        private readonly VisionCaptureSettings captureSettings;
        private DateTime lastObservationUtc;
        private string lastConditionKey;
        private uint lastFrameFingerprint;
        private VisionObservation cachedObservation;

        public VisionWindowObservationProvider(
            IntPtr windowHandle,
            IVisionTextRecognizer textRecognizer)
            : this(windowHandle, textRecognizer, new VisionOcrOptions())
        {
        }

        public VisionWindowObservationProvider(
            IntPtr windowHandle,
            IVisionTextRecognizer textRecognizer,
            VisionOcrOptions ocrOptions)
        {
            if (windowHandle == IntPtr.Zero)
            {
                throw new ArgumentException("A target window handle is required.", "windowHandle");
            }

            this.windowHandle = windowHandle;
            this.textRecognizer = textRecognizer;
            this.ocrOptions = ocrOptions == null
                ? new VisionOcrOptions()
                : ocrOptions.Clone();
            this.captureSettings = new VisionCaptureSettings();
        }

        public VisionWindowObservationProvider(
            Socket_VisionProfile profile,
            IVisionTextRecognizer textRecognizer)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }

            this.profile = profile;
            this.windowHandle = new IntPtr(profile.WindowHandle);
            this.textRecognizer = textRecognizer;
            this.ocrOptions = profile.OcrOptions == null
                ? new VisionOcrOptions()
                : profile.OcrOptions.Clone();
            this.captureSettings = profile.CaptureSettings == null
                ? new VisionCaptureSettings()
                : profile.CaptureSettings.Clone();
        }

        public VisionObservation Observe(
            VisionConditionDefinition condition,
            CancellationToken cancellationToken)
        {
            if (condition == null)
            {
                return VisionObservation.Failed("No vision condition was configured.");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return VisionObservation.Failed("Observation cancelled.");
            }

            try
            {
                IntPtr targetHandle = this.windowHandle;
                if (this.profile != null)
                {
                    VisionWindowInfo resolved;
                    string resolveReason;
                    if (!VisionWindowService.TryResolveWindow(
                        new IntPtr(this.profile.WindowHandle),
                        this.profile.ProcessId,
                        this.profile.ProcessName,
                        this.profile.ProcessPath,
                        this.profile.ProcessStartTimeUtcTicks,
                        this.profile.WindowTitle,
                        out resolved,
                        out resolveReason))
                    {
                        return VisionObservation.Failed(resolveReason);
                    }
                    targetHandle = resolved.Handle;
                    this.profile.WindowHandle = resolved.Handle.ToInt64();
                    this.profile.ProcessId = resolved.ProcessId;
                    this.profile.ProcessName = resolved.ProcessName;
                    this.profile.ProcessPath = resolved.ProcessPath;
                    this.profile.ProcessStartTimeUtcTicks = resolved.ProcessStartTimeUtcTicks;
                    this.profile.WindowTitle = resolved.WindowTitle;
                }

                string conditionKey = targetHandle.ToInt64().ToString() + "|" + BuildConditionKey(condition);
                DateTime now = DateTime.UtcNow;
                if (this.cachedObservation != null &&
                    string.Equals(this.lastConditionKey, conditionKey, StringComparison.Ordinal) &&
                    (now - this.lastObservationUtc).TotalMilliseconds < this.captureSettings.MinimumIntervalMilliseconds)
                {
                    return CopyObservation(this.cachedObservation, true);
                }

                Stopwatch captureStopwatch = Stopwatch.StartNew();
                using (VisionCaptureResult captureResult = VisionWindowService.CaptureClientRegionDetailed(
                    targetHandle,
                    condition.Region,
                    this.captureSettings))
                using (Bitmap capture = captureResult.Image == null ? null : new Bitmap(captureResult.Image))
                {
                    captureStopwatch.Stop();
                    if (this.cachedObservation != null &&
                        this.captureSettings.SkipUnchangedFrames &&
                        string.Equals(this.lastConditionKey, conditionKey, StringComparison.Ordinal) &&
                        captureResult.Fingerprint == this.lastFrameFingerprint)
                    {
                        this.lastObservationUtc = now;
                        VisionObservation unchanged = CopyObservation(this.cachedObservation, true);
                        unchanged.CaptureMilliseconds = captureStopwatch.ElapsedMilliseconds;
                        unchanged.MeanBrightness = captureResult.MeanBrightness;
                        unchanged.Contrast = captureResult.Contrast;
                        unchanged.IsBlankCapture = captureResult.IsBlank;
                        unchanged.CaptureWarning = captureResult.Warning;
                        return unchanged;
                    }

                    Stopwatch evaluationStopwatch = Stopwatch.StartNew();
                    VisionObservation observation;
                    switch (condition.Type)
                    {
                        case VisionConditionType.TextAppears:
                        case VisionConditionType.TextDisappears:
                        case VisionConditionType.NumberInRange:
                            if (this.textRecognizer == null)
                            {
                                observation = VisionObservation.Failed("No OCR recognizer is configured.");
                                break;
                            }
                            observation = VisionObservation.FromOcr(
                                this.textRecognizer.Recognize(
                                    capture,
                                    this.ocrOptions,
                                    cancellationToken));
                            break;

                        case VisionConditionType.TemplateAppears:
                        case VisionConditionType.TemplateDisappears:
                            observation = VisionObservation.FromTemplate(
                                VisionTemplateMatcher.FindBestMatch(
                                    capture,
                                    GetTemplates(condition),
                                    condition.MinimumSimilarity,
                                    BuildTemplateMatchOptions(condition),
                                    cancellationToken));
                            break;

                        default:
                            observation = VisionObservation.Failed("Unsupported vision condition type.");
                            break;
                    }
                    evaluationStopwatch.Stop();
                    observation.CaptureSource = captureResult.SourceMode.ToString();
                    observation.CaptureWarning = captureResult.Warning;
                    observation.FrameFingerprint = captureResult.Fingerprint;
                    observation.CaptureMilliseconds = captureStopwatch.ElapsedMilliseconds;
                    observation.EvaluationMilliseconds = evaluationStopwatch.ElapsedMilliseconds;
                    observation.MeanBrightness = captureResult.MeanBrightness;
                    observation.Contrast = captureResult.Contrast;
                    observation.IsBlankCapture = captureResult.IsBlank;
                    observation.CacheHit = false;
                    if (this.captureSettings.SaveFailureSnapshots &&
                        IsFailedObservation(observation))
                    {
                        observation.DiagnosticSnapshotPath = SaveFailureSnapshot(
                            capture,
                            condition,
                            captureResult,
                            this.captureSettings.FailureSnapshotDirectory);
                    }
                    this.lastObservationUtc = now;
                    this.lastConditionKey = conditionKey;
                    this.lastFrameFingerprint = captureResult.Fingerprint;
                    this.cachedObservation = observation;
                    return observation;
                }
            }
            catch (Exception ex)
            {
                return VisionObservation.Failed(ex.Message);
            }
        }

        private static IEnumerable<Bitmap> GetTemplates(VisionConditionDefinition condition)
        {
            List<Bitmap> templates = new List<Bitmap>();
            if (condition.Template != null)
            {
                templates.Add(condition.Template);
            }
            if (condition.TemplateVariants != null)
            {
                templates.AddRange(condition.TemplateVariants);
            }
            return templates;
        }

        private static string BuildConditionKey(VisionConditionDefinition condition)
        {
            return string.Format(
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|{12}",
                condition.Type,
                condition.Region == null ? string.Empty : condition.Region.ToString(),
                condition.TextCondition == null ? string.Empty : condition.TextCondition.ExpectedText,
                condition.Template == null ? string.Empty : condition.Template.Size.ToString(),
                condition.TemplateVariants == null ? 0 : condition.TemplateVariants.Count,
                condition.MinimumSimilarity,
                condition.NormalizeTemplateBrightness,
                condition.AllowTemplateScaleVariation,
                condition.TemplateMinimumScale,
                condition.TemplateMaximumScale,
                condition.TemplateScaleStep,
                CalculateBitmapFingerprint(condition.Template),
                CalculateTemplateVariantFingerprint(condition));
        }

        private static string CalculateTemplateVariantFingerprint(VisionConditionDefinition condition)
        {
            if (condition == null || condition.TemplateVariants == null ||
                condition.TemplateVariants.Count == 0)
            {
                return string.Empty;
            }

            string fingerprints = string.Empty;
            foreach (Bitmap variant in condition.TemplateVariants)
            {
                fingerprints += CalculateBitmapFingerprint(variant).ToString("X8") + ",";
            }
            return fingerprints;
        }

        private static uint CalculateBitmapFingerprint(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return 0U;
            }

            uint fingerprint = 2166136261U;
            int stepX = Math.Max(1, bitmap.Width / 32);
            int stepY = Math.Max(1, bitmap.Height / 32);
            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    fingerprint ^= unchecked((uint)bitmap.GetPixel(x, y).ToArgb());
                    fingerprint *= 16777619U;
                }
            }
            return fingerprint;
        }

        private static VisionObservation CopyObservation(
            VisionObservation source,
            bool cacheHit)
        {
            return new VisionObservation
            {
                OcrResult = source.OcrResult,
                TemplateResult = source.TemplateResult,
                Error = source.Error,
                CaptureSource = source.CaptureSource,
                CaptureWarning = source.CaptureWarning,
                FrameFingerprint = source.FrameFingerprint,
                CaptureMilliseconds = source.CaptureMilliseconds,
                EvaluationMilliseconds = source.EvaluationMilliseconds,
                CacheHit = cacheHit,
                MeanBrightness = source.MeanBrightness,
                Contrast = source.Contrast,
                IsBlankCapture = source.IsBlankCapture,
                DiagnosticSnapshotPath = source.DiagnosticSnapshotPath
            };
        }

        private static bool IsFailedObservation(VisionObservation observation)
        {
            return observation != null &&
                (!string.IsNullOrWhiteSpace(observation.Error) ||
                 (observation.OcrResult != null && !observation.OcrResult.Success) ||
                 (observation.TemplateResult != null && !observation.TemplateResult.Found));
        }

        private static string SaveFailureSnapshot(
            Bitmap capture,
            VisionConditionDefinition condition,
            VisionCaptureResult captureResult,
            string configuredDirectory)
        {
            try
            {
                string directory = configuredDirectory;
                if (string.IsNullOrWhiteSpace(directory))
                {
                    directory = Environment.GetEnvironmentVariable("WPE_VISION_DIAGNOSTIC_DIR");
                }
                if (string.IsNullOrWhiteSpace(directory))
                {
                    directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WPE",
                        "VisionDiagnostics");
                }
                Directory.CreateDirectory(directory);
                string safeType = condition == null
                    ? "unknown"
                    : condition.Type.ToString();
                string fileName = string.Format(
                    "{0:yyyyMMdd_HHmmss_fff}_{1}_{2:X8}_{3}.png",
                    DateTime.Now,
                    safeType,
                    captureResult == null ? 0U : captureResult.Fingerprint,
                    Guid.NewGuid().ToString("N").Substring(0, 8));
                string path = Path.Combine(directory, fileName);
                VisionWindowService.SavePng(capture, path);
                return path;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static VisionTemplateMatchOptions BuildTemplateMatchOptions(
            VisionConditionDefinition condition)
        {
            return new VisionTemplateMatchOptions
            {
                NormalizeBrightness = condition.NormalizeTemplateBrightness,
                AllowScaleVariation = condition.AllowTemplateScaleVariation,
                MinimumScale = condition.TemplateMinimumScale,
                MaximumScale = condition.TemplateMaximumScale,
                ScaleStep = condition.TemplateScaleStep
            };
        }
    }
}
