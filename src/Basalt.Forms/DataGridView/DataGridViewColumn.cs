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

    public DataGridViewAutoSizeColumnMode AutoSizeMode { get; set; } = DataGridViewAutoSizeColumnMode.NotSet;

    public virtual DataGridViewCell? CellTemplate
    {
        get => _cellTemplate;
        set => _cellTemplate = value;
    }

    public Type? CellType => _cellTemplate?.GetType();

    public string DataPropertyName
    {
        get => _dataPropertyName;
        set => _dataPropertyName = value ?? string.Empty;
    }

    public override DataGridViewCellStyle DefaultCellStyle
    {
        get => base.DefaultCellStyle;
        set => base.DefaultCellStyle = value;
    }

    public int DisplayIndex
    {
        get => _displayIndex < 0 ? Index : _displayIndex;
        set => DataGridView?.SetColumnDisplayIndex(this, value);
    }

    public int DividerWidth { get; set; }

    public float FillWeight { get; set; } = 100f;

    public override bool Frozen
    {
        get => base.Frozen;
        set => base.Frozen = value;
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

            _minimumWidth = value;
            if (_width < value) Width = value;
        }
    }

    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    public override bool ReadOnly
    {
        get => base.ReadOnly;
        set => base.ReadOnly = value;
    }

    public override DataGridViewTriState Resizable
    {
        get => base.Resizable;
        set => base.Resizable = value;
    }

    public ISite? Site { get; set; }

    public DataGridViewColumnSortMode SortMode { get; set; } = DataGridViewColumnSortMode.NotSortable;

    public string ToolTipText
    {
        get => _toolTipText;
        set => _toolTipText = value ?? string.Empty;
    }

    public Type? ValueType { get; set; }

    public override bool Visible
    {
        get => base.Visible;
        set => base.Visible = value;
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
    internal void SetDisplayIndexCore(int value) => _displayIndex = value;
}
