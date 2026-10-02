using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// What every entry in a PropertyGrid's tree needs besides what GridItem
/// already declares: the grid it belongs to (to report a selection or a
/// value change back) and its parent, both fixed at construction.
/// </summary>
internal abstract class GridEntry : GridItem
{
    private protected readonly PropertyGrid Grid;
    private readonly GridItem? _parent;

    private protected GridEntry(PropertyGrid grid, GridItem? parent)
    {
        Grid = grid;
        _parent = parent;
    }

    public override GridItem? Parent => _parent;
}

/// <summary>
/// The invisible top of the tree: Value is the selected object itself (or
/// the first of SelectedObjects), and its children are the top-level
/// category or property rows, built lazily and rebuilt whenever the grid's
/// own state (sort mode, selected object...) invalidates them.
/// </summary>
internal sealed class RootGridEntry : GridEntry
{
    private readonly object[] _components;
    private GridItemCollection? _children;

    public RootGridEntry(PropertyGrid grid, object[] components) : base(grid, null)
    {
        _components = components;
    }

    public override GridItemType GridItemType => GridItemType.Root;

    public override string? Label => null;

    public override PropertyDescriptor? PropertyDescriptor => null;

    public override object? Value => _components.Length > 0 ? _components[0] : null;

    public override GridItemCollection GridItems => _children ??= Grid.BuildTopLevelEntries(this, _components);

    /// <summary>Forces the next GridItems read to rebuild the tree, after the object or the sort mode changed.</summary>
    internal void Invalidate() => _children = null;

    public override bool Select() => false;
}

/// <summary>A CategoryAttribute heading; selectable (the help pane then shows nothing special for it), its children are that category's properties, alphabetical.</summary>
internal sealed class CategoryGridEntry : GridEntry
{
    private readonly string _category;
    private PropertyGridEntry[] _properties = [];

    public CategoryGridEntry(PropertyGrid grid, GridItem parent, string category) : base(grid, parent)
    {
        _category = category;
        Expanded = true;
    }

    /// <summary>Set once, right after the properties are built with this entry as their parent (they need it to exist first).</summary>
    internal void SetProperties(PropertyGridEntry[] properties) => _properties = properties;

    public override GridItemType GridItemType => GridItemType.Category;

    public override string Label => _category;

    public override PropertyDescriptor? PropertyDescriptor => null;

    public override object? Value => null;

    public override GridItemCollection GridItems => new(_properties);

    public override bool Select()
    {
        Grid.SetSelectedGridItemCore(this);
        return true;
    }
}

/// <summary>
/// One property row. Value and the in-place editor read and write through
/// PropertyDescriptor on every selected component; when its TypeConverter
/// supports sub-properties (ExpandableObjectConverter and its kin) the row
/// is Expandable and GridItems opens that sub-object's own properties.
/// </summary>
internal sealed class PropertyGridEntry : GridEntry
{
    private readonly object[] _components;
    private readonly PropertyDescriptor _descriptor;
    private GridItemCollection? _children;

    public PropertyGridEntry(PropertyGrid grid, GridItem parent, object[] components, PropertyDescriptor descriptor) : base(grid, parent)
    {
        _components = components;
        _descriptor = descriptor;
    }

    public override GridItemType GridItemType => GridItemType.Property;

    public override string Label => _descriptor.Name;

    public override PropertyDescriptor PropertyDescriptor => _descriptor;

    /// <summary>Every selected component this row edits at once; Value and painting only ever read the first.</summary>
    public object[] Components => _components;

    public override object? Value => _components.Length == 0 ? null : _descriptor.GetValue(_components[0]);

    /// <summary>Different components disagreeing on the value: shown blank, as Microsoft's grid shows it.</summary>
    public bool HasMixedValue
    {
        get
        {
            if (_components.Length < 2) return false;

            var first = _descriptor.GetValue(_components[0]);
            for (var i = 1; i < _components.Length; i++)
            {
                if (!Equals(first, _descriptor.GetValue(_components[i]))) return true;
            }

            return false;
        }
    }

    public override bool Expandable => Value is not null && _descriptor.Converter.GetPropertiesSupported();

    public override GridItemCollection GridItems => Expandable ? _children ??= Grid.BuildChildEntries(this, Value!) : GridItemCollection.Empty;

    public override bool Select()
    {
        Grid.SetSelectedGridItemCore(this);
        return true;
    }

    /// <summary>
    /// Writes newValue to every component this row edits and drops the stale
    /// sub-property children. When a setter refuses the value (it throws), the
    /// components already written get their old value back, so a selection of
    /// several objects is never left half changed; returns false then.
    /// </summary>
    internal bool TryApplyValue(object? newValue)
    {
        var written = new List<(object Component, object? OldValue)>();

        try
        {
            foreach (var component in _components)
            {
                var oldValue = _descriptor.GetValue(component);
                _descriptor.SetValue(component, newValue);
                written.Add((component, oldValue));
            }
        }
        catch (Exception)
        {
            RestoreValues(written);
            return false;
        }
        finally
        {
            _children = null;
        }

        return true;
    }

    private void RestoreValues(List<(object Component, object? OldValue)> written)
    {
        foreach (var (component, oldValue) in written)
        {
            try
            {
                _descriptor.SetValue(component, oldValue);
            }
            catch (Exception)
            {
                // The old value was accepted once; a setter that now refuses it leaves nothing better to do.
            }
        }
    }
}
