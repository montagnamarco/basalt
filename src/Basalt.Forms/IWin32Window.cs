namespace System.Windows.Forms;

/// <summary>Something with a window handle: a control, or a window of another toolkit given as an owner.</summary>
public interface IWin32Window
{
    IntPtr Handle { get; }
}
