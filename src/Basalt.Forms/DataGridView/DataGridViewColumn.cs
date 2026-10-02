using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// One column of the grid: its width, its header, the cell template every row's cell for it is
/// cloned from, and where it sits in DisplayIndex order (which can differ from Index once the
/// user or the application reorders columns).
/// </summary>
public class DataGridViewColumn : DataGridViewBand, IComponent
{
    private DataGridViewCell? _cellTemplate;
    private DataGridViewColumnHeaderCell? _headerCell;
    private string _name = string.Empty;
    private string _headerText = string.Empty;
    private string _dataPropertyName = string.Empty;
    private string _toolTipText = string.Empty;
    private int _width = 100;
    private int _minimumWidth = 5;
    private int _displayIndex = -1;

    public DataGridViewColumn()
    {
    }

    public DataGridViewColumn(DataGridViewCell? cellTemplate)
    {
        _cellTemplate = cellTemplate;
    }

    private DataGridViewAutoSizeColumnMode _autoSizeMode = DataGridViewAutoSizeColumnMode.NotSet;
    private int _dividerWidth;

    public DataGridViewAutoSizeColumnMode AutoSizeMode
    {
        get => _autoSizeMode;
        set
        {
            if (value == _autoSizeMode) return;

            var previous = _autoSizeMode;
            _autoSizeMode = value;
            DataGridView?.OnAutoSizeColumnModeChangedCore(this, previous);
        }
    }

    public virtual DataGridViewCell? CellTemplate
    {
        get => _cellTemplate;
        set => _cellTemplate = value;
    }

    public Type? CellType => _cellTemplate?.GetType();

    /// <summary>Column's own menu, raising ColumnContextMenuStripChanged on the owning grid when it is replaced (Column has no "Needed" counterpart: only the cell and row ask for one dynamically).</summary>
    public override ContextMenuStrip? ContextMenuStrip
    {
        get => base.ContextMenuStrip;
        set
        {
            if (ReferenceEquals(base.ContextMenuStrip, value)) return;

            base.ContextMenuStrip = value;
            DataGridView?.OnColumnContextMenuStripChangedCore(this);
        }
    }

    public string DataPropertyName
    {
        get => _dataPropertyName;
        set
        {
            var newValue = value ?? string.Empty;
            if (newValue == _dataPropertyName) return;

            _dataPropertyName = newValue;
            DataGridView?.OnColumnDataPropertyNameChangedCore(this);
        }
    }

    public override DataGridViewCellStyle DefaultCellStyle
    {
        get => base.DefaultCellStyle;
        set
        {
            base.DefaultCellStyle = value;
            DataGridView?.OnColumnDefaultCellStyleChangedCore(this);
            if (value is not null) DataGridView?.OnCellStyleContentChangedCore(value, DataGridViewCellStyleScopes.Column);
        }
    }

    public int DisplayIndex
    {
        get => _displayIndex < 0 ? Index : _displayIndex;
        set => DataGridView?.SetColumnDisplayIndex(this, value);
    }

    public int DividerWidth
    {
        get => _dividerWidth;
        set
        {
            if (value == _dividerWidth) return;

            _dividerWidth = value;
            DataGridView?.OnColumnDividerWidthChangedCore(this);
        }
    }

    public float FillWeight { get; set; } = 100f;

    public override bool Frozen
    {
        get => base.Frozen;
        set => SetStateFlag(value, base.Frozen, v => base.Frozen = v, DataGridViewElementStates.Frozen);
    }

    public DataGridViewColumnHeaderCell HeaderCell
    {
        get
        {
            if (_headerCell is null)
            {
                _headerCell = new DataGridViewColumnHeaderCell();
                _headerCell.OwningColumn = this;
            }

            return _headerCell;
        }
        set
        {
            _headerCell = value;
            if (value is not null) value.OwningColumn = this;
            DataGridView?.OnColumnHeaderCellChangedCore(this);
        }
    }

    public string HeaderText
    {
        get => _headerText;
        set => _headerText = value ?? string.Empty;
    }

    public DataGridViewAutoSizeColumnMode InheritedAutoSizeMode =>
        AutoSizeMode != DataGridViewAutoSizeColumnMode.NotSet ? AutoSizeMode : ColumnModeFromGrid();

    public override DataGridViewCellStyle? InheritedStyle => HasDefaultCellStyle ? DefaultCellStyle : DataGridView?.DefaultCellStyle;

    public bool IsDataBound => _dataPropertyName.Length > 0;

    public int MinimumWidth
    {
        get => _minimumWidth;
        set
        {
            if (value < 2) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _minimumWidth)
            {
                if (_width < value) Width = value;
                return;
            }

            _minimumWidth = value;
            DataGridView?.OnColumnMinimumWidthChangedCore(this);
            if (_width < value) Width = value;
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            var newValue = value ?? string.Empty;
            if (newValue == _name) return;

            _name = newValue;
            DataGridView?.OnColumnNameChangedCore(this);
        }
    }

    public override bool ReadOnly
    {
        get => base.ReadOnly;
        set => SetStateFlag(value, base.ReadOnly, v => base.ReadOnly = v, DataGridViewElementStates.ReadOnly);
    }

    public override DataGridViewTriState Resizable
    {
        get => base.Resizable;
        set => base.Resizable = value;
    }

    public ISite? Site { get; set; }

    private DataGridViewColumnSortMode _sortMode = DataGridViewColumnSortMode.NotSortable;

    public DataGridViewColumnSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (value == _sortMode) return;

            _sortMode = value;
            DataGridView?.OnColumnSortModeChangedCore(this);
        }
    }

    public string ToolTipText
    {
        get => _toolTipText;
        set
        {
            var newValue = value ?? string.Empty;
            if (newValue == _toolTipText) return;

            _toolTipText = newValue;
            DataGridView?.OnColumnToolTipTextChangedCore(this);
        }
    }

    public Type? ValueType { get; set; }

    public override bool Visible
    {
        get => base.Visible;
        set => SetStateFlag(value, base.Visible, v => base.Visible = v, DataGridViewElementStates.Visible);
    }

    public int Width
    {
        get => _width;
        set
        {
            var clamped = Math.Max(value, _minimumWidth);
            if (clamped == _width) return;

            _width = clamped;
            DataGridView?.OnColumnWidthChangedCore(this);
        }
    }

    public event EventHandler? Disposed;

    public override object Clone()
    {
        var clone = (DataGridViewColumn)base.Clone();
        clone._name = _name;
        clone._headerText = _headerText;
        clone._dataPropertyName = _dataPropertyName;
        clone._toolTipText = _toolTipText;
        clone._width = _width;
        clone._minimumWidth = _minimumWidth;
        clone.AutoSizeMode = AutoSizeMode;
        clone.SortMode = SortMode;
        clone.FillWeight = FillWeight;
        clone.ValueType = ValueType;
        clone._cellTemplate = (DataGridViewCell?)_cellTemplate?.Clone();

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Disposed?.Invoke(this, EventArgs.Empty);

        base.Dispose(disposing);
    }

    public virtual int GetPreferredWidth(DataGridViewAutoSizeColumnMode autoSizeColumnMode, bool fixedHeight) => Width;

    public override string ToString() => $"DataGridViewColumn {{ Name={Name}, Index={Index} }}";

    private DataGridViewAutoSizeColumnMode ColumnModeFromGrid() =>
        DataGridView?.AutoSizeColumnsMode switch
        {
            DataGridViewAutoSizeColumnsMode.None => DataGridViewAutoSizeColumnMode.None,
            DataGridViewAutoSizeColumnsMode.ColumnHeader => DataGridViewAutoSizeColumnMode.ColumnHeader,
            DataGridViewAutoSizeColumnsMode.AllCellsExceptHeader => DataGridViewAutoSizeColumnMode.AllCellsExceptHeader,
            DataGridViewAutoSizeColumnsMode.AllCells => DataGridViewAutoSizeColumnMode.AllCells,
            DataGridViewAutoSizeColumnsMode.DisplayedCellsExceptHeader => DataGridViewAutoSizeColumnMode.DisplayedCellsExceptHeader,
            DataGridViewAutoSizeColumnsMode.DisplayedCells => DataGridViewAutoSizeColumnMode.DisplayedCells,
            DataGridViewAutoSizeColumnsMode.Fill => DataGridViewAutoSizeColumnMode.Fill,
            _ => DataGridViewAutoSizeColumnMode.None,
        };

    /// <summary>Set by the column collection while laying out columns left to right, without going through the public setter's reorder logic.</summary>
    internal void SetDisplayIndexCore(int value)
    {
        if (value == _displayIndex) return;

        _displayIndex = value;
        DataGridView?.OnColumnDisplayIndexChangedCore(this);
    }

    /// <summary>Applies one of the Band boolean flags through its own setter only when it really changes, then reports the flip to the owning grid as ColumnStateChanged.</summary>
    private void SetStateFlag(bool value, bool current, Action<bool> apply, DataGridViewElementStates flag)
    {
        if (value == current) return;

        apply(value);
        DataGridView?.OnColumnStateChangedCore(this, flag);
    }
}
