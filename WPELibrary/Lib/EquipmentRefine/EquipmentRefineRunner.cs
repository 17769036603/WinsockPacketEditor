using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 可被桌面宿主复用的炼化入口。它不导航 UI、不扫描背包、不替换属性；
    /// 所有内存读取、发送、收包解码和展示均由宿主显式注入。
    /// </summary>
    public sealed class EquipmentRefineRunner
    {
        private readonly EquipmentRefineStateMachine _stateMachine;
        private CancellationTokenSource _cancellation;
        private Task<EquipmentRefineStateMachine.ExecutionResult> _running;

        public EquipmentRefineRunner(
            EquipmentRefinePreset preset,
            EquipmentRefineExecutor executor,
            Func<CancellationToken, Task<EquipmentRefineDetector.EquipmentInventory>> inventoryProvider,
            IRefineResponseAdapter responseAdapter,
            Func<CancellationToken, Task<byte[]>> responseFrameProvider,
            IRefineResponseSource responseSource,
            IEquipmentRefineResultPresenter presenter,
            IRefineStopReasonMapper stopReasonMapper,
            IRefineMemoryResultSource memoryResultSource = null)
        {
            if (preset == null) throw new ArgumentNullException("preset");
            string error;
            if (!preset.IsValid(out error)) throw new ArgumentException(error, "preset");

            this._stateMachine = new EquipmentRefineStateMachine(
                ToConfiguration(preset), executor, inventoryProvider);
            this._stateMachine.ConfigureResultPresenter(presenter);
            this._stateMachine.ConfigureStopReasonMapper(stopReasonMapper);
            if (responseAdapter != null && (responseSource != null || responseFrameProvider != null))
            {
                this._stateMachine.ConfigureResponseAdapter(responseAdapter, responseFrameProvider);
            }
            else
            {
                this._stateMachine.ConfigureResponseAdapter(
                    new UnconfiguredRefineResponseAdapter(),
                responseFrameProvider);
            }
            this._stateMachine.ConfigureResponseSource(responseSource);
            this._stateMachine.ConfigureMemoryResultSource(memoryResultSource);
        }

        public EquipmentRefineStateMachine.RefinementState State
        {
            get { return this._stateMachine.CurrentState; }
        }

        public Task<EquipmentRefineStateMachine.ExecutionResult> StartAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (this._running != null && !this._running.IsCompleted)
            {
                throw new InvalidOperationException("equipment_refine_already_running");
            }

            this._cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            this._running = this._stateMachine.RunAsync(this._cancellation.Token);
            return this._running;
        }

        public void Cancel()
        {
            if (this._cancellation != null) this._cancellation.Cancel();
            this._stateMachine.Abort();
        }

        /// <summary>
        /// 接收用户手动炼化捕获到的出包 ID。此方法只更新目标绑定，不启动运行器。
        /// </summary>
        public bool TryBindCapturedEquipmentId(string equipmentId, out string error)
        {
            return this._stateMachine.TryBindCapturedEquipmentId(equipmentId, out error);
        }

        public Task<EquipmentRefineStateMachine.ExecutionResult> Result
        {
            get { return this._running; }
        }

        private static EquipmentRefineStateMachine.RefineConfiguration ToConfiguration(EquipmentRefinePreset preset)
        {
            return new EquipmentRefineStateMachine.RefineConfiguration
            {
                PresetName = preset.Name,
                Target = CloneTarget(preset.Target),
                BagTargetMode = preset.BagTargetMode,
                BagTarget = CloneBagTarget(preset.BagTarget),
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute>(preset.VerifiedAttributeFields ??
                    new Dictionary<string, TargetAttribute>(), StringComparer.Ordinal),
                PacketTemplate = null,
                TypeCode = preset.TypeCode,
                OperationCode = preset.OperationCode,
                ActiveRules = new List<RefineRule>(preset.Rules ?? new List<RefineRule>()),
                RuleLogic = preset.RuleLogic,
                RequiredMatches = preset.RequiredMatches,
                ResponseCardMode = !preset.MemoryResultMode,
                MemoryResultMode = preset.MemoryResultMode,
                MaxAttempts = preset.MaxAttempts,
                IntervalMs = preset.IntervalMs,
                ReadTimeoutMs = preset.AttributeReadTimeoutMs,
                ResultConfirmTimeoutMs = preset.ResultConfirmTimeoutMs,
                SkipTargetReached = preset.SkipTargetReached,
                EnableLogging = preset.LogLevel > 0
            };
        }

        private static EquipmentRefineDetector.EquipmentTargetSelector CloneTarget(
            EquipmentRefineDetector.EquipmentTargetSelector source)
        {
            return new EquipmentRefineDetector.EquipmentTargetSelector
            {
                Slot = source == null ? string.Empty : source.Slot,
                MemberIdentity = source == null ? string.Empty : source.MemberIdentity,
                EquipmentId = source == null ? string.Empty : source.EquipmentId,
                EquipmentName = source == null ? string.Empty : source.EquipmentName
            };
        }

        private static EquipmentRefineDetector.BagTargetSelector CloneBagTarget(
            EquipmentRefineDetector.BagTargetSelector source)
        {
            return new EquipmentRefineDetector.BagTargetSelector
            {
                Slot = source == null ? string.Empty : source.Slot,
                MemberIdentity = source == null ? string.Empty : source.MemberIdentity,
                ItemId = source == null ? string.Empty : source.ItemId,
                ItemTypeId = source == null ? string.Empty : source.ItemTypeId,
                XianqiTier = source == null ? (int?)null : source.XianqiTier,
                XianqiTierLabel = source == null ? string.Empty : source.XianqiTierLabel,
                Name = source == null ? string.Empty : source.Name,
                RawFieldsSummary = source == null ? string.Empty : source.RawFieldsSummary
            };
        }
    }
}
