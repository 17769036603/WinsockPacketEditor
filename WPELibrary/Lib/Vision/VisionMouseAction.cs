using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using WindowsInput;
using WPELibrary.Lib;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionMouseAction : IVisionAssistantAction
    {
        private readonly Socket_VisionProfile profile;
        private readonly VisionActionDefinition definition;

        public VisionMouseAction(Socket_VisionProfile profile, VisionActionDefinition definition)
        {
            this.profile = profile ?? throw new ArgumentNullException("profile");
            this.definition = definition == null
                ? new VisionActionDefinition()
                : definition.Clone();
        }

        public VisionAssistantActionResult Execute(
            VisionAssistantActionContext context,
            CancellationToken cancellationToken)
        {
            if (this.definition.Type == VisionActionType.None)
            {
                return VisionAssistantActionResult.Succeeded();
            }
            if (!this.profile.AllowSystemInput)
            {
                return VisionAssistantActionResult.Failed(
                    "System input actions are disabled until the current run is explicitly confirmed.");
            }
            this.definition.Validate();
            if (cancellationToken.IsCancellationRequested)
            {
                return VisionAssistantActionResult.CancelledResult();
            }

            VisionWindowInfo window;
            string reason;
            if (!VisionWindowService.TryResolveWindow(
                new IntPtr(this.profile.WindowHandle),
                this.profile.ProcessId,
                this.profile.ProcessName,
                this.profile.ProcessPath,
                this.profile.ProcessStartTimeUtcTicks,
                this.profile.WindowTitle,
                out window,
                out reason))
            {
                return VisionAssistantActionResult.Failed(reason);
            }

            this.profile.WindowHandle = window.Handle.ToInt64();
            this.profile.ProcessId = window.ProcessId;
            this.profile.ProcessName = window.ProcessName;
            this.profile.ProcessPath = window.ProcessPath;
            this.profile.ProcessStartTimeUtcTicks = window.ProcessStartTimeUtcTicks;
            this.profile.WindowTitle = window.WindowTitle;
            Size validatedClientSize;
            if (!VisionWindowService.TryValidateClientSize(
                window.Handle,
                this.profile.CaptureSettings,
                out validatedClientSize,
                out reason))
            {
                return VisionAssistantActionResult.Failed(reason);
            }
            if (!VisionWindowService.TryActivateWindow(window.Handle))
            {
                return VisionAssistantActionResult.Failed("The target window could not be activated.");
            }

            if (!VisionWindowService.TryValidateClientSize(
                window.Handle,
                this.profile.CaptureSettings,
                out validatedClientSize,
                out reason))
            {
                return VisionAssistantActionResult.Failed(reason);
            }

            Rectangle clientBounds;
            Size clientSize;
            if (!VisionWindowService.TryGetClientBounds(window.Handle, out clientBounds, out clientSize))
            {
                return VisionAssistantActionResult.Failed("The target window client area is unavailable.");
            }

            Point clientPoint = this.ResolveClientPoint(context, clientSize);
            Point screenPoint = new Point(
                clientBounds.Left + clientPoint.X,
                clientBounds.Top + clientPoint.Y);
            InputSimulator simulator = new InputSimulator();
            Point absolutePoint = ToVirtualDesktopAbsolutePoint(screenPoint);
            simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absolutePoint.X, absolutePoint.Y);
            if (!Wait(cancellationToken, this.definition.DelayMilliseconds))
            {
                return VisionAssistantActionResult.CancelledResult();
            }

            if (!VisionWindowService.TryValidateClientSize(
                window.Handle,
                this.profile.CaptureSettings,
                out validatedClientSize,
                out reason))
            {
                return VisionAssistantActionResult.Failed(reason);
            }
            if (!VisionWindowService.TryGetClientBounds(
                window.Handle,
                out clientBounds,
                out clientSize))
            {
                return VisionAssistantActionResult.Failed("The target window client area is unavailable.");
            }
            clientPoint = this.ResolveClientPoint(context, clientSize);
            screenPoint = new Point(
                clientBounds.Left + clientPoint.X,
                clientBounds.Top + clientPoint.Y);
            absolutePoint = ToVirtualDesktopAbsolutePoint(screenPoint);
            simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absolutePoint.X, absolutePoint.Y);

            // A resize can happen while the pointer is being repositioned.
            // Recheck immediately before the irreversible input event so an
            // exact-size run cannot click or scroll using stale coordinates.
            if (!VisionWindowService.IsForegroundWindow(window.Handle))
            {
                return VisionAssistantActionResult.Failed(
                    "The target window lost the foreground before system input was sent.");
            }
            if (!VisionWindowService.TryValidateClientSize(
                window.Handle,
                this.profile.CaptureSettings,
                out validatedClientSize,
                out reason))
            {
                return VisionAssistantActionResult.Failed(reason);
            }

            switch (this.definition.Type)
            {
                case VisionActionType.LeftClick:
                    simulator.Mouse.LeftButtonClick();
                    break;
                case VisionActionType.RightClick:
                    simulator.Mouse.RightButtonClick();
                    break;
                case VisionActionType.DoubleClick:
                    simulator.Mouse.LeftButtonDoubleClick();
                    break;
                case VisionActionType.Scroll:
                    simulator.Mouse.VerticalScroll(
                        this.definition.ScrollDirection == VisionScrollDirection.Down
                            ? -this.definition.ScrollAmount
                            : this.definition.ScrollAmount);
                    break;
                default:
                    return VisionAssistantActionResult.Failed("Unsupported vision action type.");
            }

            return VisionAssistantActionResult.Succeeded();
        }

        private Point ResolveClientPoint(VisionAssistantActionContext context, Size clientSize)
        {
            Rectangle observationRegion = context == null ||
                context.Condition == null ||
                context.Condition.Region == null
                ? new Rectangle(0, 0, clientSize.Width, clientSize.Height)
                : context.Condition.Region.Resolve(clientSize);
            if (context != null && context.LastObservation != null)
            {
                if (context.LastObservation.OcrResult != null &&
                    context.Condition != null &&
                    context.Condition.TextCondition != null)
                {
                    Rectangle textBounds;
                    if (context.Condition.TextCondition.TryGetFirstMatchBounds(
                        context.LastObservation.OcrResult,
                        out textBounds))
                    {
                        return ClampPoint(
                            new Point(
                                observationRegion.Left + textBounds.Left + textBounds.Width / 2,
                                observationRegion.Top + textBounds.Top + textBounds.Height / 2),
                            clientSize);
                    }
                }
                if (context.LastObservation.TemplateResult != null &&
                    context.LastObservation.TemplateResult.Found)
                {
                    Rectangle bounds = context.LastObservation.TemplateResult.Bounds;
                    return ClampPoint(
                        new Point(
                            observationRegion.Left + bounds.Left + bounds.Width / 2,
                            observationRegion.Top + bounds.Top + bounds.Height / 2),
                        clientSize);
                }
                if (context.LastObservation.ColorResult != null &&
                    context.LastObservation.ColorResult.Found &&
                    !context.LastObservation.ColorResult.Bounds.IsEmpty)
                {
                    Rectangle bounds = context.LastObservation.ColorResult.Bounds;
                    return ClampPoint(
                        new Point(
                            observationRegion.Left + bounds.Left + bounds.Width / 2,
                            observationRegion.Top + bounds.Top + bounds.Height / 2),
                        clientSize);
                }
            }

            return ClampPoint(
                new Point(observationRegion.Left + observationRegion.Width / 2, observationRegion.Top + observationRegion.Height / 2),
                clientSize);
        }

        private static Point ClampPoint(Point point, Size clientSize)
        {
            return new Point(
                Math.Max(0, Math.Min(Math.Max(0, clientSize.Width - 1), point.X)),
                Math.Max(0, Math.Min(Math.Max(0, clientSize.Height - 1), point.Y)));
        }

        private static Point ToVirtualDesktopAbsolutePoint(Point screenPoint)
        {
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            return new Point(
                ToAbsoluteCoordinate(screenPoint.X, virtualScreen.Left, virtualScreen.Width),
                ToAbsoluteCoordinate(screenPoint.Y, virtualScreen.Top, virtualScreen.Height));
        }

        private static int ToAbsoluteCoordinate(int coordinate, int origin, int length)
        {
            if (length <= 1)
            {
                return 0;
            }
            double normalized = (coordinate - (double)origin) * 65535D / (length - 1D);
            return (int)Math.Max(0D, Math.Min(65535D, Math.Round(normalized)));
        }

        private static bool Wait(CancellationToken cancellationToken, int milliseconds)
        {
            return milliseconds <= 0 || !cancellationToken.WaitHandle.WaitOne(milliseconds);
        }
    }
}
