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

            // 当前流程规则固定：启动时一次性开满技能格，每本成功后立即锁定。
            _preset.OpenAllSlots = true;
            _preset.LockAfter = true;
            if (_preset.Books != null)
            {
                foreach (var book in _preset.Books)
                {
                    if (book != null)
                    {
                        book.LockAfter = true;
                    }
                }
            }
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
                await FailRunAsync($"预设无效: {error}");
                return;
            }

            SetState(State.IDLE);
            await LogAsync("开始执行召唤兽技能预设");

            // 1. 开始执行时调用 VerifyProcessIdentityAsync
            if (!await _readOnlyAdapter.VerifyProcessIdentityAsync(_cts.Token))
            {
                await FailRunAsync("进程身份验证失败：无法验证宠物技能书操作进程身份");
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
                _context.RunStatus.IsCancelled = true;
            }
            catch (Exception ex)
            {
                await LogAsync($"执行错误: {ex.Message}");
                await FailRunAsync($"执行错误: {ex.Message}");
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
            _context.RunStatus.IsCancelled = true;
        }

        private async Task ExecuteStateMachineAsync(CancellationToken cancellationToken)
        {
            // IDLE 状态
            SetState(State.IDLE);

            // 启动阶段：验证目标进程身份 → 读取当前参战召唤兽 → 读取技能格状态
            if (!await VerifyCurrentPetAsync(cancellationToken))
            {
                if (_context.IsPaused) return;
                return;
            }

            if (!await LoadPetStateAsync(cancellationToken))
            {
                if (_context.IsPaused) return;
                return;
            }

            // 一次性开格：OpenAllSlotsAsync 移到技能书循环之前
            bool slotsOpened = true;
            if (_preset.OpenAllSlots)
            {
                // 如果技能格已经全部开放，跳过开格
                if (_context.PetState.OpenSlotCount >= _context.PetState.MaxSlotCount)
                {
                    await LogAsync("OPEN_SLOT_VERIFY: 技能格已全部开放，跳过开格");
                }
                else
                {
                    slotsOpened = await OpenAllSlotsAsync(cancellationToken);
                }
            }

            if (!slotsOpened)
            {
                if (_context.IsPaused) return;
                return;
            }

            // 主循环：处理每本技能书。单本失败记录后继续，只有全局失败才终止。
            while (_context.CurrentBookIndex < _preset.Books.Count &&
                   !_context.IsPaused &&
                   !_context.RunStatus.IsFailed &&
                   !cancellationToken.IsCancellationRequested)
            {
                var result = await ProcessSingleBookAsync(cancellationToken);

                // 记录每本技能书的结果
                _context.RunStatus.BookResults.Add(result);
                _context.CurrentBookIndex++;

                // 下一本之前自动刷新并确认召唤兽仍是同一个对象。
                if (_context.CurrentBookIndex < _preset.Books.Count)
                {
                    SetState(State.NEXT_BOOK);
                    if (!await VerifyPetIdConsistencyBeforeNextBookAsync(cancellationToken))
                    {
                        return;
                    }
                }
            }

            // 循环结束后的最终保护
            if (_context.IsPaused ||
                _context.RunStatus.IsFailed ||
                cancellationToken.IsCancellationRequested)
            {
                return; // 已暂停、失败或取消，不进入 COMPLETE
            }

            // 决定最终结果：根据所有 BookExecutionResult 决定 SUCCESS 或 FAILED
            int failedCount = _context.RunStatus.BookResults.Count(r => r.Status == BookExecutionStatus.FAILED);
            if (failedCount == 0)
            {
                _context.RunStatus.IsCompleted = true;
                _context.RunStatus.CompletedAt = DateTime.UtcNow;
                SetState(State.COMPLETE);
                await LogAsync("完成预设执行: SUCCESS");
            }
            else
            {
                var failureDetails = string.Join(
                    "；",
                    _context.RunStatus.BookResults
                        .Where(r => r.Status == BookExecutionStatus.FAILED)
                        .Select(r => $"第{r.Sequence}本: {r.FailureReason}"));
                await FailRunAsync($"部分技能书打书失败: {failedCount} 个失败，{_context.RunStatus.BookResults.Count} 本总计；原因: {failureDetails}");
            }
        }

        private async Task<BookExecutionResult> ProcessSingleBookAsync(CancellationToken cancellationToken)
        {
            var book = _preset.Books[_context.CurrentBookIndex];
            SetState(State.BOOK_CHECK);

            // 每本开始前自动验证召唤兽 ID
            if (!await VerifyPetIdAsync(cancellationToken))
            {
                return CreateFailedResult(book, "召唤兽 ID 不一致");
            }

            // 读取当前宠物状态
            var pet = await _readOnlyAdapter.ReadCurrentPetAsync(cancellationToken);
            if (pet.PetId != _context.CurrentPetId)
            {
                return CreateFailedResult(book, "召唤兽 ID 变化");
            }

            var state = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
            _context.PetState = state;

            // 目标技能已经存在时无论槽位是否锁定都跳过，不再尝试覆盖。
            var existingSlot = state.SkillSlots?.FirstOrDefault(s => s.SkillId == book.SkillId);
            if (existingSlot != null)
            {
                await LogAsync($"BOOK_CHECK: 技能 {book.SkillId} 已存在于槽位 {existingSlot.SlotIndex}，跳过");
                return CreateSkippedResult(book, existingSlot.SlotIndex);
            }

            // 寻找空、开放、未锁定的技能格
            var availableSlot = GetNextAvailableSlot();
            if (availableSlot == null)
            {
                await LogAsync("没有可用空技能格");
                return CreateFailedResult(book, "没有可用空技能格");
            }

            // 保存打书前技能格快照（用于 VerifySkillDiffAsync 对比）
            var stateBeforeStudy = DeepCopyPetState(state);

            // 提交使用技能书
            var studyResult = await SubmitStudyBookAsync(book, availableSlot.SlotIndex, cancellationToken);
            if (studyResult.Status != BookExecutionStatus.SUCCESS)
            {
                return studyResult;
            }

            // 等待刷新并读取新状态
            bool refreshOk = await _readOnlyAdapter.WaitForStateRefreshAsync(
                state.StateVersion, _preset.TimeoutMs, cancellationToken);
            if (!refreshOk)
            {
                return CreateFailedResult(book, "状态刷新超时");
            }

            var newPetState = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
            _context.PetState = newPetState;

            // 验证打书成功条件
            if (!await VerifySkillDiffAsync(book, availableSlot.SlotIndex, stateBeforeStudy, newPetState, cancellationToken))
            {
                return CreateFailedResult(book, "技能格变化验证失败（可能存在覆盖、非目标技能格变化或目标技能未出现）");
            }

            // 立即锁定目标技能格（LockAfter 运行时固定为 true）
            var lockResult = await LockSlotAsync(book, availableSlot.SlotIndex, cancellationToken);
            if (lockResult.Status != BookExecutionStatus.SUCCESS)
            {
                return lockResult;
            }

            return lockResult;
        }

        private async Task<bool> VerifyPetIdAsync(CancellationToken cancellationToken)
        {
            var pet = await _readOnlyAdapter.ReadCurrentPetAsync(cancellationToken);

            if (!pet.IsCurrentParticipant || pet.PetId <= 0 || pet.PetId != _context.CurrentPetId)
            {
                return false;
            }
            return true;
        }

        private async Task<bool> VerifyPetIdConsistencyBeforeNextBookAsync(CancellationToken cancellationToken)
        {
            var pet = await _readOnlyAdapter.ReadCurrentPetAsync(cancellationToken);
            if (!pet.IsCurrentParticipant || pet.PetId <= 0 || pet.PetId != _context.CurrentPetId)
            {
                await FailRunAsync($"VERIFY_PET_ID: 召唤兽 ID 变化或不在参战状态: 原 {_context.CurrentPetId}，现 {pet.PetId}");
                return false;
            }
            return true;
        }

        private async Task<bool> VerifyCurrentPetAsync(CancellationToken cancellationToken)
        {
            SetState(State.VERIFY_CURRENT_PET);
            await LogAsync("VERIFY_CURRENT_PET: 读取当前参战召唤兽");

            var pet = await _readOnlyAdapter.ReadCurrentPetAsync(cancellationToken);

            // 目标进程身份验证 + 参战状态验证
            if (!pet.IsCurrentParticipant || pet.PetId <= 0)
            {
                await FailRunAsync("当前召唤兽不在参战状态或 ID 无效");
                return false;
            }

            // 检查宠物变化：每本书开始前重新确认
            if (_snapshotBeforeRefresh != null && _snapshotBeforeRefresh.PetId != pet.PetId)
            {
                await FailRunAsync($"VERIFY_CURRENT_PET: 宠物ID变化: 当前 {pet.PetId} != 上次 {_snapshotBeforeRefresh.PetId}");
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
                await FailRunAsync("LOAD_PET_STATE: 宠物ID变化");
                return false;
            }

            var state = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
            _context.PetState = state;
            _previousPetState = DeepCopyPetState(state);

            await LogAsync($"宠物状态: 开放 {state.OpenSlotCount}/{state.MaxSlotCount} 个技能格, 版本 {state.StateVersion}");
            return true;
        }

        private async Task<bool> OpenAllSlotsAsync(CancellationToken cancellationToken)
        {
            var petState = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
            if (petState == null || petState.MaxSlotCount <= 0)
            {
                await FailRunAsync("OPEN_SLOT_VERIFY: 无法读取有效的技能格状态");
                return false;
            }

            // 一次性开满所有未开放技能格
            while (petState.OpenSlotCount < petState.MaxSlotCount && !_context.IsPaused && !cancellationToken.IsCancellationRequested)
            {
                SetState(State.OPEN_SLOT_SUBMIT);
                await LogAsync("OPEN_SLOT_SUBMIT: 提交开启技能格");

                // 提交开启操作（无材料/银两预检，直接尝试操作）
                var result = await _operationAdapter.SubmitOpenSlotAsync(petState.OpenSlotCount, cancellationToken);
                await LogAsync($"OPEN_SLOT_SUBMIT: 开启操作结果 = {result}");

                if (result != OperationResult.Accepted)
                {
                    await FailRunAsync($"OPEN_SLOT_SUBMIT: 开启操作失败: {result}");
                    return false;
                }

                SetState(State.OPEN_SLOT_WAIT);

                // 等待刷新
                bool refreshOk = await _readOnlyAdapter.WaitForStateRefreshAsync(
                    petState.StateVersion, _preset.TimeoutMs, cancellationToken);
                if (!refreshOk)
                {
                    await FailRunAsync("OPEN_SLOT_WAIT: 状态刷新超时");
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
                if (newPetState == null ||
                    newPetState.PetId != _context.CurrentPetId ||
                    newPetState.OpenSlotCount <= petState.OpenSlotCount ||
                    newPetState.OpenSlotCount > newPetState.MaxSlotCount)
                {
                    await FailRunAsync("OPEN_SLOT_VERIFY: 技能格数量未按预期增加，或召唤兽状态已变化");
                    return false;
                }

                petState = newPetState;
                _context.PetState = petState;
                await LogAsync($"OPEN_SLOT_VERIFY: 槽位打开成功, 现有 {petState.OpenSlotCount}/{petState.MaxSlotCount}");
            }

            // 检查循环退出原因：暂停或取消
            if (_context.IsPaused || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            // 检查最终是否满格
            if (petState.OpenSlotCount < petState.MaxSlotCount)
            {
                await FailRunAsync($"OPEN_SLOT_VERIFY: 最终技能格未满: {petState.OpenSlotCount}/{petState.MaxSlotCount}");
                return false;
            }

            _context.PetState = petState;
            return true;
        }

        private PetSkillSlotSnapshot GetNextAvailableSlot()
        {
            if (_context.PetState?.SkillSlots == null) return null;
            return _context.PetState.SkillSlots.FirstOrDefault(s => s.IsOpen && !s.IsLocked && s.SkillId == 0);
        }

        private BookExecutionResult CreateFailedResult(SkillBookEntry book, string reason)
        {
            return new BookExecutionResult
            {
                Status = BookExecutionStatus.FAILED,
                SkillId = book.SkillId,
                ItemId = book.ItemId,
                Sequence = _context.CurrentBookIndex + 1,
                FailureReason = reason,
                StateVersionBefore = _context.PetState?.StateVersion.ToString() ?? "0",
                StateVersionAfter = _context.PetState?.StateVersion.ToString() ?? "0",
                ExecutedAt = DateTime.UtcNow
            };
        }

        private BookExecutionResult CreateSkippedResult(SkillBookEntry book, int slotIndex)
        {
            return new BookExecutionResult
            {
                Status = BookExecutionStatus.SKIPPED_ALREADY_PRESENT,
                SkillId = book.SkillId,
                ItemId = book.ItemId,
                Sequence = _context.CurrentBookIndex + 1,
                SkillSlotIndex = slotIndex,
                StateVersionBefore = _context.PetState?.StateVersion.ToString() ?? "0",
                StateVersionAfter = _context.PetState?.StateVersion.ToString() ?? "0",
                ExecutedAt = DateTime.UtcNow
            };
        }

        private BookExecutionResult CreateSuccessResult(SkillBookEntry book, int slotIndex)
        {
            return new BookExecutionResult
            {
                Status = BookExecutionStatus.SUCCESS,
                SkillId = book.SkillId,
                ItemId = book.ItemId,
                Sequence = _context.CurrentBookIndex + 1,
                SkillSlotIndex = slotIndex,
                StateVersionBefore = _previousPetState?.StateVersion.ToString() ?? "0",
                StateVersionAfter = _context.PetState?.StateVersion.ToString() ?? "0",
                ExecutedAt = DateTime.UtcNow
            };
        }

        private async Task<BookExecutionResult> SubmitStudyBookAsync(SkillBookEntry book, int slotIndex, CancellationToken cancellationToken)
        {
            SetState(State.STUDY_SUBMIT);
            await LogAsync($"STUDY_SUBMIT: 使用技能书 {book.ItemId}->{book.SkillId} 在槽位 {slotIndex}");

            var result = await _operationAdapter.SubmitStudyBookAsync(
                book.ItemId, book.SkillId, slotIndex, cancellationToken);
            await LogAsync($"STUDY_SUBMIT: 使用技能书结果 = {result}");

            if (result != OperationResult.Accepted)
            {
                return CreateFailedResult(book, $"提交技能书失败: {result}");
            }

            return new BookExecutionResult
            {
                Status = BookExecutionStatus.SUCCESS,
                SkillId = book.SkillId,
                ItemId = book.ItemId,
                Sequence = _context.CurrentBookIndex + 1,
                SkillSlotIndex = slotIndex,
                StateVersionBefore = _context.PetState?.StateVersion.ToString() ?? "0",
                StateVersionAfter = _context.PetState?.StateVersion.ToString() ?? "0",
                ExecutedAt = DateTime.UtcNow
            };
        }

        private async Task<BookExecutionResult> LockSlotAsync(SkillBookEntry book, int slotIndex, CancellationToken cancellationToken)
        {
            SetState(State.LOCK_SUBMIT);
            await LogAsync($"LOCK_SUBMIT: 锁定技能格 {slotIndex}");

            var beforeLockState = DeepCopyPetState(_context.PetState);

            var result = await _operationAdapter.SubmitLockSkillSlotAsync(slotIndex, cancellationToken);
            await LogAsync($"LOCK_SUBMIT: 锁定结果 = {result}");

            if (result != OperationResult.Accepted)
            {
                return CreateFailedResult(book, $"锁定失败: {result}");
            }

            SetState(State.LOCK_WAIT);

            // 等待刷新
            bool refreshOk = await _readOnlyAdapter.WaitForStateRefreshAsync(
                _context.PetState.StateVersion, _preset.TimeoutMs, cancellationToken);
            if (!refreshOk)
            {
                return CreateFailedResult(book, "状态刷新超时");
            }

            SetState(State.LOCK_VERIFY);

            // 重新读状态并确认目标槽位 IsLocked==true
            var newPetState = await _readOnlyAdapter.ReadPetStateAsync(cancellationToken);
            var slot = newPetState?.SkillSlots?.FirstOrDefault(s => s.SlotIndex == slotIndex);

            if (slot == null || !slot.IsLocked)
            {
                return CreateFailedResult(book, "锁定未生效");
            }

            _context.PetState = newPetState;
            _previousPetState = DeepCopyPetState(newPetState);

            await LogAsync($"LOCK_VERIFY: 锁定成功");
            var success = CreateSuccessResult(book, slotIndex);
            success.StateVersionBefore = beforeLockState?.StateVersion.ToString() ?? "0";
            success.StateVersionAfter = newPetState.StateVersion.ToString();
            return success;
        }

        private async Task<bool> VerifySkillDiffAsync(SkillBookEntry book, int slotIndex, PetStateSnapshot prevState, PetStateSnapshot newState, CancellationToken cancellationToken)
        {
            SetState(State.SKILL_DIFF);
            await LogAsync("SKILL_DIFF: 识别变化技能格");

            var expectedSkillId = book.SkillId;

            // 检查宠物 ID、槽位集合和开放槽位总数未被替换。
            if (prevState == null || newState == null ||
                newState.PetId != _context.CurrentPetId ||
                prevState.PetId != _context.CurrentPetId ||
                prevState.OpenSlotCount != newState.OpenSlotCount ||
                prevState.MaxSlotCount != newState.MaxSlotCount ||
                prevState.SkillSlots == null || newState.SkillSlots == null ||
                prevState.SkillSlots.Count != newState.SkillSlots.Count)
            {
                await LogAsync("SKILL_DIFF: 宠物或技能格集合发生异常变化");
                return false;
            }

            // 目标槽位只能从开放、未锁定、空槽变成目标技能，不能覆盖已有技能。
            var prevTargetSlot = prevState.SkillSlots.FirstOrDefault(s => s.SlotIndex == slotIndex);
            if (prevTargetSlot == null || !prevTargetSlot.IsOpen || prevTargetSlot.IsLocked || prevTargetSlot.SkillId != 0)
            {
                await LogAsync("SKILL_DIFF: 目标槽位变化前不是开放、未锁定的空槽");
                return false;
            }

            // 检查目标槽位现在是期望的技能且开放
            var newTargetSlot = newState.SkillSlots.FirstOrDefault(s => s.SlotIndex == slotIndex);
            if (newTargetSlot == null ||
                !newTargetSlot.IsOpen ||
                newTargetSlot.IsLocked ||
                newTargetSlot.SkillId != expectedSkillId)
            {
                await LogAsync($"SKILL_DIFF: 目标槽位变化后不符合期望：期望 SkillId={expectedSkillId}, 实际 IsOpen={newTargetSlot?.IsOpen}, IsLocked={newTargetSlot?.IsLocked}, SkillId={newTargetSlot?.SkillId}");
                return false;
            }

            var previousBySlot = prevState.SkillSlots.ToDictionary(s => s.SlotIndex);
            var currentBySlot = newState.SkillSlots.ToDictionary(s => s.SlotIndex);
            if (previousBySlot.Count != prevState.SkillSlots.Count ||
                currentBySlot.Count != newState.SkillSlots.Count ||
                previousBySlot.Count != currentBySlot.Count)
            {
                await LogAsync("SKILL_DIFF: 技能格索引集合发生变化");
                return false;
            }

            int changedSlotCount = 0;
            int changedSlotIndex = -1;
            foreach (var previous in previousBySlot)
            {
                PetSkillSlotSnapshot current;
                if (!currentBySlot.TryGetValue(previous.Key, out current))
                {
                    await LogAsync($"SKILL_DIFF: 技能格 {previous.Key} 丢失");
                    return false;
                }

                var previousSlot = previous.Value;
                bool changed = previousSlot.IsOpen != current.IsOpen ||
                               previousSlot.IsLocked != current.IsLocked ||
                               previousSlot.SkillId != current.SkillId;
                if (changed)
                {
                    changedSlotCount++;
                    changedSlotIndex = previous.Key;
                    if (previous.Key != slotIndex)
                    {
                        await LogAsync($"SKILL_DIFF: 其他技能格 {previous.Key} 被覆盖或发生变化");
                        return false;
                    }
                }
            }

            if (changedSlotCount != 1 || changedSlotIndex != slotIndex)
            {
                await LogAsync($"SKILL_DIFF: 变化技能格数量不符合要求: {changedSlotCount}");
                return false;
            }

            _changedSlotIndex = changedSlotIndex;
            await LogAsync($"SKILL_DIFF: 变化槽位 {slotIndex}, SkillId: {expectedSkillId}");
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

        private async Task FailRunAsync(string reason)
        {
            if (_context.RunStatus.IsCancelled)
            {
                return;
            }

            _context.RunStatus.IsFailed = true;
            _context.RunStatus.FailureReason = reason ?? string.Empty;
            _context.RunStatus.CompletedAt = DateTime.UtcNow;
            _context.IsPaused = false;
            _context.PauseReason = string.Empty;
            await LogAsync($"执行失败: {reason}");
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
