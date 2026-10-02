using System.ComponentModel;

namespace System.Windows.Forms;

public partial class Button
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassName = "Button";
            return parameters;
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DoubleClick
    {
        add => base.DoubleClick += value;
        remove => base.DoubleClick -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event MouseEventHandler? MouseDoubleClick
    {
        add => base.MouseDoubleClick += value;
        remove => base.MouseDoubleClick -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public AutoSizeMode AutoSizeMode
    {
        get => GetAutoSizeMode();
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan((int)value, (int)AutoSizeMode.GrowOnly);
            ArgumentOutOfRangeException.ThrowIfGreaterThan((int)value, (int)AutoSizeMode.GrowAndShrink);

            SetAutoSizeMode(value);
        }
    }
}
