using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// A strip of tabs, each one the caption of a <see cref="TabPage"/>; only the
/// <see cref="SelectedTab"/>'s page is shown, in the area <see
/// cref="DisplayRectangle"/> describes under the strip.
/// </summary>
/// <remarks>
/// Native in WinForms (its window is a real Win32 common control there), so
/// this class paints its strip and the page's frame through <see
/// cref="DefaultPaint"/> and <see cref="DefaultMouseDown"/> rather than
/// OnPaint/OnMouseDown, which Microsoft's TabControl does not override
/// either. Keyboard page-cycling, by contrast, really is managed code in
/// WinForms (OnKeyDown is overridden there), so it is overridden here too.
/// </remarks>
[DefaultProperty("TabPages")]
[DefaultEvent("SelectedIndexChanged")]
public partial class TabControl : Control
{
    /// <summary>The 3-D border around the page area: one pixel raised, one pixel inset, as <see cref="Border3DStyle.Raised"/> draws it.</summary>
    private const int PageBorderThickness = 2;

    /// <summary>How much taller the selected tab stands than the others, bulging up into the space above the row.</summary>
    private const int SelectedTabRaise = 2;

    private TabAlignment _alignment = TabAlignment.Top;
    private TabAppearance _appearance = TabAppearance.Normal;
    private TabDrawMode _drawMode = TabDrawMode.Normal;
    private TabSizeMode _sizeMode = TabSizeMode.Normal;
    private bool _hotTrack;
    private bool _multiline;
    private bool _showToolTips;
    private Point _padding = new(6, 3);
    private Size _itemSize = Size.Empty;
    private ImageList? _imageList;
    private int _selectedIndex = -1;
    private TabPageCollection? _tabPages;

    public TabControl()
    {
        // Native in WinForms: DefaultPaint and DefaultMouseDown draw and hit-test the strip, not OnPaint/OnMouseDown.
        SetStyle(ControlStyles.UserPaint, false);
        SetStyle(ControlStyles.Selectable, true);
        MouseLeave += ClearHotTabOnMouseLeave;
    }

    protected override Size DefaultSize => new(200, 100);

    [DefaultValue(TabAlignment.Top)]
    public TabAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TabAlignment));
            if (value == _alignment) return;

            _alignment = value;
            RelayoutStrip();
        }
    }

    [DefaultValue(TabAppearance.Normal)]
    public TabAppearance Appearance
    {
        get => _appearance;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TabAppearance));
            if (value == _appearance) return;

            _appearance = value;
            Invalidate();
        }
    }

    [DefaultValue(TabDrawMode.Normal)]
    public TabDrawMode DrawMode
    {
        get => _drawMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TabDrawMode));
            if (value == _drawMode) return;

            _drawMode = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool HotTrack
    {
        get => _hotTrack;
        set
        {
            if (value == _hotTrack) return;

            _hotTrack = value;
            if (!value) _hotTabIndex = -1;
            Invalidate();
        }
    }

    [DefaultValue(null)]
    public ImageList? ImageList
    {
        get => _imageList;
        set
        {
            if (value == _imageList) return;

            _imageList = value;
            Invalidate();
        }
    }

    /// <summary>
    /// The size every tab takes in <see cref="TabSizeMode.Fixed"/>; a zero
    /// side means that side is measured from the tab's content instead, as
    /// <see cref="TabSizeMode.Normal"/> always measures both.
    /// </summary>
    public Size ItemSize
    {
        get => _itemSize.IsEmpty ? NaturalItemSize() : _itemSize;
        set
        {
            if (value.Width < 0 || value.Height < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "ItemSize cannot be negative.");
            if (value == _itemSize) return;

            _itemSize = value;
            RelayoutStrip();
        }
    }

    [DefaultValue(false)]
    public bool Multiline
    {
        get => _multiline;
        set
        {
            if (value == _multiline) return;

            _multiline = value;
            RelayoutStrip();
        }
    }

    /// <summary>The space left and right (X) and above and below (Y) a tab's text; hides Control.Padding, which TabControl does not use.</summary>
    public new Point Padding
    {
        get => _padding;
        set
        {
            if (value == _padding) return;

            _padding = value;
            RelayoutStrip();
        }
    }

    /// <summary>How many rows the strip currently needs to show every tab.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int RowCount
    {
        get
        {
            LayOutTabs(out var rowCount);
            return rowCount;
        }
    }

    [Browsable(false)]
    [DefaultValue(-1)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= TabPages.Count) throw new ArgumentOutOfRangeException(nameof(value), value, "SelectedIndex must name an existing tab, or be -1 for none.");

            ChangeSelection(value, updateFocus: true);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TabPage? SelectedTab
    {
        get => _selectedIndex >= 0 && _selectedIndex < TabPages.Count ? TabPages[_selectedIndex] : null;
        set
        {
            if (value is null)
            {
                SelectedIndex = -1;
                return;
            }

            var index = TabPages.IndexOf(value);
            if (index < 0) throw new ArgumentException("The tab page does not belong to this TabControl's TabPages collection.", nameof(value));

            SelectedIndex = index;
        }
    }

    [DefaultValue(false)]
    [Localizable(true)]
    public bool ShowToolTips
    {
        get => _showToolTips;
        set => _showToolTips = value;
    }

    [DefaultValue(TabSizeMode.Normal)]
    public TabSizeMode SizeMode
    {
        get => _sizeMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TabSizeMode));
            if (value == _sizeMode) return;

            _sizeMode = value;
            RelayoutStrip();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TabCount => TabPages.Count;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [MergableProperty(false)]
    public TabPageCollection TabPages => _tabPages ??= new TabPageCollection(this);

    /// <summary>
    /// The page area, under the strip and inside its raised border: <see
    /// cref="Control.ClientRectangle"/> minus however many rows the strip
    /// needs and minus <see cref="PageBorderThickness"/> on every side.
    /// </summary>
    public override Rectangle DisplayRectangle
    {
        get
        {
            var frame = PageFrameRectangle();

            return new Rectangle(
                frame.X + PageBorderThickness,
                frame.Y + PageBorderThickness,
                Math.Max(frame.Width - PageBorderThickness * 2, 0),
                Math.Max(frame.Height - PageBorderThickness * 2, 0));
        }
    }

    /// <summary>The rectangle, in client coordinates, the tab at this index is drawn in, at its unraised size.</summary>
    public Rectangle GetTabRect(int index)
    {
        var rects = LayOutTabs(out _);
        if (index < 0 || index >= rects.Length) throw new ArgumentOutOfRangeException(nameof(index), index, "There is no tab at this index.");

        return rects[index];
    }

    public Control GetControl(int index) => TabPages[index];

    public void SelectTab(int index) => SelectedIndex = index;

    public void SelectTab(TabPage tabPage) => SelectedTab = tabPage;

    public void SelectTab(string tabPageName)
    {
        var index = TabPages.IndexOfKey(tabPageName);
        if (index >= 0) SelectedIndex = index;
    }

    public void DeselectTab(int index)
    {
        if (index < 0 || index >= TabPages.Count) throw new ArgumentOutOfRangeException(nameof(index), index, "There is no tab at this index.");
        if (index != _selectedIndex) return;

        var next = index + 1 < TabPages.Count ? index + 1 : index - 1;
        SelectedIndex = next;
    }

    public void DeselectTab(TabPage tabPage)
    {
        ArgumentNullException.ThrowIfNull(tabPage);
        DeselectTab(TabPages.IndexOf(tabPage));
    }

    public void DeselectTab(string tabPageName)
    {
        var index = TabPages.IndexOfKey(tabPageName);
        if (index >= 0) DeselectTab(index);
    }

    /// <summary>Removes every page, as Clear on TabPages does.</summary>
    protected void RemoveAll() => TabPages.Clear();

    protected virtual object[] GetItems() => [.. TabPagesSnapshot()];

    protected virtual object[] GetItems(Type baseType)
    {
        ArgumentNullException.ThrowIfNull(baseType);

        return [.. TabPagesSnapshot().Where(baseType.IsInstanceOfType)];
    }

    protected string GetToolTipText(object item) => item is TabPage page ? page.ToolTipText : string.Empty;

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        RelayoutStrip();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnEnter(EventArgs e) => base.OnEnter(e);

    protected override void OnLeave(EventArgs e) => base.OnLeave(e);

    protected virtual void OnSelecting(TabControlCancelEventArgs e) => ((TabControlCancelEventHandler?)Events[s_selectingEvent])?.Invoke(this, e);

    protected virtual void OnSelected(TabControlEventArgs e) => ((TabControlEventHandler?)Events[s_selectedEvent])?.Invoke(this, e);

    protected virtual void OnDeselecting(TabControlCancelEventArgs e) => ((TabControlCancelEventHandler?)Events[s_deselectingEvent])?.Invoke(this, e);

    protected virtual void OnDeselected(TabControlEventArgs e) => ((TabControlEventHandler?)Events[s_deselectedEvent])?.Invoke(this, e);

    protected virtual void OnSelectedIndexChanged(EventArgs e) => ((EventHandler?)Events[s_selectedIndexChangedEvent])?.Invoke(this, e);

    public event TabControlCancelEventHandler? Selecting
    {
        add => Events.AddHandler(s_selectingEvent, value);
        remove => Events.RemoveHandler(s_selectingEvent, value);
    }

    public event TabControlEventHandler? Selected
    {
        add => Events.AddHandler(s_selectedEvent, value);
        remove => Events.RemoveHandler(s_selectedEvent, value);
    }

    public event TabControlCancelEventHandler? Deselecting
    {
        add => Events.AddHandler(s_deselectingEvent, value);
        remove => Events.RemoveHandler(s_deselectingEvent, value);
    }

    public event TabControlEventHandler? Deselected
    {
        add => Events.AddHandler(s_deselectedEvent, value);
        remove => Events.RemoveHandler(s_deselectedEvent, value);
    }

    public event EventHandler? SelectedIndexChanged
    {
        add => Events.AddHandler(s_selectedIndexChangedEvent, value);
        remove => Events.RemoveHandler(s_selectedIndexChangedEvent, value);
    }

    private static readonly object s_selectingEvent = new();
    private static readonly object s_selectedEvent = new();
    private static readonly object s_deselectingEvent = new();
    private static readonly object s_deselectedEvent = new();
    private static readonly object s_selectedIndexChangedEvent = new();

    /// <summary>
    /// Moves the selection to <paramref name="index"/> through Deselecting
    /// and Selecting (either may cancel, leaving the selection untouched),
    /// then Deselected, Selected and SelectedIndexChanged, in that order -
    /// the order WinForms documents for SelectedIndex.
    /// </summary>
    private void ChangeSelection(int index, bool updateFocus)
    {
        if (index == _selectedIndex) return;

        var oldIndex = _selectedIndex;
        var oldPage = oldIndex >= 0 && oldIndex < TabPages.Count ? TabPages[oldIndex] : null;
        var newPage = index >= 0 ? TabPages[index] : null;

        if (oldPage is not null)
        {
            var deselecting = new TabControlCancelEventArgs(oldPage, oldIndex, false, TabControlAction.Deselecting);
            OnDeselecting(deselecting);
            if (deselecting.Cancel) return;
        }

        if (newPage is not null)
        {
            var selecting = new TabControlCancelEventArgs(newPage, index, false, TabControlAction.Selecting);
            OnSelecting(selecting);
            if (selecting.Cancel) return;
        }

        _selectedIndex = index;
        UpdateTabSelection(updateFocus);

        // The page itself never gets the keyboard focus, so WinForms gives it Enter/Leave through the selection instead.
        oldPage?.RaiseLeave(EventArgs.Empty);
        newPage?.RaiseEnter(EventArgs.Empty);

        if (oldPage is not null) OnDeselected(new TabControlEventArgs(oldPage, oldIndex, TabControlAction.Deselected));
        if (newPage is not null) OnSelected(new TabControlEventArgs(newPage, index, TabControlAction.Selected));
        OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <summary>
    /// Shows the selected page and hides every other one; with
    /// <paramref name="updateFocus"/>, also moves the focus to the first
    /// selectable control the newly selected page has.
    /// </summary>
    protected void UpdateTabSelection(bool updateFocus)
    {
        var selected = SelectedTab;

        foreach (var page in TabPagesSnapshot())
        {
            var shouldBeVisible = page == selected;
            if (page.Visible != shouldBeVisible) page.Visible = shouldBeVisible;
        }

        if (updateFocus && selected is { Visible: true } && ContainsFocus)
        {
            selected.SelectNextControl(null, true, true, true, false);
        }
    }

    /// <summary>A page the strip must redraw and lay out again, because its strip-affecting size changed.</summary>
    private void RelayoutStrip()
    {
        DoLayout(this, PropertyNames.Bounds);
        Invalidate();
    }

    private TabPage[] TabPagesSnapshot() => [.. Controls.Snapshot().Cast<TabPage>()];
}
