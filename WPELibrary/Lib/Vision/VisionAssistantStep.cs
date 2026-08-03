namespace WPELibrary.Lib.Vision
{
    public sealed class VisionAssistantStep
    {
        public string Name { get; set; }

        public VisionConditionDefinition Condition { get; set; }

        public IVisionAssistantAction Action { get; set; }

        public VisionAssistantStep()
        {
            this.Name = string.Empty;
        }
    }
}
