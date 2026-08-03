using System;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionOcrResult
    {
        private VisionOcrResult(
            bool success,
            bool available,
            bool cancelled,
            string text,
            double confidence,
            string error,
            int exitCode)
        {
            this.Success = success;
            this.Available = available;
            this.Cancelled = cancelled;
            this.Text = text ?? string.Empty;
            this.Confidence = Math.Max(0D, Math.Min(1D, confidence));
            this.Error = error ?? string.Empty;
            this.ExitCode = exitCode;
        }

        public bool Success { get; private set; }

        public bool Available { get; private set; }

        public bool Cancelled { get; private set; }

        public string Text { get; private set; }

        public double Confidence { get; private set; }

        public string Error { get; private set; }

        public int ExitCode { get; private set; }

        public static VisionOcrResult Succeeded(string text, double confidence)
        {
            return new VisionOcrResult(true, true, false, text, confidence, string.Empty, 0);
        }

        public static VisionOcrResult Failed(string error, string text, double confidence, int exitCode)
        {
            return new VisionOcrResult(false, true, false, text, confidence, error, exitCode);
        }

        public static VisionOcrResult Unavailable(string error)
        {
            return new VisionOcrResult(false, false, false, string.Empty, 0D, error, -1);
        }

        public static VisionOcrResult CancelledResult()
        {
            return new VisionOcrResult(false, true, true, string.Empty, 0D, "OCR cancelled.", -1);
        }
    }
}
