using System;
using System.Drawing;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionWindowInfo
    {
        public IntPtr Handle { get; internal set; }

        public int ProcessId { get; internal set; }

        public string ProcessName { get; internal set; }

        public string ProcessPath { get; internal set; }

        public long ProcessStartTimeUtcTicks { get; internal set; }

        public string WindowTitle { get; internal set; }

        public Rectangle ClientBoundsScreen { get; internal set; }

        public Size ClientSize
        {
            get { return this.ClientBoundsScreen.Size; }
        }

        public string DisplayName
        {
            get
            {
                string process = string.IsNullOrWhiteSpace(this.ProcessName)
                    ? "Unknown"
                    : this.ProcessName;
                return string.Format("{0} [{1}]", this.WindowTitle, process);
            }
        }

        public override string ToString()
        {
            return this.DisplayName;
        }
    }
}
