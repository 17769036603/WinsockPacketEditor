using System;
using System.Drawing;

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

        /// <summary>
        /// When enabled, the target client area must have the exact configured size.
        /// Existing profiles keep this disabled for backwards compatibility.
        /// </summary>
        public bool RequireExactClientSize { get; set; }

        public int RequiredClientWidth { get; set; }

        public int RequiredClientHeight { get; set; }

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
            this.RequireExactClientSize = false;
            this.RequiredClientWidth = 0;
            this.RequiredClientHeight = 0;
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
            if (this.RequireExactClientSize &&
                (this.RequiredClientWidth <= 0 || this.RequiredClientHeight <= 0))
            {
                throw new ArgumentOutOfRangeException("RequiredClientSize");
            }
        }

        public bool IsClientSizeMatch(Size clientSize)
        {
            return !this.RequireExactClientSize ||
                (clientSize.Width == this.RequiredClientWidth &&
                 clientSize.Height == this.RequiredClientHeight);
        }

        public string DescribeClientSizeMismatch(Size clientSize)
        {
            return string.Format(
                "目标窗口客户区必须为 {0}×{1}，当前为 {2}×{3}。请恢复雷电模拟器分辨率后再运行助手。",
                this.RequiredClientWidth,
                this.RequiredClientHeight,
                clientSize.Width,
                clientSize.Height);
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
                FailureSnapshotDirectory = this.FailureSnapshotDirectory,
                RequireExactClientSize = this.RequireExactClientSize,
                RequiredClientWidth = this.RequiredClientWidth,
                RequiredClientHeight = this.RequiredClientHeight
            };
        }
    }
}
