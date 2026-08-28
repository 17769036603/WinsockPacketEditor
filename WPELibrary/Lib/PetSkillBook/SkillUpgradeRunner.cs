using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 法术升级状态机
    /// 顺序执行：任务列表中的每个 skillId → 升级到对应 targetLevel
    /// </summary>
    public class SkillUpgradeRunner
    {
        private readonly SkillUpgradePreset _preset;
        private readonly SkillUpgradeContext _context;
        private readonly ISkillUpgradeActionAdapter _actionAdapter;
        private bool _isRunning;
        private bool _isPaused;

        public event Action<string> OnLog;
        public event Action<State> OnStateChanged;
        public event Action<string> OnPaused;

        public bool IsRunning => _isRunning;
        public bool IsPaused => _isPaused;
        public int TotalTasks => _preset.Tasks?.Count ?? 0;
        public int CurrentTask => _context.CurrentTaskIndex + 1;
        public State CurrentState { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public enum State
        {
            IDLE,
            SEND_LEARN,
            WAIT_INTERVAL,
            NEXT_TASK,
            PAUSED,
            COMPLETE
        }

        public SkillUpgradeRunner(
            SkillUpgradePreset preset,
            SkillUpgradeContext context,
            ISkillUpgradeActionAdapter actionAdapter)
        {
            _preset = preset ?? throw new ArgumentNullException(nameof(preset));
            _context = context ?? new SkillUpgradeContext();
            _actionAdapter = actionAdapter ?? throw new ArgumentNullException(nameof(actionAdapter));
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Runner 已在运行中。");

            _isRunning = true;
            _isPaused = false;
            _context.Preset = _preset;
            _context.CurrentTaskIndex = 0;
            _context.CompletedTasks = 0;
            _context.IsPaused = false;
            _context.PauseReason = string.Empty;
            LastError = string.Empty;

            try
            {
                await LogAsync("法术升级开始执行");

                while (_isRunning && _context.CurrentTaskIndex < _preset.Tasks.Count)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    while (_isPaused && !cancellationToken.IsCancellationRequested)
                        await Task.Delay(100, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var task = _preset.Tasks[_context.CurrentTaskIndex];

                    SetState(State.SEND_LEARN);
                    await LogAsync($"SEND_LEARN: 发送法术升级请求（skillId={task.SkillId}, targetLevel={task.TargetLevel}）");

                    SkillUpgradeResult result = await _actionAdapter.SubmitLearnSkillAsync(
                        task.SkillId,
                        task.TargetLevel,
                        cancellationToken);

                    await LogAsync($"SEND_LEARN: 法术升级结果 = {result}");

                    if (result != SkillUpgradeResult.Accepted)
                    {
                        HandleFailure($"skillId={task.SkillId} 升级失败，结果={result}");
                        return;
                    }

                    _context.CompletedTasks++;
                    _context.CurrentTaskIndex++;

                    SetState(State.WAIT_INTERVAL);
                    if (_preset.WaitForRequestInterval && _preset.RequestIntervalMs > 0 && !cancellationToken.IsCancellationRequested)
                    {
                        await LogAsync($"WAIT_INTERVAL: 等待 {_preset.RequestIntervalMs}ms 请求间隔");
                        await Task.Delay(_preset.RequestIntervalMs, cancellationToken);
                    }

                    if (_preset.EnableLooping && _context.CurrentTaskIndex >= _preset.Tasks.Count)
                    {
                        if (_preset.MaxRounds > 0 && _context.CompletedTasks / _preset.Tasks.Count >= _preset.MaxRounds)
                        {
                            await LogAsync($"已达到最大轮数 {_preset.MaxRounds}，停止执行。");
                            break;
                        }

                        _context.CurrentTaskIndex = 0;
                        await LogAsync("进入下一轮");
                    }

                    SetState(State.NEXT_TASK);
                }

                if (_context.IsPaused || cancellationToken.IsCancellationRequested)
                    return;

                SetState(State.COMPLETE);
                await LogAsync($"完成预设执行：完成 {_context.CompletedTasks} 个任务。");
            }
            catch (Exception ex)
            {
                SetState(State.IDLE);
                await LogAsync($"执行异常: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
            }
        }

        public void Pause(string reason = "用户暂停")
        {
            if (!_isRunning) return;
            _isPaused = true;
            _context.IsPaused = true;
            _context.PauseReason = reason;
            SetState(State.PAUSED);
            OnPaused?.Invoke(reason);
            LogAsync($"已暂停: {reason}").Wait(0);
        }

        public void Resume()
        {
            if (!_isPaused) return;
            _isPaused = false;
            _context.IsPaused = false;
            _context.PauseReason = string.Empty;
            LogAsync("已恢复执行").Wait(0);
        }

        public void Stop()
        {
            _isRunning = false;
            _isPaused = false;
            _context.IsPaused = false;
            _context.PauseReason = string.Empty;
            SetState(State.IDLE);
            LogAsync("已停止执行").Wait(0);
        }

        private bool HandleFailure(string operationName)
        {
            LastError = operationName;
            Pause(operationName);
            return false;
        }

        private void SetState(State state)
        {
            CurrentState = state;
            OnStateChanged?.Invoke(state);
        }

        private async Task LogAsync(string message)
        {
            var logEntry = new SkillUpgradeStepLog
            {
                State = CurrentState.ToString(),
                Timestamp = DateTime.Now,
                Message = message
            };
            _context.LogEntries.Add(logEntry);
            OnLog?.Invoke(message);
            await Task.CompletedTask;
        }
    }
}
