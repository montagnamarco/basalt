using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A top-level window moved to a monitor with another dpi. Cancelling keeps
/// the old dpi's scaling (on Windows; here the platform already rendered at
/// the new one, so cancelling only stops the control's own rescaling).
/// </summary>
public sealed class DpiChangedEventArgs : CancelEventArgs
{
    internal DpiChangedEventArgs(int deviceDpiOld, int deviceDpiNew, Rectangle suggestedRectangle)
    {
        DeviceDpiOld = deviceDpiOld;
        DeviceDpiNew = deviceDpiNew;
        SuggestedRectangle = suggestedRectangle;
    }

    public int DeviceDpiOld { get; }

    public int DeviceDpiNew { get; }

    public Rectangle SuggestedRectangle { get; }

    public override string ToString() =>
        $"DeviceDpiOld:{DeviceDpiOld} DeviceDpiNew:{DeviceDpiNew} SuggestedRectangle:{{{SuggestedRectangle}}} Cancel:{Cancel}";
}
