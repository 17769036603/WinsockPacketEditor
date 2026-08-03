using System;
using System.Threading;
using WPELibrary.Lib;

namespace WPELibrary.Lib.Vision
{
    public static class VisionAssistantRunner
    {
        public static VisionAssistantRunResult Run(
            Socket_VisionProfile profile,
            IVisionTextRecognizer textRecognizer,
            CancellationToken cancellationToken,
            EventHandler<VisionAssistantLogEntry> logHandler)
        {
            VisionAssistantRunResult invalid = new VisionAssistantRunResult();
            if (profile == null || profile.WindowHandle == 0)
            {
                invalid.Error = "A configured target window is required.";
                return invalid;
            }
            if (profile.AssistantSteps == null || profile.AssistantSteps.Count == 0)
            {
                invalid.Succeeded = true;
                return invalid;
            }

            VisionWindowObservationProvider provider = new VisionWindowObservationProvider(
                profile,
                textRecognizer);
            VisionAssistantStateMachine stateMachine = new VisionAssistantStateMachine(provider);
            if (logHandler != null)
            {
                stateMachine.LogEmitted += logHandler;
            }

            return stateMachine.Run(profile.AssistantSteps, cancellationToken);
        }
    }
}
