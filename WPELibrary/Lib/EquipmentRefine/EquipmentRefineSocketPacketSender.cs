using System;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 装备炼化真实发送的显式授权令牌。
    /// 不保存到预设，也不会由助手自动创建。
    /// </summary>
    public sealed class EquipmentRefineLiveSendAuthorization
    {
        public const string RequiredConfirmationText = "EQUIPMENT-REFINE-LIVE-SEND";

        private EquipmentRefineLiveSendAuthorization()
        {
        }

        public static EquipmentRefineLiveSendAuthorization Create(string confirmation)
        {
            if (!string.Equals(
                    confirmation,
                    RequiredConfirmationText,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "装备炼化真实发送需要显式确认文本。",
                    "confirmation");
            }

            return new EquipmentRefineLiveSendAuthorization();
        }
    }

    /// <summary>
    /// 只保存发送方向和地址作为当前连接解析模板，不保存或复用旧 Socket。
    /// </summary>
    public sealed class EquipmentRefineSocketRouteTemplate
    {
        private EquipmentRefineSocketRouteTemplate(
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetFrom,
            string packetTo)
        {
            this.PacketType = packetType;
            this.PacketFrom = packetFrom ?? string.Empty;
            this.PacketTo = packetTo ?? string.Empty;
        }

        public Socket_Cache.SocketPacket.PacketType PacketType { get; private set; }

        public string PacketFrom { get; private set; }

        public string PacketTo { get; private set; }

        public static bool TryCreate(
            Socket_PacketInfo capturedPacket,
            out EquipmentRefineSocketRouteTemplate route,
            out string error)
        {
            route = null;
            error = string.Empty;
            if (capturedPacket == null || capturedPacket.PacketSocket <= 0)
            {
                error = "route_capture_missing";
                return false;
            }

            if (!IsSendPacketType(capturedPacket.PacketType))
            {
                error = "route_packet_direction_invalid";
                return false;
            }

            string packetTo = (capturedPacket.PacketTo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(packetTo))
            {
                error = "route_destination_missing";
                return false;
            }

            route = new EquipmentRefineSocketRouteTemplate(
                capturedPacket.PacketType,
                (capturedPacket.PacketFrom ?? string.Empty).Trim(),
                packetTo);
            return true;
        }

        internal Socket_PacketInfo ToResolverTemplate()
        {
            return new Socket_PacketInfo
            {
                PacketType = this.PacketType,
                PacketFrom = this.PacketFrom,
                PacketTo = this.PacketTo
            };
        }

        private static bool IsSendPacketType(
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
    /// 装备炼化 Socket 发送适配器。
    /// 每次发送前重新解析当前连接；授权、方向、地址或连接存在歧义时零发送。
    /// </summary>
    public sealed class EquipmentRefineSocketPacketSender : EquipmentRefineExecutor.IRefinePacketSender
    {
        private readonly EquipmentRefineSocketRouteTemplate _routeTemplate;
        private readonly EquipmentRefineLiveSendAuthorization _authorization;

        public EquipmentRefineSocketPacketSender(
            EquipmentRefineSocketRouteTemplate routeTemplate,
            EquipmentRefineLiveSendAuthorization authorization)
        {
            this._routeTemplate = routeTemplate;
            this._authorization = authorization;
        }

        public bool IsAuthorized
        {
            get { return this._authorization != null; }
        }

        public string LastFailureCode { get; private set; } = string.Empty;

        public string LastFailureMessage { get; private set; } = string.Empty;

        public int LastSocket { get; private set; }

        public int LastBytesSent { get; private set; }

        public int LastSocketErrorCode { get; private set; }

        public Task<bool> SendAsync(byte[] packet, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ResetDiagnostics();

            if (this._authorization == null)
            {
                return Task.FromResult(this.Fail("live_send_not_authorized", "未提供装备炼化真实发送授权。"));
            }

            if (this._routeTemplate == null)
            {
                return Task.FromResult(this.Fail("route_template_missing", "未提供装备炼化当前连接解析模板。"));
            }

            if (packet == null || packet.Length == 0)
            {
                return Task.FromResult(this.Fail("packet_empty", "待发送炼化封包为空。"));
            }

            Socket_Cache.SocketList.CurrentSocketRouteResolution resolution;
            try
            {
                resolution = Socket_Cache.SocketList.ResolveCurrentRoute(
                    this._routeTemplate.ToResolverTemplate());
            }
            catch (Exception ex)
            {
                return Task.FromResult(this.Fail("route_resolution_failed", ex.Message));
            }

            if (resolution == null || !resolution.Succeeded || resolution.Route == null)
            {
                string errorCode = resolution == null || string.IsNullOrWhiteSpace(resolution.ErrorCode)
                    ? "current_route_not_found"
                    : resolution.ErrorCode;
                string message = resolution == null
                    ? "当前炼化连接解析没有结果。"
                    : resolution.ErrorMessage;
                return Task.FromResult(this.Fail(errorCode, message));
            }

            this.LastSocket = resolution.Route.Socket;
            bool sent = Socket_Operation.SendPacket(
                resolution.Route.Socket,
                resolution.Route.PacketType,
                resolution.Route.PacketFrom,
                resolution.Route.PacketTo,
                (byte[])packet.Clone(),
                out int bytesSent,
                out int socketErrorCode);
            this.LastBytesSent = bytesSent;
            this.LastSocketErrorCode = socketErrorCode;
            if (!sent)
            {
                return Task.FromResult(this.Fail(
                    "socket_send_failed",
                    string.Format(
                        "Socket 发送失败，bytesSent={0}, socketError={1}。",
                        bytesSent,
                        socketErrorCode)));
            }

            this.LastFailureCode = string.Empty;
            this.LastFailureMessage = string.Empty;
            return Task.FromResult(true);
        }

        private bool Fail(string code, string message)
        {
            this.LastFailureCode = code ?? string.Empty;
            this.LastFailureMessage = message ?? string.Empty;
            return false;
        }

        private void ResetDiagnostics()
        {
            this.LastFailureCode = string.Empty;
            this.LastFailureMessage = string.Empty;
            this.LastSocket = 0;
            this.LastBytesSent = 0;
            this.LastSocketErrorCode = 0;
        }
    }
}
