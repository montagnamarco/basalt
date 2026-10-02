using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// Wraps a window handle to receive its messages: either one <see
/// cref="AssignHandle"/> hands it (typically a <see cref="Control"/>'s own,
/// to subclass it) or one <see cref="CreateHandle"/> makes for it alone.
/// There is no real message queue on this platform (see Message.cs), so a
/// handle here is only ever found again by <see cref="FromHandle"/>; nothing
/// delivers WndProc, which stays unimplemented (skipped, with WndProc and
/// DefWndProc, until Control's own message dispatch lands).
/// </summary>
public class NativeWindow : MarshalByRefObject, IWin32Window
{
    private IntPtr _handle;

    public NativeWindow()
    {
    }

    public IntPtr Handle => _handle;

    /// <summary>Starts receiving the messages of a handle that already exists (most often a Control's).</summary>
    public void AssignHandle(IntPtr handle)
    {
        ReleaseHandle();

        _handle = handle;
        if (handle != IntPtr.Zero) HandleTable.RegisterWindow(handle, this);

        OnHandleChange();
    }

    /// <summary>Makes a handle of this window's own: nothing a real one would back it with, but one <see cref="FromHandle"/> can find.</summary>
    public virtual void CreateHandle(CreateParams cp)
    {
        ArgumentNullException.ThrowIfNull(cp);

        ReleaseHandle();

        _handle = HandleTable.RegisterWindow(this);
        OnHandleChange();
    }

    /// <summary>Forwards the message to whatever the default window procedure would have done: nothing, there being no real one.</summary>
    public void DefWndProc(ref Message m)
    {
    }

    public virtual void DestroyHandle() => ReleaseHandle();

    /// <summary>Stops receiving a handle's messages without destroying it (the opposite of AssignHandle).</summary>
    public virtual void ReleaseHandle()
    {
        if (_handle == IntPtr.Zero) return;

        HandleTable.UnregisterWindow(_handle);
        _handle = IntPtr.Zero;

        OnHandleChange();
    }

    public static NativeWindow? FromHandle(IntPtr handle) => handle == IntPtr.Zero ? null : HandleTable.FindWindow(handle);

    protected virtual void OnHandleChange()
    {
    }

    /// <summary>An exception from a message handler goes where Application.OnThreadException sends every other one.</summary>
    protected virtual void OnThreadException(Exception e) => Application.OnThreadException(e);

    // WndProc is skipped along with DefWndProc's real dispatch: there is no message queue here (see
    // Message.cs) to deliver to it, and Control's own WndProc is not ported yet either.

    ~NativeWindow() => ReleaseHandle();
}
