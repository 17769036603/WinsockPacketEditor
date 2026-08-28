using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 纯发包执行器。默认发送器永远 fail-closed；只有显式注入发送器才会执行发送调用。
    /// </summary>
    public class EquipmentRefineExecutor
    {
        public enum RefineSendResult
        {
            Unknown = 0,
            Accepted = 1,
            NotSupported = 2,
            TemplateNotFound = 3,
            AuthorizationRequired = 4,
            Failed = 5,
            Timeout = 6,
            InvalidRequest = 7
        }

        public enum RefineType
        {
            Normal = 0,
            Superior = 1,
            Legendary = 2
        }

        public enum PacketFieldSource
        {
            None = 0,
            SlotIndex = 1,
            RefineType = 2,
            EquipmentId = 3,
            EquipmentIdAscii = 4,
            OperationCode = 5,
            TypeCode = 6,
            ItemIdAscii = 7
        }

        public enum PacketByteOrder
        {
            LittleEndian = 0,
            BigEndian = 1
        }

        public class RefineRequest
        {
            public int SlotIndex { get; set; } = -1;
            public string MemberIdentity { get; set; } = string.Empty;
            public string EquipmentId { get; set; } = string.Empty;
            public string ItemId { get; set; } = string.Empty;
            public string ItemTypeId { get; set; } = string.Empty;
            public string EquipmentName { get; set; } = string.Empty;
            public RefineType Type { get; set; } = RefineType.Normal;
            public int TypeCode { get; set; } = -1;
            public int OperationCode { get; set; } = -1;
            public int WaitTimeoutMs { get; set; } = 3000;
            public bool WaitForResult { get; set; } = true;
        }

        public class RefinePacketField
        {
            public string Name { get; set; } = string.Empty;
            public int Offset { get; set; } = -1;
            public int Length { get; set; } = 0;
            public PacketFieldSource Source { get; set; } = PacketFieldSource.None;
            public PacketByteOrder ByteOrder { get; set; } = PacketByteOrder.LittleEndian;
            public bool Verified { get; set; }

            public bool IsValid
            {
                get
                {
                    int maxLength = this.Source == PacketFieldSource.EquipmentIdAscii ||
                        this.Source == PacketFieldSource.ItemIdAscii ? 256 : 8;
                    return this.Verified &&
                        this.Offset >= 0 &&
                        this.Length > 0 &&
                        this.Length <= maxLength &&
                        this.Source != PacketFieldSource.None;
                }
            }
        }

        public class RefinePacketTemplate
        {
            public int ProtocolId { get; set; }
            public byte[] FixtureBytes { get; set; } = new byte[0];
            public byte[] VariableBytes { get; set; } = new byte[0];
            public List<RefinePacketField> Fields { get; set; } = new List<RefinePacketField>();
            public string Description { get; set; } = string.Empty;
            public string EvidenceId { get; set; } = string.Empty;
            public DateTime CapturedTime { get; set; } = DateTime.MinValue;
            public bool ProtocolVerified { get; set; }

            [JsonIgnore]
            public bool IsValid
            {
                get
                {
                    if (!this.ProtocolVerified ||
                        this.ProtocolId <= 0 ||
                        this.FixtureBytes == null ||
                        this.FixtureBytes.Length == 0 ||
                        string.IsNullOrWhiteSpace(this.EvidenceId) ||
                        this.Fields == null ||
                        this.Fields.Count == 0 ||
                        this.Fields.Any(field => field == null || !field.IsValid))
                    {
                        return false;
                    }

                    return this.Fields.All(field => field.Offset + field.Length <= this.FixtureBytes.Length);
                }
            }
        }

        public interface IRefinePacketSender
        {
            bool IsAuthorized { get; }
            Task<bool> SendAsync(byte[] packet, CancellationToken cancellationToken);
        }

        /// <summary>
        /// 默认发送器。没有真实发送能力，保证未注入适配器时不会发包。
        /// </summary>
        public sealed class FailClosedPacketSender : IRefinePacketSender
        {
            public bool IsAuthorized { get { return false; } }

            public Task<bool> SendAsync(byte[] packet, CancellationToken cancellationToken)
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 离线测试发送器，只记录字节，不访问网络或游戏进程。
        /// </summary>
        public sealed class RecordingPacketSender : IRefinePacketSender
        {
            private readonly List<byte[]> _sentPackets = new List<byte[]>();

            public RecordingPacketSender(bool authorized)
            {
                this.IsAuthorized = authorized;
            }

            public bool IsAuthorized { get; private set; }

            public IReadOnlyList<byte[]> SentPackets
            {
                get { return this._sentPackets.AsReadOnly(); }
            }

            public Task<bool> SendAsync(byte[] packet, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!this.IsAuthorized || packet == null || packet.Length == 0)
                {
                    return Task.FromResult(false);
                }

                this._sentPackets.Add((byte[])packet.Clone());
                return Task.FromResult(true);
            }
        }

        private readonly IRefinePacketSender _sender;
        private RefinePacketTemplate _template;

        public EquipmentRefineExecutor(
            RefinePacketTemplate template = null,
            IRefinePacketSender sender = null)
        {
            this._template = template;
            this._sender = sender ?? new FailClosedPacketSender();
        }

        public RefinePacketTemplate Template
        {
            get { return this._template; }
            set { this._template = value; }
        }

        public async Task<RefineSendResult> SendRefinePacketAsync(
            RefineRequest request,
            int slotIndex = -1,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null) return RefineSendResult.InvalidRequest;
            if (slotIndex >= 0) request.SlotIndex = slotIndex;
            if (request.SlotIndex < 0 && RequiresSlotIndex(this._template))
            {
                return RefineSendResult.InvalidRequest;
            }
            if (!this._sender.IsAuthorized) return RefineSendResult.AuthorizationRequired;
            if (this._template == null || !this._template.IsValid) return RefineSendResult.TemplateNotFound;

            byte[] packet;
            string error;
            if (!TryBuildRefinePacket(this._template, request, out packet, out error))
            {
                return string.Equals(error, "template_invalid", StringComparison.Ordinal)
                    ? RefineSendResult.TemplateNotFound
                    : RefineSendResult.NotSupported;
            }

            bool sent = await this._sender.SendAsync(packet, cancellationToken);
            return sent ? RefineSendResult.Accepted : RefineSendResult.Failed;
        }

        public static bool TryBuildRefinePacket(
            RefinePacketTemplate template,
            RefineRequest request,
            out byte[] packet,
            out string error)
        {
            packet = null;
            error = string.Empty;
            if (template == null || !template.IsValid)
            {
                error = "template_invalid";
                return false;
            }
            if (request == null ||
                (request.SlotIndex < 0 && RequiresSlotIndex(template)))
            {
                error = "request_invalid";
                return false;
            }

            packet = (byte[])template.FixtureBytes.Clone();
            foreach (RefinePacketField field in template.Fields)
            {
                if (field.Source == PacketFieldSource.EquipmentIdAscii ||
                    field.Source == PacketFieldSource.ItemIdAscii)
                {
                    if (!WriteAscii(
                        packet,
                        field.Offset,
                        field.Length,
                        field.Source == PacketFieldSource.ItemIdAscii
                            ? request.ItemId
                            : request.EquipmentId))
                    {
                        packet = null;
                        error = "field_write_failed:" + field.Name;
                        return false;
                    }

                    continue;
                }

                long value;
                if (!TryResolveFieldValue(field.Source, request, out value))
                {
                    packet = null;
                    error = "field_value_missing:" + field.Name;
                    return false;
                }

                if (!WriteInteger(packet, field.Offset, field.Length, field.ByteOrder, value))
                {
                    packet = null;
                    error = "field_write_failed:" + field.Name;
                    return false;
                }
            }

            return true;
        }

        private static bool RequiresSlotIndex(RefinePacketTemplate template)
        {
            return template != null && template.Fields != null &&
                template.Fields.Any(field => field != null &&
                    field.Source == PacketFieldSource.SlotIndex);
        }

        private static bool WriteAscii(
            byte[] buffer,
            int offset,
            int length,
            string value)
        {
            if (buffer == null ||
                offset < 0 ||
                length <= 0 ||
                offset + length > buffer.Length ||
                string.IsNullOrEmpty(value) ||
                value.Any(character => character > 0x7F))
            {
                return false;
            }

            byte[] bytes = Encoding.ASCII.GetBytes(value);
            if (bytes.Length != length)
            {
                return false;
            }

            Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
            return true;
        }

        private static bool TryResolveFieldValue(
            PacketFieldSource source,
            RefineRequest request,
            out long value)
        {
            value = 0;
            switch (source)
            {
                case PacketFieldSource.SlotIndex:
                    value = request.SlotIndex;
                    return true;
                case PacketFieldSource.RefineType:
                    value = (long)request.Type;
                    return true;
                case PacketFieldSource.TypeCode:
                    if (request.TypeCode < 0) return false;
                    value = request.TypeCode;
                    return true;
                case PacketFieldSource.EquipmentId:
                    return long.TryParse(
                        request.EquipmentId,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out value);
                case PacketFieldSource.OperationCode:
                    if (request.OperationCode < 0) return false;
                    value = request.OperationCode;
                    return true;
                default:
                    return false;
            }
        }

        private static bool WriteInteger(
            byte[] buffer,
            int offset,
            int length,
            PacketByteOrder byteOrder,
            long value)
        {
            if (buffer == null || offset < 0 || length <= 0 || length > 8 || offset + length > buffer.Length)
            {
                return false;
            }

            ulong unsignedValue = unchecked((ulong)value);
            for (int index = 0; index < length; index++)
            {
                int targetIndex = byteOrder == PacketByteOrder.LittleEndian
                    ? offset + index
                    : offset + length - 1 - index;
                buffer[targetIndex] = (byte)(unsignedValue >> (index * 8));
            }

            return true;
        }

        public static async Task<bool> WaitForResultChangeAsync(
            string previousHash,
            Func<Task<string>> currentHashReader,
            int timeoutMs,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (currentHashReader == null || timeoutMs <= 0) return false;

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string currentHash = await currentHashReader();
                if (!string.IsNullOrWhiteSpace(currentHash) &&
                    !string.Equals(previousHash, currentHash, StringComparison.Ordinal))
                {
                    return true;
                }

                int remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining <= 0) break;
                await Task.Delay(Math.Min(50, remaining), cancellationToken);
            }

            return false;
        }

        public void SaveTemplate(RefinePacketTemplate template, string savePath = null)
        {
            if (template == null) throw new ArgumentNullException("template");
            if (string.IsNullOrWhiteSpace(savePath)) savePath = GetTemplateStoragePath();
            string directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(
                savePath,
                JsonConvert.SerializeObject(template, Formatting.Indented),
                Encoding.UTF8);
        }

        public RefinePacketTemplate LoadTemplate(string loadPath = null)
        {
            if (string.IsNullOrWhiteSpace(loadPath)) loadPath = GetTemplateStoragePath();
            if (!File.Exists(loadPath)) return null;

            try
            {
                return JsonConvert.DeserializeObject<RefinePacketTemplate>(
                    File.ReadAllText(loadPath, Encoding.UTF8));
            }
            catch
            {
                return null;
            }
        }

        private static string GetTemplateStoragePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "WPE", "EquipmentRefine", "template.json");
        }

        public static RefinePacketTemplate DiscoverRefineTemplateFromCapture()
        {
            // 没有真实验收样本时不推断协议，不从捕获列表伪造模板。
            return null;
        }
    }
}
