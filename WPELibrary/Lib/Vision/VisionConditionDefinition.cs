using System;
using System.Drawing;
using System.Collections.Generic;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionConditionDefinition
    {
        private const int MaxTemplatePixels = 1000000;
        private const int MaxTemplateVariants = 24;
        private const long MaxRegionPixels = 16000000L;
        public string Name { get; set; }

        public VisionConditionType Type { get; set; }

        public VisionRegion Region { get; set; }

        public VisionTextCondition TextCondition { get; set; }

        public VisionColorCondition ColorCondition { get; set; }

        public Bitmap Template { get; set; }

        public List<Bitmap> TemplateVariants { get; private set; }

        public double MinimumSimilarity { get; set; }

        public bool NormalizeTemplateBrightness { get; set; }

        public bool AllowTemplateScaleVariation { get; set; }

        public double TemplateMinimumScale { get; set; }

        public double TemplateMaximumScale { get; set; }

        public double TemplateScaleStep { get; set; }

        public int RequiredConfirmations { get; set; }

        public int PollIntervalMilliseconds { get; set; }

        public int TimeoutMilliseconds { get; set; }

        public int MaxRetries { get; set; }

        public VisionFailurePolicy FailurePolicy { get; set; }

        public VisionConditionDefinition()
        {
            this.Name = string.Empty;
            this.Type = VisionConditionType.TextAppears;
            this.Region = new VisionRegion();
            this.TextCondition = new VisionTextCondition();
            this.ColorCondition = new VisionColorCondition();
            this.MinimumSimilarity = 0.9D;
            this.NormalizeTemplateBrightness = true;
            this.AllowTemplateScaleVariation = false;
            this.TemplateMinimumScale = 0.9D;
            this.TemplateMaximumScale = 1.1D;
            this.TemplateScaleStep = 0.05D;
            this.RequiredConfirmations = 2;
            this.PollIntervalMilliseconds = 150;
            this.TimeoutMilliseconds = 8000;
            this.MaxRetries = 1;
            this.FailurePolicy = VisionFailurePolicy.Stop;
            this.TemplateVariants = new List<Bitmap>();
        }

        public void Validate()
        {
            if (this.Region == null || !this.Region.IsValid)
            {
                throw new ArgumentException("A valid vision condition region is required.", "Region");
            }
            if ((long)this.Region.Width * this.Region.Height > MaxRegionPixels)
            {
                throw new ArgumentException(
                    "The vision condition region is too large; reduce it before recognition.",
                    "Region");
            }
            if (this.RequiredConfirmations < 1 || this.RequiredConfirmations > 10)
            {
                throw new ArgumentOutOfRangeException("RequiredConfirmations");
            }
            if (this.PollIntervalMilliseconds < 10 || this.PollIntervalMilliseconds > 60000)
            {
                throw new ArgumentOutOfRangeException("PollIntervalMilliseconds");
            }
            if (this.TimeoutMilliseconds < this.PollIntervalMilliseconds ||
                this.TimeoutMilliseconds > 3600000)
            {
                throw new ArgumentOutOfRangeException("TimeoutMilliseconds");
            }
            if (this.MaxRetries < 0 || this.MaxRetries > 100)
            {
                throw new ArgumentOutOfRangeException("MaxRetries");
            }
            if (this.MinimumSimilarity < 0D || this.MinimumSimilarity > 1D)
            {
                throw new ArgumentOutOfRangeException("MinimumSimilarity");
            }
            VisionTemplateMatchOptions templateOptions = new VisionTemplateMatchOptions
            {
                NormalizeBrightness = this.NormalizeTemplateBrightness,
                AllowScaleVariation = this.AllowTemplateScaleVariation,
                MinimumScale = this.TemplateMinimumScale,
                MaximumScale = this.TemplateMaximumScale,
                ScaleStep = this.TemplateScaleStep
            };
            templateOptions.Validate();
            if ((this.Type == VisionConditionType.TemplateAppears ||
                 this.Type == VisionConditionType.TemplateDisappears) &&
                !HasUsableTemplate())
            {
                throw new ArgumentException("A template is required for an image condition.", "Template");
            }
            if (this.TemplateVariants != null && this.TemplateVariants.Count > MaxTemplateVariants)
            {
                throw new ArgumentException(
                    "Too many template variants are configured; reduce the variant count.",
                    "TemplateVariants");
            }
            ValidateTemplateSize(this.Template, "Template");
            if (this.TemplateVariants != null)
            {
                for (int index = 0; index < this.TemplateVariants.Count; index++)
                {
                    ValidateTemplateSize(this.TemplateVariants[index], "TemplateVariants");
                }
            }
            if ((this.Type == VisionConditionType.TextAppears ||
                 this.Type == VisionConditionType.TextDisappears ||
                 this.Type == VisionConditionType.NumberInRange) &&
                this.TextCondition == null)
            {
                throw new ArgumentException("A text condition is required for an OCR condition.", "TextCondition");
            }
            if (this.Type == VisionConditionType.NumberInRange &&
                this.TextCondition.MinimumNumber > this.TextCondition.MaximumNumber)
            {
                throw new ArgumentException("The minimum number cannot exceed the maximum number.", "TextCondition");
            }
            if ((this.Type == VisionConditionType.TextAppears ||
                 this.Type == VisionConditionType.TextDisappears) &&
                 string.IsNullOrWhiteSpace(this.TextCondition.ExpectedText))
            {
                throw new ArgumentException("An OCR keyword is required for a text condition.", "TextCondition");
            }
            if ((this.Type == VisionConditionType.ColorAppears ||
                 this.Type == VisionConditionType.ColorDisappears) &&
                this.ColorCondition == null)
            {
                throw new ArgumentException("A color condition is required for a color condition.", "ColorCondition");
            }
            if (this.Type == VisionConditionType.ColorAppears ||
                this.Type == VisionConditionType.ColorDisappears)
            {
                this.ColorCondition.Validate();
            }
        }

        private static void ValidateTemplateSize(Bitmap template, string parameterName)
        {
            if (template != null && (long)template.Width * template.Height > MaxTemplatePixels)
            {
                throw new ArgumentException(
                    "A template is too large; reduce its pixel area before recognition.",
                    parameterName);
            }
        }

        public bool FitsWithin(Size clientSize, out string error)
        {
            error = string.Empty;
            try
            {
                this.Validate();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (!this.Region.FitsWithin(clientSize))
            {
                error = string.Format(
                    "The region ({0},{1},{2}x{3}) is outside the client area ({4}x{5}).",
                    this.Region.X,
                    this.Region.Y,
                    this.Region.Width,
                    this.Region.Height,
                    clientSize.Width,
                    clientSize.Height);
                return false;
            }
            return true;
        }

        public VisionConditionDefinition Clone()
        {
            VisionConditionDefinition clone = new VisionConditionDefinition
            {
                Name = this.Name,
                Type = this.Type,
                Region = this.Region == null ? new VisionRegion() : this.Region.Clone(),
                TextCondition = this.TextCondition == null ? null : this.TextCondition.Clone(),
                ColorCondition = this.ColorCondition == null ? null : this.ColorCondition.Clone(),
                Template = this.Template == null ? null : new Bitmap(this.Template),
                MinimumSimilarity = this.MinimumSimilarity,
                NormalizeTemplateBrightness = this.NormalizeTemplateBrightness,
                AllowTemplateScaleVariation = this.AllowTemplateScaleVariation,
                TemplateMinimumScale = this.TemplateMinimumScale,
                TemplateMaximumScale = this.TemplateMaximumScale,
                TemplateScaleStep = this.TemplateScaleStep,
                RequiredConfirmations = this.RequiredConfirmations,
                PollIntervalMilliseconds = this.PollIntervalMilliseconds,
                TimeoutMilliseconds = this.TimeoutMilliseconds,
                MaxRetries = this.MaxRetries,
                FailurePolicy = this.FailurePolicy
            };
            if (this.TemplateVariants != null)
            {
                foreach (Bitmap variant in this.TemplateVariants)
                {
                    if (variant != null)
                    {
                        clone.TemplateVariants.Add(new Bitmap(variant));
                    }
                }
            }
            return clone;
        }

        public void ReplaceTemplateVariants(IEnumerable<Bitmap> variants)
        {
            this.DisposeTemplateVariants();
            if (variants == null)
            {
                return;
            }
            foreach (Bitmap variant in variants)
            {
                if (variant != null)
                {
                    this.TemplateVariants.Add(new Bitmap(variant));
                }
            }
        }

        private bool HasUsableTemplate()
        {
            if (this.Template != null && this.Template.Width > 0 && this.Template.Height > 0)
            {
                return true;
            }
            if (this.TemplateVariants == null)
            {
                return false;
            }
            foreach (Bitmap variant in this.TemplateVariants)
            {
                if (variant != null && variant.Width > 0 && variant.Height > 0)
                {
                    return true;
                }
            }
            return false;
        }

        public void DisposeTemplateVariants()
        {
            if (this.TemplateVariants != null)
            {
                foreach (Bitmap variant in this.TemplateVariants)
                {
                    if (variant != null)
                    {
                        variant.Dispose();
                    }
                }
                this.TemplateVariants.Clear();
            }
        }
    }
}
