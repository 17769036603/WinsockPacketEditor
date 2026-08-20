namespace WPELibrary.Lib.Vision
{
    public sealed class VisionAssistantStep
    {
        public string Name { get; set; }

        public VisionConditionDefinition Condition { get; set; }

        public IVisionAssistantAction Action { get; set; }

        public VisionActionDefinition ActionDefinition { get; set; }

        public bool VerificationEnabled { get; set; }

        public VisionConditionDefinition Verification { get; set; }

        public bool VerificationUsesSeparateRegion { get; set; }

        public VisionAssistantStep()
        {
            this.Name = string.Empty;
            this.ActionDefinition = new VisionActionDefinition();
            this.VerificationEnabled = false;
            this.Verification = null;
            this.VerificationUsesSeparateRegion = false;
        }
    }
}
