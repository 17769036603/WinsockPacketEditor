using System;

namespace WPELibrary.Lib.Vision
{
    public enum VisionAssistantLogLevel
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public sealed class VisionAssistantLogEntry : EventArgs
    {
        public DateTime TimestampUtc { get; private set; }

        public int StepIndex { get; private set; }

        public VisionAssistantLogLevel Level { get; private set; }

        public string Message { get; private set; }

        public VisionAssistantLogEntry(
            int stepIndex,
            VisionAssistantLogLevel level,
            string message)
        {
            this.TimestampUtc = DateTime.UtcNow;
            this.StepIndex = stepIndex;
            this.Level = level;
            this.Message = message ?? string.Empty;
        }
    }
}
