using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The base every generated ActiveX wrapper class (an "Ax" class) derives
/// from: it creates the COM object by CLSID, sinks its events and exposes
/// its properties as a Control. Basalt has no COM and no ActiveX, so the
/// one thing AxHost cannot do here is the one thing it exists for: holding
/// a real OLE control. <see cref="CreateHandle"/>, <see cref="GetOcx"/> and
/// <see cref="CreateInstanceCore"/> - the three places that would create or
/// hand out that object - all refuse. Everything that is pure data (the
/// OLE colour and date conversions, the persisted <see cref="State"/>
/// bytes, the Control properties this class re-declares to hide from the
/// designer) behaves for real.
/// </summary>
public abstract partial class AxHost : Control, ICustomTypeDescriptor, ISupportInitialize
{
    private const string NoActiveXControl =
        "AxHost hosts a real ActiveX control through COM on Windows; Basalt never creates that COM object, so there is nothing behind this control to activate, invoke a verb on, or hand out.";

    private readonly string _clsid;
    private ContainerControl? _containingControl;
    private State? _ocxState;
    private AboutBoxDelegate? _aboutBoxDelegate;

    /// <summary>A real ocx's about box, when it declares one; stored only so SetAboutBoxDelegate has somewhere to go.</summary>
    protected delegate void AboutBoxDelegate();

    protected AxHost(string clsid)
        : this(clsid, 0)
    {
    }

    protected AxHost(string clsid, int flags)
    {
        ArgumentNullException.ThrowIfNull(clsid);
        _clsid = clsid;
        _ = flags;

        SetStyle(ControlStyles.UserPaint, false);
        TabStop = true;
    }

    [Browsable(false)]
    public ContainerControl? ContainingControl
    {
        get
        {
            if (_containingControl is not null) return _containingControl;

            for (var control = Parent; control is not null; control = control.Parent)
            {
                if (control is ContainerControl container) return container;
            }

            return null;
        }
        set => _containingControl = value;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassName = _clsid;
            return parameters;
        }
    }

    protected override Size DefaultSize => new(75, 23);

    /// <summary>True while the ocx is UI-active, through DoVerb(OLEIVERB_INPLACEACTIVATE). Never happens without an ocx.</summary>
    [Browsable(false)]
    public bool EditMode => false;

    /// <summary>Whether the ocx declares an about box. Never true without an ocx.</summary>
    [Browsable(false)]
    public bool HasAboutBox => false;

    [Browsable(false)]
    [DefaultValue(null)]
    public State? OcxState
    {
        get => _ocxState;
        set => _ocxState = value;
    }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }

    /// <summary>Sends an OLE verb (run, UI-activate, hide...) to the ocx. There is no ocx to send one to.</summary>
    public void DoVerb(int verb) => throw new PlatformNotSupportedException(NoActiveXControl);

    /// <summary>The ocx itself, for a generated Ax wrapper's own members to call through to. There is none.</summary>
    public object GetOcx() => throw new PlatformNotSupportedException(NoActiveXControl);

    /// <summary>Whether the ocx offers property pages. Never true without an ocx.</summary>
    public bool HasPropertyPages() => false;

    /// <summary>UI-activates the ocx for in-place editing, as a designer does on a double-click. There is no ocx to activate.</summary>
    public void InvokeEditMode() => throw new PlatformNotSupportedException(NoActiveXControl);

    /// <summary>Tells the ocx's container its persisted state changed. Nothing here persists it, so this is a no-op.</summary>
    public void MakeDirty()
    {
    }

    /// <summary>Whether the ocx's properties can be read safely right now. Never, without an ocx.</summary>
    protected bool PropsValid() => false;

    protected void SetAboutBoxDelegate(AboutBoxDelegate aboutBoxDelegate) => _aboutBoxDelegate = aboutBoxDelegate;

    /// <summary>Shows the ocx's about box. A real host no-ops when HasAboutBox is false, as this always is.</summary>
    public void ShowAboutBox() => _aboutBoxDelegate?.Invoke();

    /// <summary>Shows the ocx's property pages. A real host no-ops when HasPropertyPages is false, as this always is.</summary>
    public void ShowPropertyPages()
    {
    }

    public void ShowPropertyPages(Control control)
    {
    }

    protected virtual void AttachInterfaces()
    {
    }

    /// <summary>Creates the native object by <see cref="_clsid"/>. There is no COM to create it through.</summary>
    protected virtual object CreateInstanceCore(Guid clsid) => throw new PlatformNotSupportedException(NoActiveXControl);

    protected virtual void CreateSink()
    {
    }

    /// <summary>Where a real AxHost creates the ocx by CLSID before giving the control a window. There is none to create.</summary>
    protected override void CreateHandle() => throw new PlatformNotSupportedException(NoActiveXControl);

    protected override void DestroyHandle() => base.DestroyHandle();

    protected virtual void DetachSink()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _ocxState = null;
        base.Dispose(disposing);
    }

    protected override Rectangle GetScaledBounds(Rectangle bounds, SizeF factor, BoundsSpecified specified) =>
        base.GetScaledBounds(bounds, factor, specified);

    protected override bool IsInputChar(char charCode) => base.IsInputChar(charCode);

    protected override void OnBackColorChanged(EventArgs e) => base.OnBackColorChanged(e);

    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);

    protected override void OnForeColorChanged(EventArgs e) => base.OnForeColorChanged(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    /// <summary>Called once the ocx becomes UI-active. Never happens without an ocx.</summary>
    protected virtual void OnInPlaceActive()
    {
    }

    protected override void OnLostFocus(EventArgs e) => base.OnLostFocus(e);

    protected override bool ProcessDialogKey(Keys keyData) => base.ProcessDialogKey(keyData);

    protected internal override bool ProcessMnemonic(char charCode) => base.ProcessMnemonic(charCode);

    /// <summary>
    /// A real ocx tells its site about a mouse message through its own
    /// IOleControlSite callback, with OLE's button mask and shift state
    /// instead of WinForms'; AxHost translates and raises MouseDown so a
    /// host built on it sees an ordinary Control event. No ocx calls these
    /// here, but the translation itself needs nothing COM does, so it
    /// still works for whatever calls them directly.
    /// </summary>
    protected void RaiseOnMouseDown(short button, short shift, int x, int y) =>
        OnMouseDown(new MouseEventArgs(ConvertOleButton(button), 1, x, y, 0));

    protected void RaiseOnMouseDown(short button, short shift, float x, float y) =>
        RaiseOnMouseDown(button, shift, (int)x, (int)y);

    protected void RaiseOnMouseDown(object button, object shift, object x, object y) =>
        RaiseOnMouseDown(Convert.ToInt16(button), Convert.ToInt16(shift), Convert.ToInt32(x), Convert.ToInt32(y));

    protected void RaiseOnMouseMove(short button, short shift, int x, int y) =>
        OnMouseMove(new MouseEventArgs(ConvertOleButton(button), 0, x, y, 0));

    protected void RaiseOnMouseMove(short button, short shift, float x, float y) =>
        RaiseOnMouseMove(button, shift, (int)x, (int)y);

    protected void RaiseOnMouseMove(object button, object shift, object x, object y) =>
        RaiseOnMouseMove(Convert.ToInt16(button), Convert.ToInt16(shift), Convert.ToInt32(x), Convert.ToInt32(y));

    protected void RaiseOnMouseUp(short button, short shift, int x, int y) =>
        OnMouseUp(new MouseEventArgs(ConvertOleButton(button), 1, x, y, 0));

    protected void RaiseOnMouseUp(short button, short shift, float x, float y) =>
        RaiseOnMouseUp(button, shift, (int)x, (int)y);

    protected void RaiseOnMouseUp(object button, object shift, object x, object y) =>
        RaiseOnMouseUp(Convert.ToInt16(button), Convert.ToInt16(shift), Convert.ToInt32(x), Convert.ToInt32(y));

    /// <summary>OLE's mouse button mask (bit 0 left, bit 1 right, bit 2 middle) translated to MouseButtons.</summary>
    private static MouseButtons ConvertOleButton(short button)
    {
        var result = MouseButtons.None;
        if ((button & 1) != 0) result |= MouseButtons.Left;
        if ((button & 2) != 0) result |= MouseButtons.Right;
        if ((button & 4) != 0) result |= MouseButtons.Middle;
        return result;
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, height, specified);

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(value);
}
