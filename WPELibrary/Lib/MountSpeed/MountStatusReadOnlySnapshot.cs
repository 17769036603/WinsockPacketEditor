using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.MountSpeed
{
    public sealed class AndroidMountStatusProcessIdentity
    {
        public int Pid { get; internal set; }

        public long StartTicks { get; internal set; }

        public string Executable { get; internal set; }
    }

    public sealed class MountStatusRuntimeValue
    {
        public string Kind { get; internal set; }

        public string Raw { get; internal set; }

        public int? Integer { get; internal set; }

        public double? Number { get; internal set; }

        public bool? Boolean { get; internal set; }
    }

    public sealed class MountRideSkillSnapshot
    {
        public int SlotIndex { get; internal set; }

        public int SkillId { get; internal set; }

        public int Exp { get; internal set; }

        public string SkillName { get; internal set; }
    }

    public sealed class MountRideInstanceSnapshot
    {
        public string RideInstanceId { get; internal set; }

        public long? RideShapeId { get; internal set; }

        /// <summary>
        /// Optional current instance growth value from PropertyValueDict.GROWUP.
        /// </summary>
        public double? GrowthRate { get; internal set; }

        public string GrowthRateSource { get; internal set; }

        public bool? IsRiding { get; internal set; }

        public bool? IsCurrent { get; internal set; }

        public IList<MountRideSkillSnapshot> Skills { get; internal set; }
    }

    public sealed class MountRideRefineSkillSnapshot
    {
        public int SlotIndex { get; internal set; }

        public int SkillId { get; internal set; }

        public string SkillName { get; internal set; }

        /// <summary>
        /// Optional raw value from the temporary refine-card skill record.
        /// It is not treated as current-skill experience.
        /// </summary>
        public string Value { get; internal set; }
    }

    public sealed class MountRideRefineCardSnapshot
    {
        public int CardIndex { get; internal set; }

        public bool? IsCurrent { get; internal set; }

        public double? GrowthRate { get; internal set; }

        public string GrowthRateSource { get; internal set; }

        public int? Speed { get; internal set; }

        public int? Score { get; internal set; }

        public string Source { get; internal set; }

        public IList<MountRideRefineSkillSnapshot> Skills { get; internal set; }
    }

    public sealed class MountRideRefineCardChange
    {
        public int CardIndex { get; internal set; }

        public string Status { get; internal set; }

        public string Summary { get; internal set; }
    }

    public sealed class MountRideRefineCardComparison
    {
        public bool HasPreviousSnapshot { get; internal set; }

        public bool HasCurrentCards { get; internal set; }

        public bool MountChanged { get; internal set; }

        public int PreviousCardCount { get; internal set; }

        public int CurrentCardCount { get; internal set; }

        public int AddedCount { get; internal set; }

        public int RemovedCount { get; internal set; }

        public int ChangedCount { get; internal set; }

        public IList<MountRideRefineCardChange> Changes { get; internal set; }

        public string Describe()
        {
            if (!this.HasCurrentCards)
            {
                return "当前未读取炼化卡片";
            }
            if (!this.HasPreviousSnapshot || this.PreviousCardCount == 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "首次建立 {0} 张卡片基线",
                    this.CurrentCardCount);
            }
            if (this.MountChanged)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "坐骑或游戏进程已变化，重新建立 {0} 张卡片基线",
                    this.CurrentCardCount);
            }
            if (this.Changes == null || this.Changes.Count == 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "未发现变化（{0} 张）",
                    this.CurrentCardCount);
            }
            return string.Format(
                CultureInfo.InvariantCulture,
                "变化 {0} 张，新增 {1} 张，消失 {2} 张",
                this.ChangedCount,
                this.AddedCount,
                this.RemovedCount);
        }
    }

    public static class MountRideRefineCardComparer
    {
        public static MountRideRefineCardComparison Compare(
            MountStatusReadOnlySnapshot previous,
            MountStatusReadOnlySnapshot current)
        {
            IList<MountRideRefineCardSnapshot> previousCards = previous == null
                ? new List<MountRideRefineCardSnapshot>()
                : (previous.RideRefineCards ?? new List<MountRideRefineCardSnapshot>());
            IList<MountRideRefineCardSnapshot> currentCards = current == null
                ? new List<MountRideRefineCardSnapshot>()
                : (current.RideRefineCards ?? new List<MountRideRefineCardSnapshot>());

            MountRideRefineCardComparison result = new MountRideRefineCardComparison
            {
                HasPreviousSnapshot = previous != null,
                HasCurrentCards = currentCards.Count > 0,
                PreviousCardCount = previousCards.Count,
                CurrentCardCount = currentCards.Count,
                Changes = new List<MountRideRefineCardChange>()
            };
            if (!result.HasCurrentCards)
            {
                return result;
            }

            result.MountChanged = HasMountContextChanged(previous, current);
            if (!result.HasPreviousSnapshot ||
                result.PreviousCardCount == 0 ||
                result.MountChanged)
            {
                return result;
            }

            Dictionary<int, MountRideRefineCardSnapshot> previousByIndex =
                ToCardMap(previousCards);
            Dictionary<int, MountRideRefineCardSnapshot> currentByIndex =
                ToCardMap(currentCards);
            SortedSet<int> indexes = new SortedSet<int>(previousByIndex.Keys);
            indexes.UnionWith(currentByIndex.Keys);
            foreach (int cardIndex in indexes)
            {
                MountRideRefineCardSnapshot oldCard;
                MountRideRefineCardSnapshot newCard;
                bool hasOld = previousByIndex.TryGetValue(cardIndex, out oldCard);
                bool hasNew = currentByIndex.TryGetValue(cardIndex, out newCard);
                if (!hasOld)
                {
                    result.AddedCount++;
                    result.Changes.Add(new MountRideRefineCardChange
                    {
                        CardIndex = cardIndex,
                        Status = "added",
                        Summary = "新增：" + DescribeCard(newCard)
                    });
                }
                else if (!hasNew)
                {
                    result.RemovedCount++;
                    result.Changes.Add(new MountRideRefineCardChange
                    {
                        CardIndex = cardIndex,
                        Status = "removed",
                        Summary = "已消失：" + DescribeCard(oldCard)
                    });
                }
                else if (!CardsEqual(oldCard, newCard))
                {
                    result.ChangedCount++;
                    result.Changes.Add(new MountRideRefineCardChange
                    {
                        CardIndex = cardIndex,
                        Status = "changed",
                        Summary = DescribeCardChange(oldCard, newCard)
                    });
                }
            }
            return result;
        }

        private static bool HasMountContextChanged(
            MountStatusReadOnlySnapshot previous,
            MountStatusReadOnlySnapshot current)
        {
            if (previous == null || current == null)
            {
                return false;
            }
            if (previous.Process != null && current.Process != null &&
                (previous.Process.Pid != current.Process.Pid ||
                 previous.Process.StartTicks != current.Process.StartTicks))
            {
                return true;
            }
            if (!string.Equals(
                previous.ActiveRideInstanceId,
                current.ActiveRideInstanceId,
                StringComparison.Ordinal))
            {
                return true;
            }
            return previous.MountId != current.MountId;
        }

        private static Dictionary<int, MountRideRefineCardSnapshot> ToCardMap(
            IList<MountRideRefineCardSnapshot> cards)
        {
            Dictionary<int, MountRideRefineCardSnapshot> result =
                new Dictionary<int, MountRideRefineCardSnapshot>();
            foreach (MountRideRefineCardSnapshot card in cards)
            {
                if (card != null && !result.ContainsKey(card.CardIndex))
                {
                    result.Add(card.CardIndex, card);
                }
            }
            return result;
        }

        private static bool CardsEqual(
            MountRideRefineCardSnapshot left,
            MountRideRefineCardSnapshot right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }
            if (left.CardIndex != right.CardIndex ||
                left.IsCurrent != right.IsCurrent ||
                left.Speed != right.Speed ||
                left.Score != right.Score ||
                !NullableDoublesEqual(left.GrowthRate, right.GrowthRate))
            {
                return false;
            }

            IList<MountRideRefineSkillSnapshot> leftSkills = left.Skills ??
                new List<MountRideRefineSkillSnapshot>();
            IList<MountRideRefineSkillSnapshot> rightSkills = right.Skills ??
                new List<MountRideRefineSkillSnapshot>();
            if (leftSkills.Count != rightSkills.Count)
            {
                return false;
            }
            foreach (MountRideRefineSkillSnapshot leftSkill in
                leftSkills.OrderBy(item => item == null ? int.MaxValue : item.SlotIndex))
            {
                MountRideRefineSkillSnapshot rightSkill = rightSkills.FirstOrDefault(
                    item => item != null && leftSkill != null &&
                        item.SlotIndex == leftSkill.SlotIndex);
                if (leftSkill == null || rightSkill == null ||
                    leftSkill.SkillId != rightSkill.SkillId ||
                    !string.Equals(leftSkill.SkillName, rightSkill.SkillName, StringComparison.Ordinal) ||
                    !string.Equals(leftSkill.Value, rightSkill.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool NullableDoublesEqual(double? left, double? right)
        {
            if (left.HasValue != right.HasValue)
            {
                return false;
            }
            return !left.HasValue || Math.Abs(left.Value - right.Value) <= 0.000001;
        }

        private static string DescribeCardChange(
            MountRideRefineCardSnapshot oldCard,
            MountRideRefineCardSnapshot newCard)
        {
            List<string> parts = new List<string>();
            if (!NullableDoublesEqual(oldCard.GrowthRate, newCard.GrowthRate))
            {
                parts.Add("成长率 " + FormatDouble(oldCard.GrowthRate) +
                    "→" + FormatDouble(newCard.GrowthRate));
            }
            if (oldCard.Speed != newCard.Speed)
            {
                parts.Add("速度 " + FormatInt(oldCard.Speed) +
                    "→" + FormatInt(newCard.Speed));
            }
            if (oldCard.Score != newCard.Score)
            {
                parts.Add("评分 " + FormatInt(oldCard.Score) +
                    "→" + FormatInt(newCard.Score));
            }
            if (!CardsSkillsEqual(oldCard, newCard))
            {
                parts.Add("技能 " + FormatSkills(oldCard) +
                    "→" + FormatSkills(newCard));
            }
            return parts.Count == 0 ? "卡片字段已变化" : string.Join("；", parts);
        }

        private static bool CardsSkillsEqual(
            MountRideRefineCardSnapshot left,
            MountRideRefineCardSnapshot right)
        {
            IList<MountRideRefineSkillSnapshot> leftSkills = left.Skills ??
                new List<MountRideRefineSkillSnapshot>();
            IList<MountRideRefineSkillSnapshot> rightSkills = right.Skills ??
                new List<MountRideRefineSkillSnapshot>();
            if (leftSkills.Count != rightSkills.Count)
            {
                return false;
            }
            return leftSkills.OrderBy(item => item == null ? int.MaxValue : item.SlotIndex)
                .Select(FormatSkill)
                .SequenceEqual(
                    rightSkills.OrderBy(item => item == null ? int.MaxValue : item.SlotIndex)
                        .Select(FormatSkill),
                    StringComparer.Ordinal);
        }

        private static string DescribeCard(MountRideRefineCardSnapshot card)
        {
            if (card == null)
            {
                return "<无卡片数据>";
            }
            return "成长率 " + FormatDouble(card.GrowthRate) +
                "，技能 " + FormatSkills(card);
        }

        private static string FormatSkills(MountRideRefineCardSnapshot card)
        {
            return string.Join(
                "/",
                (card == null || card.Skills == null
                    ? new List<MountRideRefineSkillSnapshot>()
                    : card.Skills)
                .OrderBy(item => item == null ? int.MaxValue : item.SlotIndex)
                .Select(FormatSkill));
        }

        private static string FormatSkill(MountRideRefineSkillSnapshot skill)
        {
            if (skill == null)
            {
                return "<空>";
            }
            return string.IsNullOrWhiteSpace(skill.SkillName)
                ? "ID " + skill.SkillId.ToString(CultureInfo.InvariantCulture)
                : skill.SkillName;
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "<无>";
        }

        private static string FormatInt(int? value)
        {
            return value.HasValue
                ? value.Value.ToString()
                : "<无>";
        }
    }

    /// <summary>
    /// Validated Android LuaJIT mount snapshot. It contains observation data
    /// only; no operation, packet, or memory-write capability is represented.
    /// </summary>
    public sealed class MountStatusReadOnlySnapshot
    {
        public const string SchemaName = "mount_status_snapshot.v1";

        public const int SupportedSchemaVersion = 1;

        public bool ReadOnly { get; internal set; }

        public bool ActionAuthorized { get; internal set; }

        public AndroidMountStatusProcessIdentity Process { get; internal set; }

        public Guid SessionId { get; internal set; }

        public long Sequence { get; internal set; }

        public int CandidateCount { get; internal set; }

        public string DiagnosticCode { get; internal set; }

        public int PlayerId { get; internal set; }

        public bool IsLocalPlayer { get; internal set; }

        public bool? IsMounted { get; internal set; }

        public long? MountId { get; internal set; }

        /// <summary>
        /// Optional current LocalRide.PropertyValueDict.GROWUP value. The probe
        /// preserves the client value and does not use the template GrowthRate.
        /// </summary>
        public double? GrowthRate { get; internal set; }

        public string GrowthRateSource { get; internal set; }

        public int Gx { get; internal set; }

        public int Gy { get; internal set; }

        public double RoleMoveSpeed { get; internal set; }

        public IDictionary<string, MountStatusRuntimeValue> RawValues { get; internal set; }

        /// <summary>
        /// Validated LocalRide instances and optional binding evidence.
        /// </summary>
        public IList<MountRideInstanceSnapshot> RideInstances { get; internal set; }

        /// <summary>
        /// Optional read-only cards shown by the mount-refine page. Card 1 is
        /// the current/built-in card; candidate cards retain their page index.
        /// </summary>
        public IList<MountRideRefineCardSnapshot> RideRefineCards { get; internal set; }

        public string ActiveRideInstanceId { get; internal set; }

        public string RideBindingStatus { get; internal set; }

        public string RideBindingSource { get; internal set; }

        public DateTime ReadAt { get; internal set; }

        public bool IsValid
        {
            get
            {
                return this.ReadOnly &&
                    !this.ActionAuthorized &&
                    this.Process != null &&
                    this.Process.Pid > 0 &&
                    this.PlayerId > 0 &&
                    this.IsLocalPlayer &&
                    this.IsMounted.HasValue &&
                    !double.IsNaN(this.RoleMoveSpeed) &&
                    !double.IsInfinity(this.RoleMoveSpeed);
            }
        }

        public override string ToString()
        {
            return string.Format(
                "playerId={0}, mounted={1}, mountId={2}, growthRate={3}, roleMoveSpeed={4}, pos=({5},{6}), sequence={7}",
                this.PlayerId,
                this.IsMounted.HasValue ? this.IsMounted.Value.ToString() : "<unknown>",
                this.MountId.HasValue ? this.MountId.Value.ToString() : "<nil>",
                this.GrowthRate.HasValue ? this.GrowthRate.Value.ToString() : "<unread>",
                this.RoleMoveSpeed,
                this.Gx,
                this.Gy,
                this.Sequence);
        }
    }

    /// <summary>
    /// Strict parser for the one-line Android LuaJIT mount snapshot protocol.
    /// A non-OK or ambiguous probe result is rejected fail-closed.
    /// </summary>
    public static class MountStatusReadOnlySnapshotProtocol
    {
        public static bool TryParse(
            string json,
            out MountStatusReadOnlySnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "坐骑只读快照为空。";
                return false;
            }

            try
            {
                JObject root = JObject.Parse(
                    json,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });

                if (!string.Equals(
                    root.Value<string>("schema"),
                    MountStatusReadOnlySnapshot.SchemaName,
                    StringComparison.Ordinal))
                {
                    error = "坐骑只读快照 schema 不支持。";
                    return false;
                }

                if ((root.Value<int?>("schemaVersion") ?? 0) !=
                    MountStatusReadOnlySnapshot.SupportedSchemaVersion)
                {
                    error = "坐骑只读快照版本不支持。";
                    return false;
                }

                if (root.Value<bool?>("readOnly") != true ||
                    root.Value<bool?>("actionAuthorized") != false)
                {
                    error = "坐骑快照不是只读或包含操作授权。";
                    return false;
                }

                if (!string.Equals(root.Value<string>("status"), "ok", StringComparison.Ordinal))
                {
                    string diagnosticCode = root.Value<string>("diagnosticCode") ?? "unknown";
                    string missingFields = string.Join(
                        "、",
                        (root["missingFields"] as JArray ?? new JArray())
                            .Values<string>()
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                    error = "坐骑只读探针未返回唯一有效角色：" +
                        diagnosticCode +
                        (string.IsNullOrWhiteSpace(missingFields)
                            ? string.Empty
                            : "，缺失字段：" + missingFields);
                    return false;
                }

                AndroidMountStatusProcessIdentity process;
                if (!TryReadProcess(root["process"] as JObject, out process, out error))
                {
                    return false;
                }

                Guid sessionId;
                if (!Guid.TryParse(root.Value<string>("sessionId"), out sessionId) ||
                    sessionId == Guid.Empty)
                {
                    error = "坐骑快照会话 ID 无效。";
                    return false;
                }

                long sequence = root.Value<long?>("sequence") ?? 0;
                int candidateCount = root.Value<int?>("candidateCount") ?? 0;
                if (sequence <= 0 || candidateCount != 1)
                {
                    error = "坐骑快照序号或候选数量无效。";
                    return false;
                }

                JObject player = root["player"] as JObject;
                if (player == null)
                {
                    error = "坐骑快照缺少本地角色记录。";
                    return false;
                }

                int playerId;
                int gx;
                int gy;
                double roleMoveSpeed;
                bool isLocalPlayer;
                bool isMounted;
                if (!TryReadInt(player, "playerId", out playerId, out error) ||
                    !TryReadInt(player, "gx", out gx, out error) ||
                    !TryReadInt(player, "gy", out gy, out error) ||
                    !TryReadFiniteDouble(player, "roleMoveSpeed", out roleMoveSpeed, out error) ||
                    !TryReadBoolean(player, "isLocalPlayer", out isLocalPlayer, out error) ||
                    !TryReadBoolean(player, "isMounted", out isMounted, out error))
                {
                    return false;
                }

                if (playerId <= 0 || !isLocalPlayer)
                {
                    error = "坐骑快照未绑定到本地角色。";
                    return false;
                }

                long? mountId;
                if (!TryReadOptionalLong(player["mountId"], out mountId, out error))
                {
                    return false;
                }

                double? growthRate;
                if (!TryReadOptionalFiniteDouble(
                    player["growthRate"],
                    "growthRate",
                    out growthRate,
                    out error))
                {
                    return false;
                }
                if (growthRate.HasValue && growthRate.Value < 0)
                {
                    error = "坐骑快照字段 growthRate 不能为负数。";
                    return false;
                }

                string growthRateSource;
                if (!TryReadOptionalString(
                    player["growthRateSource"],
                    "growthRateSource",
                    256,
                    out growthRateSource,
                    out error))
                {
                    return false;
                }

                IList<MountRideInstanceSnapshot> rideInstances;
                if (!TryReadRideInstances(player["rideInstances"] as JArray, out rideInstances, out error))
                {
                    return false;
                }

                IList<MountRideRefineCardSnapshot> rideRefineCards;
                if (!TryReadRideRefineCards(
                    player["rideRefineCards"] as JArray,
                    out rideRefineCards,
                    out error))
                {
                    return false;
                }

                string activeRideInstanceId;
                if (!TryReadOptionalString(
                    player["activeRideInstanceId"],
                    "activeRideInstanceId",
                    256,
                    out activeRideInstanceId,
                    out error))
                {
                    return false;
                }

                string rideBindingStatus;
                if (!TryReadOptionalString(
                    player["rideBindingStatus"],
                    "rideBindingStatus",
                    64,
                    out rideBindingStatus,
                    out error))
                {
                    return false;
                }

                string rideBindingSource;
                if (!TryReadOptionalString(
                    player["rideBindingSource"],
                    "rideBindingSource",
                    256,
                    out rideBindingSource,
                    out error))
                {
                    return false;
                }

                if (!ValidateRideBinding(
                    rideInstances,
                    mountId,
                    activeRideInstanceId,
                    rideBindingStatus,
                    rideBindingSource,
                    out error))
                {
                    return false;
                }

                snapshot = new MountStatusReadOnlySnapshot
                {
                    ReadOnly = true,
                    ActionAuthorized = false,
                    Process = process,
                    SessionId = sessionId,
                    Sequence = sequence,
                    CandidateCount = candidateCount,
                    DiagnosticCode = root.Value<string>("diagnosticCode") ?? "ok",
                    PlayerId = playerId,
                    IsLocalPlayer = isLocalPlayer,
                    IsMounted = isMounted,
                    MountId = mountId,
                    GrowthRate = growthRate,
                    GrowthRateSource = growthRateSource,
                    Gx = gx,
                    Gy = gy,
                    RoleMoveSpeed = roleMoveSpeed,
                    RawValues = ReadRawValues(player["rawValues"] as JObject),
                    RideInstances = rideInstances,
                    RideRefineCards = rideRefineCards,
                    ActiveRideInstanceId = activeRideInstanceId,
                    RideBindingStatus = rideBindingStatus,
                    RideBindingSource = rideBindingSource,
                    ReadAt = DateTime.UtcNow
                };
                return snapshot.IsValid;
            }
            catch (JsonException ex)
            {
                error = "坐骑快照 JSON 无效：" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = "坐骑快照解析失败：" + ex.Message;
                return false;
            }
        }

        private static bool TryReadRideInstances(
            JArray value,
            out IList<MountRideInstanceSnapshot> instances,
            out string error)
        {
            instances = new List<MountRideInstanceSnapshot>();
            error = string.Empty;
            if (value == null)
            {
                return true;
            }

            if (value.Count > 64)
            {
                error = "坐骑实例数量无效。";
                return false;
            }

            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken token in value)
            {
                JObject instance = token as JObject;
                string rideInstanceId = instance == null ? null : instance.Value<string>("rideInstanceId");
                JArray skillArray = instance == null ? null : instance["skills"] as JArray;
                if (string.IsNullOrWhiteSpace(rideInstanceId) ||
                    rideInstanceId.Length > 256 ||
                    !seenIds.Add(rideInstanceId) ||
                    skillArray == null ||
                    skillArray.Count == 0 ||
                    skillArray.Count > 64)
                {
                    error = "坐骑实例或技能列表无效。";
                    return false;
                }

                HashSet<int> seenSlots = new HashSet<int>();
                List<MountRideSkillSnapshot> skills = new List<MountRideSkillSnapshot>();
                foreach (JToken skillToken in skillArray)
                {
                    JObject skill = skillToken as JObject;
                    int slotIndex;
                    int skillId;
                    int exp;
                    string skillName = null;
                    if (!TryReadInt(skill, "slotIndex", out slotIndex, out error) ||
                        !TryReadInt(skill, "skillId", out skillId, out error) ||
                        !TryReadInt(skill, "exp", out exp, out error) ||
                        slotIndex <= 0 ||
                        slotIndex > 64 ||
                        skillId <= 0 ||
                        exp < 0 ||
                        !seenSlots.Add(slotIndex))
                    {
                        if (string.IsNullOrWhiteSpace(error))
                        {
                            error = "坐骑技能槽值无效。";
                        }
                        return false;
                    }

                    JToken skillNameToken = skill["skillName"];
                    if (skillNameToken != null && skillNameToken.Type != JTokenType.Null)
                    {
                        if (skillNameToken.Type != JTokenType.String)
                        {
                            error = "坐骑技能名称类型无效。";
                            return false;
                        }
                        skillName = skillNameToken.Value<string>();
                        if (string.IsNullOrWhiteSpace(skillName) || skillName.Length > 256)
                        {
                            error = "坐骑技能名称无效。";
                            return false;
                        }
                    }

                    skills.Add(new MountRideSkillSnapshot
                    {
                        SlotIndex = slotIndex,
                        SkillId = skillId,
                        Exp = exp,
                        SkillName = skillName
                    });
                }

                long? rideShapeId;
                if (!TryReadOptionalLong(
                    instance["rideShapeId"],
                    "rideShapeId",
                    out rideShapeId,
                    out error))
                {
                    return false;
                }
                if (rideShapeId.HasValue && rideShapeId.Value <= 0)
                {
                    error = "坐骑实例 rideShapeId 无效。";
                    return false;
                }

                double? growthRate;
                if (!TryReadOptionalFiniteDouble(
                    instance["growthRate"],
                    "rideInstance.growthRate",
                    out growthRate,
                    out error))
                {
                    return false;
                }
                if (growthRate.HasValue && growthRate.Value < 0)
                {
                    error = "坐骑实例 growthRate 不能为负数。";
                    return false;
                }

                string growthRateSource;
                if (!TryReadOptionalString(
                    instance["growthRateSource"],
                    "rideInstance.growthRateSource",
                    256,
                    out growthRateSource,
                    out error))
                {
                    return false;
                }

                bool? isRiding;
                if (!TryReadOptionalBoolean(
                    instance["isRiding"],
                    "isRiding",
                    out isRiding,
                    out error))
                {
                    return false;
                }

                bool? isCurrent;
                if (!TryReadOptionalBoolean(
                    instance["isCurrent"],
                    "isCurrent",
                    out isCurrent,
                    out error))
                {
                    return false;
                }

                instances.Add(new MountRideInstanceSnapshot
                {
                    RideInstanceId = rideInstanceId,
                    RideShapeId = rideShapeId,
                    GrowthRate = growthRate,
                    GrowthRateSource = growthRateSource,
                    IsRiding = isRiding,
                    IsCurrent = isCurrent,
                    Skills = skills
                });
            }
            return true;
        }

        private static bool TryReadRideRefineCards(
            JArray value,
            out IList<MountRideRefineCardSnapshot> cards,
            out string error)
        {
            cards = new List<MountRideRefineCardSnapshot>();
            error = string.Empty;
            if (value == null)
            {
                return true;
            }

            if (value.Count > 64)
            {
                error = "坐骑炼化卡片数量无效。";
                return false;
            }

            HashSet<int> seenCardIndexes = new HashSet<int>();
            int currentCardCount = 0;
            foreach (JToken token in value)
            {
                JObject card = token as JObject;
                int cardIndex;
                if (!TryReadInt(card, "cardIndex", out cardIndex, out error) ||
                    cardIndex <= 0 ||
                    cardIndex > 64 ||
                    !seenCardIndexes.Add(cardIndex))
                {
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        error = "坐骑炼化卡片索引无效。";
                    }
                    return false;
                }

                bool? isCurrent;
                if (!TryReadOptionalBoolean(
                    card["isCurrent"],
                    "rideRefineCard.isCurrent",
                    out isCurrent,
                    out error))
                {
                    return false;
                }
                if (isCurrent == true)
                {
                    currentCardCount++;
                    if (cardIndex != 1 || currentCardCount > 1)
                    {
                        error = "坐骑炼化当前卡片证据无效。";
                        return false;
                    }
                }

                JArray skillArray = card["skills"] as JArray;
                if (skillArray == null ||
                    skillArray.Count == 0 ||
                    skillArray.Count > 64)
                {
                    error = "坐骑炼化卡片技能列表无效。";
                    return false;
                }

                HashSet<int> seenSkillSlots = new HashSet<int>();
                List<MountRideRefineSkillSnapshot> skills =
                    new List<MountRideRefineSkillSnapshot>();
                foreach (JToken skillToken in skillArray)
                {
                    JObject skill = skillToken as JObject;
                    int slotIndex;
                    int skillId;
                    if (!TryReadInt(skill, "slotIndex", out slotIndex, out error) ||
                        !TryReadInt(skill, "skillId", out skillId, out error) ||
                        slotIndex <= 0 ||
                        slotIndex > 64 ||
                        skillId <= 0 ||
                        !seenSkillSlots.Add(slotIndex))
                    {
                        if (string.IsNullOrWhiteSpace(error))
                        {
                            error = "坐骑炼化卡片技能槽值无效。";
                        }
                        return false;
                    }

                    string skillName;
                    if (!TryReadOptionalString(
                        skill["skillName"],
                        "rideRefineCard.skillName",
                        256,
                        out skillName,
                        out error))
                    {
                        return false;
                    }

                    string rawValue;
                    if (!TryReadOptionalString(
                        skill["value"],
                        "rideRefineCard.value",
                        256,
                        out rawValue,
                        out error))
                    {
                        return false;
                    }

                    skills.Add(new MountRideRefineSkillSnapshot
                    {
                        SlotIndex = slotIndex,
                        SkillId = skillId,
                        SkillName = skillName,
                        Value = rawValue
                    });
                }

                double? growthRate;
                if (!TryReadOptionalFiniteDouble(
                    card["growthRate"],
                    "rideRefineCard.growthRate",
                    out growthRate,
                    out error))
                {
                    return false;
                }
                if (growthRate.HasValue && growthRate.Value < 0)
                {
                    error = "坐骑炼化卡片 growthRate 不能为负数。";
                    return false;
                }

                int? speed;
                if (!TryReadOptionalInt(
                    card["speed"],
                    "rideRefineCard.speed",
                    out speed,
                    out error) ||
                    speed.HasValue && (speed.Value < 0 || speed.Value > 1000000))
                {
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        error = "坐骑炼化卡片 speed 无效。";
                    }
                    return false;
                }

                int? score;
                if (!TryReadOptionalInt(
                    card["score"],
                    "rideRefineCard.score",
                    out score,
                    out error) ||
                    score.HasValue && (score.Value < 0 || score.Value > 1000000))
                {
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        error = "坐骑炼化卡片 score 无效。";
                    }
                    return false;
                }

                string growthRateSource;
                if (!TryReadOptionalString(
                    card["growthRateSource"],
                    "rideRefineCard.growthRateSource",
                    256,
                    out growthRateSource,
                    out error))
                {
                    return false;
                }

                string source;
                if (!TryReadOptionalString(
                    card["source"],
                    "rideRefineCard.source",
                    256,
                    out source,
                    out error))
                {
                    return false;
                }

                cards.Add(new MountRideRefineCardSnapshot
                {
                    CardIndex = cardIndex,
                    IsCurrent = isCurrent,
                    GrowthRate = growthRate,
                    GrowthRateSource = growthRateSource,
                    Speed = speed,
                    Score = score,
                    Source = source,
                    Skills = skills
                });
            }
            return true;
        }

        private static bool ValidateRideBinding(
            IList<MountRideInstanceSnapshot> instances,
            long? mountId,
            string activeRideInstanceId,
            string rideBindingStatus,
            string rideBindingSource,
            out string error)
        {
            error = string.Empty;
            if (rideBindingStatus != null &&
                !string.Equals(rideBindingStatus, "bound", StringComparison.Ordinal) &&
                !string.Equals(rideBindingStatus, "unresolved", StringComparison.Ordinal) &&
                !string.Equals(rideBindingStatus, "ambiguous", StringComparison.Ordinal) &&
                !string.Equals(rideBindingStatus, "not_mounted", StringComparison.Ordinal))
            {
                error = "坐骑绑定状态无效。";
                return false;
            }

            int currentCount = 0;
            MountRideInstanceSnapshot current = null;
            foreach (MountRideInstanceSnapshot instance in instances ?? new List<MountRideInstanceSnapshot>())
            {
                if (instance.IsCurrent == true)
                {
                    currentCount++;
                    current = instance;
                }
            }

            if (string.Equals(rideBindingStatus, "bound", StringComparison.Ordinal))
            {
                if (!mountId.HasValue ||
                    string.IsNullOrWhiteSpace(activeRideInstanceId) ||
                    string.IsNullOrWhiteSpace(rideBindingSource) ||
                    currentCount != 1 ||
                    current == null ||
                    !string.Equals(current.RideInstanceId, activeRideInstanceId, StringComparison.Ordinal) ||
                    current.IsRiding != true ||
                    !current.RideShapeId.HasValue ||
                    current.RideShapeId.Value != mountId.Value)
                {
                    error = "坐骑绑定证据不完整或不匹配。";
                    return false;
                }
                return true;
            }

            if (activeRideInstanceId != null || rideBindingSource != null || currentCount != 0)
            {
                error = "未绑定坐骑不应携带当前实例证据。";
                return false;
            }
            return true;
        }

        private static bool TryReadProcess(
            JObject value,
            out AndroidMountStatusProcessIdentity process,
            out string error)
        {
            process = null;
            error = string.Empty;
            if (value == null)
            {
                error = "坐骑快照缺少 Android 进程身份。";
                return false;
            }

            int pid;
            long startTicks;
            if (!TryReadInt(value, "pid", out pid, out error) ||
                !TryReadLong(value, "startTicks", out startTicks, out error))
            {
                return false;
            }

            string executable = value.Value<string>("exe");
            if (pid <= 0 || startTicks <= 0 || string.IsNullOrWhiteSpace(executable) || executable.Length > 1024)
            {
                error = "Android 进程身份无效。";
                return false;
            }

            process = new AndroidMountStatusProcessIdentity
            {
                Pid = pid,
                StartTicks = startTicks,
                Executable = executable
            };
            return true;
        }

        private static IDictionary<string, MountStatusRuntimeValue> ReadRawValues(JObject root)
        {
            Dictionary<string, MountStatusRuntimeValue> result =
                new Dictionary<string, MountStatusRuntimeValue>(StringComparer.Ordinal);
            if (root == null)
            {
                return result;
            }

            foreach (JProperty property in root.Properties())
            {
                JObject value = property.Value as JObject;
                if (value == null)
                {
                    continue;
                }

                string kind = value.Value<string>("kind");
                JToken scalar = value["value"];
                int? integer = null;
                double? number = null;
                bool? boolean = null;
                if (scalar != null && scalar.Type != JTokenType.Null)
                {
                    if (string.Equals(kind, "i32", StringComparison.Ordinal) &&
                        scalar.Type == JTokenType.Integer)
                    {
                        integer = scalar.Value<int>();
                    }
                    else if (string.Equals(kind, "f64", StringComparison.Ordinal) &&
                             (scalar.Type == JTokenType.Integer || scalar.Type == JTokenType.Float))
                    {
                        number = scalar.Value<double>();
                    }
                    else if (string.Equals(kind, "boolean", StringComparison.Ordinal) &&
                             scalar.Type == JTokenType.Boolean)
                    {
                        boolean = scalar.Value<bool>();
                    }
                }

                result[property.Name] = new MountStatusRuntimeValue
                {
                    Kind = kind,
                    Raw = value.Value<string>("raw"),
                    Integer = integer,
                    Number = number,
                    Boolean = boolean
                };
            }
            return result;
        }

        private static bool TryReadInt(JObject root, string name, out int value, out string error)
        {
            value = 0;
            error = string.Empty;
            JToken token = root == null ? null : root[name];
            if (token == null || token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out value))
            {
                error = "坐骑快照字段 " + name + " 不是有效整数。";
                return false;
            }
            return true;
        }

        private static bool TryReadLong(JObject root, string name, out long value, out string error)
        {
            value = 0;
            error = string.Empty;
            JToken token = root == null ? null : root[name];
            if (token == null || token.Type != JTokenType.Integer || !long.TryParse(token.ToString(), out value))
            {
                error = "坐骑快照字段 " + name + " 不是有效长整数。";
                return false;
            }
            return true;
        }

        private static bool TryReadFiniteDouble(JObject root, string name, out double value, out string error)
        {
            value = 0;
            error = string.Empty;
            JToken token = root == null ? null : root[name];
            if (token == null ||
                (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                !double.TryParse(token.ToString(), out value) ||
                double.IsNaN(value) ||
                double.IsInfinity(value))
            {
                error = "坐骑快照字段 " + name + " 不是有限数值。";
                return false;
            }
            return true;
        }

        private static bool TryReadBoolean(JObject root, string name, out bool value, out string error)
        {
            value = false;
            error = string.Empty;
            JToken token = root == null ? null : root[name];
            if (token == null || token.Type != JTokenType.Boolean || !bool.TryParse(token.ToString(), out value))
            {
                error = "坐骑快照字段 " + name + " 不是有效布尔值。";
                return false;
            }
            return true;
        }

        private static bool TryReadOptionalLong(JToken token, out long? value, out string error)
        {
            return TryReadOptionalLong(token, "mountId", out value, out error);
        }

        private static bool TryReadOptionalLong(
            JToken token,
            string name,
            out long? value,
            out string error)
        {
            value = null;
            error = string.Empty;
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            long parsed;
            if (token.Type != JTokenType.Integer || !long.TryParse(token.ToString(), out parsed))
            {
                error = "坐骑快照字段 " + name + " 不是有效整数或 null。";
                return false;
            }

            value = parsed;
            return true;
        }

        private static bool TryReadOptionalInt(
            JToken token,
            string name,
            out int? value,
            out string error)
        {
            value = null;
            error = string.Empty;
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            int parsed;
            if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out parsed))
            {
                error = "坐骑快照字段 " + name + " 不是有效整数或 null。";
                return false;
            }

            value = parsed;
            return true;
        }

        private static bool TryReadOptionalFiniteDouble(
            JToken token,
            string name,
            out double? value,
            out string error)
        {
            value = null;
            error = string.Empty;
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                error = "坐骑快照字段 " + name + " 不是有效数值或 null。";
                return false;
            }

            double parsed;
            if (!double.TryParse(token.ToString(), out parsed) ||
                double.IsNaN(parsed) ||
                double.IsInfinity(parsed))
            {
                error = "坐骑快照字段 " + name + " 不是有限数值。";
                return false;
            }

            value = parsed;
            return true;
        }

        private static bool TryReadOptionalBoolean(
            JToken token,
            string name,
            out bool? value,
            out string error)
        {
            value = null;
            error = string.Empty;
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            bool parsed;
            if (token.Type != JTokenType.Boolean || !bool.TryParse(token.ToString(), out parsed))
            {
                error = "坐骑快照字段 " + name + " 不是有效布尔值或 null。";
                return false;
            }

            value = parsed;
            return true;
        }

        private static bool TryReadOptionalString(
            JToken token,
            string name,
            int maxLength,
            out string value,
            out string error)
        {
            value = null;
            error = string.Empty;
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            if (token.Type != JTokenType.String)
            {
                error = "坐骑快照字段 " + name + " 不是有效字符串或 null。";
                return false;
            }

            value = token.Value<string>();
            if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            {
                error = "坐骑快照字段 " + name + " 无效。";
                return false;
            }
            return true;
        }
    }
}
