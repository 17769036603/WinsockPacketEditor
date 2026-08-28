using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 五行升级预设步骤定义
    /// 定义五行升级的状态机流程
    /// </summary>
    public sealed class WuxingUpgradePresetStep
    {
        public WuxingUpgradePresetStep(
            int index,
            string state,
            string title,
            string description)
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
    /// 五行升级预设步骤计划
    /// 状态机：IDLE → SENDED_WOOD → SENDED_WATER → SENDED_SOIL → SENDED_GOLD → SENDED_FIRE → SENDED_OVERALL → WAIT_ROUND_INTERVAL → COMPLETE
    /// </summary>
    public static class WuxingUpgradePresetPlan
    {
        public const int Version = 1;
        public const string InstructionPrefix = "WuxingUpgrade";

        /// <summary>
        /// 获取五行升级计划的步骤列表
        /// 说明：因五行升级是循环执行的本步骤描述为单轮流程
        /// </summary>
        public static List<WuxingUpgradePresetStep> GetSteps()
        {
            return new List<WuxingUpgradePresetStep>
            {
                new WuxingUpgradePresetStep(
                    1,
                    "IDLE",
                    "等待启动",
                    "确认预设有效、运行器空闲，并初始化运行上下文。"),
                new WuxingUpgradePresetStep(
                    2,
                    "SEND_WOOD",
                    "发送木元素升级",
                    "通过 C2S_UseCsCard 协议发送木元素升级请求（operaType=1）。"),
                new WuxingUpgradePresetStep(
                    3,
                    "SEND_WATER",
                    "发送水元素升级",
                    "通过 C2S_UseCsCard 协议发送水元素升级请求（operaType=2）。"),
                new WuxingUpgradePresetStep(
                    4,
                    "SEND_SOIL",
                    "发送土元素升级",
                    "通过 C2S_UseCsCard 协议发送土元素升级请求（operaType=3）。"),
                new WuxingUpgradePresetStep(
                    5,
                    "SEND_GOLD",
                    "发送金元素升级",
                    "通过 C2S_UseCsCard 协议发送金元素升级请求（operaType=4）。"),
                new WuxingUpgradePresetStep(
                    6,
                    "SEND_FIRE",
                    "发送火元素升级",
                    "通过 C2S_UseCsCard 协议发送火元素升级请求（operaType=5）。"),
                new WuxingUpgradePresetStep(
                    7,
                    "SEND_OVERALL",
                    "发送整体升级",
                    "通过 C2S_UseCsCard 协议发送整体升级请求（operaType=6）。"),
                new WuxingUpgradePresetStep(
                    8,
                    "WAIT_ROUND_INTERVAL",
                    "等待轮次间隔",
                    "等待配置的轮次间隔时间（默认200ms），准备进入下一轮。"),
                new WuxingUpgradePresetStep(
                    9,
                    "NEXT_ROUND",
                    "进入下一轮",
                    "递增轮次数计数，若未达到最大轮数则循环执行；否则进入COMPLETE。")
            };
        }

        /// <summary>
        /// 编码步骤格式：InstructionPrefix|Version|Index|State
        /// </summary>
        public static string EncodeStep(WuxingUpgradePresetStep step)
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
        public static bool TryDecodeStep(
            string content,
            out WuxingUpgradePresetStep step)
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
        /// 根据元素类型获取对应的步骤索引
        /// </summary>
        public static int GetStepIndexForElement(ElementalType elementType)
        {
            switch (elementType)
            {
                case ElementalType.Wood:
                    return 2;
                case ElementalType.Water:
                    return 3;
                case ElementalType.Soil:
                    return 4;
                case ElementalType.Gold:
                    return 5;
                case ElementalType.Fire:
                    return 6;
                case ElementalType.Overall:
                    return 7;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// 获取整体升级步骤索引
        /// </summary>
        public static int GetOverallStepIndex() => 7;

        /// <summary>
        /// 获取等待轮次间隔步骤索引
        /// </summary>
        public static int GetWaitIntervalStepIndex() => 8;

        /// <summary>
        /// 获取下一轮步骤索引
        /// </summary>
        public static int GetNextRoundStepIndex() => 9;
    }

    /// <summary>
    /// 五行升级操作配置
    /// 封装单个五行升级操作的参数
    /// </summary>
    public class WuxingUpgradeOperation
    {
        public ElementalType ElementType { get; set; }
        public CsCardTypeId CardTypeId { get; set; }
        public int CardId { get; set; }
        public int OperaType { get; set; }
        public bool IsOverallUpgrade { get; set; } = false;
    }
}