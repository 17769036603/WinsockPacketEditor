using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 五行升级操作适配器，基于捕获的 C2S_UseCsCard 封包模板发送五行升级请求。
    /// 模板来源：用户在捕获列表中已有的五行升级封包，或通过 <see cref="WuxingPacketRuntime"/>
    /// 预先发现的模板。
    /// </summary>
    public sealed class WuxingUpgradePacketAdapter : IWuxingUpgradeActionAdapter
    {
        private readonly WuxingUpgradeTemplate _template;
        private readonly int _currentSocket;

        /// <summary>
        /// 创建适配器实例。模板和 socket 将在首次发送时自动发现。
        /// </summary>
        public WuxingUpgradePacketAdapter()
            : this(null, 0)
        {
        }

        /// <summary>
        /// 创建适配器实例，使用预发现的模板和 socket。
        /// </summary>
        public WuxingUpgradePacketAdapter(WuxingUpgradeTemplate template, int currentSocket)
        {
            _template = template;
            _currentSocket = currentSocket > 0 ? currentSocket : 0;
        }

        /// <inheritdoc />
        public Task<WuxingUpgradeResult> SubmitElementUpgradeAsync(
            WuxingUpgradeOperation operation,
            CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 1. 获取模板：优先使用预配置模板，否则从捕获列表中发现
            WuxingUpgradeTemplate template = _template;
            if (template == null)
            {
                template = DiscoverTemplateFromCapture();
                if (template == null)
                {
                    return Task.FromResult(WuxingUpgradeResult.Unavailable);
                }
            }

            // 2. 解析当前 socket
            int socket = _currentSocket;
            if (socket <= 0)
            {
                socket = ResolveCurrentSocket(template);
                if (socket <= 0)
                {
                    return Task.FromResult(WuxingUpgradeResult.Unavailable);
                }
            }

            // 3. 基于模板生成升级帧：只替换 cardId，保留封包其余部分
            byte[] frame;
            try
            {
                frame = WuxingPacketTemplatePatcher.BuildUpgradeFrame(
                    template.PacketBuffer,
                    operation.CardId);
            }
            catch (WuxingPacketTemplateException)
            {
                return Task.FromResult(WuxingUpgradeResult.Unavailable);
            }

            if (frame == null || frame.Length == 0)
            {
                return Task.FromResult(WuxingUpgradeResult.Unavailable);
            }

            // 4. 发送封包
            bool sent = Socket_Operation.SendPacket(
                socket,
                template.PacketType,
                template.PacketFrom,
                template.PacketTo,
                frame);

            return sent
                ? Task.FromResult(WuxingUpgradeResult.Accepted)
                : Task.FromResult(WuxingUpgradeResult.Unknown);
        }

        /// <summary>
        /// 从当前捕获列表中发现 C2S_UseCsCard 模板。
        /// </summary>
        private static WuxingUpgradeTemplate DiscoverTemplateFromCapture()
        {
            try
            {
                List<Socket_PacketInfo> capturedPackets = new List<Socket_PacketInfo>();
                Action capture = () =>
                {
                    capturedPackets = Socket_Cache.SocketList.lstRecPacket
                        .Where(item => item != null &&
                                       item.PacketBuffer != null &&
                                       item.PacketBuffer.Length > 0)
                        .ToList();
                };

                if (Socket_Cache.System.InvokeAction != null)
                {
                    Socket_Cache.System.InvokeAction(capture);
                }
                else
                {
                    capture();
                }

                return WuxingPacketRuntime.DiscoverTemplate(capturedPackets);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析当前游戏 socket。
        /// </summary>
        private static int ResolveCurrentSocket(WuxingUpgradeTemplate template)
        {
            try
            {
                var templatePacket = new Socket_PacketInfo
                {
                    PacketType = template.PacketType,
                    PacketTo = template.PacketTo ?? string.Empty
                };

                return Socket_Cache.SocketList.ResolveCurrentSocket(new[] { templatePacket });
            }
            catch
            {
                return 0;
            }
        }
    }
}
