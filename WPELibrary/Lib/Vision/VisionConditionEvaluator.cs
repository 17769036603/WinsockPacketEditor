using System;

namespace WPELibrary.Lib.Vision
{
    public static class VisionConditionEvaluator
    {
        public static bool Matches(
            VisionConditionDefinition condition,
            VisionObservation observation,
            out string reason)
        {
            reason = string.Empty;
            if (condition == null)
            {
                reason = "No condition was configured.";
                return false;
            }
            if (observation == null)
            {
                reason = "No vision observation was returned.";
                return false;
            }
            if (!string.IsNullOrEmpty(observation.Error))
            {
                reason = observation.Error;
                return false;
            }

            switch (condition.Type)
            {
                case VisionConditionType.TextAppears:
                    return condition.TextCondition.Matches(observation.OcrResult, out reason);

                case VisionConditionType.TextDisappears:
                    if (observation.OcrResult == null ||
                        observation.OcrResult.Cancelled ||
                        !observation.OcrResult.Available)
                    {
                        reason = "OCR result is not usable for a disappearance condition.";
                        return false;
                    }
                    if (!observation.OcrResult.Success)
                    {
                        if (string.IsNullOrWhiteSpace(observation.OcrResult.Text) &&
                            string.Equals(
                                observation.OcrResult.Error,
                                "Tesseract returned no text.",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        reason = string.IsNullOrEmpty(observation.OcrResult.Error)
                            ? "OCR failed while checking disappearance."
                            : observation.OcrResult.Error;
                        return false;
                    }
                    if (observation.OcrResult.Confidence < condition.TextCondition.MinimumConfidence)
                    {
                        reason = "OCR confidence is below the configured threshold.";
                        return false;
                    }
                    return !condition.TextCondition.Matches(observation.OcrResult, out reason);

                case VisionConditionType.NumberInRange:
                    return condition.TextCondition.Matches(observation.OcrResult, out reason);

                case VisionConditionType.TemplateAppears:
                    if (observation.TemplateResult == null || observation.TemplateResult.Cancelled)
                    {
                        reason = "Template result is not usable.";
                        return false;
                    }
                    if (!observation.TemplateResult.Found)
                    {
                        reason = "Template similarity is below the configured threshold.";
                        return false;
                    }
                    return observation.TemplateResult.Similarity >= condition.MinimumSimilarity;

                case VisionConditionType.TemplateDisappears:
                    if (observation.TemplateResult == null || observation.TemplateResult.Cancelled)
                    {
                        reason = "Template result is not usable.";
                        return false;
                    }
                    return !observation.TemplateResult.Found ||
                        observation.TemplateResult.Similarity < condition.MinimumSimilarity;

                default:
                    reason = "Unknown vision condition type.";
                    return false;
            }
        }
    }
}
