using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A button with a narrow drop-down arrow beside it: clicking the main face
/// raises <see cref="ButtonClick"/>, clicking the arrow opens the menu.
/// </summary>
public class ToolStripSplitButton : ToolStripDropDownItem
{
    private int _dropDownButtonWidth = 11;
    private bool _buttonPressed;
    private bool _dropDownButtonPressed;

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
    public bool AutoToolTip { get; set; } = true;

    /// <summary>The main, left part of the item: a click here raises ButtonClick, not the drop-down.</summary>
    [Browsable(false)]
    public Rectangle ButtonBounds => new(0, 0, Math.Max(Bounds.Width - _dropDownButtonWidth, 0), Bounds.Height);

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

    /// <summary>The item clicking the button part performs, if different from the button part's own Click handlers.</summary>
    [DefaultValue(null)]
    [Browsable(false)]
    public ToolStripItem? DefaultItem { get; set; }

    [Browsable(false)]
    public Rectangle DropDownButtonBounds => new(ButtonBounds.Right, 0, _dropDownButtonWidth, Bounds.Height);

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

    public event EventHandler? ButtonClick;

    public event EventHandler? ButtonDoubleClick;

    public event EventHandler? DefaultItemChanged;

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

    protected virtual void OnButtonClick(EventArgs e) => ButtonClick?.Invoke(this, e);

    public virtual void OnButtonDoubleClick(EventArgs e) => ButtonDoubleClick?.Invoke(this, e);

    protected virtual void OnDefaultItemChanged(EventArgs e) => DefaultItemChanged?.Invoke(this, e);

    /// <summary>Which half of the button the point (in the strip's own, absolute coordinates) is in.</summary>
    private bool IsOverDropDownPart(Point location) => location.X - Bounds.Left >= ButtonBounds.Width;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (IsOverDropDownPart(e.Location))
        {
            _dropDownButtonPressed = true;
            if (HasDropDown && DropDown.IsOpen) HideDropDown();
            else ShowDropDown();
        }
        else
        {
            _buttonPressed = true;
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
}
