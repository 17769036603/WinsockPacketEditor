using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionTesseractRecognizer : IVisionTextRecognizer, IDisposable
    {
        private readonly string executablePath;
        private readonly System.Threading.SemaphoreSlim ocrWorkerGate =
            new System.Threading.SemaphoreSlim(1, 1);
        private readonly object resultCacheSync = new object();
        private readonly Dictionary<string, VisionOcrResult> resultCache =
            new Dictionary<string, VisionOcrResult>(StringComparer.Ordinal);
        private readonly Queue<string> resultCacheOrder = new Queue<string>();
        private const int ResultCacheLimit = 32;
        private int disposed;

        public VisionTesseractRecognizer(string executablePath)
        {
            this.executablePath = string.IsNullOrWhiteSpace(executablePath)
                ? "tesseract.exe"
                : executablePath;
        }

        public bool IsAvailable
        {
            get { return !string.IsNullOrEmpty(ResolveExecutable(this.executablePath)); }
        }

        public VisionOcrResult Recognize(
            Bitmap source,
            VisionOcrOptions options,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            options.Validate();
            if (Volatile.Read(ref this.disposed) != 0)
            {
                return VisionOcrResult.Unavailable("The Tesseract recognizer has been disposed.");
            }
            VisionOcrOptions effectiveOptions = OptimizeForLargeScreenshot(source, options);
            effectiveOptions.Validate();
            if (cancellationToken.IsCancellationRequested)
            {
                return VisionOcrResult.CancelledResult();
            }

            string cacheKey = BuildCacheKey(source, effectiveOptions);
            VisionOcrResult cached = TryGetCachedResult(cacheKey);
            if (cached != null)
            {
                return cached;
            }

            try
            {
                if (!this.ocrWorkerGate.Wait(effectiveOptions.TimeoutMilliseconds + 1000, cancellationToken))
                {
                    return VisionOcrResult.Failed("OCR worker is busy.", string.Empty, 0D, -1);
                }
            }
            catch (OperationCanceledException)
            {
                return VisionOcrResult.CancelledResult();
            }
            catch (ObjectDisposedException)
            {
                return VisionOcrResult.Unavailable("The Tesseract recognizer has been disposed.");
            }

            try
            {
                cached = TryGetCachedResult(cacheKey);
                if (cached != null)
                {
                    return cached;
                }

                string resolvedExecutable = ResolveExecutable(
                    string.IsNullOrWhiteSpace(effectiveOptions.ExecutablePath)
                        ? this.executablePath
                        : effectiveOptions.ExecutablePath);
                if (string.IsNullOrEmpty(resolvedExecutable))
                {
                    return VisionOcrResult.Unavailable(
                        "Tesseract executable was not found. Install Tesseract or configure its executable path.");
                }

                VisionOcrResult result;
                using (Bitmap prepared = VisionImagePreprocessor.Preprocess(source, effectiveOptions, cancellationToken))
                {
                    if (prepared == null || cancellationToken.IsCancellationRequested)
                    {
                        return VisionOcrResult.CancelledResult();
                    }

                    try
                    {
                        result = RunTesseract(resolvedExecutable, prepared, effectiveOptions, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        result = VisionOcrResult.Failed(ex.Message, string.Empty, 0D, -1);
                    }
                }
                if (!result.Cancelled && result.Available)
                {
                    StoreCachedResult(cacheKey, result);
                }
                return result;
            }
            finally
            {
                this.ocrWorkerGate.Release();
            }
        }

        private static VisionOcrOptions OptimizeForLargeScreenshot(
            Bitmap source,
            VisionOcrOptions options)
        {
            if ((source.Width < 1280 && source.Height < 720) ||
                options.ScaleFactor != 2 ||
                options.PageSegmentationMode != 6)
            {
                return options;
            }

            // The default 2x nearest-neighbour enlargement damages small Chinese UI glyphs
            // on already high-resolution game captures. Use the sparse-text layout at 1x.
            VisionOcrOptions optimized = options.Clone();
            optimized.ScaleFactor = 1;
            optimized.PageSegmentationMode = 12;
            return optimized;
        }

        private static VisionOcrResult RunTesseract(
            string executable,
            Bitmap prepared,
            VisionOcrOptions options,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = BuildArguments(options),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    return VisionOcrResult.Failed("Unable to start Tesseract.", string.Empty, 0D, -1);
                }

                byte[] pngBytes;
                using (MemoryStream imageStream = new MemoryStream())
                {
                    prepared.Save(imageStream, ImageFormat.Png);
                    pngBytes = imageStream.ToArray();
                }

                System.Threading.Tasks.Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> errorTask = process.StandardError.ReadToEndAsync();
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(options.TimeoutMilliseconds);
                System.Threading.Tasks.Task writeTask = process.StandardInput.BaseStream.WriteAsync(
                    pngBytes,
                    0,
                    pngBytes.Length,
                    cancellationToken);
                while (!writeTask.IsCompleted)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        CloseStandardInput(process);
                        TryKill(process);
                        DrainProcessOutput(process, outputTask, errorTask);
                        ObserveTask(writeTask);
                        return VisionOcrResult.CancelledResult();
                    }
                    if (DateTime.UtcNow >= deadline)
                    {
                        CloseStandardInput(process);
                        TryKill(process);
                        DrainProcessOutput(process, outputTask, errorTask);
                        ObserveTask(writeTask);
                        return VisionOcrResult.Failed(
                            "Tesseract timed out while writing the image.",
                            string.Empty,
                            0D,
                            -1);
                    }
                    Thread.Sleep(50);
                }
                try
                {
                    writeTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    CloseStandardInput(process);
                    TryKill(process);
                    DrainProcessOutput(process, outputTask, errorTask);
                    return VisionOcrResult.CancelledResult();
                }
                catch (Exception ex)
                {
                    CloseStandardInput(process);
                    TryKill(process);
                    DrainProcessOutput(process, outputTask, errorTask);
                    return VisionOcrResult.Failed(
                        "Unable to write the image to Tesseract: " + ex.Message,
                        string.Empty,
                        0D,
                        -1);
                }
                process.StandardInput.Close();
                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryKill(process);
                        DrainProcessOutput(process, outputTask, errorTask);
                        return VisionOcrResult.CancelledResult();
                    }
                    if (DateTime.UtcNow >= deadline)
                    {
                        TryKill(process);
                        DrainProcessOutput(process, outputTask, errorTask);
                        return VisionOcrResult.Failed(
                            "Tesseract timed out.",
                            string.Empty,
                            0D,
                            -1);
                    }

                    process.WaitForExit(50);
                }

                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                VisionTsvData tsv = ParseTsv(output, options.ScaleFactor);
                if (process.ExitCode != 0)
                {
                    return VisionOcrResult.Failed(
                        string.IsNullOrWhiteSpace(error)
                            ? "Tesseract returned a non-zero exit code."
                            : error.Trim(),
                        tsv.Text,
                        tsv.Confidence,
                        process.ExitCode);
                }
                if (string.IsNullOrWhiteSpace(tsv.Text))
                {
                    return VisionOcrResult.Failed(
                        "Tesseract returned no text.",
                        string.Empty,
                        tsv.Confidence,
                        process.ExitCode);
                }

                return VisionOcrResult.Succeeded(tsv.Text, tsv.Confidence, tsv.TextBoxes);
            }
        }

        private static string BuildArguments(VisionOcrOptions options)
        {
            StringBuilder arguments = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(options.TessdataPath))
            {
                arguments.Append("--tessdata-dir ");
                arguments.Append(QuoteArgument(options.TessdataPath));
                arguments.Append(' ');
            }

            arguments.Append("stdin stdout --psm ");
            arguments.Append(options.PageSegmentationMode.ToString(CultureInfo.InvariantCulture));
            arguments.Append(" -l ");
            arguments.Append(QuoteArgument(options.Language));
            arguments.Append(" tsv");
            if (!string.IsNullOrWhiteSpace(options.CharacterWhitelist))
            {
                arguments.Append(" -c tessedit_char_whitelist=");
                arguments.Append(QuoteArgument(options.CharacterWhitelist));
            }
            if (!string.IsNullOrWhiteSpace(options.CharacterBlacklist))
            {
                arguments.Append(" -c tessedit_char_blacklist=");
                arguments.Append(QuoteArgument(options.CharacterBlacklist));
            }
            return arguments.ToString();
        }

        private VisionOcrResult TryGetCachedResult(string key)
        {
            lock (this.resultCacheSync)
            {
                VisionOcrResult result;
                return this.resultCache.TryGetValue(key, out result) ? result : null;
            }
        }

        private void StoreCachedResult(string key, VisionOcrResult result)
        {
            lock (this.resultCacheSync)
            {
                bool alreadyCached = this.resultCache.ContainsKey(key);
                this.resultCache[key] = result;
                if (!alreadyCached)
                {
                    this.resultCacheOrder.Enqueue(key);
                }
                while (this.resultCacheOrder.Count > ResultCacheLimit)
                {
                    string oldest = this.resultCacheOrder.Dequeue();
                    this.resultCache.Remove(oldest);
                }
            }
        }

        private static string BuildCacheKey(Bitmap source, VisionOcrOptions options)
        {
            uint fingerprint = VisionBitmapFingerprint.Compute(source);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}x{1}:{2:X8}:{3}:{4}:{5}:{6}:{7}:{8}:{9}:{10}:{11}:{12}:{13}:{14}:{15}:{16}:{17}:{18}:{19}:{20}",
                source.Width,
                source.Height,
                fingerprint,
                options.ScaleFactor,
                options.ConvertToGrayscale,
                options.UseBinaryThreshold,
                options.BinaryThreshold,
                options.Contrast,
                options.UseAdaptiveThreshold,
                options.AdaptiveThresholdWindowSize,
                options.AdaptiveThresholdOffset,
                options.Invert,
                options.UseDenoise,
                options.UseSharpen,
                options.CharacterWhitelist,
                options.CharacterBlacklist,
                options.Language,
                options.PageSegmentationMode,
                options.ExecutablePath,
                options.TessdataPath,
                options.TimeoutMilliseconds);
        }

        private static string QuoteArgument(string value)
        {
            string text = value ?? string.Empty;
            StringBuilder quoted = new StringBuilder(text.Length + 2);
            quoted.Append('"');
            int backslashes = 0;
            foreach (char character in text)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1);
                    quoted.Append('"');
                    backslashes = 0;
                    continue;
                }
                quoted.Append('\\', backslashes);
                quoted.Append(character);
                backslashes = 0;
            }
            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }

        private static string ResolveExecutable(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return null;
            }
            if (File.Exists(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }
            if (configuredPath.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                configuredPath.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                return null;
            }

            string pathEnvironment = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] pathEntries = pathEnvironment.Split(Path.PathSeparator);
            foreach (string pathEntry in pathEntries)
            {
                if (string.IsNullOrWhiteSpace(pathEntry))
                {
                    continue;
                }

                string candidate = Path.Combine(pathEntry.Trim(), configuredPath);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            if (configuredPath.Equals("tesseract.exe", StringComparison.OrdinalIgnoreCase))
            {
                string[] programFilesDirectories =
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                };
                foreach (string programFilesDirectory in programFilesDirectories)
                {
                    if (string.IsNullOrWhiteSpace(programFilesDirectory))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(
                        programFilesDirectory,
                        "Tesseract-OCR",
                        "tesseract.exe");
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }

            return null;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // The caller reports timeout/cancellation; cleanup is best effort.
            }
        }

        private static void DrainProcessOutput(
            Process process,
            System.Threading.Tasks.Task<string> outputTask,
            System.Threading.Tasks.Task<string> errorTask)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.WaitForExit(1000);
                }
            }
            catch
            {
            }

            try
            {
                if (outputTask != null)
                {
                    ObserveTask(outputTask);
                }
            }
            catch
            {
            }
            try
            {
                if (errorTask != null)
                {
                    ObserveTask(errorTask);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }
            lock (this.resultCacheSync)
            {
                this.resultCache.Clear();
                this.resultCacheOrder.Clear();
            }
        }

        private static void CloseStandardInput(Process process)
        {
            try
            {
                if (process != null && process.StandardInput != null)
                {
                    process.StandardInput.Close();
                }
            }
            catch
            {
            }
        }

        private static void ObserveTask(System.Threading.Tasks.Task task)
        {
            if (task == null)
            {
                return;
            }
            if (!task.IsCompleted)
            {
                task.ContinueWith(
                    completedTask => ObserveCompletedTask(completedTask),
                    CancellationToken.None,
                    System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,
                    System.Threading.Tasks.TaskScheduler.Default);
                return;
            }

            ObserveCompletedTask(task);
        }

        private static void ObserveCompletedTask(System.Threading.Tasks.Task task)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        private static VisionTsvData ParseTsv(string output, int scaleFactor)
        {
            StringBuilder text = new StringBuilder();
            List<double> confidenceValues = new List<double>();
            List<VisionOcrTextBox> textBoxes = new List<VisionOcrTextBox>();
            string[] lines = (output ?? string.Empty).Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string[] fields = line.Split('\t');
                if (fields.Length < 12 || fields[0].Equals("level", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string token = fields[11].Trim();
                double left;
                double top;
                double width;
                double height;
                double confidence;
                if (!double.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out left) ||
                    !double.TryParse(fields[7], NumberStyles.Float, CultureInfo.InvariantCulture, out top) ||
                    !double.TryParse(fields[8], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                    !double.TryParse(fields[9], NumberStyles.Float, CultureInfo.InvariantCulture, out height))
                {
                    continue;
                }
                if (!double.TryParse(fields[10], NumberStyles.Float, CultureInfo.InvariantCulture, out confidence))
                {
                    confidence = -1D;
                }
                double scale = Math.Max(1, scaleFactor);
                Rectangle bounds = new Rectangle(
                    (int)Math.Round(left / scale),
                    (int)Math.Round(top / scale),
                    Math.Max(1, (int)Math.Round(width / scale)),
                    Math.Max(1, (int)Math.Round(height / scale)));
                if (!string.IsNullOrEmpty(token))
                {
                    if (text.Length > 0)
                    {
                        text.Append(' ');
                    }
                    text.Append(token);
                    textBoxes.Add(new VisionOcrTextBox(
                        token,
                        bounds,
                        confidence < 0D ? 0D : confidence / 100D));
                }

                if (confidence >= 0D)
                {
                    confidenceValues.Add(confidence / 100D);
                }
            }

            double average = 0D;
            if (confidenceValues.Count > 0)
            {
                double total = 0D;
                foreach (double value in confidenceValues)
                {
                    total += value;
                }
                average = total / confidenceValues.Count;
            }

            return new VisionTsvData(text.ToString(), average, textBoxes);
        }

        private sealed class VisionTsvData
        {
            public VisionTsvData(string text, double confidence, IList<VisionOcrTextBox> textBoxes)
            {
                this.Text = text;
                this.Confidence = confidence;
                this.TextBoxes = textBoxes;
            }

            public string Text { get; private set; }

            public double Confidence { get; private set; }

            public IList<VisionOcrTextBox> TextBoxes { get; private set; }
        }
    }
}
