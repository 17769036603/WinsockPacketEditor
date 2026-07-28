using Be.Windows.Forms;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    public partial class Socket_SendForm : Form
    {
        private Socket_PacketInfo SPI;
        private long Send_CNT = 0;
        private long Send_Success = 0;
        private long Send_Fail = 0;
        private CancellationTokenSource cts;
        private RadioButton rbSendType_ByteSweep;
        private Label lByteSweepHint;
        private ToolStripStatusLabel tlByteSweepProgress;
        private Button bSaveByteSweepPreset;
        private long byteSweepOriginalSelectionStart;
        private long byteSweepOriginalSelectionLength;
        private bool byteSweepWasRunning;
        private Socket_ByteAnnotationController byteAnnotationController;
        private System.Collections.Generic.List<Socket_ByteAnnotationInfo> workingByteAnnotations;

        private sealed class SendWorkItem
        {
            public int Socket;
            public int Interval;
            public int Times;
            public string IPFrom;
            public string IPTo;
            public byte[] Buffer;
            public bool Continuously;
            public bool ByteSweep;
            public bool ProgressionEnabled;
            public int ProgressionPosition;
            public int ProgressionStep;
            public bool ProgressionCarryEnabled;
            public int ProgressionCarryCount;
            public int SweepStart;
            public int SweepLength;
        }

        #region//窗体加载

        public Socket_SendForm(Socket_PacketInfo spi)
        {
            try
            {
                MultiLanguage.SetDefaultLanguage(MultiLanguage.DefaultLanguage);                
                InitializeComponent();
                this.MinimumSize = new System.Drawing.Size(950, 520);
                this.hbPacketData.AccessibleName = UiText("Main_PacketData");
                this.byteAnnotationController = new Socket_ByteAnnotationController(
                    this.hbPacketData, this.tlpPacketData, 2, 0, 2,
                    delegate { return !this.bgwSendPacket.IsBusy; });
                this.InitializeByteSweepControls();

                if (spi != null)
                { 
                    this.SPI = spi;
                }  
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//逐字节递进界面

        private void InitializeByteSweepControls()
        {
            this.rbSendType_ByteSweep = new RadioButton
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Name = "rbSendType_ByteSweep",
                Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_232),
                UseVisualStyleBackColor = true
            };
            this.rbSendType_ByteSweep.CheckedChanged += this.rbSendType_ByteSweep_CheckedChanged;

            this.lByteSweepHint = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Name = "lByteSweepHint",
                Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_233),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            this.tlpSendType.Controls.Add(this.rbSendType_ByteSweep, 0, 2);
            this.tlpSendType.Controls.Add(this.lByteSweepHint, 1, 2);
            this.tlpSendType.SetColumnSpan(this.lByteSweepHint, 2);

            this.tlByteSweepProgress = new ToolStripStatusLabel
            {
                ForeColor = System.Drawing.Color.RoyalBlue,
                Name = "tlByteSweepProgress",
                Text = string.Empty,
                Visible = false
            };
            this.ssSocketSend.Items.Add(this.tlByteSweepProgress);

            this.bSaveByteSweepPreset = new Button
            {
                Dock = DockStyle.Fill,
                Name = "bSaveByteSweepPreset",
                Text = UiText("UI_SavePreset"),
                UseVisualStyleBackColor = true,
                Visible = false
            };
            this.bSaveByteSweepPreset.Click += this.bSaveByteSweepPreset_Click;
            this.tlpButtons.Controls.Add(this.bSaveByteSweepPreset, 4, 0);
        }

        private void rbSendType_ByteSweep_CheckedChanged(object sender, EventArgs e)
        {
            this.SendTypeChanged();
        }

        #endregion

        #region//初始化

        private void Socket_SendForm_Load(object sender, EventArgs e)
        {
            this.bSend.Enabled = true;
            this.bSendStop.Enabled = false;

            this.InitHexBox();
            this.InitSendInfo();            
            this.SendTypeChanged();
            this.ProgressionPositionChange();
        }

        private void Socket_SendForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.StopSend();
        }

        private void InitSendInfo()
        {
            try
            {  
                this.txtPacketTime.Text = this.SPI.PacketTime.ToString("HH: mm: ss: fffffff");              
                this.txtPacketType.Text = Socket_Cache.SocketPacket.GetName_ByPacketType(this.SPI.PacketType);

                this.txtIPFrom.Text = this.SPI.PacketFrom;
                this.txtIPTo.Text = this.SPI.PacketTo;
                this.pbSocketType.Image = Socket_Cache.SocketPacket.GetImg_ByPacketType(this.SPI.PacketType);
                
                this.nudSendSocket_Len.Value = hbPacketData.ByteProvider.Length;
                this.nudSendSocket_Socket.Value = this.SPI.PacketSocket;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void InitHexBox()
        {
            try
            {
                this.workingByteAnnotations = Socket_ByteAnnotationEngine.Clone(this.SPI.ByteAnnotations);
                Socket_AnnotatedByteProvider dbp = new Socket_AnnotatedByteProvider(
                    this.SPI.PacketBuffer, this.workingByteAnnotations);
                dbp.Changed += new EventHandler(ByteProvider_Changed);
                dbp.LengthChanged += new EventHandler(ByteProvider_LengthChanged);
                hbPacketData.ByteProvider = dbp;
                this.byteAnnotationController.Bind(this.workingByteAnnotations);

                DefaultByteCharConverter defConverter = new DefaultByteCharConverter();
                EbcdicByteCharProvider ebcdicConverter = new EbcdicByteCharProvider();
                tscbEncoding.Items.Add(defConverter);
                tscbEncoding.Items.Add(ebcdicConverter);
                tscbEncoding.SelectedIndex = 0;
                tscbPerLine.SelectedIndex = 1;

                this.HexBox_LinePositionChanged();
                this.HexBox_UpdatePacketLen();
                this.HexBox_ManageAbility();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }        

        #endregion

        #region//递进设置

        private void cbProgressionPosition_CheckedChanged(object sender, EventArgs e)
        {
            this.ProgressionPositionChange();
        }

        private void ProgressionPositionChange()
        {
            if (this.cbProgressionPosition.Checked)
            {
                this.cbProgressionCarry.Enabled = true;
                this.nudProgressionPosition.Enabled = true;
                this.nudProgressionStep.Enabled = true;

                this.ProgressionCarryChange();
            }
            else
            {
                this.cbProgressionCarry.Enabled = false;
                this.nudProgressionPosition.Enabled = false;
                this.nudProgressionStep.Enabled = false;
                this.nudProgressionCarry.Enabled = false;
            }          
        }

        private void cbProgressionCarry_CheckedChanged(object sender, EventArgs e)
        {
            this.ProgressionCarryChange();
        }

        private void ProgressionCarryChange()
        {
            this.nudProgressionCarry.Enabled = this.cbProgressionCarry.Checked;
        }

        #endregion        

        #region//发送类型参数

        private void rbSendType_Continuously_CheckedChanged(object sender, EventArgs e)
        {
            this.SendTypeChanged();
        }

        private void rbSendType_Times_CheckedChanged(object sender, EventArgs e)
        {
            this.SendTypeChanged();
        }

        private void SendTypeChanged()
        {
            bool byteSweep = this.rbSendType_ByteSweep != null && this.rbSendType_ByteSweep.Checked;
            this.nudSendType_Times.Enabled = !this.rbSendType_Continuously.Checked && !byteSweep;
            this.gbSendStep.Enabled = !byteSweep;

            if (this.tlByteSweepProgress != null)
            {
                this.tlByteSweepProgress.Visible = byteSweep;
                if (byteSweep && !this.byteSweepWasRunning)
                {
                    this.tlByteSweepProgress.Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_233);
                }
            }

            if (this.bSaveByteSweepPreset != null)
            {
                this.bSaveByteSweepPreset.Visible = byteSweep;
                this.bSaveByteSweepPreset.Enabled = byteSweep &&
                    !this.bgwSendPacket.IsBusy &&
                    this.hbPacketData.ByteProvider != null &&
                    this.hbPacketData.SelectionLength > 0;
            }
        }

        #endregion        

        #region//检查发送数据

        private bool CheckSendPacket()
        {
            try
            {
                int iSocket = (int)this.nudSendSocket_Socket.Value;

                if (iSocket == 0)
                {
                    Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_45));
                    return false;
                }

                if (hbPacketData.ByteProvider.Length == 0)
                {
                    Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_46));
                    return false;
                }

                bool byteSweep = this.rbSendType_ByteSweep != null && this.rbSendType_ByteSweep.Checked;

                if (byteSweep)
                {
                    long selectionStart = this.hbPacketData.SelectionStart;
                    long selectionLength = this.hbPacketData.SelectionLength;

                    if (selectionStart < 0 ||
                        selectionLength <= 0 ||
                        selectionStart >= this.hbPacketData.ByteProvider.Length ||
                        selectionLength > this.hbPacketData.ByteProvider.Length - selectionStart)
                    {
                        Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_234));
                        return false;
                    }
                }

                if (!byteSweep && this.cbProgressionPosition.Checked)
                {
                    int iProgressionPosition = (int)this.nudProgressionPosition.Value;

                    if (iProgressionPosition >= hbPacketData.ByteProvider.Length)
                    {
                        Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_47));
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
                return false;
            }

            return true;
        }

        #endregion

        #region//发送封包（异步）

        private void bSend_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.CheckSendPacket())
                {
                    if (!bgwSendPacket.IsBusy)
                    {
                        SendWorkItem workItem = this.CreateSendWorkItem();

                        this.bSend.Enabled = false;
                        this.bSendStop.Enabled = true;
                        if (this.bSaveByteSweepPreset != null)
                        {
                            this.bSaveByteSweepPreset.Enabled = false;
                        }

                        this.gbSendSocket.Enabled = false;
                        this.gbSendStep.Enabled = false;
                        this.gbSendType.Enabled = false;

                        this.Send_CNT = 0;
                        this.Send_Success = 0;
                        this.Send_Fail = 0;
                        this.tlSendTimes_Value.Text = this.Send_CNT.ToString();
                        this.tlSend_Success_Value.Text = this.Send_Success.ToString();
                        this.tlSend_Fail_Value.Text = this.Send_Fail.ToString();

                        this.byteSweepWasRunning = workItem.ByteSweep;
                        if (workItem.ByteSweep)
                        {
                            this.byteSweepOriginalSelectionStart = this.hbPacketData.SelectionStart;
                            this.byteSweepOriginalSelectionLength = this.hbPacketData.SelectionLength;
                        }

                        this.cts = new CancellationTokenSource();
                        this.bgwSendPacket.RunWorkerAsync(workItem);
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private SendWorkItem CreateSendWorkItem()
        {
            IByteProvider dbp = this.hbPacketData.ByteProvider;
            bool byteSweep = this.rbSendType_ByteSweep != null && this.rbSendType_ByteSweep.Checked;

            return new SendWorkItem
            {
                Socket = (int)this.nudSendSocket_Socket.Value,
                Interval = (int)this.nudSendType_Interval.Value,
                Times = (int)this.nudSendType_Times.Value,
                IPFrom = this.txtIPFrom.Text.Trim(),
                IPTo = this.txtIPTo.Text.Trim(),
                Buffer = Socket_ByteAnnotationEngine.GetBytes(dbp),
                Continuously = this.rbSendType_Continuously.Checked,
                ByteSweep = byteSweep,
                ProgressionEnabled = !byteSweep && this.cbProgressionPosition.Checked,
                ProgressionPosition = (int)this.nudProgressionPosition.Value,
                ProgressionStep = (int)this.nudProgressionStep.Value,
                ProgressionCarryEnabled = this.cbProgressionCarry.Checked,
                ProgressionCarryCount = (int)this.nudProgressionCarry.Value,
                SweepStart = byteSweep ? (int)this.hbPacketData.SelectionStart : 0,
                SweepLength = byteSweep ? (int)this.hbPacketData.SelectionLength : 0
            };
        }

        private void bgwSendPacket_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            try
            {
                SendWorkItem workItem = e.Argument as SendWorkItem;
                if (workItem == null)
                {
                    return;
                }

                if (workItem.ByteSweep)
                {
                    if (!this.ExecuteByteSweep(workItem))
                    {
                        e.Cancel = true;
                    }
                }
                else if (workItem.Continuously)
                {
                    int iSendCount = 0;
                    while (!this.IsSendCancellationRequested())
                    {
                        this.DoSendPacket(workItem, iSendCount);
                        iSendCount++;

                        if (!this.WaitForNextSend(workItem.Interval))
                        {
                            e.Cancel = true;
                            return;
                        }
                    }

                    e.Cancel = true;
                }
                else
                {
                    for (int i = 0; i < workItem.Times; i++)
                    {
                        if (this.IsSendCancellationRequested())
                        {
                            e.Cancel = true;
                            return;
                        }

                        this.DoSendPacket(workItem, i);

                        if (i < workItem.Times - 1 && !this.WaitForNextSend(workItem.Interval))
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void bgwSendPacket_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            try
            {
                this.tlSendTimes_Value.Text = this.Send_CNT.ToString();
                this.tlSend_Success_Value.Text = this.Send_Success.ToString();
                this.tlSend_Fail_Value.Text = this.Send_Fail.ToString();

                this.bSend.Enabled = true;
                this.bSendStop.Enabled = false;
                this.gbSendSocket.Enabled = true;                
                this.gbSendStep.Enabled = true;
                this.gbSendType.Enabled = true;
                this.SendTypeChanged();

                if (this.byteSweepWasRunning)
                {
                    this.RestoreByteSweepSelection();

                    if (e.Cancelled)
                    {
                        this.tlByteSweepProgress.Text = MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_237);
                    }
                    else
                    {
                        long byteCount = this.byteSweepOriginalSelectionLength;
                        this.tlByteSweepProgress.Text = string.Format(
                            MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_236),
                            byteCount,
                            byteCount * 255L);
                    }
                }

                this.byteSweepWasRunning = false;
                if (this.cts != null)
                {
                    this.cts.Dispose();
                    this.cts = null;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void DoSendPacket(SendWorkItem workItem, int SendCount)
        {
            try
            {
                if (workItem.ProgressionEnabled)
                {
                    int iCarryCount = 0;
                    int iIndex = workItem.ProgressionPosition;
                    int iStep = workItem.ProgressionStep;

                    byte bValue = workItem.Buffer[iIndex];
                    bValue = Socket_Operation.GetStepByte(bValue, iStep, out iCarryCount);
                    workItem.Buffer[iIndex] = bValue;

                    if (workItem.ProgressionCarryEnabled && iCarryCount > 0)
                    {
                        for (int i = 0; i < workItem.ProgressionCarryCount; i++)
                        {
                            int iIndexPre = iIndex - (i + 1);

                            if (iIndexPre > -1)
                            {
                                byte bValuePrev = workItem.Buffer[iIndexPre];
                                bValuePrev = Socket_Operation.GetStepByte(bValuePrev, iCarryCount, out iCarryCount);
                                workItem.Buffer[iIndexPre] = bValuePrev;

                                if (iCarryCount == 0)
                                {
                                    break;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }

                this.SendPacketBuffer(workItem, workItem.Buffer);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void SendPacketBuffer(SendWorkItem workItem, byte[] buffer)
        {
            try
            {
                bool bSendOK = Socket_Operation.SendPacket(
                    workItem.Socket,
                    this.SPI.PacketType,
                    workItem.IPFrom,
                    workItem.IPTo,
                    buffer);

                if (bSendOK)
                {
                    this.Send_Success++;
                }
                else
                {
                    this.Send_Fail++;
                }

                this.Send_CNT++;
            }
            catch (Exception ex)
            {
                this.Send_Fail++;
                this.Send_CNT++;
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private bool ExecuteByteSweep(SendWorkItem workItem)
        {
            CancellationToken token = this.cts == null ? CancellationToken.None : this.cts.Token;
            Socket_ByteSweepResult result = Socket_ByteSweepEngine.Execute(
                workItem.Buffer,
                workItem.SweepStart,
                workItem.SweepLength,
                workItem.Interval,
                buffer => Socket_Operation.SendPacket(
                    workItem.Socket,
                    this.SPI.PacketType,
                    workItem.IPFrom,
                    workItem.IPTo,
                    buffer),
                token,
                progress =>
                {
                    this.Send_CNT = progress.TotalSend;
                    this.Send_Success = progress.Success;
                    this.Send_Fail = progress.Failure;
                    this.PostByteSweepProgress(
                        progress.Position,
                        progress.OriginalValue,
                        progress.CurrentValue,
                        progress.ByteNumber,
                        progress.ByteCount,
                        progress.ValueNumber,
                        progress.ValueNumber == 0);
                });
            this.Send_CNT = result.TotalSend;
            this.Send_Success = result.Success;
            this.Send_Fail = result.Failure;
            return !result.Cancelled;
        }

        private bool IsSendCancellationRequested()
        {
            return this.bgwSendPacket.CancellationPending ||
                (this.cts != null && this.cts.IsCancellationRequested);
        }

        private bool WaitForNextSend(int interval)
        {
            if (this.IsSendCancellationRequested())
            {
                return false;
            }

            if (interval <= 0)
            {
                return true;
            }

            CancellationTokenSource currentCts = this.cts;
            return currentCts == null || !currentCts.Token.WaitHandle.WaitOne(interval);
        }

        private void PostByteSweepProgress(
            int position,
            byte originalValue,
            byte currentValue,
            int byteNumber,
            int byteCount,
            int valueNumber,
            bool moveCursor)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                this.BeginInvoke((Action)(() =>
                {
                    if (this.IsDisposed)
                    {
                        return;
                    }

                    if (moveCursor && this.hbPacketData.ByteProvider != null &&
                        position >= 0 && position < this.hbPacketData.ByteProvider.Length)
                    {
                        this.hbPacketData.Select(position, 1);
                        this.hbPacketData.ScrollByteIntoView(position);
                    }

                    this.tlByteSweepProgress.Text = string.Format(
                        MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_235),
                        position,
                        originalValue,
                        currentValue,
                        byteNumber,
                        byteCount,
                        valueNumber);
                    this.tlSendTimes_Value.Text = this.Send_CNT.ToString();
                    this.tlSend_Success_Value.Text = this.Send_Success.ToString();
                    this.tlSend_Fail_Value.Text = this.Send_Fail.ToString();
                }));
            }
            catch (InvalidOperationException)
            {
                // 窗口关闭过程中句柄可能已销毁，无需再更新界面。
            }
        }

        private void RestoreByteSweepSelection()
        {
            if (this.hbPacketData.ByteProvider == null || this.hbPacketData.ByteProvider.Length == 0)
            {
                return;
            }

            long start = Math.Min(
                Math.Max(this.byteSweepOriginalSelectionStart, 0),
                this.hbPacketData.ByteProvider.Length - 1);
            long length = Math.Min(
                Math.Max(this.byteSweepOriginalSelectionLength, 0),
                this.hbPacketData.ByteProvider.Length - start);

            this.hbPacketData.Select(start, length);
            this.hbPacketData.ScrollByteIntoView(start);
        }

        #endregion

        #region//停止按钮

        private void bSendStop_Click(object sender, EventArgs e)
        {
            this.StopSend();
        }

        private void StopSend()
        {
            try
            {
                if (this.bgwSendPacket.IsBusy)
                {
                    if (this.cts != null)
                    {
                        this.cts.Cancel();
                    }
                    
                    this.bgwSendPacket.CancelAsync();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//保存递进预设

        private void bSaveByteSweepPreset_Click(object sender, EventArgs e)
        {
            try
            {
                IByteProvider provider = this.hbPacketData.ByteProvider;
                if (provider == null || provider.Length == 0)
                {
                    Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_46));
                    return;
                }

                long selectionStart = this.hbPacketData.SelectionStart;
                long selectionLength = this.hbPacketData.SelectionLength;
                if (selectionStart < 0 ||
                    selectionLength <= 0 ||
                    selectionStart >= provider.Length ||
                    selectionLength > provider.Length - selectionStart)
                {
                    Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_234));
                    return;
                }

                Socket_ByteSweepPresetInfo preset = new Socket_ByteSweepPresetInfo
                {
                    BID = Guid.NewGuid(),
                    BName = "递进预设" + (Socket_Cache.ByteSweepList.lstPresets.Count + 1),
                    BFolder = Socket_Cache.ByteSweepList.lstFolders.Count > 0
                        ? Socket_Cache.ByteSweepList.lstFolders[0]
                        : string.Empty,
                    BStart = (int)selectionStart,
                    BLength = (int)selectionLength,
                    BLoopCount = 1,
                    BInterval = (int)this.nudSendType_Interval.Value,
                    BNextInterval = 0,
                    PacketType = this.SPI.PacketType,
                    PacketFrom = this.txtIPFrom.Text.Trim(),
                    PacketTo = this.txtIPTo.Text.Trim(),
                    Buffer = Socket_ByteAnnotationEngine.GetBytes(provider),
                    ByteAnnotations = Socket_ByteAnnotationEngine.Clone(this.workingByteAnnotations)
                };

                using (Socket_ByteSweepPresetForm dialog = new Socket_ByteSweepPresetForm(preset, true))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        Socket_Cache.ByteSweepList.AddPreset(dialog.Result);
                        this.tlByteSweepProgress.Text = string.Format(
                            System.Globalization.CultureInfo.CurrentCulture,
                            UiText("UI_PresetSaved"),
                            dialog.Result.BFolder,
                            dialog.Result.BName);
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        private static string UiText(string key)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? key;
        }

        #region//保存按钮

        private void bSave_Click(object sender, EventArgs e)
        {
            try
            {
                if (hbPacketData.ByteProvider != null)
                {
                    IByteProvider dbp = hbPacketData.ByteProvider;

                    if (dbp != null)
                    {
                        byte[] bNewBuff = Socket_ByteAnnotationEngine.GetBytes(dbp);
                        int iNewLen = bNewBuff.Length;
                        Span<byte> bufferSpan = bNewBuff.AsSpan();
                        string sNewPacketData_Hex = Socket_Operation.GetPacketData_Hex(bufferSpan, Socket_Cache.SocketPacket.PacketData_MaxLen);

                        this.SPI.PacketSocket = (int)this.nudSendSocket_Socket.Value;
                        this.SPI.PacketBuffer = bNewBuff;
                        this.SPI.PacketData = sNewPacketData_Hex;
                        this.SPI.PacketLen = iNewLen;
                        this.SPI.ByteAnnotations = Socket_ByteAnnotationEngine.Clone(this.workingByteAnnotations);

                        dbp.ApplyChanges();
                    }                    
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
            finally
            {
                HexBox_ManageAbility();
            }
        }

        #endregion

        #region//关闭按钮

        private void bClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #endregion

        #region//右键菜单

        private void cmsHexBox_Opening(object sender, CancelEventArgs e)
        {
            Socket_Operation.InitSendListComboBox(this.tscbSendList);
        }

        private void tscbSendList_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (this.tscbSendList.SelectedItem != null)
                {
                    Socket_Cache.SendList.SendListItem item = (Socket_Cache.SendList.SendListItem)this.tscbSendList.SelectedItem;
                    Guid SID = item.SID;
                    BindingList<Socket_PacketInfo> SCollection = Socket_Cache.Send.GetSendCollection_ByGuid(SID);

                    if (SCollection != null)
                    {
                        int iSocket = (int)this.nudSendSocket_Socket.Value;
                        string sIPFrom = this.txtIPFrom.Text.Trim();
                        string sIPTo = this.txtIPTo.Text.Trim();

                        byte[] bBuffer = null;

                        if (this.hbPacketData.CanCopy())
                        {
                            this.hbPacketData.CopyHex();
                            bBuffer = Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, Clipboard.GetText());                            
                        }
                        else
                        {
                            bBuffer = Socket_ByteAnnotationEngine.GetBytes(hbPacketData.ByteProvider);
                        }                        

                        Socket_Cache.Send.AddSendCollection(SCollection, iSocket, this.SPI.PacketType, sIPFrom, sIPTo, bBuffer,
                            Socket_ByteAnnotationEngine.ForSelection(
                                this.workingByteAnnotations,
                                this.hbPacketData.CanCopy() ? this.hbPacketData.SelectionStart : 0,
                                this.hbPacketData.CanCopy() ? this.hbPacketData.SelectionLength : 0));
                    }                                       

                    this.cmsHexBox.Close();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void cmsHexBox_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            try
            {
                string sItemText = e.ClickedItem.Name;
                this.cmsHexBox.Close();

                byte[] bBuffer = Socket_ByteAnnotationEngine.GetBytes(hbPacketData.ByteProvider);

                switch (sItemText)
                {  
                    case "cmsHexBox_FilterList":

                        if (this.hbPacketData.CanCopy())
                        {
                            this.hbPacketData.CopyHex();

                            byte[] bBufferCopy = Socket_Operation.StringToBytes(Socket_Cache.SocketPacket.EncodingFormat.Hex, Clipboard.GetText());
                            Socket_Cache.Filter.AddFilter_ByPacketInfo(this.SPI, bBufferCopy);
                        }
                        else
                        {
                            Socket_Cache.Filter.AddFilter_ByPacketInfo(this.SPI, bBuffer);
                        }

                        break;

                    case "cmsHexBox_SelectAll":

                        this.hbPacketData.SelectAll();

                        break;
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }            
        }

        #endregion

        #region//封包编辑器

        #region//可用性

        private void hbPacketData_CurrentLineChanged(object sender, EventArgs e)
        {
            this.HexBox_LinePositionChanged();
        }

        private void hbPacketData_CurrentPositionInLineChanged(object sender, EventArgs e)
        {
            this.HexBox_LinePositionChanged();
        }

        private void hbPacketData_Copied(object sender, EventArgs e)
        {
            this.HexBox_ManageAbilityForCopyAndPaste();
        }

        private void hbPacketData_CopiedHex(object sender, EventArgs e)
        {
            this.HexBox_ManageAbilityForCopyAndPaste();
        }

        private void hbPacketData_SelectionLengthChanged(object sender, EventArgs e)
        {
            this.HexBox_ManageAbilityForCopyAndPaste();
            this.SendTypeChanged();
        }

        private void hbPacketData_SelectionStartChanged(object sender, EventArgs e)
        {
            this.HexBox_ManageAbilityForCopyAndPaste();
            this.SendTypeChanged();
        }

        private void ByteProvider_Changed(object sender, EventArgs e)
        {
            this.HexBox_ManageAbility();
            this.byteAnnotationController.Refresh();
        }

        private void ByteProvider_LengthChanged(object sender, EventArgs e)
        {
            this.HexBox_UpdatePacketLen();
            this.byteAnnotationController.Refresh();
        }

        private void HexBox_UpdatePacketLen()
        {
            try
            {
                this.nudSendSocket_Len.Value = this.hbPacketData.ByteProvider.Length;                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void HexBox_LinePositionChanged()
        {
            try
            {
                int iSelectIndex = (int)hbPacketData.SelectionStart;
                this.nudProgressionPosition.Value = iSelectIndex;                  

                string sPacketDataPosition = string.Format(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_24), hbPacketData.CurrentLine, hbPacketData.CurrentPositionInLine, iSelectIndex);

                string sBits_Value = string.Empty;
                string sChar_Value = string.Empty;
                string sByte_Value = string.Empty;
                string sShort_Value = string.Empty;
                string sUShort_Value = string.Empty;
                string sInt32_Value = string.Empty;
                string sUInt32_Value = string.Empty;
                string sInt64_Value = string.Empty;
                string sUInt64_Value = string.Empty;
                string sFloat_Value = string.Empty;
                string sDouble_Value = string.Empty;

                if (hbPacketData.ByteProvider != null && hbPacketData.ByteProvider.Length > hbPacketData.SelectionStart)
                {
                    byte bSelected = hbPacketData.ByteProvider.ReadByte(hbPacketData.SelectionStart);

                    Socket_BitInfo bitInfo = new Socket_BitInfo(bSelected, hbPacketData.SelectionStart);

                    if (bitInfo != null)
                    {
                        long start = hbPacketData.SelectionStart;
                        long selected = hbPacketData.SelectionLength;

                        if (selected == 0 || selected > 8)
                        {
                            selected = 8;
                        }

                        long last = hbPacketData.ByteProvider.Length;
                        long end = Math.Min(start + selected, last);

                        byte[] buffer64 = new byte[8];
                        int iBuffIndex = 0;

                        for (long i = start; i < end; i++)
                        {
                            buffer64[iBuffIndex] = hbPacketData.ByteProvider.ReadByte(i);
                            iBuffIndex++;
                        }

                        sBits_Value = bitInfo.ToString();
                        sChar_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Char, buffer64);
                        sByte_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Byte, buffer64);
                        sShort_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Short, buffer64);
                        sUShort_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UShort, buffer64);
                        sInt32_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Int32, buffer64);
                        sUInt32_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UInt32, buffer64);
                        sInt64_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Int64, buffer64);
                        sUInt64_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.UInt64, buffer64);
                        sFloat_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Float, buffer64);
                        sDouble_Value = Socket_Operation.BytesToString(Socket_Cache.SocketPacket.EncodingFormat.Double, buffer64);
                    }
                }

                this.lHexBox_Position.Text = sPacketDataPosition;

                this.lBits_Value.Text = sBits_Value;
                this.lChar_Value.Text = sChar_Value;
                this.lByte_Value.Text = sByte_Value;
                this.lShort_Value.Text = sShort_Value;
                this.lUShort_Value.Text = sUShort_Value;
                this.lInt32_Value.Text = sInt32_Value;
                this.lUInt32_Value.Text = sUInt32_Value;
                this.lInt64_Value.Text = sInt64_Value;
                this.lUInt64_Value.Text = sUInt64_Value;
                this.lFloat_Value.Text = sFloat_Value;
                this.lDouble_Value.Text = sDouble_Value;                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }            
        }

        private void HexBox_ManageAbility()
        {
            try
            {
                if (hbPacketData.ByteProvider == null)
                {
                    this.bSave.Enabled = false;
                    tsPacketData_Find.Enabled = false;
                    tsPacketData_FindNext.Enabled = false;
                    tscbEncoding.Enabled = false;
                    tscbPerLine.Enabled = false;
                }
                else
                {
                    this.bSave.Enabled = true;
                    tsPacketData_Find.Enabled = true;
                    tsPacketData_FindNext.Enabled = true;
                    tscbEncoding.Enabled = true;
                    tscbPerLine.Enabled = true;
                }

                HexBox_ManageAbilityForCopyAndPaste();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void HexBox_ManageAbilityForCopyAndPaste()
        {
            try
            {
                tsPacketData_Copy.Enabled = hbPacketData.CanCopy();
                tsPacketData_Cut.Enabled = hbPacketData.CanCut();
                tsPacketData_Paste.Enabled = hbPacketData.CanPaste();
                tsPacketData_Paste_PasteHex.Enabled = hbPacketData.CanPasteHex();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        

        #region//剪切

        private void tsPacketData_Cut_Click(object sender, EventArgs e)
        {
            this.hbPacketData.Cut();
        }

        #endregion

        #region//复制

        private void tsPacketData_Copy_ButtonClick(object sender, EventArgs e)
        {
            this.hbPacketData.Copy();
        }

        private void tsPacketData_Copy_Copy_Click(object sender, EventArgs e)
        {
            this.hbPacketData.Copy();
        }

        private void tsPacketData_Copy_CopyHex_Click(object sender, EventArgs e)
        {
            this.hbPacketData.CopyHex();
        }

        #endregion

        #region//粘贴

        private void tsPacketData_Paste_ButtonClick(object sender, EventArgs e)
        {
            this.hbPacketData.Paste();
        }

        private void tsPacketData_Paste_Paste_Click(object sender, EventArgs e)
        {
            this.hbPacketData.Paste();
        }

        private void tsPacketData_Paste_PasteHex_Click(object sender, EventArgs e)
        {
            this.hbPacketData.PasteHex();
        }

        #endregion

        #region//查找

        private void tsPacketData_Find_Click(object sender, EventArgs e)
        {
            try
            {
                this.ShowFindForm();

                if (Socket_Cache.SocketList.DoSearch)
                {
                    this.HexBox_FindNext();
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void tsPacketData_FindNext_Click(object sender, EventArgs e)
        {
            this.HexBox_FindNext();
        }

        private void ShowFindForm()
        {
            try
            {
                Socket_FindForm sffFindForm = new Socket_FindForm();
                sffFindForm.ShowDialog();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void HexBox_FindNext()
        {
            try
            {
                if (Socket_Cache.SocketList.FindOptions.IsValid)
                {
                    long res = hbPacketData.Find(Socket_Cache.SocketList.FindOptions);

                    if (res == -1)
                    {
                        Socket_Operation.ShowMessageBox(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_23));
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//编码

        private void tscbEncoding_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                hbPacketData.ByteCharConverter = tscbEncoding.SelectedItem as IByteCharConverter;
                this.hbPacketData.Focus();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region//排列

        private void tscbPerLine_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                int iIndex = tscbPerLine.SelectedIndex;

                if (iIndex == 0)
                {
                    this.hbPacketData.UseFixedBytesPerLine = false;
                }
                else if (iIndex == 1)
                {
                    this.hbPacketData.UseFixedBytesPerLine = true;
                }

                this.hbPacketData.Focus();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #endregion        
    }
}
