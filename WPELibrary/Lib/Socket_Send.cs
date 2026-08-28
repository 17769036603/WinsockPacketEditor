using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Linq;
using System.Threading;
using WPELibrary.Lib.Vision;

namespace WPELibrary.Lib
{
    public class Socket_Send
    {
        public bool SystemSocket = false;
        public int LoopCNT = 0;
        public int LoopINT = 0;
        public int SendCollection_Index = 0;
        public int Send_Success = 0;
        public int Send_Failure = 0;
        public int Total_Send = 0;
        public string SendName = string.Empty;

        private CancellationTokenSource cts;
        private int resolvedSystemSocket;
        private bool usePacketSockets;
        private bool refreshCurrentRouteBeforeEachSend;
        private bool stopOnRouteOrSendFailure;
        private List<Socket_PacketInfo> SendCollection;
        private readonly ManualResetEventSlim sendStopped = new ManualResetEventSlim(true);
        private readonly ManualResetEventSlim sendPauseGate = new ManualResetEventSlim(true);
        private static readonly SocketSendDiagnosticLogStore PanguIronSaleDiagnosticLog =
            SocketSendDiagnosticLogStore.CreateDefault();
        public BackgroundWorker Worker = new BackgroundWorker();

        private static bool RequiresPanguIronSaleTransportProtection(string sendName)
        {
            return Socket_Cache.Send.IsPanguIronSalePreset(sendName);
        }

        private static int GetEffectiveLoopInterval(string sendName, int loopInterval)
        {
            return RequiresPanguIronSaleTransportProtection(sendName)
                ? Math.Max(loopInterval, 1800)
                : loopInterval;
        }

        private static bool IsProtectedPacket(byte[] buffer)
        {
            return buffer != null &&
                buffer.Length >= 12 &&
                buffer[0] == 0x4D &&
                buffer[1] == 0x5A &&
                (((buffer[8] << 8) | buffer[9]) == buffer.Length - 10) &&
                ((buffer[10] == 0x30 && buffer[11] == 0x44) ||
                 (buffer[10] == 0x40 && buffer[11] == 0x62) ||
                 (buffer[10] == 0x70 && buffer[11] == 0xAB) ||
                 (buffer[10] == 0xF9 && buffer[11] == 0x08));
        }

        private static bool TryPrepareProtectedPacket(
            byte[] source,
            out byte[] prepared,
            out string reason)
        {
            prepared = source == null ? null : (byte[])source.Clone();
            reason = string.Empty;
            if (!IsProtectedPacket(prepared))
            {
                reason = "protected_packet_contract_invalid";
                return false;
            }

            if (!TreasurePacketRuntime.TryPrepareCurrentSessionSequence(
                prepared,
                out reason))
            {
                return false;
            }

            reason = "session_sequence_patched";
            return true;
        }

        #region//初始化

        public Socket_Send()
        {
            this.Worker.WorkerSupportsCancellation = true;
            this.Worker.WorkerReportsProgress = true;

            this.Worker.DoWork -= Send_DoWork;
            this.Worker.DoWork += Send_DoWork;

            this.Worker.ProgressChanged -= Send_ProgressChanged;
            this.Worker.ProgressChanged += Send_ProgressChanged;

            this.Worker.RunWorkerCompleted -= Send_RunCompleted;
            this.Worker.RunWorkerCompleted += Send_RunCompleted;
        }

        #endregion

        #region//启动发送

        public bool StartSend(string SendName, bool SystemSocket, int LoopCNT, int LoopINT, BindingList<Socket_PacketInfo> SendCollection)
        {
            bool protectPanguIronSale = RequiresPanguIronSaleTransportProtection(SendName);
            int socketSnapshot = SystemSocket
                ? Socket_Cache.System.SystemSocket
                : 0;
            return this.StartSendCore(
                SendName,
                protectPanguIronSale ? false : SystemSocket,
                protectPanguIronSale ? 0 : socketSnapshot,
                LoopCNT,
                GetEffectiveLoopInterval(SendName, LoopINT),
                SendCollection,
                protectPanguIronSale,
                protectPanguIronSale,
                protectPanguIronSale);
        }

        public bool StartSend(string SendName, int ResolvedSystemSocket, int LoopCNT, int LoopINT, BindingList<Socket_PacketInfo> SendCollection)
        {
            bool protectPanguIronSale = RequiresPanguIronSaleTransportProtection(SendName);
            return this.StartSendCore(
                SendName,
                protectPanguIronSale ? false : true,
                protectPanguIronSale ? 0 : ResolvedSystemSocket,
                LoopCNT,
                GetEffectiveLoopInterval(SendName, LoopINT),
                SendCollection,
                protectPanguIronSale,
                protectPanguIronSale,
                protectPanguIronSale);
        }

        public bool StartSendWithPacketSockets(
            string SendName,
            int LoopCNT,
            int LoopINT,
            BindingList<Socket_PacketInfo> SendCollection)
        {
            return this.StartSendWithPacketSockets(
                SendName,
                LoopCNT,
                LoopINT,
                SendCollection,
                false,
                false);
        }

        public bool StartSendWithPacketSockets(
            string SendName,
            int LoopCNT,
            int LoopINT,
            BindingList<Socket_PacketInfo> SendCollection,
            bool refreshCurrentRouteBeforeEachSend,
            bool stopOnRouteOrSendFailure)
        {
            bool protectPanguIronSale = RequiresPanguIronSaleTransportProtection(SendName);
            return this.StartSendCore(
                SendName,
                false,
                0,
                LoopCNT,
                GetEffectiveLoopInterval(SendName, LoopINT),
                SendCollection,
                true,
                refreshCurrentRouteBeforeEachSend || protectPanguIronSale,
                stopOnRouteOrSendFailure || protectPanguIronSale);
        }

        private bool StartSendCore(
            string SendName,
            bool SystemSocket,
            int ResolvedSystemSocket,
            int LoopCNT,
            int LoopINT,
            BindingList<Socket_PacketInfo> SendCollection,
            bool usePacketSockets,
            bool refreshCurrentRouteBeforeEachSend,
            bool stopOnRouteOrSendFailure)
        {
            try
            {
                if (SendCollection == null || SendCollection.Count == 0)
                {
                    return false;
                }
                if (LoopCNT < 0 || LoopINT < 0)
                {
                    return false;
                }
                if (this.Worker.IsBusy)
                {
                    return false;
                }

                this.Total_Send = 0;
                this.Send_Success = 0;
                this.Send_Failure = 0;

                this.SendName = SendName;
                this.SystemSocket = SystemSocket;
                this.resolvedSystemSocket = Math.Max(0, ResolvedSystemSocket);
                this.usePacketSockets = usePacketSockets;
                this.refreshCurrentRouteBeforeEachSend = refreshCurrentRouteBeforeEachSend;
                this.stopOnRouteOrSendFailure = stopOnRouteOrSendFailure;
                this.LoopCNT = LoopCNT;
                this.LoopINT = LoopINT;
                this.SendCollection = CreateSendSnapshot(SendCollection);
                this.WritePanguIronSaleDiagnostic(
                    "start",
                    -1,
                    null,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    true,
                    string.Format(
                        "loopCount={0};loopInterval={1};collectionCount={2}",
                        LoopCNT,
                        LoopINT,
                        this.SendCollection.Count));

                this.cts = new CancellationTokenSource();
                this.sendPauseGate.Set();
                this.sendStopped.Reset();
                try
                {
                    this.Worker.RunWorkerAsync();
                }
                catch
                {
                    this.sendStopped.Set();
                    throw;
                }

                string sLog = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_84), this.SendName);
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLog);
                return true;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
                return false;
            }
        }

        #endregion

        #region//停止发送

        public void StopSend()
        {
            try
            {
                if (this.Worker.IsBusy)
                {
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

        public bool WaitForStop(int millisecondsTimeout)
        {
            this.StopSend();
            if (millisecondsTimeout == Timeout.Infinite)
            {
                this.sendStopped.Wait();
                return true;
            }
            return this.sendStopped.Wait(Math.Max(0, millisecondsTimeout));
        }

        public bool WaitForCompletion(int millisecondsTimeout)
        {
            if (millisecondsTimeout == Timeout.Infinite)
            {
                this.sendStopped.Wait();
                return true;
            }
            return this.sendStopped.Wait(Math.Max(0, millisecondsTimeout));
        }

        public bool PauseSend()
        {
            if (!this.Worker.IsBusy)
            {
                return false;
            }

            this.sendPauseGate.Reset();
            return true;
        }

        public bool ResumeSend()
        {
            this.sendPauseGate.Set();
            return true;
        }

        public bool IsPaused
        {
            get { return !this.sendPauseGate.IsSet && this.Worker.IsBusy; }
        }

        private static List<Socket_PacketInfo> CreateSendSnapshot(BindingList<Socket_PacketInfo> source)
        {
            List<Socket_PacketInfo> snapshot = new List<Socket_PacketInfo>(source.Count);
            foreach (Socket_PacketInfo packet in source)
            {
                if (packet == null)
                {
                    snapshot.Add(null);
                    continue;
                }

                snapshot.Add(new Socket_PacketInfo
                {
                    PacketTime = packet.PacketTime,
                    PacketSocket = packet.PacketSocket,
                    PacketType = packet.PacketType,
                    PacketFrom = packet.PacketFrom,
                    PacketTo = packet.PacketTo,
                    RawBuffer = packet.RawBuffer == null ? null : (byte[])packet.RawBuffer.Clone(),
                    PacketBuffer = packet.PacketBuffer == null ? null : (byte[])packet.PacketBuffer.Clone(),
                    PacketData = packet.PacketData,
                    PacketLen = packet.PacketLen,
                    FilterAction = packet.FilterAction,
                    ByteAnnotations = Socket_ByteAnnotationEngine.Clone(packet.ByteAnnotations),
                    VariableBindings = (packet.VariableBindings ?? new List<PresetVariableBinding>())
                        .Where(item => item != null)
                        .Select(item => item.Clone())
                        .ToList(),
                    SortOrder = packet.SortOrder
                });
            }

            return snapshot;
        }

        #endregion

        #region//执行发送集

        private void Send_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                if (this.SystemSocket && !this.usePacketSockets)
                {
                    if (this.resolvedSystemSocket <= 0)
                    {
                        throw new InvalidOperationException(
                            MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_49));
                    }
                }

                int loopIndex = 0;
                while (this.LoopCNT == 0 || loopIndex < this.LoopCNT)
                {
                    foreach (Socket_PacketInfo spi in this.SendCollection)
                    {
                        this.sendPauseGate.Wait(this.cts.Token);
                        if (Worker.CancellationPending)
                        {
                            e.Cancel = true;
                            return;
                        }
                        else
                        {
                            int Socket = spi == null ? 0 : spi.PacketSocket;
                            string packetFrom = spi == null ? string.Empty : spi.PacketFrom;
                            string packetTo = spi == null ? string.Empty : spi.PacketTo;
                            this.WritePanguIronSaleDiagnostic(
                                "attempt",
                                loopIndex,
                                spi,
                                Socket,
                                packetFrom,
                                packetTo,
                                0,
                                0,
                                false,
                                string.Empty);
                            if (this.SystemSocket && !this.usePacketSockets && spi != null)
                            {
                                Socket = this.resolvedSystemSocket;
                            }

                            bool routeRefreshFailed = false;
                            if (this.refreshCurrentRouteBeforeEachSend &&
                                spi != null &&
                                spi.PacketBuffer != null &&
                                spi.PacketBuffer.Length > 0)
                            {
                                string routeFailure;
                                if (!this.TryRefreshCurrentRoute(
                                    spi,
                                    out Socket,
                                    out packetFrom,
                                    out packetTo,
                                    out routeFailure))
                                {
                                    this.WritePanguIronSaleDiagnostic(
                                        "route_failed",
                                        loopIndex,
                                        spi,
                                        Socket,
                                        packetFrom,
                                        packetTo,
                                        0,
                                        0,
                                        false,
                                        routeFailure);
                                    this.Send_Failure++;
                                    this.Total_Send++;
                                    if (this.stopOnRouteOrSendFailure)
                                    {
                                        this.LogFailClosedStop(
                                            "当前连接不可用：" + routeFailure);
                                        e.Cancel = true;
                                        return;
                                    }
                                    routeRefreshFailed = true;
                                }
                                else
                                {
                                    this.WritePanguIronSaleDiagnostic(
                                        "route_resolved",
                                        loopIndex,
                                        spi,
                                        Socket,
                                        packetFrom,
                                        packetTo,
                                        0,
                                        0,
                                        true,
                                        routeFailure);
                                }
                            }

                            if (routeRefreshFailed)
                            {
                                continue;
                            }

                            if (Socket <= 0 || spi == null || spi.PacketBuffer == null || spi.PacketBuffer.Length == 0)
                            {
                                this.WritePanguIronSaleDiagnostic(
                                    "invalid",
                                    loopIndex,
                                    spi,
                                    Socket,
                                    packetFrom,
                                    packetTo,
                                    0,
                                    0,
                                    false,
                                    "封包或 Socket 无效");
                                this.Send_Failure++;
                                this.Total_Send++;
                                if (this.stopOnRouteOrSendFailure)
                                {
                                    this.LogFailClosedStop("封包或 Socket 无效");
                                    e.Cancel = true;
                                    return;
                                }
                            }
                            else
                            {
                                byte[] packetToSend = spi.PacketBuffer;
                                string packetPreparationReason = string.Empty;
                                if (RequiresPanguIronSaleTransportProtection(this.SendName))
                                {
                                    if (!TryPrepareProtectedPacket(
                                        spi.PacketBuffer,
                                        out packetToSend,
                                        out packetPreparationReason))
                                    {
                                        this.WritePanguIronSaleDiagnostic(
                                            "prepare_failed",
                                            loopIndex,
                                            spi,
                                            Socket,
                                            packetFrom,
                                            packetTo,
                                            0,
                                            0,
                                            false,
                                            packetPreparationReason);
                                        this.Send_Failure++;
                                        this.Total_Send++;
                                        if (this.stopOnRouteOrSendFailure)
                                        {
                                            this.LogFailClosedStop(
                                                "受保护预设封包准备失败：" +
                                                packetPreparationReason);
                                            e.Cancel = true;
                                            return;
                                        }

                                        continue;
                                    }
                                }

                                int bytesSent = 0;
                                int socketError = 0;
                                bool bOK;
                                if (this.stopOnRouteOrSendFailure)
                                {
                                    bOK = Socket_Operation.SendPacket(
                                        Socket,
                                        spi.PacketType,
                                        packetFrom,
                                        packetTo,
                                        packetToSend,
                                        out bytesSent,
                                        out socketError);
                                }
                                else
                                {
                                    bOK = Socket_Operation.SendPacket(Socket, spi.PacketType, spi.PacketFrom, spi.PacketTo, spi.PacketBuffer);
                                }

                                this.WritePanguIronSaleDiagnostic(
                                    "send_result",
                                    loopIndex,
                                    spi,
                                    Socket,
                                    packetFrom,
                                    packetTo,
                                    bytesSent,
                                    socketError,
                                    bOK,
                                    bOK
                                        ? packetPreparationReason
                                        : "native_send_failed;prepared=" +
                                            packetPreparationReason);

                                if (bOK)
                                {
                                    this.Send_Success++;
                                }
                                else
                                {
                                    this.Send_Failure++;
                                    if (this.stopOnRouteOrSendFailure)
                                    {
                                        this.LogFailClosedStop(
                                            string.Format(
                                                "Socket 写入失败：bytesSent={0};wsaError={1}",
                                                bytesSent,
                                                socketError));
                                        e.Cancel = true;
                                        return;
                                    }
                                }

                                this.Total_Send++;
                            }

                            if (this.LoopINT > 0)
                            {
                                Worker.ReportProgress(loopIndex);
                                Socket_Operation.DoSleepAsync(this.LoopINT, this.cts.Token)
                                    .GetAwaiter()
                                    .GetResult();
                            }
                        }
                    }

                    if (loopIndex < int.MaxValue)
                    {
                        loopIndex++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                e.Cancel = true;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
                throw;
            }
            finally
            {
                this.sendStopped.Set();
            }
        }

        #endregion

        private bool TryRefreshCurrentRoute(
            Socket_PacketInfo packet,
            out int socket,
            out string packetFrom,
            out string packetTo,
            out string failureReason)
        {
            socket = 0;
            packetFrom = packet == null ? string.Empty : packet.PacketFrom;
            packetTo = packet == null ? string.Empty : packet.PacketTo;
            failureReason = string.Empty;

            if (packet == null)
            {
                failureReason = "发送封包为空";
                return false;
            }

            try
            {
                Socket_Cache.SocketList.CurrentSocketRouteResolution resolution =
                    Socket_Cache.SocketList.ResolveCurrentRoute(packet);
                if (resolution == null || !resolution.Succeeded || resolution.Route == null)
                {
                    // The treasure sender keeps a session-scoped socket after
                    // the visible capture list auto-clears. Reuse that narrow
                    // fallback for the protected sale preset only; an
                    // ambiguous live route still fails closed above.
                    if (RequiresPanguIronSaleTransportProtection(this.SendName) &&
                        (resolution == null ||
                         resolution.Status != Socket_Cache.SocketList.CurrentSocketRouteStatus.Ambiguous))
                    {
                        int sessionSocket = TreasurePacketRuntime.ResolveCurrentSessionSocket(
                            packet.PacketType,
                            packet.PacketTo);
                        if (sessionSocket > 0)
                        {
                            string sessionFrom = Socket_Operation.GetIP_BySocket(
                                sessionSocket,
                                Socket_Cache.SocketPacket.IPType.From);
                            string sessionTo = Socket_Operation.GetIP_BySocket(
                                sessionSocket,
                                Socket_Cache.SocketPacket.IPType.To);
                            if (IsUsableRouteAddress(sessionFrom))
                            {
                                socket = sessionSocket;
                                packetFrom = sessionFrom;
                                packetTo = IsUsableRouteAddress(sessionTo)
                                    ? sessionTo
                                    : packet.PacketTo;
                                failureReason = "session_cache_fallback";
                                return true;
                            }
                        }
                    }

                    failureReason = resolution == null
                        ? "当前连接解析没有结果"
                        : string.IsNullOrWhiteSpace(resolution.ErrorMessage)
                            ? resolution.ErrorCode
                            : resolution.ErrorCode + ":" + resolution.ErrorMessage;
                    return false;
                }

                socket = resolution.Route.Socket;
                packetFrom = resolution.Route.PacketFrom;
                packetTo = resolution.Route.PacketTo;
                return socket > 0;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }
        }

        private static bool IsUsableRouteAddress(string address)
        {
            return !string.IsNullOrWhiteSpace(address) &&
                !string.Equals(
                    address.Trim(),
                    "0.0.0.0:0",
                    StringComparison.OrdinalIgnoreCase);
        }

        private void LogFailClosedStop(string reason)
        {
            this.WritePanguIronSaleDiagnostic(
                "stopped",
                -1,
                null,
                0,
                string.Empty,
                string.Empty,
                0,
                0,
                false,
                reason);
            Socket_Operation.DoLog(
                nameof(Socket_Send),
                "发送预设[" + (this.SendName ?? string.Empty) + "]已停止：" +
                (string.IsNullOrWhiteSpace(reason) ? "未知原因" : reason));
        }

        private void WritePanguIronSaleDiagnostic(
            string eventName,
            int loopIndex,
            Socket_PacketInfo packet,
            int socket,
            string packetFrom,
            string packetTo,
            int bytesSent,
            int wsaError,
            bool success,
            string reason)
        {
            if (!RequiresPanguIronSaleTransportProtection(this.SendName))
            {
                return;
            }

            SocketSendDiagnosticEntry entry = new SocketSendDiagnosticEntry
            {
                TimestampUtc = DateTime.UtcNow,
                EventName = eventName,
                PresetName = this.SendName,
                LoopIndex = loopIndex,
                PacketType = packet == null ? string.Empty : packet.PacketType.ToString(),
                Socket = socket,
                PacketFrom = packetFrom,
                PacketTo = packetTo,
                PacketLength = packet == null || packet.PacketBuffer == null
                    ? 0
                    : packet.PacketBuffer.Length,
                BytesSent = bytesSent,
                WsaError = wsaError,
                Success = success,
                Reason = reason,
                TotalSend = this.Total_Send,
                SendSuccess = this.Send_Success,
                SendFailure = this.Send_Failure
            };

            if (!PanguIronSaleDiagnosticLog.TryAppend(entry) &&
                !string.IsNullOrWhiteSpace(PanguIronSaleDiagnosticLog.LastError))
            {
                Socket_Operation.DoLog(
                    nameof(Socket_Send),
                    "发送诊断日志落盘失败：" + PanguIronSaleDiagnosticLog.LastError);
            }
        }

        #region//汇报进度

        private void Send_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            this.SendCollection_Index = e.ProgressPercentage;
        }

        #endregion

        #region//执行完毕

        private void Send_RunCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                if (e.Cancelled)
                {
                    string sLog = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_163), this.SendName);
                    Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLog);
                }
                else if (e.Error != null)
                {
                    string sLog = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_164), this.SendName, e.Error.Message);
                    Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLog);
                }
                else
                {
                    string sLog = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_165), this.SendName);
                    Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, sLog);
                }

                this.cts?.Dispose();
                this.cts = null;
                this.sendPauseGate.Set();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion
    }
}
