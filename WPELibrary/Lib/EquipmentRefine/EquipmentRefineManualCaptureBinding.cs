using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 一次用户手动炼化产生的出包记录。这里只保存出包方向和原始字节，
    /// 不解析接收包、不发送封包，也不把未确认字节命名为业务字段。
    /// </summary>
    public sealed class EquipmentRefineManualCapturePacket
    {
        public DateTime PacketTimeUtc { get; set; }
        public int PacketSocket { get; set; }
        public bool IsOutbound { get; set; }
        public string PacketType { get; set; } = string.Empty;
        public string PacketFrom { get; set; } = string.Empty;
        public string PacketTo { get; set; } = string.Empty;
        public byte[] Bytes { get; set; } = new byte[0];
    }

    /// <summary>供桌面捕获器注入的只读手动出包来源。</summary>
    public interface IEquipmentRefineManualCaptureSource
    {
        Task<EquipmentRefineManualCapturePacket> WaitForNextOutboundRefinePacketAsync(
            DateTime armedAtUtc,
            int timeoutMs,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// 44 字节 0x8008/ MZ 出包样本中已被重复观察到的 ASCII ID 位置。
    /// 这些常量只描述当前离线样本的边界，不代表未知字段的业务含义。
    /// </summary>
    public sealed class EquipmentRefineManualCaptureBinding
    {
        public const int ObservedFrameLength = 44;
        public const int ObservedBodyLength = 0x22;
        public const ushort ObservedRawProtocolCode = 0x8008;
        public const int ObservedEquipmentIdOffset = 25;
        public const int ObservedEquipmentIdLength = 19;

        public byte[] RawFrame { get; private set; }
        public string EquipmentId { get; private set; }
        public string PacketType { get; private set; }
        public int PacketSocket { get; private set; }
        public string PacketFrom { get; private set; }
        public string PacketTo { get; private set; }
        public DateTime CapturedAtUtc { get; private set; }

        private EquipmentRefineManualCaptureBinding()
        {
        }

        public sealed class CaptureResult
        {
            public bool Success { get; internal set; }
            public string Error { get; internal set; } = string.Empty;
            public EquipmentRefineManualCaptureBinding Binding { get; internal set; }
        }

        /// <summary>
        /// 从一条已捕获的出包记录提取样本中反复出现的 19 位 ASCII ID。
        /// 严格要求单帧、长度、头部和方向；其它字节保持 raw，不作业务推断。
        /// </summary>
        public static bool TryCreate(
            EquipmentRefineManualCapturePacket packet,
            out EquipmentRefineManualCaptureBinding binding,
            out string error)
        {
            binding = null;
            error = string.Empty;
            if (packet == null)
            {
                error = "manual_capture_missing";
                return false;
            }
            if (!packet.IsOutbound)
            {
                error = "manual_capture_direction_invalid";
                return false;
            }
            if (packet.PacketSocket <= 0)
            {
                error = "manual_capture_socket_missing";
                return false;
            }
            if (string.IsNullOrWhiteSpace(packet.PacketTo))
            {
                error = "manual_capture_destination_missing";
                return false;
            }
            if (packet.Bytes == null || packet.Bytes.Length == 0)
            {
                error = "manual_capture_bytes_missing";
                return false;
            }

            List<EquipmentRefineRawFrame> frames;
            if (!EquipmentRefineFrameFramer.TryParseConcatenated(packet.Bytes, out frames, out error))
            {
                error = "manual_capture_frame_invalid:" + error;
                return false;
            }
            if (frames.Count != 1 || frames[0].TotalLength != ObservedFrameLength)
            {
                error = "manual_capture_frame_count_or_length_invalid";
                return false;
            }

            EquipmentRefineRawFrame frame = frames[0];
            if (frame.BodyLength != ObservedBodyLength ||
                frame.RawProtocolCode != ObservedRawProtocolCode)
            {
                error = "manual_capture_observed_header_mismatch";
                return false;
            }
            if (packet.Bytes.Length < ObservedEquipmentIdOffset + ObservedEquipmentIdLength)
            {
                error = "manual_capture_equipment_id_truncated";
                return false;
            }

            string equipmentId = Encoding.ASCII.GetString(
                packet.Bytes,
                ObservedEquipmentIdOffset,
                ObservedEquipmentIdLength);
            if (equipmentId.Length != ObservedEquipmentIdLength ||
                equipmentId.Any(ch => ch < '0' || ch > '9'))
            {
                error = "manual_capture_equipment_id_not_ascii_digits";
                return false;
            }

            binding = new EquipmentRefineManualCaptureBinding
            {
                RawFrame = (byte[])packet.Bytes.Clone(),
                EquipmentId = equipmentId,
                PacketType = packet.PacketType ?? string.Empty,
                PacketSocket = packet.PacketSocket,
                PacketFrom = packet.PacketFrom ?? string.Empty,
                PacketTo = packet.PacketTo ?? string.Empty,
                CapturedAtUtc = packet.PacketTimeUtc
            };
            return true;
        }

        /// <summary>
        /// 将手动出包识别出的 ID 绑定到已选择的目标。
        /// 不覆盖已有不一致身份；背包模式只填充原始 ItemId 证据。
        /// </summary>
        public static bool TryBindPreset(
            EquipmentRefinePreset preset,
            EquipmentRefineManualCaptureBinding binding,
            out string error)
        {
            error = string.Empty;
            if (preset == null || binding == null || string.IsNullOrWhiteSpace(binding.EquipmentId))
            {
                error = "manual_capture_binding_missing";
                return false;
            }

            if (preset.BagTargetMode)
            {
                if (preset.BagTarget == null || !preset.BagTarget.IsValid)
                {
                    error = "bag_target_identity_missing";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(preset.BagTarget.ItemId) &&
                    !string.Equals(preset.BagTarget.ItemId, binding.EquipmentId, StringComparison.Ordinal))
                {
                    error = "manual_capture_equipment_id_mismatch";
                    return false;
                }
                preset.BagTarget.ItemId = binding.EquipmentId;
                return true;
            }

            if (preset.Target == null ||
                string.IsNullOrWhiteSpace(preset.Target.Slot) ||
                string.IsNullOrWhiteSpace(preset.Target.MemberIdentity))
            {
                error = "worn_target_identity_triplet_missing";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(preset.Target.EquipmentId) &&
                !string.Equals(preset.Target.EquipmentId, binding.EquipmentId, StringComparison.Ordinal))
            {
                error = "manual_capture_equipment_id_mismatch";
                return false;
            }
            preset.Target.EquipmentId = binding.EquipmentId;
            return true;
        }
    }

    /// <summary>
    /// 通用轮询来源，便于桌面宿主把现有捕获列表或离线 fixture 注入进来。
    /// 只观察 armedAtUtc 之后的新出包，超时不会返回旧数据。
    /// </summary>
    public sealed class EquipmentRefineManualCaptureSource : IEquipmentRefineManualCaptureSource
    {
        private readonly Func<IEnumerable<EquipmentRefineManualCapturePacket>> _provider;
        private readonly int _pollIntervalMs;

        public EquipmentRefineManualCaptureSource(
            Func<IEnumerable<EquipmentRefineManualCapturePacket>> provider,
            int pollIntervalMs = 50)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            if (pollIntervalMs < 1) throw new ArgumentOutOfRangeException("pollIntervalMs");
            this._provider = provider;
            this._pollIntervalMs = pollIntervalMs;
        }

        public async Task<EquipmentRefineManualCapturePacket> WaitForNextOutboundRefinePacketAsync(
            DateTime armedAtUtc,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException("timeoutMs");
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IEnumerable<EquipmentRefineManualCapturePacket> packets = this._provider();
                foreach (EquipmentRefineManualCapturePacket packet in packets ?? Enumerable.Empty<EquipmentRefineManualCapturePacket>())
                {
                    if (packet == null || !packet.IsOutbound || packet.PacketTimeUtc < armedAtUtc)
                    {
                        continue;
                    }

                    byte[] bytes = packet.Bytes;
                    if (bytes != null && bytes.Length >= 12 && bytes[0] == 0x4D && bytes[1] == 0x5A &&
                        (ushort)((bytes[10] << 8) | bytes[11]) == EquipmentRefineManualCaptureBinding.ObservedRawProtocolCode)
                    {
                        return packet;
                    }
                }

                await Task.Delay(this._pollIntervalMs, cancellationToken);
            }

            throw new TimeoutException("manual_capture_timeout");
        }
    }
}
