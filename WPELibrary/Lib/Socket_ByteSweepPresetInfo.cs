using System;

namespace WPELibrary.Lib
{
    public class Socket_ByteSweepPresetInfo
    {
        public System.Collections.Generic.List<Socket_ByteAnnotationInfo> ByteAnnotations { get; set; } =
            new System.Collections.Generic.List<Socket_ByteAnnotationInfo>();

        public bool IsEnable { get; set; }
        public Guid BID { get; set; }
        public int BSortOrder { get; set; }
        public string BName { get; set; }
        public string BFolder { get; set; }
        public int BLoopCount { get; set; } = 1;
        public int BInterval { get; set; }
        public int BNextInterval { get; set; }
        public int BStart { get; set; }
        public int BLength { get; set; }
        public Socket_Cache.SocketPacket.PacketType PacketType { get; set; }
        public string PacketFrom { get; set; }
        public string PacketTo { get; set; }
        public byte[] Buffer { get; set; }

        public string RangeText
        {
            get
            {
                if (BLength <= 0)
                {
                    return "-";
                }

                return BLength == 1
                    ? BStart.ToString("X2")
                    : string.Format("{0:X2}-{1:X2}", BStart, BStart + BLength - 1);
            }
        }

        public long TotalSend
        {
            get { return Math.Max(0, BLength) * 255L * Math.Max(1, BLoopCount); }
        }

        public bool IsValid
        {
            get
            {
                return BID != Guid.Empty &&
                    !string.IsNullOrWhiteSpace(BName) &&
                    !string.IsNullOrWhiteSpace(BFolder) &&
                    Buffer != null &&
                    Buffer.Length > 0 &&
                    BStart >= 0 &&
                    BLength > 0 &&
                    BStart < Buffer.Length &&
                    BLength <= Buffer.Length - BStart &&
                    BLoopCount > 0 &&
                    BInterval >= 0 &&
                    BNextInterval >= 0;
            }
        }

        public Socket_ByteSweepPresetInfo Clone()
        {
            return new Socket_ByteSweepPresetInfo
            {
                IsEnable = IsEnable,
                BID = BID,
                BSortOrder = BSortOrder,
                BName = BName,
                BFolder = BFolder,
                BLoopCount = BLoopCount,
                BInterval = BInterval,
                BNextInterval = BNextInterval,
                BStart = BStart,
                BLength = BLength,
                PacketType = PacketType,
                PacketFrom = PacketFrom,
                PacketTo = PacketTo,
                Buffer = Buffer == null ? null : (byte[])Buffer.Clone(),
                ByteAnnotations = Socket_ByteAnnotationEngine.Clone(ByteAnnotations)
            };
        }
    }
}
