using System;
using System.Drawing;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionColorMatchResult
    {
        private VisionColorMatchResult(
            bool found,
            bool cancelled,
            int matchCount,
            double matchRatio,
            double meanDistance,
            Rectangle bounds)
        {
            this.Found = found;
            this.Cancelled = cancelled;
            this.MatchCount = Math.Max(0, matchCount);
            this.MatchRatio = Math.Max(0D, Math.Min(1D, matchRatio));
            this.MeanDistance = Math.Max(0D, meanDistance);
            this.Bounds = bounds;
        }

        public bool Found { get; private set; }

        public bool Cancelled { get; private set; }

        public int MatchCount { get; private set; }

        public double MatchRatio { get; private set; }

        public double MeanDistance { get; private set; }

        public Rectangle Bounds { get; private set; }

        public static VisionColorMatchResult Create(
            bool found,
            int matchCount,
            double matchRatio,
            double meanDistance,
            Rectangle bounds)
        {
            return new VisionColorMatchResult(
                found,
                false,
                matchCount,
                matchRatio,
                meanDistance,
                bounds);
        }

        public static VisionColorMatchResult CancelledResult()
        {
            return new VisionColorMatchResult(false, true, 0, 0D, 0D, Rectangle.Empty);
        }
    }
}
