using System.Drawing;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionMatchResult
    {
        private VisionMatchResult(
            bool found,
            bool cancelled,
            Point location,
            Size size,
            double similarity)
        {
            this.Found = found;
            this.Cancelled = cancelled;
            this.Location = location;
            this.Size = size;
            this.Similarity = similarity;
        }

        public bool Found { get; private set; }

        public bool Cancelled { get; private set; }

        public Point Location { get; private set; }

        public Size Size { get; private set; }

        public double Similarity { get; private set; }

        public Rectangle Bounds
        {
            get { return new Rectangle(this.Location, this.Size); }
        }

        public static VisionMatchResult FoundAt(Point location, Size size, double similarity)
        {
            return new VisionMatchResult(true, false, location, size, similarity);
        }

        public static VisionMatchResult NotFound(Point location, Size size, double similarity)
        {
            return new VisionMatchResult(false, false, location, size, similarity);
        }

        public static VisionMatchResult CancelledResult()
        {
            return new VisionMatchResult(false, true, Point.Empty, Size.Empty, 0D);
        }
    }
}
