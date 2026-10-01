using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// Mouse events, raised by the window's input routing with the rules
/// WinForms applies to mouse messages: a press focuses a selectable control
/// and captures the mouse, Click comes before MouseUp, and only when the
/// button is released over the same control.
/// </summary>
public partial class Control
{
    private bool _mousePressed;
    private bool _doubleClickFired;

    /// <summary>Whether this control receives the mouse wherever it goes, until the buttons are released.</summary>
    [Browsable(false)]
    public bool Capture
    {
        get => FormsInput.CapturedControl == this;
        set
        {
            if (value == Capture) return;

            var previous = FormsInput.CapturedControl;
            FormsInput.CapturedControl = value ? this : null;

            // Lost while a button was still down: the press is abandoned, not completed.
            if (previous is not null && previous._mousePressed)
            {
                previous._mousePressed = false;
                previous.DefaultCaptureLost();
            }

            previous?.OnMouseCaptureChanged(EventArgs.Empty);
            if (value) OnMouseCaptureChanged(EventArgs.Empty);
        }
    }

    /// <summary>The buttons held down, as the last mouse event reported them.</summary>
    public static MouseButtons MouseButtons => FormsInput.Buttons;

    /// <summary>The pointer's position in screen coordinates, as the last mouse event reported it.</summary>
    public static Point MousePosition => FormsInput.ScreenPosition;

    /// <summary>The modifier keys held down; on macOS, Command reads as Control.</summary>
    public static Keys ModifierKeys => FormsInput.Modifiers;

    internal void RaiseMouseDown(MouseButtons button, int clicks, Point location)
    {
        var buttonsBefore = MouseButtons;
        _mousePressed = true;
        _doubleClickFired = clicks >= 2 && GetStyle(ControlStyles.StandardDoubleClick);

        var arguments = new MouseEventArgs(button, clicks, location.X, location.Y, 0);

        // A native control handles the press first (and takes the focus itself); ours only focus.
        if (!GetStyle(ControlStyles.UserMouse)) DefaultMouseDown(arguments);
        else if (button == MouseButtons.Left && GetStyle(ControlStyles.Selectable)) Focus();

        // A handler that opened a dialog or released the button has changed the situation.
        if (IsDisposed || buttonsBefore != MouseButtons) return;

        Capture = true;

        if (Enabled) OnMouseDown(arguments);
    }

    internal void RaiseMouseUp(MouseButtons button, int clicks, Point location, bool releasedOverThisControl)
    {
        try
        {
            if (!GetStyle(ControlStyles.UserMouse)) DefaultMouseUp(new MouseEventArgs(button, clicks, location.X, location.Y, 0));

            var fireClick = GetStyle(ControlStyles.StandardClick) && _mousePressed && !IsDisposed && releasedOverThisControl;

            if (fireClick)
            {
                if (_doubleClickFired)
                {
                    OnDoubleClick(new MouseEventArgs(button, 2, location.X, location.Y, 0));
                    OnMouseDoubleClick(new MouseEventArgs(button, 2, location.X, location.Y, 0));
                }
                else
                {
                    OnClick(new MouseEventArgs(button, clicks, location.X, location.Y, 0));
                    OnMouseClick(new MouseEventArgs(button, clicks, location.X, location.Y, 0));
                }
            }

            OnMouseUp(new MouseEventArgs(button, clicks, location.X, location.Y, 0));

            if (button == MouseButtons.Right && !IsDisposed) ShowContextMenu(location);
        }
        finally
        {
            _doubleClickFired = false;
            _mousePressed = false;
            Capture = false;
        }
    }

    internal void RaiseMouseMove(MouseButtons buttons, Point location)
    {
        var arguments = new MouseEventArgs(buttons, 0, location.X, location.Y, 0);

        if (!GetStyle(ControlStyles.UserMouse)) DefaultMouseMove(arguments);

        OnMouseMove(arguments);
    }

    internal void RaiseMouseEnter() => OnMouseEnter(EventArgs.Empty);

    internal void RaiseMouseLeave() => OnMouseLeave(EventArgs.Empty);

    internal void RaiseMouseHover() => OnMouseHover(EventArgs.Empty);

    /// <summary>Raises MouseWheel; true when a handler marked it handled, so the parent does not scroll as well.</summary>
    internal bool RaiseMouseWheel(Point location, int delta)
    {
        var arguments = new HandledMouseEventArgs(MouseButtons, 0, location.X, location.Y, delta);
        OnMouseWheel(arguments);

        return arguments.Handled;
    }

    /// <summary>What the right button does when released: the control's context menu, when it has one.</summary>
    private void ShowContextMenu(Point location) => ContextMenuStripInternal?.ShowFromControl(this, location);

    /// <summary>The context menu; the menu family attaches it once it exists.</summary>
    internal IContextMenuSource? ContextMenuStripInternal { get; set; }

    protected virtual void OnClick(EventArgs e) => ((EventHandler?)Events[s_clickEvent])?.Invoke(this, e);

    protected virtual void OnDoubleClick(EventArgs e) => ((EventHandler?)Events[s_doubleClickEvent])?.Invoke(this, e);

    protected virtual void OnMouseClick(MouseEventArgs e) => ((MouseEventHandler?)Events[s_mouseClickEvent])?.Invoke(this, e);

    protected virtual void OnMouseDoubleClick(MouseEventArgs e) => ((MouseEventHandler?)Events[s_mouseDoubleClickEvent])?.Invoke(this, e);

    protected virtual void OnMouseDown(MouseEventArgs e) => ((MouseEventHandler?)Events[s_mouseDownEvent])?.Invoke(this, e);

    protected virtual void OnMouseUp(MouseEventArgs e) => ((MouseEventHandler?)Events[s_mouseUpEvent])?.Invoke(this, e);

    protected virtual void OnMouseMove(MouseEventArgs e) => ((MouseEventHandler?)Events[s_mouseMoveEvent])?.Invoke(this, e);

    protected virtual void OnMouseWheel(MouseEventArgs e) => ((MouseEventHandler?)Events[s_mouseWheelEvent])?.Invoke(this, e);

    protected virtual void OnMouseEnter(EventArgs e) => ((EventHandler?)Events[s_mouseEnterEvent])?.Invoke(this, e);

    protected virtual void OnMouseLeave(EventArgs e) => ((EventHandler?)Events[s_mouseLeaveEvent])?.Invoke(this, e);

    protected virtual void OnMouseHover(EventArgs e) => ((EventHandler?)Events[s_mouseHoverEvent])?.Invoke(this, e);

    protected virtual void OnMouseCaptureChanged(EventArgs e) => ((EventHandler?)Events[s_mouseCaptureChangedEvent])?.Invoke(this, e);

    /// <summary>Raises Click on another control, as a control that forwards its clicks does.</summary>
    protected void InvokeOnClick(Control? toInvoke, EventArgs e) => toInvoke?.OnClick(e);

    public event EventHandler? Click
    {
        add => Events.AddHandler(s_clickEvent, value);
        remove => Events.RemoveHandler(s_clickEvent, value);
    }

    public event EventHandler? DoubleClick
    {
        add => Events.AddHandler(s_doubleClickEvent, value);
        remove => Events.RemoveHandler(s_doubleClickEvent, value);
    }

    public event MouseEventHandler? MouseClick
    {
        add => Events.AddHandler(s_mouseClickEvent, value);
        remove => Events.RemoveHandler(s_mouseClickEvent, value);
    }

    public event MouseEventHandler? MouseDoubleClick
    {
        add => Events.AddHandler(s_mouseDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_mouseDoubleClickEvent, value);
    }

    public event MouseEventHandler? MouseDown
    {
        add => Events.AddHandler(s_mouseDownEvent, value);
        remove => Events.RemoveHandler(s_mouseDownEvent, value);
    }

    public event MouseEventHandler? MouseUp
    {
        add => Events.AddHandler(s_mouseUpEvent, value);
        remove => Events.RemoveHandler(s_mouseUpEvent, value);
    }

    public event MouseEventHandler? MouseMove
    {
        add => Events.AddHandler(s_mouseMoveEvent, value);
        remove => Events.RemoveHandler(s_mouseMoveEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public event MouseEventHandler? MouseWheel
    {
        add => Events.AddHandler(s_mouseWheelEvent, value);
        remove => Events.RemoveHandler(s_mouseWheelEvent, value);
    }

    public event EventHandler? MouseEnter
    {
        add => Events.AddHandler(s_mouseEnterEvent, value);
        remove => Events.RemoveHandler(s_mouseEnterEvent, value);
    }

    public event EventHandler? MouseLeave
    {
        add => Events.AddHandler(s_mouseLeaveEvent, value);
        remove => Events.RemoveHandler(s_mouseLeaveEvent, value);
    }

    public event EventHandler? MouseHover
    {
        add => Events.AddHandler(s_mouseHoverEvent, value);
        remove => Events.RemoveHandler(s_mouseHoverEvent, value);
    }

    public event EventHandler? MouseCaptureChanged
    {
        add => Events.AddHandler(s_mouseCaptureChangedEvent, value);
        remove => Events.RemoveHandler(s_mouseCaptureChangedEvent, value);
    }

    private static readonly object s_clickEvent = new();
    private static readonly object s_doubleClickEvent = new();
    private static readonly object s_mouseClickEvent = new();
    private static readonly object s_mouseDoubleClickEvent = new();
    private static readonly object s_mouseDownEvent = new();
    private static readonly object s_mouseUpEvent = new();
    private static readonly object s_mouseMoveEvent = new();
    private static readonly object s_mouseWheelEvent = new();
    private static readonly object s_mouseEnterEvent = new();
    private static readonly object s_mouseLeaveEvent = new();
    private static readonly object s_mouseHoverEvent = new();
    private static readonly object s_mouseCaptureChangedEvent = new();

    /// <summary>
    /// The deepest visible control under a point given in this control's
    /// client coordinates, with the point in that control's client
    /// coordinates; this control itself when no child is there.
    /// </summary>
    internal (Control Target, Point Location) HitTest(Point clientPoint)
    {
        foreach (var child in ChildrenSnapshot())
        {
            if (!child.DesiredVisibility || !child.Bounds.Contains(clientPoint)) continue;

            var frame = child.NonClient;
            var inChild = new Point(clientPoint.X - child.Left - frame.Left, clientPoint.Y - child.Top - frame.Top);

            // On the child's frame: the child is hit, but none of its children.
            if (!child.ClientRectangle.Contains(inChild)) return (child, inChild);

            return child.HitTest(inChild);
        }

        return (this, clientPoint);
    }
}

/// <summary>A context menu a control can show at a point; implemented by ContextMenuStrip.</summary>
internal interface IContextMenuSource
{
    void ShowFromControl(Control control, Point location);
}
