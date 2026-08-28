using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 发送前的 resident 内存快照基线。它只描述已绑定的只读上下文，
    /// 不包含地址、偏移、进程附加或发送权限。
    /// </summary>
    public sealed class RefineMemoryBaseline
    {
        public EquipmentRefineDetector.ProcessIdentity ProcessIdentity { get; set; }
        public string ContainerIdentity { get; set; } = string.Empty;
        public string StreamSessionId { get; set; } = string.Empty;
        public string SnapshotId { get; set; } = string.Empty;
        public long Sequence { get; set; }
        public string EquipmentIdentity { get; set; } = string.Empty;
        public string Slot { get; set; } = string.Empty;
        public string EquipmentId { get; set; } = string.Empty;
        public EquipmentTargetMode TargetMode { get; set; } = EquipmentTargetMode.Worn;
        public string ItemId { get; set; } = string.Empty;
        public string ItemTypeId { get; set; } = string.Empty;

        public bool IsValid
        {
            get
            {
                return this.ProcessIdentity != null && this.ProcessIdentity.IsValid &&
                    !string.IsNullOrWhiteSpace(this.ContainerIdentity) &&
                    !string.IsNullOrWhiteSpace(this.StreamSessionId) &&
                    !string.IsNullOrWhiteSpace(this.SnapshotId) &&
                    this.Sequence > 0 &&
                    !string.IsNullOrWhiteSpace(this.EquipmentIdentity) &&
                    !string.IsNullOrWhiteSpace(this.Slot) &&
                    (this.TargetMode == EquipmentTargetMode.Bag ||
                     !string.IsNullOrWhiteSpace(this.EquipmentId));
            }
        }
    }

    /// <summary>
    /// 已由宿主解析的只读 resident 结果。字段必须来自同一次发送后的新快照；
    /// 真实内存读取器、地址和偏移不属于本项目的实现边界。
    /// </summary>
    public sealed class RefineMemoryResultSnapshot
    {
        public EquipmentRefineDetector.ProcessIdentity ProcessIdentity { get; set; }
        public string ContainerIdentity { get; set; } = string.Empty;
        public string StreamSessionId { get; set; } = string.Empty;
        public string SnapshotId { get; set; } = string.Empty;
        public long Sequence { get; set; }
        public string EquipmentIdentity { get; set; } = string.Empty;
        public string Slot { get; set; } = string.Empty;
        public string EquipmentId { get; set; } = string.Empty;
        public EquipmentTargetMode TargetMode { get; set; } = EquipmentTargetMode.Worn;
        public string ItemId { get; set; } = string.Empty;
        public string ItemTypeId { get; set; } = string.Empty;
        public bool IsStable { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public List<RefineResponseCard> Cards { get; private set; } = new List<RefineResponseCard>();
    }

    /// <summary>
    /// 每轮 SendRefinePacketAsync 完成后读取本轮 resident 结果的注入契约。
    /// 实现不得复用旧快照；未配置时状态机发送前安全停止。
    /// </summary>
    public interface IRefineMemoryResultSource
    {
        Task<RefineMemoryResultSnapshot> ReceiveAfterSendAsync(
            RefineMemoryBaseline baseline,
            int timeoutMs,
            CancellationToken cancellationToken);
    }

    /// <summary>明确拒绝 live 读取的默认占位实现。</summary>
    public sealed class UnconfiguredRefineMemoryResultSource : IRefineMemoryResultSource
    {
        public Task<RefineMemoryResultSnapshot> ReceiveAfterSendAsync(
            RefineMemoryBaseline baseline,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<RefineMemoryResultSnapshot>(null);
        }
    }
}
