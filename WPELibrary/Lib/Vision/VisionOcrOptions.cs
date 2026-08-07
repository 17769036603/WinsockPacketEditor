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

        public VisionOcrEngine Engine { get; set; }

        public string OnnxModelDirectory { get; set; }

        public double OnnxDetectionThreshold { get; set; }

        public double OnnxRecognitionThreshold { get; set; }

        public int OnnxMaxImageSide { get; set; }

        public string PythonExecutablePath { get; set; }

        public string PythonWorkerScriptPath { get; set; }

        public int PythonWorkerTimeoutMilliseconds { get; set; }

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
            this.Engine = VisionOcrEngine.Auto;
            this.OnnxModelDirectory = "models\\ocr";
            this.OnnxDetectionThreshold = 0.3D;
            this.OnnxRecognitionThreshold = 0.5D;
            this.OnnxMaxImageSide = 960;
            this.PythonExecutablePath = string.Empty;
            this.PythonWorkerScriptPath = string.Empty;
            this.PythonWorkerTimeoutMilliseconds = 15000;
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
            if (!Enum.IsDefined(typeof(VisionOcrEngine), this.Engine))
            {
                throw new ArgumentOutOfRangeException("Engine");
            }
            if (this.OnnxDetectionThreshold < 0D || this.OnnxDetectionThreshold > 1D)
            {
                throw new ArgumentOutOfRangeException("OnnxDetectionThreshold");
            }
            if (this.OnnxRecognitionThreshold < 0D || this.OnnxRecognitionThreshold > 1D)
            {
                throw new ArgumentOutOfRangeException("OnnxRecognitionThreshold");
            }
            if (this.OnnxMaxImageSide < 128 || this.OnnxMaxImageSide > 4096)
            {
                throw new ArgumentOutOfRangeException("OnnxMaxImageSide");
            }
            if (this.PythonWorkerTimeoutMilliseconds < 500 ||
                this.PythonWorkerTimeoutMilliseconds > 120000)
            {
                throw new ArgumentOutOfRangeException("PythonWorkerTimeoutMilliseconds");
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
                PageSegmentationMode = this.PageSegmentationMode,
                Engine = this.Engine,
                OnnxModelDirectory = this.OnnxModelDirectory,
                OnnxDetectionThreshold = this.OnnxDetectionThreshold,
                OnnxRecognitionThreshold = this.OnnxRecognitionThreshold,
                OnnxMaxImageSide = this.OnnxMaxImageSide,
                PythonExecutablePath = this.PythonExecutablePath,
                PythonWorkerScriptPath = this.PythonWorkerScriptPath,
                PythonWorkerTimeoutMilliseconds = this.PythonWorkerTimeoutMilliseconds
            };
        }
    }
}
