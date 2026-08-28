using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.Memory;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 测试场景共享可变状态。
    /// ScriptableTestReadOnlyAdapter 和 ScriptableTestOperationAdapter 共享此实例，
    /// 使操作适配器的调用能正确修改读适配器返回的快照数据。
    /// 设计为 public 以便外部测试程序集访问。
    /// </summary>
    public class ScriptableGameState
    {
        // 核心状态
        public int CurrentPetId { get; set; } = 1001;
        public int StateVersion { get; set; } = 1;
        public bool SimulateRefreshTimeout { get; set; } = false;
        public bool IsCurrentParticipant { get; set; } = true;

        // 插槽状态
        public List<ScriptedSlot> Slots { get; } = new List<ScriptedSlot>();
        public int MaxSlotCount { get; set; } = 4;

        // 背包状态
        public List<ScriptedItem> InventoryItems { get; } = new List<ScriptedItem>();
        public int Silver { get; set; } = 1000;

        // 技能目录
        public List<ScriptedSkillEntry> SkillCatalog { get; } = new List<ScriptedSkillEntry>();

        // 银两消耗常量（默认值，可被修改）
        public static int DefaultSilverCostPerSlot = 100;
        public static int DefaultSilverCostPerStudy = 100;

        public ScriptableGameState(int initialPetId = 1001, int maxSlots = 4)
        {
            CurrentPetId = initialPetId;
            MaxSlotCount = maxSlots;
            IsCurrentParticipant = initialPetId > 0;

            // 初始化插槽：前两个开放，前一个锁定并有技能
            for (int i = 0; i < maxSlots; i++)
            {
                Slots.Add(new ScriptedSlot
                {
                    SlotIndex = i,
                    IsOpen = i < 2,
                    IsLocked = i < 1,
                    SkillId = i < 1 ? 1001 + i : 0
                });
            }

            // 默认技能目录：技能A-已知，技能B-D-未知
            SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 1001, ItemId = 40001, Name = "技能A", Type = "普通", Level = 1, IsKnown = true });
            SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 1002, ItemId = 40002, Name = "技能B", Type = "普通", Level = 1, IsKnown = false });
            SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });
            SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 3001, ItemId = 60001, Name = "技能D", Type = "终极", Level = 1, IsKnown = false });

            // 默认背包：技能书A/B、开格材料(1001)
            InventoryItems.Add(new ScriptedItem { ItemId = 40001, Count = 5 });
            InventoryItems.Add(new ScriptedItem { ItemId = 40002, Count = 3 });
            InventoryItems.Add(new ScriptedItem { ItemId = 1001, Count = 10 });
        }

        // 获取插槽帮助方法
        public ScriptedSlot GetSlot(int slotIndex) => Slots.FirstOrDefault(s => s.SlotIndex == slotIndex);

        // 获取背包物品帮助方法
        public ScriptedItem GetInventoryItem(int itemId) => InventoryItems.FirstOrDefault(i => i.ItemId == itemId);

        // 获取剩余银两
        public int GetSilver() => Silver;

        /// <summary>
        /// 设置背包中指定物品的数量。
        /// 物品存在时更新 Count；不存在且 count > 0 时新增；count <= 0 时不新增。
        /// </summary>
        public void SetInventoryItem(int itemId, int count)
        {
            var existing = InventoryItems.FirstOrDefault(i => i.ItemId == itemId);
            if (existing != null)
            {
                existing.Count = count;
            }
            else if (count > 0)
            {
                InventoryItems.Add(new ScriptedItem { ItemId = itemId, Count = count });
            }
            // count <= 0 且不存在时不添加，也不伪造正库存
        }

        /// <summary>
        /// 设置当前参战宠物 ID 及其参战状态。
        /// petId <= 0 时必须视为非参战。
        /// </summary>
        public void SetPetId(int petId, bool isParticipant = true)
        {
            CurrentPetId = petId;
            IsCurrentParticipant = petId > 0 && isParticipant;
        }
    }

    /// <summary>
    /// 可配置的测试读取适配器，提供确定性、可脚本化的测试场景。
    /// </summary>
    public class ScriptableTestReadOnlyAdapter : IPetSkillBookReadOnlyAdapter
    {
        private readonly ScriptableGameState _state;

        public int ReadResourcesCallCount { get; private set; }
        public int ReadSkillCatalogCallCount { get; private set; }

        public ScriptableTestReadOnlyAdapter(ScriptableGameState sharedState = null)
        {
            _state = sharedState ?? new ScriptableGameState();
        }

        // 场景配置方法

        public void SetPetId(int petId, bool isParticipant = true)
        {
            _state.CurrentPetId = petId;
            _state.IsCurrentParticipant = isParticipant && petId > 0;
        }

        public void AdvanceStateVersion() => _state.StateVersion++;

        public void SetOpenSlots(int count, int maxSlots = 4)
        {
            _state.MaxSlotCount = maxSlots;
            for (int i = 0; i < maxSlots; i++)
            {
                var slot = _state.Slots.FirstOrDefault(s => s.SlotIndex == i);
                if (slot != null)
                {
                    slot.IsOpen = i < count;
                }
            }
        }

        public void SetSlotSkill(int slotIndex, int skillId, bool isLocked = false)
        {
            var slot = _state.GetSlot(slotIndex);
            if (slot != null)
            {
                slot.SkillId = skillId;
                slot.IsLocked = isLocked;
            }
        }

        public void SetInventoryItem(int itemId, int count)
        {
            var item = _state.GetInventoryItem(itemId);
            if (item != null)
            {
                item.Count = count;
            }
            else if (count > 0)
            {
                _state.InventoryItems.Add(new ScriptedItem { ItemId = itemId, Count = count });
            }
        }

        public void SetSilver(int silver) => _state.Silver = silver;

        public void EnableRefreshTimeout(bool enable) => _state.SimulateRefreshTimeout = enable;

        public void ClearInventoryItem(int itemId)
        {
            var item = _state.GetInventoryItem(itemId);
            if (item != null) item.Count = 0;
        }

        public void RemoveSkillFromCatalog(int itemId)
        {
            _state.SkillCatalog.RemoveAll(s => s.ItemId == itemId);
        }

        public void ClearCatalog()
        {
            _state.SkillCatalog.Clear();
        }

        public Task<CurrentPetSnapshot> ReadCurrentPetAsync(CancellationToken cancellationToken)
        {
            ReadOnlyProcessIdentity capturedIdentity = null;
            ReadOnlyProcessIdentity.TryCapture(Process.GetCurrentProcess().Id, out capturedIdentity, out _);

            return Task.FromResult(new CurrentPetSnapshot
            {
                PetId = _state.CurrentPetId,
                IsCurrentParticipant = _state.IsCurrentParticipant,
                ReadAt = DateTime.UtcNow,
                Identity = capturedIdentity
            });
        }

        public Task<PetStateSnapshot> ReadPetStateAsync(CancellationToken cancellationToken)
        {
            var slotsCopy = _state.Slots.Select(s => new PetSkillSlotSnapshot
            {
                SlotIndex = s.SlotIndex,
                IsOpen = s.IsOpen,
                IsLocked = s.IsLocked,
                SkillId = s.SkillId
            }).ToList();

            return Task.FromResult(new PetStateSnapshot
            {
                PetId = _state.CurrentPetId,
                StateVersion = _state.StateVersion,
                OpenSlotCount = slotsCopy.Count(s => s.IsOpen),
                MaxSlotCount = _state.MaxSlotCount,
                SkillSlots = slotsCopy,
                ReadAt = DateTime.UtcNow
            });
        }

        public Task<InventorySnapshot> ReadResourcesAsync(CancellationToken cancellationToken)
        {
            ReadResourcesCallCount++;
            var itemsCopy = _state.InventoryItems.Select(i => new InventoryItemSnapshot
            {
                ItemId = i.ItemId,
                Count = i.Count
            }).ToList();

            return Task.FromResult(new InventorySnapshot
            {
                Items = itemsCopy,
                Silver = _state.Silver,
                ReadAt = DateTime.UtcNow
            });
        }

        public Task<List<SkillBookCatalogEntry>> ReadSkillCatalogAsync(CancellationToken cancellationToken)
        {
            ReadSkillCatalogCallCount++;
            var entries = _state.SkillCatalog.Select(s => new SkillBookCatalogEntry
            {
                SkillId = s.SkillId,
                ItemId = s.ItemId,
                Name = s.Name,
                Type = s.Type,
                Level = s.Level,
                IsKnown = s.IsKnown
            }).ToList();
            return Task.FromResult(entries);
        }

        public Task<bool> WaitForStateRefreshAsync(int targetStateVersion, int timeoutMs, CancellationToken cancellationToken)
        {
            if (_state.SimulateRefreshTimeout)
            {
                return Task.FromResult(false);
            }

            // 自增版本号直到达到目标
            while (_state.StateVersion < targetStateVersion)
            {
                _state.StateVersion++;
            }
            return Task.FromResult(_state.StateVersion >= targetStateVersion);
        }

        public Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        // 供测试代码检查状态
        internal ScriptableGameState InternalState => _state;
    }

    /// <summary>
    /// 可配置的测试操作适配器，记录所有提交的技能书、锁定操作。
    /// 与 ScriptableTestReadOnlyAdapter 共享状态，操作结果会改变只读视图。
    /// </summary>
    public class ScriptableTestOperationAdapter : IPetSkillBookOperationAdapter
    {
        private readonly ScriptableGameState _state;
        private readonly List<OpenedSlotRecord> _openSlotRecords = new List<OpenedSlotRecord>();
        private readonly List<StudyBookRecord> _studyBookRecords = new List<StudyBookRecord>();
        private readonly List<LockedSlotRecord> _lockedSlotRecords = new List<LockedSlotRecord>();
        private OperationResult _nextOpenSlotResult = OperationResult.Accepted;
        private OperationResult _nextStudyBookResult = OperationResult.Accepted;
        private OperationResult _nextLockSlotResult = OperationResult.Accepted;
        private bool _suppressStudyEffect;
        private bool _isCancelled;

        public ScriptableTestOperationAdapter(ScriptableGameState sharedState = null)
        {
            _state = sharedState ?? new ScriptableGameState();
        }

        // 操作结果控制

        public void SetNextOpenSlotResult(OperationResult result) => _nextOpenSlotResult = result;
        public void SetNextStudyBookResult(OperationResult result) => _nextStudyBookResult = result;
        public void SetNextLockSlotResult(OperationResult result) => _nextLockSlotResult = result;
        public void SetSuppressStudyEffect(bool suppress) => _suppressStudyEffect = suppress;

        public void Cancel() => _isCancelled = true;

        // IPetSkillBookOperationAdapter 实现

        public Task<OperationResult> SubmitOpenSlotAsync(int slotIndex, CancellationToken cancellationToken)
        {
            if (_isCancelled) return Task.FromResult(OperationResult.Unavailable);

            var record = new OpenedSlotRecord
            {
                SlotIndex = slotIndex,
                Result = _nextOpenSlotResult
            };
            _openSlotRecords.Add(record);

            if (_nextOpenSlotResult != OperationResult.Accepted)
            {
                return Task.FromResult(_nextOpenSlotResult);
            }

            SimulateOpenSlotAsync(slotIndex);

            return Task.FromResult(OperationResult.Accepted);
        }

        public Task<OperationResult> SubmitStudyBookAsync(int itemId, int skillId, int slotIndex, CancellationToken cancellationToken)
        {
            if (_isCancelled) return Task.FromResult(OperationResult.Unavailable);

            var record = new StudyBookRecord
            {
                ItemId = itemId,
                SkillId = skillId,
                SlotIndex = slotIndex,
                Result = _nextStudyBookResult
            };
            _studyBookRecords.Add(record);

            if (_nextStudyBookResult != OperationResult.Accepted)
            {
                return Task.FromResult(_nextStudyBookResult);
            }

            // 模拟成功操作：学习技能、消耗技能书、消耗银两。
            // 某些回归场景可关闭学习效果，用于验证目标技能未出现。
            if (!_suppressStudyEffect)
            {
                SimulateStudyBookAsync(itemId, skillId, slotIndex);
            }
            else
            {
                _state.StateVersion++;
                var bookItem = _state.GetInventoryItem(itemId);
                if (bookItem != null && bookItem.Count > 0)
                {
                    bookItem.Count--;
                }
            }

            return Task.FromResult(OperationResult.Accepted);
        }

        public Task<OperationResult> SubmitLockSkillSlotAsync(int slotIndex, CancellationToken cancellationToken)
        {
            if (_isCancelled) return Task.FromResult(OperationResult.Unavailable);

            var record = new LockedSlotRecord
            {
                SlotIndex = slotIndex,
                Result = _nextLockSlotResult
            };
            _lockedSlotRecords.Add(record);

            if (_nextLockSlotResult != OperationResult.Accepted)
            {
                return Task.FromResult(_nextLockSlotResult);
            }

            // 模拟成功操作：锁定插槽
            SimulateLockSlotAsync(slotIndex);

            return Task.FromResult(OperationResult.Accepted);
        }

        public void Reset()
        {
            _openSlotRecords.Clear();
            _studyBookRecords.Clear();
            _lockedSlotRecords.Clear();
            _isCancelled = false;
            _nextOpenSlotResult = OperationResult.Accepted;
            _nextStudyBookResult = OperationResult.Accepted;
            _nextLockSlotResult = OperationResult.Accepted;
            _suppressStudyEffect = false;
        }

        // 状态变更模拟方法（同步，调用方负责在需要时切线程）

        private void SimulateOpenSlotAsync(int slotIndex)
        {
            // 增加状态版本以模拟刷新
            _state.StateVersion++;

            // 打开指定插槽
            var slot = _state.GetSlot(slotIndex);
            if (slot != null && !slot.IsOpen)
            {
                slot.IsOpen = true;
            }

            // 消耗银两
            _state.Silver = Math.Max(0, _state.Silver - ScriptableGameState.DefaultSilverCostPerSlot);

            // 消耗开格材料 (物品 ID 1001)
            var materialItem = _state.InventoryItems.FirstOrDefault(i => i.ItemId == 1001);
            if (materialItem != null && materialItem.Count > 0)
            {
                materialItem.Count--;
            }
        }

        private void SimulateStudyBookAsync(int itemId, int skillId, int slotIndex)
        {
            // 增加状态版本以模拟刷新
            _state.StateVersion++;

            // 在插槽中设置技能
            var slot = _state.GetSlot(slotIndex);
            if (slot != null)
            {
                slot.SkillId = skillId;
            }

            // 消耗技能书
            var bookItem = _state.GetInventoryItem(itemId);
            if (bookItem != null && bookItem.Count > 0)
            {
                bookItem.Count--;
            }

            // 消耗银两
            _state.Silver = Math.Max(0, _state.Silver - ScriptableGameState.DefaultSilverCostPerStudy);
        }

        private void SimulateLockSlotAsync(int slotIndex)
        {
            // 增加状态版本以模拟刷新
            _state.StateVersion++;

            // 锁定插槽
            var slot = _state.GetSlot(slotIndex);
            if (slot != null)
            {
                slot.IsLocked = true;
            }
        }

        // 记录访问器

        public IReadOnlyList<OpenedSlotRecord> OpenSlotRecords => _openSlotRecords;
        public IReadOnlyList<StudyBookRecord> StudyBookRecords => _studyBookRecords;
        public IReadOnlyList<LockedSlotRecord> LockedSlotRecords => _lockedSlotRecords;
        public int TotalOpenSlotSubmissions => _openSlotRecords.Count;
        public int TotalStudyBookSubmissions => _studyBookRecords.Count;
        public int TotalLockSlotSubmissions => _lockedSlotRecords.Count;

        // 共享状态访问器（供测试验证）
        public ScriptableGameState SharedState => _state;
    }

    // 记录类型

    public class OpenedSlotRecord
    {
        public int SlotIndex { get; set; }
        public OperationResult Result { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class StudyBookRecord
    {
        public int ItemId { get; set; }
        public int SkillId { get; set; }
        public int SlotIndex { get; set; }
        public OperationResult Result { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class LockedSlotRecord
    {
        public int SlotIndex { get; set; }
        public OperationResult Result { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    // 脚本数据结构（public 供外部测试 Harness 访问）

    public class ScriptedSlot
    {
        public int SlotIndex { get; set; }
        public bool IsOpen { get; set; }
        public bool IsLocked { get; set; }
        public int SkillId { get; set; }
    }

    public class ScriptedItem
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    public class ScriptedSkillEntry
    {
        public int SkillId { get; set; }
        public int ItemId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public int Level { get; set; }
        public bool IsKnown { get; set; }
    }
}
