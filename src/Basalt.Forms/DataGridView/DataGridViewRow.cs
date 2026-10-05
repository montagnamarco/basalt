using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// One row of the grid: its Cells, one per column, its own height, and the few places where a
/// row is treated specially (IsNewRow, for the AllowUserToAddRows placeholder at the bottom).
/// </summary>
public partial class DataGridViewRow : DataGridViewBand
{
    private DataGridViewCellCollection? _cells;
    private DataGridViewRowHeaderCell? _headerCell;
    private int _height = 22;
    private int _minimumHeight = 3;
    private bool _selected;
    private string _errorText = string.Empty;
    private int _dividerHeight;

    public DataGridViewRow()
    {
    }

    public DataGridViewCellCollection Cells => _cells ??= CreateCellsInstance();

    /// <summary>The source object this row shows, when the grid is bound through DataSource; null while unbound.</summary>
    public object? DataBoundItem { get; internal set; }

    public override DataGridViewCellStyle DefaultCellStyle
    {
        get => base.DefaultCellStyle;
        set
        {
            base.DefaultCellStyle = value;
            DataGridView?.OnRowDefaultCellStyleChangedCore(this);
            if (value is not null) DataGridView?.OnCellStyleContentChangedCore(value, DataGridViewCellStyleScopes.Row);
        }
    }

    public override bool Displayed => base.Displayed;

    public int DividerHeight
    {
        get => _dividerHeight;
        set
        {
            if (value == _dividerHeight) return;

            _dividerHeight = value;
            DataGridView?.OnRowDividerHeightChangedCore(this);
        }
    }

    public string ErrorText
    {
        get => GetErrorText(Index);
        set
        {
            var newValue = value ?? string.Empty;
            if (newValue == _errorText) return;

            _errorText = newValue;
            DataGridView?.OnRowErrorTextChangedCore(this);
        }
    }

    public override bool Frozen
    {
        get => base.Frozen;
        set => SetStateFlag(value, base.Frozen, v => base.Frozen = v, DataGridViewElementStates.Frozen);
    }

    /// <summary>
    /// The row's own menu while it has one, or - while it has none and a grid owns it -
    /// whatever RowContextMenuStripNeeded supplies this time, asked again on every call rather
    /// than cached, exactly as GetErrorText asks RowErrorTextNeeded fresh in virtual mode.
    /// </summary>
    public ContextMenuStrip? GetContextMenuStrip(int rowIndex) => base.ContextMenuStrip ?? DataGridView?.GetRowContextMenuStripCore(rowIndex);

    public override ContextMenuStrip? ContextMenuStrip
    {
        get => GetContextMenuStrip(Index);
        set
        {
            if (ReferenceEquals(base.ContextMenuStrip, value)) return;

            base.ContextMenuStrip = value;
            DataGridView?.OnRowContextMenuStripChangedCore(this);
        }
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
            DataGridView?.OnRowHeaderCellChangedCore(this);
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
            if (value == _minimumHeight)
            {
                if (_height < value) Height = value;
                return;
            }

            _minimumHeight = value;
            DataGridView?.OnRowMinimumHeightChangedCore(this);
            if (_height < value) Height = value;
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

    public override bool Selected
    {
        get => _selected;
        set
        {
            if (value == _selected) return;

            _selected = value;
            DataGridView?.OnRowSelectedChangedCore(this);
            DataGridView?.OnRowStateChangedCore(this, DataGridViewElementStates.Selected);
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
        set => SetStateFlag(value, base.Visible, v => base.Visible = v, DataGridViewElementStates.Visible);
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

    /// <summary>Copies the configured header edges and adds the outer grid edges when requested.</summary>
    public virtual DataGridViewAdvancedBorderStyle AdjustRowHeaderBorderStyle(
        DataGridViewAdvancedBorderStyle dataGridViewAdvancedBorderStyleInput,
        DataGridViewAdvancedBorderStyle dataGridViewAdvancedBorderStylePlaceholder,
        bool singleVerticalBorderAdded,
        bool singleHorizontalBorderAdded,
        bool isFirstDisplayedRow,
        bool isLastVisibleRow)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewAdvancedBorderStyleInput);
        ArgumentNullException.ThrowIfNull(dataGridViewAdvancedBorderStylePlaceholder);

        dataGridViewAdvancedBorderStylePlaceholder.Left = dataGridViewAdvancedBorderStyleInput.Left;
        dataGridViewAdvancedBorderStylePlaceholder.Top = dataGridViewAdvancedBorderStyleInput.Top;
        dataGridViewAdvancedBorderStylePlaceholder.Right = dataGridViewAdvancedBorderStyleInput.Right;
        dataGridViewAdvancedBorderStylePlaceholder.Bottom = dataGridViewAdvancedBorderStyleInput.Bottom;

        if (singleVerticalBorderAdded && dataGridViewAdvancedBorderStylePlaceholder.Left == DataGridViewAdvancedCellBorderStyle.None)
            dataGridViewAdvancedBorderStylePlaceholder.Left = DataGridViewAdvancedCellBorderStyle.Single;

        if (singleHorizontalBorderAdded)
        {
            if (isFirstDisplayedRow && dataGridViewAdvancedBorderStylePlaceholder.Top == DataGridViewAdvancedCellBorderStyle.None)
                dataGridViewAdvancedBorderStylePlaceholder.Top = DataGridViewAdvancedCellBorderStyle.Single;
            if (isLastVisibleRow && dataGridViewAdvancedBorderStylePlaceholder.Bottom == DataGridViewAdvancedCellBorderStyle.None)
                dataGridViewAdvancedBorderStylePlaceholder.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
        }

        return dataGridViewAdvancedBorderStylePlaceholder;
    }

    /// <summary>The row's own text while VirtualMode is off, or - once it is on - whatever RowErrorTextNeeded supplies this time, asked again on every call.</summary>
    public string GetErrorText(int rowIndex) => DataGridView?.GetRowErrorTextCore(this, _errorText) ?? _errorText;

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

    /// <summary>Applies one of the Band boolean flags through its own setter only when it really changes, then reports the flip to the owning grid as RowStateChanged.</summary>
    private void SetStateFlag(bool value, bool current, Action<bool> apply, DataGridViewElementStates flag)
    {
        if (value == current) return;

        apply(value);
        DataGridView?.OnRowStateChangedCore(this, flag);
    }
}
