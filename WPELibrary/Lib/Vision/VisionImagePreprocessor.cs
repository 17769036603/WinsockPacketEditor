using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public static class VisionImagePreprocessor
    {
        public static Bitmap Preprocess(
            Bitmap source,
            VisionOcrOptions options,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            options.Validate();
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            using (Bitmap normalized = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(normalized))
                {
                    graphics.DrawImageUnscaled(source, 0, 0);
                }

                int width = normalized.Width;
                int height = normalized.Height;
                int[] grayscale = new int[width * height];
                bool useGrayscale = options.ConvertToGrayscale ||
                    options.UseBinaryThreshold ||
                    options.UseAdaptiveThreshold ||
                    options.Invert ||
                    options.UseDenoise ||
                    options.UseSharpen;
                byte[] sourceBytes;
                int sourceStride;
                int sourceStrideSigned;
                BitmapData sourceData = normalized.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    sourceStrideSigned = sourceData.Stride;
                    sourceStride = Math.Abs(sourceStrideSigned);
                    sourceBytes = new byte[sourceStride * height];
                    Marshal.Copy(sourceData.Scan0, sourceBytes, 0, sourceBytes.Length);
                    if (useGrayscale)
                    {
                        BuildGrayscale(sourceBytes, sourceData.Stride, width, height, grayscale);
                        for (int i = 0; i < grayscale.Length; i++)
                        {
                            grayscale[i] = AdjustContrast(grayscale[i], options.Contrast);
                            if (options.Invert)
                            {
                                grayscale[i] = 255 - grayscale[i];
                            }
                        }
                    }
                }
                finally
                {
                    normalized.UnlockBits(sourceData);
                }

                if (useGrayscale && options.UseDenoise)
                {
                    grayscale = MedianFilter(grayscale, width, height, cancellationToken);
                    if (grayscale == null)
                    {
                        return null;
                    }
                }
                if (useGrayscale && options.UseSharpen)
                {
                    grayscale = Sharpen(grayscale, width, height, cancellationToken);
                    if (grayscale == null)
                    {
                        return null;
                    }
                }
                if (useGrayscale && options.UseAdaptiveThreshold)
                {
                    ApplyAdaptiveThreshold(
                        grayscale,
                        width,
                        height,
                        options.AdaptiveThresholdWindowSize,
                        options.AdaptiveThresholdOffset,
                        cancellationToken);
                }
                else if (useGrayscale && options.UseBinaryThreshold)
                {
                    ApplyBinaryThreshold(grayscale, options.BinaryThreshold, cancellationToken);
                }

                int outputWidth = checked(width * options.ScaleFactor);
                int outputHeight = checked(height * options.ScaleFactor);
                Bitmap output = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
                try
                {
                    BitmapData outputData = output.LockBits(
                        new Rectangle(0, 0, outputWidth, outputHeight),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);
                    try
                    {
                        int outputStride = Math.Abs(outputData.Stride);
                        byte[] outputBytes = new byte[outputStride * outputHeight];
                        for (int y = 0; y < outputHeight; y++)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                throw new OperationCanceledException(cancellationToken);
                            }

                            int sourceY = y / options.ScaleFactor;
                            int outputRowOffset = outputData.Stride >= 0
                                ? y * outputStride
                                : (outputHeight - 1 - y) * outputStride;
                            int sourceRowOffset = sourceY * width;
                            int sourceByteRowOffset = sourceStrideSigned >= 0
                                ? sourceY * sourceStride
                                : (height - 1 - sourceY) * sourceStride;
                            for (int x = 0; x < outputWidth; x++)
                            {
                                int sourceX = x / options.ScaleFactor;
                                int outputOffset = outputRowOffset + x * 4;
                                if (useGrayscale)
                                {
                                    byte value = (byte)grayscale[sourceRowOffset + sourceX];
                                    outputBytes[outputOffset] = value;
                                    outputBytes[outputOffset + 1] = value;
                                    outputBytes[outputOffset + 2] = value;
                                }
                                else
                                {
                                    int sourceOffset = sourceByteRowOffset + sourceX * 4;
                                    outputBytes[outputOffset] = (byte)AdjustContrast(sourceBytes[sourceOffset], options.Contrast);
                                    outputBytes[outputOffset + 1] = (byte)AdjustContrast(sourceBytes[sourceOffset + 1], options.Contrast);
                                    outputBytes[outputOffset + 2] = (byte)AdjustContrast(sourceBytes[sourceOffset + 2], options.Contrast);
                                }
                                outputBytes[outputOffset + 3] = 255;
                            }
                        }

                        Marshal.Copy(outputBytes, 0, outputData.Scan0, outputBytes.Length);
                    }
                    finally
                    {
                        output.UnlockBits(outputData);
                    }
                    return output;
                }
                catch (OperationCanceledException)
                {
                    output.Dispose();
                    return null;
                }
                catch
                {
                    output.Dispose();
                    throw;
                }
            }
        }

        private static void BuildGrayscale(
            byte[] sourceBytes,
            int sourceStrideValue,
            int width,
            int height,
            int[] grayscale)
        {
            int stride = Math.Abs(sourceStrideValue);
            for (int y = 0; y < height; y++)
            {
                int rowOffset = sourceStrideValue >= 0
                    ? y * stride
                    : (height - 1 - y) * stride;
                int grayOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int pixelOffset = rowOffset + x * 4;
                    grayscale[grayOffset + x] =
                        (sourceBytes[pixelOffset + 2] * 299 +
                         sourceBytes[pixelOffset + 1] * 587 +
                         sourceBytes[pixelOffset] * 114 + 500) / 1000;
                }
            }
        }

        private static int[] MedianFilter(
            int[] source,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            int[] output = new int[source.Length];
            int[] values = new int[9];
            for (int y = 0; y < height; y++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
                for (int x = 0; x < width; x++)
                {
                    int count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int sampleY = Math.Max(0, Math.Min(height - 1, y + dy));
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int sampleX = Math.Max(0, Math.Min(width - 1, x + dx));
                            values[count++] = source[sampleY * width + sampleX];
                        }
                    }
                    Array.Sort(values, 0, count);
                    output[y * width + x] = values[count / 2];
                }
            }
            return output;
        }

        private static int[] Sharpen(
            int[] source,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            int[] output = new int[source.Length];
            for (int y = 0; y < height; y++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
                for (int x = 0; x < width; x++)
                {
                    int center = source[y * width + x];
                    int left = source[y * width + Math.Max(0, x - 1)];
                    int right = source[y * width + Math.Min(width - 1, x + 1)];
                    int top = source[Math.Max(0, y - 1) * width + x];
                    int bottom = source[Math.Min(height - 1, y + 1) * width + x];
                    int neighborAverage = (left + right + top + bottom) / 4;
                    output[y * width + x] = ClampByte(center + (center - neighborAverage));
                }
            }
            return output;
        }

        private static void ApplyBinaryThreshold(
            int[] grayscale,
            byte threshold,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < grayscale.Length; i++)
            {
                if ((i & 4095) == 0 && cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                grayscale[i] = grayscale[i] >= threshold ? 255 : 0;
            }
        }

        private static void ApplyAdaptiveThreshold(
            int[] grayscale,
            int width,
            int height,
            int windowSize,
            int offset,
            CancellationToken cancellationToken)
        {
            int halfWindow = windowSize / 2;
            int integralWidth = width + 1;
            long[] integral = new long[integralWidth * (height + 1)];
            for (int y = 1; y <= height; y++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                long rowTotal = 0L;
                for (int x = 1; x <= width; x++)
                {
                    rowTotal += grayscale[(y - 1) * width + x - 1];
                    integral[y * integralWidth + x] = integral[(y - 1) * integralWidth + x] + rowTotal;
                }
            }

            for (int y = 0; y < height; y++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                int top = Math.Max(0, y - halfWindow);
                int bottom = Math.Min(height - 1, y + halfWindow);
                for (int x = 0; x < width; x++)
                {
                    int left = Math.Max(0, x - halfWindow);
                    int right = Math.Min(width - 1, x + halfWindow);
                    long sum = integral[(bottom + 1) * integralWidth + right + 1] -
                        integral[top * integralWidth + right + 1] -
                        integral[(bottom + 1) * integralWidth + left] +
                        integral[top * integralWidth + left];
                    int count = (right - left + 1) * (bottom - top + 1);
                    int localThreshold = (int)(sum / count) - offset;
                    grayscale[y * width + x] = grayscale[y * width + x] >= localThreshold ? 255 : 0;
                }
            }
        }

        private static int AdjustContrast(int value, double contrast)
        {
            return ClampByte((int)Math.Round((value - 128D) * contrast + 128D));
        }

        private static int ClampByte(int value)
        {
            return Math.Max(0, Math.Min(255, value));
        }
    }
}
