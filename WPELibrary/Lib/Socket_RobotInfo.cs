using System;
using System.Data;

namespace WPELibrary.Lib
{
    public class Socket_RobotInfo
    {
        #region//是否启用

        protected bool isenable;

        public bool IsEnable
        {
            get { return isenable; }
            set { isenable = value; }
        }

        #endregion

        #region//藏宝图真实发送授权

        private bool treasureLiveSendAuthorized;

        /// <summary>
        /// Whether this assistant has explicitly enabled live treasure-map sends.
        /// The flag is persisted with the assistant and is intentionally separate
        /// from the per-run execution parameters.
        /// </summary>
        public bool TreasureLiveSendAuthorized
        {
            get { return treasureLiveSendAuthorized; }
            set { treasureLiveSendAuthorized = value; }
        }

        #endregion

        #region//序号

        protected Guid rid;

        public Guid RID
        {
            get { return rid; }
            set { rid = value; }
        }

        #endregion

        #region//机器人名称

        protected string rname;

        public string RName
        {
            get { return rname; }
            set { rname = value; }
        }

        #endregion

        #region//指令集        

        protected DataTable rinstruction;

        public DataTable RInstruction
        {
            get { return rinstruction; }
            set { rinstruction = value; }
        }        

        #endregion

        #region//分组

        protected string rfolder = "常用";

        public string RFolder
        {
            get { return string.IsNullOrEmpty(rfolder) ? "常用" : rfolder; }
            set { rfolder = string.IsNullOrEmpty(value) ? "常用" : value; }
        }

        #endregion

        #region//图文识别配置

        private Socket_VisionProfile visionProfile;

        public Socket_VisionProfile VisionProfile
        {
            get
            {
                if (this.visionProfile == null)
                {
                    this.visionProfile = new Socket_VisionProfile();
                }

                return this.visionProfile;
            }
            set { this.visionProfile = value ?? new Socket_VisionProfile(); }
        }

        #endregion

        #region//召唤兽技能书预设

        private PetSkillBook.SummonedPetSkillBookPreset _summonedPetSkillBookPreset;

        /// <summary>
        /// 召唤兽技能书执行预设。
        /// 持久化为 JSON 存于 RobotSummonedPetPreset 表。
        /// 未确认真实游戏数据来源时，仅用于离线测试。
        /// </summary>
        public PetSkillBook.SummonedPetSkillBookPreset SummonedPetSkillBookPreset
        {
            get => _summonedPetSkillBookPreset;
            set => _summonedPetSkillBookPreset = value;
        }

        #endregion

        #region//Socket_RobotInfo

        public Socket_RobotInfo(bool IsEnable, Guid RID, string RName, DataTable RInstructions)
            : this(IsEnable, RID, RName, RInstructions, false)
        {
        }

        public Socket_RobotInfo(
            bool IsEnable,
            Guid RID,
            string RName,
            DataTable RInstructions,
            bool treasureLiveSendAuthorized)
        {
            this.isenable = IsEnable;
            this.rid = RID;
            this.rname = RName;
            this.rinstruction = RInstructions;
            this.rfolder = "常用";
            this.treasureLiveSendAuthorized = treasureLiveSendAuthorized;
        }

        #endregion
    }
}
