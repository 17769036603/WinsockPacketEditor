using System.Drawing;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionOcrTextBox
    {
        public string Text { get; private set; }

        public Rectangle Bounds { get; private set; }

        public double Confidence { get; private set; }

        public VisionOcrTextBox(string text, Rectangle bounds, double confidence)
        {
            this.Text = text ?? string.Empty;
            this.Bounds = bounds;
            this.Confidence = confidence;
        }
    }
}
