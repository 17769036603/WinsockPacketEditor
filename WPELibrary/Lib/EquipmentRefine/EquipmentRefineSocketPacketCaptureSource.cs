using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 将 WPE 已有的只读捕获列表适配到手动出包绑定契约。
    /// 不读取接收包，不消费全局队列，不发送任何数据。
    /// </summary>
    public sealed class EquipmentRefineSocketPacketCaptureSource : IEquipmentRefineManualCaptureSource
    {
        private readonly Func<IEnumerable<Socket_PacketInfo>> _provider;
        private readonly int _pollIntervalMs;

        public EquipmentRefineSocketPacketCaptureSource(
            Func<IEnumerable<Socket_PacketInfo>> provider,
            int pollIntervalMs = 50)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            if (pollIntervalMs < 1) throw new ArgumentOutOfRangeException("pollIntervalMs");
            this._provider = provider;
            this._pollIntervalMs = pollIntervalMs;
        }

        public static EquipmentRefineSocketPacketCaptureSource FromCurrentCapture(int pollIntervalMs = 50)
        {
            return new EquipmentRefineSocketPacketCaptureSource(
                () => Socket_Cache.SocketList.lstRecPacket
                    .Where(packet => packet != null)
                    .ToList(),
                pollIntervalMs);
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
                foreach (Socket_PacketInfo packet in CapturePackets())
                {
                    if (packet == null || packet.PacketTime.ToUniversalTime() < armedAtUtc || !IsOutbound(packet.PacketType))
                    {
                        continue;
                    }

                    byte[] bytes = packet.PacketBuffer ?? packet.RawBuffer;
                    if (bytes == null || bytes.Length < 12 || bytes[0] != 0x4D || bytes[1] != 0x5A ||
                        (ushort)((bytes[10] << 8) | bytes[11]) != EquipmentRefineManualCaptureBinding.ObservedRawProtocolCode)
                    {
                        continue;
                    }

                    return new EquipmentRefineManualCapturePacket
                    {
                        PacketTimeUtc = packet.PacketTime.ToUniversalTime(),
                        PacketSocket = packet.PacketSocket,
                        IsOutbound = true,
                        PacketType = packet.PacketType.ToString(),
                        PacketFrom = packet.PacketFrom ?? string.Empty,
                        PacketTo = packet.PacketTo ?? string.Empty,
                        Bytes = (byte[])bytes.Clone()
                    };
                }

                await Task.Delay(this._pollIntervalMs, cancellationToken);
            }

            throw new TimeoutException("manual_capture_timeout");
        }

        private List<Socket_PacketInfo> CapturePackets()
        {
            List<Socket_PacketInfo> captured = new List<Socket_PacketInfo>();
            Action copy = () =>
            {
                captured = (this._provider() ?? Enumerable.Empty<Socket_PacketInfo>())
                    .Where(packet => packet != null)
                    .ToList();
            };

            try
            {
                if (Socket_Cache.System.InvokeAction != null)
                {
                    Socket_Cache.System.InvokeAction(copy);
                }
                else
                {
                    copy();
                }
            }
            catch
            {
                return new List<Socket_PacketInfo>();
            }

            return captured;
        }

        private static bool IsOutbound(Socket_Cache.SocketPacket.PacketType packetType)
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
    }
}
