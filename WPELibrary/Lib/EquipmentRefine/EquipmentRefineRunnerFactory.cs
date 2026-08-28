using System;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>桌面宿主的最小接入契约；真实适配器未注入时 Runner 仍 fail-closed。</summary>
    public static class EquipmentRefineRunnerFactory
    {
        public static EquipmentRefineRunner Create(
            EquipmentRefinePreset preset,
            EquipmentRefineExecutor executor = null,
            Func<CancellationToken, Task<EquipmentRefineDetector.EquipmentInventory>> inventoryProvider = null,
            IRefineResponseAdapter responseAdapter = null,
            Func<CancellationToken, Task<byte[]>> responseFrameProvider = null,
            IRefineResponseSource responseSource = null,
            IEquipmentRefineResultPresenter presenter = null,
            IRefineStopReasonMapper stopReasonMapper = null,
            IRefineMemoryResultSource memoryResultSource = null)
        {
            return new EquipmentRefineRunner(
                preset,
                executor,
                inventoryProvider,
                responseAdapter,
                responseFrameProvider,
                responseSource,
                presenter,
                stopReasonMapper,
                memoryResultSource);
        }
    }
}
