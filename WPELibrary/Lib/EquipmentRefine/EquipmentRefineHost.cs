using System;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>宿主 facade：只编排依赖，不自动启动、导航页面、扫描背包或替换属性。</summary>
    public sealed class EquipmentRefineHost
    {
        private readonly EquipmentRefineRunner _runner;

        public EquipmentRefineHost(
            EquipmentRefinePreset preset,
            EquipmentRefineExecutor executor,
            Func<CancellationToken, Task<EquipmentRefineDetector.EquipmentInventory>> inventoryProvider,
            IRefineResponseAdapter responseAdapter,
            IRefineResponseSource responseSource,
            IEquipmentRefineResultPresenter presenter,
            IRefineStopReasonMapper stopReasonMapper,
            IRefineMemoryResultSource memoryResultSource = null)
        {
            this._runner = EquipmentRefineRunnerFactory.Create(
                preset, executor, inventoryProvider, responseAdapter, null, responseSource, presenter, stopReasonMapper,
                memoryResultSource);
        }

        public Task<EquipmentRefineStateMachine.ExecutionResult> StartAsync(CancellationToken token = default(CancellationToken))
        {
            return this._runner.StartAsync(token);
        }

        public void Cancel() { this._runner.Cancel(); }

        /// <summary>
        /// 等待用户在外部手动完成一次炼化，提取出包中的目标 ID 并绑定到 Runner。
        /// 不解析接收包，不自动启动炼化；成功后仍需调用 StartAsync。
        /// </summary>
        public async Task<EquipmentRefineManualCaptureBinding.CaptureResult> CaptureAndBindManualRefineAsync(
            IEquipmentRefineManualCaptureSource source,
            DateTime armedAtUtc,
            int timeoutMs,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            EquipmentRefineManualCaptureBinding.CaptureResult result =
                new EquipmentRefineManualCaptureBinding.CaptureResult();
            if (source == null)
            {
                result.Error = "manual_capture_source_unconfigured";
                return result;
            }

            EquipmentRefineManualCapturePacket packet;
            try
            {
                packet = await source.WaitForNextOutboundRefinePacketAsync(
                    armedAtUtc,
                    timeoutMs,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                result.Error = "manual_capture_cancelled";
                return result;
            }
            catch (TimeoutException)
            {
                result.Error = "manual_capture_timeout";
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "manual_capture_source_failed:" + ex.Message;
                return result;
            }

            EquipmentRefineManualCaptureBinding binding;
            string error;
            if (!EquipmentRefineManualCaptureBinding.TryCreate(packet, out binding, out error))
            {
                result.Error = error;
                return result;
            }
            if (!this._runner.TryBindCapturedEquipmentId(binding.EquipmentId, out error))
            {
                result.Error = error;
                return result;
            }

            result.Success = true;
            result.Binding = binding;
            return result;
        }

        public Task<EquipmentRefineStateMachine.ExecutionResult> Result { get { return this._runner.Result; } }
    }
}
