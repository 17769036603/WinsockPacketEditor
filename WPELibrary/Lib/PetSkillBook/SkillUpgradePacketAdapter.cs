using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 法术升级操作适配器接口
    /// 负责提交法术升级的游戏操作
    /// </summary>
    public interface ISkillUpgradeActionAdapter
    {
        /// <summary>
        /// 提交法术升级请求
        /// </summary>
        /// <param name="skillId">法术 ID</param>
        /// <param name="learnLevel">目标等级</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>操作结果</returns>
        Task<SkillUpgradeResult> SubmitLearnSkillAsync(int skillId, int learnLevel, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 法术升级操作适配器，基于捕获的 C2S_LearnSkill (0x2074) 封包模板发送法术升级请求。
    /// 模板来源：用户在捕获列表中已有的法术升级封包，或通过 <see cref="SkillUpgradePacketRuntime"/>
    /// 预先发现的模板。roleId 始终沿用模板中的真实值，不硬编码。
    /// </summary>
    public sealed class SkillUpgradePacketAdapter : ISkillUpgradeActionAdapter
    {
        private readonly SkillUpgradeTemplate _template;
        private readonly int _currentSocket;

        /// <summary>
        /// 创建适配器实例。模板和 socket 将在首次发送时自动发现。
        /// </summary>
        public SkillUpgradePacketAdapter()
            : this(null, 0)
        {
        }

        /// <summary>
        /// 创建适配器实例，使用预发现的模板和 socket。
        /// </summary>
        public SkillUpgradePacketAdapter(SkillUpgradeTemplate template, int currentSocket)
        {
            _template = template;
            _currentSocket = currentSocket > 0 ? currentSocket : 0;
        }

        /// <inheritdoc />
        public Task<SkillUpgradeResult> SubmitLearnSkillAsync(
            int skillId,
            int learnLevel,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. 获取模板：优先使用预配置模板，否则从捕获列表中发现
            SkillUpgradeTemplate template = _template;
            if (template == null)
            {
                template = DiscoverTemplateFromCapture();
                if (template == null)
                {
                    return Task.FromResult(SkillUpgradeResult.Unavailable);
                }
            }

            // 2. 解析当前 socket
            int socket = _currentSocket;
            if (socket <= 0)
            {
                socket = ResolveCurrentSocket(template);
                if (socket <= 0)
                {
                    return Task.FromResult(SkillUpgradeResult.Unavailable);
                }
            }

            // 3. 基于模板生成升级帧：只替换 skillId 与 learnLevel，
            //    roleId 沿用模板中的真实值
            byte[] frame;
            try
            {
                frame = SkillUpgradePacketTemplatePatcher.BuildLearnSkillFrame(
                    template.PacketBuffer,
                    skillId,
                    learnLevel);
            }
            catch (SkillUpgradePacketTemplateException)
            {
                return Task.FromResult(SkillUpgradeResult.Unavailable);
            }

            if (frame == null || frame.Length == 0)
            {
                return Task.FromResult(SkillUpgradeResult.Unavailable);
            }

            // 发送边界再次解码校验，防止半更新帧进入网络
            int checkRoleId;
            int checkSkillId;
            int checkLearnLevel;
            if (!SkillUpgradePacketEncoder.TryDecodeFrame(frame, out checkRoleId, out checkSkillId, out checkLearnLevel) ||
                checkSkillId != skillId ||
                checkLearnLevel != learnLevel)
            {
                return Task.FromResult(SkillUpgradeResult.Unavailable);
            }

            // 4. 发送封包
            bool sent = Socket_Operation.SendPacket(
                socket,
                template.PacketType,
                template.PacketFrom,
                template.PacketTo,
                frame);

            return sent
                ? Task.FromResult(SkillUpgradeResult.Accepted)
                : Task.FromResult(SkillUpgradeResult.Unknown);
        }

        /// <summary>
        /// 从当前捕获列表中发现 C2S_LearnSkill 模板。
        /// </summary>
        private static SkillUpgradeTemplate DiscoverTemplateFromCapture()
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

                return SkillUpgradePacketRuntime.DiscoverTemplate(capturedPackets);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析当前游戏 socket。
        /// </summary>
        private static int ResolveCurrentSocket(SkillUpgradeTemplate template)
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
