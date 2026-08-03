using System.Drawing;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public interface IVisionTextRecognizer
    {
        VisionOcrResult Recognize(
            Bitmap source,
            VisionOcrOptions options,
            CancellationToken cancellationToken);
    }
}
