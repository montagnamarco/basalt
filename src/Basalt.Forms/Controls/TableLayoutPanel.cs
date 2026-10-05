using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>A panel that arranges its children in a grid of columns and rows.</summary>
[DefaultProperty("ColumnCount")]
public partial class TableLayoutPanel : Panel, IExtenderProvider
{
    private TableLayoutColumnStyleCollection? _columnStyles;
    private TableLayoutRowStyleCollection? _rowStyles;
    private int _columnCount;
    private int _rowCount;
    private TableLayoutPanelGrowStyle _growStyle = TableLayoutPanelGrowStyle.AddRows;
    private TableLayoutPanelCellBorderStyle _cellBorderStyle = TableLayoutPanelCellBorderStyle.None;

    private int _computedColumns;
    private int _computedRows;
    private int[] _columnWidths = [];
    private int[] _rowHeights = [];
    private Control?[,] _cells = new Control?[0, 0];
    private Dictionary<Control, TableLayoutPanelCellPosition> _positions = [];

    public TableLayoutPanel()
    {
    }

    [DefaultValue(BorderStyle.None)]
    public new BorderStyle BorderStyle
    {
        get => base.BorderStyle;
        set => base.BorderStyle = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public new TableLayoutControlCollection Controls => (TableLayoutControlCollection)base.Controls;

    [DefaultValue(0)]
    [Localizable(true)]
    public int ColumnCount
    {
        get => _columnCount;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "ColumnCount must not be negative.");

            if (value == _columnCount) return;

            _columnCount = value;
            PerformLayout(this, "ColumnCount");
        }
    }

    [DefaultValue(0)]
    [Localizable(true)]
    public int RowCount
    {
        get => _rowCount;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "RowCount must not be negative.");

            if (value == _rowCount) return;

            _rowCount = value;
            PerformLayout(this, "RowCount");
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [MergableProperty(false)]
    public TableLayoutColumnStyleCollection ColumnStyles => _columnStyles ??= new TableLayoutColumnStyleCollection { Owner = this };

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [MergableProperty(false)]
    public TableLayoutRowStyleCollection RowStyles => _rowStyles ??= new TableLayoutRowStyleCollection { Owner = this };

    [DefaultValue(TableLayoutPanelGrowStyle.AddRows)]
    public TableLayoutPanelGrowStyle GrowStyle
    {
        get => _growStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TableLayoutPanelGrowStyle));

            if (value == _growStyle) return;

            _growStyle = value;
            PerformLayout(this, "GrowStyle");
        }
    }

    [DefaultValue(TableLayoutPanelCellBorderStyle.None)]
    [Localizable(true)]
    public TableLayoutPanelCellBorderStyle CellBorderStyle
    {
        get => _cellBorderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TableLayoutPanelCellBorderStyle));

            if (value == _cellBorderStyle) return;

            _cellBorderStyle = value;
            PerformLayout(this, "CellBorderStyle");
            Invalidate();
        }
    }

    [Browsable(false)]
    public override LayoutEngine LayoutEngine => TableLayout.Instance;

    private TableLayoutSettings? _layoutSettings;

    /// <summary>The panel's table as settings; assigning settings of another panel copies its table.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TableLayoutSettings LayoutSettings
    {
        get => _layoutSettings ??= new TableLayoutSettings(this);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            LayoutSettings.CopyFrom(value);
        }
    }

    protected override ControlCollection CreateControlsInstance() => new TableLayoutControlCollection(this);

    bool IExtenderProvider.CanExtend(object extendee) => extendee is Control and not TableLayoutPanel;

    [DefaultValue(-1)]
    public int GetColumn(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return TableLayout.GetColumn((IArrangedElement)control);
    }

    public void SetColumn(Control control, int column)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (column < -1) throw new ArgumentOutOfRangeException(nameof(column), column, "Column must be -1 or greater.");

        TableLayout.SetColumn((IArrangedElement)control, column);
        PerformLayout(control, "Column");
    }

    [DefaultValue(-1)]
    public int GetRow(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return TableLayout.GetRow((IArrangedElement)control);
    }

    public void SetRow(Control control, int row)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (row < -1) throw new ArgumentOutOfRangeException(nameof(row), row, "Row must be -1 or greater.");

        TableLayout.SetRow((IArrangedElement)control, row);
        PerformLayout(control, "Row");
    }

    [DefaultValue(1)]
    public int GetColumnSpan(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return TableLayout.GetColumnSpan((IArrangedElement)control);
    }

    public void SetColumnSpan(Control control, int value)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "ColumnSpan must be 1 or greater.");

        TableLayout.SetColumnSpan((IArrangedElement)control, value);
        PerformLayout(control, "ColumnSpan");
    }

    [DefaultValue(1)]
    public int GetRowSpan(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return TableLayout.GetRowSpan((IArrangedElement)control);
    }

    public void SetRowSpan(Control control, int value)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "RowSpan must be 1 or greater.");

        TableLayout.SetRowSpan((IArrangedElement)control, value);
        PerformLayout(control, "RowSpan");
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TableLayoutPanelCellPosition GetCellPosition(Control control) => new(GetColumn(control), GetRow(control));

    public void SetCellPosition(Control control, TableLayoutPanelCellPosition position)
    {
        SuspendLayout();

        try
        {
            SetColumn(control, position.Column);
            SetRow(control, position.Row);
        }
        finally
        {
            ResumeLayout();
        }
    }

    /// <summary>The width of every column, as the last layout computed them.</summary>
    public int[] GetColumnWidths()
    {
        PerformLayout();

        return (int[])_columnWidths.Clone();
    }

    /// <summary>The height of every row, as the last layout computed them.</summary>
    public int[] GetRowHeights()
    {
        PerformLayout();

        return (int[])_rowHeights.Clone();
    }

    /// <summary>The control occupying the cell, including a cell it only reaches through a span; null when the cell is empty or outside the grid.</summary>
    public Control? GetControlFromPosition(int column, int row)
    {
        PerformLayout();

        if (column < 0 || column >= _computedColumns || row < 0 || row >= _computedRows) return null;

        return _cells[row, column];
    }

    /// <summary>The cell a control was placed in by the last layout; (-1, -1) when it is not one of this panel's children.</summary>
    public TableLayoutPanelCellPosition GetPositionFromControl(Control? control)
    {
        PerformLayout();

        if (control is not null && _positions.TryGetValue(control, out var position)) return position;

        return new TableLayoutPanelCellPosition(-1, -1);
    }

    /// <summary>Called by the layout engine once it has placed every cell, so the panel can answer size and position queries without laying out again.</summary>
    internal void SetComputedGrid(
        int columns, int rows, int[] columnWidths, int[] rowHeights, Control?[,] cells, Dictionary<Control, TableLayoutPanelCellPosition> positions)
    {
        _computedColumns = columns;
        _computedRows = rows;
        _columnWidths = columnWidths;
        _rowHeights = rowHeights;
        _cells = cells;
        _positions = positions;
    }

    internal override Size GetPreferredSizeCore(Size proposedSize) => TableLayout.GetPreferredSize(this, proposedSize);

    /// <summary>Paints the border between cells, and raises <see cref="CellPaint"/> for every cell the clip rectangle touches.</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);

        var borderWidth = TableLayout.BorderWidth(CellBorderStyle);
        var display = DisplayRectangle;
        var columnEdges = TableLayout.Edges(_columnWidths, display.X, borderWidth);
        var rowEdges = TableLayout.Edges(_rowHeights, display.Y, borderWidth);

        for (var row = 0; row < _computedRows; row++)
        {
            for (var column = 0; column < _computedColumns; column++)
            {
                var cellBounds = new Rectangle(columnEdges[column], rowEdges[row], _columnWidths[column], _rowHeights[row]);
                if (!cellBounds.IntersectsWith(e.ClipRectangle)) continue;

                using var cellArguments = new TableLayoutCellPaintEventArgs(e.Graphics, e.ClipRectangle, cellBounds, column, row);
                OnCellPaint(cellArguments);

                if (borderWidth > 0) DrawCellBorder(e.Graphics, cellBounds, borderWidth);
            }
        }
    }

    private void DrawCellBorder(Graphics graphics, Rectangle cellBounds, int borderWidth)
    {
        var slot = Rectangle.Inflate(cellBounds, borderWidth, borderWidth);

        switch (CellBorderStyle)
        {
            case TableLayoutPanelCellBorderStyle.Single:
                using (var pen = new Pen(SystemColors.ControlDark))
                {
                    graphics.DrawRectangle(pen, slot.X, slot.Y, Math.Max(slot.Width - 1, 0), Math.Max(slot.Height - 1, 0));
                }

                break;

            case TableLayoutPanelCellBorderStyle.Inset:
            case TableLayoutPanelCellBorderStyle.InsetDouble:
                ControlPaint.DrawBorder3D(graphics, slot, Border3DStyle.Sunken);
                break;

            case TableLayoutPanelCellBorderStyle.Outset:
            case TableLayoutPanelCellBorderStyle.OutsetDouble:
            case TableLayoutPanelCellBorderStyle.OutsetPartial:
                ControlPaint.DrawBorder3D(graphics, slot, Border3DStyle.Raised);
                break;
        }
    }

    protected virtual void OnCellPaint(TableLayoutCellPaintEventArgs e) => ((TableLayoutCellPaintEventHandler?)Events[s_cellPaintEvent])?.Invoke(this, e);

    public event TableLayoutCellPaintEventHandler? CellPaint
    {
        add => Events.AddHandler(s_cellPaintEvent, value);
        remove => Events.RemoveHandler(s_cellPaintEvent, value);
    }

    private static readonly object s_cellPaintEvent = new();
}
