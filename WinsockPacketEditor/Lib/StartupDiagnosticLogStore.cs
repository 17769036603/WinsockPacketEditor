using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace WinsockPacketEditor
{
    /// <summary>
    /// Writes the earliest startup and elevation hand-off diagnostics without
    /// depending on the database, WPELibrary initialization, or UI resources.
    /// </summary>
    internal static class StartupDiagnosticLogStore
    {
        internal const long MaxFileBytes = 2L * 1024L * 1024L;
        internal const int ArchiveCount = 3;

        private const string LogFileName = "startup.jsonl";
        private static readonly object FileSync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static string LogFilePath
        {
            get
            {
                string localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localAppData))
                {
                    localAppData = Path.GetTempPath();
                }

                return Path.Combine(
                    localAppData,
                    "XNAS",
                    "WPE",
                    "startup",
                    "logs",
                    LogFileName);
            }
        }

        internal static bool TryAppend(
            string eventName,
            bool? isAdministrator,
            string message,
            int? childProcessId,
            int? exitCode,
            out string error)
        {
            error = string.Empty;
            try
            {
                string line = BuildRecord(
                    eventName,
                    isAdministrator,
                    message,
                    childProcessId,
                    exitCode);
                int upcomingBytes = Utf8NoBom.GetByteCount(line + Environment.NewLine);
                string logPath = LogFilePath;
                string logDirectory = Path.GetDirectoryName(logPath);

                lock (FileSync)
                {
                    Directory.CreateDirectory(logDirectory);
                    RotateIfRequired(logPath, upcomingBytes);
                    using (FileStream stream = new FileStream(
                        logPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite))
                    using (StreamWriter writer = new StreamWriter(stream, Utf8NoBom))
                    {
                        writer.WriteLine(line);
                        writer.Flush();
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string BuildRecord(
            string eventName,
            bool? isAdministrator,
            string message,
            int? childProcessId,
            int? exitCode)
        {
            string executablePath = GetExecutablePath();
            StringBuilder builder = new StringBuilder(512);
            builder.Append('{');
            AppendProperty(builder, "schemaVersion", "1", false);
            AppendProperty(builder, "timestampUtc", Quote(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)), true);
            AppendProperty(builder, "processId", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture), true);
            AppendProperty(builder, "event", Quote(eventName), true);
            AppendProperty(
                builder,
                "isAdministrator",
                isAdministrator.HasValue ? (isAdministrator.Value ? "true" : "false") : "null",
                true);
            AppendProperty(builder, "childProcessId", NullableNumber(childProcessId), true);
            AppendProperty(builder, "exitCode", NullableNumber(exitCode), true);
            AppendProperty(builder, "assemblyVersion", Quote(GetAssemblyVersion()), true);
            AppendProperty(builder, "fileVersion", Quote(GetFileVersion(executablePath)), true);
            AppendProperty(builder, "executablePath", Quote(executablePath), true);
            AppendProperty(builder, "baseDirectory", Quote(AppDomain.CurrentDomain.BaseDirectory), true);
            AppendProperty(builder, "message", Quote(message), true);
            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendProperty(
            StringBuilder builder,
            string name,
            string jsonValue,
            bool prependComma)
        {
            if (prependComma)
            {
                builder.Append(',');
            }
            builder.Append(Quote(name));
            builder.Append(':');
            builder.Append(jsonValue);
        }

        private static string NullableNumber(int? value)
        {
            return value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "null";
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "null";
            }

            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        private static string GetExecutablePath()
        {
            try
            {
                return Assembly.GetExecutingAssembly().Location ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetAssemblyVersion()
        {
            try
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? string.Empty : version.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetFileVersion(string executablePath)
        {
            try
            {
                return string.IsNullOrWhiteSpace(executablePath)
                    ? string.Empty
                    : FileVersionInfo.GetVersionInfo(executablePath).FileVersion ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void RotateIfRequired(string logPath, int upcomingBytes)
        {
            FileInfo active = new FileInfo(logPath);
            if (!active.Exists || active.Length + upcomingBytes <= MaxFileBytes)
            {
                return;
            }

            string oldestArchive = GetArchivePath(logPath, ArchiveCount);
            if (File.Exists(oldestArchive))
            {
                File.Delete(oldestArchive);
            }

            for (int index = ArchiveCount - 1; index >= 1; index--)
            {
                string source = GetArchivePath(logPath, index);
                if (File.Exists(source))
                {
                    File.Move(source, GetArchivePath(logPath, index + 1));
                }
            }

            File.Move(logPath, GetArchivePath(logPath, 1));
        }

        private static string GetArchivePath(string logPath, int index)
        {
            string directory = Path.GetDirectoryName(logPath);
            return Path.Combine(
                directory,
                string.Format(CultureInfo.InvariantCulture, "startup.{0}.jsonl", index));
        }
    }
}
