using System;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionActionDefinition
    {
        public VisionActionType Type { get; set; }

        public VisionScrollDirection ScrollDirection { get; set; }

        public int ScrollAmount { get; set; }

        public int DelayMilliseconds { get; set; }

        public VisionActionDefinition()
        {
            this.Type = VisionActionType.None;
            this.ScrollDirection = VisionScrollDirection.Down;
            this.ScrollAmount = 3;
            this.DelayMilliseconds = 300;
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(VisionActionType), this.Type))
            {
                throw new ArgumentOutOfRangeException("Type");
            }
            if (!Enum.IsDefined(typeof(VisionScrollDirection), this.ScrollDirection))
            {
                throw new ArgumentOutOfRangeException("ScrollDirection");
            }
            if (this.ScrollAmount < 1 || this.ScrollAmount > 100)
            {
                throw new ArgumentOutOfRangeException("ScrollAmount");
            }
            if (this.DelayMilliseconds < 0 || this.DelayMilliseconds > 60000)
            {
                throw new ArgumentOutOfRangeException("DelayMilliseconds");
            }
        }

        public VisionActionDefinition Clone()
        {
            return new VisionActionDefinition
            {
                Type = this.Type,
                ScrollDirection = this.ScrollDirection,
                ScrollAmount = this.ScrollAmount,
                DelayMilliseconds = this.DelayMilliseconds
            };
        }
    }
}
