using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 五行升级状态机
    /// 顺序发送：木×N → 水×N → 土×N → 金×N → 火×N → 整体升×M
    /// </summary>
    public class WuxingUpgradeRunner
    {
        private readonly WuxingUpgradePreset _preset;
        private readonly WuxingUpgradeContext _context;
        private readonly IWuxingUpgradeActionAdapter _actionAdapter;
        private bool _isRunning;
        private bool _isPaused;

        public event Action<string> OnLog;
        public event Action<State> OnStateChanged;
        public event Action<string> OnPaused;

        public bool IsRunning => _isRunning;
        public bool IsPaused => _isPaused;
        public int TotalOperations => _context.CurrentRound?.CurrentOperationCount ?? 0;
        public int CurrentRound => _context.CurrentRound?.RoundNumber ?? 0;
        public State CurrentState { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public enum State
        {
            IDLE,
            SENDED_WOOD,
            SENDED_WATER,
            SENDED_SOIL,
            SENDED_GOLD,
            SENDED_FIRE,
            SENDED_OVERALL,
            WAIT_ROUND_INTERVAL,
            PAUSED,
            COMPLETE
        }

        public WuxingUpgradeRunner(
            WuxingUpgradePreset preset,
            WuxingUpgradeContext context,
            IWuxingUpgradeActionAdapter actionAdapter)
        {
            _preset = preset ?? throw new ArgumentNullException(nameof(preset));
            _context = context ?? new WuxingUpgradeContext();
            _actionAdapter = actionAdapter ?? throw new ArgumentNullException(nameof(actionAdapter));
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Runner 已在运行中。");

            _isRunning = true;
            _isPaused = false;
            _context.Preset = _preset;
            _context.TotalRounds = 0;
            _context.CompletedRounds = 0;
            _context.CurrentRound = new WuxingUpgradeRoundContext();
            _context.IsPaused = false;
            _context.PauseReason = string.Empty;
            LastError = string.Empty;

            try
            {
                await LogAsync("五行升级开始执行");

                while (_isRunning)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    while (_isPaused && !cancellationToken.IsCancellationRequested)
                        await Task.Delay(100, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    SetState(State.IDLE);
                    _context.CurrentRound.RoundNumber = _context.CompletedRounds + 1;
                    _context.CurrentRound.CurrentElementIndex = 0;
                    _context.CurrentRound.CurrentOperationCount = 0;
                    _context.CurrentRound.RoundStartTime = DateTime.Now;

                    await LogAsync($"=== 开始第 {_context.CurrentRound.RoundNumber} 轮 ===");

                    bool success = await ExecuteRoundAsync(cancellationToken);
                    if (!success)
                    {
                        if (_context.IsPaused) continue;
                        break;
                    }

                    _context.CompletedRounds++;
                    _context.TotalRounds++;

                    SetState(State.WAIT_ROUND_INTERVAL);
                    if (_preset.WaitForRoundInterval && _preset.RoundIntervalMs > 0 && !cancellationToken.IsCancellationRequested)
                    {
                        await LogAsync($"WAIT_ROUND_INTERVAL: 等待 {_preset.RoundIntervalMs}ms 轮次间隔");
                        await Task.Delay(_preset.RoundIntervalMs, cancellationToken);
                    }

                    if (_preset.MaxRounds > 0 && _context.CompletedRounds >= _preset.MaxRounds)
                    {
                        await LogAsync($"已达到最大轮数 {_preset.MaxRounds}，停止执行。");
                        break;
                    }
                }

                if (_context.IsPaused || cancellationToken.IsCancellationRequested)
                    return;

                SetState(State.COMPLETE);
                await LogAsync($"完成预设执行：完成 {_context.CompletedRounds} 轮，总计 {_context.CurrentRound.CurrentOperationCount} 次操作");
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

        private async Task<bool> ExecuteRoundAsync(CancellationToken cancellationToken)
        {
            _context.CurrentRound.ElementSteps.Clear();
            
            // 添加五行元素步骤，每个元素重复 ElementUpgradeCount 次
            for (int i = 0; i < _preset.ElementUpgradeCount; i++)
            {
                _context.CurrentRound.ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Wood, 1));
                _context.CurrentRound.ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Water, 1));
                _context.CurrentRound.ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Soil, 1));
                _context.CurrentRound.ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Gold, 1));
                _context.CurrentRound.ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Fire, 1));
            }
            
            // 添加整体升级步骤，重复 OverallUpgradeCount 次
            for (int i = 0; i < _preset.OverallUpgradeCount; i++)
            {
                _context.CurrentRound.ElementSteps.Add(new WuxingUpgradeStepConfig(ElementalType.Overall, 1));
            }

            while (_context.CurrentRound.CurrentElementIndex < _context.CurrentRound.ElementSteps.Count)
            {
                if (cancellationToken.IsCancellationRequested || _context.IsPaused)
                    return false;

                var step = _context.CurrentRound.ElementSteps[_context.CurrentRound.CurrentElementIndex];
                
                if (!await SendUpgradeAsync(step.ElementType, GetCardIdForElement(step.ElementType), cancellationToken))
                {
                    HandleFailure($"元素 {step.ElementType} 升级失败");
                    return false;
                }

                _context.CurrentRound.CurrentElementIndex++;
            }

            return true;
        }

        private int GetCardIdForElement(ElementalType elementType)
        {
            switch (elementType)
            {
                case ElementalType.Wood: return _preset.WoodCardId;
                case ElementalType.Water: return _preset.WaterCardId;
                case ElementalType.Soil: return _preset.SoilCardId;
                case ElementalType.Gold: return _preset.GoldCardId;
                case ElementalType.Fire: return _preset.FireCardId;
                case ElementalType.Overall: return _preset.OverallUpgradeCardId;
                default: return 0;
            }
        }

        private async Task<bool> SendElementUpgradeAsync(ElementalType elementType, int cardId, CancellationToken cancellationToken)
        {
            SetState(GetStateForElement(elementType));
            await LogAsync($"{GetStateNameForElement(elementType)}: 发送 {elementType} 元素升级请求");

            var operation = new WuxingUpgradeOperation
            {
                ElementType = elementType,
                CardTypeId = (CsCardTypeId)elementType,
                CardId = cardId,
                OperaType = (int)elementType,
                IsOverallUpgrade = false
            };

            var result = await _actionAdapter.SubmitElementUpgradeAsync(operation, cancellationToken);
            await LogAsync($"SEND_{elementType}: 元素升级结果 = {result}");

            if (result != WuxingUpgradeResult.Accepted)
                return false;

            _context.CurrentRound.CurrentOperationCount++;

            if (_preset.WaitForRequestInterval && !cancellationToken.IsCancellationRequested)
                await Task.Delay(_preset.RequestIntervalMs, cancellationToken);

            return true;
        }

        private async Task<bool> SendOverallUpgradeAsync(int cardId, CancellationToken cancellationToken)
        {
            SetState(State.SENDED_OVERALL);
            await LogAsync("SEND_OVERALL: 发送整体升级请求");

            var operation = new WuxingUpgradeOperation
            {
                ElementType = ElementalType.Overall,
                CardTypeId = CsCardTypeId.Overall,
                CardId = cardId,
                OperaType = (int)CsCardTypeId.Overall,
                IsOverallUpgrade = true
            };

            var result = await _actionAdapter.SubmitElementUpgradeAsync(operation, cancellationToken);
            await LogAsync($"SEND_OVERALL: 整体升级结果 = {result}");

            if (result != WuxingUpgradeResult.Accepted)
                return false;

            _context.CurrentRound.CurrentOperationCount++;

            if (_preset.WaitForRequestInterval && !cancellationToken.IsCancellationRequested)
                await Task.Delay(_preset.RequestIntervalMs, cancellationToken);

            return true;
        }

        private async Task<bool> SendUpgradeAsync(ElementalType elementType, int cardId, CancellationToken cancellationToken)
        {
            if (elementType == ElementalType.Overall)
                return await SendOverallUpgradeAsync(cardId, cancellationToken);
            else
                return await SendElementUpgradeAsync(elementType, cardId, cancellationToken);
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

        private State GetStateForElement(ElementalType elementType)
        {
            switch (elementType)
            {
                case ElementalType.Wood: return State.SENDED_WOOD;
                case ElementalType.Water: return State.SENDED_WATER;
                case ElementalType.Soil: return State.SENDED_SOIL;
                case ElementalType.Gold: return State.SENDED_GOLD;
                case ElementalType.Fire: return State.SENDED_FIRE;
                default: return State.IDLE;
            }
        }

        private string GetStateNameForElement(ElementalType elementType)
        {
            switch (elementType)
            {
                case ElementalType.Wood: return "SEND_WOOD";
                case ElementalType.Water: return "SEND_WATER";
                case ElementalType.Soil: return "SEND_SOIL";
                case ElementalType.Gold: return "SEND_GOLD";
                case ElementalType.Fire: return "SEND_FIRE";
                default: return "IDLE";
            }
        }

        private async Task LogAsync(string message)
        {
            var logEntry = new WuxingUpgradeStepLog
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
