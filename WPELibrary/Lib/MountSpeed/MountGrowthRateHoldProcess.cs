using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>Launches the guarded growth write and its matching restore action.</summary>
    internal static class MountGrowthRateHoldProcess
    {
        internal static string SetAndRestore(
            MountStatusAndroidSnapshotLaunchContext context, long startTicks, string executable,
            int playerId, long mountId, string rideInstanceId, double startupGrowthRate,
            ulong startupGrowthAddress, string journalPath, CancellationToken cancellationToken)
        {
            string setResult = "{\"status\":\"error\",\"writeAttempted\":false}";
            string restoreResult = "{\"status\":\"skipped\",\"written\":false}";
            bool restoreNeeded = false;
            bool verified = false;
            try
            {
                setResult = SetAndJournal(context, startTicks, executable, playerId, mountId,
                    rideInstanceId, startupGrowthRate, startupGrowthAddress, journalPath);
                Newtonsoft.Json.Linq.JObject set = Newtonsoft.Json.Linq.JObject.Parse(setResult);
                verified = string.Equals((string)set["status"], "field_verified", StringComparison.Ordinal);
                restoreNeeded = set["writeAttempted"] != null && (bool)set["writeAttempted"];
                if (verified) cancellationToken.WaitHandle.WaitOne();
            }
            catch (Exception error)
            {
                setResult = "{\"status\":\"error\",\"error\":" + JsonString(error.Message) + "}";
            }
            finally
            {
                restoreNeeded = restoreNeeded || File.Exists(journalPath);
                if (restoreNeeded && File.Exists(journalPath))
                {
                    try
                    {
                        restoreResult = Restore(context, startTicks, executable, playerId, mountId,
                            rideInstanceId, startupGrowthRate, startupGrowthAddress, journalPath);
                    }
                    catch (Exception error)
                    {
                        restoreResult = "{\"status\":\"ambiguous\",\"writeAttempted\":false,\"error\":" + JsonString(error.Message) + "}";
                    }
                }
                else if (verified)
                {
                    restoreResult = "{\"status\":\"not_needed\",\"written\":false,\"writeAttempted\":false}";
                }
            }
            string setStatus = StatusOf(setResult);
            string restoreStatus = StatusOf(restoreResult);
            return "{\"setStatus\":" + JsonString(setStatus) + ",\"restoreStatus\":" +
                JsonString(restoreStatus) + ",\"setResult\":" + setResult +
                ",\"restoreResult\":" + restoreResult + "}";
        }

        internal static string SetAndJournal(
            MountStatusAndroidSnapshotLaunchContext context,
            long startTicks, string executable, int playerId, long mountId,
            string rideInstanceId, double growthRate, ulong growthAddress, string journalPath)
        {
            return Run(context, "set", startTicks, executable, playerId, mountId,
                rideInstanceId, growthRate, growthAddress, journalPath, true);
        }

        internal static string Restore(
            MountStatusAndroidSnapshotLaunchContext context,
            long startTicks, string executable, int playerId, long mountId,
            string rideInstanceId, double startupGrowthRate, ulong growthAddress, string journalPath)
        {
            return Run(context, "restore", startTicks, executable, playerId, mountId,
                rideInstanceId, startupGrowthRate, growthAddress, journalPath, true);
        }

        private static string Run(
            MountStatusAndroidSnapshotLaunchContext context, string action,
            long startTicks, string executable, int playerId, long mountId,
            string rideInstanceId, double growthRate, ulong growthAddress,
            string journalPath, bool write)
        {
            if (context == null || context.ProcessId <= 0 || startTicks <= 0 ||
                string.IsNullOrWhiteSpace(executable) || playerId <= 0 || mountId <= 0 ||
                string.IsNullOrWhiteSpace(rideInstanceId) || growthAddress == 0 ||
                string.IsNullOrWhiteSpace(journalPath) || string.IsNullOrWhiteSpace(context.RootBrokerPipe))
                throw new InvalidOperationException("坐骑成长率预设上下文不完整。");

            string script = Path.Combine(Path.GetDirectoryName(context.ProbePath) ?? string.Empty,
                "mount_growth_rate_memory_test.py");
            if (!File.Exists(script)) throw new FileNotFoundException("未找到成长率写入脚本。", script);
            StringBuilder args = new StringBuilder();
            Add(args, action); Add(args, "--adb"); Add(args, context.AdbPath);
            Add(args, "--serial"); Add(args, context.Serial);
            Add(args, "--pid"); Add(args, context.ProcessId.ToString(CultureInfo.InvariantCulture));
            Add(args, "--expected-start-ticks"); Add(args, startTicks.ToString(CultureInfo.InvariantCulture));
            Add(args, "--expected-exe"); Add(args, executable);
            Add(args, "--ride-instance-id"); Add(args, rideInstanceId);
            Add(args, "--expected-player-id"); Add(args, playerId.ToString(CultureInfo.InvariantCulture));
            Add(args, "--expected-mount-id"); Add(args, mountId.ToString(CultureInfo.InvariantCulture));
            Add(args, "--expected-growth-address"); Add(args, "0x" + growthAddress.ToString("X", CultureInfo.InvariantCulture));
            Add(args, "--journal"); Add(args, journalPath);
            Add(args, "--root-broker-pipe"); Add(args, context.RootBrokerPipe);
            Add(args, "--expected-growth-rate"); Add(args, growthRate.ToString("R", CultureInfo.InvariantCulture));
            if (action == "set") { Add(args, "--target"); Add(args, "100.0"); }
            if (write) Add(args, "--write");

            ProcessStartInfo info = new ProcessStartInfo(context.PythonPath, args.ToString())
            {
                WorkingDirectory = Path.GetDirectoryName(script), UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (Process process = Process.Start(info))
            {
                System.Threading.Tasks.Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(600000))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                    process.WaitForExit();
                    throw new TimeoutException("坐骑成长率操作超时；恢复日志保留于：" + journalPath);
                }
                process.WaitForExit();
                string output = outputTask.Result;
                string error = errorTask.Result;
                if (string.IsNullOrWhiteSpace(output))
                    throw new InvalidOperationException("成长率操作没有返回结果。 " + error);
                return output.Trim();
            }
        }

        private static void Add(StringBuilder args, string value)
        {
            if (args.Length != 0) args.Append(' ');
            args.Append(Quote(value));
        }

        private static string Quote(string value)
        {
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value ?? string.Empty)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                result.Append('\\', slashes).Append(c); slashes = 0;
            }
            result.Append('\\', slashes * 2).Append('"');
            return result.ToString();
        }

        private static string JsonString(string value)
        {
            return Newtonsoft.Json.JsonConvert.ToString(value ?? string.Empty);
        }

        private static string StatusOf(string json)
        {
            try { return (string)Newtonsoft.Json.Linq.JObject.Parse(json)["status"] ?? "invalid"; }
            catch { return "invalid"; }
        }
    }
}
