using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑速度预设运行上下文。
    /// </summary>
    public class MountSpeedPresetContext
    {
        public MountSpeedPreset Preset { get; set; }
        public double ReadBaseSpeed { get; set; } = 0;
        public double ReadRideAddSpeed { get; set; } = 0;
        public double ReadRoleMoveSpeed { get; set; } = 0;
        public double CalculatedRoleMoveSpeed { get; set; } = 0;
        public bool IsMounted { get; set; } = false;
        public List<StepLogEntry> LogEntries { get; set; } = new List<StepLogEntry>();
        public bool IsPaused { get; set; } = false;
        public string PauseReason { get; set; } = string.Empty;
        public double BaseSpeedTolerance { get; set; } = 0.001;
    }

    /// <summary>
    /// 步骤日志条目。
    /// </summary>
    public class StepLogEntry
    {
        public string State { get; set; }
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }
    }
}