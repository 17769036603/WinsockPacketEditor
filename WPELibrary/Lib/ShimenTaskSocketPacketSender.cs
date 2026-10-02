using System;
using System.Threading;

namespace WPELibrary.Lib
{
    /// <summary>
    /// 师门任务真实发送的本次运行授权。不会写入预设或数据库。
    /// </summary>
    public sealed class ShimenTaskLiveSendAuthorization
    {
        private ShimenTaskLiveSendAuthorization()
        {
        }

        /// <summary>
        /// 由用户点击师门任务启动入口时创建一次性授权。
        /// 授权只存在本次运行参数中，不写入预设或数据库。
        /// </summary>
        public static ShimenTaskLiveSendAuthorization CreateForInteractiveStart()
        {
            return new ShimenTaskLiveSendAuthorization();
        }
    }

    /// <summary>
    /// 从当前选中的出站封包提取路由解析模板。只保存协议方向和地址，
    /// 不保存旧 Socket；发送时必须重新解析当前注入会话的路由。
    /// </summary>
    public sealed class ShimenTaskSocketRouteTemplate
    {
        private ShimenTaskSocketRouteTemplate(
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
            out ShimenTaskSocketRouteTemplate route,
            out string error)
        {
            route = null;
            error = string.Empty;
            if (capturedPacket == null || capturedPacket.PacketSocket <= 0)
            {
                error = "shimen_route_capture_missing";
                return false;
            }

            if (!IsSendPacketType(capturedPacket.PacketType))
            {
                error = "shimen_route_packet_direction_invalid";
                return false;
            }

            string packetTo = (capturedPacket.PacketTo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(packetTo))
            {
                error = "shimen_route_destination_missing";
                return false;
            }

            route = new ShimenTaskSocketRouteTemplate(
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
    /// 师门任务四类动作的 Socket 发送器。
    /// 每次发送前都重新解析当前连接；模板、方向、当前路由或授权任一不满足时零发送。
    /// </summary>
    public sealed class ShimenTaskSocketPacketSender : IShimenTaskPacketSender
    {
        private readonly ShimenTaskPacketTemplateSlots packetSlots;
        private readonly ShimenTaskSocketRouteTemplate routeTemplate;
        private readonly ShimenTaskLiveSendAuthorization authorization;
        private readonly Guid presetId;

        public ShimenTaskSocketPacketSender(
            ShimenTaskPacketTemplateSlots packetSlots,
            ShimenTaskSocketRouteTemplate routeTemplate,
            ShimenTaskLiveSendAuthorization authorization,
            Guid presetId)
        {
            this.packetSlots = packetSlots;
            this.routeTemplate = routeTemplate;
            this.authorization = authorization;
            this.presetId = presetId;
        }

        public bool IsConfigured
        {
            get
            {
                return this.authorization != null &&
                    this.routeTemplate != null &&
                    this.packetSlots != null &&
                    this.packetSlots.IsComplete;
            }
        }

        public string LastFailureCode { get; private set; } = string.Empty;

        public string LastFailureMessage { get; private set; } = string.Empty;

        public int LastSocket { get; private set; }

        public int LastBytesSent { get; private set; }

        public int LastSocketErrorCode { get; private set; }

        public ShimenTaskPacketSendResult Send(
            ShimenTaskPacketAction action,
            ShimenTaskMemoryTask task,
            ShimenTaskPresetEntry presetEntry,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ResetDiagnostics();

            if (this.authorization == null)
            {
                return this.Fail(
                    "shimen_live_send_not_authorized",
                    "未提供师门任务本次真实发送授权。");
            }

            if (this.packetSlots == null || !this.packetSlots.IsComplete)
            {
                return this.Fail(
                    "shimen_packet_template_invalid",
                    "师门任务封包模板不完整或结构校验失败。");
            }

            if (this.routeTemplate == null)
            {
                return this.Fail(
                    "shimen_route_template_missing",
                    "未提供师门任务当前连接解析模板。");
            }

            if ((action == ShimenTaskPacketAction.Purchase ||
                 action == ShimenTaskPacketAction.Submit) &&
                (task == null ||
                 presetEntry == null ||
                 task.TaskId != presetEntry.TaskId))
            {
                return this.Fail(
                    "shimen_task_binding_invalid",
                    "师门任务内存记录与采购目录不一致，已拒绝发送。");
            }

            if (task == null)
            {
                return this.Fail(
                    "shimen_task_missing",
                    "师门任务动作缺少当前任务记录。");
            }

            byte[] packet;
            string packetError;
            if (!this.packetSlots.TryGetPacket(
                    action,
                    task.TaskId,
                    out packet,
                    out packetError))
            {
                return this.Fail(
                    string.IsNullOrWhiteSpace(packetError)
                        ? "shimen_packet_invalid"
                        : packetError,
                    "师门任务封包校验失败。");
            }

            Socket_Cache.SocketList.CurrentSocketRouteResolution resolution;
            try
            {
                Socket_PacketInfo resolverTemplate = this.routeTemplate.ToResolverTemplate();
                resolution = Socket_Cache.SocketList.ResolveCurrentRoute(
                    resolverTemplate);
                Socket_Cache.SocketList.LogCurrentRouteResolution(
                    this.presetId,
                    (int)action,
                    resolverTemplate,
                    resolution);
            }
            catch (Exception ex)
            {
                return this.Fail("shimen_route_resolution_failed", ex.Message);
            }

            if (resolution == null || !resolution.Succeeded || resolution.Route == null)
            {
                string errorCode = resolution == null ||
                    string.IsNullOrWhiteSpace(resolution.ErrorCode)
                    ? "shimen_current_route_not_found"
                    : resolution.ErrorCode;
                string message = resolution == null
                    ? "师门任务当前连接解析没有结果。"
                    : resolution.ErrorMessage;
                return this.Fail(errorCode, message);
            }

            if (resolution.Route.Socket <= 0)
            {
                return this.Fail(
                    "shimen_current_socket_invalid",
                    "师门任务当前连接没有可用 Socket。");
            }

            this.LastSocket = resolution.Route.Socket;
            int bytesSent;
            int socketErrorCode;
            bool sent = Socket_Operation.SendPacket(
                resolution.Route.Socket,
                resolution.Route.PacketType,
                resolution.Route.PacketFrom,
                resolution.Route.PacketTo,
                (byte[])packet.Clone(),
                out bytesSent,
                out socketErrorCode);
            this.LastBytesSent = bytesSent;
            this.LastSocketErrorCode = socketErrorCode;
            if (!sent || bytesSent != packet.Length)
            {
                return this.Fail(
                    "shimen_socket_send_failed",
                    string.Format(
                        "师门任务 Socket 发送失败，bytesSent={0}, expected={1}, socketError={2}。",
                        bytesSent,
                        packet.Length,
                        socketErrorCode));
            }

            Socket_Operation.DoLog(
                "ShimenTaskSend",
                string.Format(
                    "presetId={0};action={1};taskId={2};bytesSent={3};socket={4}",
                    this.presetId == Guid.Empty ? "unknown" : this.presetId.ToString("D"),
                    action,
                    task.TaskId,
                    bytesSent,
                    this.LastSocket));
            this.LastFailureCode = string.Empty;
            this.LastFailureMessage = string.Empty;
            return new ShimenTaskPacketSendResult
            {
                Succeeded = true,
                BytesSent = true,
                SentByteCount = bytesSent
            };
        }

        private ShimenTaskPacketSendResult Fail(string code, string message)
        {
            this.LastFailureCode = code ?? string.Empty;
            this.LastFailureMessage = message ?? string.Empty;
            return new ShimenTaskPacketSendResult
            {
                Succeeded = false,
                BytesSent = false,
                SentByteCount = 0,
                Error = string.IsNullOrWhiteSpace(code)
                    ? message ?? string.Empty
                    : code + ": " + (message ?? string.Empty)
            };
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
