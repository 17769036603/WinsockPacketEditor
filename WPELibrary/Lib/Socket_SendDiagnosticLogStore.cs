using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib
{
    /// <summary>
    /// Persists bounded diagnostics for the protected ordinary send preset.
    /// It records transport metadata and native results, never the packet body.
    /// </summary>
    public sealed class SocketSendDiagnosticLogStore
    {
        public const int CurrentSchemaVersion = 1;
        public const long DefaultMaxFileBytes = 5L * 1024L * 1024L;
        public const int DefaultArchiveCount = 5;

        private const string LogFileName = "socket-send.jsonl";
        private static readonly object FileSync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string logDirectory;
        private readonly long maxFileBytes;
        private readonly int archiveCount;

        public SocketSendDiagnosticLogStore(
            string logDirectory,
            long maxFileBytes,
            int archiveCount)
        {
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                throw new ArgumentException("Socket-send log directory is required.", nameof(logDirectory));
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

                return Path.Combine(localAppData, "XNAS", "WPE", "socket-send", "logs");
            }
        }

        public static SocketSendDiagnosticLogStore CreateDefault()
        {
            return new SocketSendDiagnosticLogStore(
                DefaultLogDirectory,
                DefaultMaxFileBytes,
                DefaultArchiveCount);
        }

        public bool TryAppend(SocketSendDiagnosticEntry entry)
        {
            if (entry == null)
            {
                this.LastError = "Socket-send diagnostic entry is missing.";
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
            catch (Exception ex)
            {
                this.LastError = ex.Message;
                return false;
            }
        }

        private static JObject CreateRecord(SocketSendDiagnosticEntry entry)
        {
            DateTime timestampUtc = entry.TimestampUtc == default(DateTime)
                ? DateTime.UtcNow
                : entry.TimestampUtc.ToUniversalTime();

            return new JObject
            {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["timestampUtc"] = timestampUtc.ToString("O", CultureInfo.InvariantCulture),
                ["processId"] = Process.GetCurrentProcess().Id,
                ["event"] = entry.EventName ?? string.Empty,
                ["preset"] = entry.PresetName ?? string.Empty,
                ["loopIndex"] = entry.LoopIndex,
                ["packetType"] = entry.PacketType ?? string.Empty,
                ["socket"] = entry.Socket,
                ["packetFrom"] = entry.PacketFrom ?? string.Empty,
                ["packetTo"] = entry.PacketTo ?? string.Empty,
                ["packetLength"] = entry.PacketLength,
                ["bytesSent"] = entry.BytesSent,
                ["wsaError"] = entry.WsaError,
                ["success"] = entry.Success,
                ["reason"] = entry.Reason ?? string.Empty,
                ["totalSend"] = entry.TotalSend,
                ["sendSuccess"] = entry.SendSuccess,
                ["sendFailure"] = entry.SendFailure
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
                string.Format(
                    CultureInfo.InvariantCulture,
                    "socket-send.{0}.jsonl",
                    index));
        }
    }

    public sealed class SocketSendDiagnosticEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string EventName { get; set; }
        public string PresetName { get; set; }
        public int LoopIndex { get; set; }
        public string PacketType { get; set; }
        public int Socket { get; set; }
        public string PacketFrom { get; set; }
        public string PacketTo { get; set; }
        public int PacketLength { get; set; }
        public int BytesSent { get; set; }
        public int WsaError { get; set; }
        public bool Success { get; set; }
        public string Reason { get; set; }
        public int TotalSend { get; set; }
        public int SendSuccess { get; set; }
        public int SendFailure { get; set; }
    }
}
