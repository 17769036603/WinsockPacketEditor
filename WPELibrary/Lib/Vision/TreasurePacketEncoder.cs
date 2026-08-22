using System;
using System.Text;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// Pure, side-effect-free codec for the confirmed current Jump/Use and
    /// native auto-dig contracts. It does not read memory, inspect UI state,
    /// or send bytes.
    /// </summary>
    public static class TreasurePacketEncoder
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] EncodeJump(TreasureJumpPacketRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            byte[] frame = new byte[TreasureJumpPacketContract.FrameLength];
            Array.Copy(
                TreasureJumpPacketContract.Header,
                0,
                frame,
                0,
                TreasureJumpPacketContract.Header.Length);
            WriteInt32BigEndian(frame, TreasureJumpPacketContract.MapIdOffset, request.MapId);
            WriteInt32BigEndian(frame, TreasureJumpPacketContract.XOffset, request.X);
            WriteInt32BigEndian(frame, TreasureJumpPacketContract.YOffset, request.Y);
            WriteInt32BigEndian(frame, TreasureJumpPacketContract.IsTaskWalkOffset, request.IsTaskWalk);
            return frame;
        }

        public static TreasureJumpPacketRequest DecodeJump(byte[] frame)
        {
            ValidateJumpFrame(frame);
            int mapId = ReadInt32BigEndian(frame, TreasureJumpPacketContract.MapIdOffset);
            int x = ReadInt32BigEndian(frame, TreasureJumpPacketContract.XOffset);
            int y = ReadInt32BigEndian(frame, TreasureJumpPacketContract.YOffset);
            int isTaskWalk = ReadInt32BigEndian(frame, TreasureJumpPacketContract.IsTaskWalkOffset);

            if (mapId <= 0 || x < 0 || y < 0 || isTaskWalk != 0)
            {
                throw new TreasurePacketContractException(
                    "jump_field_out_of_range",
                    "0x5828 contains an unsupported field value.");
            }

            return new TreasureJumpPacketRequest(mapId, x, y);
        }

        public static byte[] EncodeUse(TreasureUsePacketRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            TreasureUsePacketContract.DemandEncodingClosed();
            byte[] paramBytes;
            try
            {
                paramBytes = StrictUtf8.GetBytes(request.Param);
            }
            catch (EncoderFallbackException exception)
            {
                throw new TreasurePacketContractException(
                    "use_param_utf8_invalid",
                    "0x783A param is not valid UTF-8 text.",
                    exception);
            }

            if (paramBytes.Length > TreasureUsePacketContract.MaxParamByteLength)
            {
                throw new TreasurePacketContractException(
                    "use_param_length_invalid",
                    "0x783A param exceeds the confirmed one-byte length prefix.");
            }

            int bodyLength = 12 + 1 + paramBytes.Length;
            int outerLength = 2 + bodyLength;
            byte[] frame = new byte[TreasureUsePacketContract.ParamOffset + paramBytes.Length];
            Array.Copy(
                TreasureUsePacketContract.FramePrefix,
                0,
                frame,
                0,
                TreasureUsePacketContract.FramePrefix.Length);
            WriteUInt16BigEndian(frame, TreasureUsePacketContract.LengthFieldOffset, outerLength);
            WriteUInt16BigEndian(frame, TreasureUsePacketContract.ProtocolOffset, TreasureUsePacketContract.ProtocolId);
            WriteInt32BigEndian(frame, TreasureUsePacketContract.PosOffset, request.PackageNum);
            WriteInt32BigEndian(frame, TreasureUsePacketContract.TypeOffset, request.Type);
            WriteInt32BigEndian(frame, TreasureUsePacketContract.NumOffset, request.Num);
            frame[TreasureUsePacketContract.ParamLengthOffset] = (byte)paramBytes.Length;
            Buffer.BlockCopy(
                paramBytes,
                0,
                frame,
                TreasureUsePacketContract.ParamOffset,
                paramBytes.Length);
            return frame;
        }

        public static TreasureUsePacketRequest DecodeUse(byte[] frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            TreasureUsePacketContract.DemandEncodingClosed();
            ValidateUseFrame(frame);
            int packageNum = ReadInt32BigEndian(frame, TreasureUsePacketContract.PosOffset);
            int type = ReadInt32BigEndian(frame, TreasureUsePacketContract.TypeOffset);
            int num = ReadInt32BigEndian(frame, TreasureUsePacketContract.NumOffset);
            if (packageNum <= 0 || type <= 0 || num <= 0)
            {
                throw new TreasurePacketContractException(
                    "use_field_out_of_range",
                    "0x783A contains an unsupported pos, type, or num value.");
            }

            int paramByteLength = frame[TreasureUsePacketContract.ParamLengthOffset];
            string param;
            try
            {
                param = StrictUtf8.GetString(
                    frame,
                    TreasureUsePacketContract.ParamOffset,
                    paramByteLength);
            }
            catch (DecoderFallbackException exception)
            {
                throw new TreasurePacketContractException(
                    "use_param_utf8_invalid",
                    "0x783A param is not valid UTF-8 text.",
                    exception);
            }

            return new TreasureUsePacketRequest(packageNum, type, num, param);
        }

        /// <summary>
        /// Returns the fixed native auto-dig command captured from the game.
        /// The target coordinates are already carried by the preceding Jump
        /// frame, so no package-specific values are encoded here.
        /// </summary>
        public static byte[] EncodeAutoDig()
        {
            return TreasureAutoDigPacketContract.CreateFrame();
        }

        public static void ValidateAutoDig(byte[] frame)
        {
            if (!TreasureAutoDigPacketContract.IsFrame(frame))
            {
                throw new TreasurePacketContractException(
                    "auto_dig_frame_invalid",
                    "0xB0F4 must match the confirmed 24-byte native auto-dig frame.");
            }
        }

        private static void ValidateUseFrame(byte[] frame)
        {
            if (frame.Length < TreasureUsePacketContract.MinimumFrameLength)
            {
                throw new TreasurePacketContractException(
                    "use_frame_length_invalid",
                    "0x783A is shorter than the confirmed current contract.");
            }

            if (!TreasureUsePacketContract.IsFramePrefix(frame) ||
                ReadUInt16BigEndian(frame, TreasureUsePacketContract.ProtocolOffset) != TreasureUsePacketContract.ProtocolId)
            {
                throw new TreasurePacketContractException(
                    "use_header_invalid",
                    "0x783A frame prefix or protocol ID does not match the closed contract.");
            }

            int outerLength = ReadUInt16BigEndian(frame, TreasureUsePacketContract.LengthFieldOffset);
            if (outerLength != frame.Length - 10)
            {
                throw new TreasurePacketContractException(
                    "use_outer_length_invalid",
                    "0x783A outer length does not match the protocol-plus-body boundary.");
            }

            int paramByteLength = frame[TreasureUsePacketContract.ParamLengthOffset];
            int expectedFrameLength = TreasureUsePacketContract.ParamOffset + paramByteLength;
            if (frame.Length != expectedFrameLength)
            {
                throw new TreasurePacketContractException(
                    "use_frame_length_invalid",
                    "0x783A frame length does not match its one-byte param length.");
            }
        }

        private static void ValidateJumpFrame(byte[] frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (frame.Length != TreasureJumpPacketContract.FrameLength)
            {
                throw new TreasurePacketContractException(
                    "jump_frame_length_invalid",
                    "0x5828 must be exactly 28 bytes.");
            }

            if (!TreasureJumpPacketContract.IsHeader(frame))
            {
                throw new TreasurePacketContractException(
                    "jump_header_invalid",
                    "0x5828 header or protocol ID does not match the closed contract.");
            }
        }

        private static int ReadInt32BigEndian(byte[] buffer, int offset)
        {
            uint value = ((uint)buffer[offset] << 24) |
                ((uint)buffer[offset + 1] << 16) |
                ((uint)buffer[offset + 2] << 8) |
                buffer[offset + 3];
            return unchecked((int)value);
        }

        private static int ReadUInt16BigEndian(byte[] buffer, int offset)
        {
            return (buffer[offset] << 8) | buffer[offset + 1];
        }

        private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
        {
            uint unsignedValue = unchecked((uint)value);
            buffer[offset] = (byte)(unsignedValue >> 24);
            buffer[offset + 1] = (byte)(unsignedValue >> 16);
            buffer[offset + 2] = (byte)(unsignedValue >> 8);
            buffer[offset + 3] = (byte)unsignedValue;
        }

        private static void WriteUInt16BigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }
    }
}
