using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A mouse button, click count, position in client coordinates and wheel turn.</summary>
public class MouseEventArgs : EventArgs
{
    public MouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta)
    {
        Button = button;
        Clicks = clicks;
        X = x;
        Y = y;
        Delta = delta;
    }

    public MouseButtons Button { get; }

    public int Clicks { get; }

    public int X { get; }

    public int Y { get; }

    /// <summary>Wheel rotation in 120ths of a notch, positive away from the user.</summary>
    public int Delta { get; }

    public Point Location => new(X, Y);
}

/// <summary>Mouse arguments a handler can mark as handled, so the wheel does not scroll the parent too.</summary>
public class HandledMouseEventArgs : MouseEventArgs
{
    public HandledMouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta)
        : this(button, clicks, x, y, delta, false)
    {
    }

    public HandledMouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta, bool defaultHandledValue)
        : base(button, clicks, x, y, delta)
    {
        Handled = defaultHandledValue;
    }

    public bool Handled { get; set; }
}
