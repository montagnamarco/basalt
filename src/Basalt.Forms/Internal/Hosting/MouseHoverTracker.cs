using System.Drawing;
using Avalonia.Threading;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// MouseHover as TrackMouseEvent produces it: once per entry into a
/// control, after the pointer has rested for SystemInformation.MouseHoverTime
/// inside a rectangle of SystemInformation.MouseHoverSize. Moving out of the
/// rectangle starts the wait again from the new point; only a new entry or
/// Control.ResetMouseEventArgs arms the control for another hover.
/// </summary>
internal static class MouseHoverTracker
{
    private static Control? s_control;
    private static Point s_anchor;
    private static Point s_lastPoint;
    private static bool s_armed;
    private static DispatcherTimer? s_timer;

    /// <summary>The pointer entered a control; the point is in screen coordinates.</summary>
    public static void Entered(Control control, Point screenPoint)
    {
        s_control = control;
        s_armed = true;
        s_lastPoint = screenPoint;
        Restart(screenPoint);
    }

    public static void Left(Control control)
    {
        if (s_control != control) return;

        s_control = null;
        s_armed = false;
        s_timer?.Stop();
    }

    public static void Moved(Point screenPoint)
    {
        s_lastPoint = screenPoint;
        if (!s_armed) return;

        var size = SystemInformation.MouseHoverSize;
        var still = Math.Abs(screenPoint.X - s_anchor.X) <= size.Width / 2 && Math.Abs(screenPoint.Y - s_anchor.Y) <= size.Height / 2;

        if (!still) Restart(screenPoint);
    }

    /// <summary>Arms the control again, if the pointer is still over it, for one more MouseHover.</summary>
    public static void Rearm(Control control)
    {
        if (s_control != control) return;

        s_armed = true;
        Restart(s_lastPoint);
    }

    /// <summary>The wait is over: the pointer rested long enough.</summary>
    internal static void Elapse()
    {
        s_timer?.Stop();

        if (!s_armed || s_control is not { IsDisposed: false } control) return;

        s_armed = false;

        // A control taken out of its window since it was entered has nowhere to report the hover.
        if (control.TopLevelControl is not { } window) return;

        FormsInput.Guard(window, control.RaiseMouseHover);
    }

    private static void Restart(Point screenPoint)
    {
        s_anchor = screenPoint;

        s_timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(SystemInformation.MouseHoverTime), DispatcherPriority.Input, (_, _) => Elapse());
        s_timer.Stop();
        s_timer.Start();
    }
}
