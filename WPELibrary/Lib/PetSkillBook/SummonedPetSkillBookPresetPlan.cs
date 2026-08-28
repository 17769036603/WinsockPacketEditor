using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// A persisted, human-readable step in the summoned-pet skill-book plan.
    /// This is a plan description only; it does not contain memory addresses
    /// and does not perform a game operation.
    /// </summary>
    public sealed class SummonedPetSkillBookPresetStep
    {
        public SummonedPetSkillBookPresetStep(
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
    /// Defines the initial "召唤兽技能" assistant preset plan.
    /// The plan is deliberately separate from the future game-specific
    /// reader/action adapters so it can be shown and persisted safely first.
    /// </summary>
    public static class SummonedPetSkillBookPresetPlan
    {
        public const int Version = 2;
        public const string InstructionPrefix = "SummonedPetSkillBook";

        /// <summary>
        /// 版本 1 的步骤列表（16 步，包含 CHECK_MATERIALS）。
        /// 用于解码旧版本的编码字符串。
        /// </summary>
        private static readonly List<SummonedPetSkillBookPresetStep> _stepsV1 = new List<SummonedPetSkillBookPresetStep>
        {
            new SummonedPetSkillBookPresetStep(
                1,
                "IDLE",
                "等待启动",
                "确认预设有效、运行器空闲，并创建本次运行上下文。"),
            new SummonedPetSkillBookPresetStep(
                2,
                "VERIFY_CURRENT_PET",
                "确认当前参战召唤兽",
                "读取当前参战召唤兽 ID；与启动时目标比对，宠物变化立即暂停。"),
            new SummonedPetSkillBookPresetStep(
                3,
                "LOAD_PET_STATE",
                "读取召唤兽状态",
                "读取技能格、技能列表、锁定状态和状态版本，并确认快照一致。"),
            new SummonedPetSkillBookPresetStep(
                4,
                "CHECK_MATERIALS",
                "检查执行材料",
                "按物品 ID检查开格材料、技能书、银两和技能目录；缺少材料默认暂停，不跳过。"),
            new SummonedPetSkillBookPresetStep(
                5,
                "OPEN_SLOT_SUBMIT",
                "提交开启技能格",
                "仅通过已确认的正常 UI 事件、业务接口或测试适配器提交一次开格操作。"),
            new SummonedPetSkillBookPresetStep(
                6,
                "OPEN_SLOT_WAIT",
                "等待技能格刷新",
                "等待操作完成或只读状态刷新；不使用固定延时连续点击，超时立即暂停。"),
            new SummonedPetSkillBookPresetStep(
                7,
                "OPEN_SLOT_VERIFY",
                "确认技能格结果",
                "确认宠物未变化、技能格按预期增加且没有其他异常变化。"),
            new SummonedPetSkillBookPresetStep(
                8,
                "BOOK_CHECK",
                "检查当前技能书",
                "重新确认当前宠物、技能书物品 ID、技能 ID、背包数量和使用前技能快照。"),
            new SummonedPetSkillBookPresetStep(
                9,
                "STUDY_SUBMIT",
                "提交使用技能书",
                "按预设顺序通过已确认的正常 UI 事件、业务接口或测试适配器使用一次技能书。"),
            new SummonedPetSkillBookPresetStep(
                10,
                "STUDY_WAIT",
                "等待技能列表刷新",
                "等待使用结果和技能列表刷新；提交结果未知时不重复使用，立即暂停。"),
            new SummonedPetSkillBookPresetStep(
                11,
                "SKILL_DIFF",
                "识别变化技能格",
                "对比前后技能列表，要求恰好一个未锁定技能格变化且结果符合目标技能 ID。"),
            new SummonedPetSkillBookPresetStep(
                12,
                "LOCK_SUBMIT",
                "提交锁定技能格",
                "仅在当前书籍 lockAfter=true 时，对已识别的变化技能格提交一次锁定操作。"),
            new SummonedPetSkillBookPresetStep(
                13,
                "LOCK_WAIT",
                "等待锁定刷新",
                "等待锁定操作完成并读取到目标技能格锁定状态；超时立即暂停。"),
            new SummonedPetSkillBookPresetStep(
                14,
                "LOCK_VERIFY",
                "确认锁定成功",
                "确认宠物、技能 ID和变化技能格未改变，且锁定状态已经生效。"),
            new SummonedPetSkillBookPresetStep(
                15,
                "NEXT_BOOK",
                "进入下一本技能书",
                "记录本本结果并重新校验宠物和材料；还有技能书时回到 BOOK_CHECK。"),
            new SummonedPetSkillBookPresetStep(
                16,
                "COMPLETE",
                "完成预设",
                "最终读取并确认目标宠物、所有步骤和操作结果，写入步骤日志后结束。")
        };

        /// <summary>
        /// 版本 2 的步骤列表（15 步，不包含 CHECK_MATERIALS）。
        /// 当前流程不做开格材料、银两或技能目录预检。
        /// </summary>
        private static readonly List<SummonedPetSkillBookPresetStep> _stepsV2 = new List<SummonedPetSkillBookPresetStep>
        {
            new SummonedPetSkillBookPresetStep(
                1,
                "IDLE",
                "等待启动",
                "确认预设有效、运行器空闲，并创建本次运行上下文。"),
            new SummonedPetSkillBookPresetStep(
                2,
                "VERIFY_CURRENT_PET",
                "确认当前参战召唤兽",
                "读取当前参战召唤兽 ID；与启动时目标比对，宠物变化立即整体失败。"),
            new SummonedPetSkillBookPresetStep(
                3,
                "LOAD_PET_STATE",
                "读取召唤兽状态",
                "读取技能格、技能列表、锁定状态和状态版本，并确认快照一致。"),
            new SummonedPetSkillBookPresetStep(
                4,
                "OPEN_SLOT_SUBMIT",
                "提交开启技能格",
                "启动阶段直接提交开格操作，循环直到所有技能格开放；不预检材料或银两。"),
            new SummonedPetSkillBookPresetStep(
                5,
                "OPEN_SLOT_WAIT",
                "等待技能格刷新",
                "等待每次开格操作完成或只读状态刷新；状态不增加或超时则整体失败。"),
            new SummonedPetSkillBookPresetStep(
                6,
                "OPEN_SLOT_VERIFY",
                "确认技能格结果",
                "确认宠物未变化、技能格严格增加；开满后只进入后续技能书流程。"),
            new SummonedPetSkillBookPresetStep(
                7,
                "BOOK_CHECK",
                "检查当前技能书",
                "确认目标技能是否已存在；已存在则跳过，否则确认空、开放、未锁定技能格并保存使用前快照。"),
            new SummonedPetSkillBookPresetStep(
                8,
                "STUDY_SUBMIT",
                "提交使用技能书",
                "按预设顺序通过已确认的正常 UI 事件、业务接口或测试适配器使用一次技能书。"),
            new SummonedPetSkillBookPresetStep(
                9,
                "STUDY_WAIT",
                "等待技能列表刷新",
                "等待使用结果和技能列表刷新；提交结果未知时不重复使用，将本本记为失败并继续后续书籍。"),
            new SummonedPetSkillBookPresetStep(
                10,
                "SKILL_DIFF",
                "识别变化技能格",
                "对比前后技能列表，要求恰好一个未锁定技能格变化且结果符合目标技能 ID。"),
            new SummonedPetSkillBookPresetStep(
                11,
                "LOCK_SUBMIT",
                "提交锁定技能格",
                "对已识别的目标变化技能格立即提交一次锁定操作，忽略旧配置中的 lockAfter 值。"),
            new SummonedPetSkillBookPresetStep(
                12,
                "LOCK_WAIT",
                "等待锁定刷新",
                "等待锁定操作完成并读取到目标技能格锁定状态；超时将本本记为失败。"),
            new SummonedPetSkillBookPresetStep(
                13,
                "LOCK_VERIFY",
                "确认锁定成功",
                "确认宠物、技能 ID和变化技能格未改变，且锁定状态已经生效。"),
            new SummonedPetSkillBookPresetStep(
                14,
                "NEXT_BOOK",
                "进入下一本技能书",
                "记录本本成功、跳过或失败结果；还有技能书时重新校验宠物并回到 BOOK_CHECK。"),
            new SummonedPetSkillBookPresetStep(
                15,
                "COMPLETE",
                "完成预设",
                "最终读取并确认目标宠物、所有步骤和操作结果，写入步骤日志后结束。")
        };

        public static List<SummonedPetSkillBookPresetStep> GetSteps()
        {
            return _stepsV2;
        }

        /// <summary>
        /// 获取指定版本的步骤列表。
        /// </summary>
        public static List<SummonedPetSkillBookPresetStep> GetSteps(int version)
        {
            return version == 1 ? _stepsV1 : _stepsV2;
        }

        /// <summary>
        /// 检查指令内容列表是否匹配指定版本的模板。
        /// 用于自动发现模板版本，以便进行 V1->V2 迁移。
        /// </summary>
        public static bool MatchesTemplateVersion(
            IEnumerable<string> contents,
            int expectedVersion)
        {
            var expectedSteps = GetSteps(expectedVersion);
            if (expectedSteps == null || expectedSteps.Count == 0)
            {
                return false;
            }

            var contentList = contents as IList<string> ?? contents.ToList();
            if (contentList.Count != expectedSteps.Count)
            {
                return false;
            }

            for (int i = 0; i < expectedSteps.Count; i++)
            {
                SummonedPetSkillBookPresetStep decodedStep;
                if (!TryDecodeStep(contentList[i], out decodedStep) ||
                    decodedStep.Index != expectedSteps[i].Index ||
                    !string.Equals(decodedStep.State, expectedSteps[i].State, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public static string EncodeStep(SummonedPetSkillBookPresetStep step)
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
        /// 解码步骤字符串。支持版本 1 和版本 2 编码格式。
        /// 版本 1 编码格式：SummonedPetSkillBook|1|{Index}|{State}
        /// 版本 2 编码格式：SummonedPetSkillBook|2|{Index}|{State}
        /// </summary>
        public static bool TryDecodeStep(
            string content,
            out SummonedPetSkillBookPresetStep step)
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
                (version != 1 && version != Version) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return false;
            }

            var stepsForVersion = GetSteps(version);
            step = stepsForVersion.FirstOrDefault(item =>
                item.Index == index &&
                string.Equals(item.State, parts[3], StringComparison.Ordinal));
            return step != null;
        }
    }
}
