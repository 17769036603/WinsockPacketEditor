using EasyHook;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WPELibrary.Lib.NativeMethods;

namespace WPELibrary.Lib
{
    public sealed class HookStartResult
    {
        private HookStartResult(bool success, int createdHookCount, string failedHook, string errorMessage)
        {
            this.Success = success;
            this.CreatedHookCount = createdHookCount;
            this.FailedHook = failedHook ?? string.Empty;
            this.ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Success { get; private set; }
        public int CreatedHookCount { get; private set; }
        public string FailedHook { get; private set; }
        public string ErrorMessage { get; private set; }

        public static HookStartResult Succeeded(int createdHookCount)
        {
            return new HookStartResult(true, createdHookCount, string.Empty, string.Empty);
        }

        public static HookStartResult Failed(string failedHook, string errorMessage)
        {
            return new HookStartResult(false, 0, failedHook, errorMessage);
        }
    }

    public class WinSockHook : IEntryPoint
    {        
        private LocalHook lhWS1_Send, lhWS1_SendTo, lhWS1_Recv, lhWS1_RecvFrom;
        private LocalHook lhWS2_Send, lhWS2_SendTo, lhWS2_Recv, lhWS2_RecvFrom;
        private LocalHook lhWSA_Send, lhWSA_SendTo, lhWSA_Recv, lhWSA_RecvFrom;
        private LocalHook lhWSA_RecvEx;

        public bool IsRunning { get; private set; }

        #region//EasyHook

        public WinSockHook()
        {
            //
        }

        public WinSockHook(RemoteHooking.IContext InContext, string ChannelName)
        {
            //
        }

        public unsafe void Run(RemoteHooking.IContext InContext, string ChannelName)
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    User32.SetProcessDPIAware();
                }

                Application.EnableVisualStyles();                
                Application.SetCompatibleTextRenderingDefault(false);                
                Application.Run(new Socket_Form());                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }          
        }

        private static Socket_Cache.Filter.FilterAction ApplyFilterSafely(
            int socket,
            Span<byte> buffer,
            out byte[] newBuffer,
            Socket_Cache.SocketPacket.PacketType packetType,
            Socket_Cache.SocketPacket.SockAddr address)
        {
            try
            {
                Socket_Cache.Filter.FilterAction action = Socket_Cache.FilterList.DoFilterList(
                    socket,
                    buffer,
                    out newBuffer,
                    packetType,
                    address);

                if (action != Socket_Cache.Filter.FilterAction.Intercept && newBuffer == null)
                {
                    newBuffer = buffer.ToArray();
                }

                return action;
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(ApplyFilterSafely), ex.Message);
                newBuffer = buffer.ToArray();
                return Socket_Cache.Filter.FilterAction.NoModify_Display;
            }
        }

        #endregion

        #region//开始拦截

        public HookStartResult StartHook()
        {
            int createdHookCount = 0;
            string failedHook = string.Empty;
            try
            {
                if (Socket_Cache.SocketPacket.Support_WS1)
                {
                    #region//Winsock 1.1 Start Hook

                    if (Socket_Cache.SocketPacket.HookWS1_Send)
                    {
                        failedHook = "WS1.send";
                        lhWS1_Send = LocalHook.Create(LocalHook.GetProcAddress(WSock32.ModuleName, "send"), new WSock32.DSend(WSock32.SendHook), this);
                        lhWS1_Send.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWS1_SendTo)
                    {
                        failedHook = "WS1.sendto";
                        lhWS1_SendTo = LocalHook.Create(LocalHook.GetProcAddress(WSock32.ModuleName, "sendto"), new WSock32.DSendTo(WSock32.SendToHook), this);
                        lhWS1_SendTo.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWS1_Recv)
                    {
                        failedHook = "WS1.recv";
                        lhWS1_Recv = LocalHook.Create(LocalHook.GetProcAddress(WSock32.ModuleName, "recv"), new WSock32.Drecv(WSock32.RecvHook), this);
                        lhWS1_Recv.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWS1_RecvFrom)
                    {
                        failedHook = "WS1.recvfrom";
                        lhWS1_RecvFrom = LocalHook.Create(LocalHook.GetProcAddress(WSock32.ModuleName, "recvfrom"), new WSock32.DRecvFrom(WSock32.RecvFromHook), this);
                        lhWS1_RecvFrom.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    #endregion
                }

                if (Socket_Cache.SocketPacket.Support_WS2)
                {
                    #region//Winsock 2.0 Start Hook

                    if (Socket_Cache.SocketPacket.HookWS2_Send)
                    {
                        failedHook = "WS2.send";
                        lhWS2_Send = LocalHook.Create(LocalHook.GetProcAddress(WS2_32.ModuleName, "send"), new WS2_32.DSend(WS2_32.SendHook), this);
                        lhWS2_Send.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWS2_SendTo)
                    {
                        failedHook = "WS2.sendto";
                        lhWS2_SendTo = LocalHook.Create(LocalHook.GetProcAddress(WS2_32.ModuleName, "sendto"), new WS2_32.DSendTo(WS2_32.SendToHook), this);
                        lhWS2_SendTo.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWS2_Recv)
                    {
                        failedHook = "WS2.recv";
                        lhWS2_Recv = LocalHook.Create(LocalHook.GetProcAddress(WS2_32.ModuleName, "recv"), new WS2_32.Drecv(WS2_32.RecvHook), this);
                        lhWS2_Recv.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWS2_RecvFrom)
                    {
                        failedHook = "WS2.recvfrom";
                        lhWS2_RecvFrom = LocalHook.Create(LocalHook.GetProcAddress(WS2_32.ModuleName, "recvfrom"), new WS2_32.DRecvFrom(WS2_32.RecvFromHook), this);
                        lhWS2_RecvFrom.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWSA_Send)
                    {
                        failedHook = "WSA.send";
                        lhWSA_Send = LocalHook.Create(LocalHook.GetProcAddress(WS2_32.ModuleName, "WSASend"), new WS2_32.DWSASend(WSASend_Hook), this);
                        lhWSA_Send.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWSA_SendTo)
                    {
                        failedHook = "WSA.sendto";
                        lhWSA_SendTo = LocalHook.Create(LocalHook.GetProcAddress(WS2_32.ModuleName, "WSASendTo"), new WS2_32.DWSASendTo(WSASendTo_Hook), this);
                        lhWSA_SendTo.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWSA_Recv)
                    {
                        failedHook = "WSA.recv";
                        lhWSA_Recv = LocalHook.Create(LocalHook.GetProcAddress(WS2_32.ModuleName, "WSARecv"), new WS2_32.DWSARecv(WSARecv_Hook), this);
                        lhWSA_Recv.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    if (Socket_Cache.SocketPacket.HookWSA_RecvFrom)
                    {
                        failedHook = "WSA.recvfrom";
                        lhWSA_RecvFrom = LocalHook.Create(LocalHook.GetProcAddress(WS2_32.ModuleName, "WSARecvFrom"), new WS2_32.DWSARecvFrom(WSARecvFrom_Hook), this);
                        lhWSA_RecvFrom.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    #endregion
                }

                if (Socket_Cache.SocketPacket.Support_MsWS)
                {
                    #region//Winsock Microsoft Start Hook

                    if (Socket_Cache.SocketPacket.HookWSA_Recv)
                    {
                        failedHook = "Mswsock.WSARecvEx";
                        lhWSA_RecvEx = LocalHook.Create(LocalHook.GetProcAddress(Mswsock.ModuleName, "WSARecvEx"), new Mswsock.DWSARecvEx(Mswsock.WSARecvExHook), this);
                        lhWSA_RecvEx.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                        createdHookCount++;
                    }

                    #endregion
                }
                if (createdHookCount == 0)
                {
                    return HookStartResult.Failed("configuration", "No Winsock hooks are enabled.");
                }

                this.IsRunning = true;
                return HookStartResult.Succeeded(createdHookCount);
            }
            catch (Exception ex)
            {
                this.StopHook();
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
                return HookStartResult.Failed(failedHook, ex.Message);
            }
        }

        #endregion

        #region//停止拦截

        public void StopHook()
        {
            List<string> errors = new List<string>();
            this.DisposeHook(ref this.lhWS1_Send, errors, "WS1.send");
            this.DisposeHook(ref this.lhWS1_SendTo, errors, "WS1.sendto");
            this.DisposeHook(ref this.lhWS1_Recv, errors, "WS1.recv");
            this.DisposeHook(ref this.lhWS1_RecvFrom, errors, "WS1.recvfrom");
            this.DisposeHook(ref this.lhWS2_Send, errors, "WS2.send");
            this.DisposeHook(ref this.lhWS2_SendTo, errors, "WS2.sendto");
            this.DisposeHook(ref this.lhWS2_Recv, errors, "WS2.recv");
            this.DisposeHook(ref this.lhWS2_RecvFrom, errors, "WS2.recvfrom");
            this.DisposeHook(ref this.lhWSA_Send, errors, "WSA.send");
            this.DisposeHook(ref this.lhWSA_SendTo, errors, "WSA.sendto");
            this.DisposeHook(ref this.lhWSA_Recv, errors, "WSA.recv");
            this.DisposeHook(ref this.lhWSA_RecvFrom, errors, "WSA.recvfrom");
            this.DisposeHook(ref this.lhWSA_RecvEx, errors, "Mswsock.WSARecvEx");

            if (errors.Count > 0)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, string.Join("; ", errors.ToArray()));
            }

            this.IsRunning = false;
        }

        private void DisposeHook(ref LocalHook hook, List<string> errors, string name)
        {
            if (hook == null)
            {
                return;
            }

            try
            {
                hook.Dispose();
            }
            catch (Exception ex)
            {
                errors.Add(name + ": " + ex.Message);
            }
            finally
            {
                hook = null;
            }
        }

        #endregion

        #region//退出

        public void ExitHook()
        {
            try
            {
                this.StopHook();
                LocalHook.Release();
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion                

        #region//Send_Hook

        public static unsafe Int32 Send_Hook(
            [In] Socket_Cache.SocketPacket.PacketType ptType,
            [In] Int32 Socket,
            [In] IntPtr lpBuffer,
            [In] Int32 Length,
            [In] SocketFlags Flags)
        {
            Int32 res = 0;
            byte[] bRawBuffer = null;
            byte[] bNewBuffer = null;
            DateTime PacketTime = DateTime.Now;

            try
            {
                Span<byte> bBufferSpan = new Span<byte>((byte*)lpBuffer, Length);
                bRawBuffer = bBufferSpan.ToArray();

                Socket_Cache.Filter.FilterAction FilterAction = ApplyFilterSafely(Socket, bBufferSpan, out bNewBuffer, ptType, new Socket_Cache.SocketPacket.SockAddr());

                if (FilterAction == Socket_Cache.Filter.FilterAction.Intercept)
                {
                    res = Length;
                }
                else
                {
                    fixed (byte* pBuffer = bNewBuffer)
                    {
                        switch (ptType)
                        {
                            case Socket_Cache.SocketPacket.PacketType.WS1_Send:
                                res = WSock32.send(Socket, (IntPtr)pBuffer, bNewBuffer.Length, Flags);
                                break;

                            case Socket_Cache.SocketPacket.PacketType.WS2_Send:
                                res = WS2_32.send(Socket, (IntPtr)pBuffer, bNewBuffer.Length, Flags);
                                break;
                        }
                    }
                }

                _ = Socket_Operation.ProcessingHookResultAsync(Socket, bRawBuffer, bNewBuffer, res, ptType, FilterAction, new Socket_Cache.SocketPacket.SockAddr(), PacketTime);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return res;
        }

        #endregion

        #region//Recv_Hook

        public static unsafe Int32 Recv_Hook(
            [In] Socket_Cache.SocketPacket.PacketType ptType,
            [In] Int32 Socket,
            [Out] IntPtr lpBuffer,
            [In] Int32 Length,
            [In] SocketFlags Flags)
        {
            Int32 res = 0;

            try
            {
                switch (ptType)
                {
                    case Socket_Cache.SocketPacket.PacketType.WS1_Recv:
                        res = WSock32.recv(Socket, lpBuffer, Length, Flags);
                        break;

                    case Socket_Cache.SocketPacket.PacketType.WS2_Recv:
                        res = WS2_32.recv(Socket, lpBuffer, Length, Flags);
                        break;

                    case Socket_Cache.SocketPacket.PacketType.WSARecvEx:
                        res = Mswsock.WSARecvEx(Socket, lpBuffer, Length, Flags);
                        break;
                }

                if (res > 0)
                {
                    byte[] bRawBuffer = null;
                    byte[] bNewBuffer = null;
                    DateTime PacketTime = DateTime.Now;

                    Span<byte> bBufferSpan = new Span<byte>((byte*)lpBuffer, res);
                    bRawBuffer = bBufferSpan.ToArray();

                    Socket_Cache.Filter.FilterAction FilterAction = ApplyFilterSafely(Socket, bBufferSpan, out bNewBuffer, ptType, new Socket_Cache.SocketPacket.SockAddr());

                    if (FilterAction == Socket_Cache.Filter.FilterAction.Intercept)
                    {
                        res = 0;
                    }
                    else
                    {
                        res = Math.Min(bNewBuffer.Length, res);
                        new Span<byte>(bNewBuffer).CopyTo(new Span<byte>((byte*)lpBuffer, res));
                    }

                    _ = Socket_Operation.ProcessingHookResultAsync(Socket, bRawBuffer, bNewBuffer, res, ptType, FilterAction, new Socket_Cache.SocketPacket.SockAddr(), PacketTime);
                }                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return res;
        }       

        #endregion

        #region//SendTo_Hook

        public static unsafe Int32 SendTo_Hook(
            [In] Socket_Cache.SocketPacket.PacketType ptType,
            [In] Int32 Socket,
            [In] IntPtr lpBuffer,
            [In] Int32 Length,
            [In] SocketFlags Flags,
            [In] ref Socket_Cache.SocketPacket.SockAddr To,
            [In] Int32 ToLen)
        {
            Int32 res = 0;
            byte[] bRawBuffer = null;
            byte[] bNewBuffer = null;
            DateTime PacketTime = DateTime.Now;

            try
            {
                Span<byte> bBufferSpan = new Span<byte>((byte*)lpBuffer, Length);
                bRawBuffer = bBufferSpan.ToArray();

                Socket_Cache.Filter.FilterAction FilterAction = ApplyFilterSafely(Socket, bBufferSpan, out bNewBuffer, ptType, To);

                if (FilterAction == Socket_Cache.Filter.FilterAction.Intercept)
                {
                    res = Length;
                }
                else
                {
                    fixed (byte* pBuffer = bNewBuffer)
                    {
                        switch (ptType)
                        {
                            case Socket_Cache.SocketPacket.PacketType.WS1_SendTo:
                                res = WSock32.sendto(Socket, (IntPtr)pBuffer, bNewBuffer.Length, Flags, ref To, ToLen);
                                break;

                            case Socket_Cache.SocketPacket.PacketType.WS2_SendTo:
                                res = WS2_32.sendto(Socket, (IntPtr)pBuffer, bNewBuffer.Length, Flags, ref To, ToLen);
                                break;
                        }
                    }
                }

                _ = Socket_Operation.ProcessingHookResultAsync(Socket, bRawBuffer, bNewBuffer, res, ptType, FilterAction, To, PacketTime);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return res;
        }        

        #endregion

        #region//RecvFrom_Hook

        public static unsafe Int32 RecvFrom_Hook(
            [In] Socket_Cache.SocketPacket.PacketType ptType,
            [In] Int32 Socket,
            [Out] IntPtr lpBuffer,
            [In] Int32 Length,
            [In] SocketFlags Flags,
            [In, Out] ref Socket_Cache.SocketPacket.SockAddr From,
            [In, Out, Optional] IntPtr FromLen)
        {
            Int32 res = 0;

            try
            {
                switch (ptType)
                {
                    case Socket_Cache.SocketPacket.PacketType.WS1_RecvFrom:
                        res = WSock32.recvfrom(Socket, lpBuffer, Length, Flags, ref From, FromLen);
                        break;

                    case Socket_Cache.SocketPacket.PacketType.WS2_RecvFrom:
                        res = WS2_32.recvfrom(Socket, lpBuffer, Length, Flags, ref From, FromLen);
                        break;
                }

                if (res > 0)
                {
                    byte[] bNewBuffer = null;
                    byte[] bRawBuffer = null;
                    DateTime PacketTime = DateTime.Now;

                    Span<byte> bBufferSpan = new Span<byte>((byte*)lpBuffer, res);
                    bRawBuffer = bBufferSpan.ToArray();

                    Socket_Cache.Filter.FilterAction FilterAction = ApplyFilterSafely(Socket, bBufferSpan, out bNewBuffer, ptType, From);

                    if (FilterAction == Socket_Cache.Filter.FilterAction.Intercept)
                    {
                        res = 0;
                    }
                    else
                    {
                        res = Math.Min(bNewBuffer.Length, res);
                        new Span<byte>(bNewBuffer).CopyTo(new Span<byte>((byte*)lpBuffer, res));
                    }

                    _ = Socket_Operation.ProcessingHookResultAsync(Socket, bRawBuffer, bNewBuffer, res, ptType, FilterAction, From, PacketTime);
                }                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return res;
        }

        #endregion

        #region//WSASend_Hook

        public static unsafe SocketError WSASend_Hook(
            [In] Int32 socket,
            [In] IntPtr lpWSABuffer,
            [In] Int32 bufferCount,
            [Out] IntPtr lpNumberOfBytesSent,
            [In] SocketFlags flags,
            [In] IntPtr lpOverlapped,
            [In] IntPtr lpCompletionRoutine)
        {
            SocketError res = SocketError.SocketError;
            Socket_Cache.SocketPacket.PacketType packetType = Socket_Cache.SocketPacket.PacketType.WSASend;            

            try
            {
                DateTime packetTime = DateTime.Now;
                Socket_Cache.SocketPacket.WSABUF* pWSABuffers = (Socket_Cache.SocketPacket.WSABUF*)lpWSABuffer;

                if (bufferCount == 1)
                {
                    #region//单缓存区

                    int BytesSent = pWSABuffers[0].len;
                    if (BytesSent > 0)
                    {
                        byte[] bRawBuffer = null;
                        byte[] bNewBuffer = null;

                        Span<byte> bBufferSpan = new Span<byte>((byte*)pWSABuffers[0].buf, BytesSent);                        
                        bRawBuffer = bBufferSpan.ToArray();

                        Socket_Cache.Filter.FilterAction filterAction =
                        ApplyFilterSafely(
                            socket,
                            bBufferSpan,
                            out bNewBuffer,
                            packetType,
                            new Socket_Cache.SocketPacket.SockAddr());

                        if (filterAction == Socket_Cache.Filter.FilterAction.Intercept)
                        {
                            Marshal.WriteInt32(lpNumberOfBytesSent, BytesSent);
                            res = SocketError.Success;
                        }
                        else
                        {
                            BytesSent = Math.Min(bNewBuffer.Length, BytesSent);
                            bNewBuffer.AsSpan(0, BytesSent).CopyTo(bBufferSpan);

                            int WSABufferLen = pWSABuffers[0].len;
                            pWSABuffers[0].len = BytesSent;

                            res = WS2_32.WSASend(
                                socket,
                                lpWSABuffer,
                                bufferCount,
                                lpNumberOfBytesSent,
                                flags,
                                lpOverlapped,
                                lpCompletionRoutine);

                            pWSABuffers[0].len = WSABufferLen;
                        }

                        BytesSent = Marshal.ReadInt32(lpNumberOfBytesSent);

                        _ = Socket_Operation.ProcessingHookResultAsync(
                       socket,
                       bRawBuffer,
                       bNewBuffer,
                       BytesSent,
                       packetType,
                       filterAction,
                       new Socket_Cache.SocketPacket.SockAddr(),
                       packetTime);
                    }

                    #endregion
                }
                else
                {
                    #region//多缓存区

                    int totalBytes = 0;
                    for (int i = 0; i < bufferCount; i++)
                    {
                        totalBytes += pWSABuffers[i].len;
                    }

                    if (totalBytes > 0)
                    {
                        byte[] bRawBuffer = new byte[totalBytes];
                        byte[] bNewBuffer = null;

                        int offset = 0;
                        for (int i = 0; i < bufferCount; i++)
                        {
                            if (pWSABuffers[i].len > 0)
                            {
                                Span<byte> bufferSpan = new Span<byte>((byte*)pWSABuffers[i].buf, pWSABuffers[i].len);
                                bufferSpan.CopyTo(new Span<byte>(bRawBuffer, offset, pWSABuffers[i].len));
                                offset += pWSABuffers[i].len;
                            }
                        }

                        Socket_Cache.Filter.FilterAction filterAction =
                            ApplyFilterSafely(
                                socket,
                                bRawBuffer.AsSpan(),
                                out bNewBuffer,
                                packetType,
                                new Socket_Cache.SocketPacket.SockAddr());

                        if (filterAction == Socket_Cache.Filter.FilterAction.Intercept)
                        {
                            Marshal.WriteInt32(lpNumberOfBytesSent, totalBytes);
                            res = SocketError.Success;
                        }
                        else if (bNewBuffer != null && bNewBuffer.Length > 0)
                        {
                            int remainingBytes = Math.Min(bNewBuffer.Length, totalBytes);
                            int bufferIndex = 0;
                            int bytesCopied = 0;

                            int[] originalLengths = new int[bufferCount];
                            for (int i = 0; i < bufferCount; i++)
                            {
                                originalLengths[i] = pWSABuffers[i].len;
                            }

                            while (remainingBytes > 0 && bufferIndex < bufferCount)
                            {
                                int copyLength = Math.Min(pWSABuffers[bufferIndex].len, remainingBytes);
                                if (copyLength > 0)
                                {
                                    Span<byte> destSpan = new Span<byte>((byte*)pWSABuffers[bufferIndex].buf, pWSABuffers[bufferIndex].len);
                                    bNewBuffer.AsSpan(bytesCopied, copyLength).CopyTo(destSpan);
                                    pWSABuffers[bufferIndex].len = copyLength;
                                    bytesCopied += copyLength;
                                    remainingBytes -= copyLength;
                                }

                                bufferIndex++;
                            }

                            res = WS2_32.WSASend(
                                socket,
                                lpWSABuffer,
                                bufferCount,
                                lpNumberOfBytesSent,
                                flags,
                                lpOverlapped,
                                lpCompletionRoutine);

                            for (int i = 0; i < bufferCount; i++)
                            {
                                pWSABuffers[i].len = originalLengths[i];
                            }
                        }
                        else
                        {
                            res = WS2_32.WSASend(
                                socket,
                                lpWSABuffer,
                                bufferCount,
                                lpNumberOfBytesSent,
                                flags,
                                lpOverlapped,
                                lpCompletionRoutine);
                        }

                        int bytesSent = Marshal.ReadInt32(lpNumberOfBytesSent);

                        _ = Socket_Operation.ProcessingHookResultAsync(
                            socket,
                            bRawBuffer,
                            bNewBuffer,
                            bytesSent,
                            packetType,
                            filterAction,
                            new Socket_Cache.SocketPacket.SockAddr(),
                            packetTime);
                    }

                    #endregion
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return res;
        }

        #endregion

        #region//WSARecv_Hook

        public static unsafe SocketError WSARecv_Hook(
            [In] Int32 socket,
            [In, Out] IntPtr lpWSABuffer,
            [In] Int32 bufferCount,
            [Out] IntPtr lpNumberOfBytesRecvd,
            [In, Out] ref SocketFlags flags,
            [In] IntPtr lpOverlapped,
            [In] IntPtr lpCompletionRoutine)
        {
            Socket_Cache.SocketPacket.PacketType packetType = Socket_Cache.SocketPacket.PacketType.WSARecv;
            SocketError res = WS2_32.WSARecv(socket, lpWSABuffer, bufferCount, lpNumberOfBytesRecvd, ref flags, lpOverlapped, lpCompletionRoutine);

            try
            {
                if (res == SocketError.Success)
                {
                    int BytesRecvd = Marshal.ReadInt32(lpNumberOfBytesRecvd);
                    if (BytesRecvd > 0)
                    {
                        DateTime packetTime = DateTime.Now;
                        Socket_Cache.SocketPacket.WSABUF* pWSABuffers = (Socket_Cache.SocketPacket.WSABUF*)lpWSABuffer;

                        if (bufferCount == 1)
                        {
                            #region//单缓存区

                            byte[] bRawBuffer = null;
                            byte[] bNewBuffer = null;

                            Span<byte> bufferSpan = new Span<byte>((byte*)pWSABuffers[0].buf, BytesRecvd);
                            bRawBuffer = bufferSpan.ToArray();

                            Socket_Cache.Filter.FilterAction filterAction =
                                ApplyFilterSafely(
                                    socket,
                                    bufferSpan,
                                    out bNewBuffer,
                                    packetType,
                                    new Socket_Cache.SocketPacket.SockAddr());

                            int bytesToWrite = 0;
                            if (filterAction != Socket_Cache.Filter.FilterAction.Intercept)
                            {
                                bytesToWrite = Math.Min(bNewBuffer.Length, BytesRecvd);
                                bNewBuffer.AsSpan(0, bytesToWrite).CopyTo(bufferSpan);
                            }

                            Marshal.WriteInt32(lpNumberOfBytesRecvd, bytesToWrite);

                            _ = Socket_Operation.ProcessingHookResultAsync(
                                socket,
                                bRawBuffer,
                                bNewBuffer,
                                bytesToWrite,
                                packetType,
                                filterAction,
                                new Socket_Cache.SocketPacket.SockAddr(),
                                packetTime);

                            #endregion
                        }
                        else
                        {
                            #region//多缓存区

                            int remainingBytes = BytesRecvd;
                            int[] bufferBytes = new int[bufferCount];

                            for (int i = 0; i < bufferCount && remainingBytes > 0; i++)
                            {
                                int bufferSize = pWSABuffers[i].len;
                                bufferBytes[i] = Math.Min(bufferSize, remainingBytes);
                                remainingBytes -= bufferBytes[i];
                            }

                            byte[] bRawBuffer = new byte[BytesRecvd];
                            int offset = 0;
                            for (int i = 0; i < bufferCount; i++)
                            {
                                if (bufferBytes[i] > 0)
                                {
                                    Span<byte> bufferSpan = new Span<byte>((byte*)pWSABuffers[i].buf, bufferBytes[i]);
                                    bufferSpan.CopyTo(new Span<byte>(bRawBuffer, offset, bufferBytes[i]));
                                    offset += bufferBytes[i];
                                }
                            }

                            byte[] bNewBuffer = null;
                            Socket_Cache.Filter.FilterAction filterAction =
                                ApplyFilterSafely(
                                    socket,
                                    bRawBuffer.AsSpan(),
                                    out bNewBuffer,
                                    packetType,
                                    new Socket_Cache.SocketPacket.SockAddr());

                            int bytesToWrite = 0;
                            if (filterAction != Socket_Cache.Filter.FilterAction.Intercept && bNewBuffer != null)
                            {
                                remainingBytes = Math.Min(bNewBuffer.Length, BytesRecvd);
                                bytesToWrite = remainingBytes;

                                for (int i = 0; i < bufferCount && remainingBytes > 0; i++)
                                {
                                    int copyLength = Math.Min(pWSABuffers[i].len, remainingBytes);
                                    if (copyLength > 0)
                                    {
                                        Span<byte> destSpan = new Span<byte>((byte*)pWSABuffers[i].buf, copyLength);
                                        bNewBuffer.AsSpan(BytesRecvd - remainingBytes, copyLength).CopyTo(destSpan);
                                        remainingBytes -= copyLength;
                                    }
                                }
                            }

                            Marshal.WriteInt32(lpNumberOfBytesRecvd, bytesToWrite);

                            _ = Socket_Operation.ProcessingHookResultAsync(
                                socket,
                                bRawBuffer,
                                bNewBuffer,
                                bytesToWrite,
                                packetType,
                                filterAction,
                                new Socket_Cache.SocketPacket.SockAddr(),
                                packetTime);

                            #endregion
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return res;
        }

        #endregion

        #region//WSASendTo_Hook  

        public static unsafe SocketError WSASendTo_Hook(
            [In] Int32 socket,
            [In] IntPtr lpWSABuffer,
            [In] Int32 bufferCount,
            [Out] IntPtr lpNumberOfBytesSent,
            [In] SocketFlags flags,
            [In] ref Socket_Cache.SocketPacket.SockAddr To,
            [In] IntPtr lpToLen,
            [In] IntPtr lpOverlapped,
            [In] IntPtr lpCompletionRoutine)
        {
            SocketError res = SocketError.SocketError;
            Socket_Cache.SocketPacket.PacketType packetType = Socket_Cache.SocketPacket.PacketType.WSASendTo;

            try
            {
                DateTime packetTime = DateTime.Now;
                Socket_Cache.SocketPacket.WSABUF* pWSABuffers = (Socket_Cache.SocketPacket.WSABUF*)lpWSABuffer;

                if (bufferCount == 1)
                {
                    #region//单缓存区

                    int BytesSent = pWSABuffers[0].len;
                    if (BytesSent > 0)
                    {
                        byte[] bRawBuffer = null;
                        byte[] bNewBuffer = null;

                        Span<byte> bBufferSpan = new Span<byte>((byte*)pWSABuffers[0].buf, BytesSent);
                        bRawBuffer = bBufferSpan.ToArray();

                        Socket_Cache.Filter.FilterAction filterAction =
                        ApplyFilterSafely(
                            socket,
                            bBufferSpan,
                            out bNewBuffer,
                            packetType,
                            To);

                        if (filterAction == Socket_Cache.Filter.FilterAction.Intercept)
                        {
                            Marshal.WriteInt32(lpNumberOfBytesSent, BytesSent);
                            res = SocketError.Success;
                        }
                        else
                        {
                            BytesSent = Math.Min(bNewBuffer.Length, BytesSent);
                            bNewBuffer.AsSpan(0, BytesSent).CopyTo(bBufferSpan);

                            int WSABufferLen = pWSABuffers[0].len;
                            pWSABuffers[0].len = BytesSent;

                            res = WS2_32.WSASendTo(
                                socket,
                                lpWSABuffer,
                                bufferCount,
                                lpNumberOfBytesSent,
                                flags,
                                ref To,
                                lpToLen,
                                lpOverlapped,
                                lpCompletionRoutine);

                            pWSABuffers[0].len = WSABufferLen;
                        }

                        BytesSent = Marshal.ReadInt32(lpNumberOfBytesSent);

                        _ = Socket_Operation.ProcessingHookResultAsync(
                       socket,
                       bRawBuffer,
                       bNewBuffer,
                       BytesSent,
                       packetType,
                       filterAction,
                       To,
                       packetTime);
                    }

                    #endregion                    
                }
                else
                {
                    #region//多缓存区

                    int totalBytes = 0;
                    for (int i = 0; i < bufferCount; i++)
                    {
                        totalBytes += pWSABuffers[i].len;
                    }

                    if (totalBytes > 0)
                    {
                        byte[] bRawBuffer = new byte[totalBytes];
                        byte[] bNewBuffer = null;

                        int offset = 0;
                        for (int i = 0; i < bufferCount; i++)
                        {
                            if (pWSABuffers[i].len > 0)
                            {
                                Span<byte> bufferSpan = new Span<byte>((byte*)pWSABuffers[i].buf, pWSABuffers[i].len);
                                bufferSpan.CopyTo(new Span<byte>(bRawBuffer, offset, pWSABuffers[i].len));
                                offset += pWSABuffers[i].len;
                            }
                        }

                        Socket_Cache.Filter.FilterAction filterAction =
                            ApplyFilterSafely(
                                socket,
                                bRawBuffer.AsSpan(),
                                out bNewBuffer,
                                packetType,
                                To);

                        if (filterAction == Socket_Cache.Filter.FilterAction.Intercept)
                        {
                            Marshal.WriteInt32(lpNumberOfBytesSent, totalBytes);
                            res = SocketError.Success;
                        }
                        else if (bNewBuffer != null && bNewBuffer.Length > 0)
                        {
                            int remainingBytes = Math.Min(bNewBuffer.Length, totalBytes);
                            int bufferIndex = 0;
                            int bytesCopied = 0;

                            int[] originalLengths = new int[bufferCount];
                            for (int i = 0; i < bufferCount; i++)
                            {
                                originalLengths[i] = pWSABuffers[i].len;
                            }

                            while (remainingBytes > 0 && bufferIndex < bufferCount)
                            {
                                int copyLength = Math.Min(pWSABuffers[bufferIndex].len, remainingBytes);
                                if (copyLength > 0)
                                {
                                    Span<byte> destSpan = new Span<byte>((byte*)pWSABuffers[bufferIndex].buf, pWSABuffers[bufferIndex].len);
                                    bNewBuffer.AsSpan(bytesCopied, copyLength).CopyTo(destSpan);
                                    pWSABuffers[bufferIndex].len = copyLength;
                                    bytesCopied += copyLength;
                                    remainingBytes -= copyLength;
                                }
                                bufferIndex++;
                            }

                            res = WS2_32.WSASendTo(
                                socket,
                                lpWSABuffer,
                                bufferCount,
                                lpNumberOfBytesSent,
                                flags,
                                ref To,
                                lpToLen,
                                lpOverlapped,
                                lpCompletionRoutine);

                            for (int i = 0; i < bufferCount; i++)
                            {
                                pWSABuffers[i].len = originalLengths[i];
                            }
                        }
                        else
                        {
                            res = WS2_32.WSASendTo(
                                socket,
                                lpWSABuffer,
                                bufferCount,
                                lpNumberOfBytesSent,
                                flags,
                                ref To,
                                lpToLen,
                                lpOverlapped,
                                lpCompletionRoutine);
                        }

                        int bytesSent = Marshal.ReadInt32(lpNumberOfBytesSent);
                        _ = Socket_Operation.ProcessingHookResultAsync(
                            socket,
                            bRawBuffer,
                            bNewBuffer,
                            bytesSent,
                            packetType,
                            filterAction,
                            To,
                            packetTime);
                    }                    

                    #endregion
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return res;
        }

        #endregion

        #region//WSARecvFrom_Hook

        public static unsafe SocketError WSARecvFrom_Hook(
            [In] Int32 socket,
            [In, Out] IntPtr lpWSABuffer,
            [In] Int32 bufferCount,
            [Out] IntPtr lpNumberOfBytesRecvd,
            [In, Out] ref SocketFlags flags,
            [In, Out] ref Socket_Cache.SocketPacket.SockAddr from,
            [In, Out] IntPtr lpFromlen,
            [In] IntPtr lpOverlapped,
            [In] IntPtr lpCompletionRoutine)
        {
            Socket_Cache.SocketPacket.PacketType packetType = Socket_Cache.SocketPacket.PacketType.WSARecvFrom;
            SocketError res = WS2_32.WSARecvFrom(socket, lpWSABuffer, bufferCount, lpNumberOfBytesRecvd, ref flags, ref from, lpFromlen, lpOverlapped, lpCompletionRoutine);

            try
            {
                if (res == SocketError.Success)
                {
                    int BytesRecvd = Marshal.ReadInt32(lpNumberOfBytesRecvd);
                    if (BytesRecvd > 0)
                    {  
                        DateTime packetTime = DateTime.Now;
                        Socket_Cache.SocketPacket.WSABUF* pWSABuffers = (Socket_Cache.SocketPacket.WSABUF*)lpWSABuffer;

                        if (bufferCount == 1)
                        {
                            #region//单缓存区

                            byte[] bRawBuffer = null;
                            byte[] bNewBuffer = null;

                            Span<byte> bufferSpan = new Span<byte>((byte*)pWSABuffers[0].buf, BytesRecvd);
                            bRawBuffer = bufferSpan.ToArray();

                            Socket_Cache.Filter.FilterAction filterAction =
                                ApplyFilterSafely(
                                    socket,
                                    bufferSpan,
                                    out bNewBuffer,
                                    packetType,
                                    from);

                            int bytesToWrite = 0;
                            if (filterAction != Socket_Cache.Filter.FilterAction.Intercept)
                            {
                                bytesToWrite = Math.Min(bNewBuffer.Length, BytesRecvd);
                                bNewBuffer.AsSpan(0, bytesToWrite).CopyTo(bufferSpan);
                            }

                            Marshal.WriteInt32(lpNumberOfBytesRecvd, bytesToWrite);

                            _ = Socket_Operation.ProcessingHookResultAsync(
                                socket,
                                bRawBuffer,
                                bNewBuffer,
                                bytesToWrite,
                                packetType,
                                filterAction,
                                from,
                                packetTime);

                            #endregion
                        }
                        else
                        {
                            #region//多缓存区

                            int remainingBytes = BytesRecvd;
                            int[] bufferBytes = new int[bufferCount];

                            for (int i = 0; i < bufferCount && remainingBytes > 0; i++)
                            {
                                int bufferSize = pWSABuffers[i].len;
                                bufferBytes[i] = Math.Min(bufferSize, remainingBytes);
                                remainingBytes -= bufferBytes[i];
                            }

                            byte[] bRawBuffer = new byte[BytesRecvd];
                            int offset = 0;
                            for (int i = 0; i < bufferCount; i++)
                            {
                                if (bufferBytes[i] > 0)
                                {
                                    Span<byte> bufferSpan = new Span<byte>((byte*)pWSABuffers[i].buf, bufferBytes[i]);
                                    bufferSpan.CopyTo(new Span<byte>(bRawBuffer, offset, bufferBytes[i]));
                                    offset += bufferBytes[i];
                                }
                            }

                            byte[] bNewBuffer = null;
                            Socket_Cache.Filter.FilterAction filterAction =
                                ApplyFilterSafely(
                                    socket,
                                    bRawBuffer.AsSpan(),
                                    out bNewBuffer,
                                    packetType,
                                    from);

                            int bytesToWrite = 0;
                            if (filterAction != Socket_Cache.Filter.FilterAction.Intercept && bNewBuffer != null)
                            {
                                remainingBytes = Math.Min(bNewBuffer.Length, BytesRecvd);
                                bytesToWrite = remainingBytes;

                                for (int i = 0; i < bufferCount && remainingBytes > 0; i++)
                                {
                                    int copyLength = Math.Min(pWSABuffers[i].len, remainingBytes);
                                    if (copyLength > 0)
                                    {
                                        Span<byte> destSpan = new Span<byte>((byte*)pWSABuffers[i].buf, copyLength);
                                        bNewBuffer.AsSpan(BytesRecvd - remainingBytes, copyLength).CopyTo(destSpan);
                                        remainingBytes -= copyLength;
                                    }
                                }
                            }

                            Marshal.WriteInt32(lpNumberOfBytesRecvd, bytesToWrite);

                            _ = Socket_Operation.ProcessingHookResultAsync(
                                socket,
                                bRawBuffer,
                                bNewBuffer,
                                bytesToWrite,
                                packetType,
                                filterAction,
                                from,
                                packetTime);

                            #endregion
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return res;
        }

        #endregion
    }
}
