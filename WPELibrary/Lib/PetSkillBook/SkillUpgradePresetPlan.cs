using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 法术升级预设步骤定义
    /// 定义法术升级的状态机流程
    /// </summary>
    public sealed class SkillUpgradePresetStep
    {
        public SkillUpgradePresetStep(int index, string state, string title, string description)
        {
            this.Index = index;
            this.State = state ?? string.Empty;
            this.Title = title ?? string.Empty;
            this.Description = description ?? string.Empty;
        }

        public int Index { get; private set; }
        public string State { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }

        public string DisplayText
        {
            get
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:00}. {1}：{2}",
                    this.Index,
                    this.State,
                    this.Description);
            }
        }
    }

    /// <summary>
    /// 法术升级预设步骤计划
    /// 状态机：IDLE → SEND_LEARN → WAIT_INTERVAL → NEXT_TASK → COMPLETE
    /// </summary>
    public static class SkillUpgradePresetPlan
    {
        public const int Version = 1;
        public const string InstructionPrefix = "SkillUpgrade";

        /// <summary>
        /// 获取法术升级计划的步骤列表
        /// 说明：因法术升级是多任务循环执行，步骤描述为单任务流程
        /// </summary>
        public static List<SkillUpgradePresetStep> GetSteps()
        {
            return new List<SkillUpgradePresetStep>
            {
                new SkillUpgradePresetStep(1, "IDLE", "等待启动",
                    "确认预设有效、运行器空闲，并初始化运行上下文。"),
                new SkillUpgradePresetStep(2, "SEND_LEARN", "发送法术升级请求",
                    "通过 C2S_LearnSkill (0x2074) 协议发送当前任务的法术升级请求。"),
                new SkillUpgradePresetStep(3, "WAIT_INTERVAL", "等待请求间隔",
                    "等待配置的请求间隔时间（默认 200ms）。"),
                new SkillUpgradePresetStep(4, "NEXT_TASK", "进入下一任务",
                    "递增任务索引，若未到达列表末尾则继续执行；否则判断是否进入下一轮。"),
                new SkillUpgradePresetStep(5, "COMPLETE", "完成",
                    "所有任务已完成。")
            };
        }

        /// <summary>
        /// 编码步骤格式：InstructionPrefix|Version|Index|State
        /// </summary>
        public static string EncodeStep(SkillUpgradePresetStep step)
        {
            if (step == null || step.Index <= 0 || string.IsNullOrWhiteSpace(step.State))
            {
                return string.Empty;
            }

            return string.Join(
                "|",
                InstructionPrefix,
                Version.ToString(CultureInfo.InvariantCulture),
                step.Index.ToString(CultureInfo.InvariantCulture),
                step.State);
        }

        /// <summary>
        /// 解码步骤格式：InstructionPrefix|Version|Index|State
        /// </summary>
        public static bool TryDecodeStep(string content, out SkillUpgradePresetStep step)
        {
            step = null;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            string[] parts = content.Split(new[] { '|' }, StringSplitOptions.None);
            if (parts.Length != 4 ||
                !string.Equals(parts[0], InstructionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            int version;
            int index;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version) ||
                version != Version ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return false;
            }

            step = GetSteps().FirstOrDefault(item =>
                item.Index == index &&
                string.Equals(item.State, parts[3], StringComparison.Ordinal));
            return step != null;
        }

        /// <summary>
        /// 获取发送法术升级请求步骤索引
        /// </summary>
        public static int GetSendStepIndex() => 2;

        /// <summary>
        /// 获取等待间隔步骤索引
        /// </summary>
        public static int GetWaitIntervalStepIndex() => 3;

        /// <summary>
        /// 获取下一任务步骤索引
        /// </summary>
        public static int GetNextTaskStepIndex() => 4;

        /// <summary>
        /// 获取完成步骤索引
        /// </summary>
        public static int GetCompleteStepIndex() => 5;
    }
}
