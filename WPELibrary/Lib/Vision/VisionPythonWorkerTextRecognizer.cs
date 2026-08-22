using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// Long-lived JSONL bridge to the optional Python RapidOCR/Airtest worker.
    /// One bridge instance owns one worker process and serializes requests.
    /// </summary>
    public sealed class VisionPythonWorkerTextRecognizer :
        IVisionTextRecognizer,
        IVisionCaptureProvider,
        IVisionSystemInputProvider,
        IDisposable
    {
        private readonly object processSync = new object();
        private Process workerProcess;
        private string workerExecutable;
        private string workerScript;
        private int requestId;
        private int disposed;
        private string lastDiagnostic = string.Empty;
        private readonly object modelStatusSync = new object();
        private string modelStatusFingerprint = string.Empty;
        private string modelStatusDirectory = string.Empty;
        private VisionWorkerModelStatus modelStatusCache;

        public string LastDiagnostic
        {
            get { return this.lastDiagnostic ?? string.Empty; }
        }

        public bool IsAvailable(VisionOcrOptions options)
        {
            return this.CheckHealth(options, false, CancellationToken.None).IsReady;
        }

        public VisionPythonWorkerHealth CheckHealth(
            VisionOcrOptions options,
            bool warmup,
            CancellationToken cancellationToken)
        {
            if (options == null)
            {
                return new VisionPythonWorkerHealth(
                    false,
                    "OCR options are missing.",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false,
                    false,
                    0,
                    string.Empty);
            }

            string executable = string.Empty;
            string script = string.Empty;
            string modelDirectory = string.Empty;
            try
            {
                options.Validate();
                executable = ResolvePythonExecutable(options.PythonExecutablePath);
                script = ResolveWorkerScript(options.PythonWorkerScriptPath);
                if (string.IsNullOrEmpty(executable))
                {
                    return CreateHealth(false, "Python runtime was not found.", executable, script, modelDirectory, false, false, 0, string.Empty);
                }
                if (string.IsNullOrEmpty(script))
                {
                    return CreateHealth(false, "The bundled vision_worker\\worker.py file was not found.", executable, script, modelDirectory, false, false, 0, string.Empty);
                }

                modelDirectory = ResolveWorkerModelDirectory(options.OnnxModelDirectory, script);
                VisionWorkerModelStatus modelStatus = GetWorkerModelStatus(modelDirectory);
                if (!modelStatus.IsValid)
                {
                    return CreateHealth(false, modelStatus.Message, executable, script, modelDirectory, modelStatus.ManifestVerified, false, 0, string.Empty);
                }
                if (!warmup)
                {
                    return CreateHealth(true, modelStatus.Message, executable, script, modelDirectory, modelStatus.ManifestVerified, false, 0, string.Empty);
                }

                JObject ping = SendRequest(
                    executable,
                    script,
                    new JObject(),
                    "ping",
                    Math.Min(10000, options.PythonWorkerTimeoutMilliseconds),
                    cancellationToken);
                if (!(ping.Value<bool?>("ok") ?? false))
                {
                    return CreateHealth(false, ping.Value<string>("error") ?? "Python Worker ping failed.", executable, script, modelDirectory, modelStatus.ManifestVerified, true, 0, string.Empty);
                }

                JObject warmupResponse = SendRequest(
                    executable,
                    script,
                    new JObject
                    {
                        ["model_directory"] = modelDirectory,
                        ["language"] = options.Language ?? string.Empty
                    },
                    "warmup",
                    options.PythonWorkerTimeoutMilliseconds,
                    cancellationToken);
                bool success = warmupResponse.Value<bool?>("success") ?? false;
                long warmupMilliseconds = warmupResponse.Value<long?>("warmup_ms") ?? 0L;
                string summary = success
                    ? string.Format("Python Worker ready; warmup {0} ms; {1}", warmupMilliseconds, modelStatus.Message)
                    : (warmupResponse.Value<string>("error") ?? "Python Worker warmup failed.");
                JObject capabilities = ping["capabilities"] as JObject;
                string capabilityText = capabilities == null
                    ? string.Empty
                    : capabilities.ToString(Formatting.None);
                return CreateHealth(
                    success,
                    summary,
                    executable,
                    script,
                    modelDirectory,
                    modelStatus.ManifestVerified,
                    true,
                    warmupMilliseconds,
                    capabilityText);
            }
            catch (OperationCanceledException)
            {
                ResetProcess();
                return CreateHealth(false, "Python Worker health check was cancelled.", executable, script, modelDirectory, false, false, 0, string.Empty);
            }
            catch (Exception ex)
            {
                ResetProcess();
                return CreateHealth(false, "Python Worker health check failed: " + ex.Message, executable, script, modelDirectory, false, false, 0, string.Empty);
            }
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
                return VisionOcrResult.Unavailable("The Python vision worker has been disposed.");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return VisionOcrResult.CancelledResult();
            }

            string executable = ResolvePythonExecutable(options.PythonExecutablePath);
            string script = ResolveWorkerScript(options.PythonWorkerScriptPath);
            if (string.IsNullOrEmpty(executable))
            {
                return VisionOcrResult.Unavailable(
                    "Python was not found. Install Python 3 and the local vision worker dependencies.");
            }
            if (string.IsNullOrEmpty(script))
            {
                return VisionOcrResult.Unavailable(
                    "The bundled vision_worker\\worker.py file was not found.");
            }

            try
            {
                JObject request = BuildRequest(source, options, script);
                JObject result = SendRequest(
                    executable,
                    script,
                    request,
                    "ocr",
                    options.PythonWorkerTimeoutMilliseconds,
                    cancellationToken);
                return ConvertResult(result);
            }
            catch (OperationCanceledException)
            {
                ResetProcess();
                return VisionOcrResult.CancelledResult();
            }
            catch (Exception ex)
            {
                ResetProcess();
                return VisionOcrResult.Unavailable("Python vision worker failed: " + ex.Message);
            }
        }

        public VisionCaptureResult Capture(
            IntPtr windowHandle,
            VisionRegion region,
            VisionCaptureSettings settings,
            VisionOcrOptions ocrOptions,
            CancellationToken cancellationToken)
        {
            if (windowHandle == IntPtr.Zero)
            {
                throw new ArgumentException("A target window handle is required.", "windowHandle");
            }
            if (region == null || !region.IsValid)
            {
                throw new ArgumentException("A valid vision region is required.", "region");
            }
            VisionCaptureSettings effectiveSettings = settings == null
                ? new VisionCaptureSettings()
                : settings.Clone();
            effectiveSettings.Validate();
            Size clientSize;
            string clientSizeError;
            if (!VisionWindowService.TryValidateClientSize(
                windowHandle,
                effectiveSettings,
                out clientSize,
                out clientSizeError))
            {
                throw new InvalidOperationException(clientSizeError);
            }
            if (!region.FitsWithin(clientSize))
            {
                throw new ArgumentOutOfRangeException(
                    "region",
                    "The vision region must stay inside the current client area.");
            }
            VisionOcrOptions effectiveOcrOptions = ocrOptions == null
                ? new VisionOcrOptions()
                : ocrOptions.Clone();
            string executable = ResolvePythonExecutable(effectiveOcrOptions.PythonExecutablePath);
            string script = ResolveWorkerScript(effectiveOcrOptions.PythonWorkerScriptPath);
            if (string.IsNullOrEmpty(executable) || string.IsNullOrEmpty(script))
            {
                throw new InvalidOperationException(
                    "The Python vision worker runtime is not installed.");
            }

            JObject request = new JObject
            {
                ["window_handle"] = windowHandle.ToInt64(),
                ["region"] = new JObject
                {
                    ["x"] = region.Resolve(clientSize).X,
                    ["y"] = region.Resolve(clientSize).Y,
                    ["width"] = region.Resolve(clientSize).Width,
                    ["height"] = region.Resolve(clientSize).Height
                }
            };
            JObject response = SendRequest(
                executable,
                script,
                request,
                "capture",
                30000,
                cancellationToken);
            if (!(response.Value<bool?>("success") ?? false))
            {
                throw new InvalidOperationException(
                    response.Value<string>("error") ?? "Airtest capture failed.");
            }
            string encoded = response.Value<string>("image_base64");
            if (string.IsNullOrWhiteSpace(encoded))
            {
                throw new InvalidOperationException("Airtest returned an empty screenshot.");
            }
            using (MemoryStream stream = new MemoryStream(Convert.FromBase64String(encoded)))
            using (Bitmap decoded = new Bitmap(stream))
            {
                return VisionWindowService.CreateCaptureResult(
                    new Bitmap(decoded),
                    VisionCaptureSourceMode.Airtest,
                    effectiveSettings);
            }
        }

        public VisionAssistantActionResult ExecuteAirtestAction(
            IntPtr windowHandle,
            Point clientPoint,
            VisionActionDefinition definition,
            VisionOcrOptions ocrOptions,
            bool allowSystemInput,
            CancellationToken cancellationToken)
        {
            if (definition == null)
            {
                return VisionAssistantActionResult.Failed("An Airtest action is required.");
            }
            try
            {
                definition.Validate();
                VisionOcrOptions effectiveOcrOptions = ocrOptions == null
                    ? new VisionOcrOptions()
                    : ocrOptions.Clone();
                string executable = ResolvePythonExecutable(effectiveOcrOptions.PythonExecutablePath);
                string script = ResolveWorkerScript(effectiveOcrOptions.PythonWorkerScriptPath);
                if (string.IsNullOrEmpty(executable) || string.IsNullOrEmpty(script))
                {
                    return VisionAssistantActionResult.Failed(
                        "The Python vision worker runtime is not installed.");
                }
                JObject session = SendRequest(
                    executable,
                    script,
                    new JObject
                    {
                        ["window_handle"] = windowHandle.ToInt64(),
                        ["allow_system_input"] = allowSystemInput
                    },
                    "begin_input_session",
                    10000,
                    cancellationToken);
                if (!(session.Value<bool?>("success") ?? false))
                {
                    return VisionAssistantActionResult.Failed(
                        session.Value<string>("error") ?? "Airtest input authorization failed.");
                }

                string action;
                JObject request = new JObject
                {
                    ["window_handle"] = windowHandle.ToInt64(),
                    ["authorization_token"] = session.Value<string>("authorization_token") ?? string.Empty,
                    ["x"] = clientPoint.X,
                    ["y"] = clientPoint.Y
                };
                switch (definition.Type)
                {
                    case VisionActionType.LeftClick:
                        action = "touch";
                        request["button"] = "left";
                        break;
                    case VisionActionType.RightClick:
                        action = "touch";
                        request["button"] = "right";
                        break;
                    case VisionActionType.DoubleClick:
                        action = "touch";
                        request["button"] = "left";
                        request["times"] = 2;
                        break;
                    case VisionActionType.Scroll:
                        action = "scroll";
                        request["amount"] = definition.ScrollDirection == VisionScrollDirection.Down
                            ? -definition.ScrollAmount
                            : definition.ScrollAmount;
                        break;
                    default:
                        return VisionAssistantActionResult.Failed("Unsupported Airtest action type.");
                }
                request["action"] = action;
                JObject result = SendRequest(
                    executable,
                    script,
                    request,
                    "airtest_action",
                    10000,
                    cancellationToken);
                if (result.Value<bool?>("cancelled") ?? false)
                {
                    return VisionAssistantActionResult.CancelledResult();
                }
                return (result.Value<bool?>("success") ?? false)
                    ? VisionAssistantActionResult.Succeeded()
                    : VisionAssistantActionResult.Failed(
                        result.Value<string>("error") ?? "Airtest action failed.");
            }
            catch (OperationCanceledException)
            {
                ResetProcess();
                return VisionAssistantActionResult.CancelledResult();
            }
            catch (Exception ex)
            {
                ResetProcess();
                return VisionAssistantActionResult.Failed("Airtest action failed: " + ex.Message);
            }
        }

        private JObject BuildRequest(Bitmap source, VisionOcrOptions options, string script)
        {
            byte[] imageBytes;
            using (MemoryStream stream = new MemoryStream())
            {
                source.Save(stream, ImageFormat.Png);
                imageBytes = stream.ToArray();
            }

            return new JObject
            {
                ["image_base64"] = Convert.ToBase64String(imageBytes),
                ["language"] = options.Language ?? string.Empty,
                ["model_directory"] = ResolveWorkerModelDirectory(options.OnnxModelDirectory, script),
                ["recognition_threshold"] = options.OnnxRecognitionThreshold,
                ["detection_threshold"] = options.OnnxDetectionThreshold,
                ["max_image_side"] = options.OnnxMaxImageSide,
                ["character_whitelist"] = options.CharacterWhitelist ?? string.Empty,
                ["character_blacklist"] = options.CharacterBlacklist ?? string.Empty,
                ["scale_factor"] = options.ScaleFactor,
                ["convert_to_grayscale"] = options.ConvertToGrayscale,
                ["use_binary_threshold"] = options.UseBinaryThreshold,
                ["binary_threshold"] = options.BinaryThreshold,
                ["contrast"] = options.Contrast,
                ["use_adaptive_threshold"] = options.UseAdaptiveThreshold,
                ["adaptive_threshold_window_size"] = options.AdaptiveThresholdWindowSize,
                ["adaptive_threshold_offset"] = options.AdaptiveThresholdOffset,
                ["invert"] = options.Invert,
                ["use_denoise"] = options.UseDenoise,
                ["use_sharpen"] = options.UseSharpen
            };
        }

        private JObject SendRequest(
            string executable,
            string script,
            JObject request,
            string method,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            lock (this.processSync)
            {
                if (Volatile.Read(ref this.disposed) != 0)
                {
                    throw new ObjectDisposedException("VisionPythonWorkerTextRecognizer");
                }
                EnsureProcess(executable, script);
                int id = Interlocked.Increment(ref this.requestId);
                request["id"] = id;
                request["method"] = method;
                this.workerProcess.StandardInput.WriteLine(request.ToString(Formatting.None));
                this.workerProcess.StandardInput.Flush();
                JObject response = ReadResponse(id, timeoutMilliseconds, cancellationToken);
                this.lastDiagnostic = DescribeDiagnostic(method, response);
                return response;
            }
        }

        private static VisionPythonWorkerHealth CreateHealth(
            bool isReady,
            string summary,
            string executable,
            string script,
            string modelDirectory,
            bool manifestVerified,
            bool workerResponded,
            long warmupMilliseconds,
            string capabilities)
        {
            return new VisionPythonWorkerHealth(
                isReady,
                summary,
                executable,
                script,
                modelDirectory,
                manifestVerified,
                workerResponded,
                warmupMilliseconds,
                capabilities);
        }

        private static string DescribeDiagnostic(string method, JObject response)
        {
            if (response == null)
            {
                return method + ": empty response";
            }
            JObject diagnostics = response["diagnostics"] as JObject;
            if (diagnostics == null)
            {
                return method + (response.Value<bool?>("success") == false ? ": failed" : ": ok");
            }
            return method + ": " + diagnostics.ToString(Formatting.None);
        }

        private JObject ReadResponse(int expectedId, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                if (this.workerProcess == null || this.workerProcess.HasExited)
                {
                    throw new InvalidOperationException("The Python vision worker exited before returning a response.");
                }

                Task<string> readTask = this.workerProcess.StandardOutput.ReadLineAsync();
                while (!readTask.Wait(20))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException("The Python vision worker timed out.");
                    }
                    Thread.Sleep(20);
                }

                string line = readTask.GetAwaiter().GetResult();
                if (line == null)
                {
                    throw new InvalidOperationException("The Python vision worker closed stdout.");
                }
                JObject response;
                try
                {
                    response = JObject.Parse(line);
                }
                catch (JsonException)
                {
                    // A dependency must never write to stdout, but ignoring a
                    // stray line keeps the protocol recoverable until timeout.
                    continue;
                }
                if (response["id"] != null && response["id"].Value<int>() == expectedId)
                {
                    return response;
                }
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("The Python vision worker returned no matching response.");
                }
            }
        }

        private static VisionOcrResult ConvertResult(JObject response)
        {
            if (response == null)
            {
                return VisionOcrResult.Unavailable("The Python vision worker returned an empty response.");
            }
            bool available = response.Value<bool?>("available") ?? true;
            bool cancelled = response.Value<bool?>("cancelled") ?? false;
            string error = response.Value<string>("error") ?? string.Empty;
            string text = response.Value<string>("text") ?? string.Empty;
            double confidence = response.Value<double?>("confidence") ?? 0D;
            if (cancelled)
            {
                return VisionOcrResult.CancelledResult();
            }
            if (!available)
            {
                return VisionOcrResult.Unavailable(error);
            }

            List<VisionOcrTextBox> boxes = new List<VisionOcrTextBox>();
            JArray responseBoxes = response["boxes"] as JArray;
            if (responseBoxes != null)
            {
                foreach (JToken token in responseBoxes)
                {
                    JObject box = token as JObject;
                    if (box == null)
                    {
                        continue;
                    }
                    boxes.Add(new VisionOcrTextBox(
                        box.Value<string>("text") ?? string.Empty,
                        new Rectangle(
                            box.Value<int?>("x") ?? 0,
                            box.Value<int?>("y") ?? 0,
                            Math.Max(0, box.Value<int?>("width") ?? 0),
                            Math.Max(0, box.Value<int?>("height") ?? 0)),
                        box.Value<double?>("confidence") ?? 0D));
                }
            }
            bool success = response.Value<bool?>("success") ?? boxes.Count > 0;
            return success
                ? VisionOcrResult.Succeeded(text, confidence, boxes)
                : VisionOcrResult.Failed(error, text, confidence, 0);
        }

        private void EnsureProcess(string executable, string script)
        {
            if (this.workerProcess != null &&
                !this.workerProcess.HasExited &&
                string.Equals(this.workerExecutable, executable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(this.workerScript, script, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            ResetProcess();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = BuildArguments(executable, script),
                WorkingDirectory = Path.GetDirectoryName(script),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            this.workerProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            this.workerProcess.ErrorDataReceived += (sender, args) => { };
            if (!this.workerProcess.Start())
            {
                ResetProcess();
                throw new InvalidOperationException("Unable to start the Python vision worker.");
            }
            this.workerProcess.BeginErrorReadLine();
            this.workerExecutable = executable;
            this.workerScript = script;
        }

        private static string BuildArguments(string executable, string script)
        {
            string prefix = Path.GetFileNameWithoutExtension(executable).Equals(
                "py", StringComparison.OrdinalIgnoreCase)
                ? "-3 "
                : string.Empty;
            return prefix + "-u " + QuoteArgument(script) + " --jsonl";
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string ResolvePythonExecutable(string configured)
        {
            string[] candidates = string.IsNullOrWhiteSpace(configured)
                ? new[]
                {
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "XNAS", "WPE", "vision-worker", ".venv", "Scripts", "python.exe"),
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "XNAS", "WPE", "vision-worker", "python", "python.exe"),
                    "python.exe",
                    "py.exe"
                }
                : new[] { configured };
            foreach (string candidate in candidates)
            {
                if (Path.IsPathRooted(candidate))
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                    continue;
                }
                string pathMatch = FindOnPath(candidate);
                if (!string.IsNullOrEmpty(pathMatch))
                {
                    return pathMatch;
                }
            }
            return string.Empty;
        }

        private static string FindOnPath(string fileName)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }
                string candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return string.Empty;
        }

        private static string ResolveWorkerScript(string configured)
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                candidates.Add(configured);
            }
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vision_worker", "worker.py"));
            candidates.Add(Path.Combine(Environment.CurrentDirectory, "vision_worker", "worker.py"));
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int level = 0; directory != null && level < 5; level++, directory = directory.Parent)
            {
                candidates.Add(Path.Combine(directory.FullName, "vision_worker", "worker.py"));
            }
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            return string.Empty;
        }

        private static string ResolveWorkerModelDirectory(string configured, string script)
        {
            string value = string.IsNullOrWhiteSpace(configured)
                ? "models\\ocr"
                : configured.Trim();
            if (Path.IsPathRooted(value))
            {
                return Path.GetFullPath(value);
            }
            string scriptDirectory = Path.GetDirectoryName(script);
            return Path.GetFullPath(Path.Combine(
                string.IsNullOrEmpty(scriptDirectory)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : scriptDirectory,
                value));
        }

        private VisionWorkerModelStatus GetWorkerModelStatus(string directory)
        {
            string fingerprint = BuildModelStatusFingerprint(directory);
            lock (this.modelStatusSync)
            {
                if (this.modelStatusCache != null &&
                    string.Equals(this.modelStatusDirectory, directory, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(this.modelStatusFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return this.modelStatusCache;
                }
                VisionWorkerModelStatus status = ValidateWorkerModels(directory);
                this.modelStatusDirectory = directory ?? string.Empty;
                this.modelStatusFingerprint = fingerprint;
                this.modelStatusCache = status;
                return status;
            }
        }

        private static string BuildModelStatusFingerprint(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return directory ?? string.Empty;
            }
            try
            {
                List<string> entries = new List<string>();
                foreach (string path in Directory.GetFiles(directory, "*.onnx"))
                {
                    FileInfo info = new FileInfo(path);
                    entries.Add(info.Name + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks);
                }
                string manifestPath = Path.Combine(directory, "models.manifest.json");
                if (File.Exists(manifestPath))
                {
                    FileInfo manifestInfo = new FileInfo(manifestPath);
                    entries.Add(manifestInfo.Name + ":" + manifestInfo.Length + ":" + manifestInfo.LastWriteTimeUtc.Ticks);
                }
                entries.Sort(StringComparer.OrdinalIgnoreCase);
                return string.Join("|", entries);
            }
            catch (IOException)
            {
                return directory + ":io";
            }
            catch (UnauthorizedAccessException)
            {
                return directory + ":access";
            }
        }

        private static VisionWorkerModelStatus ValidateWorkerModels(string directory)
        {
            VisionWorkerModelStatus status = new VisionWorkerModelStatus
            {
                IsValid = false,
                ManifestVerified = false,
                Message = string.Empty
            };
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                status.Message = "OCR model directory was not found: " + directory;
                return status;
            }
            string[] modelFiles;
            try
            {
                modelFiles = Directory.GetFiles(directory, "*.onnx");
            }
            catch (IOException)
            {
                status.Message = "OCR model directory could not be read: " + directory;
                return status;
            }
            catch (UnauthorizedAccessException)
            {
                status.Message = "OCR model directory is not accessible: " + directory;
                return status;
            }
            if (modelFiles.Length < 3)
            {
                status.Message = "OCR model directory is incomplete; at least three ONNX files are required.";
                return status;
            }

            string manifestPath = Path.Combine(directory, "models.manifest.json");
            if (!File.Exists(manifestPath))
            {
                status.IsValid = true;
                status.Message = "OCR models found; manifest not present (file presence checked only).";
                return status;
            }

            try
            {
                JObject manifest = JObject.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
                JArray files = manifest["files"] as JArray;
                if (files == null || files.Count == 0)
                {
                    status.Message = "OCR model manifest is empty.";
                    return status;
                }
                foreach (JToken token in files)
                {
                    JObject entry = token as JObject;
                    string relativePath = entry == null ? string.Empty : entry.Value<string>("path");
                    string expectedHash = entry == null ? string.Empty : entry.Value<string>("sha256");
                    long expectedSize = entry == null ? -1L : entry.Value<long?>("size") ?? -1L;
                    if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(expectedHash))
                    {
                        status.Message = "OCR model manifest contains an invalid entry.";
                        return status;
                    }
                    string candidate = Path.GetFullPath(Path.Combine(directory, relativePath));
                    string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
                    {
                        status.Message = "OCR model manifest references a missing file: " + relativePath;
                        return status;
                    }
                    FileInfo info = new FileInfo(candidate);
                    if (info.Length != expectedSize || !string.Equals(ComputeSha256(candidate), expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        status.Message = "OCR model integrity check failed: " + relativePath;
                        return status;
                    }
                }
                status.IsValid = true;
                status.ManifestVerified = true;
                status.Message = "OCR models found and SHA-256 manifest verified.";
                return status;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is CryptographicException)
            {
                status.Message = "OCR model manifest could not be verified: " + ex.Message;
                return status;
            }
        }

        private static string ComputeSha256(string filePath)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private void ResetProcess()
        {
            lock (this.processSync)
            {
                Process current = this.workerProcess;
                this.workerProcess = null;
                this.workerExecutable = null;
                this.workerScript = null;
                if (current == null)
                {
                    return;
                }
                try
                {
                    if (!current.HasExited)
                    {
                        current.Kill();
                        current.WaitForExit(500);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                current.Dispose();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }
            lock (this.processSync)
            {
                ResetProcess();
            }
        }
    }
}
