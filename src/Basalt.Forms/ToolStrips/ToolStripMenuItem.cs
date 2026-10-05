using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// One row of a <see cref="MenuStrip"/> or of a drop-down menu: a caption, an
/// optional check mark or image in the leading margin, an optional shortcut
/// key shown right-aligned, and an arrow when it owns a submenu.
/// </summary>
public class ToolStripMenuItem : ToolStripDropDownItem
{
    /// <summary>How long the mouse must rest on a item with a submenu before it opens by itself.</summary>
    private const int SubmenuHoverDelayMilliseconds = 400;

    private const int ImageMarginWidth = 22;

    private bool _checkOnClick;
    private CheckState _checkState = CheckState.Unchecked;
    private Keys _shortcutKeys = Keys.None;
    private string? _shortcutKeyDisplayString;
    private Timer? _hoverTimer;

    public ToolStripMenuItem()
    {
    }

    public ToolStripMenuItem(string? text)
        : base(text, null, (EventHandler?)null)
    {
    }

    public ToolStripMenuItem(Image? image)
        : base(null, image, (EventHandler?)null)
    {
    }

    public ToolStripMenuItem(string? text, Image? image)
        : base(text, image, (EventHandler?)null)
    {
    }

    public ToolStripMenuItem(string? text, Image? image, EventHandler? onClick)
        : base(text, image, onClick)
    {
    }

    public ToolStripMenuItem(string? text, Image? image, params ToolStripItem[]? dropDownItems)
        : base(text, image, dropDownItems)
    {
    }

    public ToolStripMenuItem(string? text, Image? image, EventHandler? onClick, string? name)
        : base(text, image, onClick, name)
    {
    }

    public ToolStripMenuItem(string? text, Image? image, EventHandler? onClick, Keys shortcutKeys)
        : base(text, image, onClick)
    {
        ShortcutKeys = shortcutKeys;
    }

    [DefaultValue(false)]
    public bool CheckOnClick
    {
        get => _checkOnClick;
        set => _checkOnClick = value;
    }

    [DefaultValue(CheckState.Unchecked)]
    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            if (value == _checkState) return;

            _checkState = value;
            OnCheckStateChanged(EventArgs.Empty);
            OnCheckedChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checkState != CheckState.Unchecked;
        set => CheckState = value ? CheckState.Checked : CheckState.Unchecked;
    }

    protected override Padding DefaultMargin => Padding.Empty;

    protected override Padding DefaultPadding => new(ImageMarginWidth, 3, 28, 3);

    protected override Size DefaultSize => new(32, 22);

    internal override ToolStripItemOverflow DefaultOverflow => ToolStripItemOverflow.Never;

    // Only the designer default differs: the value lives in the base, which the strip's layout reads.
    [DefaultValue(ToolStripItemOverflow.Never)]
    public new ToolStripItemOverflow Overflow
    {
        get => base.Overflow;
        set => base.Overflow = value;
    }

    [DefaultValue(null)]
    [Localizable(true)]
    public string? ShortcutKeyDisplayString
    {
        get => _shortcutKeyDisplayString;
        set
        {
            if (value == _shortcutKeyDisplayString) return;

            _shortcutKeyDisplayString = value;
            Owner?.DoLayout(this, nameof(ShortcutKeyDisplayString));
            Invalidate();
        }
    }

    [DefaultValue(Keys.None)]
    [Localizable(true)]
    public Keys ShortcutKeys
    {
        get => _shortcutKeys;
        set
        {
            if (value == _shortcutKeys) return;

            _shortcutKeys = value;
            Owner?.DoLayout(this, nameof(ShortcutKeys));
            Invalidate();
        }
    }

    [DefaultValue(true)]
    [Localizable(true)]
    public bool ShowShortcutKeys { get; set; } = true;

    public event EventHandler? CheckStateChanged;

    public event EventHandler? CheckedChanged;

    protected override ToolStripDropDown CreateDefaultDropDown() => new ToolStripDropDownMenu { OwnerItem = this };

    protected virtual void OnCheckStateChanged(EventArgs e) => CheckStateChanged?.Invoke(this, e);

    protected virtual void OnCheckedChanged(EventArgs e) => CheckedChanged?.Invoke(this, e);

    /// <summary>A click toggles the check state, then raises Click; a submenu stays open instead of closing the chain.</summary>
    protected override void OnClick(EventArgs e)
    {
        if (CheckOnClick) Checked = !Checked;

        base.OnClick(e);
    }

    /// <summary>Opening on press (not on release) matches how a real menu feels: the submenu drops as soon as you press.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // A menu bar is not in the tab order, but it still answers the keyboard once a press on one of its
        // items gives it the focus: see MenuStrip's WantsNavigationKey/ProcessNavigationKey.
        Owner?.Focus();

        if (!HasDropDownItems) return;

        if (HasDropDown && IsDropDownOpen) HideDropDown();
        else ShowDropDown();
    }

    /// <summary>Hovering a sibling item with a submenu swaps which submenu is open, after a short delay.</summary>
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);

        if (Owner is MenuStrip bar && bar.IsMenuModeActive && bar.ActiveTopItem != this)
        {
            bar.ActiveTopItem?.HideDropDown();
            ShowDropDown();
            return;
        }

        if (ParentInternal is ToolStripDropDown dropDown && HasDropDownItems)
        {
            StartHoverTimer(dropDown);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        StopHoverTimer();
    }

    private void StartHoverTimer(ToolStripDropDown dropDown)
    {
        StopHoverTimer();

        _hoverTimer = new Timer { Interval = SubmenuHoverDelayMilliseconds };
        _hoverTimer.Tick += (_, _) =>
        {
            StopHoverTimer();
            if (Selected && dropDown.ActiveItem == this) ShowDropDown();
        };
        _hoverTimer.Start();
    }

    private void StopHoverTimer()
    {
        if (_hoverTimer is null) return;

        _hoverTimer.Stop();
        _hoverTimer.Dispose();
        _hoverTimer = null;
    }

    protected override void OnOwnerChanged(EventArgs e)
    {
        // A menu bar needs compact labels; popup rows reserve check and shortcut columns.
        if (!PaddingExplicitlySet)
        {
            SetDefaultPadding(Owner is MenuStrip ? new Padding(4, 3, 4, 3) : DefaultPadding);
        }
        base.OnOwnerChanged(e);
        StopHoverTimer();
    }

    /// <summary>A checked menu item surfaces that to assistive technology the same way a checked button does.</summary>
    protected override AccessibleObject CreateAccessibilityInstance()
    {
        var accessibleObject = new ToolStripDropDownItemAccessibleObject(this);
        if (Checked) accessibleObject.AddState(AccessibleStates.Checked);

        return accessibleObject;
    }

    /// <summary>Whatever hover-reopens a sibling's submenu must not fire once this one's own just hid.</summary>
    protected override void OnDropDownHide(EventArgs e)
    {
        base.OnDropDownHide(e);
        StopHoverTimer();
    }

    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);

    /// <summary>Letting go cancels whatever hover-driven submenu swap a hover over this item had queued.</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        StopHoverTimer();
    }

    /// <summary>A Ctrl/Alt shortcut reaches a menu item through the normal command-key preview, same as TryInvokeShortcut reaches it from the keyboard pipeline that calls that directly.</summary>
    protected internal override bool ProcessCmdKey(ref Message m, Keys keyData)
    {
        if (TryInvokeShortcut(keyData)) return true;

        return base.ProcessCmdKey(ref m, keyData);
    }

    /// <summary>The mnemonic opens a submenu instead of merely clicking through it, when there is one.</summary>
    protected internal override bool ProcessMnemonic(char charCode)
    {
        if (!Enabled || !Available || !Control.IsMnemonic(charCode, Text)) return false;

        if (HasDropDownItems) ShowDropDown();
        else PerformClick();

        return true;
    }

    /// <summary>
    /// A submenu entry (not a menu bar's own top-level item) is never let smaller than its own
    /// DefaultSize: autosize measurement does not add room for the arrow it still has to paint.
    /// </summary>
    protected override void SetBounds(Rectangle bounds)
    {
        if (HasDropDownItems && Owner is not MenuStrip && bounds.Width < DefaultSize.Width)
        {
            bounds = new Rectangle(bounds.Location, new Size(DefaultSize.Width, bounds.Height));
        }

        base.SetBounds(bounds);
    }

    /// <summary>
    /// Whether an MDI parent form generated this item as an entry of its own "Window" list: this
    /// codebase has no such merging yet, so there is no data source that could ever mark one, and this
    /// honestly always answers false rather than pretending a feature that does not exist.
    /// </summary>
    [Browsable(false)]
    public bool IsMdiWindowListEntry => false;

    /// <summary>
    /// Forwards verbatim: Microsoft's own override additionally special-cases an MDI window-list entry,
    /// a branch <see cref="IsMdiWindowListEntry"/> can never take here (see its own remarks).
    /// </summary>
    public override bool Enabled
    {
        get => base.Enabled;
        set => base.Enabled = value;
    }

    protected override void OnDropDownShow(EventArgs e)
    {
        base.OnDropDownShow(e);

        if (Owner is MenuStrip bar) bar.NotifyTopItemOpened(this);
    }

    // Notifying the bar that this top item's drop-down closed happens from ToolStripDropDownItem's own
    // Closed subscription (OnDropDownClosedCore), not an OnDropDownClosed override here: Microsoft's own
    // ToolStripMenuItem does not re-override it either.

    private bool IsDropDownOpen => HasDropDown && DropDown.IsOpen;

    protected override void OnPaint(PaintEventArgs e)
    {
        var owner = Owner;
        if (owner is null)
        {
            base.OnPaint(e);
            return;
        }

        var renderer = owner.Renderer;
        var onMenuBar = owner is MenuStrip;

        // Painting runs on the strip's own untranslated Graphics: Bounds (already absolute) is the
        // rectangle to measure from, not ContentRectangle (item-relative, and here Padding reserves
        // overall width for GetPreferredSize's sake, not a layout the paint code also has to honour).
        var bounds = Bounds;

        renderer.DrawMenuItemBackground(new ToolStripItemRenderEventArgs(e.Graphics, this));

        var leftMargin = onMenuBar ? 0 : ImageMarginWidth;
        var contentRight = bounds.Right;
        var contentTop = bounds.Top;
        var contentHeight = bounds.Height;

        if (!onMenuBar)
        {
            var imageArea = new Rectangle(bounds.Left, contentTop, leftMargin, contentHeight);

            if (Checked)
            {
                renderer.DrawItemCheck(new ToolStripItemImageRenderEventArgs(e.Graphics, this, Shrink(imageArea, 3)));
            }
            else if (ImageToDraw is { } image)
            {
                renderer.DrawItemImage(new ToolStripItemImageRenderEventArgs(e.Graphics, this, image, CenterIn(imageArea, image.Size)));
            }
        }
        else if (ImageToDraw is { } topImage)
        {
            var imageArea = new Rectangle(bounds.Left, contentTop, topImage.Width, contentHeight);
            renderer.DrawItemImage(new ToolStripItemImageRenderEventArgs(e.Graphics, this, topImage, CenterIn(imageArea, topImage.Size)));
            leftMargin = topImage.Width + 2;
        }

        var arrowWidth = HasDropDownItems && !onMenuBar ? contentHeight : 0;
        var shortcutText = ShowShortcutKeys ? EffectiveShortcutDisplayString() : null;
        var shortcutWidth = !string.IsNullOrEmpty(shortcutText) ? TextRenderer.MeasureText(shortcutText, Font).Width + 10 : 0;

        var textLeft = bounds.Left + leftMargin + (onMenuBar ? 0 : 4);
        var textRight = contentRight - arrowWidth - shortcutWidth;
        var textRect = new Rectangle(textLeft, contentTop, Math.Max(textRight - textLeft, 0), contentHeight);

        var textColor = owner is MenuStrip && Selected ? SystemColors.HighlightText : ForeColor;
        var format = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (onMenuBar ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);

        if (Enabled)
        {
            renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(e.Graphics, this, Text, textRect, textColor, Font, format));
        }
        else
        {
            ControlPaint.DrawStringDisabled(e.Graphics, Text ?? string.Empty, Font, SystemColors.ControlText, textRect, format);
        }

        if (!string.IsNullOrEmpty(shortcutText))
        {
            var shortcutRect = new Rectangle(contentRight - arrowWidth - shortcutWidth, contentTop, shortcutWidth, contentHeight);
            renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(e.Graphics, this, shortcutText, shortcutRect, textColor, Font, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine));
        }

        if (arrowWidth > 0)
        {
            var arrowRect = Shrink(new Rectangle(contentRight - arrowWidth, contentTop, arrowWidth, contentHeight), 5);
            renderer.DrawArrow(new ToolStripArrowRenderEventArgs(e.Graphics, this, arrowRect, textColor, ArrowDirection.Right));
        }

        base.OnPaint(e);
    }

    // Sizing is left to the base ToolStripItem.GetPreferredSize (text + image + Padding.Horizontal):
    // Microsoft's own ToolStripMenuItem does not override GetPreferredSize either, it tunes
    // DefaultMargin/DefaultPadding/DefaultSize instead, which is why they reserve generous room
    // on the trailing edge for a shortcut and a submenu arrow (an approximation: this does not
    // shrink to fit when there is neither).

    /// <summary>The text shown for the shortcut: the explicit override string, else one derived from ShortcutKeys.</summary>
    private string? EffectiveShortcutDisplayString()
    {
        if (!string.IsNullOrEmpty(_shortcutKeyDisplayString)) return _shortcutKeyDisplayString;

        return _shortcutKeys == Keys.None ? null : FormatShortcut(_shortcutKeys);
    }

    private static string FormatShortcut(Keys keys)
    {
        var parts = new List<string>(4);

        if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keys & Keys.Alt) != 0) parts.Add("Alt");
        if ((keys & Keys.Shift) != 0) parts.Add("Shift");

        parts.Add(KeyCodeName(keys & Keys.KeyCode));

        return string.Join("+", parts);
    }

    private static string KeyCodeName(Keys code)
    {
        if (code is >= Keys.D0 and <= Keys.D9) return ((int)(code - Keys.D0)).ToString();
        if (code is >= Keys.F1 and <= Keys.F24) return "F" + (code - Keys.F1 + 1);

        return code switch
        {
            Keys.Delete => "Del",
            Keys.Back => "Backspace",
            _ => code.ToString(),
        };
    }

    /// <summary>
    /// Matches the shortcut against the key the form's keyboard pipeline is
    /// carrying; true (and the item is clicked) when it fires. Reached only
    /// while this item (or a drop-down it opened) holds the keyboard, or when
    /// the owning MenuStrip itself has the focus: see the report for the
    /// Form-level hook a global shortcut still needs.
    /// </summary>
    private bool HasDropDownItemsInternal => HasDropDown && DropDownItems.Count > 0;

    internal bool TryInvokeShortcut(Keys keyData)
    {
        // Visibility does not matter, as in WinForms, where a hidden item is the usual way to give
        // a form a shortcut with no menu entry; a disabled item, or one with a submenu, does not fire.
        if (!Enabled) return false;

        if (ShortcutKeys != Keys.None && ShortcutKeys == keyData && !HasDropDownItemsInternal)
        {
            // OnClick, not PerformClick: PerformClick ignores an item that is not placed, and a hidden one must still fire.
            OnClick(EventArgs.Empty);
            return true;
        }

        if (HasDropDown)
        {
            foreach (ToolStripItem item in DropDownItems)
            {
                if (item is ToolStripMenuItem child && child.TryInvokeShortcut(keyData)) return true;
            }
        }

        return false;
    }

    private static Rectangle Shrink(Rectangle rectangle, int amount) => Rectangle.Inflate(rectangle, -amount, -amount);

    private static Rectangle CenterIn(Rectangle area, Size size) =>
        new(area.Left + Math.Max((area.Width - size.Width) / 2, 0), area.Top + Math.Max((area.Height - size.Height) / 2, 0), size.Width, size.Height);

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopHoverTimer();

        base.Dispose(disposing);
    }
}
