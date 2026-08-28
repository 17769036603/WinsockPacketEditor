using System;
using System.Collections.Generic;
using System.Linq;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 五行升级封包运行时辅助类。
    /// 负责从捕获列表中发现合法的 C2S_UseCsCard 模板，并提供模板缓存。
    /// </summary>
    public static class WuxingPacketRuntime
    {
        /// <summary>
        /// 当前会话已发现的 C2S_UseCsCard 模板（按 Socket + PacketType + PacketTo 分组）。
        /// </summary>
        private static readonly Dictionary<string, WuxingUpgradeTemplate> _templates
            = new Dictionary<string, WuxingUpgradeTemplate>();

        /// <summary>
        /// 当前会话版本（每次 BeginSession 递增）。
        /// </summary>
        private static long _sessionVersion = 0;

        /// <summary>
        /// 开始新的捕获会话，清空旧模板。
        /// </summary>
        public static void BeginSession()
        {
            _sessionVersion++;
            _templates.Clear();
        }

        /// <summary>
        /// 获取当前会话版本。
        /// </summary>
        public static long GetSessionVersion()
        {
            return _sessionVersion;
        }

        /// <summary>
        /// 结束当前会话，清空模板。
        /// </summary>
        public static void EndSession()
        {
            _templates.Clear();
        }

        /// <summary>
        /// 观察新捕获的封包，若为 C2S_UseCsCard 则尝试注册为模板。
        /// </summary>
        public static void ObserveCapturedPacket(Socket_PacketInfo packet)
        {
            if (packet == null || packet.PacketBuffer == null || packet.PacketBuffer.Length == 0)
            {
                return;
            }

            if (!IsSendPacketType(packet.PacketType))
            {
                return;
            }

            if (IsWuxingUpgradeTemplate(packet.PacketBuffer))
            {
                var key = BuildTemplateKey(packet);
                _templates[key] = new WuxingUpgradeTemplate(
                    packet.PacketType,
                    packet.PacketFrom,
                    packet.PacketTo,
                    packet.PacketBuffer);
            }
        }

        /// <summary>
        /// 批量观察捕获的封包。
        /// </summary>
        public static void ObserveCapturedPackets(IEnumerable<Socket_PacketInfo> capturedPackets)
        {
            if (capturedPackets == null)
            {
                return;
            }

            foreach (var packet in capturedPackets)
            {
                ObserveCapturedPacket(packet);
            }
        }

        /// <summary>
        /// 从当前捕获列表中发现 C2S_UseCsCard 模板。
        /// </summary>
        public static WuxingUpgradeTemplate DiscoverTemplate(IEnumerable<Socket_PacketInfo> capturedPackets)
        {
            if (capturedPackets == null)
            {
                throw new ArgumentNullException(nameof(capturedPackets));
            }

            WuxingUpgradeTemplate template = null;
            foreach (Socket_PacketInfo packet in capturedPackets)
            {
                if (packet == null || packet.PacketBuffer == null ||
                    packet.PacketBuffer.Length == 0 || !IsSendPacketType(packet.PacketType))
                {
                    continue;
                }

                if (IsWuxingUpgradeTemplate(packet.PacketBuffer))
                {
                    template = new WuxingUpgradeTemplate(
                        packet.PacketType,
                        packet.PacketFrom,
                        packet.PacketTo,
                        packet.PacketBuffer);
                    break;
                }
            }

            if (template == null)
            {
                throw new WuxingPacketRuntimeException(
                    "wuxing_template_not_found",
                    "No current outgoing C2S_UseCsCard (0x00FA) template was found in the capture list.");
            }

            return template;
        }

        /// <summary>
        /// 尝试从缓存中获取指定路由的模板。
        /// </summary>
        public static bool TryGetCachedTemplate(
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetFrom,
            string packetTo,
            out WuxingUpgradeTemplate template)
        {
            var key = BuildTemplateKey(packetType, packetFrom, packetTo);
            return _templates.TryGetValue(key, out template);
        }

        /// <summary>
        /// 获取当前会话的模板快照（用于诊断）。
        /// </summary>
        public static IReadOnlyCollection<WuxingUpgradeTemplate> GetSessionTemplates()
        {
            return _templates.Values.ToList().AsReadOnly();
        }

        /// <summary>
        /// 判断封包类型是否为发送类型（客户端 → 服务端）。
        /// </summary>
        private static bool IsSendPacketType(Socket_Cache.SocketPacket.PacketType packetType)
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

        /// <summary>
        /// 判断封包缓冲区是否为合法的 C2S_UseCsCard 模板。
        /// </summary>
        private static bool IsWuxingUpgradeTemplate(byte[] buffer)
        {
            if (buffer == null || buffer.Length < WuxingPacketTemplatePatcher.MinimumFrameLength)
            {
                return false;
            }

            // 快速判断协议号
            if (buffer.Length < 12)
            {
                return false;
            }

            ushort protocolId = (ushort)((buffer[10] << 8) | buffer[11]);
            return protocolId == WuxingPacketTemplatePatcher.ProtocolId;
        }

        /// <summary>
        /// 构建模板缓存键。
        /// </summary>
        private static string BuildTemplateKey(Socket_PacketInfo packet)
        {
            return BuildTemplateKey(packet.PacketType, packet.PacketFrom, packet.PacketTo);
        }

        /// <summary>
        /// 构建模板缓存键。
        /// </summary>
        private static string BuildTemplateKey(
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetFrom,
            string packetTo)
        {
            return $"{packetType}:{packetFrom ?? string.Empty}:{packetTo ?? string.Empty}";
        }
    }

    /// <summary>
    /// 五行升级封包模板。
    /// </summary>
    public sealed class WuxingUpgradeTemplate
    {
        public WuxingUpgradeTemplate(
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetFrom,
            string packetTo,
            byte[] packetBuffer)
        {
            this.PacketType = packetType;
            this.PacketFrom = packetFrom ?? string.Empty;
            this.PacketTo = packetTo ?? string.Empty;
            this.PacketBuffer = packetBuffer ?? throw new ArgumentNullException(nameof(packetBuffer));
        }

        public Socket_Cache.SocketPacket.PacketType PacketType { get; private set; }

        public string PacketFrom { get; private set; }

        public string PacketTo { get; private set; }

        public byte[] PacketBuffer { get; private set; }
    }

    /// <summary>
    /// 五行升级运行时异常。
    /// </summary>
    public sealed class WuxingPacketRuntimeException : InvalidOperationException
    {
        public WuxingPacketRuntimeException(string code, string message)
            : base(message)
        {
            this.Code = code ?? string.Empty;
        }

        public string Code { get; private set; }
    }
}
