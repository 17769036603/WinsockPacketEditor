using System;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// Safe placeholder for the real open/study/lock transport adapter.
    /// Until packet contracts, session binding, and response verification are
    /// independently confirmed, every operation is rejected without touching
    /// a socket, process memory, or game state.
    /// </summary>
    public sealed class FailClosedPetSkillBookOperationAdapter : IPetSkillBookOperationAdapter
    {
        public const string DefaultReason =
            "技能书真实操作协议尚未完成验证，已保持只读并拒绝执行。";

        private readonly string reason;

        public FailClosedPetSkillBookOperationAdapter()
            : this(null)
        {
        }

        public FailClosedPetSkillBookOperationAdapter(string rejectionReason = null)
        {
            this.reason = string.IsNullOrWhiteSpace(rejectionReason)
                ? DefaultReason
                : rejectionReason.Trim();
        }

        public int RejectedCallCount { get; private set; }

        public string LastOperation { get; private set; } = string.Empty;

        public string LastRejectionReason { get; private set; } = string.Empty;

        public Task<OperationResult> SubmitOpenSlotAsync(
            int slotIndex,
            CancellationToken cancellationToken)
        {
            return Reject("开格", "slotIndex=" + slotIndex);
        }

        public Task<OperationResult> SubmitStudyBookAsync(
            int itemId,
            int skillId,
            int slotIndex,
            CancellationToken cancellationToken)
        {
            return Reject(
                "学习技能书",
                string.Format(
                    "itemId={0}, skillId={1}, slotIndex={2}",
                    itemId,
                    skillId,
                    slotIndex));
        }

        public Task<OperationResult> SubmitLockSkillSlotAsync(
            int slotIndex,
            CancellationToken cancellationToken)
        {
            return Reject("锁格", "slotIndex=" + slotIndex);
        }

        private Task<OperationResult> Reject(string operation, string detail)
        {
            this.RejectedCallCount++;
            this.LastOperation = operation + "（" + detail + "）";
            this.LastRejectionReason = this.reason;
            return Task.FromResult(OperationResult.Unavailable);
        }
    }
}
