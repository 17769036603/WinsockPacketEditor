using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.Memory
{
    /// <summary>
    /// Persists bounded, structured memory-trace diagnostics as JSON Lines.
    /// Each append is flushed immediately so logs survive the injected process
    /// or assistant window closing. Records contain no sensitive packet bytes;
    /// only bounded hex previews of memory reads are stored.
    /// </summary>
    public sealed class MemoryTraceLogStore
    {
        public const long DefaultMaxFileBytes = 5L * 1024L * 1024L;
        public const int DefaultArchiveCount = 5;

        private const string LogFileName = "memory-trace.jsonl";
        private static readonly object FileSync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string logDirectory;
        private readonly long maxFileBytes;
        private readonly int archiveCount;

        public MemoryTraceLogStore(
            string logDirectory,
            long maxFileBytes,
            int archiveCount)
        {
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                throw new ArgumentException("Memory-trace log directory is required.", nameof(logDirectory));
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

                return Path.Combine(localAppData, "XNAS", "WPE", "memory-trace", "logs");
            }
        }

        public static MemoryTraceLogStore CreateDefault()
        {
            return new MemoryTraceLogStore(
                DefaultLogDirectory,
                DefaultMaxFileBytes,
                DefaultArchiveCount);
        }

        public bool TryAppend(JObject record)
        {
            if (record == null)
            {
                this.LastError = "Memory-trace record is missing.";
                return false;
            }

            try
            {
                string line = record.ToString(Formatting.None) + Environment.NewLine;
                int upcomingBytes = Utf8NoBom.GetByteCount(line);

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
                        writer.Write(line);
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
                    "memory-trace.{0}.jsonl",
                    index));
        }
    }
}
