using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WPELibrary.Lib.MountSpeed
{
    public sealed class MountSkillPresetPreviewItem
    {
        public MountSkillTarget Target { get; internal set; }

        public string Status { get; internal set; }

        public int? CurrentSlotIndex { get; internal set; }

        public string CurrentSkillName { get; internal set; }
    }

    public sealed class MountSkillPresetPreviewResult
    {
        public bool HasTargets { get; internal set; }

        public bool IsSatisfied { get; internal set; }

        public IList<MountSkillPresetPreviewItem> Items { get; internal set; }

        public string Describe()
        {
            if (!this.HasTargets)
            {
                return "只读预演：未配置目标坐骑技能。";
            }

            string details = string.Join(
                "；",
                (this.Items ?? new List<MountSkillPresetPreviewItem>())
                    .Where(item => item != null)
                    .Select(item => string.Format(
                        "{0}={1}",
                        item.Target == null ? "目标" : item.Target.GetDisplayName(),
                        item.Status ?? "未知")));
            return string.Format(
                "只读预演：目标技能 {0}，{1}。",
                this.IsSatisfied ? "已全部满足" : "尚未全部满足",
                string.IsNullOrWhiteSpace(details) ? "无匹配明细" : details);
        }
    }

    public sealed class MountRideRefineTargetPreviewItem
    {
        public MountSkillTarget Target { get; internal set; }

        public string Status { get; internal set; }

        public int? MatchedSlotIndex { get; internal set; }

        public string MatchedSkillName { get; internal set; }
    }

    public sealed class MountRideRefineTargetPreviewResult
    {
        public bool HasTargets { get; internal set; }

        public bool IsSatisfied { get; internal set; }

        public bool GrowthRateMatched { get; internal set; }

        public bool SkillsMatched { get; internal set; }

        public double? TargetGrowthRate { get; internal set; }

        public double? MatchedGrowthRate { get; internal set; }

        public int? MatchedCardIndex { get; internal set; }

        public int CardsChecked { get; internal set; }

        public IList<MountRideRefineTargetPreviewItem> Items { get; internal set; }

        public string Describe()
        {
            if (!this.HasTargets)
            {
                return "炼化目标预演：未配置完整的 3 个技能和成长率。";
            }

            string targetGrowth = this.TargetGrowthRate.HasValue
                ? this.TargetGrowthRate.Value.ToString("0.000", CultureInfo.InvariantCulture)
                : "<未填写>";
            if (this.IsSatisfied)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "炼化目标预演：已命中卡片 {0}（成长率 {1}，3 个技能无序匹配）",
                    this.MatchedCardIndex.HasValue
                        ? this.MatchedCardIndex.Value.ToString(CultureInfo.InvariantCulture)
                        : "<未知>",
                    this.MatchedGrowthRate.HasValue
                        ? this.MatchedGrowthRate.Value.ToString("0.000", CultureInfo.InvariantCulture)
                        : targetGrowth);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "炼化目标预演：未命中（已检查 {0} 张卡片；目标成长率 {1}，成长率匹配={2}，技能集合匹配={3}）",
                this.CardsChecked,
                targetGrowth,
                this.GrowthRateMatched ? "是" : "否",
                this.SkillsMatched ? "是" : "否");
        }
    }

    /// <summary>
    /// Compares the configured target skills with the current bound LocalRide
    /// instance. This class only evaluates an already validated snapshot; it
    /// has no operation, memory-write, input, or packet capability.
    /// </summary>
    public static class MountSkillPresetPreview
    {
        public static bool TryEvaluate(
            MountStatusReadOnlySnapshot snapshot,
            MountSpeedPreset preset,
            out MountSkillPresetPreviewResult result,
            out string error)
        {
            result = null;
            error = string.Empty;
            if (preset == null)
            {
                result = new MountSkillPresetPreviewResult
                {
                    HasTargets = false,
                    IsSatisfied = true,
                    Items = new List<MountSkillPresetPreviewItem>()
                };
                return true;
            }

            if (!preset.IsValid(out error))
            {
                return false;
            }

            IList<MountSkillTarget> targets = preset.TargetSkills ??
                new List<MountSkillTarget>();
            List<MountSkillPresetPreviewItem> items = new List<MountSkillPresetPreviewItem>();
            if (targets.Count == 0)
            {
                result = new MountSkillPresetPreviewResult
                {
                    HasTargets = false,
                    IsSatisfied = true,
                    Items = items
                };
                return true;
            }

            MountRideInstanceSnapshot current = snapshot == null
                ? null
                : (snapshot.RideInstances ??
                    new List<MountRideInstanceSnapshot>())
                    .FirstOrDefault(instance => instance != null &&
                        instance.IsCurrent == true &&
                        string.Equals(
                            instance.RideInstanceId,
                            snapshot.ActiveRideInstanceId,
                            StringComparison.Ordinal));
            IList<MountRideSkillSnapshot> skills = current == null
                ? null
                : current.Skills;

            foreach (MountSkillTarget target in targets)
            {
                MountRideSkillSnapshot matched = skills == null
                    ? null
                    : skills.FirstOrDefault(skill => target.Matches(skill));
                items.Add(new MountSkillPresetPreviewItem
                {
                    Target = target,
                    Status = matched == null
                        ? (current == null ? "无法判断" : "缺少")
                        : "已满足",
                    CurrentSlotIndex = matched == null ? (int?)null : matched.SlotIndex,
                    CurrentSkillName = matched == null ? null : matched.SkillName
                });
            }

            result = new MountSkillPresetPreviewResult
            {
                HasTargets = true,
                IsSatisfied = items.All(item =>
                    item != null && string.Equals(item.Status, "已满足", StringComparison.Ordinal)),
                Items = items
            };
            return true;
        }
    }

    /// <summary>
    /// Evaluates the four-field mount refine target against the cards currently
    /// exposed by the read-only Android snapshot. Skill order is ignored and a
    /// target skill can only consume one card skill slot.
    /// </summary>
    public static class MountRideRefineTargetMatcher
    {
        public const double GrowthRateTolerance = 0.0005;

        public static bool TryEvaluate(
            MountStatusReadOnlySnapshot snapshot,
            MountSpeedPreset preset,
            out MountRideRefineTargetPreviewResult result,
            out string error)
        {
            result = null;
            error = string.Empty;
            if (preset == null)
            {
                error = "坐骑炼化目标预设为空。";
                return false;
            }

            if (!preset.IsCompleteMountRefineTarget(out error))
            {
                return false;
            }

            IList<MountRideRefineCardSnapshot> cards = snapshot == null
                ? new List<MountRideRefineCardSnapshot>()
                : snapshot.RideRefineCards ??
                    new List<MountRideRefineCardSnapshot>();
            IList<MountSkillTarget> targets = preset.TargetSkills;
            List<MountRideRefineTargetPreviewItem> items = CreateItems(targets);
            bool growthRateMatched = false;
            bool skillsMatched = false;

            foreach (MountRideRefineCardSnapshot card in cards)
            {
                if (card == null)
                {
                    continue;
                }

                bool cardGrowthMatched = card.GrowthRate.HasValue &&
                    Math.Abs(
                        card.GrowthRate.Value - preset.TargetGrowthRate.Value) <=
                        GrowthRateTolerance;
                List<int> matchedSlots = FindSkillMatches(targets, card.Skills);
                bool cardSkillsMatched = matchedSlots != null;
                growthRateMatched = growthRateMatched || cardGrowthMatched;
                skillsMatched = skillsMatched || cardSkillsMatched;

                if (!cardGrowthMatched || !cardSkillsMatched)
                {
                    continue;
                }

                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    MountRideRefineSkillSnapshot matchedSkill =
                        card.Skills[matchedSlots[targetIndex]];
                    items[targetIndex].Status = "已满足";
                    items[targetIndex].MatchedSlotIndex = matchedSkill.SlotIndex;
                    items[targetIndex].MatchedSkillName = matchedSkill.SkillName;
                }

                result = new MountRideRefineTargetPreviewResult
                {
                    HasTargets = true,
                    IsSatisfied = true,
                    GrowthRateMatched = true,
                    SkillsMatched = true,
                    TargetGrowthRate = preset.TargetGrowthRate,
                    MatchedGrowthRate = card.GrowthRate,
                    MatchedCardIndex = card.CardIndex,
                    CardsChecked = cards.Count,
                    Items = items
                };
                return true;
            }

            result = new MountRideRefineTargetPreviewResult
            {
                HasTargets = true,
                IsSatisfied = false,
                GrowthRateMatched = growthRateMatched,
                SkillsMatched = skillsMatched,
                TargetGrowthRate = preset.TargetGrowthRate,
                CardsChecked = cards.Count,
                Items = items
            };
            return true;
        }

        private static List<MountRideRefineTargetPreviewItem> CreateItems(
            IList<MountSkillTarget> targets)
        {
            List<MountRideRefineTargetPreviewItem> items =
                new List<MountRideRefineTargetPreviewItem>();
            foreach (MountSkillTarget target in targets)
            {
                items.Add(new MountRideRefineTargetPreviewItem
                {
                    Target = target,
                    Status = "未命中"
                });
            }
            return items;
        }

        private static List<int> FindSkillMatches(
            IList<MountSkillTarget> targets,
            IList<MountRideRefineSkillSnapshot> skills)
        {
            if (skills == null || skills.Count < targets.Count)
            {
                return null;
            }

            bool[] used = new bool[skills.Count];
            List<int> matchedSlots = new List<int>();
            for (int index = 0; index < targets.Count; index++)
            {
                matchedSlots.Add(-1);
            }

            return TryFindSkillMatches(
                    targets,
                    skills,
                    0,
                    used,
                    matchedSlots)
                ? matchedSlots
                : null;
        }

        private static bool TryFindSkillMatches(
            IList<MountSkillTarget> targets,
            IList<MountRideRefineSkillSnapshot> skills,
            int targetIndex,
            bool[] used,
            IList<int> matchedSlots)
        {
            if (targetIndex >= targets.Count)
            {
                return true;
            }

            for (int skillIndex = 0; skillIndex < skills.Count; skillIndex++)
            {
                if (used[skillIndex] || !targets[targetIndex].Matches(skills[skillIndex]))
                {
                    continue;
                }

                used[skillIndex] = true;
                matchedSlots[targetIndex] = skillIndex;
                if (TryFindSkillMatches(
                        targets,
                        skills,
                        targetIndex + 1,
                        used,
                        matchedSlots))
                {
                    return true;
                }

                used[skillIndex] = false;
                matchedSlots[targetIndex] = -1;
            }

            return false;
        }
    }
}
