using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A tool tip about to appear: a handler may cancel it or change its size.</summary>
public class PopupEventArgs : CancelEventArgs
{
    public PopupEventArgs(IWin32Window? associatedWindow, Control? associatedControl, bool isBalloon, Size size)
    {
        AssociatedWindow = associatedWindow;
        AssociatedControl = associatedControl;
        IsBalloon = isBalloon;
        ToolTipSize = size;
    }

    public IWin32Window? AssociatedWindow { get; }

    public Control? AssociatedControl { get; }

    public bool IsBalloon { get; }

    public Size ToolTipSize { get; set; }
}
