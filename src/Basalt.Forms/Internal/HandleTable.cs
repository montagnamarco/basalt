namespace System.Windows.Forms.Internal;

/// <summary>
/// Gives each control that creates a handle a number of its own, and finds
/// the control again from it, as Control.FromHandle does with a window.
/// </summary>
/// <remarks>
/// The numbers are even and start high, so they are never mistaken for a
/// small integer and, being even, never collide with the odd values Windows
/// uses for some special handles. The table holds controls weakly: a control
/// that was never disposed does not stay alive because it once had a handle.
/// </remarks>
internal static class HandleTable
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<IntPtr, WeakReference<Control>> Controls = [];
    private static long s_next = 0x10000;

    public static IntPtr Register(Control control)
    {
        lock (Gate)
        {
            s_next += 2;
            var handle = new IntPtr(s_next);
            Controls[handle] = new WeakReference<Control>(control);

            return handle;
        }
    }

    public static void Unregister(IntPtr handle)
    {
        lock (Gate)
        {
            Controls.Remove(handle);
        }
    }

    public static Control? Find(IntPtr handle)
    {
        lock (Gate)
        {
            if (!Controls.TryGetValue(handle, out var reference)) return null;

            if (reference.TryGetTarget(out var control)) return control;

            // Collected without being disposed or finalized yet: the entry is stale.
            Controls.Remove(handle);

            return null;
        }
    }

    // A separate map for NativeWindow: it is not a Control, but its handles come from the
    // same counter, so a NativeWindow's handle is never mistaken for a Control's or the other way round.
    private static readonly Dictionary<IntPtr, WeakReference<NativeWindow>> Windows = [];

    public static IntPtr RegisterWindow(NativeWindow window)
    {
        lock (Gate)
        {
            s_next += 2;
            var handle = new IntPtr(s_next);
            Windows[handle] = new WeakReference<NativeWindow>(window);

            return handle;
        }
    }

    /// <summary>AssignHandle's case: the handle already exists (often a Control's own), so the window is filed under it, not a fresh one.</summary>
    public static void RegisterWindow(IntPtr handle, NativeWindow window)
    {
        lock (Gate)
        {
            Windows[handle] = new WeakReference<NativeWindow>(window);
        }
    }

    public static void UnregisterWindow(IntPtr handle)
    {
        lock (Gate)
        {
            Windows.Remove(handle);
        }
    }

    public static NativeWindow? FindWindow(IntPtr handle)
    {
        lock (Gate)
        {
            if (!Windows.TryGetValue(handle, out var reference)) return null;

            if (reference.TryGetTarget(out var window)) return window;

            Windows.Remove(handle);

            return null;
        }
    }
}
