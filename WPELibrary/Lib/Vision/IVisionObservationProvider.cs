using System.Threading;

namespace WPELibrary.Lib.Vision
{
    public interface IVisionObservationProvider
    {
        VisionObservation Observe(
            VisionConditionDefinition condition,
            CancellationToken cancellationToken);
    }
}
