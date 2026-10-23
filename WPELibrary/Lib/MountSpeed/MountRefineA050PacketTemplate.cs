using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑炼化 A050 样本的离线模板。
    /// 该类只校验帧结构并生成发送副本，不解析 Socket，也不执行发送。
    /// </summary>
    public sealed class MountRefineA050PacketTemplate
    {
        public const ushort ProtocolId = 0xA050;
        public const int FrameLength = 39;
        public const int RideInstanceIdOffset = 20;
        public const int RideInstanceIdLength = 19;

        private static readonly byte[] OperationPrefix =
            { 0x00, 0x06, 0x03, 0x00, 0x00, 0x00, 0x00 };

        private readonly byte[] _fixtureBytes;

        private MountRefineA050PacketTemplate(byte[] fixtureBytes, string evidenceId)
        {
            _fixtureBytes = (byte[])fixtureBytes.Clone();
            EvidenceId = evidenceId;
        }

        public string EvidenceId { get; private set; }

        public byte[] FixtureBytes
        {
            get { return (byte[])_fixtureBytes.Clone(); }
        }

        /// <summary>
        /// 预设帧中经过结构校验的 19 位坐骑实例 ID。
        /// 该值只用于把只读炼化页绑定到同一个实例，不用于改写封包。
        /// </summary>
        public string RideInstanceId
        {
            get
            {
                return _fixtureBytes == null || _fixtureBytes.Length != FrameLength
                    ? string.Empty
                    : Encoding.ASCII.GetString(
                        _fixtureBytes,
                        RideInstanceIdOffset,
                        RideInstanceIdLength);
            }
        }

        public bool IsValid
        {
            get
            {
                return _fixtureBytes != null &&
                    _fixtureBytes.Length == FrameLength &&
                    !string.IsNullOrWhiteSpace(EvidenceId);
            }
        }

        public static bool TryCreate(
            byte[] capturedPacket,
            string evidenceId,
            out MountRefineA050PacketTemplate template,
            out string error)
        {
            template = null;
            error = string.Empty;
            if (capturedPacket == null || capturedPacket.Length == 0)
            {
                error = "mount_refine_a050_sample_empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(evidenceId))
            {
                error = "mount_refine_a050_evidence_missing";
                return false;
            }

            if (!TryValidateFrame(capturedPacket, out error))
            {
                return false;
            }

            template = new MountRefineA050PacketTemplate(capturedPacket, evidenceId.Trim());
            return true;
        }

        /// <summary>
        /// 从预设保存的十六进制文本创建模板。解析和 A050 结构校验均为离线操作，
        /// 不读取进程、不解析 Socket，也不执行发送。
        /// </summary>
        public static bool TryCreateFromHex(
            string hex,
            string evidenceId,
            out MountRefineA050PacketTemplate template,
            out string error)
        {
            template = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(hex))
            {
                error = "mount_refine_a050_hex_empty";
                return false;
            }

            string[] tokens = hex.Split(
                new[] { ' ', '\t', '\r', '\n', ',', ';', ':', '-' },
                StringSplitOptions.RemoveEmptyEntries);
            List<byte> bytes = new List<byte>();
            foreach (string rawToken in tokens)
            {
                string token = (rawToken ?? string.Empty).Trim();
                if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    token = token.Substring(2);
                }

                if (token.Length == 0)
                {
                    continue;
                }

                if ((token.Length & 1) != 0)
                {
                    error = "mount_refine_a050_hex_odd_length";
                    return false;
                }

                for (int index = 0; index < token.Length; index += 2)
                {
                    byte value;
                    if (!byte.TryParse(
                            token.Substring(index, 2),
                            NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture,
                            out value))
                    {
                        error = "mount_refine_a050_hex_invalid";
                        return false;
                    }
                    bytes.Add(value);
                }
            }

            return TryCreate(bytes.ToArray(), evidenceId, out template, out error);
        }

        /// <summary>
        /// A050 是当前客户端使用的固定 39 字节请求帧。
        /// 发送副本以捕获模板为基础，只把偏移 20-38 的 19 字节重写为
        /// 请求携带的当前坐骑实例 ID（服务端会重新下发实例 ID，包内旧 ID 会过期），
        /// 其余字节与模板逐字节一致。改写后重新做一次完整结构校验；
        /// 请求缺失或实例 ID 非法时拒绝发送，绝不回退到模板里的旧 ID。
        /// 不修改模板自身保存的字节，也不触发网络操作。
        /// </summary>
        public bool TryBuild(
            MountRefineSendRequest request,
            out byte[] packet,
            out string error)
        {
            packet = null;
            error = string.Empty;
            if (!IsValid)
            {
                error = "mount_refine_a050_template_invalid";
                return false;
            }

            if (request == null)
            {
                error = "mount_refine_a050_request_missing";
                return false;
            }

            if (!IsValidRideInstanceId(request.ActiveRideInstanceId))
            {
                error = "mount_refine_a050_active_ride_instance_id_invalid";
                return false;
            }

            packet = (byte[])_fixtureBytes.Clone();
            byte[] encodedRideInstanceId = Encoding.ASCII.GetBytes(request.ActiveRideInstanceId);
            for (int index = 0; index < RideInstanceIdLength; index++)
            {
                packet[RideInstanceIdOffset + index] = encodedRideInstanceId[index];
            }

            // 改写后必须重新完成一次完整结构校验；任何一项不通过都拒绝发送。
            if (!TryValidateFrame(packet, out error))
            {
                packet = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 完整 A050 结构校验：帧长 39、4D5A、body 0x1D、协议 0xA050、
        /// 固定操作前缀、偏移 19=0x13 且尾部 19 字节为 ASCII 数字。
        /// </summary>
        private static bool TryValidateFrame(byte[] frame, out string error)
        {
            error = string.Empty;
            if (frame == null || frame.Length != FrameLength)
            {
                error = "mount_refine_a050_length_invalid";
                return false;
            }

            if (frame[0] != 0x4D || frame[1] != 0x5A)
            {
                error = "mount_refine_a050_header_invalid";
                return false;
            }

            int bodyLength = (frame[8] << 8) | frame[9];
            if (bodyLength != FrameLength - 10)
            {
                error = "mount_refine_a050_body_length_invalid";
                return false;
            }

            ushort protocolId = (ushort)((frame[10] << 8) | frame[11]);
            if (protocolId != ProtocolId)
            {
                error = "mount_refine_a050_protocol_invalid";
                return false;
            }

            for (int index = 0; index < OperationPrefix.Length; index++)
            {
                if (frame[12 + index] != OperationPrefix[index])
                {
                    error = "mount_refine_a050_operation_prefix_invalid";
                    return false;
                }
            }

            if (frame[19] != RideInstanceIdLength ||
                !IsAsciiDigits(frame, RideInstanceIdOffset, RideInstanceIdLength))
            {
                error = "mount_refine_a050_ride_instance_id_invalid";
                return false;
            }

            return true;
        }

        private static bool IsValidRideInstanceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != RideInstanceIdLength)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (character < '0' || character > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAsciiDigits(byte[] bytes, int offset, int length)
        {
            if (bytes == null || offset < 0 || length < 0 || offset + length > bytes.Length)
            {
                return false;
            }

            for (int index = offset; index < offset + length; index++)
            {
                if (bytes[index] < (byte)'0' || bytes[index] > (byte)'9')
                {
                    return false;
                }
            }
            return true;
        }

    }
}
