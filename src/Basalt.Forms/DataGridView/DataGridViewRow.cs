using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// One row of the grid: its Cells, one per column, its own height, and the few places where a
/// row is treated specially (IsNewRow, for the AllowUserToAddRows placeholder at the bottom).
/// </summary>
public class DataGridViewRow : DataGridViewBand
{
    private DataGridViewCellCollection? _cells;
    private DataGridViewRowHeaderCell? _headerCell;
    private int _height = 22;
    private int _minimumHeight = 3;
    private bool _selected;
    private string _errorText = string.Empty;

    public DataGridViewRow()
    {
    }

    public DataGridViewCellCollection Cells => _cells ??= CreateCellsInstance();

    /// <summary>The source object this row shows, when the grid is bound through DataSource; null while unbound.</summary>
    public object? DataBoundItem { get; internal set; }

    public override DataGridViewCellStyle DefaultCellStyle
    {
        get => base.DefaultCellStyle;
        set => base.DefaultCellStyle = value;
    }

    public override bool Displayed => base.Displayed;

    public int DividerHeight { get; set; }

    public string ErrorText
    {
        get => _errorText;
        set => _errorText = value ?? string.Empty;
    }

    public override bool Frozen
    {
        get => base.Frozen;
        set => base.Frozen = value;
    }

    public DataGridViewRowHeaderCell HeaderCell
    {
        get
        {
            if (_headerCell is null)
            {
                _headerCell = new DataGridViewRowHeaderCell();
                _headerCell.OwningRow = this;
            }

            return _headerCell;
        }
        set
        {
            _headerCell = value;
            if (value is not null) value.OwningRow = this;
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            var clamped = Math.Max(value, _minimumHeight);
            if (clamped == _height) return;

            _height = clamped;
            DataGridView?.OnRowHeightChangedCore(this);
        }
    }

    public override DataGridViewCellStyle InheritedStyle
    {
        get
        {
            var resolved = new DataGridViewCellStyle();
            if (DataGridView is { } grid) resolved.ApplyStyle(grid.DefaultCellStyle);
            if (HasDefaultCellStyle) resolved.ApplyStyle(DefaultCellStyle);

            return resolved;
        }
    }

    /// <summary>True for the single placeholder row AllowUserToAddRows shows at the bottom.</summary>
    public bool IsNewRow => DataGridView is { } grid && grid.NewRowIndex == Index && Index >= 0;

    public int MinimumHeight
    {
        get => _minimumHeight;
        set
        {
            if (value < 2) throw new ArgumentOutOfRangeException(nameof(value));

            _minimumHeight = value;
            if (_height < value) Height = value;
        }
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

    public override bool Selected
    {
        get => _selected;
        set
        {
            if (value == _selected) return;

            _selected = value;
            DataGridView?.OnRowSelectedChangedCore(this);
        }
    }

    public override DataGridViewElementStates State =>
        (Selected ? DataGridViewElementStates.Selected : DataGridViewElementStates.None) |
        (Visible ? DataGridViewElementStates.Visible : DataGridViewElementStates.None) |
        (Frozen ? DataGridViewElementStates.Frozen : DataGridViewElementStates.None) |
        (Displayed ? DataGridViewElementStates.Displayed : DataGridViewElementStates.None);

    public override bool Visible
    {
        get => base.Visible;
        set => base.Visible = value;
    }

    public override object Clone()
    {
        var clone = (DataGridViewRow)base.Clone();
        clone._height = _height;
        clone._minimumHeight = _minimumHeight;
        clone._errorText = _errorText;

        return clone;
    }

    public void CreateCells(DataGridView dataGridView)
    {
        ArgumentNullException.ThrowIfNull(dataGridView);

        _cells = CreateCellsInstance();
        foreach (DataGridViewColumn column in dataGridView.Columns)
        {
            var cell = (DataGridViewCell)(column.CellTemplate?.Clone() ?? new DataGridViewTextBoxCell());
            cell.OwningColumn = column;
            cell.OwningRow = this;
            _cells.AddCore(cell);
        }
    }

    public void CreateCells(DataGridView dataGridView, params object[] values)
    {
        CreateCells(dataGridView);
        SetValues(values);
    }

    protected virtual DataGridViewCellCollection CreateCellsInstance() => new(this);

    public string GetErrorText(int rowIndex) => ErrorText;

    public virtual int GetPreferredHeight(int rowIndex, DataGridViewAutoSizeRowMode autoSizeRowMode, bool fixedWidth) => Height;

    public virtual DataGridViewElementStates GetState(int rowIndex) => State;

    /// <summary>Writes one value per cell, in column order; extra values beyond Cells.Count are ignored, as in WinForms.</summary>
    public bool SetValues(params object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var changedAny = false;
        for (var index = 0; index < values.Length && index < Cells.Count; index++)
        {
            Cells[index].Value = values[index];
            changedAny = true;
        }

        return changedAny;
    }

    public override string ToString() => $"DataGridViewRow {{ Index={Index} }}";
}
