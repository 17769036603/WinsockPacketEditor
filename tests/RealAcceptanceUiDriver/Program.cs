using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace RealAcceptanceUiDriver
{
    internal static class Program
    {
        private const uint BmClick = 0x00F5;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private static int Main(string[] args)
        {
            string resultPath = args != null && args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.Combine(Path.GetTempPath(), "wpe-ui-driver.txt");
            int desktopPid = args != null && args.Length > 1 && int.TryParse(args[1], out int parsedDesktopPid)
                ? parsedDesktopPid
                : 23388;
            int targetPid = args != null && args.Length > 2 && int.TryParse(args[2], out int parsedTargetPid)
                ? parsedTargetPid
                : 20644;

            try
            {
                Process desktop = Process.GetProcessById(desktopPid);
                Write(resultPath, "desktop=" + desktop.ProcessName + ";handle=" + desktop.MainWindowHandle);

                AutomationElement root = AutomationElement.FromHandle(desktop.MainWindowHandle);
                if (args != null && args.Length > 3 && string.Equals(args[3], "verify", StringComparison.OrdinalIgnoreCase))
                {
                    Write(resultPath, "verify-only=true");
                    return VerifyTarget(resultPath, targetPid, desktop.MainWindowHandle);
                }

                if (args != null && args.Length > 3 && string.Equals(args[3], "close", StringComparison.OrdinalIgnoreCase))
                {
                    AutomationElement processListToClose = WaitForWindow(desktop.Id, "进程列表", 1000);
                    if (processListToClose != null)
                    {
                        CloseWindow(processListToClose);
                        Thread.Sleep(500);
                    }

                    root = AutomationElement.FromHandle(desktop.MainWindowHandle);
                    CloseWindow(root);
                    Write(resultPath, "close-requested=true");
                    return 0;
                }

                AutomationElement processList = WaitForWindow(desktop.Id, "进程列表", 10000);
                if (processList == null)
                {
                    AutomationElement choose = FindButton(root, "选择进程");
                    if (choose == null)
                    {
                        Write(resultPath, "choose-button=false");
                        return 10;
                    }

                    Write(resultPath, "choose-button=true");
                    Invoke(choose);
                    processList = WaitForWindow(desktop.Id, "进程列表", 10000);
                    if (processList == null)
                    {
                        root = AutomationElement.FromHandle(desktop.MainWindowHandle);
                        AutomationElement autoInject = FindButton(root, "注入进程");
                        if (autoInject == null)
                        {
                            Write(resultPath, "process-list-window=false;auto-selection=false");
                            return 11;
                        }

                        Write(resultPath, "process-list-window=false;auto-selection=true");
                        Invoke(autoInject);
                        Write(resultPath, "inject-clicked=true");
                        return VerifyTarget(resultPath, targetPid, desktop.MainWindowHandle);
                    }
                }

                Write(resultPath, "process-list-window=true");
                AutomationElement emulator = WaitForNamed(processList, "Ld9BoxHeadless", 15000);
                if (emulator == null)
                {
                    AutomationElement refresh = FindButton(processList, "刷新");
                    if (refresh != null)
                    {
                        Write(resultPath, "emulator-row=false;refreshing=true");
                        Invoke(refresh);
                        emulator = WaitForNamed(processList, "Ld9BoxHeadless", 15000);
                    }

                    if (emulator == null)
                    {
                        Write(resultPath, "emulator-row=false");
                        DumpControls(resultPath, processList);
                        return 12;
                    }
                }

                Write(resultPath, "emulator-row=true");
                SelectionItemPattern selection = emulator.GetCurrentPattern(SelectionItemPattern.Pattern) as SelectionItemPattern;
                if (selection != null)
                {
                    selection.Select();
                    Write(resultPath, "emulator-row-selected=true");
                }
                else
                {
                    Write(resultPath, "emulator-row-selected=false;pattern-missing");
                    return 13;
                }

                AutomationElement ok = FindButton(processList, "确定");
                if (ok == null)
                {
                    Write(resultPath, "confirm-button=false");
                    return 14;
                }

                Invoke(ok);
                Thread.Sleep(500);
                root = AutomationElement.FromHandle(desktop.MainWindowHandle);
                AutomationElement inject = FindButton(root, "注入进程");
                if (inject == null)
                {
                    Write(resultPath, "inject-button=false");
                    return 15;
                }

                Write(resultPath, "inject-button=true");
                Invoke(inject);
                Write(resultPath, "inject-clicked=true");
                return VerifyTarget(resultPath, targetPid, desktop.MainWindowHandle);
            }
            catch (Exception ex)
            {
                Write(resultPath, "driver-error=" + Flatten(ex));
                return 2;
            }
        }

        private static AutomationElement FindButton(AutomationElement root, string name)
        {
            return root.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, name)));
        }

        private static AutomationElement WaitForNamed(AutomationElement root, string name, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                AutomationElement result = root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, name));
                if (result != null)
                {
                    return result;
                }

                Thread.Sleep(250);
            }

            return null;
        }

        private static void Invoke(AutomationElement element)
        {
            InvokePattern pattern = element.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
            if (pattern == null)
            {
                throw new InvalidOperationException("InvokePattern missing for " + element.Current.Name);
            }

            pattern.Invoke();
        }

        private static void CloseWindow(AutomationElement element)
        {
            WindowPattern pattern = element.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
            if (pattern == null)
            {
                throw new InvalidOperationException("WindowPattern missing for " + element.Current.Name);
            }

            pattern.Close();
        }

        private static int VerifyTarget(string resultPath, int targetPid, IntPtr desktopHandle)
        {
            Thread.Sleep(5000);
            Write(resultPath, "driver-log=" + ReadMainLog(desktopHandle));
            Write(resultPath, "driver-values=" + ReadUiValues(desktopHandle));
            using (Process target = Process.GetProcessById(targetPid))
            {
                bool targetResponding = !target.HasExited;
                bool libraryLoaded = false;
                string moduleInspectionError = string.Empty;
                try
                {
                    libraryLoaded = target.Modules.Cast<ProcessModule>().Any(
                        module => string.Equals(module.ModuleName, "WPELibrary.dll", StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    moduleInspectionError = Flatten(ex);
                }

                Write(resultPath, "target-responding=" + targetResponding);
                Write(resultPath, "wpe-library-loaded=" + libraryLoaded);
                if (!string.IsNullOrEmpty(moduleInspectionError))
                {
                    Write(resultPath, "module-inspection-error=" + moduleInspectionError);
                }

                if (!targetResponding || !libraryLoaded || !string.IsNullOrEmpty(moduleInspectionError))
                {
                    Write(resultPath, "real-acceptance=false;target-injection-not-proven=true");
                    return 20;
                }
            }

            Write(resultPath, "real-acceptance=true;target-injection-proven=true");
            return 0;
        }

        private static string ReadMainLog(IntPtr mainWindow)
        {
            StringBuilder result = new StringBuilder();
            EnumChildWindows(mainWindow, (hWnd, lParam) =>
            {
                StringBuilder className = new StringBuilder(128);
                GetClassName(hWnd, className, className.Capacity);
                if (className.ToString().IndexOf("RICHEDIT", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }

                StringBuilder text = new StringBuilder(8192);
                GetWindowText(hWnd, text, text.Capacity);
                result.Append(text.ToString().Replace(Environment.NewLine, " | "));
                return false;
            }, IntPtr.Zero);
            return result.ToString();
        }

        private static string ReadUiValues(IntPtr mainWindow)
        {
            StringBuilder result = new StringBuilder();
            AutomationElement root = AutomationElement.FromHandle(mainWindow);
            AutomationElementCollection descendants = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement element in descendants)
            {
                ControlType type = element.Current.ControlType;
                if (type != ControlType.Edit && type != ControlType.Document)
                {
                    continue;
                }

                string value = string.Empty;
                try
                {
                    ValuePattern pattern = element.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                    value = pattern == null ? element.Current.Name : pattern.Current.Value;
                }
                catch
                {
                    value = element.Current.Name;
                }

                if (!string.IsNullOrEmpty(value))
                {
                    if (result.Length > 0)
                    {
                        result.Append(" || ");
                    }

                    result.Append(value.Replace(Environment.NewLine, " | "));
                }
            }

            return result.ToString();
        }

        private static AutomationElement WaitForWindow(int processId, string title, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                IntPtr handle = FindWindow(processId, title);
                if (handle != IntPtr.Zero)
                {
                    return AutomationElement.FromHandle(handle);
                }

                Thread.Sleep(100);
            }

            return null;
        }

        private static IntPtr FindWindow(int processId, string title)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint owner);
                if (owner == processId && IsWindowVisible(hWnd))
                {
                    StringBuilder text = new StringBuilder(256);
                    GetWindowText(hWnd, text, text.Capacity);
                    if (string.Equals(text.ToString(), title, StringComparison.Ordinal))
                    {
                        result = hWnd;
                        return false;
                    }
                }

                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static void DumpControls(string resultPath, AutomationElement root)
        {
            AutomationElementCollection descendants = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement element in descendants)
            {
                string name = element.Current.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    Write(resultPath, "ui-control=" + element.Current.ControlType.ProgrammaticName + ";name=" + name + ";id=" + element.Current.AutomationId);
                }
            }
        }

        private static void Write(string path, string line)
        {
            Console.WriteLine(line);
            File.AppendAllText(path, line + Environment.NewLine);
        }

        private static string Flatten(Exception exception)
        {
            StringBuilder result = new StringBuilder();
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (result.Length > 0)
                {
                    result.Append(" | ");
                }

                result.Append(current.Message.Replace(Environment.NewLine, " "));
            }

            return result.ToString();
        }
    }
}
