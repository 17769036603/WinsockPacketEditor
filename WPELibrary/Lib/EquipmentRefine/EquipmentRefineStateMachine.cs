using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 装备炼化状态机。
    /// 只依赖只读装备快照和显式注入的 packet sender，默认不会发送真实封包。
    /// </summary>
    public class EquipmentRefineStateMachine
    {
        public enum RefinementState
        {
            Idle = 0,
            FindingEquipment,
            ReadingAttributes,
            CheckingRules,
            PerformingRefinement,
            WaitingForResult,
            VerifyingResult,
            ContinueRefining,
            Completed,
            Stopped,
            Failed,
            EquipmentNotFound,
            AttributeReadFailed,
            IdentityChanged
        }

        public enum StopReason
        {
            TargetReached,
            UserStopped,
            MaxAttemptsReached,
            EquipmentNotFound,
            EquipmentIdentityChanged,
            AttributeReadFailed,
            RefineResultTimeout,
            GameProcessLost,
            GameStateInvalid,
            AuthorizationRequired,
            ProtocolUnverified,
            InternalError
        }

        public class RefineConfiguration
        {
            public string PresetName { get; set; } = "默认炼化";
            public string StateFilePath { get; set; } = string.Empty;
            public EquipmentRefineDetector.EquipmentTargetSelector Target { get; set; }
            public bool BagTargetMode { get; set; }
            public EquipmentRefineDetector.BagTargetSelector BagTarget { get; set; }
            public IDictionary<string, TargetAttribute> VerifiedAttributeFields { get; set; } =
                new Dictionary<string, TargetAttribute>(StringComparer.Ordinal);
            public EquipmentRefineExecutor.RefinePacketTemplate PacketTemplate { get; set; }
            public int TypeCode { get; set; } = -1;
            public int OperationCode { get; set; } = -1;
            public List<RefineRule> ActiveRules { get; set; } = new List<RefineRule>();
            public RuleLogic RuleLogic { get; set; } = RuleLogic.All;
            public int RequiredMatches { get; set; } = 1;
            public bool ResponseCardMode { get; set; }
            public bool MemoryResultMode { get; set; }
            public int MaxAttempts { get; set; } = 20;
            public int IntervalMs { get; set; } = 1500;
            public int ReadTimeoutMs { get; set; } = 5000;
            public int ResultConfirmTimeoutMs { get; set; } = 3000;
            public bool SkipTargetReached { get; set; } = true;
            public bool EnableLogging { get; set; } = true;

            public bool IsValid(out string error)
            {
                error = string.Empty;
                if (!this.BagTargetMode && (this.Target == null ||
                    string.IsNullOrWhiteSpace(this.Target.Slot)))
                {
                    error = "worn_target_slot_missing";
                    return false;
                }
                if (this.BagTargetMode && (this.BagTarget == null || !this.BagTarget.IsValid))
                {
                    error = "bag_target_identity_missing";
                    return false;
                }
                if (this.ActiveRules == null || !this.ActiveRules.Any(rule => rule != null && rule.Enabled))
                {
                    error = "refine_rules_missing";
                    return false;
                }
                List<RefineRule> activeRules = this.ActiveRules.Where(rule => rule != null && rule.Enabled).ToList();
                if (this.ResponseCardMode && this.MemoryResultMode)
                {
                    error = "response_mode_conflict";
                    return false;
                }
                if (this.RequiredMatches < 1 || this.RequiredMatches > activeRules.Count)
                {
                    error = "required_matches_out_of_range";
                    return false;
                }
                foreach (RefineRule rule in this.ActiveRules)
                {
                    if (rule == null)
                    {
                        error = "invalid_refine_rule";
                        return false;
                    }
                    string ruleError;
                    if (!rule.IsValid(out ruleError))
                    {
                        error = ruleError;
                        return false;
                    }
                }
                if (activeRules.GroupBy(rule => new { rule.Attribute, rule.Operator, rule.TargetValue })
                    .Any(group => group.Count() > 1))
                {
                    error = "duplicate_refine_rule";
                    return false;
                }
                if (this.MaxAttempts < 0 || this.IntervalMs < 0 || this.ReadTimeoutMs <= 0 || this.ResultConfirmTimeoutMs <= 0)
                {
                    error = "timing_or_attempts_invalid";
                    return false;
                }
                return true;
            }
        }

        public class ExecutionResult
        {
            public RefinementState FinalState { get; set; } = RefinementState.Idle;
            public StopReason StopReason { get; set; } = StopReason.InternalError;
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public int AttemptCount { get; set; }
            public EquipmentAttributesSnapshot FinalAttributes { get; set; }
            public string FinalAttributeHash { get; set; } = string.Empty;
            public RefineResponseCard MatchedResponseCard { get; set; }
            public List<int> MatchedRuleIndexes { get; set; } = new List<int>();
            public int MatchedCount { get; set; }
            public int RequiredMatches { get; set; }
            public int TargetCount { get; set; }
            public bool Replaced { get; set; }
            public DateTime StartedAt { get; set; } = DateTime.MinValue;
            public DateTime CompletedAt { get; set; } = DateTime.MinValue;
            public List<StepRecord> Steps { get; set; } = new List<StepRecord>();
        }

        public class StepRecord
        {
            public RefinementState State { get; set; }
            public string Description { get; set; } = string.Empty;
            public DateTime StartedAt { get; set; }
            public DateTime CompletedAt { get; set; }
            public bool Succeeded { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
        }

        private RefineConfiguration _configuration;
        private EquipmentRefineExecutor _executor;
        private EquipmentRefineAttributeReader _attributeReader;
        private Func<CancellationToken, Task<EquipmentRefineDetector.EquipmentInventory>> _inventoryProvider;
        private CancellationTokenSource _runCancellation;
        private IRefineResponseAdapter _responseAdapter;
        private Func<CancellationToken, Task<byte[]>> _responseFrameProvider;
        private IRefineResponseSource _responseSource;
        private IRefineMemoryResultSource _memoryResultSource;
        private IEquipmentRefineResultPresenter _resultPresenter;
        private IRefineStopReasonMapper _stopReasonMapper;
        private EquipmentRefineDetector.EquipmentSlot _targetSlot;
        private EquipmentAttributesSnapshot _currentSnapshot;
        private int _attemptCount;
        private RefinementState _currentState = RefinementState.Idle;
        private readonly List<StepRecord> _stepHistory = new List<StepRecord>();

        public EquipmentRefineStateMachine()
        {
        }

        public EquipmentRefineStateMachine(
            RefineConfiguration configuration,
            EquipmentRefineExecutor executor = null,
            Func<CancellationToken, Task<EquipmentRefineDetector.EquipmentInventory>> inventoryProvider = null)
        {
            this._executor = executor;
            this._inventoryProvider = inventoryProvider;
            this.Initialize(configuration);
        }

        /// <summary>
        /// Optional response path. The adapter/provider must be supplied by the
        /// real host after protocol evidence is verified; null remains fail-closed.
        /// </summary>
        public void ConfigureResponseAdapter(
            IRefineResponseAdapter responseAdapter,
            Func<CancellationToken, Task<byte[]>> responseFrameProvider)
        {
            this._responseAdapter = responseAdapter;
            this._responseFrameProvider = responseFrameProvider;
        }

        public void ConfigureResponseSource(IRefineResponseSource responseSource)
        {
            this._responseSource = responseSource;
        }

        public void ConfigureMemoryResultSource(IRefineMemoryResultSource memoryResultSource)
        {
            this._memoryResultSource = memoryResultSource;
        }

        public void ConfigureResultPresenter(IEquipmentRefineResultPresenter presenter)
        {
            this._resultPresenter = presenter;
        }

        public void ConfigureStopReasonMapper(IRefineStopReasonMapper mapper)
        {
            this._stopReasonMapper = mapper;
        }

        /// <summary>
        /// 将用户手动炼化出包中确认的 ID 绑定到当前目标。
        /// 只能在运行前调用；不会改变 slot/memberIdentity，也不会发送封包。
        /// </summary>
        public bool TryBindCapturedEquipmentId(string equipmentId, out string error)
        {
            error = string.Empty;
            if (this._configuration == null || string.IsNullOrWhiteSpace(equipmentId))
            {
                error = "manual_capture_binding_missing";
                return false;
            }
            if (this._currentState != RefinementState.Idle)
            {
                error = "manual_capture_binding_requires_idle";
                return false;
            }

            if (this._configuration.BagTargetMode)
            {
                if (this._configuration.BagTarget == null || !this._configuration.BagTarget.IsValid)
                {
                    error = "bag_target_identity_missing";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(this._configuration.BagTarget.ItemId) &&
                    !string.Equals(this._configuration.BagTarget.ItemId, equipmentId, StringComparison.Ordinal))
                {
                    error = "manual_capture_equipment_id_mismatch";
                    return false;
                }
                this._configuration.BagTarget.ItemId = equipmentId;
                return true;
            }

            if (this._configuration.Target == null ||
                string.IsNullOrWhiteSpace(this._configuration.Target.Slot) ||
                string.IsNullOrWhiteSpace(this._configuration.Target.MemberIdentity))
            {
                error = "worn_target_identity_triplet_missing";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(this._configuration.Target.EquipmentId) &&
                !string.Equals(this._configuration.Target.EquipmentId, equipmentId, StringComparison.Ordinal))
            {
                error = "manual_capture_equipment_id_mismatch";
                return false;
            }
            this._configuration.Target.EquipmentId = equipmentId;
            return true;
        }

        public RefinementState CurrentState { get { return this._currentState; } }
        public int AttemptCount { get { return this._attemptCount; } }
        public IReadOnlyList<StepRecord> StepHistory { get { return this._stepHistory.AsReadOnly(); } }

        public void Initialize(RefineConfiguration configuration)
        {
            this._configuration = configuration ?? new RefineConfiguration();
            this._attributeReader = new EquipmentRefineAttributeReader(this._configuration.VerifiedAttributeFields);
            if (this._executor == null)
            {
                this._executor = new EquipmentRefineExecutor(this._configuration.PacketTemplate);
            }
            else if (this._configuration.PacketTemplate != null)
            {
                this._executor.Template = this._configuration.PacketTemplate;
            }

            this._attemptCount = 0;
            this._currentState = RefinementState.Idle;
            this._currentSnapshot = null;
            this._targetSlot = null;
            this._stepHistory.Clear();
        }

        public Task<ExecutionResult> RunAsync(CancellationTokenSource cancellationTokenSource)
        {
            CancellationToken token = cancellationTokenSource == null
                ? CancellationToken.None
                : cancellationTokenSource.Token;
            this._runCancellation = cancellationTokenSource;
            return this.RunAsync(token);
        }

        public async Task<ExecutionResult> RunAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (this._configuration == null) this.Initialize(new RefineConfiguration());

            ExecutionResult result = new ExecutionResult
            {
                StartedAt = DateTime.UtcNow,
                FinalState = RefinementState.Idle
            };

            string configError;
            if (!this._configuration.IsValid(out configError))
            {
                return Finish(result, RefinementState.Failed, StopReason.GameStateInvalid, configError, null);
            }

            try
            {
                this._runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                CancellationToken token = this._runCancellation.Token;

                EquipmentRefineDetector.EquipmentInventory inventory = await ReadInventoryAsync(token);
                if (!IsInventoryUsable(inventory))
                {
                    return Finish(result, RefinementState.Failed, StopReason.GameStateInvalid, "inventory_unavailable", null);
                }

                this.SetState(RefinementState.FindingEquipment, "寻找目标装备");
                this._targetSlot = FindTarget(inventory);
                if (this._targetSlot == null)
                {
                    return Finish(result, RefinementState.EquipmentNotFound, StopReason.EquipmentNotFound, "target_equipment_not_found_or_ambiguous", null);
                }
                string identityBindingError;
                if (!BindWornTargetIdentity(out identityBindingError))
                {
                    return Finish(result, RefinementState.Failed, StopReason.GameStateInvalid,
                        identityBindingError, null);
                }

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (this._configuration.MaxAttempts > 0 && this._attemptCount >= this._configuration.MaxAttempts)
                    {
                        return Finish(result, RefinementState.Stopped, StopReason.MaxAttemptsReached, "max_attempts_reached", this._currentSnapshot);
                    }

                    this.SetState(RefinementState.ReadingAttributes, "读取当前属性");
                    EquipmentAttributesSnapshot snapshot = await ReadTargetSnapshotAsync(inventory, token);
                    if (snapshot == null || !snapshot.IsValid)
                    {
                        return Finish(result, RefinementState.AttributeReadFailed, StopReason.AttributeReadFailed,
                            snapshot == null ? "attribute_snapshot_missing" : snapshot.ReadError,
                            this._currentSnapshot);
                    }

                    if (!IsSameTarget(snapshot, this._targetSlot))
                    {
                        return Finish(result, RefinementState.IdentityChanged, StopReason.EquipmentIdentityChanged, "target_identity_changed", snapshot);
                    }

                    this._currentSnapshot = snapshot;
                    this.SetState(RefinementState.CheckingRules, "检查停止规则");
                    // Legacy attribute-snapshot mode may stop before sending.
                    // Response-card and memory-result modes must decide only
                    // from the post-send result of the current round; their
                    // pre-send snapshot contains no authoritative card result.
                    bool reached = !this._configuration.ResponseCardMode &&
                        !this._configuration.MemoryResultMode &&
                        CheckRules(snapshot);
                    if (reached && this._configuration.SkipTargetReached)
                    {
                        return Finish(result, RefinementState.Completed, StopReason.TargetReached,
                            "target_reached_before_send", snapshot, true);
                    }

                    if (this._configuration.ResponseCardMode &&
                        (this._responseAdapter == null ||
                         (this._responseSource == null && this._responseFrameProvider == null)))
                    {
                        return Finish(result, RefinementState.Stopped, StopReason.ProtocolUnverified,
                            "response_card_adapter_unconfigured", snapshot);
                    }
                    if (this._configuration.MemoryResultMode && this._memoryResultSource == null)
                    {
                        return Finish(result, RefinementState.Stopped, StopReason.ProtocolUnverified,
                            "memory_result_source_unconfigured", snapshot);
                    }

                    this.SetState(RefinementState.PerformingRefinement, "执行炼化封包");
                    int requestSlotIndex;
                    if (!EquipmentRefineDetector.TryParseSlotIndex(this._targetSlot.Slot, out requestSlotIndex))
                    {
                        requestSlotIndex = this._configuration.BagTargetMode &&
                            this._configuration.BagTarget != null
                            ? this._configuration.BagTarget.RequestSlotIndex
                            : -1;
                    }
                    bool packetNeedsSlotIndex = this._configuration.PacketTemplate == null ||
                        this._configuration.PacketTemplate.Fields == null ||
                        this._configuration.PacketTemplate.Fields.Any(field => field != null &&
                            field.Source == EquipmentRefineExecutor.PacketFieldSource.SlotIndex);
                    if ((!this._configuration.BagTargetMode && requestSlotIndex < 0) ||
                        (this._configuration.BagTargetMode && requestSlotIndex < 0 && packetNeedsSlotIndex) ||
                        string.IsNullOrWhiteSpace(this._targetSlot.MemberIdentity) ||
                        (!this._configuration.BagTargetMode && string.IsNullOrWhiteSpace(this._targetSlot.EquipmentId)))
                    {
                        return Finish(result, RefinementState.Stopped, StopReason.GameStateInvalid,
                            this._configuration.BagTargetMode
                                ? "bag_target_request_identity_incomplete"
                                : "worn_target_request_identity_incomplete", snapshot);
                    }
                    EquipmentRefineExecutor.RefineRequest request = BuildRequest(this._targetSlot);
                    // This is the pre-send inventory snapshot sequence. It is
                    // captured before the send call and is the only baseline
                    // accepted by the response-card and typed memory paths.
                    long sendBeforeSequence = snapshot.Sequence;
                    RefineMemoryBaseline memoryBaseline = this._configuration.MemoryResultMode
                        ? CreateMemoryBaseline(inventory, this._targetSlot)
                        : null;
                    if (this._configuration.MemoryResultMode &&
                        (memoryBaseline == null || !memoryBaseline.IsValid))
                    {
                        return Finish(result, RefinementState.Stopped, StopReason.ProtocolUnverified,
                            "memory_result_baseline_invalid", snapshot);
                    }
                    EquipmentRefineExecutor.RefineSendResult sendResult = await this._executor.SendRefinePacketAsync(request, -1, token);
                    if (sendResult == EquipmentRefineExecutor.RefineSendResult.AuthorizationRequired)
                    {
                        return Finish(result, RefinementState.Stopped, StopReason.AuthorizationRequired, "packet_sender_not_authorized", snapshot);
                    }
                    if (sendResult == EquipmentRefineExecutor.RefineSendResult.TemplateNotFound)
                    {
                        return Finish(result, RefinementState.Stopped, StopReason.ProtocolUnverified, "refine_packet_template_unverified", snapshot);
                    }
                    if (sendResult != EquipmentRefineExecutor.RefineSendResult.Accepted)
                    {
                        return Finish(result, RefinementState.Failed, StopReason.InternalError, "refine_packet_send_failed", snapshot);
                    }

                    this._attemptCount++;

                    if (this._configuration.ResponseCardMode && this._responseAdapter != null &&
                        (this._responseSource != null || this._responseFrameProvider != null))
                    {
                        byte[] responseFrame = await ReceiveResponseFrameWithTimeoutAsync(
                            sendBeforeSequence,
                            token);
                        RefineResponseEnvelope response;
                        string decodeError = string.Empty;
                        if (responseFrame == null || !this._responseAdapter.TryDecode(responseFrame, out response, out decodeError))
                        {
                            return Finish(result, RefinementState.Stopped, StopReason.ProtocolUnverified,
                                string.IsNullOrWhiteSpace(decodeError) ? "refine_response_decode_failed" : decodeError,
                                snapshot);
                        }

                        RefineResponseResult responseResult = RefineResponseEvaluator.Evaluate(
                            response,
                            this._configuration.ActiveRules,
                            this._configuration.RequiredMatches,
                            this._targetSlot.MemberIdentity,
                            sendBeforeSequence);
                        if (responseResult.Success)
                        {
                            result.MatchedResponseCard = responseResult.MatchedCard;
                            result.MatchedRuleIndexes = responseResult.MatchedRuleIndexes.ToList();
                            result.MatchedCount = responseResult.MatchedCount;
                            result.RequiredMatches = responseResult.RequiredMatches;
                            result.TargetCount = responseResult.TargetCount;
                            result.Replaced = responseResult.Replaced;
                            return Finish(result, RefinementState.Completed, StopReason.TargetReached,
                                "target_reached_in_response_card", snapshot, true);
                        }
                        if (!responseResult.ContinueAllowed)
                        {
                            return Finish(result, RefinementState.Stopped, StopReason.ProtocolUnverified,
                                responseResult.Reason, snapshot);
                        }

                        // Response-card mode owns the post-send decision. Do not
                        // fall through to the legacy formal-current snapshot path.
                        this.SetState(RefinementState.ContinueRefining, "响应卡未达到目标，继续炼化");
                        if (this._configuration.IntervalMs > 0)
                        {
                            await Task.Delay(this._configuration.IntervalMs, token);
                        }

                        // Refresh the resident target before another response-card
                        // round so an equipment replacement cannot be hidden by
                        // the previous inventory object.
                        inventory = await ReadInventoryAsync(token);
                        if (!IsInventoryUsable(inventory))
                        {
                            return Finish(result, RefinementState.Failed, StopReason.GameStateInvalid,
                                "inventory_unavailable_after_response", snapshot);
                        }
                        EquipmentRefineDetector.EquipmentSlot refreshedTarget =
                            FindTarget(inventory);
                        if (refreshedTarget == null ||
                            EquipmentRefineDetector.EquipmentIdentityChanged(refreshedTarget, this._targetSlot))
                        {
                            return Finish(result, RefinementState.IdentityChanged,
                                StopReason.EquipmentIdentityChanged,
                                "target_identity_changed_after_response",
                                snapshot);
                        }
                        this._targetSlot = refreshedTarget;
                        continue;
                    }

                    if (this._configuration.MemoryResultMode)
                    {
                        RefineMemoryResultSnapshot memoryResponse =
                            await ReceiveMemoryResultWithTimeoutAsync(
                                memoryBaseline,
                                this._configuration.ResultConfirmTimeoutMs,
                                token);
                        string memoryError;
                        if (!TryValidateMemoryResult(memoryResponse, memoryBaseline, out memoryError))
                        {
                            return Finish(result, RefinementState.Stopped, StopReason.ProtocolUnverified,
                                memoryError, snapshot);
                        }

                        RefineResponseEnvelope response = new RefineResponseEnvelope
                        {
                            Sequence = memoryResponse.Sequence,
                            EquipmentIdentity = memoryResponse.EquipmentIdentity,
                            ErrorCode = memoryResponse.ErrorCode,
                            IsStable = memoryResponse.IsStable
                        };
                        response.Cards.AddRange(memoryResponse.Cards);
                        RefineResponseResult responseResult = RefineResponseEvaluator.Evaluate(
                            response,
                            this._configuration.ActiveRules,
                            this._configuration.RequiredMatches,
                            this._targetSlot.MemberIdentity,
                            memoryBaseline.Sequence);
                        if (responseResult.Success)
                        {
                            result.MatchedResponseCard = responseResult.MatchedCard;
                            result.MatchedRuleIndexes = responseResult.MatchedRuleIndexes.ToList();
                            result.MatchedCount = responseResult.MatchedCount;
                            result.RequiredMatches = responseResult.RequiredMatches;
                            result.TargetCount = responseResult.TargetCount;
                            result.Replaced = false;
                            return Finish(result, RefinementState.Completed, StopReason.TargetReached,
                                "target_reached_in_memory_result", snapshot, true);
                        }
                        if (!responseResult.ContinueAllowed)
                        {
                            return Finish(result, RefinementState.Stopped, StopReason.ProtocolUnverified,
                                responseResult.Reason, snapshot);
                        }

                        this.SetState(RefinementState.ContinueRefining, "内存卡片未达到目标，继续炼化");
                        if (this._configuration.IntervalMs > 0)
                        {
                            await Task.Delay(this._configuration.IntervalMs, token);
                        }
                        inventory = await ReadInventoryAsync(token);
                        if (!IsInventoryUsable(inventory))
                        {
                            return Finish(result, RefinementState.Failed, StopReason.GameStateInvalid,
                                "inventory_unavailable_after_memory_result", snapshot);
                        }
                        EquipmentRefineDetector.EquipmentSlot refreshedMemoryTarget =
                            FindTarget(inventory);
                        if (refreshedMemoryTarget == null ||
                            EquipmentRefineDetector.EquipmentIdentityChanged(refreshedMemoryTarget, this._targetSlot))
                        {
                            return Finish(result, RefinementState.IdentityChanged,
                                StopReason.EquipmentIdentityChanged,
                                "target_identity_changed_after_memory_result",
                                snapshot);
                        }
                        this._targetSlot = refreshedMemoryTarget;
                        continue;
                    }

                    this.SetState(RefinementState.WaitingForResult, "等待 resident 结果变化");
                    EquipmentAttributesSnapshot changedSnapshot = await WaitForChangedSnapshotAsync(
                        snapshot,
                        token);
                    if (changedSnapshot == null)
                    {
                        return Finish(result, RefinementState.Stopped, StopReason.RefineResultTimeout, "refine_result_timeout", snapshot);
                    }

                    this.SetState(RefinementState.VerifyingResult, "验证炼化结果");
                    if (!IsSameTarget(changedSnapshot, this._targetSlot))
                    {
                        return Finish(result, RefinementState.IdentityChanged, StopReason.EquipmentIdentityChanged, "target_identity_changed_after_send", changedSnapshot);
                    }

                    this._currentSnapshot = changedSnapshot;
                    bool reachedAfterRefine = CheckRules(changedSnapshot);
                    this.SetState(
                        reachedAfterRefine
                            ? RefinementState.Completed
                            : RefinementState.ContinueRefining,
                        reachedAfterRefine ? "达到目标规则" : "未达到目标，继续炼化");
                    if (reachedAfterRefine && this._configuration.SkipTargetReached)
                    {
                        return Finish(result, RefinementState.Completed, StopReason.TargetReached, "target_reached_after_refine", changedSnapshot, true);
                    }

                    if (this._configuration.IntervalMs > 0)
                    {
                        await Task.Delay(this._configuration.IntervalMs, token);
                    }

                    inventory = await ReadInventoryAsync(token);
                    if (!IsInventoryUsable(inventory))
                    {
                        return Finish(result, RefinementState.Failed, StopReason.GameStateInvalid, "inventory_unavailable_after_refine", changedSnapshot);
                    }

                    EquipmentRefineDetector.EquipmentSlot currentTarget =
                        FindTarget(inventory);
                    if (currentTarget == null || EquipmentRefineDetector.EquipmentIdentityChanged(currentTarget, this._targetSlot))
                    {
                        return Finish(result, RefinementState.IdentityChanged, StopReason.EquipmentIdentityChanged, "target_identity_changed_after_refresh", changedSnapshot);
                    }
                    this._targetSlot = currentTarget;
                }
            }
            catch (OperationCanceledException)
            {
                return Finish(result, RefinementState.Stopped, StopReason.UserStopped, "user_stopped", this._currentSnapshot);
            }
            catch (TimeoutException)
            {
                return Finish(result, RefinementState.Stopped, StopReason.RefineResultTimeout,
                    "refine_result_timeout", this._currentSnapshot);
            }
            catch (Exception ex)
            {
                return Finish(result, RefinementState.Failed, StopReason.InternalError, ex.Message, this._currentSnapshot);
            }
        }

        public void Abort()
        {
            if (this._runCancellation != null) this._runCancellation.Cancel();
            this.SetState(RefinementState.Stopped, "用户停止");
        }

        private async Task<EquipmentRefineDetector.EquipmentInventory> ReadInventoryAsync(CancellationToken token)
        {
            if (this._inventoryProvider != null)
            {
                return await this._inventoryProvider(token);
            }

            string path = this._configuration.StateFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Environment.GetEnvironmentVariable("WPE_EQUIPMENT_STATE_FILE");
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return this._configuration.BagTargetMode
                ? await EquipmentRefineDetector.ReadBagInventoryAsync(path, token)
                : await EquipmentRefineDetector.ReadInventoryAsync(path, token);
        }

        private async Task<RefineMemoryResultSnapshot> ReceiveMemoryResultWithTimeoutAsync(
            RefineMemoryBaseline baseline,
            int timeoutMs,
            CancellationToken token)
        {
            using (CancellationTokenSource receiveCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task<RefineMemoryResultSnapshot> receiveTask =
                    this._memoryResultSource.ReceiveAfterSendAsync(
                        baseline, timeoutMs, receiveCancellation.Token);
                Task timeoutTask = Task.Delay(timeoutMs, receiveCancellation.Token);
                Task completed = await Task.WhenAny(receiveTask, timeoutTask);
                if (completed != receiveTask)
                {
                    receiveCancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("memory_result_timeout");
                }
                RefineMemoryResultSnapshot result = await receiveTask;
                receiveCancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return result;
            }
        }

        private async Task<byte[]> ReceiveResponseFrameWithTimeoutAsync(
            long baselineSequence,
            CancellationToken token)
        {
            using (CancellationTokenSource receiveCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task<byte[]> receiveTask = this._responseSource != null
                    ? this._responseSource.ReceiveAfterSendAsync(
                        baselineSequence,
                        this._configuration.ResultConfirmTimeoutMs,
                        receiveCancellation.Token)
                    : this._responseFrameProvider(receiveCancellation.Token);
                Task timeoutTask = Task.Delay(
                    this._configuration.ResultConfirmTimeoutMs,
                    receiveCancellation.Token);
                Task completed = await Task.WhenAny(receiveTask, timeoutTask);
                if (completed != receiveTask)
                {
                    receiveCancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("refine_response_timeout");
                }
                byte[] frame = await receiveTask;
                receiveCancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return frame;
            }
        }

        private async Task<EquipmentAttributesSnapshot> ReadTargetSnapshotAsync(
            EquipmentRefineDetector.EquipmentInventory inventory,
            CancellationToken token)
        {
            EquipmentRefineDetector.EquipmentSlot slot = FindTarget(inventory);
            if (slot == null) return null;
            this._targetSlot = slot;

            if (this._configuration.MemoryResultMode)
            {
                return new EquipmentAttributesSnapshot
                {
                    MemberIdentity = slot.MemberIdentity,
                    Slot = slot.Slot,
                    EquipmentId = slot.EquipmentId,
                    ItemId = slot.ItemId,
                    ItemTypeId = slot.ItemTypeId,
                    BaseAttributeHash = slot.BaseAttributeHash,
                    SnapshotId = inventory.SnapshotId,
                    StreamSessionId = inventory.StreamSessionId,
                    Sequence = inventory.Sequence,
                    CandidateOnly = false,
                    IsValid = true,
                    ReadTime = DateTime.UtcNow
                };
            }

            EquipmentAttributesSnapshot snapshot = await this._attributeReader.ReadAttributesAsync(
                slot,
                slot.BaseAttributeHash,
                token);
            snapshot.SnapshotId = inventory.SnapshotId;
            snapshot.StreamSessionId = inventory.StreamSessionId;
            snapshot.Sequence = inventory.Sequence;
            return snapshot;
        }

        private RefineMemoryBaseline CreateMemoryBaseline(
            EquipmentRefineDetector.EquipmentInventory inventory,
            EquipmentRefineDetector.EquipmentSlot slot)
        {
            if (inventory == null || slot == null || inventory.ProcessIdentity == null)
            {
                return null;
            }

            return new RefineMemoryBaseline
            {
                ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity
                {
                    pid = inventory.ProcessIdentity.pid,
                    startTicks = inventory.ProcessIdentity.startTicks,
                    exe = inventory.ProcessIdentity.exe
                },
                ContainerIdentity = inventory.ContainerIdentity,
                StreamSessionId = inventory.StreamSessionId,
                SnapshotId = inventory.SnapshotId,
                Sequence = inventory.Sequence,
                EquipmentIdentity = slot.MemberIdentity,
                Slot = slot.Slot,
                EquipmentId = slot.EquipmentId,
                TargetMode = this._configuration.BagTargetMode ? EquipmentTargetMode.Bag : EquipmentTargetMode.Worn,
                ItemId = slot.ItemId,
                ItemTypeId = slot.ItemTypeId
            };
        }

        private static bool TryValidateMemoryResult(
            RefineMemoryResultSnapshot response,
            RefineMemoryBaseline baseline,
            out string error)
        {
            error = string.Empty;
            if (response == null || baseline == null || !baseline.IsValid)
            {
                error = "memory_result_missing";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            {
                error = response.ErrorCode;
                return false;
            }
            if (!response.IsStable)
            {
                error = "memory_result_unstable";
                return false;
            }
            if (response.ProcessIdentity == null || !response.ProcessIdentity.IsValid ||
                response.ProcessIdentity.pid != baseline.ProcessIdentity.pid ||
                response.ProcessIdentity.startTicks != baseline.ProcessIdentity.startTicks)
            {
                error = "memory_process_identity_changed";
                return false;
            }
            if (!string.Equals(response.ContainerIdentity, baseline.ContainerIdentity, StringComparison.Ordinal))
            {
                error = "memory_container_identity_changed";
                return false;
            }
            if (!string.Equals(response.StreamSessionId, baseline.StreamSessionId, StringComparison.Ordinal))
            {
                error = "memory_stream_session_changed";
                return false;
            }
            if (response.Sequence <= baseline.Sequence ||
                string.IsNullOrWhiteSpace(response.SnapshotId) ||
                string.Equals(response.SnapshotId, baseline.SnapshotId, StringComparison.Ordinal))
            {
                error = "memory_snapshot_not_refreshed";
                return false;
            }
            if (!string.Equals(response.EquipmentIdentity, baseline.EquipmentIdentity, StringComparison.Ordinal))
            {
                error = "memory_equipment_identity_changed";
                return false;
            }
            bool targetChanged = !string.Equals(response.Slot, baseline.Slot, StringComparison.Ordinal);
            if (baseline.TargetMode == EquipmentTargetMode.Worn &&
                !string.Equals(response.EquipmentId, baseline.EquipmentId, StringComparison.Ordinal))
            {
                targetChanged = true;
            }
            if (baseline.TargetMode == EquipmentTargetMode.Bag &&
                ((!string.IsNullOrWhiteSpace(baseline.ItemId) && !string.Equals(response.ItemId, baseline.ItemId, StringComparison.Ordinal)) ||
                 (!string.IsNullOrWhiteSpace(baseline.ItemTypeId) && !string.Equals(response.ItemTypeId, baseline.ItemTypeId, StringComparison.Ordinal))))
            {
                targetChanged = true;
            }
            if (targetChanged)
            {
                error = "memory_equipment_target_changed";
                return false;
            }
            if (response.Cards == null || response.Cards.Count != 20)
            {
                error = "memory_response_cards_incomplete";
                return false;
            }
            return true;
        }

        private async Task<EquipmentAttributesSnapshot> WaitForChangedSnapshotAsync(
            EquipmentAttributesSnapshot previous,
            CancellationToken token)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < this._configuration.ResultConfirmTimeoutMs)
            {
                token.ThrowIfCancellationRequested();
                EquipmentRefineDetector.EquipmentInventory inventory = await ReadInventoryAsync(token);
                if (IsInventoryUsable(inventory))
                {
                    EquipmentRefineDetector.EquipmentSlot slot = FindTarget(inventory);
                    if (slot == null) return null;
                    if (EquipmentRefineDetector.EquipmentIdentityChanged(slot, this._targetSlot)) return null;

                    EquipmentAttributesSnapshot current = await this._attributeReader.ReadAttributesAsync(
                        slot,
                        this._targetSlot.BaseAttributeHash,
                        token);
                    current.SnapshotId = inventory.SnapshotId;
                    current.StreamSessionId = inventory.StreamSessionId;
                    current.Sequence = inventory.Sequence;
                    if (current.IsValid &&
                        !string.Equals(previous.RefineAttributeHash, current.RefineAttributeHash, StringComparison.Ordinal))
                    {
                        return current;
                    }
                }

                int remaining = this._configuration.ResultConfirmTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining <= 0) break;
                await Task.Delay(Math.Min(50, remaining), token);
            }

            return null;
        }

        private EquipmentRefineExecutor.RefineRequest BuildRequest(EquipmentRefineDetector.EquipmentSlot slot)
        {
            int slotIndex;
            if (!EquipmentRefineDetector.TryParseSlotIndex(slot.Slot, out slotIndex))
            {
                slotIndex = this._configuration.BagTargetMode &&
                    this._configuration.BagTarget != null
                    ? this._configuration.BagTarget.RequestSlotIndex
                    : -1;
            }
            return new EquipmentRefineExecutor.RefineRequest
            {
                SlotIndex = slotIndex,
                MemberIdentity = slot.MemberIdentity,
                EquipmentId = slot.EquipmentId,
                ItemId = slot.ItemId,
                ItemTypeId = slot.ItemTypeId,
                EquipmentName = slot.EquipmentName,
                Type = EquipmentRefineExecutor.RefineType.Normal,
                TypeCode = this._configuration.TypeCode,
                OperationCode = this._configuration.OperationCode,
                WaitTimeoutMs = this._configuration.ResultConfirmTimeoutMs,
                WaitForResult = true
            };
        }

        private bool IsSameTarget(EquipmentAttributesSnapshot snapshot, EquipmentRefineDetector.EquipmentSlot slot)
        {
            return snapshot != null && slot != null &&
                string.Equals(snapshot.MemberIdentity, slot.MemberIdentity, StringComparison.Ordinal) &&
                string.Equals(snapshot.Slot, slot.Slot, StringComparison.Ordinal) &&
                (this._configuration.BagTargetMode || string.Equals(snapshot.EquipmentId, slot.EquipmentId, StringComparison.Ordinal)) &&
                (!this._configuration.BagTargetMode ||
                 (string.IsNullOrWhiteSpace(slot.ItemId) || string.Equals(snapshot.ItemId, slot.ItemId, StringComparison.Ordinal))) &&
                (!this._configuration.BagTargetMode ||
                 (string.IsNullOrWhiteSpace(slot.ItemTypeId) || string.Equals(snapshot.ItemTypeId, slot.ItemTypeId, StringComparison.Ordinal))) &&
                string.Equals(snapshot.BaseAttributeHash, slot.BaseAttributeHash, StringComparison.Ordinal);
        }

        private bool IsInventoryUsable(EquipmentRefineDetector.EquipmentInventory inventory)
        {
            return inventory != null && inventory.IsUsableFor(
                this._configuration.BagTargetMode ? EquipmentTargetMode.Bag : EquipmentTargetMode.Worn);
        }

        private EquipmentRefineDetector.EquipmentSlot FindTarget(
            EquipmentRefineDetector.EquipmentInventory inventory)
        {
            return this._configuration.BagTargetMode
                ? EquipmentRefineDetector.FindBagTarget(inventory, this._configuration.BagTarget)
                : EquipmentRefineDetector.FindByTarget(inventory, this._configuration.Target);
        }

        private bool BindWornTargetIdentity(out string error)
        {
            error = string.Empty;
            if (this._configuration.BagTargetMode) return true;
            if (this._configuration.Target == null || this._targetSlot == null)
            {
                error = "worn_target_identity_unavailable";
                return false;
            }
            if (string.IsNullOrWhiteSpace(this._targetSlot.MemberIdentity) ||
                string.IsNullOrWhiteSpace(this._targetSlot.EquipmentId))
            {
                error = "worn_target_identity_unavailable";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(this._configuration.Target.MemberIdentity) &&
                !string.Equals(
                    this._configuration.Target.MemberIdentity,
                    this._targetSlot.MemberIdentity,
                    StringComparison.Ordinal))
            {
                error = "worn_target_member_identity_mismatch";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(this._configuration.Target.EquipmentId) &&
                !string.Equals(
                    this._configuration.Target.EquipmentId,
                    this._targetSlot.EquipmentId,
                    StringComparison.Ordinal))
            {
                error = "worn_target_equipment_id_mismatch";
                return false;
            }
            this._configuration.Target.MemberIdentity = this._targetSlot.MemberIdentity;
            this._configuration.Target.EquipmentId = this._targetSlot.EquipmentId;
            return true;
        }

        private bool CheckRules(EquipmentAttributesSnapshot snapshot)
        {
            List<RefineRule> rules = this._configuration.ActiveRules == null
                ? new List<RefineRule>()
                : this._configuration.ActiveRules.Where(rule => rule != null && rule.Enabled).ToList();
            // Memory-result mode reads identity/sequence before a send and
            // receives the 20 candidate cards afterwards. That pre-send
            // snapshot deliberately has no current attributes; treating a
            // missing value as zero would make a rule such as `根骨 >= 0`
            // stop before the first send.
            if (rules.Count == 0 || snapshot == null ||
                snapshot.Attributes == null || snapshot.Attributes.Count == 0)
            {
                return false;
            }

            List<bool> results = rules.Select(rule => rule.Evaluate(snapshot.GetValue(rule.Attribute))).ToList();
            return this._configuration.RuleLogic == RuleLogic.Any
                ? results.Any(value => value)
                : results.All(value => value);
        }

        private ExecutionResult Finish(
            ExecutionResult result,
            RefinementState state,
            StopReason reason,
            string message,
            EquipmentAttributesSnapshot snapshot,
            bool success = false)
        {
            this.SetState(state, message);
            result.FinalState = state;
            result.StopReason = reason;
            result.Success = success;
            result.Message = message ?? string.Empty;
            result.AttemptCount = this._attemptCount;
            result.FinalAttributes = snapshot == null ? null : snapshot.Clone();
            result.FinalAttributeHash = snapshot == null ? string.Empty : snapshot.RefineAttributeHash;
            result.Steps = this._stepHistory.ToList();
            result.CompletedAt = DateTime.UtcNow;
            if (this._stepHistory.Count > 0)
            {
                StepRecord finalStep = this._stepHistory[this._stepHistory.Count - 1];
                finalStep.CompletedAt = result.CompletedAt;
                finalStep.Succeeded = success;
                finalStep.ErrorMessage = success ? string.Empty : result.Message;
            }
            if (this._resultPresenter != null)
            {
                this._resultPresenter.Present(result);
                if (!success)
                {
                    string displayMessage = this._stopReasonMapper == null
                        ? result.Message
                        : this._stopReasonMapper.Map(result.Message);
                    this._resultPresenter.PresentStop(result.Message, displayMessage);
                }
            }
            return result;
        }

        private void SetState(RefinementState state, string description)
        {
            DateTime now = DateTime.UtcNow;
            StepRecord last = this._stepHistory.Count == 0
                ? null
                : this._stepHistory[this._stepHistory.Count - 1];
            if (last != null && last.State == state && last.CompletedAt == DateTime.MinValue)
            {
                this._currentState = state;
                return;
            }

            if (last != null && last.CompletedAt == DateTime.MinValue)
            {
                last.CompletedAt = now;
                last.Succeeded = true;
            }

            this._currentState = state;
            this._stepHistory.Add(new StepRecord
            {
                State = state,
                Description = string.IsNullOrWhiteSpace(description)
                    ? state.ToString()
                    : description,
                StartedAt = now
            });
        }
    }
}
