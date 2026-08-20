using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace WPELibrary.Lib.Vision
{
    public static class VisionResourceSerializer
    {
        public static byte[] ToPngBytes(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return new byte[0];
            }

            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        public static Bitmap FromPngBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            using (MemoryStream stream = new MemoryStream(bytes))
            using (Image image = Image.FromStream(stream))
            {
                return new Bitmap(image);
            }
        }
    }
}
