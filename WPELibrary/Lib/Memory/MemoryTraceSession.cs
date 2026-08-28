using System;
using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.Memory
{
    /// <summary>
    /// Bounded, in-proc memory-trace session. Logging starts automatically the
    /// first time a <see cref="ReadOnlyProcessMemoryReader"/> is opened against a
    /// game process (the "connected" point) and ends when <see cref="Stop"/> is
    /// called. When disabled the fast path is only a single boolean read, so it
    /// adds no meaningful cost to the memory read hot path.
    /// </summary>
    public static class MemoryTraceSession
    {
        private static readonly object Sync = new object();

        private static MemoryTraceLogStore store;
        private static bool sessionEnabled;
        private static Guid runId;
        private static int targetProcessId;
        private static string targetProcessName = string.Empty;
        private static string targetProcessPath = string.Empty;
        private static int pointerSize;

        public static bool IsEnabled
        {
            get { return sessionEnabled; }
        }

        public static Guid RunId
        {
            get { return runId; }
        }

        public static int TargetProcessId
        {
            get { return targetProcessId; }
        }

        public static string TargetProcessName
        {
            get { return targetProcessName; }
        }

        public static string LogFilePath
        {
            get
            {
                MemoryTraceLogStore current;
                lock (Sync)
                {
                    current = store;
                }

                return current == null ? string.Empty : current.LogFilePath;
            }
        }

        public static void Configure(MemoryTraceLogStore traceStore)
        {
            lock (Sync)
            {
                store = traceStore;
            }
        }

        /// <summary>
        /// Begins a memory-trace session for the given target identity. A session
        /// is started only once; subsequent calls while active are ignored.
        /// </summary>
        public static void Start(ReadOnlyProcessIdentity identity, string reason)
        {
            if (identity == null)
            {
                return;
            }

            bool shouldStart;
            lock (Sync)
            {
                if (sessionEnabled)
                {
                    return;
                }

                if (store == null)
                {
                    store = MemoryTraceLogStore.CreateDefault();
                }

                sessionEnabled = true;
                runId = Guid.NewGuid();
                targetProcessId = identity.ProcessId;
                targetProcessName = identity.ProcessName ?? string.Empty;
                targetProcessPath = identity.ProcessPath ?? string.Empty;
                pointerSize = identity.PointerSize;
                shouldStart = true;
            }

            if (shouldStart)
            {
                Trace("session", "begin", 0, 0, null, null, true, reason ?? "connected", 0);
            }
        }

        /// <summary>
        /// Ends the current memory-trace session. The last run identifier remains
        /// readable so the active log lines stay attributable.
        /// </summary>
        public static void Stop(string reason)
        {
            lock (Sync)
            {
                if (!sessionEnabled)
                {
                    return;
                }
            }

            // Write the end marker while the session is still enabled so the
            // record is actually persisted, then disable future tracing.
            Trace("session", "end", 0, 0, null, null, true, reason ?? "disconnected", 0);
            lock (Sync)
            {
                sessionEnabled = false;
            }
        }

        /// <summary>
        /// Records a memory-trace line when a session is active. Safe to call
        /// from any thread; failures are swallowed so tracing never breaks the
        /// read path.
        /// </summary>
        public static string Trace(
            string area,
            string op,
            long address,
            int length,
            string hex,
            string value,
            bool? success,
            string error,
            double elapsedMs)
        {
            if (!sessionEnabled)
            {
                return string.Empty;
            }

            MemoryTraceLogStore current;
            lock (Sync)
            {
                current = store;
            }

            if (current == null)
            {
                return string.Empty;
            }

            try
            {
                JObject record = new JObject
                {
                    ["schemaVersion"] = 1,
                    ["timestampUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ["runId"] = runId == Guid.Empty ? string.Empty : runId.ToString("D", CultureInfo.InvariantCulture),
                    ["toolProcessId"] = Process.GetCurrentProcess().Id,
                    ["targetProcessId"] = targetProcessId,
                    ["targetProcessName"] = targetProcessName,
                    ["targetProcessPath"] = string.IsNullOrWhiteSpace(targetProcessPath)
                        ? JValue.CreateNull()
                        : new JValue(targetProcessPath),
                    ["pointerSize"] = pointerSize,
                    ["area"] = area,
                    ["op"] = op,
                    ["address"] = address <= 0 ? JValue.CreateNull() : new JValue(address),
                    ["length"] = length,
                    ["hex"] = string.IsNullOrWhiteSpace(hex) ? JValue.CreateNull() : new JValue(hex),
                    ["value"] = string.IsNullOrWhiteSpace(value) ? JValue.CreateNull() : new JValue(value),
                    ["success"] = success.HasValue ? new JValue(success.Value) : JValue.CreateNull(),
                    ["error"] = string.IsNullOrWhiteSpace(error) ? JValue.CreateNull() : new JValue(error),
                    ["elapsedMs"] = Math.Round(elapsedMs, 3, MidpointRounding.AwayFromZero)
                };

                current.TryAppend(record);
            }
            catch (Exception)
            {
                // Tracing must never break memory reads.
            }

            return string.Empty;
        }
    }
}
