using System;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.Memory;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// Real, read-only adapter that reads mount-speed fields from the injected
    /// game process using <see cref="ReadOnlyProcessMemoryReader"/>. Addresses are
    /// explicit and version-specific; an unset (0) address yields an invalid field
    /// rather than guessing. Opening the reader also drives the memory trace log.
    /// </summary>
    public sealed class MountSpeedReadOnlyAdapter : IMountSpeedReadOnlyAdapter, IDisposable
    {
        private readonly ReadOnlyProcessMemoryReader reader;
        private readonly ReadOnlyProcessIdentity identity;
        private long baseSpeedAddress;
        private long rideAddSpeedAddress;
        private long roleMoveSpeedAddress;
        private long isMountedAddress;
        private SpeedValueType speedValueType = SpeedValueType.Float;
        private bool disposed;

        private MountSpeedReadOnlyAdapter(
            ReadOnlyProcessMemoryReader reader,
            ReadOnlyProcessIdentity identity)
        {
            this.reader = reader;
            this.identity = identity;
        }

        /// <summary>
        /// Creates a real read-only adapter for the given game process id.
        /// The reader is opened immediately; this is also what starts the memory
        /// trace session (the "connected" point) for the injected game process.
        /// </summary>
        public static bool TryCreate(
            int processId,
            out MountSpeedReadOnlyAdapter adapter,
            out string error)
        {
            adapter = null;
            error = string.Empty;

            if (processId <= 0)
            {
                error = "游戏进程 ID 无效。";
                return false;
            }

            ReadOnlyProcessIdentity identity;
            if (!ReadOnlyProcessIdentity.TryCapture(processId, out identity, out error))
            {
                return false;
            }

            ReadOnlyProcessMemoryReader reader;
            if (!ReadOnlyProcessMemoryReader.TryOpen(identity, out reader, out error))
            {
                return false;
            }

            adapter = new MountSpeedReadOnlyAdapter(reader, identity);
            return true;
        }

        /// <summary>
        /// Configures the explicit mount-speed memory addresses.
        /// </summary>
        public void SetMemoryAddresses(
            long baseSpeedAddress,
            long rideAddSpeedAddress,
            long roleMoveSpeedAddress,
            long isMountedAddress)
        {
            this.baseSpeedAddress = baseSpeedAddress;
            this.rideAddSpeedAddress = rideAddSpeedAddress;
            this.roleMoveSpeedAddress = roleMoveSpeedAddress;
            this.isMountedAddress = isMountedAddress;
        }

        public void SetSpeedValueType(SpeedValueType valueType)
        {
            this.speedValueType = valueType;
        }

        public Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken)
        {
            string ignored;
            return Task.FromResult(this.reader != null && this.reader.EnsureProcessIdentity(out ignored));
        }

        public Task<MountSpeedSnapshot> ReadMountSpeedAsync(CancellationToken cancellationToken)
        {
            MountSpeedSnapshot snapshot = new MountSpeedSnapshot
            {
                ReadAt = DateTime.UtcNow
            };

            if (this.reader == null)
            {
                return Task.FromResult(snapshot);
            }

            double baseSpeed;
            if (this.ReadSpeed(this.baseSpeedAddress, out baseSpeed, cancellationToken))
            {
                snapshot.BaseSpeed = baseSpeed;
            }

            double rideAddSpeed;
            if (this.ReadSpeed(this.rideAddSpeedAddress, out rideAddSpeed, cancellationToken))
            {
                snapshot.RideAddSpeed = rideAddSpeed;
            }

            double roleMoveSpeed;
            if (this.ReadSpeed(this.roleMoveSpeedAddress, out roleMoveSpeed, cancellationToken))
            {
                snapshot.RoleMoveSpeed = roleMoveSpeed;
            }

            int mounted;
            if (this.ReadInt32(this.isMountedAddress, out mounted, cancellationToken))
            {
                snapshot.IsMounted = mounted != 0;
            }

            MemoryTraceSession.Trace(
                "mountspeed", "read_snapshot", 0, 0, null, snapshot.ToString(), true, null, 0);
            return Task.FromResult(snapshot);
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.reader?.Dispose();
        }

        private bool ReadSpeed(long address, out double value, CancellationToken cancellationToken)
        {
            value = 0;
            if (address <= 0)
            {
                MemoryTraceSession.Trace(
                    "mountspeed", "read_skip", 0, 0, null, null, null, "address_not_configured", 0);
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            byte[] bytes;
            string error;
            if (this.speedValueType == SpeedValueType.Double)
            {
                if (!this.reader.TryReadBytes(address, sizeof(double), out bytes, out error))
                {
                    return false;
                }

                value = BitConverter.ToDouble(bytes, 0);
                return true;
            }

            if (this.speedValueType == SpeedValueType.Int32)
            {
                if (!this.reader.TryReadBytes(address, sizeof(int), out bytes, out error))
                {
                    return false;
                }

                value = BitConverter.ToInt32(bytes, 0);
                return true;
            }

            if (!this.reader.TryReadBytes(address, sizeof(float), out bytes, out error))
            {
                return false;
            }

            value = BitConverter.ToSingle(bytes, 0);
            return true;
        }

        private bool ReadInt32(long address, out int value, CancellationToken cancellationToken)
        {
            value = 0;
            if (address <= 0)
            {
                MemoryTraceSession.Trace(
                    "mountspeed", "read_skip", 0, 0, null, null, null, "address_not_configured", 0);
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            byte[] bytes;
            string error;
            if (!this.reader.TryReadBytes(address, sizeof(int), out bytes, out error))
            {
                return false;
            }

            value = BitConverter.ToInt32(bytes, 0);
            return true;
        }
    }
}
