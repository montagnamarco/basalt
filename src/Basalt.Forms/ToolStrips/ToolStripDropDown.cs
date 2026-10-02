using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// A <see cref="ToolStrip"/> shown as a borderless popup instead of being
/// docked on a form: the body of a menu, a drop-down button's list, a
/// context menu. Its items always stack top to bottom, and the keyboard
/// (arrows, Enter, Escape) navigates them while it is open.
/// </summary>
public class ToolStripDropDown : ToolStrip
{
    private ToolStripItem? _activeItem;
    private ToolStripDropDownDirection _dropDownDirection = ToolStripDropDownDirection.Default;

    public ToolStripDropDown()
    {
        SetTopLevel(true);
        AutoSize = true;
        GripStyle = ToolStripGripStyle.Hidden;
        Dock = DockStyle.None;

        // Through the public event, not an override of OnItemRemoved, which Microsoft's own
        // ToolStripDropDown does not re-override either.
        ItemRemoved += (_, e) => ForgetActiveItem(e.Item);
    }

    /// <summary>Whether a press outside the drop-down closes it, as a menu or a combo list does.</summary>
    [DefaultValue(true)]
    public bool AutoClose { get; set; } = true;

    /// <summary>The item this drop-down belongs to, so keyboard navigation can walk back up a menu chain.</summary>
    [DefaultValue(null)]
    [Browsable(false)]
    public ToolStripItem? OwnerItem { get; set; }

    public override ToolStripDropDownDirection DefaultDropDownDirection
    {
        get => _dropDownDirection;
        set => _dropDownDirection = value;
    }

    protected override DockStyle DefaultDock => DockStyle.None;

    protected override Padding DefaultPadding => new(1, 2, 1, 2);

    protected override bool DefaultShowItemToolTips => true;

    /// <summary>A drop-down is always a vertical list, whatever LayoutStyle says.</summary>
    internal override bool StacksVertically => true;

    internal override bool ClosesOnOutsideClick => AutoClose;

    internal override bool TakesKeyboardWhileOpen => true;

    /// <summary>The item currently highlighted by the mouse or the keyboard, if any.</summary>
    internal ToolStripItem? ActiveItem => _activeItem;

    internal bool IsOpen => Popups.All.Contains(this);

    /// <summary>
    /// The real, original window this drop-down ultimately opened from,
    /// walking up through any outer drop-downs: what every level of a nested
    /// menu registers as its popup owner, since the keyboard-owner lookup
    /// only ever checks that one window, never a chain of popups.
    /// </summary>
    internal Control? RootOwnerWindow => OwnerItem?.ParentInternal is ToolStripDropDown parent ? parent.RootOwnerWindow : OwnerItem?.ParentInternal;

    /// <summary>Whether this drop-down's owner item itself lives inside another, outer drop-down.</summary>
    private bool IsNestedSubmenu => OwnerItem?.ParentInternal is ToolStripDropDown;

    public event ToolStripDropDownClosedEventHandler? Closed;

    public event ToolStripDropDownClosingEventHandler? Closing;

    public event EventHandler? Opened;

    public event CancelEventHandler? Opening;

    /// <summary>
    /// Called by an item this drop-down shows when its selection changes. The
    /// item reports it rather than this drop-down subscribing on ItemAdded,
    /// because the overflow drop-down shows items that were never added to it.
    /// </summary>
    internal void OnShownItemSelectedChanged(ToolStripItem item)
    {
        if (item.Selected) _activeItem = item;
        else if (_activeItem == item) _activeItem = null;
    }

    /// <summary>The item left this drop-down (removed, or taken back by the strip that lent it).</summary>
    internal void ForgetActiveItem(ToolStripItem? item)
    {
        if (_activeItem == item) _activeItem = null;
    }

    /// <summary>Highlights the item, as hovering or arrowing to it does; null clears the highlight.</summary>
    internal void SetActiveItem(ToolStripItem? item)
    {
        if (_activeItem == item) return;

        _activeItem?.SetSelectedInternal(false);
        item?.SetSelectedInternal(true);
    }

    /// <summary>Highlights the first selectable item, as opening the drop-down with the keyboard does.</summary>
    internal void SelectFirstAvailableItem() => SetActiveItem(GetNextItem(null, ArrowDirection.Down));

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new void Show() => ShowAt(DefaultShowBounds(), ComputeDirection(), OwnerControl());

    public void Show(Control control, Point position) => Show(control, position, ToolStripDropDownDirection.Default);

    public void Show(Control control, Point position, ToolStripDropDownDirection direction)
    {
        ArgumentNullException.ThrowIfNull(control);

        var anchor = control.RectangleToScreen(new Rectangle(position, Size.Empty));
        ShowAt(anchor, direction, control);
    }

    public void Show(Control control, int x, int y) => Show(control, new Point(x, y));

    public void Show(Point screenLocation) => ShowAt(new Rectangle(screenLocation, Size.Empty), ToolStripDropDownDirection.BelowRight, OwnerControl());

    public void Show(Point position, ToolStripDropDownDirection direction) => ShowAt(new Rectangle(position, Size.Empty), direction, OwnerControl());

    public void Show(int x, int y) => Show(new Point(x, y));

    /// <summary>Shows the drop-down anchored to a screen rectangle (an item's or a control's bounds), in the given direction.</summary>
    internal virtual void ShowAt(Rectangle anchor, ToolStripDropDownDirection direction, Control? owner)
    {
        if (!IsOpen)
        {
            var opening = new CancelEventArgs();
            OnOpening(opening);
            if (opening.Cancel) return;
        }

        Size = GetPreferredSize(Size.Empty);
        var location = KeepOnScreen(anchor, direction, Size);
        var wasOpen = IsOpen;
        Popups.Show(this, owner, new Rectangle(location, Size));

        if (!wasOpen) OnOpened(EventArgs.Empty);
    }

    private Rectangle DefaultShowBounds()
    {
        if (OwnerItem is { Owner: { } strip })
        {
            return strip.RectangleToScreen(OwnerItem.Bounds);
        }

        return new Rectangle(MousePosition, Size.Empty);
    }

    private Control? OwnerControl() => OwnerItem?.Owner;

    private ToolStripDropDownDirection ComputeDirection() => _dropDownDirection == ToolStripDropDownDirection.Default
        ? (IsNestedSubmenu ? ToolStripDropDownDirection.Right : ToolStripDropDownDirection.BelowLeft)
        : _dropDownDirection;

    /// <summary>Where the drop-down's top-left goes for an anchor rectangle and a direction, in screen coordinates.</summary>
    private static Point PlacementFor(Rectangle anchor, ToolStripDropDownDirection direction, Size size) => direction switch
    {
        ToolStripDropDownDirection.AboveLeft => new Point(anchor.Left, anchor.Top - size.Height),
        ToolStripDropDownDirection.AboveRight => new Point(anchor.Right - size.Width, anchor.Top - size.Height),
        ToolStripDropDownDirection.BelowRight => new Point(anchor.Right - size.Width, anchor.Bottom),
        ToolStripDropDownDirection.Left => new Point(anchor.Left - size.Width, anchor.Top),
        ToolStripDropDownDirection.Right => new Point(anchor.Right, anchor.Top),
        _ => new Point(anchor.Left, anchor.Bottom),
    };

    /// <summary>
    /// The placement for the direction, flipped to the other side of the anchor
    /// when it would leave the screen's working area (a menu near the bottom
    /// opens upwards, a submenu near the right edge opens to the left), then
    /// pushed inside the working area if it still does not fit.
    /// </summary>
    private static Point KeepOnScreen(Rectangle anchor, ToolStripDropDownDirection direction, Size size)
    {
        var area = Screen.GetWorkingArea(anchor.Location);
        var location = PlacementFor(anchor, direction, size);

        if (location.Y + size.Height > area.Bottom && anchor.Top - size.Height >= area.Top)
        {
            location.Y = direction is ToolStripDropDownDirection.Left or ToolStripDropDownDirection.Right
                ? anchor.Bottom - size.Height
                : anchor.Top - size.Height;
        }

        if (location.X + size.Width > area.Right)
        {
            location.X = direction == ToolStripDropDownDirection.Right && anchor.Left - size.Width >= area.Left
                ? anchor.Left - size.Width
                : area.Right - size.Width;
        }

        // Still too low (taller than the room above and below): pull it up so its bottom shows, but never above the top.
        location.Y = Math.Min(location.Y, area.Bottom - size.Height);

        location.X = Math.Max(location.X, area.Left);
        location.Y = Math.Max(location.Y, area.Top);

        return location;
    }

    public void Close() => Close(ToolStripDropDownCloseReason.CloseCalled);

    /// <summary>Closes this drop-down (and, first, any of its own open submenus) for the given reason.</summary>
    public void Close(ToolStripDropDownCloseReason reason) => CloseCore(reason, cancellable: true);

    /// <summary>Closes even if a Closing handler cancels: the item that owned the drop-down is gone.</summary>
    internal void CloseUncancellable(ToolStripDropDownCloseReason reason) => CloseCore(reason, cancellable: false);

    private void CloseCore(ToolStripDropDownCloseReason reason, bool cancellable)
    {
        if (!IsOpen) return;

        // A cancelled close leaves everything open, submenus included: ask before closing anything.
        var closing = new ToolStripDropDownClosingEventArgs(reason);
        OnClosing(closing);
        if (closing.Cancel && cancellable) return;

        foreach (ToolStripItem item in DisplayedItems)
        {
            if (item is ToolStripDropDownItem { HasDropDown: true } dropDownItem && dropDownItem.DropDown.IsOpen)
            {
                dropDownItem.DropDown.CloseCore(reason, cancellable);
            }
        }

        _activeItem?.SetSelectedInternal(false);
        _activeItem = null;
        Popups.Close(this);

        OnClosed(new ToolStripDropDownClosedEventArgs(reason));
    }

    internal override void DismissPopup() => Close(ToolStripDropDownCloseReason.AppClicked);

    /// <summary>A press lands in one of this drop-down's own open submenus: it must not be treated as outside.</summary>
    internal override bool IsPopupAncestorOf(Control other)
    {
        foreach (ToolStripItem item in DisplayedItems)
        {
            if (item is not ToolStripDropDownItem { HasDropDown: true } dropDownItem) continue;
            if (dropDownItem.DropDown == other || dropDownItem.DropDown.IsPopupAncestorOf(other)) return true;
        }

        return false;
    }

    /// <summary>A drop-down always wants the keys that navigate and activate its own items.</summary>
    internal override bool WantsNavigationKey(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;

        return code is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Enter or Keys.Escape or Keys.Space or Keys.Tab;
    }

    internal override bool ProcessNavigationKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Down:
                SetActiveItem(GetNextItem(_activeItem, ArrowDirection.Down));
                return true;

            case Keys.Up:
                SetActiveItem(GetNextItem(_activeItem, ArrowDirection.Up));
                return true;

            case Keys.Right:
                HandleRightArrow();
                return true;

            case Keys.Left:
                HandleLeftArrow();
                return true;

            case Keys.Enter:
            case Keys.Space:
                ActivateSelection();
                return true;

            case Keys.Escape:
                Close(ToolStripDropDownCloseReason.Keyboard);
                return true;

            default:
                return false;
        }
    }

    private void ActivateSelection()
    {
        if (_activeItem is not { Enabled: true, CanSelect: true } item) return;

        if (item is ToolStripDropDownItem { HasDropDownItems: true } withChildren)
        {
            withChildren.ShowDropDown();
            withChildren.DropDown.SelectFirstAvailableItem();
            return;
        }

        item.PerformClick();
        OnItemClicked(new ToolStripItemClickedEventArgs(item));
    }

    private void HandleRightArrow()
    {
        if (_activeItem is ToolStripDropDownItem { HasDropDownItems: true } withChildren)
        {
            withChildren.ShowDropDown();
            withChildren.DropDown.SelectFirstAvailableItem();
            return;
        }

        MoveToAdjacentTopLevelItem(forward: true);
    }

    private void HandleLeftArrow()
    {
        if (IsNestedSubmenu)
        {
            Close(ToolStripDropDownCloseReason.Keyboard);
            return;
        }

        MoveToAdjacentTopLevelItem(forward: false);
    }

    /// <summary>Walks up the chain of owning items to the MenuStrip item that ultimately opened it, if any.</summary>
    private (MenuStrip? Bar, ToolStripMenuItem? TopItem) FindMenuBarChain()
    {
        for (var dropDown = this; dropDown?.OwnerItem is ToolStripMenuItem ownerItem; dropDown = ownerItem.ParentInternal as ToolStripDropDown)
        {
            if (ownerItem.Owner is MenuStrip bar) return (bar, ownerItem);
        }

        return (null, null);
    }

    private void MoveToAdjacentTopLevelItem(bool forward)
    {
        var (bar, topItem) = FindMenuBarChain();
        if (bar is null || topItem is null) return;

        var next = bar.GetNextItem(topItem, forward ? ArrowDirection.Right : ArrowDirection.Left);

        topItem.DropDown.Close(ToolStripDropDownCloseReason.Keyboard);

        if (next is not ToolStripMenuItem nextItem) return;

        nextItem.ShowDropDown();
        nextItem.DropDown.SelectFirstAvailableItem();
    }

    /// <summary>
    /// A leaf item was clicked: the whole drop-down (not a submenu this very
    /// click just opened) closes. DismissWhenClicked is the item's own veto
    /// (a split button's arrow half says no, since clicking it only ever
    /// opens its own submenu, never a reason to close anything); whether its
    /// own drop-down is open right now covers every other item that owns one
    /// (a submenu entry's click already opened - or re-closed - that, and
    /// either way is not this ancestor's to react to the same click for).
    /// </summary>
    protected internal override void OnItemClicked(ToolStripItemClickedEventArgs e)
    {
        base.OnItemClicked(e);

        if (e.ClickedItem is not { } clickedItem || !clickedItem.DismissWhenClickedInternal) return;
        if (clickedItem is ToolStripDropDownItem { HasDropDown: true, DropDown.IsOpen: true }) return;

        Close(ToolStripDropDownCloseReason.ItemClicked);
    }

    protected virtual void OnOpened(EventArgs e) => Opened?.Invoke(this, e);

    protected virtual void OnOpening(CancelEventArgs e) => Opening?.Invoke(this, e);

    protected virtual void OnClosed(ToolStripDropDownClosedEventArgs e) => Closed?.Invoke(this, e);

    protected virtual void OnClosing(ToolStripDropDownClosingEventArgs e) => Closing?.Invoke(this, e);

    protected override void Dispose(bool disposing)
    {
        if (disposing && IsOpen) Popups.Close(this);

        base.Dispose(disposing);
    }
}
