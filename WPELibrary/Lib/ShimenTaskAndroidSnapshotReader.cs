using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WPELibrary.Lib.Android;

namespace WPELibrary.Lib
{
    /// <summary>
    /// Windows-to-ADB/Python bridge for the Android LuaJIT task snapshot.
    /// The bridge only reads /proc/&lt;pid&gt;/mem through the shared Root session;
    /// it has no memory-write, OCR, input, or packet-send path.
    /// </summary>
    public sealed class ShimenTaskAndroidSnapshotReader : IShimenTaskMemoryReader
    {
        private const int DefaultTimeoutMilliseconds = 90000;
        private const string DefaultPackage = "com.gdoo.yzqcxy";
        private const string DefaultReaderScript =
            @"F:\项目目录\飘渺游戏助手\tools\treasure-reader\shimen_task_memory_reader.py";
        private readonly string configuredAdbPath;
        private readonly string configuredSerial;
        private readonly string configuredPackage;
        private readonly string configuredPythonPath;
        private readonly string configuredReaderScript;
        private readonly string configuredTaskListAddress;
        private readonly string configuredCachePath;

        public ShimenTaskAndroidSnapshotReader()
            : this(null, null, null, null, null, null, null)
        {
        }

        public ShimenTaskAndroidSnapshotReader(
            string adbPath,
            string deviceSerial,
            string gamePackage,
            string pythonPath,
            string readerScriptPath,
            string taskListAddress,
            string cachePath)
        {
            this.configuredAdbPath = adbPath;
            this.configuredSerial = deviceSerial;
            this.configuredPackage = gamePackage;
            this.configuredPythonPath = pythonPath;
            this.configuredReaderScript = readerScriptPath;
            this.configuredTaskListAddress = taskListAddress;
            this.configuredCachePath = cachePath;
        }

        public bool IsConfigured
        {
            get
            {
                return File.Exists(this.ResolveAdbPath()) &&
                    File.Exists(this.ResolvePythonPath()) &&
                    File.Exists(this.ResolveReaderScriptPath()) &&
                    IsSafePackageName(this.ResolvePackage());
            }
        }

        public string BuildCommandLine(string cachePath, string rootBrokerPipe)
        {
            return this.BuildCommandLine(
                cachePath,
                rootBrokerPipe,
                0,
                this.ResolveSerialForCommand());
        }

        private string BuildCommandLine(
            string cachePath,
            string rootBrokerPipe,
            int pid,
            string serial)
        {
            string adb = this.ResolveAdbPath();
            string script = this.ResolveReaderScriptPath();
            string resolvedSerial = string.IsNullOrWhiteSpace(serial)
                ? this.ResolveSerialForCommand()
                : serial.Trim();
            string packageName = this.ResolvePackage();
            List<string> arguments = new List<string>
            {
                "-u",
                Quote(script),
                "--adb",
                Quote(adb),
                "--serial",
                Quote(resolvedSerial),
                "--package",
                Quote(packageName),
                "--pid",
                pid.ToString(),
                "--cache-path",
                Quote(cachePath),
                "--read-timeout",
                "12",
                "--max-task-slots",
                "128"
            };
            if (!string.IsNullOrWhiteSpace(this.configuredTaskListAddress))
            {
                arguments.Add("--task-list-address");
                arguments.Add(Quote(this.configuredTaskListAddress.Trim()));
            }
            if (!string.IsNullOrWhiteSpace(rootBrokerPipe))
            {
                arguments.Add("--root-broker-pipe");
                arguments.Add(Quote(rootBrokerPipe));
            }
            return string.Join(" ", arguments.ToArray());
        }

        public async Task<ShimenTaskMemoryReadResult> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(
                    () => this.ReadSnapshot(cancellationToken),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ShimenTaskMemoryReadResult
                {
                    Error = "师门任务内存读取已取消。",
                    WasCancelled = true
                };
            }
            catch (Exception ex)
            {
                return new ShimenTaskMemoryReadResult
                {
                    Error = "师门任务内存读取失败：" + Compact(ex.Message)
                };
            }
        }

        public void Dispose()
        {
            // Each read owns only a short-lived Root lease and child process;
            // both are disposed inside ReadSnapshot.
        }

        private ShimenTaskMemoryReadResult ReadSnapshot(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string adb = this.ResolveAdbPath();
            string python = this.ResolvePythonPath();
            string script = this.ResolveReaderScriptPath();
            string packageName = this.ResolvePackage();
            if (string.IsNullOrWhiteSpace(adb) || !File.Exists(adb))
            {
                return Failure("未找到 adb.exe；可通过 WPE_ADB_PATH 配置。");
            }
            if (string.IsNullOrWhiteSpace(python) || !File.Exists(python))
            {
                return Failure("未找到 Python 解释器；可通过 WPE_PYTHON_PATH 配置。");
            }
            if (string.IsNullOrWhiteSpace(script) || !File.Exists(script))
            {
                return Failure(
                    "未找到 shimen_task_memory_reader.py；可通过 WPE_SHIMEN_TASK_READER_PATH 配置。");
            }
            if (!IsSafePackageName(packageName))
            {
                return Failure("游戏包名配置无效。");
            }

            string devicesOutput;
            ProcessResult devices = RunProcess(
                adb,
                BuildArguments("devices"),
                cancellationToken,
                20000);
            if (!devices.Succeeded)
            {
                return Failure("ADB 不可用：" + Compact(devices.Error));
            }
            devicesOutput = devices.Output;
            string serial = SelectDeviceSerial(devicesOutput, this.configuredSerial);
            if (string.IsNullOrWhiteSpace(serial))
            {
                return Failure("未发现在线 Android 设备。");
            }

            ProcessResult pidResult = RunProcess(
                adb,
                BuildArguments(
                    "-s",
                    serial,
                    "shell",
                    "pidof",
                    packageName),
                cancellationToken,
                20000);
            int pid = ParseFirstPid(pidResult.Output);
            if (!pidResult.Succeeded || pid <= 0)
            {
                return Failure("未找到游戏进程：" + packageName + "。");
            }

            string cachePath = this.ResolveCachePath(serial, packageName);
            using (AndroidRootShellSessionLease rootLease =
                AndroidRootShellSessionManager.Acquire(
                    adb,
                    serial,
                    cancellationToken))
            {
                string arguments = this.BuildCommandLine(
                    cachePath,
                    rootLease.BrokerPipePath,
                    pid,
                    serial);
                ProcessResult process = RunProcess(
                    python,
                    arguments,
                    cancellationToken,
                    DefaultTimeoutMilliseconds);
                JObject payload;
                string parseError;
                if (!TryGetSingleJsonPayload(
                    process.Output,
                    out payload,
                    out parseError))
                {
                    return Failure(
                        "任务只读探针未返回唯一 JSON：" +
                        (string.IsNullOrWhiteSpace(process.Error)
                            ? parseError
                            : Compact(process.Error)));
                }

                string status = (string)payload["status"] ?? string.Empty;
                if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    string diagnostic = (string)payload["diagnosticCode"] ?? string.Empty;
                    string message = (string)payload["message"] ?? string.Empty;
                    return Failure(
                        string.IsNullOrWhiteSpace(message)
                            ? "任务结构尚未验证：" + diagnostic
                            : message);
                }
                if (!process.Succeeded)
                {
                    return Failure(
                        "任务只读探针执行失败：" + Compact(process.Error));
                }
                return ParseSnapshot(payload);
            }
        }

        private static ShimenTaskMemoryReadResult ParseSnapshot(JObject payload)
        {
            ulong ownerAddress;
            if (payload == null ||
                payload.Value<bool?>("readOnly") != true ||
                payload.Value<bool?>("actionAuthorized") != false ||
                payload.Value<bool?>("ocrUsed") != false ||
                payload.Value<bool?>("ownerVerified") != true ||
                !TryParseGc64Address(payload["ownerAddress"], out ownerAddress))
            {
                return Failure("任务快照未满足只读、GC64 owner 验证、无 OCR 和未授权操作约束。");
            }

            JObject identity = payload["processIdentity"] as JObject;
            int pid = identity == null ? 0 : identity.Value<int?>("pid") ?? 0;
            long startTicks = identity == null
                ? 0
                : identity.Value<long?>("startTicks") ?? 0;
            string executable = identity == null
                ? string.Empty
                : (string)identity["exe"] ?? string.Empty;
            if (pid <= 0 || startTicks <= 0 || string.IsNullOrWhiteSpace(executable))
            {
                return Failure("任务快照缺少有效的进程身份绑定。");
            }

            JArray taskArray = payload["tasks"] as JArray;
            if (taskArray == null)
            {
                return Failure("任务快照缺少任务列表。");
            }
            List<ShimenTaskMemoryTask> tasks = new List<ShimenTaskMemoryTask>();
            foreach (JToken token in taskArray)
            {
                JObject item = token as JObject;
                if (item == null)
                {
                    return Failure("任务快照包含无效任务项。");
                }
                bool canGiveUpKnown = item.Value<bool?>("canGiveUpKnown") == true;
                int taskId = item.Value<int?>("taskId") ?? 0;
                if (taskId <= 0 || !canGiveUpKnown)
                {
                    return Failure("任务快照的任务 ID 或 canGiveUp 未能可靠解码。");
                }
                tasks.Add(new ShimenTaskMemoryTask
                {
                    Slot = item.Value<int?>("slot") ?? 0,
                    TaskId = taskId,
                    TaskType = (string)item["taskType"] ?? string.Empty,
                    CanGiveUp = item.Value<bool?>("canGiveUp") == true,
                    CanGiveUpKnown = true,
                    FinishTimes = item.Value<int?>("finishTimes") ?? 0,
                    EndTime = item.Value<int?>("endTime") ?? 0,
                    UpdateTime = item.Value<int?>("updateTime") ?? 0,
                    ConditionEntryCount = item.Value<int?>("conditionEntryCount") ?? 0,
                    NpcListEntryCount = item.Value<int?>("npcListEntryCount") ?? 0
                });
            }

            JObject timingJson = payload["timing"] as JObject;
            ShimenTaskReadDiagnostics diagnostics = new ShimenTaskReadDiagnostics
            {
                ReadPath = (string)payload["readPath"] ?? string.Empty,
                ProcessLookupMilliseconds = ValueDouble(timingJson, "processLookupMilliseconds"),
                RootSessionMilliseconds = ValueDouble(timingJson, "rootSessionMilliseconds"),
                MemoryReadMilliseconds = ValueDouble(timingJson, "memoryReadMilliseconds"),
                DecodeMilliseconds = ValueDouble(timingJson, "decodeMilliseconds"),
                TotalMilliseconds = ValueDouble(timingJson, "totalMilliseconds")
            };
            return new ShimenTaskMemoryReadResult
            {
                Snapshot = new ShimenTaskMemorySnapshot
                {
                    IsValid = true,
                    ProcessId = pid,
                    ProcessStartTicks = startTicks,
                    Executable = executable,
                    TaskListAddress = (string)payload["taskListAddress"] ?? string.Empty,
                    Tasks = tasks,
                    Diagnostics = diagnostics,
                    ReadOnly = true,
                    ActionAuthorized = false,
                    OcrUsed = false,
                    NoClaimableTaskKnown = payload.Value<bool?>("noClaimableTaskKnown") == true,
                    NoClaimableTask = payload.Value<bool?>("noClaimableTask") == true
                }
            };
        }

        private static bool TryParseGc64Address(JToken token, out ulong address)
        {
            address = 0UL;
            if (token == null || token.Type != JTokenType.String)
            {
                return false;
            }
            string text = token.Value<string>();
            if (string.IsNullOrWhiteSpace(text) ||
                !text.TrimStart().StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string hexadecimal = text.Trim().Substring(2);
            if (!ulong.TryParse(
                hexadecimal,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out address))
            {
                address = 0UL;
                return false;
            }
            return address > 0UL && address < (1UL << 47);
        }

        private string ResolveAdbPath()
        {
            List<string> candidates = new List<string>();
            AddCandidate(candidates, this.configuredAdbPath);
            AddCandidate(candidates, Environment.GetEnvironmentVariable("WPE_ADB_PATH"));
            AddCandidate(candidates, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe"));
            AddCandidate(candidates, @"C:\Users\Administrator\AppData\Local\Android\Sdk\platform-tools\adb.exe");
            AddCandidate(candidates, @"E:\leidian\LDPlayer14\adb.exe");
            AddCandidate(candidates, @"C:\leidian\LDPlayer14\adb.exe");
            AddPathCandidates(candidates, "adb.exe");
            return candidates.Where(File.Exists).FirstOrDefault() ?? string.Empty;
        }

        private string ResolvePythonPath()
        {
            List<string> candidates = new List<string>();
            AddCandidate(candidates, this.configuredPythonPath);
            AddCandidate(candidates, Environment.GetEnvironmentVariable("WPE_PYTHON_PATH"));
            AddCandidate(candidates, @"C:\Users\Administrator\AppData\Local\Programs\Python\Python314\python.exe");
            AddCandidate(candidates, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python.exe"));
            AddPathCandidates(candidates, "python.exe");
            AddPathCandidates(candidates, "py.exe");
            return candidates.Where(File.Exists).FirstOrDefault() ?? string.Empty;
        }

        private string ResolveReaderScriptPath()
        {
            List<string> candidates = new List<string>();
            AddCandidate(candidates, this.configuredReaderScript);
            AddCandidate(candidates, Environment.GetEnvironmentVariable("WPE_SHIMEN_TASK_READER_PATH"));
            AddCandidate(candidates, DefaultReaderScript);
            AddCandidate(candidates, Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "tools",
                "task-reader",
                "shimen_task_memory_reader.py"));
            AddCandidate(candidates, Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "tools",
                "treasure-reader",
                "shimen_task_memory_reader.py"));
            return candidates.Where(File.Exists).FirstOrDefault() ?? string.Empty;
        }

        private string ResolvePackage()
        {
            string value = string.IsNullOrWhiteSpace(this.configuredPackage)
                ? Environment.GetEnvironmentVariable("WPE_SHIMEN_GAME_PACKAGE")
                : this.configuredPackage;
            return string.IsNullOrWhiteSpace(value)
                ? DefaultPackage
                : value.Trim();
        }

        private string ResolveSerialForCommand()
        {
            string value = string.IsNullOrWhiteSpace(this.configuredSerial)
                ? Environment.GetEnvironmentVariable("WPE_SHIMEN_READER_SERIAL")
                : this.configuredSerial;
            return string.IsNullOrWhiteSpace(value) ? "emulator-5554" : value.Trim();
        }

        private string ResolveCachePath(string serial, string packageName)
        {
            if (!string.IsNullOrWhiteSpace(this.configuredCachePath))
            {
                return this.configuredCachePath.Trim();
            }
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XNAS",
                "WPE",
                "shimen-task",
                "probe-cache");
            return Path.Combine(
                directory,
                SafePathPart(serial) + "-" + SafePathPart(packageName) + ".json");
        }

        private static ShimenTaskMemoryReadResult Failure(string message)
        {
            return new ShimenTaskMemoryReadResult
            {
                Error = message ?? string.Empty
            };
        }

        private static bool TryGetSingleJsonPayload(
            string output,
            out JObject payload,
            out string error)
        {
            payload = null;
            error = string.Empty;
            string[] lines = (output ?? string.Empty).Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            string json = lines
                .Select(line => line.Trim())
                .FirstOrDefault(line =>
                    line.StartsWith("{", StringComparison.Ordinal) &&
                    line.EndsWith("}", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "没有找到 JSON 行。";
                return false;
            }
            try
            {
                payload = JObject.Parse(json);
                return true;
            }
            catch (Exception ex)
            {
                error = Compact(ex.Message);
                return false;
            }
        }

        private static double ValueDouble(JObject value, string key)
        {
            return value == null ? 0 : value.Value<double?>(key) ?? 0;
        }

        private static bool IsSafePackageName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+$");
        }

        private static string SelectDeviceSerial(string output, string configured)
        {
            List<string> online = new List<string>();
            foreach (string line in (output ?? string.Empty).Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Trim().Split(
                    new[] { '\t', ' ' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 2 &&
                    string.Equals(fields[1], "device", StringComparison.OrdinalIgnoreCase))
                {
                    online.Add(fields[0]);
                }
            }
            if (!string.IsNullOrWhiteSpace(configured) &&
                online.Contains(configured.Trim()))
            {
                return configured.Trim();
            }
            return online.FirstOrDefault() ?? string.Empty;
        }

        private static int ParseFirstPid(string output)
        {
            foreach (Match match in Regex.Matches(output ?? string.Empty, "\\b[1-9][0-9]{0,8}\\b"))
            {
                int pid;
                if (int.TryParse(match.Value, out pid) && pid > 0)
                {
                    return pid;
                }
            }
            return 0;
        }

        private static string BuildArguments(params string[] values)
        {
            return string.Join(" ", values.Select(Quote));
        }

        private static string Quote(string value)
        {
            string normalized = value ?? string.Empty;
            if (normalized.Length == 0)
            {
                return "\"\"";
            }
            if (!normalized.Any(character =>
                char.IsWhiteSpace(character) || character == '"'))
            {
                return normalized;
            }
            return "\"" + normalized.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static ProcessResult RunProcess(
            string fileName,
            string arguments,
            CancellationToken cancellationToken,
            int timeoutMilliseconds)
        {
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                try
                {
                    if (!process.Start())
                    {
                        return new ProcessResult(false, string.Empty, "进程启动失败。", -1);
                    }
                }
                catch (Exception ex)
                {
                    return new ProcessResult(false, string.Empty, ex.Message, -1);
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                Stopwatch timer = Stopwatch.StartNew();
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (timer.ElapsedMilliseconds >= timeoutMilliseconds)
                        {
                            TryKill(process);
                            return new ProcessResult(
                                false,
                                outputTask.IsCompleted ? outputTask.Result : string.Empty,
                                "reader 执行超时。",
                                -1);
                        }
                    }
                    process.WaitForExit();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    throw;
                }
                string output = outputTask.Result ?? string.Empty;
                string error = errorTask.Result ?? string.Empty;
                return new ProcessResult(
                    process.ExitCode == 0,
                    output,
                    string.IsNullOrWhiteSpace(error) ? output : error,
                    process.ExitCode);
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Best-effort cleanup for a timed-out child process.
            }
        }

        private static string SafePathPart(string value)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
            StringBuilder builder = new StringBuilder(normalized.Length);
            foreach (char character in normalized)
            {
                builder.Append(
                    char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.'
                        ? character
                        : '-');
            }
            return builder.Length == 0 ? "default" : builder.ToString();
        }

        private static void AddCandidate(ICollection<string> candidates, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !candidates.Contains(value.Trim()))
            {
                candidates.Add(value.Trim());
            }
        }

        private static void AddPathCandidates(ICollection<string> candidates, string fileName)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    AddCandidate(candidates, Path.Combine(directory.Trim(), fileName));
                }
            }
        }

        private static string Compact(string value)
        {
            string normalized = (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return normalized.Length <= 600 ? normalized : normalized.Substring(0, 600);
        }

        private sealed class ProcessResult
        {
            internal ProcessResult(bool succeeded, string output, string error, int exitCode)
            {
                this.Succeeded = succeeded;
                this.Output = output ?? string.Empty;
                this.Error = error ?? string.Empty;
                this.ExitCode = exitCode;
            }

            internal bool Succeeded { get; private set; }
            internal string Output { get; private set; }
            internal string Error { get; private set; }
            internal int ExitCode { get; private set; }
        }
    }
}
