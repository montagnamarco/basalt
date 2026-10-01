using System.Runtime.InteropServices;

namespace System.Windows.Forms;

/// <summary>
/// A Windows message. There is no message queue here: the type exists so
/// that code which builds or compares one compiles and runs, and so that
/// <c>WndProc</c> overrides receive the few messages the controls synthesize.
/// </summary>
public struct Message : IEquatable<Message>
{
    public IntPtr HWnd { get; set; }

    public int Msg { get; set; }

    public IntPtr WParam { get; set; }

    public IntPtr LParam { get; set; }

    public IntPtr Result { get; set; }

    public static Message Create(IntPtr hWnd, int msg, IntPtr wparam, IntPtr lparam) =>
        new() { HWnd = hWnd, Msg = msg, WParam = wparam, LParam = lparam, Result = IntPtr.Zero };

    /// <summary>Reads the structure LParam points to, as Windows' does.</summary>
    public object? GetLParam(Type cls) => Marshal.PtrToStructure(LParam, cls);

    public override bool Equals(object? o) => o is Message other && Equals(other);

    public bool Equals(Message other) =>
        HWnd == other.HWnd && Msg == other.Msg && WParam == other.WParam && LParam == other.LParam && Result == other.Result;

    public override int GetHashCode() => HashCode.Combine(HWnd, Msg);

    public static bool operator ==(Message a, Message b) => a.Equals(b);

    public static bool operator !=(Message a, Message b) => !a.Equals(b);

    public override string ToString() =>
        $"msg=0x{Msg:x} hwnd=0x{(long)HWnd:x} wparam=0x{(long)WParam:x} lparam=0x{(long)LParam:x} result=0x{(long)Result:x}";
}
