using System;
using System.Collections.Generic;

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

        public IList<VisionOcrTextBox> TextBoxes { get; private set; }

        public static VisionOcrResult Succeeded(string text, double confidence)
        {
            return Succeeded(text, confidence, null);
        }

        public static VisionOcrResult Succeeded(
            string text,
            double confidence,
            IList<VisionOcrTextBox> textBoxes)
        {
            VisionOcrResult result = new VisionOcrResult(true, true, false, text, confidence, string.Empty, 0);
            result.TextBoxes = textBoxes ?? new List<VisionOcrTextBox>();
            return result;
        }

        public static VisionOcrResult Failed(string error, string text, double confidence, int exitCode)
        {
            VisionOcrResult result = new VisionOcrResult(false, true, false, text, confidence, error, exitCode);
            result.TextBoxes = new List<VisionOcrTextBox>();
            return result;
        }

        public static VisionOcrResult Unavailable(string error)
        {
            VisionOcrResult result = new VisionOcrResult(false, false, false, string.Empty, 0D, error, -1);
            result.TextBoxes = new List<VisionOcrTextBox>();
            return result;
        }

        public static VisionOcrResult CancelledResult()
        {
            VisionOcrResult result = new VisionOcrResult(false, true, true, string.Empty, 0D, "OCR cancelled.", -1);
            result.TextBoxes = new List<VisionOcrTextBox>();
            return result;
        }
    }
}
