using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.MountSpeed
{
    internal static class MountSpeedHoldRestartSupervisor
    {
        internal enum ChildOutcome
        {
            Completed,
            SafeRecoveredFailure,
            Failure
        }

        internal static bool IsEligible(string presetName, Guid presetId, int instructionCount, int instructionType, int mountSpeedHoldType)
        {
            return string.Equals(presetName, "坐骑速度保持", StringComparison.Ordinal) &&
                presetId != Guid.Empty && instructionCount == 1 && instructionType == mountSpeedHoldType;
        }

        internal static bool IsSafeRecoveredFailure(string logPath, Guid runId, Guid presetId, string presetName, string journalPath)
        {
            if (runId == Guid.Empty || presetId == Guid.Empty || string.IsNullOrWhiteSpace(presetName) ||
                string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(journalPath)) return false;
            try
            {
                bool foundAnyLog = false;
                JObject match = null;
                for (int archiveIndex = 0; archiveIndex <= 20; archiveIndex++)
                {
                    string path = archiveIndex == 0 ? logPath : logPath + "." + archiveIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!File.Exists(path)) continue;
                    foundAnyLog = true;
                    foreach (string line in File.ReadLines(path))
                    {
                        if (string.IsNullOrWhiteSpace(line)) return false;
                        JObject record = JObject.Parse(line);
                        if (string.Equals((string)record["runId"], runId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
                            string.Equals((string)record["source"], "reader", StringComparison.Ordinal) &&
                            string.Equals((string)record["event"], "runEnd", StringComparison.Ordinal))
                        {
                            if (match != null) return false;
                            match = record;
                        }
                    }
                }
                if (!foundAnyLog || match == null ||
                    !string.Equals((string)match["presetId"], presetId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals((string)match["presetName"], presetName, StringComparison.Ordinal)) return false;
                JObject data = match["data"] as JObject;
                return data != null &&
                    string.Equals((string)data["outcome"], "failure-recovered", StringComparison.Ordinal) &&
                    string.Equals((string)data["journal"], "deleted", StringComparison.Ordinal) &&
                    string.Equals((string)data["status"], "error", StringComparison.Ordinal) &&
                    !File.Exists(journalPath);
            }
            catch { return false; }
        }

        internal static bool Run(
            CancellationToken cancellationToken,
            bool eligible,
            Func<CancellationToken, bool> preflight,
            Func<CancellationToken, ChildOutcome> runChild,
            Action<int, CancellationToken> delay)
        {
            if (preflight == null) throw new ArgumentNullException("preflight");
            if (runChild == null) throw new ArgumentNullException("runChild");
            if (delay == null) throw new ArgumentNullException("delay");
            if (cancellationToken.IsCancellationRequested) return false;
            ChildOutcome outcome = runChild(cancellationToken);
            int retryCount = 0;
            while (outcome == ChildOutcome.SafeRecoveredFailure && eligible)
            {
                if (cancellationToken.IsCancellationRequested) return false;
                int backoff = retryCount == 0 ? 0 : (int)Math.Min(5000, 250L << Math.Min(retryCount - 1, 5));
                delay(backoff, cancellationToken);
                if (cancellationToken.IsCancellationRequested || !preflight(cancellationToken)) return false;
                outcome = runChild(cancellationToken);
                if (retryCount < 6) retryCount++;
            }
            return outcome == ChildOutcome.Completed;
        }
    }
}
