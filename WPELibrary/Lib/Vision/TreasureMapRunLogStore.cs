using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// Persists bounded, structured treasure-map diagnostics without recording
    /// raw packet bytes or C6 payloads. Each append is flushed immediately so
    /// logs survive the injected process or assistant window closing.
    /// </summary>
    public sealed class TreasureMapRunLogStore
    {
        public const int CurrentSchemaVersion = 2;
        public const long DefaultMaxFileBytes = 5L * 1024L * 1024L;
        public const int DefaultArchiveCount = 5;

        private const string LogFileName = "treasure-map.jsonl";
        private static readonly object FileSync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string logDirectory;
        private readonly long maxFileBytes;
        private readonly int archiveCount;

        public TreasureMapRunLogStore(
            string logDirectory,
            long maxFileBytes,
            int archiveCount)
        {
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                throw new ArgumentException("Treasure-map log directory is required.", nameof(logDirectory));
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

                return Path.Combine(localAppData, "XNAS", "WPE", "treasure-map", "logs");
            }
        }

        public static TreasureMapRunLogStore CreateDefault()
        {
            return new TreasureMapRunLogStore(
                DefaultLogDirectory,
                DefaultMaxFileBytes,
                DefaultArchiveCount);
        }

        public bool TryAppend(TreasureMapLogEntry entry)
        {
            if (entry == null)
            {
                this.LastError = "Treasure-map log entry is missing.";
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

        private static JObject CreateRecord(TreasureMapLogEntry entry)
        {
            JObject record = new JObject
            {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["timestampUtc"] = entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                ["runId"] = entry.RunId == Guid.Empty
                    ? string.Empty
                    : entry.RunId.ToString("D", CultureInfo.InvariantCulture),
                ["processId"] = Process.GetCurrentProcess().Id,
                ["step"] = entry.Step,
                ["packetKind"] = entry.Packet,
                ["code"] = entry.Code,
                ["retry"] = entry.Retry,
                ["success"] = entry.Success,
                ["attemptId"] = entry.AttemptId
            };

            if (entry.Target == null)
            {
                record["target"] = JValue.CreateNull();
            }
            else
            {
                record["target"] = new JObject
                {
                    ["slot"] = entry.Target.PackageNum,
                    ["scene"] = entry.Target.Scene,
                    ["x"] = entry.Target.X,
                    ["y"] = entry.Target.Y
                };
            }

            return record;
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
                    "treasure-map.{0}.jsonl",
                    index));
        }
    }
}
