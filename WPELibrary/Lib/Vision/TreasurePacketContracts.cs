using System;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// The four bytes at offsets 4..7 belong to the current client
    /// connection/session. They are not part of the static protocol
    /// signature.
    /// </summary>
    public static class TreasurePacketSessionHeader
    {
        public const int SequenceOffset = 4;
        public const int SequenceLength = 4;

        public static bool MatchesStaticBytes(byte[] frame, byte[] expected)
        {
            if (frame == null || expected == null || frame.Length < expected.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (i >= SequenceOffset &&
                    i < SequenceOffset + SequenceLength)
                {
                    continue;
                }

                if (frame[i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static uint ReadSequence(byte[] frame)
        {
            if (frame == null ||
                frame.Length < SequenceOffset + SequenceLength)
            {
                throw new ArgumentException(
                    "A complete session header is required.",
                    nameof(frame));
            }

            return ((uint)frame[SequenceOffset] << 24) |
                ((uint)frame[SequenceOffset + 1] << 16) |
                ((uint)frame[SequenceOffset + 2] << 8) |
                frame[SequenceOffset + 3];
        }

        public static void WriteSequence(byte[] frame, uint sequence)
        {
            if (frame == null ||
                frame.Length < SequenceOffset + SequenceLength)
            {
                throw new ArgumentException(
                    "A complete session header is required.",
                    nameof(frame));
            }

            frame[SequenceOffset] = (byte)(sequence >> 24);
            frame[SequenceOffset + 1] = (byte)(sequence >> 16);
            frame[SequenceOffset + 2] = (byte)(sequence >> 8);
            frame[SequenceOffset + 3] = (byte)sequence;
        }
    }

    public sealed class TreasurePacketContractException : InvalidOperationException
    {
        public TreasurePacketContractException(string code, string message)
            : base(message)
        {
            this.Code = code ?? string.Empty;
        }

        public TreasurePacketContractException(string code, string message, Exception innerException)
            : base(message, innerException)
        {
            this.Code = code ?? string.Empty;
        }

        public string Code { get; private set; }
    }

    public sealed class TreasureJumpPacketRequest : IEquatable<TreasureJumpPacketRequest>
    {
        public TreasureJumpPacketRequest(int mapId, int x, int y)
        {
            if (mapId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mapId), "mapId must be positive.");
            }

            if (x < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "x must be non-negative.");
            }

            if (y < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(y), "y must be non-negative.");
            }

            this.MapId = mapId;
            this.X = x;
            this.Y = y;
        }

        public int MapId { get; private set; }

        public int X { get; private set; }

        public int Y { get; private set; }

        public int IsTaskWalk
        {
            get { return 0; }
        }

        public bool Equals(TreasureJumpPacketRequest other)
        {
            return other != null &&
                this.MapId == other.MapId &&
                this.X == other.X &&
                this.Y == other.Y &&
                this.IsTaskWalk == other.IsTaskWalk;
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as TreasureJumpPacketRequest);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = this.MapId;
                hash = (hash * 397) ^ this.X;
                hash = (hash * 397) ^ this.Y;
                hash = (hash * 397) ^ this.IsTaskWalk;
                return hash;
            }
        }
    }

    /// <summary>
    /// The complete logical input for one 0x783A Use frame. packageNum is
    /// bound to the wire field pos; type/num/param remain explicit because
    /// the client has more than one treasure operation call site.
    /// </summary>
    public sealed class TreasureUsePacketRequest
    {
        public TreasureUsePacketRequest(int packageNum, int type, int num, string param)
        {
            if (packageNum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(packageNum), "packageNum must be positive.");
            }

            if (type <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(type), "type must be positive.");
            }

            if (num <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(num), "num must be positive.");
            }

            if (param == null)
            {
                throw new ArgumentNullException(nameof(param));
            }

            this.PackageNum = packageNum;
            this.Type = type;
            this.Num = num;
            this.Param = param;
        }

        public int PackageNum { get; private set; }

        public int Type { get; private set; }

        public int Num { get; private set; }

        public string Param { get; private set; }
    }

    public static class TreasureJumpPacketContract
    {
        public const int ProtocolId = 0x5828;
        public const int FrameLength = 28;
        public const int HeaderLength = 12;
        public const int MapIdOffset = 12;
        public const int XOffset = 16;
        public const int YOffset = 20;
        public const int IsTaskWalkOffset = 24;

        public static readonly byte[] Header =
        {
            0x4D, 0x5A,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x12,
            0x58, 0x28
        };

        public static bool IsHeader(byte[] frame)
        {
            return TreasurePacketSessionHeader.MatchesStaticBytes(frame, Header);
        }
    }

    public static class TreasureUsePacketContract
    {
        public const int ProtocolId = 0x783A;
        public const int LengthFieldOffset = 8;
        public const int ProtocolOffset = 10;
        public const int PosOffset = 12;
        public const int TypeOffset = 16;
        public const int NumOffset = 20;
        public const int ParamLengthOffset = 24;
        public const int ParamOffset = 25;
        public const int MinimumFrameLength = 25;
        public const int MaxParamByteLength = 255;
        public const string PackageNumWireField = "pos";
        public const string CandidateWireFieldOrder = "pos:int,type:int,num:int,param:string";
        public const string ClientParamLengthEncoding = "u8_prefix";
        public const string HistoricalRawParamLengthEncoding = "u16_be";
        public const int DistinctPackageNumRawSampleCount = 3;
        public const bool PackageNumBindingConfirmed = true;
        public const bool DistinctPackageNumEvidenceConfirmed = true;
        public const bool ParamLengthEncodingConfirmed = true;
        public const bool IsEncodingClosed = true;
        public const string CurrentContractScope = "current_client_and_server_codec_u8_param_length";
        public const string BlockingReason =
            "The three historical raw frames use an unversioned uint16-be param length and are " +
            "retained as a legacy negative corpus. The closed current contract is the independently " +
            "confirmed client/server codec path with a one-byte UTF-8 param length; it does not " +
            "silently reinterpret the historical frames as current-client frames.";

        public static readonly byte[] FramePrefix =
        {
            0x4D, 0x5A,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        public static void DemandEncodingClosed()
        {
            if (!IsEncodingClosed)
            {
                throw new TreasurePacketContractException(
                    "use_contract_evidence_insufficient",
                    BlockingReason);
            }
        }

        public static bool IsFramePrefix(byte[] frame)
        {
            return TreasurePacketSessionHeader.MatchesStaticBytes(frame, FramePrefix);
        }

        /// <summary>
        /// The closed encoder uses a one-byte UTF-8 length at offset 24.
        /// Captured current-client 0x783A frames use a zero marker there and
        /// carry the UTF-8 parameter to the frame end. Both forms are
        /// retained without rewriting captured bytes.
        /// </summary>
        public static bool TryGetParamByteLength(byte[] frame, out int length)
        {
            length = 0;
            if (frame == null || frame.Length < ParamOffset)
            {
                return false;
            }

            if (frame[ParamLengthOffset] != 0)
            {
                length = frame[ParamLengthOffset];
                return ParamOffset + length == frame.Length;
            }

            length = frame.Length - ParamOffset;
            return true;
        }
    }

    /// <summary>
    /// The fixed client command that starts the game's native treasure-map
    /// digging flow after the Jump has placed the character at the target.
    /// The target coordinates are carried by 0x5828; this command has no
    /// per-map fields and is sent once for each target.
    /// </summary>
    public static class TreasureAutoDigPacketContract
    {
        public const int ProtocolId = 0xB0F4;
        public const int LengthFieldOffset = 8;
        public const int ProtocolOffset = 10;
        public const int FrameLength = 24;
        public const int PayloadLength = 12;

        private static readonly byte[] FrameBytes =
        {
            0x4D, 0x5A,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C,
            0xB0, 0xF4,
            0x00, 0x02, 0x00, 0x00, 0x00, 0x07,
            0xD0, 0x00, 0x00, 0x00, 0x07, 0xD1
        };

        // Existing ordinary send presets may contain the older 22-byte
        // representation of the same saved 0xB0F4 command. Keep it as an
        // explicit closed contract so the assistant can reuse that preset
        // byte-for-byte instead of inventing a new payload.
        private static readonly byte[] LegacyFrameBytes =
        {
            0x4D, 0x5A,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C,
            0xB0, 0xF4,
            0x00, 0x02, 0x00, 0x00, 0x07, 0xD0,
            0x00, 0x00, 0x07, 0xD1
        };

        public static byte[] CreateFrame()
        {
            return (byte[])FrameBytes.Clone();
        }

        public static bool IsFrame(byte[] frame)
        {
            if (frame == null)
            {
                return false;
            }

            if (frame.Length == FrameBytes.Length && Matches(frame, FrameBytes))
            {
                return true;
            }

            return frame.Length == LegacyFrameBytes.Length &&
                Matches(frame, LegacyFrameBytes);
        }

        private static bool Matches(byte[] frame, byte[] expected)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (frame[i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
