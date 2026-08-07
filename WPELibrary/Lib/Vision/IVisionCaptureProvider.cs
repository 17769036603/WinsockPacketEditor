using System;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public interface IVisionCaptureProvider
    {
        VisionCaptureResult Capture(
            IntPtr windowHandle,
            VisionRegion region,
            VisionCaptureSettings settings,
            VisionOcrOptions ocrOptions,
            CancellationToken cancellationToken);
    }
}
