using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Design;
using System.Windows.Forms.PropertyGridInternal;

namespace System.Windows.Forms;

/// <summary>
/// Shows and edits the properties of one object (or the properties common
/// to several), grouped by category or alphabetical, with a two-column grid
/// and a description pane - the classic design-time property browser.
/// </summary>
/// <remarks>
/// Split across PropertyGrid.Entries.cs (the TypeDescriptor tree),
/// PropertyGrid.Layout.cs (the toolbar/grid/help rectangles and hit testing),
/// PropertyGrid.Painting.cs (OnPaint), PropertyGrid.Editing.cs (the in-place
/// TextBox editor) and PropertyGrid.Toolbar.cs (the two sort buttons).
/// Left out, with the rest of the family's gaps, in the builder's report:
/// PropertyTabs beyond the default one, the hot-commands pane, UITypeEditor
/// drop-downs and AccessibleObject.
/// </remarks>
[Designer("System.Windows.Forms.Design.PropertyGridDesigner, System.Design")]
public partial class PropertyGrid : ContainerControl
{
    private static readonly object s_propertySortChangedEvent = new();
    private static readonly object s_propertyTabChangedEvent = new();
    private static readonly object s_propertyValueChangedEvent = new();
    private static readonly object s_selectedGridItemChangedEvent = new();
    private static readonly object s_selectedObjectsChangedEvent = new();

    private readonly PropertiesTab _defaultTab = new();

    private object[] _selectedObjects = [];
    private PropertySort _propertySort = PropertySort.CategorizedAlphabetical;
    private AttributeCollection _browsableAttributes = new(new BrowsableAttribute(true));
    private RootGridEntry? _root;
    private GridItem? _selectedGridItem;
    private bool _toolbarVisible = true;
    private bool _helpVisible = true;
    private bool _largeButtons;
    private bool _canShowVisualStyleGlyphs = true;
    private bool _useCompatibleTextRendering;

    public PropertyGrid()
    {
        SetStyle(ControlStyles.UserMouse | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);


        InitializeLayoutState();
    }

    protected override Size DefaultSize => new(200, 200);

    /// <summary>One object, or null; setting it is the same as setting SelectedObjects to a one-element array.</summary>
    [DefaultValue(null)]
    public object? SelectedObject
    {
        get => _selectedObjects.Length > 0 ? _selectedObjects[0] : null;
        set => SelectedObjects = value is null ? [] : [value];
    }

    /// <summary>Every object shown at once; the grid lists only the properties they have in common.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object[] SelectedObjects
    {
        get => [.. _selectedObjects];
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _selectedObjects = [.. value];
            _root = null;
            SetSelectedGridItemCore(null, raiseEvent: false);
            OnSelectedObjectsChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    /// <summary>The root of the item tree: the invisible parent of every category or top-level property row.</summary>
    internal RootGridEntry Root => _root ??= new RootGridEntry(this, _selectedObjects);

    [DefaultValue(PropertySort.CategorizedAlphabetical)]
    public PropertySort PropertySort
    {
        get => _propertySort;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(PropertySort));
            if (value == _propertySort) return;

            _propertySort = value;
            _root = null;
            SetSelectedGridItemCore(null, raiseEvent: false);
            OnPropertySortChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    /// <summary>Which properties TypeDescriptor keeps: BrowsableAttribute(true) unless reset with ResetBrowsableAttributes.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AttributeCollection BrowsableAttributes
    {
        get => _browsableAttributes;
        set
        {
            _browsableAttributes = value ?? new AttributeCollection(new BrowsableAttribute(true));
            _root = null;
            Invalidate();
        }
    }

    /// <summary>Back to BrowsableAttribute(true) alone; IRootGridEntry.ResetBrowsableAttributes on Microsoft's internal root entry, not public on PropertyGrid itself.</summary>
    private void ResetBrowsableAttributes()
    {
        BrowsableAttributes = new AttributeCollection(new BrowsableAttribute(true));
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GridItem? SelectedGridItem
    {
        get => _selectedGridItem ?? FirstVisibleItem();
        set => SetSelectedGridItemCore(value, raiseEvent: true);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PropertyTab SelectedTab => _defaultTab;

    [DefaultValue(true)]
    public virtual bool ToolbarVisible
    {
        get => _toolbarVisible;
        set
        {
            if (value == _toolbarVisible) return;

            _toolbarVisible = value;
            PerformLayout();
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public virtual bool HelpVisible
    {
        get => _helpVisible;
        set
        {
            if (value == _helpVisible) return;

            _helpVisible = value;
            PerformLayout();
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool LargeButtons
    {
        get => _largeButtons;
        set
        {
            if (value == _largeButtons) return;

            _largeButtons = value;
            PerformLayout();
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool CanShowVisualStyleGlyphs
    {
        get => _canShowVisualStyleGlyphs;
        set
        {
            _canShowVisualStyleGlyphs = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool UseCompatibleTextRendering
    {
        get => _useCompatibleTextRendering;
        set => _useCompatibleTextRendering = value;
    }

    /// <summary>Rebuilds the item tree from the current selection and redraws; drops the in-place editor first.</summary>
    public override void Refresh()
    {
        CancelEdit();
        _root = null;
        SetSelectedGridItemCore(null, raiseEvent: false);
        base.Refresh();
    }

    /// <summary>Puts the selected property back to the value TypeDescriptor's ResetValue gives it.</summary>
    public void ResetSelectedProperty()
    {
        if (SelectedGridItem is not PropertyGridEntry entry) return;

        var oldValue = entry.Value;
        foreach (var component in entry.Components) entry.PropertyDescriptor!.ResetValue(component);

        Invalidate();
        OnPropertyValueChanged(new PropertyValueChangedEventArgs(entry, oldValue));
    }

    public void ExpandAllGridItems() => SetAllExpanded(Root, true);

    public void CollapseAllGridItems() => SetAllExpanded(Root, false);

    private void SetAllExpanded(GridItem item, bool expanded)
    {
        foreach (GridItem child in item.GridItems)
        {
            if (child.Expandable) child.Expanded = expanded;
            SetAllExpanded(child, expanded);
        }

        Invalidate();
    }

    /// <summary>The first row shown: the first category, or (when flat) the first property.</summary>
    private GridItem? FirstVisibleItem() => VisibleRows().FirstOrDefault();

    internal void SetSelectedGridItemCore(GridItem? item, bool raiseEvent = true)
    {
        if (ReferenceEquals(item, _selectedGridItem)) return;

        var old = _selectedGridItem;
        _selectedGridItem = item;
        CancelEdit();
        Invalidate();

        if (raiseEvent) OnSelectedGridItemChanged(new SelectedGridItemChangedEventArgs(old, item));
    }

    protected virtual void OnPropertySortChanged(EventArgs e) => ((EventHandler?)Events[s_propertySortChangedEvent])?.Invoke(this, e);

    protected virtual void OnPropertyTabChanged(PropertyTabChangedEventArgs e) => ((PropertyTabChangedEventHandler?)Events[s_propertyTabChangedEvent])?.Invoke(this, e);

    protected virtual void OnPropertyValueChanged(PropertyValueChangedEventArgs e) => ((PropertyValueChangedEventHandler?)Events[s_propertyValueChangedEvent])?.Invoke(this, e);

    protected virtual void OnSelectedGridItemChanged(SelectedGridItemChangedEventArgs e) => ((SelectedGridItemChangedEventHandler?)Events[s_selectedGridItemChangedEvent])?.Invoke(this, e);

    protected virtual void OnSelectedObjectsChanged(EventArgs e) => ((EventHandler?)Events[s_selectedObjectsChangedEvent])?.Invoke(this, e);

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        RecomputeRowHeight();
        PerformLayout();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecomputeRowHeight();
        PerformLayout();
        PositionEditor();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    public event EventHandler? PropertySortChanged
    {
        add => Events.AddHandler(s_propertySortChangedEvent, value);
        remove => Events.RemoveHandler(s_propertySortChangedEvent, value);
    }

    public event PropertyTabChangedEventHandler? PropertyTabChanged
    {
        add => Events.AddHandler(s_propertyTabChangedEvent, value);
        remove => Events.RemoveHandler(s_propertyTabChangedEvent, value);
    }

    public event PropertyValueChangedEventHandler? PropertyValueChanged
    {
        add => Events.AddHandler(s_propertyValueChangedEvent, value);
        remove => Events.RemoveHandler(s_propertyValueChangedEvent, value);
    }

    public event SelectedGridItemChangedEventHandler? SelectedGridItemChanged
    {
        add => Events.AddHandler(s_selectedGridItemChangedEvent, value);
        remove => Events.RemoveHandler(s_selectedGridItemChangedEvent, value);
    }

    public event EventHandler? SelectedObjectsChanged
    {
        add => Events.AddHandler(s_selectedObjectsChangedEvent, value);
        remove => Events.RemoveHandler(s_selectedObjectsChangedEvent, value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeEditor();
        base.Dispose(disposing);
    }
}
