using System;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// The four values emitted by the resident treasure-map reader.
    /// This type is deliberately independent from UI, agents, sockets, and
    /// process memory so the byte replacement can be tested offline.
    /// </summary>
    public sealed class TreasureInventoryTarget
    {
        public TreasureInventoryTarget(int packageNum, int scene, int x, int y)
        {
            if (packageNum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(packageNum), "packageNum must be positive.");
            }

            if (scene <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scene), "scene must be positive.");
            }

            if (x < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "x must be non-negative.");
            }

            if (y < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(y), "y must be non-negative.");
            }

            this.PackageNum = packageNum;
            this.Scene = scene;
            this.X = x;
            this.Y = y;
        }

        public int PackageNum { get; private set; }

        public int Slot
        {
            get { return this.PackageNum; }
        }

        public int Scene { get; private set; }

        public int MapId
        {
            get { return this.Scene; }
        }

        public int X { get; private set; }

        public int Y { get; private set; }
    }

    public sealed class TreasurePacketTemplateException : InvalidOperationException
    {
        public TreasurePacketTemplateException(string code, string message)
            : base(message)
        {
            this.Code = code ?? string.Empty;
        }

        public string Code { get; private set; }
    }

    /// <summary>
    /// Patches only the fields that are allowed to change for the current
    /// treasure-map templates. The fixed header and all unrelated Use fields
    /// are retained from the supplied template.
    /// </summary>
    public static class TreasurePacketTemplatePatcher
    {
        public const int JumpProtocolId = 0x5828;
        public const int JumpFrameLength = 28;
        public const int JumpMapIdOffset = 12;
        public const int JumpXOffset = 16;
        public const int JumpYOffset = 20;
        public const int JumpIsTaskWalkOffset = 24;

        public const int UseProtocolId = 0x783A;
        public const int UseLengthFieldOffset = 8;
        public const int UseProtocolOffset = 10;
        public const int UsePosOffset = 12;
        public const int UseTypeOffset = 16;
        public const int UseNumOffset = 20;
        public const int UseParamLengthOffset = 24;
        public const int UseParamOffset = 25;
        public const int UseMinimumFrameLength = 25;

        private static readonly byte[] JumpHeader =
        {
            0x4D, 0x5A,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x12,
            0x58, 0x28
        };

        public static byte[] BuildJump(TreasureInventoryTarget target)
        {
            RequireTarget(target);
            byte[] template = new byte[JumpFrameLength];
            Buffer.BlockCopy(JumpHeader, 0, template, 0, JumpHeader.Length);
            return PatchJump(template, target);
        }

        public static byte[] PatchJump(byte[] template, TreasureInventoryTarget target)
        {
            RequireTarget(target);
            ValidateJumpTemplate(template);

            byte[] result = (byte[])template.Clone();
            WriteInt32BigEndian(result, JumpMapIdOffset, target.MapId);
            WriteInt32BigEndian(result, JumpXOffset, target.X);
            WriteInt32BigEndian(result, JumpYOffset, target.Y);
            return result;
        }

        /// <summary>
        /// Replaces only ReqOperateItem.pos. type, num, param, their lengths,
        /// and the complete frame prefix remain from the validated template.
        /// </summary>
        public static byte[] PatchUse(byte[] template, TreasureInventoryTarget target)
        {
            RequireTarget(target);
            ValidateUseTemplate(template);

            byte[] result = (byte[])template.Clone();
            WriteInt32BigEndian(result, UsePosOffset, target.PackageNum);
            return result;
        }

        private static void RequireTarget(TreasureInventoryTarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
        }

        private static void ValidateJumpTemplate(byte[] template)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            if (template.Length != JumpFrameLength)
            {
                throw new TreasurePacketTemplateException(
                    "jump_frame_length_invalid",
                    "0x5828 template must be exactly 28 bytes.");
            }

            if (!TreasureJumpPacketContract.IsHeader(template))
            {
                throw new TreasurePacketTemplateException(
                    "jump_header_invalid",
                    "0x5828 template static header or protocol ID does not match.");
            }

            if (ReadInt32BigEndian(template, JumpIsTaskWalkOffset) != 0)
            {
                throw new TreasurePacketTemplateException(
                    "jump_task_walk_invalid",
                    "Only the confirmed isTaskWalk=0 template is accepted.");
            }
        }

        private static void ValidateUseTemplate(byte[] template)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            if (template.Length < UseMinimumFrameLength)
            {
                throw new TreasurePacketTemplateException(
                    "use_frame_length_invalid",
                    "0x783A template is shorter than the confirmed current contract.");
            }

            if (!TreasureUsePacketContract.IsFramePrefix(template))
            {
                throw new TreasurePacketTemplateException(
                    "use_prefix_invalid",
                    "0x783A template static prefix does not match.");
            }

            if (ReadUInt16BigEndian(template, UseProtocolOffset) != UseProtocolId)
            {
                throw new TreasurePacketTemplateException(
                    "use_protocol_invalid",
                    "0x783A template protocol ID does not match.");
            }

            if (ReadUInt16BigEndian(template, UseLengthFieldOffset) != template.Length - 10)
            {
                throw new TreasurePacketTemplateException(
                    "use_outer_length_invalid",
                    "0x783A template outer length does not match the frame boundary.");
            }

            int paramLength;
            if (!TreasureUsePacketContract.TryGetParamByteLength(
                    template,
                    out paramLength) ||
                UseParamOffset + paramLength != template.Length)
            {
                throw new TreasurePacketTemplateException(
                    "use_param_length_invalid",
                    "0x783A template parameter length does not match the frame boundary.");
            }

            if (ReadInt32BigEndian(template, UseTypeOffset) <= 0 ||
                ReadInt32BigEndian(template, UseNumOffset) <= 0)
            {
                throw new TreasurePacketTemplateException(
                    "use_static_fields_invalid",
                    "0x783A template type and num must be positive.");
            }
        }

        private static int ReadUInt16BigEndian(byte[] buffer, int offset)
        {
            return (buffer[offset] << 8) | buffer[offset + 1];
        }

        private static int ReadInt32BigEndian(byte[] buffer, int offset)
        {
            uint value = ((uint)buffer[offset] << 24) |
                ((uint)buffer[offset + 1] << 16) |
                ((uint)buffer[offset + 2] << 8) |
                buffer[offset + 3];
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
