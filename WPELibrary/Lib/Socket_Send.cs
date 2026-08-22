using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Linq;
using System.Threading;

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
        private List<Socket_PacketInfo> SendCollection;
        private readonly ManualResetEventSlim sendStopped = new ManualResetEventSlim(true);
        private readonly ManualResetEventSlim sendPauseGate = new ManualResetEventSlim(true);
        public BackgroundWorker Worker = new BackgroundWorker();

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
            int socketSnapshot = SystemSocket
                ? Socket_Cache.System.SystemSocket
                : 0;
            return this.StartSendCore(
                SendName,
                SystemSocket,
                socketSnapshot,
                LoopCNT,
                LoopINT,
                SendCollection,
                false);
        }

        public bool StartSend(string SendName, int ResolvedSystemSocket, int LoopCNT, int LoopINT, BindingList<Socket_PacketInfo> SendCollection)
        {
            return this.StartSendCore(
                SendName,
                true,
                ResolvedSystemSocket,
                LoopCNT,
                LoopINT,
                SendCollection,
                false);
        }

        public bool StartSendWithPacketSockets(
            string SendName,
            int LoopCNT,
            int LoopINT,
            BindingList<Socket_PacketInfo> SendCollection)
        {
            return this.StartSendCore(
                SendName,
                false,
                0,
                LoopCNT,
                LoopINT,
                SendCollection,
                true);
        }

        private bool StartSendCore(
            string SendName,
            bool SystemSocket,
            int ResolvedSystemSocket,
            int LoopCNT,
            int LoopINT,
            BindingList<Socket_PacketInfo> SendCollection,
            bool usePacketSockets)
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
                this.LoopCNT = LoopCNT;
                this.LoopINT = LoopINT;
                this.SendCollection = CreateSendSnapshot(SendCollection);

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
                            if (this.SystemSocket && !this.usePacketSockets && spi != null)
                            {
                                Socket = this.resolvedSystemSocket;
                            }

                            if (Socket <= 0 || spi == null || spi.PacketBuffer == null || spi.PacketBuffer.Length == 0)
                            {
                                this.Send_Failure++;
                                this.Total_Send++;
                            }
                            else
                            {
                                bool bOK = Socket_Operation.SendPacket(Socket, spi.PacketType, spi.PacketFrom, spi.PacketTo, spi.PacketBuffer);

                                if (bOK)
                                {
                                    this.Send_Success++;
                                }
                                else
                                {
                                    this.Send_Failure++;
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
