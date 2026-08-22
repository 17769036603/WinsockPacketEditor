using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// Result of preparing the read-only C6 memory stream. This controller never
    /// sends game packets; it only prepares the external reader and ADB forward.
    /// </summary>
    public sealed class TreasureC6StartupResult
    {
        private TreasureC6StartupResult(
            bool succeeded,
            string code,
            string detail,
            string adbPath,
            string deviceSerial,
            int gamePid,
            string readerPath)
        {
            this.Succeeded = succeeded;
            this.Code = code ?? string.Empty;
            this.Detail = detail ?? string.Empty;
            this.AdbPath = adbPath ?? string.Empty;
            this.DeviceSerial = deviceSerial ?? string.Empty;
            this.GamePid = gamePid;
            this.ReaderPath = readerPath ?? string.Empty;
        }

        public bool Succeeded { get; private set; }

        public string Code { get; private set; }

        public string Detail { get; private set; }

        public string AdbPath { get; private set; }

        public string DeviceSerial { get; private set; }

        public int GamePid { get; private set; }

        public string ReaderPath { get; private set; }

        public string UserMessage
        {
            get
            {
                if (this.Succeeded)
                {
                    return string.Empty;
                }

                return string.Format(
                    "藏宝图助手未能自动启动 C6 读取服务：{0}",
                    string.IsNullOrWhiteSpace(this.Detail) ? this.Code : this.Detail);
            }
        }

        internal static TreasureC6StartupResult Success(
            string adbPath,
            string deviceSerial,
            int gamePid,
            string readerPath)
        {
            return new TreasureC6StartupResult(
                true,
                "ready",
                "C6 读取器和 ADB 转发已就绪。",
                adbPath,
                deviceSerial,
                gamePid,
                readerPath);
        }

        internal static TreasureC6StartupResult Failure(
            string code,
            string detail,
            string adbPath,
            string deviceSerial,
            int gamePid,
            string readerPath)
        {
            return new TreasureC6StartupResult(
                false,
                code,
                detail,
                adbPath,
                deviceSerial,
                gamePid,
                readerPath);
        }
    }

    /// <summary>
    /// Discovers the local emulator, starts the read-only treasure reader on the
    /// device, and creates the TCP-to-localabstract ADB forward expected by the
    /// production C6 stream client.
    /// </summary>
    public static class TreasureC6ServiceController
    {
        private const string ForwardSpec = "tcp:28765";
        private const string RemoteSocketName = TreasureC6StreamProtocolDefinition.ProtocolName;
        private const string ReaderLogPath = "/data/local/tmp/treasure-streamd-wpe.log";
        private const int CommandTimeoutMilliseconds = 15000;
        private const int ReaderReadyTimeoutMilliseconds = 5000;
        private const int ReaderIntervalMilliseconds = 10;

        private static readonly SemaphoreSlim EnsureSync = new SemaphoreSlim(1, 1);

        private static readonly string[] GamePackageCandidates =
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

        private static readonly string[] ReaderPathCandidates =
        {
            "/data/local/tmp/treasure-streamd-resident",
            "/data/local/tmp/treasure-streamd"
        };

        public static async Task<TreasureC6StartupResult> EnsureReadyAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                await EnsureSync.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return TreasureC6StartupResult.Failure(
                    "cancelled",
                    "C6 自动启动已取消。",
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty);
            }

            try
            {
                return await Task.Run(
                    () => EnsureReady(cancellationToken),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return TreasureC6StartupResult.Failure(
                    "cancelled",
                    "C6 自动启动已取消。",
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty);
            }
            catch (Exception ex)
            {
                return TreasureC6StartupResult.Failure(
                    "unexpected",
                    "C6 自动启动异常：" + ex.Message,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty);
            }
            finally
            {
                EnsureSync.Release();
            }
        }

        private static TreasureC6StartupResult EnsureReady(CancellationToken cancellationToken)
        {
            string adbPath = ResolveAdbPath();
            if (string.IsNullOrWhiteSpace(adbPath))
            {
                return TreasureC6StartupResult.Failure(
                    "adb_not_found",
                    "未找到 adb.exe，请确认模拟器已安装，或将 adb.exe 加入 PATH。",
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty);
            }

            AdbCommandResult devices = RunAdb(
                adbPath,
                BuildArguments("devices"),
                cancellationToken);
            if (!devices.Succeeded)
            {
                return FailureFromAdb(
                    "adb_unavailable",
                    "ADB 无法启动：" + Compact(devices.ErrorOrOutput),
                    adbPath,
                    string.Empty,
                    0,
                    string.Empty);
            }

            string deviceSerial = SelectDeviceSerial(devices.Output);
            if (string.IsNullOrWhiteSpace(deviceSerial))
            {
                return TreasureC6StartupResult.Failure(
                    "device_not_found",
                    "未发现在线 Android 设备，请先启动模拟器。",
                    adbPath,
                    string.Empty,
                    0,
                    string.Empty);
            }

            int gamePid;
            string gamePackage;
            if (!TryFindGameProcess(
                adbPath,
                deviceSerial,
                cancellationToken,
                out gamePid,
                out gamePackage))
            {
                return TreasureC6StartupResult.Failure(
                    "game_not_running",
                    "未找到游戏进程，请先启动游戏并进入角色后再运行助手。",
                    adbPath,
                    deviceSerial,
                    0,
                    string.Empty);
            }

            string readerPath = FindReaderPath(adbPath, deviceSerial, cancellationToken);
            if (string.IsNullOrWhiteSpace(readerPath))
            {
                return TreasureC6StartupResult.Failure(
                    "reader_not_found",
                    "设备内未找到只读 C6 读取器（treasure-streamd-resident）。",
                    adbPath,
                    deviceSerial,
                    gamePid,
                    string.Empty);
            }

            cancellationToken.ThrowIfCancellationRequested();
            string processTable = GetReaderProcessTable(
                adbPath,
                deviceSerial,
                cancellationToken);
            bool matchingReader = HasMatchingReader(processTable, gamePid);
            if (!matchingReader)
            {
                StopExistingReaders(
                    adbPath,
                    deviceSerial,
                    processTable,
                    cancellationToken);

                string startCommand = string.Format(
                    "nohup {0} --pid {1} --socket-name {2} --interval-ms {3} " +
                        "--resident >{4} 2>&1 </dev/null & echo started",
                    readerPath,
                    gamePid,
                    RemoteSocketName,
                    ReaderIntervalMilliseconds,
                    ReaderLogPath);
                AdbCommandResult started = RunRootShell(
                    adbPath,
                    deviceSerial,
                    startCommand,
                    cancellationToken);
                if (!started.Succeeded)
                {
                    return FailureFromAdb(
                        "reader_start_failed",
                        "启动 C6 读取器失败：" + Compact(started.ErrorOrOutput),
                        adbPath,
                        deviceSerial,
                        gamePid,
                        readerPath);
                }

                if (!WaitForReader(
                    adbPath,
                    deviceSerial,
                    gamePid,
                    cancellationToken))
                {
                    string log = RunRootShell(
                        adbPath,
                        deviceSerial,
                        "tail -n 20 " + ReaderLogPath + " 2>/dev/null || true",
                        cancellationToken).Output;
                    string detail = "C6 读取器启动后未进入运行状态。";
                    if (!string.IsNullOrWhiteSpace(log))
                    {
                        detail += " " + Compact(log);
                    }
                    return TreasureC6StartupResult.Failure(
                        "reader_start_failed",
                        detail,
                        adbPath,
                        deviceSerial,
                        gamePid,
                        readerPath);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            RunAdb(
                adbPath,
                BuildArguments(
                    "-s",
                    deviceSerial,
                    "forward",
                    "--remove",
                    ForwardSpec),
                cancellationToken);

            AdbCommandResult forward = RunAdb(
                adbPath,
                BuildArguments(
                    "-s",
                    deviceSerial,
                    "forward",
                    ForwardSpec,
                    "localabstract:" + RemoteSocketName),
                cancellationToken);
            if (!forward.Succeeded)
            {
                return FailureFromAdb(
                    "forward_failed",
                    "建立 ADB 转发失败：" + Compact(forward.ErrorOrOutput),
                    adbPath,
                    deviceSerial,
                    gamePid,
                    readerPath);
            }

            if (!WaitForLocalPort(cancellationToken))
            {
                return TreasureC6StartupResult.Failure(
                    "forward_not_ready",
                    "ADB 转发已创建，但本地 28765 端口尚未可连接。",
                    adbPath,
                    deviceSerial,
                    gamePid,
                    readerPath);
            }

            return TreasureC6StartupResult.Success(
                adbPath,
                deviceSerial,
                gamePid,
                readerPath);
        }

        private static TreasureC6StartupResult FailureFromAdb(
            string code,
            string detail,
            string adbPath,
            string deviceSerial,
            int gamePid,
            string readerPath)
        {
            return TreasureC6StartupResult.Failure(
                code,
                detail,
                adbPath,
                deviceSerial,
                gamePid,
                readerPath);
        }

        private static string ResolveAdbPath()
        {
            List<string> candidates = new List<string>();
            string configured = Environment.GetEnvironmentVariable("WPE_ADB_PATH");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                candidates.Add(configured.Trim());
            }

            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe"));
            candidates.Add("E:\\leidian\\LDPlayer14\\adb.exe");
            candidates.Add("C:\\leidian\\LDPlayer14\\adb.exe");

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                candidates.Add(Path.Combine(directory.Trim(), "adb.exe"));
            }

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Ignore malformed PATH entries and continue discovery.
                }
            }

            return string.Empty;
        }

        private static string SelectDeviceSerial(string output)
        {
            string configured = Environment.GetEnvironmentVariable("WPE_ADB_SERIAL");
            List<string> online = new List<string>();
            foreach (string rawLine in (output ?? string.Empty).Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] fields = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
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

        private static bool TryFindGameProcess(
            string adbPath,
            string deviceSerial,
            CancellationToken cancellationToken,
            out int gamePid,
            out string gamePackage)
        {
            gamePid = 0;
            gamePackage = string.Empty;
            List<string> packages = GetGamePackageCandidates().ToList();

            // 优先使用当前前台应用。这样游戏更换包名或版本后，只要用户
            // 仍在游戏内，C6 仍可绑定到当前进程；已知包名继续作为后台
            // 游戏和旧版本的兼容回退。
            string foregroundPackage = FindForegroundPackage(
                adbPath,
                deviceSerial,
                cancellationToken);
            if (IsLikelyGamePackage(foregroundPackage))
            {
                packages.RemoveAll(packageName =>
                    string.Equals(packageName, foregroundPackage, StringComparison.OrdinalIgnoreCase));
                packages.Insert(0, foregroundPackage);
            }

            foreach (string packageName in packages)
            {
                AdbCommandResult pidResult = RunRootShell(
                    adbPath,
                    deviceSerial,
                    "pidof " + packageName,
                    cancellationToken);
                int pid = ParseFirstPid(pidResult.Output);
                if (pid > 0)
                {
                    gamePid = pid;
                    gamePackage = packageName;
                    return true;
                }
            }

            // A few Android builds restrict pidof for shell callers. The process
            // table fallback remains read-only and also covers a newly discovered
            // foreground package.
            AdbCommandResult processTable = RunRootShell(
                adbPath,
                deviceSerial,
                "ps -A",
                cancellationToken);
            foreach (string packageName in packages)
            {
                foreach (string line in (processTable.Output ?? string.Empty).Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.IndexOf(packageName, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    int pid = ParsePidFromProcessLine(line);
                    if (pid > 0)
                    {
                        gamePid = pid;
                        gamePackage = packageName;
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<string> GetGamePackageCandidates()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string configured = Environment.GetEnvironmentVariable("WPE_TREASURE_GAME_PACKAGE");
            foreach (string packageName in (configured ?? string.Empty).Split(
                new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string normalized = packageName.Trim();
                if (Regex.IsMatch(normalized, @"^[A-Za-z0-9._-]+$") && seen.Add(normalized))
                {
                    yield return normalized;
                }
            }

            foreach (string packageName in GamePackageCandidates)
            {
                if (seen.Add(packageName))
                {
                    yield return packageName;
                }
            }
        }

        private static string FindForegroundPackage(
            string adbPath,
            string deviceSerial,
            CancellationToken cancellationToken)
        {
            AdbCommandResult activityDump = RunRootShell(
                adbPath,
                deviceSerial,
                "dumpsys activity activities",
                cancellationToken);
            return ExtractForegroundPackage(
                activityDump == null ? string.Empty : activityDump.Output);
        }

        private static string ExtractForegroundPackage(string output)
        {
            string[] markers =
            {
                "topResumedActivity",
                "ResumedActivity",
                "mCurrentFocus",
                "mFocusedApp"
            };

            foreach (string marker in markers)
            {
                foreach (string rawLine in (output ?? string.Empty).Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = rawLine.Trim();
                    if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    Match packageMatch = Regex.Match(
                        line,
                        @"\b([A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)/");
                    if (packageMatch.Success &&
                        IsLikelyGamePackage(packageMatch.Groups[1].Value))
                    {
                        return packageMatch.Groups[1].Value;
                    }
                }
            }

            return string.Empty;
        }

        private static bool IsLikelyGamePackage(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName) ||
                !Regex.IsMatch(packageName, @"^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+$"))
            {
                return false;
            }

            if (string.Equals(packageName, "android", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (string prefix in NonGamePackagePrefixes)
            {
                if (packageName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static string FindReaderPath(
            string adbPath,
            string deviceSerial,
            CancellationToken cancellationToken)
        {
            foreach (string candidate in ReaderPathCandidates)
            {
                AdbCommandResult result = RunRootShell(
                    adbPath,
                    deviceSerial,
                    "test -x " + candidate + " && echo ready",
                    cancellationToken);
                if (result.Succeeded &&
                    result.Output.IndexOf("ready", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static bool HasMatchingReader(string processTable, int gamePid)
        {
            string pidArgument = "--pid " + gamePid.ToString();
            string socketArgument = "--socket-name " + RemoteSocketName;
            string intervalArgument = "--interval-ms " + ReaderIntervalMilliseconds.ToString();
            foreach (ReaderProcessEntry entry in ParseReaderProcessEntries(processTable))
            {
                if (entry.CommandLine.IndexOf("treasure-streamd", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    entry.CommandLine.IndexOf(pidArgument, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    entry.CommandLine.IndexOf(socketArgument, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    entry.CommandLine.IndexOf(intervalArgument, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void StopExistingReaders(
            string adbPath,
            string deviceSerial,
            string processTable,
            CancellationToken cancellationToken)
        {
            List<int> pids = new List<int>();
            foreach (ReaderProcessEntry entry in ParseReaderProcessEntries(processTable))
            {
                if (entry.CommandLine.IndexOf("treasure-streamd", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                int pid = entry.Pid;
                if (pid > 0 && !pids.Contains(pid))
                {
                    pids.Add(pid);
                }
            }

            if (pids.Count > 0)
            {
                RunRootShell(
                    adbPath,
                    deviceSerial,
                    "kill " + string.Join(" ", pids),
                    cancellationToken);
            }
        }

        private static string GetReaderProcessTable(
            string adbPath,
            string deviceSerial,
            CancellationToken cancellationToken)
        {
            // Android's ps wraps long command lines at the terminal width. Read
            // /proc/<pid>/cmdline instead so the target pid and socket name stay
            // together. Houdini is included because the current emulator runs
            // the arm64 reader under its translation wrapper.
            string command =
                "for p in $(pidof houdini64 2>/dev/null) $(pidof treasure-streamd-resident 2>/dev/null) $(pidof treasure-streamd 2>/dev/null); " +
                "do echo PID:$p; cat /proc/$p/cmdline 2>/dev/null; echo; done";
            return RunRootShell(
                adbPath,
                deviceSerial,
                command,
                cancellationToken).Output;
        }

        private static IEnumerable<ReaderProcessEntry> ParseReaderProcessEntries(string processTable)
        {
            int currentPid = 0;
            StringBuilder currentCommand = new StringBuilder();
            foreach (string rawLine in (processTable ?? string.Empty).Replace('\0', ' ').Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("PID:", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentPid > 0)
                    {
                        yield return new ReaderProcessEntry(
                            currentPid,
                            currentCommand.ToString());
                    }

                    int parsedPid;
                    currentPid = int.TryParse(line.Substring(4).Trim(), out parsedPid)
                        ? parsedPid
                        : 0;
                    currentCommand.Clear();
                    continue;
                }

                if (currentPid > 0)
                {
                    if (currentCommand.Length > 0)
                    {
                        currentCommand.Append(' ');
                    }
                    currentCommand.Append(line);
                }
            }

            if (currentPid > 0)
            {
                yield return new ReaderProcessEntry(currentPid, currentCommand.ToString());
            }
        }

        private static bool WaitForReader(
            string adbPath,
            string deviceSerial,
            int gamePid,
            CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < ReaderReadyTimeoutMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string processTable = GetReaderProcessTable(
                    adbPath,
                    deviceSerial,
                    cancellationToken);
                if (HasMatchingReader(processTable, gamePid))
                {
                    return true;
                }

                Thread.Sleep(250);
            }

            return false;
        }

        private static bool WaitForLocalPort(CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < ReaderReadyTimeoutMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (CanConnectToLocalPort())
                {
                    return true;
                }

                Thread.Sleep(250);
            }

            return false;
        }

        private static bool CanConnectToLocalPort()
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult pending = client.BeginConnect(
                        TreasureC6StreamProtocolDefinition.DefaultHost,
                        TreasureC6StreamProtocolDefinition.DefaultPort,
                        null,
                        null);
                    if (!pending.AsyncWaitHandle.WaitOne(750))
                    {
                        return false;
                    }

                    client.EndConnect(pending);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static int ParseFirstPid(string output)
        {
            Match match = Regex.Match(output ?? string.Empty, @"\b[1-9][0-9]{0,8}\b");
            int pid;
            return match.Success && int.TryParse(match.Value, out pid) ? pid : 0;
        }

        private static int ParsePidFromProcessLine(string line)
        {
            Match match = Regex.Match(line ?? string.Empty, @"^\S+\s+(\d+)\s+");
            int pid;
            return match.Success && int.TryParse(match.Groups[1].Value, out pid) ? pid : 0;
        }

        private static AdbCommandResult RunRootShell(
            string adbPath,
            string deviceSerial,
            string command,
            CancellationToken cancellationToken)
        {
            string shellCommand = "su -c '" + (command ?? string.Empty).Replace("'", "'\\''") + "'";
            return RunAdb(
                adbPath,
                BuildArguments("-s", deviceSerial, "shell", shellCommand),
                cancellationToken);
        }

        private static AdbCommandResult RunAdb(
            string adbPath,
            string arguments,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                try
                {
                    if (!process.Start())
                    {
                        return new AdbCommandResult(-1, string.Empty, "进程启动失败。");
                    }
                }
                catch (Exception ex)
                {
                    return new AdbCommandResult(-1, string.Empty, ex.Message);
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!process.WaitForExit(200))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryKill(process);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (stopwatch.ElapsedMilliseconds >= CommandTimeoutMilliseconds)
                    {
                        TryKill(process);
                        return new AdbCommandResult(
                            -1,
                            stdoutTask.GetAwaiter().GetResult(),
                            "ADB 命令超时。");
                    }
                }

                process.WaitForExit();
                return new AdbCommandResult(
                    process.ExitCode,
                    stdoutTask.GetAwaiter().GetResult(),
                    stderrTask.GetAwaiter().GetResult());
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
                // Best effort only; the caller reports the original timeout.
            }
        }

        private static string BuildArguments(params string[] arguments)
        {
            return string.Join(
                " ",
                (arguments ?? new string[0]).Select(QuoteWindowsArgument));
        }

        private static string QuoteWindowsArgument(string value)
        {
            string text = value ?? string.Empty;
            if (text.Length > 0 &&
                text.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return text;
            }

            return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string Compact(string value)
        {
            string text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length > 260)
            {
                text = text.Substring(0, 260) + "…";
            }

            return text;
        }

        private sealed class AdbCommandResult
        {
            public AdbCommandResult(int exitCode, string output, string error)
            {
                this.ExitCode = exitCode;
                this.Output = output ?? string.Empty;
                this.Error = error ?? string.Empty;
            }

            public int ExitCode { get; private set; }

            public string Output { get; private set; }

            public string Error { get; private set; }

            public bool Succeeded
            {
                get { return this.ExitCode == 0; }
            }

            public string ErrorOrOutput
            {
                get
                {
                    return string.IsNullOrWhiteSpace(this.Error)
                        ? this.Output
                        : this.Error;
                }
            }
        }

        private sealed class ReaderProcessEntry
        {
            public ReaderProcessEntry(int pid, string commandLine)
            {
                this.Pid = pid;
                this.CommandLine = commandLine ?? string.Empty;
            }

            public int Pid { get; private set; }

            public string CommandLine { get; private set; }
        }
    }
}
