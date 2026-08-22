using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Net;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WPELibrary.Lib;
using WPELibrary.Lib.Vision;

namespace WPELibrary.Lib.WebAPI
{
    [RoutePrefix("MobileSync")]
    public sealed class MobileSync_Controller : ApiController
    {
        [HttpGet]
        [Route("manifest")]
        public IHttpActionResult GetManifest()
        {
            try
            {
                MobilePresetSnapshot snapshot = MobilePresetSnapshotBuilder.Build();
                return Ok(snapshot.ToManifestObject());
            }
            catch (MobileSnapshotTooLargeException ex)
            {
                return SnapshotTooLarge(ex);
            }
        }

        [HttpGet]
        [Route("snapshot")]
        public IHttpActionResult GetSnapshot()
        {
            try
            {
                return Ok(MobilePresetSnapshotBuilder.Build().ToResponseObject());
            }
            catch (MobileSnapshotTooLargeException ex)
            {
                return SnapshotTooLarge(ex);
            }
        }

        [HttpGet]
        [Route("runtime")]
        public IHttpActionResult GetRuntime()
        {
            return Ok(new
            {
                send = MobileSendRuntime.GetStatus(),
                progression = MobileProgressionRuntime.GetStatus(),
                assistant = MobileAssistantRuntime.GetStatus()
            });
        }

        [HttpPost]
        [Route("send/{sid:guid}/start")]
        public IHttpActionResult StartSend(Guid sid, [FromBody] MobileActionRequest request)
        {
            MobileActionResponse cached;
            if (TryGetCached("send-start", request == null ? null : request.RequestId, out cached))
            {
                return ToActionResult(cached, request.RequestId);
            }
            MobilePresetSnapshot snapshot;
            IHttpActionResult validation = ValidateRequest("send", sid, request, false, out snapshot);
            if (validation != null)
            {
                return validation;
            }
            return ExecuteAction("send-start", request.RequestId,
                () => MobileSendRuntime.Start(sid, snapshot.Revision));
        }

        [HttpPost]
        [Route("send/{sid:guid}/pause")]
        public IHttpActionResult PauseSend(Guid sid, [FromBody] MobileActionRequest request)
        {
            MobileActionResponse cached;
            if (TryGetCached("send-pause", request == null ? null : request.RequestId, out cached))
            {
                return ToActionResult(cached, request.RequestId);
            }
            MobilePresetSnapshot snapshot;
            IHttpActionResult validation = ValidateRequest("send", sid, request, true, out snapshot);
            if (validation != null)
            {
                return validation;
            }
            return ExecuteAction("send-pause", request.RequestId,
                () => MobileSendRuntime.Pause(sid, request.JobId, snapshot.Revision));
        }

        [HttpPost]
        [Route("send/stop")]
        public IHttpActionResult StopSend([FromBody] MobileActionRequest request)
        {
            MobileActionResponse cached;
            if (TryGetCached("send-stop", request == null ? null : request.RequestId, out cached))
            {
                return ToActionResult(cached, request.RequestId);
            }
            MobilePresetSnapshot snapshot;
            IHttpActionResult validation = ValidateRequest("send", Guid.Empty, request, true, out snapshot);
            if (validation != null)
            {
                return validation;
            }
            return ExecuteAction("send-stop", request.RequestId,
                () => MobileSendRuntime.Stop(request.JobId, request.ExpectedRevision));
        }

        [HttpPost]
        [Route("progression/{bid:guid}/start")]
        public IHttpActionResult StartProgression(Guid bid, [FromBody] MobileActionRequest request)
        {
            MobileActionResponse cached;
            if (TryGetCached("progression-start", request == null ? null : request.RequestId, out cached))
            {
                return ToActionResult(cached, request.RequestId);
            }
            MobilePresetSnapshot snapshot;
            IHttpActionResult validation = ValidateRequest("progression", bid, request, false, out snapshot);
            if (validation != null)
            {
                return validation;
            }
            return ExecuteAction("progression-start", request.RequestId,
                () => MobileProgressionRuntime.Start(bid, snapshot.Revision));
        }

        [HttpPost]
        [Route("progression/{bid:guid}/pause")]
        public IHttpActionResult PauseProgression(Guid bid, [FromBody] MobileActionRequest request)
        {
            MobileActionResponse cached;
            if (TryGetCached("progression-pause", request == null ? null : request.RequestId, out cached))
            {
                return ToActionResult(cached, request.RequestId);
            }
            MobilePresetSnapshot snapshot;
            IHttpActionResult validation = ValidateRequest("progression", bid, request, true, out snapshot);
            if (validation != null)
            {
                return validation;
            }
            return ExecuteAction("progression-pause", request.RequestId,
                () => MobileProgressionRuntime.Pause(bid, request.JobId, snapshot.Revision));
        }

        [HttpPost]
        [Route("progression/stop")]
        public IHttpActionResult StopProgression([FromBody] MobileActionRequest request)
        {
            MobileActionResponse cached;
            if (TryGetCached("progression-stop", request == null ? null : request.RequestId, out cached))
            {
                return ToActionResult(cached, request.RequestId);
            }
            MobilePresetSnapshot snapshot;
            IHttpActionResult validation = ValidateRequest("progression", Guid.Empty, request, true, out snapshot);
            if (validation != null)
            {
                return validation;
            }
            return ExecuteAction("progression-stop", request.RequestId,
                () => MobileProgressionRuntime.Stop(request.JobId, request.ExpectedRevision));
        }

        [HttpPost]
        [Route("assistant/{rid:guid}/start")]
        public IHttpActionResult StartAssistant(Guid rid, [FromBody] MobileActionRequest request)
        {
            MobileActionResponse cached;
            if (TryGetCached("assistant-start", request == null ? null : request.RequestId, out cached))
            {
                return ToActionResult(cached, request.RequestId);
            }
            MobilePresetSnapshot snapshot;
            IHttpActionResult validation = ValidateRequest("assistant", rid, request, false, out snapshot);
            if (validation != null)
            {
                return validation;
            }
            return ExecuteAction("assistant-start", request.RequestId,
                () => MobileAssistantRuntime.Start(rid, snapshot.Revision));
        }

        [HttpPost]
        [Route("assistant/{rid:guid}/pause")]
        public IHttpActionResult PauseAssistant(Guid rid, [FromBody] MobileActionRequest request)
        {
            MobileActionResponse cached;
            if (TryGetCached("assistant-pause", request == null ? null : request.RequestId, out cached))
            {
                return ToActionResult(cached, request.RequestId);
            }
            MobilePresetSnapshot snapshot;
            IHttpActionResult validation = ValidateRequest("assistant", rid, request, true, out snapshot);
            if (validation != null)
            {
                return validation;
            }
            return ExecuteAction("assistant-pause", request.RequestId,
                () => MobileAssistantRuntime.Pause(rid, request.JobId, snapshot.Revision));
        }

        [HttpPost]
        [Route("assistant/stop")]
        public IHttpActionResult StopAssistant([FromBody] MobileActionRequest request)
        {
            MobileActionResponse cached;
            if (TryGetCached("assistant-stop", request == null ? null : request.RequestId, out cached))
            {
                return ToActionResult(cached, request.RequestId);
            }
            MobilePresetSnapshot snapshot;
            IHttpActionResult validation = ValidateRequest("assistant", Guid.Empty, request, true, out snapshot);
            if (validation != null)
            {
                return validation;
            }
            return ExecuteAction("assistant-stop", request.RequestId,
                () => MobileAssistantRuntime.Stop(request.JobId, request.ExpectedRevision));
        }

        private bool TryGetCached(string operation, string requestId, out MobileActionResponse result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return false;
            }
            return MobileRequestDeduplicator.TryGet(RequestCacheKey(operation, requestId), out result);
        }

        private IHttpActionResult ExecuteAction(string operation, string requestId,
            Func<MobileActionResponse> action)
        {
            MobileActionResponse result = MobileRequestDeduplicator.GetOrCreate(
                RequestCacheKey(operation, requestId), () =>
                {
                    try
                    {
                        return action();
                    }
                    catch (Exception)
                    {
                        // Cache a safe, non-accepted result so a replay cannot
                        // execute an operation whose outcome is unknown twice.
                        return MobileRuntimeFactory.Rejected(
                            Guid.Empty,
                            Guid.Empty,
                            string.Empty,
                            operation,
                            "runtime_faulted",
                            "电脑端操作结果未知，请刷新运行状态。");
                    }
                });
            return ToActionResult(result, requestId);
        }

        private string RequestCacheKey(string operation, string requestId)
        {
            string username = User == null || User.Identity == null
                ? string.Empty : User.Identity.Name ?? string.Empty;
            return username + "\n" + (operation ?? string.Empty) + "\n" + (requestId ?? string.Empty);
        }

        private IHttpActionResult ValidateRequest(string category, Guid presetId,
            MobileActionRequest request, bool requiresJobId, out MobilePresetSnapshot snapshot)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId)
                || request.RequestId.Length > 128
                || string.IsNullOrWhiteSpace(request.ExpectedRevision))
            {
                snapshot = null;
                return Content(HttpStatusCode.BadRequest, new MobileError
                {
                    Code = "request_invalid",
                    Message = "移动端请求缺少 requestId 或 expectedRevision。",
                    Retryable = false
                });
            }
            try
            {
                snapshot = MobilePresetSnapshotBuilder.Build();
            }
            catch (MobileSnapshotTooLargeException ex)
            {
                snapshot = null;
                return SnapshotTooLarge(ex, request.RequestId);
            }
            // Stop is a job-scoped cleanup action. It must remain available
            // when the desktop has already published a newer preset revision;
            // start/pause/resume still require the current revision below.
            bool jobScopedStop = requiresJobId && presetId == Guid.Empty;
            if (!jobScopedStop
                && !string.Equals(request.ExpectedRevision, snapshot.Revision, StringComparison.OrdinalIgnoreCase))
            {
                return Content(HttpStatusCode.Conflict, new MobileError
                {
                    Code = "preset_revision_mismatch",
                    Message = "电脑端预设已变化，请先更新预设。",
                    Retryable = false,
                    RequestId = request.RequestId
                });
            }
            if (presetId != Guid.Empty && !snapshot.Contains(category, presetId))
            {
                return Content(HttpStatusCode.NotFound, new MobileError
                {
                    Code = "preset_not_found",
                    Message = "电脑端找不到该预设。",
                    Retryable = false,
                    RequestId = request.RequestId
                });
            }
            if (requiresJobId && request.JobId == Guid.Empty)
            {
                return Content(HttpStatusCode.BadRequest, new MobileError
                {
                    Code = "job_not_found",
                    Message = "请求缺少当前任务 jobId。",
                    Retryable = false,
                    RequestId = request.RequestId
                });
            }
            return null;
        }

        private IHttpActionResult SnapshotTooLarge(
            MobileSnapshotTooLargeException exception,
            string requestId = null)
        {
            return Content((HttpStatusCode)413, new MobileError
            {
                Code = "snapshot_too_large",
                Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "The mobile preset snapshot is {0} bytes; the maximum is {1} bytes.",
                    exception.PayloadBytes,
                    MobilePresetSnapshotBuilder.MaxSnapshotBytes),
                Retryable = false,
                RequestId = requestId
            });
        }

        private IHttpActionResult ToActionResult(MobileActionResponse result, string requestId)
        {
            if (result.Accepted)
            {
                return Ok(result);
            }
            return Content(HttpStatusCode.Conflict, MobileError.From(result, requestId));
        }
    }

    public sealed class MobileActionRequest
    {
        [JsonProperty("expectedRevision")]
        public string ExpectedRevision { get; set; }

        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        [JsonProperty("jobId")]
        public Guid JobId { get; set; }
    }

    public sealed class MobileError
    {
        [JsonProperty("code")]
        public string Code { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("retryable")]
        public bool Retryable { get; set; }
        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        public static MobileError From(MobileActionResponse response, string requestId)
        {
            return new MobileError
            {
                Code = string.IsNullOrEmpty(response.ErrorCode) ? "runtime_conflict" : response.ErrorCode,
                Message = string.IsNullOrEmpty(response.Detail) ? "电脑端未接受此操作。" : response.Detail,
                Retryable = response.ErrorCode == "runtime_busy",
                RequestId = requestId
            };
        }
    }

    public sealed class MobileActionResponse
    {
        [JsonProperty("accepted")]
        public bool Accepted { get; set; }
        [JsonProperty("id")]
        public Guid Id { get; set; }
        [JsonProperty("jobId")]
        public Guid JobId { get; set; }
        [JsonProperty("revision")]
        public string Revision { get; set; }
        [JsonProperty("action")]
        public string Action { get; set; }
        [JsonIgnore]
        public string ErrorCode { get; set; }
        [JsonIgnore]
        public string Detail { get; set; }
    }

    public sealed class MobileRuntimeStatus
    {
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("starting")] public bool Starting { get; set; }
        [JsonProperty("running")] public bool Running { get; set; }
        [JsonProperty("pausing")] public bool Pausing { get; set; }
        [JsonProperty("paused")] public bool Paused { get; set; }
        [JsonProperty("stopping")] public bool Stopping { get; set; }
        [JsonProperty("presetId")] public Guid PresetId { get; set; }
        [JsonProperty("jobId")] public Guid JobId { get; set; }
        [JsonProperty("revision")] public string Revision { get; set; }
        [JsonProperty("startedAt")] public DateTime? StartedAt { get; set; }
        [JsonProperty("finishedAt")] public DateTime? FinishedAt { get; set; }
        [JsonProperty("completedCount")] public int? CompletedCount { get; set; }
        [JsonProperty("totalCount")] public int? TotalCount { get; set; }
        [JsonProperty("errorCode")] public string ErrorCode { get; set; }
        [JsonProperty("detail")] public string Detail { get; set; }
        [JsonProperty("updatedAt")] public DateTime UpdatedAt { get; set; }

        public bool IsBusy
        {
            get { return Starting || Running || Pausing || Paused || Stopping; }
        }
    }

    public sealed class MobileGroupInfo
    {
        [JsonProperty("id")] public Guid Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("sortOrder")] public int SortOrder { get; set; }
    }

    public sealed class MobilePresetDescriptor
    {
        [JsonProperty("id")] public Guid Id { get; set; }
        [JsonProperty("groupId")] public Guid GroupId { get; set; }
        [JsonProperty("groupName")] public string GroupName { get; set; }
        [JsonProperty("folder")] public string Folder { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("sortOrder")] public int SortOrder { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("systemSocket")] public bool SystemSocket { get; set; }
        [JsonProperty("loopCount")] public int LoopCount { get; set; }
        [JsonProperty("intervalMs")] public int IntervalMs { get; set; }
        [JsonProperty("nextIntervalMs", NullValueHandling = NullValueHandling.Ignore)] public int? NextIntervalMs { get; set; }
        [JsonProperty("notes")] public string Notes { get; set; }
        [JsonProperty("packets", NullValueHandling = NullValueHandling.Ignore)] public JArray Packets { get; set; }
        [JsonProperty("mode", NullValueHandling = NullValueHandling.Ignore)] public string Mode { get; set; }
        [JsonProperty("range", NullValueHandling = NullValueHandling.Ignore)] public JObject Range { get; set; }
        [JsonProperty("combinationFirstPosition", NullValueHandling = NullValueHandling.Ignore)] public int? CombinationFirstPosition { get; set; }
        [JsonProperty("combinationFirstLength", NullValueHandling = NullValueHandling.Ignore)] public int? CombinationFirstLength { get; set; }
        [JsonProperty("combinationFirstIntervalMs", NullValueHandling = NullValueHandling.Ignore)] public int? CombinationFirstIntervalMs { get; set; }
        [JsonProperty("combinationSecondPosition", NullValueHandling = NullValueHandling.Ignore)] public int? CombinationSecondPosition { get; set; }
        [JsonProperty("combinationSecondLength", NullValueHandling = NullValueHandling.Ignore)] public int? CombinationSecondLength { get; set; }
        [JsonProperty("combinationSecondIntervalMs", NullValueHandling = NullValueHandling.Ignore)] public int? CombinationSecondIntervalMs { get; set; }
        [JsonProperty("packetType", NullValueHandling = NullValueHandling.Ignore)] public string PacketType { get; set; }
        [JsonProperty("from", NullValueHandling = NullValueHandling.Ignore)] public string From { get; set; }
        [JsonProperty("to", NullValueHandling = NullValueHandling.Ignore)] public string To { get; set; }
        [JsonProperty("bufferBase64", NullValueHandling = NullValueHandling.Ignore)] public string BufferBase64 { get; set; }
        [JsonProperty("byteAnnotations", NullValueHandling = NullValueHandling.Ignore)] public JArray ByteAnnotations { get; set; }
        [JsonProperty("instructions", NullValueHandling = NullValueHandling.Ignore)] public JArray Instructions { get; set; }
        // Keep an explicit null in the allow-list DTO.  Android validates that
        // the field exists even when the assistant has no visual profile.
        [JsonProperty("visionProfile")] public JToken VisionProfile { get; set; }
    }

    public sealed class MobilePresetSnapshot
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonProperty("revision")] public string Revision { get; set; }
        [JsonProperty("payloadSha256")] public string PayloadSha256 { get; set; }
        [JsonProperty("payloadBytes")] public int PayloadBytes { get; set; }
        [JsonProperty("generatedAt")] public DateTime GeneratedAt { get; set; }
        [JsonProperty("sendGroups")] public List<MobileGroupInfo> SendGroups { get; set; }
        [JsonProperty("progressionGroups")] public List<MobileGroupInfo> ProgressionGroups { get; set; }
        [JsonProperty("assistantGroups")] public List<MobileGroupInfo> AssistantGroups { get; set; }
        [JsonProperty("send")] public List<MobilePresetDescriptor> Send { get; set; }
        [JsonProperty("progression")] public List<MobilePresetDescriptor> Progression { get; set; }
        [JsonProperty("assistant")] public List<MobilePresetDescriptor> Assistant { get; set; }

        [JsonIgnore] internal JObject Payload { get; set; }

        public bool Contains(string category, Guid id)
        {
            IEnumerable<MobilePresetDescriptor> source = category == "progression" ? Progression
                : category == "assistant" ? Assistant : Send;
            return source.Any(item => item.Id == id);
        }

        public JObject ToManifestObject()
        {
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["revision"] = Revision,
                ["payloadSha256"] = PayloadSha256,
                ["payloadBytes"] = PayloadBytes,
                ["generatedAt"] = GeneratedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["sendCount"] = Send.Count,
                ["progressionCount"] = Progression.Count,
                ["assistantCount"] = Assistant.Count,
                ["capabilities"] = new JArray("fullSnapshot", "expectedRevision", "requestDedup", "gzipSnapshot")
            };
        }

        public JObject ToResponseObject()
        {
            JObject result = (JObject)Payload.DeepClone();
            result["revision"] = Revision;
            result["payloadSha256"] = PayloadSha256;
            result["payloadBytes"] = PayloadBytes;
            result["generatedAt"] = GeneratedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            return result;
        }
    }

    internal static class MobilePresetSnapshotBuilder
    {
        internal const int MaxSnapshotBytes = 8 * 1024 * 1024;

        public static MobilePresetSnapshot Build()
        {
            List<MobilePresetDescriptor> send = null;
            List<MobilePresetDescriptor> progression = null;
            List<MobilePresetDescriptor> assistant = null;
            Action capture = () =>
            {
                send = Socket_Cache.SendList.lstSend.Select((item, index) => BuildSend(item)).ToList();
                progression = Socket_Cache.ByteSweepList.lstPresets.Select(item => BuildProgression(item)).ToList();
                assistant = Socket_Cache.RobotList.lstRobot.Select((item, index) => BuildAssistant(item, index)).ToList();
            };
            Action<Action> invoke = Socket_Cache.System.InvokeAction;
            if (invoke != null)
            {
                invoke(capture);
            }
            else
            {
                capture();
            }
            send = send ?? new List<MobilePresetDescriptor>();
            progression = progression ?? new List<MobilePresetDescriptor>();
            assistant = assistant ?? new List<MobilePresetDescriptor>();
            AssignGroupIds("send", send);
            AssignGroupIds("progression", progression);
            AssignGroupIds("assistant", assistant);
            send.Sort(ComparePreset);
            progression.Sort(ComparePreset);
            assistant.Sort(ComparePreset);

            List<MobileGroupInfo> sendGroups = BuildGroups("send", send);
            List<MobileGroupInfo> progressionGroups = BuildGroups("progression", progression);
            List<MobileGroupInfo> assistantGroups = BuildGroups("assistant", assistant);
            JObject payload = new JObject
            {
                ["schemaVersion"] = 2,
                ["sendGroups"] = JArray.FromObject(sendGroups),
                ["progressionGroups"] = JArray.FromObject(progressionGroups),
                ["assistantGroups"] = JArray.FromObject(assistantGroups),
                ["send"] = JArray.FromObject(send),
                ["progression"] = JArray.FromObject(progression),
                ["assistant"] = JArray.FromObject(assistant)
            };
            string canonical = Canonicalize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(canonical);
            if (bytes.Length > MaxSnapshotBytes)
            {
                throw new MobileSnapshotTooLargeException(bytes.Length);
            }
            return new MobilePresetSnapshot
            {
                SchemaVersion = 2,
                Revision = Sha256(bytes),
                PayloadSha256 = Sha256(bytes),
                PayloadBytes = bytes.Length,
                GeneratedAt = DateTime.UtcNow,
                SendGroups = sendGroups,
                ProgressionGroups = progressionGroups,
                AssistantGroups = assistantGroups,
                Send = send,
                Progression = progression,
                Assistant = assistant,
                Payload = payload
            };
        }

        private static MobilePresetDescriptor BuildSend(Socket_SendInfo item)
        {
            EnsureGuid(item.SID, "send preset");
            EnsurePositive(item.SSortOrder, "send sortOrder");
            EnsureNonNegative(item.SLoopCNT, "send loopCount");
            EnsureNonNegative(item.SLoopINT, "send intervalMs");
            string folder = string.IsNullOrWhiteSpace(item.SFolder) ? "常用" : item.SFolder;
            return new MobilePresetDescriptor
            {
                Id = item.SID,
                GroupId = Guid.Empty,
                GroupName = folder,
                Folder = folder,
                Name = item.SName ?? string.Empty,
                SortOrder = item.SSortOrder,
                Enabled = item.IsEnable,
                SystemSocket = item.SSystemSocket,
                LoopCount = item.SLoopCNT,
                IntervalMs = item.SLoopINT,
                Notes = item.SNotes ?? string.Empty,
                Packets = new JArray((item.SCollection ?? new System.ComponentModel.BindingList<Socket_PacketInfo>())
                    .Select(BuildPacket))
            };
        }

        private static MobilePresetDescriptor BuildProgression(Socket_ByteSweepPresetInfo item)
        {
            EnsureGuid(item.BID, "progression preset");
            EnsurePositive(item.BSortOrder, "progression sortOrder");
            EnsureNonNegative(item.BLoopCount, "progression loopCount");
            EnsureNonNegative(item.BInterval, "progression intervalMs");
            EnsureNonNegative(item.BNextInterval, "progression nextIntervalMs");
            EnsureNonNegative(item.BStart, "progression range.start");
            EnsureNonNegative(item.BLength, "progression range.length");
            if (item.BLoopCount <= 0 || item.Buffer == null || item.BStart > item.Buffer.Length ||
                item.BLength <= 0 || item.BLength > item.Buffer.Length - item.BStart)
            {
                throw new InvalidOperationException("Mobile progression range is outside the preset buffer.");
            }
            if (item.BMode != Socket_ByteSweepMode.Sequential && item.BMode != Socket_ByteSweepMode.PairCombination)
            {
                throw new InvalidOperationException("Mobile progression mode is invalid.");
            }
                if (item.BMode == Socket_ByteSweepMode.PairCombination)
                {
                EnsureNonNegative(item.BCombinationFirstPosition, "progression first position");
                EnsureNonNegative(item.BCombinationFirstLength, "progression first length");
                EnsureNonNegative(item.BCombinationFirstInterval, "progression first interval");
                EnsureNonNegative(item.BCombinationSecondPosition, "progression second position");
                EnsureNonNegative(item.BCombinationSecondLength, "progression second length");
                EnsureNonNegative(item.BCombinationSecondInterval, "progression second interval");
                if (item.BCombinationFirstPosition >= item.Buffer.Length ||
                    item.BCombinationSecondPosition >= item.Buffer.Length ||
                    item.BCombinationFirstPosition == item.BCombinationSecondPosition ||
                    item.BCombinationFirstLength <= 0 || item.BCombinationFirstLength > 255 ||
                    item.BCombinationSecondLength <= 0 || item.BCombinationSecondLength > 255 ||
                    item.BCombinationFirstLength > item.Buffer.Length - item.BCombinationFirstPosition ||
                    item.BCombinationSecondLength > item.Buffer.Length - item.BCombinationSecondPosition)
                {
                    throw new InvalidOperationException("Mobile progression pair-combination range is invalid.");
                }
            }
            ValidateAnnotations(item.ByteAnnotations, item.Buffer.Length, "progression");
            string folder = string.IsNullOrWhiteSpace(item.BFolder) ? "常用" : item.BFolder;
            return new MobilePresetDescriptor
            {
                Id = item.BID,
                GroupId = Guid.Empty,
                GroupName = folder,
                Folder = folder,
                Name = item.BName ?? string.Empty,
                SortOrder = item.BSortOrder,
                Enabled = item.IsEnable,
                LoopCount = item.BLoopCount,
                IntervalMs = item.BInterval,
                NextIntervalMs = item.BNextInterval,
                Mode = item.BMode.ToString().ToLowerInvariant(),
                Range = new JObject { ["start"] = item.BStart, ["length"] = item.BLength },
                CombinationFirstPosition = item.BMode == Socket_ByteSweepMode.PairCombination ? item.BCombinationFirstPosition : (int?)null,
                CombinationFirstLength = item.BMode == Socket_ByteSweepMode.PairCombination ? item.BCombinationFirstLength : (int?)null,
                CombinationFirstIntervalMs = item.BMode == Socket_ByteSweepMode.PairCombination ? item.BCombinationFirstInterval : (int?)null,
                CombinationSecondPosition = item.BMode == Socket_ByteSweepMode.PairCombination ? item.BCombinationSecondPosition : (int?)null,
                CombinationSecondLength = item.BMode == Socket_ByteSweepMode.PairCombination ? item.BCombinationSecondLength : (int?)null,
                CombinationSecondIntervalMs = item.BMode == Socket_ByteSweepMode.PairCombination ? item.BCombinationSecondInterval : (int?)null,
                PacketType = item.PacketType.ToString(),
                From = item.PacketFrom ?? string.Empty,
                To = item.PacketTo ?? string.Empty,
                BufferBase64 = Convert.ToBase64String(item.Buffer ?? new byte[0]),
                ByteAnnotations = new JArray((item.ByteAnnotations ?? new List<Socket_ByteAnnotationInfo>())
                    .Select(annotation => new JObject
                    {
                        ["start"] = annotation.Start,
                        ["length"] = annotation.Length,
                        ["note"] = annotation.Note ?? string.Empty,
                        ["color"] = annotation.Color.ToString()
                    }))
            };
        }

        private static MobilePresetDescriptor BuildAssistant(Socket_RobotInfo item, int index)
        {
            EnsureGuid(item.RID, "assistant preset");
            string folder = string.IsNullOrWhiteSpace(item.RFolder) ? "常用" : item.RFolder;
            JArray instructions = new JArray();
            if (item.RInstruction != null)
            {
                foreach (DataRow row in item.RInstruction.Rows)
                {
                    JObject instruction = new JObject();
                    foreach (DataColumn column in item.RInstruction.Columns)
                    {
                        instruction[column.ColumnName] = row[column] == DBNull.Value
                            ? string.Empty : JToken.FromObject(row[column]);
                    }
                    instructions.Add(instruction);
                }
            }
            JToken vision = JValue.CreateNull();
            try
            {
                // Socket_VisionProfile also contains the currently resolved
                // desktop window and executable-bound runtime state.  Never
                // serialize that object directly into the mobile snapshot.
                vision = BuildAssistantVisionProfile(item.VisionProfile);
            }
            catch
            {
                // A malformed optional visual profile must not hide the assistant preset.
            }
            return new MobilePresetDescriptor
            {
                Id = item.RID,
                GroupId = Guid.Empty,
                GroupName = folder,
                Folder = folder,
                Name = item.RName ?? string.Empty,
                SortOrder = index + 1,
                Enabled = item.IsEnable,
                Instructions = instructions,
                VisionProfile = vision
            };
        }

        private static JToken BuildAssistantVisionProfile(Socket_VisionProfile profile)
        {
            if (profile == null)
            {
                return JValue.CreateNull();
            }

            JObject result = new JObject
            {
                ["region"] = BuildVisionRegion(profile.Region),
                ["ocrOptions"] = BuildVisionOcrOptions(profile.OcrOptions),
                ["ocrCondition"] = BuildVisionTextCondition(profile.OcrCondition),
                ["captureSettings"] = BuildVisionCaptureSettings(profile.CaptureSettings),
                ["assistantSteps"] = new JArray((profile.AssistantSteps ?? new List<VisionAssistantStep>())
                    .Where(step => step != null)
                    .Select(BuildVisionAssistantStep))
            };
            return result;
        }

        private static JObject BuildVisionAssistantStep(VisionAssistantStep step)
        {
            return new JObject
            {
                ["name"] = step.Name ?? string.Empty,
                ["condition"] = BuildVisionCondition(step.Condition),
                // Action is an executable interface/delegate and is deliberately
                // not part of the transport DTO.
                ["actionDefinition"] = BuildVisionActionDefinition(step.ActionDefinition),
                ["verificationEnabled"] = step.VerificationEnabled,
                ["verification"] = BuildVisionCondition(step.Verification),
                ["verificationUsesSeparateRegion"] = step.VerificationUsesSeparateRegion
            };
        }

        private static JObject BuildVisionCondition(VisionConditionDefinition condition)
        {
            if (condition == null)
            {
                return null;
            }

            return new JObject
            {
                ["name"] = condition.Name ?? string.Empty,
                ["type"] = condition.Type.ToString(),
                ["region"] = BuildVisionRegion(condition.Region),
                ["textCondition"] = BuildVisionTextCondition(condition.TextCondition),
                ["colorCondition"] = BuildVisionColorCondition(condition.ColorCondition),
                ["template"] = BuildVisionTemplate(condition.Template),
                ["templateVariants"] = new JArray((condition.TemplateVariants ?? new List<Bitmap>())
                    .Where(template => template != null)
                    .Select(BuildVisionTemplate)),
                ["minimumSimilarity"] = condition.MinimumSimilarity,
                ["normalizeTemplateBrightness"] = condition.NormalizeTemplateBrightness,
                ["allowTemplateScaleVariation"] = condition.AllowTemplateScaleVariation,
                ["templateMinimumScale"] = condition.TemplateMinimumScale,
                ["templateMaximumScale"] = condition.TemplateMaximumScale,
                ["templateScaleStep"] = condition.TemplateScaleStep,
                ["requiredConfirmations"] = condition.RequiredConfirmations,
                ["pollIntervalMilliseconds"] = condition.PollIntervalMilliseconds,
                ["timeoutMilliseconds"] = condition.TimeoutMilliseconds,
                ["maxRetries"] = condition.MaxRetries,
                ["failurePolicy"] = condition.FailurePolicy.ToString()
            };
        }

        private static JObject BuildVisionActionDefinition(VisionActionDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new JObject
            {
                ["type"] = definition.Type.ToString(),
                ["scrollDirection"] = definition.ScrollDirection.ToString(),
                ["scrollAmount"] = definition.ScrollAmount,
                ["delayMilliseconds"] = definition.DelayMilliseconds
            };
        }

        private static JObject BuildVisionRegion(VisionRegion region)
        {
            if (region == null)
            {
                return null;
            }

            return new JObject
            {
                ["x"] = region.X,
                ["y"] = region.Y,
                ["width"] = region.Width,
                ["height"] = region.Height,
                ["useNormalizedCoordinates"] = region.UseNormalizedCoordinates,
                ["referenceWidth"] = region.ReferenceWidth,
                ["referenceHeight"] = region.ReferenceHeight
            };
        }

        private static JObject BuildVisionTextCondition(VisionTextCondition condition)
        {
            if (condition == null)
            {
                return null;
            }

            return new JObject
            {
                ["matchMode"] = condition.MatchMode.ToString(),
                ["expectedText"] = condition.ExpectedText ?? string.Empty,
                ["minimumNumber"] = condition.MinimumNumber,
                ["maximumNumber"] = condition.MaximumNumber,
                ["minimumConfidence"] = condition.MinimumConfidence
            };
        }

        private static JObject BuildVisionColorCondition(VisionColorCondition condition)
        {
            if (condition == null)
            {
                return null;
            }

            return new JObject
            {
                ["red"] = condition.Red,
                ["green"] = condition.Green,
                ["blue"] = condition.Blue,
                ["tolerance"] = condition.Tolerance,
                ["minimumPixelCount"] = condition.MinimumPixelCount,
                ["minimumMatchRatio"] = condition.MinimumMatchRatio
            };
        }

        private static JObject BuildVisionOcrOptions(VisionOcrOptions options)
        {
            if (options == null)
            {
                return null;
            }

            return new JObject
            {
                ["scaleFactor"] = options.ScaleFactor,
                ["convertToGrayscale"] = options.ConvertToGrayscale,
                ["useBinaryThreshold"] = options.UseBinaryThreshold,
                ["binaryThreshold"] = options.BinaryThreshold,
                ["contrast"] = options.Contrast,
                ["useAdaptiveThreshold"] = options.UseAdaptiveThreshold,
                ["adaptiveThresholdWindowSize"] = options.AdaptiveThresholdWindowSize,
                ["adaptiveThresholdOffset"] = options.AdaptiveThresholdOffset,
                ["invert"] = options.Invert,
                ["useDenoise"] = options.UseDenoise,
                ["useSharpen"] = options.UseSharpen,
                ["characterWhitelist"] = options.CharacterWhitelist ?? string.Empty,
                ["characterBlacklist"] = options.CharacterBlacklist ?? string.Empty,
                ["language"] = options.Language ?? string.Empty,
                ["timeoutMilliseconds"] = options.TimeoutMilliseconds,
                ["pageSegmentationMode"] = options.PageSegmentationMode,
                ["engine"] = options.Engine.ToString(),
                ["onnxDetectionThreshold"] = options.OnnxDetectionThreshold,
                ["onnxRecognitionThreshold"] = options.OnnxRecognitionThreshold,
                ["onnxMaxImageSide"] = options.OnnxMaxImageSide,
                ["pythonWorkerTimeoutMilliseconds"] = options.PythonWorkerTimeoutMilliseconds
            };
        }

        private static JObject BuildVisionCaptureSettings(VisionCaptureSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            return new JObject
            {
                ["sourceMode"] = settings.SourceMode.ToString(),
                ["minimumIntervalMilliseconds"] = settings.MinimumIntervalMilliseconds,
                ["skipUnchangedFrames"] = settings.SkipUnchangedFrames,
                ["historyLimit"] = settings.HistoryLimit,
                ["blankBrightnessThreshold"] = settings.BlankBrightnessThreshold,
                ["blankContrastThreshold"] = settings.BlankContrastThreshold,
                ["requireExactClientSize"] = settings.RequireExactClientSize,
                ["requiredClientWidth"] = settings.RequiredClientWidth,
                ["requiredClientHeight"] = settings.RequiredClientHeight
            };
        }

        private static JObject BuildVisionTemplate(Bitmap template)
        {
            if (template == null)
            {
                return null;
            }

            byte[] png = VisionResourceSerializer.ToPngBytes(template);
            return new JObject
            {
                ["width"] = template.Width,
                ["height"] = template.Height,
                ["sha256"] = Sha256(png),
                ["pngBase64"] = Convert.ToBase64String(png)
            };
        }

        private static JObject BuildPacket(Socket_PacketInfo packet)
        {
            byte[] data = packet == null ? null : packet.PacketBuffer ?? packet.RawBuffer;
            if (packet != null)
            {
                EnsureNonNegative(packet.PacketLen, "send packet length");
                if (packet.PacketLen > 0 && data != null && packet.PacketLen != data.Length)
                {
                    throw new InvalidOperationException("Mobile send packet length does not match its buffer.");
                }
                ValidateAnnotations(packet.ByteAnnotations, data == null ? 0 : data.Length, "send packet");
            }
            return new JObject
            {
                ["packetType"] = packet == null ? string.Empty : packet.PacketType.ToString(),
                ["from"] = packet == null ? string.Empty : packet.PacketFrom ?? string.Empty,
                ["to"] = packet == null ? string.Empty : packet.PacketTo ?? string.Empty,
                ["length"] = packet == null ? 0 : packet.PacketLen > 0 ? packet.PacketLen : (data == null ? 0 : data.Length),
                ["dataBase64"] = Convert.ToBase64String(data ?? new byte[0]),
                ["byteAnnotations"] = new JArray((packet == null ? new List<Socket_ByteAnnotationInfo>() : packet.ByteAnnotations ?? new List<Socket_ByteAnnotationInfo>())
                    .Select(annotation => new JObject
                    {
                        ["start"] = annotation.Start,
                        ["length"] = annotation.Length,
                        ["note"] = annotation.Note ?? string.Empty,
                        ["color"] = annotation.Color.ToString()
                    }))
            };
        }

        private static List<MobileGroupInfo> BuildGroups(string category, IEnumerable<MobilePresetDescriptor> values)
        {
            return values.GroupBy(item => new { item.GroupId, item.GroupName })
                .OrderBy(group => group.Key.GroupName, StringComparer.Ordinal)
                .Select((group, index) => new MobileGroupInfo
                {
                    Id = group.Key.GroupId,
                    Name = group.Key.GroupName,
                    SortOrder = index + 1
                }).ToList();
        }

        private static void AssignGroupIds(string category, List<MobilePresetDescriptor> values)
        {
            HashSet<Guid> usedGroupIds = new HashSet<Guid>();
            foreach (IGrouping<string, MobilePresetDescriptor> group in values.GroupBy(item => item.GroupName ?? string.Empty))
            {
                Guid groupId = MobileGroupIdentityStore.Get(category, group.Key,
                    group.Select(item => item.Id), usedGroupIds);
                usedGroupIds.Add(groupId);
                foreach (MobilePresetDescriptor item in group)
                {
                    item.GroupId = groupId;
                }
            }
        }

        private static void EnsureGuid(Guid value, string field)
        {
            if (value == Guid.Empty)
            {
                throw new InvalidOperationException("Mobile snapshot contains an empty " + field + " id.");
            }
        }

        private static void EnsureNonNegative(int value, string field)
        {
            if (value < 0)
            {
                throw new InvalidOperationException("Mobile snapshot contains a negative " + field + ".");
            }
        }

        private static void EnsurePositive(int value, string field)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException("Mobile snapshot contains a non-positive " + field + ".");
            }
        }

        private static void ValidateAnnotations(IEnumerable<Socket_ByteAnnotationInfo> annotations,
            int bufferLength, string field)
        {
            if (annotations == null)
            {
                return;
            }
            foreach (Socket_ByteAnnotationInfo annotation in annotations)
            {
                EnsureNonNegative(annotation.Start, field + " annotation start");
                EnsureNonNegative(annotation.Length, field + " annotation length");
                if (annotation.Start > bufferLength || annotation.Length > bufferLength - annotation.Start)
                {
                    throw new InvalidOperationException("Mobile snapshot contains an annotation outside " + field + ".");
                }
            }
        }

        private static int ComparePreset(MobilePresetDescriptor left, MobilePresetDescriptor right)
        {
            int group = left.GroupId.CompareTo(right.GroupId);
            return group != 0 ? group : left.SortOrder != right.SortOrder
                ? left.SortOrder.CompareTo(right.SortOrder) : left.Id.CompareTo(right.Id);
        }

        private static string Canonicalize(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return "null";
            }
            JObject obj = value as JObject;
            if (obj != null)
            {
                return "{" + string.Join(",", obj.Properties()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => JsonConvert.ToString(property.Name) + ":" + Canonicalize(property.Value))) + "}";
            }
            JArray array = value as JArray;
            if (array != null)
            {
                return "[" + string.Join(",", array.Select(Canonicalize)) + "]";
            }
            return value.ToString(Formatting.None);
        }

        private static string Sha256(byte[] value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }

    internal sealed class MobileSnapshotTooLargeException : InvalidOperationException
    {
        public MobileSnapshotTooLargeException(int payloadBytes)
            : base("The mobile preset snapshot exceeds the supported payload size.")
        {
            this.PayloadBytes = payloadBytes;
        }

        public int PayloadBytes { get; private set; }
    }

    internal static class MobileGroupIdentityStore
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, Guid> ByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Guid> BySignature = new Dictionary<string, Guid>(StringComparer.Ordinal);
        private static bool loaded;

        public static Guid Get(string category, string name, IEnumerable<Guid> presetIds,
            ISet<Guid> usedGroupIds = null)
        {
            string normalizedCategory = category ?? string.Empty;
            string normalizedName = name ?? string.Empty;
            string signature = normalizedCategory + "\n" + string.Join(",", (presetIds ?? Enumerable.Empty<Guid>())
                .OrderBy(value => value).Select(value => value.ToString("D")));
            string nameKey = normalizedCategory + "\n" + normalizedName;
            lock (SyncRoot)
            {
                EnsureLoaded();
                Guid id;
                if ((!BySignature.TryGetValue(signature, out id) && !ByName.TryGetValue(nameKey, out id))
                    || id == Guid.Empty || (usedGroupIds != null && usedGroupIds.Contains(id)))
                {
                    do
                    {
                        id = Guid.NewGuid();
                    } while (usedGroupIds != null && usedGroupIds.Contains(id));
                }
                BySignature[signature] = id;
                ByName[nameKey] = id;
                Save();
                return id;
            }
        }

        private static string StorePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WPE", "MobileGroupIds.json");
            }
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;
            if (!File.Exists(StorePath))
            {
                return;
            }
            try
            {
                JObject root = JObject.Parse(File.ReadAllText(StorePath, Encoding.UTF8));
                foreach (JToken token in root["entries"] ?? new JArray())
                {
                    Guid id;
                    if (!Guid.TryParse((string)token["id"], out id) || id == Guid.Empty)
                    {
                        continue;
                    }
                    string category = (string)token["category"] ?? string.Empty;
                    string name = (string)token["name"] ?? string.Empty;
                    string signature = (string)token["signature"] ?? string.Empty;
                    ByName[category + "\n" + name] = id;
                    if (!string.IsNullOrEmpty(signature))
                    {
                        BySignature[signature] = id;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Mobile group ID mapping cannot be loaded.", ex);
            }
        }

        private static void Save()
        {
            string path = StorePath;
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            JArray entries = new JArray();
            foreach (KeyValuePair<string, Guid> item in ByName)
            {
                string[] parts = item.Key.Split(new[] { '\n' }, 2);
                string category = parts.Length > 0 ? parts[0] : string.Empty;
                string name = parts.Length > 1 ? parts[1] : string.Empty;
                string signature = BySignature.FirstOrDefault(value => value.Value == item.Value).Key ?? string.Empty;
                entries.Add(new JObject
                {
                    ["category"] = category,
                    ["name"] = name,
                    ["signature"] = signature,
                    ["id"] = item.Value.ToString("D")
                });
            }
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JObject { ["entries"] = entries }.ToString(Formatting.None), Encoding.UTF8);
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
    }

    internal static class MobileRequestDeduplicator
    {
        private sealed class Entry
        {
            public DateTime CreatedAt { get; set; }
            public MobileActionResponse Result { get; set; }
        }

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, object> RequestLocks =
            new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        private const int MaxEntries = 4096;

        public static bool TryGet(string requestId, out MobileActionResponse result)
        {
            lock (SyncRoot)
            {
                Cleanup();
                Entry entry;
                if (Entries.TryGetValue(requestId ?? string.Empty, out entry))
                {
                    result = entry.Result;
                    return true;
                }
                result = null;
                return false;
            }
        }

        public static MobileActionResponse GetOrCreate(string requestId,
            Func<MobileActionResponse> factory)
        {
            if (string.IsNullOrWhiteSpace(requestId) || factory == null)
            {
                throw new ArgumentException("The mobile action cache request is invalid.");
            }
            object requestLock = RequestLocks.GetOrAdd(requestId, key => new object());
            try
            {
                lock (requestLock)
                {
                    lock (SyncRoot)
                    {
                        Cleanup();
                        Entry entry;
                        if (Entries.TryGetValue(requestId, out entry))
                        {
                            return entry.Result;
                        }
                    }

                    MobileActionResponse result = factory();
                    if (result == null)
                    {
                        throw new InvalidOperationException("The mobile action did not return a result.");
                    }
                    lock (SyncRoot)
                    {
                        Entries[requestId] = new Entry
                        {
                            CreatedAt = DateTime.UtcNow,
                            Result = result
                        };
                    }
                    return result;
                }
            }
            finally
            {
                object ignored;
                RequestLocks.TryRemove(requestId, out ignored);
            }
        }

        private static void Cleanup()
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-15);
            foreach (string key in Entries.Where(item => item.Value.CreatedAt < cutoff).Select(item => item.Key).ToList())
            {
                Entries.Remove(key);
            }

            while (Entries.Count > MaxEntries)
            {
                string oldest = Entries
                    .OrderBy(item => item.Value.CreatedAt)
                    .Select(item => item.Key)
                    .FirstOrDefault();
                if (oldest == null)
                {
                    break;
                }
                Entries.Remove(oldest);
            }
        }
    }

    internal static class MobileRuntimeFactory
    {
        public static MobileActionResponse Accepted(Guid id, Guid jobId, string revision, string action)
        {
            return new MobileActionResponse
            {
                Accepted = true,
                Id = id,
                JobId = jobId,
                Revision = revision ?? string.Empty,
                Action = action
            };
        }

        public static MobileActionResponse Rejected(Guid id, Guid jobId, string revision,
            string action, string code, string detail)
        {
            return new MobileActionResponse
            {
                Accepted = false,
                Id = id,
                JobId = jobId,
                Revision = revision ?? string.Empty,
                Action = action,
                ErrorCode = code,
                Detail = detail
            };
        }

        public static MobileRuntimeStatus Status(string state, Guid presetId, Guid jobId,
            string revision, DateTime? startedAt, DateTime? finishedAt, string errorCode, string detail,
            int? completedCount = null, int? totalCount = null)
        {
            return new MobileRuntimeStatus
            {
                State = state,
                Starting = state == "starting",
                Running = state == "running",
                Pausing = state == "pausing",
                Paused = state == "paused",
                Stopping = state == "stopping",
                PresetId = presetId,
                JobId = jobId,
                Revision = revision ?? string.Empty,
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                ErrorCode = errorCode ?? string.Empty,
                Detail = detail ?? string.Empty,
                CompletedCount = completedCount,
                TotalCount = totalCount,
                UpdatedAt = DateTime.UtcNow
            };
        }
    }

    internal static class MobileSendRuntime
    {
        private static readonly object SyncRoot = new object();
        private static Socket_Send activeSend;
        private static Guid activePresetId;
        private static Guid activeJobId;
        private static Guid lastStoppedPresetId;
        private static Guid lastStoppedJobId;
        private static string activeRevision = string.Empty;
        private static string state = "idle";
        private static DateTime? startedAt;
        private static DateTime? finishedAt;
        private static string detail = string.Empty;
        private static string errorCode = string.Empty;
        private static bool startInFlight;
        private static bool pauseRequested;
        private static bool stopRequested;

        public static MobileActionResponse Start(Guid presetId, string revision)
        {
            Socket_Form socketForm = Application.OpenForms.OfType<Socket_Form>().FirstOrDefault();
            if (socketForm == null)
            {
                return MobileRuntimeFactory.Rejected(
                    presetId,
                    Guid.Empty,
                    revision,
                    "send-start",
                    "runtime_not_connected",
                    "电脑端封包窗口未运行。");
            }

            lock (SyncRoot)
            {
                if (startInFlight)
                {
                    return MobileRuntimeFactory.Rejected(presetId, activeJobId, revision,
                        "send-start", "runtime_busy", "发送任务正在启动，请稍候。");
                }
                if (activeSend != null && activeSend.Worker.IsBusy)
                {
                    if (activePresetId == presetId && activeSend.IsPaused)
                    {
                        if (!string.Equals(activeRevision, revision, StringComparison.OrdinalIgnoreCase))
                        {
                            return MobileRuntimeFactory.Rejected(
                                presetId,
                                activeJobId,
                                revision,
                                "send-resume",
                                "runtime_revision_mismatch",
                                "暂停中的发送任务对应的预设已变化，请先更新任务。");
                        }
                        activeSend.ResumeSend();
                        state = "running";
                        return MobileRuntimeFactory.Accepted(presetId, activeJobId, revision, "send-resume");
                    }
                    return MobileRuntimeFactory.Rejected(presetId, activeJobId, revision, "send-start", "runtime_busy", "当前已有发送任务运行。");
                }
                startInFlight = true;
                activeJobId = Guid.NewGuid();
                activePresetId = presetId;
                activeRevision = revision;
                state = "starting";
                startedAt = DateTime.UtcNow;
                finishedAt = null;
                detail = string.Empty;
                errorCode = string.Empty;
                pauseRequested = false;
                stopRequested = false;
            }
            try
            {
                // Register the job before hook startup so a slow hook admission
                // remains stoppable through the same job-scoped route.
                HookStartResult hookResult = socketForm.EnsureHookRunningForMobile();
                if (!hookResult.Success)
                {
                    return FailedStart(presetId, revision,
                        string.IsNullOrWhiteSpace(hookResult.ErrorMessage)
                            ? "电脑端封包挂钩未启动。"
                            : hookResult.ErrorMessage,
                        "runtime_not_connected");
                }
                Socket_Cache.Send.SendStartResult startResult =
                    Socket_Cache.Send.DoSendWithResult(presetId);
                // DoSend starts the existing BackgroundWorker and returns its handle.
                // A one-packet/one-round preset can finish between that return and
                // this check, so Worker.IsBusy is not a reliable admission signal.
                // A null handle still means the preset could not resolve a usable
                // socket or could not be started at all.
                if (startResult == null || startResult.Send == null)
                {
                    return FailedStart(presetId, revision,
                        startResult == null ? "发送预设未能启动。" : startResult.Error,
                        startResult == null ? "preset_invalid" : startResult.ErrorCode);
                }
                Socket_Send send = startResult.Send;
                bool stopAfterAdmission;
                bool pauseAfterAdmission;
                lock (SyncRoot)
                {
                    activeSend = send;
                    stopAfterAdmission = stopRequested;
                    pauseAfterAdmission = pauseRequested && !stopAfterAdmission;
                    state = stopAfterAdmission
                        ? "stopping" : pauseAfterAdmission && send.Worker.IsBusy
                        ? "paused" : send.Worker.IsBusy ? "running" : "completed";
                    if (!send.Worker.IsBusy && !stopAfterAdmission)
                    {
                        finishedAt = DateTime.UtcNow;
                    }
                }
                if (stopAfterAdmission)
                {
                    send.StopSend();
                }
                else if (pauseAfterAdmission)
                {
                    send.PauseSend();
                }
                Monitor(send, activeJobId);
                return MobileRuntimeFactory.Accepted(presetId, activeJobId, revision, "send-start");
            }
            catch (Exception ex)
            {
                return FailedStart(presetId, revision, ex.Message);
            }
            finally
            {
                lock (SyncRoot)
                {
                    startInFlight = false;
                }
            }
        }

        public static MobileActionResponse Pause(Guid presetId, Guid jobId, string revision)
        {
            lock (SyncRoot)
            {
                if (startInFlight && activeSend == null && activeJobId == jobId
                        && activePresetId == presetId)
                {
                    if (!string.Equals(activeRevision, revision, StringComparison.OrdinalIgnoreCase))
                    {
                        return MobileRuntimeFactory.Rejected(
                            presetId,
                            jobId,
                            revision,
                            "send-pause",
                            "runtime_revision_mismatch",
                            "启动中的发送任务对应的预设已变化，请先更新任务。");
                    }
                    pauseRequested = true;
                    state = "paused";
                    return MobileRuntimeFactory.Accepted(presetId, jobId, revision, "send-pause");
                }
                if (activeSend == null || activeJobId != jobId || activePresetId != presetId || !activeSend.Worker.IsBusy)
                {
                    return MobileRuntimeFactory.Rejected(presetId, jobId, revision, "send-pause", "pause_not_found", "发送任务不存在。");
                }
                if (!string.Equals(activeRevision, revision, StringComparison.OrdinalIgnoreCase))
                {
                    return MobileRuntimeFactory.Rejected(
                        activePresetId,
                        jobId,
                        revision,
                        "send-pause",
                        "runtime_revision_mismatch",
                        "运行中的发送任务对应的预设已变化，请先更新任务。");
                }
                if (!activeSend.PauseSend())
                {
                    return MobileRuntimeFactory.Rejected(presetId, jobId, revision, "send-pause", "pause_not_allowed", "当前发送任务不能暂停。");
                }
                state = "paused";
                return MobileRuntimeFactory.Accepted(presetId, jobId, revision, "send-pause");
            }
        }

        public static MobileActionResponse Stop(Guid jobId, string revision)
        {
            lock (SyncRoot)
            {
                if (lastStoppedJobId == jobId && jobId != Guid.Empty)
                {
                    return MobileRuntimeFactory.Accepted(lastStoppedPresetId, jobId, revision, "send-stop");
                }
                if (startInFlight && activeSend == null && activeJobId == jobId)
                {
                    stopRequested = true;
                    state = "stopping";
                    lastStoppedPresetId = activePresetId;
                    lastStoppedJobId = jobId;
                    return MobileRuntimeFactory.Accepted(activePresetId, jobId, revision, "send-stop");
                }
                if (activeSend == null || activeJobId != jobId || !activeSend.Worker.IsBusy)
                {
                    return MobileRuntimeFactory.Rejected(Guid.Empty, jobId, revision, "send-stop", "stop_not_found", "发送任务不存在。");
                }
                state = "stopping";
                lastStoppedPresetId = activePresetId;
                lastStoppedJobId = jobId;
                activeSend.StopSend();
                return MobileRuntimeFactory.Accepted(activePresetId, jobId, revision, "send-stop");
            }
        }

        // Keep the existing desktop shutdown path source-compatible. Mobile
        // callers use the job-scoped overload above; desktop close/stop code
        // has historically called Stop() without a job id.
        public static bool Stop()
        {
            lock (SyncRoot)
            {
                if (startInFlight && activeSend == null)
                {
                    stopRequested = true;
                    state = "stopping";
                    lastStoppedPresetId = activePresetId;
                    lastStoppedJobId = activeJobId;
                    return activeJobId != Guid.Empty;
                }
                if (activeSend == null || !activeSend.Worker.IsBusy)
                {
                    return false;
                }
                state = "stopping";
                activeSend.StopSend();
                return true;
            }
        }

        public static MobileRuntimeStatus GetStatus()
        {
            lock (SyncRoot)
            {
                bool busy = activeSend != null && activeSend.Worker.IsBusy;
                return MobileRuntimeFactory.Status(busy ? state : (state == "stopping" ? "stopping" : state),
                    activePresetId, activeJobId, activeRevision, startedAt, finishedAt, errorCode, detail,
                    activeSend == null ? (int?)null : activeSend.Total_Send, null);
            }
        }

        private static MobileActionResponse FailedStart(
            Guid presetId,
            string revision,
            string message,
            string failureCode = "preset_invalid")
        {
            bool cancelled;
            string detailText;
            lock (SyncRoot)
            {
                cancelled = stopRequested;
                detailText = cancelled ? "发送任务已在启动阶段取消。" : message;
                state = cancelled ? "cancelled" : "faulted";
                detail = detailText;
                errorCode = cancelled ? string.Empty : failureCode;
                finishedAt = DateTime.UtcNow;
                startInFlight = false;
            }
            return MobileRuntimeFactory.Rejected(presetId, activeJobId, revision, "send-start",
                cancelled ? "runtime_conflict" : failureCode, detailText);
        }

        private static void Monitor(Socket_Send send, Guid jobId)
        {
            Task.Run(() =>
            {
                send.WaitForCompletion(Timeout.Infinite);
                lock (SyncRoot)
                {
                    if (activeJobId == jobId && ReferenceEquals(activeSend, send))
                    {
                        if (state == "stopping")
                        {
                            state = "cancelled";
                            errorCode = string.Empty;
                            detail = string.Empty;
                        }
                        else if (send.Send_Success <= 0)
                        {
                            state = "faulted";
                            errorCode = "send_failed";
                            detail = send.Total_Send <= 0
                                ? "当前预设没有实际发送封包，请先让目标程序产生同类型封包后重试。"
                                : "当前游戏连接已失效，封包未实际发送；请先重新捕获当前连接后重试。";
                        }
                        else
                        {
                            state = "completed";
                            errorCode = string.Empty;
                            detail = string.Empty;
                        }
                        finishedAt = DateTime.UtcNow;
                        activeSend = null;
                    }
                }
            });
        }
    }

    internal static class MobileAssistantRuntime
    {
        private static readonly object SyncRoot = new object();
        private static Socket_Robot activeRobot;
        private static Guid activePresetId;
        private static Guid activeJobId;
        private static Guid lastStoppedPresetId;
        private static Guid lastStoppedJobId;
        private static string activeRevision = string.Empty;
        private static string state = "idle";
        private static DateTime? startedAt;
        private static DateTime? finishedAt;
        private static string detail = string.Empty;
        private static string errorCode = string.Empty;
        private static bool startInFlight;
        private static bool pauseRequested;
        private static bool stopRequested;

        public static MobileActionResponse Start(Guid presetId, string revision)
        {
            lock (SyncRoot)
            {
                if (startInFlight)
                {
                    return MobileRuntimeFactory.Rejected(presetId, activeJobId, revision,
                        "assistant-start", "runtime_busy", "助手任务正在启动，请稍候。");
                }
                if (activeRobot != null && activeRobot.Worker.IsBusy)
                {
                    if (activePresetId == presetId && activeRobot.IsPaused)
                    {
                        if (!string.Equals(activeRevision, revision, StringComparison.OrdinalIgnoreCase))
                        {
                            return MobileRuntimeFactory.Rejected(
                                presetId,
                                activeJobId,
                                revision,
                                "assistant-resume",
                                "runtime_revision_mismatch",
                                "暂停中的助手任务对应的预设已变化，请先更新任务。 ");
                        }
                        activeRobot.ResumeRobot();
                        state = "running";
                        return MobileRuntimeFactory.Accepted(presetId, activeJobId, revision, "assistant-resume");
                    }
                    return MobileRuntimeFactory.Rejected(presetId, activeJobId, revision, "assistant-start", "runtime_busy", "当前已有助手任务运行。");
                }
                startInFlight = true;
                activeJobId = Guid.NewGuid();
                activePresetId = presetId;
                activeRevision = revision;
                state = "starting";
                startedAt = DateTime.UtcNow;
                finishedAt = null;
                detail = string.Empty;
                errorCode = string.Empty;
                pauseRequested = false;
                stopRequested = false;
            }
            try
            {
                Socket_Robot robot = Socket_Cache.Robot.DoRobot(presetId, null);
                // StartRobot returns true after admission; a very short instruction
                // set may already have completed by the time the handle is checked.
                if (robot == null)
                {
                    return FailedStart(presetId, revision, "助手预设无效或未能启动。");
                }
                bool stopAfterAdmission;
                bool pauseAfterAdmission;
                lock (SyncRoot)
                {
                    activeRobot = robot;
                    stopAfterAdmission = stopRequested;
                    pauseAfterAdmission = pauseRequested && !stopAfterAdmission;
                    state = stopAfterAdmission
                        ? "stopping" : pauseAfterAdmission && robot.Worker.IsBusy
                        ? "paused" : robot.Worker.IsBusy ? "running" : "completed";
                    if (!robot.Worker.IsBusy && !stopAfterAdmission)
                    {
                        finishedAt = DateTime.UtcNow;
                    }
                }
                if (stopAfterAdmission)
                {
                    robot.StopRobot();
                }
                else if (pauseAfterAdmission)
                {
                    robot.PauseRobot();
                }
                Monitor(robot, activeJobId);
                return MobileRuntimeFactory.Accepted(presetId, activeJobId, revision, "assistant-start");
            }
            catch (Exception ex)
            {
                return FailedStart(presetId, revision, ex.Message);
            }
            finally
            {
                lock (SyncRoot)
                {
                    startInFlight = false;
                }
            }
        }

        public static MobileActionResponse Pause(Guid presetId, Guid jobId, string revision)
        {
            lock (SyncRoot)
            {
                if (startInFlight && activeRobot == null && activeJobId == jobId
                        && activePresetId == presetId)
                {
                    if (!string.Equals(activeRevision, revision, StringComparison.OrdinalIgnoreCase))
                    {
                        return MobileRuntimeFactory.Rejected(
                            presetId,
                            jobId,
                            revision,
                            "assistant-pause",
                            "runtime_revision_mismatch",
                            "启动中的助手任务对应的预设已变化，请先更新任务。");
                    }
                    pauseRequested = true;
                    state = "paused";
                    return MobileRuntimeFactory.Accepted(presetId, jobId, revision, "assistant-pause");
                }
                if (activeRobot == null || activeJobId != jobId || activePresetId != presetId || !activeRobot.Worker.IsBusy)
                {
                    return MobileRuntimeFactory.Rejected(presetId, jobId, revision, "assistant-pause", "pause_not_found", "助手任务不存在。");
                }
                if (!string.Equals(activeRevision, revision, StringComparison.OrdinalIgnoreCase))
                {
                    return MobileRuntimeFactory.Rejected(
                        activePresetId,
                        jobId,
                        revision,
                        "assistant-pause",
                        "runtime_revision_mismatch",
                        "运行中的助手任务对应的预设已变化，请先更新任务。");
                }
                if (!activeRobot.PauseRobot())
                {
                    return MobileRuntimeFactory.Rejected(presetId, jobId, revision, "assistant-pause", "pause_not_allowed", "当前助手任务不能暂停。");
                }
                state = "paused";
                return MobileRuntimeFactory.Accepted(presetId, jobId, revision, "assistant-pause");
            }
        }

        public static MobileActionResponse Stop(Guid jobId, string revision)
        {
            lock (SyncRoot)
            {
                if (lastStoppedJobId == jobId && jobId != Guid.Empty)
                {
                    return MobileRuntimeFactory.Accepted(lastStoppedPresetId, jobId, revision, "assistant-stop");
                }
                if (startInFlight && activeRobot == null && activeJobId == jobId)
                {
                    stopRequested = true;
                    state = "stopping";
                    lastStoppedPresetId = activePresetId;
                    lastStoppedJobId = jobId;
                    return MobileRuntimeFactory.Accepted(activePresetId, jobId, revision, "assistant-stop");
                }
                if (activeRobot == null || activeJobId != jobId || !activeRobot.Worker.IsBusy)
                {
                    return MobileRuntimeFactory.Rejected(Guid.Empty, jobId, revision, "assistant-stop", "stop_not_found", "助手任务不存在。");
                }
                state = "stopping";
                lastStoppedPresetId = activePresetId;
                lastStoppedJobId = jobId;
                activeRobot.StopRobot();
                return MobileRuntimeFactory.Accepted(activePresetId, jobId, revision, "assistant-stop");
            }
        }

        // Compatibility overload for the existing desktop shutdown path.
        public static bool Stop()
        {
            lock (SyncRoot)
            {
                if (startInFlight && activeRobot == null)
                {
                    stopRequested = true;
                    state = "stopping";
                    lastStoppedPresetId = activePresetId;
                    lastStoppedJobId = activeJobId;
                    return activeJobId != Guid.Empty;
                }
                if (activeRobot == null || !activeRobot.Worker.IsBusy)
                {
                    return false;
                }
                state = "stopping";
                activeRobot.StopRobot();
                return true;
            }
        }

        public static MobileRuntimeStatus GetStatus()
        {
            lock (SyncRoot)
            {
                bool busy = activeRobot != null && activeRobot.Worker.IsBusy;
                return MobileRuntimeFactory.Status(busy ? state : state, activePresetId, activeJobId,
                    activeRevision, startedAt, finishedAt, errorCode, detail,
                    activeRobot == null ? (int?)null : activeRobot.Instruction_Index,
                    activeRobot == null ? (int?)null : activeRobot.Total_Instruction);
            }
        }

        private static MobileActionResponse FailedStart(Guid presetId, string revision, string message)
        {
            bool cancelled;
            string detailText;
            lock (SyncRoot)
            {
                cancelled = stopRequested;
                detailText = cancelled ? "助手任务已在启动阶段取消。" : message;
                state = cancelled ? "cancelled" : "faulted";
                detail = detailText;
                errorCode = cancelled ? string.Empty : "preset_invalid";
                finishedAt = DateTime.UtcNow;
                startInFlight = false;
            }
            return MobileRuntimeFactory.Rejected(presetId, activeJobId, revision, "assistant-start",
                cancelled ? "runtime_conflict" : "preset_invalid", detailText);
        }

        private static void Monitor(Socket_Robot robot, Guid jobId)
        {
            Task.Run(() =>
            {
                robot.WaitForCompletion(Timeout.Infinite);
                lock (SyncRoot)
                {
                    if (activeJobId == jobId && ReferenceEquals(activeRobot, robot))
                    {
                        if (state == "stopping")
                        {
                            state = "cancelled";
                        }
                        else if (robot.HasTreasureMapInstructionRows())
                        {
                            Socket_Robot.TreasureMapRunState treasureState =
                                robot.GetTreasureMapRunState();
                            if (treasureState != null &&
                                treasureState.CurrentState == TreasureMapState.Ambiguous)
                            {
                                state = "ambiguous";
                                errorCode = string.IsNullOrWhiteSpace(treasureState.LastError)
                                    ? "treasure_result_ambiguous"
                                    : treasureState.LastError;
                                detail = "藏宝图流程结果不确定，请先人工复核现场状态，禁止自动重试。";
                            }
                            else if (treasureState != null &&
                                     (treasureState.CurrentState == TreasureMapState.Failed ||
                                      treasureState.CurrentState == TreasureMapState.ControllerConflict))
                            {
                                state = "faulted";
                                errorCode = string.IsNullOrWhiteSpace(treasureState.LastError)
                                    ? treasureState.CurrentState == TreasureMapState.ControllerConflict
                                        ? "controller_conflict"
                                        : "treasure_run_failed"
                                    : treasureState.LastError;
                                detail = treasureState.CurrentState == TreasureMapState.ControllerConflict
                                    ? "藏宝图流程已有其他控制器占用。"
                                    : "藏宝图流程未完成。";
                            }
                            else
                            {
                                state = "completed";
                                errorCode = string.Empty;
                                detail = string.Empty;
                            }
                        }
                        else
                        {
                            state = "completed";
                        }
                        finishedAt = DateTime.UtcNow;
                        activeRobot = null;
                    }
                }
            });
        }
    }

    internal static class MobileProgressionRuntime
    {
        private static readonly object SyncRoot = new object();
        private static Guid lastStoppedPresetId;
        private static Guid lastStoppedJobId;

        public static MobileActionResponse Start(Guid presetId, string revision)
        {
            Socket_Form socketForm = Application.OpenForms.OfType<Socket_Form>().FirstOrDefault();
            if (socketForm == null)
            {
                return MobileRuntimeFactory.Rejected(presetId, Guid.Empty, revision, "progression-start", "runtime_not_connected", "电脑端封包窗口未运行。");
            }
            Socket_ByteSweepRuntimeSnapshot current = Socket_ByteSweepRuntime.Current.GetSnapshot();
            if (current.State == Socket_ByteSweepRuntimeState.Paused && current.PresetId == presetId)
            {
                if (!string.Equals(current.Revision, revision, StringComparison.OrdinalIgnoreCase))
                {
                    return MobileRuntimeFactory.Rejected(
                        presetId,
                        current.JobId,
                        revision,
                        "progression-resume",
                        "runtime_revision_mismatch",
                        "暂停中的递进任务对应的预设已变化，请先更新任务。");
                }
                return Socket_ByteSweepRuntime.Current.Resume(current.JobId)
                    ? MobileRuntimeFactory.Accepted(presetId, current.JobId, revision, "progression-resume")
                    : MobileRuntimeFactory.Rejected(presetId, current.JobId, revision, "progression-resume", "runtime_conflict", "递进任务无法恢复。");
            }
            if (Socket_ByteSweepRuntime.Current.IsBusy)
            {
                return MobileRuntimeFactory.Rejected(presetId, current.JobId, revision, "progression-start", "runtime_busy", "当前已有递进任务运行。");
            }
            MobileByteSweepStartResult result = socketForm.StartByteSweepFromMobile(presetId, revision);
            if (!result.Accepted)
            {
                return MobileRuntimeFactory.Rejected(
                    presetId,
                    Guid.Empty,
                    revision,
                    "progression-start",
                    string.IsNullOrWhiteSpace(result.ErrorCode)
                        ? "preset_invalid"
                        : result.ErrorCode,
                    result.Error);
            }
            return MobileRuntimeFactory.Accepted(presetId, result.JobId, revision, "progression-start");
        }

        public static MobileActionResponse Pause(Guid presetId, Guid jobId, string revision)
        {
            Socket_ByteSweepRuntimeSnapshot current = Socket_ByteSweepRuntime.Current.GetSnapshot();
            if (current.JobId != jobId || current.PresetId != presetId)
            {
                return MobileRuntimeFactory.Rejected(presetId, jobId, revision, "progression-pause", "pause_not_found", "递进任务不存在。");
            }
            if (!string.Equals(current.Revision, revision, StringComparison.OrdinalIgnoreCase))
            {
                return MobileRuntimeFactory.Rejected(
                    presetId,
                    jobId,
                    revision,
                    "progression-pause",
                    "runtime_revision_mismatch",
                    "运行中的递进任务对应的预设已变化，请先更新任务。");
            }
            bool accepted = Socket_ByteSweepRuntime.Current.RequestPause(jobId);
            return accepted ? MobileRuntimeFactory.Accepted(presetId, jobId, revision, "progression-pause")
                : MobileRuntimeFactory.Rejected(presetId, jobId, revision, "progression-pause", "pause_not_found", "递进任务不存在。");
        }

        public static MobileActionResponse Stop(Guid jobId, string revision)
        {
            lock (SyncRoot)
            {
                Socket_ByteSweepRuntimeSnapshot current = Socket_ByteSweepRuntime.Current.GetSnapshot();
                if (lastStoppedJobId == jobId && jobId != Guid.Empty)
                {
                    return MobileRuntimeFactory.Accepted(lastStoppedPresetId, jobId, revision, "progression-stop");
                }
                if (current.JobId != jobId)
                {
                    return MobileRuntimeFactory.Rejected(Guid.Empty, jobId, revision, "progression-stop", "stop_not_found", "递进任务不存在。");
                }
                bool accepted = Socket_ByteSweepRuntime.Current.RequestStop(jobId);
                if (!accepted)
                {
                    return MobileRuntimeFactory.Rejected(Guid.Empty, jobId, revision, "progression-stop", "stop_not_found", "递进任务不存在。");
                }
                lastStoppedPresetId = current.PresetId;
                lastStoppedJobId = jobId;
                return MobileRuntimeFactory.Accepted(current.PresetId, jobId, revision, "progression-stop");
            }
        }

        public static MobileRuntimeStatus GetStatus()
        {
            Socket_ByteSweepRuntimeSnapshot value = Socket_ByteSweepRuntime.Current.GetSnapshot();
            string state = value.State.ToString().ToLowerInvariant();
            if (state == "pausing") state = "pausing";
            return MobileRuntimeFactory.Status(state, value.PresetId, value.JobId, value.Revision,
                null, null, value.State == Socket_ByteSweepRuntimeState.Faulted ? "runtime_faulted" : string.Empty,
                value.Detail, (int?)value.TotalSend, (int?)value.PlannedTotal);
        }
    }
}
