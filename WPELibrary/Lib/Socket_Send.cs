using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Reflection;
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
        private List<Socket_PacketInfo> SendCollection;
        private readonly ManualResetEventSlim sendStopped = new ManualResetEventSlim(true);
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

        public void StartSend(string SendName, bool SystemSocket, int LoopCNT, int LoopINT, BindingList<Socket_PacketInfo> SendCollection)
        {
            int socketSnapshot = SystemSocket
                ? Socket_Cache.System.SystemSocket
                : 0;
            this.StartSend(
                SendName,
                SystemSocket,
                socketSnapshot,
                LoopCNT,
                LoopINT,
                SendCollection);
        }

        public void StartSend(string SendName, int ResolvedSystemSocket, int LoopCNT, int LoopINT, BindingList<Socket_PacketInfo> SendCollection)
        {
            this.StartSend(
                SendName,
                true,
                ResolvedSystemSocket,
                LoopCNT,
                LoopINT,
                SendCollection);
        }

        private void StartSend(
            string SendName,
            bool SystemSocket,
            int ResolvedSystemSocket,
            int LoopCNT,
            int LoopINT,
            BindingList<Socket_PacketInfo> SendCollection)
        {
            try
            {
                if (SendCollection.Count > 0)
                {
                    if (!this.Worker.IsBusy)
                    {
                        this.Total_Send = 0;
                        this.Send_Success = 0;
                        this.Send_Failure = 0;

                        this.SendName = SendName;
                        this.SystemSocket = SystemSocket;
                        this.resolvedSystemSocket = Math.Max(0, ResolvedSystemSocket);
                        this.LoopCNT = LoopCNT;
                        this.LoopINT = LoopINT;
                        this.SendCollection = CreateSendSnapshot(SendCollection);

                        this.cts = new CancellationTokenSource();
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
                    }
                }                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
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
            return this.sendStopped.Wait(Math.Max(0, millisecondsTimeout));
        }

        public bool WaitForCompletion(int millisecondsTimeout)
        {
            return this.sendStopped.Wait(Math.Max(0, millisecondsTimeout));
        }

        private static List<Socket_PacketInfo> CreateSendSnapshot(BindingList<Socket_PacketInfo> source)
        {
            List<Socket_PacketInfo> snapshot = new List<Socket_PacketInfo>(source.Count);
            foreach (Socket_PacketInfo packet in source)
            {
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
                    FilterAction = packet.FilterAction
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
                if (this.SystemSocket)
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
                        if (Worker.CancellationPending)
                        {
                            e.Cancel = true;
                            return;
                        }
                        else
                        {
                            int Socket = spi.PacketSocket;
                            if (this.SystemSocket)
                            {
                                Socket = this.resolvedSystemSocket;
                            }

                            if (Socket > 0)
                            {
                                bool bOK = Socket_Operation.SendPacket(Socket, spi.PacketType, string.Empty, spi.PacketTo, spi.PacketBuffer);

                                if (bOK)
                                {
                                    this.Send_Success++;
                                }
                                else
                                {
                                    this.Send_Failure++;
                                }

                                this.Total_Send++;

                                if (this.LoopINT > 0)
                                {
                                    Worker.ReportProgress(loopIndex);
                                    Socket_Operation.DoSleepAsync(this.LoopINT, this.cts.Token)
                                        .GetAwaiter()
                                        .GetResult();
                                }
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
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion
    }
}
