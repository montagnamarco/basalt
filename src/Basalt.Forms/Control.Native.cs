namespace System.Windows.Forms;

/// <summary>
/// What a native Windows control does by itself, for the controls that are
/// native in WinForms (TextBox, ListBox, ComboBox, ScrollBar and the like).
/// </summary>
/// <remarks>
/// WinForms draws and edits those controls through the window's default
/// procedure (DefWndProc), not through OnPaint and OnKeyDown, and an
/// application that overrides those methods relies on that order: a native
/// control has already handled the mouse press when OnMouseDown runs, and
/// its KeyDown handlers run before the control's own editing, which they can
/// stop with Handled. These hooks are that default procedure. They are
/// internal, so the public surface stays Microsoft's.
/// </remarks>
public partial class Control
{
    /// <summary>Paints a control that does not paint itself through OnPaint (ControlStyles.UserPaint off).</summary>
    internal virtual void DefaultPaint(PaintEventArgs e)
    {
    }

    /// <summary>A key the handlers left unhandled.</summary>
    internal virtual void DefaultKeyDown(KeyEventArgs e)
    {
    }

    internal virtual void DefaultKeyUp(KeyEventArgs e)
    {
    }

    internal virtual void DefaultKeyPress(KeyPressEventArgs e)
    {
    }

    /// <summary>
    /// A mouse press on a control without ControlStyles.UserMouse, before
    /// OnMouseDown. Windows' default procedure does nothing with it: a plain
    /// Control does not take the focus when clicked, a native control that
    /// wants it takes it here.
    /// </summary>
    internal virtual void DefaultMouseDown(MouseEventArgs e)
    {
    }

    internal virtual void DefaultMouseMove(MouseEventArgs e)
    {
    }

    internal virtual void DefaultMouseUp(MouseEventArgs e)
    {
    }

    /// <summary>The window received the focus, before GotFocus is raised.</summary>
    internal virtual void DefaultGotFocus()
    {
    }

    internal virtual void DefaultLostFocus()
    {
    }

    /// <summary>
    /// The control lost the mouse capture without a button release (another
    /// window took it, the window was deactivated): a native control abandons
    /// the press it was tracking, as on WM_CAPTURECHANGED.
    /// </summary>
    internal virtual void DefaultCaptureLost()
    {
    }

    /// <summary>The state of the window's own scroll bar, for drawing it; null when the control has none.</summary>
    internal virtual Internal.WindowScrollInfo? GetWindowScroll(bool vertical) => null;

    /// <summary>
    /// A press on the window's frame (its scroll bars), in outer coordinates;
    /// true when the control took it and wants the moves until the release.
    /// </summary>
    internal virtual bool NonClientMouseDown(MouseButtons button, System.Drawing.Point outer) => false;

    internal virtual void NonClientMouseMove(System.Drawing.Point outer)
    {
    }

    internal virtual void NonClientMouseUp(System.Drawing.Point outer)
    {
    }

    /// <summary>Whether the control is tracking a press on its frame, which receives the moves and the release.</summary>
    internal virtual bool IsTrackingNonClient => false;

    /// <summary>
    /// A key pressed while this control holds the mouse capture, before the
    /// focused control sees it: what WinForms controls catch with a message
    /// filter during a drag (Escape cancels a splitter). True when handled.
    /// </summary>
    internal virtual bool KeyDownWhileCaptured(Keys keyData) => false;
}
