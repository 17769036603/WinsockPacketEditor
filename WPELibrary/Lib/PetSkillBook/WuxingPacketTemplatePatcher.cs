using System;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// C2S_UseCsCard 模板校验与字段修补器。
    /// 与藏宝图 <c>TreasurePacketTemplatePatcher</c> 同级：只替换允许变化的字段，
    /// 保留用户捕获的真实封包头部和静态段。
    /// 已知协议号 0x00FA，字段 <c>id:string; cardId:int</c>。
    /// </summary>
    public static class WuxingPacketTemplatePatcher
    {
        /// <summary>
        /// C2S_UseCsCard 协议号（网络序）
        /// </summary>
        public const ushort ProtocolId = 0x00FA;

        /// <summary>
        /// 外层长度字段在封包内的字节偏移（U16 big-endian）。
        /// 当前未完全审计的游戏帧中，外层长度从第 8 字节起。
        /// </summary>
        public const int OuterLengthOffset = 8;

        /// <summary>
        /// 协议号字段在封包内的字节偏移（U16 big-endian）。
        /// </summary>
        public const int ProtocolOffset = 10;

        /// <summary>
        /// 玩家/账号 ID 字段在封包内的字节偏移（string）。
        /// 该字段由服务端维护，客户端发送时重复自身当前值，运行时不得覆盖。
        /// </summary>
        public const int PlayerIdOffset = 12;

        /// <summary>
        /// 卡片 ID 字段在封包内的字节偏移（U32 big-endian）。
        /// 五行升级运行时只替换此字段。
        /// </summary>
        public const int CardIdOffset = 20;

        /// <summary>
        /// 当前已审计的最小 C2S_UseCsCard 帧长度。
        /// </summary>
        public const int MinimumFrameLength = 24;

        /// <summary>
        /// 固定帧头（10 字节），用于模板合法性校验。
        /// </summary>
        private static readonly byte[] FramePrefix =
        {
            0x4D, 0x5A,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0xFA
        };

        /// <summary>
        /// 校验捕获的模板封包是否为合法 C2S_UseCsCard 帧。
        /// </summary>
        public static void ValidateTemplate(byte[] template)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            if (template.Length < MinimumFrameLength)
            {
                throw new WuxingPacketTemplateException(
                    "frame_length_invalid",
                    "C2S_UseCsCard template is shorter than the audited contract.");
            }

            for (int i = 0; i < FramePrefix.Length; i++)
            {
                if (template[i] != FramePrefix[i])
                {
                    throw new WuxingPacketTemplateException(
                        "frame_prefix_invalid",
                        "C2S_UseCsCard template prefix or protocol ID does not match.");
                }
            }

            if (ReadUInt16BigEndian(template, ProtocolOffset) != ProtocolId)
            {
                throw new WuxingPacketTemplateException(
                    "protocol_id_invalid",
                    "C2S_UseCsCard protocol ID mismatch.");
            }

            if (ReadUInt16BigEndian(template, OuterLengthOffset) != template.Length - 10)
            {
                throw new WuxingPacketTemplateException(
                    "outer_length_invalid",
                    "C2S_UseCsCard outer length does not match frame boundary.");
            }
        }

        /// <summary>
        /// 基于合法模板生成五行升级请求帧。
        /// 只替换 <c>cardId</c> 字段，其余全部保留。
        /// </summary>
        public static byte[] BuildUpgradeFrame(byte[] template, int cardId)
        {
            ValidateTemplate(template);
            byte[] result = (byte[])template.Clone();
            WriteInt32BigEndian(result, CardIdOffset, cardId);
            return result;
        }

        private static ushort ReadUInt16BigEndian(byte[] buffer, int offset)
        {
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
        {
            uint unsignedValue = unchecked((uint)value);
            buffer[offset] = (byte)(unsignedValue >> 24);
            buffer[offset + 1] = (byte)(unsignedValue >> 16);
            buffer[offset + 2] = (byte)(unsignedValue >> 8);
            buffer[offset + 3] = (byte)unsignedValue;
        }
    }

    /// <summary>
    /// 五行升级封包模板异常。
    /// </summary>
    public sealed class WuxingPacketTemplateException : InvalidOperationException
    {
        public WuxingPacketTemplateException(string code, string message)
            : base(message)
        {
            this.Code = code ?? string.Empty;
        }

        public string Code { get; private set; }
    }
}