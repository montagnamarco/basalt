using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The overflow of a strip too short for its items: what does not fit moves
/// to <see cref="OverflowButton"/>'s drop-down. As in WinForms the item stays
/// in <see cref="Items"/> with the same owner; only its
/// <see cref="ToolStripItem.Placement"/> and the strip that shows it change,
/// so code walking Items still sees every item in its order.
/// </summary>
public partial class ToolStrip
{
    /// <summary>The fixed width (horizontal) or height (vertical) of the overflow button, reserved at the strip's trailing edge.</summary>
    private const int OverflowButtonThickness = 13;

    private bool _canOverflow = true;
    private ToolStripOverflowButton? _overflowButton;

    /// <summary>Whether the last layout showed the overflow button (something is on its drop-down).</summary>
    private bool _overflowActive;

    /// <summary>
    /// Whether, in a *WithOverflow <see cref="LayoutStyle"/>, an item that does
    /// not fit moves to <see cref="OverflowButton"/>'s drop-down instead of
    /// being left off the strip's end. MenuStrip and StatusStrip hide this at
    /// false: a menu bar or a status bar is never too narrow in that sense.
    /// </summary>
    [DefaultValue(true)]
    public bool CanOverflow
    {
        get => _canOverflow;
        set
        {
            if (value == _canOverflow) return;

            _canOverflow = value;
            DoLayout(this, nameof(CanOverflow));
        }
    }

    /// <summary>The button shown at the strip's trailing edge once something no longer fits; created on first use.</summary>
    [Browsable(false)]
    public ToolStripOverflowButton OverflowButton
    {
        get
        {
            if (_overflowButton is { } existing) return existing;

            var button = new ToolStripOverflowButton();
            button.SetParentInternal(this);
            _overflowButton = button;

            // Out of sight until a layout finds something that does not fit.
            Place(button, Rectangle.Empty);

            return button;
        }
    }

    /// <summary>The items the last layout moved to the overflow drop-down, in their Items order, without taking them from this strip.</summary>
    internal ToolStripItemCollection OverflowItems
    {
        get
        {
            var overflowed = new List<ToolStripItem>();
            foreach (ToolStripItem item in Items)
            {
                if (item.Available && item.Placement == ToolStripItemPlacement.Overflow) overflowed.Add(item);
            }

            return ToolStripItemCollection.CreateView(this, overflowed);
        }
    }

    /// <summary>Whether this strip draws and hit-tests the item now: an item it owns but lent to the overflow drop-down is not.</summary>
    private bool IsShownHere(ToolStripItem item) => item.Available && item.GetCurrentParent() == this;

    /// <summary>Whether this strip's own layout places the item; the overflow drop-down places the overflowed ones.</summary>
    internal virtual bool LaysOut(ToolStripItem item) => item.Available && item.Placement != ToolStripItemPlacement.Overflow;

    /// <summary>
    /// Decides, from a clean slate every time, which items sit on the strip and
    /// which overflow, so an item that fits again (the strip grew) comes back.
    /// Right-aligned items and Never items always stay; Always items always go.
    /// </summary>
    private void ApplyOverflow()
    {
        var supportsOverflow = _canOverflow
            && this is not ToolStripDropDown
            && LayoutStyle is ToolStripLayoutStyle.HorizontalStackWithOverflow or ToolStripLayoutStyle.VerticalStackWithOverflow;

        if (!supportsOverflow)
        {
            // A drop-down always sizes itself to everything it has; the overflow drop-down keeps the
            // placements its strip gave to the items it borrows, so it does not decide any here.
            if (this is not ToolStripOverflow)
            {
                foreach (ToolStripItem item in Items) item.SetPlacementInternal(item.Available ? ToolStripItemPlacement.Main : ToolStripItemPlacement.None);
            }

            _overflowActive = false;
            return;
        }

        var available = (StacksVertically ? DisplayRectangle.Height : DisplayRectangle.Width) - LeadingGripSpace();
        var required = RequiredLength(out var fixedLength, out var hasAlways);
        var willOverflow = required > available || hasAlways;
        if (willOverflow) available -= OverflowButtonThickness;

        // Items that never move take their space first; AsNeeded items share what is left.
        available -= fixedLength;
        var used = 0;
        var overflowing = false;

        foreach (ToolStripItem item in Items)
        {
            var placement = PlacementOf(item, available, ref used, ref overflowing);
            item.SetPlacementInternal(placement);

            // An overflowed item is shown by the drop-down from now on, not by this strip.
            if (placement == ToolStripItemPlacement.Overflow) item.SetDisplayParentInternal(OverflowButton.DropDown);
        }

        _overflowActive = willOverflow;
    }

    /// <summary>The main-axis length of every item that may stay on the strip, and the part of it taken by items that never move.</summary>
    private int RequiredLength(out int fixedLength, out bool hasAlways)
    {
        var required = 0;
        fixedLength = 0;
        hasAlways = false;

        foreach (ToolStripItem item in Items)
        {
            if (!item.Available) continue;

            if (item.Overflow == ToolStripItemOverflow.Always)
            {
                hasAlways = true;
                continue;
            }

            var length = MainAxisLength(item);
            required += length;
            if (NeverMoves(item)) fixedLength += length;
        }

        return required;
    }

    /// <summary>A right-aligned or Never item stays on the strip whatever the space.</summary>
    private static bool NeverMoves(ToolStripItem item) => item.Alignment == ToolStripItemAlignment.Right || item.Overflow == ToolStripItemOverflow.Never;

    /// <summary>
    /// One item's placement, in Items order: available is what the AsNeeded
    /// items share. Once one of them overflows, those after it follow it, so
    /// the drop-down keeps the strip's order.
    /// </summary>
    private ToolStripItemPlacement PlacementOf(ToolStripItem item, int available, ref int used, ref bool overflowing)
    {
        if (!item.Available) return ToolStripItemPlacement.None;
        if (item.Overflow == ToolStripItemOverflow.Always) return ToolStripItemPlacement.Overflow;
        if (NeverMoves(item)) return ToolStripItemPlacement.Main;

        var length = MainAxisLength(item);
        if (!overflowing && used + length <= available)
        {
            used += length;
            return ToolStripItemPlacement.Main;
        }

        overflowing = true;
        return ToolStripItemPlacement.Overflow;
    }

    private int MainAxisLength(ToolStripItem item)
    {
        var size = MeasureItem(item);
        var margin = item.Margin;

        return StacksVertically ? size.Height + margin.Vertical : size.Width + margin.Horizontal;
    }

    /// <summary>Puts the overflow button at the strip's trailing edge when active, or out of the way when it is not.</summary>
    private void PlaceOverflowButton(Rectangle display, bool horizontal)
    {
        if (!_overflowActive)
        {
            if (_overflowButton is { } button) Place(button, Rectangle.Empty);
            return;
        }

        var bounds = horizontal
            ? new Rectangle(display.Right - OverflowButtonThickness, display.Top, OverflowButtonThickness, display.Height)
            : new Rectangle(display.Left, display.Bottom - OverflowButtonThickness, display.Width, OverflowButtonThickness);

        Place(OverflowButton, bounds);
    }
}
