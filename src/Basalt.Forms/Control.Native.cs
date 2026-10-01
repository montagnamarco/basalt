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
}
