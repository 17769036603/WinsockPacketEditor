using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.Memory;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 测试场景共享可变状态。
    /// ScriptableTestReadOnlyAdapter 和外部测试 Harness 共享此实例，
    /// 使测试代码能正确修改读适配器返回的快照数据。
    /// 设计为 public 以便外部测试程序集访问。
    /// </summary>
    public class ScriptableMountSpeedState
    {
        // 核心状态
        public double BaseSpeed { get; set; } = 200;
        public double RideAddSpeed { get; set; } = 0;
        public bool IsMounted { get; set; } = false;
        public bool SimulateRefreshTimeout { get; set; } = false;
        public OperationResult LastSetRideAddSpeedResult { get; set; } = OperationResult.Accepted;
        public OperationResult LastToggleMountResult { get; set; } = OperationResult.Accepted;

        public ScriptableMountSpeedState(double initialBaseSpeed = 200)
        {
            BaseSpeed = initialBaseSpeed;
        }
    }

    /// <summary>
    /// 可配置的测试读取适配器，提供确定性、可脚本化的测试场景。
    /// </summary>
    public class ScriptableTestReadOnlyAdapter : IMountSpeedReadOnlyAdapter
    {
        private readonly ScriptableMountSpeedState _state;

        public ScriptableTestReadOnlyAdapter(ScriptableMountSpeedState sharedState = null)
        {
            _state = sharedState ?? new ScriptableMountSpeedState();
        }

        // 场景配置方法

        public void SetBaseSpeed(double baseSpeed)
        {
            _state.BaseSpeed = baseSpeed;
        }

        public void SetRideAddSpeed(double rideAddSpeed)
        {
            _state.RideAddSpeed = rideAddSpeed;
        }

        public void SetIsMounted(bool isMounted)
        {
            _state.IsMounted = isMounted;
        }

        public void EnableRefreshTimeout(bool enable) => _state.SimulateRefreshTimeout = enable;

        public Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken)
        {
            ReadOnlyProcessIdentity capturedIdentity = null;
            ReadOnlyProcessIdentity.TryCapture(Process.GetCurrentProcess().Id, out capturedIdentity, out _);
            return Task.FromResult(true);
        }

        public Task<MountSpeedSnapshot> ReadMountSpeedAsync(CancellationToken cancellationToken)
        {
            double roleMoveSpeed = CalculateMountSpeedModel.CalculateRoleMoveSpeed(
                _state.BaseSpeed, _state.RideAddSpeed);

            return Task.FromResult(new MountSpeedSnapshot
            {
                BaseSpeed = _state.BaseSpeed,
                RideAddSpeed = _state.RideAddSpeed,
                RoleMoveSpeed = roleMoveSpeed,
                IsMounted = _state.IsMounted,
                ReadAt = DateTime.UtcNow
            });
        }

        // 供测试代码检查状态
        internal ScriptableMountSpeedState InternalState => _state;
    }

    /// <summary>
    /// 可配置的测试写适配器，模拟坐骑速度修改操作。
    /// </summary>
    public class ScriptableTestOperationAdapter : IMountSpeedOperationAdapter
    {
        private readonly ScriptableMountSpeedState _state;

        public ScriptableTestOperationAdapter(ScriptableMountSpeedState sharedState = null)
        {
            _state = sharedState ?? new ScriptableMountSpeedState();
        }

        public Task<OperationResult> SetRideAddSpeedAsync(double rideAddSpeed, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(OperationResult.Rejected);
            }

            // 模拟设置速度
            _state.RideAddSpeed = rideAddSpeed;
            
            var result = _state.LastSetRideAddSpeedResult;
            return Task.FromResult(result);
        }

        public Task<OperationResult> ToggleMountAsync(bool mount, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(OperationResult.Rejected);
            }

            // 模拟切换坐骑状态
            _state.IsMounted = mount;
            
            var result = _state.LastToggleMountResult;
            return Task.FromResult(result);
        }
    }
}