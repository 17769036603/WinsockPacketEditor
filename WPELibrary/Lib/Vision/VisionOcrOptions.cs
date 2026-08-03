using System;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionOcrOptions
    {
        public int ScaleFactor { get; set; }

        public bool ConvertToGrayscale { get; set; }

        public bool UseBinaryThreshold { get; set; }

        public byte BinaryThreshold { get; set; }

        public double Contrast { get; set; }

        public bool UseAdaptiveThreshold { get; set; }

        public int AdaptiveThresholdWindowSize { get; set; }

        public int AdaptiveThresholdOffset { get; set; }

        public bool Invert { get; set; }

        public bool UseDenoise { get; set; }

        public bool UseSharpen { get; set; }

        public string CharacterWhitelist { get; set; }

        public string CharacterBlacklist { get; set; }

        public string Language { get; set; }

        public string ExecutablePath { get; set; }

        public string TessdataPath { get; set; }

        public int TimeoutMilliseconds { get; set; }

        public int PageSegmentationMode { get; set; }

        public VisionOcrOptions()
        {
            this.ScaleFactor = 2;
            this.ConvertToGrayscale = true;
            this.UseBinaryThreshold = false;
            this.BinaryThreshold = 160;
            this.Contrast = 1D;
            this.UseAdaptiveThreshold = false;
            this.AdaptiveThresholdWindowSize = 15;
            this.AdaptiveThresholdOffset = 8;
            this.Invert = false;
            this.UseDenoise = false;
            this.UseSharpen = false;
            this.CharacterWhitelist = string.Empty;
            this.CharacterBlacklist = string.Empty;
            this.Language = "chi_sim+eng";
            this.ExecutablePath = "tesseract.exe";
            this.TessdataPath = string.Empty;
            this.TimeoutMilliseconds = 5000;
            this.PageSegmentationMode = 6;
        }

        public void Validate()
        {
            if (this.ScaleFactor < 1 || this.ScaleFactor > 4)
            {
                throw new ArgumentOutOfRangeException("ScaleFactor");
            }
            if (this.Contrast < 0.1D || this.Contrast > 4D)
            {
                throw new ArgumentOutOfRangeException("Contrast");
            }
            if (this.AdaptiveThresholdWindowSize < 3 ||
                this.AdaptiveThresholdWindowSize > 51 ||
                this.AdaptiveThresholdWindowSize % 2 == 0)
            {
                throw new ArgumentOutOfRangeException("AdaptiveThresholdWindowSize");
            }
            if (this.AdaptiveThresholdOffset < -64 || this.AdaptiveThresholdOffset > 64)
            {
                throw new ArgumentOutOfRangeException("AdaptiveThresholdOffset");
            }
            if ((this.CharacterWhitelist ?? string.Empty).Length > 1024)
            {
                throw new ArgumentOutOfRangeException("CharacterWhitelist");
            }
            if ((this.CharacterBlacklist ?? string.Empty).Length > 1024)
            {
                throw new ArgumentOutOfRangeException("CharacterBlacklist");
            }
            if (string.IsNullOrWhiteSpace(this.Language))
            {
                throw new ArgumentException("An OCR language is required.", "Language");
            }
            if (this.TimeoutMilliseconds < 100 || this.TimeoutMilliseconds > 120000)
            {
                throw new ArgumentOutOfRangeException("TimeoutMilliseconds");
            }
            if (this.PageSegmentationMode < 0 || this.PageSegmentationMode > 13)
            {
                throw new ArgumentOutOfRangeException("PageSegmentationMode");
            }
        }

        public VisionOcrOptions Clone()
        {
            return new VisionOcrOptions
            {
                ScaleFactor = this.ScaleFactor,
                ConvertToGrayscale = this.ConvertToGrayscale,
                UseBinaryThreshold = this.UseBinaryThreshold,
                BinaryThreshold = this.BinaryThreshold,
                Contrast = this.Contrast,
                UseAdaptiveThreshold = this.UseAdaptiveThreshold,
                AdaptiveThresholdWindowSize = this.AdaptiveThresholdWindowSize,
                AdaptiveThresholdOffset = this.AdaptiveThresholdOffset,
                Invert = this.Invert,
                UseDenoise = this.UseDenoise,
                UseSharpen = this.UseSharpen,
                CharacterWhitelist = this.CharacterWhitelist,
                CharacterBlacklist = this.CharacterBlacklist,
                Language = this.Language,
                ExecutablePath = this.ExecutablePath,
                TessdataPath = this.TessdataPath,
                TimeoutMilliseconds = this.TimeoutMilliseconds,
                PageSegmentationMode = this.PageSegmentationMode
            };
        }
    }
}
