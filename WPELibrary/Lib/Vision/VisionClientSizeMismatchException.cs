using System;
using System.Drawing;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionClientSizeMismatchException : InvalidOperationException
    {
        public Size ActualClientSize { get; private set; }

        public Size RequiredClientSize { get; private set; }

        public VisionClientSizeMismatchException(Size actualClientSize, Size requiredClientSize)
            : base(string.Format(
                "目标窗口客户区必须为 {0}×{1}，当前为 {2}×{3}。请恢复雷电模拟器分辨率后再运行助手。",
                requiredClientSize.Width,
                requiredClientSize.Height,
                actualClientSize.Width,
                actualClientSize.Height))
        {
            this.ActualClientSize = actualClientSize;
            this.RequiredClientSize = requiredClientSize;
        }
    }
}
