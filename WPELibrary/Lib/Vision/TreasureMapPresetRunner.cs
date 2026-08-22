using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// 藏宝图指令版本
    /// </summary>
    public enum TreasureMapInstructionVersion
    {
        /// <summary>原始版本：持续模式，无限重试</summary>
        V1 = 1,
        /// <summary>v2 版本：一次/持续模式、状态机、自动挖宝发送结果处理</summary>
        V2 = 2,
        /// <summary>v3 版本：增加控制模式和证据策略字段</summary>
        V3 = 3
    }

    /// <summary>
    /// 藏宝图预设选项
    /// </summary>
    public sealed class TreasureMapPresetOptions
    {
        public TreasureMapPresetOptions()
        {
            this.Host = TreasureC6StreamProtocolDefinition.DefaultHost;
            this.Port = TreasureC6StreamProtocolDefinition.DefaultPort;
            this.UseType = 13;
            this.UseNum = 1;
            this.UseParam = "2";
            this.JumpUseDelayMilliseconds = 300;
            this.NextTargetDelayMilliseconds = 100;
            // 新节拍默认关闭，由桌面生产入口显式启用，避免改变既有离线
            // 回归调用方的发送次数和耗时。
            this.TargetStabilityMilliseconds = 0;
            this.MinimumJumpIntervalMilliseconds = 0;
            this.JumpRetryDelayMilliseconds = 0;
            // Jump 明确未发送时使用短间隔重试；自动挖宝包不重试。
            this.RetryDelayMilliseconds = 250;
            // C6 背包变化可能晚于自动挖宝包返回；确认窗口只设上限，
            // 目标提前消失时立即继续。
            this.ConsumptionConfirmTimeoutMilliseconds = 2500;
            this.FailedTargetCooldownMilliseconds = 3000;
            this.LiveSendEnabled = false;
            this.Mode = TreasureMapExecutionMode.Continuous;
            this.Version = TreasureMapInstructionVersion.V2;
            // 生产桌面入口显式使用兼容性 Jump→Use；AutoDig 仅保留给
            // 离线回归和明确选择该路径的调用方，避免回退版本被意外带回。
            this.UseNativeAutoDig = true;
            this.ControlMode = TreasureControlMode.Exclusive;
            this.EvidenceMode = TreasureEvidenceMode.Shadow;
            this.ArrivalEvidenceTimeoutMilliseconds = 1000;
        }

        public string Host { get; set; }

        public int Port { get; set; }

        public int UseType { get; set; }

        public int UseNum { get; set; }

        public string UseParam { get; set; }

        public int JumpUseDelayMilliseconds { get; set; }

        /// <summary>
        /// 成功完成一张藏宝图后，开始下一张前的固定节拍间隔。
        /// </summary>
        public int NextTargetDelayMilliseconds { get; set; }

        /// <summary>
        /// 首次 Jump 前要求目标在 C6 快照中保持不变的时间。
        /// </summary>
        public int TargetStabilityMilliseconds { get; set; }

        /// <summary>
        /// 同一运行器相邻两次实际 Jump 之间的最短间隔。
        /// </summary>
        public int MinimumJumpIntervalMilliseconds { get; set; }

        /// <summary>
        /// 首次 Jump/Use 后仍未确认消耗时，最多补发一次 Jump 的等待时间。
        /// </summary>
        public int JumpRetryDelayMilliseconds { get; set; }

        /// <summary>
        /// Jump 明确未发送时的短重试间隔。自动挖宝包不使用此配置。
        /// </summary>
        public int RetryDelayMilliseconds { get; set; }

        /// <summary>
        /// 自动挖宝包发出后等待同一张藏宝图离开背包的最长时间。
        /// </summary>
        public int ConsumptionConfirmTimeoutMilliseconds { get; set; }

        /// <summary>
        /// 同一目标完成一次立即重试后仍未消耗时的重试冷却。
        /// </summary>
        public int FailedTargetCooldownMilliseconds { get; set; }

        public bool LiveSendEnabled { get; set; }

        /// <summary>
        /// 执行模式：一次性处理当前快照或持续监听
        /// </summary>
        public TreasureMapExecutionMode Mode { get; set; }

        /// <summary>
        /// 指令版本：用于兼容旧指令
        /// </summary>
        public TreasureMapInstructionVersion Version { get; set; }

        /// <summary>
        /// 是否在 Jump 后发送固定 AutoDig 帧。生产机器人入口关闭此项，
        /// 保留默认值只用于兼容现有离线 AutoDig 回归调用方。
        /// </summary>
        public bool UseNativeAutoDig { get; set; }

        /// <summary>
        /// 外部控制器协调策略。默认 Exclusive，避免多个控制器同时动作。
        /// </summary>
        public TreasureControlMode ControlMode { get; set; }

        /// <summary>
        /// 到达证据策略。Shadow 只记录，不改变旧流程；Enforced 要求匹配证据。
        /// </summary>
        public TreasureEvidenceMode EvidenceMode { get; set; }

        public int ArrivalEvidenceTimeoutMilliseconds { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(this.Host))
            {
                throw new ArgumentException("C6 host is required.", nameof(this.Host));
            }

            if (this.Port <= 0 || this.Port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(this.Port));
            }

            if (this.UseType <= 0 || this.UseNum <= 0 || this.UseParam == null)
            {
                throw new ArgumentException("The treasure Use request is invalid.");
            }

            if (this.JumpUseDelayMilliseconds < 0 ||
                this.NextTargetDelayMilliseconds < 0 ||
                this.TargetStabilityMilliseconds < 0 ||
                this.MinimumJumpIntervalMilliseconds < 0 ||
                this.JumpRetryDelayMilliseconds < 0 ||
                this.RetryDelayMilliseconds < 0 ||
                this.ConsumptionConfirmTimeoutMilliseconds < 0 ||
                this.FailedTargetCooldownMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(this.JumpUseDelayMilliseconds),
                    "Treasure delays cannot be negative.");
            }

            if (this.Mode != TreasureMapExecutionMode.CurrentSnapshot &&
                this.Mode != TreasureMapExecutionMode.Continuous)
            {
                throw new ArgumentOutOfRangeException(nameof(this.Mode));
            }

            if (this.Version != TreasureMapInstructionVersion.V1 &&
                this.Version != TreasureMapInstructionVersion.V2 &&
                this.Version != TreasureMapInstructionVersion.V3)
            {
                throw new ArgumentOutOfRangeException(nameof(this.Version));
            }

            if (this.ControlMode != TreasureControlMode.Exclusive &&
                this.ControlMode != TreasureControlMode.Cooperative)
            {
                throw new ArgumentOutOfRangeException(nameof(this.ControlMode));
            }

            if (this.EvidenceMode != TreasureEvidenceMode.Shadow &&
                this.EvidenceMode != TreasureEvidenceMode.Enforced)
            {
                throw new ArgumentOutOfRangeException(nameof(this.EvidenceMode));
            }

            if (this.ArrivalEvidenceTimeoutMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(this.ArrivalEvidenceTimeoutMilliseconds));
            }
        }
    }

    /// <summary>
        /// 发送结果模型：NotDispatched/Dispatched/Ambiguous
        /// </summary>
        public sealed class TreasureMapPacketSendResult
        {
            public TreasureMapPacketSendResult(
                bool success,
                string code,
                int socket,
                int bytesSent)
                : this(
                    success,
                    code,
                    socket,
                    bytesSent,
                    success
                        ? TreasureMapPacketSendDisposition.Dispatched
                        : string.Equals(code, "ambiguous", StringComparison.OrdinalIgnoreCase)
                            ? TreasureMapPacketSendDisposition.Ambiguous
                            : TreasureMapPacketSendDisposition.NotDispatched)
            {
            }

            public TreasureMapPacketSendResult(
                bool success,
                string code,
                int socket,
                int bytesSent,
                TreasureMapPacketSendDisposition disposition)
            {
                this.Success = success;
                this.Code = code ?? string.Empty;
                this.Socket = socket;
                this.BytesSent = bytesSent;
                this.Disposition = disposition;
            }

            public bool Success { get; private set; }
            public string Code { get; private set; }
            public int Socket { get; private set; }
            public int BytesSent { get; private set; }

            public TreasureMapPacketSendDisposition Disposition { get; private set; }

            public static TreasureMapPacketSendResult NotDispatched(string code, string reason)
            {
                return new TreasureMapPacketSendResult(
                    false,
                    code ?? reason,
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            public static TreasureMapPacketSendResult Dispatched(int socket, int bytesSent)
            {
                return new TreasureMapPacketSendResult(
                    true,
                    "dispatched",
                    socket,
                    bytesSent,
                    TreasureMapPacketSendDisposition.Dispatched);
            }

            public static TreasureMapPacketSendResult Ambiguous(string code, int socket, int bytesSent)
            {
                return new TreasureMapPacketSendResult(
                    false,
                    code ?? "ambiguous",
                    socket,
                    bytesSent,
                    TreasureMapPacketSendDisposition.Ambiguous);
            }
        }

    public interface ITreasureMapPacketSender
    {
        TreasureMapPacketSendResult SendOnce(
            Socket_PacketInfo packet,
            bool liveSendEnabled);
    }

    public sealed class TreasureMapRuntimePacketSender : ITreasureMapPacketSender
    {
        public TreasureMapPacketSendResult SendOnce(
            Socket_PacketInfo packet,
            bool liveSendEnabled)
        {
            if (!liveSendEnabled)
            {
                return new TreasureMapPacketSendResult(
                    false,
                    "live_send_not_authorized",
                    0,
                    0);
            }

            TreasureLiveSendAuthorization authorization =
                TreasureLiveSendAuthorization.Create("TREASURE-LIVE-SEND");
            try
            {
                TreasurePacketSendResult result =
                    TreasurePacketRuntime.SendPreparedPacketOnce(packet, authorization);
                return new TreasureMapPacketSendResult(
                    result.Success,
                    result.Code,
                    result.Socket,
                    result.BytesSent,
                    result.Disposition);
            }
            catch (Exception ex)
            {
                return TreasureMapPacketSendResult.Ambiguous(
                    "send_exception:" + ex.Message,
                    0,
                    0);
            }
        }
    }

    public sealed class TreasureMapLogEntry
    {
        public TreasureMapLogEntry(
            string step,
            string packet,
            TreasureC6InventoryItem target,
            string code,
            int retry,
            bool success)
            : this(Guid.Empty, step, packet, target, code, retry, success, 0)
        {
        }

        public TreasureMapLogEntry(
            Guid runId,
            string step,
            string packet,
            TreasureC6InventoryItem target,
            string code,
            int retry,
            bool success)
            : this(runId, step, packet, target, code, retry, success, 0)
        {
        }

        public TreasureMapLogEntry(
            Guid runId,
            string step,
            string packet,
            TreasureC6InventoryItem target,
            string code,
            int retry,
            bool success,
            long attemptId)
        {
            this.RunId = runId;
            this.TimestampUtc = DateTime.UtcNow;
            this.Step = step ?? string.Empty;
            this.Packet = packet ?? string.Empty;
            this.Target = target;
            this.Code = code ?? string.Empty;
            this.Retry = retry;
            this.Success = success;
            this.AttemptId = Math.Max(0, attemptId);
        }

        public DateTime TimestampUtc { get; private set; }

        public Guid RunId { get; private set; }

        public string Step { get; private set; }

        public string Packet { get; private set; }

        public TreasureC6InventoryItem Target { get; private set; }

        public string Code { get; private set; }

        public int Retry { get; private set; }

        public bool Success { get; private set; }

        /// <summary>
        /// 当前运行内的目标尝试序号；识别/运行控制记录为 0。
        /// </summary>
        public long AttemptId { get; private set; }

        public override string ToString()
        {
            string target = this.Target == null
                ? "none"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "slot={0},scene={1},x={2},y={3}",
                    this.Target.PackageNum,
                    this.Target.Scene,
                    this.Target.X,
                    this.Target.Y);
            return string.Format(
                CultureInfo.InvariantCulture,
                "treasure-map timestamp={0:O} runId={1} step={2} code={3} success={4} packet={5} target={6} retry={7} attemptId={8}",
                this.TimestampUtc,
                this.RunId == Guid.Empty ? "none" : this.RunId.ToString("D"),
                this.Step,
                this.Code,
                this.Success,
                this.Packet,
                target,
                this.Retry,
                this.AttemptId);
        }
    }

    /// <summary>
    /// 执行藏宝图指令的安全状态机。
    /// 改造为可暂停、可终止、不会盲目重复自动挖宝包。
    /// </summary>
    public sealed class TreasureMapPresetRunner
    {
        private readonly ITreasureC6InventoryStream inventoryStream;
        private readonly ITreasureMapPacketSender packetSender;
        private readonly Func<TreasurePacketRoute> routeProvider;
        private readonly Action<TreasureMapLogEntry> logger;
        private readonly TreasureMapPresetOptions options;

        // 运行控制
        private readonly CancellationTokenSource internalCts;
        private readonly ManualResetEventSlim pauseGate;
        private volatile bool isPaused;
        private volatile bool isStopping;

        // Exclusive is the safe default: only one treasure-map runner may
        // own the live action boundary in this process at a time. Cooperative
        // callers can opt out explicitly when they coordinate externally.
        private static int exclusiveRunnerActive;
        private bool exclusiveRunnerLeaseHeld;

        // 同一份 C6 快照可能连续产生多条事件；只记录识别状态变化，
        // 避免“未识别/等待新目标”日志淹没真正的发送结果。
        private string lastRecognitionFingerprint = string.Empty;
        private DateTime lastEmptyRecognitionLogUtc = DateTime.MinValue;

        // 运行状态
        private TreasureMapState currentState = TreasureMapState.Idle;
        private string lastError = string.Empty;
        private Guid currentRunId;
        private long hookSessionVersion;
        private long nextAttemptId;
        private long currentAttemptId;
        private DateTime lastJumpDispatchedUtc = DateTime.MinValue;

        // 动作账本
        private List<TreasureMapActionLog> actionLog = new List<TreasureMapActionLog>();

        // C6 连接在快照轮换或读取器短暂重启时可能丢失基线。恢复次数有限，
        // 避免在服务持续异常时无限重连；每次恢复都必须重新拿到完整快照。
        private const int C6ResyncMaxAttempts = 3;
        private const int C6ResyncDelayMilliseconds = 250;
        private const int C6ResyncRetryDelayMilliseconds = 1000;
        private const int JumpNotDispatchedMaxRetries = 2;
        private const int ConsumptionPollIntervalMilliseconds = 25;
        private const int ArrivalEvidencePollIntervalMilliseconds = 25;
        private const long C6WaitDiagnosticThresholdMilliseconds = 500;
        private const int EmptyRecognitionHeartbeatMilliseconds = 5000;

        public TreasureMapPresetRunner(
            ITreasureC6InventoryStream inventoryStream,
            ITreasureMapPacketSender packetSender,
            Func<TreasurePacketRoute> routeProvider,
            Action<TreasureMapLogEntry> logger,
            TreasureMapPresetOptions options)
            : this(
                inventoryStream,
                packetSender,
                routeProvider,
                logger,
                options,
                new CancellationTokenSource(),
                new ManualResetEventSlim(true))
        {
        }

        internal TreasureMapPresetRunner(
            ITreasureC6InventoryStream inventoryStream,
            ITreasureMapPacketSender packetSender,
            Func<TreasurePacketRoute> routeProvider,
            Action<TreasureMapLogEntry> logger,
            TreasureMapPresetOptions options,
            CancellationTokenSource externalCts,
            ManualResetEventSlim externalPauseGate)
        {
            this.inventoryStream = inventoryStream ?? throw new ArgumentNullException(nameof(inventoryStream));
            this.packetSender = packetSender ?? throw new ArgumentNullException(nameof(packetSender));
            this.routeProvider = routeProvider ?? throw new ArgumentNullException(nameof(routeProvider));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.options.Validate();

            // 使用外部传入的取消令牌和暂停门（用于集成到机器人控制）
            this.internalCts = externalCts ?? new CancellationTokenSource();
            this.pauseGate = externalPauseGate ?? new ManualResetEventSlim(true);
        }

        /// <summary>
        /// 获取当前运行状态
        /// </summary>
        public TreasureMapState CurrentState => this.currentState;

        public string LastError => this.lastError;

        /// <summary>
        /// 获取当前运行ID
        /// </summary>
        public Guid CurrentRunId => this.currentRunId;

        /// <summary>
        /// 暂停运行
        /// </summary>
        public void Pause()
        {
            if (this.currentState == TreasureMapState.Paused ||
                this.currentState == TreasureMapState.Stopping ||
                this.currentState == TreasureMapState.Completed ||
                this.currentState == TreasureMapState.Failed ||
                this.currentState == TreasureMapState.Ambiguous)
            {
                return;
            }
            this.isPaused = true;
            this.pauseGate.Reset();
            this.currentState = TreasureMapState.Paused;
        }

        /// <summary>
        /// 恢复运行
        /// </summary>
        public bool Resume()
        {
            if (this.currentState != TreasureMapState.Paused)
            {
                return false;
            }
            this.isPaused = false;
            this.pauseGate.Set();
            return true;
        }

        /// <summary>
        /// 停止运行
        /// </summary>
        public void Stop()
        {
            this.isStopping = true;
            this.internalCts.Cancel();
        }

        /// <summary>
        /// 检查是否已暂停
        /// </summary>
        public bool IsPaused => this.isPaused && this.currentState != TreasureMapState.Stopping;

        /// <summary>
        /// 检查是否正在停止
        /// </summary>
        public bool IsStopping => this.isStopping;

        /// <summary>
        /// 获取动作账本（只读）
        /// </summary>
        public IReadOnlyList<TreasureMapActionLog> ActionLog => this.actionLog.AsReadOnly();

        /// <summary>
        /// 启动藏宝图运行（一次模式或持续模式）
        /// </summary>
        public void Run()
        {
            this.Run(this.internalCts?.Token ?? CancellationToken.None);
        }

        /// <summary>
        /// 启动藏宝图运行（一次模式或持续模式），接受外部取消令牌
        /// </summary>
        public void Run(CancellationToken externalToken)
        {
            this.lastError = string.Empty;
            this.currentRunId = Guid.NewGuid();
            this.actionLog.Clear();
            this.lastRecognitionFingerprint = string.Empty;
            this.lastEmptyRecognitionLogUtc = DateTime.MinValue;
            this.nextAttemptId = 0;
            this.currentAttemptId = 0;
            this.lastJumpDispatchedUtc = DateTime.MinValue;
            // Evidence is process/session scoped. Do not let an observation
            // from a previous run satisfy the first Jump of this run.
            TreasureC6StreamObservation.ClearEvidence();

            this.Log(
                "run",
                "Control",
                null,
                "started:" + this.options.Mode.ToString(),
                0,
                true);

            try
            {
                // 批次A：检查授权
                if (!this.options.LiveSendEnabled)
                {
                    this.Log("preflight", "authorization", null, "live_send_not_authorized", 0, false);
                    this.lastError = "live_send_not_authorized";
                    this.currentState = TreasureMapState.Failed;
                    return;
                }

                if (this.options.ControlMode == TreasureControlMode.Exclusive &&
                    Interlocked.CompareExchange(ref exclusiveRunnerActive, 1, 0) != 0)
                {
                    this.lastError = "controller_conflict";
                    this.currentState = TreasureMapState.ControllerConflict;
                    this.Log(
                        "control",
                        "Control",
                        null,
                        "controller_conflict",
                        0,
                        false);
                    return;
                }

                this.exclusiveRunnerLeaseHeld =
                    this.options.ControlMode == TreasureControlMode.Exclusive;

                this.hookSessionVersion = TreasurePacketRuntime.GetSessionVersion();

                // 根据模式执行
                if (this.options.Mode == TreasureMapExecutionMode.CurrentSnapshot)
                {
                    this.RunCurrentSnapshotMode(externalToken);
                }
                else
                {
                    this.RunContinuousMode(externalToken);
                }
            }
            finally
            {
                bool completedWithoutFailure =
                    this.currentState != TreasureMapState.Failed &&
                    this.currentState != TreasureMapState.Ambiguous &&
                    this.currentState != TreasureMapState.ControllerConflict;
                this.Log(
                    "run",
                    "Control",
                    null,
                    "ended:" + this.currentState.ToString(),
                    0,
                    completedWithoutFailure);

                if (this.exclusiveRunnerLeaseHeld)
                {
                    this.exclusiveRunnerLeaseHeld = false;
                    Interlocked.Exchange(ref exclusiveRunnerActive, 0);
                }
            }
        }

        /// <summary>
        /// 一次模式：处理当前快照中的所有目标，完成后退出
        /// </summary>
        private void RunCurrentSnapshotMode(CancellationToken externalToken)
        {
            this.currentState = TreasureMapState.Preflight;

            try
            {
                // 连接C6并获取完整快照
                this.ConnectToC6();

                // 冻结首个完整快照中的目标身份集合
                int c6RecoveryAttempts = 0;
                HashSet<TreasureMapTargetIdentity> frozenTargets =
                    this.GetFrozenTargets(externalToken, ref c6RecoveryAttempts);
                Dictionary<TreasureMapTargetIdentity, TreasureMapTargetVersion> frozenVersions =
                    this.GetTargetVersions(frozenTargets);

                if (frozenTargets.Count == 0)
                {
                    this.LogRecognitionStatus(
                        this.inventoryStream.CurrentSnapshot,
                        this.inventoryStream.CurrentSnapshot == null
                            ? null
                            : this.inventoryStream.CurrentSnapshot.Items,
                        "target_not_recognized",
                        null);
                    this.currentState = TreasureMapState.Completed;
                    return;
                }

                // 按 packageNum 排序处理目标
                List<TreasureMapTargetIdentity> sortedTargets = frozenTargets
                    .OrderBy(t => t.PackageNum)
                    .ThenBy(t => t.MemberIdentity, StringComparer.Ordinal)
                    .ToList();

                for (int targetIndex = 0; targetIndex < sortedTargets.Count; targetIndex++)
                {
                    TreasureMapTargetIdentity target = sortedTargets[targetIndex];
                    this.Checkpoint(externalToken);
                    if (this.isStopping || externalToken.IsCancellationRequested)
                    {
                        this.currentState = TreasureMapState.Stopping;
                        return;
                    }

                    TreasureMapTargetVersion frozenVersion;
                    if (!frozenVersions.TryGetValue(target, out frozenVersion))
                    {
                        this.currentState = TreasureMapState.Failed;
                        return;
                    }

                    this.ProcessSingleTarget(target, externalToken, frozenVersion);
                    if (this.currentState == TreasureMapState.Ambiguous ||
                        this.currentState == TreasureMapState.Failed)
                    {
                        return;
                    }

                    if (this.currentState == TreasureMapState.TargetCompleted &&
                        targetIndex < sortedTargets.Count - 1)
                    {
                        this.currentState = TreasureMapState.WaitingNextTarget;
                        this.WaitForMinimumDelay(
                            this.options.NextTargetDelayMilliseconds,
                            externalToken);
                    }
                }

                this.currentState = TreasureMapState.Completed;
            }
            catch (OperationCanceledException)
            {
                this.currentState = TreasureMapState.Stopping;
            }
            catch (Exception ex)
            {
                this.lastError = ex is TreasureC6TransportException
                    ? ((TreasureC6TransportException)ex).Code
                    : "runner_exception:" + ex.Message;
                this.Log("runner", "exception", null, this.lastError, 0, false);
                this.currentState = TreasureMapState.Failed;
            }
        }

        /// <summary>
        /// 持续模式：持续消费新快照和成员事件
        /// </summary>
        private void RunContinuousMode(CancellationToken externalToken)
        {
            this.currentState = TreasureMapState.WaitingSnapshot;

            try
            {
                Dictionary<TreasureMapTargetIdentity, TreasureMapTargetVersion> completedTargets =
                    new Dictionary<TreasureMapTargetIdentity, TreasureMapTargetVersion>();
                Dictionary<string, TreasureMapTargetVersion> skippedTargets =
                    new Dictionary<string, TreasureMapTargetVersion>(StringComparer.Ordinal);
                int c6RecoveryAttempts = 0;

                while (!this.isStopping &&
                       !this.internalCts.Token.IsCancellationRequested &&
                       !externalToken.IsCancellationRequested)
                {
                    this.Checkpoint(externalToken);
                    this.EnsureC6ConnectedWithRecovery(
                        externalToken,
                        ref c6RecoveryAttempts);

                    // 先确保已有完整快照；后续循环只在没有可处理成员时读取下一条事件。
                    this.currentState = TreasureMapState.WaitingSnapshot;
                    this.ReadUntilSnapshotWithRecovery(
                        externalToken,
                        ref c6RecoveryAttempts,
                        true);

                    if (this.isStopping || externalToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // 获取当前成员
                    var currentItems = this.GetCurrentMembers();
                    this.PruneTargetLedgers(
                        this.inventoryStream.CurrentSnapshot,
                        completedTargets,
                        skippedTargets);
                    if (currentItems.Count == 0)
                    {
                        this.LogRecognitionStatus(
                            this.inventoryStream.CurrentSnapshot,
                            currentItems,
                            "snapshot_empty",
                            null);
                        this.currentState = TreasureMapState.WaitingNextTarget;
                        this.ReadNextEventWithRecovery(
                            externalToken,
                            ref c6RecoveryAttempts,
                            true);
                        continue;
                    }

                    List<TreasureC6InventoryItem> candidateItems = currentItems
                        .Where(item => item != null && item.PackageNum > 0)
                        .ToList();
                    if (candidateItems.Count == 0)
                    {
                        this.LogRecognitionStatus(
                            this.inventoryStream.CurrentSnapshot,
                            currentItems,
                            "target_not_recognized",
                            null);
                        this.currentState = TreasureMapState.WaitingNextTarget;
                        this.ReadNextEventWithRecovery(
                            externalToken,
                            ref c6RecoveryAttempts,
                            true);
                        continue;
                    }

                    // 选择并处理目标
                    var target = this.SelectNextTarget(
                        currentItems,
                        completedTargets,
                        skippedTargets);
                    if (target != null)
                    {
                        this.LogRecognitionStatus(
                            this.inventoryStream.CurrentSnapshot,
                            currentItems,
                            "target_detected",
                            this.FindCurrentItem(target));
                        this.ProcessSingleTarget(target, externalToken, null);
                        if (this.currentState == TreasureMapState.TargetCompleted)
                        {
                            // ProcessSingleTarget 只有在原目标已经离开背包或槽位
                            // 已换成新图后才会完成，因此不能把当前槽位的新版本写入
                            // completedTargets，否则会误跳过刚进入该槽位的下一张图。
                            // 成功目标和下一张之间保持固定节拍；没有新图时，
                            // 后续仍由 C6 事件驱动，不额外启动轮询定时器。
                            this.currentState = TreasureMapState.WaitingNextTarget;
                            this.WaitForMinimumDelay(
                                this.options.NextTargetDelayMilliseconds,
                                externalToken);
                        }
                        else if (this.currentState == TreasureMapState.Ambiguous ||
                                 this.currentState == TreasureMapState.Failed)
                        {
                            TreasureMapTargetVersion failedVersion;
                            this.TryGetTargetVersion(target, out failedVersion);
                            skippedTargets[BuildTargetSlotKey(target)] = failedVersion;
                            this.Log(
                                "target",
                                "",
                                this.FindCurrentItem(target),
                                "target_failed_skipped",
                                0,
                                false);
                            // 持续模式下失败目标直接跳过，等待下一张，不结束助手。
                            this.currentState = TreasureMapState.WaitingNextTarget;
                        }
                    }
                    else
                    {
                        this.LogRecognitionStatus(
                            this.inventoryStream.CurrentSnapshot,
                            currentItems,
                            "waiting_new_target",
                            null);
                        this.currentState = TreasureMapState.WaitingNextTarget;
                        this.ReadNextEventWithRecovery(
                            externalToken,
                            ref c6RecoveryAttempts,
                            true);
                    }
                }

                this.currentState = TreasureMapState.Stopping;
            }
            catch (OperationCanceledException)
            {
                this.currentState = TreasureMapState.Stopping;
            }
            catch (Exception ex)
            {
                this.lastError = ex is TreasureC6TransportException
                    ? ((TreasureC6TransportException)ex).Code
                    : "runner_exception:" + ex.Message;
                this.Log("runner", "exception", null, this.lastError, 0, false);
                this.currentState = TreasureMapState.Failed;
            }
        }

        /// <summary>
        /// 处理单个目标的完整流程
        /// </summary>
        private void ProcessSingleTarget(
            TreasureMapTargetIdentity target,
            CancellationToken externalToken,
            TreasureMapTargetVersion expectedVersion)
        {
            TreasureMapTargetVersion targetVersion = expectedVersion;
            if (targetVersion == null && !this.TryGetTargetVersion(target, out targetVersion))
            {
                this.Log(
                    "recognition",
                    "C6",
                    this.FindCurrentItem(target),
                    "target_missing_before_jump",
                    0,
                    false);
                this.currentState = TreasureMapState.Failed;
                return;
            }

            int consumptionRetryCount = 0;
            bool midRouteJumpRetryIssued = false;
            while (true)
            {
                this.currentState = TreasureMapState.SelectingTarget;

                // 冷却或确认等待期间，原图可能已被游戏延迟消耗；此时直接完成，
                // 不能再对已经换入同一槽位的新图发送旧目标封包。
                if (consumptionRetryCount > 0 &&
                    this.IsTargetConsumptionConfirmed(target, targetVersion))
                {
                    this.CompleteTarget(target, targetVersion, consumptionRetryCount, "consume_confirmed");
                    return;
                }

                if (!this.IsHookSessionStable())
                {
                    this.RecordAction(target, TreasureMapState.Ambiguous, "hook_session_changed", 0);
                    this.currentState = TreasureMapState.Ambiguous;
                    return;
                }

                if (!this.ValidateTarget(target, targetVersion))
                {
                    this.Log(
                        "recognition",
                        "C6",
                        this.BuildLogTarget(target, targetVersion),
                        "target_validation_failed",
                        consumptionRetryCount,
                        false);
                    this.currentState = TreasureMapState.Failed;
                    return;
                }

                long attemptId = ++this.nextAttemptId;
                this.currentAttemptId = attemptId;

                if (consumptionRetryCount == 0 &&
                    !this.WaitForTargetStability(
                        target,
                        targetVersion,
                        externalToken,
                        attemptId))
                {
                    return;
                }

                // 生产 Jump→Use 路径第一次超时只补发 Use，避免重复跳转；
                // AutoDig 路径首次尝试已经把角色送到目标，后续消费超时只
                // 重发挖宝包，不再每隔一个确认窗口重复发送同一坐标的 Jump。
                bool sendJump = consumptionRetryCount == 0 ||
                    (!this.options.UseNativeAutoDig && consumptionRetryCount != 1);
                if (sendJump)
                {
                    this.currentState = TreasureMapState.PreparingJump;
                    this.currentState = TreasureMapState.SendingJump;
                    this.WaitForJumpThrottle(
                        target,
                        targetVersion,
                        externalToken,
                        attemptId);
                    if (!this.EnsureTargetReadyForJump(
                        target,
                        targetVersion,
                        consumptionRetryCount,
                        attemptId))
                    {
                        return;
                    }
                    DateTime jumpDispatchedUtc;
                    TreasureMapPacketSendResult jumpResult = this.SendJumpWithRetry(
                        target,
                        targetVersion,
                        externalToken,
                        attemptId,
                        out jumpDispatchedUtc);
                    if (jumpResult.Disposition == TreasureMapPacketSendDisposition.Dispatched)
                    {
                        this.lastJumpDispatchedUtc = jumpDispatchedUtc;
                    }
                    this.Log(
                        "jump",
                        "Jump",
                        this.BuildLogTarget(target, targetVersion),
                        jumpResult.Code,
                        consumptionRetryCount,
                        jumpResult.Disposition == TreasureMapPacketSendDisposition.Dispatched,
                        attemptId);

                    if (jumpResult.Code == "live_send_not_authorized")
                    {
                        this.currentState = TreasureMapState.Failed;
                        return;
                    }

                    if (jumpResult.Disposition == TreasureMapPacketSendDisposition.Ambiguous)
                    {
                        this.RecordAction(target, TreasureMapState.Ambiguous, jumpResult.Code, 0);
                        this.currentState = TreasureMapState.Ambiguous;
                        return;
                    }

                    if (jumpResult.Disposition == TreasureMapPacketSendDisposition.NotDispatched)
                    {
                        this.RecordAction(target, TreasureMapState.SendingJump, jumpResult.Code, 0);
                        this.currentState = TreasureMapState.Failed;
                        return;
                    }

                    // Jump 后先完成配置的最短节奏等待。Shadow 模式仅消费
                    // 已到达的观察并记录；Enforced 模式还要求匹配证据。
                    this.currentState = TreasureMapState.WaitingArrival;
                    this.WaitForMinimumDelay(this.options.JumpUseDelayMilliseconds, externalToken);
                    bool arrivalConfirmed = this.WaitForArrivalEvidence(
                        target,
                        targetVersion,
                        attemptId,
                        externalToken,
                        jumpResult,
                        jumpDispatchedUtc);
                    if (this.options.EvidenceMode == TreasureEvidenceMode.Enforced &&
                        !arrivalConfirmed)
                    {
                        this.lastError = "arrival_evidence_timeout";
                        this.currentState = TreasureMapState.Failed;
                        return;
                    }
                }

                TreasureMapPacketSendResult actionResult;
                TreasureMapState actionState;
                string actionStep;
                string actionPacket;
                string completedDetail;
                if (this.options.UseNativeAutoDig)
                {
                    // 兼容回归路径：Jump 后发送固定的原生自动挖宝帧。
                    this.currentState = TreasureMapState.PreparingAutoDig;
                    this.currentState = TreasureMapState.SendingAutoDig;
                    actionResult = this.SendAutoDig(
                        target,
                        targetVersion,
                        externalToken,
                        attemptId);
                    actionState = TreasureMapState.SendingAutoDig;
                    actionStep = "auto_dig";
                    actionPacket = "AutoDig";
                    completedDetail = "auto_dig_dispatched";
                }
                else
                {
                    // 生产路径：首次 Jump→Use，首次确认超时后只补发 Use。
                    this.currentState = TreasureMapState.PreparingUse;
                    this.currentState = TreasureMapState.SendingUse;
                    actionResult = this.SendUse(target, targetVersion, externalToken, attemptId);
                    actionState = TreasureMapState.SendingUse;
                    actionStep = "use";
                    actionPacket = "Use";
                    completedDetail = "use_dispatched";
                }

                this.Log(
                    actionStep,
                    actionPacket,
                    this.BuildLogTarget(target, targetVersion),
                    actionResult.Code,
                    consumptionRetryCount,
                    actionResult.Disposition == TreasureMapPacketSendDisposition.Dispatched,
                    attemptId);

                if (actionResult.Disposition == TreasureMapPacketSendDisposition.Ambiguous)
                {
                    this.RecordAction(target, TreasureMapState.Ambiguous, actionResult.Code, 0);
                    this.currentState = TreasureMapState.Ambiguous;
                    return;
                }

                if (actionResult.Code == "live_send_not_authorized")
                {
                    this.currentState = TreasureMapState.Failed;
                    return;
                }

                if (actionResult.Disposition == TreasureMapPacketSendDisposition.NotDispatched)
                {
                    this.RecordAction(target, actionState, actionResult.Code, 0);
                    this.currentState = TreasureMapState.Failed;
                    return;
                }

                // 一次模式保持原有语义；持续助手才依赖持续更新的 C6 背包流
                // 确认服务器确实消耗了目标。
                if (this.options.Mode != TreasureMapExecutionMode.Continuous)
                {
                    this.CompleteTarget(target, targetVersion, consumptionRetryCount, completedDetail);
                    return;
                }

                if (this.WaitForTargetConsumption(
                    target,
                    targetVersion,
                    consumptionRetryCount,
                    this.options.ConsumptionConfirmTimeoutMilliseconds,
                    true,
                    sendJump && !midRouteJumpRetryIssued,
                    ref midRouteJumpRetryIssued,
                    externalToken,
                    attemptId))
                {
                    this.CompleteTarget(target, targetVersion, consumptionRetryCount, "consume_confirmed");
                    return;
                }

                consumptionRetryCount++;
                if (consumptionRetryCount == 1)
                {
                    this.Log(
                        "target_retry",
                        this.options.UseNativeAutoDig ? "AutoDig" : "Use",
                        this.BuildLogTarget(target, targetVersion),
                        "target_retry",
                        consumptionRetryCount,
                        false,
                        attemptId);
                    continue;
                }

                this.Log(
                    "target_cooldown",
                    this.options.UseNativeAutoDig ? "AutoDig" : "Jump+Use",
                    this.BuildLogTarget(target, targetVersion),
                    "target_cooldown",
                    consumptionRetryCount,
                    false,
                    attemptId);
                if (this.WaitForTargetConsumption(
                    target,
                    targetVersion,
                    consumptionRetryCount,
                    this.options.FailedTargetCooldownMilliseconds,
                    false,
                    false,
                    ref midRouteJumpRetryIssued,
                    externalToken,
                    attemptId))
                {
                    this.CompleteTarget(
                        target,
                        targetVersion,
                        consumptionRetryCount,
                        "consume_confirmed");
                    return;
                }
            }
        }

        private void ConnectToC6()
        {
            if (!this.inventoryStream.IsConnected)
            {
                this.inventoryStream.Connect(this.internalCts.Token);
                this.Log("stream", "C6", null, "connected", 0, true);
            }

            TreasureC6StreamObservation.PublishSnapshot(this.inventoryStream.CurrentSnapshot);
        }

        private void EnsureC6ConnectedWithRecovery(
            CancellationToken externalToken,
            ref int c6RecoveryAttempts)
        {
            while (!this.inventoryStream.IsConnected)
            {
                try
                {
                    this.ConnectToC6();
                    c6RecoveryAttempts = 0;
                    return;
                }
                catch (Exception ex) when (IsC6ResyncRequired(ex))
                {
                    this.ResyncC6Stream(
                        ex,
                        externalToken,
                        ref c6RecoveryAttempts,
                        true);
                }
            }
        }

        private HashSet<TreasureMapTargetIdentity> GetFrozenTargets(
            CancellationToken externalToken,
            ref int c6RecoveryAttempts)
        {
            var snapshot = this.inventoryStream.CurrentSnapshot;
            if (snapshot == null)
            {
                this.currentState = TreasureMapState.WaitingSnapshot;
                this.ReadUntilSnapshotWithRecovery(
                    externalToken,
                    ref c6RecoveryAttempts,
                    false);
                snapshot = this.inventoryStream.CurrentSnapshot;
            }

            if (snapshot == null)
            {
                return new HashSet<TreasureMapTargetIdentity>();
            }

            TreasureC6StreamObservation.PublishSnapshot(snapshot);

            var targets = new HashSet<TreasureMapTargetIdentity>();
            foreach (var item in snapshot.Items)
            {
                if (item.TryCreateTarget(out TreasureInventoryTarget target))
                {
                    var identity = new TreasureMapTargetIdentity(
                        snapshot.StreamSessionId,
                        snapshot.ProcessIdentity,
                        snapshot.ContainerIdentity,
                        item.MemberIdentity,
                        item.PackageNum);
                    targets.Add(identity);
                }
            }
            return targets;
        }

        private Dictionary<TreasureMapTargetIdentity, TreasureMapTargetVersion> GetTargetVersions(
            IEnumerable<TreasureMapTargetIdentity> targets)
        {
            Dictionary<TreasureMapTargetIdentity, TreasureMapTargetVersion> versions =
                new Dictionary<TreasureMapTargetIdentity, TreasureMapTargetVersion>();
            TreasureC6InventorySnapshot snapshot = this.inventoryStream.CurrentSnapshot;
            if (snapshot == null || targets == null)
            {
                return versions;
            }

            foreach (TreasureMapTargetIdentity target in targets)
            {
                TreasureC6InventoryItem item = snapshot.Items.FirstOrDefault(i =>
                    i.MemberIdentity == target.MemberIdentity &&
                    i.PackageNum == target.PackageNum);
                if (item != null)
                {
                    versions[target] = new TreasureMapTargetVersion(
                        item.Scene,
                        item.X,
                        item.Y);
                }
            }
            return versions;
        }

        private List<TreasureC6InventoryItem> GetCurrentMembers()
        {
            var snapshot = this.inventoryStream.CurrentSnapshot;
            TreasureC6StreamObservation.PublishSnapshot(snapshot);
            if (snapshot != null)
            {
                return snapshot.Items.ToList();
            }
            return new List<TreasureC6InventoryItem>();
        }

        private TreasureMapTargetIdentity SelectNextTarget(
            List<TreasureC6InventoryItem> items,
            Dictionary<TreasureMapTargetIdentity, TreasureMapTargetVersion> completedTargets,
            Dictionary<string, TreasureMapTargetVersion> skippedTargets)
        {
            var snapshot = this.inventoryStream.CurrentSnapshot;
            if (snapshot == null)
            {
                return null;
            }

            foreach (TreasureC6InventoryItem item in items
                .Where(item => item.PackageNum > 0)
                .OrderBy(item => item.PackageNum)
                .ThenBy(item => item.MemberIdentity, StringComparer.Ordinal))
            {
                TreasureMapTargetIdentity identity = new TreasureMapTargetIdentity(
                    snapshot.StreamSessionId,
                    snapshot.ProcessIdentity,
                    snapshot.ContainerIdentity,
                    item.MemberIdentity,
                    item.PackageNum);
                TreasureMapTargetVersion version = new TreasureMapTargetVersion(
                    item.Scene,
                    item.X,
                    item.Y);
                TreasureMapTargetVersion skippedVersion;
                if (skippedTargets != null &&
                    skippedTargets.TryGetValue(BuildTargetSlotKey(identity), out skippedVersion) &&
                    (skippedVersion == null || skippedVersion.Equals(version)))
                {
                    continue;
                }

                TreasureMapTargetVersion completedVersion;
                if (completedTargets == null ||
                    !completedTargets.TryGetValue(identity, out completedVersion) ||
                    !completedVersion.Equals(version))
                {
                    return identity;
                }
            }

            return null;
        }

        /// <summary>
        /// 清理已经离开背包的目标记录。
        ///
        /// C6 的 packageNum/memberIdentity 是背包槽位身份，不是藏宝图实例的
        /// 永久 ID。自动挖宝成功后，同一槽位可能再次放入一张新图；如果不在槽位
        /// 消失时清理账本，新图会被误判为上一张已完成/已跳过的图。
        /// </summary>
        private void PruneTargetLedgers(
            TreasureC6InventorySnapshot snapshot,
            Dictionary<TreasureMapTargetIdentity, TreasureMapTargetVersion> completedTargets,
            Dictionary<string, TreasureMapTargetVersion> skippedTargets)
        {
            if (snapshot == null)
            {
                return;
            }

            HashSet<string> activeSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (TreasureC6InventoryItem item in snapshot.Items)
            {
                if (item.PackageNum > 0)
                {
                    activeSlots.Add(BuildTargetSlotKey(item.MemberIdentity, item.PackageNum));
                }
            }

            if (completedTargets != null)
            {
                List<TreasureMapTargetIdentity> staleCompleted = completedTargets.Keys
                    .Where(target =>
                        !IsSameEvidenceEpoch(target, snapshot) ||
                        !activeSlots.Contains(BuildTargetSlotKey(target)))
                    .ToList();
                foreach (TreasureMapTargetIdentity target in staleCompleted)
                {
                    completedTargets.Remove(target);
                }
            }

            if (skippedTargets != null)
            {
                List<string> staleSkipped = skippedTargets.Keys
                    .Where(slotKey => !activeSlots.Contains(slotKey))
                    .ToList();
                foreach (string slotKey in staleSkipped)
                {
                    skippedTargets.Remove(slotKey);
                }
            }
        }

        private static bool IsSameEvidenceEpoch(
            TreasureMapTargetIdentity target,
            TreasureC6InventorySnapshot snapshot)
        {
            return target != null &&
                snapshot != null &&
                string.Equals(target.StreamSessionId, snapshot.StreamSessionId, StringComparison.Ordinal) &&
                string.Equals(target.ProcessIdentity, snapshot.ProcessIdentity, StringComparison.Ordinal) &&
                string.Equals(target.ContainerIdentity, snapshot.ContainerIdentity, StringComparison.Ordinal);
        }

        private static string BuildTargetSlotKey(TreasureMapTargetIdentity target)
        {
            return target == null
                ? string.Empty
                : BuildTargetSlotKey(target.MemberIdentity, target.PackageNum);
        }

        private static string BuildTargetSlotKey(string memberIdentity, int packageNum)
        {
            string member = memberIdentity ?? string.Empty;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1}:{2}",
                member.Length,
                member,
                packageNum);
        }

        private bool TryGetTargetVersion(
            TreasureMapTargetIdentity target,
            out TreasureMapTargetVersion targetVersion)
        {
            targetVersion = null;
            var snapshot = this.inventoryStream.CurrentSnapshot;
            if (snapshot == null || target == null)
            {
                return false;
            }

            TreasureC6InventoryItem item = snapshot.Items.FirstOrDefault(i =>
                string.Equals(i.MemberIdentity, target.MemberIdentity, StringComparison.Ordinal) &&
                i.PackageNum == target.PackageNum);
            if (item == null)
            {
                return false;
            }

            targetVersion = new TreasureMapTargetVersion(item.Scene, item.X, item.Y);
            return true;
        }

        private bool ValidateTarget(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion)
        {
            var snapshot = this.inventoryStream.CurrentSnapshot;
            if (snapshot == null || target == null || expectedVersion == null)
            {
                return false;
            }

            // 验证身份变化
            if (snapshot.StreamSessionId != target.StreamSessionId ||
                snapshot.ProcessIdentity != target.ProcessIdentity ||
                snapshot.ContainerIdentity != target.ContainerIdentity)
            {
                this.Log("target", "validation", null, "session_identity_changed", 0, false);
                return false;
            }

            TreasureC6InventoryItem currentItem = snapshot.Items.FirstOrDefault(i =>
                i.MemberIdentity == target.MemberIdentity && i.PackageNum == target.PackageNum);
            if (currentItem == null)
            {
                return false;
            }

            return currentItem.Scene == expectedVersion.Scene &&
                currentItem.X == expectedVersion.X &&
                currentItem.Y == expectedVersion.Y;
        }

        private bool WaitForTargetStability(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion,
            CancellationToken externalToken,
            long attemptId)
        {
            int requiredMilliseconds = this.options.TargetStabilityMilliseconds;
            if (requiredMilliseconds <= 0)
            {
                return true;
            }

            Stopwatch wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < requiredMilliseconds)
            {
                this.Checkpoint(externalToken);

                // 目标在稳定窗口内离开背包或换成新版本时，不发送旧目标；
                // 直接沿用现有消费确认路径，让持续模式继续处理下一张。
                if (this.IsTargetConsumptionConfirmed(target, expectedVersion))
                {
                    this.Log(
                        "native_auto_use",
                        "C6",
                        this.BuildLogTarget(target, expectedVersion),
                        "native_consumed_before_jump",
                        0,
                        true,
                        attemptId);
                    this.CompleteTarget(target, expectedVersion, 0, "consume_confirmed");
                    return false;
                }

                if (!this.IsHookSessionStable())
                {
                    this.RecordAction(target, TreasureMapState.Ambiguous, "hook_session_changed", 0);
                    this.currentState = TreasureMapState.Ambiguous;
                    return false;
                }

                if (!this.ValidateTarget(target, expectedVersion))
                {
                    this.Log(
                        "recognition",
                        "C6",
                        this.BuildLogTarget(target, expectedVersion),
                        "target_changed_during_stability",
                        0,
                        false,
                        attemptId);
                    this.currentState = TreasureMapState.Failed;
                    return false;
                }

                int remainingMilliseconds = requiredMilliseconds - (int)wait.ElapsedMilliseconds;
                this.WaitForMinimumDelay(
                    Math.Min(ConsumptionPollIntervalMilliseconds, Math.Max(1, remainingMilliseconds)),
                    externalToken);
            }

            if (this.IsTargetConsumptionConfirmed(target, expectedVersion))
            {
                this.Log(
                    "native_auto_use",
                    "C6",
                    this.BuildLogTarget(target, expectedVersion),
                    "native_consumed_before_jump",
                    0,
                    true,
                    attemptId);
                this.CompleteTarget(target, expectedVersion, 0, "consume_confirmed");
                return false;
            }

            if (!this.IsHookSessionStable() || !this.ValidateTarget(target, expectedVersion))
            {
                this.Log(
                    "recognition",
                    "C6",
                    this.BuildLogTarget(target, expectedVersion),
                    "target_changed_after_stability",
                    0,
                    false,
                    attemptId);
                this.currentState = TreasureMapState.Failed;
                return false;
            }

            this.Log(
                "target_stable",
                "C6",
                this.BuildLogTarget(target, expectedVersion),
                "stable_for_ms:" + wait.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                0,
                true,
                attemptId);
            return true;
        }

        private bool EnsureTargetReadyForJump(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion,
            int retryCount,
            long attemptId)
        {
            if (!this.IsHookSessionStable())
            {
                this.RecordAction(target, TreasureMapState.Ambiguous, "hook_session_changed_before_jump", retryCount);
                this.currentState = TreasureMapState.Ambiguous;
                return false;
            }

            if (this.ValidateTarget(target, expectedVersion))
            {
                return true;
            }

            if (this.IsTargetConsumptionConfirmed(target, expectedVersion))
            {
                this.Log(
                    "native_auto_use",
                    "C6",
                    this.BuildLogTarget(target, expectedVersion),
                    "native_consumed_before_jump",
                    retryCount,
                    true,
                    attemptId);
                this.CompleteTarget(target, expectedVersion, retryCount, "consume_confirmed");
                return false;
            }

            this.Log(
                "recognition",
                "C6",
                this.BuildLogTarget(target, expectedVersion),
                "target_changed_before_jump",
                retryCount,
                false,
                attemptId);
            this.currentState = TreasureMapState.Failed;
            return false;
        }

        private void WaitForJumpThrottle(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion,
            CancellationToken externalToken,
            long attemptId)
        {
            int minimumMilliseconds = this.options.MinimumJumpIntervalMilliseconds;
            if (minimumMilliseconds <= 0 || this.lastJumpDispatchedUtc == DateTime.MinValue)
            {
                return;
            }

            DateTime deadline = this.lastJumpDispatchedUtc.AddMilliseconds(minimumMilliseconds);
            double remaining = (deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remaining <= 0)
            {
                return;
            }

            int waitMilliseconds = Math.Max(1, (int)Math.Ceiling(remaining));
            this.Log(
                "jump_throttle_wait",
                "Jump",
                this.BuildLogTarget(target, expectedVersion),
                "wait_ms:" + waitMilliseconds.ToString(CultureInfo.InvariantCulture),
                0,
                false,
                attemptId);
            this.WaitForMinimumDelay(waitMilliseconds, externalToken);
        }

        private bool WaitForArrivalEvidence(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion,
            long attemptId,
            CancellationToken externalToken,
            TreasureMapPacketSendResult jumpResult,
            DateTime jumpDispatchedUtc)
        {
            if (jumpResult == null ||
                jumpResult.Disposition != TreasureMapPacketSendDisposition.Dispatched ||
                expectedVersion == null)
            {
                return false;
            }

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(
                Math.Max(0, this.options.ArrivalEvidenceTimeoutMilliseconds));
            while (true)
            {
                this.Checkpoint(externalToken);
                TreasureJumpArrivalEvidence evidence;
                while (TreasureC6StreamObservation.TryDequeueArrivalEvidence(out evidence))
                {
                    if (evidence == null || evidence.ObservedUtc < jumpDispatchedUtc)
                    {
                        continue;
                    }

                    if (evidence.MatchesTarget(
                        expectedVersion.Scene,
                        expectedVersion.X,
                        expectedVersion.Y))
                    {
                        this.currentState = TreasureMapState.ArrivalConfirmed;
                        this.Log(
                            "arrival",
                            "Evidence",
                            this.BuildLogTarget(target, expectedVersion),
                            "arrival_evidence_confirmed:" + evidence.EvidenceType,
                            0,
                            true,
                            attemptId);
                        return true;
                    }
                }

                // Shadow mode must not add a second full wait to the existing
                // Jump→Use rhythm. It reports the absence and continues.
                if (this.options.EvidenceMode == TreasureEvidenceMode.Shadow ||
                    DateTime.UtcNow >= deadline)
                {
                    this.Log(
                        "arrival",
                        "Evidence",
                        this.BuildLogTarget(target, expectedVersion),
                        this.options.EvidenceMode == TreasureEvidenceMode.Enforced
                            ? "arrival_evidence_timeout"
                            : "arrival_evidence_not_observed_shadow",
                        0,
                        false,
                        attemptId);
                    return false;
                }

                this.WaitForMinimumDelay(
                    Math.Min(
                        ArrivalEvidencePollIntervalMilliseconds,
                        Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds)),
                    externalToken);
            }
        }

        private bool WaitForTargetConsumption(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion,
            int retryCount,
            int timeoutMilliseconds,
            bool logTimeout,
            bool allowMidRouteJumpRetry,
            ref bool midRouteJumpRetryIssued,
            CancellationToken externalToken,
            long attemptId)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (true)
            {
                this.currentState = TreasureMapState.WaitingConsumption;
                this.Checkpoint(externalToken);
                if (this.IsTargetConsumptionConfirmed(target, expectedVersion))
                {
                    return true;
                }

                if (allowMidRouteJumpRetry &&
                    !midRouteJumpRetryIssued &&
                    this.options.JumpRetryDelayMilliseconds > 0 &&
                    wait.ElapsedMilliseconds >= this.options.JumpRetryDelayMilliseconds)
                {
                    // 只允许一次途中补 Jump；补发前再次校验目标，避免 C6
                    // 已经换图时把旧坐标发给游戏。
                    midRouteJumpRetryIssued = true;
                    this.TryDispatchMidRouteJump(
                        target,
                        expectedVersion,
                        retryCount,
                        externalToken,
                        attemptId);
                }

                int remainingMilliseconds =
                    timeoutMilliseconds -
                    (int)wait.ElapsedMilliseconds;
                if (remainingMilliseconds <= 0)
                {
                    if (logTimeout)
                    {
                        this.Log(
                            "consume_timeout",
                            "C6",
                            this.BuildLogTarget(target, expectedVersion),
                            "consume_timeout",
                            retryCount,
                            false,
                            attemptId);
                    }
                    return false;
                }

                this.WaitForMinimumDelay(
                    Math.Min(ConsumptionPollIntervalMilliseconds, remainingMilliseconds),
                    externalToken);
            }
        }

        private void TryDispatchMidRouteJump(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion,
            int retryCount,
            CancellationToken externalToken,
            long attemptId)
        {
            this.WaitForJumpThrottle(
                target,
                expectedVersion,
                externalToken,
                attemptId);

            if (!this.IsHookSessionStable())
            {
                this.Log(
                    "jump_retry",
                    "Jump",
                    this.BuildLogTarget(target, expectedVersion),
                    "mid_route_retry_skipped:hook_session_changed",
                    retryCount,
                    false,
                    attemptId);
                return;
            }

            if (!this.ValidateTarget(target, expectedVersion))
            {
                this.Log(
                    "jump_retry",
                    "Jump",
                    this.BuildLogTarget(target, expectedVersion),
                    this.IsTargetConsumptionConfirmed(target, expectedVersion)
                        ? "mid_route_retry_skipped:target_consumed"
                        : "mid_route_retry_skipped:target_changed",
                    retryCount,
                    false,
                    attemptId);
                return;
            }

            this.currentState = TreasureMapState.SendingJump;
            DateTime jumpDispatchedUtc;
            TreasureMapPacketSendResult result = this.SendJumpWithRetry(
                target,
                expectedVersion,
                externalToken,
                attemptId,
                out jumpDispatchedUtc);
            if (result.Disposition == TreasureMapPacketSendDisposition.Dispatched)
            {
                this.lastJumpDispatchedUtc = jumpDispatchedUtc;
            }
            this.Log(
                "jump_retry",
                "Jump",
                this.BuildLogTarget(target, expectedVersion),
                "mid_route_retry:" + result.Code,
                retryCount + 1,
                result.Disposition == TreasureMapPacketSendDisposition.Dispatched,
                attemptId);
            this.currentState = TreasureMapState.WaitingConsumption;
        }

        private bool IsTargetConsumptionConfirmed(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion)
        {
            TreasureC6InventorySnapshot snapshot = this.inventoryStream.CurrentSnapshot;
            if (snapshot == null ||
                target == null ||
                expectedVersion == null ||
                !IsSameEvidenceEpoch(target, snapshot))
            {
                return false;
            }

            TreasureC6InventoryItem currentItem = snapshot.Items.FirstOrDefault(item =>
                string.Equals(item.MemberIdentity, target.MemberIdentity, StringComparison.Ordinal) &&
                item.PackageNum == target.PackageNum);
            if (currentItem == null)
            {
                return true;
            }

            TreasureMapTargetVersion currentVersion = new TreasureMapTargetVersion(
                currentItem.Scene,
                currentItem.X,
                currentItem.Y);
            return !expectedVersion.Equals(currentVersion);
        }

        private void CompleteTarget(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion,
            int retryCount,
            string detail)
        {
            if (string.Equals(detail, "consume_confirmed", StringComparison.Ordinal))
            {
                this.Log(
                    "consume_confirmed",
                    "C6",
                    this.BuildLogTarget(target, expectedVersion),
                    detail,
                    retryCount,
                    true,
                    this.currentAttemptId);
            }

            this.actionLog.Add(new TreasureMapActionLog(
                this.currentRunId,
                target,
                TreasureMapState.TargetCompleted,
                "completed",
                retryCount,
                detail));
            this.currentState = TreasureMapState.TargetCompleted;
        }

        private void ReadUntilSnapshot(CancellationToken externalToken)
        {
            Stopwatch wait = Stopwatch.StartNew();
            TreasureC6StreamMessage lastMessage = null;
            using (CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(this.internalCts.Token, externalToken))
            {
                while (this.inventoryStream.CurrentSnapshot == null)
                {
                    this.Checkpoint(externalToken);
                    lastMessage = this.inventoryStream.ReadNext(linkedCts.Token);
                }
            }
            TreasureC6StreamObservation.PublishSnapshot(this.inventoryStream.CurrentSnapshot);
            this.LogC6WaitDiagnostic("snapshot", wait.ElapsedMilliseconds, lastMessage);
        }

        private void ReadUntilSnapshotWithRecovery(
            CancellationToken externalToken,
            ref int c6RecoveryAttempts,
            bool keepListeningOnFailure)
        {
            while (true)
            {
                try
                {
                    this.ReadUntilSnapshot(externalToken);
                    c6RecoveryAttempts = 0;
                    return;
                }
                catch (Exception ex) when (IsC6ResyncRequired(ex))
                {
                    this.ResyncC6Stream(
                        ex,
                        externalToken,
                        ref c6RecoveryAttempts,
                        keepListeningOnFailure);
                }
            }
        }

        private void ReadNextEvent(CancellationToken externalToken)
        {
            Stopwatch wait = Stopwatch.StartNew();
            TreasureC6StreamMessage message;
            using (CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(this.internalCts.Token, externalToken))
            {
                message = this.inventoryStream.ReadNext(linkedCts.Token);
            }
            TreasureC6StreamObservation.PublishSnapshot(this.inventoryStream.CurrentSnapshot);
            this.LogC6WaitDiagnostic("new_inventory", wait.ElapsedMilliseconds, message);
        }

        private void LogC6WaitDiagnostic(
            string reason,
            long elapsedMilliseconds,
            TreasureC6StreamMessage message)
        {
            if (elapsedMilliseconds < C6WaitDiagnosticThresholdMilliseconds)
            {
                return;
            }

            string messageType = message == null || string.IsNullOrWhiteSpace(message.MessageType)
                ? "unknown"
                : message.MessageType;
            this.Log(
                "recognition",
                "C6",
                null,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "c6_wait_ms={0};reason={1};message={2}",
                    elapsedMilliseconds,
                    reason ?? string.Empty,
                    messageType),
                0,
                false);
        }

        private void ReadNextEventWithRecovery(
            CancellationToken externalToken,
            ref int c6RecoveryAttempts,
            bool keepListeningOnFailure)
        {
            while (true)
            {
                try
                {
                    this.ReadNextEvent(externalToken);
                    c6RecoveryAttempts = 0;
                    return;
                }
                catch (Exception ex) when (IsC6ResyncRequired(ex))
                {
                    this.ResyncC6Stream(
                        ex,
                        externalToken,
                        ref c6RecoveryAttempts,
                        keepListeningOnFailure);

                    // ReadNextEvent 可能是在旧快照上失败的；重连后先恢复
                    // 一份完整基线，下一轮再继续选择目标。
                    this.ReadUntilSnapshotWithRecovery(
                        externalToken,
                        ref c6RecoveryAttempts,
                        keepListeningOnFailure);
                    return;
                }
            }
        }

        private void ResyncC6Stream(
            Exception cause,
            CancellationToken externalToken,
            ref int c6RecoveryAttempts,
            bool keepListeningOnFailure)
        {
            if (!IsC6ResyncRequired(cause))
            {
                throw cause;
            }

            if (c6RecoveryAttempts >= C6ResyncMaxAttempts)
            {
                this.Log(
                    "stream",
                    "C6",
                    null,
                    "c6_resync_exhausted",
                    c6RecoveryAttempts,
                    false);

                if (keepListeningOnFailure)
                {
                    this.WaitForC6Availability(externalToken, ref c6RecoveryAttempts);
                    return;
                }

                throw new TreasureC6TransportException(
                    "c6_resync_exhausted",
                    "C6 stream could not restore a complete snapshot.",
                    cause);
            }

            c6RecoveryAttempts++;
            string code = GetC6ErrorCode(cause);
            this.currentState = TreasureMapState.WaitingSnapshot;
            this.Log(
                "stream",
                "C6",
                null,
                "c6_resync_required:" + code,
                c6RecoveryAttempts,
                false);

            this.inventoryStream.Disconnect();
            try
            {
                this.WaitForMinimumDelay(
                    C6ResyncDelayMilliseconds,
                    externalToken);
                this.ConnectToC6();
                this.Log(
                    "stream",
                    "C6",
                    null,
                    "c6_resync_connected",
                    c6RecoveryAttempts,
                    true);
            }
            catch (TreasureC6TransportException ex)
            {
                this.Log(
                    "stream",
                    "C6",
                    null,
                    "c6_resync_connect_failed:" + ex.Code,
                    c6RecoveryAttempts,
                    false);
                if (c6RecoveryAttempts >= C6ResyncMaxAttempts)
                {
                    if (keepListeningOnFailure)
                    {
                        // 第三次内部重连失败也必须回到持续等待，不能从这个
                        // catch 分支抛出并结束整个助手。
                        this.WaitForC6Availability(externalToken, ref c6RecoveryAttempts);
                        return;
                    }

                    throw new TreasureC6TransportException(
                        "c6_resync_exhausted",
                        "C6 stream could not reconnect for snapshot recovery.",
                        ex);
                }
            }
        }

        private void WaitForC6Availability(
            CancellationToken externalToken,
            ref int c6RecoveryAttempts)
        {
            // 持续模式不因 C6 短暂不可用而退出；断开旧连接后等待一秒，
            // 再从全新的重连周期继续监听。
            this.inventoryStream.Disconnect();
            this.currentState = TreasureMapState.WaitingSnapshot;
            this.Log(
                "stream",
                "C6",
                null,
                "c6_resync_waiting",
                c6RecoveryAttempts,
                false);
            c6RecoveryAttempts = 0;
            this.WaitForMinimumDelay(
                C6ResyncRetryDelayMilliseconds,
                externalToken);
        }

        private static bool IsC6ResyncRequired(Exception exception)
        {
            TreasureC6ProtocolException protocolException =
                exception as TreasureC6ProtocolException;
            if (protocolException != null)
            {
                // C6 协议状态异常都丢弃当前基线，重新建立连接和完整快照；
                // 不把失步事件当作可消费的藏宝图数据。
                return string.Equals(
                    protocolException.Code,
                    "stale_snapshot_rejected",
                    StringComparison.Ordinal) ||
                    !string.IsNullOrWhiteSpace(protocolException.Code);
            }

            TreasureC6TransportException transportException =
                exception as TreasureC6TransportException;
            if (transportException == null)
            {
                return false;
            }

            return string.Equals(transportException.Code, "transport_closed", StringComparison.Ordinal) ||
                string.Equals(transportException.Code, "transport_read_failed", StringComparison.Ordinal) ||
                string.Equals(transportException.Code, "not_connected", StringComparison.Ordinal) ||
                string.Equals(transportException.Code, "connect_failed", StringComparison.Ordinal) ||
                string.Equals(transportException.Code, "connect_timeout", StringComparison.Ordinal);
        }

        private static string GetC6ErrorCode(Exception exception)
        {
            TreasureC6ProtocolException protocolException =
                exception as TreasureC6ProtocolException;
            if (protocolException != null)
            {
                return protocolException.Code;
            }

            TreasureC6TransportException transportException =
                exception as TreasureC6TransportException;
            return transportException == null
                ? "unknown"
                : transportException.Code;
        }

        private bool IsHookSessionStable()
        {
            return TreasurePacketRuntime.GetSessionVersion() == this.hookSessionVersion;
        }

        private TreasureC6InventoryItem FindCurrentItem(TreasureMapTargetIdentity target)
        {
            var snapshot = this.inventoryStream.CurrentSnapshot;
            return snapshot == null || target == null
                ? null
                : snapshot.Items.FirstOrDefault(i =>
                    i.MemberIdentity == target.MemberIdentity && i.PackageNum == target.PackageNum);
        }

        private TreasureC6InventoryItem BuildLogTarget(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion expectedVersion)
        {
            TreasureC6InventoryItem current = this.FindCurrentItem(target);
            if (current != null || target == null || expectedVersion == null)
            {
                return current;
            }

            // 发送/超时日志不能依赖发送后才更新的 CurrentSnapshot；否则目标
            // 恰好在记录日志前离开背包时，坐标会变成空值。
            return new TreasureC6InventoryItem(
                target.MemberIdentity,
                target.PackageNum,
                expectedVersion.Scene,
                expectedVersion.X,
                expectedVersion.Y);
        }

        private void RecordAction(
            TreasureMapTargetIdentity target,
            TreasureMapState state,
            string code,
            int retryCount)
        {
            this.actionLog.Add(new TreasureMapActionLog(
                this.currentRunId,
                target,
                state,
                code,
                retryCount));
            this.Log(
                state.ToString(),
                "",
                this.FindCurrentItem(target),
                code,
                retryCount,
                state == TreasureMapState.TargetCompleted,
                this.currentAttemptId);
        }

        private void LogRecognitionStatus(
            TreasureC6InventorySnapshot snapshot,
            IEnumerable<TreasureC6InventoryItem> items,
            string status,
            TreasureC6InventoryItem target)
        {
            List<TreasureC6InventoryItem> itemList = items == null
                ? new List<TreasureC6InventoryItem>()
                : items.Where(item => item != null).ToList();
            int candidateCount = itemList.Count(item => item.PackageNum > 0);
            string snapshotId = snapshot == null ? "none" : snapshot.SnapshotId;
            string streamSessionId = snapshot == null ? "none" : snapshot.StreamSessionId;
            string targetFingerprint = target == null ? "none" : target.Fingerprint;
            string fingerprint = string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}",
                streamSessionId,
                status ?? string.Empty,
                itemList.Count,
                candidateCount,
                targetFingerprint);
            bool sameFingerprint = string.Equals(
                this.lastRecognitionFingerprint,
                fingerprint,
                StringComparison.Ordinal);
            if (sameFingerprint)
            {
                bool isEmpty = string.Equals(status, "snapshot_empty", StringComparison.Ordinal);
                if (!isEmpty ||
                    (DateTime.UtcNow - this.lastEmptyRecognitionLogUtc).TotalMilliseconds <
                    EmptyRecognitionHeartbeatMilliseconds)
                {
                    return;
                }
            }

            this.lastRecognitionFingerprint = fingerprint;
            if (string.Equals(status, "snapshot_empty", StringComparison.Ordinal))
            {
                this.lastEmptyRecognitionLogUtc = DateTime.UtcNow;
            }
            string code = string.Format(
                CultureInfo.InvariantCulture,
                "{0};snapshot={1};items={2};candidates={3}",
                status ?? string.Empty,
                snapshotId,
                itemList.Count,
                candidateCount);
            this.Log(
                "recognition",
                "C6",
                target,
                code,
                0,
                string.Equals(status, "target_detected", StringComparison.Ordinal));
        }

        private TreasureMapPacketSendResult SendJump(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion targetVersion)
        {
            if (targetVersion == null)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "target_version_missing",
                    "target version is missing");
            }

            // 准备 Jump 包
            TreasureInventoryTarget inventoryTarget = new TreasureInventoryTarget(
                target.PackageNum,
                targetVersion.Scene,
                targetVersion.X,
                targetVersion.Y);

            TreasurePacketRoute route;
            try
            {
                route = this.routeProvider();
            }
            catch (Exception ex)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "current_route_not_found",
                    ex.Message);
            }

            if (route == null)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "current_route_not_found",
                    "current route is not available");
            }

            TreasurePacketPreparedSet prepared;
            try
            {
                prepared = TreasurePacketRuntime.PrepareEncodedFromTarget(
                    inventoryTarget,
                    route);
            }
            catch (TreasurePacketRuntimeException ex)
            {
                return TreasureMapPacketSendResult.NotDispatched(ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "jump_prepare_failed",
                    ex.Message);
            }

            return this.packetSender.SendOnce(
                prepared.JumpPacket,
                this.options.LiveSendEnabled);
        }

        private TreasureMapPacketSendResult SendAutoDig(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion targetVersion,
            CancellationToken externalToken,
            long attemptId)
        {
            this.Checkpoint(externalToken);
            if (targetVersion == null)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "target_version_missing",
                    "target version is missing");
            }

            // 准备 Jump 和固定的自动挖宝包。Jump 仍使用目标坐标；自动
            // 挖宝包本身不绑定 packageNum。
            TreasureInventoryTarget inventoryTarget = new TreasureInventoryTarget(
                target.PackageNum,
                targetVersion.Scene,
                targetVersion.X,
                targetVersion.Y);

            TreasurePacketRoute route;
            try
            {
                route = this.routeProvider();
            }
            catch (Exception ex)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "current_route_not_found",
                    ex.Message);
            }

            if (route == null)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "current_route_not_found",
                    "current route is not available");
            }

            Socket_PacketInfo autoDigPacket;
            try
            {
                autoDigPacket = TreasurePacketRuntime.GetCurrentAutoDigPacket(route);
                this.Log(
                    "auto_dig_prepare",
                    "AutoDig",
                    this.BuildLogTarget(target, targetVersion),
                    "current_send_preset",
                    0,
                    false,
                    attemptId);
            }
            catch (TreasurePacketRuntimeException ex) when (
                string.Equals(
                    ex.Code,
                    "auto_dig_template_not_found",
                    StringComparison.Ordinal))
            {
                try
                {
                    TreasurePacketPreparedSet prepared =
                        TreasurePacketRuntime.PrepareEncodedFromTarget(
                            inventoryTarget,
                            route);
                    autoDigPacket = prepared.AutoDigPacket;
                    this.Log(
                        "auto_dig_prepare",
                        "AutoDig",
                        this.BuildLogTarget(target, targetVersion),
                        "fixed_contract",
                        0,
                        false,
                        attemptId);
                }
                catch (TreasurePacketRuntimeException prepareException)
                {
                    return TreasureMapPacketSendResult.NotDispatched(
                        prepareException.Code,
                        prepareException.Message);
                }
                catch (Exception prepareException)
                {
                    return TreasureMapPacketSendResult.NotDispatched(
                        "auto_dig_prepare_failed",
                        prepareException.Message);
                }
            }
            catch (Exception ex)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "auto_dig_prepare_failed",
                    ex.Message);
            }

            return this.packetSender.SendOnce(
                autoDigPacket,
                this.options.LiveSendEnabled);
        }

        private TreasureMapPacketSendResult SendUse(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion targetVersion,
            CancellationToken externalToken,
            long attemptId)
        {
            this.Checkpoint(externalToken);
            if (targetVersion == null)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "target_version_missing",
                    "target version is missing");
            }

            TreasureInventoryTarget inventoryTarget = new TreasureInventoryTarget(
                target.PackageNum,
                targetVersion.Scene,
                targetVersion.X,
                targetVersion.Y);

            TreasurePacketRoute route;
            try
            {
                route = this.routeProvider();
            }
            catch (Exception ex)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "current_route_not_found",
                    ex.Message);
            }

            if (route == null)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "current_route_not_found",
                    "current route is not available");
            }

            TreasureUsePacketRequest useRequest = this.ResolveUseRequest(
                inventoryTarget.PackageNum,
                out string useRequestSource);
            this.Log(
                "use_prepare",
                "Use",
                this.BuildLogTarget(target, targetVersion),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0};type={1};num={2};param={3}",
                    useRequestSource,
                    useRequest.Type,
                    useRequest.Num,
                    useRequest.Param),
                0,
                false,
                attemptId);

            TreasurePacketPreparedSet prepared;
            try
            {
                prepared = TreasurePacketRuntime.PrepareEncodedFromTarget(
                    inventoryTarget,
                    route,
                    useRequest);
            }
            catch (TreasurePacketRuntimeException ex)
            {
                return TreasureMapPacketSendResult.NotDispatched(ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "use_prepare_failed",
                    ex.Message);
            }

            if (prepared.UsePacket == null)
            {
                return TreasureMapPacketSendResult.NotDispatched(
                    "use_packet_missing",
                    "prepared Use packet is missing");
            }

            return this.packetSender.SendOnce(
                prepared.UsePacket,
                this.options.LiveSendEnabled);
        }

        private TreasureUsePacketRequest ResolveUseRequest(
            int packageNum,
            out string source)
        {
            try
            {
                TreasureUsePacketRequest captured =
                    TreasurePacketRuntime.GetCurrentUseRequest(packageNum);
                source = "current_template";
                return captured;
            }
            catch (TreasurePacketRuntimeException ex)
            {
                source = "configured_fallback:" + ex.Code;
            }
            catch (TreasurePacketContractException ex)
            {
                source = "configured_fallback:" + ex.Code;
            }

            return new TreasureUsePacketRequest(
                packageNum,
                this.options.UseType,
                this.options.UseNum,
                this.options.UseParam);
        }

        private TreasureMapPacketSendResult SendJumpWithRetry(
            TreasureMapTargetIdentity target,
            TreasureMapTargetVersion targetVersion,
            CancellationToken externalToken,
            long attemptId,
            out DateTime dispatchedUtc)
        {
            dispatchedUtc = DateTime.MinValue;
            TreasureMapPacketSendResult result = null;
            for (int retry = 0; retry <= JumpNotDispatchedMaxRetries; retry++)
            {
                this.Checkpoint(externalToken);

                // 第一次发送前已经完成目标与 Hook 会话校验。短重试前再次校验，
                // 避免在等待期间目标离开背包或 Hook 会话切换后发送旧目标。
                if (retry > 0)
                {
                    if (!this.IsHookSessionStable())
                    {
                        return TreasureMapPacketSendResult.Ambiguous(
                            "hook_session_changed_before_jump_retry",
                            0,
                            0);
                    }

                    if (!this.ValidateTarget(target, targetVersion))
                    {
                        return TreasureMapPacketSendResult.NotDispatched(
                            "target_changed_before_jump_retry",
                            "target changed before Jump retry");
                    }
                }

                // Capture a lower-bound timestamp immediately before entering
                // the send boundary. Evidence can be decoded on another hook
                // thread before SendJump returns, so taking the timestamp only
                // after the call would discard a legitimate fast arrival.
                DateTime sendBoundaryUtc = DateTime.UtcNow;
                result = this.SendJump(target, targetVersion);
                if (result.Disposition == TreasureMapPacketSendDisposition.Dispatched)
                {
                    dispatchedUtc = sendBoundaryUtc;
                }
                if (result.Disposition != TreasureMapPacketSendDisposition.NotDispatched ||
                    string.Equals(
                        result.Code,
                        "live_send_not_authorized",
                        StringComparison.Ordinal))
                {
                    return result;
                }

                if (retry >= JumpNotDispatchedMaxRetries)
                {
                    return result;
                }

                this.Log(
                    "jump_retry",
                    "Jump",
                    this.BuildLogTarget(target, targetVersion),
                    "retry_scheduled:" + result.Code,
                    retry + 1,
                    false,
                    attemptId);
                this.WaitForMinimumDelay(
                    this.options.RetryDelayMilliseconds,
                    externalToken);
            }

            return result ?? TreasureMapPacketSendResult.NotDispatched(
                "jump_retry_exhausted",
                "Jump retry exhausted");
        }

        private void WaitForMinimumDelay(int delayMilliseconds, CancellationToken externalToken)
        {
            if (delayMilliseconds <= 0)
            {
                return;
            }

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(delayMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                this.Checkpoint(externalToken);
                int remaining = (int)Math.Min(
                    100,
                    Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds));
                this.pauseGate.Wait(remaining, this.internalCts.Token);
            }
        }

        private void Checkpoint(CancellationToken externalToken)
        {
            // 外部机器人暂停门与运行器自身暂停门都通过同一个门控制。
            while (!this.pauseGate.IsSet)
            {
                this.internalCts.Token.ThrowIfCancellationRequested();
                externalToken.ThrowIfCancellationRequested();
                this.pauseGate.Wait(100);
            }

            this.internalCts.Token.ThrowIfCancellationRequested();
            externalToken.ThrowIfCancellationRequested();
        }

        private void Log(
            string step,
            string packet,
            TreasureC6InventoryItem target,
            string code,
            int retry,
            bool success,
            long attemptId = 0)
        {
            try
            {
                this.logger(new TreasureMapLogEntry(
                    this.currentRunId,
                    step,
                    packet,
                    target,
                    code,
                    retry,
                    success,
                    attemptId));
            }
            catch (Exception)
            {
                // Logging must never end the preset
            }
        }
    }
}
