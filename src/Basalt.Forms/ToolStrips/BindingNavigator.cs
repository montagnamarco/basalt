using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A <see cref="ToolStrip"/> with the usual record-navigation buttons (first,
/// previous, a typed position, a count label, next, last) plus add-new and
/// delete, all wired to one <see cref="BindingSource"/>.
/// </summary>
public class BindingNavigator : ToolStrip, ISupportInitialize
{
    private BindingSource? _bindingSource;
    private string _countItemFormat = "of {0}";
    private ToolStripItem? _moveFirstItem;
    private ToolStripItem? _movePreviousItem;
    private ToolStripItem? _positionItem;
    private ToolStripItem? _countItem;
    private ToolStripItem? _moveNextItem;
    private ToolStripItem? _moveLastItem;
    private ToolStripItem? _addNewItem;
    private ToolStripItem? _deleteItem;
    private int _initializeCount;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public BindingNavigator()
        : this(false)
    {
    }

    public BindingNavigator(BindingSource? bindingSource)
        : this(true)
    {
        BindingSource = bindingSource;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public BindingNavigator(IContainer container)
        : this(false)
    {
        container?.Add(this);
    }

    public BindingNavigator(bool addStandardItems)
    {
        if (addStandardItems) AddStandardItems();
    }

    [TypeConverter(typeof(ReferenceConverter))]
    public ToolStripItem? AddNewItem
    {
        get => _addNewItem;
        set => ReplaceItem(ref _addNewItem, value, HandleAddNewClick);
    }

    public BindingSource? BindingSource
    {
        get => _bindingSource;
        set
        {
            if (ReferenceEquals(_bindingSource, value)) return;

            DetachBindingSource();
            _bindingSource = value;
            AttachBindingSource();
            RefreshItemsCore();
        }
    }

    [TypeConverter(typeof(ReferenceConverter))]
    public ToolStripItem? CountItem
    {
        get => _countItem;
        set
        {
            _countItem = value;
            RefreshItemsCore();
        }
    }

    public string CountItemFormat
    {
        get => _countItemFormat;
        set
        {
            _countItemFormat = value ?? string.Empty;
            RefreshItemsCore();
        }
    }

    [TypeConverter(typeof(ReferenceConverter))]
    public ToolStripItem? DeleteItem
    {
        get => _deleteItem;
        set => ReplaceItem(ref _deleteItem, value, HandleDeleteClick);
    }

    [TypeConverter(typeof(ReferenceConverter))]
    public ToolStripItem? MoveFirstItem
    {
        get => _moveFirstItem;
        set => ReplaceItem(ref _moveFirstItem, value, HandleMoveFirstClick);
    }

    [TypeConverter(typeof(ReferenceConverter))]
    public ToolStripItem? MoveLastItem
    {
        get => _moveLastItem;
        set => ReplaceItem(ref _moveLastItem, value, HandleMoveLastClick);
    }

    [TypeConverter(typeof(ReferenceConverter))]
    public ToolStripItem? MoveNextItem
    {
        get => _moveNextItem;
        set => ReplaceItem(ref _moveNextItem, value, HandleMoveNextClick);
    }

    [TypeConverter(typeof(ReferenceConverter))]
    public ToolStripItem? MovePreviousItem
    {
        get => _movePreviousItem;
        set => ReplaceItem(ref _movePreviousItem, value, HandleMovePreviousClick);
    }

    [TypeConverter(typeof(ReferenceConverter))]
    public ToolStripItem? PositionItem
    {
        get => _positionItem;
        set
        {
            if (_positionItem is ToolStripTextBox oldBox)
            {
                oldBox.KeyDown -= HandlePositionKeyDown;
                oldBox.Leave -= HandlePositionLeave;
            }

            _positionItem = value;

            if (_positionItem is ToolStripTextBox newBox)
            {
                newBox.KeyDown += HandlePositionKeyDown;
                newBox.Leave += HandlePositionLeave;
            }

            RefreshItemsCore();
        }
    }

    public event EventHandler? RefreshItems;

    /// <summary>Creates the standard eight items, in Microsoft's order, and wires each to a navigation action.</summary>
    public virtual void AddStandardItems()
    {
        MoveFirstItem = new ToolStripButton("Move first") { DisplayStyle = ToolStripItemDisplayStyle.Image };
        MovePreviousItem = new ToolStripButton("Move previous") { DisplayStyle = ToolStripItemDisplayStyle.Image };
        PositionItem = new ToolStripTextBox { Text = "0", AutoSize = false, Size = new Size(50, 22) };
        CountItem = new ToolStripLabel { Text = "of 0" };
        MoveNextItem = new ToolStripButton("Move next") { DisplayStyle = ToolStripItemDisplayStyle.Image };
        MoveLastItem = new ToolStripButton("Move last") { DisplayStyle = ToolStripItemDisplayStyle.Image };
        AddNewItem = new ToolStripButton("Add new") { DisplayStyle = ToolStripItemDisplayStyle.Image };
        DeleteItem = new ToolStripButton("Delete") { DisplayStyle = ToolStripItemDisplayStyle.Image };

        Items.Add(_moveFirstItem!);
        Items.Add(_movePreviousItem!);
        Items.Add(_positionItem!);
        Items.Add(_countItem!);
        Items.Add(_moveNextItem!);
        Items.Add(_moveLastItem!);
        Items.Add(new ToolStripSeparator());
        Items.Add(_addNewItem!);
        Items.Add(_deleteItem!);

        RefreshItemsCore();
    }

    public void BeginInit() => _initializeCount++;

    public void EndInit()
    {
        if (_initializeCount > 0) _initializeCount--;
        if (_initializeCount == 0) RefreshItemsCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DetachBindingSource();

        base.Dispose(disposing);
    }

    protected virtual void OnRefreshItems() => RefreshItems?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Enables or disables every navigation item for the BindingSource's current Position and Count,
    /// and writes the position box and the count label.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void RefreshItemsCore()
    {
        if (_initializeCount > 0) return;

        var count = _bindingSource?.Count ?? 0;
        var position = _bindingSource?.Position ?? -1;
        var hasSource = _bindingSource is not null;
        var oneBasedPosition = position + 1;

        if (_moveFirstItem is not null) _moveFirstItem.Enabled = hasSource && position > 0;
        if (_movePreviousItem is not null) _movePreviousItem.Enabled = hasSource && position > 0;
        if (_moveNextItem is not null) _moveNextItem.Enabled = hasSource && position < count - 1;
        if (_moveLastItem is not null) _moveLastItem.Enabled = hasSource && position < count - 1;
        if (_deleteItem is not null) _deleteItem.Enabled = hasSource && count > 0 && (_bindingSource?.AllowRemove ?? false);
        if (_addNewItem is not null) _addNewItem.Enabled = hasSource && (_bindingSource?.AllowNew ?? false);

        if (_positionItem is not null)
        {
            _positionItem.Enabled = hasSource && count > 0;

            // Committing only happens on Enter or Leave (see HandlePositionKeyDown/HandlePositionLeave),
            // so writing Text here from the BindingSource's own Position cannot loop back into it.
            var text = count > 0 ? oneBasedPosition.ToString() : "0";
            if (_positionItem.Text != text) _positionItem.Text = text;
        }

        if (_countItem is not null) _countItem.Text = string.Format(_countItemFormat, count);

        OnRefreshItems();
    }

    public bool Validate()
    {
        _bindingSource?.EndEdit();

        return true;
    }

    private void AttachBindingSource()
    {
        if (_bindingSource is null) return;

        _bindingSource.PositionChanged += HandleBindingSourcePositionChanged;
        _bindingSource.ListChanged += HandleBindingSourceListChanged;
    }

    private void DetachBindingSource()
    {
        if (_bindingSource is null) return;

        _bindingSource.PositionChanged -= HandleBindingSourcePositionChanged;
        _bindingSource.ListChanged -= HandleBindingSourceListChanged;
    }

    private void HandleAddNewClick(object? sender, EventArgs e) => _bindingSource?.AddNew();

    private void HandleBindingSourceListChanged(object? sender, ListChangedEventArgs e) => RefreshItemsCore();

    private void HandleBindingSourcePositionChanged(object? sender, EventArgs e) => RefreshItemsCore();

    private void HandleDeleteClick(object? sender, EventArgs e) => _bindingSource?.RemoveCurrent();

    private void HandleMoveFirstClick(object? sender, EventArgs e) => _bindingSource?.MoveFirst();

    private void HandleMoveLastClick(object? sender, EventArgs e) => _bindingSource?.MoveLast();

    private void HandleMoveNextClick(object? sender, EventArgs e) => _bindingSource?.MoveNext();

    private void HandleMovePreviousClick(object? sender, EventArgs e) => _bindingSource?.MovePrevious();

    /// <summary>Enter commits the typed position, as a real text box's default button would.</summary>
    private void HandlePositionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;

        CommitPosition();
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    /// <summary>Leaving the box without pressing Enter still commits, the way leaving a bound text box does.</summary>
    private void HandlePositionLeave(object? sender, EventArgs e) => CommitPosition();

    /// <summary>Parses the position box's Text and moves the BindingSource there; text that is not a number puts the current position back.</summary>
    private void CommitPosition()
    {
        if (_bindingSource is null || _positionItem is null) return;

        if (!int.TryParse(_positionItem.Text, out var typedPosition))
        {
            _positionItem.Text = (_bindingSource.Position + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
            return;
        }

        _bindingSource.Position = typedPosition - 1;
    }

    private void ReplaceItem(ref ToolStripItem? field, ToolStripItem? value, EventHandler clickHandler)
    {
        if (field is not null) field.Click -= clickHandler;

        field = value;

        if (field is not null) field.Click += clickHandler;

        RefreshItemsCore();
    }
}
