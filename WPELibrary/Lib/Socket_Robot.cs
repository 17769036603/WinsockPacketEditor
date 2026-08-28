using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsInput.Native;
using WPELibrary.Lib.EquipmentRefine;
using WPELibrary.Lib.Vision;
using WPELibrary.Lib.PetSkillBook;

namespace WPELibrary.Lib
{
public class Socket_Robot
    {
        public int Instruction_Index = 0;
        public int Total_Instruction = 0;
        public string RobotName = string.Empty;
        public string LastStartFailureReason { get; private set; } = string.Empty;
        public string LastRunFailureReason { get; private set; } = string.Empty;
        public string ReadOnlySnapshotStatus { get; private set; } = string.Empty;
        public EquipmentRefineStateMachine.ExecutionResult LastEquipmentRefineResult { get; private set; }
        public string LastEquipmentRefineResultMessage { get; private set; } = string.Empty;
        public WPELibrary.Lib.MountSpeed.MountRefineExecutionResult LastMountRefineResult { get; private set; }
        public string LastMountRefineResultMessage { get; private set; } = string.Empty;
        public PetSkillBookReadOnlySnapshot ReadOnlyPetSkillSnapshot
        {
            get { return this.currentPetSkillSnapshot; }
        }
        public PetSkillBookReadOnlyState ReadOnlyPetSkillState
        {
            get { return this.currentPetSkillReadOnlyState; }
        }
        private Dictionary<string, object> _parameters = new Dictionary<string, object>();
        private IDisposable ownedVisionTextRecognizer;
        private Socket_VisionProfile ownedVisionProfile;
        private PetSkillBookReadOnlySnapshot currentPetSkillSnapshot;
        private PetSkillBookReadOnlyState currentPetSkillReadOnlyState;

        private CancellationTokenSource cts;
        private DataTable RobotInstruction = new DataTable();
        private readonly ManualResetEventSlim robotStopped = new ManualResetEventSlim(true);
        private readonly ManualResetEventSlim robotPauseGate = new ManualResetEventSlim(true);
        public BackgroundWorker Worker = new BackgroundWorker();
        private readonly WindowsInput.InputSimulator sim = new WindowsInput.InputSimulator();
        private WPELibrary.Lib.MountSpeed.MountRefineStateMachine activeMountRefineStateMachine;
        private WPELibrary.Lib.MountSpeed.MountRefineRunLogStore mountRefineRunLogStore;
        private Guid mountRefineRunId;

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
                this.LastStartFailureReason = string.Empty;
                if (dtRobotInstruction == null || dtRobotInstruction.Rows.Count <= 0 || this.Worker.IsBusy)
                {
                    this.LastStartFailureReason = dtRobotInstruction == null || dtRobotInstruction.Rows.Count <= 0
                        ? "机器人指令为空。"
                        : "机器人当前正在运行。";
                    return false;
                }

                this.DisposeOwnedVisionResources();

                this.Total_Instruction = 0;
                this.LastRunFailureReason = string.Empty;
                this.ReadOnlySnapshotStatus = string.Empty;
                this.LastEquipmentRefineResult = null;
                this.LastEquipmentRefineResultMessage = string.Empty;
                this.LastMountRefineResult = null;
                this.LastMountRefineResultMessage = string.Empty;
                this.activeMountRefineStateMachine = null;
                this.mountRefineRunLogStore = null;
                this.mountRefineRunId = Guid.Empty;
                this.currentPetSkillSnapshot = null;
                this.currentPetSkillReadOnlyState = null;
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

                // A mount snapshot belongs to one assistant startup only.
                // Never carry a snapshot supplied by a previous run into the
                // next preflight.
                this._parameters.Remove("MountStatusReadOnlySnapshot");
                this._parameters.Remove("MountStatusAndroidReadOnlySnapshot");
                // Route/template binding is rebuilt from the current capture
                // after injection; never reuse an automatically derived A050
                // binding from an earlier assistant run.
                this._parameters.Remove("MountRefineA050PacketTemplate");
                this._parameters.Remove("MountRefineA050RouteTemplate");
                this._parameters.Remove("MountRefineA050CaptureError");

                this.ownedVisionProfile = this.GetParameter("OwnVisionProfile", false)
                    ? this.GetParameter("VisionProfile") as Socket_VisionProfile
                    : null;

                int iReturn = Socket_Cache.Robot.CheckRobotInstruction(this.RobotInstruction, true);
                if (iReturn > -1)
                {
                    this.LastStartFailureReason = string.Format(
                        "第 {0} 步指令校验失败。",
                        iReturn + 1);
                    string sLog = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_123), iReturn + 1, this.RobotName);
                    Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLog);
                    this.DisposeOwnedVisionResources();
                    return false;
                }

                if (this.HasMountSpeedInstructionRows() &&
                    !this.TryPrepareMountSpeedRuntime())
                {
                    this.DisposeOwnedVisionResources();
                    return false;
                }

                if (this.HasSummonedPetSkillBookInstructionRows() &&
                    !this.TryPrepareSummonedPetSkillBookRuntime())
                {
                    this.DisposeOwnedVisionResources();
                    return false;
                }

                if (this.IsReadOnlySummonedPetSkillBookFlow())
                {
                    this.ReadOnlySnapshotStatus =
                        DescribeCurrentPetSkillReadOnlyState(this.currentPetSkillReadOnlyState) +
                        "\r\n" +
                        PetSkillBookReadOnlyStateAdapter.DescribePresetTargets(
                            this.GetParameter("SummonedPetSkillBookPreset") as SummonedPetSkillBookPreset,
                            this.currentPetSkillReadOnlyState) +
                        "\r\n未执行开格、学习、锁格或其他游戏操作。";
                    Socket_Operation.DoLog(
                        "SummonedPetSkillBook",
                        this.ReadOnlySnapshotStatus.Replace("\r\n", " "));
                    this.DisposeOwnedVisionResources();
                    return true;
                }

                if (!this.TryPrepareVisionRuntime())
                {
                    this.DisposeOwnedVisionResources();
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
                this.LastStartFailureReason = ex.Message;
                this.DisposeOwnedVisionResources();
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
                    WPELibrary.Lib.MountSpeed.MountRefineStateMachine mountRefine =
                        this.activeMountRefineStateMachine;
                    if (mountRefine != null)
                    {
                        this.WriteMountRefineStopRequestedDiagnostic(mountRefine);
                        mountRefine.Abort();
                    }
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

        private void WriteMountRefineStopRequestedDiagnostic(
            WPELibrary.Lib.MountSpeed.MountRefineStateMachine mountRefine)
        {
            if (this.mountRefineRunLogStore == null)
            {
                return;
            }

            this.PersistMountRefineLog(
                new WPELibrary.Lib.MountSpeed.MountRefineLogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    RunId = this.mountRefineRunId,
                    EventName = "stop_requested",
                    State = mountRefine == null
                        ? "Unknown"
                        : mountRefine.State.ToString(),
                    StopReason = string.Empty,
                    Code = "mount_refine_stop_requested",
                    Message = "收到机器人停止请求，正在取消当前坐骑 Android 只读读取。",
                    AttemptCount = 0,
                    Success = null,
                    Sequence = 0,
                    MountId = null,
                    ActiveRideInstanceId = string.Empty,
                    RideBindingStatus = string.Empty,
                    RefineCardCount = 0,
                    MatchedCardIndex = null,
                    ProtocolVerified = this.GetParameter(
                        "MountRefineProtocolVerified",
                        false),
                    Authorized = this.GetParameter(
                        "MountRefineLiveSendAuthorization") != null,
                    Socket = 0,
                    BytesSent = 0,
                    SocketError = 0
                });
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
                                        this.LastRunFailureReason = string.IsNullOrWhiteSpace(visionResult.Error)
                                            ? "视觉助手步骤执行失败。"
                                            : visionResult.Error;
                                        Socket_Operation.DoLog(
                                            MethodBase.GetCurrentMethod().Name,
                                            this.LastRunFailureReason);
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

                                case Socket_Cache.Robot.InstructionType.MountSpeed:

                                    this.RunMountSpeedPlanInstruction(sContent);

                                    break;

                                case Socket_Cache.Robot.InstructionType.FiveElementUpgrade:

                                    this.RunFiveElementUpgradePlanInstruction(sContent);

                                    break;

                                case Socket_Cache.Robot.InstructionType.SkillUpgrade:

                                    this.RunSkillUpgradePlanInstruction(sContent);

                                    break;

                                case Socket_Cache.Robot.InstructionType.EquipmentRefine:

                                    this.RunEquipmentRefinePlanInstruction(sContent);

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

            if (string.Equals(step.State, "IDLE", StringComparison.Ordinal) &&
                this.currentPetSkillSnapshot != null)
            {
                Socket_Operation.DoLog(
                    "SummonedPetSkillBook",
                    DescribeCurrentPetSkillSnapshot(this.currentPetSkillSnapshot));
            }

            throw new NotSupportedException(
                string.Format(
                    "召唤兽技能预设步骤 {0} 已完成当前宠物技能只读读取；开格、学习和锁格业务适配器尚未接入，未执行任何游戏操作。",
                    step.State));
        }

        private bool HasMountSpeedInstructionRows()
        {
            if (this.RobotInstruction == null || this.RobotInstruction.Rows.Count <= 0)
            {
                return false;
            }

            foreach (DataRow row in this.RobotInstruction.Rows)
            {
                if (row == null || row["Type"] == null || row["Type"] == DBNull.Value)
                {
                    continue;
                }

                if (Convert.ToInt32(row["Type"]) ==
                    (int)Socket_Cache.Robot.InstructionType.MountSpeed)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryPrepareMountSpeedRuntime()
        {
            // Callers that still provide an explicit Windows layout keep the
            // legacy reader path. The Android path is supplied by the normal
            // assistant UI and is refreshed below on every StartRobot call.
            WPELibrary.Lib.MountSpeed.MountSpeedPreset preset =
                this.GetParameter("MountSpeedPreset") as
                WPELibrary.Lib.MountSpeed.MountSpeedPreset;
            string refineTargetError;
            bool hasMountRefineTarget = preset != null &&
                preset.IsCompleteMountRefineTarget(out refineTargetError);
            if (hasMountRefineTarget)
            {
                this.mountRefineRunId = Guid.NewGuid();
                this.mountRefineRunLogStore =
                    WPELibrary.Lib.MountSpeed.MountRefineRunLogStore.CreateDefault();
                this.WriteMountRefineDiagnostic(
                    "preflight_started",
                    "mount_refine_preflight_started",
                    "开始读取当前坐骑、21 张炼化卡并绑定本次 A050 出站方向。",
                    null,
                    null);
            }
            WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReader reader =
                this.GetParameter("MountStatusAndroidSnapshotReader") as
                WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReader;
            if (reader == null)
            {
                if (hasMountRefineTarget)
                {
                    this.LastStartFailureReason =
                        "坐骑炼化需要 Android 坐骑只读读取器和 21 张炼化卡；当前未配置读取器，已停止且不会发送。";
                    Socket_Operation.DoLog(
                        nameof(TryPrepareMountSpeedRuntime),
                        this.LastStartFailureReason);
                    this.WriteMountRefineDiagnostic(
                        "preflight_failed",
                        "mount_refine_snapshot_reader_unconfigured",
                        this.LastStartFailureReason,
                        null,
                        false);
                    return false;
                }
                return true;
            }

            WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReadResult result;
            try
            {
                result = this.ReadMountPreflightSnapshot(
                    reader,
                    hasMountRefineTarget);
            }
            catch (Exception ex)
            {
                this.LastStartFailureReason = "当前坐骑技能只读读取失败：" + ex.Message;
                Socket_Operation.DoLog(
                    nameof(TryPrepareMountSpeedRuntime),
                    this.LastStartFailureReason);
                this.WriteMountRefineDiagnostic(
                    "preflight_failed",
                    "mount_refine_snapshot_exception",
                    this.LastStartFailureReason,
                    null,
                    false);
                return false;
            }

            if (result == null || !result.Succeeded || result.Snapshot == null ||
                !result.Snapshot.IsValid)
            {
                this.LastStartFailureReason = "当前坐骑技能只读读取失败：" +
                    (result == null || string.IsNullOrWhiteSpace(result.Error)
                        ? "未返回有效坐骑快照。"
                        : result.Error);
                Socket_Operation.DoLog(
                    nameof(TryPrepareMountSpeedRuntime),
                    this.LastStartFailureReason);
                this.WriteMountRefineDiagnostic(
                    "preflight_failed",
                    "mount_refine_snapshot_invalid",
                    this.LastStartFailureReason,
                    result == null ? null : result.Snapshot,
                    false);
                return false;
            }

            WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot androidSnapshot =
                result.Snapshot;
            if (androidSnapshot.IsMounted == true)
            {
                WPELibrary.Lib.MountSpeed.MountRideInstanceSnapshot current =
                    (androidSnapshot.RideInstances ??
                        new List<WPELibrary.Lib.MountSpeed.MountRideInstanceSnapshot>())
                    .FirstOrDefault(instance => instance != null &&
                        instance.IsCurrent == true &&
                        string.Equals(
                            instance.RideInstanceId,
                            androidSnapshot.ActiveRideInstanceId,
                            StringComparison.Ordinal));
                if (!string.Equals(
                        androidSnapshot.RideBindingStatus,
                        "bound",
                        StringComparison.Ordinal) ||
                    current == null || current.Skills == null || current.Skills.Count == 0)
                {
                    this.LastStartFailureReason =
                        "当前坐骑技能只读读取失败：无法唯一绑定正在骑乘的坐骑实例或技能列表为空。";
                    Socket_Operation.DoLog(
                        nameof(TryPrepareMountSpeedRuntime),
                        this.LastStartFailureReason);
                    this.WriteMountRefineDiagnostic(
                        "preflight_failed",
                        "mount_refine_current_ride_not_bound",
                        this.LastStartFailureReason,
                        androidSnapshot,
                        false);
                    return false;
                }
            }

            WPELibrary.Lib.MountSpeed.MountSkillPresetPreviewResult preview;
            string previewError;
            if (!WPELibrary.Lib.MountSpeed.MountSkillPresetPreview.TryEvaluate(
                    androidSnapshot,
                    preset,
                    out preview,
                    out previewError))
            {
                this.LastStartFailureReason = "坐骑目标技能预演失败：" + previewError;
                Socket_Operation.DoLog(
                    nameof(TryPrepareMountSpeedRuntime),
                    this.LastStartFailureReason);
                this.WriteMountRefineDiagnostic(
                    "preflight_failed",
                    "mount_refine_target_preview_failed",
                    this.LastStartFailureReason,
                    androidSnapshot,
                    false);
                return false;
            }

            string mountRefinePreflightStatus = string.Empty;
            if (hasMountRefineTarget)
            {
                if (!this.TryPrepareMountRefineRuntime(
                        androidSnapshot,
                        preset,
                        reader,
                        out mountRefinePreflightStatus))
                {
                    return false;
                }
            }

            WPELibrary.Lib.MountSpeed.MountStatusSnapshot snapshot =
                new WPELibrary.Lib.MountSpeed.MountStatusSnapshot
                {
                    SessionId = androidSnapshot.SessionId,
                    Sequence = androidSnapshot.Sequence,
                    ReadAt = androidSnapshot.ReadAt,
                    ProcessId = androidSnapshot.Process.Pid,
                    ProcessStartTimeUtcTicks = androidSnapshot.Process.StartTicks,
                    ProcessName = androidSnapshot.Process.Executable,
                    MountId = androidSnapshot.MountId,
                    IsMounted = androidSnapshot.IsMounted,
                    GrowthRate = androidSnapshot.GrowthRate,
                    GrowthRateSource = androidSnapshot.GrowthRateSource,
                    RoleMoveSpeed = androidSnapshot.RoleMoveSpeed,
                    RideInstances = androidSnapshot.RideInstances,
                    ActiveRideInstanceId = androidSnapshot.ActiveRideInstanceId,
                    RideBindingStatus = androidSnapshot.RideBindingStatus,
                    RideBindingSource = androidSnapshot.RideBindingSource
                };

            this._parameters["MountStatusAndroidReadOnlySnapshot"] = androidSnapshot;
            this._parameters["MountStatusReadOnlySnapshot"] = snapshot;
            this._parameters["MountSkillPresetPreview"] = preview;
            this.ReadOnlySnapshotStatus = DescribeCurrentMountSnapshot(androidSnapshot) +
                Environment.NewLine +
                preview.Describe() +
                (string.IsNullOrWhiteSpace(mountRefinePreflightStatus)
                    ? string.Empty
                    : Environment.NewLine + mountRefinePreflightStatus);
            Socket_Operation.DoLog(
                nameof(TryPrepareMountSpeedRuntime),
                this.ReadOnlySnapshotStatus);
            if (hasMountRefineTarget)
            {
                this.WriteMountRefineDiagnostic(
                    "preflight_completed",
                    "mount_refine_preflight_completed",
                    mountRefinePreflightStatus,
                    androidSnapshot,
                    true);
            }
            return true;
        }

        private WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReadResult
            ReadMountPreflightSnapshot(
                WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReader reader,
                bool requireCompleteRefineCards)
        {
            const int maxAttempts = 3;
            WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReadResult result = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result = reader.ReadSnapshotAsync(
                        CancellationToken.None,
                        requireCompleteRefineCards)
                    .GetAwaiter()
                    .GetResult();
                bool incompleteCards = requireCompleteRefineCards &&
                    result != null &&
                    result.Succeeded &&
                    result.Snapshot != null &&
                    result.Snapshot.IsValid &&
                    (result.Snapshot.RideRefineCards == null ||
                     result.Snapshot.RideRefineCards.Count != 21);
                if (!incompleteCards || attempt >= maxAttempts)
                {
                    return result;
                }

                this.WriteMountRefineDiagnostic(
                    "snapshot_retry",
                    "mount_refine_cards_incomplete",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "第 {0} 次快照只读取到 {1} 张炼化卡，等待页面数据稳定后重读。",
                        attempt,
                        result.Snapshot.RideRefineCards == null
                            ? 0
                            : result.Snapshot.RideRefineCards.Count),
                    result.Snapshot,
                    null);
                Thread.Sleep(1000);
            }
            return result;
        }

        private bool TryPrepareMountRefineRuntime(
            WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot snapshot,
            WPELibrary.Lib.MountSpeed.MountSpeedPreset preset,
            WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReader reader,
            out string status)
        {
            status = string.Empty;
            if (snapshot == null || preset == null || reader == null)
            {
                this.LastStartFailureReason =
                    "坐骑炼化启动准备失败：坐骑快照、预设或 Android 读取器为空。";
                Socket_Operation.DoLog(
                    nameof(TryPrepareMountRefineRuntime),
                    this.LastStartFailureReason);
                this.WriteMountRefineDiagnostic(
                    "preflight_failed",
                    "mount_refine_runtime_input_missing",
                    this.LastStartFailureReason,
                    snapshot,
                    false);
                return false;
            }

            if (snapshot.RideRefineCards == null || snapshot.RideRefineCards.Count != 21)
            {
                this.LastStartFailureReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "坐骑炼化启动准备失败：当前只读快照只读取到 {0} 张卡，要求完整 21 张；已停止且不会发送。",
                    snapshot.RideRefineCards == null ? 0 : snapshot.RideRefineCards.Count);
                Socket_Operation.DoLog(
                    nameof(TryPrepareMountRefineRuntime),
                    this.LastStartFailureReason);
                this.WriteMountRefineDiagnostic(
                    "preflight_failed",
                    "mount_refine_cards_incomplete",
                    this.LastStartFailureReason,
                    snapshot,
                    false);
                return false;
            }

            WPELibrary.Lib.MountSpeed.MountRideRefineTargetPreviewResult refinePreview;
            string refinePreviewError;
            if (!WPELibrary.Lib.MountSpeed.MountRideRefineTargetMatcher.TryEvaluate(
                    snapshot,
                    preset,
                    out refinePreview,
                    out refinePreviewError) ||
                refinePreview == null)
            {
                this.LastStartFailureReason =
                    "坐骑炼化目标预演失败：" +
                    (string.IsNullOrWhiteSpace(refinePreviewError)
                        ? "未返回有效匹配结果。"
                        : refinePreviewError);
                Socket_Operation.DoLog(
                    nameof(TryPrepareMountRefineRuntime),
                    this.LastStartFailureReason);
                this.WriteMountRefineDiagnostic(
                    "preflight_failed",
                    "mount_refine_target_preview_failed",
                    this.LastStartFailureReason,
                    snapshot,
                    false);
                return false;
            }

            this._parameters["MountRefineRuntimeEnabled"] = true;
            this._parameters["MountRefineSnapshotSource"] =
                new WPELibrary.Lib.MountSpeed.MountAndroidRefineSnapshotSource(reader);
            this._parameters["MountRideRefineTargetPreview"] = refinePreview;

            WPELibrary.Lib.MountSpeed.MountRefineA050PacketTemplate packetTemplate = null;
            WPELibrary.Lib.MountSpeed.MountRefineA050RouteTemplate routeTemplate = null;
            string bindingError = string.Empty;
            string bindingDiagnostics = string.Empty;
            string evidenceId = string.Format(
                CultureInfo.InvariantCulture,
                "assistant-start-{0:yyyyMMddHHmmssfff}",
                DateTime.UtcNow);
            bool autoSendRequested = this.GetParameter(
                "MountRefineAutoSendRequested",
                false);
            bool captureBound = false;
            const int bindingAttempts = 6;
            for (int bindingAttempt = 1;
                bindingAttempt <= bindingAttempts;
                bindingAttempt++)
            {
                captureBound =
                    WPELibrary.Lib.MountSpeed.MountRefineA050CaptureBinding.TryCreateFromCurrentCapture(
                        evidenceId,
                        out packetTemplate,
                        out routeTemplate,
                        out bindingError,
                        out bindingDiagnostics);
                if (captureBound || !autoSendRequested || bindingAttempt >= bindingAttempts)
                {
                    break;
                }

                this.WriteMountRefineDiagnostic(
                    "a050_binding_retry",
                    bindingError,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "第 {0} 次未在当前注入会话绑定 A050，等待捕获队列稳定后重试；{1}。",
                        bindingAttempt,
                        bindingDiagnostics),
                    snapshot,
                    null);
                Thread.Sleep(500);
            }

            if (captureBound)
            {
                this._parameters["MountRefineA050PacketTemplate"] = packetTemplate;
                this._parameters["MountRefineA050RouteTemplate"] = routeTemplate;
                string bindingStatus = string.Format(
                    CultureInfo.InvariantCulture,
                    "坐骑炼化已自动绑定当前 A050 出站方向：{0}，{1} → {2}；已重建本次运行模板。",
                    routeTemplate.PacketType,
                    routeTemplate.PacketFrom,
                    routeTemplate.PacketTo);
                if (autoSendRequested)
                {
                    // Starting the explicitly selected first-ride refine
                    // assistant is the user's run-scoped authorization. A
                    // valid current outbound capture completes protocol
                    // acceptance for this run only; no flag is persisted.
                    this._parameters["MountRefineProtocolVerified"] = true;
                    this._parameters["MountRefineLiveSendAuthorization"] =
                        WPELibrary.Lib.MountSpeed.MountRefineLiveSendAuthorization.Create(
                            WPELibrary.Lib.MountSpeed.MountRefineLiveSendAuthorization.RequiredConfirmationText);
                    this._parameters["MountRefineA050AutoVerified"] = true;
                    status = bindingStatus +
                        " 已根据本次启动捕获到的有效出站包自动完成协议验收，进入自动炼化。";
                    this.WriteMountRefineDiagnostic(
                        "a050_protocol_accepted",
                        "mount_refine_a050_protocol_accepted",
                        status,
                        snapshot,
                        true);
                }
                else
                {
                    status = bindingStatus + " 发送闸门保持关闭。";
                }
            }
            else
            {
                this._parameters["MountRefineA050CaptureError"] = bindingError ?? string.Empty;
                status = "坐骑炼化尚未绑定 A050 出站方向：" +
                    (string.IsNullOrWhiteSpace(bindingError)
                        ? "未捕获有效出站包。"
                        : bindingError) +
                    "；" + bindingDiagnostics +
                    "；发送闸门保持关闭。";
                this.WriteMountRefineDiagnostic(
                    "a050_binding_failed",
                    bindingError,
                    status,
                    snapshot,
                    false);
                if (autoSendRequested)
                {
                    this.LastStartFailureReason =
                        "坐骑炼化启动失败：当前注入会话中没有可用的 A050 出站包。" +
                        "请保持注入和捕获开启，先在当前连接产生一次坐骑炼化请求后重试；本次发送次数 0。";
                    Socket_Operation.DoLog(
                        nameof(TryPrepareMountRefineRuntime),
                        this.LastStartFailureReason);
                    return false;
                }
            }

            return true;
        }

        private void WriteMountRefineDiagnostic(
            string eventName,
            string code,
            string message,
            WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot snapshot,
            bool? success)
        {
            WPELibrary.Lib.MountSpeed.MountRefineRunLogStore store =
                this.mountRefineRunLogStore;
            if (store == null)
            {
                return;
            }

            WPELibrary.Lib.MountSpeed.MountRefineLogEntry entry =
                new WPELibrary.Lib.MountSpeed.MountRefineLogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    RunId = this.mountRefineRunId,
                    EventName = eventName ?? string.Empty,
                    State = "Preflight",
                    StopReason = success == false ? code ?? string.Empty : string.Empty,
                    Code = code ?? string.Empty,
                    Message = message ?? string.Empty,
                    AttemptCount = 0,
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
                    ProtocolVerified = this.GetParameter(
                        "MountRefineProtocolVerified",
                        false),
                    Authorized = this.GetParameter(
                        "MountRefineLiveSendAuthorization") != null
                };
            this.PersistMountRefineLog(entry);
        }

        private void PersistMountRefineLog(
            WPELibrary.Lib.MountSpeed.MountRefineLogEntry entry)
        {
            WPELibrary.Lib.MountSpeed.MountRefineRunLogStore store =
                this.mountRefineRunLogStore;
            if (store != null && !store.TryAppend(entry))
            {
                Socket_Operation.DoLog(
                    "MountRefineLog",
                    "坐骑炼化持久日志写入失败：" + store.LastError);
            }
        }

        private static string DescribeCurrentMountSnapshot(
            WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "当前坐骑技能只读快照为空。";
            }

            WPELibrary.Lib.MountSpeed.MountRideInstanceSnapshot current =
                (snapshot.RideInstances ??
                    new List<WPELibrary.Lib.MountSpeed.MountRideInstanceSnapshot>())
                .FirstOrDefault(instance => instance != null &&
                    instance.IsCurrent == true &&
                    string.Equals(
                        instance.RideInstanceId,
                        snapshot.ActiveRideInstanceId,
                        StringComparison.Ordinal));
            List<string> skills = current == null || current.Skills == null
                ? new List<string>()
                : current.Skills
                    .Where(skill => skill != null)
                    .OrderBy(skill => skill.SlotIndex)
                    .Select(skill => string.Format(
                        "槽{0}={1}",
                        skill.SlotIndex,
                        string.IsNullOrWhiteSpace(skill.SkillName)
                            ? "技能ID " + skill.SkillId
                            : skill.SkillName))
                    .ToList();

            return string.Format(
                "已在本次启动前重新读取坐骑：mountId={0}，成长率原值={1}，实例={2}，绑定={3}，技能={4}。",
                snapshot.MountId.HasValue ? snapshot.MountId.Value.ToString() : "<未上马>",
                snapshot.GrowthRate.HasValue
                    ? snapshot.GrowthRate.Value.ToString()
                    : "<未读取>",
                string.IsNullOrWhiteSpace(snapshot.ActiveRideInstanceId)
                    ? "<无>"
                    : snapshot.ActiveRideInstanceId,
                snapshot.RideBindingStatus ?? "<未绑定>",
                skills.Count == 0 ? "<未读取>" : string.Join("、", skills));
        }

        private bool HasSummonedPetSkillBookInstructionRows()
        {
            if (this.RobotInstruction == null || this.RobotInstruction.Rows.Count <= 0)
            {
                return false;
            }

            foreach (DataRow row in this.RobotInstruction.Rows)
            {
                if (row == null || row["Type"] == null || row["Type"] == DBNull.Value)
                {
                    continue;
                }

                if (Convert.ToInt32(row["Type"]) ==
                    (int)Socket_Cache.Robot.InstructionType.SummonedPetSkillBook)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsReadOnlySummonedPetSkillBookFlow()
        {
            if (this.RobotInstruction == null || this.RobotInstruction.Rows.Count == 0)
            {
                return false;
            }

            foreach (DataRow row in this.RobotInstruction.Rows)
            {
                if (row == null || row["Type"] == null || row["Type"] == DBNull.Value ||
                    Convert.ToInt32(row["Type"]) !=
                        (int)Socket_Cache.Robot.InstructionType.SummonedPetSkillBook)
                {
                    return false;
                }
            }

            return this.currentPetSkillSnapshot != null;
        }

        private bool TryPrepareSummonedPetSkillBookRuntime()
        {
            PetSkillBookSnapshotReadResult result;
            try
            {
                PetSkillBookAndroidSnapshotReader reader =
                    new PetSkillBookAndroidSnapshotReader();
                result = reader.ReadSnapshotAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                this.LastStartFailureReason = "当前宠物技能只读读取失败：" + ex.Message;
                Socket_Operation.DoLog(
                    nameof(TryPrepareSummonedPetSkillBookRuntime),
                    this.LastStartFailureReason);
                return false;
            }

            if (result == null || !result.Succeeded || result.Snapshot == null)
            {
                this.LastStartFailureReason = "当前宠物技能只读读取失败：" +
                    (result == null || string.IsNullOrWhiteSpace(result.Error)
                        ? "未返回有效快照。"
                        : result.Error);
                Socket_Operation.DoLog(
                    nameof(TryPrepareSummonedPetSkillBookRuntime),
                    this.LastStartFailureReason);
                return false;
            }

            PetSkillBookReadOnlySnapshot snapshot = result.Snapshot;
            if (!snapshot.IsCurrentPetMatch)
            {
                this.LastStartFailureReason = "当前宠物技能只读快照中的宠物 ID 不一致。";
                Socket_Operation.DoLog(
                    nameof(TryPrepareSummonedPetSkillBookRuntime),
                    this.LastStartFailureReason);
                return false;
            }

            PetSkillBook.SummonedPetSkillBookPreset preset =
                this.GetParameter("SummonedPetSkillBookPreset") as
                PetSkillBook.SummonedPetSkillBookPreset;
            if (preset != null &&
                preset.PetMode == PetSkillBook.PetMode.Specified &&
                preset.PetId > 0 &&
                preset.PetId != snapshot.PetId)
            {
                this.LastStartFailureReason = string.Format(
                    "当前宠物 ID 为 {0}，与预设指定的宠物 ID {1} 不一致。",
                    snapshot.PetId,
                    preset.PetId);
                Socket_Operation.DoLog(
                    nameof(TryPrepareSummonedPetSkillBookRuntime),
                    this.LastStartFailureReason);
                return false;
            }

            PetSkillBookReadOnlyState readOnlyState;
            string readOnlyStateError;
            if (!PetSkillBookReadOnlyStateAdapter.TryCreate(
                snapshot,
                out readOnlyState,
                out readOnlyStateError))
            {
                this.LastStartFailureReason = "当前宠物技能只读状态规范化失败：" + readOnlyStateError;
                Socket_Operation.DoLog(
                    nameof(TryPrepareSummonedPetSkillBookRuntime),
                    this.LastStartFailureReason);
                return false;
            }

            this.currentPetSkillSnapshot = snapshot;
            this.currentPetSkillReadOnlyState = readOnlyState;
            this._parameters["PetSkillBookReadOnlySnapshot"] = snapshot;
            this._parameters["PetSkillBookReadOnlyState"] = readOnlyState;
            Socket_Operation.DoLog(
                nameof(TryPrepareSummonedPetSkillBookRuntime),
                DescribeCurrentPetSkillReadOnlyState(readOnlyState));
            return true;
        }

        private static string DescribeCurrentPetSkillReadOnlyState(
            PetSkillBookReadOnlyState state)
        {
            return "已通过只读内存读取当前宠物技能情况：" +
                PetSkillBookReadOnlyStateAdapter.Describe(state);
        }

        private static string DescribeCurrentPetSkillSnapshot(
            PetSkillBookReadOnlySnapshot snapshot)
        {
            PetSkillBookReadOnlyState state;
            string error;
            if (!PetSkillBookReadOnlyStateAdapter.TryCreate(
                snapshot,
                out state,
                out error))
            {
                return "宠物技能只读状态规范化失败：" + error;
            }

            return "已通过只读内存读取当前宠物技能情况：" +
                PetSkillBookReadOnlyStateAdapter.Describe(state);
        }

        private void RunMountSpeedPlanInstruction(string instructionContent)
        {
            WPELibrary.Lib.MountSpeed.MountSpeedPresetStep step;
            if (!WPELibrary.Lib.MountSpeed.MountSpeedPresetPlan.TryDecodeStep(
                    instructionContent,
                    out step))
            {
                throw new InvalidOperationException("坐骑速度预设步骤内容无效，已停止执行。");
            }

            if (string.Equals(step.State, "IDLE", StringComparison.Ordinal))
            {
                WPELibrary.Lib.MountSpeed.MountSpeedPreset mountPreset =
                    this.GetParameter("MountSpeedPreset") as
                    WPELibrary.Lib.MountSpeed.MountSpeedPreset;
                string mountRefineTargetError;
                if (mountPreset != null &&
                    mountPreset.IsCompleteMountRefineTarget(out mountRefineTargetError))
                {
                    this.RunMountRefinePlanInstruction(mountPreset);
                    return;
                }
            }

            if (!string.Equals(step.State, "IDLE", StringComparison.Ordinal))
            {
                WPELibrary.Lib.MountSpeed.MountStatusSnapshot cachedSnapshot =
                    this.GetParameter("MountStatusReadOnlySnapshot") as
                    WPELibrary.Lib.MountSpeed.MountStatusSnapshot;
                if (cachedSnapshot == null || !cachedSnapshot.IsValid)
                {
                    throw new NotSupportedException(
                        "坐骑状态只读读取失败：后续步骤没有可复用的有效快照，请从 IDLE 步骤开始执行。");
                }

                Socket_Operation.DoLog(
                    "MountSpeedPreset",
                    string.Format(
                        "坐骑速度预设步骤 {0}：复用只读快照 {1}",
                        step.State,
                        cachedSnapshot == null ? "<unavailable>" : cachedSnapshot.ToString()));
                return;
            }

            WPELibrary.Lib.MountSpeed.MountStatusSnapshot preparedSnapshot =
                this.GetParameter("MountStatusReadOnlySnapshot") as
                WPELibrary.Lib.MountSpeed.MountStatusSnapshot;
            if (preparedSnapshot != null && preparedSnapshot.IsValid)
            {
                Socket_Operation.DoLog(
                    "MountSpeedPreset",
                    "坐骑预设使用本次启动前已刷新且未复用旧数据的只读快照：" +
                    preparedSnapshot.ToString());
                return;
            }

            WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReader androidReader =
                this.GetParameter("MountStatusAndroidSnapshotReader") as
                WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReader;
            if (androidReader != null)
            {
                WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReadResult androidResult =
                    androidReader.ReadSnapshotAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                if (androidResult == null || !androidResult.Succeeded ||
                    androidResult.Snapshot == null || !androidResult.Snapshot.IsValid)
                {
                    throw new NotSupportedException(
                        "坐骑 Android 状态只读读取失败：" +
                        (androidResult == null || string.IsNullOrWhiteSpace(androidResult.Error)
                            ? "未返回有效坐骑快照。"
                            : androidResult.Error));
                }

                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot androidSnapshot =
                    androidResult.Snapshot;
                WPELibrary.Lib.MountSpeed.MountStatusSnapshot snapshot =
                    new WPELibrary.Lib.MountSpeed.MountStatusSnapshot
                    {
                        SessionId = androidSnapshot.SessionId,
                        Sequence = androidSnapshot.Sequence,
                        ReadAt = androidSnapshot.ReadAt,
                        ProcessId = androidSnapshot.Process.Pid,
                        ProcessStartTimeUtcTicks = androidSnapshot.Process.StartTicks,
                        ProcessName = androidSnapshot.Process.Executable,
                        MountId = androidSnapshot.MountId,
                        IsMounted = androidSnapshot.IsMounted,
                        GrowthRate = androidSnapshot.GrowthRate,
                        GrowthRateSource = androidSnapshot.GrowthRateSource,
                        RoleMoveSpeed = androidSnapshot.RoleMoveSpeed,
                        RideInstances = androidSnapshot.RideInstances,
                        ActiveRideInstanceId = androidSnapshot.ActiveRideInstanceId,
                        RideBindingStatus = androidSnapshot.RideBindingStatus,
                        RideBindingSource = androidSnapshot.RideBindingSource
                    };
                this._parameters["MountStatusReadOnlySnapshot"] = snapshot;
                this._parameters["MountStatusAndroidReadOnlySnapshot"] = androidSnapshot;
                this.ReadOnlySnapshotStatus = DescribeCurrentMountSnapshot(androidSnapshot);
                Socket_Operation.DoLog(
                    "MountSpeedPreset",
                    string.Format(
                        "坐骑速度预设步骤 {0}：读取 Android 坐骑情况 {1}",
                        step.State,
                        snapshot.ToString()));
                return;
            }

            int processId = this.ResolveInjectedProcessId();
            Socket_Operation.DoLog(
                "MountSpeedPreset",
                string.Format(
                    "坐骑速度预设：注入进程信息={0}；解析目标 PID={1}",
                    Socket_Cache.SocketPacket.InjectProcess,
                    processId));
            if (processId <= 0)
            {
                throw new NotSupportedException(
                    "坐骑状态只读读取失败：未检测到已注入的游戏进程，请先注入后再运行。");
            }

            WPELibrary.Lib.MountSpeed.MountStatusMemoryLayout layout =
                this.GetParameter("MountStatusMemoryLayout") as
                WPELibrary.Lib.MountSpeed.MountStatusMemoryLayout;
            if (layout == null)
            {
                throw new NotSupportedException(
                    "坐骑状态只读读取失败：未配置 MountStatusMemoryLayout；未确认真实字段地址前不会扫描或猜测偏移。");
            }

            WPELibrary.Lib.MountSpeed.MountStatusMemoryReader reader;
            string error;
            if (!WPELibrary.Lib.MountSpeed.MountStatusMemoryReader.TryCreate(
                    processId,
                    out reader,
                    out error))
            {
                throw new NotSupportedException(
                    string.Format(
                        "坐骑状态只读读取失败：无法连接游戏进程 {0}（{1}）。",
                        processId,
                        error));
            }

            using (reader)
            {
                if (!reader.ConfigureLayout(layout, out error))
                {
                    throw new NotSupportedException(
                        "坐骑状态只读读取布局无效：" + error);
                }

                WPELibrary.Lib.MountSpeed.MountStatusReadResult result =
                    reader.ReadSnapshotAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                if (result == null || !result.Succeeded || result.Snapshot == null || !result.Snapshot.IsValid)
                {
                    throw new NotSupportedException(
                        "坐骑状态只读读取失败：" +
                        (result == null || string.IsNullOrWhiteSpace(result.Error)
                            ? "未返回有效坐骑快照。"
                            : result.Error));
                }

                WPELibrary.Lib.MountSpeed.MountStatusSnapshot snapshot = result.Snapshot;
                this._parameters["MountStatusReadOnlySnapshot"] = snapshot;
                this.ReadOnlySnapshotStatus = "已通过专用只读内存读取器获取坐骑情况：" + snapshot;
                Socket_Operation.DoLog(
                    "MountSpeedPreset",
                    string.Format(
                        "坐骑速度预设步骤 {0}：读取坐骑情况 {1}",
                        step.State,
                        snapshot.ToString()));
            }
        }

        private void RunMountRefinePlanInstruction(
            WPELibrary.Lib.MountSpeed.MountSpeedPreset preset)
        {
            if (this.GetParameter("MountRefineExecutionCompleted", false))
            {
                return;
            }

            string presetError = string.Empty;
            if (preset == null || !preset.IsCompleteMountRefineTarget(out presetError))
            {
                throw new NotSupportedException(
                    "坐骑炼化预设无效：" +
                    (string.IsNullOrWhiteSpace(presetError)
                        ? "目标未完整填写。"
                        : presetError));
            }

            if (!this.GetParameter("MountRefineRuntimeEnabled", false))
            {
                throw new NotSupportedException(
                    "坐骑炼化尚未完成启动预检；未获得完整 21 张卡片和当前运行绑定，已停止且不会发送。");
            }

            WPELibrary.Lib.MountSpeed.IMountRefineSnapshotSource snapshotSource =
                this.GetParameter("MountRefineSnapshotSource") as
                WPELibrary.Lib.MountSpeed.IMountRefineSnapshotSource;
            if (snapshotSource == null)
            {
                WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReader reader =
                    this.GetParameter("MountStatusAndroidSnapshotReader") as
                    WPELibrary.Lib.MountSpeed.MountStatusAndroidSnapshotReader;
                if (reader != null)
                {
                    snapshotSource =
                        new WPELibrary.Lib.MountSpeed.MountAndroidRefineSnapshotSource(reader);
                }
            }

            if (snapshotSource == null)
            {
                throw new NotSupportedException(
                    "坐骑炼化只读读取器未配置，已停止且不会发送。");
            }

            WPELibrary.Lib.MountSpeed.MountRefineConfiguration configuration =
                new WPELibrary.Lib.MountSpeed.MountRefineConfiguration
                {
                    Preset = preset,
                    // 默认不按炼化次数自动停止；只有命中目标、手动停止或安全故障才结束。
                    MaxAttempts = this.GetParameter("MountRefineMaxAttempts", 0),
                    ResultConfirmTimeoutMs = this.GetParameter(
                        "MountRefineResultConfirmTimeoutMs",
                        3000),
                    IntervalMs = this.GetParameter("MountRefineIntervalMs", 250)
                };

            WPELibrary.Lib.MountSpeed.MountRefineStateMachine machine =
                new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                    configuration,
                    snapshotSource,
                    this.ResolveMountRefinePacketSender(),
                    this.PersistMountRefineLog,
                    this.mountRefineRunId);
            this.activeMountRefineStateMachine = machine;

            WPELibrary.Lib.MountSpeed.MountRefineExecutionResult result;
            try
            {
                CancellationToken cancellationToken = this.cts == null
                    ? CancellationToken.None
                    : this.cts.Token;
                result = machine.RunAsync(cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                if (object.ReferenceEquals(this.activeMountRefineStateMachine, machine))
                {
                    this.activeMountRefineStateMachine = null;
                }
            }

            this.LastMountRefineResult = result;
            this.LastMountRefineResultMessage = DescribeMountRefineResult(result);
            this._parameters["MountRefineExecutionCompleted"] = true;
            Socket_Operation.DoLog(
                "MountRefinePreset",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "坐骑炼化运行结束：state={0}, reason={1}, attempts={2}, message={3}",
                    result == null ? "<null>" : result.FinalState.ToString(),
                    result == null ? "<null>" : result.StopReason.ToString(),
                    result == null ? 0 : result.AttemptCount,
                    result == null ? "<null>" : result.Message));

            if (result == null || !result.Success)
            {
                throw new NotSupportedException(
                    "坐骑炼化未完成：" +
                    (result == null || string.IsNullOrWhiteSpace(result.Message)
                        ? "未返回成功结果。"
                        : result.Message) +
                    "。未授权或未验收协议时已停止发包。");
            }
        }

        private WPELibrary.Lib.MountSpeed.IMountRefinePacketSender
            ResolveMountRefinePacketSender()
        {
            WPELibrary.Lib.MountSpeed.IMountRefinePacketSender explicitSender =
                this.GetParameter("MountRefinePacketSender") as
                WPELibrary.Lib.MountSpeed.IMountRefinePacketSender;
            if (explicitSender != null)
            {
                return explicitSender;
            }

            WPELibrary.Lib.MountSpeed.MountRefineA050PacketTemplate packetTemplate =
                this.GetParameter("MountRefineA050PacketTemplate") as
                WPELibrary.Lib.MountSpeed.MountRefineA050PacketTemplate;
            WPELibrary.Lib.MountSpeed.MountRefineA050RouteTemplate routeTemplate =
                this.GetParameter("MountRefineA050RouteTemplate") as
                WPELibrary.Lib.MountSpeed.MountRefineA050RouteTemplate;
            WPELibrary.Lib.MountSpeed.MountRefineLiveSendAuthorization authorization =
                this.GetParameter("MountRefineLiveSendAuthorization") as
                WPELibrary.Lib.MountSpeed.MountRefineLiveSendAuthorization;
            if (authorization == null)
            {
                string confirmation = this.GetParameter<string>(
                    "MountRefineLiveSendConfirmation",
                    string.Empty);
                if (!string.IsNullOrWhiteSpace(confirmation))
                {
                    try
                    {
                        authorization =
                            WPELibrary.Lib.MountSpeed.MountRefineLiveSendAuthorization.Create(
                                confirmation);
                    }
                    catch (ArgumentException ex)
                    {
                        Socket_Operation.DoLog(
                            "MountRefinePreset",
                            "坐骑炼化真实发送授权无效：" + ex.Message);
                    }
                }
            }

            bool protocolVerified = this.GetParameter(
                "MountRefineProtocolVerified",
                false);
            if (packetTemplate != null || routeTemplate != null || authorization != null || protocolVerified)
            {
                return new WPELibrary.Lib.MountSpeed.MountRefineA050SocketPacketSender(
                    packetTemplate,
                    routeTemplate,
                    authorization,
                    protocolVerified);
            }

            return new WPELibrary.Lib.MountSpeed.FailClosedMountRefinePacketSender();
        }

        private static string DescribeMountRefineResult(
            WPELibrary.Lib.MountSpeed.MountRefineExecutionResult result)
        {
            if (result == null)
            {
                return "坐骑炼化结果未知。";
            }

            if (result.Success &&
                result.StopReason == WPELibrary.Lib.MountSpeed.MountRefineStopReason.TargetReached)
            {
                string card = result.MatchedCardIndex.HasValue
                    ? "第 " + result.MatchedCardIndex.Value.ToString(CultureInfo.InvariantCulture) + " 张卡片"
                    : "目标卡片";
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "坐骑炼化成功：{0}同时满足成长率和三个技能（技能顺序不限制）；发送次数 {1}。",
                    card,
                    result.AttemptCount);
            }

            string reason;
            switch (result.StopReason)
            {
                case WPELibrary.Lib.MountSpeed.MountRefineStopReason.AuthorizationRequired:
                    reason = "未获得本次真实发送授权";
                    break;
                case WPELibrary.Lib.MountSpeed.MountRefineStopReason.ProtocolUnverified:
                    reason = "A050 协议尚未完成验收";
                    break;
                case WPELibrary.Lib.MountSpeed.MountRefineStopReason.UserStopped:
                    reason = "用户或宿主停止请求";
                    break;
                case WPELibrary.Lib.MountSpeed.MountRefineStopReason.SnapshotNotRefreshed:
                    reason = "发送后炼化卡片未刷新";
                    break;
                case WPELibrary.Lib.MountSpeed.MountRefineStopReason.MountChanged:
                    reason = "当前坐骑或实例发生变化";
                    break;
                default:
                    reason = string.IsNullOrWhiteSpace(result.Message)
                        ? result.StopReason.ToString()
                        : result.Message;
                    break;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "坐骑炼化已停止：{0}；发送次数 {1}。",
                reason,
                result.AttemptCount);
        }

        private void RunEquipmentRefinePlanInstruction(string instructionContent)
        {
            EquipmentRefinePresetStep step;
            if (!EquipmentRefinePresetPlan.TryDecodeStep(
                    instructionContent,
                    out step))
            {
                throw new InvalidOperationException("装备炼化预设步骤内容无效，已停止执行。");
            }

            // 整个纯发包状态机只在首步运行一次；其余步骤保留为可审计的流程模板。
            if (!string.Equals(step.State, "IDLE", StringComparison.Ordinal))
            {
                Socket_Operation.DoLog(
                    "EquipmentRefinePreset",
                    string.Format("装备炼化预设步骤 {0}：{1}", step.State, step.Description));
                return;
            }

            EquipmentRefinePreset preset = this.GetParameter("EquipmentRefinePreset") as EquipmentRefinePreset;
            if (preset == null)
            {
                throw new NotSupportedException(
                    "装备炼化预设未配置。请先保存目标装备、规则和已验收属性字段映射；未配置时不会发送任何封包。");
            }

            EquipmentRefinePreset runPreset = preset.Clone();
            string presetError;
            if (!runPreset.IsValid(out presetError))
            {
                throw new NotSupportedException(
                    "装备炼化预设无效：" + presetError);
            }

            string stateFilePath = this.GetParameter<string>(
                "EquipmentRefineStateFilePath",
                string.Empty);
            if (string.IsNullOrWhiteSpace(stateFilePath))
            {
                stateFilePath = runPreset.MemoryResultPath;
            }
            EquipmentRefineExecutor.RefinePacketTemplate packetTemplate =
                this.GetParameter("EquipmentRefinePacketTemplate") as EquipmentRefineExecutor.RefinePacketTemplate;
            string templatePath = this.GetParameter<string>(
                "EquipmentRefinePacketTemplatePath",
                string.Empty);
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                templatePath = runPreset.PacketTemplatePath;
            }
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                templatePath = Environment.GetEnvironmentVariable(
                    "WPE_EQUIPMENT_REFINE_TEMPLATE_FILE");
            }
            if (packetTemplate == null)
            {
                EquipmentRefineExecutor templateLoader = new EquipmentRefineExecutor();
                packetTemplate = templateLoader.LoadTemplate(
                    string.IsNullOrWhiteSpace(templatePath) ? null : templatePath);
            }

            IRefineMemoryResultSource memoryResultSource =
                this.GetParameter("EquipmentRefineMemoryResultSource") as IRefineMemoryResultSource;
            if (memoryResultSource == null && runPreset.MemoryResultMode)
            {
                string memoryResultPath = this.GetParameter<string>(
                    "EquipmentRefineMemoryResultPath",
                    string.Empty);
                if (string.IsNullOrWhiteSpace(memoryResultPath))
                {
                    memoryResultPath = runPreset.MemoryResultPath;
                }
                if (string.IsNullOrWhiteSpace(memoryResultPath))
                {
                    memoryResultPath = stateFilePath;
                }
                if (string.IsNullOrWhiteSpace(memoryResultPath))
                {
                    memoryResultPath = Environment.GetEnvironmentVariable(
                        "WPE_EQUIPMENT_STATE_FILE");
                }
                if (!string.IsNullOrWhiteSpace(memoryResultPath))
                {
                    memoryResultSource = new ResidentJsonlRefineMemoryResultSource(
                        memoryResultPath,
                        runPreset.VerifiedAttributeFields);
                }
            }

            EquipmentRefineExecutor.IRefinePacketSender packetSender =
                this.GetParameter("EquipmentRefinePacketSender") as EquipmentRefineExecutor.IRefinePacketSender;
            if (packetSender == null)
            {
                EquipmentRefineSocketRouteTemplate routeTemplate =
                    this.GetParameter("EquipmentRefineSocketRouteTemplate") as EquipmentRefineSocketRouteTemplate;
                EquipmentRefineLiveSendAuthorization authorization =
                    this.GetParameter("EquipmentRefineLiveSendAuthorization") as EquipmentRefineLiveSendAuthorization;
                if (authorization == null)
                {
                    string confirmation = this.GetParameter<string>(
                        "EquipmentRefineLiveSendConfirmation",
                        string.Empty);
                    if (!string.IsNullOrWhiteSpace(confirmation))
                    {
                        try
                        {
                            authorization = EquipmentRefineLiveSendAuthorization.Create(confirmation);
                        }
                        catch (ArgumentException ex)
                        {
                            Socket_Operation.DoLog(
                                "EquipmentRefinePreset",
                                "装备炼化真实发送授权无效：" + ex.Message);
                        }
                    }
                }

                if (routeTemplate != null || authorization != null)
                {
                    packetSender = new EquipmentRefineSocketPacketSender(
                        routeTemplate,
                        authorization);
                }
            }
            Func<CancellationToken, Task<EquipmentRefineDetector.EquipmentInventory>> inventoryProvider =
                this.GetParameter("EquipmentRefineInventoryProvider") as Func<CancellationToken, Task<EquipmentRefineDetector.EquipmentInventory>>;

            EquipmentRefineStateMachine.RefineConfiguration configuration =
                new EquipmentRefineStateMachine.RefineConfiguration
                {
                    PresetName = runPreset.Name,
                    StateFilePath = stateFilePath,
                    Target = runPreset.Target,
                    BagTargetMode = runPreset.BagTargetMode,
                    BagTarget = runPreset.BagTarget,
                    VerifiedAttributeFields = runPreset.VerifiedAttributeFields,
                    PacketTemplate = packetTemplate,
                    TypeCode = runPreset.TypeCode,
                    OperationCode = runPreset.OperationCode,
                    ActiveRules = runPreset.Rules,
                    RuleLogic = runPreset.RuleLogic,
                    MemoryResultMode = runPreset.MemoryResultMode,
                    MaxAttempts = runPreset.MaxAttempts,
                    IntervalMs = runPreset.IntervalMs,
                    ReadTimeoutMs = runPreset.AttributeReadTimeoutMs,
                    ResultConfirmTimeoutMs = runPreset.ResultConfirmTimeoutMs,
                    SkipTargetReached = runPreset.SkipTargetReached
                };

            EquipmentRefineExecutor executor = new EquipmentRefineExecutor(
                packetTemplate,
                packetSender);
            EquipmentRefineStateMachine machine = new EquipmentRefineStateMachine(
                configuration,
                executor,
                inventoryProvider);
            machine.ConfigureMemoryResultSource(memoryResultSource);
            EquipmentRefineStateMachine.ExecutionResult result = machine
                .RunAsync(this.cts)
                .GetAwaiter()
                .GetResult();
            this.LastEquipmentRefineResult = result;
            this.LastEquipmentRefineResultMessage = EquipmentRefineResultFormatter.Format(
                result,
                runPreset);

            Socket_Operation.DoLog(
                "EquipmentRefinePreset",
                string.Format(
                    "装备炼化运行结束：state={0}, reason={1}, attempts={2}, message={3}",
                    result.FinalState,
                    result.StopReason,
                    result.AttemptCount,
                    result.Message));

            if (!result.Success)
            {
                throw new NotSupportedException(
                    "装备炼化未完成：" + result.Message + "。未授权或未验收协议时已停止发包。");
            }
        }

        /// <summary>
        /// 从注入后记录的进程信息（形如“游戏名 [PID]”）解析目标进程 ID。
        /// </summary>
        private int ResolveInjectedProcessId()
        {
            string value = Socket_Cache.SocketPacket.InjectProcess;
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            int start = value.LastIndexOf('[');
            int end = value.LastIndexOf(']');
            if (start < 0 || end <= start)
            {
                return 0;
            }

            string token = value.Substring(start + 1, end - start - 1).Trim();
            int processId;
            return int.TryParse(token, out processId) ? processId : 0;
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

            // Prefer the native AutoDig route when the current connection has
            // a validated 0xB0F4 frame and either side of the Jump->Use pair
            // is unavailable. In particular, a current Use frame must not
            // force the strict Jump gate when this game build exposes Use but
            // does not expose a 0x5828 Jump frame.
            bool hasCurrentJumpTemplate = TreasurePacketRuntime.HasCurrentJumpTemplate();
            bool hasCurrentUseTemplate = TreasurePacketRuntime.HasCurrentUseTemplate();
            bool hasCurrentAutoDigPacket = TreasurePacketRuntime.HasCurrentAutoDigPacket();
            bool useNativeAutoDig = hasCurrentAutoDigPacket &&
                (!hasCurrentUseTemplate || !hasCurrentJumpTemplate);
            bool isLuoshenFuPreset = this.IsLuoshenFuTreasureMapPreset();
            bool isXiangjuChangAnPreset = this.IsXiangjuChangAnTreasureMapPreset();

            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                LiveSendEnabled = liveSendEnabled,
                // 洛神赋沿用已复制的藏宝图指令，但其当前客户端运行日志
                // 没有提供 0x5828 Jump 模板；仅对这一预设开放现有的
                // 当前连接编码兼容路径，其他预设继续严格要求会话模板。
                RequireCurrentPacketTemplates = !isLuoshenFuPreset,
                Mode = mode,
                Version = instruction.Version,
                ControlMode = instruction.Control,
                // 旧版 V1/V2 指令没有可用的到达证据字段；当前生产路径只观察
                // 接收侧证据，不因证据缺失阻断 Jump→Use。历史现场已证明该类
                // 到达帧不会稳定出现；V3 保留显式策略。
                EvidenceMode = instruction.Version == TreasureMapInstructionVersion.V3
                    ? instruction.Evidence
                    : TreasureEvidenceMode.Shadow,
                // 有当前 Use 模板时保持兼容性 Jump→Use；没有时仅复用已校验的
                // 当前路由 AutoDig 帧。Jump/Use 缺少当前会话模板时在发送前零发送；
                // 到达证据只作诊断，消费确认超时后才按完整目标重试，避免打断当前移动。
                UseNativeAutoDig = useNativeAutoDig,
                RequireActionTemplateBeforeJump = isXiangjuChangAnPreset,
                ArrivalEvidenceTimeoutMilliseconds = 1000,
                TargetStabilityMilliseconds = 20,
                // 现场正常客户端样本的 Jump→Use 间隔至少约 1.6 秒；原先
                // 300ms 会在地图状态尚未稳定时继续发 Use，连续多张后容易
                // 触发服务端断线。生产入口留出保守缓冲，不改离线默认值。
                JumpUseDelayMilliseconds = isXiangjuChangAnPreset ? 1800 : 300,
                NextTargetDelayMilliseconds = isXiangjuChangAnPreset ? 1000 : 0,
                MinimumJumpIntervalMilliseconds = isXiangjuChangAnPreset ? 1800 : 800,
                StopContinuousOnTransportFailure = isXiangjuChangAnPreset,
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
                        new TreasureMapRuntimePacketSender(
                            isXiangjuChangAnPreset,
                            isLuoshenFuPreset),
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
            if (profile == null)
            {
                return new VisionAssistantRunResult
                {
                    Error = "Vision instruction requires a configured vision profile."
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

            VisionAssistantStep step = profile.AssistantSteps[stepIndex];
            if (StepRequiresVisionOcr(step) && recognizer == null)
            {
                return new VisionAssistantRunResult
                {
                    Error = "该视觉步骤需要 OCR 条件，但当前运行未配置 OCR。"
                };
            }
            if (VisionUsesAirtest(profile) &&
                !(recognizer is IVisionCaptureProvider))
            {
                return new VisionAssistantRunResult
                {
                    Error = "Airtest 截图需要 Python Worker 捕获提供器。"
                };
            }

            return VisionAssistantRunner.Run(
                profile,
                new[] { step },
                recognizer,
                this.cts == null ? CancellationToken.None : this.cts.Token,
                null);
        }

        private bool HasVisionInstructionRows()
        {
            if (this.RobotInstruction == null || this.RobotInstruction.Rows.Count <= 0)
            {
                return false;
            }

            foreach (DataRow row in this.RobotInstruction.Rows)
            {
                if (row == null || row["Type"] == null || row["Type"] == DBNull.Value)
                {
                    continue;
                }

                if (Convert.ToInt32(row["Type"]) ==
                    (int)Socket_Cache.Robot.InstructionType.VisionWait)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryPrepareVisionRuntime()
        {
            if (!this.HasVisionInstructionRows())
            {
                return true;
            }

            Socket_VisionProfile profile = this.GetParameter("VisionProfile") as Socket_VisionProfile;
            if (profile == null || profile.AssistantSteps == null || profile.AssistantSteps.Count == 0)
            {
                this.LastStartFailureReason =
                    "视觉助手配置未加载，请先在助手设置中保存目标窗口和唤醒检测步骤。";
                return false;
            }
            if (profile.WindowHandle == 0)
            {
                this.LastStartFailureReason =
                    "视觉助手尚未绑定目标窗口，请先在助手设置中选择并保存目标窗口。";
                return false;
            }

            IVisionTextRecognizer recognizer =
                this.GetParameter("VisionTextRecognizer") as IVisionTextRecognizer;
            if (recognizer != null)
            {
                if (VisionUsesAirtest(profile) &&
                    !(recognizer is IVisionCaptureProvider))
                {
                    this.LastStartFailureReason =
                        "Airtest 截图需要 Python Worker 捕获提供器。";
                    return false;
                }
                return true;
            }

            if (VisionUsesAirtest(profile))
            {
                VisionPythonWorkerTextRecognizer captureProvider =
                    new VisionPythonWorkerTextRecognizer();
                this._parameters["VisionTextRecognizer"] = captureProvider;
                this.ownedVisionTextRecognizer = captureProvider;
                return true;
            }

            if (!VisionFlowRequiresOcr(profile))
            {
                // Native window capture, template matching, color matching and
                // native mouse input do not need an OCR runtime at all.
                return true;
            }

            VisionOcrOptions options = profile.OcrOptions ?? new VisionOcrOptions();
            VisionAutoTextRecognizer ownedRecognizer = new VisionAutoTextRecognizer(
                new VisionOnnxTextRecognizer(),
                new VisionTesseractRecognizer(
                    string.IsNullOrWhiteSpace(options.ExecutablePath)
                        ? "tesseract.exe"
                        : options.ExecutablePath),
                new VisionPythonWorkerTextRecognizer());
            this._parameters["VisionTextRecognizer"] = ownedRecognizer;
            this.ownedVisionTextRecognizer = ownedRecognizer;
            return true;
        }

        private static bool StepRequiresVisionOcr(VisionAssistantStep step)
        {
            return step != null &&
                (IsVisionOcrCondition(step.Condition) ||
                 (step.VerificationEnabled && IsVisionOcrCondition(step.Verification)));
        }

        private static bool IsVisionOcrCondition(VisionConditionDefinition condition)
        {
            return condition != null &&
                (condition.Type == VisionConditionType.TextAppears ||
                 condition.Type == VisionConditionType.TextDisappears ||
                 condition.Type == VisionConditionType.NumberInRange);
        }

        private static bool VisionFlowRequiresOcr(Socket_VisionProfile profile)
        {
            return profile != null &&
                profile.AssistantSteps != null &&
                profile.AssistantSteps.Any(StepRequiresVisionOcr);
        }

        private static bool VisionUsesAirtest(Socket_VisionProfile profile)
        {
            return profile != null &&
                profile.CaptureSettings != null &&
                profile.CaptureSettings.SourceMode == VisionCaptureSourceMode.Airtest;
        }

        private void DisposeOwnedVisionResources()
        {
            IDisposable mountStatusReader = this.GetParameter(
                "MountStatusAndroidSnapshotReader") as IDisposable;
            this._parameters.Remove("MountStatusAndroidSnapshotReader");
            if (mountStatusReader != null)
            {
                try
                {
                    mountStatusReader.Dispose();
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(
                        nameof(DisposeOwnedVisionResources),
                        ex.Message);
                }
            }

            IDisposable recognizer = this.ownedVisionTextRecognizer;
            this.ownedVisionTextRecognizer = null;
            if (recognizer != null)
            {
                try
                {
                    recognizer.Dispose();
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(
                        nameof(DisposeOwnedVisionResources),
                        ex.Message);
                }
            }

            Socket_VisionProfile profile = this.ownedVisionProfile;
            this.ownedVisionProfile = null;
            if (profile == null || profile.AssistantSteps == null)
            {
                return;
            }

            foreach (VisionAssistantStep step in profile.AssistantSteps)
            {
                if (step == null)
                {
                    continue;
                }

                DisposeVisionConditionTemplates(step.Condition);
                DisposeVisionConditionTemplates(step.Verification);
            }
        }

        private static void DisposeVisionConditionTemplates(VisionConditionDefinition condition)
        {
            if (condition == null)
            {
                return;
            }
            if (condition.Template != null)
            {
                condition.Template.Dispose();
                condition.Template = null;
            }
            condition.DisposeTemplateVariants();
        }

        private bool IsXiangjuChangAnTreasureMapPreset()
        {
            string name = (this.RobotName ?? string.Empty).Trim();
            return name.StartsWith("相聚长安", StringComparison.Ordinal);
        }

        private bool IsLuoshenFuTreasureMapPreset()
        {
            string name = (this.RobotName ?? string.Empty).Trim();
            return string.Equals(name, "洛神赋", StringComparison.Ordinal);
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
                    this.LastRunFailureReason = e.Error.Message;
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
                this.DisposeOwnedVisionResources();
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

        #region//五行升级指令处理

        private void RunFiveElementUpgradePlanInstruction(string instructionContent)
        {
            WuxingUpgradePresetStep step;
            if (!WuxingUpgradePresetPlan.TryDecodeStep(
                    instructionContent,
                    out step))
            {
                throw new InvalidOperationException("五行升级预设步骤内容无效，已停止执行。");
            }

            // 非发送类步骤只做日志，不涉及真实网络流量
            if (!step.State.StartsWith("SEND_", StringComparison.Ordinal))
            {
                Socket_Operation.DoLog(
                    "FiveElementUpgradePreset",
                    string.Format("五行升级预设步骤 {0}：{1}", step.State, step.Description));
                return;
            }

            // 发送类步骤：从捕获列表推导 C2S_UseCsCard 模板并修补 cardId 后发送
            int cardId = ResolveWuxingCardId(step.State);
            WuxingUpgradeOperation operation;
            if (step.State == "SEND_OVERALL")
            {
                operation = new WuxingUpgradeOperation
                {
                    ElementType = ElementalType.Overall,
                    CardTypeId = CsCardTypeId.Overall,
                    CardId = cardId,
                    OperaType = (int)CsCardTypeId.Overall,
                    IsOverallUpgrade = true
                };
            }
            else
            {
                ElementalType elementType = ResolveWuxingElementType(step.State);
                operation = new WuxingUpgradeOperation
                {
                    ElementType = elementType,
                    CardTypeId = (CsCardTypeId)elementType,
                    CardId = cardId,
                    OperaType = (int)elementType,
                    IsOverallUpgrade = false
                };
            }

            WuxingUpgradeResult result = new WuxingUpgradePacketAdapter()
                .SubmitElementUpgradeAsync(operation, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (result != WuxingUpgradeResult.Accepted)
            {
                throw new NotSupportedException(
                    string.Format(
                        "五行升级预设步骤 {0} 未能发送升级请求（结果：{1}）。" +
                        "\n请先在游戏会话中手动执行一次五行升级，使捕获列表中出现 C2S_UseCsCard (0x00FA) 封包后重试。",
                        step.State,
                        result));
            }

            Socket_Operation.DoLog(
                "FiveElementUpgradePreset",
                string.Format(
                    "五行升级预设步骤 {0}：已按捕获模板发送升级请求（cardId={1}）。",
                    step.State,
                    cardId));
        }

        private static int ResolveWuxingCardId(string state)
        {
            // 卡片 ID 使用预设默认值（未经验证的占位值，需按真实抓包结果修正）
            WuxingUpgradePreset preset = new WuxingUpgradePreset();
            switch (state)
            {
                case "SEND_WOOD": return preset.WoodCardId;
                case "SEND_WATER": return preset.WaterCardId;
                case "SEND_SOIL": return preset.SoilCardId;
                case "SEND_GOLD": return preset.GoldCardId;
                case "SEND_FIRE": return preset.FireCardId;
                case "SEND_OVERALL": return preset.OverallUpgradeCardId;
                default:
                    throw new InvalidOperationException(
                        string.Format("五行升级预设步骤 {0} 不支持发送操作。", state));
            }
        }

        private static ElementalType ResolveWuxingElementType(string state)
        {
            switch (state)
            {
                case "SEND_WOOD": return ElementalType.Wood;
                case "SEND_WATER": return ElementalType.Water;
                case "SEND_SOIL": return ElementalType.Soil;
                case "SEND_GOLD": return ElementalType.Gold;
                case "SEND_FIRE": return ElementalType.Fire;
                default:
                    throw new InvalidOperationException(
                        string.Format("五行升级预设步骤 {0} 不是元素升级步骤。", state));
            }
        }

        #endregion

        #region//法术升级指令处理

        private void RunSkillUpgradePlanInstruction(string instructionContent)
        {
            SkillUpgradePresetStep step;
            if (!SkillUpgradePresetPlan.TryDecodeStep(
                    instructionContent,
                    out step))
            {
                throw new InvalidOperationException("法术升级预设步骤内容无效，已停止执行。");
            }

            // 非发送类步骤只做日志
            if (!step.State.StartsWith("SEND_", StringComparison.Ordinal))
            {
                Socket_Operation.DoLog(
                    "SkillUpgradePreset",
                    string.Format("法术升级预设步骤 {0}：{1}", step.State, step.Description));
                return;
            }

            // 顺序执行预设任务列表中的全部法术升级任务
            SkillUpgradePreset preset = ResolveSkillUpgradePreset();
            string presetError = string.Empty;
            if (preset == null || !preset.IsValid(out presetError))
            {
                throw new NotSupportedException(
                    "法术升级预设无效：" +
                    (string.IsNullOrEmpty(presetError) ? "任务列表为空。" : presetError));
            }

            // 先做一次模板预检：没有捕获模板时直接给出明确提示，
            // 避免机器人看似启动成功却一个请求都没发出去
            SkillUpgradeTemplate template = null;
            try
            {
                List<Socket_PacketInfo> capturedPackets = new List<Socket_PacketInfo>();
                Action capture = () =>
                {
                    capturedPackets = Socket_Cache.SocketList.lstRecPacket
                        .Where(item => item != null &&
                                       item.PacketBuffer != null &&
                                       item.PacketBuffer.Length > 0)
                        .ToList();
                };

                if (Socket_Cache.System.InvokeAction != null)
                {
                    Socket_Cache.System.InvokeAction(capture);
                }
                else
                {
                    capture();
                }

                template = SkillUpgradePacketRuntime.DiscoverTemplate(capturedPackets);
            }
            catch (SkillUpgradePacketRuntimeException)
            {
                template = null;
            }

            if (template == null)
            {
                throw new NotSupportedException(
                    "捕获列表中未找到 C2S_LearnSkill (0x2074) 模板。" +
                    "\n请先在游戏会话中手动执行一次法术升级，使捕获列表出现该封包后重试。");
            }

            int sentCount = 0;
            for (int i = 0; i < preset.Tasks.Count; i++)
            {
                SkillUpgradeTask task = preset.Tasks[i];

                // 从捕获模板修补 skillId / learnLevel 后发送（roleId 沿用模板真实值）
                SkillUpgradeResult result = new SkillUpgradePacketAdapter(template, 0)
                    .SubmitLearnSkillAsync(task.SkillId, task.TargetLevel, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                if (result != SkillUpgradeResult.Accepted)
                {
                    throw new NotSupportedException(
                        string.Format(
                            "法术升级任务 {0}/{1} 未能发送升级请求（skillId={2}，结果：{3}）。",
                            i + 1,
                            preset.Tasks.Count,
                            task.SkillId,
                            result));
                }

                sentCount++;
                Socket_Operation.DoLog(
                    "SkillUpgradePreset",
                    string.Format(
                        "法术升级预设步骤 {0}：已按捕获模板发送升级请求 {1}/{2}（skillId={3}, learnLevel={4}）。",
                        step.State,
                        i + 1,
                        preset.Tasks.Count,
                        task.SkillId,
                        task.TargetLevel));

                if (preset.WaitForRequestInterval &&
                    preset.RequestIntervalMs > 0 &&
                    i < preset.Tasks.Count - 1)
                {
                    Thread.Sleep(preset.RequestIntervalMs);
                }
            }

            Socket_Operation.DoLog(
                "SkillUpgradePreset",
                string.Format("法术升级本轮完成：共发送 {0} 个升级请求。", sentCount));
        }

        private static SkillUpgradePreset ResolveSkillUpgradePreset()
        {
            if (Socket_Cache.System.CurrentSkillUpgradePreset != null)
            {
                return Socket_Cache.System.CurrentSkillUpgradePreset;
            }
            return SkillUpgradePreset.CreateDefault();
        }

        #endregion
    }
}
