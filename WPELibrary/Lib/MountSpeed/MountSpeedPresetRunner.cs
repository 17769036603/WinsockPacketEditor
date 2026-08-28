using System;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑速度预设状态机 runner。
    /// 使用依赖注入的适配器，在没有真实游戏进程时使用 TestAdapters 进行离线测试。
    /// 支持读取验证和修改坐骑速度。
    /// </summary>
    public class MountSpeedPresetRunner
    {
        public enum State
        {
            IDLE,
            VERIFY_PROCESS,
            READ_BASE_SPEED,
            READ_RIDE_ADD_SPEED,
            READ_ROLE_MOVE_SPEED,
            VERIFY_CALCULATION,
            MOUNT_TOGGLE_CHECK,
            APPLY_MOUNT_SPEED,
            SPEED_STABILITY_CHECK,
            LOG_RESULT,
            COMPLETE
        }

        public event Action<string, string> OnLog;
        public event Action<State> OnStateChanged;
        public event Action<bool> OnPaused;

        private readonly MountSpeedPreset _preset;
        private readonly IMountSpeedReadOnlyAdapter _readOnlyAdapter;
        private readonly IMountSpeedOperationAdapter _operationAdapter;
        private readonly CancellationTokenSource _cts;
        private readonly MountSpeedPresetContext _context;

        private State _currentState = State.IDLE;

        public State CurrentState => _currentState;
        public MountSpeedPresetContext Context => _context;
        public bool IsPaused => _context.IsPaused;
        public bool IsCancelled => _cts?.IsCancellationRequested ?? true;

        public MountSpeedPresetRunner(
            MountSpeedPreset preset,
            IMountSpeedReadOnlyAdapter readOnlyAdapter,
            IMountSpeedOperationAdapter operationAdapter = null)
        {
            _preset = preset ?? throw new ArgumentNullException(nameof(preset));
            _readOnlyAdapter = readOnlyAdapter ?? throw new ArgumentNullException(nameof(readOnlyAdapter));
            _operationAdapter = operationAdapter;
            _cts = new CancellationTokenSource();
            _context = new MountSpeedPresetContext { Preset = preset };
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
            await LogAsync("开始执行坐骑速度预设");

            // 1. 开始执行时调用 VerifyProcessIdentityAsync
            if (!await _readOnlyAdapter.VerifyProcessIdentityAsync(_cts.Token))
            {
                Pause("进程身份验证失败：无法验证坐骑速度监控进程身份");
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
            // VERIFY_PROCESS 状态
            SetState(State.VERIFY_PROCESS);
            await LogAsync("VERIFY_PROCESS: 验证进程身份已通过");

            // READ_BASE_SPEED 状态
            SetState(State.READ_BASE_SPEED);
            await LogAsync($"READ_BASE_SPEED: 读取基础速度 {_preset.BaseSpeed}");
            _context.ReadBaseSpeed = _preset.BaseSpeed;

            // READ_RIDE_ADD_SPEED 状态
            SetState(State.READ_RIDE_ADD_SPEED);
            await LogAsync("READ_RIDE_ADD_SPEED: 读取坐骑速度加成");

            // 读取当前坐骑速度快照
            var snapshot = await _readOnlyAdapter.ReadMountSpeedAsync(cancellationToken);
            _context.ReadRideAddSpeed = snapshot.RideAddSpeed;
            _context.ReadRoleMoveSpeed = snapshot.RoleMoveSpeed;
            _context.IsMounted = snapshot.IsMounted;

            await LogAsync($"读取到: {snapshot}");

            // READ_ROLE_MOVE_SPEED 状态
            SetState(State.READ_ROLE_MOVE_SPEED);
            await LogAsync($"READ_ROLE_MOVE_SPEED: 角色移动速度 {_context.ReadRoleMoveSpeed}");

            // VERIFY_CALCULATION 状态
            SetState(State.VERIFY_CALCULATION);
            var calculatedSpeed = CalculateMountSpeedModel.CalculateRoleMoveSpeed(
                _preset.BaseSpeed, _preset.ExpectedRideAddSpeed);
            _context.CalculatedRoleMoveSpeed = calculatedSpeed;

            // 验证计算是否匹配
            bool calculationValid = Math.Abs(_context.ReadRoleMoveSpeed - calculatedSpeed) < _context.BaseSpeedTolerance;
            if (!calculationValid && _context.ReadRideAddSpeed != _preset.ExpectedRideAddSpeed)
            {
                await LogAsync($"VERIFY_CALCULATION: 计算不匹配 - 期望 {_preset.ExpectedRideAddSpeed}%, 实际 {_context.ReadRideAddSpeed}%");
            }
            else
            {
                await LogAsync($"VERIFY_CALCULATION: 计算匹配 - rideAddSpeed={_preset.ExpectedRideAddSpeed}%");
            }

            // MOUNT_TOGGLE_CHECK 状态
            SetState(State.MOUNT_TOGGLE_CHECK);
            if (_preset.ApplyMountSpeed && _preset.ExpectedRideAddSpeed > 0 && !snapshot.IsMounted)
            {
                // 需要下马后再设定速度 - 需要操作适配器
                if (_operationAdapter == null)
                {
                    Pause("MOUNT_TOGGLE_CHECK: 未乘坐但需要应用坐骑速度，操作适配器未配置。");
                    return;
                }
                
                await LogAsync("MOUNT_TOGGLE_CHECK: 未乘坐，需要先乘坐");
                
                // 先乘坐坐骑
                var result = await _operationAdapter.ToggleMountAsync(true, cancellationToken);
                if (result != OperationResult.Accepted)
                {
                    Pause($"MOUNT_TOGGLE_CHECK: 乘坐失败: {result}");
                    return;
                }
                await LogAsync("MOUNT_TOGGLE_CHECK: 乘坐成功");
                
                // 重新读取状态
                snapshot = await _readOnlyAdapter.ReadMountSpeedAsync(cancellationToken);
                if (!snapshot.IsMounted)
                {
                    Pause("MOUNT_TOGGLE_CHECK: 乘坐后仍未检测到坐骑状态");
                    return;
                }
            }
            
            if (!_preset.ApplyMountSpeed && _preset.ExpectedRideAddSpeed == 0 && snapshot.IsMounted)
            {
                Pause("MOUNT_TOGGLE_CHECK: 已乘坐但期望普通速度");
                return;
            }

            // APPLY_MOUNT_SPEED 状态
            SetState(State.APPLY_MOUNT_SPEED);
            if (_preset.ApplyMountSpeed && _operationAdapter != null)
            {
                await LogAsync($"APPLY_MOUNT_SPEED: 设置坐骑速度加成为 {_preset.ExpectedRideAddSpeed}%");
                
                var result = await _operationAdapter.SetRideAddSpeedAsync(
                    _preset.ExpectedRideAddSpeed, cancellationToken);
                
                if (result != OperationResult.Accepted)
                {
                    Pause($"APPLY_MOUNT_SPEED: 设置失败: {result}");
                    return;
                }
                await LogAsync("APPLY_MOUNT_SPEED: 坐骑速度设置成功");
                
                // 重新读取验证
                snapshot = await _readOnlyAdapter.ReadMountSpeedAsync(cancellationToken);
                _context.ReadRideAddSpeed = snapshot.RideAddSpeed;
                _context.ReadRoleMoveSpeed = snapshot.RoleMoveSpeed;
            }
            else
            {
                await LogAsync("APPLY_MOUNT_SPEED: 跳过速度设置（未启用或无操作适配器）");
            }

            // SPEED_STABILITY_CHECK 状态
            SetState(State.SPEED_STABILITY_CHECK);
            if (_context.ReadBaseSpeed <= 0)
            {
                Pause("SPEED_STABILITY_CHECK: 基础速度无效");
                return;
            }

            // LOG_RESULT 状态
            SetState(State.LOG_RESULT);
            await LogAsync($"LOG_RESULT: 完成速度读取 - 基础速度={_context.ReadBaseSpeed}, 坐骑加成={_context.ReadRideAddSpeed}%, 角色速度={_context.ReadRoleMoveSpeed}");

            // COMPLETE 状态
            SetState(State.COMPLETE);
            await LogAsync("完成预设执行");
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