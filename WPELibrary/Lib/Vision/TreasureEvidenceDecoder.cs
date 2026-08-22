using System;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// Decoder for read-only arrival/consumption observations. The existing
    /// game frames use an MZ envelope with a two-byte big-endian protocol at
    /// offset 10. A few offline captures carry the same logical payload with
    /// the protocol at offset 12, so that layout is accepted only for the
    /// evidence protocols and never for outgoing packet preparation.
    /// </summary>
    public static class TreasureEvidenceDecoder
    {
        private const int MzProtocolOffset = 10;
        private const int AlternateProtocolOffset = 12;

        public static TreasureJumpArrivalEvidence DecodeEnterMap(byte[] frame)
        {
            return DecodeArrival(frame, TreasureJumpArrivalEvidenceType.EnterMap, 0xFFE1, false);
        }

        public static TreasureJumpArrivalEvidence DecodeGoToPos(byte[] frame)
        {
            return DecodeArrival(frame, TreasureJumpArrivalEvidenceType.GoToPos, 0xFFE2, false);
        }

        public static TreasureJumpArrivalEvidence DecodePlayerJumpToPos(byte[] frame)
        {
            return DecodeArrival(frame, TreasureJumpArrivalEvidenceType.PlayerJumpToPos, 0x1099, true);
        }

        public static TreasureUseResultEvidence DecodeRespPotholing(byte[] frame)
        {
            int protocolOffset;
            EnsureProtocol(frame, 0x8112, out protocolOffset);
            int resultOffset = protocolOffset + 2;
            if (resultOffset + 4 > frame.Length)
            {
                throw new TreasurePacketContractException(
                    "resp_potholing_frame_length_invalid",
                    "RespPotholing frame does not contain a four-byte result.");
            }

            int result = ReadInt32BigEndian(frame, resultOffset);
            return new TreasureUseResultEvidence(result != 0, result, DateTime.UtcNow);
        }

        public static TreasureJumpArrivalEvidence DecodeJumpArrivalEvidenceByFrame(byte[] frame)
        {
            int protocolId = GetProtocolId(frame);
            switch (protocolId)
            {
                case 0xFFE1:
                    return DecodeEnterMap(frame);
                case 0xFFE2:
                    return DecodeGoToPos(frame);
                case 0x1099:
                    return DecodePlayerJumpToPos(frame);
                default:
                    return null;
            }
        }

        /// <summary>Returns the two-byte protocol ID, or zero for an unknown frame.</summary>
        public static int GetProtocolId(byte[] frame)
        {
            int protocolId;
            int protocolOffset;
            return TryGetProtocolId(frame, out protocolId, out protocolOffset) ? protocolId : 0;
        }

        public static bool IsJumpArrivalEvidenceFrame(byte[] frame)
        {
            int protocolId = GetProtocolId(frame);
            return protocolId == 0xFFE1 || protocolId == 0xFFE2 || protocolId == 0x1099;
        }

        public static bool IsUseResultEvidenceFrame(byte[] frame)
        {
            return GetProtocolId(frame) == 0x8112;
        }

        private static TreasureJumpArrivalEvidence DecodeArrival(
            byte[] frame,
            TreasureJumpArrivalEvidenceType evidenceType,
            int expectedProtocol,
            bool playerFrame)
        {
            int protocolOffset;
            EnsureProtocol(frame, expectedProtocol, out protocolOffset);

            int mapOffset;
            int xOffset;
            int yOffset;
            int mapId;
            int x;
            int y;
            if (playerFrame &&
                protocolOffset + 22 <= frame.Length &&
                TryReadValidCoordinates(
                    frame,
                    protocolOffset + 10,
                    protocolOffset + 14,
                    protocolOffset + 18,
                    out mapId,
                    out x,
                    out y))
            {
                // PlayerJumpToPos commonly prefixes the destination with an actor/sequence field.
                mapOffset = protocolOffset + 10;
                xOffset = protocolOffset + 14;
                yOffset = protocolOffset + 18;
            }
            else
            {
                mapOffset = protocolOffset + 2;
                xOffset = protocolOffset + 6;
                yOffset = protocolOffset + 10;
            }

            if (yOffset + 4 > frame.Length)
            {
                throw new TreasurePacketContractException(
                    "arrival_frame_length_invalid",
                    "Arrival evidence frame does not contain map/x/y fields.");
            }

            mapId = ReadInt32BigEndian(frame, mapOffset);
            x = ReadInt32BigEndian(frame, xOffset);
            y = ReadInt32BigEndian(frame, yOffset);
            if (!IsValidCoordinates(mapId, x, y))
            {
                throw new TreasurePacketContractException(
                    "arrival_field_out_of_range",
                    "Arrival evidence contains an invalid map or coordinate.");
            }

            return new TreasureJumpArrivalEvidence(evidenceType, mapId, x, y, DateTime.UtcNow);
        }

        private static void EnsureProtocol(byte[] frame, int expectedProtocol, out int protocolOffset)
        {
            int protocolId;
            if (!TryGetProtocolId(frame, out protocolId, out protocolOffset) ||
                protocolId != expectedProtocol)
            {
                throw new TreasurePacketContractException(
                    "evidence_protocol_invalid",
                    string.Format(
                        "Expected evidence protocol 0x{0:X4}, got 0x{1:X4}.",
                        expectedProtocol,
                        protocolId));
            }
        }

        private static bool TryGetProtocolId(
            byte[] frame,
            out int protocolId,
            out int protocolOffset)
        {
            protocolId = 0;
            protocolOffset = -1;
            if (frame == null || frame.Length < MzProtocolOffset + 2)
            {
                return false;
            }

            if (frame[0] == 0x4D && frame[1] == 0x5A)
            {
                protocolId = ReadUInt16BigEndian(frame, MzProtocolOffset);
                protocolOffset = MzProtocolOffset;
                if (!IsEvidenceProtocol(protocolId) &&
                    frame.Length >= AlternateProtocolOffset + 2)
                {
                    int alternate = ReadUInt16BigEndian(frame, AlternateProtocolOffset);
                    if (IsEvidenceProtocol(alternate))
                    {
                        protocolId = alternate;
                        protocolOffset = AlternateProtocolOffset;
                    }
                }
                return true;
            }

            if (frame.Length >= AlternateProtocolOffset + 2)
            {
                int candidate = ReadUInt16BigEndian(frame, AlternateProtocolOffset);
                if (candidate == 0xFFE1 || candidate == 0xFFE2 ||
                    candidate == 0x1099 || candidate == 0x8112)
                {
                    protocolId = candidate;
                    protocolOffset = AlternateProtocolOffset;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadValidCoordinates(
            byte[] frame,
            int mapOffset,
            int xOffset,
            int yOffset,
            out int mapId,
            out int x,
            out int y)
        {
            mapId = 0;
            x = 0;
            y = 0;
            if (frame == null ||
                mapOffset < 0 ||
                yOffset + 4 > frame.Length)
            {
                return false;
            }

            mapId = ReadInt32BigEndian(frame, mapOffset);
            x = ReadInt32BigEndian(frame, xOffset);
            y = ReadInt32BigEndian(frame, yOffset);
            return IsValidCoordinates(mapId, x, y);
        }

        private static bool IsValidCoordinates(int mapId, int x, int y)
        {
            return mapId > 0 && x >= 0 && y >= 0;
        }

        private static bool IsEvidenceProtocol(int protocolId)
        {
            return protocolId == 0xFFE1 || protocolId == 0xFFE2 ||
                protocolId == 0x1099 || protocolId == 0x8112;
        }

        private static int ReadUInt16BigEndian(byte[] buffer, int offset)
        {
            return (buffer[offset] << 8) | buffer[offset + 1];
        }

        private static int ReadInt32BigEndian(byte[] buffer, int offset)
        {
            return unchecked(
                (int)(((uint)buffer[offset] << 24) |
                      ((uint)buffer[offset + 1] << 16) |
                      ((uint)buffer[offset + 2] << 8) |
                      buffer[offset + 3]));
        }
    }
}
