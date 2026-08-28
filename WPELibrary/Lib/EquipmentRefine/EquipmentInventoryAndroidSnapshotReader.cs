using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.Android;

namespace WPELibrary.Lib.EquipmentRefine
{
    public sealed class EquipmentInventoryAndroidSnapshotReadResult
    {
        internal EquipmentInventoryAndroidSnapshotReadResult(
            EquipmentRefineDetector.EquipmentInventory inventory,
            string error)
        {
            this.Inventory = inventory;
            this.Error = error ?? string.Empty;
        }

        public EquipmentRefineDetector.EquipmentInventory Inventory { get; private set; }

        public string Error { get; private set; }

        public bool Succeeded
        {
            get
            {
                return this.Inventory != null &&
                    string.IsNullOrWhiteSpace(this.Error);
            }
        }
    }

    /// <summary>
    /// Windows-to-Python/ADB bridge for one bounded, read-only bag snapshot.
    /// The existing equipment_inventory_resident.py remains the owner of the
    /// native reader lifecycle and schema. This class only invokes its
    /// inventory-only CLI and validates the resulting state through
    /// EquipmentRefineDetector; it has no page navigation, memory-write or
    /// packet-send path.
    /// </summary>
    public sealed class EquipmentInventoryAndroidSnapshotReader
    {
        private const int DefaultTimeoutMilliseconds = 90000;
        private const string DefaultPackage = "com.gdoo.yzqcxy";
        private const string DefaultRemoteBinary = "/data/local/tmp/equipment-streamd";
        private const string DefaultSocketName = "piaomiao.eqtest2";
        private const int DefaultPort = 28772;
        private const string DefaultReaderScript =
            @"F:\项目目录\飘渺游戏助手\tools\equipment-reader\equipment_inventory_resident.py";

        private readonly string configuredAdbPath;
        private readonly string configuredSerial;
        private readonly string configuredPackage;
        private readonly string configuredPythonPath;
        private readonly string configuredReaderScript;
        private readonly string remoteBinary;
        private readonly string socketName;
        private readonly int port;

        public EquipmentInventoryAndroidSnapshotReader()
            : this(null, null, null, null, null, null, null, null)
        {
        }

        public EquipmentInventoryAndroidSnapshotReader(
            string adbPath,
            string deviceSerial,
            string gamePackage,
            string pythonPath,
            string readerScriptPath,
            string remoteBinaryPath,
            string readerSocketName,
            int? readerPort)
        {
            this.configuredAdbPath = adbPath;
            this.configuredSerial = deviceSerial;
            this.configuredPackage = gamePackage;
            this.configuredPythonPath = pythonPath;
            this.configuredReaderScript = readerScriptPath;
            this.remoteBinary = string.IsNullOrWhiteSpace(remoteBinaryPath)
                ? ResolveRemoteBinary()
                : remoteBinaryPath.Trim();
            this.socketName = string.IsNullOrWhiteSpace(readerSocketName)
                ? ResolveSocketName()
                : readerSocketName.Trim();
            this.port = readerPort.HasValue && readerPort.Value > 0
                ? readerPort.Value
                : ResolvePort();
        }

        /// <summary>只检查本地桥接依赖是否存在，不连接设备。</summary>
        public bool IsConfigured
        {
            get
            {
                return File.Exists(ResolveAdbPath()) &&
                    File.Exists(ResolvePythonPath()) &&
                    File.Exists(ResolveReaderScriptPath()) &&
                    IsSafePackageName(ResolvePackage()) &&
                    IsSafeRemotePath(this.remoteBinary) &&
                    IsSafeSocketName(this.socketName) &&
                    this.port >= 1024 && this.port <= 65535;
            }
        }

        /// <summary>
        /// 公开离线可审计的启动参数构造，不启动任何进程；测试用它确认
        /// 只使用 inventory-only、单次扫描和临时 state/output 文件。
        /// </summary>
        public string BuildCommandLine(string statePath, string outputPath)
        {
            return this.BuildCommandLine(statePath, outputPath, null);
        }

        private string BuildCommandLine(
            string statePath,
            string outputPath,
            string rootBrokerPipe)
        {
            string adb = ResolveAdbPath();
            string pythonScript = ResolveReaderScriptPath();
            string serial = ResolveSerialForCommand();
            string packageName = ResolvePackage();
            List<string> arguments = new List<string>
            {
                Quote(pythonScript),
                "--adb", Quote(adb),
                "--serial", Quote(serial),
                "--package", Quote(packageName),
                "--remote-binary", Quote(this.remoteBinary),
                "--socket-name", Quote(this.socketName),
                "--port", this.port.ToString(),
                "--interval-ms", "15",
                "--max-polls", "1",
                "--max-session-attempts", "1",
                "--connect-timeout", "15",
                "--scan-timeout", "30",
                "--rebind-interval", "0.5",
                "--inventory-only",
                "--output", Quote(outputPath),
                "--state", Quote(statePath)
            };
            if (!string.IsNullOrWhiteSpace(rootBrokerPipe))
            {
                arguments.Add("--root-broker-pipe");
                arguments.Add(Quote(rootBrokerPipe));
            }
            return string.Join(" ", arguments.ToArray());
        }

        public async Task<EquipmentInventoryAndroidSnapshotReadResult> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(
                    () => ReadSnapshot(cancellationToken),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new EquipmentInventoryAndroidSnapshotReadResult(
                    null,
                    "背包只读读取已取消。");
            }
            catch (Exception ex)
            {
                return new EquipmentInventoryAndroidSnapshotReadResult(
                    null,
                    "背包只读读取失败：" + Compact(ex.Message));
            }
        }

        public async Task<EquipmentRefineDetector.EquipmentInventory> ReadInventoryAsync(
            CancellationToken cancellationToken)
        {
            EquipmentInventoryAndroidSnapshotReadResult result =
                await this.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (result == null || !result.Succeeded)
            {
                throw new InvalidOperationException(
                    result == null ? "背包只读读取没有结果。" : result.Error);
            }

            return result.Inventory;
        }

        private EquipmentInventoryAndroidSnapshotReadResult ReadSnapshot(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string adb = ResolveAdbPath();
            string python = ResolvePythonPath();
            string script = ResolveReaderScriptPath();
            string packageName = ResolvePackage();
            string serial = ResolveSerialForCommand();
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
                return Failure("未找到 equipment_inventory_resident.py；可通过 WPE_EQUIPMENT_READER_PATH 配置。");
            }
            if (!IsSafePackageName(packageName))
            {
                return Failure("游戏包名配置无效。");
            }
            if (!IsSafeRemotePath(this.remoteBinary) || !IsSafeSocketName(this.socketName))
            {
                return Failure("reader 的远端路径或 socket 配置无效。");
            }

            string token = Guid.NewGuid().ToString("N");
            string statePath = Path.Combine(
                Path.GetTempPath(),
                "wpe-equipment-reader-" + token + ".state.json");
            string outputPath = Path.Combine(
                Path.GetTempPath(),
                "wpe-equipment-reader-" + token + ".jsonl");
            try
            {
                using (AndroidRootShellSessionLease rootLease =
                    AndroidRootShellSessionManager.Acquire(
                        adb,
                        serial,
                        cancellationToken))
                {
                    string arguments = this.BuildCommandLine(
                        statePath,
                        outputPath,
                        rootLease.BrokerPipePath);
                    ProcessResult process = RunProcess(
                        python,
                        arguments,
                        cancellationToken);
                    if (!process.Succeeded)
                    {
                        return Failure(
                            "只读 reader 执行失败：" +
                            Compact(string.IsNullOrWhiteSpace(process.Error)
                                ? process.Output
                                : process.Error));
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    EquipmentRefineDetector.EquipmentInventory inventory =
                        EquipmentRefineDetector.ReadBagInventoryAsync(
                            statePath,
                            cancellationToken).GetAwaiter().GetResult();
                    if (inventory == null ||
                        !inventory.IsUsableFor(EquipmentTargetMode.Bag))
                    {
                        return Failure(
                            "reader 未返回可用的当前背包快照（需要 schema 3、只读、同会话且绑定有效）。");
                    }

                    return new EquipmentInventoryAndroidSnapshotReadResult(
                        inventory,
                        string.Empty);
                }
            }
            finally
            {
                TryDelete(statePath);
                TryDelete(outputPath);
            }
        }

        private static EquipmentInventoryAndroidSnapshotReadResult Failure(string message)
        {
            return new EquipmentInventoryAndroidSnapshotReadResult(null, message);
        }

        private ProcessResult RunProcess(
            string fileName,
            string arguments,
            CancellationToken cancellationToken)
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
                        return new ProcessResult(false, string.Empty, "Python 进程启动失败。");
                    }
                }
                catch (Exception ex)
                {
                    return new ProcessResult(false, string.Empty, ex.Message);
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(GetTimeoutMilliseconds()))
                {
                    try { process.Kill(); } catch { }
                    return new ProcessResult(false, string.Empty, "reader 执行超时。");
                }

                cancellationToken.ThrowIfCancellationRequested();
                string output = outputTask.Result ?? string.Empty;
                string error = errorTask.Result ?? string.Empty;
                return new ProcessResult(
                    process.ExitCode == 0,
                    output,
                    string.IsNullOrWhiteSpace(error) ? output : error);
            }
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
            AddCandidate(candidates, Environment.GetEnvironmentVariable("WPE_EQUIPMENT_READER_PATH"));
            AddCandidate(candidates, DefaultReaderScript);
            AddCandidate(candidates, Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "equipment-reader",
                "equipment_inventory_resident.py"));
            return candidates.Where(File.Exists).FirstOrDefault() ?? string.Empty;
        }

        private string ResolvePackage()
        {
            string value = string.IsNullOrWhiteSpace(this.configuredPackage)
                ? Environment.GetEnvironmentVariable("WPE_EQUIPMENT_GAME_PACKAGE")
                : this.configuredPackage;
            return string.IsNullOrWhiteSpace(value) ? DefaultPackage : value.Trim();
        }

        private string ResolveSerialForCommand()
        {
            string value = string.IsNullOrWhiteSpace(this.configuredSerial)
                ? Environment.GetEnvironmentVariable("WPE_EQUIPMENT_READER_SERIAL")
                : this.configuredSerial;
            return string.IsNullOrWhiteSpace(value) ? "emulator-5554" : value.Trim();
        }

        private static string ResolveRemoteBinary()
        {
            string value = Environment.GetEnvironmentVariable(
                "WPE_EQUIPMENT_READER_REMOTE_BINARY");
            return string.IsNullOrWhiteSpace(value) ? DefaultRemoteBinary : value.Trim();
        }

        private static string ResolveSocketName()
        {
            string value = Environment.GetEnvironmentVariable("WPE_EQUIPMENT_READER_SOCKET");
            return string.IsNullOrWhiteSpace(value) ? DefaultSocketName : value.Trim();
        }

        private static int ResolvePort()
        {
            int value;
            return int.TryParse(
                Environment.GetEnvironmentVariable("WPE_EQUIPMENT_READER_PORT"),
                out value) && value >= 1024 && value <= 65535
                ? value
                : DefaultPort;
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

        private static bool IsSafePackageName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+$");
        }

        private static bool IsSafeRemotePath(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                Regex.IsMatch(value, "^/[A-Za-z0-9._/-]+$");
        }

        private static bool IsSafeSocketName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                Regex.IsMatch(value, "^[A-Za-z0-9._-]+$");
        }

        private static int GetTimeoutMilliseconds()
        {
            int value;
            return int.TryParse(
                Environment.GetEnvironmentVariable("WPE_EQUIPMENT_READER_TIMEOUT_MS"),
                out value) && value >= 1000 && value <= 300000
                ? value
                : DefaultTimeoutMilliseconds;
        }

        private static string Quote(string value)
        {
            string normalized = value ?? string.Empty;
            if (normalized.Length == 0) return "\"\"";
            if (!normalized.Any(character =>
                char.IsWhiteSpace(character) || character == '\"'))
            {
                return normalized;
            }

            return "\"" + normalized
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"") + "\"";
        }

        private static string Compact(string value)
        {
            string normalized = (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return normalized.Length <= 600
                ? normalized
                : normalized.Substring(0, 600);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Temporary diagnostics are best-effort; no game or reader state
                // is deleted here.
            }
        }

        private sealed class ProcessResult
        {
            internal ProcessResult(bool succeeded, string output, string error)
            {
                this.Succeeded = succeeded;
                this.Output = output ?? string.Empty;
                this.Error = error ?? string.Empty;
            }

            internal bool Succeeded { get; private set; }
            internal string Output { get; private set; }
            internal string Error { get; private set; }
        }
    }
}
