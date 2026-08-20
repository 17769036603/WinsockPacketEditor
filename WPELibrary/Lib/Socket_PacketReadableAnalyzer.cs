using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WPELibrary.Lib
{
    internal sealed class Socket_PacketReadableResult
    {
        public string KindKey { get; private set; }

        public string EncodingName { get; private set; }

        public string Details { get; private set; }

        public string Preview { get; private set; }

        public bool IsBinary { get; private set; }

        public bool IsEmpty { get; private set; }

        public bool IsTruncated { get; private set; }

        internal static Socket_PacketReadableResult Create(
            string kindKey,
            string encodingName,
            string details,
            string preview,
            bool isBinary,
            bool isEmpty,
            bool isTruncated)
        {
            return new Socket_PacketReadableResult
            {
                KindKey = kindKey,
                EncodingName = encodingName,
                Details = details,
                Preview = preview,
                IsBinary = isBinary,
                IsEmpty = isEmpty,
                IsTruncated = isTruncated
            };
        }
    }

    internal static class Socket_PacketReadableAnalyzer
    {
        private const int MaxPreviewChars = 8192;
        private const int MaxExtractedStrings = 20;
        private const int MinimumExtractedStringLength = 4;

        public static Socket_PacketReadableResult Analyze(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return Socket_PacketReadableResult.Create(
                    "UI_ReadableKind_Empty",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false,
                    true,
                    false);
            }

            Socket_PacketReadableResult knownFileResult = AnalyzeKnownFile(buffer);
            if (knownFileResult != null)
            {
                return knownFileResult;
            }

            string text;
            string encodingName;
            if (TryDecodeText(buffer, out text, out encodingName))
            {
                return AnalyzeText(text, encodingName);
            }

            string strings = ExtractReadableStrings(buffer);
            bool stringsTruncated;
            strings = LimitPreview(strings, out stringsTruncated);
            return Socket_PacketReadableResult.Create(
                "UI_ReadableKind_Binary",
                string.Empty,
                string.Empty,
                strings,
                true,
                false,
                stringsTruncated);
        }

        private static Socket_PacketReadableResult AnalyzeKnownFile(byte[] buffer)
        {
            if (HasBytes(buffer, 0, 0x4D, 0x5A))
            {
                int peHeaderOffset;
                if (TryGetPeHeaderOffset(buffer, out peHeaderOffset))
                {
                    return Socket_PacketReadableResult.Create(
                        "UI_ReadableKind_PE",
                        string.Empty,
                        string.Format(
                            "signature=MZ + PE\\0\\0; peOffset=0x{0:X}",
                            peHeaderOffset),
                        string.Empty,
                        true,
                        false,
                        false);
                }

                bool mzStringsTruncated;
                string mzPreview = GetReadableStringsPreview(buffer, out mzStringsTruncated);
                return Socket_PacketReadableResult.Create(
                    "UI_ReadableKind_MZ",
                    string.Empty,
                    "signature=MZ; PE header was not confirmed",
                    mzPreview,
                    true,
                    false,
                    mzStringsTruncated);
            }

            string kindKey;
            string signature;
            if (HasBytes(buffer, 0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
            {
                kindKey = "UI_ReadableKind_PNG";
                signature = "signature=PNG";
            }
            else if (HasBytes(buffer, 0, 0xFF, 0xD8, 0xFF))
            {
                kindKey = "UI_ReadableKind_JPEG";
                signature = "signature=JPEG";
            }
            else if (HasAscii(buffer, 0, "GIF8"))
            {
                kindKey = "UI_ReadableKind_GIF";
                signature = "signature=GIF";
            }
            else if (HasAscii(buffer, 0, "BM"))
            {
                kindKey = "UI_ReadableKind_BMP";
                signature = "signature=BMP";
            }
            else if (HasAscii(buffer, 0, "%PDF-"))
            {
                kindKey = "UI_ReadableKind_PDF";
                signature = "signature=PDF";
            }
            else if (HasBytes(buffer, 0, 0x50, 0x4B, 0x03, 0x04) ||
                HasBytes(buffer, 0, 0x50, 0x4B, 0x05, 0x06))
            {
                kindKey = "UI_ReadableKind_ZIP";
                signature = "signature=ZIP";
            }
            else if (HasBytes(buffer, 0, 0x1F, 0x8B))
            {
                kindKey = "UI_ReadableKind_GZip";
                signature = "signature=GZip";
            }
            else if (HasBytes(buffer, 0, 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07))
            {
                kindKey = "UI_ReadableKind_RAR";
                signature = "signature=RAR";
            }
            else if (HasBytes(buffer, 0, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C))
            {
                kindKey = "UI_ReadableKind_7z";
                signature = "signature=7z";
            }
            else
            {
                return null;
            }

            bool stringsTruncated;
            string stringsPreview = GetReadableStringsPreview(buffer, out stringsTruncated);
            return Socket_PacketReadableResult.Create(
                kindKey,
                string.Empty,
                signature,
                stringsPreview,
                true,
                false,
                stringsTruncated);
        }

        private static Socket_PacketReadableResult AnalyzeText(string text, string encodingName)
        {
            string normalizedText = text == null ? string.Empty : text.Trim('\uFEFF');
            string trimmedText = normalizedText.TrimStart();
            string kindKey = "UI_ReadableKind_Text";

            if (trimmedText.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) ||
                trimmedText.StartsWith("GET ", StringComparison.OrdinalIgnoreCase) ||
                trimmedText.StartsWith("POST ", StringComparison.OrdinalIgnoreCase) ||
                trimmedText.StartsWith("PUT ", StringComparison.OrdinalIgnoreCase) ||
                trimmedText.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase))
            {
                kindKey = "UI_ReadableKind_HTTP";
            }
            else if (trimmedText.StartsWith("{") || trimmedText.StartsWith("["))
            {
                try
                {
                    normalizedText = JToken.Parse(trimmedText).ToString(Newtonsoft.Json.Formatting.Indented);
                    kindKey = "UI_ReadableKind_JSON";
                }
                catch (JsonException)
                {
                    // Keep malformed JSON as ordinary text.
                }
            }
            else if (trimmedText.StartsWith("<"))
            {
                try
                {
                    normalizedText = XDocument.Parse(trimmedText).ToString();
                    kindKey = "UI_ReadableKind_XML";
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is XmlException)
                {
                    // Keep malformed XML as ordinary text.
                }
            }

            bool truncated;
            string preview = LimitPreview(normalizedText, out truncated);
            return Socket_PacketReadableResult.Create(
                kindKey,
                encodingName,
                string.Empty,
                preview,
                false,
                false,
                truncated);
        }

        private static bool TryDecodeText(byte[] buffer, out string text, out string encodingName)
        {
            text = null;
            encodingName = null;

            Encoding bomEncoding;
            int bomLength;
            if (TryGetBomEncoding(buffer, out bomEncoding, out bomLength))
            {
                string bomText;
                if (TryDecode(buffer, bomEncoding, bomLength, out bomText) && IsReadableText(bomText))
                {
                    text = bomText;
                    encodingName = GetEncodingName(bomEncoding);
                    return true;
                }
            }

            string utf8Text;
            if (TryDecode(buffer, new UTF8Encoding(false, true), 0, out utf8Text) &&
                IsReadableText(utf8Text))
            {
                text = utf8Text;
                encodingName = IsAscii(buffer) ? "ASCII" : "UTF-8";
                return true;
            }

            if (LooksLikeUtf16(buffer))
            {
                string utf16Text;
                Encoding utf16Encoding = IsLikelyBigEndianUtf16(buffer)
                    ? new UnicodeEncoding(true, false, true)
                    : new UnicodeEncoding(false, false, true);
                if (TryDecode(buffer, utf16Encoding, 0, out utf16Text) && IsReadableText(utf16Text))
                {
                    text = utf16Text;
                    encodingName = IsLikelyBigEndianUtf16(buffer) ? "UTF-16 BE" : "UTF-16 LE";
                    return true;
                }
            }

            try
            {
                Encoding gbk = Encoding.GetEncoding(
                    "GBK",
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
                string gbkText;
                if (TryDecode(buffer, gbk, 0, out gbkText) && IsReadableText(gbkText))
                {
                    text = gbkText;
                    encodingName = "GBK";
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // GBK is available on the target .NET Framework, but keep a safe fallback.
            }

            return false;
        }

        private static bool TryDecode(byte[] buffer, Encoding encoding, int offset, out string text)
        {
            text = null;
            try
            {
                text = encoding.GetString(buffer, offset, buffer.Length - offset);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool TryGetBomEncoding(byte[] buffer, out Encoding encoding, out int bomLength)
        {
            encoding = null;
            bomLength = 0;

            if (HasBytes(buffer, 0, 0x00, 0x00, 0xFE, 0xFF))
            {
                encoding = new UTF32Encoding(true, true, true);
                bomLength = 4;
            }
            else if (HasBytes(buffer, 0, 0xFF, 0xFE, 0x00, 0x00))
            {
                encoding = new UTF32Encoding(false, true, true);
                bomLength = 4;
            }
            else if (HasBytes(buffer, 0, 0xEF, 0xBB, 0xBF))
            {
                encoding = new UTF8Encoding(true, true);
                bomLength = 3;
            }
            else if (HasBytes(buffer, 0, 0xFE, 0xFF))
            {
                encoding = new UnicodeEncoding(true, true, true);
                bomLength = 2;
            }
            else if (HasBytes(buffer, 0, 0xFF, 0xFE))
            {
                encoding = new UnicodeEncoding(false, true, true);
                bomLength = 2;
            }

            return encoding != null;
        }

        private static string GetEncodingName(Encoding encoding)
        {
            if (encoding is UTF8Encoding)
            {
                return "UTF-8";
            }

            if (encoding is UTF32Encoding)
            {
                return encoding.CodePage == 12001 ? "UTF-32 BE" : "UTF-32 LE";
            }

            if (encoding is UnicodeEncoding)
            {
                return encoding.CodePage == 1201 ? "UTF-16 BE" : "UTF-16 LE";
            }

            return encoding.WebName;
        }

        private static bool IsReadableText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int readableCharacters = 0;
            foreach (char character in text)
            {
                if (character == '\uFFFD')
                {
                    return false;
                }

                if (char.IsControl(character) && character != '\r' && character != '\n' && character != '\t')
                {
                    return false;
                }

                if (char.IsLetterOrDigit(character) ||
                    char.IsPunctuation(character) ||
                    char.IsSymbol(character) ||
                    char.IsWhiteSpace(character))
                {
                    readableCharacters++;
                }
            }

            return readableCharacters > 0 && readableCharacters * 100 >= text.Length * 80;
        }

        private static bool LooksLikeUtf16(byte[] buffer)
        {
            if (buffer.Length < 4 || buffer.Length % 2 != 0)
            {
                return false;
            }

            int evenZeroes = 0;
            int oddZeroes = 0;
            for (int i = 0; i < buffer.Length; i += 2)
            {
                if (buffer[i] == 0) evenZeroes++;
                if (buffer[i + 1] == 0) oddZeroes++;
            }

            return Math.Max(evenZeroes, oddZeroes) * 100 >= buffer.Length * 20;
        }

        private static bool IsLikelyBigEndianUtf16(byte[] buffer)
        {
            int evenZeroes = 0;
            int oddZeroes = 0;
            for (int i = 0; i + 1 < buffer.Length; i += 2)
            {
                if (buffer[i] == 0) evenZeroes++;
                if (buffer[i + 1] == 0) oddZeroes++;
            }

            return evenZeroes > oddZeroes;
        }

        private static bool IsAscii(byte[] buffer)
        {
            return buffer.All(value => value <= 0x7F);
        }

        private static string ExtractReadableStrings(byte[] buffer)
        {
            List<string> strings = new List<string>();
            StringBuilder current = new StringBuilder();

            Action flush = () =>
            {
                if (current.Length >= MinimumExtractedStringLength && strings.Count < MaxExtractedStrings)
                {
                    strings.Add(current.ToString());
                }

                current.Clear();
            };

            foreach (byte value in buffer)
            {
                if (value >= 0x20 && value <= 0x7E)
                {
                    current.Append((char)value);
                }
                else
                {
                    flush();
                }
            }

            flush();
            return string.Join(Environment.NewLine, strings);
        }

        private static string GetReadableStringsPreview(byte[] buffer, out bool truncated)
        {
            return LimitPreview(ExtractReadableStrings(buffer), out truncated);
        }

        private static string LimitPreview(string value, out bool truncated)
        {
            if (string.IsNullOrEmpty(value))
            {
                truncated = false;
                return string.Empty;
            }

            if (value.Length <= MaxPreviewChars)
            {
                truncated = false;
                return value;
            }

            truncated = true;
            return value.Substring(0, MaxPreviewChars);
        }

        private static bool TryGetPeHeaderOffset(byte[] buffer, out int peHeaderOffset)
        {
            peHeaderOffset = 0;
            if (buffer.Length < 0x40)
            {
                return false;
            }

            peHeaderOffset = BitConverter.ToInt32(buffer, 0x3C);
            return peHeaderOffset >= 0 &&
                peHeaderOffset <= buffer.Length - 4 &&
                HasBytes(buffer, peHeaderOffset, 0x50, 0x45, 0x00, 0x00);
        }

        private static bool HasAscii(byte[] buffer, int offset, string value)
        {
            return HasBytes(buffer, offset, Encoding.ASCII.GetBytes(value));
        }

        private static bool HasBytes(byte[] buffer, int offset, byte[] values)
        {
            if (buffer == null || values == null || offset < 0 || offset + values.Length > buffer.Length)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (buffer[offset + i] != values[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasBytes(byte[] buffer, int offset, params int[] values)
        {
            if (buffer == null || offset < 0 || offset + values.Length > buffer.Length)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (buffer[offset + i] != (byte)values[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
