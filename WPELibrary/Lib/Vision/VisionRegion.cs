using System;
using System.Drawing;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionRegion
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        /// <summary>
        /// When enabled, the region is scaled from the reference client size.
        /// Existing profiles keep this disabled and therefore retain pixel behavior.
        /// </summary>
        public bool UseNormalizedCoordinates { get; set; }

        public int ReferenceWidth { get; set; }

        public int ReferenceHeight { get; set; }

        public bool IsValid
        {
            get
            {
                return this.X >= 0 &&
                    this.Y >= 0 &&
                    this.Width > 0 &&
                    this.Height > 0;
            }
        }

        public Rectangle Bounds
        {
            get { return new Rectangle(this.X, this.Y, this.Width, this.Height); }
        }

        public Rectangle Resolve(Size clientSize)
        {
            if (!this.UseNormalizedCoordinates ||
                this.ReferenceWidth <= 0 ||
                this.ReferenceHeight <= 0)
            {
                return this.Bounds;
            }

            return new Rectangle(
                ScaleCoordinate(this.X, this.ReferenceWidth, clientSize.Width),
                ScaleCoordinate(this.Y, this.ReferenceHeight, clientSize.Height),
                Math.Max(1, ScaleCoordinate(this.Width, this.ReferenceWidth, clientSize.Width)),
                Math.Max(1, ScaleCoordinate(this.Height, this.ReferenceHeight, clientSize.Height)));
        }

        public bool FitsWithin(Size clientSize)
        {
            Rectangle resolved = this.Resolve(clientSize);
            return this.IsValid &&
                resolved.X >= 0 &&
                resolved.Y >= 0 &&
                resolved.Width > 0 &&
                resolved.Height > 0 &&
                resolved.Right <= clientSize.Width &&
                resolved.Bottom <= clientSize.Height;
        }

        public VisionRegion Clone()
        {
            return new VisionRegion
            {
                X = this.X,
                Y = this.Y,
                Width = this.Width,
                Height = this.Height,
                UseNormalizedCoordinates = this.UseNormalizedCoordinates,
                ReferenceWidth = this.ReferenceWidth,
                ReferenceHeight = this.ReferenceHeight
            };
        }

        private static int ScaleCoordinate(int value, int reference, int actual)
        {
            return (int)Math.Round(value * (double)actual / reference, MidpointRounding.AwayFromZero);
        }

        public override string ToString()
        {
            return string.Format("{0},{1},{2},{3}", this.X, this.Y, this.Width, this.Height);
        }
    }
}
