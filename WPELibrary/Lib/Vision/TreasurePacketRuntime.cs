using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// A captured outgoing packet used as the immutable route/template source.
    /// Its socket handle is deliberately not reused after a reconnect.
    /// </summary>
    public sealed class TreasurePacketTemplate
    {
        public TreasurePacketTemplate(
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetFrom,
            string packetTo,
            byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("A packet template must contain bytes.", nameof(buffer));
            }

            this.PacketType = packetType;
            this.PacketFrom = packetFrom ?? string.Empty;
            this.PacketTo = packetTo ?? string.Empty;
            this.Buffer = (byte[])buffer.Clone();
        }

        public Socket_Cache.SocketPacket.PacketType PacketType { get; private set; }

        public string PacketFrom { get; private set; }

        public string PacketTo { get; private set; }

        public byte[] Buffer { get; private set; }
    }

    /// <summary>
    /// Current-session network route metadata. Unlike a template, a route has
    /// no packet bytes and therefore cannot accidentally retain an old
    /// protocol frame. It is populated from a current outgoing game frame.
    /// </summary>
    public sealed class TreasurePacketRoute
    {
        public TreasurePacketRoute(
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetFrom,
            string packetTo)
        {
            this.PacketType = packetType;
            this.PacketFrom = packetFrom ?? string.Empty;
            this.PacketTo = packetTo ?? string.Empty;
        }

        public Socket_Cache.SocketPacket.PacketType PacketType { get; private set; }

        public string PacketFrom { get; private set; }

        public string PacketTo { get; private set; }
    }

    /// <summary>
    /// The two confirmed outgoing templates discovered from the current WPE
    /// capture list. Discovery is protocol/field based and does not depend on
    /// an Agent or on a hard-coded socket handle.
    /// </summary>
    public sealed class TreasurePacketTemplateSet
    {
        public TreasurePacketTemplateSet(
            TreasurePacketTemplate jump,
            TreasurePacketTemplate use)
        {
            this.Jump = jump ?? throw new ArgumentNullException(nameof(jump));
            this.Use = use ?? throw new ArgumentNullException(nameof(use));
        }

        public TreasurePacketTemplate Jump { get; private set; }

        public TreasurePacketTemplate Use { get; private set; }
    }

    /// <summary>
    /// Immutable behavior definition for the pure-packet treasure workflow.
    /// Concrete packet bytes and Socket handles are deliberately not part of
    /// this definition; they are captured again for each hook session.
    /// </summary>
    public sealed class TreasurePacketPresetDefinition
    {
        public TreasurePacketPresetDefinition(string id, int jumpToUseDelayMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A preset id is required.", nameof(id));
            }

            if (jumpToUseDelayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(jumpToUseDelayMilliseconds),
                    "The Jump-to-Use delay cannot be negative.");
            }

            this.Id = id;
            this.JumpToUseDelayMilliseconds = jumpToUseDelayMilliseconds;
        }

        public string Id { get; private set; }

        public int JumpProtocolId
        {
            get { return TreasureJumpPacketContract.ProtocolId; }
        }

        public int UseProtocolId
        {
            get { return TreasureUsePacketContract.ProtocolId; }
        }

        public int AutoDigProtocolId
        {
            get { return TreasureAutoDigPacketContract.ProtocolId; }
        }

        public int JumpToUseDelayMilliseconds { get; private set; }

        public int JumpToAutoDigDelayMilliseconds
        {
            get { return this.JumpToUseDelayMilliseconds; }
        }

        public bool UsesUiInteraction
        {
            get { return false; }
        }

        public bool ReadsTargetFromResidentState
        {
            get { return false; }
        }

        public bool ReadsTargetFromC6Stream
        {
            get { return true; }
        }

        public bool ResolvesSocketAtSendTime
        {
            get { return true; }
        }
    }

    /// <summary>
    /// A read-only snapshot of the templates and route handles observed in
    /// the current injected process. It is session-scoped and is never loaded
    /// from a previous process or from a historical packet file.
    /// </summary>
    public sealed class TreasurePacketSessionSnapshot
    {
        internal TreasurePacketSessionSnapshot(
            TreasurePacketTemplate jump,
            TreasurePacketTemplate use,
            int jumpSocket,
            int useSocket,
            DateTime lastObservedUtc)
        {
            this.Templates = new TreasurePacketTemplateSet(jump, use);
            this.JumpSocket = jumpSocket;
            this.UseSocket = useSocket;
            this.LastObservedUtc = lastObservedUtc;
        }

        public TreasurePacketTemplateSet Templates { get; private set; }

        public int JumpSocket { get; private set; }

        public int UseSocket { get; private set; }

        public DateTime LastObservedUtc { get; private set; }

        public bool Ready
        {
            get { return this.JumpSocket > 0 && this.UseSocket > 0; }
        }
    }

    public sealed class TreasurePacketPreparedSet
    {
        internal TreasurePacketPreparedSet(
            TreasureInventoryTarget target,
            Socket_PacketInfo jumpPacket,
            Socket_PacketInfo usePacket)
            : this(target, jumpPacket, usePacket, null)
        {
        }

        internal TreasurePacketPreparedSet(
            TreasureInventoryTarget target,
            Socket_PacketInfo jumpPacket,
            Socket_PacketInfo usePacket,
            Socket_PacketInfo autoDigPacket)
        {
            this.Target = target;
            this.JumpPacket = jumpPacket;
            this.UsePacket = usePacket;
            this.AutoDigPacket = autoDigPacket;
        }

        public TreasureInventoryTarget Target { get; private set; }

        public Socket_PacketInfo JumpPacket { get; private set; }

        public Socket_PacketInfo UsePacket { get; private set; }

        public Socket_PacketInfo AutoDigPacket { get; private set; }

        public bool ReadOnly
        {
            get { return true; }
        }

        public bool ActionAuthorized
        {
            get { return false; }
        }

        public bool PacketSend
        {
            get { return false; }
        }
    }

    public sealed class TreasureJumpPreparedSet
    {
        internal TreasureJumpPreparedSet(
            TreasureInventoryTarget target,
            Socket_PacketInfo jumpPacket)
        {
            this.Target = target;
            this.JumpPacket = jumpPacket;
        }

        public TreasureInventoryTarget Target { get; private set; }

        public Socket_PacketInfo JumpPacket { get; private set; }
    }

    /// <summary>
    /// Explicit confirmation token for the live send boundary. No production
    /// code path creates this token implicitly, and preparation never needs it.
    /// </summary>
    public sealed class TreasureLiveSendAuthorization
    {
        private const string ConfirmationText = "TREASURE-LIVE-SEND";

        private TreasureLiveSendAuthorization()
        {
        }

        public static TreasureLiveSendAuthorization Create(string confirmation)
        {
            if (!string.Equals(confirmation, ConfirmationText, StringComparison.Ordinal))
            {
                throw new TreasurePacketRuntimeException(
                    "live_send_confirmation_invalid",
                    "Live treasure sending requires the exact explicit confirmation text.");
            }

            return new TreasureLiveSendAuthorization();
        }
    }

    public sealed class TreasurePacketSendResult
    {
        internal TreasurePacketSendResult(
            bool success,
            string code,
            int socket,
            int bytesSent,
            TreasureMapPacketSendDisposition disposition,
            int socketErrorCode = 0)
        {
            this.Success = success;
            this.Code = code ?? string.Empty;
            this.Socket = socket;
            this.BytesSent = bytesSent;
            this.Disposition = disposition;
            this.SocketErrorCode = socketErrorCode;
        }

        public bool Success { get; private set; }

        public string Code { get; private set; }

        public int Socket { get; private set; }

        public int BytesSent { get; private set; }

        public TreasureMapPacketSendDisposition Disposition { get; private set; }

        public int SocketErrorCode { get; private set; }
    }

    public sealed class TreasurePacketRuntimeException : InvalidOperationException
    {
        public TreasurePacketRuntimeException(string code, string message)
            : base(message)
        {
            this.Code = code ?? string.Empty;
        }

        public string Code { get; private set; }
    }

    /// <summary>
    /// Runtime bridge for the read-only C6 stream and the existing WPE socket
    /// sender. Offline callers can retain validated templates or construct
    /// Jump/Use/AutoDig frames through the closed encoder using current route
    /// metadata. The production Jump/Use path instead requires a validated
    /// template from the current hook session before the one-shot send.
    /// Resident-state JSON remains a compatibility input, while the
    /// production preset binds targets directly from C6.
    /// </summary>
    public static class TreasurePacketRuntime
    {
        private static readonly TreasureInventoryTarget ValidationTarget =
            new TreasureInventoryTarget(1, 1, 0, 0);
        private static readonly object SessionCacheSync = new object();
        private static TreasurePacketTemplate sessionJumpTemplate;
        private static TreasurePacketTemplate sessionUseTemplate;
        private static TreasurePacketRoute sessionRoute;
        private static int sessionJumpSocket;
        private static int sessionUseSocket;
        private static int sessionRouteSocket;
        private static DateTime sessionLastObservedUtc = DateTime.MinValue;
        private static uint sessionLastSequence;
        private static bool sessionSequenceAvailable;

        // 会话代次跟踪
        private static long sessionVersion = 0;

        public static readonly TreasurePacketPresetDefinition PurePacketPreset =
            new TreasurePacketPresetDefinition("treasure-map-pure-packet", 1500);

        /// <summary>
        /// Starts a new injected hook session. No template or Socket from a
        /// previous session is allowed to cross this boundary.
        /// </summary>
        public static void BeginSession()
        {
            lock (SessionCacheSync)
            {
                sessionVersion++;
                sessionJumpTemplate = null;
                sessionUseTemplate = null;
                sessionRoute = null;
                sessionJumpSocket = 0;
                sessionUseSocket = 0;
                sessionRouteSocket = 0;
                sessionLastObservedUtc = DateTime.MinValue;
                sessionLastSequence = 0;
                sessionSequenceAvailable = false;
            }
        }

        /// <summary>
        /// 获取当前会话代次（用于目标复核）
        /// </summary>
        public static long GetSessionVersion()
        {
            lock (SessionCacheSync)
            {
                return sessionVersion;
            }
        }

        public static void EndSession()
        {
            BeginSession();
        }

        /// <summary>
        /// Prepares a caller-validated current-session game frame by binding
        /// the next observed session sequence. Ordinary send presets may use
        /// this narrow bridge only after validating their own protocol shape;
        /// this method does not invent a sequence for a session that has not
        /// produced one.
        /// </summary>
        public static bool TryPrepareCurrentSessionSequence(
            byte[] buffer,
            out string errorCode)
        {
            if (!IsCurrentGameFrame(buffer))
            {
                errorCode = "not_current_game_frame";
                return false;
            }

            // Ordinary send presets may contain a stale non-zero sequence in
            // their saved bytes. Only a sequence observed after the current
            // session began is valid for this bridge; never promote the
            // preset body into session state.
            lock (SessionCacheSync)
            {
                if (!sessionSequenceAvailable)
                {
                    errorCode = "session_sequence_unavailable";
                    return false;
                }
            }

            return TryBindNextSessionSequence(buffer, out errorCode);
        }

        /// <summary>
        /// Observes one real captured packet. This is intentionally called
        /// before the UI list's auto-clear step, so the UI list is only a
        /// display buffer and cannot destroy the session preset.
        /// </summary>
        public static void ObserveCapturedPacket(Socket_PacketInfo packet)
        {
            if (packet == null || packet.PacketBuffer == null ||
                packet.PacketBuffer.Length == 0 || !IsSendPacketType(packet.PacketType))
            {
                return;
            }

            bool isJump = IsJumpTemplate(packet.PacketBuffer);
            bool isUse = IsUseTemplate(packet.PacketBuffer);
            bool isGameFrame = IsCurrentGameFrame(packet.PacketBuffer);
            lock (SessionCacheSync)
            {
                if (isGameFrame)
                {
                    bool routeChanged = sessionRoute != null &&
                        (sessionRoute.PacketType != packet.PacketType ||
                         !string.Equals(
                             sessionRoute.PacketFrom,
                             packet.PacketFrom ?? string.Empty,
                             StringComparison.OrdinalIgnoreCase) ||
                         !string.Equals(
                             sessionRoute.PacketTo,
                             packet.PacketTo ?? string.Empty,
                             StringComparison.OrdinalIgnoreCase));
                    if (routeChanged)
                    {
                        // A reconnect can keep the hook session alive while
                        // replacing both the local ephemeral port and the
                        // remote endpoint. Do not let the old Jump/Use
                        // templates or their sockets cross that boundary.
                        sessionJumpTemplate = null;
                        sessionUseTemplate = null;
                        sessionJumpSocket = 0;
                        sessionUseSocket = 0;
                        sessionRouteSocket = 0;
                        sessionLastSequence = 0;
                        sessionSequenceAvailable = false;
                    }

                    sessionRoute = new TreasurePacketRoute(
                        packet.PacketType,
                        packet.PacketFrom,
                        packet.PacketTo);

                    uint observedSequence = 0;
                    if (TryReadSessionSequence(packet.PacketBuffer, out observedSequence) &&
                        observedSequence != 0)
                    {
                        sessionLastSequence = observedSequence;
                        sessionSequenceAvailable = true;
                    }
                }

                if (isJump)
                {
                    sessionJumpTemplate = new TreasurePacketTemplate(
                        packet.PacketType,
                        packet.PacketFrom,
                        packet.PacketTo,
                        packet.PacketBuffer);
                }

                if (isUse)
                {
                    sessionUseTemplate = new TreasurePacketTemplate(
                        packet.PacketType,
                        packet.PacketFrom,
                        packet.PacketTo,
                        packet.PacketBuffer);
                }

                if (packet.PacketSocket > 0)
                {
                    if (isGameFrame)
                    {
                        sessionRouteSocket = packet.PacketSocket;
                    }

                    if (sessionJumpTemplate != null &&
                        MatchesRoute(sessionJumpTemplate, packet.PacketType, packet.PacketTo))
                    {
                        sessionJumpSocket = packet.PacketSocket;
                    }

                    if (sessionUseTemplate != null &&
                        MatchesRoute(sessionUseTemplate, packet.PacketType, packet.PacketTo))
                    {
                        sessionUseSocket = packet.PacketSocket;
                    }
                }

                sessionLastObservedUtc = DateTime.UtcNow;
            }
        }

        public static void ObserveCapturedPackets(IEnumerable<Socket_PacketInfo> capturedPackets)
        {
            if (capturedPackets == null)
            {
                return;
            }

            foreach (Socket_PacketInfo packet in capturedPackets)
            {
                ObserveCapturedPacket(packet);
            }
        }

        public static TreasurePacketSessionSnapshot GetSessionSnapshot()
        {
            lock (SessionCacheSync)
            {
                if (sessionJumpTemplate == null)
                {
                    throw new TreasurePacketRuntimeException(
                        "jump_template_not_found",
                        "No current-session outgoing 0x5828 Jump template has been observed.");
                }

                if (sessionUseTemplate == null)
                {
                    throw new TreasurePacketRuntimeException(
                        "use_template_not_found",
                        "No current-session outgoing 0x783A Use template has been observed.");
                }

                return new TreasurePacketSessionSnapshot(
                    CloneTemplate(sessionJumpTemplate),
                    CloneTemplate(sessionUseTemplate),
                    sessionJumpSocket,
                    sessionUseSocket,
                    sessionLastObservedUtc);
            }
        }

        /// <summary>
        /// Returns the current session's Use parameters while rebinding only
        /// the inventory position to the supplied C6 target. The type, count,
        /// and parameter are kept from the current game's own 0x783A call so
        /// different treasure-map call sites remain compatible.
        /// </summary>
        public static TreasureUsePacketRequest GetCurrentSessionUseRequest(int packageNum)
        {
            if (packageNum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(packageNum));
            }

            byte[] templateBuffer;
            lock (SessionCacheSync)
            {
                if (sessionUseTemplate == null)
                {
                    throw new TreasurePacketRuntimeException(
                        "use_template_not_found",
                        "No current-session outgoing 0x783A Use template has been observed.");
                }

                templateBuffer = (byte[])sessionUseTemplate.Buffer.Clone();
            }

            TreasureUsePacketRequest captured = TreasurePacketEncoder.DecodeUse(templateBuffer);
            return new TreasureUsePacketRequest(
                packageNum,
                captured.Type,
                captured.Num,
                captured.Param);
        }

        /// <summary>
        /// Returns the current game's Use parameters. If the hook observer has
        /// not seen the 0x783A frame yet, seed the session cache from the
        /// current capture list before failing. This mirrors the existing
        /// route/socket fallback used by ordinary send presets while keeping
        /// the request bound to the current route.
        /// </summary>
        public static TreasureUsePacketRequest GetCurrentUseRequest(int packageNum)
        {
            try
            {
                return GetCurrentSessionUseRequest(packageNum);
            }
            catch (TreasurePacketRuntimeException ex)
            {
                if (!string.Equals(
                    ex.Code,
                    "use_template_not_found",
                    StringComparison.Ordinal))
                {
                    throw;
                }

                TreasurePacketRoute currentRoute;
                try
                {
                    currentRoute = GetSessionRoute();
                }
                catch (TreasurePacketRuntimeException routeException)
                {
                    if (!string.Equals(
                        routeException.Code,
                        "current_route_not_found",
                        StringComparison.Ordinal))
                    {
                        throw;
                    }

                    currentRoute = GetCurrentRoute();
                }

                IEnumerable<Socket_PacketInfo> candidates =
                    CaptureCurrentPacketsForRoute()
                        .Where(item => item != null &&
                            IsSendPacketType(item.PacketType) &&
                            IsUseTemplate(item.PacketBuffer) &&
                            MatchesRoute(
                                currentRoute,
                                item.PacketType,
                                item.PacketTo))
                        .OrderByDescending(item => item.PacketTime);

                Socket_PacketInfo candidate = candidates.FirstOrDefault();
                if (candidate == null)
                {
                    candidate = CaptureSavedSendPacketsForTreasure()
                        .Where(item => item != null &&
                            IsSendPacketType(item.PacketType) &&
                            IsUseTemplate(item.PacketBuffer) &&
                            MatchesRoute(
                                currentRoute,
                                item.PacketType,
                                item.PacketTo))
                        .FirstOrDefault();
                }

                if (candidate != null)
                {
                    ObserveCapturedPacket(candidate);
                    return GetCurrentSessionUseRequest(packageNum);
                }

                throw;
            }
        }

        /// <summary>
        /// Builds a Jump packet only from a validated template observed in
        /// the current hook session. Production live sending must not fall
        /// back to the closed encoder when the game has not supplied a real
        /// outgoing Jump frame.
        /// </summary>
        public static Socket_PacketInfo GetCurrentJumpPacket(
            TreasureInventoryTarget target,
            TreasurePacketRoute route)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            TreasurePacketTemplate template = FindCurrentTemplate(
                route,
                IsJumpTemplate,
                false);
            if (template == null)
            {
                throw new TreasurePacketRuntimeException(
                    "jump_template_not_found",
                    "No current-session outgoing 0x5828 Jump template matches the current connection.");
            }

            byte[] patched = TreasurePacketTemplatePatcher.PatchJump(
                template.Buffer,
                target);
            return CreatePacket(route, patched);
        }

        /// <summary>
        /// Builds a Use packet only from a validated template observed in the
        /// current hook session. The package position is the only field that
        /// is rebound; type, count, parameter, and all length fields remain
        /// from the game's own frame.
        /// </summary>
        public static Socket_PacketInfo GetCurrentUsePacket(
            TreasureInventoryTarget target,
            TreasurePacketRoute route)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            TreasurePacketTemplate template = FindCurrentTemplate(
                route,
                IsUseTemplate,
                true);
            if (template == null)
            {
                throw new TreasurePacketRuntimeException(
                    "use_template_not_found",
                    "No current-session outgoing 0x783A Use template matches the current connection.");
            }

            byte[] patched = TreasurePacketTemplatePatcher.PatchUse(
                template.Buffer,
                target);
            return CreatePacket(route, patched);
        }

        /// <summary>
        /// Returns the native AutoDig packet used by an ordinary send preset,
        /// after the template has been validated against the current
        /// connection. A saved template from a different destination is
        /// rejected; Jump/Use templates remain current-route-only.
        /// </summary>
        public static Socket_PacketInfo GetCurrentAutoDigPacket(TreasurePacketRoute route)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            Socket_PacketInfo candidate = CaptureCurrentPacketsForRoute()
                .Where(item => item != null &&
                    IsSendPacketType(item.PacketType) &&
                    IsAutoDigTemplate(item.PacketBuffer) &&
                    MatchesRoute(route, item.PacketType, item.PacketTo))
                .OrderByDescending(item => item.PacketTime)
                .FirstOrDefault();

            if (candidate == null)
            {
                candidate = CaptureSavedSendPacketsForTreasure()
                    .Where(item => item != null &&
                        IsSendPacketType(item.PacketType) &&
                        IsAutoDigTemplate(item.PacketBuffer) &&
                        MatchesRoute(route, item.PacketType, item.PacketTo))
                    .FirstOrDefault();
            }

            if (candidate == null)
            {
                throw new TreasurePacketRuntimeException(
                    "auto_dig_template_not_found",
                    "No validated outgoing 0xB0F4 AutoDig template matches the current connection.");
            }

            return CreatePacket(route, candidate.PacketBuffer);
        }

        public static bool HasCurrentAutoDigPacket()
        {
            try
            {
                TreasurePacketRoute route = GetCurrentRoute();
                GetCurrentAutoDigPacket(route);
                return true;
            }
            catch (TreasurePacketRuntimeException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks only for a Jump template from the current hook session.
        /// This is used to choose the guarded AutoDig compatibility path
        /// when a game build does not expose a current 0x5828 frame.
        /// </summary>
        public static bool HasCurrentJumpTemplate()
        {
            try
            {
                TreasurePacketRoute route = GetCurrentRoute();
                return FindCurrentTemplate(route, IsJumpTemplate, false) != null;
            }
            catch (TreasurePacketRuntimeException)
            {
                return false;
            }
            catch (TreasurePacketContractException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks only for a validated Use template from the current hook
        /// session. This deliberately does not use GetCurrentUseRequest,
        /// whose compatibility fallback can resolve saved Use parameters
        /// without a real current-session 0x783A frame.
        /// </summary>
        public static bool HasCurrentUseTemplate()
        {
            try
            {
                TreasurePacketRoute route = GetCurrentRoute();
                return FindCurrentTemplate(route, IsUseTemplate, true) != null;
            }
            catch (TreasurePacketRuntimeException)
            {
                return false;
            }
            catch (TreasurePacketContractException)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns only the current-session route metadata. This is the route
        /// used by the pure encoder path when no protocol template has been
        /// captured yet.
        /// </summary>
        public static TreasurePacketRoute GetSessionRoute()
        {
            lock (SessionCacheSync)
            {
                if (sessionRoute == null)
                {
                    throw new TreasurePacketRuntimeException(
                        "current_route_not_found",
                        "No current-session outgoing game route has been observed.");
                }

                return CloneRoute(sessionRoute);
            }
        }

        /// <summary>
        /// Returns the best currently usable game route. The current capture
        /// list is checked first on every call so a reconnect can replace the
        /// route cached earlier in the same hook session. When the display
        /// list has auto-cleared, a still-live session socket is used as the
        /// compatibility fallback.
        /// </summary>
        public static TreasurePacketRoute GetCurrentRoute()
        {
            List<Socket_PacketInfo> capturedPackets = CaptureCurrentPacketsForRoute();
            IEnumerable<Socket_PacketInfo> candidates = capturedPackets
                .Where(item => item != null &&
                    item.PacketSocket > 0 &&
                    IsSendPacketType(item.PacketType) &&
                    IsCurrentGameFrame(item.PacketBuffer))
                .OrderByDescending(item => item.PacketTime);

            string lastError = string.Empty;
            foreach (Socket_PacketInfo candidate in candidates)
            {
                Socket_Cache.SocketList.CurrentSocketRouteResolution resolution;
                try
                {
                    resolution = Socket_Cache.SocketList.ResolveCurrentRoute(candidate);
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    continue;
                }

                if (resolution != null && resolution.Succeeded && resolution.Route != null)
                {
                    // Seed the current-session cache so a UI list auto-clear
                    // between route discovery and the actual send cannot erase
                    // the socket we just validated.
                    ObserveCapturedPacket(candidate);
                    return new TreasurePacketRoute(
                        resolution.Route.PacketType,
                        resolution.Route.PacketFrom,
                        resolution.Route.PacketTo);
                }

                if (resolution != null &&
                    resolution.Status == Socket_Cache.SocketList.CurrentSocketRouteStatus.Ambiguous)
                {
                    throw new TreasurePacketRuntimeException(
                        "runtime_route_ambiguous",
                        string.IsNullOrWhiteSpace(resolution.ErrorMessage)
                            ? "Multiple current game connections are available."
                            : resolution.ErrorMessage);
                }

                if (resolution != null && !string.IsNullOrWhiteSpace(resolution.ErrorMessage))
                {
                    lastError = resolution.ErrorMessage;
                }
            }

            TreasurePacketRoute cachedRoute = null;
            int cachedSocket = 0;
            lock (SessionCacheSync)
            {
                if (sessionRoute != null)
                {
                    cachedRoute = CloneRoute(sessionRoute);
                    cachedSocket = sessionRouteSocket;
                }
            }

            if (cachedRoute != null && IsCurrentSocketUsable(cachedSocket))
            {
                return cachedRoute;
            }

            throw new TreasurePacketRuntimeException(
                "current_route_not_found",
                string.IsNullOrWhiteSpace(lastError)
                    ? "No current outgoing game route was found in the existing connection list."
                    : lastError);
        }

        private static bool IsCurrentSocketUsable(int socket)
        {
            if (socket <= 0)
            {
                return false;
            }

            string currentAddress = Socket_Operation.GetIP_BySocket(
                socket,
                Socket_Cache.SocketPacket.IPType.From);
            return !string.IsNullOrWhiteSpace(currentAddress) &&
                !string.Equals(
                    currentAddress.Trim(),
                    "0.0.0.0:0",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void InvalidateSessionSocket(int socket)
        {
            if (socket <= 0)
            {
                return;
            }

            lock (SessionCacheSync)
            {
                if (sessionJumpSocket == socket)
                {
                    sessionJumpSocket = 0;
                }

                if (sessionUseSocket == socket)
                {
                    sessionUseSocket = 0;
                }

                if (sessionRouteSocket == socket)
                {
                    sessionRouteSocket = 0;
                }
            }
        }

        private static List<Socket_PacketInfo> CaptureCurrentPacketsForRoute()
        {
            List<Socket_PacketInfo> capturedPackets = new List<Socket_PacketInfo>();
            Action capture = () =>
            {
                capturedPackets = Socket_Cache.SocketList.lstRecPacket
                    .Where(item => item != null)
                    .ToList();
            };

            if (Socket_Cache.System.InvokeAction != null)
            {
                Socket_Cache.System.InvokeAction(capture);
            }
            else
            {
                capture();
            }

            return capturedPackets;
        }

        private static List<Socket_PacketInfo> CaptureSavedSendPacketsForTreasure()
        {
            List<Socket_PacketInfo> preferred = new List<Socket_PacketInfo>();
            List<Socket_PacketInfo> other = new List<Socket_PacketInfo>();
            Action capture = () =>
            {
                foreach (Socket_SendInfo sendInfo in Socket_Cache.SendList.lstSend)
                {
                    if (sendInfo == null || sendInfo.SCollection == null)
                    {
                        continue;
                    }

                    List<Socket_PacketInfo> destination =
                        IsTreasurePresetName(sendInfo.SName) ? preferred : other;
                    foreach (Socket_PacketInfo packet in sendInfo.SCollection)
                    {
                        if (packet == null || packet.PacketBuffer == null ||
                            packet.PacketBuffer.Length == 0)
                        {
                            continue;
                        }

                        destination.Add(ClonePacketForObservation(packet));
                    }
                }
            };

            if (Socket_Cache.System.InvokeAction != null)
            {
                Socket_Cache.System.InvokeAction(capture);
            }
            else
            {
                capture();
            }

            preferred.AddRange(other);
            return preferred;
        }

        private static Socket_PacketInfo ClonePacketForObservation(Socket_PacketInfo packet)
        {
            return new Socket_PacketInfo
            {
                PacketSocket = 0,
                PacketType = packet.PacketType,
                PacketFrom = packet.PacketFrom,
                PacketTo = packet.PacketTo,
                PacketBuffer = packet.PacketBuffer == null
                    ? null
                    : (byte[])packet.PacketBuffer.Clone(),
                PacketLen = packet.PacketLen,
                PacketTime = packet.PacketTime
            };
        }

        private static bool IsTreasurePresetName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                (name.IndexOf("挖宝", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("treasure", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Discovers the latest outgoing game route from the current capture
        /// list. Only route metadata is retained; packet bytes are ignored.
        /// </summary>
        public static TreasurePacketRoute DiscoverOutgoingRoute(
            IEnumerable<Socket_PacketInfo> capturedPackets)
        {
            if (capturedPackets == null)
            {
                throw new ArgumentNullException(nameof(capturedPackets));
            }

            TreasurePacketRoute route = null;
            foreach (Socket_PacketInfo packet in capturedPackets)
            {
                if (packet == null || packet.PacketSocket <= 0 ||
                    !IsSendPacketType(packet.PacketType) ||
                    !IsCurrentGameFrame(packet.PacketBuffer))
                {
                    continue;
                }

                route = new TreasurePacketRoute(
                    packet.PacketType,
                    packet.PacketFrom,
                    packet.PacketTo);
            }

            if (route == null)
            {
                throw new TreasurePacketRuntimeException(
                    "current_route_not_found",
                    "No current outgoing game route was found in the capture list.");
            }

            return route;
        }

        /// <summary>
        /// Resolves the latest route from the current display list first and
        /// then from the current-session observation cache. The second path
        /// is needed because the display list may have auto-cleared; it is
        /// still limited to the live injected process and its current session.
        /// </summary>
        public static int ResolveCurrentSessionSocket(
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetTo)
        {
            return ResolveCurrentSessionSocket(packetType, packetTo, false);
        }

        private static int ResolveCurrentSessionSocket(
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetTo,
            bool preferUseSocket)
        {
            Socket_PacketInfo template = new Socket_PacketInfo
            {
                PacketType = packetType,
                PacketTo = packetTo ?? string.Empty
            };

            Socket_Cache.SocketList.CurrentSocketRouteResolution resolution = null;
            try
            {
                resolution = Socket_Cache.SocketList.ResolveCurrentRoute(template);
            }
            catch (Exception)
            {
                resolution = null;
            }

            if (resolution != null && resolution.Succeeded && resolution.Route != null)
            {
                ObserveCapturedPackets(CaptureCurrentPacketsForRoute());
                return resolution.Route.Socket;
            }

            if (resolution != null &&
                resolution.Status == Socket_Cache.SocketList.CurrentSocketRouteStatus.Ambiguous)
            {
                return 0;
            }

            List<Socket_PacketInfo> capturedPackets = CaptureCurrentPacketsForRoute();
            if (capturedPackets.Count > 0)
            {
                ObserveCapturedPackets(capturedPackets);
            }

            bool enforceSocketLiveness =
                Socket_Cache.SocketList.CaptureSessionStartedAt != DateTime.MinValue;
            int cachedSocket = 0;
            lock (SessionCacheSync)
            {
                if (preferUseSocket &&
                    sessionUseTemplate != null &&
                    MatchesRoute(sessionUseTemplate, packetType, packetTo) &&
                    sessionUseSocket > 0)
                {
                    cachedSocket = sessionUseSocket;
                }
                else if (!preferUseSocket &&
                    sessionJumpTemplate != null &&
                    MatchesRoute(sessionJumpTemplate, packetType, packetTo) &&
                    sessionJumpSocket > 0)
                {
                    cachedSocket = sessionJumpSocket;
                }
                else if (sessionRoute != null &&
                    MatchesRoute(sessionRoute, packetType, packetTo) &&
                    sessionRouteSocket > 0)
                {
                    cachedSocket = sessionRouteSocket;
                }
                else if (preferUseSocket &&
                    sessionJumpTemplate != null &&
                    MatchesRoute(sessionJumpTemplate, packetType, packetTo) &&
                    sessionJumpSocket > 0)
                {
                    cachedSocket = sessionJumpSocket;
                }
                else if (!preferUseSocket &&
                    sessionUseTemplate != null &&
                    MatchesRoute(sessionUseTemplate, packetType, packetTo) &&
                    sessionUseSocket > 0)
                {
                    cachedSocket = sessionUseSocket;
                }
            }

            if (cachedSocket > 0 &&
                (!enforceSocketLiveness || IsCurrentSocketUsable(cachedSocket)))
            {
                return cachedSocket;
            }

            if (cachedSocket > 0 && enforceSocketLiveness)
            {
                InvalidateSessionSocket(cachedSocket);
            }

            return 0;
        }

        public static int ResolveCurrentSessionSocket(TreasurePacketRoute route)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            return ResolveCurrentSessionSocket(route.PacketType, route.PacketTo);
        }

        public static TreasurePacketTemplateSet DiscoverTemplates(
            IEnumerable<Socket_PacketInfo> capturedPackets)
        {
            if (capturedPackets == null)
            {
                throw new ArgumentNullException(nameof(capturedPackets));
            }

            TreasurePacketTemplate jump = null;
            TreasurePacketTemplate use = null;
            foreach (Socket_PacketInfo packet in capturedPackets)
            {
                if (packet == null || packet.PacketBuffer == null ||
                    packet.PacketBuffer.Length == 0 || !IsSendPacketType(packet.PacketType))
                {
                    continue;
                }

                if (IsJumpTemplate(packet.PacketBuffer))
                {
                    jump = new TreasurePacketTemplate(
                        packet.PacketType,
                        packet.PacketFrom,
                        packet.PacketTo,
                        packet.PacketBuffer);
                }

                if (IsUseTemplate(packet.PacketBuffer))
                {
                    use = new TreasurePacketTemplate(
                        packet.PacketType,
                        packet.PacketFrom,
                        packet.PacketTo,
                        packet.PacketBuffer);
                }
            }

            if (jump == null)
            {
                throw new TreasurePacketRuntimeException(
                    "jump_template_not_found",
                    "No current outgoing 0x5828 Jump template was found in the capture list.");
            }

            if (use == null)
            {
                throw new TreasurePacketRuntimeException(
                    "use_template_not_found",
                    "No current outgoing 0x783A Use template was found in the capture list.");
            }

            return new TreasurePacketTemplateSet(jump, use);
        }

        public static TreasurePacketTemplate DiscoverJumpTemplate(
            IEnumerable<Socket_PacketInfo> capturedPackets)
        {
            if (capturedPackets == null)
            {
                throw new ArgumentNullException(nameof(capturedPackets));
            }

            TreasurePacketTemplate jump = null;
            foreach (Socket_PacketInfo packet in capturedPackets)
            {
                if (packet == null || packet.PacketBuffer == null ||
                    packet.PacketBuffer.Length == 0 || !IsSendPacketType(packet.PacketType))
                {
                    continue;
                }

                if (IsJumpTemplate(packet.PacketBuffer))
                {
                    jump = new TreasurePacketTemplate(
                        packet.PacketType,
                        packet.PacketFrom,
                        packet.PacketTo,
                        packet.PacketBuffer);
                }
            }

            if (jump == null)
            {
                throw new TreasurePacketRuntimeException(
                    "jump_template_not_found",
                    "No current outgoing 0x5828 Jump template was found in the capture list.");
            }

            return jump;
        }

        public static TreasurePacketPreparedSet PrepareFromResidentState(
            string residentStateJson,
            TreasurePacketTemplateSet templates,
            int? packageNum = null)
        {
            if (string.IsNullOrWhiteSpace(residentStateJson))
            {
                throw new ArgumentException("Resident state JSON is required.", nameof(residentStateJson));
            }

            if (templates == null)
            {
                throw new ArgumentNullException(nameof(templates));
            }

            JObject state = ParseAndValidateResidentState(residentStateJson);

            TreasureInventoryTarget target = SelectTarget(state, packageNum);
            TreasureUsePacketRequest capturedUseRequest = TreasurePacketEncoder.DecodeUse(
                templates.Use.Buffer);
            byte[] jumpBytes = TreasurePacketEncoder.EncodeJump(
                new TreasureJumpPacketRequest(target.MapId, target.X, target.Y));
            byte[] useBytes = TreasurePacketEncoder.EncodeUse(
                new TreasureUsePacketRequest(
                    target.PackageNum,
                    capturedUseRequest.Type,
                    capturedUseRequest.Num,
                    capturedUseRequest.Param));
            byte[] autoDigBytes = TreasurePacketEncoder.EncodeAutoDig();

            return new TreasurePacketPreparedSet(
                target,
                CreatePacket(templates.Jump, jumpBytes),
                CreatePacket(templates.Use, useBytes),
                CreatePacket(templates.Use, autoDigBytes));
        }

        /// <summary>
        /// Builds Jump, the compatibility Use frame, and the fixed native
        /// AutoDig frame from the resident target and the closed codec, using
        /// route metadata only. No Jump/Use template bytes are needed. The
        /// Use request is explicit so legacy callers never guess between the
        /// client's type=7 and type=13 treasure call sites.
        /// </summary>
        public static TreasurePacketPreparedSet PrepareEncodedFromResidentState(
            string residentStateJson,
            TreasurePacketRoute route,
            TreasureUsePacketRequest useRequest,
            int? packageNum = null)
        {
            if (string.IsNullOrWhiteSpace(residentStateJson))
            {
                throw new ArgumentException("Resident state JSON is required.", nameof(residentStateJson));
            }

            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (useRequest == null)
            {
                throw new ArgumentNullException(nameof(useRequest));
            }

            JObject state = ParseAndValidateResidentState(residentStateJson);
            TreasureInventoryTarget target = SelectTarget(state, packageNum);
            if (useRequest.PackageNum != target.PackageNum)
            {
                throw new TreasurePacketRuntimeException(
                    "use_package_mismatch",
                    "The explicit Use request packageNum does not match the resident target.");
            }

            byte[] jumpBytes = TreasurePacketEncoder.EncodeJump(
                new TreasureJumpPacketRequest(target.MapId, target.X, target.Y));
            byte[] useBytes = TreasurePacketEncoder.EncodeUse(useRequest);
            byte[] autoDigBytes = TreasurePacketEncoder.EncodeAutoDig();
            return new TreasurePacketPreparedSet(
                target,
                CreatePacket(route, jumpBytes),
                CreatePacket(route, useBytes),
                CreatePacket(route, autoDigBytes));
        }

        /// <summary>
        /// Convenience overload that binds packageNum from the resident
        /// target while keeping the other Use fields explicit.
        /// </summary>
        public static TreasurePacketPreparedSet PrepareEncodedFromResidentState(
            string residentStateJson,
            TreasurePacketRoute route,
            int useType,
            int useNum,
            string useParam,
            int? packageNum = null)
        {
            JObject state = ParseAndValidateResidentState(residentStateJson);
            TreasureInventoryTarget target = SelectTarget(state, packageNum);
            return PrepareEncodedFromResidentState(
                residentStateJson,
                route,
                new TreasureUsePacketRequest(target.PackageNum, useType, useNum, useParam),
                target.PackageNum);
        }

        /// <summary>
        /// Builds the Jump, compatibility Use, and fixed native AutoDig frames
        /// directly from the current C6 inventory member. The C6 stream is
        /// already the validated source of the current package position and
        /// coordinates, so this path does not serialize or re-read a resident
        /// JSON state file.
        /// </summary>
        public static TreasurePacketPreparedSet PrepareEncodedFromTarget(
            TreasureInventoryTarget target,
            TreasurePacketRoute route,
            TreasureUsePacketRequest useRequest)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (useRequest == null)
            {
                throw new ArgumentNullException(nameof(useRequest));
            }

            if (useRequest.PackageNum != target.PackageNum)
            {
                throw new TreasurePacketRuntimeException(
                    "use_package_mismatch",
                    "The explicit Use request packageNum does not match the C6 target.");
            }

            byte[] jumpBytes = TreasurePacketEncoder.EncodeJump(
                new TreasureJumpPacketRequest(target.MapId, target.X, target.Y));
            byte[] useBytes = TreasurePacketEncoder.EncodeUse(useRequest);
            byte[] autoDigBytes = TreasurePacketEncoder.EncodeAutoDig();
            return new TreasurePacketPreparedSet(
                target,
                CreatePacket(route, jumpBytes),
                CreatePacket(route, useBytes),
                CreatePacket(route, autoDigBytes));
        }

        /// <summary>
        /// Convenience overload for the fixed advanced treasure-map call.
        /// </summary>
        public static TreasurePacketPreparedSet PrepareEncodedFromTarget(
            TreasureInventoryTarget target,
            TreasurePacketRoute route,
            int useType,
            int useNum,
            string useParam)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return PrepareEncodedFromTarget(
                target,
                route,
                new TreasureUsePacketRequest(
                    target.PackageNum,
                    useType,
                    useNum,
                useParam));
        }

        /// <summary>
        /// Builds only the production Jump and native AutoDig frames. This
        /// overload keeps the live runner independent from the legacy Use
        /// fields while the three-frame preparation overloads remain available
        /// to compatibility and offline callers.
        /// </summary>
        public static TreasurePacketPreparedSet PrepareEncodedFromTarget(
            TreasureInventoryTarget target,
            TreasurePacketRoute route)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            byte[] jumpBytes = TreasurePacketEncoder.EncodeJump(
                new TreasureJumpPacketRequest(target.MapId, target.X, target.Y));
            byte[] autoDigBytes = TreasurePacketEncoder.EncodeAutoDig();
            return new TreasurePacketPreparedSet(
                target,
                CreatePacket(route, jumpBytes),
                null,
                CreatePacket(route, autoDigBytes));
        }

        public static TreasureJumpPreparedSet PrepareJumpEncodedFromResidentState(
            string residentStateJson,
            TreasurePacketRoute route,
            int? packageNum = null)
        {
            if (string.IsNullOrWhiteSpace(residentStateJson))
            {
                throw new ArgumentException("Resident state JSON is required.", nameof(residentStateJson));
            }

            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            JObject state = ParseAndValidateResidentState(residentStateJson);
            TreasureInventoryTarget target = SelectTarget(state, packageNum);
            byte[] jumpBytes = TreasurePacketEncoder.EncodeJump(
                new TreasureJumpPacketRequest(target.MapId, target.X, target.Y));
            return new TreasureJumpPreparedSet(
                target,
                CreatePacket(route, jumpBytes));
        }

        public static TreasureJumpPreparedSet PrepareJumpFromResidentState(
            string residentStateJson,
            TreasurePacketTemplate template,
            int? packageNum = null)
        {
            if (string.IsNullOrWhiteSpace(residentStateJson))
            {
                throw new ArgumentException("Resident state JSON is required.", nameof(residentStateJson));
            }

            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            JObject state = ParseAndValidateResidentState(residentStateJson);

            TreasureInventoryTarget target = SelectTarget(state, packageNum);
            byte[] jumpBytes = TreasurePacketEncoder.EncodeJump(
                new TreasureJumpPacketRequest(target.MapId, target.X, target.Y));
            return new TreasureJumpPreparedSet(
                target,
                CreatePacket(template, jumpBytes));
        }

        /// <summary>
        /// Sends exactly one already-prepared packet. The caller must provide
        /// the explicit token. The current route is read again immediately
        /// before socket resolution; if it changed after preparation, the
        /// packet is rejected rather than rebound across connections.
        /// </summary>
        public static TreasurePacketSendResult SendPreparedPacketOnce(
            Socket_PacketInfo preparedPacket,
            TreasureLiveSendAuthorization authorization)
        {
            return SendPreparedPacketOnce(
                preparedPacket,
                authorization,
                false);
        }

        /// <summary>
        /// Sends one prepared packet. The Xiangju Chang'an production path can
        /// opt into action-specific cached-socket fallback so a Use packet does
        /// not inherit a stale Jump socket when the visible capture list has
        /// just auto-cleared. Other callers retain the previous generic order.
        /// </summary>
        public static TreasurePacketSendResult SendPreparedPacketOnce(
            Socket_PacketInfo preparedPacket,
            TreasureLiveSendAuthorization authorization,
            bool preferActionSpecificSocketFallback)
        {
            return SendPreparedPacketOnce(
                preparedPacket,
                authorization,
                preferActionSpecificSocketFallback,
                false);
        }

        /// <summary>
        /// Sends one prepared packet with an explicit compatibility choice for
        /// a caller that has no current-session sequence template. The
        /// compatibility choice is opt-in; ordinary callers keep the strict
        /// session-sequence requirement.
        /// </summary>
        public static TreasurePacketSendResult SendPreparedPacketOnce(
            Socket_PacketInfo preparedPacket,
            TreasureLiveSendAuthorization authorization,
            bool preferActionSpecificSocketFallback,
            bool allowMissingSessionSequence)
        {
            if (preparedPacket == null)
            {
                return new TreasurePacketSendResult(
                    false,
                    "prepared_packet_missing",
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            if (authorization == null)
            {
                return new TreasurePacketSendResult(
                    false,
                    "live_send_not_authorized",
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            if (!IsSendPacketType(preparedPacket.PacketType))
            {
                return new TreasurePacketSendResult(
                    false,
                    "packet_direction_invalid",
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            if (preparedPacket.PacketBuffer == null || preparedPacket.PacketBuffer.Length == 0)
            {
                return new TreasurePacketSendResult(
                    false,
                    "prepared_packet_empty",
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            byte[] sendBuffer = (byte[])preparedPacket.PacketBuffer.Clone();
            bool isJumpFrame = IsEncodedJumpFrame(sendBuffer);
            bool isUseFrame = IsEncodedUseFrame(sendBuffer);
            bool isAutoDigFrame = IsEncodedAutoDigFrame(sendBuffer);
            if (!isJumpFrame && !isUseFrame && !isAutoDigFrame)
            {
                return new TreasurePacketSendResult(
                    false,
                    "packet_contract_invalid",
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            TreasurePacketRoute currentRoute;
            try
            {
                currentRoute = GetCurrentRoute();
            }
            catch (TreasurePacketRuntimeException ex)
            {
                return new TreasurePacketSendResult(
                    false,
                    ex.Code,
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }
            catch (Exception)
            {
                return new TreasurePacketSendResult(
                    false,
                    "current_route_not_found",
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            if (!MatchesRoute(currentRoute, preparedPacket))
            {
                return new TreasurePacketSendResult(
                    false,
                    "current_route_changed",
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            int socket = ResolveCurrentCapturedSocket(
                preparedPacket.PacketType,
                preparedPacket.PacketTo,
                isUseFrame,
                preferActionSpecificSocketFallback);
            if (socket <= 0)
            {
                return new TreasurePacketSendResult(
                    false,
                    "current_socket_not_found",
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            if ((isJumpFrame || isUseFrame) &&
                !TryBindNextSessionSequence(sendBuffer, out string sequenceError) &&
                !(allowMissingSessionSequence &&
                  string.Equals(
                      sequenceError,
                      "session_sequence_unavailable",
                      StringComparison.Ordinal)))
            {
                return new TreasurePacketSendResult(
                    false,
                    sequenceError,
                    0,
                    0,
                    TreasureMapPacketSendDisposition.NotDispatched);
            }

            int bytesSent;
            int socketErrorCode;
            bool sent = Socket_Operation.SendPacket(
                socket,
                preparedPacket.PacketType,
                preparedPacket.PacketFrom,
                preparedPacket.PacketTo,
                sendBuffer,
                out bytesSent,
                out socketErrorCode);
            return sent
                ? new TreasurePacketSendResult(
                    true,
                    "sent",
                    socket,
                    bytesSent,
                    TreasureMapPacketSendDisposition.Dispatched,
                    0)
                : CreateSocketSendFailureResult(
                    socket,
                    bytesSent,
                    socketErrorCode);
        }

        private static TreasurePacketSendResult CreateSocketSendFailureResult(
            int socket,
            int bytesSent,
            int socketErrorCode)
        {
            InvalidateSessionSocket(socket);
            return new TreasurePacketSendResult(
                false,
                "socket_send_failed",
                socket,
                bytesSent,
                TreasureMapPacketSendDisposition.Ambiguous,
                socketErrorCode);
        }

        private static JObject ParseAndValidateResidentState(string residentStateJson)
        {
            JObject state;
            try
            {
                state = JObject.Parse(residentStateJson);
            }
            catch (Exception ex)
            {
                throw new TreasurePacketRuntimeException(
                    "state_json_invalid",
                    "Resident state is not a valid JSON object: " + ex.Message);
            }

            RequireBoolean(state, "available", true, "state_not_ready");
            RequireBoolean(state, "actionAuthorized", false, "action_authority_invalid");
            return state;
        }

        private static TreasureInventoryTarget SelectTarget(JObject state, int? packageNum)
        {
            JArray items = state["items"] as JArray;
            if (items == null)
            {
                throw new TreasurePacketRuntimeException(
                    "state_items_invalid",
                    "Resident state items must be an array.");
            }

            List<TreasureInventoryTarget> candidates = new List<TreasureInventoryTarget>();
            foreach (JToken itemToken in items)
            {
                JObject item = itemToken as JObject;
                if (item == null)
                {
                    continue;
                }

                int itemPackageNum;
                int itemSlot;
                int scene;
                int mapId;
                int x;
                int y;
                if (!TryReadPositiveInt(item, "packageNum", out itemPackageNum) ||
                    !TryReadPositiveInt(item, "slot", out itemSlot) ||
                    !TryReadPositiveInt(item, "scene", out scene) ||
                    !TryReadPositiveInt(item, "mapId", out mapId) ||
                    !TryReadNonNegativeInt(item, "x", out x) ||
                    !TryReadNonNegativeInt(item, "y", out y))
                {
                    continue;
                }

                if (itemPackageNum != itemSlot || scene != mapId)
                {
                    continue;
                }

                if (packageNum.HasValue && itemPackageNum != packageNum.Value)
                {
                    continue;
                }

                candidates.Add(new TreasureInventoryTarget(itemPackageNum, scene, x, y));
            }

            if (candidates.Count == 0)
            {
                throw new TreasurePacketRuntimeException(
                    packageNum.HasValue ? "target_slot_not_found" : "no_valid_treasure",
                    "No valid treasure-map member matched the requested slot.");
            }

            if (candidates.Count != 1)
            {
                throw new TreasurePacketRuntimeException(
                    "ambiguous_treasure",
                    "Multiple treasure-map members are current; select one packageNum.");
            }

            return candidates[0];
        }

        private static Socket_PacketInfo CreatePacket(
            TreasurePacketTemplate template,
            byte[] buffer)
        {
            return new Socket_PacketInfo
            {
                PacketSocket = 0,
                PacketType = template.PacketType,
                PacketFrom = template.PacketFrom,
                PacketTo = template.PacketTo,
                PacketBuffer = (byte[])buffer.Clone(),
                PacketLen = buffer.Length,
                PacketTime = DateTime.UtcNow
            };
        }

        private static Socket_PacketInfo CreatePacket(
            TreasurePacketRoute route,
            byte[] buffer)
        {
            return new Socket_PacketInfo
            {
                PacketSocket = 0,
                PacketType = route.PacketType,
                PacketFrom = route.PacketFrom,
                PacketTo = route.PacketTo,
                PacketBuffer = (byte[])buffer.Clone(),
                PacketLen = buffer.Length,
                PacketTime = DateTime.UtcNow
            };
        }

        private static TreasurePacketTemplate FindCurrentTemplate(
            TreasurePacketRoute route,
            Func<byte[], bool> validator,
            bool useTemplate)
        {
            Socket_PacketInfo candidate = CaptureCurrentPacketsForRoute()
                .Where(item => item != null &&
                    IsSendPacketType(item.PacketType) &&
                    validator(item.PacketBuffer) &&
                    MatchesRoute(route, item.PacketType, item.PacketTo))
                .OrderByDescending(item => item.PacketTime)
                .FirstOrDefault();

            if (candidate != null)
            {
                // The capture list is also a valid source for the session
                // sequence when a caller reached this method without first
                // draining the hook queue through SocketToList.
                ObserveCapturedPacket(candidate);
                return new TreasurePacketTemplate(
                    candidate.PacketType,
                    candidate.PacketFrom,
                    candidate.PacketTo,
                    candidate.PacketBuffer);
            }

            lock (SessionCacheSync)
            {
                TreasurePacketTemplate cached = useTemplate
                    ? sessionUseTemplate
                    : sessionJumpTemplate;
                if (cached != null &&
                    validator(cached.Buffer) &&
                    MatchesRoute(route, cached.PacketType, cached.PacketTo))
                {
                    return CloneTemplate(cached);
                }
            }

            return null;
        }

        private static TreasurePacketTemplate CloneTemplate(TreasurePacketTemplate template)
        {
            return new TreasurePacketTemplate(
                template.PacketType,
                template.PacketFrom,
                template.PacketTo,
                template.Buffer);
        }

        private static TreasurePacketRoute CloneRoute(TreasurePacketRoute route)
        {
            return new TreasurePacketRoute(
                route.PacketType,
                route.PacketFrom,
                route.PacketTo);
        }

        private static bool MatchesRoute(
            TreasurePacketTemplate template,
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetTo)
        {
            if (template.PacketType != packetType)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(template.PacketTo))
            {
                return true;
            }

            return string.Equals(
                template.PacketTo.Trim(),
                (packetTo ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesRoute(
            TreasurePacketRoute route,
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetTo)
        {
            if (route.PacketType != packetType)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(route.PacketTo))
            {
                return true;
            }

            return string.Equals(
                route.PacketTo.Trim(),
                (packetTo ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesRoute(
            TreasurePacketRoute route,
            Socket_PacketInfo packet)
        {
            return route != null && packet != null &&
                route.PacketType == packet.PacketType &&
                string.Equals(
                    route.PacketFrom ?? string.Empty,
                    packet.PacketFrom ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    route.PacketTo ?? string.Empty,
                    packet.PacketTo ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCurrentGameFrame(byte[] buffer)
        {
            return buffer != null && buffer.Length >= TreasureUsePacketContract.ProtocolOffset + 2 &&
                buffer[0] == 0x4D && buffer[1] == 0x5A &&
                (buffer[TreasureUsePacketContract.ProtocolOffset] != 0 ||
                    buffer[TreasureUsePacketContract.ProtocolOffset + 1] != 0);
        }

        private static bool IsJumpTemplate(byte[] buffer)
        {
            try
            {
                TreasurePacketTemplatePatcher.PatchJump(buffer, ValidationTarget);
                return true;
            }
            catch (TreasurePacketTemplateException)
            {
                return false;
            }
        }

        private static bool IsUseTemplate(byte[] buffer)
        {
            try
            {
                TreasurePacketTemplatePatcher.PatchUse(buffer, ValidationTarget);
                return true;
            }
            catch (TreasurePacketTemplateException)
            {
                return false;
            }
        }

        private static bool IsAutoDigTemplate(byte[] buffer)
        {
            return TreasureAutoDigPacketContract.IsFrame(buffer);
        }

        private static bool IsEncodedJumpFrame(byte[] buffer)
        {
            try
            {
                TreasurePacketEncoder.DecodeJump(buffer);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsEncodedUseFrame(byte[] buffer)
        {
            try
            {
                TreasurePacketEncoder.DecodeUse(buffer);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsEncodedAutoDigFrame(byte[] buffer)
        {
            try
            {
                TreasurePacketEncoder.ValidateAutoDig(buffer);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryReadSessionSequence(byte[] buffer, out uint sequence)
        {
            sequence = 0;
            if (buffer == null ||
                buffer.Length < TreasurePacketSessionHeader.SequenceOffset +
                    TreasurePacketSessionHeader.SequenceLength)
            {
                return false;
            }

            sequence = TreasurePacketSessionHeader.ReadSequence(buffer);
            return true;
        }

        private static bool TryBindNextSessionSequence(
            byte[] buffer,
            out string errorCode)
        {
            // The supplied client captures show this field advancing with
            // outgoing game frames. Use the next value only after a current
            // non-zero session value has been observed; do not invent a
            // value for a session that has not produced one.
            errorCode = string.Empty;
            if (buffer == null ||
                buffer.Length < TreasurePacketSessionHeader.SequenceOffset +
                    TreasurePacketSessionHeader.SequenceLength)
            {
                errorCode = "session_sequence_unavailable";
                return false;
            }

            lock (SessionCacheSync)
            {
                if (!sessionSequenceAvailable)
                {
                    uint templateSequence =
                        TreasurePacketSessionHeader.ReadSequence(buffer);
                    if (templateSequence == 0)
                    {
                        errorCode = "session_sequence_unavailable";
                        return false;
                    }

                    sessionLastSequence = templateSequence;
                    sessionSequenceAvailable = true;
                }

                uint nextSequence = unchecked(sessionLastSequence + 1U);
                if (nextSequence == 0)
                {
                    errorCode = "session_sequence_exhausted";
                    return false;
                }

                TreasurePacketSessionHeader.WriteSequence(buffer, nextSequence);
                sessionLastSequence = nextSequence;
            }

            return true;
        }

        private static bool IsSendPacketType(Socket_Cache.SocketPacket.PacketType packetType)
        {
            switch (packetType)
            {
                case Socket_Cache.SocketPacket.PacketType.WS1_Send:
                case Socket_Cache.SocketPacket.PacketType.WS2_Send:
                case Socket_Cache.SocketPacket.PacketType.WS1_SendTo:
                case Socket_Cache.SocketPacket.PacketType.WS2_SendTo:
                case Socket_Cache.SocketPacket.PacketType.WSASend:
                case Socket_Cache.SocketPacket.PacketType.WSASendTo:
                    return true;
                default:
                    return false;
            }
        }

        private static int ResolveCurrentCapturedSocket(
            Socket_Cache.SocketPacket.PacketType packetType,
            string packetTo,
            bool isUseFrame,
            bool preferActionSpecificSocketFallback)
        {
            return preferActionSpecificSocketFallback
                ? ResolveCurrentSessionSocket(packetType, packetTo, isUseFrame)
                : ResolveCurrentSessionSocket(packetType, packetTo);
        }

        private static void RequireBoolean(
            JObject state,
            string propertyName,
            bool expected,
            string code)
        {
            JToken token = state[propertyName];
            if (token == null || token.Type != JTokenType.Boolean || token.Value<bool>() != expected)
            {
                throw new TreasurePacketRuntimeException(
                    code,
                    "Resident state field '" + propertyName + "' is not the required value.");
            }
        }

        private static bool TryReadPositiveInt(JObject item, string propertyName, out int value)
        {
            return TryReadInt(item, propertyName, 1, out value);
        }

        private static bool TryReadNonNegativeInt(JObject item, string propertyName, out int value)
        {
            return TryReadInt(item, propertyName, 0, out value);
        }

        private static bool TryReadInt(JObject item, string propertyName, int minimum, out int value)
        {
            value = 0;
            JToken token = item[propertyName];
            if (token == null || token.Type != JTokenType.Integer)
            {
                return false;
            }

            long parsed;
            try
            {
                parsed = token.Value<long>();
            }
            catch (Exception)
            {
                return false;
            }

            if (parsed < minimum || parsed > int.MaxValue)
            {
                return false;
            }

            value = (int)parsed;
            return true;
        }
    }
}
