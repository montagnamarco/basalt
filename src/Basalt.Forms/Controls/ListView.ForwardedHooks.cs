namespace System.Windows.Forms;

/// <summary>
/// ListView declares these lifecycle hooks itself in the WinForms API. The
/// inherited Control implementations already manage handles, layout, focus,
/// painting state and event delivery for this implementation.
/// </summary>
public partial class ListView
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void CreateHandle() => base.CreateHandle();

    protected override bool IsInputKey(Keys keyData) => base.IsInputKey(keyData);

    protected override void OnBackgroundImageChanged(EventArgs e) => base.OnBackgroundImageChanged(e);

    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);

    protected override void OnGotFocus(EventArgs e) => base.OnGotFocus(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnHandleDestroyed(EventArgs e) => base.OnHandleDestroyed(e);

    protected override void OnLostFocus(EventArgs e) => base.OnLostFocus(e);

    protected override void OnParentChanged(EventArgs e) => base.OnParentChanged(e);

    protected override void OnResize(EventArgs e) => base.OnResize(e);

    protected override void OnSystemColorsChanged(EventArgs e) => base.OnSystemColorsChanged(e);

    protected override bool DoubleBuffered
    {
        get => base.DoubleBuffered;
        set => base.DoubleBuffered = value;
    }
}
