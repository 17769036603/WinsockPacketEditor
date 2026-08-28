using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑炼化真实发送的显式授权令牌。
    /// 软件注入和方向捕获不会自动产生此令牌。
    /// </summary>
    public sealed class MountRefineLiveSendAuthorization
    {
        public const string RequiredConfirmationText = "MOUNT-REFINE-LIVE-SEND";

        private MountRefineLiveSendAuthorization()
        {
        }

        public static MountRefineLiveSendAuthorization Create(string confirmation)
        {
            if (!string.Equals(
                    confirmation,
                    RequiredConfirmationText,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "坐骑炼化真实发送需要显式确认文本。",
                    "confirmation");
            }

            return new MountRefineLiveSendAuthorization();
        }
    }

    /// <summary>
    /// 从注入捕获到的出站 A050 包保存手动发送所需的完整路由。
    /// Socket 只在当前注入会话内使用，不跨会话持久化。
    /// </summary>
    public sealed class MountRefineA050RouteTemplate
    {
        private MountRefineA050RouteTemplate(
            int packetSocket,
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetFrom,
            string packetTo)
        {
            PacketSocket = packetSocket;
            PacketType = packetType;
            PacketFrom = packetFrom ?? string.Empty;
            PacketTo = packetTo ?? string.Empty;
        }

        public int PacketSocket { get; private set; }

        public Socket_Cache.SocketPacket.PacketType PacketType { get; private set; }

        public string PacketFrom { get; private set; }

        public string PacketTo { get; private set; }

        public static bool TryCreate(
            Socket_PacketInfo capturedPacket,
            out MountRefineA050RouteTemplate route,
            out string error)
        {
            route = null;
            error = string.Empty;
            if (capturedPacket == null || capturedPacket.PacketSocket <= 0)
            {
                error = "mount_refine_route_capture_missing";
                return false;
            }

            if (!IsSendPacketType(capturedPacket.PacketType))
            {
                error = "mount_refine_route_packet_direction_invalid";
                return false;
            }

            string packetTo = (capturedPacket.PacketTo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(packetTo))
            {
                error = "mount_refine_route_destination_missing";
                return false;
            }

            route = new MountRefineA050RouteTemplate(
                capturedPacket.PacketSocket,
                capturedPacket.PacketType,
                (capturedPacket.PacketFrom ?? string.Empty).Trim(),
                packetTo);
            return true;
        }

        internal Socket_PacketInfo ToResolverTemplate()
        {
            return new Socket_PacketInfo
            {
                PacketSocket = PacketSocket,
                PacketType = PacketType,
                PacketFrom = PacketFrom,
                PacketTo = PacketTo
            };
        }

        internal static bool IsSendPacketType(
            Socket_Cache.SocketPacket.PacketType packetType)
        {
            switch (packetType)
            {
                case Socket_Cache.SocketPacket.PacketType.WS1_Send:
                case Socket_Cache.SocketPacket.PacketType.WS2_Send:
                case Socket_Cache.SocketPacket.PacketType.WS1_SendTo:
                case Socket_Cache.SocketPacket.PacketType.WS2_SendTo:
                case Socket_Cache.SocketPacket.PacketType.WSASend:
                case Socket_Cache.SocketPacket.PacketType.WSASendTo:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// 在注入捕获列表中自动选择唯一的 A050 出站方向。
    /// 没有候选或存在多个不同方向时 fail-closed，不猜测方向。
    /// </summary>
    public static class MountRefineA050CaptureBinding
    {
        private sealed class Candidate
        {
            public Socket_PacketInfo Packet { get; set; }

            public MountRefineA050RouteTemplate Route { get; set; }
        }

        public static bool TryCreate(
            IEnumerable<Socket_PacketInfo> capturedPackets,
            string evidenceId,
            out MountRefineA050PacketTemplate packetTemplate,
            out MountRefineA050RouteTemplate route,
            out string error)
        {
            string captureDiagnostics;
            return TryCreate(
                capturedPackets,
                evidenceId,
                out packetTemplate,
                out route,
                out error,
                out captureDiagnostics);
        }

        public static bool TryCreate(
            IEnumerable<Socket_PacketInfo> capturedPackets,
            string evidenceId,
            out MountRefineA050PacketTemplate packetTemplate,
            out MountRefineA050RouteTemplate route,
            out string error,
            out string captureDiagnostics)
        {
            packetTemplate = null;
            route = null;
            error = string.Empty;
            captureDiagnostics = string.Empty;
            List<Candidate> candidates = new List<Candidate>();
            Dictionary<string, int> rejectionCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            int totalPacketCount = 0;

            foreach (Socket_PacketInfo packet in capturedPackets ?? Enumerable.Empty<Socket_PacketInfo>())
            {
                totalPacketCount++;
                string prefilterRejection = GetA050PrefilterRejectionCode(packet);
                if (!string.IsNullOrWhiteSpace(prefilterRejection))
                {
                    IncrementRejectionCount(rejectionCounts, prefilterRejection);
                    continue;
                }

                // A050 可能同时出现在收发两侧；只从明确的出站类型
                // 推导方向，接收包不能把自动绑定误报成方向冲突。
                MountRefineA050RouteTemplate capturedRoute;
                if (!MountRefineA050RouteTemplate.TryCreate(
                        packet,
                        out capturedRoute,
                        out error))
                {
                    IncrementRejectionCount(
                        rejectionCounts,
                        string.IsNullOrWhiteSpace(error)
                            ? "mount_refine_route_rejected"
                            : error);
                    captureDiagnostics = BuildCaptureDiagnostics(
                        totalPacketCount,
                        candidates.Count,
                        0,
                        rejectionCounts,
                        error);
                    return false;
                }

                candidates.Add(new Candidate
                {
                    Packet = packet,
                    Route = capturedRoute
                });
            }

            if (candidates.Count == 0)
            {
                error = "mount_refine_a050_outbound_capture_missing";
                captureDiagnostics = BuildCaptureDiagnostics(
                    totalPacketCount,
                    candidates.Count,
                    0,
                    rejectionCounts,
                    error);
                return false;
            }

            // The same outbound call may be observed through send/WSASend
            // wrapper layers. Those records are one route when they share the
            // same live Socket and endpoints; packet API alone must not turn
            // them into a false ambiguity.
            List<IGrouping<string, Candidate>> routeGroups = candidates
                .GroupBy(BuildRouteKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (routeGroups.Count != 1)
            {
                error = "mount_refine_a050_outbound_route_ambiguous";
                captureDiagnostics = BuildCaptureDiagnostics(
                    totalPacketCount,
                    candidates.Count,
                    routeGroups.Count,
                    rejectionCounts,
                    error);
                return false;
            }

            Candidate selected = routeGroups[0]
                .OrderByDescending(item => item.Packet.PacketTime)
                .FirstOrDefault();
            if (selected == null)
            {
                error = "mount_refine_a050_outbound_capture_missing";
                captureDiagnostics = BuildCaptureDiagnostics(
                    totalPacketCount,
                    candidates.Count,
                    routeGroups.Count,
                    rejectionCounts,
                    error);
                return false;
            }

            if (!MountRefineA050PacketTemplate.TryCreate(
                selected.Packet.PacketBuffer,
                evidenceId,
                out packetTemplate,
                out error))
            {
                captureDiagnostics = BuildCaptureDiagnostics(
                    totalPacketCount,
                    candidates.Count,
                    routeGroups.Count,
                    rejectionCounts,
                    error);
                return false;
            }

            route = selected.Route;
            captureDiagnostics = BuildCaptureDiagnostics(
                totalPacketCount,
                candidates.Count,
                routeGroups.Count,
                rejectionCounts,
                string.Empty);
            return true;
        }

        /// <summary>
        /// 在当前注入会话上重新建立绑定。除了可见捕获列表，还会读取
        /// 本会话独立保留的 A050 证据，避免页面自动清空导致丢失。
        /// 该方法只复制当前记录并做离线校验，不读取旧会话模板，也不触发发送。
        /// </summary>
        public static bool TryCreateFromCurrentCapture(
            string evidenceId,
            out MountRefineA050PacketTemplate packetTemplate,
            out MountRefineA050RouteTemplate route,
            out string error)
        {
            string captureDiagnostics;
            return TryCreateFromCurrentCapture(
                evidenceId,
                out packetTemplate,
                out route,
                out error,
                out captureDiagnostics);
        }

        public static bool TryCreateFromCurrentCapture(
            string evidenceId,
            out MountRefineA050PacketTemplate packetTemplate,
            out MountRefineA050RouteTemplate route,
            out string error,
            out string captureDiagnostics)
        {
            packetTemplate = null;
            route = null;
            error = string.Empty;
            captureDiagnostics = string.Empty;
            List<Socket_PacketInfo> capturedPackets = new List<Socket_PacketInfo>();

            Action capture = () =>
            {
                capturedPackets = Socket_Cache.SocketList.lstRecPacket
                    .Where(item => item != null)
                    .ToList();
            };

            try
            {
                if (Socket_Cache.System.InvokeAction != null)
                {
                    Socket_Cache.System.InvokeAction(capture);
                }
                else
                {
                    capture();
                }
            }
            catch (Exception exception)
            {
                error = "mount_refine_a050_capture_read_failed: " + exception.Message;
                return false;
            }

            int visiblePacketCount = capturedPackets.Count;
            List<Socket_PacketInfo> sessionEvidence =
                Socket_Cache.SocketList.CaptureMountRefineA050Evidence();
            capturedPackets.AddRange(sessionEvidence);
            captureDiagnostics = string.Format(
                "visible={0}; evidence={1}; queued={2}; speedMode={3}; totalPackets={4}; sendBytes={5}",
                visiblePacketCount,
                sessionEvidence.Count,
                Socket_Cache.SocketQueue.qSocket_PacketInfo.Count,
                Socket_Cache.SocketPacket.SpeedMode,
                Socket_Cache.SocketPacket.TotalPackets,
                Socket_Cache.SocketPacket.Total_SendBytes);

            string scanDiagnostics;
            bool bound = TryCreate(
                capturedPackets,
                evidenceId,
                out packetTemplate,
                out route,
                out error,
                out scanDiagnostics);
            if (!string.IsNullOrWhiteSpace(scanDiagnostics))
            {
                captureDiagnostics += "; " + scanDiagnostics;
            }

            return bound;
        }

        private static string GetA050PrefilterRejectionCode(Socket_PacketInfo packet)
        {
            if (packet == null)
            {
                return "capture_packet_null";
            }

            byte[] packetBuffer = packet.PacketBuffer;
            if (packetBuffer == null || packetBuffer.Length == 0)
            {
                return "capture_packet_buffer_missing";
            }

            if (packetBuffer.Length < 12)
            {
                return packetBuffer.Length < 2 ||
                    packetBuffer[0] != 0x4D ||
                    packetBuffer[1] != 0x5A
                    ? "a050_header_not_matched"
                    : "a050_protocol_not_readable";
            }

            if (!LooksLikeA050(packetBuffer))
            {
                return packetBuffer[0] != 0x4D || packetBuffer[1] != 0x5A
                    ? "a050_header_not_matched"
                    : "a050_protocol_not_matched";
            }

            if (!MountRefineA050RouteTemplate.IsSendPacketType(packet.PacketType))
            {
                return "a050_outbound_type_not_matched";
            }

            return string.Empty;
        }

        private static void IncrementRejectionCount(
            IDictionary<string, int> rejectionCounts,
            string rejectionCode)
        {
            if (rejectionCounts == null || string.IsNullOrWhiteSpace(rejectionCode))
            {
                return;
            }

            int count;
            if (!rejectionCounts.TryGetValue(rejectionCode, out count))
            {
                count = 0;
            }
            rejectionCounts[rejectionCode] = count + 1;
        }

        private static string BuildCaptureDiagnostics(
            int totalPacketCount,
            int candidateCount,
            int routeGroupCount,
            IDictionary<string, int> rejectionCounts,
            string terminalError)
        {
            List<string> fields = new List<string>
            {
                string.Format("scanTotal={0}", totalPacketCount),
                string.Format("a050Candidates={0}", candidateCount),
                string.Format("routeGroups={0}", routeGroupCount)
            };

            if (rejectionCounts != null && rejectionCounts.Count > 0)
            {
                string rejectionSummary = string.Join(
                    ",",
                    rejectionCounts
                        .OrderBy(item => item.Key, StringComparer.Ordinal)
                        .Select(item => item.Key + "=" + item.Value));
                fields.Add("reject=" + rejectionSummary);
            }

            if (!string.IsNullOrWhiteSpace(terminalError))
            {
                fields.Add("terminal=" + terminalError);
            }

            return string.Join("; ", fields);
        }

        private static bool LooksLikeA050(byte[] packet)
        {
            return packet != null &&
                packet.Length >= 12 &&
                packet[0] == 0x4D &&
                packet[1] == 0x5A &&
                packet[10] == 0xA0 &&
                packet[11] == 0x50;
        }

        private static string BuildRouteKey(Candidate candidate)
        {
            return string.Format(
                "{0}|{1}|{2}",
                candidate.Packet.PacketSocket,
                (candidate.Route.PacketFrom ?? string.Empty).Trim().ToUpperInvariant(),
                (candidate.Route.PacketTo ?? string.Empty).Trim().ToUpperInvariant());
        }
    }

    /// <summary>
    /// 坐骑炼化 A050 Socket 发送器。
    /// 只有协议显式标记、当前注入方向和真实发送授权同时具备时才会调用 SendPacket。
    /// </summary>
    public sealed class MountRefineA050SocketPacketSender :
        IMountRefinePacketSender,
        IMountRefinePacketSenderDiagnostics
    {
        private readonly MountRefineA050PacketTemplate _packetTemplate;
        private readonly MountRefineA050RouteTemplate _routeTemplate;
        private readonly MountRefineLiveSendAuthorization _authorization;
        private readonly bool _protocolVerified;

        public MountRefineA050SocketPacketSender(
            MountRefineA050PacketTemplate packetTemplate,
            MountRefineA050RouteTemplate routeTemplate,
            MountRefineLiveSendAuthorization authorization,
            bool protocolVerified)
        {
            _packetTemplate = packetTemplate;
            _routeTemplate = routeTemplate;
            _authorization = authorization;
            _protocolVerified = protocolVerified;
        }

        public bool IsAuthorized
        {
            get { return _authorization != null; }
        }

        public bool IsProtocolVerified
        {
            get { return _protocolVerified && _packetTemplate != null && _packetTemplate.IsValid; }
        }

        public string LastFailureCode { get; private set; } = string.Empty;

        public string LastFailureMessage { get; private set; } = string.Empty;

        public int LastSocket { get; private set; }

        public int LastBytesSent { get; private set; }

        public int LastSocketErrorCode { get; private set; }

        public async Task<MountRefineSendResult> SendAsync(
            MountRefineSendRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetDiagnostics();
            await Task.CompletedTask.ConfigureAwait(false);

            if (_authorization == null)
            {
                return Fail(MountRefineSendResult.AuthorizationRequired, "mount_refine_live_send_not_authorized", "未提供坐骑炼化真实发送授权。");
            }

            if (!IsProtocolVerified)
            {
                return Fail(MountRefineSendResult.ProtocolUnverified, "mount_refine_protocol_unverified", "坐骑炼化 A050 模板尚未完成协议确认。");
            }

            if (_routeTemplate == null)
            {
                return Fail(MountRefineSendResult.Failed, "mount_refine_route_template_missing", "未捕获到坐骑炼化 A050 的出站方向。");
            }

            byte[] packet;
            string buildError;
            if (!_packetTemplate.TryBuild(request, out packet, out buildError))
            {
                return Fail(MountRefineSendResult.InvalidRequest, buildError, "坐骑炼化 A050 请求构造失败：" + buildError);
            }

            // Manual sending uses the selected row's original Socket, packet
            // type and endpoints. Re-resolving by packet type/destination can
            // silently select a different live connection when several client
            // sockets share the same server endpoint.
            if (_routeTemplate.PacketSocket <= 0)
            {
                return Fail(
                    MountRefineSendResult.Failed,
                    "mount_refine_route_socket_missing",
                    "当前坐骑炼化出站包没有可用的原始 Socket。");
            }

            LastSocket = _routeTemplate.PacketSocket;
            int bytesSent;
            int socketErrorCode;
            bool sent = Socket_Operation.SendPacket(
                _routeTemplate.PacketSocket,
                _routeTemplate.PacketType,
                _routeTemplate.PacketFrom,
                _routeTemplate.PacketTo,
                (byte[])packet.Clone(),
                out bytesSent,
                out socketErrorCode);
            LastBytesSent = bytesSent;
            LastSocketErrorCode = socketErrorCode;
            if (!sent)
            {
                return Fail(
                    MountRefineSendResult.Failed,
                    "mount_refine_socket_send_failed",
                    string.Format(
                        "坐骑炼化 Socket 发送失败，bytesSent={0}, socketError={1}。",
                        bytesSent,
                        socketErrorCode));
            }

            return MountRefineSendResult.Accepted;
        }

        private MountRefineSendResult Fail(
            MountRefineSendResult result,
            string code,
            string message)
        {
            LastFailureCode = code ?? string.Empty;
            LastFailureMessage = message ?? string.Empty;
            return result;
        }

        private void ResetDiagnostics()
        {
            LastFailureCode = string.Empty;
            LastFailureMessage = string.Empty;
            LastSocket = 0;
            LastBytesSent = 0;
            LastSocketErrorCode = 0;
        }
    }
}
