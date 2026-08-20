using System;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionPythonWorkerHealth
    {
        public VisionPythonWorkerHealth(
            bool isReady,
            string summary,
            string pythonExecutable,
            string workerScript,
            string modelDirectory,
            bool manifestVerified,
            bool workerResponded,
            long warmupMilliseconds,
            string capabilities)
        {
            this.IsReady = isReady;
            this.Summary = summary ?? string.Empty;
            this.PythonExecutable = pythonExecutable ?? string.Empty;
            this.WorkerScript = workerScript ?? string.Empty;
            this.ModelDirectory = modelDirectory ?? string.Empty;
            this.ManifestVerified = manifestVerified;
            this.WorkerResponded = workerResponded;
            this.WarmupMilliseconds = warmupMilliseconds;
            this.Capabilities = capabilities ?? string.Empty;
        }

        public bool IsReady { get; private set; }

        public string Summary { get; private set; }

        public string PythonExecutable { get; private set; }

        public string WorkerScript { get; private set; }

        public string ModelDirectory { get; private set; }

        public bool ManifestVerified { get; private set; }

        public bool WorkerResponded { get; private set; }

        public long WarmupMilliseconds { get; private set; }

        public string Capabilities { get; private set; }
    }

    internal sealed class VisionWorkerModelStatus
    {
        public bool IsValid { get; set; }

        public bool ManifestVerified { get; set; }

        public string Message { get; set; }
    }
}
