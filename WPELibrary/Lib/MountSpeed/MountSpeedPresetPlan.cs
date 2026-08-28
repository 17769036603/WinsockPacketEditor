using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑速度预设步骤定义。
    /// 支持读取验证和修改坐骑速度。
    /// </summary>
    public sealed class MountSpeedPresetStep
    {
        public MountSpeedPresetStep(
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
    /// 坐骑速度预设状态机定义。
    /// 支持读取验证和修改坐骑速度。
    /// </summary>
    public static class MountSpeedPresetPlan
    {
        public const int Version = 1;
        public const string InstructionPrefix = "MountSpeed";

        public static List<MountSpeedPresetStep> GetSteps()
        {
            return new List<MountSpeedPresetStep>
            {
                new MountSpeedPresetStep(
                    1,
                    "IDLE",
                    "等待启动",
                    "确认预设有效、运行器空闲，并创建本次运行上下文。"),
                new MountSpeedPresetStep(
                    2,
                    "VERIFY_PROCESS",
                    "验证进程身份",
                    "读取当前游戏进程身份标识，确保目标进程正确。"),
                new MountSpeedPresetStep(
                    3,
                    "READ_BASE_SPEED",
                    "读取基础速度",
                    "从游戏内存读取角色的基础移动速度 m_BaseSpeed。"),
                new MountSpeedPresetStep(
                    4,
                    "READ_RIDE_ADD_SPEED",
                    "读取坐骑加成",
                    "从游戏内存读取坐骑速度加成 rideAddSpeed。"),
                new MountSpeedPresetStep(
                    5,
                    "READ_ROLE_MOVE_SPEED",
                    "读取角色移动速度",
                    "从游戏内存读取计算后的角色移动速度 m_RoleMoveSpeed。"),
                new MountSpeedPresetStep(
                    6,
                    "VERIFY_CALCULATION",
                    "验证速度计算",
                    "验证 m_RoleMoveSpeed = baseSpeed * (1 + rideAddSpeed / 100) 的计算是否正确。"),
                new MountSpeedPresetStep(
                    7,
                    "MOUNT_TOGGLE_CHECK",
                    "检查坐骑状态",
                    "如果期望乘坐但未坐骑，或期望未乘坐但已乘骑，暂停并请求确认。"),
                new MountSpeedPresetStep(
                    8,
                    "APPLY_MOUNT_SPEED",
                    "应用坐骑速度",
                    "设置坐骑速度加成到期望值，或下马。"),
                new MountSpeedPresetStep(
                    9,
                    "SPEED_STABILITY_CHECK",
                    "速度稳定性检查",
                    "检查速度值是否在合理范围内，无异常波动。"),
                new MountSpeedPresetStep(
                    10,
                    "LOG_RESULT",
                    "记录结果",
                    "将读取的速度值和验证结果写入步骤日志。"),
                new MountSpeedPresetStep(
                    11,
                    "COMPLETE",
                    "完成预设",
                    "最终确认所有步骤完成，写入完成日志后结束。")
            };
        }

        public static string EncodeStep(MountSpeedPresetStep step)
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

        public static bool TryDecodeStep(
            string content,
            out MountSpeedPresetStep step)
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
    }
}