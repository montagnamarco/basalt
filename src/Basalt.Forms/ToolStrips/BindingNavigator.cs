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
            if (_positionItem is not null) _positionItem.TextChanged -= HandlePositionTextChanged;

            _positionItem = value;

            if (_positionItem is not null) _positionItem.TextChanged += HandlePositionTextChanged;

            RefreshItemsCore();
        }
    }

    public event EventHandler? RefreshItems;

    /// <summary>Creates the standard eight items, in Microsoft's order, and wires each to a navigation action.</summary>
    public virtual void AddStandardItems()
    {
        MoveFirstItem = new ToolStripButton("Move first") { DisplayStyle = ToolStripItemDisplayStyle.Image };
        MovePreviousItem = new ToolStripButton("Move previous") { DisplayStyle = ToolStripItemDisplayStyle.Image };
        PositionItem = new BindingNavigatorPositionItem { Text = "0" };
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

            // Setting Text here must not loop back into HandlePositionTextChanged and re-navigate.
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

    /// <summary>
    /// What a real ToolStripTextBox's Enter key would commit: here, since the position item is not a
    /// real hosted TextBox (ToolStripControlHost does not exist yet), a typed number commits as soon
    /// as it parses, directly from Text.
    /// </summary>
    private void HandlePositionTextChanged(object? sender, EventArgs e)
    {
        if (_bindingSource is null || _positionItem is null) return;
        if (!int.TryParse(_positionItem.Text, out var typedPosition)) return;

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

/// <summary>
/// The minimal stand-in for a ToolStripTextBox's hosted TextBox: ToolStripControlHost does not exist
/// yet in this library, so the position box cannot host a real, independently focusable Control. It
/// paints like a sunken text field and accepts a typed number through its Text property (see
/// <see cref="BindingNavigator.HandlePositionTextChanged"/>), but it does not receive real keyboard
/// input of its own; a caller (or test) sets Text the way a keystroke would.
/// </summary>
internal sealed class BindingNavigatorPositionItem : ToolStripItem
{
    public BindingNavigatorPositionItem()
    {
        AutoSize = false;
        Size = new Size(50, DefaultSize.Height);
    }

    public override bool CanSelect => true;

    protected override Size DefaultSize => new(50, 22);

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = ContentRectangle;
        bounds.Offset(Bounds.Location);

        ControlPaint.DrawBorder3D(e.Graphics, bounds, Border3DStyle.Sunken);

        var textBounds = Rectangle.Inflate(bounds, -2, -2);
        TextRenderer.DrawText(e.Graphics, Text ?? string.Empty, Font, textBounds, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        base.OnPaint(e);
    }
}
