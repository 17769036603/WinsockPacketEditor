using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WPELibrary.Lib.Vision
{
    public enum VisionTextMatchMode
    {
        Contains = 0,
        Exact = 1,
        NumberRange = 2
    }

    public sealed class VisionTextCondition
    {
        public VisionTextMatchMode MatchMode { get; set; }

        public string ExpectedText { get; set; }

        public double MinimumNumber { get; set; }

        public double MaximumNumber { get; set; }

        public double MinimumConfidence { get; set; }

        public VisionTextCondition()
        {
            this.MatchMode = VisionTextMatchMode.Contains;
            this.ExpectedText = string.Empty;
            this.MinimumNumber = double.MinValue;
            this.MaximumNumber = double.MaxValue;
            this.MinimumConfidence = 0.5D;
        }

        public bool Matches(VisionOcrResult result, out string reason)
        {
            reason = string.Empty;
            if (result == null)
            {
                reason = "No OCR result.";
                return false;
            }
            if (!result.Success)
            {
                reason = string.IsNullOrEmpty(result.Error) ? "OCR failed." : result.Error;
                return false;
            }
            if (result.Confidence < this.MinimumConfidence)
            {
                reason = "OCR confidence is below the configured threshold.";
                return false;
            }

            string actual = (result.Text ?? string.Empty).Trim();
            string expected = (this.ExpectedText ?? string.Empty).Trim();
            switch (this.MatchMode)
            {
                case VisionTextMatchMode.Exact:
                    if (string.IsNullOrEmpty(expected))
                    {
                        reason = "An exact OCR keyword is required.";
                        return false;
                    }
                    return string.Equals(
                        NormalizeForMatch(actual),
                        NormalizeForMatch(expected),
                        StringComparison.OrdinalIgnoreCase);

                case VisionTextMatchMode.NumberRange:
                    double number;
                    if (!TryReadNumber(actual, out number))
                    {
                        reason = "OCR text does not contain a number.";
                        return false;
                    }
                    return number >= this.MinimumNumber && number <= this.MaximumNumber;

                case VisionTextMatchMode.Contains:
                default:
                    if (string.IsNullOrEmpty(expected))
                    {
                        reason = "An OCR keyword is required.";
                        return false;
                    }
                    return NormalizeForMatch(actual).IndexOf(
                        NormalizeForMatch(expected),
                        StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public VisionTextCondition Clone()
        {
            return new VisionTextCondition
            {
                MatchMode = this.MatchMode,
                ExpectedText = this.ExpectedText,
                MinimumNumber = this.MinimumNumber,
                MaximumNumber = this.MaximumNumber,
                MinimumConfidence = this.MinimumConfidence
            };
        }

        private static bool TryReadNumber(string text, out double number)
        {
            Match match = Regex.Match(text ?? string.Empty, @"[-+]?\d+(?:[.,]\d+)?");
            if (!match.Success)
            {
                number = 0D;
                return false;
            }

            string normalized = match.Value.Replace(',', '.');
            return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number);
        }

        private static string NormalizeForMatch(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", string.Empty);
        }
    }
}
