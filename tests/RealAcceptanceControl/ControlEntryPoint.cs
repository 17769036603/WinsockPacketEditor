using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using EasyHook;
using Newtonsoft.Json.Linq;
using WPELibrary.Lib;
using WPELibrary.Lib.Vision;

namespace RealAcceptanceControl
{
    public sealed class ControlEntryPoint : IEntryPoint
    {
        private const string ResidentStatePath =
            @"F:\项目目录\飘渺游戏助手\tools\treasure-reader\work\treasure-inventory-resident-state.json";
        private const string PreflightChannel = "TREASURE_PREFLIGHT";
        private const string LiveJumpChannel = "TREASURE_LIVE_JUMP";
        private const string JumpOnlyPreflightChannel = "TREASURE_JUMP_ONLY_PREFLIGHT";
        private const string LiveJumpOnlyChannel = "TREASURE_LIVE_JUMP_ONLY";
        private const string LiveJumpLastRealChannel = "TREASURE_LIVE_JUMP_LAST_REAL";
        private const string EncodedAdvancedPreflightChannel = "TREASURE_ENCODED_ADVANCED_PREFLIGHT";
        private const string EncodedAdvancedLiveChannel = "TREASURE_ENCODED_ADVANCED_LIVE";
        private const string EncodedOrdinaryPreflightChannel = "TREASURE_ENCODED_ORDINARY_PREFLIGHT";
        private const string EncodedOrdinaryLiveChannel = "TREASURE_ENCODED_ORDINARY_LIVE";
        private const string CaptureDiagnosticChannel = "TREASURE_CAPTURE_DIAGNOSTIC";
        private const string TemplateUseOnlyLiveChannel = "TREASURE_TEMPLATE_USE_ONLY_LIVE";
        private const int TreasureUseNum = 1;
        private const string TreasureUseParam = "2";
        private readonly string resultPath;

        public ControlEntryPoint(RemoteHooking.IContext context, string channelName)
        {
            this.resultPath = Path.Combine(
                Path.GetDirectoryName(typeof(ControlEntryPoint).Assembly.Location) ?? string.Empty,
                "control-result.txt");
        }

        public void Run(RemoteHooking.IContext context, string channelName)
        {
            try
            {
                Form socketForm = FindSocketForm();
                if (socketForm == null)
                {
                    WriteResult("socket-form-found=false");
                    return;
                }

                MethodInfo startHook = socketForm.GetType().GetMethod(
                    "StartHook_MainForm",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (startHook == null)
                {
                    WriteResult("start-hook-method-found=false");
                    return;
                }

                bool hookWasRunning = ReadHookRunning(socketForm);
                Exception invokeError = null;
                if (!hookWasRunning)
                {
                    using (ManualResetEvent completed = new ManualResetEvent(false))
                    {
                        socketForm.BeginInvoke((MethodInvoker)(() =>
                        {
                            try
                            {
                                startHook.Invoke(socketForm, null);
                            }
                            catch (Exception ex)
                            {
                                invokeError = ex;
                            }
                            finally
                            {
                                completed.Set();
                            }
                        }));

                        if (!completed.WaitOne(TimeSpan.FromSeconds(20)))
                        {
                            WriteResult("start-hook-completed=false;reason=timeout");
                            return;
                        }
                    }
                }

                if (invokeError != null)
                {
                    WriteResult("start-hook-completed=false;error=" + FlattenException(invokeError));
                    return;
                }

                WriteResult("socket-form-found=true");
                WriteResult("start-hook-invoked=" + (!hookWasRunning));
                bool hookRunning = ReadHookRunning(socketForm);
                WriteResult("hook-running=" + hookRunning);
                if (!hookRunning)
                {
                    return;
                }

                if (string.Equals(channelName, PreflightChannel, StringComparison.Ordinal) ||
                    string.Equals(channelName, LiveJumpChannel, StringComparison.Ordinal))
                {
                    RunTreasureAcceptance(
                        socketForm,
                        string.Equals(channelName, LiveJumpChannel, StringComparison.Ordinal),
                        false,
                        0);
                }
                else if (string.Equals(channelName, JumpOnlyPreflightChannel, StringComparison.Ordinal) ||
                    string.Equals(channelName, LiveJumpOnlyChannel, StringComparison.Ordinal))
                {
                    RunJumpOnlyAcceptance(
                        socketForm,
                        string.Equals(channelName, LiveJumpOnlyChannel, StringComparison.Ordinal),
                        false);
                }
                else if (string.Equals(channelName, LiveJumpLastRealChannel, StringComparison.Ordinal))
                {
                    RunJumpOnlyAcceptance(socketForm, true, true);
                }
                else if (string.Equals(channelName, EncodedAdvancedPreflightChannel, StringComparison.Ordinal) ||
                    string.Equals(channelName, EncodedAdvancedLiveChannel, StringComparison.Ordinal))
                {
                    RunTreasureAcceptance(
                        socketForm,
                        string.Equals(channelName, EncodedAdvancedLiveChannel, StringComparison.Ordinal),
                        true,
                        13);
                }
                else if (string.Equals(channelName, EncodedOrdinaryPreflightChannel, StringComparison.Ordinal) ||
                    string.Equals(channelName, EncodedOrdinaryLiveChannel, StringComparison.Ordinal))
                {
                    RunTreasureAcceptance(
                        socketForm,
                        string.Equals(channelName, EncodedOrdinaryLiveChannel, StringComparison.Ordinal),
                        true,
                        7);
                }
                else if (string.Equals(channelName, CaptureDiagnosticChannel, StringComparison.Ordinal))
                {
                    RunCaptureDiagnostic(socketForm);
                }
                else if (string.Equals(channelName, TemplateUseOnlyLiveChannel, StringComparison.Ordinal))
                {
                    RunTemplateUseOnly(socketForm);
                }
            }
            catch (Exception ex)
            {
                WriteResult("control-error=" + FlattenException(ex));
            }
        }

        private void RunJumpOnlyAcceptance(
            Form socketForm,
            bool liveSend,
            bool allowLastRealCapturedTemplate)
        {
            JumpOnlyPrepared prepared = null;
            int capturedListCount = 0;
            int currentSocket = 0;
            string lastErrorCode = string.Empty;
            string lastError = string.Empty;
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);

            while (DateTime.UtcNow < deadline)
            {
                JumpOnlyPrepared candidate = null;
                int candidateSocket = 0;
                Exception snapshotError = null;
                bool invoked = InvokeOnForm(socketForm, delegate
                {
                    try
                    {
                        capturedListCount = Socket_Cache.SocketList.lstRecPacket.Count;
                        TreasurePacketTemplate template;
                        try
                        {
                            template = DiscoverJumpTemplate(
                                Socket_Cache.SocketList.lstRecPacket.ToList());
                        }
                        catch (TreasurePacketRuntimeException ex)
                        {
                            if (!allowLastRealCapturedTemplate || ex.Code != "jump_template_not_found")
                            {
                                throw;
                            }

                            template = CreateLastRealCapturedJumpTemplate();
                            WriteResult("template-source=last-real-capture-20260820");
                        }
                        string residentState = File.ReadAllText(ResidentStatePath, Encoding.UTF8);
                        candidate = PrepareJumpFromResidentState(
                            residentState,
                            template,
                            null);
                        candidateSocket = Socket_Cache.SocketList.FindLatestMatchingSocket(
                            Socket_Cache.SocketList.lstRecPacket,
                            new[]
                            {
                                new Socket_PacketInfo
                                {
                                    PacketType = template.PacketType,
                                    PacketTo = template.PacketTo
                                }
                            });
                        if (candidateSocket <= 0)
                        {
                            candidate = null;
                            lastErrorCode = "current_socket_not_found";
                            lastError = "No current captured socket matched the real Jump template.";
                        }
                        else
                        {
                            lastErrorCode = string.Empty;
                            lastError = string.Empty;
                        }
                    }
                    catch (TreasurePacketRuntimeException ex)
                    {
                        lastErrorCode = ex.Code;
                        lastError = ex.Message;
                        WriteCaptureDiagnostics(Socket_Cache.SocketList.lstRecPacket);
                    }
                    catch (Exception ex)
                    {
                        snapshotError = ex;
                    }
                }, out Exception invokeError);

                if (!invoked)
                {
                    lastErrorCode = "form_invoke_failed";
                    lastError = FlattenException(invokeError);
                    break;
                }

                if (snapshotError != null)
                {
                    lastErrorCode = "snapshot_failed";
                    lastError = FlattenException(snapshotError);
                    break;
                }

                if (candidate != null && candidateSocket > 0)
                {
                    prepared = candidate;
                    currentSocket = candidateSocket;
                    break;
                }

                Thread.Sleep(500);
            }

            WriteResult("treasure-mode=" + (liveSend ? "live-jump-only" : "jump-only-preflight"));
            WriteResult("resident-state-path=" + ResidentStatePath);
            WriteResult("target-package-num=resident-selected");
            WriteResult("captured-list-count=" + capturedListCount);
            WriteResult("current-socket=" + currentSocket);
            WriteResult("preflight-ready=" + (prepared != null));
            if (!string.IsNullOrEmpty(lastErrorCode))
            {
                WriteResult("preflight-error-code=" + lastErrorCode);
                WriteResult("preflight-error=" + lastError);
            }

            if (prepared == null)
            {
                return;
            }

            WriteResult("target-package=" + prepared.Target.PackageNum);
            WriteResult("target-scene=" + prepared.Target.Scene);
            WriteResult("target-x=" + prepared.Target.X);
            WriteResult("target-y=" + prepared.Target.Y);
            WriteResult("jump-bytes=" + ToHex(prepared.JumpPacket.PacketBuffer));
            WriteResult("use-bytes=not-prepared-in-jump-only-mode");

            if (!liveSend)
            {
                WriteResult("live-send-attempted=false");
                return;
            }

            TreasurePacketSendResult sendResult = null;
            Exception sendError = null;
            if (!InvokeOnForm(socketForm, delegate
            {
                try
                {
                    TreasureLiveSendAuthorization authorization =
                        TreasureLiveSendAuthorization.Create("TREASURE-LIVE-SEND");
                    sendResult = TreasurePacketRuntime.SendPreparedPacketOnce(
                        prepared.JumpPacket,
                        authorization);
                }
                catch (Exception ex)
                {
                    sendError = ex;
                }
            }, out Exception sendInvokeError))
            {
                sendError = sendInvokeError;
            }

            WriteResult("live-send-attempted=true");
            if (sendError != null)
            {
                WriteResult("live-send-result=false");
                WriteResult("live-send-error=" + FlattenException(sendError));
                return;
            }

            WriteResult("live-send-result=" + sendResult.Success);
            WriteResult("live-send-code=" + sendResult.Code);
            WriteResult("live-send-socket=" + sendResult.Socket);
            WriteResult("live-send-bytes=" + sendResult.BytesSent);
        }

        private void RunTreasureAcceptance(
            Form socketForm,
            bool liveSend,
            bool useEncoder,
            int useType)
        {
            PurePacketPrepared prepared = null;
            int capturedListCount = 0;
            int jumpSocket = 0;
            int useSocket = 0;
            string lastErrorCode = string.Empty;
            string lastError = string.Empty;
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);

            while (DateTime.UtcNow < deadline)
            {
                PurePacketPrepared candidate = null;
                int candidateJumpSocket = 0;
                int candidateUseSocket = 0;
                Exception snapshotError = null;
                bool invoked = InvokeOnForm(socketForm, delegate
                {
                    try
                    {
                        capturedListCount = Socket_Cache.SocketList.lstRecPacket.Count;
                        List<Socket_PacketInfo> capturedPackets =
                            Socket_Cache.SocketList.lstRecPacket.ToList();
                        TreasurePacketRuntime.ObserveCapturedPackets(capturedPackets);
                        string residentState = File.ReadAllText(
                            ResidentStatePath,
                            Encoding.UTF8);
                        TreasurePacketPreparedSet preparedSet;
                        TreasurePacketRoute encodedRoute = null;
                        TreasurePacketSessionSnapshot sessionSnapshot = null;
                        if (useEncoder)
                        {
                            try
                            {
                                encodedRoute = TreasurePacketRuntime.DiscoverOutgoingRoute(capturedPackets);
                            }
                            catch (TreasurePacketRuntimeException ex)
                            {
                                if (ex.Code != "current_route_not_found")
                                {
                                    throw;
                                }

                                encodedRoute = TreasurePacketRuntime.GetSessionRoute();
                            }
                            preparedSet = TreasurePacketRuntime.PrepareEncodedFromResidentState(
                                residentState,
                                encodedRoute,
                                useType,
                                TreasureUseNum,
                                TreasureUseParam,
                                null);
                        }
                        else
                        {
                            sessionSnapshot = TreasurePacketRuntime.GetSessionSnapshot();
                            preparedSet = TreasurePacketRuntime.PrepareFromResidentState(
                                residentState,
                                sessionSnapshot.Templates,
                                null);
                        }
                        candidate = new PurePacketPrepared
                        {
                            Target = preparedSet.Target,
                            JumpPacket = preparedSet.JumpPacket,
                            UsePacket = preparedSet.UsePacket
                        };

                        if (useEncoder)
                        {
                            candidateJumpSocket = TreasurePacketRuntime.ResolveCurrentSessionSocket(encodedRoute);
                            candidateUseSocket = candidateJumpSocket;
                        }
                        else
                        {
                            candidateJumpSocket = TreasurePacketRuntime.ResolveCurrentSessionSocket(
                                sessionSnapshot.Templates.Jump.PacketType,
                                sessionSnapshot.Templates.Jump.PacketTo);
                            candidateUseSocket = TreasurePacketRuntime.ResolveCurrentSessionSocket(
                                sessionSnapshot.Templates.Use.PacketType,
                                sessionSnapshot.Templates.Use.PacketTo);
                        }
                        if (candidateJumpSocket <= 0 || candidateUseSocket <= 0)
                        {
                            candidate = null;
                            lastErrorCode = "current_socket_not_found";
                            lastError = useEncoder
                                ? "No current captured socket matched the encoded game route."
                                : "No current captured socket matched both real Jump and Use templates.";
                        }
                        else
                        {
                            lastErrorCode = string.Empty;
                            lastError = string.Empty;
                        }
                    }
                    catch (TreasurePacketRuntimeException ex)
                    {
                        lastErrorCode = ex.Code;
                        lastError = ex.Message;
                        if (ex.Code == "jump_template_not_found" ||
                            ex.Code == "use_template_not_found" ||
                            ex.Code == "current_route_not_found")
                        {
                            WriteCaptureDiagnostics(Socket_Cache.SocketList.lstRecPacket);
                        }
                    }
                    catch (Exception ex)
                    {
                        snapshotError = ex;
                    }
                }, out Exception invokeError);

                if (!invoked)
                {
                    lastErrorCode = "form_invoke_failed";
                    lastError = FlattenException(invokeError);
                    break;
                }

                if (snapshotError != null)
                {
                    lastErrorCode = "snapshot_failed";
                    lastError = FlattenException(snapshotError);
                    break;
                }

                if (candidate != null && candidateJumpSocket > 0 && candidateUseSocket > 0)
                {
                    prepared = candidate;
                    jumpSocket = candidateJumpSocket;
                    useSocket = candidateUseSocket;
                    break;
                }

                Thread.Sleep(500);
            }

            string mode = liveSend ? "live-pure-packet" : "pure-packet-preflight";
            if (useEncoder)
            {
                mode = liveSend ? "live-encoded-packet" : "encoded-packet-preflight";
            }
            WriteResult("treasure-mode=" + mode);
            WriteResult("resident-state-path=" + ResidentStatePath);
            WriteResult("target-package-num=resident-selected");
            WriteResult("packet-source=" + (useEncoder ? "current-game-route-and-closed-encoder" : "current-session-templates"));
            if (useEncoder)
            {
                WriteResult("use-type=" + useType);
                WriteResult("use-num=" + TreasureUseNum);
                WriteResult("use-param=" + TreasureUseParam);
            }
            WriteResult("captured-list-count=" + capturedListCount);
            WriteResult("jump-socket=" + jumpSocket);
            WriteResult("use-socket=" + useSocket);
            WriteResult("preflight-ready=" + (prepared != null));
            if (!string.IsNullOrEmpty(lastErrorCode))
            {
                WriteResult("preflight-error-code=" + lastErrorCode);
                WriteResult("preflight-error=" + lastError);
            }

            if (prepared == null)
            {
                return;
            }

            WriteResult("target-package=" + prepared.Target.PackageNum);
            WriteResult("target-scene=" + prepared.Target.Scene);
            WriteResult("target-x=" + prepared.Target.X);
            WriteResult("target-y=" + prepared.Target.Y);
            WriteResult("jump-bytes=" + ToHex(prepared.JumpPacket.PacketBuffer));
            WriteResult("use-bytes=" + ToHex(prepared.UsePacket.PacketBuffer));

            if (!liveSend)
            {
                WriteResult("live-send-attempted=false");
                return;
            }

            TreasurePacketSendResult jumpSendResult = null;
            Exception jumpSendError = null;
            if (!InvokeOnForm(socketForm, delegate
            {
                try
                {
                    TreasureLiveSendAuthorization authorization =
                        TreasureLiveSendAuthorization.Create("TREASURE-LIVE-SEND");
                    jumpSendResult = TreasurePacketRuntime.SendPreparedPacketOnce(
                        prepared.JumpPacket,
                        authorization);
                }
                catch (Exception ex)
                {
                    jumpSendError = ex;
                }
            }, out Exception sendInvokeError))
            {
                jumpSendError = sendInvokeError;
            }

            WriteResult("jump-live-send-attempted=true");
            if (jumpSendError != null)
            {
                WriteResult("jump-live-send-result=false");
                WriteResult("jump-live-send-error=" + FlattenException(jumpSendError));
                return;
            }

            WriteResult("jump-live-send-result=" + jumpSendResult.Success);
            WriteResult("jump-live-send-code=" + jumpSendResult.Code);
            WriteResult("jump-live-send-socket=" + jumpSendResult.Socket);
            WriteResult("jump-live-send-bytes=" + jumpSendResult.BytesSent);
            if (!jumpSendResult.Success)
            {
                WriteResult("live-send-result=false");
                return;
            }

            Thread.Sleep(TreasurePacketRuntime.PurePacketPreset.JumpToUseDelayMilliseconds);

            TreasurePacketSendResult useSendResult = null;
            Exception useSendError = null;
            if (!InvokeOnForm(socketForm, delegate
            {
                try
                {
                    TreasureLiveSendAuthorization authorization =
                        TreasureLiveSendAuthorization.Create("TREASURE-LIVE-SEND");
                    useSendResult = TreasurePacketRuntime.SendPreparedPacketOnce(
                        prepared.UsePacket,
                        authorization);
                }
                catch (Exception ex)
                {
                    useSendError = ex;
                }
            }, out Exception useInvokeError))
            {
                useSendError = useInvokeError;
            }

            WriteResult("use-live-send-attempted=true");
            if (useSendError != null)
            {
                WriteResult("use-live-send-result=false");
                WriteResult("use-live-send-error=" + FlattenException(useSendError));
                WriteResult("live-send-result=false");
                return;
            }

            WriteResult("use-live-send-result=" + useSendResult.Success);
            WriteResult("use-live-send-code=" + useSendResult.Code);
            WriteResult("use-live-send-socket=" + useSendResult.Socket);
            WriteResult("use-live-send-bytes=" + useSendResult.BytesSent);
            WriteResult("live-send-result=" + (jumpSendResult.Success && useSendResult.Success));
        }

        private void RunCaptureDiagnostic(Form socketForm)
        {
            List<Socket_PacketInfo> recent = new List<Socket_PacketInfo>();
            int total = 0;
            Exception diagnosticError = null;
            if (!InvokeOnForm(socketForm, delegate
            {
                try
                {
                    List<Socket_PacketInfo> captured =
                        Socket_Cache.SocketList.lstRecPacket.ToList();
                    total = captured.Count;
                    recent = captured
                        .Where(IsDiagnosticProtocolPacket)
                        .Skip(Math.Max(0, captured.Count - 200))
                        .Take(80)
                        .ToList();
                }
                catch (Exception ex)
                {
                    diagnosticError = ex;
                }
            }, out Exception invokeError))
            {
                diagnosticError = invokeError;
            }

            WriteResult("capture-diagnostic-mode=read-only");
            WriteResult("capture-diagnostic-total=" + total);
            if (diagnosticError != null)
            {
                WriteResult("capture-diagnostic-error=" + FlattenException(diagnosticError));
                return;
            }

            WriteResult("capture-diagnostic-matching-count=" + recent.Count);
            foreach (Socket_PacketInfo packet in recent)
            {
                WriteResult("capture-diagnostic-packet=" + DescribeDiagnosticPacket(packet));
            }
        }

        private void RunTemplateUseOnly(Form socketForm)
        {
            Socket_PacketInfo preparedUse = null;
            TreasureInventoryTarget target = null;
            int currentSocket = 0;
            int capturedListCount = 0;
            string lastErrorCode = string.Empty;
            string lastError = string.Empty;
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);

            while (DateTime.UtcNow < deadline)
            {
                Socket_PacketInfo candidateUse = null;
                TreasureInventoryTarget candidateTarget = null;
                int candidateSocket = 0;
                Exception snapshotError = null;
                bool invoked = InvokeOnForm(socketForm, delegate
                {
                    try
                    {
                        capturedListCount = Socket_Cache.SocketList.lstRecPacket.Count;
                        TreasurePacketRuntime.ObserveCapturedPackets(
                            Socket_Cache.SocketList.lstRecPacket.ToList());
                        TreasurePacketSessionSnapshot sessionSnapshot =
                            TreasurePacketRuntime.GetSessionSnapshot();
                        string residentState = File.ReadAllText(
                            ResidentStatePath,
                            Encoding.UTF8);
                        TreasurePacketPreparedSet preparedSet =
                            TreasurePacketRuntime.PrepareFromResidentState(
                                residentState,
                                sessionSnapshot.Templates,
                                null);
                        candidateUse = preparedSet.UsePacket;
                        candidateTarget = preparedSet.Target;
                        candidateSocket = TreasurePacketRuntime.ResolveCurrentSessionSocket(
                            sessionSnapshot.Templates.Use.PacketType,
                            sessionSnapshot.Templates.Use.PacketTo);
                        if (candidateSocket <= 0)
                        {
                            candidateUse = null;
                            candidateTarget = null;
                            lastErrorCode = "current_socket_not_found";
                            lastError = "No current captured socket matched the Use template.";
                        }
                        else
                        {
                            lastErrorCode = string.Empty;
                            lastError = string.Empty;
                        }
                    }
                    catch (TreasurePacketRuntimeException ex)
                    {
                        lastErrorCode = ex.Code;
                        lastError = ex.Message;
                    }
                    catch (Exception ex)
                    {
                        snapshotError = ex;
                    }
                }, out Exception invokeError);

                if (!invoked)
                {
                    lastErrorCode = "form_invoke_failed";
                    lastError = FlattenException(invokeError);
                    break;
                }

                if (snapshotError != null)
                {
                    lastErrorCode = "snapshot_failed";
                    lastError = FlattenException(snapshotError);
                    break;
                }

                if (candidateUse != null && candidateTarget != null && candidateSocket > 0)
                {
                    preparedUse = candidateUse;
                    target = candidateTarget;
                    currentSocket = candidateSocket;
                    break;
                }

                Thread.Sleep(500);
            }

            WriteResult("treasure-mode=live-template-use-only");
            WriteResult("resident-state-path=" + ResidentStatePath);
            WriteResult("packet-source=current-session-use-template-with-slot-patch");
            WriteResult("captured-list-count=" + capturedListCount);
            WriteResult("use-socket=" + currentSocket);
            WriteResult("preflight-ready=" + (preparedUse != null));
            if (!string.IsNullOrEmpty(lastErrorCode))
            {
                WriteResult("preflight-error-code=" + lastErrorCode);
                WriteResult("preflight-error=" + lastError);
            }

            if (preparedUse == null || target == null)
            {
                return;
            }

            TreasureUsePacketRequest request = TreasurePacketEncoder.DecodeUse(
                preparedUse.PacketBuffer);
            WriteResult("target-package=" + target.PackageNum);
            WriteResult("target-scene=" + target.Scene);
            WriteResult("target-x=" + target.X);
            WriteResult("target-y=" + target.Y);
            WriteResult("use-type=" + request.Type);
            WriteResult("use-num=" + request.Num);
            WriteResult("use-param=" + request.Param);
            WriteResult("use-bytes=" + ToHex(preparedUse.PacketBuffer));

            TreasurePacketSendResult sendResult = null;
            Exception sendError = null;
            if (!InvokeOnForm(socketForm, delegate
            {
                try
                {
                    sendResult = TreasurePacketRuntime.SendPreparedPacketOnce(
                        preparedUse,
                        TreasureLiveSendAuthorization.Create("TREASURE-LIVE-SEND"));
                }
                catch (Exception ex)
                {
                    sendError = ex;
                }
            }, out Exception sendInvokeError))
            {
                sendError = sendInvokeError;
            }

            WriteResult("use-live-send-attempted=true");
            if (sendError != null)
            {
                WriteResult("use-live-send-result=false");
                WriteResult("use-live-send-error=" + FlattenException(sendError));
                return;
            }

            WriteResult("use-live-send-result=" + sendResult.Success);
            WriteResult("use-live-send-code=" + sendResult.Code);
            WriteResult("use-live-send-socket=" + sendResult.Socket);
            WriteResult("use-live-send-bytes=" + sendResult.BytesSent);
            WriteResult("live-send-result=" + sendResult.Success);
        }

        private static bool IsDiagnosticProtocolPacket(Socket_PacketInfo packet)
        {
            if (packet == null || packet.PacketBuffer == null || packet.PacketBuffer.Length < 12)
            {
                return false;
            }

            int protocol = (packet.PacketBuffer[10] << 8) | packet.PacketBuffer[11];
            return protocol == 0x5828 || protocol == 0x783A ||
                protocol == 0x8112 || protocol == 0x10D4 ||
                protocol == 0xFFE1 || protocol == 0xFFE2 ||
                protocol == 0x1099;
        }

        private static string DescribeDiagnosticPacket(Socket_PacketInfo packet)
        {
            int protocol = (packet.PacketBuffer[10] << 8) | packet.PacketBuffer[11];
            StringBuilder description = new StringBuilder();
            description.Append("time=").Append(packet.PacketTime.ToString("HH:mm:ss.fffffff"));
            description.Append(";type=").Append(packet.PacketType);
            description.Append(";socket=").Append(packet.PacketSocket);
            description.Append(";from=").Append(packet.PacketFrom);
            description.Append(";to=").Append(packet.PacketTo);
            description.Append(";protocol=0x").Append(protocol.ToString("X4"));
            description.Append(";len=").Append(packet.PacketBuffer.Length);

            if (protocol == TreasureUsePacketContract.ProtocolId && packet.PacketBuffer.Length >= 25)
            {
                description.Append(";pos=").Append(ReadInt32BigEndian(packet.PacketBuffer, 12));
                description.Append(";use-type=").Append(ReadInt32BigEndian(packet.PacketBuffer, 16));
                description.Append(";num=").Append(ReadInt32BigEndian(packet.PacketBuffer, 20));
                description.Append(";param-len=").Append(packet.PacketBuffer[24]);
            }
            else if (protocol == TreasureJumpPacketContract.ProtocolId && packet.PacketBuffer.Length >= 28)
            {
                description.Append(";map=").Append(ReadInt32BigEndian(packet.PacketBuffer, 12));
                description.Append(";x=").Append(ReadInt32BigEndian(packet.PacketBuffer, 16));
                description.Append(";y=").Append(ReadInt32BigEndian(packet.PacketBuffer, 20));
            }

            description.Append(";hex=").Append(ToHex(packet.PacketBuffer));
            return description.ToString();
        }

        private static int ReadInt32BigEndian(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24) |
                (buffer[offset + 1] << 16) |
                (buffer[offset + 2] << 8) |
                buffer[offset + 3];
        }

        private static TreasurePacketTemplate CreateLastRealCapturedJumpTemplate()
        {
            return new TreasurePacketTemplate(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "198.18.0.1:46852",
                "202.189.18.52:14567",
                new byte[]
                {
                    0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x12, 0x58, 0x28, 0x00, 0x00, 0x03, 0xFA,
                    0x00, 0x00, 0x00, 0x90, 0x00, 0x00, 0x00, 0x16,
                    0x00, 0x00, 0x00, 0x00
                });
        }

        private sealed class JumpOnlyPrepared
        {
            public TreasureInventoryTarget Target { get; set; }

            public Socket_PacketInfo JumpPacket { get; set; }
        }

        private sealed class PurePacketPrepared
        {
            public TreasureInventoryTarget Target { get; set; }

            public Socket_PacketInfo JumpPacket { get; set; }

            public Socket_PacketInfo UsePacket { get; set; }
        }

        private static TreasurePacketTemplate DiscoverJumpTemplate(
            IEnumerable<Socket_PacketInfo> capturedPackets)
        {
            TreasurePacketTemplate jump = null;
            TreasureInventoryTarget validationTarget = new TreasureInventoryTarget(1, 1, 0, 0);
            foreach (Socket_PacketInfo packet in capturedPackets)
            {
                if (packet == null || packet.PacketBuffer == null || packet.PacketBuffer.Length == 0 ||
                    packet.PacketType.ToString().IndexOf("Send", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                try
                {
                    TreasurePacketTemplatePatcher.PatchJump(packet.PacketBuffer, validationTarget);
                    jump = new TreasurePacketTemplate(
                        packet.PacketType,
                        packet.PacketFrom,
                        packet.PacketTo,
                        packet.PacketBuffer);
                }
                catch (TreasurePacketTemplateException)
                {
                    // This is a normal non-Jump packet in the live capture list.
                }
            }

            if (jump == null)
            {
                throw new TreasurePacketRuntimeException(
                    "jump_template_not_found",
                    "No current outgoing 0x5828 Jump template was found in the capture list.");
            }

            return jump;
        }

        private static JumpOnlyPrepared PrepareJumpFromResidentState(
            string residentStateJson,
            TreasurePacketTemplate template,
            int? packageNum)
        {
            JObject state;
            try
            {
                state = JObject.Parse(residentStateJson);
            }
            catch (Exception ex)
            {
                throw new TreasurePacketRuntimeException(
                    "state_json_invalid",
                    "Resident state is not a valid JSON object: " + ex.Message);
            }

            if (state.Value<bool?>("available") != true)
            {
                throw new TreasurePacketRuntimeException(
                    "state_not_ready",
                    "Resident state is not available.");
            }

            if (state.Value<bool?>("actionAuthorized") != false)
            {
                throw new TreasurePacketRuntimeException(
                    "action_authority_invalid",
                    "Resident state action authority must remain false.");
            }

            List<TreasureInventoryTarget> targets = new List<TreasureInventoryTarget>();
            JArray items = state["items"] as JArray;
            if (items != null)
            {
                foreach (JObject item in items.OfType<JObject>())
                {
                    int itemPackage = item.Value<int?>("packageNum") ?? 0;
                    int itemSlot = item.Value<int?>("slot") ?? 0;
                    int scene = item.Value<int?>("scene") ?? 0;
                    int mapId = item.Value<int?>("mapId") ?? 0;
                    int x = item.Value<int?>("x") ?? -1;
                    int y = item.Value<int?>("y") ?? -1;
                    if (itemPackage <= 0 || itemSlot <= 0 || scene <= 0 || mapId <= 0 ||
                        x < 0 || y < 0 || itemPackage != itemSlot || scene != mapId ||
                        (packageNum.HasValue && itemPackage != packageNum.Value))
                    {
                        continue;
                    }

                    targets.Add(new TreasureInventoryTarget(itemPackage, scene, x, y));
                }
            }

            if (targets.Count == 0)
            {
                throw new TreasurePacketRuntimeException(
                    "no_valid_treasure",
                    "No valid treasure-map member is available.");
            }

            if (targets.Count != 1)
            {
                throw new TreasurePacketRuntimeException(
                    "ambiguous_treasure",
                    "Multiple treasure-map members are current; select one packageNum.");
            }

            TreasureInventoryTarget target = targets[0];
            byte[] jumpBytes = TreasurePacketTemplatePatcher.PatchJump(template.Buffer, target);
            Socket_PacketInfo packet = new Socket_PacketInfo
            {
                PacketSocket = 0,
                PacketType = template.PacketType,
                PacketFrom = template.PacketFrom,
                PacketTo = template.PacketTo,
                PacketBuffer = jumpBytes,
                PacketLen = jumpBytes.Length,
                PacketTime = DateTime.UtcNow
            };
            return new JumpOnlyPrepared
            {
                Target = target,
                JumpPacket = packet
            };
        }

        private void WriteCaptureDiagnostics(IEnumerable<Socket_PacketInfo> packets)
        {
            int total = 0;
            int sendCount = 0;
            Dictionary<string, int> typeCounts = new Dictionary<string, int>();
            List<Socket_PacketInfo> candidates = new List<Socket_PacketInfo>();

            foreach (Socket_PacketInfo packet in packets)
            {
                if (packet == null || packet.PacketBuffer == null || packet.PacketBuffer.Length == 0)
                {
                    continue;
                }

                total++;
                string type = packet.PacketType.ToString();
                int count;
                typeCounts.TryGetValue(type, out count);
                typeCounts[type] = count + 1;

                if (type.IndexOf("Send", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sendCount++;
                }

                bool protocolAtExpectedOffset = packet.PacketBuffer.Length >= 12 &&
                    packet.PacketBuffer[10] == 0x58 && packet.PacketBuffer[11] == 0x28;
                bool useProtocolAtExpectedOffset = packet.PacketBuffer.Length >= 12 &&
                    packet.PacketBuffer[10] == 0x78 && packet.PacketBuffer[11] == 0x3A;
                bool hasProtocolBytes = false;
                bool hasUseProtocolBytes = false;
                for (int index = 0; index + 1 < packet.PacketBuffer.Length; index++)
                {
                    if (packet.PacketBuffer[index] == 0x58 && packet.PacketBuffer[index + 1] == 0x28)
                    {
                        hasProtocolBytes = true;
                    }
                    if (packet.PacketBuffer[index] == 0x78 && packet.PacketBuffer[index + 1] == 0x3A)
                    {
                        hasUseProtocolBytes = true;
                    }
                }

                if (packet.PacketBuffer.Length == 28 || protocolAtExpectedOffset || hasProtocolBytes ||
                    useProtocolAtExpectedOffset || hasUseProtocolBytes)
                {
                    candidates.Add(packet);
                }
            }

            WriteResult("capture-diagnostic-total=" + total);
            WriteResult("capture-diagnostic-send-count=" + sendCount);
            foreach (KeyValuePair<string, int> item in typeCounts)
            {
                WriteResult("capture-diagnostic-type=" + item.Key + ":" + item.Value);
            }

            int limit = Math.Min(candidates.Count, 20);
            for (int index = 0; index < limit; index++)
            {
                Socket_PacketInfo packet = candidates[index];
                WriteResult(
                    "capture-diagnostic-candidate=" +
                    packet.PacketType +
                    ";socket=" + packet.PacketSocket +
                    ";from=" + packet.PacketFrom +
                    ";to=" + packet.PacketTo +
                    ";len=" + packet.PacketBuffer.Length +
                    ";jump-protocol=" + (packet.PacketBuffer.Length >= 12 &&
                        packet.PacketBuffer[10] == 0x58 && packet.PacketBuffer[11] == 0x28) +
                    ";use-protocol=" + (packet.PacketBuffer.Length >= 12 &&
                        packet.PacketBuffer[10] == 0x78 && packet.PacketBuffer[11] == 0x3A) +
                    ";hex=" + ToHex(packet.PacketBuffer));
            }

            WriteResult("capture-diagnostic-candidate-count=" + candidates.Count);
        }

        private static bool InvokeOnForm(Form form, Action action, out Exception error)
        {
            error = null;
            Exception callbackError = null;
            using (ManualResetEvent completed = new ManualResetEvent(false))
            {
                try
                {
                    form.BeginInvoke((MethodInvoker)(() =>
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            callbackError = ex;
                        }
                        finally
                        {
                            completed.Set();
                        }
                    }));
                }
                catch (Exception ex)
                {
                    error = ex;
                    return false;
                }

                if (!completed.WaitOne(TimeSpan.FromSeconds(20)))
                {
                    error = new TimeoutException("The target Socket_Form did not complete the acceptance action.");
                    return false;
                }
            }

            error = callbackError;
            return error == null;
        }

        private static string ToHex(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder(buffer.Length * 3 - 1);
            for (int index = 0; index < buffer.Length; index++)
            {
                if (index > 0)
                {
                    result.Append(' ');
                }

                result.Append(buffer[index].ToString("X2"));
            }

            return result.ToString();
        }

        private static Form FindSocketForm()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (string.Equals(
                    form.GetType().FullName,
                    "WPELibrary.Socket_Form",
                    StringComparison.Ordinal))
                {
                    return form;
                }
            }

            return null;
        }

        private static bool ReadHookRunning(Form socketForm)
        {
            FieldInfo hookField = socketForm.GetType().GetField(
                "ws",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object hook = hookField == null ? null : hookField.GetValue(socketForm);
            PropertyInfo isRunning = hook == null
                ? null
                : hook.GetType().GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public);
            return isRunning != null && (bool)isRunning.GetValue(hook, null);
        }

        private void WriteResult(string line)
        {
            try
            {
                File.AppendAllText(this.resultPath, line + Environment.NewLine);
            }
            catch
            {
                // The UI state remains the authoritative verification signal.
            }
        }

        private static string FlattenException(Exception exception)
        {
            string message = exception.Message;
            Exception inner = exception.InnerException;
            while (inner != null)
            {
                message += " | " + inner.Message;
                inner = inner.InnerException;
            }

            return message.Replace(Environment.NewLine, " ");
        }
    }
}
