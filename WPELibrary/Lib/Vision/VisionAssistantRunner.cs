using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
            return Run(
                profile,
                profile == null ? null : profile.AssistantSteps,
                textRecognizer,
                cancellationToken,
                logHandler);
        }

        public static VisionAssistantRunResult Run(
            Socket_VisionProfile profile,
            IList<VisionAssistantStep> steps,
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
            if (steps == null || steps.Count == 0)
            {
                invalid.Succeeded = true;
                return invalid;
            }

            bool hasSystemInputAction = steps.Any(
                step => step != null && step.ActionDefinition != null &&
                    step.ActionDefinition.Type != VisionActionType.None);
            if (hasSystemInputAction && !profile.AllowSystemInput)
            {
                invalid.Error = "System input actions are disabled until the current run is explicitly confirmed.";
                return invalid;
            }

            VisionWindowInfo resolvedWindow;
            string resolveReason;
            if (!VisionWindowService.TryResolveWindow(
                new IntPtr(profile.WindowHandle),
                profile.ProcessId,
                profile.ProcessName,
                profile.ProcessPath,
                profile.ProcessStartTimeUtcTicks,
                profile.WindowTitle,
                out resolvedWindow,
                out resolveReason))
            {
                invalid.Error = resolveReason;
                return invalid;
            }

            profile.WindowHandle = resolvedWindow.Handle.ToInt64();
            profile.ProcessId = resolvedWindow.ProcessId;
            profile.ProcessName = resolvedWindow.ProcessName;
            profile.ProcessPath = resolvedWindow.ProcessPath;
            profile.ProcessStartTimeUtcTicks = resolvedWindow.ProcessStartTimeUtcTicks;
            profile.WindowTitle = resolvedWindow.WindowTitle;
            VisionCaptureSettings captureSettings = profile.CaptureSettings == null
                ? new VisionCaptureSettings()
                : profile.CaptureSettings;
            Size clientSize;
            if (!VisionWindowService.TryValidateClientSize(
                resolvedWindow.Handle,
                captureSettings,
                out clientSize,
                out resolveReason))
            {
                invalid.Error = resolveReason;
                return invalid;
            }
            if (profile.Region == null || !profile.Region.FitsWithin(clientSize))
            {
                invalid.Error = string.Format(
                    "The profile region must stay inside the target client area ({0}x{1}).",
                    clientSize.Width,
                    clientSize.Height);
                return invalid;
            }
            if (captureSettings.RequireExactClientSize && profile.Region.UseNormalizedCoordinates)
            {
                invalid.Error = "The profile region must use fixed pixel coordinates when exact client size mode is enabled.";
                return invalid;
            }

            for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                VisionAssistantStep step = steps[stepIndex];
                if (step == null || step.Condition == null)
                {
                    invalid.FailedStepIndex = stepIndex;
                    invalid.Error = "A state-machine step has no condition.";
                    return invalid;
                }
                try
                {
                    if (step.ActionDefinition != null)
                    {
                        step.ActionDefinition.Validate();
                    }
                }
                catch (Exception ex)
                {
                    invalid.FailedStepIndex = stepIndex;
                    invalid.Error = string.Format(
                        "Step '{0}' has an invalid action: {1}",
                        step.Name,
                        ex.Message);
                    return invalid;
                }
                if (captureSettings.RequireExactClientSize &&
                    ((step.Condition.Region != null && step.Condition.Region.UseNormalizedCoordinates) ||
                     (step.VerificationEnabled && step.Verification != null &&
                      step.Verification.Region != null && step.Verification.Region.UseNormalizedCoordinates)))
                {
                    invalid.FailedStepIndex = stepIndex;
                    invalid.Error = string.Format(
                        "Step '{0}' must use fixed pixel regions when exact client size mode is enabled.",
                        step.Name);
                    return invalid;
                }
                if (!step.Condition.FitsWithin(clientSize, out resolveReason))
                {
                    invalid.FailedStepIndex = stepIndex;
                    invalid.Error = string.Format(
                        "Step '{0}' has an invalid region: {1}",
                        step.Name,
                        resolveReason);
                    return invalid;
                }
                if (step.VerificationEnabled)
                {
                    if (step.Verification == null)
                    {
                        invalid.FailedStepIndex = stepIndex;
                        invalid.Error = string.Format(
                            "Step '{0}' has an invalid verification region: A verification condition is required.",
                            step.Name);
                        return invalid;
                    }
                    if (!step.Verification.FitsWithin(clientSize, out resolveReason))
                    {
                        invalid.FailedStepIndex = stepIndex;
                        invalid.Error = string.Format(
                            "Step '{0}' has an invalid verification region: {1}",
                            step.Name,
                            resolveReason);
                        return invalid;
                    }
                }
            }

            VisionWindowObservationProvider provider = new VisionWindowObservationProvider(
                profile,
                textRecognizer);
            foreach (VisionAssistantStep step in steps)
            {
                if (step == null)
                {
                    continue;
                }
                step.Action = null;
                if (step.ActionDefinition != null &&
                    step.ActionDefinition.Type != VisionActionType.None)
                {
                    step.Action = new VisionMouseAction(profile, step.ActionDefinition);
                }
            }
            VisionAssistantStateMachine stateMachine = new VisionAssistantStateMachine(provider);
            if (logHandler != null)
            {
                stateMachine.LogEmitted += logHandler;
            }

            return stateMachine.Run(steps, cancellationToken);
        }
    }
}
