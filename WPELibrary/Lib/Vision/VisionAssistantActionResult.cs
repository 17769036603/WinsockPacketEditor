namespace WPELibrary.Lib.Vision
{
    public sealed class VisionAssistantActionResult
    {
        private VisionAssistantActionResult(bool success, bool cancelled, string error)
        {
            this.Success = success;
            this.Cancelled = cancelled;
            this.Error = error ?? string.Empty;
        }

        public bool Success { get; private set; }

        public bool Cancelled { get; private set; }

        public string Error { get; private set; }

        public static VisionAssistantActionResult Succeeded()
        {
            return new VisionAssistantActionResult(true, false, string.Empty);
        }

        public static VisionAssistantActionResult Failed(string error)
        {
            return new VisionAssistantActionResult(false, false, error);
        }

        public static VisionAssistantActionResult CancelledResult()
        {
            return new VisionAssistantActionResult(false, true, "Action cancelled.");
        }
    }
}
