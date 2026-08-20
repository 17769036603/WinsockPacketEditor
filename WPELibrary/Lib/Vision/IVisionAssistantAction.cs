using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public interface IVisionAssistantAction
    {
        VisionAssistantActionResult Execute(
            VisionAssistantActionContext context,
            CancellationToken cancellationToken);
    }
}
