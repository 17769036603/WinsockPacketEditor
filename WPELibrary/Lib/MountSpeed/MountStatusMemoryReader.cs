using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.Memory;

namespace WPELibrary.Lib.MountSpeed
{
    public enum MountStatusValueType
    {
        Int32,
        Int64,
        UInt32,
        Byte,
        Boolean,
        Float,
        Double
    }

    public sealed class MountStatusMemoryLayout
    {
        public long MountIdAddress { get; set; }
        public MountStatusValueType MountIdType { get; set; } = MountStatusValueType.Int32;

        public long IsMountedAddress { get; set; }
        public MountStatusValueType IsMountedType { get; set; } = MountStatusValueType.Int32;

        public long BaseSpeedAddress { get; set; }
        public long RideAddSpeedAddress { get; set; }
        public long RoleMoveSpeedAddress { get; set; }
        public MountStatusValueType SpeedType { get; set; } = MountStatusValueType.Float;

        public bool IsValid(out string error)
        {
            if (MountIdAddress > 0 && !IsIntegerType(MountIdType))
            {
                error = "Mount ID type must be an integer type.";
                return false;
            }

            if (IsMountedAddress <= 0)
            {
                error = "Mounted flag address is not configured.";
                return false;
            }

            if (!IsIntegerType(IsMountedType))
            {
                error = "Mounted flag type must be an integer type.";
                return false;
            }

            if (!IsNumericType(SpeedType))
            {
                error = "Speed type must be a numeric type.";
                return false;
            }

            error = null;
            return true;
        }

        public MountStatusMemoryLayout Clone()
        {
            return new MountStatusMemoryLayout
            {
                MountIdAddress = MountIdAddress,
                MountIdType = MountIdType,
                IsMountedAddress = IsMountedAddress,
                IsMountedType = IsMountedType,
                BaseSpeedAddress = BaseSpeedAddress,
                RideAddSpeedAddress = RideAddSpeedAddress,
                RoleMoveSpeedAddress = RoleMoveSpeedAddress,
                SpeedType = SpeedType
            };
        }

        internal static bool IsIntegerType(MountStatusValueType valueType)
        {
            return valueType == MountStatusValueType.Int32
                || valueType == MountStatusValueType.Int64
                || valueType == MountStatusValueType.UInt32
                || valueType == MountStatusValueType.Byte
                || valueType == MountStatusValueType.Boolean;
        }

        internal static bool IsNumericType(MountStatusValueType valueType)
        {
            return IsIntegerType(valueType)
                || valueType == MountStatusValueType.Float
                || valueType == MountStatusValueType.Double;
        }
    }

    public sealed class MountStatusSnapshot
    {
        public Guid SessionId { get; internal set; }
        public long Sequence { get; internal set; }
        public DateTime ReadAt { get; internal set; }
        public int ProcessId { get; internal set; }
        public long ProcessStartTimeUtcTicks { get; internal set; }
        public string ProcessName { get; internal set; }

        public long? MountId { get; internal set; }
        public bool? IsMounted { get; internal set; }
        public double? GrowthRate { get; internal set; }
        public string GrowthRateSource { get; internal set; }
        public double? BaseSpeed { get; internal set; }
        public double? RideAddSpeed { get; internal set; }
        public double? RoleMoveSpeed { get; internal set; }
        public string OptionalReadError { get; internal set; }

        /// <summary>
        /// Optional LocalRide projection copied from the Android read-only
        /// snapshot. The Windows explicit-address reader leaves these fields
        /// empty; the Android startup preflight fills them with the current
        /// ride instance and its skills.
        /// </summary>
        public IList<MountRideInstanceSnapshot> RideInstances { get; internal set; }

        public string ActiveRideInstanceId { get; internal set; }

        public string RideBindingStatus { get; internal set; }

        public string RideBindingSource { get; internal set; }

        public bool IsValid
        {
            get { return IsMounted.HasValue; }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "mountId={0}, mounted={1}, growthRate={2}, baseSpeed={3}, rideAddSpeed={4}, roleMoveSpeed={5}, sequence={6}",
                FormatValue(MountId),
                FormatValue(IsMounted),
                FormatValue(GrowthRate),
                FormatValue(BaseSpeed),
                FormatValue(RideAddSpeed),
                FormatValue(RoleMoveSpeed),
                Sequence);
        }

        private static string FormatValue<T>(T? value) where T : struct
        {
            return value.HasValue ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) : "<unread>";
        }
    }

    public sealed class MountStatusReadResult
    {
        public bool Succeeded { get; private set; }
        public string Code { get; private set; }
        public string Error { get; private set; }
        public MountStatusSnapshot Snapshot { get; private set; }

        internal static MountStatusReadResult Success(MountStatusSnapshot snapshot)
        {
            return new MountStatusReadResult
            {
                Succeeded = true,
                Code = "ok",
                Snapshot = snapshot
            };
        }

        internal static MountStatusReadResult Failure(string code, string error)
        {
            return new MountStatusReadResult
            {
                Succeeded = false,
                Code = code,
                Error = error ?? code
            };
        }
    }

    public sealed class MountStatusMemoryReader : IDisposable
    {
        private const string TraceComponent = "mount.status";

        private readonly ReadOnlyProcessMemoryReader reader;
        private readonly ReadOnlyProcessIdentity identity;
        private readonly Guid sessionId = Guid.NewGuid();
        private MountStatusMemoryLayout layout;
        private long sequence;
        private bool disposed;

        private MountStatusMemoryReader(
            ReadOnlyProcessMemoryReader reader,
            ReadOnlyProcessIdentity identity)
        {
            this.reader = reader;
            this.identity = identity;
        }

        public ReadOnlyProcessIdentity Identity
        {
            get { return identity; }
        }

        public MountStatusMemoryLayout Layout
        {
            get { return layout == null ? null : layout.Clone(); }
        }

        public static bool TryCreate(
            int processId,
            out MountStatusMemoryReader mountReader,
            out string error)
        {
            mountReader = null;
            error = null;

            ReadOnlyProcessIdentity identity;
            if (!ReadOnlyProcessIdentity.TryCapture(processId, out identity, out error))
            {
                return false;
            }

            ReadOnlyProcessMemoryReader processReader;
            if (!ReadOnlyProcessMemoryReader.TryOpen(identity, out processReader, out error))
            {
                return false;
            }

            mountReader = new MountStatusMemoryReader(processReader, identity);
            return true;
        }

        public bool ConfigureLayout(MountStatusMemoryLayout value, out string error)
        {
            error = null;
            if (disposed)
            {
                error = "Mount status reader is disposed.";
                return false;
            }

            if (value == null)
            {
                error = "Mount status memory layout is not configured.";
                return false;
            }

            if (!value.IsValid(out error))
            {
                Trace("layout_rejected", 0, 0, error, false);
                return false;
            }

            layout = value.Clone();
            Trace("layout_configured", 0, 0, null, true);
            return true;
        }

        public Task<MountStatusReadResult> ReadSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ReadSnapshot(cancellationToken));
        }

        public Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string error = null;
            bool matches = !disposed && reader.EnsureProcessIdentity(out error);
            if (!matches)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "Mount status reader is disposed.";
                }

                Trace("process_identity_mismatch", 0, 0, error, false);
            }

            return Task.FromResult(matches);
        }

        private MountStatusReadResult ReadSnapshot(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (disposed)
            {
                return Failure("disposed", "Mount status reader is disposed.");
            }

            MountStatusMemoryLayout configuredLayout = layout;
            string layoutError = null;
            if (configuredLayout == null || !configuredLayout.IsValid(out layoutError))
            {
                return Failure("layout_not_configured", layoutError ?? "Mount status memory layout is not configured.");
            }

            string identityError;
            if (!reader.EnsureProcessIdentity(out identityError))
            {
                return Failure("process_identity_mismatch", identityError);
            }

            bool mounted;
            string error;
            if (!TryReadBoolean(configuredLayout.IsMountedAddress, configuredLayout.IsMountedType, out mounted, out error))
            {
                return Failure("mounted_flag_unreadable", error);
            }

            var snapshot = new MountStatusSnapshot
            {
                SessionId = sessionId,
                Sequence = ++sequence,
                ReadAt = DateTime.UtcNow,
                ProcessId = identity.ProcessId,
                ProcessStartTimeUtcTicks = identity.StartTimeUtcTicks,
                ProcessName = identity.ProcessName,
                IsMounted = mounted
            };

            var optionalErrors = new List<string>();
            if (configuredLayout.MountIdAddress > 0)
            {
                long mountId;
                if (TryReadInteger(configuredLayout.MountIdAddress, configuredLayout.MountIdType, out mountId, out error))
                {
                    snapshot.MountId = mountId;
                }
                else
                {
                    optionalErrors.Add("mountId: " + error);
                }
            }

            ReadOptionalSpeed(configuredLayout.BaseSpeedAddress, configuredLayout.SpeedType, "baseSpeed", snapshot, optionalErrors);
            ReadOptionalSpeed(configuredLayout.RideAddSpeedAddress, configuredLayout.SpeedType, "rideAddSpeed", snapshot, optionalErrors);
            ReadOptionalSpeed(configuredLayout.RoleMoveSpeedAddress, configuredLayout.SpeedType, "roleMoveSpeed", snapshot, optionalErrors);
            snapshot.OptionalReadError = optionalErrors.Count == 0 ? null : string.Join("; ", optionalErrors);

            Trace("read_snapshot", 0, 0, snapshot.OptionalReadError, true, snapshot.ToString());
            return MountStatusReadResult.Success(snapshot);
        }

        private void ReadOptionalSpeed(
            long address,
            MountStatusValueType valueType,
            string fieldName,
            MountStatusSnapshot snapshot,
            ICollection<string> errors)
        {
            if (address <= 0)
            {
                return;
            }

            double value;
            string error;
            if (!TryReadNumber(address, valueType, out value, out error))
            {
                errors.Add(fieldName + ": " + error);
                return;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                errors.Add(fieldName + ": numeric value is not finite.");
                return;
            }

            if (fieldName == "baseSpeed")
            {
                snapshot.BaseSpeed = value;
            }
            else if (fieldName == "rideAddSpeed")
            {
                snapshot.RideAddSpeed = value;
            }
            else
            {
                snapshot.RoleMoveSpeed = value;
            }
        }

        private bool TryReadInteger(
            long address,
            MountStatusValueType valueType,
            out long value,
            out string error)
        {
            value = 0;
            error = null;

            switch (valueType)
            {
                case MountStatusValueType.Int32:
                    int int32Value;
                    if (!reader.TryReadInt32(address, out int32Value, out error))
                    {
                        return false;
                    }

                    value = int32Value;
                    return true;
                case MountStatusValueType.Int64:
                    long int64Value;
                    if (!reader.TryReadInt64(address, out int64Value, out error))
                    {
                        return false;
                    }

                    value = int64Value;
                    return true;
                case MountStatusValueType.UInt32:
                    uint uint32Value;
                    if (!reader.TryReadUInt32(address, out uint32Value, out error))
                    {
                        return false;
                    }

                    value = uint32Value;
                    return true;
                case MountStatusValueType.Byte:
                case MountStatusValueType.Boolean:
                    byte[] bytes;
                    if (!reader.TryReadBytes(address, 1, out bytes, out error))
                    {
                        return false;
                    }

                    value = bytes[0];
                    return true;
                default:
                    error = "Unsupported integer value type: " + valueType + ".";
                    return false;
            }
        }

        private bool TryReadBoolean(
            long address,
            MountStatusValueType valueType,
            out bool value,
            out string error)
        {
            long integerValue;
            if (!TryReadInteger(address, valueType, out integerValue, out error))
            {
                value = false;
                return false;
            }

            value = integerValue != 0;
            return true;
        }

        private bool TryReadNumber(
            long address,
            MountStatusValueType valueType,
            out double value,
            out string error)
        {
            value = 0;
            error = null;

            switch (valueType)
            {
                case MountStatusValueType.Float:
                    byte[] floatBytes;
                    if (!reader.TryReadBytes(address, 4, out floatBytes, out error))
                    {
                        return false;
                    }

                    value = BitConverter.ToSingle(floatBytes, 0);
                    return true;
                case MountStatusValueType.Double:
                    byte[] doubleBytes;
                    if (!reader.TryReadBytes(address, 8, out doubleBytes, out error))
                    {
                        return false;
                    }

                    value = BitConverter.ToDouble(doubleBytes, 0);
                    return true;
                default:
                    long integerValue;
                    if (!TryReadInteger(address, valueType, out integerValue, out error))
                    {
                        return false;
                    }

                    value = integerValue;
                    return true;
            }
        }

        private MountStatusReadResult Failure(string code, string error)
        {
            Trace(code, 0, 0, error, false);
            return MountStatusReadResult.Failure(code, error);
        }

        private void Trace(
            string eventName,
            long address,
            int length,
            string error,
            bool success,
            string preview = null)
        {
            MemoryTraceSession.Trace(
                TraceComponent,
                eventName,
                address,
                length,
                null,
                preview,
                success,
                error,
                0);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            reader.Dispose();
        }
    }
}
