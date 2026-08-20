using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using WPELibrary;
using WPELibrary.Lib;
using WPELibrary.Lib.Vision;

namespace VisionLiveHarness
{
    internal static class Program
    {
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "VisionLiveHarness8.live.log");

        [STAThread]
        private static void Main()
        {
            try
            {
                Mark("main-start");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MultiLanguage.SetDefaultLanguage("zh-CN");

                using (Socket_RobotForm form = new Socket_RobotForm(null))
                {
                    form.Text = "Vision live test (visual only)";
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(100, 100);
                    form.ShowInTaskbar = true;
                    SelectVisionTab(form);
                    form.Show();
                    Mark("shown handle=" + form.Handle.ToInt64());
                    form.BeginInvoke(new Action(() => RunLiveVisionCheck(form)));
                    Application.Run(form);
                }
            }
            catch (Exception ex)
            {
                Mark("fatal: " + ex);
            }
        }

        private static async void RunLiveVisionCheck(Socket_RobotForm form)
        {
            try
            {
                Mark("check-start");
                InvokePrivate(form, "RefreshVisionWindows");

                ComboBox windows = GetField<ComboBox>(form, "cbbVisionWindows");
                Mark("visible-window-count=" + (windows == null ? -1 : windows.Items.Count));
                VisionWindowInfo target = FindGameWindow(windows);
                if (target == null)
                {
                    Mark("target-not-found");
                    LogWindowItems(windows);
                    return;
                }

                windows.SelectedItem = target;
                SetFullClientRegion(form, target.ClientSize);
                Mark(string.Format(
                    "target={0};pid={1};handle={2};client={3}x{4}",
                    target.DisplayName,
                    target.ProcessId,
                    target.Handle.ToInt64(),
                    target.ClientSize.Width,
                    target.ClientSize.Height));

                InvokePrivate(form, "bCaptureVision_Click", form, EventArgs.Empty);
                SavePreview(form);
                Mark("capture-invoked");
                await WaitForVisionOcrAsync(form);
                Mark("auto-ocr-status=" + ReadLabel(form, "lVisionOcrStatus"));

                TextBox keyword = GetField<TextBox>(form, "txtVisionOcrKeyword");
                if (keyword != null)
                {
                    keyword.Text = "序章";
                }
                Task verification = InvokePrivate(form, "RecognizeVisionTextAsync", false) as Task;
                if (verification != null)
                {
                    await verification;
                }
                Mark("fixed-keyword=序章;ocr-status=" + ReadLabel(form, "lVisionOcrStatus"));
                SavePreview(form);
                Mark("capture-status=" + ReadLabel(form, "lVisionStatus"));
                Mark("check-complete");
            }
            catch (Exception ex)
            {
                Mark("check-error: " + ex);
            }
        }

        private static async Task WaitForVisionOcrAsync(Socket_RobotForm form)
        {
            await Task.Delay(250);
            FieldInfo field = typeof(Socket_RobotForm).GetField(
                "visionOcrTask",
                BindingFlags.Instance | BindingFlags.NonPublic);
            for (int attempt = 0; attempt < 80; attempt++)
            {
                Task task = field == null ? null : field.GetValue(form) as Task;
                if (task == null || task.IsCompleted)
                {
                    return;
                }
                await Task.Delay(250);
            }
            Mark("auto-ocr-wait-timeout");
        }

        private static VisionWindowInfo FindGameWindow(ComboBox windows)
        {
            if (windows == null)
            {
                return null;
            }

            VisionWindowInfo best = null;
            long bestScore = long.MinValue;
            for (int index = 0; index < windows.Items.Count; index++)
            {
                VisionWindowInfo item = windows.Items[index] as VisionWindowInfo;
                if (item == null)
                {
                    continue;
                }

                string text = (item.WindowTitle + " " + item.ProcessName).ToLowerInvariant();
                if (!text.Contains("雷电") && !text.Contains("dnplayer") && !text.Contains("ldplayer"))
                {
                    continue;
                }

                long area = (long)Math.Max(0, item.ClientSize.Width) * Math.Max(0, item.ClientSize.Height);
                long score = area;
                if (item.WindowTitle.IndexOf("雷电模拟器", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 1000000000L;
                }
                Mark(string.Format(
                    "candidate={0};handle={1};client={2}x{3};score={4}",
                    item.DisplayName,
                    item.Handle.ToInt64(),
                    item.ClientSize.Width,
                    item.ClientSize.Height,
                    score));
                if (score > bestScore)
                {
                    best = item;
                    bestScore = score;
                }
            }
            return best;
        }

        private static void LogWindowItems(ComboBox windows)
        {
            if (windows == null)
            {
                return;
            }

            for (int index = 0; index < windows.Items.Count; index++)
            {
                VisionWindowInfo item = windows.Items[index] as VisionWindowInfo;
                if (item != null)
                {
                    Mark("window[" + index + "]=" + item.DisplayName);
                }
            }
        }

        private static void SetFullClientRegion(Socket_RobotForm form, Size clientSize)
        {
            SetNumeric(form, "nudVisionX", 0);
            SetNumeric(form, "nudVisionY", 0);
            SetNumeric(form, "nudVisionWidth", clientSize.Width);
            SetNumeric(form, "nudVisionHeight", clientSize.Height);
        }

        private static void SetNumeric(Socket_RobotForm form, string fieldName, int value)
        {
            NumericUpDown control = GetField<NumericUpDown>(form, fieldName);
            if (control == null)
            {
                throw new InvalidOperationException("Missing numeric control: " + fieldName);
            }

            decimal next = Math.Max(control.Minimum, Math.Min(control.Maximum, value));
            control.Value = next;
        }

        private static void SavePreview(Socket_RobotForm form)
        {
            FieldInfo field = typeof(Socket_RobotForm).GetField(
                "visionPreview",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Bitmap preview = field == null ? null : field.GetValue(form) as Bitmap;
            if (preview == null)
            {
                return;
            }

            string path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "VisionLiveHarness8.live.png");
            using (Bitmap copy = new Bitmap(preview))
            {
                VisionWindowService.SavePng(copy, path);
            }
            Mark("preview-saved=" + path + ";size=" + preview.Width + "x" + preview.Height);
        }

        private static string ReadLabel(Socket_RobotForm form, string fieldName)
        {
            Label label = GetField<Label>(form, fieldName);
            return label == null ? "<missing>" : label.Text;
        }

        private static T GetField<T>(Socket_RobotForm form, string fieldName)
            where T : class
        {
            FieldInfo field = typeof(Socket_RobotForm).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(form) as T;
        }

        private static object InvokePrivate(Socket_RobotForm form, string methodName, params object[] args)
        {
            MethodInfo method = typeof(Socket_RobotForm).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(typeof(Socket_RobotForm).FullName, methodName);
            }
            return method.Invoke(form, args);
        }

        private static void Mark(string message)
        {
            File.AppendAllText(
                LogPath,
                DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
        }

        private static void SelectVisionTab(Socket_RobotForm form)
        {
            FieldInfo field = typeof(Socket_RobotForm).GetField(
                "tcRobotInstruction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TabControl tabs = field == null ? null : field.GetValue(form) as TabControl;
            if (tabs == null)
            {
                return;
            }

            TabPage visionTab = tabs.TabPages["tpInstruction_Vision"];
            if (visionTab != null)
            {
                tabs.SelectedTab = visionTab;
            }
        }
    }
}
