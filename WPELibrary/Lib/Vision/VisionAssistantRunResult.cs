namespace WPELibrary.Lib.Vision
{
    public sealed class VisionAssistantRunResult
    {
        public bool Succeeded { get; internal set; }

        public bool Cancelled { get; internal set; }

        public bool SkippedStep { get; internal set; }

        public int CompletedSteps { get; internal set; }

        public int FailedStepIndex { get; internal set; }

        public string Error { get; internal set; }

        public VisionAssistantRunResult()
        {
            this.FailedStepIndex = -1;
            this.Error = string.Empty;
        }
    }
}
