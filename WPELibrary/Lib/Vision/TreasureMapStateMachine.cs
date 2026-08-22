using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.Vision
{
    public enum TreasureMapExecutionMode
    {
        CurrentSnapshot = 0,
        Continuous = 1
    }

    public enum TreasureControlMode
    {
        Exclusive = 0,
        Cooperative = 1
    }

    public enum TreasureEvidenceMode
    {
        Shadow = 0,
        Enforced = 1
    }

    public enum TreasureTargetOutcome
    {
        Success = 0,
        Failure = 1,
        Timeout = 2,
        ExternalController = 3
    }

    public enum TreasureJumpArrivalEvidenceType
    {
        EnterMap = 0xFFE1,
        GoToPos = 0xFFE2,
        PlayerJumpToPos = 0x1099
    }

    public enum TreasureMapState
    {
        Idle = 0,
        Preflight = 1,
        WaitingSnapshot = 2,
        SelectingTarget = 3,
        PreparingFreeze = 4,
        FreezingIdentity = 5,
        PreparingJump = 6,
        SendingJump = 7,
        WaitingArrival = 8,
        // Compatibility alias for the pre-evidence runner implementation.
        WaitingAfterJump = 8,
        ArrivalConfirmed = 9,
        ArrivalFallback = 10,
        RevalidatingTarget = 11,
        PreparingUse = 12,
        SendingUse = 13,
        WaitingConsumption = 14,
        WaitingExternalController = 15,
        ControllerConflict = 16,
        TargetCompleted = 17,
        WaitingNextTarget = 18,
        Paused = 19,
        Stopping = 20,
        Completed = 21,
        Failed = 22,
        Ambiguous = 23,
        SlowRecovery = 24,
        PreparingAutoDig = 25,
        SendingAutoDig = 26
    }

    public enum TreasureMapPacketSendDisposition
    {
        NotDispatched = 0,
        Dispatched = 1,
        Ambiguous = 2
    }

    public sealed class TreasureJumpArrivalEvidence
    {
        public TreasureJumpArrivalEvidenceType EvidenceType { get; private set; }
        public int MapId { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public DateTime ObservedUtc { get; private set; }

        public TreasureJumpArrivalEvidence(
            TreasureJumpArrivalEvidenceType evidenceType,
            int mapId,
            int x,
            int y,
            DateTime observedUtc)
        {
            this.EvidenceType = evidenceType;
            this.MapId = mapId;
            this.X = x;
            this.Y = y;
            this.ObservedUtc = observedUtc;
        }

        public bool MatchesTarget(int expectedMapId, int expectedX, int expectedY)
        {
            return this.MapId == expectedMapId && this.X == expectedX && this.Y == expectedY;
        }
    }

    public sealed class TreasureUseResultEvidence
    {
        public bool Consumed { get; private set; }
        public int Result { get; private set; }
        public DateTime ObservedUtc { get; private set; }

        public TreasureUseResultEvidence(bool consumed, int result, DateTime observedUtc)
        {
            this.Consumed = consumed;
            this.Result = result;
            this.ObservedUtc = observedUtc;
        }
    }

    public sealed class TreasureMapTargetIdentity : IEquatable<TreasureMapTargetIdentity>
    {
        public TreasureMapTargetIdentity(
            string streamSessionId,
            string processIdentity,
            string containerIdentity,
            string memberIdentity,
            int packageNum)
        {
            this.StreamSessionId = streamSessionId ?? string.Empty;
            this.ProcessIdentity = processIdentity ?? string.Empty;
            this.ContainerIdentity = containerIdentity ?? string.Empty;
            this.MemberIdentity = memberIdentity ?? string.Empty;
            this.PackageNum = packageNum;
        }

        public string StreamSessionId { get; private set; }
        public string ProcessIdentity { get; private set; }
        public string ContainerIdentity { get; private set; }
        public string MemberIdentity { get; private set; }
        public int PackageNum { get; private set; }

        public bool Equals(TreasureMapTargetIdentity other)
        {
            if (other == null) return false;
            return string.Equals(this.StreamSessionId, other.StreamSessionId, StringComparison.Ordinal) &&
                   string.Equals(this.ProcessIdentity, other.ProcessIdentity, StringComparison.Ordinal) &&
                   string.Equals(this.ContainerIdentity, other.ContainerIdentity, StringComparison.Ordinal) &&
                   string.Equals(this.MemberIdentity, other.MemberIdentity, StringComparison.Ordinal) &&
                   this.PackageNum == other.PackageNum;
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as TreasureMapTargetIdentity);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 0;
                hash = (hash * 397) ^ (this.StreamSessionId?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (this.ProcessIdentity?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (this.ContainerIdentity?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (this.MemberIdentity?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ this.PackageNum;
                return hash;
            }
        }
    }

    public sealed class TreasureMapTargetVersion : IEquatable<TreasureMapTargetVersion>
    {
        public TreasureMapTargetVersion(int scene, int x, int y)
        {
            this.Scene = scene;
            this.X = x;
            this.Y = y;
        }

        public int Scene { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }

        public bool Equals(TreasureMapTargetVersion other)
        {
            if (other == null) return false;
            return this.Scene == other.Scene &&
                   this.X == other.X &&
                   this.Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as TreasureMapTargetVersion);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = this.Scene;
                hash = (hash * 397) ^ this.X;
                hash = (hash * 397) ^ this.Y;
                return hash;
            }
        }
    }

    public sealed class TreasureMapActionLog
    {
        public TreasureMapActionLog(
            Guid runId,
            TreasureMapTargetIdentity target,
            TreasureMapState state,
            string code,
            int retryCount,
            string evidenceKey = null)
        {
            this.RunId = runId;
            this.TimestampUtc = DateTime.UtcNow;
            this.Target = target;
            this.State = state;
            this.Code = code ?? string.Empty;
            this.RetryCount = retryCount;
            this.EvidenceKey = evidenceKey;
        }

        public Guid RunId { get; private set; }
        public DateTime TimestampUtc { get; private set; }
        public TreasureMapTargetIdentity Target { get; private set; }
        public TreasureMapState State { get; private set; }
        public string Code { get; private set; }
        public int RetryCount { get; private set; }
        public string EvidenceKey { get; private set; }
    }
}
