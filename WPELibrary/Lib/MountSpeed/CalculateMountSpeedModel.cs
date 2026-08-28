using System;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑速度计算模型。
    /// 根据游戏客户端字节码验证得到的公式：
    /// m_RoleMoveSpeed = baseSpeed * (1 + rideAddSpeed / 100)
    /// </summary>
    public static class CalculateMountSpeedModel
    {
        /// <summary>
        /// 计算角色移动速度。
        /// </summary>
        /// <param name="baseSpeed">基础移动速度（未乘坐骑时的速度）</param>
        /// <param name="rideAddSpeed">坐骑速度加成（正数表示速度提升）</param>
        /// <returns>计算后的角色移动速度 m_RoleMoveSpeed</returns>
        public static double CalculateRoleMoveSpeed(double baseSpeed, double rideAddSpeed)
        {
            if (baseSpeed <= 0)
            {
                throw new ArgumentException("基础速度必须为正数", nameof(baseSpeed));
            }
            
            return baseSpeed * (1 + rideAddSpeed / 100);
        }

        /// <summary>
        /// 计算从角色移动速度反推坐骑加成。
        /// </summary>
        /// <param name="baseSpeed">基础移动速度</param>
        /// <param name="roleMoveSpeed">计算后的角色移动速度</param>
        /// <returns>坐骑加成值 rideAddSpeed</returns>
        public static double CalculateRideAddSpeed(double baseSpeed, double roleMoveSpeed)
        {
            if (baseSpeed <= 0)
            {
                throw new ArgumentException("基础速度必须为正数", nameof(baseSpeed));
            }
            
            return (roleMoveSpeed / baseSpeed - 1) * 100;
        }

        /// <summary>
        /// 速度模型的验证结果。
        /// </summary>
        public class SpeedCalculationResult
        {
            public double BaseSpeed { get; set; }
            public double RideAddSpeed { get; set; }
            public double RoleMoveSpeed { get; set; }
            public bool IsValid => BaseSpeed > 0 && RoleMoveSpeed > 0;

            public override string ToString()
            {
                return $"baseSpeed={BaseSpeed}, rideAddSpeed={RideAddSpeed}, roleMoveSpeed={RoleMoveSpeed}";
            }
        }

        /// <summary>
        /// 验证速度计算结果。
        /// </summary>
        public static SpeedCalculationResult ValidateCalculation(double baseSpeed, double rideAddSpeed)
        {
            double roleMoveSpeed = CalculateRoleMoveSpeed(baseSpeed, rideAddSpeed);
            return new SpeedCalculationResult
            {
                BaseSpeed = baseSpeed,
                RideAddSpeed = rideAddSpeed,
                RoleMoveSpeed = roleMoveSpeed
            };
        }
    }
}