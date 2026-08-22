using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace WinsockPacketEditor
{
    /// <summary>
    /// Resolves the current injection target from process identity instead of
    /// persisting a PID.  PIDs are intentionally treated as short-lived;
    /// after a game/network-process restart the newest matching instance is
    /// selected again.
    /// </summary>
    internal static class InjectionTargetResolver
    {
        // WPE hooks the emulator's Windows Winsock host.  The companion and
        // real-acceptance harness both use Ld9BoxHeadless as that host; the
        // separate 0dcloudCore process does not own the WPE management socket.
        private const string PreferredNetworkProcessName = "Ld9BoxHeadless";

        internal static bool TryResolveLatestPreferred(out Process process)
        {
            return TryResolveLatest(
                new[] { PreferredNetworkProcessName },
                out process);
        }

        internal static bool TryResolveLatestSupported(out Process process)
        {
            return TryResolveLatest(
                ProcessList_Form.GetSupportedInjectionProcessNames(),
                out process);
        }

        private static bool TryResolveLatest(
            IEnumerable<string> processNames,
            out Process process)
        {
            process = null;
            if (processNames == null)
            {
                return false;
            }

            List<Process> candidates = new List<Process>();
            HashSet<string> visitedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string processName in processNames)
                {
                    if (string.IsNullOrWhiteSpace(processName) ||
                        !visitedNames.Add(processName))
                    {
                        continue;
                    }

                    Process[] matches;
                    try
                    {
                        matches = Process.GetProcessesByName(processName);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (Process match in matches)
                    {
                        if (match != null)
                        {
                            candidates.Add(match);
                        }
                    }
                }

                foreach (Process candidate in candidates)
                {
                    if (process == null || IsNewer(candidate, process))
                    {
                        process = candidate;
                    }
                }

                if (process == null)
                {
                    return false;
                }

                foreach (Process candidate in candidates)
                {
                    if (!object.ReferenceEquals(candidate, process))
                    {
                        candidate.Dispose();
                    }
                }

                return true;
            }
            catch
            {
                if (process != null)
                {
                    process.Dispose();
                    process = null;
                }
                return false;
            }
            finally
            {
                if (process == null)
                {
                    foreach (Process candidate in candidates)
                    {
                        candidate.Dispose();
                    }
                }
            }
        }

        private static bool IsNewer(Process candidate, Process current)
        {
            long candidateStart = ReadStartTicks(candidate);
            long currentStart = ReadStartTicks(current);
            if (candidateStart != currentStart)
            {
                return candidateStart > currentStart;
            }

            // If access to StartTime is restricted, the PID tie-breaker is a
            // deterministic fallback and does not pretend to be a timestamp.
            return candidate.Id > current.Id;
        }

        private static long ReadStartTicks(Process process)
        {
            try
            {
                return process.StartTime.ToUniversalTime().Ticks;
            }
            catch
            {
                try
                {
                    using (ManagementObjectSearcher searcher =
                        new ManagementObjectSearcher(
                            "SELECT CreationDate FROM Win32_Process WHERE ProcessId = " +
                            process.Id.ToString()))
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject result in results)
                        {
                            object creationDate = result["CreationDate"];
                            if (creationDate != null)
                            {
                                return ManagementDateTimeConverter
                                    .ToDateTime(creationDate.ToString())
                                    .ToUniversalTime()
                                    .Ticks;
                            }
                        }
                    }
                }
                catch
                {
                    // The PID tie-breaker remains the final safe fallback.
                }

                return 0;
            }
        }
    }
}
