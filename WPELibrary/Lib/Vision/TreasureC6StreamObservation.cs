using System;
using System.Collections.Concurrent;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// Thread-safe, read-only observation cache fed by receive-side hook
    /// frames. It never prepares or sends a game packet. Arrival evidence is
    /// deliberately kept in a FIFO so the runner can correlate it with the
    /// target revision and discard stale observations.
    /// </summary>
    public static class TreasureC6StreamObservation
    {
        private static readonly object cacheSync = new object();
        private static readonly ConcurrentQueue<TreasureJumpArrivalEvidence> arrivalQueue =
            new ConcurrentQueue<TreasureJumpArrivalEvidence>();
        private static readonly ConcurrentQueue<TreasureUseResultEvidence> useResultQueue =
            new ConcurrentQueue<TreasureUseResultEvidence>();

        private static TreasureC6InventorySnapshot currentSnapshot;
        private static TreasureJumpArrivalEvidence latestArrivalEvidence;
        private static TreasureUseResultEvidence latestUseResultEvidence;
        private static DateTime arrivalEvidenceObservedUtc = DateTime.MinValue;
        private static DateTime useResultEvidenceObservedUtc = DateTime.MinValue;

        public static TreasureC6InventorySnapshot GetCurrentSnapshot()
        {
            lock (cacheSync)
            {
                return currentSnapshot;
            }
        }

        /// <summary>Publishes the latest validated C6 snapshot for diagnostics.</summary>
        public static void PublishSnapshot(TreasureC6InventorySnapshot snapshot)
        {
            lock (cacheSync)
            {
                currentSnapshot = snapshot;
            }
        }

        public static TreasureJumpArrivalEvidence GetLatestArrivalEvidence()
        {
            lock (cacheSync)
            {
                return latestArrivalEvidence;
            }
        }

        public static TreasureUseResultEvidence GetLatestUseResultEvidence()
        {
            lock (cacheSync)
            {
                return latestUseResultEvidence;
            }
        }

        public static bool TryDequeueArrivalEvidence(
            out TreasureJumpArrivalEvidence evidence)
        {
            return arrivalQueue.TryDequeue(out evidence);
        }

        public static bool TryDequeueUseResultEvidence(
            out TreasureUseResultEvidence evidence)
        {
            return useResultQueue.TryDequeue(out evidence);
        }

        /// <summary>
        /// Compatibility API. Arrival frames do not carry a package number, so
        /// targetId is intentionally not used as a false correlation key. The
        /// runner must match map/coordinates and observation time instead.
        /// </summary>
        public static bool TryDequeuePendingArrivalEvidence(
            string targetId,
            out TreasureJumpArrivalEvidence evidence)
        {
            return TryDequeueArrivalEvidence(out evidence);
        }

        public static bool TryDequeuePendingUseResultEvidence(
            string targetId,
            out TreasureUseResultEvidence evidence)
        {
            return TryDequeueUseResultEvidence(out evidence);
        }

        internal static void OnArrivalEvidence(byte[] frame)
        {
            TreasureJumpArrivalEvidence evidence;
            try
            {
                evidence = TreasureEvidenceDecoder.DecodeJumpArrivalEvidenceByFrame(frame);
            }
            catch (TreasurePacketContractException)
            {
                return;
            }

            if (evidence == null)
            {
                return;
            }

            lock (cacheSync)
            {
                latestArrivalEvidence = evidence;
                arrivalEvidenceObservedUtc = DateTime.UtcNow;
            }
            arrivalQueue.Enqueue(evidence);
        }

        internal static void OnUseResultEvidence(byte[] frame)
        {
            TreasureUseResultEvidence evidence;
            try
            {
                evidence = TreasureEvidenceDecoder.DecodeRespPotholing(frame);
            }
            catch (TreasurePacketContractException)
            {
                return;
            }

            if (evidence == null)
            {
                return;
            }

            lock (cacheSync)
            {
                latestUseResultEvidence = evidence;
                useResultEvidenceObservedUtc = DateTime.UtcNow;
            }
            useResultQueue.Enqueue(evidence);
        }

        public static void ClearEvidence()
        {
            TreasureJumpArrivalEvidence arrival;
            TreasureUseResultEvidence useResult;
            while (arrivalQueue.TryDequeue(out arrival))
            {
            }
            while (useResultQueue.TryDequeue(out useResult))
            {
            }

            lock (cacheSync)
            {
                currentSnapshot = null;
                latestArrivalEvidence = null;
                latestUseResultEvidence = null;
                arrivalEvidenceObservedUtc = DateTime.MinValue;
                useResultEvidenceObservedUtc = DateTime.MinValue;
            }
        }

        public static JObject GetObservationStats()
        {
            lock (cacheSync)
            {
                return new JObject
                {
                    ["latestArrivalEvidenceType"] =
                        latestArrivalEvidence == null
                            ? "none"
                            : latestArrivalEvidence.EvidenceType.ToString(),
                    ["latestUseResultConsumed"] =
                        latestUseResultEvidence != null && latestUseResultEvidence.Consumed,
                    ["arrivalEvidenceAgeMs"] = AgeMilliseconds(arrivalEvidenceObservedUtc),
                    ["useResultEvidenceAgeMs"] = AgeMilliseconds(useResultEvidenceObservedUtc),
                    ["pendingArrivalCount"] = arrivalQueue.Count,
                    ["pendingUseResultCount"] = useResultQueue.Count
                };
            }
        }

        private static double AgeMilliseconds(DateTime observedUtc)
        {
            return observedUtc == DateTime.MinValue
                ? -1
                : Math.Max(0, (DateTime.UtcNow - observedUtc).TotalMilliseconds);
        }
    }
}
