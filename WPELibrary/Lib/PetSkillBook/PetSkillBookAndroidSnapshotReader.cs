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

namespace WPELibrary.Lib.PetSkillBook
{
    public sealed class PetSkillBookSnapshotReadResult
    {
        internal PetSkillBookSnapshotReadResult(
            PetSkillBookReadOnlySnapshot snapshot,
            string error)
        {
            this.Snapshot = snapshot;
            this.Error = error ?? string.Empty;
        }

        public PetSkillBookReadOnlySnapshot Snapshot { get; private set; }

        public string Error { get; private set; }

        public bool Succeeded
        {
            get { return this.Snapshot != null && string.IsNullOrWhiteSpace(this.Error); }
        }
    }

    /// <summary>
    /// Windows-to-ADB bridge for the Android read-only pet snapshot probe.
    /// The remote executable must itself be a read-only reader and must be
    /// installed separately at the configured device path.
    /// </summary>
    public sealed class PetSkillBookAndroidSnapshotReader
    {
        private const int DefaultCommandTimeoutMilliseconds = 60000;
        private const string DefaultReaderPath = "/data/local/tmp/pet-skill-probe";

        private static readonly string[] DefaultPackages =
        {
            "com.gdoo.yzqcxy",
            "com.gdoo.xxxy12026",
            "com.gdoo.dhxy",
            "com.gdoo.xxxy2026"
        };

        private static readonly string[] NonGamePackagePrefixes =
        {
            "android.",
            "com.android.",
            "com.google.android.",
            "com.ldplayer.",
            "com.microvirt."
        };

        private readonly string configuredAdbPath;
        private readonly string configuredSerial;
        private readonly string configuredPackage;
        private readonly string readerPath;

        public PetSkillBookAndroidSnapshotReader()
            : this(null, null, null, null)
        {
        }

        public PetSkillBookAndroidSnapshotReader(
            string adbPath = null,
            string deviceSerial = null,
            string gamePackage = null,
            string remoteReaderPath = null)
        {
            this.configuredAdbPath = adbPath;
            this.configuredSerial = deviceSerial;
            this.configuredPackage = gamePackage;
            this.readerPath = string.IsNullOrWhiteSpace(remoteReaderPath)
                ? (Environment.GetEnvironmentVariable("WPE_PET_SKILL_READER_PATH") ?? DefaultReaderPath)
                : remoteReaderPath;
        }

        public async Task<PetSkillBookSnapshotReadResult> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                string error = string.Empty;
                PetSkillBookReadOnlySnapshot snapshot = await Task.Run(
                    () => this.ReadSnapshot(cancellationToken, out error),
                    CancellationToken.None).ConfigureAwait(false);
                return new PetSkillBookSnapshotReadResult(snapshot, error);
            }
            catch (OperationCanceledException)
            {
                return new PetSkillBookSnapshotReadResult(null, "宠物技能只读读取已取消。");
            }
            catch (Exception ex)
            {
                return new PetSkillBookSnapshotReadResult(null, "宠物技能只读读取失败：" + ex.Message);
            }
        }

        public async Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken)
        {
            PetSkillBookSnapshotReadResult result = await this.ReadSnapshotAsync(
                cancellationToken).ConfigureAwait(false);
            return result != null && result.Succeeded && result.Snapshot.IsCurrentPetMatch;
        }

        private PetSkillBookReadOnlySnapshot ReadSnapshot(
            CancellationToken cancellationToken,
            out string error)
        {
            error = string.Empty;
            cancellationToken.ThrowIfCancellationRequested();

            string adbPath = ResolveAdbPath(this.configuredAdbPath);
            if (string.IsNullOrWhiteSpace(adbPath))
            {
                error = "未找到 adb.exe。";
                return null;
            }

            AdbCommandResult devices = RunAdb(
                adbPath,
                BuildArguments("devices"),
                cancellationToken);
            if (!devices.Succeeded)
            {
                error = "ADB 不可用：" + Compact(devices.ErrorOrOutput);
                return null;
            }

            string serial = SelectDeviceSerial(devices.Output, this.configuredSerial);
            if (string.IsNullOrWhiteSpace(serial))
            {
                error = "未发现在线 Android 设备。";
                return null;
            }

            if (!IsSafeRemoteReaderPath(this.readerPath))
            {
                error = "只读读取器路径不合法。";
                return null;
            }

            string packageName;
            int gamePid;
            if (!TryFindGameProcess(
                adbPath,
                serial,
                this.configuredPackage,
                cancellationToken,
                out packageName,
                out gamePid,
                out error))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            string readerCommand = BuildReaderCommand(this.readerPath, gamePid);
            AdbCommandResult reader = RunAdb(
                adbPath,
                BuildArguments(
                    "-s",
                    serial,
                    "shell",
                    readerCommand),
                cancellationToken);

            if (ShouldRetryWithRootShell(reader))
            {
                string directError = Compact(reader.ErrorOrOutput);
                AndroidRootShellCommandResult sharedRootReader;
                using (AndroidRootShellSessionLease rootLease =
                    AndroidRootShellSessionManager.Acquire(
                        adbPath,
                        serial,
                        cancellationToken))
                {
                    sharedRootReader = rootLease.Execute(
                        BuildRootShellCommand(readerCommand),
                        DefaultCommandTimeoutMilliseconds,
                        cancellationToken);
                }
                AdbCommandResult rootReader = new AdbCommandResult(
                    sharedRootReader.Succeeded,
                    sharedRootReader.Output,
                    string.IsNullOrWhiteSpace(sharedRootReader.Error)
                        ? sharedRootReader.Output
                        : sharedRootReader.Error);
                if (rootReader.Succeeded)
                {
                    reader = rootReader;
                }
                else
                {
                    reader = new AdbCommandResult(
                        false,
                        rootReader.Output,
                        "普通 shell：" + directError +
                        "；root shell：" + Compact(rootReader.ErrorOrOutput));
                }
            }

            if (!reader.Succeeded)
            {
                error = "只读读取器执行失败（包名 " + packageName + "）：" + Compact(reader.ErrorOrOutput);
                return null;
            }

            string json;
            if (!TryGetSingleJsonLine(reader.Output, out json, out error))
            {
                return null;
            }

            PetSkillBookReadOnlySnapshot snapshot;
            string parseError;
            if (!PetSkillBookReadOnlySnapshotProtocol.TryParse(json, out snapshot, out parseError))
            {
                error = "只读快照校验失败：" + parseError;
                return null;
            }

            if (snapshot.Process == null || snapshot.Process.Pid != gamePid)
            {
                error = "只读快照进程身份与发现的游戏 PID 不一致。";
                return null;
            }

            return snapshot;
        }

        private static bool TryFindGameProcess(
            string adbPath,
            string serial,
            string configuredPackage,
            CancellationToken cancellationToken,
            out string packageName,
            out int gamePid,
            out string error)
        {
            packageName = string.Empty;
            gamePid = 0;
            error = string.Empty;
            foreach (string candidate in GetPackageCandidates(configuredPackage))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AdbCommandResult result = RunAdb(
                    adbPath,
                    BuildArguments("-s", serial, "shell", "pidof", candidate),
                    cancellationToken);
                int pid = ParseFirstPid(result.Output);
                if (pid > 0)
                {
                    packageName = candidate;
                    gamePid = pid;
                    return true;
                }
            }

            error = "未找到候选游戏进程。";
            return false;
        }

        private static IEnumerable<string> GetPackageCandidates(string configuredPackage)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> configured = new[]
            {
                configuredPackage,
                Environment.GetEnvironmentVariable("WPE_PET_SKILL_GAME_PACKAGE")
            };

            foreach (string value in configured.Concat(DefaultPackages))
            {
                string candidate = (value ?? string.Empty).Trim();
                if (IsSafePackageName(candidate) && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static bool IsSafePackageName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+$"))
            {
                return false;
            }

            if (string.Equals(value, "android", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !NonGamePackagePrefixes.Any(prefix =>
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSafeRemoteReaderPath(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                Regex.IsMatch(value, "^/[A-Za-z0-9._/-]+$");
        }

        private static string ResolveAdbPath(string configured)
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured.Trim());
            string environmentPath = Environment.GetEnvironmentVariable("WPE_ADB_PATH");
            if (!string.IsNullOrWhiteSpace(environmentPath)) candidates.Add(environmentPath.Trim());
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe"));
            candidates.Add("E:\\leidian\\LDPlayer14\\adb.exe");
            candidates.Add("C:\\leidian\\LDPlayer14\\adb.exe");

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                candidates.Add(Path.Combine(directory.Trim(), "adb.exe"));
            }

            return candidates
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static string SelectDeviceSerial(string output, string configured)
        {
            List<string> online = new List<string>();
            foreach (string rawLine in (output ?? string.Empty).Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = rawLine.Trim().Split(
                    new[] { '\t', ' ' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 2 && string.Equals(fields[1], "device", StringComparison.OrdinalIgnoreCase))
                {
                    online.Add(fields[0]);
                }
            }

            if (!string.IsNullOrWhiteSpace(configured) && online.Contains(configured.Trim()))
            {
                return configured.Trim();
            }

            return online.FirstOrDefault() ?? string.Empty;
        }

        private static bool TryGetSingleJsonLine(string output, out string json, out string error)
        {
            json = string.Empty;
            error = string.Empty;
            string[] lines = (output ?? string.Empty).Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            string[] jsonLines = lines
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("{", StringComparison.Ordinal) &&
                               line.EndsWith("}", StringComparison.Ordinal))
                .ToArray();
            if (jsonLines.Length != 1)
            {
                error = "只读读取器未返回唯一 JSON 快照。";
                return false;
            }

            json = jsonLines[0];
            return true;
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
            return string.Join(" ", values.Select(QuoteArgument));
        }

        private static string BuildReaderCommand(string path, int gamePid)
        {
            return path + " " + gamePid.ToString() + " --json";
        }

        private static string BuildRootShellCommand(string command)
        {
            // The shared session already owns `su -c sh`; keep this helper as
            // the explicit root-reader boundary without creating a new su
            // process for every snapshot.
            return command ?? string.Empty;
        }

        private static bool ShouldRetryWithRootShell(AdbCommandResult result)
        {
            string text = result == null
                ? string.Empty
                : (result.Output + "\n" + result.ErrorOrOutput).ToLowerInvariant();
            return text.Contains("proc_mem_open_failed") ||
                text.Contains("permission denied") ||
                text.Contains("operation not permitted") ||
                text.Contains("access denied");
        }

        private static string QuoteArgument(string value)
        {
            string normalized = value ?? string.Empty;
            if (normalized.Length == 0) return "\"\"";
            if (!normalized.Any(character => char.IsWhiteSpace(character) || character == '"'))
            {
                return normalized;
            }
            return "\"" + normalized.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static AdbCommandResult RunAdb(
            string adbPath,
            string arguments,
            CancellationToken cancellationToken)
        {
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
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
                        return new AdbCommandResult(false, string.Empty, "adb 启动失败。");
                    }
                }
                catch (Exception ex)
                {
                    return new AdbCommandResult(false, string.Empty, ex.Message);
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(GetCommandTimeoutMilliseconds()))
                {
                    try { process.Kill(); } catch { }
                    return new AdbCommandResult(false, outputTask.Result, "adb 命令超时。");
                }

                cancellationToken.ThrowIfCancellationRequested();
                string output = outputTask.Result;
                string error = errorTask.Result;
                return new AdbCommandResult(
                    process.ExitCode == 0,
                    output,
                    string.IsNullOrWhiteSpace(error) ? output : error);
            }
        }

        private static int GetCommandTimeoutMilliseconds()
        {
            const int minimum = 1000;
            const int maximum = 300000;
            const string environmentName = "WPE_PET_SKILL_ADB_TIMEOUT_MS";

            int configured;
            if (int.TryParse(
                Environment.GetEnvironmentVariable(environmentName),
                out configured)
                && configured >= minimum
                && configured <= maximum)
            {
                return configured;
            }

            return DefaultCommandTimeoutMilliseconds;
        }

        private static string Compact(string value)
        {
            return string.Join(
                " ",
                (value ?? string.Empty).Split(
                    new[] { '\r', '\n', '\t', ' ' },
                    StringSplitOptions.RemoveEmptyEntries));
        }

        private sealed class AdbCommandResult
        {
            public AdbCommandResult(bool succeeded, string output, string errorOrOutput)
            {
                this.Succeeded = succeeded;
                this.Output = output ?? string.Empty;
                this.ErrorOrOutput = errorOrOutput ?? string.Empty;
            }

            public bool Succeeded { get; private set; }

            public string Output { get; private set; }

            public string ErrorOrOutput { get; private set; }
        }
    }
}
