using System;
using System.Drawing;
using System.IO;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionAutoTextRecognizer : IVisionTextRecognizer, IDisposable
    {
        private readonly VisionOnnxTextRecognizer onnxRecognizer;
        private readonly VisionTesseractRecognizer tesseractRecognizer;

        public VisionAutoTextRecognizer(
            VisionOnnxTextRecognizer onnxRecognizer,
            VisionTesseractRecognizer tesseractRecognizer)
        {
            this.onnxRecognizer = onnxRecognizer ?? new VisionOnnxTextRecognizer();
            this.tesseractRecognizer = tesseractRecognizer ?? new VisionTesseractRecognizer("tesseract.exe");
            this.LastEngine = string.Empty;
        }

        public string LastEngine { get; private set; }

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
            this.LastEngine = string.Empty;
            if (options.Engine == VisionOcrEngine.Tesseract)
            {
                this.LastEngine = "Tesseract";
                return this.tesseractRecognizer.Recognize(source, options, cancellationToken);
            }

            VisionOcrResult onnxResult;
            try
            {
                onnxResult = this.onnxRecognizer.Recognize(
                    source,
                    options,
                    cancellationToken);
            }
            catch (Exception ex) when (IsOnnxRuntimeLoadFailure(ex))
            {
                // Optional ONNX dependencies can fail during JIT/type loading,
                // before VisionOnnxTextRecognizer can convert the error into a
                // result. Keep Auto OCR usable and let it fall back to Tesseract.
                onnxResult = VisionOcrResult.Unavailable(
                    "ONNX OCR runtime could not be loaded: " + ex.Message);
            }
            if (options.Engine == VisionOcrEngine.Onnx)
            {
                this.LastEngine = "ONNX";
                return onnxResult;
            }

            if (onnxResult.Cancelled)
            {
                return onnxResult;
            }
            if (onnxResult.Success &&
                onnxResult.Confidence >= options.OnnxRecognitionThreshold)
            {
                this.LastEngine = "ONNX";
                return onnxResult;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return VisionOcrResult.CancelledResult();
            }

            VisionOcrResult tesseractResult = this.tesseractRecognizer.Recognize(
                source,
                options,
                cancellationToken);
            if (tesseractResult.Cancelled)
            {
                return tesseractResult;
            }
            if (tesseractResult.Success || tesseractResult.Available)
            {
                this.LastEngine = "Tesseract";
                return tesseractResult;
            }

            if (onnxResult.Available)
            {
                if (onnxResult.Success)
                {
                    this.LastEngine = "ONNX-LowConfidence";
                    return VisionOcrResult.Failed(
                        "ONNX OCR confidence is below the automatic engine threshold.",
                        onnxResult.Text,
                        onnxResult.Confidence,
                        onnxResult.ExitCode);
                }
            }

            this.LastEngine = "Unavailable";
            return tesseractResult;
        }

        private static bool IsOnnxRuntimeLoadFailure(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is FileNotFoundException ||
                    current is FileLoadException ||
                    current is BadImageFormatException ||
                    current is DllNotFoundException ||
                    current is EntryPointNotFoundException ||
                    current is TypeLoadException ||
                    current is MissingMethodException)
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (this.onnxRecognizer != null)
            {
                this.onnxRecognizer.Dispose();
            }
            if (this.tesseractRecognizer != null)
            {
                this.tesseractRecognizer.Dispose();
            }
        }
    }
}
