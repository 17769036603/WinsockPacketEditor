using System;

namespace WPELibrary.Lib
{
    internal static class Socket_Operation
    {
        internal static bool IsHexString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Replace(" ", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\t", string.Empty);
            if (normalized.Length == 0 || normalized.Length % 2 != 0)
            {
                return false;
            }

            foreach (char character in normalized)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        internal static byte[] StringToBytes(
            Socket_Cache.SocketPacket.EncodingFormat format,
            string value)
        {
            if (format != Socket_Cache.SocketPacket.EncodingFormat.Hex ||
                !IsHexString(value))
            {
                return null;
            }

            string normalized = value.Replace(" ", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\t", string.Empty);
            byte[] result = new byte[normalized.Length / 2];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = Convert.ToByte(
                    normalized.Substring(index * 2, 2),
                    16);
            }

            return result;
        }
    }

    internal static class Socket_Cache
    {
        internal static class SocketPacket
        {
            internal enum EncodingFormat
            {
                Hex = 0
            }
        }
    }
}
