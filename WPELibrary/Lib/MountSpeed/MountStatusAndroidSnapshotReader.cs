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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WPELibrary.Lib.Android;

namespace WPELibrary.Lib.MountSpeed
{
    public sealed class MountStatusReadDiagnostics
    {
        public string ReadPath { get; internal set; }

        public double RootAdbMilliseconds { get; internal set; }

        public double ProbeMilliseconds { get; internal set; }

        public double CardParseMilliseconds { get; internal set; }

        public double ProtocolParseMilliseconds { get; internal set; }

        public double TotalMilliseconds { get; internal set; }
    }

    public sealed class MountStatusAndroidSnapshotReadResult
    {
        internal MountStatusAndroidSnapshotReadResult(
            MountStatusReadOnlySnapshot snapshot,
            string error,
            bool wasCancelled = false,
            MountStatusReadDiagnostics diagnostics = null)
        {
            this.Snapshot = snapshot;
            this.Error = error ?? string.Empty;
            this.WasCancelled = wasCancelled;
            this.Diagnostics = diagnostics;
        }

        public MountStatusReadOnlySnapshot Snapshot { get; private set; }

        public string Error { get; private set; }

        public bool WasCancelled { get; private set; }

        public MountStatusReadDiagnostics Diagnostics { get; private set; }

        public bool Succeeded
        {
            get { return this.Snapshot != null && string.IsNullOrWhiteSpace(this.Error); }
        }
    }

    /// <summary>
    /// Windows-to-ADB/Python bridge for the Android LuaJIT read-only mount
    /// probe. The bridge only launches the probe and validates its snapshot;
    /// it has no memory-write or packet-send path.
    /// </summary>
    public sealed class MountStatusAndroidSnapshotReader : IDisposable
    {
        private const string DefaultGamePackage = "com.gdoo.yzqcxy";
        private const int DefaultCommandTimeoutMilliseconds = 300000;
        private const string DefaultProbeRelativePath = "tools\\mount-reader\\mount_status_luajit_probe.py";
        private const int TransientProbeRetryCount = 1;
        private const int TransientProbeRetryDelayMilliseconds = 1000;
        private const int TransientMountedSnapshotRetryCount = 2;
        private const int TransientRefineCardRetryCount = 2;
        private const int CachedRefineCardRetryCount = 2;
        private const int CachedRefineCardRetryDelayMilliseconds = 250;

        private readonly string configuredAdbPath;
        private readonly string configuredSerial;
        private readonly string configuredPackage;
        private readonly string configuredPythonPath;
        private readonly string configuredProbePath;
        private readonly SemaphoreSlim readGate = new SemaphoreSlim(1, 1);
        private readonly object probeWorkerSync = new object();
        private MountStatusProbeWorker probeWorker;
        private AndroidRootShellSessionLease rootSessionLease;
        private string rootSessionAdb;
        private string rootSessionSerial;
        private int disposed;
        private bool hasSuccessfulRead;
        private string lastProbeCachePath;
        private MountStatusReadDiagnostics lastReadDiagnostics;

        public MountStatusAndroidSnapshotReader()
            : this(null, null, null, null, null)
        {
        }

        public MountStatusAndroidSnapshotReader(
            string adbPath = null,
            string deviceSerial = null,
            string gamePackage = null,
            string pythonPath = null,
            string probePath = null)
        {
            this.configuredAdbPath = adbPath;
            this.configuredSerial = deviceSerial;
            this.configuredPackage = gamePackage;
            this.configuredPythonPath = pythonPath;
            this.configuredProbePath = probePath;
        }

        public async Task<MountStatusAndroidSnapshotReadResult> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            return await this.ReadSnapshotAsync(
                cancellationToken,
                false).ConfigureAwait(false);
        }

        public async Task<MountStatusAndroidSnapshotReadResult> ReadSnapshotAsync(
            CancellationToken cancellationToken,
            bool requireCompleteRefineCards)
        {
            bool gateEntered = false;
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            try
            {
                await this.readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateEntered = true;
                this.lastReadDiagnostics = new MountStatusReadDiagnostics
                {
                    ReadPath = "fallback"
                };
                string error = string.Empty;
                MountStatusReadOnlySnapshot snapshot = await Task.Run(
                    () => this.ReadSnapshot(
                        cancellationToken,
                        out error,
                        0,
                        false,
                        requireCompleteRefineCards,
                        0),
                    CancellationToken.None).ConfigureAwait(false);
                MountStatusReadDiagnostics diagnostics = this.lastReadDiagnostics ??
                    new MountStatusReadDiagnostics();
                diagnostics.TotalMilliseconds = totalStopwatch.Elapsed.TotalMilliseconds;
                if (snapshot != null &&
                    string.IsNullOrWhiteSpace(error) &&
                    !string.IsNullOrWhiteSpace(this.lastProbeCachePath) &&
                    File.Exists(this.lastProbeCachePath) &&
                    (!requireCompleteRefineCards || HasCompleteRefineCards(snapshot)))
                {
                    this.hasSuccessfulRead = true;
                }
                return new MountStatusAndroidSnapshotReadResult(snapshot, error, false, diagnostics);
            }
            catch (OperationCanceledException)
            {
                return new MountStatusAndroidSnapshotReadResult(
                    null,
                    "坐骑 Android 只读读取已取消。",
                    true,
                    this.lastReadDiagnostics);
            }
            catch (Exception ex)
            {
                return new MountStatusAndroidSnapshotReadResult(
                    null,
                    "坐骑 Android 只读读取失败：" + ex.Message,
                    false,
                    this.lastReadDiagnostics);
            }
            finally
            {
                if (gateEntered)
                {
                    this.readGate.Release();
                }
            }
        }

        public async Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken)
        {
            MountStatusAndroidSnapshotReadResult result = await this.ReadSnapshotAsync(
                cancellationToken).ConfigureAwait(false);
            return result != null && result.Succeeded &&
                result.Snapshot != null && result.Snapshot.IsValid;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            MountStatusProbeWorker worker;
            AndroidRootShellSessionLease rootLease;
            lock (this.probeWorkerSync)
            {
                worker = this.probeWorker;
                this.probeWorker = null;
                rootLease = this.rootSessionLease;
                this.rootSessionLease = null;
                this.rootSessionAdb = null;
                this.rootSessionSerial = null;
            }
            if (worker != null)
            {
                worker.Dispose();
            }
            if (rootLease != null)
            {
                rootLease.Dispose();
            }
            // Keep the gate usable for an in-flight ReadSnapshotAsync to
            // release itself after the worker has been cancelled. The reader
            // is no longer reachable after its owner releases it, so the
            // managed gate can be reclaimed normally.
        }

        private AdbCommandResult RunProbeWorker(
            string pythonPath,
            string probePath,
            string adbPath,
            string serial,
            JObject request,
            CancellationToken cancellationToken)
        {
            lock (this.probeWorkerSync)
            {
                if (Volatile.Read(ref this.disposed) != 0)
                {
                    throw new ObjectDisposedException("MountStatusAndroidSnapshotReader");
                }
                string rootBrokerPipe = this.EnsureRootSession(
                    adbPath,
                    serial,
                    cancellationToken);
                if (this.probeWorker == null)
                {
                    this.probeWorker = new MountStatusProbeWorker();
                }
                return this.probeWorker.SendRequest(
                    pythonPath,
                    probePath,
                    adbPath,
                    serial,
                    request,
                    rootBrokerPipe,
                    cancellationToken);
            }
        }

        private string EnsureRootSession(
            string adbPath,
            string serial,
            CancellationToken cancellationToken)
        {
            if (this.rootSessionLease != null &&
                string.Equals(this.rootSessionAdb, adbPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(this.rootSessionSerial, serial, StringComparison.Ordinal) &&
                this.rootSessionLease.Session.IsAlive)
            {
                return this.rootSessionLease.BrokerPipePath;
            }

            if (this.rootSessionLease != null)
            {
                this.rootSessionLease.Dispose();
                this.rootSessionLease = null;
            }

            this.rootSessionLease = AndroidRootShellSessionManager.Acquire(
                adbPath,
                serial,
                cancellationToken);
            this.rootSessionAdb = adbPath;
            this.rootSessionSerial = serial;
            return this.rootSessionLease.BrokerPipePath;
        }

        private MountStatusReadOnlySnapshot ReadSnapshot(
            CancellationToken cancellationToken,
            out string error,
            int mountedSnapshotRetry = 0,
            bool forceDiscovery = false,
            bool requireCompleteRefineCards = false,
            int refineCardRetry = 0,
            int cachedPlanRetry = 0)
        {
            error = string.Empty;
            cancellationToken.ThrowIfCancellationRequested();

            string adbPath = ResolveAdbPath(this.configuredAdbPath);
            if (string.IsNullOrWhiteSpace(adbPath))
            {
                error = "未找到 adb.exe。";
                return null;
            }

            string pythonPath = ResolvePythonPath(this.configuredPythonPath);
            if (string.IsNullOrWhiteSpace(pythonPath))
            {
                error = "未找到 Python 解释器。";
                return null;
            }

            string probePath = ResolveProbePath(this.configuredProbePath);
            if (string.IsNullOrWhiteSpace(probePath) || !File.Exists(probePath))
            {
                error = "未找到坐骑 LuaJIT 只读探针：" + (probePath ?? "<empty>");
                return null;
            }

            AdbCommandResult devices = RunProcess(
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

            string rootBrokerPipe = this.EnsureRootSession(
                adbPath,
                serial,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            string cachePath = ResolveProbeCachePath(serial, packageName);
            this.lastProbeCachePath = cachePath;
            AdbCommandResult probe = null;
            int probeAttempt = 0;
            int maxProbeAttempts = 1 + TransientProbeRetryCount;
            bool cacheOnly = this.hasSuccessfulRead && !forceDiscovery;
            while (probeAttempt < maxProbeAttempts)
            {
                probeAttempt++;
                JObject probeRequest = new JObject();
                probeRequest["pid"] = gamePid;
                probeRequest["cachePath"] = cachePath;
                probeRequest["cacheOnly"] = cacheOnly;
                probeRequest["requireCompleteRefineCards"] = requireCompleteRefineCards;
                probeRequest["readTimeout"] = 8.0;
                probe = this.RunProbeWorker(
                    pythonPath,
                    probePath,
                    adbPath,
                    serial,
                    probeRequest,
                    cancellationToken);
                if (!probe.Succeeded && !IsProbeWorkerResponseTimeout(probe))
                {
                    // Keep the old one-shot path as a bounded compatibility
                    // fallback if the long-lived worker cannot start or
                    // loses its JSONL channel. A response timeout means the
                    // worker already spent the full discovery budget in the
                    // same backend, so repeating it cannot improve recovery.
                    List<string> probeArguments = new List<string>
                    {
                        probePath,
                        "--adb",
                        adbPath,
                        "--serial",
                        serial,
                        "--pid",
                        gamePid.ToString(),
                        "--cache-path",
                        cachePath,
                        "--root-broker-pipe",
                        rootBrokerPipe
                    };
                    if (cacheOnly)
                    {
                        probeArguments.Add("--cache-only");
                    }
                    if (requireCompleteRefineCards)
                    {
                        probeArguments.Add("--require-complete-refine-cards");
                    }
                    probe = RunProcess(
                        pythonPath,
                        BuildArguments(probeArguments.ToArray()),
                        cancellationToken);
                }

                if (probe.Succeeded ||
                    !IsTransientDiscoveryFailure(probe.Output) ||
                    probeAttempt >= maxProbeAttempts)
                {
                    break;
                }

                if (cancellationToken.WaitHandle.WaitOne(TransientProbeRetryDelayMilliseconds))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            if (cacheOnly && IsCacheDiscoveryFallbackRequired(probe.Output))
            {
                if (IsCachedRefineCardsIncomplete(probe.Output) &&
                    cachedPlanRetry < CachedRefineCardRetryCount)
                {
                    if (cancellationToken.WaitHandle.WaitOne(
                        CachedRefineCardRetryDelayMilliseconds))
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    return this.ReadSnapshot(
                        cancellationToken,
                        out error,
                        mountedSnapshotRetry,
                        false,
                        requireCompleteRefineCards,
                        refineCardRetry,
                        cachedPlanRetry + 1);
                }

                this.hasSuccessfulRead = false;
                return this.ReadSnapshot(
                    cancellationToken,
                    out error,
                    mountedSnapshotRetry,
                    true,
                    requireCompleteRefineCards,
                    refineCardRetry,
                    cachedPlanRetry);
            }
            if (requireCompleteRefineCards &&
                IsRefineCardDiscoveryRequired(probe.Output))
            {
                this.hasSuccessfulRead = false;
                if (refineCardRetry < TransientRefineCardRetryCount)
                {
                    if (cancellationToken.WaitHandle.WaitOne(
                        TransientProbeRetryDelayMilliseconds))
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    return this.ReadSnapshot(
                        cancellationToken,
                        out error,
                        mountedSnapshotRetry,
                        true,
                        requireCompleteRefineCards,
                        refineCardRetry + 1,
                        cachedPlanRetry);
                }

                error = "坐骑只读探针未返回完整 21 张炼化卡。";
                return null;
            }
            if (!probe.Succeeded)
            {
                error = "坐骑只读探针执行失败（包名 " + packageName + "）：" +
                    (probeAttempt > 1
                        ? "已重试 " + (probeAttempt - 1) + " 次；"
                        : string.Empty) +
                    Compact(probe.ErrorOrOutput);
                return null;
            }

            string json;
            if (!TryGetSingleJsonLine(probe.Output, out json, out error))
            {
                return null;
            }

            MountStatusReadOnlySnapshot snapshot;
            string parseError;
            Stopwatch protocolParseStopwatch = Stopwatch.StartNew();
            bool parsed = MountStatusReadOnlySnapshotProtocol.TryParse(
                json,
                out snapshot,
                out parseError);
            protocolParseStopwatch.Stop();
            MountStatusReadDiagnostics probeDiagnostics = ReadProbeDiagnostics(json);
            probeDiagnostics.ProtocolParseMilliseconds = protocolParseStopwatch.Elapsed.TotalMilliseconds;
            this.lastReadDiagnostics = probeDiagnostics;
            if (!parsed)
            {
                error = "坐骑只读快照校验失败：" + parseError;
                return null;
            }

            if (snapshot.Process == null || snapshot.Process.Pid != gamePid)
            {
                error = "坐骑只读快照进程身份与发现的游戏 PID 不一致。";
                return null;
            }

            if (requireCompleteRefineCards && !HasCompleteRefineCards(snapshot))
            {
                this.hasSuccessfulRead = false;
                if (refineCardRetry < TransientRefineCardRetryCount)
                {
                    if (cancellationToken.WaitHandle.WaitOne(
                        TransientProbeRetryDelayMilliseconds))
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    return this.ReadSnapshot(
                        cancellationToken,
                        out error,
                        mountedSnapshotRetry,
                        true,
                        requireCompleteRefineCards,
                        refineCardRetry + 1,
                        cachedPlanRetry);
                }

                error = string.Format(
                    "坐骑只读快照中的炼化卡不完整：当前读取到 {0} 张，要求完整 21 张。",
                    snapshot.RideRefineCards == null
                        ? 0
                        : snapshot.RideRefineCards.Count);
                return null;
            }

            // LuaJIT discovery may produce a schema-valid frame one tick
            // before the active ride pointer and its formal skill list have
            // converged. Treat that narrow state as transient; never replace
            // the final fail-closed validation with guessed ride data.
            if (IsTransientMountedSnapshot(snapshot) &&
                mountedSnapshotRetry < TransientMountedSnapshotRetryCount)
            {
                if (cancellationToken.WaitHandle.WaitOne(
                    TransientProbeRetryDelayMilliseconds))
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return this.ReadSnapshot(
                    cancellationToken,
                    out error,
                    mountedSnapshotRetry + 1,
                    forceDiscovery,
                    requireCompleteRefineCards,
                    refineCardRetry,
                    cachedPlanRetry);
            }

            return snapshot;
        }

        private static MountStatusReadDiagnostics ReadProbeDiagnostics(string json)
        {
            MountStatusReadDiagnostics diagnostics = new MountStatusReadDiagnostics
            {
                ReadPath = "fallback"
            };
            try
            {
                JObject root = JObject.Parse(json);
                JObject reader = root["reader"] as JObject;
                if (reader == null)
                {
                    return diagnostics;
                }

                string readPath = reader.Value<string>("readPath");
                if (!string.IsNullOrWhiteSpace(readPath) &&
                    (string.Equals(readPath, "point", StringComparison.Ordinal) ||
                     string.Equals(readPath, "fast", StringComparison.Ordinal) ||
                     string.Equals(readPath, "warm", StringComparison.Ordinal) ||
                     string.Equals(readPath, "cold", StringComparison.Ordinal) ||
                     string.Equals(readPath, "discover", StringComparison.Ordinal) ||
                     string.Equals(readPath, "fallback", StringComparison.Ordinal)))
                {
                    diagnostics.ReadPath = readPath;
                }

                JObject timing = reader["timingMs"] as JObject;
                if (timing == null)
                {
                    return diagnostics;
                }

                diagnostics.RootAdbMilliseconds = ReadMilliseconds(timing["rootAdbMs"]);
                diagnostics.ProbeMilliseconds = ReadMilliseconds(timing["probeMs"]);
                diagnostics.CardParseMilliseconds = ReadMilliseconds(timing["cardParseMs"]);
            }
            catch (Exception)
            {
                // Diagnostics are optional and must never make a valid
                // read fail. The protocol parser remains authoritative.
            }
            return diagnostics;
        }

        private static double ReadMilliseconds(JToken token)
        {
            double value;
            if (token == null ||
                !double.TryParse(
                    token.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) ||
                double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < 0)
            {
                return 0;
            }
            return Math.Min(value, 300000);
        }

        private static bool IsTransientMountedSnapshot(
            MountStatusReadOnlySnapshot snapshot)
        {
            if (snapshot == null || snapshot.IsMounted != true)
            {
                return false;
            }

            MountRideInstanceSnapshot current =
                (snapshot.RideInstances ??
                    new List<MountRideInstanceSnapshot>())
                .FirstOrDefault(instance => instance != null &&
                    instance.IsCurrent == true &&
                    string.Equals(
                        instance.RideInstanceId,
                        snapshot.ActiveRideInstanceId,
                        StringComparison.Ordinal));
            return !string.Equals(
                    snapshot.RideBindingStatus,
                    "bound",
                    StringComparison.OrdinalIgnoreCase) ||
                current == null ||
                current.Skills == null ||
                current.Skills.Count == 0;
        }

        private static bool HasCompleteRefineCards(
            MountStatusReadOnlySnapshot snapshot)
        {
            return snapshot != null &&
                snapshot.RideRefineCards != null &&
                snapshot.RideRefineCards.Count == 21;
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
                AdbCommandResult result = RunProcess(
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
            string[] values =
            {
                configuredPackage,
                Environment.GetEnvironmentVariable("WPE_MOUNT_STATUS_GAME_PACKAGE"),
                DefaultGamePackage
            };

            foreach (string value in values)
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
            return !string.IsNullOrWhiteSpace(value) &&
                Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+$");
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
            return candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        private static string ResolvePythonPath(string configured)
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured.Trim());
            string environmentPath = Environment.GetEnvironmentVariable("WPE_PYTHON_PATH");
            if (!string.IsNullOrWhiteSpace(environmentPath)) candidates.Add(environmentPath.Trim());
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XNAS", "WPE", "vision-worker", ".venv", "Scripts", "python.exe"));
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XNAS", "WPE", "vision-worker", "python", "python.exe"));
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python.exe"));
            candidates.Add("python.exe");
            candidates.Add("py.exe");
            foreach (string candidate in candidates)
            {
                if (Path.IsPathRooted(candidate))
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                    continue;
                }

                string pathMatch = FindOnPath(candidate);
                if (!string.IsNullOrWhiteSpace(pathMatch))
                {
                    return pathMatch;
                }

                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            return string.Empty;
        }

        private static string FindOnPath(string fileName)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return string.Empty;
        }

        private static string ResolveProbePath(string configured)
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured.Trim());
            string environmentPath = Environment.GetEnvironmentVariable("WPE_MOUNT_STATUS_PROBE_PY");
            if (!string.IsNullOrWhiteSpace(environmentPath)) candidates.Add(environmentPath.Trim());
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultProbeRelativePath));
            candidates.Add(Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..",
                "..",
                "..",
                DefaultProbeRelativePath)));
            return candidates.FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault();
        }

        private static string ResolveProbeCachePath(string serial, string packageName)
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XNAS",
                "WPE",
                "mount-refine",
                "probe-cache");
            return Path.Combine(
                directory,
                SafeCacheFilePart(serial) + "-" + SafeCacheFilePart(packageName) + ".json");
        }

        private static string SafeCacheFilePart(string value)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
            StringBuilder builder = new StringBuilder(normalized.Length);
            foreach (char character in normalized)
            {
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.')
                {
                    builder.Append(character);
                }
                else
                {
                    builder.Append('-');
                }
            }
            return builder.Length == 0 ? "default" : builder.ToString();
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
                error = "坐骑只读探针未返回唯一 JSON 快照。";
                return false;
            }
            json = jsonLines[0];
            return true;
        }

        private static bool IsTransientDiscoveryFailure(string output)
        {
            string json;
            string error;
            if (!TryGetSingleJsonLine(output, out json, out error))
            {
                return false;
            }

            try
            {
                JObject payload = JObject.Parse(json);
                string status = (string)payload["status"];
                string diagnosticCode = (string)payload["diagnosticCode"];
                return string.Equals(status, "needs_discover", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(diagnosticCode, "local_player_missing", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(diagnosticCode, "field_string_missing", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(diagnosticCode, "refine_cards_incomplete", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsProbeWorkerResponseTimeout(AdbCommandResult result)
        {
            return result != null &&
                !result.Succeeded &&
                result.ErrorOrOutput.IndexOf(
                    "坐骑 LuaJIT 长驻探针响应超时",
                    StringComparison.Ordinal) >= 0;
        }

        private static bool IsCacheDiscoveryFallbackRequired(string output)
        {
            string json;
            string error;
            if (!TryGetSingleJsonLine(output, out json, out error))
            {
                return false;
            }

            try
            {
                JObject payload = JObject.Parse(json);
                string status = (string)payload["status"];
                string diagnosticCode = (string)payload["diagnosticCode"];
                return string.Equals(status, "needs_discover", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(diagnosticCode, "cached_plan_invalid", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(diagnosticCode, "cached_plan_missing", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(diagnosticCode, "cached_refine_cards_incomplete", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsCachedRefineCardsIncomplete(string output)
        {
            string json;
            string error;
            if (!TryGetSingleJsonLine(output, out json, out error))
            {
                return false;
            }

            try
            {
                JObject payload = JObject.Parse(json);
                return string.Equals(
                        (string)payload["status"],
                        "needs_discover",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        (string)payload["diagnosticCode"],
                        "cached_refine_cards_incomplete",
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsRefineCardDiscoveryRequired(string output)
        {
            string json;
            string error;
            if (!TryGetSingleJsonLine(output, out json, out error))
            {
                return false;
            }

            try
            {
                JObject payload = JObject.Parse(json);
                return string.Equals(
                        (string)payload["status"],
                        "needs_discover",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        (string)payload["diagnosticCode"],
                        "refine_cards_incomplete",
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
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

    private static AdbCommandResult RunProcess(
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
                        return new AdbCommandResult(false, string.Empty, "进程启动失败。", -1);
                    }
                }
                catch (Exception ex)
                {
                    return new AdbCommandResult(false, string.Empty, ex.Message, -1);
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (stopwatch.ElapsedMilliseconds >= GetCommandTimeoutMilliseconds())
                        {
                            TerminateProcess(process);
                            return new AdbCommandResult(
                                false,
                                outputTask.IsCompleted ? outputTask.Result : string.Empty,
                                "命令超时。",
                                -1);
                        }
                    }

                    // Ensure redirected output has been fully drained before
                    // reading the tasks. This remains bounded by the child
                    // process exit and avoids the old uninterruptible wait.
                    process.WaitForExit();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    TerminateProcess(process);
                    throw;
                }

                string output = outputTask.Result;
                string error = errorTask.Result;
                return new AdbCommandResult(
                    process.ExitCode == 0,
                    output,
                    string.IsNullOrWhiteSpace(error) ? output : error,
                    process.ExitCode);
            }
        }

        private static void TerminateProcess(Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // The child may have exited between the checks. The caller
                // still owns the original timeout/cancellation result.
            }

            try
            {
                process.WaitForExit(1000);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static int GetCommandTimeoutMilliseconds()
        {
            int configured;
            if (int.TryParse(
                Environment.GetEnvironmentVariable("WPE_MOUNT_STATUS_TIMEOUT_MS"),
                out configured) && configured >= 1000)
            {
                return configured;
            }
            return DefaultCommandTimeoutMilliseconds;
        }

        private static string Compact(string value)
        {
            string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= 600 ? normalized : normalized.Substring(0, 600);
        }

        private sealed class MountStatusProbeWorker : IDisposable
        {
            private const int StartupTimeoutMilliseconds = 30000;

            private readonly object processSync = new object();
            private Process process;
            private string workerPython;
            private string workerProbe;
            private string workerAdb;
            private string workerSerial;
            private string workerRootBrokerPipe;
            private string lastError = string.Empty;
            private int requestId;
            private int disposed;

            internal AdbCommandResult SendRequest(
                string pythonPath,
                string probePath,
                string adbPath,
                string serial,
                JObject request,
                string rootBrokerPipe,
                CancellationToken cancellationToken)
            {
                lock (this.processSync)
                {
                    if (Volatile.Read(ref this.disposed) != 0)
                    {
                        throw new ObjectDisposedException("MountStatusProbeWorker");
                    }

                    try
                    {
                        this.EnsureProcess(
                            pythonPath,
                            probePath,
                            adbPath,
                            serial,
                            request == null
                                ? 1
                                : request.Value<int?>("pid") ?? 1,
                            rootBrokerPipe,
                            cancellationToken);
                        JObject payload = request ?? new JObject();
                        payload["id"] = Interlocked.Increment(ref this.requestId);
                        this.process.StandardInput.WriteLine(payload.ToString(Formatting.None));
                        this.process.StandardInput.Flush();
                        string response = this.ReadLineWithTimeout(
                            cancellationToken,
                            MountStatusAndroidSnapshotReader.GetCommandTimeoutMilliseconds());
                        return new AdbCommandResult(true, response, response, 0);
                    }
                    catch (OperationCanceledException)
                    {
                        this.ResetProcess();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        string details = string.IsNullOrWhiteSpace(this.lastError)
                            ? ex.Message
                            : ex.Message + "；" + this.lastError;
                        this.ResetProcess();
                        return new AdbCommandResult(false, string.Empty, details, -1);
                    }
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref this.disposed, 1) != 0)
                {
                    return;
                }
                lock (this.processSync)
                {
                    this.ResetProcess();
                }
            }

            private void EnsureProcess(
                string pythonPath,
                string probePath,
                string adbPath,
                string serial,
                int initialPid,
                string rootBrokerPipe,
                CancellationToken cancellationToken)
            {
                if (this.process != null &&
                    !this.process.HasExited &&
                    string.Equals(this.workerPython, pythonPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(this.workerProbe, probePath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(this.workerAdb, adbPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(this.workerSerial, serial, StringComparison.Ordinal) &&
                    string.Equals(this.workerRootBrokerPipe, rootBrokerPipe, StringComparison.Ordinal))
                {
                    return;
                }

                this.ResetProcess();
                string workingDirectory = Path.GetDirectoryName(probePath);
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = MountStatusAndroidSnapshotReader.BuildArguments(
                        "-u",
                        probePath,
                        "--server",
                        "--adb",
                        adbPath,
                        "--serial",
                        serial,
                        "--pid",
                        initialPid.ToString(),
                        "--root-broker-pipe",
                        rootBrokerPipe),
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                        ? AppDomain.CurrentDomain.BaseDirectory
                        : workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                Process next = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                next.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        this.lastError = args.Data;
                    }
                };
                if (!next.Start())
                {
                    next.Dispose();
                    throw new InvalidOperationException("坐骑 LuaJIT 长驻探针启动失败。");
                }

                this.process = next;
                this.workerPython = pythonPath;
                this.workerProbe = probePath;
                this.workerAdb = adbPath;
                this.workerSerial = serial;
                this.workerRootBrokerPipe = rootBrokerPipe;
                this.lastError = string.Empty;
                next.BeginErrorReadLine();
                try
                {
                    string readyLine = this.ReadLineWithTimeout(
                        cancellationToken,
                        Math.Min(
                            StartupTimeoutMilliseconds,
                            MountStatusAndroidSnapshotReader.GetCommandTimeoutMilliseconds()));
                    JObject ready = JObject.Parse(readyLine);
                    if (!string.Equals(
                            (string)ready["event"],
                            "mount_status_probe_ready",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("坐骑 LuaJIT 长驻探针未返回就绪标记。");
                    }
                }
                catch
                {
                    this.ResetProcess();
                    throw;
                }
            }

            private string ReadLineWithTimeout(
                CancellationToken cancellationToken,
                int timeoutMilliseconds)
            {
                if (this.process == null || this.process.HasExited)
                {
                    throw new InvalidOperationException("坐骑 LuaJIT 长驻探针已退出。");
                }

                Task<string> readTask = this.process.StandardOutput.ReadLineAsync();
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
                while (!readTask.Wait(20))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException("坐骑 LuaJIT 长驻探针响应超时。");
                    }
                    Thread.Sleep(10);
                }

                string line = readTask.GetAwaiter().GetResult();
                if (line == null)
                {
                    throw new InvalidOperationException("坐骑 LuaJIT 长驻探针已关闭输出。");
                }
                return line;
            }

            private void ResetProcess()
            {
                Process oldProcess = this.process;
                this.process = null;
                this.workerPython = null;
                this.workerProbe = null;
                this.workerAdb = null;
                this.workerSerial = null;
                this.workerRootBrokerPipe = null;
                if (oldProcess == null)
                {
                    return;
                }

                try
                {
                    if (!oldProcess.HasExited)
                    {
                        oldProcess.StandardInput.Close();
                    }
                }
                catch
                {
                    // The worker may already be closing its input stream.
                }
                try
                {
                    if (!oldProcess.HasExited)
                    {
                        oldProcess.WaitForExit(1000);
                    }
                }
                catch
                {
                    // Best-effort graceful shutdown before the hard fallback.
                }
                MountStatusAndroidSnapshotReader.TerminateProcess(oldProcess);
                oldProcess.Dispose();
            }
        }

        private sealed class AdbCommandResult
        {
            internal AdbCommandResult(bool succeeded, string output, string errorOrOutput, int exitCode)
            {
                this.Succeeded = succeeded;
                this.Output = output ?? string.Empty;
                this.ErrorOrOutput = errorOrOutput ?? string.Empty;
                this.ExitCode = exitCode;
            }

            internal bool Succeeded { get; private set; }

            internal string Output { get; private set; }

            internal string ErrorOrOutput { get; private set; }

            internal int ExitCode { get; private set; }
        }
    }
}
