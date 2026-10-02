using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A button with a narrow drop-down arrow beside it: clicking the main face
/// raises <see cref="ButtonClick"/>, clicking the arrow opens the menu.
/// </summary>
public partial class ToolStripSplitButton : ToolStripDropDownItem
{
    private int _dropDownButtonWidth = 11;
    private bool _buttonPressed;
    private bool _dropDownButtonPressed;
    private ToolStripItem? _defaultItem;

    /// <summary>
    /// Which half the most recent press landed on, kept past the mouse-up
    /// reset of <see cref="_dropDownButtonPressed"/> so <see cref="DismissWhenClicked"/>
    /// can still tell, once the click itself is being processed, whether it
    /// should close a parent chain (the button half) or not (the arrow half,
    /// which only ever opens this item's own submenu).
    /// </summary>
    private bool _lastPressWasDropDownPart;

    public ToolStripSplitButton()
    {
    }

    public ToolStripSplitButton(string? text)
        : base(text, null, (EventHandler?)null)
    {
    }

    public ToolStripSplitButton(Image? image)
        : base(null, image, (EventHandler?)null)
    {
    }

    public ToolStripSplitButton(string? text, Image? image)
        : base(text, image, (EventHandler?)null)
    {
    }

    public ToolStripSplitButton(string? text, Image? image, EventHandler? onClick)
        : base(text, image, onClick)
    {
    }

    public ToolStripSplitButton(string? text, Image? image, params ToolStripItem[]? dropDownItems)
        : base(text, image, dropDownItems)
    {
    }

    public ToolStripSplitButton(string? text, Image? image, EventHandler? onClick, string? name)
        : base(text, image, onClick, name)
    {
    }

    [DefaultValue(true)]
    public new bool AutoToolTip
    {
        get => base.AutoToolTip;
        set => base.AutoToolTip = value;
    }

    /// <summary>The main part of the item: a click here raises ButtonClick, not the drop-down. Mirrored to the right edge under RightToLeft.</summary>
    [Browsable(false)]
    public Rectangle ButtonBounds => RightToLeft == RightToLeft.Yes
        ? new Rectangle(_dropDownButtonWidth, 0, Math.Max(Bounds.Width - _dropDownButtonWidth, 0), Bounds.Height)
        : new Rectangle(0, 0, Math.Max(Bounds.Width - _dropDownButtonWidth, 0), Bounds.Height);

    [Browsable(false)]
    public bool ButtonPressed => _buttonPressed;

    /// <summary>
    /// Approximated as "the item is hovered and the arrow part is not pressed": without an
    /// OnMouseMove override (Microsoft's own ToolStripSplitButton does not re-override it either)
    /// there is no per-half hover tracking, only per-half press tracking.
    /// </summary>
    [Browsable(false)]
    public bool ButtonSelected => Selected && !_dropDownButtonPressed;

    protected override bool DefaultAutoToolTip => true;

    /// <summary>The item clicking the button part performs, in addition to the button part's own Click/ButtonClick handlers.</summary>
    [DefaultValue(null)]
    [Browsable(false)]
    public ToolStripItem? DefaultItem
    {
        get => _defaultItem;
        set
        {
            if (value == _defaultItem) return;

            _defaultItem = value;
            OnDefaultItemChanged(EventArgs.Empty);
        }
    }

    /// <summary>Whether a click should close the drop-down chain this item is shown on: no while the arrow half is what was pressed, since that only opens this item's own submenu.</summary>
    protected override bool DismissWhenClicked => !_lastPressWasDropDownPart;

    [Browsable(false)]
    public Rectangle DropDownButtonBounds => RightToLeft == RightToLeft.Yes
        ? new Rectangle(0, 0, _dropDownButtonWidth, Bounds.Height)
        : new Rectangle(ButtonBounds.Right, 0, _dropDownButtonWidth, Bounds.Height);

    [Browsable(false)]
    public bool DropDownButtonPressed => _dropDownButtonPressed;

    [Browsable(false)]
    public bool DropDownButtonSelected => Selected && _dropDownButtonPressed;

    public int DropDownButtonWidth
    {
        get => _dropDownButtonWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            if (value == _dropDownButtonWidth) return;

            _dropDownButtonWidth = value;
            Owner?.DoLayout(this, nameof(DropDownButtonWidth));
            Invalidate();
        }
    }

    /// <summary>The thin dividing line between the button and the arrow, wherever RightToLeft currently puts it.</summary>
    [Browsable(false)]
    public Rectangle SplitterBounds => new(Math.Min(ButtonBounds.Right, DropDownButtonBounds.Right) - 1, 0, 1, Bounds.Height);

    public event EventHandler? ButtonClick;

    public event EventHandler? ButtonDoubleClick;

    public event EventHandler? DefaultItemChanged;

    protected override AccessibleObject CreateAccessibilityInstance() => new ToolStripSplitButtonAccessibleObject(this);

    /// <summary>The split button's own submenu looks like a menu (image margin, checkmarks), the same shape ToolStripMenuItem's own uses.</summary>
    protected override ToolStripDropDown CreateDefaultDropDown() => new ToolStripDropDownMenu { OwnerItem = this };

    public override Size GetPreferredSize(Size constrainingSize)
    {
        var size = base.GetPreferredSize(constrainingSize);

        return new Size(size.Width + _dropDownButtonWidth, size.Height);
    }

    public void PerformButtonClick()
    {
        if (!Enabled) return;

        OnButtonClick(EventArgs.Empty);
    }

    /// <summary>Raises ButtonClick, then performs DefaultItem's own action, if one was chosen from the drop-down to stand in as this button's quick action.</summary>
    protected virtual void OnButtonClick(EventArgs e)
    {
        ButtonClick?.Invoke(this, e);
        DefaultItem?.PerformClick();
    }

    public virtual void OnButtonDoubleClick(EventArgs e) => ButtonDoubleClick?.Invoke(this, e);

    protected virtual void OnDefaultItemChanged(EventArgs e) => DefaultItemChanged?.Invoke(this, e);

    /// <summary>Which half of the button the point (in the strip's own, absolute coordinates) is in.</summary>
    private bool IsOverDropDownPart(Point location) =>
        RightToLeft == RightToLeft.Yes
            ? location.X - Bounds.Left < _dropDownButtonWidth
            : location.X - Bounds.Left >= ButtonBounds.Width;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (IsOverDropDownPart(e.Location))
        {
            _dropDownButtonPressed = true;
            _lastPressWasDropDownPart = true;
            if (HasDropDown && DropDown.IsOpen) HideDropDown();
            else ShowDropDown();
        }
        else
        {
            _buttonPressed = true;
            _lastPressWasDropDownPart = false;
        }

        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        var wasButtonPressed = _buttonPressed;
        _buttonPressed = false;
        _dropDownButtonPressed = false;
        Invalidate();

        // OnMouseUp only runs here when the release landed on this item (the owning strip already checked that).
        if (wasButtonPressed && !IsOverDropDownPart(e.Location)) PerformButtonClick();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        _buttonPressed = false;
        _dropDownButtonPressed = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var renderer = Owner?.Renderer;
        if (renderer is null)
        {
            base.OnPaint(e);
            return;
        }

        renderer.DrawSplitButton(new ToolStripItemRenderEventArgs(e.Graphics, this));
        ToolStripItemPainting.DrawImageAndText(renderer, e.Graphics, this);

        var dropDownArea = DropDownButtonBounds;
        dropDownArea.Offset(Bounds.Location);
        var arrowArea = Rectangle.Inflate(dropDownArea, -3, -3);
        renderer.DrawArrow(new ToolStripArrowRenderEventArgs(e.Graphics, this, arrowArea, ForeColor, ArrowDirection.Down));

        base.OnPaint(e);
    }

    /// <summary>Repaints so the mirrored ButtonBounds/DropDownButtonBounds above show up on the correct side right away.</summary>
    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);
        Invalidate();
    }

    /// <summary>Enter/Space performs the button's own action; Down (like a combo box) opens the submenu instead.</summary>
    protected internal override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData is Keys.Enter or Keys.Space)
        {
            PerformButtonClick();
            return true;
        }

        if (keyData == Keys.Down && HasDropDownItems)
        {
            ShowDropDown();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    /// <summary>The mnemonic performs the button's own default action, the part a plain click would otherwise reach.</summary>
    protected internal override bool ProcessMnemonic(char charCode)
    {
        if (!Enabled || !Available || !Control.IsMnemonic(charCode, Text)) return false;

        PerformButtonClick();
        return true;
    }

    /// <summary>Back to the item's own construction-time default (11), the same value DropDownButtonWidth starts with.</summary>
    public virtual void ResetDropDownButtonWidth() => DropDownButtonWidth = 11;
}
