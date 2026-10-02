namespace System.Windows.Forms;

/// <summary>
/// WinForms describes a DOM script error with one of these, raised from
/// HtmlWindow.Error. Basalt never raises it - there is no DOM and no
/// script to fail - so nothing in this assembly calls the constructor
/// below, matching WinForms, where this type has no public one either.
/// Every member still exists, for source compatibility, and refuses the
/// moment code reaches past the checks that would normally stop it first.
/// </summary>
public sealed class HtmlElementErrorEventArgs : EventArgs
{
    private const string NoDom = "There is no HTML DOM behind this control: no script error is ever raised to describe.";

    internal HtmlElementErrorEventArgs()
    {
    }

    public string Description => throw new PlatformNotSupportedException(NoDom);

    public bool Handled
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public int LineNumber => throw new PlatformNotSupportedException(NoDom);

    public Uri Url => throw new PlatformNotSupportedException(NoDom);
}
