using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using EasyHook;

namespace RealAcceptanceDesktopHarness
{
    internal static class Program
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        private static int Main(string[] args)
        {
            if (args == null || args.Length < 3 || args.Length > 4 || !int.TryParse(args[0], out int processId))
            {
                Console.Error.WriteLine("Usage: RealAcceptanceDesktopHarness.exe <pid> <injection-library.dll> <result-file> [channel-name]");
                return 64;
            }

            string libraryPath = Path.GetFullPath(args[1]);
            string resultPath = Path.GetFullPath(args[2]);
            string channelName = args.Length == 4 ? args[3] : "WPE64-Control";

            try
            {
                using (Process target = Process.GetProcessById(processId))
                {
                    Write(resultPath, string.Format("target={0};name={1};library={2}", target.Id, target.ProcessName, libraryPath));
                }

                if (!File.Exists(libraryPath))
                {
                    Write(resultPath, "library-missing");
                    return 66;
                }

                string runtimeDirectory = Path.GetDirectoryName(libraryPath);
                Config.HelperLibraryLocation = runtimeDirectory;
                Config.DependencyPath = runtimeDirectory;
                SetDllDirectory(runtimeDirectory);
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                Environment.SetEnvironmentVariable("PATH", runtimeDirectory + Path.PathSeparator + currentPath);
                Write(resultPath, "easyhook-runtime=" + runtimeDirectory);

                RemoteHooking.Inject(processId, libraryPath, libraryPath, channelName);
                Write(resultPath, "inject-returned=true");
                return 0;
            }
            catch (Exception ex)
            {
                Write(resultPath, "inject-returned=false");
                for (Exception current = ex; current != null; current = current.InnerException)
                {
                    Write(resultPath, "error-type=" + current.GetType().FullName);
                    Write(resultPath, "error=" + current.Message);
                }

                return 2;
            }
        }

        private static void Write(string path, string line)
        {
            Console.WriteLine(line);
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
