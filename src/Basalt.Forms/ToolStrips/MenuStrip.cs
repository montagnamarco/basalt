using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The classic menu bar across the top of a form: a row of
/// <see cref="ToolStripMenuItem"/>s, each opening its own drop-down.
/// </summary>
/// <remarks>
/// Without a hook on <c>Form</c> (see the task's report), Alt and F10 cannot
/// enter menu mode from an arbitrary focused control, the way Windows does:
/// clicking a top item (or giving the strip the focus directly) starts it
/// here instead. Once a drop-down is open, every arrow, Enter, Escape and
/// Left/Right-between-top-items keystroke works through the normal keyboard
/// pipeline, because the open drop-down is a popup that owns the keyboard.
/// </remarks>
public class MenuStrip : ToolStrip
{
    private ToolStripMenuItem? _activeTopItem;

    public MenuStrip()
    {
        GripStyle = ToolStripGripStyle.Hidden;
        Dock = DockStyle.Top;
        Stretch = true;
        ShowItemToolTips = false;
    }

    [DefaultValue(false)]
    [Browsable(false)]
    public bool CanOverflow { get; set; }

    protected override Padding DefaultGripMargin => Padding.Empty;

    protected override Padding DefaultPadding => new(7, 2, 0, 2);

    protected override bool DefaultShowItemToolTips => false;

    protected override Size DefaultSize => new(200, 24);

    [DefaultValue(null)]
    public ToolStripMenuItem? MdiWindowListItem { get; set; }

    [DefaultValue(false)]
    public new bool ShowItemToolTips
    {
        get => base.ShowItemToolTips;
        set => base.ShowItemToolTips = value;
    }

    /// <summary>Whether the bar stretches to fill its container's width, as a form's main menu does.</summary>
    [DefaultValue(true)]
    public bool Stretch { get; set; } = true;

    /// <summary>The top item whose drop-down is open, if the bar is in menu mode.</summary>
    internal ToolStripMenuItem? ActiveTopItem => _activeTopItem;

    internal bool IsMenuModeActive => _activeTopItem is not null;

    public event EventHandler? MenuActivate;

    public event EventHandler? MenuDeactivate;

    protected internal override ToolStripItem CreateDefaultItem(string? text, Image? image, EventHandler? onClick) =>
        new ToolStripMenuItem(text, image, onClick);

    /// <summary>A top item's drop-down just opened: any other open one closes, and menu mode starts if it had not.</summary>
    internal void NotifyTopItemOpened(ToolStripMenuItem item)
    {
        if (_activeTopItem is { } previous && previous != item) previous.HideDropDown();

        var wasActive = _activeTopItem is not null;
        _activeTopItem = item;

        if (!wasActive) OnMenuActivate(EventArgs.Empty);
    }

    /// <summary>A top item's drop-down just closed: menu mode ends unless another one is opening right now.</summary>
    internal void NotifyTopItemClosed(ToolStripMenuItem item)
    {
        if (_activeTopItem != item) return;

        _activeTopItem = null;
        OnMenuDeactivate(EventArgs.Empty);
    }

    protected virtual void OnMenuActivate(EventArgs e) => MenuActivate?.Invoke(this, e);

    protected virtual void OnMenuDeactivate(EventArgs e) => MenuDeactivate?.Invoke(this, e);

    // The hover-swaps-the-open-dropdown behaviour lives on ToolStripMenuItem.OnMouseEnter (a legitimate
    // override there), not here: Microsoft's own MenuStrip does not re-override OnMouseMove either.

    /// <summary>With the bar itself focused (ToolStripMenuItem.OnMouseDown gives it that), the arrows and Enter open and move between items.</summary>
    internal override bool WantsNavigationKey(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;

        return code is Keys.Down or Keys.Up or Keys.Left or Keys.Right or Keys.Enter or Keys.Escape;
    }

    internal override bool ProcessNavigationKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Down:
            case Keys.Enter:
                ActivateForKeyboard(_activeTopItem ?? FirstSelectableItem());
                return true;

            case Keys.Right:
                ActivateForKeyboard(GetNextItem(_activeTopItem, ArrowDirection.Right) as ToolStripMenuItem);
                return true;

            case Keys.Left:
                ActivateForKeyboard(GetNextItem(_activeTopItem, ArrowDirection.Left) as ToolStripMenuItem);
                return true;

            case Keys.Escape:
                _activeTopItem?.HideDropDown();
                return true;

            default:
                return false;
        }
    }

    private ToolStripMenuItem? FirstSelectableItem() => GetNextItem(null, ArrowDirection.Right) as ToolStripMenuItem;

    private void ActivateForKeyboard(ToolStripMenuItem? item)
    {
        if (item is null) return;

        _activeTopItem?.HideDropDown();
        item.ShowDropDown();
        item.DropDown.SelectFirstAvailableItem();
    }

    /// <summary>
    /// A global accelerator (Ctrl+S and the like): matched against every item
    /// of the bar and its submenus. Reached only when the bar (or a control
    /// inside its ancestor chain) holds the focus; see the report for what a
    /// Form.MainMenuStrip hook would add.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        foreach (ToolStripItem item in Items)
        {
            if (item is ToolStripMenuItem menuItem && menuItem.TryInvokeShortcut(keyData)) return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
