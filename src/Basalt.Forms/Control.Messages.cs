using System.ComponentModel;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// The window procedure. Basalt has no Win32 message queue: the messages
/// that reach WndProc are the ones it synthesizes (the key messages, and
/// what ReflectMessage forwards), so an override that intercepts them sees
/// them in WinForms' order, after PreProcessMessage and before the key
/// events. Painting and the mouse are dispatched directly.
/// </summary>
public partial class Control
{
    /// <summary>WM_REFLECT: added to a notification a parent sends back to the child it came from.</summary>
    private const int ReflectedMessageOffset = 0x2000;

    private IWindowTarget? _windowTarget;

    /// <summary>Set when a synthesized message reaches DefWndProc, that is when nothing in WndProc consumed it.</summary>
    private bool _reachedDefWndProc;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWindowTarget WindowTarget
    {
        get => _windowTarget ??= new ControlWindowTarget(this);
        set => _windowTarget = value;
    }

    /// <summary>
    /// Handles one message: the key messages run the key events, anything
    /// else goes to DefWndProc. Overrides call base for what they leave alone.
    /// </summary>
    protected virtual void WndProc(ref Message m)
    {
        if (GetStyle(ControlStyles.EnableNotifyMessage)) OnNotifyMessage(m);

        if (WindowMessages.IsKeyDown(m.Msg) || WindowMessages.IsChar(m.Msg) || m.Msg is WindowMessages.KeyUp or WindowMessages.SystemKeyUp)
        {
            if (ProcessKeyMessage(ref m)) return;
        }

        DefWndProc(ref m);
    }

    /// <summary>
    /// What Windows does with a message nobody handled. The native behaviour
    /// of Basalt's controls runs in the key pipeline itself, so there is
    /// nothing left to do but report that the message got here.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void DefWndProc(ref Message m)
    {
        _reachedDefWndProc = true;
        m.Result = IntPtr.Zero;
    }

    /// <summary>Called first in WndProc for controls with ControlStyles.EnableNotifyMessage; it cannot change the message.</summary>
    protected virtual void OnNotifyMessage(Message m)
    {
    }

    /// <summary>
    /// Sends a notification back to the control it is about, as a parent
    /// does with the child's WM_COMMAND or WM_NOTIFY; false when the handle
    /// is no control's.
    /// </summary>
    protected static bool ReflectMessage(IntPtr hWnd, ref Message m)
    {
        if (HandleTable.Find(hWnd) is not { } control) return false;

        var reflected = Message.Create(hWnd, m.Msg + ReflectedMessageOffset, m.WParam, m.LParam);
        control.WindowTarget.OnMessage(ref reflected);
        m.Result = reflected.Result;

        return true;
    }

    /// <summary>
    /// Delivers a synthesized message to the window procedure; true when
    /// something consumed it before the default procedure.
    /// </summary>
    private bool SendToWindowProcedure(ref Message message)
    {
        _reachedDefWndProc = false;
        WindowTarget.OnMessage(ref message);

        return !_reachedDefWndProc;
    }

    /// <summary>The window target every control starts with: its own WndProc.</summary>
    private sealed class ControlWindowTarget(Control control) : IWindowTarget
    {
        public void OnHandleChange(IntPtr newHandle)
        {
        }

        public void OnMessage(ref Message m) => control.WndProc(ref m);
    }
}
