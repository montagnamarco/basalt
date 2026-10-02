using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The base WinForms gives an ActiveX browser control a window to live in:
/// handle lifecycle, focus and the ambient properties the ocx is told about.
/// Basalt has no ActiveX and no browser engine, so the hooks a real ocx
/// would use (<see cref="AttachInterfaces"/>, <see cref="CreateSink"/> and
/// their counterparts) stay empty, and <see cref="ActiveXInstance"/> - the
/// one member whose whole purpose is to hand out that COM object - refuses.
/// <see cref="WebBrowser"/> is the only control built on this base; its
/// derived behaviour (navigation, history, painting a blank page) lives
/// there, not here.
/// </summary>
public partial class WebBrowserBase : Control
{
    private const string NoActiveXInstance =
        "WebBrowserBase hosts a real ActiveX browser on Windows; Basalt never creates that COM object, so there is no instance to return.";

    private WebBrowserSiteBase? _site;

    /// <summary>
    /// Internal, as in WinForms: only <see cref="WebBrowser"/>, in this same
    /// assembly, may derive from it. Every other application derives its own
    /// ActiveX hosts straight from <see cref="AxHost"/> instead.
    /// </summary>
    internal WebBrowserBase()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
        TabStop = true;
    }

    /// <summary>The native browser object a real host would hand out; refuses, since none exists.</summary>
    public object ActiveXInstance => throw new PlatformNotSupportedException(NoActiveXInstance);

    protected override Size DefaultSize => new(250, 250);

    /// <summary>Queries the native object for the interfaces a host sinks events through. Nothing to query here.</summary>
    protected virtual void AttachInterfaces(object nativeActiveXObject)
    {
    }

    /// <summary>Connects the host's event sink to the native object's outgoing interface. Nothing to connect here.</summary>
    protected virtual void CreateSink()
    {
    }

    /// <summary>Lets a derived host plug in its own <see cref="WebBrowserSiteBase"/>.</summary>
    protected virtual WebBrowserSiteBase CreateWebBrowserSiteBase() => new(this);

    /// <summary>The reverse of <see cref="AttachInterfaces"/>. Nothing to release here.</summary>
    protected virtual void DetachInterfaces()
    {
    }

    /// <summary>The reverse of <see cref="CreateSink"/>. Nothing to disconnect here.</summary>
    protected virtual void DetachSink()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _site?.Dispose();
            _site = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>An ActiveX control is handed every character WinForms sees; so is this placeholder.</summary>
    protected override bool IsInputChar(char charCode) => true;

    protected override void OnBackColorChanged(EventArgs e) => base.OnBackColorChanged(e);

    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);

    protected override void OnForeColorChanged(EventArgs e) => base.OnForeColorChanged(e);

    protected override void OnGotFocus(EventArgs e) => base.OnGotFocus(e);

    /// <summary>Where a real host would create the native object; there is none to create, so only the handle itself exists.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _site ??= CreateWebBrowserSiteBase();
    }

    protected override void OnLostFocus(EventArgs e) => base.OnLostFocus(e);

    protected override void OnParentChanged(EventArgs e) => base.OnParentChanged(e);

    protected override void OnRightToLeftChanged(EventArgs e) => base.OnRightToLeftChanged(e);

    protected override void OnVisibleChanged(EventArgs e) => base.OnVisibleChanged(e);

    /// <summary>A real host offers the key to the ocx first; with none, the base's own dialog navigation applies.</summary>
    protected override bool ProcessDialogKey(Keys keyData) => base.ProcessDialogKey(keyData);

    protected internal override bool ProcessMnemonic(char charCode) => base.ProcessMnemonic(charCode);
}
