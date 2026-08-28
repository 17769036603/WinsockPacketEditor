using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑炼化读取结果。读取失败时不提供可供发送使用的快照。
    /// </summary>
    public sealed class MountRefineSnapshotReadResult
    {
        public MountRefineSnapshotReadResult(
            MountStatusReadOnlySnapshot snapshot,
            string error = null,
            bool wasCancelled = false,
            MountStatusReadDiagnostics diagnostics = null)
        {
            Snapshot = snapshot;
            Error = error ?? string.Empty;
            WasCancelled = wasCancelled;
            Diagnostics = diagnostics;
        }

        public MountStatusReadOnlySnapshot Snapshot { get; private set; }

        public string Error { get; private set; }

        public bool WasCancelled { get; private set; }

        public MountStatusReadDiagnostics Diagnostics { get; private set; }

        public bool Succeeded
        {
            get { return Snapshot != null && string.IsNullOrWhiteSpace(Error); }
        }
    }

    /// <summary>
    /// 坐骑炼化循环的只读快照来源。
    /// </summary>
    public interface IMountRefineSnapshotSource
    {
        Task<MountRefineSnapshotReadResult> ReadSnapshotAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 将现有 Android/LuaJIT 只读探针接入炼化状态机。
    /// </summary>
    public sealed class MountAndroidRefineSnapshotSource : IMountRefineSnapshotSource
    {
        private readonly MountStatusAndroidSnapshotReader _reader;

        public MountAndroidRefineSnapshotSource(MountStatusAndroidSnapshotReader reader)
        {
            _reader = reader;
        }

        public async Task<MountRefineSnapshotReadResult> ReadSnapshotAsync(CancellationToken cancellationToken)
        {
            if (_reader == null)
            {
                return new MountRefineSnapshotReadResult(null, "mount_refine_snapshot_reader_unconfigured");
            }

            MountStatusAndroidSnapshotReadResult readResult = await _reader.ReadSnapshotAsync(
                cancellationToken,
                true).ConfigureAwait(false);
            if (readResult == null)
            {
                return new MountRefineSnapshotReadResult(null, "mount_refine_snapshot_reader_returned_null");
            }

            return new MountRefineSnapshotReadResult(
                readResult.Snapshot,
                readResult.Error,
                readResult.WasCancelled,
                readResult.Diagnostics);
        }
    }

    public enum MountRefineSendResult
    {
        Unknown = 0,
        Accepted = 1,
        ProtocolUnverified = 2,
        AuthorizationRequired = 3,
        Failed = 4,
        Timeout = 5,
        InvalidRequest = 6
    }

    /// <summary>
    /// 发送器只接收已经读取到的炼化上下文，不直接暴露原始 Socket。
    /// 在协议、方向和连接验收完成前，状态机使用拒绝发送的实现。
    /// </summary>
    public interface IMountRefinePacketSender
    {
        bool IsAuthorized { get; }

        bool IsProtocolVerified { get; }

        Task<MountRefineSendResult> SendAsync(MountRefineSendRequest request, CancellationToken cancellationToken);
    }

    public interface IMountRefinePacketSenderDiagnostics
    {
        string LastFailureCode { get; }

        string LastFailureMessage { get; }

        int LastSocket { get; }

        int LastBytesSent { get; }

        int LastSocketErrorCode { get; }
    }

    public sealed class MountRefineSendRequest
    {
        public int AttemptNumber { get; set; }

        public int ProcessId { get; set; }

        public long ProcessStartTicks { get; set; }

        public Guid SessionId { get; set; }

        public long Sequence { get; set; }

        public long? MountId { get; set; }

        public string ActiveRideInstanceId { get; set; }

        public double TargetGrowthRate { get; set; }

        public int RefineCardCount { get; set; }
    }

    /// <summary>
    /// 默认安全发送器：没有协议验证就永远不发送。
    /// </summary>
    public sealed class FailClosedMountRefinePacketSender : IMountRefinePacketSender
    {
        public bool IsAuthorized
        {
            get { return false; }
        }

        public bool IsProtocolVerified
        {
            get { return false; }
        }

        public Task<MountRefineSendResult> SendAsync(MountRefineSendRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(MountRefineSendResult.ProtocolUnverified);
        }
    }

    /// <summary>
    /// 离线测试用发送器。只记录请求，不连接游戏、不写 Socket。
    /// </summary>
    public sealed class RecordingMountRefinePacketSender : IMountRefinePacketSender
    {
        private readonly List<MountRefineSendRequest> _requests = new List<MountRefineSendRequest>();

        public RecordingMountRefinePacketSender(bool isAuthorized, bool isProtocolVerified)
        {
            IsAuthorized = isAuthorized;
            IsProtocolVerified = isProtocolVerified;
        }

        public bool IsAuthorized { get; private set; }

        public bool IsProtocolVerified { get; private set; }

        public IReadOnlyList<MountRefineSendRequest> Requests
        {
            get { return _requests.AsReadOnly(); }
        }

        public Task<MountRefineSendResult> SendAsync(MountRefineSendRequest request, CancellationToken cancellationToken)
        {
            if (!IsProtocolVerified)
            {
                return Task.FromResult(MountRefineSendResult.ProtocolUnverified);
            }

            if (!IsAuthorized)
            {
                return Task.FromResult(MountRefineSendResult.AuthorizationRequired);
            }

            if (request == null || request.AttemptNumber <= 0)
            {
                return Task.FromResult(MountRefineSendResult.InvalidRequest);
            }

            _requests.Add(CloneRequest(request));
            return Task.FromResult(MountRefineSendResult.Accepted);
        }

        private static MountRefineSendRequest CloneRequest(MountRefineSendRequest request)
        {
            return new MountRefineSendRequest
            {
                AttemptNumber = request.AttemptNumber,
                ProcessId = request.ProcessId,
                ProcessStartTicks = request.ProcessStartTicks,
                SessionId = request.SessionId,
                Sequence = request.Sequence,
                MountId = request.MountId,
                ActiveRideInstanceId = request.ActiveRideInstanceId,
                TargetGrowthRate = request.TargetGrowthRate,
                RefineCardCount = request.RefineCardCount
            };
        }
    }

    public sealed class MountRefineConfiguration
    {
        /// <summary>
        /// 0 表示不按炼化次数限制；运行仍会在命中、手动停止或安全故障时结束。
        /// </summary>
        public int MaxAttempts { get; set; }

        /// <summary>
        /// 单个刷新确认窗口的时长。窗口超时只触发继续轮询，不会自动重发或终止运行。
        /// </summary>
        public int ResultConfirmTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 单次只读快照读取的上限。实际读取时会再限制为当前确认窗口的剩余时间；
        /// 读取超时只结束当前窗口，状态机继续等待同一次发送的结果。
        /// </summary>
        public int SnapshotReadTimeoutMs { get; set; } = 2800;

        public int IntervalMs { get; set; } = 250;

        public MountSpeedPreset Preset { get; set; }

        public bool IsValid(out string error)
        {
            if (Preset == null)
            {
                error = "mount_refine_preset_missing";
                return false;
            }

            if (!Preset.IsCompleteMountRefineTarget(out error))
            {
                return false;
            }

            if (MaxAttempts < 0)
            {
                error = "mount_refine_max_attempts_invalid";
                return false;
            }

            if (ResultConfirmTimeoutMs <= 0)
            {
                error = "mount_refine_result_timeout_invalid";
                return false;
            }

            if (SnapshotReadTimeoutMs <= 0)
            {
                error = "mount_refine_snapshot_read_timeout_invalid";
                return false;
            }

            if (IntervalMs < 0)
            {
                error = "mount_refine_interval_invalid";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    public enum MountRefineState
    {
        Idle = 0,
        ReadingSnapshot = 1,
        CheckingTarget = 2,
        PreparingSend = 3,
        Sending = 4,
        WaitingForRefresh = 5,
        VerifyingRefresh = 6,
        Continuing = 7,
        Completed = 8,
        Stopped = 9,
        Failed = 10
    }

    public enum MountRefineStopReason
    {
        None = 0,
        TargetReached = 1,
        UserStopped = 2,
        MaxAttemptsReached = 3,
        SnapshotReadFailed = 4,
        SnapshotNotRefreshed = 5,
        MountChanged = 6,
        AuthorizationRequired = 7,
        ProtocolUnverified = 8,
        SendFailed = 9,
        SendTimeout = 10,
        InvalidConfiguration = 11,
        InternalError = 12
    }

    public sealed class MountRefineStep
    {
        public MountRefineState State { get; set; }

        public string Description { get; set; }

        public DateTimeOffset At { get; set; }
    }

    public sealed class MountRefineExecutionResult
    {
        public MountRefineState FinalState { get; internal set; }

        public MountRefineStopReason StopReason { get; internal set; }

        public bool Success { get; internal set; }

        public string Message { get; internal set; }

        public int AttemptCount { get; internal set; }

        public int? MatchedCardIndex { get; internal set; }

        public MountStatusReadOnlySnapshot FinalSnapshot { get; internal set; }

        public IReadOnlyList<MountRefineStep> Steps { get; internal set; }
    }

    /// <summary>
    /// 坐骑炼化控制循环：读取当前状态 → 匹配四项目标 → 发送接口 → 等待新读数 → 再匹配。
    /// 默认发送器 fail-closed，未通过协议与授权闸门时不会产生任何真实发送。
    /// </summary>
    public sealed class MountRefineStateMachine
    {
        private const int ExpectedRefineCardCount = 21;

        private readonly MountRefineConfiguration _configuration;
        private readonly IMountRefineSnapshotSource _snapshotSource;
        private readonly IMountRefinePacketSender _packetSender;
        private readonly Action<MountRefineLogEntry> _logger;
        private readonly Guid _runId;
        private readonly List<MountRefineStep> _steps = new List<MountRefineStep>();
        private CancellationTokenSource _runCancellation;
        private MountStatusReadOnlySnapshot _currentSnapshot;
        private int _attemptCount;
        private int? _matchedCardIndex;
        private MountRefineState _state = MountRefineState.Idle;

        public MountRefineStateMachine(
            MountRefineConfiguration configuration,
            IMountRefineSnapshotSource snapshotSource,
            IMountRefinePacketSender packetSender = null,
            Action<MountRefineLogEntry> logger = null,
            Guid runId = default(Guid))
        {
            _configuration = configuration;
            _snapshotSource = snapshotSource;
            _packetSender = packetSender ?? new FailClosedMountRefinePacketSender();
            _logger = logger;
            _runId = runId == Guid.Empty ? Guid.NewGuid() : runId;
        }

        public MountRefineState State
        {
            get { return _state; }
        }

        public Guid RunId
        {
            get { return _runId; }
        }

        public void Abort()
        {
            CancellationTokenSource cancellation = _runCancellation;
            if (cancellation != null)
            {
                cancellation.Cancel();
            }
        }

        public async Task<MountRefineExecutionResult> RunAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ResetRunState();

            string configurationError = null;
            if (_configuration == null || !_configuration.IsValid(out configurationError))
            {
                return Finish(MountRefineState.Failed, MountRefineStopReason.InvalidConfiguration, configurationError);
            }

            if (_snapshotSource == null)
            {
                return Finish(MountRefineState.Failed, MountRefineStopReason.SnapshotReadFailed, "mount_refine_snapshot_source_unconfigured");
            }

            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken token = _runCancellation.Token;

            try
            {
                SetState(MountRefineState.ReadingSnapshot, "mount_refine_read_initial_snapshot");
                MountRefineSnapshotReadResult initialRead = await ReadSnapshotAsync(token).ConfigureAwait(false);
                if (!TryValidateSnapshot(initialRead, out string initialError))
                {
                    if (initialRead.WasCancelled || token.IsCancellationRequested)
                    {
                        return Finish(MountRefineState.Stopped, MountRefineStopReason.UserStopped, "mount_refine_user_stopped");
                    }
                    return Finish(MountRefineState.Stopped, MountRefineStopReason.SnapshotReadFailed, initialError);
                }

                _currentSnapshot = initialRead.Snapshot;

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    if (_configuration.MaxAttempts > 0 && _attemptCount >= _configuration.MaxAttempts)
                    {
                        return Finish(MountRefineState.Stopped, MountRefineStopReason.MaxAttemptsReached, "mount_refine_max_attempts_reached");
                    }

                    SetState(MountRefineState.CheckingTarget, "mount_refine_check_target");
                    MountRideRefineTargetPreviewResult preview;
                    string previewError;
                    if (!MountRideRefineTargetMatcher.TryEvaluate(
                        _currentSnapshot,
                        _configuration.Preset,
                        out preview,
                        out previewError) || preview == null)
                    {
                        return Finish(MountRefineState.Failed, MountRefineStopReason.SnapshotReadFailed, string.IsNullOrWhiteSpace(previewError) ? "mount_refine_target_preview_missing" : previewError);
                    }

                    if (preview.IsSatisfied)
                    {
                        _matchedCardIndex = preview.MatchedCardIndex;
                        return Finish(MountRefineState.Completed, MountRefineStopReason.TargetReached, "target_reached_before_send");
                    }

                    SetState(MountRefineState.PreparingSend, "mount_refine_prepare_send");
                    if (!_packetSender.IsProtocolVerified)
                    {
                        return Finish(MountRefineState.Stopped, MountRefineStopReason.ProtocolUnverified, "mount_refine_protocol_unverified");
                    }

                    if (!_packetSender.IsAuthorized)
                    {
                        return Finish(MountRefineState.Stopped, MountRefineStopReason.AuthorizationRequired, "mount_refine_send_authorization_required");
                    }

                    MountRefineSendRequest request = CreateSendRequest(_currentSnapshot, _attemptCount + 1);
                    SetState(MountRefineState.Sending, "mount_refine_send_attempt_" + request.AttemptNumber);
                    MountRefineSendResult sendResult = await _packetSender.SendAsync(request, token).ConfigureAwait(false);
                    string sendMessage = DescribeSendResult(sendResult);
                    LogEvent(
                        "send_result",
                        MountRefineState.Sending,
                        sendResult.ToString(),
                        sendMessage,
                        sendResult == MountRefineSendResult.Accepted,
                        string.Empty,
                        null);
                    if (sendResult != MountRefineSendResult.Accepted)
                    {
                        return Finish(
                            MountRefineState.Stopped,
                            ToStopReason(sendResult),
                            sendMessage);
                    }

                    _attemptCount++;
                    Stopwatch sendToVerifyStopwatch = Stopwatch.StartNew();
                    SetState(MountRefineState.WaitingForRefresh, "mount_refine_wait_for_new_snapshot");
                    MountStatusReadOnlySnapshot previousSnapshot = _currentSnapshot;
                    MountRefineSnapshotReadResult refreshedRead = await WaitForChangedSnapshotAsync(previousSnapshot, token).ConfigureAwait(false);
                    if (!refreshedRead.Succeeded)
                    {
                        if (refreshedRead.WasCancelled || token.IsCancellationRequested)
                        {
                            return Finish(MountRefineState.Stopped, MountRefineStopReason.UserStopped, "mount_refine_user_stopped");
                        }
                        MountRefineStopReason reason = string.Equals(refreshedRead.Error, "mount_refine_mount_context_changed", StringComparison.Ordinal)
                            ? MountRefineStopReason.MountChanged
                            : string.Equals(refreshedRead.Error, "mount_refine_result_timeout", StringComparison.Ordinal)
                                ? MountRefineStopReason.SnapshotNotRefreshed
                                : MountRefineStopReason.SnapshotReadFailed;
                        return Finish(MountRefineState.Stopped, reason, refreshedRead.Error);
                    }

                    _currentSnapshot = refreshedRead.Snapshot;
                    SetState(MountRefineState.VerifyingRefresh, "mount_refine_verify_new_snapshot");
                    LogEvent(
                        "verify_new_snapshot",
                        MountRefineState.VerifyingRefresh,
                        "mount_refine_verify_new_snapshot",
                        "A new validated snapshot was observed after the accepted send.",
                        null,
                        string.Empty,
                        null,
                        refreshedRead.Diagnostics,
                        sendToVerifyStopwatch.Elapsed.TotalMilliseconds);
                    MountRideRefineTargetPreviewResult refreshedPreview;
                    string refreshedPreviewError;
                    if (!MountRideRefineTargetMatcher.TryEvaluate(
                        _currentSnapshot,
                        _configuration.Preset,
                        out refreshedPreview,
                        out refreshedPreviewError) || refreshedPreview == null)
                    {
                        return Finish(MountRefineState.Stopped, MountRefineStopReason.SnapshotReadFailed, string.IsNullOrWhiteSpace(refreshedPreviewError) ? "mount_refine_refreshed_preview_missing" : refreshedPreviewError);
                    }

                    if (refreshedPreview.IsSatisfied)
                    {
                        _matchedCardIndex = refreshedPreview.MatchedCardIndex;
                        return Finish(MountRefineState.Completed, MountRefineStopReason.TargetReached, "target_reached_after_refine");
                    }

                    SetState(MountRefineState.Continuing, "mount_refine_target_not_reached_continue");
                    if (_configuration.IntervalMs > 0)
                    {
                        await Task.Delay(_configuration.IntervalMs, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return Finish(MountRefineState.Stopped, MountRefineStopReason.UserStopped, "mount_refine_user_stopped");
            }
            catch (Exception exception)
            {
                return Finish(MountRefineState.Failed, MountRefineStopReason.InternalError, "mount_refine_internal_error: " + exception.Message);
            }
            finally
            {
                CancellationTokenSource cancellation = _runCancellation;
                _runCancellation = null;
                if (cancellation != null)
                {
                    cancellation.Dispose();
                }
            }
        }

        private void ResetRunState()
        {
            _steps.Clear();
            _currentSnapshot = null;
            _attemptCount = 0;
            _matchedCardIndex = null;
            _state = MountRefineState.Idle;
        }

        private async Task<MountRefineSnapshotReadResult> ReadSnapshotAsync(CancellationToken cancellationToken)
        {
            MountRefineSnapshotReadResult result = await _snapshotSource.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            result = result ?? new MountRefineSnapshotReadResult(null, "mount_refine_snapshot_source_returned_null");
            if (result.Diagnostics != null)
            {
                LogEvent(
                    "read_timing",
                    _state,
                    "mount_refine_read_timing",
                    string.Empty,
                    null,
                    string.Empty,
                    null,
                    result.Diagnostics,
                    null);
            }
            return result;
        }

        private async Task<MountRefineSnapshotReadResult> WaitForChangedSnapshotAsync(
            MountStatusReadOnlySnapshot previousSnapshot,
            CancellationToken cancellationToken)
        {
            long baselineSequence = previousSnapshot == null
                ? long.MinValue
                : previousSnapshot.Sequence;
            int retryWindow = 0;
            int lastSequenceWaitLogWindow = -10;
            int lastContentWaitLogWindow = -10;
            while (true)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < _configuration.ResultConfirmTimeoutMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int remainingMs = _configuration.ResultConfirmTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                    if (remainingMs <= 0)
                    {
                        break;
                    }

                    MountRefineSnapshotReadResult readResult = await ReadSnapshotWithTimeoutAsync(
                        cancellationToken,
                        Math.Min(remainingMs, _configuration.SnapshotReadTimeoutMs)).ConfigureAwait(false);
                    if (!readResult.Succeeded)
                    {
                        if (string.Equals(readResult.Error, "mount_refine_result_timeout", StringComparison.Ordinal))
                        {
                            break;
                        }

                        return readResult;
                    }

                    if (!HasSameMountContext(previousSnapshot, readResult.Snapshot))
                    {
                        return new MountRefineSnapshotReadResult(null, "mount_refine_mount_context_changed");
                    }

                    string validationError;
                    if (!TryValidateSnapshot(readResult, out validationError))
                    {
                        return new MountRefineSnapshotReadResult(null, validationError);
                    }

                    if (readResult.Snapshot.Sequence <= baselineSequence)
                    {
                        if (retryWindow - lastSequenceWaitLogWindow >= 10)
                        {
                            LogEvent(
                                "snapshot_sequence_wait",
                                MountRefineState.WaitingForRefresh,
                                "mount_refine_snapshot_sequence_not_advanced_waiting",
                                "Snapshot sequence has not advanced (observed " +
                                    readResult.Snapshot.Sequence +
                                    ", baseline " + baselineSequence +
                                    "); keep waiting without target evaluation or resend.",
                                null,
                                string.Empty,
                                null,
                                readResult.Diagnostics,
                                null);
                            lastSequenceWaitLogWindow = retryWindow;
                        }

                        remainingMs = _configuration.ResultConfirmTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                        if (remainingMs <= 0)
                        {
                            break;
                        }

                        await Task.Delay(Math.Min(50, remainingMs), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (!HasChangedRefineCards(previousSnapshot, readResult.Snapshot))
                    {
                        if (retryWindow - lastContentWaitLogWindow >= 10)
                        {
                            LogEvent(
                                "snapshot_content_wait",
                                MountRefineState.WaitingForRefresh,
                                "mount_refine_snapshot_cards_not_changed_waiting",
                                "Snapshot sequence advanced to " +
                                    readResult.Snapshot.Sequence +
                                    " but the 21-card content still matches the pre-send baseline; " +
                                    "keep waiting without target evaluation or resend.",
                                null,
                                string.Empty,
                                null,
                                readResult.Diagnostics,
                                null);
                            lastContentWaitLogWindow = retryWindow;
                        }

                        remainingMs = _configuration.ResultConfirmTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                        if (remainingMs <= 0)
                        {
                            break;
                        }

                        await Task.Delay(Math.Min(50, remainingMs), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return readResult;
                }

                cancellationToken.ThrowIfCancellationRequested();
                retryWindow++;
                if (retryWindow == 1 || retryWindow % 10 == 0)
                {
                    LogEvent(
                        "refresh_wait_retry",
                        MountRefineState.WaitingForRefresh,
                        "mount_refine_result_timeout_retrying",
                        "Refresh confirmation window " + retryWindow + " elapsed; keep waiting without resending.",
                        null,
                        string.Empty,
                        null);
                }
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<MountRefineSnapshotReadResult> ReadSnapshotWithTimeoutAsync(
            CancellationToken cancellationToken,
            int timeoutMs)
        {
            using (CancellationTokenSource readCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                readCancellation.CancelAfter(timeoutMs);
                MountRefineSnapshotReadResult result = await ReadSnapshotAsync(
                    readCancellation.Token).ConfigureAwait(false);
                if (result != null &&
                    result.WasCancelled &&
                    readCancellation.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested)
                {
                    return new MountRefineSnapshotReadResult(null, "mount_refine_result_timeout");
                }
                return result ?? new MountRefineSnapshotReadResult(null, "mount_refine_snapshot_source_returned_null");
            }
        }

        private static bool TryValidateSnapshot(MountRefineSnapshotReadResult readResult, out string error)
        {
            if (readResult == null || !readResult.Succeeded || readResult.Snapshot == null)
            {
                error = readResult == null || string.IsNullOrWhiteSpace(readResult.Error)
                    ? "mount_refine_snapshot_read_failed"
                    : readResult.Error;
                return false;
            }

            MountStatusReadOnlySnapshot snapshot = readResult.Snapshot;
            if (!snapshot.IsValid)
            {
                error = "mount_refine_snapshot_invalid";
                return false;
            }

            if (snapshot.IsMounted != true || !string.Equals(snapshot.RideBindingStatus, "bound", StringComparison.OrdinalIgnoreCase))
            {
                error = "mount_refine_current_ride_not_bound";
                return false;
            }

            if (string.IsNullOrWhiteSpace(snapshot.ActiveRideInstanceId))
            {
                error = "mount_refine_active_ride_missing";
                return false;
            }

            if (snapshot.RideRefineCards == null || snapshot.RideRefineCards.Count != ExpectedRefineCardCount)
            {
                error = "mount_refine_cards_incomplete";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool HasChangedRefineCards(
            MountStatusReadOnlySnapshot previousSnapshot,
            MountStatusReadOnlySnapshot currentSnapshot)
        {
            if (previousSnapshot == null || currentSnapshot == null || previousSnapshot.RideRefineCards == null || currentSnapshot.RideRefineCards == null)
            {
                return false;
            }

            MountRideRefineCardComparison comparison = MountRideRefineCardComparer.Compare(
                previousSnapshot,
                currentSnapshot);
            return comparison != null && comparison.Changes != null && comparison.Changes.Count > 0;
        }

        private static bool HasSameMountContext(
            MountStatusReadOnlySnapshot previousSnapshot,
            MountStatusReadOnlySnapshot currentSnapshot)
        {
            if (previousSnapshot == null || currentSnapshot == null || previousSnapshot.Process == null || currentSnapshot.Process == null)
            {
                return false;
            }

            return previousSnapshot.Process.Pid == currentSnapshot.Process.Pid
                && previousSnapshot.Process.StartTicks == currentSnapshot.Process.StartTicks
                && previousSnapshot.SessionId == currentSnapshot.SessionId
                && previousSnapshot.MountId == currentSnapshot.MountId
                && string.Equals(previousSnapshot.ActiveRideInstanceId, currentSnapshot.ActiveRideInstanceId, StringComparison.Ordinal);
        }

        private MountRefineSendRequest CreateSendRequest(MountStatusReadOnlySnapshot snapshot, int attemptNumber)
        {
            return new MountRefineSendRequest
            {
                AttemptNumber = attemptNumber,
                ProcessId = snapshot.Process.Pid,
                ProcessStartTicks = snapshot.Process.StartTicks,
                SessionId = snapshot.SessionId,
                Sequence = snapshot.Sequence,
                MountId = snapshot.MountId,
                ActiveRideInstanceId = snapshot.ActiveRideInstanceId,
                TargetGrowthRate = _configuration.Preset.TargetGrowthRate.Value,
                RefineCardCount = snapshot.RideRefineCards == null ? 0 : snapshot.RideRefineCards.Count
            };
        }

        private static MountRefineStopReason ToStopReason(MountRefineSendResult sendResult)
        {
            switch (sendResult)
            {
                case MountRefineSendResult.ProtocolUnverified:
                    return MountRefineStopReason.ProtocolUnverified;
                case MountRefineSendResult.AuthorizationRequired:
                    return MountRefineStopReason.AuthorizationRequired;
                case MountRefineSendResult.Timeout:
                    return MountRefineStopReason.SendTimeout;
                case MountRefineSendResult.InvalidRequest:
                    return MountRefineStopReason.SendFailed;
                case MountRefineSendResult.Failed:
                case MountRefineSendResult.Unknown:
                default:
                    return MountRefineStopReason.SendFailed;
            }
        }

        private string DescribeSendResult(MountRefineSendResult sendResult)
        {
            IMountRefinePacketSenderDiagnostics diagnostics =
                _packetSender as IMountRefinePacketSenderDiagnostics;
            if (diagnostics != null &&
                (!string.IsNullOrWhiteSpace(diagnostics.LastFailureCode) ||
                 !string.IsNullOrWhiteSpace(diagnostics.LastFailureMessage)))
            {
                return string.IsNullOrWhiteSpace(diagnostics.LastFailureMessage)
                    ? diagnostics.LastFailureCode
                    : string.IsNullOrWhiteSpace(diagnostics.LastFailureCode)
                        ? diagnostics.LastFailureMessage
                        : diagnostics.LastFailureCode + ": " + diagnostics.LastFailureMessage;
            }

            return "mount_refine_send_" + sendResult.ToString();
        }

        private void SetState(MountRefineState state, string description)
        {
            _state = state;
            _steps.Add(new MountRefineStep
            {
                State = state,
                Description = description ?? string.Empty,
                At = DateTimeOffset.UtcNow
            });
            LogEvent(
                "state_changed",
                state,
                description,
                description,
                null,
                string.Empty,
                null);
        }

        private MountRefineExecutionResult Finish(
            MountRefineState finalState,
            MountRefineStopReason reason,
            string message)
        {
            SetState(finalState, message);
            LogEvent(
                "finished",
                finalState,
                message,
                message,
                reason == MountRefineStopReason.TargetReached,
                reason.ToString(),
                _matchedCardIndex);
            return new MountRefineExecutionResult
            {
                FinalState = finalState,
                StopReason = reason,
                Success = reason == MountRefineStopReason.TargetReached,
                Message = message ?? string.Empty,
                AttemptCount = _attemptCount,
                MatchedCardIndex = _matchedCardIndex,
                FinalSnapshot = _currentSnapshot,
                Steps = new List<MountRefineStep>(_steps).AsReadOnly()
            };
        }

        private void LogEvent(
            string eventName,
            MountRefineState state,
            string code,
            string message,
            bool? success,
            string stopReason,
            int? matchedCardIndex,
            MountStatusReadDiagnostics readDiagnostics = null,
            double? sendToVerifyMilliseconds = null)
        {
            if (_logger == null)
            {
                return;
            }

            IMountRefinePacketSenderDiagnostics diagnostics =
                _packetSender as IMountRefinePacketSenderDiagnostics;
            MountStatusReadOnlySnapshot snapshot = _currentSnapshot;
            MountRefineLogEntry entry = new MountRefineLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                RunId = _runId,
                EventName = eventName,
                State = state.ToString(),
                StopReason = stopReason ?? string.Empty,
                Code = code ?? string.Empty,
                Message = message ?? string.Empty,
                AttemptCount = _attemptCount,
                Success = success,
                Sequence = snapshot == null ? 0 : snapshot.Sequence,
                MountId = snapshot == null ? null : snapshot.MountId,
                ActiveRideInstanceId = snapshot == null
                    ? string.Empty
                    : snapshot.ActiveRideInstanceId,
                RideBindingStatus = snapshot == null
                    ? string.Empty
                    : snapshot.RideBindingStatus,
                RefineCardCount = snapshot == null || snapshot.RideRefineCards == null
                    ? 0
                    : snapshot.RideRefineCards.Count,
                MatchedCardIndex = matchedCardIndex,
                ProtocolVerified = _packetSender.IsProtocolVerified,
                Authorized = _packetSender.IsAuthorized,
                Socket = diagnostics == null ? 0 : diagnostics.LastSocket,
                BytesSent = diagnostics == null ? 0 : diagnostics.LastBytesSent,
                SocketError = diagnostics == null ? 0 : diagnostics.LastSocketErrorCode,
                ReadPath = readDiagnostics == null ? string.Empty : readDiagnostics.ReadPath,
                RootAdbMilliseconds = readDiagnostics == null
                    ? (double?)null
                    : readDiagnostics.RootAdbMilliseconds,
                ProbeMilliseconds = readDiagnostics == null
                    ? (double?)null
                    : readDiagnostics.ProbeMilliseconds,
                CardParseMilliseconds = readDiagnostics == null
                    ? (double?)null
                    : readDiagnostics.CardParseMilliseconds,
                ProtocolParseMilliseconds = readDiagnostics == null
                    ? (double?)null
                    : readDiagnostics.ProtocolParseMilliseconds,
                ReadTotalMilliseconds = readDiagnostics == null
                    ? (double?)null
                    : readDiagnostics.TotalMilliseconds,
                SendToVerifyMilliseconds = sendToVerifyMilliseconds
            };

            try
            {
                _logger(entry);
            }
            catch
            {
                // Diagnostics must never change the fail-closed state machine.
            }
        }
    }
}
