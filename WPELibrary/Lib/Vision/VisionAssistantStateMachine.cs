using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionAssistantStateMachine
    {
        private readonly IVisionObservationProvider observationProvider;

        public event EventHandler<VisionAssistantLogEntry> LogEmitted;

        public VisionAssistantStateMachine(IVisionObservationProvider observationProvider)
        {
            this.observationProvider = observationProvider ??
                throw new ArgumentNullException("observationProvider");
        }

        public VisionAssistantRunResult Run(
            IList<VisionAssistantStep> steps,
            CancellationToken cancellationToken)
        {
            VisionAssistantRunResult result = new VisionAssistantRunResult();
            if (steps == null || steps.Count == 0)
            {
                result.Succeeded = true;
                return result;
            }

            for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    result.Error = Text("Vision_LogAssistantCancelled");
                    this.Emit(stepIndex, VisionAssistantLogLevel.Warning, result.Error);
                    return result;
                }

                VisionAssistantStep step = steps[stepIndex];
                if (step == null || step.Condition == null)
                {
                    result.FailedStepIndex = stepIndex;
                    result.Error = Text("Vision_LogStepMissingCondition");
                    this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                    return result;
                }

                try
                {
                    step.Condition.Validate();
                    if (step.VerificationEnabled)
                    {
                        if (step.Verification == null)
                        {
                            throw new InvalidOperationException(Text("Vision_LogVerificationMissingCondition"));
                        }
                        step.Verification.Validate();
                    }
                }
                catch (Exception ex)
                {
                    result.FailedStepIndex = stepIndex;
                    result.Error = ex.Message;
                    this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                    return result;
                }

                int attempt = 0;
                bool stepCompleted = false;
                while (!stepCompleted)
                {
                    attempt++;
                    this.Emit(
                        stepIndex,
                        VisionAssistantLogLevel.Info,
                        Format("Vision_LogWaitingStep", step.Name, attempt));
                    VisionWaitResult waitResult = this.WaitForCondition(
                        stepIndex,
                        step.Condition,
                        cancellationToken);
                    if (waitResult.Cancelled)
                    {
                        result.Cancelled = true;
                        result.Error = Text("Vision_LogCancelledWaiting");
                        return result;
                    }
                    if (waitResult.TerminalFailure)
                    {
                        result.FailedStepIndex = stepIndex;
                        result.Error = waitResult.LastObservation == null
                            ? Text("Vision_LogObservationNoResult")
                            : waitResult.LastObservation.Error;
                        this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                        return result;
                    }
                    if (waitResult.Matched)
                    {
                        this.Emit(
                            stepIndex,
                            VisionAssistantLogLevel.Info,
                            Format("Vision_LogStepConfirmed", step.Name));
                        if (step.Action != null)
                        {
                            VisionAssistantActionResult actionResult;
                            try
                            {
                                actionResult = step.Action.Execute(
                                    new VisionAssistantActionContext
                                    {
                                        StepIndex = stepIndex,
                                        Condition = step.Condition,
                                        LastObservation = waitResult.LastObservation
                                    },
                                    cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                actionResult = VisionAssistantActionResult.Failed(ex.Message);
                            }
                            if (actionResult == null || !actionResult.Success)
                            {
                                if (actionResult != null && actionResult.Cancelled)
                                {
                                    result.Cancelled = true;
                                    result.Error = actionResult.Error;
                                    return result;
                                }

                                result.FailedStepIndex = stepIndex;
                                result.Error = actionResult == null
                                    ? Text("Vision_LogActionNoResult")
                                    : actionResult.Error;
                                this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                                return result;
                            }
                        }

                        if (step.VerificationEnabled)
                        {
                            VisionWaitResult verificationResult = this.WaitForVerification(
                                stepIndex,
                                step.Verification,
                                cancellationToken);
                            if (verificationResult.Cancelled)
                            {
                                result.Cancelled = true;
                                result.Error = Text("Vision_LogCancelledVerifying");
                                return result;
                            }
                            if (verificationResult.TerminalFailure)
                            {
                                result.FailedStepIndex = stepIndex;
                                result.Error = verificationResult.LastObservation == null
                                    ? Text("Vision_LogObservationNoResult")
                                    : verificationResult.LastObservation.Error;
                                this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                                return result;
                            }
                            if (!verificationResult.Matched)
                            {
                                if (verificationResult.Skipped)
                                {
                                    result.SkippedStep = true;
                                    stepCompleted = true;
                                    continue;
                                }
                                result.FailedStepIndex = stepIndex;
                                result.Error = Format(
                                    "Vision_LogVerificationTimeout",
                                    step.Name);
                                this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                                return result;
                            }
                            this.Emit(
                                stepIndex,
                                VisionAssistantLogLevel.Info,
                                Format("Vision_LogVerificationConfirmed", step.Name));
                        }

                        stepCompleted = true;
                        result.CompletedSteps++;
                        continue;
                    }

                    if (attempt <= step.Condition.MaxRetries)
                    {
                        this.Emit(
                            stepIndex,
                            VisionAssistantLogLevel.Warning,
                            Format("Vision_LogStepRetry", step.Name));
                        continue;
                    }

                    if (step.Condition.FailurePolicy == VisionFailurePolicy.Skip)
                    {
                        result.SkippedStep = true;
                        this.Emit(
                            stepIndex,
                            VisionAssistantLogLevel.Warning,
                            Format("Vision_LogStepSkipped", step.Name));
                        stepCompleted = true;
                        continue;
                    }

                    result.FailedStepIndex = stepIndex;
                    result.Error = Format("Vision_LogStepTimeout", step.Name);
                    this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                    return result;
                }
            }

            result.Succeeded = true;
            this.Emit(-1, VisionAssistantLogLevel.Info, Text("Vision_LogCompleted"));
            return result;
        }

        private VisionWaitResult WaitForCondition(
            int stepIndex,
            VisionConditionDefinition condition,
            CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int consecutiveMatches = 0;
            VisionObservation lastObservation = null;
            while (stopwatch.ElapsedMilliseconds <= condition.TimeoutMilliseconds)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return VisionWaitResult.CancelledResult(lastObservation);
                }

                VisionObservation observation = this.observationProvider.Observe(
                    condition,
                    cancellationToken);
                lastObservation = observation;
                this.Emit(
                    stepIndex,
                    VisionAssistantLogLevel.Info,
                    this.DescribeObservation(condition, observation));
                if (observation != null && observation.IsTerminalFailure)
                {
                    return VisionWaitResult.TerminalFailureResult(observation);
                }
                string reason;
                bool matched = VisionConditionEvaluator.Matches(condition, observation, out reason);
                if (matched)
                {
                    consecutiveMatches++;
                    this.Emit(
                        stepIndex,
                        VisionAssistantLogLevel.Info,
                        Format(
                            "Vision_LogConditionConfirmation",
                            consecutiveMatches,
                            condition.RequiredConfirmations));
                    if (consecutiveMatches >= condition.RequiredConfirmations)
                    {
                        return VisionWaitResult.MatchedResult(lastObservation);
                    }
                }
                else
                {
                    consecutiveMatches = 0;
                    this.Emit(
                        stepIndex,
                        VisionAssistantLogLevel.Info,
                        string.IsNullOrEmpty(reason) ? Text("Vision_LogConditionNotMatched") : reason);
                }

                int remaining = condition.TimeoutMilliseconds - (int)stopwatch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    break;
                }
                int waitMilliseconds = Math.Min(condition.PollIntervalMilliseconds, remaining);
                if (cancellationToken.WaitHandle.WaitOne(waitMilliseconds))
                {
                    return VisionWaitResult.CancelledResult(lastObservation);
                }
            }

            return VisionWaitResult.TimedOutResult(lastObservation);
        }

        private VisionWaitResult WaitForVerification(
            int stepIndex,
            VisionConditionDefinition condition,
            CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= condition.MaxRetries + 1; attempt++)
            {
                this.Emit(
                    stepIndex,
                    VisionAssistantLogLevel.Info,
                    Format("Vision_LogVerificationWaiting", attempt));
                VisionWaitResult result = this.WaitForCondition(
                    stepIndex,
                    condition,
                    cancellationToken);
                if (result.Cancelled || result.Matched)
                {
                    return result;
                }
                if (attempt <= condition.MaxRetries)
                {
                    this.Emit(
                        stepIndex,
                        VisionAssistantLogLevel.Warning,
                        Text("Vision_LogVerificationRetry"));
                }
            }
            if (condition.FailurePolicy == VisionFailurePolicy.Skip)
            {
                return VisionWaitResult.SkippedResult();
            }
            return VisionWaitResult.TimedOutResult(null);
        }

        private string DescribeObservation(
            VisionConditionDefinition condition,
            VisionObservation observation)
        {
            if (observation == null)
            {
                return Text("Vision_LogObservationNoResult");
            }
            if (!string.IsNullOrEmpty(observation.Error))
            {
                return Format("Vision_LogObservationError", observation.Error);
            }
            if (observation.OcrResult != null)
            {
                string text = (observation.OcrResult.Text ?? string.Empty)
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();
                string description = Format(
                    "Vision_LogOcr",
                    text,
                    observation.OcrResult.Confidence,
                    observation.OcrResult.Success);
                return description + DescribeCaptureDiagnostics(observation);
            }
            if (observation.TemplateResult != null)
            {
                string description = Format(
                    "Vision_LogTemplate",
                    observation.TemplateResult.Found,
                    observation.TemplateResult.Similarity,
                    observation.TemplateResult.Location.X,
                    observation.TemplateResult.Location.Y);
                return description + DescribeCaptureDiagnostics(observation);
            }
            if (observation.ColorResult != null)
            {
                string description = Format(
                    "Vision_LogColor",
                    observation.ColorResult.Found,
                    observation.ColorResult.MatchCount,
                    observation.ColorResult.MatchRatio,
                    observation.ColorResult.MeanDistance);
                return description + DescribeCaptureDiagnostics(observation);
            }
            return Text("Vision_LogObservationEmpty");
        }

        private static string DescribeCaptureDiagnostics(VisionObservation observation)
        {
            if (observation == null || observation.CaptureMilliseconds <= 0 &&
                observation.EvaluationMilliseconds <= 0 &&
                string.IsNullOrEmpty(observation.CaptureSource))
            {
                return string.Empty;
            }
            string warning = string.IsNullOrWhiteSpace(observation.CaptureWarning)
                ? string.Empty
                : ", warning=" + observation.CaptureWarning;
            string snapshot = string.IsNullOrWhiteSpace(observation.DiagnosticSnapshotPath)
                ? string.Empty
                : ", snapshot=" + observation.DiagnosticSnapshotPath;
            return Format(
                "Vision_LogCaptureDiagnostics",
                observation.CaptureMilliseconds,
                observation.EvaluationMilliseconds,
                observation.CaptureSource,
                observation.CacheHit,
                observation.MeanBrightness,
                observation.Contrast,
                warning,
                snapshot);
        }

        private void Emit(int stepIndex, VisionAssistantLogLevel level, string message)
        {
            EventHandler<VisionAssistantLogEntry> handler = this.LogEmitted;
            if (handler != null)
            {
                handler(this, new VisionAssistantLogEntry(stepIndex, level, message));
            }
        }

        private static string Text(string key)
        {
            return WPELibrary.Properties.Resources.ResourceManager.GetString(key) ?? key;
        }

        private static string Format(string key, params object[] arguments)
        {
            return string.Format(Text(key), arguments);
        }

        private sealed class VisionWaitResult
        {
            public bool Matched { get; private set; }

            public bool Cancelled { get; private set; }

            public bool Skipped { get; private set; }

            public bool TerminalFailure { get; private set; }

            public VisionObservation LastObservation { get; private set; }

            public static VisionWaitResult MatchedResult(VisionObservation observation)
            {
                return new VisionWaitResult { Matched = true, LastObservation = observation };
            }

            public static VisionWaitResult CancelledResult(VisionObservation observation)
            {
                return new VisionWaitResult { Cancelled = true, LastObservation = observation };
            }

            public static VisionWaitResult TimedOutResult(VisionObservation observation)
            {
                return new VisionWaitResult { LastObservation = observation };
            }

            public static VisionWaitResult SkippedResult()
            {
                return new VisionWaitResult { Skipped = true };
            }

            public static VisionWaitResult TerminalFailureResult(VisionObservation observation)
            {
                return new VisionWaitResult
                {
                    TerminalFailure = true,
                    LastObservation = observation
                };
            }
        }
    }
}
