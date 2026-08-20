using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public static class VisionColorMatcher
    {
        public static VisionColorMatchResult Find(
            Bitmap source,
            VisionColorCondition condition,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (condition == null)
            {
                throw new ArgumentNullException("condition");
            }

            condition.Validate();
            if (cancellationToken.IsCancellationRequested)
            {
                return VisionColorMatchResult.CancelledResult();
            }

            using (Bitmap normalized = new Bitmap(
                source.Width,
                source.Height,
                PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(normalized))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(source, 0, 0);
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
                    int count = 0;
                    long distanceTotal = 0L;
                    int minX = normalized.Width;
                    int minY = normalized.Height;
                    int maxX = -1;
                    int maxY = -1;
                    for (int y = 0; y < normalized.Height; y++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return VisionColorMatchResult.CancelledResult();
                        }

                        int rowOffset = data.Stride >= 0
                            ? y * stride
                            : (normalized.Height - 1 - y) * stride;
                        for (int x = 0; x < normalized.Width; x++)
                        {
                            int pixelOffset = rowOffset + x * 4;
                            int blue = pixels[pixelOffset];
                            int green = pixels[pixelOffset + 1];
                            int red = pixels[pixelOffset + 2];
                            int distance = Math.Max(
                                Math.Abs(red - condition.Red),
                                Math.Max(
                                    Math.Abs(green - condition.Green),
                                    Math.Abs(blue - condition.Blue)));
                            if (distance > condition.Tolerance)
                            {
                                continue;
                            }

                            count++;
                            distanceTotal += distance;
                            minX = Math.Min(minX, x);
                            minY = Math.Min(minY, y);
                            maxX = Math.Max(maxX, x);
                            maxY = Math.Max(maxY, y);
                        }
                    }

                    double ratio = count / (double)(normalized.Width * (long)normalized.Height);
                    bool found = count >= condition.MinimumPixelCount &&
                        ratio >= condition.MinimumMatchRatio;
                    Rectangle matchBounds = maxX < minX
                        ? Rectangle.Empty
                        : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                    double meanDistance = count == 0 ? 0D : distanceTotal / (double)count;
                    return VisionColorMatchResult.Create(
                        found,
                        count,
                        ratio,
                        meanDistance,
                        matchBounds);
                }
                finally
                {
                    normalized.UnlockBits(data);
                }
            }
        }
    }
}
