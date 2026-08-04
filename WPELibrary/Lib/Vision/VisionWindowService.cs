using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public static class VisionWindowService
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);

        private const uint PrintWindowClientOnly = 1;
        private const int ShowWindowRestore = 9;

        public static bool TryActivateWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !IsWindowVisible(hWnd))
            {
                return false;
            }
            if (IsIconic(hWnd))
            {
                ShowWindowAsync(hWnd, ShowWindowRestore);
            }
            if (GetForegroundWindow() == hWnd)
            {
                return true;
            }

            uint currentThreadId = GetCurrentThreadId();
            IntPtr foregroundWindow = GetForegroundWindow();
            uint ignoredProcessId;
            uint foregroundThreadId = foregroundWindow == IntPtr.Zero
                ? 0
                : GetWindowThreadProcessId(foregroundWindow, out ignoredProcessId);
            bool attachedToForegroundThread = false;
            try
            {
                // The assistant runs on a worker thread. Temporarily joining the
                // current foreground input queue allows Windows to honor this
                // activation request without weakening the final verification.
                if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
                {
                    attachedToForegroundThread = AttachThreadInput(
                        currentThreadId,
                        foregroundThreadId,
                        true);
                }

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (IsIconic(hWnd))
                    {
                        ShowWindowAsync(hWnd, ShowWindowRestore);
                    }
                    BringWindowToTop(hWnd);
                    SetActiveWindow(hWnd);
                    SetForegroundWindow(hWnd);
                    if (GetForegroundWindow() == hWnd)
                    {
                        return true;
                    }
                    Thread.Sleep(25);
                }

                return GetForegroundWindow() == hWnd;
            }
            finally
            {
                if (attachedToForegroundThread)
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }
        }

        public static IList<VisionWindowInfo> EnumerateVisibleWindows(int excludedProcessId)
        {
            List<VisionWindowInfo> windows = new List<VisionWindowInfo>();

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                {
                    return true;
                }

                uint processId;
                GetWindowThreadProcessId(hWnd, out processId);
                if (processId == 0 || processId == excludedProcessId)
                {
                    return true;
                }

                string title = GetWindowTitle(hWnd);
                if (string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                Rectangle clientBounds;
                Size clientSize;
                if (!TryGetClientBounds(hWnd, out clientBounds, out clientSize))
                {
                    return true;
                }

                string processName = string.Empty;
                string processPath = string.Empty;
                long processStartTimeUtcTicks = 0L;
                try
                {
                    using (Process process = Process.GetProcessById((int)processId))
                    {
                        processName = process.ProcessName;
                        try
                        {
                            processPath = process.MainModule == null ? string.Empty : process.MainModule.FileName;
                        }
                        catch (Exception)
                        {
                        }
                        try
                        {
                            processStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                catch (Exception)
                {
                    // A process may exit while the window list is being enumerated.
                }

                windows.Add(new VisionWindowInfo
                {
                    Handle = hWnd,
                    ProcessId = (int)processId,
                    ProcessName = processName,
                    ProcessPath = processPath,
                    ProcessStartTimeUtcTicks = processStartTimeUtcTicks,
                    WindowTitle = title,
                    ClientBoundsScreen = clientBounds
                });
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        public static bool TryFindInjectedTargetWindow(
            IList<VisionWindowInfo> windows,
            int injectedProcessId,
            string injectedProcessName,
            out VisionWindowInfo targetWindow)
        {
            targetWindow = null;
            if (windows == null || windows.Count == 0 || injectedProcessId <= 0)
            {
                return false;
            }

            VisionWindowInfo sameProcessWindow = null;
            for (int i = 0; i < windows.Count; i++)
            {
                VisionWindowInfo window = windows[i];
                if (window.ProcessId != injectedProcessId)
                {
                    continue;
                }

                if (sameProcessWindow != null)
                {
                    return false;
                }

                sameProcessWindow = window;
            }

            if (sameProcessWindow != null)
            {
                targetWindow = sameProcessWindow;
                return true;
            }

            if (!IsLdPlayerEngineProcess(injectedProcessName))
            {
                return false;
            }

            VisionWindowInfo ldPlayerWindow = null;
            int ldPlayerWindowCount = 0;
            for (int i = 0; i < windows.Count; i++)
            {
                VisionWindowInfo window = windows[i];
                if (!IsLdPlayerFrontendProcess(window.ProcessName))
                {
                    continue;
                }

                ldPlayerWindow = window;
                ldPlayerWindowCount++;
            }

            if (ldPlayerWindowCount != 1)
            {
                return false;
            }

            targetWindow = ldPlayerWindow;
            return true;
        }

        public static bool TryGetClientBounds(
            IntPtr hWnd,
            out Rectangle clientBoundsScreen,
            out Size clientSize)
        {
            clientBoundsScreen = Rectangle.Empty;
            clientSize = Size.Empty;

            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            {
                return false;
            }

            NativeRect clientRect;
            if (!GetClientRect(hWnd, out clientRect))
            {
                return false;
            }

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            NativePoint origin = new NativePoint
            {
                X = clientRect.Left,
                Y = clientRect.Top
            };
            if (!ClientToScreen(hWnd, ref origin))
            {
                return false;
            }

            clientSize = new Size(width, height);
            clientBoundsScreen = new Rectangle(origin.X, origin.Y, width, height);
            return true;
        }

        public static Bitmap CaptureClientRegion(IntPtr hWnd, VisionRegion region)
        {
            using (VisionCaptureResult result = CaptureClientRegionDetailed(
                hWnd,
                region,
                new VisionCaptureSettings()))
            {
                return new Bitmap(result.Image);
            }
        }

        public static VisionCaptureResult CaptureClientRegionDetailed(
            IntPtr hWnd,
            VisionRegion region,
            VisionCaptureSettings settings)
        {
            if (region == null || !region.IsValid)
            {
                throw new ArgumentException("The vision region must be a positive client-area rectangle.", "region");
            }

            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
            {
                throw new InvalidOperationException("The target window is not visible or is minimized.");
            }

            VisionCaptureSettings effectiveSettings = settings == null
                ? new VisionCaptureSettings()
                : settings.Clone();
            effectiveSettings.Validate();

            Rectangle clientBounds;
            Size clientSize;
            if (!TryGetClientBounds(hWnd, out clientBounds, out clientSize))
            {
                throw new InvalidOperationException("The target window client area is unavailable.");
            }

            string clientSizeError;
            if (!TryValidateClientSize(clientSize, effectiveSettings, out clientSizeError))
            {
                throw new VisionClientSizeMismatchException(
                    clientSize,
                    new Size(
                        effectiveSettings.RequiredClientWidth,
                        effectiveSettings.RequiredClientHeight));
            }

            if (!region.FitsWithin(clientSize))
            {
                throw new ArgumentOutOfRangeException(
                    "region",
                    "The vision region must stay inside the current client area.");
            }

            Rectangle effectiveRegion = region.Resolve(clientSize);

            Rectangle screenRegion = new Rectangle(
                clientBounds.Left + effectiveRegion.X,
                clientBounds.Top + effectiveRegion.Y,
                effectiveRegion.Width,
                effectiveRegion.Height);

            Exception screenCaptureFailure = null;
            if (effectiveSettings.SourceMode == VisionCaptureSourceMode.Auto &&
                GetForegroundWindow() != hWnd)
            {
                VisionCaptureResult backgroundRender = CaptureWindowRender(
                    hWnd,
                    clientSize,
                    effectiveRegion,
                    effectiveSettings);
                if (backgroundRender != null)
                {
                    return backgroundRender;
                }
                throw new InvalidOperationException(
                    "The target window is in the background and could not be rendered safely.");
            }

            if (effectiveSettings.SourceMode != VisionCaptureSourceMode.WindowRender)
            {
                Bitmap bitmap = new Bitmap(effectiveRegion.Width, effectiveRegion.Height, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(
                            screenRegion.Location,
                            Point.Empty,
                            screenRegion.Size,
                            CopyPixelOperation.SourceCopy);
                    }
                    VisionCaptureResult screenResult = CreateCaptureResult(
                        bitmap,
                        VisionCaptureSourceMode.Screen,
                        effectiveSettings);
                    if (!screenResult.IsBlank || effectiveSettings.SourceMode == VisionCaptureSourceMode.Screen)
                    {
                        return screenResult;
                    }
                    screenResult.Dispose();
                }
                catch (Exception ex)
                {
                    bitmap.Dispose();
                    screenCaptureFailure = ex;
                    if (effectiveSettings.SourceMode == VisionCaptureSourceMode.Screen)
                    {
                        throw new InvalidOperationException(
                            "The target client area could not be captured from the screen.",
                            screenCaptureFailure);
                    }
                }
            }

            VisionCaptureResult windowRender = CaptureWindowRender(
                hWnd,
                clientSize,
                effectiveRegion,
                effectiveSettings);
            if (windowRender != null)
            {
                return windowRender;
            }

            throw new InvalidOperationException(
                "The target client area could not be captured by screen or window rendering.",
                screenCaptureFailure);
        }

        public static bool TryValidateClientSize(
            IntPtr hWnd,
            VisionCaptureSettings settings,
            out Size clientSize,
            out string error)
        {
            clientSize = Size.Empty;
            error = string.Empty;
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            {
                error = "The target window is unavailable.";
                return false;
            }
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
            {
                error = "The target window is not visible or is minimized.";
                return false;
            }

            Rectangle clientBounds;
            if (!TryGetClientBounds(hWnd, out clientBounds, out clientSize))
            {
                error = "The target window client area is unavailable.";
                return false;
            }

            VisionCaptureSettings effectiveSettings = settings == null
                ? new VisionCaptureSettings()
                : settings;
            try
            {
                effectiveSettings.Validate();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            return TryValidateClientSize(clientSize, effectiveSettings, out error);
        }

        private static bool TryValidateClientSize(
            Size clientSize,
            VisionCaptureSettings settings,
            out string error)
        {
            error = string.Empty;
            if (settings == null || !settings.RequireExactClientSize ||
                settings.IsClientSizeMatch(clientSize))
            {
                return true;
            }

            error = settings.DescribeClientSizeMismatch(clientSize);
            return false;
        }

        private static VisionCaptureResult CaptureWindowRender(
            IntPtr hWnd,
            Size clientSize,
            Rectangle effectiveRegion,
            VisionCaptureSettings settings)
        {
            Bitmap clientCapture = TryPrintWindowClient(hWnd, clientSize);
            if (clientCapture == null)
            {
                return null;
            }
            try
            {
                Bitmap bitmap = clientCapture.Clone(effectiveRegion, PixelFormat.Format32bppArgb);
                return CreateCaptureResult(bitmap, VisionCaptureSourceMode.WindowRender, settings);
            }
            finally
            {
                clientCapture.Dispose();
            }
        }

        public static bool IsWindowUsable(IntPtr hWnd)
        {
            Rectangle bounds;
            Size size;
            return hWnd != IntPtr.Zero &&
                IsWindow(hWnd) &&
                IsWindowVisible(hWnd) &&
                !IsIconic(hWnd) &&
                TryGetClientBounds(hWnd, out bounds, out size);
        }

        public static bool IsForegroundWindow(IntPtr hWnd)
        {
            return hWnd != IntPtr.Zero && GetForegroundWindow() == hWnd;
        }

        public static bool TryResolveWindow(
            IntPtr preferredHandle,
            int processId,
            string processName,
            string processPath,
            long processStartTimeUtcTicks,
            string windowTitle,
            out VisionWindowInfo resolved,
            out string reason)
        {
            resolved = null;
            reason = string.Empty;
            IList<VisionWindowInfo> windows = EnumerateVisibleWindows(0);
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i].Handle == preferredHandle &&
                    IsWindowUsable(preferredHandle) &&
                    MatchesProcessIdentity(
                        windows[i],
                        processId,
                        processName,
                        processPath,
                        processStartTimeUtcTicks,
                        windowTitle))
                {
                    resolved = windows[i];
                    return true;
                }
            }

            List<VisionWindowInfo> identityMatches = new List<VisionWindowInfo>();
            for (int i = 0; i < windows.Count; i++)
            {
                VisionWindowInfo candidate = windows[i];
                if (MatchesProcessIdentity(
                    candidate,
                    processId,
                    processName,
                    processPath,
                    processStartTimeUtcTicks,
                    windowTitle))
                {
                    identityMatches.Add(candidate);
                }
            }
            if (identityMatches.Count == 1)
            {
                resolved = identityMatches[0];
                reason = "rebound by unique process identity";
                return true;
            }
            reason = identityMatches.Count > 1
                ? "multiple windows match the configured process identity; select the target window again"
                : "the configured window is unavailable; reopen it or refresh the window list";
            return false;
        }

        private static bool MatchesProcessIdentity(
            VisionWindowInfo candidate,
            int processId,
            string processName,
            string processPath,
            long processStartTimeUtcTicks,
            string windowTitle)
        {
            if (candidate == null)
            {
                return false;
            }
            if (processId > 0 && candidate.ProcessId != processId)
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(processName) &&
                !string.Equals(candidate.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(processPath) &&
                !string.Equals(candidate.ProcessPath, processPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (processStartTimeUtcTicks > 0L &&
                candidate.ProcessStartTimeUtcTicks != processStartTimeUtcTicks)
            {
                return false;
            }
            return string.IsNullOrWhiteSpace(windowTitle) ||
                string.Equals(candidate.WindowTitle, windowTitle, StringComparison.Ordinal);
        }

        public static void SavePng(Bitmap bitmap, string filePath)
        {
            if (bitmap == null)
            {
                throw new ArgumentNullException("bitmap");
            }
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A screenshot path is required.", "filePath");
            }

            bitmap.Save(filePath, ImageFormat.Png);
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            StringBuilder text = new StringBuilder(512);
            GetWindowText(hWnd, text, text.Capacity);
            return text.ToString().Trim();
        }

        private static bool IsLdPlayerEngineProcess(string processName)
        {
            return string.Equals(processName, "Ld9BoxHeadless", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, "LdVBoxHeadless", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLdPlayerFrontendProcess(string processName)
        {
            return string.Equals(processName, "dnplayer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, "dnmultiplayer", StringComparison.OrdinalIgnoreCase);
        }

        private static VisionCaptureResult CreateCaptureResult(
            Bitmap bitmap,
            VisionCaptureSourceMode sourceMode,
            VisionCaptureSettings settings)
        {
            double mean;
            double contrast;
            uint fingerprint;
            CalculateImageStats(bitmap, out mean, out contrast, out fingerprint);
            bool blank = mean <= settings.BlankBrightnessThreshold &&
                contrast <= settings.BlankContrastThreshold;
            return new VisionCaptureResult(
                bitmap,
                sourceMode,
                mean,
                contrast,
                blank,
                fingerprint,
                blank ? "capture appears blank or uniform" : string.Empty);
        }

        private static void CalculateImageStats(
            Bitmap bitmap,
            out double mean,
            out double contrast,
            out uint fingerprint)
        {
            long sum = 0L;
            long sumSquared = 0L;
            int count = 0;
            int sampleStepX = Math.Max(1, bitmap.Width / 64);
            int sampleStepY = Math.Max(1, bitmap.Height / 64);
            for (int y = 0; y < bitmap.Height; y += sampleStepY)
            {
                for (int x = 0; x < bitmap.Width; x += sampleStepX)
                {
                    Color color = bitmap.GetPixel(x, y);
                    int value = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                    sum += value;
                    sumSquared += value * value;
                    count++;
                }
            }
            mean = count == 0 ? 0D : sum / (double)count;
            double variance = count == 0
                ? 0D
                : sumSquared / (double)count - mean * mean;
            contrast = Math.Sqrt(Math.Max(0D, variance));
            fingerprint = VisionBitmapFingerprint.Compute(bitmap);
        }

        private static Bitmap TryPrintWindowClient(IntPtr hWnd, Size clientSize)
        {
            Bitmap bitmap = new Bitmap(clientSize.Width, clientSize.Height, PixelFormat.Format32bppArgb);
            Graphics graphics = null;
            IntPtr hdc = IntPtr.Zero;
            try
            {
                graphics = Graphics.FromImage(bitmap);
                hdc = graphics.GetHdc();
                if (!PrintWindow(hWnd, hdc, PrintWindowClientOnly))
                {
                    bitmap.Dispose();
                    return null;
                }

                return bitmap;
            }
            catch (Exception)
            {
                bitmap.Dispose();
                return null;
            }
            finally
            {
                if (hdc != IntPtr.Zero && graphics != null)
                {
                    graphics.ReleaseHdc(hdc);
                }
                if (graphics != null)
                {
                    graphics.Dispose();
                }
            }
        }
    }
}
