using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// Raw frame metadata only. The marker, length and raw code are framing
    /// observations from offline samples; they have no business meaning.
    /// </summary>
    public sealed class EquipmentRefineRawFrame
    {
        internal EquipmentRefineRawFrame(int offset, int totalLength, int bodyLength, ushort rawProtocolCode, byte[] bytes)
        {
            Offset = offset;
            TotalLength = totalLength;
            BodyLength = bodyLength;
            RawProtocolCode = rawProtocolCode;
            Bytes = bytes;
        }

        public int Offset { get; private set; }
        public int TotalLength { get; private set; }
        public int BodyLength { get; private set; }
        public ushort RawProtocolCode { get; private set; }
        public byte[] Bytes { get; private set; }
    }

    /// <summary>
    /// Offline protocol-neutral framing helper. It intentionally does not
    /// interpret payload, card fields, error codes, or sender semantics.
    /// </summary>
    public static class EquipmentRefineFrameFramer
    {
        public static bool TryParseConcatenated(byte[] bytes, out List<EquipmentRefineRawFrame> frames, out string error)
        {
            frames = new List<EquipmentRefineRawFrame>();
            error = string.Empty;
            if (bytes == null || bytes.Length == 0)
            {
                error = "empty_frame_data";
                return false;
            }

            int offset = 0;
            while (offset < bytes.Length)
            {
                int remaining = bytes.Length - offset;
                if (remaining < 12 || bytes[offset] != 0x4D || bytes[offset + 1] != 0x5A)
                {
                    error = "invalid_frame_header";
                    frames.Clear();
                    return false;
                }

                int bodyLength = (bytes[offset + 8] << 8) | bytes[offset + 9];
                int totalLength = 10 + bodyLength;
                if (bodyLength < 2 || totalLength > remaining)
                {
                    error = "invalid_frame_length";
                    frames.Clear();
                    return false;
                }

                ushort rawProtocolCode = (ushort)((bytes[offset + 10] << 8) | bytes[offset + 11]);
                byte[] frameBytes = new byte[totalLength];
                Buffer.BlockCopy(bytes, offset, frameBytes, 0, totalLength);
                frames.Add(new EquipmentRefineRawFrame(offset, totalLength, bodyLength, rawProtocolCode, frameBytes));
                offset += totalLength;
            }

            return frames.Count > 0;
        }
    }
}
