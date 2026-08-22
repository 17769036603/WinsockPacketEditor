using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WindowsInput.Native;
using WPELibrary.Lib.Vision;

namespace WPELibrary.Lib
{
public class Socket_Robot
    {
        public int Instruction_Index = 0;
        public int Total_Instruction = 0;
        public string RobotName = string.Empty;
        private Dictionary<string, object> _parameters = new Dictionary<string, object>();

        private CancellationTokenSource cts;
        private DataTable RobotInstruction = new DataTable();
        private readonly ManualResetEventSlim robotStopped = new ManualResetEventSlim(true);
        private readonly ManualResetEventSlim robotPauseGate = new ManualResetEventSlim(true);
        public BackgroundWorker Worker = new BackgroundWorker();
        private readonly WindowsInput.InputSimulator sim = new WindowsInput.InputSimulator();

        // 藏宝图运行控制
        private TreasureMapRunState treasureRunState;
        private volatile bool isPaused;

    #region//初始化

        public Socket_Robot()
        {
            this.Worker.WorkerSupportsCancellation = true;
            this.Worker.WorkerReportsProgress = true;

            this.Worker.DoWork -= Robot_DoWork;
            this.Worker.DoWork += Robot_DoWork;

            this.Worker.ProgressChanged -= Robot_ProgressChanged;
            this.Worker.ProgressChanged += Robot_ProgressChanged;

            this.Worker.RunWorkerCompleted -= Robot_RunCompleted;
            this.Worker.RunWorkerCompleted += Robot_RunCompleted;

            // 藏宝图运行控制初始化
            this.treasureRunState = new TreasureMapRunState();
        }

#endregion

        #region//启动机器人

        public bool StartRobot(string RobotName, DataTable dtRobotInstruction, Dictionary<string, object> parameters)
        {
            try
            {
                if (dtRobotInstruction == null || dtRobotInstruction.Rows.Count <= 0 || this.Worker.IsBusy)
                {
                    return false;
                }

                this.Total_Instruction = 0;
                this.RobotName = RobotName;
                this.RobotInstruction = dtRobotInstruction.Copy();

                if (parameters != null)
                {
                    this._parameters = new Dictionary<string, object>(parameters);
                }
                else
                {
                    this._parameters.Clear();
                }

                int iReturn = Socket_Cache.Robot.CheckRobotInstruction(this.RobotInstruction, true);
                if (iReturn > -1)
                {
                    string sLog = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_123), iReturn + 1, this.RobotName);
                    Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLog);
                    return false;
                }

                this.cts = new CancellationTokenSource();
                this.isPaused = false;
                this.treasureRunState = new TreasureMapRunState();
                this.robotPauseGate.Set();
                this.robotStopped.Reset();
                try
                {
                    this.Worker.RunWorkerAsync();
                }
                catch
                {
                    this.robotStopped.Set();
                    throw;
                }

                string sLogStarted = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_109), this.RobotName);
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLogStarted);
                return true;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
                return false;
            }
        }

        #endregion

        #region//停止机器人

        public void StopRobot()
        {
            try
            {
                if (this.Worker.IsBusy)
                {
                    this.treasureRunState.IsStopped = true;
                    this.robotPauseGate.Set();
                    if (this.cts != null)
                    {
                        this.cts.Cancel();
                    }
                    
                    this.Worker.CancelAsync();
                }                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public bool WaitForCompletion(int millisecondsTimeout)
        {
            if (millisecondsTimeout == Timeout.Infinite)
            {
                this.robotStopped.Wait();
                return true;
            }
            return this.robotStopped.Wait(Math.Max(0, millisecondsTimeout));
        }

        public bool PauseRobot()
        {
            if (!this.Worker.IsBusy)
            {
                return false;
            }

            this.isPaused = true;
            if (this.treasureRunState.IsRunning)
            {
                this.treasureRunState.IsPaused = true;
                this.treasureRunState.CurrentState = TreasureMapState.Paused;
                this.treasureRunState.LastStateChange = DateTime.UtcNow;
            }
            this.robotPauseGate.Reset();
            return true;
        }

        public bool ResumeRobot()
        {
            this.isPaused = false;
            if (this.treasureRunState.IsRunning)
            {
                this.treasureRunState.IsPaused = false;
                if (this.treasureRunState.CurrentState == TreasureMapState.Paused)
                {
                    this.treasureRunState.CurrentState = TreasureMapState.WaitingSnapshot;
                }
                this.treasureRunState.LastStateChange = DateTime.UtcNow;
            }
            this.robotPauseGate.Set();
            return true;
        }

        public bool IsPaused
        {
            get { return !this.robotPauseGate.IsSet && this.Worker.IsBusy; }
        }

        #endregion

        #region//执行指令集

        private void Robot_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                if (this.RobotInstruction.Rows.Count > 0)
                {
                    Stack<int> sLoopStart = new Stack<int>();
                    Dictionary<int, int> dLoopCNT = new Dictionary<int, int>();

                    for (int i = 0; i < this.RobotInstruction.Rows.Count; i++)
                    {
                        this.robotPauseGate.Wait(this.cts.Token);
                        if (Worker.CancellationPending)
                        {
                            e.Cancel = true;
                            return;
                        }
                        else
                        {
                            Worker.ReportProgress(i);

                            Socket_Cache.Robot.InstructionType instructionType = (Socket_Cache.Robot.InstructionType)RobotInstruction.Rows[i]["Type"];
                            string sContent = RobotInstruction.Rows[i]["Content"].ToString();

                            switch (instructionType)
                            {
                                case Socket_Cache.Robot.InstructionType.SendSendList:

                                    if (!string.IsNullOrEmpty(sContent))
                                    {
                                        if (!Guid.TryParse(sContent, out Guid SID) || SID == Guid.Empty)
                                        {
                                            throw new InvalidOperationException("机器人发送预设 GUID 无效。");
                                        }

                                        Socket_Send ss = Socket_Cache.Send.DoSend(SID);

                                        if (ss != null)
                                        {
                                            while (ss.Worker.IsBusy)
                                            {
                                                if (this.Worker.CancellationPending)
                                                {
                                                    ss.StopSend();

                                                    e.Cancel = true;
                                                    return;
                                                }

                                                Thread.Sleep(100);
                                            }
                                        }                                        
                                    }

                                    break;

                                case Socket_Cache.Robot.InstructionType.SendSocketList:

                                    Socket_Cache.SocketList.SendSocketList_BySelect();

                                    break;

                                case Socket_Cache.Robot.InstructionType.SetSystemSocket:

                                    int iSocket = 0;
                                    if (sContent.Equals("SocketList"))
                                    {
                                        if (Socket_Cache.SocketList.spiSelect != null)
                                        {
                                            iSocket = Socket_Cache.SocketList.spiSelect.PacketSocket;                                            
                                        }
                                    }
                                    else if (sContent.Equals("FilterSocket"))
                                    {
                                        iSocket = GetParameter<int>("FilterSocket", -1);
                                    }
                                    else if (sContent.Contains("Customize") && sContent.Contains("|"))
                                    {
                                        if (int.TryParse(sContent.Split('|')[1], out int CustomSocket))
                                        {
                                            iSocket = CustomSocket;
                                        }
                                    }

                                    if (iSocket > 0)
                                    {
                                        Socket_Cache.System.SystemSocket = iSocket;
                                    }

                                    break;

                                case Socket_Cache.Robot.InstructionType.Delay:

                                    int iDelay = 0;
                                    if (sContent.Contains("-"))
                                    {
                                        string sFrom = sContent.Split('-')[0];
                                        string sTo = sContent.Split('-')[1];

                                        if (int.TryParse(sFrom, out int iFrom) && int.TryParse(sTo, out int iTo))
                                        {
                                            Random random = new Random();
                                            iDelay = random.Next(iFrom, iTo + 1);

                                            Socket_Operation.DoSleepAsync(iDelay, this.cts.Token)
                                                .GetAwaiter()
                                                .GetResult();
                                        }
                                    }
                                    else
                                    {
                                        if (int.TryParse(sContent, out iDelay))
                                        {
                                            Socket_Operation.DoSleepAsync(iDelay, this.cts.Token)
                                                .GetAwaiter()
                                                .GetResult();
                                        }
                                    }                                    

                                    break;

                                case Socket_Cache.Robot.InstructionType.LoopStart:

                                    if (int.TryParse(sContent, out int Count) && Count > 0)
                                    {
                                        sLoopStart.Push(i);

                                        if (dLoopCNT.ContainsKey(i))
                                        {
                                            dLoopCNT[i] = Count;
                                        }
                                        else
                                        {
                                            dLoopCNT.Add(i, Count);
                                        }
                                    }
                                    else
                                    {
                                        throw new InvalidOperationException("机器人循环次数无效。");
                                    }

                                    break;

                                case Socket_Cache.Robot.InstructionType.LoopEnd:

                                    if (sLoopStart.Count > 0)
                                    {
                                        int iLoopStart = sLoopStart.Peek();

                                        if (dLoopCNT.ContainsKey(iLoopStart))
                                        {
                                            int iLoopCNT = dLoopCNT[iLoopStart];

                                            iLoopCNT--;

                                            if (iLoopCNT > 0)
                                            {
                                                dLoopCNT[iLoopStart] = iLoopCNT;
                                                i = iLoopStart;
                                            }
                                            else
                                            {
                                                sLoopStart.Pop();
                                            }
                                        }
                                    }

                                    break;

                                case Socket_Cache.Robot.InstructionType.KeyBoard:

                                    if (!string.IsNullOrEmpty(sContent) && sContent.IndexOf("|") > 0)
                                    {
                                        Socket_Cache.Robot.KeyBoardType kbType = Socket_Cache.Robot.GetKeyBoardType_ByString(sContent.Split('|')[0].ToString());
                                        string KeyCode = sContent.Split('|')[1];

                                        Keys kCode;
                                        VirtualKeyCode vkCode;

                                        switch (kbType)
                                        {
                                            case Socket_Cache.Robot.KeyBoardType.Press:

                                                if (Enum.TryParse(KeyCode, true, out kCode))
                                                {
                                                    if (Enum.TryParse(((int)kCode).ToString(), true, out vkCode))
                                                    {
                                                        sim.Keyboard.KeyPress(vkCode);
                                                    }
                                                }

                                                break;

                                            case Socket_Cache.Robot.KeyBoardType.Down:

                                                if (Enum.TryParse(KeyCode, true, out kCode))
                                                {
                                                    if (Enum.TryParse(((int)kCode).ToString(), true, out vkCode))
                                                    {
                                                        sim.Keyboard.KeyDown(vkCode);
                                                    }
                                                }

                                                break;

                                            case Socket_Cache.Robot.KeyBoardType.Up:

                                                if (Enum.TryParse(KeyCode, true, out kCode))
                                                {
                                                    if (Enum.TryParse(((int)kCode).ToString(), true, out vkCode))
                                                    {
                                                        sim.Keyboard.KeyUp(vkCode);
                                                    }
                                                }

                                                break;

                                            case Socket_Cache.Robot.KeyBoardType.Combine:

                                                if (KeyCode.IndexOf("+") > 0)
                                                {
                                                    string[] slKeyCode = KeyCode.Split('+');

                                                    List<VirtualKeyCode> ControlKey = new List<VirtualKeyCode>();
                                                    List<VirtualKeyCode> NormalKey = new List<VirtualKeyCode>();

                                                    foreach (string sKey in slKeyCode)
                                                    {
                                                        if (Enum.TryParse(sKey, true, out kCode))
                                                        {
                                                            if (Enum.TryParse(((int)kCode).ToString(), true, out vkCode))
                                                            {
                                                                if (vkCode == VirtualKeyCode.CONTROL || vkCode == VirtualKeyCode.MENU || vkCode == VirtualKeyCode.SHIFT)
                                                                {
                                                                    ControlKey.Add(vkCode);
                                                                }
                                                                else
                                                                {
                                                                    NormalKey.Add(vkCode);
                                                                }
                                                            }
                                                        }
                                                    }

                                                    sim.Keyboard.ModifiedKeyStroke(ControlKey, NormalKey);
                                                }

                                                break;

                                            case Socket_Cache.Robot.KeyBoardType.Text:

                                                if (!string.IsNullOrEmpty(KeyCode))
                                                {
                                                    sim.Keyboard.TextEntry(KeyCode);
                                                }

                                                break;
                                        }
                                    }

                                    break;

                                case Socket_Cache.Robot.InstructionType.Mouse:

                                    if (!string.IsNullOrEmpty(sContent) && sContent.IndexOf("|") > 0)
                                    {
                                        Socket_Cache.Robot.MouseType mType = Socket_Cache.Robot.GetMouseType_ByString(sContent.Split('|')[0].ToString());
                                        string MouseCode = sContent.Split('|')[1];

                                        int iMouseCode = 0;
                                        switch (mType)
                                        {
                                            case Socket_Cache.Robot.MouseType.LeftClick:
                                                sim.Mouse.LeftButtonClick();
                                                break;

                                            case Socket_Cache.Robot.MouseType.RightClick:
                                                sim.Mouse.RightButtonClick();
                                                break;

                                            case Socket_Cache.Robot.MouseType.LeftDBClick:
                                                sim.Mouse.LeftButtonDoubleClick();
                                                break;

                                            case Socket_Cache.Robot.MouseType.RightDBClick:
                                                sim.Mouse.RightButtonDoubleClick();
                                                break;

                                            case Socket_Cache.Robot.MouseType.LeftDown:
                                                sim.Mouse.LeftButtonDown();
                                                break;

                                            case Socket_Cache.Robot.MouseType.LeftUp:
                                                sim.Mouse.LeftButtonUp();
                                                break;

                                            case Socket_Cache.Robot.MouseType.RightDown:
                                                sim.Mouse.RightButtonDown();
                                                break;

                                            case Socket_Cache.Robot.MouseType.RightUp:
                                                sim.Mouse.RightButtonUp();
                                                break;

                                            case Socket_Cache.Robot.MouseType.WheelUp:

                                                if (int.TryParse(MouseCode, out iMouseCode))
                                                {
                                                    sim.Mouse.VerticalScroll(iMouseCode);
                                                }

                                                break;

                                            case Socket_Cache.Robot.MouseType.WheelDown:

                                                if (int.TryParse(MouseCode, out iMouseCode))
                                                {
                                                    sim.Mouse.VerticalScroll(-iMouseCode);
                                                }

                                                break;

                                            case Socket_Cache.Robot.MouseType.MoveTo:

                                                if (MouseCode.IndexOf(",") > 0)
                                                {
                                                    string sMoveX = MouseCode.Split(',')[0].Trim();
                                                    string sMoveY = MouseCode.Split(',')[1].Trim();

                                                    if (int.TryParse(sMoveX, out int iX) && int.TryParse(sMoveY, out int iY))
                                                    {
                                                        sim.Mouse.MoveMouseTo(iX, iY);
                                                    }
                                                }

                                                break;

                                            case Socket_Cache.Robot.MouseType.MoveBy:

                                                if (MouseCode.IndexOf(",") > 0)
                                                {
                                                    string sMoveX = MouseCode.Split(',')[0].Trim();
                                                    string sMoveY = MouseCode.Split(',')[1].Trim();

                                                    if (int.TryParse(sMoveX, out int iX) && int.TryParse(sMoveY, out int iY))
                                                    {
                                                        sim.Mouse.MoveMouseBy(iX, iY);
                                                    }
                                                }

                                                break;
                                        }
                                    }

                                    break;

                                case Socket_Cache.Robot.InstructionType.VisionWait:

                                    VisionAssistantRunResult visionResult = this.RunVisionInstruction(sContent);
                                    if (visionResult == null || visionResult.Cancelled)
                                    {
                                        e.Cancel = true;
                                        return;
                                    }
                                    if (!visionResult.Succeeded)
                                    {
                                        Socket_Operation.DoLog(
                                            MethodBase.GetCurrentMethod().Name,
                                            string.IsNullOrWhiteSpace(visionResult.Error)
                                                ? "Vision instruction failed."
                                                : visionResult.Error);
                                        e.Cancel = true;
                                        return;
                                    }

                                    break;

                                case Socket_Cache.Robot.InstructionType.TreasureMap:

                                    this.RunTreasureMapInstruction(sContent);

                                    break;

                                case Socket_Cache.Robot.InstructionType.SummonedPetSkillBook:

                                    this.RunSummonedPetSkillBookPlanInstruction(sContent);

                                    break;

                            }

                            // 藏宝图运行器会在内部消费取消令牌并正常返回；必须在
                            // 指令返回后把取消状态同步给 BackgroundWorker，否则 UI
                            // 会把用户停止误记成“执行完毕”。
                            if (this.Worker.CancellationPending ||
                                (this.cts != null && this.cts.IsCancellationRequested))
                            {
                                e.Cancel = true;
                                return;
                            }

                            if (instructionType != Socket_Cache.Robot.InstructionType.LoopStart && instructionType != Socket_Cache.Robot.InstructionType.LoopEnd)
                            {
                                this.Total_Instruction++;
                            }
                        }                        
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
                if (this.Worker.CancellationPending ||
                    (this.cts != null && this.cts.IsCancellationRequested))
                {
                    e.Cancel = true;
                    return;
                }
                throw;
            }
        }

        #endregion

        private void RunSummonedPetSkillBookPlanInstruction(string instructionContent)
        {
            WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPresetStep step;
            if (!WPELibrary.Lib.PetSkillBook.SummonedPetSkillBookPresetPlan.TryDecodeStep(
                    instructionContent,
                    out step))
            {
                throw new InvalidOperationException("召唤兽技能预设步骤内容无效，已停止执行。");
            }

            throw new NotSupportedException(
                string.Format(
                    "召唤兽技能预设步骤 {0} 当前仅保存完整流程模板；只读召唤兽数据适配器和正常 UI/业务事件适配器尚未接入，未执行任何游戏操作。",
                    step.State));
        }

        private bool EnsureTreasureC6Ready()
        {
            CancellationToken cancellationToken = this.cts == null
                ? CancellationToken.None
                : this.cts.Token;
            TreasureC6StartupResult startup =
                TreasureC6ServiceController.EnsureReadyAsync(cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            if (startup.Succeeded)
            {
                return true;
            }

            if (cancellationToken.IsCancellationRequested ||
                string.Equals(startup.Code, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                this.treasureRunState.IsStopped = true;
                this.treasureRunState.CurrentState = TreasureMapState.Stopping;
                this.treasureRunState.IsRunning = false;
                return false;
            }

            string startupCode = string.IsNullOrWhiteSpace(startup.Code)
                ? "unavailable"
                : startup.Code;
            this.treasureRunState.LastError = "c6_" + startupCode;
            this.treasureRunState.CurrentState = TreasureMapState.Failed;
            this.treasureRunState.IsRunning = false;
            Socket_Operation.DoLog(
                "TreasureMapPreset",
                "c6_startup_failed:" + startup.Detail);
            return false;
        }

        private void RunTreasureMapInstruction(string instructionContent)
        {
            // 批次A：检查授权
            bool liveSendEnabled = this.GetParameter("TreasureLiveSendEnabled", false);
            if (!liveSendEnabled)
            {
                Socket_Operation.DoLog("TreasureMapPreset", "live_send_not_authorized");
                this.treasureRunState.LastError = "live_send_not_authorized";
                this.treasureRunState.CurrentState = TreasureMapState.Failed;
                this.treasureRunState.IsRunning = false;
                return;
            }

            // 读取指令内容中的静态模式；旧版内容按兼容规则映射为持续模式。
            TreasureMapInstructionDefinition instruction;
            if (!TreasureMapInstructionCodec.TryDecode(instructionContent, out instruction))
            {
                // 指令内容属于持久化配置；解析失败时必须 fail-closed，
                // 不能把损坏内容静默降级成可发送的持续模式。
                Socket_Operation.DoLog(
                    "TreasureMapPreset",
                    "instruction_invalid");
                this.treasureRunState.LastError = "instruction_invalid";
                this.treasureRunState.CurrentState = TreasureMapState.Failed;
                this.treasureRunState.IsRunning = false;
                return;
            }
            TreasureMapExecutionMode mode = instruction.Mode;

            // Keep the normal Use path when the current game or a saved send
            // preset provides a validated 0x783A template. If it does not,
            // follow the ordinary send-preset path and reuse the current
            // route's saved 0xB0F4 AutoDig frame when available.
            bool useNativeAutoDig = false;
            try
            {
                TreasurePacketRuntime.GetCurrentUseRequest(1);
            }
            catch (TreasurePacketRuntimeException ex) when (
                string.Equals(
                    ex.Code,
                    "use_template_not_found",
                    StringComparison.Ordinal))
            {
                useNativeAutoDig = TreasurePacketRuntime.HasCurrentAutoDigPacket();
            }
            catch (TreasurePacketContractException)
            {
                useNativeAutoDig = TreasurePacketRuntime.HasCurrentAutoDigPacket();
            }

            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                LiveSendEnabled = liveSendEnabled,
                Mode = mode,
                Version = instruction.Version,
                ControlMode = instruction.Control,
                // 旧版 V1/V2 指令没有可用的到达证据字段；当前生产路径只观察
                // 接收侧证据，不因证据缺失阻断 Jump→Use。历史现场已证明该类
                // 到达帧不会稳定出现；V3 保留显式策略。
                EvidenceMode = instruction.Version == TreasureMapInstructionVersion.V3
                    ? instruction.Evidence
                    : TreasureEvidenceMode.Shadow,
                // 有当前 Use 模板时保持兼容性 Jump→Use；没有时复用普通发送
                // 预设的 AutoDig 帧。到达证据只作诊断；动作后不在移动途中补发 Jump，消费确认
                // 超时后才按完整目标重试，避免打断当前移动。
                UseNativeAutoDig = useNativeAutoDig,
                ArrivalEvidenceTimeoutMilliseconds = 1000,
                TargetStabilityMilliseconds = 20,
                NextTargetDelayMilliseconds = 0,
                MinimumJumpIntervalMilliseconds = 800,
                JumpRetryDelayMilliseconds = 0
            };

            // 更新运行状态
            this.treasureRunState.RunId = Guid.NewGuid();
            this.treasureRunState.StartTime = DateTime.UtcNow;
            this.treasureRunState.LastStateChange = DateTime.UtcNow;
            this.treasureRunState.IsRunning = true;
            this.treasureRunState.CurrentState = TreasureMapState.Preflight;
            this.treasureRunState.ExecutionMode = mode;
            this.treasureRunState.SendCount = 0;
            this.treasureRunState.SuccessfulCount = 0;
            this.treasureRunState.FailCount = 0;
            this.treasureRunState.IsPaused = false;
            this.treasureRunState.IsStopped = false;
            this.treasureRunState.LiveSendEnabled = options.LiveSendEnabled;
            this.treasureRunState.ControlMode = options.ControlMode;
            this.treasureRunState.EvidenceMode = options.EvidenceMode;
            this.treasureRunState.ArrivalEvidenceConfirmed = false;
            this.treasureRunState.LastEvidenceCode = string.Empty;

            // C6 是藏宝图动态目标的运行期依赖，不再阻塞整个机器人启动。
            if (!this.EnsureTreasureC6Ready())
            {
                return;
            }

            TreasureMapRunLogStore persistentLog = null;
            bool persistentLogFailureReported = false;
            try
            {
                persistentLog = TreasureMapRunLogStore.CreateDefault();
                Socket_Operation.DoLog(
                    "TreasureMapPreset",
                    "persistent_log=" + persistentLog.LogFilePath);
            }
            catch (Exception ex)
            {
                persistentLogFailureReported = true;
                Socket_Operation.DoLog(
                    "TreasureMapRunLog",
                    "persist_initialize_failed:" + ex.Message);
            }

            try
            {
                using (TreasureC6BufferedInventoryStream stream =
                    new TreasureC6BufferedInventoryStream(
                        new TreasureC6StreamClient(
                            options.Host,
                            options.Port)))
                {
                    TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                        stream,
                        new TreasureMapRuntimePacketSender(),
                        () => TreasurePacketRuntime.GetCurrentRoute(),
                        entry => {
                            if (entry.RunId != Guid.Empty)
                            {
                                this.treasureRunState.RunId = entry.RunId;
                            }
                            if (persistentLog != null &&
                                !persistentLog.TryAppend(entry) &&
                                !persistentLogFailureReported)
                            {
                                persistentLogFailureReported = true;
                                Socket_Operation.DoLog(
                                    "TreasureMapRunLog",
                                    "persist_failed:" + persistentLog.LastError);
                            }
                            Socket_Operation.DoLog("TreasureMapPreset", entry.ToString());
                            this.UpdateTreasureRunStateFromLog(entry);
                            this.treasureRunState.LastStateChange = DateTime.UtcNow;
                            if (string.Equals(entry.Step, "arrival", StringComparison.Ordinal))
                            {
                                this.treasureRunState.ArrivalEvidenceConfirmed = entry.Success;
                                this.treasureRunState.LastEvidenceCode = entry.Code;
                            }
                            // 识别状态是诊断记录，不计入 Jump/AutoDig 发送统计，
                            // 也不能把“等待新目标/未识别”当成运行失败。
                            if (string.Equals(entry.Step, "recognition", StringComparison.Ordinal))
                            {
                                return;
                            }

                            // 发送统计只反映真正进入 Jump/AutoDig 发送步骤的结果。
                            // 消费确认超时、补偿重试和冷却都是运行过程状态，
                            // 不能把一张慢图重复累计成多次“发送失败”。
                            bool isPacketSend =
                                string.Equals(entry.Step, "jump", StringComparison.Ordinal) ||
                                string.Equals(entry.Step, "auto_dig", StringComparison.Ordinal) ||
                                // 保留旧日志兼容，当前运行器不会再生成 use 步骤。
                                string.Equals(entry.Step, "use", StringComparison.Ordinal);
                            if (!isPacketSend)
                            {
                                return;
                            }

                            if (!entry.Success)
                            {
                                this.treasureRunState.LastError = entry.Code;
                            }
                            this.treasureRunState.SendCount++;
                            if (entry.Success)
                            {
                                this.treasureRunState.SuccessfulCount++;
                            }
                            else
                            {
                                this.treasureRunState.FailCount++;
                            }
                        },
                        options,
                        this.cts,
                        this.robotPauseGate);

                    runner.Run();
                    this.treasureRunState.CurrentState = runner.CurrentState;
                    if ((runner.CurrentState == TreasureMapState.Failed ||
                         runner.CurrentState == TreasureMapState.ControllerConflict) &&
                        string.IsNullOrWhiteSpace(this.treasureRunState.LastError))
                    {
                        this.treasureRunState.LastError = runner.LastError;
                    }
                    if (runner.CurrentState == TreasureMapState.Ambiguous)
                    {
                        this.treasureRunState.AmbiguousState.HasAmbiguousResult = true;
                        this.treasureRunState.AmbiguousState.LastAmbiguousCode = runner.LastError;
                        this.treasureRunState.AmbiguousState.AmbiguousTimestamp = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex)
            {
                if (persistentLog != null)
                {
                    persistentLog.TryAppend(new TreasureMapLogEntry(
                        this.treasureRunState.RunId,
                        "runner",
                        "exception",
                        null,
                        ex is TreasureC6TransportException
                            ? ((TreasureC6TransportException)ex).Code
                            : "exception:" + ex.Message,
                        0,
                        false));
                }
                Socket_Operation.DoLog("TreasureMapPreset", "exception: " + ex.Message);
                TreasureC6TransportException transportException = ex as TreasureC6TransportException;
                this.treasureRunState.LastError = transportException == null
                    ? ex.Message
                    : transportException.Code;
                this.treasureRunState.CurrentState = TreasureMapState.Failed;
            }
            finally
            {
                this.treasureRunState.IsRunning = false;
                this.treasureRunState.IsPaused = false;
            }
        }

        private VisionAssistantRunResult RunVisionInstruction(string content)
        {
            Socket_VisionProfile profile = this.GetParameter("VisionProfile") as Socket_VisionProfile;
            IVisionTextRecognizer recognizer = this.GetParameter("VisionTextRecognizer") as IVisionTextRecognizer;
            if (profile == null || recognizer == null)
            {
                return new VisionAssistantRunResult
                {
                    Error = "Vision instruction requires a configured vision profile and OCR recognizer."
                };
            }

            if (string.IsNullOrEmpty(content) ||
                !content.StartsWith(
                    Socket_Cache.Robot.VisionInstructionContentPrefix,
                    StringComparison.Ordinal))
            {
                return new VisionAssistantRunResult
                {
                    Error = "Vision instruction content is invalid."
                };
            }

            string payload = content.Substring(Socket_Cache.Robot.VisionInstructionContentPrefix.Length);
            int separator = payload.IndexOf('|');
            string indexText = separator >= 0 ? payload.Substring(0, separator) : payload;
            int stepIndex;
            if (!int.TryParse(indexText, out stepIndex) ||
                stepIndex < 0 ||
                profile.AssistantSteps == null ||
                stepIndex >= profile.AssistantSteps.Count ||
                profile.AssistantSteps[stepIndex] == null)
            {
                return new VisionAssistantRunResult
                {
                    Error = "Vision instruction references a missing assistant step."
                };
            }

            return VisionAssistantRunner.Run(
                profile,
                new[] { profile.AssistantSteps[stepIndex] },
                recognizer,
                this.cts == null ? CancellationToken.None : this.cts.Token,
                null);
        }

        #region//藏宝图运行控制

        public bool HasTreasureMapInstructionRows()
        {
            if (this.RobotInstruction == null || this.RobotInstruction.Rows.Count <= 0)
            {
                return false;
            }
            foreach (DataRow row in this.RobotInstruction.Rows)
            {
                int type = (int)row["Type"];
                if (type == (int)Socket_Cache.Robot.InstructionType.TreasureMap)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 藏宝图运行状态
        /// </summary>
        public class TreasureMapRunState
        {
            public Guid RunId { get; set; } = Guid.Empty;
            public bool IsRunning { get; set; } = false;
            public bool IsPaused { get; set; } = false;
            public bool IsStopped { get; set; } = false;
            public bool LiveSendEnabled { get; set; } = false;
            public TreasureMapExecutionMode ExecutionMode { get; set; } = TreasureMapExecutionMode.Continuous;
            public TreasureControlMode ControlMode { get; set; } = TreasureControlMode.Exclusive;
            public TreasureEvidenceMode EvidenceMode { get; set; } = TreasureEvidenceMode.Shadow;
            public TreasureMapState CurrentState { get; set; } = TreasureMapState.Idle;
            public DateTime StartTime { get; set; } = DateTime.MinValue;
            public DateTime LastStateChange { get; set; } = DateTime.MinValue;
            public string LastError { get; set; } = string.Empty;
            public int SendCount { get; set; } = 0;
            public int SuccessfulCount { get; set; } = 0;
            public int FailCount { get; set; } = 0;
            public long CurrentAttemptId { get; set; } = 0;
            public int CurrentTargetSlot { get; set; } = 0;
            public int CurrentTargetScene { get; set; } = 0;
            public int CurrentTargetX { get; set; } = 0;
            public int CurrentTargetY { get; set; } = 0;
            public bool ArrivalEvidenceConfirmed { get; set; } = false;
            public string LastEvidenceCode { get; set; } = string.Empty;
            public AmbiguousState AmbiguousState { get; set; } = new AmbiguousState();
        }

        /// <summary>
        /// 结果不确定状态
        /// </summary>
        public class AmbiguousState
        {
            public bool HasAmbiguousResult { get; set; } = false;
            public string LastAmbiguousCode { get; set; } = string.Empty;
            public DateTime AmbiguousTimestamp { get; set; } = DateTime.MinValue;
            public int AmbiguousRetryCount { get; set; } = 0;
        }

        private void UpdateTreasureRunStateFromLog(TreasureMapLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.Target != null)
            {
                this.treasureRunState.CurrentTargetSlot = entry.Target.PackageNum;
                this.treasureRunState.CurrentTargetScene = entry.Target.Scene;
                this.treasureRunState.CurrentTargetX = entry.Target.X;
                this.treasureRunState.CurrentTargetY = entry.Target.Y;
            }
            if (entry.AttemptId > 0)
            {
                this.treasureRunState.CurrentAttemptId = entry.AttemptId;
            }
            if (entry.Step == "jump" || entry.Step == "jump_retry")
            {
                this.treasureRunState.CurrentState = TreasureMapState.SendingJump;
            }
            else if (entry.Step == "arrival")
            {
                this.treasureRunState.CurrentState = entry.Success
                    ? TreasureMapState.ArrivalConfirmed
                    : TreasureMapState.WaitingArrival;
            }
            else if (entry.Step == "auto_dig")
            {
                this.treasureRunState.CurrentState = TreasureMapState.SendingAutoDig;
            }
            else if (entry.Step == "use")
            {
                this.treasureRunState.CurrentState = TreasureMapState.SendingUse;
            }
            else if (entry.Step == "consume_timeout" ||
                     entry.Step == "target_retry" ||
                     entry.Step == "target_cooldown")
            {
                this.treasureRunState.CurrentState = TreasureMapState.WaitingConsumption;
            }
            else if (entry.Step == "consume_confirmed")
            {
                this.treasureRunState.CurrentState = TreasureMapState.TargetCompleted;
            }
            else if (entry.Step == "recognition")
            {
                if (entry.Code.StartsWith("target_detected", StringComparison.Ordinal))
                {
                    this.treasureRunState.CurrentState = TreasureMapState.SelectingTarget;
                }
                else if (entry.Code.StartsWith("snapshot_empty", StringComparison.Ordinal) ||
                         entry.Code.StartsWith("waiting_new_target", StringComparison.Ordinal) ||
                         entry.Code.StartsWith("target_not_recognized", StringComparison.Ordinal))
                {
                    this.treasureRunState.CurrentState = TreasureMapState.WaitingNextTarget;
                }
                else if (entry.Code.StartsWith("c6_wait_ms=", StringComparison.Ordinal))
                {
                    this.treasureRunState.CurrentState = TreasureMapState.WaitingSnapshot;
                }
            }
            else if (entry.Step == "stream")
            {
                this.treasureRunState.CurrentState = entry.Code.IndexOf(
                    "resync", StringComparison.OrdinalIgnoreCase) >= 0
                    ? TreasureMapState.SlowRecovery
                    : TreasureMapState.WaitingSnapshot;
            }
            else if (entry.Step == "control" &&
                     string.Equals(entry.Code, "controller_conflict", StringComparison.Ordinal))
            {
                this.treasureRunState.CurrentState = TreasureMapState.ControllerConflict;
            }
            else if (entry.Step == "runner" && !entry.Success)
            {
                this.treasureRunState.CurrentState = TreasureMapState.Failed;
            }
        }

        #endregion

        #region//汇报进度

        private void Robot_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            this.Instruction_Index = e.ProgressPercentage;
        }

        #endregion

        #region//执行完毕

        private void Robot_RunCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                if (e.Cancelled)
                {
                    string sLog = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_110), this.RobotName);
                    Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLog);                    
                }
                else if (e.Error != null)
                {
                    string sLog = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_111), this.RobotName, e.Error.Message);
                    Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLog);
                }
                else
                {
                    string sLog = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_112), this.RobotName);
                    Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLog);
                }              
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
            finally
            {
                this.isPaused = false;
                this.treasureRunState.IsRunning = false;
                this.treasureRunState.IsPaused = false;
                this.robotPauseGate.Set();
                this.robotStopped.Set();
                CancellationTokenSource completedCts = this.cts;
                this.cts = null;
                completedCts?.Dispose();
            }
        }

        #endregion

        #region//藏宝图控制方法

        /// <summary>
        /// 暂停藏宝图运行
        /// </summary>
        public bool PauseTreasureMap()
        {
            if (!this.treasureRunState.IsRunning)
            {
                return false;
            }
            this.isPaused = true;
            this.treasureRunState.IsPaused = true;
            this.treasureRunState.CurrentState = TreasureMapState.Paused;
            this.treasureRunState.LastStateChange = DateTime.UtcNow;
            this.robotPauseGate.Reset();
            return true;
        }

        /// <summary>
        /// 恢复藏宝图运行
        /// </summary>
        public bool ResumeTreasureMap()
        {
            if (!this.isPaused)
            {
                return false;
            }
            this.isPaused = false;
            this.treasureRunState.IsPaused = false;
            if (this.treasureRunState.CurrentState == TreasureMapState.Paused)
            {
                this.treasureRunState.CurrentState = TreasureMapState.WaitingSnapshot;
            }
            this.treasureRunState.LastStateChange = DateTime.UtcNow;
            this.robotPauseGate.Set();
            return true;
        }

        /// <summary>
        /// 停止藏宝图运行
        /// </summary>
        public bool StopTreasureMap()
        {
            if (!this.treasureRunState.IsRunning)
            {
                return false;
            }
            this.treasureRunState.IsStopped = true;
            this.treasureRunState.CurrentState = TreasureMapState.Stopping;
            this.treasureRunState.LastStateChange = DateTime.UtcNow;

            // 触发取消
            if (this.cts != null)
            {
                this.cts.Cancel();
            }
            this.robotPauseGate.Set();
            return true;
        }

        /// <summary>
        /// 获取藏宝图运行状态
        /// </summary>
        public TreasureMapRunState GetTreasureMapRunState()
        {
            return this.treasureRunState;
        }

        /// <summary>
        /// 检查是否可以开始藏宝图运行
        /// </summary>
        public PreFlightResult CheckTreasureMapPreFlight()
        {
            var result = new PreFlightResult();

            // 检查指令版本
            if (this.RobotInstruction == null)
            {
                result.AddError("no_instructions", "没有指令行");
                return result;
            }

            // 检查连续模式指令位置
            bool hasTreasureMap = false;
            int lastTreasureIndex = -1;
            for (int i = 0; i < this.RobotInstruction.Rows.Count; i++)
            {
                int type = Convert.ToInt32(this.RobotInstruction.Rows[i]["Type"]);
                if (type == (int)Socket_Cache.Robot.InstructionType.TreasureMap)
                {
                    hasTreasureMap = true;
                    lastTreasureIndex = i;
                }
            }

            if (hasTreasureMap)
            {
                // 持续模式只能放最后
                string content = this.RobotInstruction.Rows[lastTreasureIndex]["Content"].ToString();
                // 解析是否为持续模式
                // 简化处理：检查内容是否包含模式参数
            }

            // 检查路由。当前藏宝图流程直接从当前路由编码 Jump；Use
            // 优先复用当前会话捕获的 0x783A 模板字段，没有模板时再回退固定值。
            bool routeAvailable = false;
            try
            {
                var route = TreasurePacketRuntime.GetCurrentRoute();
                if (route == null)
                {
                    result.AddError("route_not_found", "当前没有可用的游戏路由");
                }
                else
                {
                    routeAvailable = true;
                }
            }
            catch (Exception ex)
            {
                result.AddError("route_not_found", "当前没有可用的游戏路由: " + ex.Message);
            }

            result.JumpTemplateAvailable = routeAvailable;

            // 自动挖宝包是已确认的固定帧，不依赖本次捕获列表中的 Use 模板。
            try
            {
                TreasurePacketEncoder.ValidateAutoDig(TreasurePacketEncoder.EncodeAutoDig());
                result.AutoDigPacketAvailable = true;
            }
            catch (Exception ex)
            {
                result.AddError("auto_dig_packet_invalid", "自动挖宝包无效: " + ex.Message);
            }

            return result;
        }

        public class PreFlightResult
        {
            public bool IsSuccess { get; set; } = true;
            public List<(string code, string message)> Errors { get; } = new List<(string, string)>();
            public bool JumpTemplateAvailable { get; set; } = false;
            public bool UseTemplateAvailable { get; set; } = false;
            public bool AutoDigPacketAvailable { get; set; } = false;
            public bool IsPaused { get; set; } = false;
            public bool IsRunning { get; set; } = false;

            public void AddError(string code, string message)
            {
                this.Errors.Add((code, message));
                this.IsSuccess = false;
            }
        }

        #endregion

        #region//解析参数

        private object GetParameter(string key)
        {
            try
            {
                if (_parameters.ContainsKey(key))
                {
                    return _parameters[key];
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }            

            return null;
        }

        private T GetParameter<T>(string key, T defaultValue = default(T))
        {
            try
            {
                if (_parameters.ContainsKey(key))
                {
                    object value = _parameters[key];
                    if (value == null)
                    {
                        return defaultValue;
                    }

                    if (value.GetType() == typeof(T))
                    {
                        return (T)value;
                    }

                    if (typeof(T).IsEnum)
                    {
                        if (value is string)
                        {
                            return (T)Enum.Parse(typeof(T), (string)value, true);
                        }

                        return (T)Enum.ToObject(typeof(T), value);
                    }

                    try
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
            
            return defaultValue;
        }

        #endregion
    }
}
