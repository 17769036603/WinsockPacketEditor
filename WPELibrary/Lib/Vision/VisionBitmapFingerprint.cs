using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WPELibrary.Lib.Vision
{
    internal static class VisionBitmapFingerprint
    {
        public static uint Compute(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return 0U;
            }

            Bitmap normalized = null;
            Bitmap source = bitmap;
            try
            {
                if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
                {
                    normalized = new Bitmap(
                        bitmap.Width,
                        bitmap.Height,
                        PixelFormat.Format32bppArgb);
                    using (Graphics graphics = Graphics.FromImage(normalized))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImageUnscaled(bitmap, 0, 0);
                    }
                    source = normalized;
                }

                Rectangle bounds = new Rectangle(0, 0, source.Width, source.Height);
                BitmapData data = source.LockBits(
                    bounds,
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    uint fingerprint = 2166136261U;
                    fingerprint = Mix(fingerprint, source.Width);
                    fingerprint = Mix(fingerprint, source.Height);
                    int stride = Math.Abs(data.Stride);
                    int rowBytes = source.Width * 4;
                    byte[] row = new byte[rowBytes];
                    for (int y = 0; y < source.Height; y++)
                    {
                        int rowOffset = data.Stride >= 0
                            ? y * stride
                            : (source.Height - 1 - y) * stride;
                        Marshal.Copy(
                            IntPtr.Add(data.Scan0, rowOffset),
                            row,
                            0,
                            rowBytes);
                        for (int index = 0; index < rowBytes; index++)
                        {
                            fingerprint ^= row[index];
                            fingerprint *= 16777619U;
                        }
                    }
                    return fingerprint;
                }
                finally
                {
                    source.UnlockBits(data);
                }
            }
            catch
            {
                // Preserve fail-closed cache behavior if an unusual bitmap format
                // cannot be locked. The fallback still covers every pixel.
                uint fingerprint = 2166136261U;
                fingerprint = Mix(fingerprint, bitmap.Width);
                fingerprint = Mix(fingerprint, bitmap.Height);
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        fingerprint = Mix(fingerprint, bitmap.GetPixel(x, y).ToArgb());
                    }
                }
                return fingerprint;
            }
            finally
            {
                if (normalized != null)
                {
                    normalized.Dispose();
                }
            }
        }

        private static uint Mix(uint fingerprint, int value)
        {
            unchecked
            {
                fingerprint ^= (uint)value;
                return fingerprint * 16777619U;
            }
        }
    }
}
