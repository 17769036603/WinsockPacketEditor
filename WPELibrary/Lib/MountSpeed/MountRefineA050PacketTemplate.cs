using System;

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

            if (capturedPacket.Length != FrameLength)
            {
                error = "mount_refine_a050_length_invalid";
                return false;
            }

            if (capturedPacket[0] != 0x4D || capturedPacket[1] != 0x5A)
            {
                error = "mount_refine_a050_header_invalid";
                return false;
            }

            int bodyLength = (capturedPacket[8] << 8) | capturedPacket[9];
            if (bodyLength != FrameLength - 10)
            {
                error = "mount_refine_a050_body_length_invalid";
                return false;
            }

            ushort protocolId = (ushort)((capturedPacket[10] << 8) | capturedPacket[11]);
            if (protocolId != ProtocolId)
            {
                error = "mount_refine_a050_protocol_invalid";
                return false;
            }

            byte[] expectedOperationPrefix = { 0x00, 0x06, 0x03, 0x00, 0x00, 0x00, 0x00 };
            for (int index = 0; index < expectedOperationPrefix.Length; index++)
            {
                if (capturedPacket[12 + index] != expectedOperationPrefix[index])
                {
                    error = "mount_refine_a050_operation_prefix_invalid";
                    return false;
                }
            }

            if (capturedPacket[19] != RideInstanceIdLength ||
                !IsAsciiDigits(capturedPacket, RideInstanceIdOffset, RideInstanceIdLength))
            {
                error = "mount_refine_a050_ride_instance_id_invalid";
                return false;
            }

            template = new MountRefineA050PacketTemplate(capturedPacket, evidenceId.Trim());
            return true;
        }

        /// <summary>
        /// A050 是当前客户端使用的固定 39 字节请求帧。
        /// 发送副本必须保持捕获到的全部字节不变，不根据只读快照改写
        /// 尾部 19 字节；这与手动发送选中封包的行为一致。
        /// 不修改模板原始字节，也不触发网络操作。
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

            packet = (byte[])_fixtureBytes.Clone();
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
