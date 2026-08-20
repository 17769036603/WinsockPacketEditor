using System;
using System.Drawing;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionCaptureResult : IDisposable
    {
        public Bitmap Image { get; private set; }

        public VisionCaptureSourceMode SourceMode { get; private set; }

        public double MeanBrightness { get; private set; }

        public double Contrast { get; private set; }

        public bool IsBlank { get; private set; }

        public uint Fingerprint { get; private set; }

        public string Warning { get; private set; }

        public DateTime CapturedAtUtc { get; private set; }

        public VisionCaptureResult(
            Bitmap image,
            VisionCaptureSourceMode sourceMode,
            double meanBrightness,
            double contrast,
            bool isBlank,
            uint fingerprint,
            string warning)
        {
            this.Image = image;
            this.SourceMode = sourceMode;
            this.MeanBrightness = meanBrightness;
            this.Contrast = contrast;
            this.IsBlank = isBlank;
            this.Fingerprint = fingerprint;
            this.Warning = warning ?? string.Empty;
            this.CapturedAtUtc = DateTime.UtcNow;
        }

        public void Dispose()
        {
            if (this.Image != null)
            {
                this.Image.Dispose();
                this.Image = null;
            }
        }
    }
}
