using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// A grid of DataGridViewColumn/DataGridViewRow/DataGridViewCell: the model, selection and the
/// few events this round wires end to end. Painting lives in DataGridView.Painting.cs, hit
/// testing and bounds math in DataGridView.Layout.cs, mouse/keyboard/editing in DataGridView.Input.cs.
/// </summary>
/// <remarks>
/// Left out this round (never stubbed: simply absent, so they stay in the pending list):
/// DataSource/DataMember/AutoGenerateColumns data binding, VirtualMode, AllowUserToAddRows'
/// automatic placeholder row and AllowUserToDeleteRows, AutoResize*/AutoSizeColumnsMode layout,
/// row sharing, clipboard content, accessibility objects, and every CheckBox/ComboBox/Button/
/// Link/Image cell and column type (their abstract hooks on DataGridViewCell are in place).
/// </remarks>
[ComplexBindingProperties("DataSource", "DataMember")]
public partial class DataGridView : Control, ISupportInitialize
{
    private DataGridViewColumnCollection? _columns;
    private DataGridViewRowCollection? _rows;
    private DataGridViewRow _rowTemplate = new();

    private DataGridViewCellStyle? _defaultCellStyle;
    private DataGridViewCellStyle? _columnHeadersDefaultCellStyle;
    private DataGridViewCellStyle? _rowHeadersDefaultCellStyle;
    private DataGridViewCellStyle? _rowsDefaultCellStyle;
    private DataGridViewCellStyle? _alternatingRowsDefaultCellStyle;

    private DataGridViewCell? _currentCell;
    private readonly HashSet<DataGridViewCell> _selectedCells = [];
    private readonly HashSet<int> _selectedRowIndexes = [];
    private readonly HashSet<int> _selectedColumnIndexes = [];

    private bool _allowUserToAddRows = true;
    private bool _allowUserToDeleteRows = true;
    private bool _allowUserToResizeColumns = true;
    private bool _allowUserToResizeRows = true;
    private bool _columnHeadersVisible = true;
    private bool _rowHeadersVisible = true;
    private bool _readOnly;
    private bool _multiSelect = true;
    private int _columnHeadersHeight = 23;
    private int _rowHeadersWidth = 41;
    private Color _gridColor = SystemColors.ControlDark;
    private Color _backgroundColor = SystemColors.AppWorkspace;
    private BorderStyle _borderStyle = BorderStyle.FixedSingle;
    private DataGridViewCellBorderStyle _cellBorderStyle = DataGridViewCellBorderStyle.Single;
    private DataGridViewSelectionMode _selectionMode = DataGridViewSelectionMode.RowHeaderSelect;
    private DataGridViewEditMode _editMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
    private DataGridViewAutoSizeColumnsMode _autoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

    public DataGridView()
    {
        SetStyle(ControlStyles.ResizeRedraw, true);
        TabStop = true;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public virtual DataGridViewAdvancedBorderStyle AdjustedTopLeftHeaderBorderStyle => new();

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public DataGridViewAdvancedBorderStyle AdvancedCellBorderStyle { get; } = new()
    {
        All = DataGridViewAdvancedCellBorderStyle.Single
    };

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public DataGridViewAdvancedBorderStyle AdvancedColumnHeadersBorderStyle { get; } = new();

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public DataGridViewAdvancedBorderStyle AdvancedRowHeadersBorderStyle { get; } = new();

    [DefaultValue(true)]
    public bool AllowUserToAddRows
    {
        get => _allowUserToAddRows;
        set
        {
            if (value == _allowUserToAddRows) return;

            _allowUserToAddRows = value;
            OnAllowUserToAddRowsChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    public bool AllowUserToDeleteRows
    {
        get => _allowUserToDeleteRows;
        set
        {
            if (value == _allowUserToDeleteRows) return;

            _allowUserToDeleteRows = value;
            OnAllowUserToDeleteRowsChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    public bool AllowUserToResizeColumns
    {
        get => _allowUserToResizeColumns;
        set
        {
            if (value == _allowUserToResizeColumns) return;

            _allowUserToResizeColumns = value;
            OnAllowUserToResizeColumnsChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    public bool AllowUserToResizeRows
    {
        get => _allowUserToResizeRows;
        set
        {
            if (value == _allowUserToResizeRows) return;

            _allowUserToResizeRows = value;
            OnAllowUserToResizeRowsChanged(EventArgs.Empty);
        }
    }

    public DataGridViewCellStyle AlternatingRowsDefaultCellStyle
    {
        get => _alternatingRowsDefaultCellStyle ??= new DataGridViewCellStyle();
        set
        {
            _alternatingRowsDefaultCellStyle = value;
            OnAlternatingRowsDefaultCellStyleChanged(EventArgs.Empty);
            if (value is not null) OnCellStyleContentChangedCore(value, DataGridViewCellStyleScopes.AlternatingRows);
            Invalidate();
        }
    }

    internal bool HasAlternatingRowsDefaultCellStyle => _alternatingRowsDefaultCellStyle is not null;

    [DefaultValue(BorderStyle.FixedSingle)]
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(BorderStyle));
            if (value == _borderStyle) return;

            _borderStyle = value;
            OnBorderStyleChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [DefaultValue(DataGridViewAutoSizeColumnsMode.None)]
    public DataGridViewAutoSizeColumnsMode AutoSizeColumnsMode
    {
        get => _autoSizeColumnsMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DataGridViewAutoSizeColumnsMode));
            if (value == _autoSizeColumnsMode) return;

            var previousModes = Columns.Cast<DataGridViewColumn>().Select(column => column.AutoSizeMode).ToArray();
            _autoSizeColumnsMode = value;
            OnAutoSizeColumnsModeChanged(new DataGridViewAutoSizeColumnsModeEventArgs(previousModes));
        }
    }

    public Color BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            _portableBackgroundColorExplicit = true;
            if (value == _backgroundColor) return;

            _backgroundColor = value;
            OnBackgroundColorChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [DefaultValue(DataGridViewCellBorderStyle.Single)]
    public DataGridViewCellBorderStyle CellBorderStyle
    {
        get => _cellBorderStyle;
        set
        {
            if (value == _cellBorderStyle) return;

            _cellBorderStyle = value;
            OnCellBorderStyleChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [DefaultValue(DataGridViewHeaderBorderStyle.Raised)]
    public DataGridViewHeaderBorderStyle ColumnHeadersBorderStyle { get; set; } = DataGridViewHeaderBorderStyle.Raised;

    public DataGridViewCellStyle ColumnHeadersDefaultCellStyle
    {
        get => _columnHeadersDefaultCellStyle ??= new DataGridViewCellStyle();
        set
        {
            _columnHeadersDefaultCellStyle = value;
            OnColumnHeadersDefaultCellStyleChanged(EventArgs.Empty);
            if (value is not null) OnCellStyleContentChangedCore(value, DataGridViewCellStyleScopes.ColumnHeaders);
            Invalidate();
        }
    }

    public int ColumnHeadersHeight
    {
        get => _columnHeadersHeight;
        set
        {
            var clamped = Math.Clamp(value, 4, 32768);
            if (clamped == _columnHeadersHeight) return;

            _columnHeadersHeight = clamped;
            OnColumnHeadersHeightChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    public DataGridViewColumnHeadersHeightSizeMode ColumnHeadersHeightSizeMode { get; set; } = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;

    [DefaultValue(true)]
    public bool ColumnHeadersVisible
    {
        get => _columnHeadersVisible;
        set
        {
            if (value == _columnHeadersVisible) return;

            _columnHeadersVisible = value;
            Invalidate();
        }
    }

    [MergableProperty(false)]
    public DataGridViewColumnCollection Columns => _columns ??= CreateColumnsInstance();

    [Browsable(false)]
    public DataGridViewCell? CurrentCell
    {
        get => _currentCell;
        set
        {
            // As WinForms: a current cell whose edit cannot be committed cannot be left.
            if (!SetCurrentCell(value, throughMouseClick: false))
            {
                throw new InvalidOperationException("Operation did not succeed because the program cannot commit or quit a cell value change.");
            }
        }
    }

    [Browsable(false)]
    public Point CurrentCellAddress => _currentCell is { } cell ? new Point(cell.ColumnIndex, cell.RowIndex) : new Point(-1, -1);

    [Browsable(false)]
    public DataGridViewRow? CurrentRow => _currentCell?.OwningRow;

    public DataGridViewCellStyle DefaultCellStyle
    {
        get => _defaultCellStyle ??= DefaultDefaultCellStyle();
        set
        {
            _defaultCellStyle = value;
            OnDefaultCellStyleChanged(EventArgs.Empty);
            if (value is not null) OnCellStyleContentChangedCore(value, DataGridViewCellStyleScopes.DataGridView);
            Invalidate();
        }
    }

    [DefaultValue(DataGridViewEditMode.EditOnKeystrokeOrF2)]
    public DataGridViewEditMode EditMode
    {
        get => _editMode;
        set
        {
            if (value == _editMode) return;

            _editMode = value;
            OnEditModeChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public Control? EditingControl { get; private set; }

    public Color GridColor
    {
        get => _gridColor;
        set
        {
            _portableGridColorExplicit = true;
            if (value == _gridColor) return;

            _gridColor = value;
            OnGridColorChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [Browsable(false)]
    public bool IsCurrentCellDirty { get; private set; }

    [Browsable(false)]
    public bool IsCurrentCellInEditMode => EditingControl is not null;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DataGridViewCell this[int columnIndex, int rowIndex]
    {
        get => Rows[rowIndex].Cells[columnIndex];
        set => Rows[rowIndex].Cells[columnIndex] = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DataGridViewCell this[string columnName, int rowIndex]
    {
        get => Rows[rowIndex].Cells[columnName];
        set => Rows[rowIndex].Cells[columnName] = value;
    }

    [DefaultValue(true)]
    public bool MultiSelect
    {
        get => _multiSelect;
        set
        {
            if (value == _multiSelect) return;

            _multiSelect = value;
            OnMultiSelectChanged(EventArgs.Empty);
        }
    }

    /// <summary>Always -1 this round: AllowUserToAddRows does not yet maintain the automatic blank row at the bottom.</summary>
    [Browsable(false)]
    public int NewRowIndex => -1;

    [DefaultValue(false)]
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (value == _readOnly) return;

            _readOnly = value;
            OnReadOnlyChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(DataGridViewHeaderBorderStyle.Raised)]
    public DataGridViewHeaderBorderStyle RowHeadersBorderStyle { get; set; } = DataGridViewHeaderBorderStyle.Raised;

    public DataGridViewCellStyle RowHeadersDefaultCellStyle
    {
        get => _rowHeadersDefaultCellStyle ??= new DataGridViewCellStyle();
        set
        {
            _rowHeadersDefaultCellStyle = value;
            OnRowHeadersDefaultCellStyleChanged(EventArgs.Empty);
            if (value is not null) OnCellStyleContentChangedCore(value, DataGridViewCellStyleScopes.RowHeaders);
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool RowHeadersVisible
    {
        get => _rowHeadersVisible;
        set
        {
            if (value == _rowHeadersVisible) return;

            _rowHeadersVisible = value;
            Invalidate();
        }
    }

    public int RowHeadersWidth
    {
        get => _rowHeadersWidth;
        set
        {
            var clamped = Math.Clamp(value, 4, 32768);
            if (clamped == _rowHeadersWidth) return;

            _rowHeadersWidth = clamped;
            OnRowHeadersWidthChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    public DataGridViewRowHeadersWidthSizeMode RowHeadersWidthSizeMode { get; set; } = DataGridViewRowHeadersWidthSizeMode.EnableResizing;

    public DataGridViewRow RowTemplate
    {
        get => _rowTemplate;
        set => _rowTemplate = value ?? throw new ArgumentNullException(nameof(value));
    }

    [Browsable(false)]
    public DataGridViewRowCollection Rows => _rows ??= CreateRowsInstance();

    public DataGridViewCellStyle RowsDefaultCellStyle
    {
        get => _rowsDefaultCellStyle ??= new DataGridViewCellStyle();
        set
        {
            _rowsDefaultCellStyle = value;
            OnRowsDefaultCellStyleChanged(EventArgs.Empty);
            if (value is not null) OnCellStyleContentChangedCore(value, DataGridViewCellStyleScopes.Rows);
            Invalidate();
        }
    }

    [Browsable(false)]
    public DataGridViewSelectedCellCollection SelectedCells => new(EnumerateSelectedCells());

    [Browsable(false)]
    public DataGridViewSelectedColumnCollection SelectedColumns => new(_selectedColumnIndexes.OrderBy(index => index).Select(index => Columns[index]));

    [Browsable(false)]
    public DataGridViewSelectedRowCollection SelectedRows => new(_selectedRowIndexes.OrderBy(index => index).Select(index => Rows[index]));

    [DefaultValue(DataGridViewSelectionMode.RowHeaderSelect)]
    public DataGridViewSelectionMode SelectionMode
    {
        get => _selectionMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DataGridViewSelectionMode));
            if (value == _selectionMode) return;

            _selectionMode = value;
            ClearSelection();
        }
    }

    [Browsable(false)]
    public SortOrder SortOrder { get; private set; } = SortOrder.None;

    [Browsable(false)]
    public DataGridViewColumn? SortedColumn { get; private set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DataGridViewHeaderCell TopLeftHeaderCell { get; set; } = new DataGridViewTopLeftHeaderCell();

    public event EventHandler? AllowUserToAddRowsChanged
    {
        add => Events.AddHandler(s_allowUserToAddRowsChangedEvent, value);
        remove => Events.RemoveHandler(s_allowUserToAddRowsChangedEvent, value);
    }

    public event EventHandler? AllowUserToDeleteRowsChanged
    {
        add => Events.AddHandler(s_allowUserToDeleteRowsChangedEvent, value);
        remove => Events.RemoveHandler(s_allowUserToDeleteRowsChangedEvent, value);
    }

    public event EventHandler? AllowUserToResizeColumnsChanged
    {
        add => Events.AddHandler(s_allowUserToResizeColumnsChangedEvent, value);
        remove => Events.RemoveHandler(s_allowUserToResizeColumnsChangedEvent, value);
    }

    public event EventHandler? AllowUserToResizeRowsChanged
    {
        add => Events.AddHandler(s_allowUserToResizeRowsChangedEvent, value);
        remove => Events.RemoveHandler(s_allowUserToResizeRowsChangedEvent, value);
    }

    public event EventHandler? AlternatingRowsDefaultCellStyleChanged
    {
        add => Events.AddHandler(s_alternatingRowsDefaultCellStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_alternatingRowsDefaultCellStyleChangedEvent, value);
    }

    public event EventHandler? BackgroundColorChanged
    {
        add => Events.AddHandler(s_backgroundColorChangedEvent, value);
        remove => Events.RemoveHandler(s_backgroundColorChangedEvent, value);
    }

    public event EventHandler? BorderStyleChanged
    {
        add => Events.AddHandler(s_borderStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_borderStyleChangedEvent, value);
    }

    public event DataGridViewCellCancelEventHandler? CellBeginEdit
    {
        add => Events.AddHandler(s_cellBeginEditEvent, value);
        remove => Events.RemoveHandler(s_cellBeginEditEvent, value);
    }

    public event EventHandler? CellBorderStyleChanged
    {
        add => Events.AddHandler(s_cellBorderStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_cellBorderStyleChangedEvent, value);
    }

    public event DataGridViewCellEventHandler? CellClick
    {
        add => Events.AddHandler(s_cellClickEvent, value);
        remove => Events.RemoveHandler(s_cellClickEvent, value);
    }

    public event DataGridViewCellEventHandler? CellContentClick
    {
        add => Events.AddHandler(s_cellContentClickEvent, value);
        remove => Events.RemoveHandler(s_cellContentClickEvent, value);
    }

    public event DataGridViewCellEventHandler? CellContentDoubleClick
    {
        add => Events.AddHandler(s_cellContentDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_cellContentDoubleClickEvent, value);
    }

    public event DataGridViewCellEventHandler? CellDoubleClick
    {
        add => Events.AddHandler(s_cellDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_cellDoubleClickEvent, value);
    }

    public event DataGridViewCellEventHandler? CellEndEdit
    {
        add => Events.AddHandler(s_cellEndEditEvent, value);
        remove => Events.RemoveHandler(s_cellEndEditEvent, value);
    }

    public event DataGridViewCellEventHandler? CellEnter
    {
        add => Events.AddHandler(s_cellEnterEvent, value);
        remove => Events.RemoveHandler(s_cellEnterEvent, value);
    }

    public event DataGridViewCellFormattingEventHandler? CellFormatting
    {
        add => Events.AddHandler(s_cellFormattingEvent, value);
        remove => Events.RemoveHandler(s_cellFormattingEvent, value);
    }

    public event DataGridViewCellEventHandler? CellLeave
    {
        add => Events.AddHandler(s_cellLeaveEvent, value);
        remove => Events.RemoveHandler(s_cellLeaveEvent, value);
    }

    public event DataGridViewCellMouseEventHandler? CellMouseDown
    {
        add => Events.AddHandler(s_cellMouseDownEvent, value);
        remove => Events.RemoveHandler(s_cellMouseDownEvent, value);
    }

    public event DataGridViewCellMouseEventHandler? CellMouseUp
    {
        add => Events.AddHandler(s_cellMouseUpEvent, value);
        remove => Events.RemoveHandler(s_cellMouseUpEvent, value);
    }

    public event DataGridViewCellParsingEventHandler? CellParsing
    {
        add => Events.AddHandler(s_cellParsingEvent, value);
        remove => Events.RemoveHandler(s_cellParsingEvent, value);
    }

    public event DataGridViewCellEventHandler? CellValidated
    {
        add => Events.AddHandler(s_cellValidatedEvent, value);
        remove => Events.RemoveHandler(s_cellValidatedEvent, value);
    }

    public event DataGridViewCellValidatingEventHandler? CellValidating
    {
        add => Events.AddHandler(s_cellValidatingEvent, value);
        remove => Events.RemoveHandler(s_cellValidatingEvent, value);
    }

    public event DataGridViewCellEventHandler? CellValueChanged
    {
        add => Events.AddHandler(s_cellValueChangedEvent, value);
        remove => Events.RemoveHandler(s_cellValueChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnAdded
    {
        add => Events.AddHandler(s_columnAddedEvent, value);
        remove => Events.RemoveHandler(s_columnAddedEvent, value);
    }

    public event EventHandler? ColumnHeadersDefaultCellStyleChanged
    {
        add => Events.AddHandler(s_columnHeadersDefaultCellStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_columnHeadersDefaultCellStyleChangedEvent, value);
    }

    public event EventHandler? ColumnHeadersHeightChanged
    {
        add => Events.AddHandler(s_columnHeadersHeightChangedEvent, value);
        remove => Events.RemoveHandler(s_columnHeadersHeightChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnRemoved
    {
        add => Events.AddHandler(s_columnRemovedEvent, value);
        remove => Events.RemoveHandler(s_columnRemovedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnWidthChanged
    {
        add => Events.AddHandler(s_columnWidthChangedEvent, value);
        remove => Events.RemoveHandler(s_columnWidthChangedEvent, value);
    }

    public event EventHandler? CurrentCellChanged
    {
        add => Events.AddHandler(s_currentCellChangedEvent, value);
        remove => Events.RemoveHandler(s_currentCellChangedEvent, value);
    }

    public event EventHandler? DefaultCellStyleChanged
    {
        add => Events.AddHandler(s_defaultCellStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_defaultCellStyleChangedEvent, value);
    }

    public event EventHandler? EditModeChanged
    {
        add => Events.AddHandler(s_editModeChangedEvent, value);
        remove => Events.RemoveHandler(s_editModeChangedEvent, value);
    }

    public event EventHandler? GridColorChanged
    {
        add => Events.AddHandler(s_gridColorChangedEvent, value);
        remove => Events.RemoveHandler(s_gridColorChangedEvent, value);
    }

    public event EventHandler? MultiSelectChanged
    {
        add => Events.AddHandler(s_multiSelectChangedEvent, value);
        remove => Events.RemoveHandler(s_multiSelectChangedEvent, value);
    }

    public event EventHandler? ReadOnlyChanged
    {
        add => Events.AddHandler(s_readOnlyChangedEvent, value);
        remove => Events.RemoveHandler(s_readOnlyChangedEvent, value);
    }

    public event EventHandler? RowHeadersDefaultCellStyleChanged
    {
        add => Events.AddHandler(s_rowHeadersDefaultCellStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_rowHeadersDefaultCellStyleChangedEvent, value);
    }

    public event EventHandler? RowHeadersWidthChanged
    {
        add => Events.AddHandler(s_rowHeadersWidthChangedEvent, value);
        remove => Events.RemoveHandler(s_rowHeadersWidthChangedEvent, value);
    }

    public event DataGridViewRowEventHandler? RowHeightChanged
    {
        add => Events.AddHandler(s_rowHeightChangedEvent, value);
        remove => Events.RemoveHandler(s_rowHeightChangedEvent, value);
    }

    public event DataGridViewCellCancelEventHandler? RowValidating
    {
        add => Events.AddHandler(s_rowValidatingEvent, value);
        remove => Events.RemoveHandler(s_rowValidatingEvent, value);
    }

    public event DataGridViewRowsAddedEventHandler? RowsAdded
    {
        add => Events.AddHandler(s_rowsAddedEvent, value);
        remove => Events.RemoveHandler(s_rowsAddedEvent, value);
    }

    public event EventHandler? RowsDefaultCellStyleChanged
    {
        add => Events.AddHandler(s_rowsDefaultCellStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_rowsDefaultCellStyleChangedEvent, value);
    }

    public event DataGridViewRowsRemovedEventHandler? RowsRemoved
    {
        add => Events.AddHandler(s_rowsRemovedEvent, value);
        remove => Events.RemoveHandler(s_rowsRemovedEvent, value);
    }

    public event EventHandler? SelectionChanged
    {
        add => Events.AddHandler(s_selectionChangedEvent, value);
        remove => Events.RemoveHandler(s_selectionChangedEvent, value);
    }

    public event DataGridViewSortCompareEventHandler? SortCompare
    {
        add => Events.AddHandler(s_sortCompareEvent, value);
        remove => Events.RemoveHandler(s_sortCompareEvent, value);
    }

    public event EventHandler? Sorted
    {
        add => Events.AddHandler(s_sortedEvent, value);
        remove => Events.RemoveHandler(s_sortedEvent, value);
    }

    private static readonly object s_allowUserToAddRowsChangedEvent = new();
    private static readonly object s_allowUserToDeleteRowsChangedEvent = new();
    private static readonly object s_allowUserToResizeColumnsChangedEvent = new();
    private static readonly object s_allowUserToResizeRowsChangedEvent = new();
    private static readonly object s_alternatingRowsDefaultCellStyleChangedEvent = new();
    private static readonly object s_backgroundColorChangedEvent = new();
    private static readonly object s_borderStyleChangedEvent = new();
    private static readonly object s_cellBeginEditEvent = new();
    private static readonly object s_cellBorderStyleChangedEvent = new();
    private static readonly object s_cellClickEvent = new();
    private static readonly object s_cellContentClickEvent = new();
    private static readonly object s_cellContentDoubleClickEvent = new();
    private static readonly object s_cellDoubleClickEvent = new();
    private static readonly object s_cellEndEditEvent = new();
    private static readonly object s_cellEnterEvent = new();
    private static readonly object s_cellFormattingEvent = new();
    private static readonly object s_cellLeaveEvent = new();
    private static readonly object s_cellMouseDownEvent = new();
    private static readonly object s_cellMouseUpEvent = new();
    private static readonly object s_cellParsingEvent = new();
    private static readonly object s_cellValidatedEvent = new();
    private static readonly object s_cellValidatingEvent = new();
    private static readonly object s_cellValueChangedEvent = new();
    private static readonly object s_columnAddedEvent = new();
    private static readonly object s_columnHeadersDefaultCellStyleChangedEvent = new();
    private static readonly object s_columnHeadersHeightChangedEvent = new();
    private static readonly object s_columnRemovedEvent = new();
    private static readonly object s_columnWidthChangedEvent = new();
    private static readonly object s_currentCellChangedEvent = new();
    private static readonly object s_defaultCellStyleChangedEvent = new();
    private static readonly object s_editModeChangedEvent = new();
    private static readonly object s_gridColorChangedEvent = new();
    private static readonly object s_multiSelectChangedEvent = new();
    private static readonly object s_readOnlyChangedEvent = new();
    private static readonly object s_rowHeadersDefaultCellStyleChangedEvent = new();
    private static readonly object s_rowHeadersWidthChangedEvent = new();
    private static readonly object s_rowHeightChangedEvent = new();
    private static readonly object s_rowValidatingEvent = new();
    private static readonly object s_rowsAddedEvent = new();
    private static readonly object s_rowsDefaultCellStyleChangedEvent = new();
    private static readonly object s_rowsRemovedEvent = new();
    private static readonly object s_selectionChangedEvent = new();
    private static readonly object s_sortCompareEvent = new();
    private static readonly object s_sortedEvent = new();

    public bool AreAllCellsSelected(bool includeInvisibleCells) =>
        Columns.Count > 0 && Rows.Count > 0 && _selectedCells.Count == Columns.Count * Rows.Count;

    public virtual bool BeginEdit(bool selectAll) => BeginEditCore(selectAll);

    public bool CancelEdit() => CancelEditCore();

    public void ClearSelection()
    {
        if (_selectedCells.Count == 0 && _selectedRowIndexes.Count == 0 && _selectedColumnIndexes.Count == 0) return;

        _selectedCells.Clear();
        _selectedRowIndexes.Clear();
        _selectedColumnIndexes.Clear();
        OnSelectionChanged(EventArgs.Empty);
        Invalidate();
    }

    public bool EndEdit() => EndEdit(DataGridViewDataErrorContexts.Commit);

    public bool EndEdit(DataGridViewDataErrorContexts context) => EndEditCore(context);

    protected virtual DataGridViewColumnCollection CreateColumnsInstance() => new(this);

    protected virtual DataGridViewRowCollection CreateRowsInstance() => new(this);

    public virtual void Sort(DataGridViewColumn dataGridViewColumn, ListSortDirection direction) => SortCore(dataGridViewColumn, direction);

    public virtual void Sort(IComparer comparer) => SortCore(comparer);

    void ISupportInitialize.BeginInit()
    {
    }

    void ISupportInitialize.EndInit()
    {
    }

    private static DataGridViewCellStyle DefaultDefaultCellStyle()
    {
        var style = new DataGridViewCellStyle
        {
            BackColor = SystemColors.Window,
            ForeColor = SystemColors.ControlText,
            SelectionBackColor = SystemColors.Highlight,
            SelectionForeColor = SystemColors.HighlightText,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
        };
        style.MarkColorsAsDefaults();
        return style;
    }

    private IEnumerable<DataGridViewCell> EnumerateSelectedCells()
    {
        if (_selectedRowIndexes.Count > 0 || _selectedColumnIndexes.Count > 0)
        {
            foreach (var rowIndex in _selectedRowIndexes.OrderBy(index => index))
            foreach (DataGridViewColumn column in Columns)
                yield return Rows[rowIndex].Cells[column.Index];

            foreach (var columnIndex in _selectedColumnIndexes.OrderBy(index => index))
            foreach (DataGridViewRow row in Rows)
                yield return row.Cells[columnIndex];

            yield break;
        }

        foreach (var cell in _selectedCells) yield return cell;
    }
}
