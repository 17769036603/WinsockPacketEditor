using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib
{
    public enum ShimenTaskFlowAction
    {
        None = 0,
        Submit = 1,
        Cancel = 2,
        Rejected = 3
    }

    public enum ShimenTaskPacketAction
    {
        Submit = 1,
        Cancel = 2,
        Purchase = 3,
        Reclaim = 4
    }

    /// <summary>
    /// One task object decoded from the Android LuaJIT runtime. The object is
    /// read-only evidence and is not a handle to any game-side object.
    /// </summary>
    public sealed class ShimenTaskMemoryTask
    {
        public int Slot { get; set; }
        public int TaskId { get; set; }
        public string TaskType { get; set; } = string.Empty;
        public bool CanGiveUp { get; set; }
        public bool CanGiveUpKnown { get; set; }
        public int FinishTimes { get; set; }
        public int EndTime { get; set; }
        public int UpdateTime { get; set; }
        public int ConditionEntryCount { get; set; }
        public int NpcListEntryCount { get; set; }
    }

    public sealed class ShimenTaskReadDiagnostics
    {
        public string ReadPath { get; set; } = string.Empty;
        public double ProcessLookupMilliseconds { get; set; }
        public double RootSessionMilliseconds { get; set; }
        public double MemoryReadMilliseconds { get; set; }
        public double DecodeMilliseconds { get; set; }
        public double TotalMilliseconds { get; set; }
        public double StartupToReadMilliseconds { get; set; }
        public string StartupBoundary { get; set; } = string.Empty;
    }

    public sealed class ShimenTaskMemorySnapshot
    {
        public bool IsValid { get; set; }
        public int ProcessId { get; set; }
        public long ProcessStartTicks { get; set; }
        public string Executable { get; set; } = string.Empty;
        public string TaskListAddress { get; set; } = string.Empty;
        public IList<ShimenTaskMemoryTask> Tasks { get; set; } =
            new List<ShimenTaskMemoryTask>();
        public ShimenTaskReadDiagnostics Diagnostics { get; set; } =
            new ShimenTaskReadDiagnostics();
        public bool ReadOnly { get; set; } = true;
        public bool ActionAuthorized { get; set; }
        public bool OcrUsed { get; set; }
        public bool NoClaimableTaskKnown { get; set; }
        public bool NoClaimableTask { get; set; }
    }

    public sealed class ShimenTaskMemoryReadResult
    {
        public ShimenTaskMemorySnapshot Snapshot { get; set; }
        public string Error { get; set; } = string.Empty;
        public bool WasCancelled { get; set; }

        public bool Succeeded
        {
            get
            {
                return this.Snapshot != null &&
                    this.Snapshot.IsValid &&
                    string.IsNullOrWhiteSpace(this.Error);
            }
        }
    }

    public interface IShimenTaskMemoryReader : IDisposable
    {
        Task<ShimenTaskMemoryReadResult> ReadSnapshotAsync(
            CancellationToken cancellationToken);
    }

    public sealed class ShimenTaskPacketSendResult
    {
        public bool Succeeded { get; set; }
        public bool BytesSent { get; set; }
        public int SentByteCount { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>
    /// 师门任务的精确封包槽位。采购包按六个任务 ID 独立保存，其他三类包按动作保存；
    /// 发送前还要经过长度、MZ 帧头、当前路由和本次授权校验。
    /// </summary>
    public sealed class ShimenTaskPacketTemplateSlots
    {
        private const int PurchasePacketLength = 26;
        private const int SubmitPacketLength = 65;
        private const int CancelPacketLength = 61;
        private const int ReclaimPacketLength = 29;

        private readonly Dictionary<int, string> purchasePreSendHexByTaskId =
            new Dictionary<int, string>();

        private const string QianNianLingHuaPurchasePreSendHex =
            "4D 5A 00 00 00 00 00 00 00 10 30 44 00 01 60 6E 00 00 00 03 00 00 27 13 00 00";

        private const string FengShuiHunYuanDanPurchasePreSendHex =
            "4D 5A 00 00 00 00 00 00 00 10 30 44 00 01 60 6E 00 00 00 03 00 00 27 11 00 00";

        private const string LingChiTianXiangPurchasePreSendHex =
            "4D 5A 00 00 00 00 00 00 00 10 30 44 00 01 60 6E 00 00 00 03 00 00 27 12 00 00";

        private const string XueYanShiPurchasePreSendHex =
            "4D 5A 00 00 00 00 00 00 00 10 30 44 00 01 60 6E 00 00 00 03 00 00 27 14 00 00";

        private const string YinYaoXiangPurchasePreSendHex =
            "4D 5A 00 00 00 00 00 00 00 10 30 44 00 01 5F AA 00 00 00 03 00 01 5F 95 00 00";

        private const string TieGuShanPurchasePreSendHex =
            "4D 5A 00 00 00 00 00 00 00 10 30 44 00 01 5F 9E 00 00 00 01 00 1E AF 7B 00 00";

        private const string SubmitTaskPreSendHex =
            "4D 5A 00 00 00 00 00 00 00 37 F0 C2 00 00 00 42 00 00 04 B3 00 2E 00 23 6A 95 28 D2 24 49 74 32 56 57 38 44 6C 64 59 6E 5A 70 65 75 35 59 70 49 41 77 6B 55 46 69 39 6C 53 30 6E 41 58 75 76 42 32";

        private const string ReclaimShimenTaskPreSendHex =
            "4D 5A 00 00 00 00 00 00 00 13 30 38 00 01 60 65 0B 73 63 68 6F 6F 6C 5F 74 61 73 6B 00";

        private const string CancelShimenTaskPreSendHex =
            "4D 5A 00 00 00 00 00 00 00 33 F9 08 00 00 00 02 06 34 30 30 30 33 37 00 24 49 74 32 56 57 38 44 6C 64 59 6E 5A 70 65 75 35 59 70 49 41 77 6B 55 46 69 39 6C 53 30 6E 41 58 75 76 42 32";

        public ShimenTaskPacketTemplateSlots()
        {
            foreach (ShimenTaskPresetEntry entry in ShimenTaskPresetPlan.GetEntries())
            {
                this.purchasePreSendHexByTaskId[entry.TaskId] = string.Empty;
            }

            // 已收到的第一条采购包：任务 400043，千年灵花 × 3。
            this.purchasePreSendHexByTaskId[400043] =
                QianNianLingHuaPurchasePreSendHex;

            // 已收到的第二条采购包：任务 400023，风水混元丹 × 3。
            this.purchasePreSendHexByTaskId[400023] =
                FengShuiHunYuanDanPurchasePreSendHex;

            // 已收到的第三条采购包：任务 400033，灵翅天香 × 3。
            // 用户原消息标注为“灵翼天香”，按包内 27 12 归入该任务。
            this.purchasePreSendHexByTaskId[400033] =
                LingChiTianXiangPurchasePreSendHex;

            // 已收到的第四条采购包：任务 400003，血烟石 × 3。
            this.purchasePreSendHexByTaskId[400003] =
                XueYanShiPurchasePreSendHex;

            // 已收到的第五条采购包：任务 400053，引妖香 × 5。
            this.purchasePreSendHexByTaskId[400053] =
                YinYaoXiangPurchasePreSendHex;

            // 已收到的第六条采购包：任务 400013，铁骨扇 × 1。
            this.purchasePreSendHexByTaskId[400013] =
                TieGuShanPurchasePreSendHex;

            // 已收到的通用提交任务包。
            this.SubmitPreSendHex = SubmitTaskPreSendHex;

            // 已收到的“领取师门任务”包，用于取消后重新领取。
            this.ReclaimHex = ReclaimShimenTaskPreSendHex;

            // 已收到的“放弃师门任务”包。
            this.CancelHex = CancelShimenTaskPreSendHex;
        }

        public string SubmitPreSendHex { get; set; } = string.Empty;
        public string CancelHex { get; set; } = string.Empty;
        public string ReclaimHex { get; set; } = string.Empty;

        public IDictionary<int, string> PurchasePreSendHexByTaskId
        {
            get { return this.purchasePreSendHexByTaskId; }
        }

        public string GetPurchasePreSendHex(int taskId)
        {
            string value;
            return this.purchasePreSendHexByTaskId.TryGetValue(taskId, out value)
                ? value
                : string.Empty;
        }

        public bool TryGetPacket(
            ShimenTaskPacketAction action,
            int taskId,
            out byte[] packet,
            out string error)
        {
            packet = null;
            error = string.Empty;
            string hex;
            int expectedLength;
            switch (action)
            {
                case ShimenTaskPacketAction.Purchase:
                    hex = this.GetPurchasePreSendHex(taskId);
                    expectedLength = PurchasePacketLength;
                    if (string.IsNullOrWhiteSpace(hex))
                    {
                        error = "purchase_template_missing";
                        return false;
                    }
                    break;
                case ShimenTaskPacketAction.Submit:
                    hex = this.SubmitPreSendHex;
                    expectedLength = SubmitPacketLength;
                    break;
                case ShimenTaskPacketAction.Cancel:
                    hex = this.CancelHex;
                    expectedLength = CancelPacketLength;
                    break;
                case ShimenTaskPacketAction.Reclaim:
                    hex = this.ReclaimHex;
                    expectedLength = ReclaimPacketLength;
                    break;
                default:
                    error = "packet_action_invalid";
                    return false;
            }

            if (!Socket_Operation.IsHexString(hex))
            {
                error = "packet_hex_invalid";
                return false;
            }

            byte[] parsed = Socket_Operation.StringToBytes(
                Socket_Cache.SocketPacket.EncodingFormat.Hex,
                hex);
            if (parsed == null || parsed.Length != expectedLength)
            {
                error = string.Format(
                    CultureInfo.InvariantCulture,
                    "packet_length_invalid:{0}",
                    parsed == null ? 0 : parsed.Length);
                return false;
            }
            if (parsed[0] != 0x4D || parsed[1] != 0x5A)
            {
                error = "packet_header_invalid";
                return false;
            }

            packet = (byte[])parsed.Clone();
            return true;
        }

        public bool IsComplete
        {
            get
            {
                byte[] packet;
                string error;
                if (!this.TryGetPacket(
                        ShimenTaskPacketAction.Submit,
                        0,
                        out packet,
                        out error) ||
                    !this.TryGetPacket(
                        ShimenTaskPacketAction.Cancel,
                        0,
                        out packet,
                        out error) ||
                    !this.TryGetPacket(
                        ShimenTaskPacketAction.Reclaim,
                        0,
                        out packet,
                        out error))
                {
                    return false;
                }

                return ShimenTaskPresetPlan.GetEntries().All(entry =>
                    this.TryGetPacket(
                        ShimenTaskPacketAction.Purchase,
                        entry.TaskId,
                        out packet,
                        out error));
            }
        }
    }

    /// <summary>
    /// Packet boundary for the later, user-supplied purchase/submit/cancel/
    /// reclaim samples. Implementations must validate the current route and
    /// exact template before calling the existing socket send path.
    /// </summary>
    public interface IShimenTaskPacketSender
    {
        bool IsConfigured { get; }

        ShimenTaskPacketSendResult Send(
            ShimenTaskPacketAction action,
            ShimenTaskMemoryTask task,
            ShimenTaskPresetEntry presetEntry,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Default sender until all real packet templates have been supplied and
    /// independently verified. It never touches a socket.
    /// </summary>
    public sealed class FailClosedShimenTaskPacketSender : IShimenTaskPacketSender
    {
        public bool IsConfigured
        {
            get { return false; }
        }

        public ShimenTaskPacketSendResult Send(
            ShimenTaskPacketAction action,
            ShimenTaskMemoryTask task,
            ShimenTaskPresetEntry presetEntry,
            CancellationToken cancellationToken)
        {
            return new ShimenTaskPacketSendResult
            {
                Succeeded = false,
                BytesSent = false,
                SentByteCount = 0,
                Error = "师门任务取消/提交封包尚未配置，已拒绝发送。"
            };
        }
    }

    public sealed class ShimenTaskFlowDecision
    {
        public ShimenTaskFlowAction Action { get; set; }
        public ShimenTaskMemoryTask Task { get; set; }
        public ShimenTaskPresetEntry PresetEntry { get; set; }
        public string Reason { get; set; } = string.Empty;

        public bool RequiresPacket
        {
            get
            {
                return this.Action == ShimenTaskFlowAction.Submit ||
                    this.Action == ShimenTaskFlowAction.Cancel;
            }
        }
    }

    public sealed class ShimenTaskFlowTiming
    {
        public double ReadMilliseconds { get; set; }
        public double DecisionMilliseconds { get; set; }
        public double SendMilliseconds { get; set; }
        public double TotalMilliseconds { get; set; }
        public double StartupToReadMilliseconds { get; set; }
        public string StartupBoundary { get; set; } = string.Empty;
    }

    public sealed class ShimenTaskFlowResult
    {
        public bool Succeeded { get; set; }
        public ShimenTaskMemorySnapshot Snapshot { get; set; }
        public ShimenTaskFlowDecision Decision { get; set; }
        public ShimenTaskPacketSendResult SendResult { get; set; }
        public bool SendAttempted { get; set; }
        public bool ActionPerformed { get; set; }
        public string Error { get; set; } = string.Empty;
        public ShimenTaskFlowTiming Timing { get; set; } = new ShimenTaskFlowTiming();

        public string Describe()
        {
            string actionText;
            if (this.Decision == null)
            {
                actionText = "未形成分支判定";
            }
            else
            {
                switch (this.Decision.Action)
                {
                    case ShimenTaskFlowAction.Submit:
                        actionText = "命中师门采购白名单，计划提交";
                        break;
                    case ShimenTaskFlowAction.Cancel:
                        actionText = "未命中师门采购白名单，计划取消";
                        break;
                    case ShimenTaskFlowAction.None:
                        actionText = "没有可操作任务，不发送";
                        break;
                    default:
                        actionText = "任务状态有歧义，已停止";
                        break;
                }
            }

            string taskText = this.Decision == null || this.Decision.Task == null
                ? string.Empty
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "；任务ID {0}，列表槽位 {1}",
                    this.Decision.Task.TaskId,
                    this.Decision.Task.Slot);
            string sendText = this.ActionPerformed
                ? "已执行发包"
                : (this.Decision != null && this.Decision.RequiresPacket
                    ? "封包模板未配置或未获运行授权，未发送"
                    : "未执行发包");
            string errorText = string.IsNullOrWhiteSpace(this.Error)
                ? string.Empty
                : "\r\n原因：" + this.Error;
            return string.Format(
                CultureInfo.InvariantCulture,
                "师门任务内存流程：{0}{1}\r\n{2}\r\n启动到读到任务 {3:0.0} ms，读取耗时 {4:0.0} ms，判定耗时 {5:0.0} ms，总耗时 {6:0.0} ms（启动边界：{7}）{8}",
                actionText,
                taskText,
                sendText,
                this.Timing == null ? 0 : this.Timing.StartupToReadMilliseconds,
                this.Timing == null ? 0 : this.Timing.ReadMilliseconds,
                this.Timing == null ? 0 : this.Timing.DecisionMilliseconds,
                this.Timing == null ? 0 : this.Timing.TotalMilliseconds,
                this.Timing == null ? string.Empty : this.Timing.StartupBoundary,
                errorText);
        }
    }

    public enum ShimenTaskLoopState
    {
        NotStarted = 0,
        Reading = 1,
        Purchasing = 2,
        Submitting = 3,
        WaitingForRefresh = 4,
        Cancelling = 5,
        Reclaiming = 6,
        Completed = 7,
        Preview = 8,
        Rejected = 9,
        Failed = 10,
        Stopped = 11
    }

    public sealed class ShimenTaskLoopOptions
    {
        public int RefreshPollMilliseconds { get; set; } = 100;
        public int RefreshTimeoutMilliseconds { get; set; } = 10000;
        public int MaxCycles { get; set; } = 1000;
    }

    public sealed class ShimenTaskLoopResult
    {
        public bool Succeeded { get; set; }
        public bool Completed { get; set; }
        public ShimenTaskLoopState State { get; set; } = ShimenTaskLoopState.NotStarted;
        public int CycleCount { get; set; }
        public int PurchaseCount { get; set; }
        public int SubmitCount { get; set; }
        public int CancelCount { get; set; }
        public int ReclaimCount { get; set; }
        public ShimenTaskMemorySnapshot LastSnapshot { get; set; }
        public ShimenTaskFlowDecision LastDecision { get; set; }
        public ShimenTaskPacketAction? LastPacketAction { get; set; }
        public ShimenTaskPacketSendResult LastSendResult { get; set; }
        public double TotalMilliseconds { get; set; }
        public double StartupToReadMilliseconds { get; set; }
        public string StartupBoundary { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;

        public string Describe()
        {
            string completion = this.Completed ? "已完成" : "未完成";
            string error = string.IsNullOrWhiteSpace(this.Error)
                ? string.Empty
                : "\r\n原因：" + this.Error;
            return string.Format(
                CultureInfo.InvariantCulture,
                "师门任务循环：{0}，状态 {1}，循环 {2} 次；采购 {3} 次，提交 {4} 次，取消 {5} 次，重新领取 {6} 次。\r\n启动到首次读到任务 {7:0.0} ms，总耗时 {8:0.0} ms（启动边界：{9}）{10}",
                completion,
                this.State,
                this.CycleCount,
                this.PurchaseCount,
                this.SubmitCount,
                this.CancelCount,
                this.ReclaimCount,
                this.StartupToReadMilliseconds,
                this.TotalMilliseconds,
                this.StartupBoundary,
                error);
        }
    }

    public static class ShimenTaskFlowEngine
    {
        public static ShimenTaskFlowDecision Decide(
            ShimenTaskMemorySnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsValid)
            {
                return Reject("内存快照无效，拒绝形成任务分支。");
            }
            if (!snapshot.ReadOnly || snapshot.ActionAuthorized || snapshot.OcrUsed)
            {
                return Reject("任务快照的只读/无 OCR 约束未满足，拒绝形成任务分支。");
            }

            if (snapshot.NoClaimableTaskKnown && snapshot.NoClaimableTask)
            {
                return new ShimenTaskFlowDecision
                {
                    Action = ShimenTaskFlowAction.None,
                    Reason = "系统已确认当前没有可领取的任务。"
                };
            }

            IList<ShimenTaskMemoryTask> tasks = snapshot.Tasks;
            if (tasks == null)
            {
                return Reject("任务列表为空对象，拒绝形成任务分支。");
            }
            if (tasks.Count == 0)
            {
                return new ShimenTaskFlowDecision
                {
                    Action = ShimenTaskFlowAction.None,
                    Reason = "当前没有可读取的任务记录。"
                };
            }
            if (tasks.Any(task => task == null || task.TaskId <= 0))
            {
                return Reject("任务列表包含无法验证的任务记录。");
            }

            List<Tuple<ShimenTaskMemoryTask, ShimenTaskPresetEntry>> matches =
                new List<Tuple<ShimenTaskMemoryTask, ShimenTaskPresetEntry>>();
            foreach (ShimenTaskMemoryTask task in tasks)
            {
                ShimenTaskPresetEntry entry = ShimenTaskPresetPlan.GetEntries()
                    .FirstOrDefault(item => item.TaskId == task.TaskId);
                if (entry != null)
                {
                    matches.Add(Tuple.Create(task, entry));
                }
            }

            if (matches.Count > 1)
            {
                return Reject("同时命中多个师门采购任务，无法确认当前目标。");
            }
            if (matches.Count == 1)
            {
                return new ShimenTaskFlowDecision
                {
                    Action = ShimenTaskFlowAction.Submit,
                    Task = matches[0].Item1,
                    PresetEntry = matches[0].Item2,
                    Reason = "命中唯一师门采购任务。"
                };
            }

            // 取消包是按动作保存的独立模板，不需要采购白名单条目绑定。
            // 任务列表可能同时包含多个已读记录；这里保留读取顺序中的
            // 第一条作为本轮日志和刷新指纹的任务锚点，但只形成一次取消动作。
            ShimenTaskMemoryTask cancelTask = tasks[0];
            return new ShimenTaskFlowDecision
            {
                Action = ShimenTaskFlowAction.Cancel,
                Task = cancelTask,
                Reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "未命中师门采购白名单，发送取消任务封包（读取到 {0} 条任务记录）。",
                    tasks.Count)
            };
        }

        private static ShimenTaskFlowDecision Reject(string reason)
        {
            return new ShimenTaskFlowDecision
            {
                Action = ShimenTaskFlowAction.Rejected,
                Reason = reason ?? string.Empty
            };
        }
    }

    public sealed class ShimenTaskFlowRunner
    {
        public ShimenTaskFlowResult Run(
            IShimenTaskMemoryReader reader,
            IShimenTaskPacketSender sender,
            bool allowLiveSend,
            CancellationToken cancellationToken,
            Stopwatch startupStopwatch)
        {
            Stopwatch total = Stopwatch.StartNew();
            ShimenTaskFlowResult result = new ShimenTaskFlowResult();
            result.Timing.StartupBoundary = "DoRobotAsync 调用开始";
            result.Timing.StartupToReadMilliseconds =
                startupStopwatch == null ? 0 : startupStopwatch.Elapsed.TotalMilliseconds;
            if (reader == null)
            {
                result.Error = "师门任务内存读取器未配置。";
                result.Timing.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
                return result;
            }

            Stopwatch readTimer = Stopwatch.StartNew();
            ShimenTaskMemoryReadResult readResult;
            try
            {
                readResult = reader.ReadSnapshotAsync(cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                result.Error = "师门任务内存读取已取消。";
                result.Timing.ReadMilliseconds = readTimer.Elapsed.TotalMilliseconds;
                result.Timing.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
                result.Timing.StartupToReadMilliseconds =
                    startupStopwatch == null ? 0 : startupStopwatch.Elapsed.TotalMilliseconds;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "师门任务内存读取异常：" + ex.Message;
                result.Timing.ReadMilliseconds = readTimer.Elapsed.TotalMilliseconds;
                result.Timing.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
                result.Timing.StartupToReadMilliseconds =
                    startupStopwatch == null ? 0 : startupStopwatch.Elapsed.TotalMilliseconds;
                return result;
            }

            result.Timing.ReadMilliseconds = readTimer.Elapsed.TotalMilliseconds;
            result.Timing.StartupToReadMilliseconds =
                startupStopwatch == null ? 0 : startupStopwatch.Elapsed.TotalMilliseconds;
            if (readResult != null && readResult.Snapshot != null &&
                readResult.Snapshot.Diagnostics != null)
            {
                readResult.Snapshot.Diagnostics.StartupToReadMilliseconds =
                    result.Timing.StartupToReadMilliseconds;
                readResult.Snapshot.Diagnostics.StartupBoundary =
                    result.Timing.StartupBoundary;
            }
            if (readResult == null || !readResult.Succeeded)
            {
                result.Snapshot = readResult == null ? null : readResult.Snapshot;
                result.Error = readResult == null || string.IsNullOrWhiteSpace(readResult.Error)
                    ? "未返回有效任务内存快照。"
                    : readResult.Error;
                result.Timing.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
                return result;
            }

            result.Snapshot = readResult.Snapshot;
            Stopwatch decisionTimer = Stopwatch.StartNew();
            result.Decision = ShimenTaskFlowEngine.Decide(result.Snapshot);
            result.Timing.DecisionMilliseconds = decisionTimer.Elapsed.TotalMilliseconds;
            if (result.Decision.Action == ShimenTaskFlowAction.Rejected)
            {
                result.Error = result.Decision.Reason;
                result.Timing.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
                return result;
            }
            if (!result.Decision.RequiresPacket)
            {
                result.Succeeded = true;
                result.Timing.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
                return result;
            }

            if (!allowLiveSend || sender == null || !sender.IsConfigured)
            {
                result.Succeeded = true;
                result.Error = "取消/提交封包模板尚未配置，当前只完成内存读取和分支预览。";
                result.Timing.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Stopwatch sendTimer = Stopwatch.StartNew();
            result.SendAttempted = true;
            ShimenTaskPacketAction packetAction = result.Decision.Action ==
                ShimenTaskFlowAction.Submit
                ? ShimenTaskPacketAction.Submit
                : ShimenTaskPacketAction.Cancel;
            result.SendResult = sender.Send(
                packetAction,
                result.Decision.Task,
                result.Decision.PresetEntry,
                cancellationToken);
            result.Timing.SendMilliseconds = sendTimer.Elapsed.TotalMilliseconds;
            result.ActionPerformed = result.SendResult != null &&
                result.SendResult.Succeeded &&
                result.SendResult.BytesSent;
            result.Succeeded = result.ActionPerformed;
            if (!result.Succeeded)
            {
                result.Error = result.SendResult == null ||
                    string.IsNullOrWhiteSpace(result.SendResult.Error)
                    ? "师门任务封包发送失败。"
                    : result.SendResult.Error;
            }
            result.Timing.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
            return result;
        }
    }

    /// <summary>
    /// Repeats the task decision after each server refresh. The first read can
    /// be supplied by the startup preflight so a configured sender does not
    /// pay for a duplicate memory scan before the first purchase packet.
    /// </summary>
    public sealed class ShimenTaskLoopRunner
    {
        public ShimenTaskLoopResult Run(
            IShimenTaskMemoryReader reader,
            IShimenTaskPacketSender sender,
            bool allowLiveSend,
            CancellationToken cancellationToken,
            ShimenTaskLoopOptions options,
            Stopwatch startupStopwatch,
            ShimenTaskMemorySnapshot initialSnapshot = null)
        {
            Stopwatch total = Stopwatch.StartNew();
            ShimenTaskLoopResult result = new ShimenTaskLoopResult
            {
                StartupBoundary = "DoRobotAsync 调用开始",
                StartupToReadMilliseconds = startupStopwatch == null
                    ? 0
                    : startupStopwatch.Elapsed.TotalMilliseconds
            };
            options = options ?? new ShimenTaskLoopOptions();
            bool useInitialSnapshot = initialSnapshot != null;
            ShimenTaskMemorySnapshot pendingSnapshot = initialSnapshot;
            int maxCycles = Math.Max(1, options.MaxCycles);

            try
            {
                if (reader == null)
                {
                    result.State = ShimenTaskLoopState.Failed;
                    result.Error = "师门任务内存读取器未配置。";
                    return result;
                }

                while (result.CycleCount < maxCycles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ShimenTaskMemorySnapshot snapshot;
                    if (useInitialSnapshot)
                    {
                        snapshot = pendingSnapshot;
                        useInitialSnapshot = false;
                    }
                    else
                    {
                        result.State = ShimenTaskLoopState.Reading;
                        string readError;
                        if (!TryReadSnapshot(
                            reader,
                            cancellationToken,
                            out snapshot,
                            out readError))
                        {
                            result.State = ShimenTaskLoopState.Failed;
                            result.Error = readError;
                            return result;
                        }
                        if (result.CycleCount == 0 && startupStopwatch != null)
                        {
                            result.StartupToReadMilliseconds =
                                startupStopwatch.Elapsed.TotalMilliseconds;
                        }
                    }

                    result.LastSnapshot = snapshot;
                    result.CycleCount++;
                    result.LastDecision = ShimenTaskFlowEngine.Decide(snapshot);
                    if (result.LastDecision == null)
                    {
                        result.State = ShimenTaskLoopState.Failed;
                        result.Error = "师门任务未形成有效分支。";
                        return result;
                    }
                    if (result.LastDecision.Action == ShimenTaskFlowAction.None)
                    {
                        result.State = ShimenTaskLoopState.Completed;
                        result.Completed = true;
                        result.Succeeded = true;
                        return result;
                    }
                    if (result.LastDecision.Action == ShimenTaskFlowAction.Rejected)
                    {
                        result.State = ShimenTaskLoopState.Rejected;
                        result.Error = result.LastDecision.Reason;
                        return result;
                    }

                    if (!allowLiveSend || sender == null || !sender.IsConfigured)
                    {
                        result.State = ShimenTaskLoopState.Preview;
                        result.Succeeded = true;
                        result.Error =
                            "师门任务封包模板未配置或未获运行授权，循环仅完成一次读取和分支预览。";
                        return result;
                    }

                    if (result.LastDecision.Action == ShimenTaskFlowAction.Submit)
                    {
                        result.State = ShimenTaskLoopState.Purchasing;
                        if (!TrySend(
                            sender,
                            ShimenTaskPacketAction.Purchase,
                            result.LastDecision.Task,
                            result.LastDecision.PresetEntry,
                            cancellationToken,
                            result))
                        {
                            result.State = ShimenTaskLoopState.Failed;
                            return result;
                        }
                        result.PurchaseCount++;

                        result.State = ShimenTaskLoopState.Submitting;
                        if (!TrySend(
                            sender,
                            ShimenTaskPacketAction.Submit,
                            result.LastDecision.Task,
                            result.LastDecision.PresetEntry,
                            cancellationToken,
                            result))
                        {
                            result.State = ShimenTaskLoopState.Failed;
                            return result;
                        }
                        result.SubmitCount++;
                    }
                    else
                    {
                        result.State = ShimenTaskLoopState.Cancelling;
                        if (!TrySend(
                            sender,
                            ShimenTaskPacketAction.Cancel,
                            result.LastDecision.Task,
                            null,
                            cancellationToken,
                            result))
                        {
                            result.State = ShimenTaskLoopState.Failed;
                            return result;
                        }
                        result.CancelCount++;

                        result.State = ShimenTaskLoopState.Reclaiming;
                        if (!TrySend(
                            sender,
                            ShimenTaskPacketAction.Reclaim,
                            result.LastDecision.Task,
                            null,
                            cancellationToken,
                            result))
                        {
                            result.State = ShimenTaskLoopState.Failed;
                            return result;
                        }
                        result.ReclaimCount++;
                    }

                    result.State = ShimenTaskLoopState.WaitingForRefresh;
                    ShimenTaskMemorySnapshot refreshedSnapshot;
                    string refreshError;
                    if (!TryWaitForRefresh(
                        reader,
                        snapshot,
                        options,
                        cancellationToken,
                        out refreshedSnapshot,
                        out refreshError))
                    {
                        result.State = ShimenTaskLoopState.Failed;
                        result.Error = refreshError;
                        return result;
                    }
                    pendingSnapshot = refreshedSnapshot;
                    useInitialSnapshot = true;
                }

                result.State = ShimenTaskLoopState.Failed;
                result.Error = "师门任务循环达到安全次数上限，已停止。";
                return result;
            }
            catch (OperationCanceledException)
            {
                result.State = ShimenTaskLoopState.Stopped;
                result.Error = "师门任务循环已取消。";
                return result;
            }
            catch (Exception ex)
            {
                result.State = ShimenTaskLoopState.Failed;
                result.Error = "师门任务循环异常：" + ex.Message;
                return result;
            }
            finally
            {
                result.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
            }
        }

        private static bool TryReadSnapshot(
            IShimenTaskMemoryReader reader,
            CancellationToken cancellationToken,
            out ShimenTaskMemorySnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;
            try
            {
                ShimenTaskMemoryReadResult readResult = reader
                    .ReadSnapshotAsync(cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                if (readResult == null || !readResult.Succeeded || readResult.Snapshot == null)
                {
                    error = readResult == null || string.IsNullOrWhiteSpace(readResult.Error)
                        ? "未返回有效任务内存快照。"
                        : readResult.Error;
                    return false;
                }
                snapshot = readResult.Snapshot;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = "师门任务内存读取异常：" + ex.Message;
                return false;
            }
        }

        private static bool TryWaitForRefresh(
            IShimenTaskMemoryReader reader,
            ShimenTaskMemorySnapshot previousSnapshot,
            ShimenTaskLoopOptions options,
            CancellationToken cancellationToken,
            out ShimenTaskMemorySnapshot refreshedSnapshot,
            out string error)
        {
            refreshedSnapshot = null;
            error = string.Empty;
            string previousFingerprint = SnapshotFingerprint(previousSnapshot);
            Stopwatch timer = Stopwatch.StartNew();
            int timeoutMilliseconds = Math.Max(1, options.RefreshTimeoutMilliseconds);
            int pollMilliseconds = Math.Max(0, options.RefreshPollMilliseconds);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pollMilliseconds > 0)
                {
                    Task.Delay(pollMilliseconds, cancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }

                if (!TryReadSnapshot(
                    reader,
                    cancellationToken,
                    out refreshedSnapshot,
                    out error))
                {
                    return false;
                }
                if (!string.Equals(
                    previousFingerprint,
                    SnapshotFingerprint(refreshedSnapshot),
                    StringComparison.Ordinal))
                {
                    return true;
                }
                if (timer.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    error = "封包执行后任务列表在限定时间内未刷新，已停止重复操作。";
                    refreshedSnapshot = null;
                    return false;
                }
            }
        }

        private static bool TrySend(
            IShimenTaskPacketSender sender,
            ShimenTaskPacketAction action,
            ShimenTaskMemoryTask task,
            ShimenTaskPresetEntry presetEntry,
            CancellationToken cancellationToken,
            ShimenTaskLoopResult result)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.LastPacketAction = action;
            try
            {
                result.LastSendResult = sender.Send(
                    action,
                    task,
                    presetEntry,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Error = "师门任务封包调用异常：" + ex.Message;
                return false;
            }

            if (result.LastSendResult == null ||
                !result.LastSendResult.Succeeded ||
                !result.LastSendResult.BytesSent)
            {
                result.Error = result.LastSendResult == null ||
                    string.IsNullOrWhiteSpace(result.LastSendResult.Error)
                    ? "师门任务封包发送失败。"
                    : result.LastSendResult.Error;
                return false;
            }
            return true;
        }

        private static string SnapshotFingerprint(ShimenTaskMemorySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "<null>";
            }
            List<string> parts = new List<string>();
            if (snapshot.Tasks != null)
            {
                foreach (ShimenTaskMemoryTask task in snapshot.Tasks)
                {
                    parts.Add(task == null
                        ? "<null>"
                        : string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}:{1}",
                            task.Slot,
                            task.TaskId));
                }
            }
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}",
                snapshot.NoClaimableTaskKnown,
                snapshot.NoClaimableTask,
                string.Join(",", parts.ToArray()));
        }
    }
}
