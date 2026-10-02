using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// FlatAppearance only changes anything once <see cref="ButtonBase.FlatStyle"/>
/// is Flat or Popup: Standard and System keep the 3D edges PaintButton
/// already draws. Hover tracking lives here because Standard buttons have no
/// concept of "mouse over" worth painting, so ButtonBase itself never needed it before.
/// </summary>
public abstract partial class ButtonBase
{
    private FlatButtonAppearance? _flatAppearance;
    private bool _mouseInside;

    /// <summary>The flat/popup border and highlight colours, created the first time anyone asks.</summary>
    public FlatButtonAppearance FlatAppearance => _flatAppearance ??= new FlatButtonAppearance(this);

    protected override void OnMouseEnter(EventArgs e)
    {
        _mouseInside = true;
        if (_flatStyle is FlatStyle.Flat or FlatStyle.Popup) Invalidate();

        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _mouseInside = false;
        if (_flatStyle is FlatStyle.Flat or FlatStyle.Popup) Invalidate();

        base.OnMouseLeave(e);
    }

    /// <summary>
    /// The background a flat/popup button paints: FlatAppearance's colour for
    /// whichever state is current (pressed, then checked-and-resting, then
    /// hovered), falling back to the plain BackColor once none applies or no
    /// appearance was ever created.
    /// </summary>
    private protected Color FlatPaintBackColor
    {
        get
        {
            if (_flatAppearance is not { } appearance) return BackColor;

            if (InputPressed && appearance.MouseDownBackColor != Color.Empty) return appearance.MouseDownBackColor;
            if (Pushed && !InputPressed && appearance.CheckedBackColor != Color.Empty) return appearance.CheckedBackColor;
            if (_mouseInside && appearance.MouseOverBackColor != Color.Empty) return appearance.MouseOverBackColor;

            return BackColor;
        }
    }

    /// <summary>The flat/popup border colour: FlatAppearance.BorderColor when set, else ForeColor (Flat's long-standing default) or the 3D grey Popup falls back to.</summary>
    private protected Color FlatPaintBorderColor
    {
        get
        {
            if (_flatAppearance is { BorderColor: var color } && color != Color.Empty) return color;

            return _flatStyle == FlatStyle.Flat ? ForeColor : SystemColors.ControlDark;
        }
    }

    private protected int FlatPaintBorderSize => _flatAppearance?.BorderSize ?? 1;
}
