using System;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// C2S_LearnSkill (0x2074) 纯编解码器。
    /// 与藏宝图/五行升级的模板修补器同级：只负责帧的构造、校验与解码，
    /// 不写内存、不调用游戏函数、不接触 Socket。
    ///
    /// 帧布局（大端序，共 25 字节，与抓包样本一致）：
    ///   0-1   4D 5A          魔数 "MZ"
    ///   2-3   00 00          标志位（无加密模式）
    ///   4-9   00 x6          保留位
    ///   10    0E             长度 uint8（14 = 协议号 2 + 包体 12）
    ///   11-12 20 74          协议号 C2S_LearnSkill
    ///   13-16 roleId         int32 大端
    ///   17-20 skillId        int32 大端
    ///   21-24 learnLevel     int32 大端
    ///
    /// 注意：roleId 由服务端分配且动态变化；真实发送路径必须沿用
    /// 捕获模板中的角色 ID（见 SkillUpgradePacketTemplatePatcher），
    /// 本编码器的完整构造入口仅供离线校验与测试使用。
    /// </summary>
    public static class SkillUpgradePacketEncoder
    {
        /// <summary>
        /// C2S_LearnSkill 协议号
        /// </summary>
        public const ushort ProtocolId = 0x2074;

        /// <summary>
        /// 长度字段字节偏移（uint8）
        /// </summary>
        public const int LengthOffset = 10;

        /// <summary>
        /// 协议号字段字节偏移（uint16 大端）
        /// </summary>
        public const int ProtocolOffset = 11;

        /// <summary>
        /// roleId 字段字节偏移（int32 大端）
        /// </summary>
        public const int RoleIdOffset = 13;

        /// <summary>
        /// skillId 字段字节偏移（int32 大端）
        /// </summary>
        public const int SkillIdOffset = 17;

        /// <summary>
        /// learnLevel 字段字节偏移（int32 大端）
        /// </summary>
        public const int LearnLevelOffset = 21;

        /// <summary>
        /// 帧总长度
        /// </summary>
        public const int FrameLength = 25;

        /// <summary>
        /// 包体长度字段固定值：协议号 2 + 3 个 int32 共 12 = 14
        /// </summary>
        public const byte BodyLengthValue = 0x0E;

        /// <summary>
        /// 构造完整的 C2S_LearnSkill 帧。
        /// 仅用于离线校验与测试；真实发送请走模板修补路径。
        /// </summary>
        public static byte[] BuildLearnSkillFrame(int roleId, int skillId, int learnLevel)
        {
            byte[] frame = new byte[FrameLength];

            // 帧头 "MZ" + 无加密标志位 + 保留位（数组已初始化为 0）
            frame[0] = 0x4D;
            frame[1] = 0x5A;
            frame[LengthOffset] = BodyLengthValue;
            frame[ProtocolOffset] = 0x20;
            frame[ProtocolOffset + 1] = 0x74;

            WriteInt32BigEndian(frame, RoleIdOffset, roleId);
            WriteInt32BigEndian(frame, SkillIdOffset, skillId);
            WriteInt32BigEndian(frame, LearnLevelOffset, learnLevel);

            return frame;
        }

        /// <summary>
        /// 校验缓冲区是否为合法 C2S_LearnSkill 帧。
        /// 校验魔数、长度字段、协议号与帧边界，不解包业务字段。
        /// </summary>
        public static bool TryValidateFrame(byte[] buffer)
        {
            if (buffer == null || buffer.Length != FrameLength)
            {
                return false;
            }

            if (buffer[0] != 0x4D || buffer[1] != 0x5A)
            {
                return false;
            }

            if (buffer[LengthOffset] != BodyLengthValue)
            {
                return false;
            }

            if (ReadUInt16BigEndian(buffer, ProtocolOffset) != ProtocolId)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 解码 C2S_LearnSkill 帧的业务字段。
        /// 解码失败时返回 false，不抛异常。
        /// </summary>
        public static bool TryDecodeFrame(
            byte[] buffer,
            out int roleId,
            out int skillId,
            out int learnLevel)
        {
            roleId = 0;
            skillId = 0;
            learnLevel = 0;

            if (!TryValidateFrame(buffer))
            {
                return false;
            }

            roleId = ReadInt32BigEndian(buffer, RoleIdOffset);
            skillId = ReadInt32BigEndian(buffer, SkillIdOffset);
            learnLevel = ReadInt32BigEndian(buffer, LearnLevelOffset);
            return true;
        }

        private static ushort ReadUInt16BigEndian(byte[] buffer, int offset)
        {
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        private static int ReadInt32BigEndian(byte[] buffer, int offset)
        {
            uint value = (uint)((buffer[offset] << 24) |
                                (buffer[offset + 1] << 16) |
                                (buffer[offset + 2] << 8) |
                                buffer[offset + 3]);
            return unchecked((int)value);
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
}
