using System;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionCaptureSettings
    {
        public VisionCaptureSourceMode SourceMode { get; set; }

        public int MinimumIntervalMilliseconds { get; set; }

        public bool SkipUnchangedFrames { get; set; }

        public int HistoryLimit { get; set; }

        public double BlankBrightnessThreshold { get; set; }

        public double BlankContrastThreshold { get; set; }

        public bool SaveFailureSnapshots { get; set; }

        public string FailureSnapshotDirectory { get; set; }

        public VisionCaptureSettings()
        {
            this.SourceMode = VisionCaptureSourceMode.Auto;
            this.MinimumIntervalMilliseconds = 150;
            this.SkipUnchangedFrames = true;
            this.HistoryLimit = 30;
            this.BlankBrightnessThreshold = 3D;
            this.BlankContrastThreshold = 2D;
            this.SaveFailureSnapshots = false;
            this.FailureSnapshotDirectory = string.Empty;
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(VisionCaptureSourceMode), this.SourceMode))
            {
                throw new ArgumentOutOfRangeException("SourceMode");
            }
            if (this.MinimumIntervalMilliseconds < 0 || this.MinimumIntervalMilliseconds > 60000)
            {
                throw new ArgumentOutOfRangeException("MinimumIntervalMilliseconds");
            }
            if (this.HistoryLimit < 0 || this.HistoryLimit > 200)
            {
                throw new ArgumentOutOfRangeException("HistoryLimit");
            }
            if (this.BlankBrightnessThreshold < 0D || this.BlankBrightnessThreshold > 255D)
            {
                throw new ArgumentOutOfRangeException("BlankBrightnessThreshold");
            }
            if (this.BlankContrastThreshold < 0D || this.BlankContrastThreshold > 255D)
            {
                throw new ArgumentOutOfRangeException("BlankContrastThreshold");
            }
            if ((this.FailureSnapshotDirectory ?? string.Empty).Length > 2048)
            {
                throw new ArgumentOutOfRangeException("FailureSnapshotDirectory");
            }
        }

        public VisionCaptureSettings Clone()
        {
            return new VisionCaptureSettings
            {
                SourceMode = this.SourceMode,
                MinimumIntervalMilliseconds = this.MinimumIntervalMilliseconds,
                SkipUnchangedFrames = this.SkipUnchangedFrames,
                HistoryLimit = this.HistoryLimit,
                BlankBrightnessThreshold = this.BlankBrightnessThreshold,
                BlankContrastThreshold = this.BlankContrastThreshold,
                SaveFailureSnapshots = this.SaveFailureSnapshots,
                FailureSnapshotDirectory = this.FailureSnapshotDirectory
            };
        }
    }
}
