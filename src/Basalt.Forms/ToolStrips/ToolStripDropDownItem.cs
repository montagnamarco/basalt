using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A <see cref="ToolStripItem"/> that owns its own <see cref="ToolStripDropDown"/>:
/// a menu item with a submenu, a drop-down button, a split button's arrow.
/// </summary>
[DefaultProperty("DropDownItems")]
public abstract class ToolStripDropDownItem : ToolStripItem
{
    private ToolStripDropDown? _dropDown;
    private ToolStripDropDownDirection _dropDownDirection = ToolStripDropDownDirection.Default;
    private bool _dropDownShown;

    protected ToolStripDropDownItem()
    {
    }

    protected ToolStripDropDownItem(string? text, Image? image, EventHandler? onClick)
        : base(text, image, onClick)
    {
    }

    protected ToolStripDropDownItem(string? text, Image? image, params ToolStripItem[]? dropDownItems)
        : base(text, image, null)
    {
        if (dropDownItems is { Length: > 0 }) DropDownItems.AddRange(dropDownItems);
    }

    protected ToolStripDropDownItem(string? text, Image? image, EventHandler? onClick, string? name)
        : base(text, image, onClick, name)
    {
    }

    /// <summary>The submenu (or button's list): created on first use unless one was assigned.</summary>
    public ToolStripDropDown DropDown
    {
        get => _dropDown ??= Wire(CreateDefaultDropDown());
        set
        {
            if (value == _dropDown) return;

            Unwire(_dropDown);
            _dropDown = value is null ? null : Wire(value);
        }
    }

    [Browsable(false)]
    public ToolStripDropDownDirection DropDownDirection
    {
        get => _dropDownDirection;
        set => _dropDownDirection = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ToolStripItemCollection DropDownItems => DropDown.Items;

    protected virtual Point DropDownLocation => ParentInternal is not { } parent ? Point.Empty : parent.PointToScreen(new Point(Bounds.Left, Bounds.Bottom));

    /// <summary>Whether a DropDown exists yet, without creating one just to ask.</summary>
    [Browsable(false)]
    public bool HasDropDown => _dropDown is not null;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual bool HasDropDownItems => _dropDown is { Items.Count: > 0 };

    /// <summary>Drawn sunken while its own drop-down is open, as a pressed button would be.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override bool Pressed => base.Pressed || _dropDownShown;

    public event EventHandler? DropDownClosed;

    public event ToolStripItemClickedEventHandler? DropDownItemClicked;

    public event EventHandler? DropDownOpened;

    public event EventHandler? DropDownOpening;

    protected virtual ToolStripDropDown CreateDefaultDropDown() => new() { OwnerItem = this };

    private ToolStripDropDown Wire(ToolStripDropDown dropDown)
    {
        dropDown.OwnerItem = this;
        dropDown.Opened += OnDropDownOpenedCore;
        dropDown.Closed += OnDropDownClosedCore;
        dropDown.ItemClicked += OnDropDownItemClickedCore;

        return dropDown;
    }

    private void Unwire(ToolStripDropDown? dropDown)
    {
        if (dropDown is null) return;

        dropDown.Opened -= OnDropDownOpenedCore;
        dropDown.Closed -= OnDropDownClosedCore;
        dropDown.ItemClicked -= OnDropDownItemClickedCore;
    }

    private void OnDropDownOpenedCore(object? sender, EventArgs e)
    {
        _dropDownShown = true;
        Invalidate();
        OnDropDownOpened(e);
    }

    private void OnDropDownClosedCore(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        _dropDownShown = false;
        Invalidate();
        OnDropDownClosed(EventArgs.Empty);

        // A click anywhere in the chain collapses the whole chain, as Windows closes every level of an opened menu at once.
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && ParentInternal is ToolStripDropDown parent)
        {
            parent.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        // MenuStrip's own menu-mode tracking lives here (not an OnDropDownClosed override on
        // ToolStripMenuItem: Microsoft's own does not re-override it at that level either).
        if (this is ToolStripMenuItem menuItem && Owner is MenuStrip bar) bar.NotifyTopItemClosed(menuItem);
    }

    private void OnDropDownItemClickedCore(object? sender, ToolStripItemClickedEventArgs e) => OnDropDownItemClicked(e);

    /// <summary>Shows the drop-down near this item, picking above/below and left/right from the direction and the context.</summary>
    public void ShowDropDown()
    {
        if (ParentInternal is not { } shownOn) return;

        OnDropDownShow(EventArgs.Empty);
        DropDownOpening?.Invoke(this, EventArgs.Empty);

        var anchor = shownOn.RectangleToScreen(Bounds);
        var direction = EffectiveDropDownDirection();

        // The keyboard-owner lookup only ever checks the real, original window (FormsInput.KeyTarget
        // starts there, never recursing through a chain of popups), so every level of a nested menu
        // must register as owned by that same window, not by the drop-down one level up.
        var rootOwner = shownOn is ToolStripDropDown ownerDropDown ? ownerDropDown.RootOwnerWindow : shownOn;
        DropDown.ShowAt(anchor, direction, rootOwner);
    }

    // A drop-down left open by an item that was removed or moved would float on with nothing owning it.
    internal override void ParentChangedInternal()
    {
        if (_dropDown is { IsOpen: true } open) open.CloseUncancellable(ToolStripDropDownCloseReason.CloseCalled);
    }

    public void HideDropDown()
    {
        if (!HasDropDown) return;

        OnDropDownHide(EventArgs.Empty);
        DropDown.Close(ToolStripDropDownCloseReason.CloseCalled);
    }

    private ToolStripDropDownDirection EffectiveDropDownDirection()
    {
        if (_dropDownDirection != ToolStripDropDownDirection.Default) return _dropDownDirection;

        // Inside another drop-down a submenu opens to the side; on a strip (a toolbar, a menu bar) it opens below.
        return ParentInternal is ToolStripDropDown ? ToolStripDropDownDirection.Right : ToolStripDropDownDirection.BelowLeft;
    }

    protected override void OnBoundsChanged()
    {
        base.OnBoundsChanged();

        if (_dropDownShown) ShowDropDown();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ToolStripDropDownItemAccessibleObject(this);

    /// <summary>An open submenu measures its items from this one's Font; refreshed so a live Font change does not leave it stale.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        if (HasDropDown && DropDown.IsOpen) DropDown.PerformLayout();
    }

    /// <summary>A submenu reads its own direction ambiently, same as any other control: it mirrors once its owner does.</summary>
    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);

        if (HasDropDown) DropDown.RightToLeft = RightToLeft;
    }

    /// <summary>While the submenu is open, each of its own items gets first refusal at a command key.</summary>
    protected internal override bool ProcessCmdKey(ref Message m, Keys keyData)
    {
        if (HasDropDown && DropDown.IsOpen)
        {
            foreach (ToolStripItem item in DropDownItems)
            {
                if (item.ProcessCmdKey(ref m, keyData)) return true;
            }
        }

        return base.ProcessCmdKey(ref m, keyData);
    }

    /// <summary>Enter (or Space) opens the submenu instead of clicking, when there is one to open.</summary>
    protected internal override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData == Keys.Enter || keyData == Keys.Space) && HasDropDownItems)
        {
            ShowDropDown();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected virtual void OnDropDownClosed(EventArgs e) => DropDownClosed?.Invoke(this, e);

    /// <summary>Called right as the drop-down is about to disappear, before it actually closes.</summary>
    protected virtual void OnDropDownHide(EventArgs e)
    {
    }

    protected virtual void OnDropDownItemClicked(ToolStripItemClickedEventArgs e) => DropDownItemClicked?.Invoke(this, e);

    protected virtual void OnDropDownOpened(EventArgs e) => DropDownOpened?.Invoke(this, e);

    /// <summary>Called right as the drop-down is about to appear, before it actually shows.</summary>
    protected virtual void OnDropDownShow(EventArgs e)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unwire(_dropDown);
            _dropDown?.Dispose();
            _dropDown = null;
        }

        base.Dispose(disposing);
    }
}
