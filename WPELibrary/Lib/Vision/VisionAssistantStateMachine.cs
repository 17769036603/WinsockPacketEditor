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
                    result.Error = "Assistant cancelled.";
                    this.Emit(stepIndex, VisionAssistantLogLevel.Warning, result.Error);
                    return result;
                }

                VisionAssistantStep step = steps[stepIndex];
                if (step == null || step.Condition == null)
                {
                    result.FailedStepIndex = stepIndex;
                    result.Error = "A state-machine step has no condition.";
                    this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                    return result;
                }

                try
                {
                    step.Condition.Validate();
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
                        string.Format("Waiting for step '{0}', attempt {1}.", step.Name, attempt));
                    VisionWaitResult waitResult = this.WaitForCondition(
                        stepIndex,
                        step.Condition,
                        cancellationToken);
                    if (waitResult.Cancelled)
                    {
                        result.Cancelled = true;
                        result.Error = "Assistant cancelled while waiting for a vision condition.";
                        return result;
                    }
                    if (waitResult.Matched)
                    {
                        this.Emit(
                            stepIndex,
                            VisionAssistantLogLevel.Info,
                            string.Format("Step '{0}' condition confirmed.", step.Name));
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
                                    ? "Vision action returned no result."
                                    : actionResult.Error;
                                this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                                return result;
                            }
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
                            string.Format("Step '{0}' timed out; retrying.", step.Name));
                        continue;
                    }

                    if (step.Condition.FailurePolicy == VisionFailurePolicy.Skip)
                    {
                        result.SkippedStep = true;
                        this.Emit(
                            stepIndex,
                            VisionAssistantLogLevel.Warning,
                            string.Format("Step '{0}' timed out; skipped.", step.Name));
                        stepCompleted = true;
                        continue;
                    }

                    result.FailedStepIndex = stepIndex;
                    result.Error = string.Format("Step '{0}' timed out.", step.Name);
                    this.Emit(stepIndex, VisionAssistantLogLevel.Error, result.Error);
                    return result;
                }
            }

            result.Succeeded = true;
            this.Emit(-1, VisionAssistantLogLevel.Info, "Assistant state machine completed.");
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
                string reason;
                bool matched = VisionConditionEvaluator.Matches(condition, observation, out reason);
                if (matched)
                {
                    consecutiveMatches++;
                    this.Emit(
                        stepIndex,
                        VisionAssistantLogLevel.Info,
                        string.Format(
                            "Condition confirmation {0}/{1}.",
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
                        string.IsNullOrEmpty(reason) ? "Condition not matched." : reason);
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

        private string DescribeObservation(
            VisionConditionDefinition condition,
            VisionObservation observation)
        {
            if (observation == null)
            {
                return "Observation: no result.";
            }
            if (!string.IsNullOrEmpty(observation.Error))
            {
                return "Observation error: " + observation.Error;
            }
            if (observation.OcrResult != null)
            {
                string text = (observation.OcrResult.Text ?? string.Empty)
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();
                string description = string.Format(
                    "OCR text='{0}', confidence={1:P1}, success={2}.",
                    text,
                    observation.OcrResult.Confidence,
                    observation.OcrResult.Success);
                return description + DescribeCaptureDiagnostics(observation);
            }
            if (observation.TemplateResult != null)
            {
                string description = string.Format(
                    "Template found={0}, similarity={1:P1}, location=({2},{3}).",
                    observation.TemplateResult.Found,
                    observation.TemplateResult.Similarity,
                    observation.TemplateResult.Location.X,
                    observation.TemplateResult.Location.Y);
                return description + DescribeCaptureDiagnostics(observation);
            }
            return "Observation: no OCR or template result.";
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
            return string.Format(
                " capture={0}ms, eval={1}ms, source={2}, cache={3}, brightness={4:0.0}, contrast={5:0.0}{6}{7}.",
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

        private sealed class VisionWaitResult
        {
            public bool Matched { get; private set; }

            public bool Cancelled { get; private set; }

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
        }
    }
}
