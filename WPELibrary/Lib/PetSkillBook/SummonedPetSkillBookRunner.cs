using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.Memory;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 召唤兽技能书预设状态机 runner。
    /// 使用依赖注入的适配器，在没有真实游戏进程时使用 TestAdapters 进行离线测试。
    /// </summary>
    public class SummonedPetSkillBookRunner
    {
        public enum State
        {
            IDLE,
            VERIFY_CURRENT_PET,
            LOAD_PET_STATE,
            CHECK_MATERIALS,
            OPEN_SLOT_SUBMIT,
            OPEN_SLOT_WAIT,
            OPEN_SLOT_VERIFY,
            BOOK_CHECK,
            STUDY_SUBMIT,
            STUDY_WAIT,
            SKILL_DIFF,
            LOCK_SUBMIT,
            LOCK_WAIT,
            LOCK_VERIFY,
            NEXT_BOOK,
            COMPLETE
        }

        public event Action<string, string> OnLog;
        public event Action<State> OnStateChanged;
        public event Action<bool> OnPaused;

        private readonly SummonedPetSkillBookPreset _preset;
        private readonly IPetSkillBookReadOnlyAdapter _readOnlyAdapter;
        private readonly IPetSkillBookOperationAdapter _operationAdapter;
        private readonly CancellationTokenSource _cts;
        private readonly SummonedPetSkillBookContext _context;

        private State _currentState = State.IDLE;
        private CurrentPetSnapshot _snapshotBeforeRefresh;
        private PetStateSnapshot _previousPetState;
        private int _changedSlotIndex = -1;

        public State CurrentState => _currentState;
        public SummonedPetSkillBookContext Context => _context;
        public bool IsPaused => _context.IsPaused;
        public bool IsCancelled => _cts?.IsCancellationRequested ?? true;

        public SummonedPetSkillBookRunner(
            SummonedPetSkillBookPreset preset,
            IPetSkillBookReadOnlyAdapter readOnlyAdapter,
            IPetSkillBookOperationAdapter operationAdapter)
        {
            _preset = preset ?? throw new ArgumentNullException(nameof(preset));
            _readOnlyAdapter = readOnlyAdapter ?? throw new ArgumentNullException(nameof(readOnlyAdapter));
            _operationAdapter = operationAdapter ?? throw new ArgumentNullException(nameof(operationAdapter));
            _cts = new CancellationTokenSource();
            _context = new SummonedPetSkillBookContext { Preset = preset };
        }

        public async Task StartAsync()
        {
            if (_currentState != State.IDLE)
            {
                throw new InvalidOperationException("Runner is not in Idle state.");
            }

            if (!_preset.IsValid(out string error))
            {
                await LogAsync($"预设无效: {error}");
                Pause($"预设无效: {error}");
                return;
            }

            SetState(State.IDLE);
            await LogAsync("开始执行召唤兽技能预设");

            // 1. 开始执行时调用 VerifyProcessIdentityAsync
            if (!await _readOnlyAdapter.VerifyProcessIdentityAsync(_cts.Token))
            {
                Pause("进程身份验证失败：无法验证宠物技能书操作进程身份");
                return;
            }
            await LogAsync("进程身份验证通过");

            try
            {
                await ExecuteStateMachineAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                await LogAsync("执行被取消");
            }
            catch (Exception ex)
            {
                await LogAsync($"执行错误: {ex.Message}");
                Pause($"执行错误: {ex.Message}");
            }
        }

        public void Pause(string reason)
        {
            _context.IsPaused = true;
            _context.PauseReason = reason;
            OnPaused?.Invoke(true);
        }

        public void Resume()
        {
            _context.IsPaused = false;
            _context.PauseReason = string.Empty;
            OnPaused?.Invoke(false);
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        private async Task ExecuteStateMachineAsync(CancellationToken cancellationToken)
        {
            // IDLE 状态
            SetState(State.IDLE);

            // 主循环
            while (_context.CurrentBookIndex < _preset.Books.Count && !_context.IsPaused && !cancellationToken.IsCancellationRequested)
            {
                // VERIFY_CURRENT_PET：每本书开始前重新确认当前参战宠物
                if (!await VerifyCurrentPetAsync(cancellationToken))
                {
                    if (_context.IsPaused) return;
                    continue;
                }

                // LOAD_PET_STATE
                if (!await LoadPetStateAsync(cancellationToken))
                {
                    if (_context.IsPaused) return;
                    continue;
                }

                // CHECK_MATERIALS
                if (!await CheckMaterialsAsync(cancellationToken))
                {
                    if (_context.IsPaused) return;
                    continue;
                }

                // OPEN_SLOT_SUBMIT/WAIT/VERIFY
                if (_preset.OpenAllSlots)
                {
                    if (!await OpenAllSlotsAsync(cancellationToken))
                    {
                        if (_context.IsPaused) return;
                        continue;
                    }
                }

                // BOOK_CHECK
                if (!await BookCheckAsync(cancellationToken))
                {
                    if (_context.IsPaused) return;
                    continue;
                }

                // STUDY_SUBMIT/WAIT
                var book = _preset.Books[_context.CurrentBookIndex];
                var openSlot = GetNextAvailableSlot();
                if (openSlot == null)
                {
                    Pause("没有可用技能格");
                    return;
                }

                if (!await StudyBookAsync(book, openSlot, cancellationToken))
                {
                    if (_context.IsPaused) return;
                    continue;
                }

                // SKILL_DIFF
                if (!await VerifySkillDiffAsync(book, cancellationToken))
                {
                    if (_context.IsPaused) return;
                    continue;
                }

                // LOCK_SUBMIT/WAIT/VERIFY
                if (book.LockAfter)
                {
                    if (!await LockSlotAsync(_changedSlotIndex, cancellationToken))
                    {
                        if (_context.IsPaused) return;
                        continue;
                    }
                }

                // NEXT_BOOK
                SetState(State.NEXT_BOOK);
                await LogAsync("NEXT_BOOK: 完成当前技能书，进入下一本");
                _context.CurrentBookIndex++;
            }

            // 循环结束后的最终保护
            if (_context.IsPaused || cancellationToken.IsCancellationRequested)
            {
                return; // 已暂停或取消，不进入 COMPLETE
            }

            // 只有所有技能书完成、未暂停、未取消时才进入 COMPLETE
            if (_context.CurrentBookIndex == _preset.Books.Count)
            {
                SetState(State.COMPLETE);
                await LogAsync("完成预设执行");
            }
        }

        private async Task<bool> VerifyCurrentPetAsync(CancellationToken cancellationToken)
        {
            SetState(State.VERIFY_CURRENT_PET);
            await LogAsync("VERIFY_CURRENT_PET: 读取当前参战召唤兽");

            var pet = await _readOnlyAdapter.ReadCurrentPetAsync(cancellationToken);

            if (!pet.IsCurrentParticipant)
            {
                Pause("当前召唤兽不在参战状态");
                return false;
            }

            // 检查宠物变化：每本书开始前重新确认
            if (_snapshotBeforeRefresh != null && _snapshotBeforeRefresh.PetId != pet.PetId)
            {
                Pause($"VERIFY_CURRENT_PET: 宠物ID变化: 当前 {pet.PetId} != 上次 {_snapshotBeforeRefresh.PetId}");
                return false;
            }

            _snapshotBeforeRefresh = pet;
            _context.CurrentPetId = pet.PetId;
            await LogAsync($"当前召唤兽 ID: {pet.PetId}");
            return true;
        }

        private async Task<bool> LoadPetStateAsync(CancellationToken cancellationToken)
        {
            SetState(State.LOAD_PET_STATE);
            await LogAsync("LOAD_PET_STATE: 读取宠物状态");

            var pet = await _readOnlyAdapter.ReadCurrentPetAsync(cancellationToken);
            if (pet.PetId != _context.CurrentPetId)
            {
                Pause("LOAD_PET_STATE: 宠物ID变化");
                return false;
            }

            var state = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
            _context.PetState = state;
            _previousPetState = DeepCopyPetState(state);

            await LogAsync($"宠物状态: 开放 {state.OpenSlotCount}/{state.MaxSlotCount} 个技能格, 版本 {state.StateVersion}");
            return true;
        }

        private async Task<bool> CheckMaterialsAsync(CancellationToken cancellationToken)
        {
            SetState(State.CHECK_MATERIALS);
            await LogAsync("CHECK_MATERIALS: 检查执行材料");

            var inventory = await _readOnlyAdapter.ReadResourcesAsync(cancellationToken);
            _context.Inventory = inventory;

            var catalog = await _readOnlyAdapter.ReadSkillCatalogAsync(cancellationToken);
            var book = _preset.Books[_context.CurrentBookIndex];

            // 检查技能书：必须同时比对 ItemId 和 SkillId
            var bookItem = catalog.FirstOrDefault(c => c.ItemId == book.ItemId && c.SkillId == book.SkillId);
            if (bookItem == null)
            {
                Pause($"CHECK_MATERIALS: 目录中找不到匹配的技能书 (ItemId={book.ItemId}, SkillId={book.SkillId})");
                return false;
            }

            var inventoryItem = inventory.Items.FirstOrDefault(i => i.ItemId == book.ItemId);
            if (inventoryItem == null || inventoryItem.Count < 1)
            {
                Pause($"CHECK_MATERIALS: 技能书数量不足: {book.ItemId}");
                return false;
            }

            // 检查银两：仅当成本 > 0 时才检查
            if (_preset.StudySilverCost > 0 && inventory.Silver < _preset.StudySilverCost)
            {
                Pause($"CHECK_MATERIALS: 银两不足: {inventory.Silver} < {_preset.StudySilverCost}");
                return false;
            }

            await LogAsync($"材料充足: 技能书 {book.ItemId}x{inventoryItem.Count}, 银两 {inventory.Silver}");
            return true;
        }

        private async Task<bool> OpenAllSlotsAsync(CancellationToken cancellationToken)
        {
            var petState = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);

            while (petState.OpenSlotCount < petState.MaxSlotCount && !_context.IsPaused && !cancellationToken.IsCancellationRequested)
            {
                SetState(State.OPEN_SLOT_SUBMIT);
                await LogAsync("OPEN_SLOT_SUBMIT: 提交开启技能格");

                // 检查开格材料
                if (!_preset.OpenItemId.HasValue)
                {
                    Pause("OPEN_SLOT_SUBMIT: 未配置开格材料物品 ID");
                    return false;
                }

                var inventory = await _readOnlyAdapter.ReadResourcesAsync(cancellationToken);
                var openMaterial = inventory.Items.FirstOrDefault(item => item.ItemId == _preset.OpenItemId.Value);
                if (openMaterial == null || openMaterial.Count < 1)
                {
                    Pause($"OPEN_SLOT_SUBMIT: 开格材料不足: ItemId={_preset.OpenItemId.Value}");
                    return false;
                }

                // 检查银两：仅当 OpenSlotSilverCost > 0 时才检查
                if (_preset.OpenSlotSilverCost > 0 && inventory.Silver < _preset.OpenSlotSilverCost)
                {
                    Pause($"OPEN_SLOT_SUBMIT: 银两不足: {inventory.Silver} < {_preset.OpenSlotSilverCost}");
                    return false;
                }

                // 提交开启操作
                var result = await _operationAdapter.SubmitOpenSlotAsync(petState.OpenSlotCount, cancellationToken);
                await LogAsync($"OPEN_SLOT_SUBMIT: 开启操作结果 = {result}");

                if (result != OperationResult.Accepted)
                {
                    Pause($"OPEN_SLOT_SUBMIT: 开启操作失败: {result}");
                    return false;
                }

                SetState(State.OPEN_SLOT_WAIT);

                // 等待刷新
                bool refreshOk = await _readOnlyAdapter.WaitForStateRefreshAsync(
                    petState.StateVersion, _preset.TimeoutMs, cancellationToken);
                if (!refreshOk)
                {
                    Pause("OPEN_SLOT_WAIT: 状态刷新超时");
                    return false;
                }

                // 检查暂停状态
                if (_context.IsPaused || cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                SetState(State.OPEN_SLOT_VERIFY);

                // 重新读状态并验证开放格数量严格增加
                var newPetState = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
                if (newPetState.OpenSlotCount <= petState.OpenSlotCount)
                {
                    Pause("OPEN_SLOT_VERIFY: 技能格数量未增加");
                    return false;
                }

                petState = newPetState;
                await LogAsync($"OPEN_SLOT_VERIFY: 槽位打开成功, 现有 {petState.OpenSlotCount}/{petState.MaxSlotCount}");
            }

            // 检查循环退出原因：暂停或取消
            if (_context.IsPaused || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            return true;
        }

        private PetSkillSlotSnapshot GetNextAvailableSlot()
        {
            if (_context.PetState?.SkillSlots == null) return null;
            return _context.PetState.SkillSlots.FirstOrDefault(s => s.IsOpen && !s.IsLocked && s.SkillId == 0);
        }

        private async Task<bool> BookCheckAsync(CancellationToken cancellationToken)
        {
            SetState(State.BOOK_CHECK);
            await LogAsync("BOOK_CHECK: 检查当前技能书");

            var pet = await _readOnlyAdapter.ReadCurrentPetAsync(cancellationToken);
            if (pet.PetId != _context.CurrentPetId)
            {
                Pause("BOOK_CHECK: 宠物ID变化");
                return false;
            }

            var state = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
            _context.PetState = state;
            // 保存学习前 PetState 深拷贝快照，供 SKILL_DIFF 对比
            _previousPetState = DeepCopyPetState(state);

            await LogAsync($"BOOK_CHECK: 当前宠物 ID: {pet.PetId}, 状态版本: {state.StateVersion}");
            return true;
        }

        private async Task<bool> StudyBookAsync(SkillBookEntry book, PetSkillSlotSnapshot slot, CancellationToken cancellationToken)
        {
            SetState(State.STUDY_SUBMIT);
            await LogAsync($"STUDY_SUBMIT: 使用技能书 {book.ItemId}->{book.SkillId} 在槽位 {slot.SlotIndex}");

            var result = await _operationAdapter.SubmitStudyBookAsync(
                book.ItemId, book.SkillId, slot.SlotIndex, cancellationToken);
            await LogAsync($"STUDY_SUBMIT: 使用技能书结果 = {result}");

            if (result != OperationResult.Accepted)
            {
                Pause($"STUDY_SUBMIT: 使用技能书失败: {result}");
                return false;
            }

            SetState(State.STUDY_WAIT);

            // 等待刷新成功
            bool refreshOk = await _readOnlyAdapter.WaitForStateRefreshAsync(
                _context.PetState.StateVersion, _preset.TimeoutMs, cancellationToken);
            if (!refreshOk)
            {
                Pause("STUDY_WAIT: 状态刷新超时");
                return false;
            }

            // 重新读新快照（不覆盖 _previousPetState，保持学习前的快照供 SKILL_DIFF 用）
            var newPetState = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
            _context.PetState = newPetState;

            await LogAsync($"STUDY_WAIT: 状态刷新完成, 版本 {newPetState.StateVersion}");
            return true;
        }

        private async Task<bool> VerifySkillDiffAsync(SkillBookEntry book, CancellationToken cancellationToken)
        {
            SetState(State.SKILL_DIFF);
            await LogAsync("SKILL_DIFF: 识别变化技能格");

            var expectedSkillId = book.SkillId;

            // 读取新状态
            var newState = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);

            // 检查宠物 ID 未变化
            if (newState.PetId != _context.CurrentPetId)
            {
                Pause("SKILL_DIFF: 宠物ID变化");
                return false;
            }

            // 分析变化：恰好识别一个变化槽位
            int slotChanges = 0;
            int changedSlot = -1;

            foreach (var currentSlot in newState.SkillSlots)
            {
                var prevSlot = _previousPetState.SkillSlots.FirstOrDefault(s => s.SlotIndex == currentSlot.SlotIndex);

                if (prevSlot == null) continue;

                // 变化：学习前未锁定，学习后 SkillId 等于预设 SkillId，槽位存在且开放
                bool wasNotLocked = !prevSlot.IsLocked;
                bool isNowOpen = currentSlot.IsOpen;
                bool skillMatches = currentSlot.SkillId == expectedSkillId;
                bool skillChanged = prevSlot.SkillId != currentSlot.SkillId || wasNotLocked != currentSlot.IsLocked;

                if (skillChanged && isNowOpen && wasNotLocked && skillMatches)
                {
                    slotChanges++;
                    changedSlot = currentSlot.SlotIndex;
                }
            }

            if (slotChanges != 1)
            {
                Pause($"SKILL_DIFF: 想变的槽位数量不正确: {slotChanges} (期望为 1)");
                return false;
            }

            _changedSlotIndex = changedSlot;
            await LogAsync($"SKILL_DIFF: 变化槽位 {changedSlot}, SkillId: {expectedSkillId}");
            return true;
        }

        private async Task<bool> LockSlotAsync(int slotIndex, CancellationToken cancellationToken)
        {
            SetState(State.LOCK_SUBMIT);
            await LogAsync($"LOCK_SUBMIT: 锁定技能格 {slotIndex}");

            var result = await _operationAdapter.SubmitLockSkillSlotAsync(slotIndex, cancellationToken);
            await LogAsync($"LOCK_SUBMIT: 锁定结果 = {result}");

            if (result != OperationResult.Accepted)
            {
                Pause($"LOCK_SUBMIT: 锁定失败: {result}");
                return false;
            }

            SetState(State.LOCK_WAIT);

            // 等待刷新
            bool refreshOk = await _readOnlyAdapter.WaitForStateRefreshAsync(
                _context.PetState.StateVersion, _preset.TimeoutMs, cancellationToken);
            if (!refreshOk)
            {
                Pause("LOCK_WAIT: 状态刷新超时");
                return false;
            }

            SetState(State.LOCK_VERIFY);

            // 重新读状态并确认目标槽位 IsLocked==true
            var newPetState = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
            var slot = newPetState.SkillSlots.FirstOrDefault(s => s.SlotIndex == slotIndex);

            if (slot == null || !slot.IsLocked)
            {
                Pause("LOCK_VERIFY: 锁定未生效");
                return false;
            }

            _context.PetState = newPetState;
            _previousPetState = DeepCopyPetState(newPetState);

            await LogAsync($"LOCK_VERIFY: 锁定成功");
            return true;
        }

        private PetStateSnapshot DeepCopyPetState(PetStateSnapshot source)
        {
            if (source == null) return null;
            return new PetStateSnapshot
            {
                PetId = source.PetId,
                StateVersion = source.StateVersion,
                OpenSlotCount = source.OpenSlotCount,
                MaxSlotCount = source.MaxSlotCount,
                SkillSlots = source.SkillSlots?.Select(s => new PetSkillSlotSnapshot
                {
                    SlotIndex = s.SlotIndex,
                    IsOpen = s.IsOpen,
                    IsLocked = s.IsLocked,
                    SkillId = s.SkillId
                }).ToList(),
                ReadAt = source.ReadAt
            };
        }

        private void SetState(State newState)
        {
            _currentState = newState;
            OnStateChanged?.Invoke(newState);
        }

        private async Task LogAsync(string message)
        {
            await Task.Yield();
            var entry = new StepLogEntry
            {
                State = _currentState.ToString(),
                Timestamp = DateTime.UtcNow,
                Message = message
            };
            _context.LogEntries.Add(entry);
            Log(message);
        }

        private void Log(string message)
        {
            OnLog?.Invoke(_currentState.ToString(), message);
        }
    }
}
