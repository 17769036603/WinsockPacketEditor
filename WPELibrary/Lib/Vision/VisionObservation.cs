namespace WPELibrary.Lib.Vision
{
    public sealed class VisionObservation
    {
        public VisionOcrResult OcrResult { get; set; }

        public VisionMatchResult TemplateResult { get; set; }

        public string Error { get; set; }

        public string CaptureSource { get; set; }

        public string CaptureWarning { get; set; }

        public uint FrameFingerprint { get; set; }

        public long CaptureMilliseconds { get; set; }

        public long EvaluationMilliseconds { get; set; }

        public bool CacheHit { get; set; }

        public double MeanBrightness { get; set; }

        public double Contrast { get; set; }

        public bool IsBlankCapture { get; set; }

        public string DiagnosticSnapshotPath { get; set; }

        public VisionObservation()
        {
            this.Error = string.Empty;
            this.CaptureSource = string.Empty;
            this.CaptureWarning = string.Empty;
            this.DiagnosticSnapshotPath = string.Empty;
        }

        public static VisionObservation FromOcr(VisionOcrResult result)
        {
            return new VisionObservation { OcrResult = result };
        }

        public static VisionObservation FromTemplate(VisionMatchResult result)
        {
            return new VisionObservation { TemplateResult = result };
        }

        public static VisionObservation Failed(string error)
        {
            return new VisionObservation { Error = error ?? string.Empty };
        }
    }
}
