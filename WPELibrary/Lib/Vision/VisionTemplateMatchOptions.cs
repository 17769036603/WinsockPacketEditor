using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionTemplateMatchOptions
    {
        public bool NormalizeBrightness { get; set; }

        public bool AllowScaleVariation { get; set; }

        public double MinimumScale { get; set; }

        public double MaximumScale { get; set; }

        public double ScaleStep { get; set; }

        public VisionTemplateMatchOptions()
        {
            this.NormalizeBrightness = true;
            this.AllowScaleVariation = false;
            this.MinimumScale = 0.9D;
            this.MaximumScale = 1.1D;
            this.ScaleStep = 0.05D;
        }

        public void Validate()
        {
            if (this.MinimumScale < 0.5D || this.MinimumScale > 2D)
            {
                throw new ArgumentOutOfRangeException("MinimumScale");
            }
            if (this.MaximumScale < 0.5D || this.MaximumScale > 2D ||
                this.MaximumScale < this.MinimumScale)
            {
                throw new ArgumentOutOfRangeException("MaximumScale");
            }
            if (this.ScaleStep < 0.01D || this.ScaleStep > 0.5D)
            {
                throw new ArgumentOutOfRangeException("ScaleStep");
            }
            int estimatedScaleCount = (int)Math.Ceiling(
                (this.MaximumScale - this.MinimumScale) / this.ScaleStep) + 1;
            if (this.AllowScaleVariation && estimatedScaleCount > 64)
            {
                throw new ArgumentException(
                    "The template scale range produces too many variants; increase the scale step.",
                    "ScaleStep");
            }
        }

        public IEnumerable<double> GetScales()
        {
            List<double> scales = new List<double>();
            if (!this.AllowScaleVariation)
            {
                scales.Add(1D);
                return scales;
            }

            for (double scale = this.MinimumScale;
                 scale <= this.MaximumScale + this.ScaleStep / 2D;
                 scale += this.ScaleStep)
            {
                double rounded = Math.Round(scale, 4);
                if (rounded >= 0.5D && rounded <= 2D && !scales.Contains(rounded))
                {
                    scales.Add(rounded);
                }
            }
            if (!scales.Contains(1D))
            {
                scales.Add(1D);
            }
            scales.Sort();
            return scales;
        }

        public VisionTemplateMatchOptions Clone()
        {
            return new VisionTemplateMatchOptions
            {
                NormalizeBrightness = this.NormalizeBrightness,
                AllowScaleVariation = this.AllowScaleVariation,
                MinimumScale = this.MinimumScale,
                MaximumScale = this.MaximumScale,
                ScaleStep = this.ScaleStep
            };
        }
    }
}
