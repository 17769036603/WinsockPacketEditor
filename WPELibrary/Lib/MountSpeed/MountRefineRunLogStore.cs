using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// Persists bounded mount-refine diagnostics without packet payloads.
    /// Records are flushed immediately so CLI inspection remains useful after
    /// the assistant window or injected process exits.
    /// </summary>
    public sealed class MountRefineRunLogStore
    {
        public const int CurrentSchemaVersion = 1;
        public const long DefaultMaxFileBytes = 5L * 1024L * 1024L;
        public const int DefaultArchiveCount = 5;

        private const string LogFileName = "mount-refine.jsonl";
        private static readonly object FileSync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string logDirectory;
        private readonly long maxFileBytes;
        private readonly int archiveCount;

        public MountRefineRunLogStore(
            string logDirectory,
            long maxFileBytes,
            int archiveCount)
        {
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                throw new ArgumentException("Mount-refine log directory is required.", nameof(logDirectory));
            }
            if (maxFileBytes < 256)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            }
            if (archiveCount < 1 || archiveCount > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(archiveCount));
            }

            this.logDirectory = Path.GetFullPath(logDirectory);
            this.maxFileBytes = maxFileBytes;
            this.archiveCount = archiveCount;
        }

        public string LogFilePath
        {
            get { return Path.Combine(this.logDirectory, LogFileName); }
        }

        public string LastError { get; private set; }

        public static string DefaultLogDirectory
        {
            get
            {
                string localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localAppData))
                {
                    localAppData = Path.GetTempPath();
                }
                return Path.Combine(localAppData, "XNAS", "WPE", "mount-refine", "logs");
            }
        }

        public static MountRefineRunLogStore CreateDefault()
        {
            return new MountRefineRunLogStore(
                DefaultLogDirectory,
                DefaultMaxFileBytes,
                DefaultArchiveCount);
        }

        public bool TryAppend(MountRefineLogEntry entry)
        {
            if (entry == null)
            {
                this.LastError = "Mount-refine log entry is missing.";
                return false;
            }

            try
            {
                string line = CreateRecord(entry).ToString(Formatting.None);
                int upcomingBytes = Utf8NoBom.GetByteCount(line + Environment.NewLine);
                lock (FileSync)
                {
                    Directory.CreateDirectory(this.logDirectory);
                    this.RotateIfRequired(upcomingBytes);
                    using (FileStream stream = new FileStream(
                        this.LogFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite))
                    using (StreamWriter writer = new StreamWriter(stream, Utf8NoBom))
                    {
                        writer.WriteLine(line);
                        writer.Flush();
                    }
                }

                this.LastError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                this.LastError = exception.Message;
                return false;
            }
        }

        private static JObject CreateRecord(MountRefineLogEntry entry)
        {
            DateTime timestampUtc = entry.TimestampUtc == default(DateTime)
                ? DateTime.UtcNow
                : entry.TimestampUtc.ToUniversalTime();
            return new JObject
            {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["timestampUtc"] = timestampUtc.ToString("O", CultureInfo.InvariantCulture),
                ["processId"] = Process.GetCurrentProcess().Id,
                ["runId"] = entry.RunId == Guid.Empty
                    ? string.Empty
                    : entry.RunId.ToString("D", CultureInfo.InvariantCulture),
                ["event"] = entry.EventName ?? string.Empty,
                ["state"] = entry.State ?? string.Empty,
                ["stopReason"] = entry.StopReason ?? string.Empty,
                ["code"] = entry.Code ?? string.Empty,
                ["message"] = entry.Message ?? string.Empty,
                ["attempt"] = entry.AttemptCount,
                ["success"] = entry.Success.HasValue
                    ? new JValue(entry.Success.Value)
                    : JValue.CreateNull(),
                ["sequence"] = entry.Sequence,
                ["mountId"] = entry.MountId.HasValue
                    ? new JValue(entry.MountId.Value)
                    : JValue.CreateNull(),
                ["activeRideInstanceId"] = entry.ActiveRideInstanceId ?? string.Empty,
                ["rideBindingStatus"] = entry.RideBindingStatus ?? string.Empty,
                ["refineCardCount"] = entry.RefineCardCount,
                ["matchedCardIndex"] = entry.MatchedCardIndex.HasValue
                    ? new JValue(entry.MatchedCardIndex.Value)
                    : JValue.CreateNull(),
                ["protocolVerified"] = entry.ProtocolVerified,
                ["authorized"] = entry.Authorized,
                ["socket"] = entry.Socket,
                ["bytesSent"] = entry.BytesSent,
                ["socketError"] = entry.SocketError,
                ["readPath"] = entry.ReadPath ?? string.Empty,
                ["rootAdbMs"] = entry.RootAdbMilliseconds.HasValue
                    ? new JValue(entry.RootAdbMilliseconds.Value)
                    : JValue.CreateNull(),
                ["probeMs"] = entry.ProbeMilliseconds.HasValue
                    ? new JValue(entry.ProbeMilliseconds.Value)
                    : JValue.CreateNull(),
                ["cardParseMs"] = entry.CardParseMilliseconds.HasValue
                    ? new JValue(entry.CardParseMilliseconds.Value)
                    : JValue.CreateNull(),
                ["protocolParseMs"] = entry.ProtocolParseMilliseconds.HasValue
                    ? new JValue(entry.ProtocolParseMilliseconds.Value)
                    : JValue.CreateNull(),
                ["readTotalMs"] = entry.ReadTotalMilliseconds.HasValue
                    ? new JValue(entry.ReadTotalMilliseconds.Value)
                    : JValue.CreateNull(),
                ["sendToVerifyMs"] = entry.SendToVerifyMilliseconds.HasValue
                    ? new JValue(entry.SendToVerifyMilliseconds.Value)
                    : JValue.CreateNull()
            };
        }

        private void RotateIfRequired(int upcomingBytes)
        {
            FileInfo active = new FileInfo(this.LogFilePath);
            if (!active.Exists || active.Length + upcomingBytes <= this.maxFileBytes)
            {
                return;
            }

            string oldestArchive = this.GetArchivePath(this.archiveCount);
            if (File.Exists(oldestArchive))
            {
                File.Delete(oldestArchive);
            }
            for (int index = this.archiveCount - 1; index >= 1; index--)
            {
                string source = this.GetArchivePath(index);
                if (File.Exists(source))
                {
                    File.Move(source, this.GetArchivePath(index + 1));
                }
            }
            File.Move(this.LogFilePath, this.GetArchivePath(1));
        }

        private string GetArchivePath(int index)
        {
            return Path.Combine(
                this.logDirectory,
                string.Format(CultureInfo.InvariantCulture, "mount-refine.{0}.jsonl", index));
        }
    }

    public sealed class MountRefineLogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public Guid RunId { get; set; }
        public string EventName { get; set; }
        public string State { get; set; }
        public string StopReason { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public int AttemptCount { get; set; }
        public bool? Success { get; set; }
        public long Sequence { get; set; }
        public long? MountId { get; set; }
        public string ActiveRideInstanceId { get; set; }
        public string RideBindingStatus { get; set; }
        public int RefineCardCount { get; set; }
        public int? MatchedCardIndex { get; set; }
        public bool ProtocolVerified { get; set; }
        public bool Authorized { get; set; }
        public int Socket { get; set; }
        public int BytesSent { get; set; }
        public int SocketError { get; set; }
        public string ReadPath { get; set; }
        public double? RootAdbMilliseconds { get; set; }
        public double? ProbeMilliseconds { get; set; }
        public double? CardParseMilliseconds { get; set; }
        public double? ProtocolParseMilliseconds { get; set; }
        public double? ReadTotalMilliseconds { get; set; }
        public double? SendToVerifyMilliseconds { get; set; }
    }
}
