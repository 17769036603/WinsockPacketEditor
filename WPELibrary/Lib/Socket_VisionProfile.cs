using System.Collections.Generic;
using WPELibrary.Lib.Vision;

namespace WPELibrary.Lib
{
    public sealed class Socket_VisionProfile
    {
        public long WindowHandle { get; set; }

        public int ProcessId { get; set; }

        public string ProcessName { get; set; }

        public string ProcessPath { get; set; }

        public long ProcessStartTimeUtcTicks { get; set; }

        public string WindowTitle { get; set; }

        public VisionRegion Region { get; set; }

        public VisionOcrOptions OcrOptions { get; set; }

        public VisionTextCondition OcrCondition { get; set; }

        public VisionCaptureSettings CaptureSettings { get; set; }

        public List<VisionAssistantStep> AssistantSteps { get; private set; }

        public bool HasConfiguration
        {
            get
            {
                return this.WindowHandle != 0 &&
                    this.Region != null &&
                    this.Region.IsValid ||
                    (this.AssistantSteps != null && this.AssistantSteps.Count > 0) ||
                    (this.OcrCondition != null &&
                     !string.IsNullOrWhiteSpace(this.OcrCondition.ExpectedText));
            }
        }

        public Socket_VisionProfile()
        {
            this.ProcessName = string.Empty;
            this.ProcessPath = string.Empty;
            this.WindowTitle = string.Empty;
            this.Region = new VisionRegion();
            this.Region.UseNormalizedCoordinates = true;
            this.OcrOptions = new VisionOcrOptions();
            this.OcrCondition = new VisionTextCondition();
            this.CaptureSettings = new VisionCaptureSettings();
            this.AssistantSteps = new List<VisionAssistantStep>();
        }

        public Socket_VisionProfile Clone()
        {
            Socket_VisionProfile clone = new Socket_VisionProfile
            {
                WindowHandle = this.WindowHandle,
                ProcessId = this.ProcessId,
                ProcessName = this.ProcessName,
                ProcessPath = this.ProcessPath,
                ProcessStartTimeUtcTicks = this.ProcessStartTimeUtcTicks,
                WindowTitle = this.WindowTitle,
                Region = this.Region == null ? new VisionRegion() : this.Region.Clone(),
                OcrOptions = this.OcrOptions == null
                    ? new VisionOcrOptions()
                    : this.OcrOptions.Clone(),
                OcrCondition = this.OcrCondition == null
                    ? new VisionTextCondition()
                    : this.OcrCondition.Clone(),
                CaptureSettings = this.CaptureSettings == null
                    ? new VisionCaptureSettings()
                    : this.CaptureSettings.Clone()
            };
            this.CopyAssistantStepsTo(clone);
            return clone;
        }

        public void CopyAssistantStepsTo(Socket_VisionProfile target)
        {
            if (target == null)
            {
                return;
            }

            target.AssistantSteps.Clear();
            if (this.AssistantSteps == null)
            {
                return;
            }

            foreach (VisionAssistantStep step in this.AssistantSteps)
            {
                if (step == null)
                {
                    continue;
                }

                target.AssistantSteps.Add(new VisionAssistantStep
                {
                    Name = step.Name,
                    Condition = step.Condition == null ? null : step.Condition.Clone(),
                    Action = null
                });
            }
        }
    }
}
