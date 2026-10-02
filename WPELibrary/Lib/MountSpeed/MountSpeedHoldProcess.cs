using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.MountSpeed
{
    internal sealed class MountSpeedHoldProcess : IDisposable
    {
        private const int MaxExitStderrCharacters = 4096;
        private readonly MountStatusAndroidSnapshotLaunchContext context;
        private readonly double target;
        private readonly MountStatusReadOnlySnapshot preflightSnapshot;
        private readonly Guid runId;
        private readonly Guid presetId;
        private readonly string presetName;
        private readonly object outputSync = new object();
        private readonly StringBuilder output = new StringBuilder();
        private readonly StringBuilder stderrOutput = new StringBuilder();
        private Process process;
        private int stopped;
        private int exitLogged;
        private bool processStarted;
        private bool logUnavailableReported;

        public MountSpeedHoldProcess(
            MountStatusAndroidSnapshotLaunchContext context,
            double target,
            long mountId,
            MountStatusReadOnlySnapshot preflightSnapshot,
            Guid runId,
            Guid presetId,
            string presetName)
        {
            this.context = context ?? throw new ArgumentNullException("context");
            this.target = target;
            this.MountId = mountId;
            this.preflightSnapshot = preflightSnapshot;
            this.runId = runId;
            this.presetId = presetId;
            this.presetName = presetName ?? string.Empty;
            this.JournalPath = Path.Combine(
                Path.GetTempPath(),
                "mount-speed-hold-" + Guid.NewGuid().ToString("N") + ".json");
        }

        public string JournalPath { get; private set; }

        internal static string RunLogPath
        {
            get { return MountSpeedHoldRunLog.LogFilePath; }
        }

        internal static bool TryAppendRunLog(
            Guid runId,
            Guid presetId,
            string presetName,
            string source,
            string eventName,
            object data,
            out string error)
        {
            return MountSpeedHoldRunLog.TryAppend(
                runId,
                presetId,
                presetName,
                source,
                eventName,
                data,
                out error);
        }

        public void Start()
        {
            string scriptPath;
            try
            {
                scriptPath = ResolveScriptPath(this.context.ProbePath);
            }
            catch (Exception exception)
            {
                this.Log("helperStartFailed", new { diagnostic = exception.Message });
                throw;
            }
            if (string.IsNullOrWhiteSpace(this.context.RootBrokerPipe))
            {
                InvalidOperationException exception =
                    new InvalidOperationException("只读探针未提供有效的 root broker pipe。");
                this.Log("helperStartFailed", new { diagnostic = exception.Message });
                throw exception;
            }

            List<string> arguments = new List<string>
            {
                scriptPath,
                "hold",
                "--persistent",
                "--write",
                "--speed",
                this.target.ToString("R", CultureInfo.InvariantCulture),
                "--adb",
                this.context.AdbPath,
                "--serial",
                this.context.Serial,
                "--pid",
                this.context.ProcessId.ToString(CultureInfo.InvariantCulture),
                "--mount-id",
                this.MountId.ToString(CultureInfo.InvariantCulture),
                "--interval",
                "0.1",
                "--journal",
                this.JournalPath,
                "--root-broker-pipe",
                this.context.RootBrokerPipe
            };
            arguments.Add("--log-path");
            arguments.Add(RunLogPath);
            arguments.Add("--run-id");
            arguments.Add(this.runId.ToString("D", CultureInfo.InvariantCulture));
            arguments.Add("--preset-name");
            arguments.Add(this.presetName);
            arguments.Add("--preset-id");
            arguments.Add(this.presetId.ToString("D", CultureInfo.InvariantCulture));
            this.AddPreflightBindingArguments(arguments);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = this.context.PythonPath,
                Arguments = JoinArguments(arguments.ToArray()),
                WorkingDirectory = Path.GetDirectoryName(scriptPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            this.process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            this.process.OutputDataReceived += this.OnOutput;
            this.process.ErrorDataReceived += this.OnError;
            this.Log("helperStarting", new
            {
                pythonPath = this.context.PythonPath,
                processId = this.context.ProcessId,
                mountId = this.MountId,
                targetSpeed = this.target,
                intervalSeconds = 0.1,
                journalPath = this.JournalPath,
                logPath = RunLogPath
            });
            try
            {
                if (!this.process.Start())
                {
                    throw new InvalidOperationException("坐骑速度保持子进程启动失败。");
                }
                this.processStarted = true;
                this.process.BeginOutputReadLine();
                this.process.BeginErrorReadLine();
            }
            catch (Exception exception)
            {
                if (!this.processStarted)
                {
                    this.Log("helperStartFailed", new { diagnostic = exception.Message });
                }
                throw;
            }
        }

        private long MountId { get; set; }

        public void WaitForStop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.WaitHandle.WaitOne(250))
            {
                if (this.process.HasExited)
                {
                    this.CheckExitCode();
                    throw new InvalidOperationException("坐骑速度保持子进程意外退出。");
                }
            }
        }

        public void StopAndWait()
        {
            if (Interlocked.Exchange(ref this.stopped, 1) != 0 ||
                this.process == null || !this.processStarted)
            {
                return;
            }

            try
            {
                if (!this.process.HasExited)
                {
                    this.process.StandardInput.WriteLine("stop");
                    this.process.StandardInput.Flush();
                }
            }
            catch (IOException)
            {
                // Closing stdin still requests the child's documented EOF cleanup.
            }
            finally
            {
                try
                {
                    this.process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The child may have closed its pipe while exiting.
                }
            }

            this.process.WaitForExit();
            this.process.WaitForExit();
            this.CheckExitCode();
        }

        public void Dispose()
        {
            try
            {
                this.StopAndWait();
            }
            finally
            {
                if (this.process != null)
                {
                    this.process.Dispose();
                    this.process = null;
                }
            }
        }

        internal static string WriteGrowthRateOnce(
            MountStatusAndroidSnapshotLaunchContext context,
            long expectedStartTicks,
            string expectedExecutable,
            int playerId,
            long mountId,
            double expectedGrowthRate,
            ulong expectedGrowthAddress,
            string rideInstanceId,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            if (string.IsNullOrWhiteSpace(context.ProbePath) ||
                string.IsNullOrWhiteSpace(context.PythonPath) ||
                string.IsNullOrWhiteSpace(context.AdbPath) ||
                string.IsNullOrWhiteSpace(context.Serial) ||
                string.IsNullOrWhiteSpace(context.RootBrokerPipe) ||
                context.ProcessId <= 0 || playerId <= 0 || mountId <= 0 ||
                expectedStartTicks <= 0 || string.IsNullOrWhiteSpace(expectedExecutable) ||
                expectedGrowthAddress == 0 ||
                string.IsNullOrWhiteSpace(rideInstanceId))
            {
                throw new InvalidOperationException("当前坐骑写入所需的 Android 进程上下文不完整。");
            }

            string scriptPath = Path.Combine(
                Path.GetDirectoryName(context.ProbePath) ?? string.Empty,
                "mount_growth_rate_memory_test.py");
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("未找到坐骑成长率单次写入脚本。", scriptPath);
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = context.PythonPath,
                Arguments = JoinArguments(
                    scriptPath,
                    "--adb",
                    context.AdbPath,
                    "--serial",
                    context.Serial,
                    "--pid",
                    context.ProcessId.ToString(CultureInfo.InvariantCulture),
                    "--expected-start-ticks",
                    expectedStartTicks.ToString(CultureInfo.InvariantCulture),
                    "--expected-exe",
                    expectedExecutable,
                    "--ride-instance-id",
                    rideInstanceId,
                    "--expected-player-id",
                    playerId.ToString(CultureInfo.InvariantCulture),
                    "--expected-mount-id",
                    mountId.ToString(CultureInfo.InvariantCulture),
                    "--expected-growth-rate",
                    expectedGrowthRate.ToString("R", CultureInfo.InvariantCulture),
                    "--expected-growth-address",
                    "0x" + expectedGrowthAddress.ToString("X", CultureInfo.InvariantCulture),
                    "--target",
                    "100.0",
                    "--root-broker-pipe",
                    context.RootBrokerPipe,
                    "--write"),
                WorkingDirectory = Path.GetDirectoryName(scriptPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("坐骑成长率写入子进程启动失败。");
                }

                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                // Once started, let the one-shot process finish so it can report
                // whether the memory write and read-back completed or became ambiguous.
                if (!process.WaitForExit(600000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // It exited between the timeout check and Kill.
                    }
                    process.WaitForExit();
                    throw new TimeoutException(
                        "坐骑成长率写入子进程超时；写入结果不明确，请刷新快照核对后再操作。");
                }
                process.WaitForExit();

                string output = standardOutput.Result.Trim();
                if (string.IsNullOrWhiteSpace(output))
                {
                    string details = standardError.Result.Trim();
                    throw new InvalidOperationException(
                        "坐骑成长率写入子进程未返回结果" +
                        (string.IsNullOrWhiteSpace(details) ? "。" : "：" + details));
                }
                return output;
            }
        }

        private void CheckExitCode()
        {
            if (this.process.HasExited && Interlocked.Exchange(ref this.exitLogged, 1) == 0)
            {
                int exitCode = this.process.ExitCode;
                string stderrDiagnostic = string.Empty;
                if (exitCode != 0)
                {
                    lock (this.outputSync)
                    {
                        stderrDiagnostic = this.stderrOutput.ToString().Trim();
                    }
                }
                Dictionary<string, object> exitData = new Dictionary<string, object>
                {
                    { "exitCode", exitCode },
                    { "journalExists", File.Exists(this.JournalPath) },
                    { "journalPath", this.JournalPath }
                };
                if (exitCode != 0)
                {
                    exitData.Add("stderrDiagnostic", stderrDiagnostic);
                }
                this.Log("helperExited", exitData);
            }
            if (this.process.ExitCode != 0)
            {
                string details;
                lock (this.outputSync)
                {
                    details = this.output.ToString().Trim();
                }
                string journalStatus = File.Exists(this.JournalPath)
                    ? "；恢复日志已保留于 " + this.JournalPath
                    : "；未找到恢复日志：" + this.JournalPath;
                throw new InvalidOperationException(
                    "坐骑速度保持子进程退出码 " + this.process.ExitCode +
                    (string.IsNullOrWhiteSpace(details) ? string.Empty : "：" + details) +
                    journalStatus);
            }
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }
            lock (this.outputSync)
            {
                this.output.AppendLine(e.Data);
            }
            Socket_Operation.DoLog("MountSpeedHold", e.Data);
        }

        private void OnError(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }
            lock (this.outputSync)
            {
                this.stderrOutput.AppendLine(e.Data);
                if (this.stderrOutput.Length > MaxExitStderrCharacters)
                {
                    this.stderrOutput.Remove(0, this.stderrOutput.Length - MaxExitStderrCharacters);
                }
                this.output.AppendLine(e.Data);
            }
            Socket_Operation.DoLog("MountSpeedHold", e.Data);
        }

        private void Log(string eventName, object data)
        {
            string error;
            if (!TryAppendRunLog(
                this.runId,
                this.presetId,
                this.presetName,
                "desktop",
                eventName,
                data,
                out error) && !this.logUnavailableReported)
            {
                this.logUnavailableReported = true;
                Socket_Operation.DoLog("MountSpeedHold", "速度保持结构化日志写入失败：" + error);
            }
        }

        private void AddPreflightBindingArguments(ICollection<string> arguments)
        {
            MountStatusReadOnlySnapshot snapshot = this.preflightSnapshot;
            if (snapshot == null ||
                !snapshot.IsValid ||
                snapshot.IsMounted != true ||
                !snapshot.MountId.HasValue ||
                snapshot.MountId.Value != this.MountId ||
                snapshot.PlayerId <= 0 ||
                snapshot.Process == null ||
                snapshot.Process.Pid != this.context.ProcessId ||
                snapshot.Process.StartTicks <= 0 ||
                string.IsNullOrWhiteSpace(snapshot.Process.Executable) ||
                !snapshot.RoleMoveSpeedMemoryAddress.HasValue)
            {
                return;
            }

            arguments.Add("--preflight-player-id");
            arguments.Add(snapshot.PlayerId.ToString(CultureInfo.InvariantCulture));
            arguments.Add("--preflight-start-ticks");
            arguments.Add(snapshot.Process.StartTicks.ToString(CultureInfo.InvariantCulture));
            arguments.Add("--preflight-exe");
            arguments.Add(snapshot.Process.Executable);
            arguments.Add("--preflight-speed-address");
            arguments.Add("0x" + snapshot.RoleMoveSpeedMemoryAddress.Value.ToString(
                "X",
                CultureInfo.InvariantCulture));
        }

        private static string JoinArguments(params string[] values)
        {
            StringBuilder result = new StringBuilder();
            foreach (string value in values)
            {
                if (result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(QuoteArgument(value));
            }
            return result.ToString();
        }

        internal static string QuoteArgument(string value)
        {
            value = value ?? string.Empty;
            StringBuilder result = new StringBuilder(value.Length + 2);
            result.Append('"');
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }

                result.Append('\\', backslashes);
                result.Append(character);
                backslashes = 0;
            }
            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }

        private static string ResolveScriptPath(string probePath)
        {
            string adjacent = Path.Combine(
                Path.GetDirectoryName(probePath) ?? string.Empty,
                "mount_speed_memory_test.py");
            if (File.Exists(adjacent))
            {
                return adjacent;
            }

            string configured = Environment.GetEnvironmentVariable(
                "WPE_MOUNT_SPEED_MEMORY_TEST_PY");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return Path.GetFullPath(configured);
            }

            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int depth = 0; directory != null && depth < 5; depth++, directory = directory.Parent)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "tools",
                    "mount-reader",
                    "mount_speed_memory_test.py");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException(
                "未找到坐骑速度保持脚本，请设置 WPE_MOUNT_SPEED_MEMORY_TEST_PY。",
                adjacent);
        }
    }

    internal static class MountSpeedHoldRunLog
    {
        private const int SchemaVersion = 1;
        private const long MaxFileBytes = 5L * 1024L * 1024L;
        // ponytail: 105 MiB rolling history cap; use per-run files if tests need longer retention.
        private const int ArchiveCount = 20;
        private static readonly object FileSync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string LogFilePath
        {
            get
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localAppData))
                {
                    localAppData = Path.GetTempPath();
                }
                return Path.Combine(localAppData, "XNAS", "WPE", "mount-speed-hold", "logs", "mount-speed-hold.jsonl");
            }
        }

        public static bool TryAppend(
            Guid runId,
            Guid presetId,
            string presetName,
            string source,
            string eventName,
            object data,
            out string error)
        {
            error = string.Empty;
            try
            {
                JToken dataToken = data == null ? new JObject() : JToken.FromObject(data);
                JObject dataObject = dataToken as JObject ?? new JObject { ["value"] = dataToken };
                JObject record = new JObject
                {
                    ["schemaVersion"] = SchemaVersion,
                    ["timestampUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ["runId"] = runId.ToString("D", CultureInfo.InvariantCulture),
                    ["presetName"] = presetName ?? string.Empty,
                    ["presetId"] = presetId.ToString("D", CultureInfo.InvariantCulture),
                    ["source"] = source ?? string.Empty,
                    ["event"] = eventName ?? string.Empty,
                    ["data"] = dataObject
                };
                string line = record.ToString(Formatting.None);
                int upcomingBytes = Utf8NoBom.GetByteCount(line + Environment.NewLine);
                lock (FileSync)
                {
                    string directory = Path.GetDirectoryName(LogFilePath);
                    Directory.CreateDirectory(directory);
                    RotateIfRequired(LogFilePath, upcomingBytes);
                    using (FileStream stream = new FileStream(
                        LogFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite))
                    using (StreamWriter writer = new StreamWriter(stream, Utf8NoBom))
                    {
                        writer.WriteLine(line);
                        writer.Flush();
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void RotateIfRequired(string path, int upcomingBytes)
        {
            FileInfo active = new FileInfo(path);
            if (!active.Exists || active.Length + upcomingBytes <= MaxFileBytes)
            {
                return;
            }

            string oldestArchive = GetArchivePath(path, ArchiveCount);
            if (File.Exists(oldestArchive))
            {
                File.Delete(oldestArchive);
            }
            for (int index = ArchiveCount - 1; index >= 1; index--)
            {
                string source = GetArchivePath(path, index);
                if (File.Exists(source))
                {
                    File.Move(source, GetArchivePath(path, index + 1));
                }
            }
            File.Move(path, GetArchivePath(path, 1));
        }

        private static string GetArchivePath(string path, int index)
        {
            return path + "." + index.ToString(CultureInfo.InvariantCulture);
        }
    }
}
