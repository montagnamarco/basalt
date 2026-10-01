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
}
