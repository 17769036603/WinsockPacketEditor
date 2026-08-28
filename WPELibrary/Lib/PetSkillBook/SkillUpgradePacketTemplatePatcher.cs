using System;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// C2S_LearnSkill 模板校验与字段修补器。
    /// 与藏宝图/五行升级的模板修补器同级：只替换允许变化的字段，
    /// 保留用户捕获的真实封包头部、静态段和角色 ID。
    ///
    /// roleId 由服务端分配且动态变化，运行时不得覆盖；
    /// 只修补 skillId 与 learnLevel 两个字段。
    /// </summary>
    public static class SkillUpgradePacketTemplatePatcher
    {
        /// <summary>
        /// 校验捕获的模板封包是否为合法 C2S_LearnSkill 帧。
        /// </summary>
        public static void ValidateTemplate(byte[] template)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            if (!SkillUpgradePacketEncoder.TryValidateFrame(template))
            {
                throw new SkillUpgradePacketTemplateException(
                    "frame_invalid",
                    "C2S_LearnSkill template does not match the audited contract.");
            }
        }

        /// <summary>
        /// 基于合法模板生成法术升级请求帧。
        /// 只替换 <c>skillId</c> 与 <c>learnLevel</c>，其余（含 roleId）全部保留。
        /// </summary>
        public static byte[] BuildLearnSkillFrame(byte[] template, int skillId, int learnLevel)
        {
            ValidateTemplate(template);
            byte[] result = (byte[])template.Clone();
            WriteInt32BigEndian(result, SkillUpgradePacketEncoder.SkillIdOffset, skillId);
            WriteInt32BigEndian(result, SkillUpgradePacketEncoder.LearnLevelOffset, learnLevel);
            return result;
        }

        private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
        {
            uint unsignedValue = unchecked((uint)value);
            buffer[offset] = (byte)(unsignedValue >> 24);
            buffer[offset + 1] = (byte)(unsignedValue >> 16);
            buffer[offset + 2] = (byte)(unsignedValue >> 8);
            buffer[offset + 3] = (byte)unsignedValue;
        }
    }

    /// <summary>
    /// 法术升级封包模板异常。
    /// </summary>
    public sealed class SkillUpgradePacketTemplateException : InvalidOperationException
    {
        public SkillUpgradePacketTemplateException(string code, string message)
            : base(message)
        {
            this.Code = code ?? string.Empty;
        }

        public string Code { get; private set; }
    }
}
