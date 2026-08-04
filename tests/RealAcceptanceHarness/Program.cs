using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using EasyHook;

namespace RealAcceptanceHarness
{
    internal static class Program
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private static int Main(string[] args)
        {
            if (args == null || (args.Length < 2 || args.Length > 4) || !int.TryParse(args[0], out int processId))
            {
                Console.Error.WriteLine("Usage: RealAcceptanceHarness.exe <pid> <injection-library.dll> [result-file] [channel-name]");
                return 64;
            }

            string libraryPath = Path.GetFullPath(args[1]);
            string resultPath = args.Length == 3 ? Path.GetFullPath(args[2]) : string.Empty;
            if (args.Length == 4)
            {
                resultPath = Path.GetFullPath(args[2]);
            }
            string channelName = args.Length == 4 ? args[3] : "WPE64";
            try
            {
                using (Process target = Process.GetProcessById(processId))
                {
                    WriteResult(resultPath, string.Format("target={0};name={1};library={2}", target.Id, target.ProcessName, libraryPath));
                    if (!string.Equals(target.ProcessName, "Ld9BoxHeadless", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteResult(resultPath, "target-name-mismatch");
                        return 65;
                    }
                }

                if (!File.Exists(libraryPath))
                {
                    WriteResult(resultPath, "library-missing");
                    return 66;
                }

                string runtimeDirectory = Path.GetDirectoryName(libraryPath);
                Config.HelperLibraryLocation = runtimeDirectory;
                Config.DependencyPath = runtimeDirectory;
                SetDllDirectory(runtimeDirectory);
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                Environment.SetEnvironmentVariable("PATH", runtimeDirectory + Path.PathSeparator + currentPath);
                WriteResult(resultPath, "easyhook-runtime=" + runtimeDirectory);

                RemoteHooking.Inject(processId, libraryPath, libraryPath, channelName);
                WriteResult(resultPath, "inject-returned=true");
                return 0;
            }
            catch (Exception ex)
            {
                WriteResult(resultPath, "inject-returned=false");
                for (Exception current = ex; current != null; current = current.InnerException)
                {
                    WriteResult(resultPath, "error-type=" + current.GetType().FullName);
                    WriteResult(resultPath, "error=" + current.Message);
                }

                return 2;
            }
        }

        private static void WriteResult(string resultPath, string line)
        {
            Console.WriteLine(line);
            if (!string.IsNullOrEmpty(resultPath))
            {
                File.AppendAllText(resultPath, line + Environment.NewLine);
            }
        }
    }
}
