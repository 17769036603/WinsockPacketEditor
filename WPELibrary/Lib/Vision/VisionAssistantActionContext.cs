namespace WPELibrary.Lib.Vision
{
    public sealed class VisionAssistantActionContext
    {
        public int StepIndex { get; internal set; }

        public VisionConditionDefinition Condition { get; internal set; }

        public VisionObservation LastObservation { get; internal set; }
    }
}
