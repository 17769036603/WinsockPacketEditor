using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public static class VisionTemplateMatcher
    {
        public static VisionMatchResult FindBestMatch(
            Bitmap source,
            IEnumerable<Bitmap> templates,
            double minimumSimilarity,
            CancellationToken cancellationToken)
        {
            return FindBestMatch(
                source,
                templates,
                minimumSimilarity,
                new VisionTemplateMatchOptions(),
                cancellationToken);
        }

        public static VisionMatchResult FindBestMatch(
            Bitmap source,
            IEnumerable<Bitmap> templates,
            double minimumSimilarity,
            VisionTemplateMatchOptions options,
            CancellationToken cancellationToken)
        {
            if (templates == null)
            {
                throw new ArgumentNullException("templates");
            }
            VisionMatchResult best = null;
            foreach (Bitmap template in templates)
            {
                if (template == null)
                {
                    continue;
                }
                VisionMatchResult current = FindBestMatch(
                    source,
                    template,
                    minimumSimilarity,
                    options,
                    cancellationToken);
                if (current.Cancelled)
                {
                    return current;
                }
                if (best == null || current.Similarity > best.Similarity)
                {
                    best = current;
                }
            }
            return best ?? VisionMatchResult.NotFound(Point.Empty, Size.Empty, 0D);
        }

        public static VisionMatchResult FindBestMatch(
            Bitmap source,
            Bitmap template,
            double minimumSimilarity,
            CancellationToken cancellationToken)
        {
            return FindBestMatch(
                source,
                template,
                minimumSimilarity,
                new VisionTemplateMatchOptions(),
                cancellationToken);
        }

        public static VisionMatchResult FindBestMatch(
            Bitmap source,
            Bitmap template,
            double minimumSimilarity,
            VisionTemplateMatchOptions options,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (template == null)
            {
                throw new ArgumentNullException("template");
            }
            if (minimumSimilarity < 0D || minimumSimilarity > 1D)
            {
                throw new ArgumentOutOfRangeException("minimumSimilarity");
            }
            if (template.Width <= 0 || template.Height <= 0)
            {
                throw new ArgumentException("The template must have a positive size.", "template");
            }
            if (options == null)
            {
                options = new VisionTemplateMatchOptions();
            }
            options.Validate();
            if (cancellationToken.IsCancellationRequested)
            {
                return VisionMatchResult.CancelledResult();
            }

            VisionMatchResult best = null;
            foreach (double scale in options.GetScales())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return VisionMatchResult.CancelledResult();
                }

                Bitmap scaledTemplate = null;
                Bitmap candidateTemplate = template;
                try
                {
                    if (Math.Abs(scale - 1D) > 0.0001D)
                    {
                        scaledTemplate = ResizeTemplate(template, scale);
                        candidateTemplate = scaledTemplate;
                    }
                    VisionMatchResult current = FindBestMatchSingle(
                        source,
                        candidateTemplate,
                        minimumSimilarity,
                        options.NormalizeBrightness,
                        cancellationToken);
                    if (current.Cancelled)
                    {
                        return current;
                    }
                    if (best == null || current.Similarity > best.Similarity)
                    {
                        best = current;
                    }
                }
                finally
                {
                    if (scaledTemplate != null)
                    {
                        scaledTemplate.Dispose();
                    }
                }
            }

            return best ?? VisionMatchResult.NotFound(Point.Empty, template.Size, 0D);
        }

        private static VisionMatchResult FindBestMatchSingle(
            Bitmap source,
            Bitmap template,
            double minimumSimilarity,
            bool normalizeBrightness,
            CancellationToken cancellationToken)
        {
            if (template.Width > source.Width || template.Height > source.Height)
            {
                return VisionMatchResult.NotFound(Point.Empty, template.Size, 0D);
            }

            byte[] sourceGray = ToGrayscale(source);
            byte[] templateGray = ToGrayscale(template);
            int sourceWidth = source.Width;
            int templateWidth = template.Width;
            int templateHeight = template.Height;
            long templatePixelCount = (long)templateWidth * templateHeight;
            double templateMean = Mean(templateGray);
            bool useNormalizedComparison = normalizeBrightness && HasContrast(templateGray);
            double maximumDifference = (useNormalizedComparison ? 510D : 255D) * templatePixelCount;
            double bestSimilarity = double.MinValue;
            Point bestLocation = Point.Empty;

            for (int y = 0; y <= source.Height - templateHeight; y++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return VisionMatchResult.CancelledResult();
                }

                for (int x = 0; x <= sourceWidth - templateWidth; x++)
                {
                    double sourceMean = 0D;
                    if (useNormalizedComparison)
                    {
                        long sourceSum = 0L;
                        for (int templateY = 0; templateY < templateHeight; templateY++)
                        {
                            int sourceOffset = (y + templateY) * sourceWidth + x;
                            for (int templateX = 0; templateX < templateWidth; templateX++)
                            {
                                sourceSum += sourceGray[sourceOffset + templateX];
                            }
                        }
                        sourceMean = sourceSum / (double)templatePixelCount;
                    }

                    double difference = 0D;
                    for (int templateY = 0; templateY < templateHeight; templateY++)
                    {
                        int sourceOffset = (y + templateY) * sourceWidth + x;
                        int templateOffset = templateY * templateWidth;
                        for (int templateX = 0; templateX < templateWidth; templateX++)
                        {
                            double sourceValue = sourceGray[sourceOffset + templateX];
                            double templateValue = templateGray[templateOffset + templateX];
                            if (useNormalizedComparison)
                            {
                                sourceValue -= sourceMean;
                                templateValue -= templateMean;
                            }
                            difference += Math.Abs(sourceValue - templateValue);
                        }
                    }

                    double similarity = 1D - difference / maximumDifference;
                    if (similarity > bestSimilarity)
                    {
                        bestSimilarity = similarity;
                        bestLocation = new Point(x, y);
                    }
                }
            }

            if (bestSimilarity >= minimumSimilarity)
            {
                return VisionMatchResult.FoundAt(bestLocation, template.Size, bestSimilarity);
            }
            return VisionMatchResult.NotFound(bestLocation, template.Size, bestSimilarity);
        }

        private static Bitmap ResizeTemplate(Bitmap template, double scale)
        {
            int width = Math.Max(1, (int)Math.Round(template.Width * scale));
            int height = Math.Max(1, (int)Math.Round(template.Height * scale));
            Bitmap resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(resized))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(template, new Rectangle(0, 0, width, height));
            }
            return resized;
        }

        private static bool HasContrast(byte[] grayscale)
        {
            if (grayscale.Length == 0)
            {
                return false;
            }
            byte first = grayscale[0];
            for (int i = 1; i < grayscale.Length; i++)
            {
                if (grayscale[i] != first)
                {
                    return true;
                }
            }
            return false;
        }

        private static double Mean(byte[] values)
        {
            if (values.Length == 0)
            {
                return 0D;
            }
            long total = 0L;
            foreach (byte value in values)
            {
                total += value;
            }
            return total / (double)values.Length;
        }

        private static byte[] ToGrayscale(Bitmap bitmap)
        {
            using (Bitmap normalized = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(normalized))
                {
                    graphics.DrawImageUnscaled(bitmap, 0, 0);
                }

                Rectangle bounds = new Rectangle(0, 0, normalized.Width, normalized.Height);
                BitmapData data = normalized.LockBits(
                    bounds,
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] pixels = new byte[stride * normalized.Height];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    byte[] grayscale = new byte[normalized.Width * normalized.Height];
                    for (int y = 0; y < normalized.Height; y++)
                    {
                        int rowOffset = data.Stride >= 0
                            ? y * stride
                            : (normalized.Height - 1 - y) * stride;
                        int grayscaleOffset = y * normalized.Width;
                        for (int x = 0; x < normalized.Width; x++)
                        {
                            int pixelOffset = rowOffset + x * 4;
                            int blue = pixels[pixelOffset];
                            int green = pixels[pixelOffset + 1];
                            int red = pixels[pixelOffset + 2];
                            grayscale[grayscaleOffset + x] = (byte)(
                                (red * 299 + green * 587 + blue * 114 + 500) / 1000);
                        }
                    }

                    return grayscale;
                }
                finally
                {
                    normalized.UnlockBits(data);
                }
            }
        }
    }
}
