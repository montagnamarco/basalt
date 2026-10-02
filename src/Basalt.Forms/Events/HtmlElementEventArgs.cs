using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// WinForms describes a DOM event (a click, a key, a drag) with one of
/// these. Basalt never raises an HtmlElement event - there is no DOM to
/// raise one from - so nothing in this assembly calls the constructor
/// below, matching WinForms, where this type has no public one either.
/// Every member still exists, for source compatibility, and refuses the
/// moment code reaches past the checks that would normally stop it first.
/// </summary>
public sealed class HtmlElementEventArgs : EventArgs
{
    private const string NoDom = "There is no HTML DOM behind this control: no HtmlElement event is ever raised to describe.";

    internal HtmlElementEventArgs()
    {
    }

    public bool AltKeyPressed => throw new PlatformNotSupportedException(NoDom);

    public bool BubbleEvent
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public Point ClientMousePosition => throw new PlatformNotSupportedException(NoDom);

    public bool CtrlKeyPressed => throw new PlatformNotSupportedException(NoDom);

    public string EventType => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement? FromElement => throw new PlatformNotSupportedException(NoDom);

    public int KeyPressedCode => throw new PlatformNotSupportedException(NoDom);

    public MouseButtons MouseButtonsPressed => throw new PlatformNotSupportedException(NoDom);

    public Point MousePosition => throw new PlatformNotSupportedException(NoDom);

    public Point OffsetMousePosition => throw new PlatformNotSupportedException(NoDom);

    public bool ReturnValue
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public bool ShiftKeyPressed => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement? ToElement => throw new PlatformNotSupportedException(NoDom);
}
