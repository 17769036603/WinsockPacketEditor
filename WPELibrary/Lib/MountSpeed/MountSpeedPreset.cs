using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// One target skill for the read-only mount preset preview.
    /// SkillId and SkillName are aliases when only one is provided; when both
    /// are present they must match the same current skill.
    /// </summary>
    public sealed class MountSkillTarget
    {
        public int SkillId { get; set; }

        public string SkillName { get; set; } = string.Empty;

        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;
            this.SkillName = (this.SkillName ?? string.Empty).Trim();
            if (this.SkillId <= 0 && string.IsNullOrWhiteSpace(this.SkillName))
            {
                errorMessage = "目标坐骑技能必须提供技能 ID 或名称。";
                return false;
            }

            if (this.SkillId < 0)
            {
                errorMessage = "目标坐骑技能 ID 不能为负数。";
                return false;
            }

            if (this.SkillName.Length > 256)
            {
                errorMessage = "目标坐骑技能名称过长。";
                return false;
            }

            return true;
        }

        public bool Matches(MountRideSkillSnapshot skill)
        {
            if (skill == null)
            {
                return false;
            }

            return this.Matches(skill.SkillId, skill.SkillName);
        }

        public bool Matches(MountRideRefineSkillSnapshot skill)
        {
            if (skill == null)
            {
                return false;
            }

            return this.Matches(skill.SkillId, skill.SkillName);
        }

        private bool Matches(int skillId, string skillName)
        {
            bool idMatches = this.SkillId <= 0 || this.SkillId == skillId;
            bool nameMatches = string.IsNullOrWhiteSpace(this.SkillName) ||
                string.Equals(this.SkillName, skillName, StringComparison.Ordinal);
            return idMatches && nameMatches;
        }

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(this.SkillName) && this.SkillId > 0)
            {
                return this.SkillName + " (ID " + this.SkillId + ")";
            }

            return !string.IsNullOrWhiteSpace(this.SkillName)
                ? this.SkillName
                : "技能 ID " + this.SkillId;
        }
    }

    /// <summary>
    /// 坐骑速度预设定义。
    /// 支持读取验证和修改坐骑速度。
    /// </summary>
    public class MountSpeedPreset
    {
        public const int SchemaVersion = 1;

        public int SchemaVersionProperty { get; set; } = SchemaVersion;

        /// <summary>
        /// 预设名称。
        /// </summary>
        public string Name { get; set; } = "坐骑速度";

        /// <summary>
        /// 基础移动速度。0 表示使用游戏默认值。
        /// </summary>
        public double BaseSpeed { get; set; } = 200;

        /// <summary>
        /// 期望的坐骑速度加成（百分比）。
        /// </summary>
        public double ExpectedRideAddSpeed { get; set; } = 0;

        /// <summary>
        /// 是否希望乘坐坐骑。
        /// </summary>
        public bool DesiredMount { get; set; } = false;

        /// <summary>
        /// 是否启用自动刷新速度检测。
        /// </summary>
        public bool EnableSpeedRefresh { get; set; } = true;

        /// <summary>
        /// 步骤日志启用。
        /// </summary>
        public bool EnableStepLog { get; set; } = true;

        /// <summary>
        /// 是否应用坐骑速度。启用后会设置指定的坐骑加成。
        /// </summary>
        public bool ApplyMountSpeed { get; set; } = false;

        /// <summary>
        /// 只读预演目标技能。空列表表示只读取当前技能，不做目标匹配。
        /// </summary>
        public List<MountSkillTarget> TargetSkills { get; set; } =
            new List<MountSkillTarget>();

        /// <summary>
        /// 坐骑炼化目标成长率。与三个目标技能一起组成炼化命中条件。
        /// 保持可空，以兼容旧的坐骑速度预设。
        /// </summary>
        public double? TargetGrowthRate { get; set; }

        /// <summary>
        /// 创建一坐骑炼化的默认目标。
        /// 只由新建/未配置目标的编辑入口调用，不改变已有保存预设。
        /// </summary>
        public static MountSpeedPreset CreateDefaultFirstRideRefinePreset()
        {
            return new MountSpeedPreset
            {
                Name = "一坐骑洗炼",
                TargetGrowthRate = 1.175,
                TargetSkills = new List<MountSkillTarget>
                {
                    new MountSkillTarget
                    {
                        SkillId = 61108,
                        SkillName = "高级秋水流弦"
                    },
                    new MountSkillTarget
                    {
                        SkillId = 61118,
                        SkillName = "高级百步穿杨"
                    },
                    new MountSkillTarget
                    {
                        SkillId = 61117,
                        SkillName = "高级追魂夺命"
                    }
                }
            };
        }

        /// <summary>
        /// 获取预设是否有效。
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (BaseSpeed <= 0)
            {
                errorMessage = "基础速度必须为正数。";
                return false;
            }

            if (ExpectedRideAddSpeed < 0)
            {
                errorMessage = "坐骑速度加成不能为负数。";
                return false;
            }

            // 当应用坐骑速度时，期望的坐骑加成必须为正数
            if (ApplyMountSpeed && ExpectedRideAddSpeed <= 0)
            {
                errorMessage = "应用坐骑速度时，期望的坐骑加成必须为正数。";
                return false;
            }

            if (TargetSkills == null)
            {
                TargetSkills = new List<MountSkillTarget>();
            }

            for (int index = 0; index < TargetSkills.Count; index++)
            {
                MountSkillTarget target = TargetSkills[index];
                if (target == null)
                {
                    errorMessage = string.Format(
                        "第 {0} 个目标坐骑技能无效：{1}",
                        index + 1,
                        "配置为空。");
                    return false;
                }

                string targetError;
                if (!target.IsValid(out targetError))
                {
                    errorMessage = string.Format(
                        "第 {0} 个目标坐骑技能无效：{1}",
                        index + 1,
                        targetError);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 验证坐骑炼化目标是否已经填写完整。
        /// 这是炼化目标的专用校验，不改变旧坐骑速度预设的通用校验规则。
        /// </summary>
        public bool IsCompleteMountRefineTarget(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!this.IsValid(out errorMessage))
            {
                return false;
            }

            if (this.TargetSkills == null || this.TargetSkills.Count != 3)
            {
                errorMessage = "坐骑炼化目标必须填写 3 个技能。";
                return false;
            }

            if (!this.TargetGrowthRate.HasValue ||
                double.IsNaN(this.TargetGrowthRate.Value) ||
                double.IsInfinity(this.TargetGrowthRate.Value) ||
                this.TargetGrowthRate.Value < 0)
            {
                errorMessage = "坐骑炼化目标必须填写有效的成长率。";
                return false;
            }

            for (int leftIndex = 0; leftIndex < this.TargetSkills.Count; leftIndex++)
            {
                MountSkillTarget left = this.TargetSkills[leftIndex];
                for (int rightIndex = leftIndex + 1;
                    rightIndex < this.TargetSkills.Count;
                    rightIndex++)
                {
                    MountSkillTarget right = this.TargetSkills[rightIndex];
                    if (left.SkillId == right.SkillId &&
                        string.Equals(
                            (left.SkillName ?? string.Empty).Trim(),
                            (right.SkillName ?? string.Empty).Trim(),
                            StringComparison.Ordinal))
                    {
                        errorMessage = "坐骑炼化目标的 3 个技能不能重复。";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 获取计算后的角色移动速度。
        /// 公式：m_RoleMoveSpeed = baseSpeed * (1 + rideAddSpeed / 100)
        /// </summary>
        public double GetCalculatedRoleMoveSpeed()
        {
            return CalculateMountSpeedModel.CalculateRoleMoveSpeed(BaseSpeed, ExpectedRideAddSpeed);
        }

        /// <summary>
        /// 获取预设的显示名称。
        /// </summary>
        public string GetDisplayName()
        {
            if (!ApplyMountSpeed || ExpectedRideAddSpeed == 0)
            {
                return $"{Name} (未乘坐)";
            }
            return $"{Name} (加成 {ExpectedRideAddSpeed}%)";
        }
    }
}
