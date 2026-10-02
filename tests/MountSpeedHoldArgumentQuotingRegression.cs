using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

internal static class MountSpeedHoldArgumentQuotingRegression
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: regression <WPELibrary.dll> <python.exe>");
            return 2;
        }

        Assembly library = Assembly.LoadFrom(args[0]);
        Type processType = library.GetType(
            "WPELibrary.Lib.MountSpeed.MountSpeedHoldProcess",
            true);
        MethodInfo quote = processType.GetMethod(
            "QuoteArgument",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (quote == null)
        {
            throw new InvalidOperationException("MountSpeedHoldProcess.QuoteArgument not found.");
        }

        string[] values =
        {
            @"C:\Program Files\Android\platform-tools\adb.exe",
            @"C:\custom\a ""quoted"" path\file.py",
            @"C:\path with spaces\trailing\"
        };
        foreach (string value in values)
        {
            string encoded = (string)quote.Invoke(null, new object[] { value });
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = args[1],
                Arguments = "-c \"import sys; print(sys.argv[1])\" " + encoded,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (Process process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !string.Equals(output, value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Windows argv round-trip failed: " + value + " => " + output + " " + error);
                }
            }
        }

        Console.WriteLine("MountSpeedHoldArgumentQuotingRegression: PASS");
        return 0;
    }
}
