using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 法术升级预设定义
    /// 用于自动化人物法术升级流程（C2S_LearnSkill 0x2074）
    /// </summary>
    public class SkillUpgradePreset
    {
        public const int SchemaVersion = 1;

        public int SchemaVersionProperty { get; set; } = SchemaVersion;

        /// <summary>
        /// 预设名称
        /// </summary>
        public string Name { get; set; } = "法术升级";

        /// <summary>
        /// 法术升级任务列表（默认含候选占位任务）
        /// skillId 与 TargetLevel 均为未经验证的占位默认值，需按真实抓包结果修正
        /// </summary>
        public List<SkillUpgradeTask> Tasks { get; set; } = new List<SkillUpgradeTask>();

        /// <summary>
        /// 使用默认候选任务初始化预设
        /// 默认任务为占位值（skillId=23 → 目标等级 121），需按真实抓包结果修正
        /// </summary>
        public static SkillUpgradePreset CreateDefault()
        {
            return new SkillUpgradePreset
            {
                Tasks = new List<SkillUpgradeTask>
                {
                    new SkillUpgradeTask
                    {
                        SkillId = 23,
                        TargetLevel = 121,
                        Remark = "占位默认任务，需按真实抓包结果修正"
                    }
                }
            };
        }

        /// <summary>
        /// 每个请求间隔时间（毫秒）
        /// </summary>
        public int RequestIntervalMs { get; set; } = 200;

        /// <summary>
        /// 是否在每次操作后等待请求间隔时间
        /// </summary>
        public bool WaitForRequestInterval { get; set; } = true;

        /// <summary>
        /// 是否循环运行
        /// </summary>
        public bool EnableLooping { get; set; } = true;

        /// <summary>
        /// 最大运行轮数（0 表示无限循环）
        /// </summary>
        public int MaxRounds { get; set; } = 0;

        /// <summary>
        /// 日志级别：0=仅错误, 1=正常, 2=详细
        /// </summary>
        public int LogLevel { get; set; } = 1;

        /// <summary>
        /// 验证预设有效性
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (Tasks == null || Tasks.Count == 0)
            {
                errorMessage = "法术升级任务列表不能为空。";
                return false;
            }

            for (int i = 0; i < Tasks.Count; i++)
            {
                if (Tasks[i].SkillId <= 0)
                {
                    errorMessage = string.Format("任务 {0} 的 skillId 必须大于 0。", i + 1);
                    return false;
                }
                if (Tasks[i].TargetLevel <= 0)
                {
                    errorMessage = string.Format("任务 {0} 的 TargetLevel 必须大于 0。", i + 1);
                    return false;
                }
            }

            if (RequestIntervalMs < 0)
            {
                errorMessage = "请求间隔时间不能为负数。";
                return false;
            }

            if (MaxRounds < 0)
            {
                errorMessage = "最大轮数不能为负数。";
                return false;
            }

            return true;
        }
    }
}
