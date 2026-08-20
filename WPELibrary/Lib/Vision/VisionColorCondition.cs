using System;
using System.Drawing;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionColorCondition
    {
        public byte Red { get; set; }

        public byte Green { get; set; }

        public byte Blue { get; set; }

        public int Tolerance { get; set; }

        public int MinimumPixelCount { get; set; }

        public double MinimumMatchRatio { get; set; }

        public Color TargetColor
        {
            get { return Color.FromArgb(this.Red, this.Green, this.Blue); }
            set
            {
                this.Red = value.R;
                this.Green = value.G;
                this.Blue = value.B;
            }
        }

        public VisionColorCondition()
        {
            this.Red = 255;
            this.Green = 255;
            this.Blue = 255;
            this.Tolerance = 16;
            this.MinimumPixelCount = 10;
            this.MinimumMatchRatio = 0D;
        }

        public void Validate()
        {
            if (this.Tolerance < 0 || this.Tolerance > 255)
            {
                throw new ArgumentOutOfRangeException("Tolerance");
            }
            if (this.MinimumPixelCount < 1 || this.MinimumPixelCount > 100000000)
            {
                throw new ArgumentOutOfRangeException("MinimumPixelCount");
            }
            if (this.MinimumMatchRatio < 0D || this.MinimumMatchRatio > 1D)
            {
                throw new ArgumentOutOfRangeException("MinimumMatchRatio");
            }
        }

        public VisionColorCondition Clone()
        {
            return new VisionColorCondition
            {
                Red = this.Red,
                Green = this.Green,
                Blue = this.Blue,
                Tolerance = this.Tolerance,
                MinimumPixelCount = this.MinimumPixelCount,
                MinimumMatchRatio = this.MinimumMatchRatio
            };
        }
    }
}
