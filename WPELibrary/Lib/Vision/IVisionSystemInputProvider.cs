using System;
using System.Drawing;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public interface IVisionSystemInputProvider
    {
        VisionAssistantActionResult ExecuteAirtestAction(
            IntPtr windowHandle,
            Point clientPoint,
            VisionActionDefinition definition,
            VisionOcrOptions ocrOptions,
            bool allowSystemInput,
            CancellationToken cancellationToken);
    }
}
