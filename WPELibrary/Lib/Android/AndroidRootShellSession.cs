using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.Android
{
    /// <summary>
    /// Result of one command executed through the process-wide Android Root
    /// shell. The command itself must remain read-only or be an explicitly
    /// authorized reader lifecycle operation supplied by the caller.
    /// </summary>
    public sealed class AndroidRootShellCommandResult
    {
        internal AndroidRootShellCommandResult(
            bool succeeded,
            string output,
            string error,
            int exitCode)
        {
            this.Succeeded = succeeded;
            this.Output = output ?? string.Empty;
            this.Error = error ?? string.Empty;
            this.ExitCode = exitCode;
        }

        public bool Succeeded { get; private set; }

        public string Output { get; private set; }

        public string Error { get; private set; }

        public int ExitCode { get; private set; }
    }

    /// <summary>
    /// A lease on the shared Root shell for one ADB device. Disposing a lease
    /// releases the caller's ownership but intentionally leaves the session in
    /// the process-wide manager so later presets can reuse the same `su`
    /// process without requesting Shell authorization again.
    /// </summary>
    public sealed class AndroidRootShellSessionLease : IDisposable
    {
        private AndroidRootShellSession session;
        private int disposed;

        internal AndroidRootShellSessionLease(AndroidRootShellSession session)
        {
            this.session = session;
        }

        internal AndroidRootShellSession Session
        {
            get
            {
                AndroidRootShellSession current = this.session;
                if (current == null || Volatile.Read(ref this.disposed) != 0)
                {
                    throw new ObjectDisposedException("AndroidRootShellSessionLease");
                }
                return current;
            }
        }

        public string BrokerPipePath
        {
            get { return this.Session.BrokerPipePath; }
        }

        public AndroidRootShellCommandResult Execute(
            string command,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            return this.Session.Execute(command, timeoutMilliseconds, cancellationToken);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            this.session = null;
        }
    }

    /// <summary>
    /// Process-wide owner of one persistent `adb shell -t su -c sh` per
    /// (adb.exe, device serial) pair. C# callers use Execute directly; Python
    /// readers use the local named-pipe broker exposed by the same session.
    /// No caller should build a separate `adb shell su` command.
    /// </summary>
    public static class AndroidRootShellSessionManager
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, AndroidRootShellSession> Sessions =
            new Dictionary<string, AndroidRootShellSession>(StringComparer.OrdinalIgnoreCase);

        static AndroidRootShellSessionManager()
        {
            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                ShutdownAll();
            };
        }

        public static AndroidRootShellSessionLease Acquire(
            string adbPath,
            string deviceSerial,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(adbPath))
            {
                throw new ArgumentException("adb 路径不能为空。", "adbPath");
            }
            if (string.IsNullOrWhiteSpace(deviceSerial))
            {
                throw new ArgumentException("Android 设备序列号不能为空。", "deviceSerial");
            }

            cancellationToken.ThrowIfCancellationRequested();
            string key = BuildKey(adbPath, deviceSerial);
            AndroidRootShellSession stale = null;
            AndroidRootShellSession session;
            lock (Sync)
            {
                if (Sessions.TryGetValue(key, out session) && !session.IsAlive)
                {
                    Sessions.Remove(key);
                    stale = session;
                    session = null;
                }

                if (session == null)
                {
                    if (stale != null)
                    {
                        stale.Dispose();
                    }
                    session = new AndroidRootShellSession(adbPath, deviceSerial, cancellationToken);
                    Sessions[key] = session;
                }
            }

            return new AndroidRootShellSessionLease(session);
        }

        public static void ShutdownAll()
        {
            List<AndroidRootShellSession> sessions;
            lock (Sync)
            {
                sessions = Sessions.Values.ToList();
                Sessions.Clear();
            }

            foreach (AndroidRootShellSession session in sessions)
            {
                try
                {
                    session.Dispose();
                }
                catch
                {
                    // Process-exit cleanup is best effort.
                }
            }
        }

        private static string BuildKey(string adbPath, string deviceSerial)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(adbPath.Trim());
            }
            catch
            {
                fullPath = adbPath.Trim();
            }
            return fullPath + "\n" + deviceSerial.Trim();
        }
    }

    internal sealed class AndroidRootShellSession : IDisposable
    {
        private const string ReadyMarker = "WPE_ANDROID_ROOT_READY";
        private const string EndMarkerPrefix = "WPE_ANDROID_ROOT_END_";
        private const int StartupTimeoutMilliseconds = 15000;

        private readonly object processSync = new object();
        private readonly object pipeSync = new object();
        private readonly SemaphoreSlim commandGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource brokerCancellation =
            new CancellationTokenSource();
        private readonly string adbPath;
        private readonly string deviceSerial;
        private readonly string pipeName;
        private readonly string brokerPipePath;
        private Process process;
        private NamedPipeServerStream activePipe;
        private string lastError = string.Empty;
        private int disposed;

        internal AndroidRootShellSession(
            string adbPath,
            string deviceSerial,
            CancellationToken cancellationToken)
        {
            this.adbPath = adbPath.Trim();
            this.deviceSerial = deviceSerial.Trim();
            this.pipeName = "WPE.AndroidRoot." + Guid.NewGuid().ToString("N");
            this.brokerPipePath = @"\\.\pipe\" + this.pipeName;

            try
            {
                this.StartProcess(cancellationToken);
                this.StartBroker();
            }
            catch
            {
                this.Dispose();
                throw;
            }
        }

        internal string BrokerPipePath
        {
            get { return this.brokerPipePath; }
        }

        internal bool IsAlive
        {
            get
            {
                lock (this.processSync)
                {
                    return Volatile.Read(ref this.disposed) == 0 &&
                        this.process != null && !this.process.HasExited;
                }
            }
        }

        internal AndroidRootShellCommandResult Execute(
            string command,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Root shell 命令不能为空。", "command");
            }
            if (Volatile.Read(ref this.disposed) != 0)
            {
                throw new ObjectDisposedException("AndroidRootShellSession");
            }

            int timeout = Math.Max(1000, timeoutMilliseconds);
            this.commandGate.Wait(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Process current;
                lock (this.processSync)
                {
                    current = this.process;
                    if (current == null || current.HasExited)
                    {
                        throw new InvalidOperationException(
                            "Android Root shell 已退出。" +
                            (string.IsNullOrWhiteSpace(this.lastError)
                                ? string.Empty
                                : " " + this.lastError));
                    }
                }

                string marker = EndMarkerPrefix + Guid.NewGuid().ToString("N");
                string wrapped =
                    "set +e\n" +
                    command.TrimEnd() + "\n" +
                    "wpe_status=$?\n" +
                    "printf '" + marker + ":%s\\n' \"$wpe_status\"\n";
                try
                {
                    this.lastError = string.Empty;
                    current.StandardInput.Write(wrapped);
                    current.StandardInput.Flush();
                }
                catch (Exception ex)
                {
                    this.ResetProcess();
                    throw new InvalidOperationException("写入 Android Root shell 失败。", ex);
                }

                StringBuilder output = new StringBuilder();
                int exitCode = -1;
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeout);
                while (true)
                {
                    int remainingMilliseconds = (int)Math.Max(
                        100,
                        Math.Min(
                            int.MaxValue,
                            (deadline - DateTime.UtcNow).TotalMilliseconds));
                    string line = this.ReadLineWithTimeout(
                        current,
                        cancellationToken,
                        remainingMilliseconds);
                    if (line == null)
                    {
                        this.ResetProcess();
                        throw new InvalidOperationException("Android Root shell 已关闭输出。" );
                    }

                    string normalized = line.TrimEnd('\r');
                    string markerPrefix = marker + ":";
                    int markerIndex = normalized.IndexOf(markerPrefix, StringComparison.Ordinal);
                    if (markerIndex >= 0)
                    {
                        string statusText = normalized.Substring(markerIndex + markerPrefix.Length).Trim();
                        if (!int.TryParse(statusText, out exitCode))
                        {
                            this.ResetProcess();
                            throw new InvalidOperationException("Android Root shell 返回了无效状态码。" );
                        }
                        string beforeMarker = normalized.Substring(0, markerIndex);
                        if (beforeMarker.Length > 0)
                        {
                            output.AppendLine(beforeMarker);
                        }
                        break;
                    }

                    output.AppendLine(normalized);
                    if (DateTime.UtcNow >= deadline)
                    {
                        this.ResetProcess();
                        throw new TimeoutException("Android Root shell 命令超时。" );
                    }
                }

                string error = this.lastError;
                return new AndroidRootShellCommandResult(
                    exitCode == 0,
                    output.ToString(),
                    string.IsNullOrWhiteSpace(error) ? output.ToString() : error,
                    exitCode);
            }
            catch (OperationCanceledException)
            {
                this.ResetProcess();
                throw;
            }
            finally
            {
                this.commandGate.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            this.brokerCancellation.Cancel();
            NamedPipeServerStream pipe;
            lock (this.pipeSync)
            {
                pipe = this.activePipe;
                this.activePipe = null;
            }
            if (pipe != null)
            {
                try { pipe.Dispose(); } catch { }
            }

            this.ResetProcess();
            this.commandGate.Dispose();
            this.brokerCancellation.Dispose();
        }

        private void StartProcess(CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = this.adbPath,
                Arguments = BuildArguments(
                    "-s",
                    this.deviceSerial,
                    "shell",
                    "-t",
                    "su",
                    "-c",
                    "sh"),
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
            next.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    this.lastError = args.Data.Trim();
                }
            };

            cancellationToken.ThrowIfCancellationRequested();
            if (!next.Start())
            {
                next.Dispose();
                throw new InvalidOperationException("Android Root shell 启动失败。" );
            }

            lock (this.processSync)
            {
                this.process = next;
                this.lastError = string.Empty;
            }
            next.BeginErrorReadLine();
            try
            {
                next.StandardInput.Write("stty -echo; printf '" + ReadyMarker + "\\n'\n");
                next.StandardInput.Flush();
                string ready = this.ReadLineWithTimeout(
                    next,
                    cancellationToken,
                    StartupTimeoutMilliseconds);
                if (!string.Equals(ready == null ? string.Empty : ready.Trim(), ReadyMarker, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Android Root shell 未返回就绪标记：" + (ready ?? "<empty>"));
                }
            }
            catch
            {
                this.ResetProcess();
                throw;
            }
        }

        private void StartBroker()
        {
            Task.Factory.StartNew(
                this.RunBroker,
                this.brokerCancellation.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private void RunBroker()
        {
            while (!this.brokerCancellation.IsCancellationRequested &&
                Volatile.Read(ref this.disposed) == 0)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        this.pipeName,
                        PipeDirection.InOut,
                        4,
                        PipeTransmissionMode.Byte,
                        // .NET Framework 4.8 does not expose CurrentUserOnly;
                        // the process-specific random pipe name is passed only
                        // to the child reader that owns this session.
                        PipeOptions.None);
                    lock (this.pipeSync)
                    {
                        this.activePipe = server;
                    }
                    server.WaitForConnection();
                    if (this.brokerCancellation.IsCancellationRequested)
                    {
                        break;
                    }

                    using (server)
                    using (StreamReader reader = new StreamReader(
                        server,
                        new UTF8Encoding(false),
                        false,
                        4096,
                        true))
                    using (StreamWriter writer = new StreamWriter(
                        server,
                        new UTF8Encoding(false),
                        4096,
                        true))
                    {
                        writer.AutoFlush = true;
                        while (server.IsConnected &&
                            !this.brokerCancellation.IsCancellationRequested)
                        {
                            string requestLine = reader.ReadLine();
                            if (requestLine == null)
                            {
                                break;
                            }
                            if (string.IsNullOrWhiteSpace(requestLine))
                            {
                                continue;
                            }

                            JObject response;
                            try
                            {
                                JObject request = JObject.Parse(requestLine);
                                string command = (string)request["command"];
                                int timeout = request.Value<int?>("timeoutMs") ?? 300000;
                                AndroidRootShellCommandResult result = this.Execute(
                                    command,
                                    timeout,
                                    this.brokerCancellation.Token);
                                response = new JObject
                                {
                                    ["ok"] = result.Succeeded,
                                    ["exitCode"] = result.ExitCode,
                                    ["output"] = result.Output,
                                    ["error"] = result.Error
                                };
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                            catch (Exception ex)
                            {
                                response = new JObject
                                {
                                    ["ok"] = false,
                                    ["exitCode"] = -1,
                                    ["output"] = string.Empty,
                                    ["error"] = ex.Message
                                };
                            }
                            writer.WriteLine(response.ToString(Formatting.None));
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    if (!this.brokerCancellation.IsCancellationRequested)
                    {
                        Thread.Sleep(25);
                    }
                }
                catch (IOException)
                {
                    if (!this.brokerCancellation.IsCancellationRequested)
                    {
                        Thread.Sleep(25);
                    }
                }
                catch
                {
                    if (!this.brokerCancellation.IsCancellationRequested)
                    {
                        Thread.Sleep(25);
                    }
                }
                finally
                {
                    lock (this.pipeSync)
                    {
                        if (ReferenceEquals(this.activePipe, server))
                        {
                            this.activePipe = null;
                        }
                    }
                    if (server != null)
                    {
                        try { server.Dispose(); } catch { }
                    }
                }
            }
        }

        private string ReadLineWithTimeout(
            Process current,
            CancellationToken cancellationToken,
            int timeoutMilliseconds)
        {
            if (current == null || current.HasExited)
            {
                throw new InvalidOperationException("Android Root shell 已退出。" );
            }

            Task<string> readTask = current.StandardOutput.ReadLineAsync();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMilliseconds));
            while (!readTask.Wait(20))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("Android Root shell 响应超时。" );
                }
            }
            return readTask.GetAwaiter().GetResult();
        }

        private void ResetProcess()
        {
            Process oldProcess;
            lock (this.processSync)
            {
                oldProcess = this.process;
                this.process = null;
            }
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
            catch { }
            try
            {
                if (!oldProcess.HasExited)
                {
                    oldProcess.Kill();
                }
            }
            catch { }
            try { oldProcess.WaitForExit(1000); } catch { }
            try { oldProcess.Dispose(); } catch { }
        }

        private static string BuildArguments(params string[] values)
        {
            return string.Join(" ", values.Select(QuoteArgument));
        }

        private static string QuoteArgument(string value)
        {
            string normalized = value ?? string.Empty;
            if (normalized.Length == 0)
            {
                return "\"\"";
            }
            if (!normalized.Any(character =>
                char.IsWhiteSpace(character) || character == '\"'))
            {
                return normalized;
            }
            return "\"" + normalized
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"") + "\"";
        }
    }
}
