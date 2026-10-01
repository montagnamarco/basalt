using System.Drawing;

namespace System.Windows.Forms.Layout;

/// <summary>
/// Arranges a <see cref="TableLayoutPanel"/>'s children in a grid: columns
/// and rows sized by their styles, cells assigned explicitly or filled
/// automatically, children placed inside their cell by their dock or anchor.
/// </summary>
/// <remarks>
/// Written from WinForms' documented behaviour, not from its source. Column
/// and row sizing is absolute first, then autosize columns measured from the
/// single-span children they hold, then whatever is left split among percent
/// columns in proportion to their percentage (evenly when none is given).
/// Spanning children occupy every cell they cover but only single-span
/// children are measured for an autosize column or row, which keeps the
/// measurement from depending on how other columns end up sized.
/// </remarks>
internal sealed class TableLayout : LayoutEngine
{
    public static TableLayout Instance { get; } = new();

    public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
    {
        var control = AsControl(container, nameof(container));
        if (control is not TableLayoutPanel panel)
        {
            throw new NotSupportedException($"{nameof(TableLayout)} cannot lay out an object of type {control.GetType()}.");
        }

        return Layout(panel);
    }

    private static bool Layout(TableLayoutPanel panel)
    {
        var assignment = BuildAssignment(panel);
        var borderWidth = BorderWidth(panel.CellBorderStyle);
        var display = panel.DisplayRectangle;

        var columnWidths = ComputeColumnWidths(panel, assignment, display.Width, borderWidth);
        var rowHeights = ComputeRowHeights(panel, assignment, display.Height, borderWidth);

        var columnEdges = Edges(columnWidths, display.X, borderWidth);
        var rowEdges = Edges(rowHeights, display.Y, borderWidth);

        foreach (var (control, position) in assignment.Positions)
        {
            var element = (IArrangedElement)control;
            if (!element.ParticipatesInLayout) continue;

            var columnSpan = Math.Max(1, Math.Min(GetColumnSpan(element), assignment.Columns - position.Column));
            var rowSpan = Math.Max(1, Math.Min(GetRowSpan(element), assignment.Rows - position.Row));

            var cellBounds = new Rectangle(
                columnEdges[position.Column],
                rowEdges[position.Row],
                columnEdges[position.Column + columnSpan] - borderWidth - columnEdges[position.Column],
                rowEdges[position.Row + rowSpan] - borderWidth - rowEdges[position.Row]);

            var bounds = PlaceInCell(element, cellBounds);
            if (element.Bounds != bounds) element.SetBounds(bounds, BoundsSpecified.None);
        }

        panel.SetComputedGrid(assignment.Columns, assignment.Rows, columnWidths, rowHeights, assignment.Cells, assignment.Positions);

        return ((IArrangedElement)panel).LayoutStore.AutoSize;
    }

    /// <summary>The size the panel needs to show every column and row at its own content size, ignoring percent columns.</summary>
    internal static Size GetPreferredSize(TableLayoutPanel panel, Size proposedSize)
    {
        var assignment = BuildAssignment(panel);
        var borderWidth = BorderWidth(panel.CellBorderStyle);

        // Percent columns and rows have nothing to be a percentage of here, so they measure as zero, as in WinForms.
        var columnWidths = ComputeColumnWidths(panel, assignment, 0, borderWidth);
        var rowHeights = ComputeRowHeights(panel, assignment, 0, borderWidth);

        var width = Sum(columnWidths) + borderWidth * (assignment.Columns + 1);
        var height = Sum(rowHeights) + borderWidth * (assignment.Rows + 1);

        var padding = panel.Padding;

        return new Size(width + padding.Horizontal, height + padding.Vertical);
    }

    /// <summary>The pixels <see cref="TableLayoutPanelCellBorderStyle"/> draws between cells and around the grid's edge.</summary>
    internal static int BorderWidth(TableLayoutPanelCellBorderStyle style) => style switch
    {
        TableLayoutPanelCellBorderStyle.None => 0,
        TableLayoutPanelCellBorderStyle.Single => 1,
        TableLayoutPanelCellBorderStyle.OutsetPartial => 1,
        TableLayoutPanelCellBorderStyle.Inset => 2,
        TableLayoutPanelCellBorderStyle.Outset => 2,
        TableLayoutPanelCellBorderStyle.InsetDouble => 3,
        TableLayoutPanelCellBorderStyle.OutsetDouble => 3,
        _ => 0,
    };

    /// <summary>The left edge of every column (or the top edge of every row), the last entry being the edge past the end.</summary>
    internal static int[] Edges(int[] sizes, int start, int borderWidth)
    {
        var edges = new int[sizes.Length + 1];
        edges[0] = start + borderWidth;

        for (var i = 0; i < sizes.Length; i++) edges[i + 1] = edges[i] + sizes[i] + borderWidth;

        return edges;
    }

    private static int Sum(int[] values)
    {
        var total = 0;
        foreach (var value in values) total += value;

        return total;
    }

    // --- The per-child cell settings an extender provider stores on each control ---

    private sealed class CellSettings
    {
        public int Column = -1;
        public int Row = -1;
        public int ColumnSpan = 1;
        public int RowSpan = 1;
    }

    private static readonly object CellSettingsKey = new();

    private static CellSettings GetCellSettings(IArrangedElement element)
    {
        var extended = element.LayoutStore.Extended;
        if (extended.TryGetValue(CellSettingsKey, out var value) && value is CellSettings settings) return settings;

        settings = new CellSettings();
        extended[CellSettingsKey] = settings;

        return settings;
    }

    internal static int GetColumn(IArrangedElement element) => GetCellSettings(element).Column;

    internal static void SetColumn(IArrangedElement element, int value) => GetCellSettings(element).Column = value;

    internal static int GetRow(IArrangedElement element) => GetCellSettings(element).Row;

    internal static void SetRow(IArrangedElement element, int value) => GetCellSettings(element).Row = value;

    internal static int GetColumnSpan(IArrangedElement element) => GetCellSettings(element).ColumnSpan;

    internal static void SetColumnSpan(IArrangedElement element, int value) => GetCellSettings(element).ColumnSpan = value;

    internal static int GetRowSpan(IArrangedElement element) => GetCellSettings(element).RowSpan;

    internal static void SetRowSpan(IArrangedElement element, int value) => GetCellSettings(element).RowSpan = value;

    // --- Building the grid: explicit cells first, then automatic placement, growing as GrowStyle allows ---

    private sealed class Assignment
    {
        public int Columns;
        public int Rows;
        public required Control?[,] Cells;
        public required Dictionary<Control, TableLayoutPanelCellPosition> Positions;
    }

    private static Assignment BuildAssignment(TableLayoutPanel panel)
    {
        var growStyle = panel.GrowStyle;

        int initialColumns;
        int initialRows;

        switch (growStyle)
        {
            case TableLayoutPanelGrowStyle.AddColumns:
                initialColumns = Math.Max(panel.ColumnCount, 0);
                initialRows = Math.Max(panel.RowCount, 1);
                break;

            case TableLayoutPanelGrowStyle.FixedSize:
                initialColumns = Math.Max(panel.ColumnCount, 1);
                initialRows = Math.Max(panel.RowCount, 1);
                break;

            default: // AddRows
                initialColumns = Math.Max(panel.ColumnCount, 1);
                initialRows = Math.Max(panel.RowCount, 0);
                break;
        }

        var grid = new GridBuilder(initialColumns, initialRows, growStyle);
        var positions = new Dictionary<Control, TableLayoutPanelCellPosition>();

        var children = new List<(Control Control, IArrangedElement Element)>(panel.Controls.Count);
        for (var i = 0; i < panel.Controls.Count; i++)
        {
            var control = panel.Controls[i];
            children.Add((control, (IArrangedElement)control));
        }

        // Pass 1: controls with both column and row set explicitly.
        foreach (var (control, element) in children)
        {
            var column = GetColumn(element);
            var row = GetRow(element);
            if (column < 0 || row < 0) continue;

            var columnSpan = Math.Max(GetColumnSpan(element), 1);
            var rowSpan = Math.Max(GetRowSpan(element), 1);

            grid.EnsureColumn(column + columnSpan - 1);
            grid.EnsureRow(row + rowSpan - 1);

            if (grid.IsFree(column, row, columnSpan, rowSpan)) grid.Place(control, column, row, columnSpan, rowSpan);

            positions[control] = new TableLayoutPanelCellPosition(column, row);
        }

        // Pass 2: controls with only one coordinate fixed, the other found automatically.
        foreach (var (control, element) in children)
        {
            if (positions.ContainsKey(control)) continue;

            var column = GetColumn(element);
            var row = GetRow(element);
            if (column < 0 && row < 0) continue;

            var columnSpan = Math.Max(GetColumnSpan(element), 1);
            var rowSpan = Math.Max(GetRowSpan(element), 1);

            var position = FindFreeCell(grid, growStyle, columnSpan, rowSpan, column >= 0 ? column : null, row >= 0 ? row : null);
            grid.Place(control, position.Column, position.Row, columnSpan, rowSpan);
            positions[control] = position;
        }

        // Pass 3: controls placed wherever the next free cell is, in the order they were added.
        foreach (var (control, element) in children)
        {
            if (positions.ContainsKey(control)) continue;

            var columnSpan = Math.Max(GetColumnSpan(element), 1);
            var rowSpan = Math.Max(GetRowSpan(element), 1);

            var position = FindFreeCell(grid, growStyle, columnSpan, rowSpan, null, null);
            grid.Place(control, position.Column, position.Row, columnSpan, rowSpan);
            positions[control] = position;
        }

        return new Assignment
        {
            Columns = grid.Columns,
            Rows = grid.Rows,
            Cells = grid.ToCells(),
            Positions = positions,
        };
    }

    /// <summary>
    /// The next free cell for a control that is not fully placed explicitly:
    /// scanning down a fixed column, across a fixed row, or row by row (or
    /// column by column, when the panel grows sideways) in the order the
    /// panel fills cells automatically. Growing past the panel's capacity
    /// throws, through <see cref="GridBuilder.EnsureRow"/> or
    /// <see cref="GridBuilder.EnsureColumn"/>, exactly where WinForms does.
    /// </summary>
    private static TableLayoutPanelCellPosition FindFreeCell(
        GridBuilder grid, TableLayoutPanelGrowStyle growStyle, int columnSpan, int rowSpan, int? fixedColumn, int? fixedRow)
    {
        if (fixedColumn is { } column)
        {
            for (var row = 0; ; row++)
            {
                grid.EnsureRow(row + rowSpan - 1);
                grid.EnsureColumn(column + columnSpan - 1);
                if (grid.IsFree(column, row, columnSpan, rowSpan)) return new TableLayoutPanelCellPosition(column, row);
            }
        }

        if (fixedRow is { } row2)
        {
            for (var col = 0; ; col++)
            {
                grid.EnsureColumn(col + columnSpan - 1);
                grid.EnsureRow(row2 + rowSpan - 1);
                if (grid.IsFree(col, row2, columnSpan, rowSpan)) return new TableLayoutPanelCellPosition(col, row2);
            }
        }

        if (growStyle == TableLayoutPanelGrowStyle.AddColumns)
        {
            for (var col = 0; ; col++)
            {
                grid.EnsureColumn(col + columnSpan - 1);

                for (var row = 0; row + rowSpan <= grid.Rows; row++)
                {
                    if (grid.IsFree(col, row, columnSpan, rowSpan)) return new TableLayoutPanelCellPosition(col, row);
                }
            }
        }

        for (var row = 0; ; row++)
        {
            grid.EnsureRow(row + rowSpan - 1);

            for (var col = 0; col + columnSpan <= grid.Columns; col++)
            {
                if (grid.IsFree(col, row, columnSpan, rowSpan)) return new TableLayoutPanelCellPosition(col, row);
            }
        }
    }

    /// <summary>The grid's occupancy while it is being built: which cells are taken, and by which control.</summary>
    private sealed class GridBuilder
    {
        private readonly List<bool[]> _occupiedRows = [];
        private readonly List<Control?[]> _cellRows = [];
        private readonly TableLayoutPanelGrowStyle _growStyle;

        public GridBuilder(int columns, int rows, TableLayoutPanelGrowStyle growStyle)
        {
            Columns = columns;
            _growStyle = growStyle;

            for (var i = 0; i < rows; i++) AddRow();
        }

        public int Columns { get; private set; }

        public int Rows => _occupiedRows.Count;

        public void EnsureRow(int row)
        {
            if (row < Rows) return;

            if (_growStyle != TableLayoutPanelGrowStyle.AddRows)
            {
                throw new ArgumentException("The TableLayoutPanel does not have enough rows for its content.");
            }

            while (row >= Rows) AddRow();
        }

        public void EnsureColumn(int column)
        {
            if (column < Columns) return;

            if (_growStyle != TableLayoutPanelGrowStyle.AddColumns)
            {
                throw new ArgumentException("The TableLayoutPanel does not have enough columns for its content.");
            }

            var newColumns = column + 1;

            for (var i = 0; i < _occupiedRows.Count; i++)
            {
                var occupied = new bool[newColumns];
                Array.Copy(_occupiedRows[i], occupied, _occupiedRows[i].Length);
                _occupiedRows[i] = occupied;

                var cells = new Control?[newColumns];
                Array.Copy(_cellRows[i], cells, _cellRows[i].Length);
                _cellRows[i] = cells;
            }

            Columns = newColumns;
        }

        public bool IsFree(int column, int row, int columnSpan, int rowSpan)
        {
            if (column < 0 || row < 0 || column + columnSpan > Columns || row + rowSpan > Rows) return false;

            for (var r = row; r < row + rowSpan; r++)
            {
                for (var c = column; c < column + columnSpan; c++)
                {
                    if (_occupiedRows[r][c]) return false;
                }
            }

            return true;
        }

        public void Place(Control control, int column, int row, int columnSpan, int rowSpan)
        {
            for (var r = row; r < row + rowSpan && r < Rows; r++)
            {
                for (var c = column; c < column + columnSpan && c < Columns; c++)
                {
                    _occupiedRows[r][c] = true;
                    _cellRows[r][c] = control;
                }
            }
        }

        public Control?[,] ToCells()
        {
            var cells = new Control?[Rows, Columns];

            for (var r = 0; r < Rows; r++)
            {
                for (var c = 0; c < Columns; c++) cells[r, c] = _cellRows[r][c];
            }

            return cells;
        }

        private void AddRow()
        {
            _occupiedRows.Add(new bool[Columns]);
            _cellRows.Add(new Control?[Columns]);
        }
    }

    // --- Sizing columns and rows: absolute, then autosize from single-span content, then percent of what remains ---

    private static int[] ComputeColumnWidths(TableLayoutPanel panel, Assignment assignment, int availableWidth, int borderWidth)
    {
        var columns = assignment.Columns;
        var styles = StyleAt(panel.ColumnStyles, columns);
        var widths = new int[columns];
        var consumed = 0;

        for (var i = 0; i < columns; i++)
        {
            if (styles[i].SizeType != SizeType.Absolute) continue;

            widths[i] = (int)MathF.Round(styles[i].Value, MidpointRounding.AwayFromZero);
            consumed += widths[i];
        }

        for (var i = 0; i < columns; i++)
        {
            if (styles[i].SizeType != SizeType.AutoSize) continue;

            var max = 0;

            foreach (var (control, position) in assignment.Positions)
            {
                if (position.Column != i) continue;

                var element = (IArrangedElement)control;
                if (!element.ParticipatesInLayout || GetColumnSpan(element) != 1) continue;

                var preferred = element.GetPreferredSize(Size.Empty).Width + control.Margin.Horizontal;
                if (preferred > max) max = preferred;
            }

            widths[i] = max;
            consumed += max;
        }

        var remaining = Math.Max(0, availableWidth - (borderWidth * (columns + 1)) - consumed);
        DistributePercent(widths, styles, remaining);

        return widths;
    }

    private static int[] ComputeRowHeights(TableLayoutPanel panel, Assignment assignment, int availableHeight, int borderWidth)
    {
        var rows = assignment.Rows;
        var styles = StyleAt(panel.RowStyles, rows);
        var heights = new int[rows];
        var consumed = 0;

        for (var i = 0; i < rows; i++)
        {
            if (styles[i].SizeType != SizeType.Absolute) continue;

            heights[i] = (int)MathF.Round(styles[i].Value, MidpointRounding.AwayFromZero);
            consumed += heights[i];
        }

        for (var i = 0; i < rows; i++)
        {
            if (styles[i].SizeType != SizeType.AutoSize) continue;

            var max = 0;

            foreach (var (control, position) in assignment.Positions)
            {
                if (position.Row != i) continue;

                var element = (IArrangedElement)control;
                if (!element.ParticipatesInLayout || GetRowSpan(element) != 1) continue;

                var preferred = element.GetPreferredSize(Size.Empty).Height + control.Margin.Vertical;
                if (preferred > max) max = preferred;
            }

            heights[i] = max;
            consumed += max;
        }

        var remaining = Math.Max(0, availableHeight - (borderWidth * (rows + 1)) - consumed);
        DistributePercent(heights, styles, remaining);

        return heights;
    }

    /// <summary>The style of every column or row, an unstyled one past the end of the collection defaulting to AutoSize, as a bare WinForms column or row does.</summary>
    private static (SizeType SizeType, float Value)[] StyleAt(TableLayoutStyleCollection styles, int count)
    {
        var result = new (SizeType SizeType, float Value)[count];

        for (var i = 0; i < count; i++)
        {
            if (i >= styles.Count)
            {
                result[i] = (SizeType.AutoSize, 0f);
            }
            else
            {
                var style = styles[i];
                var value = style is ColumnStyle column ? column.Width : style is RowStyle row ? row.Height : 0f;
                result[i] = (style.SizeType, value);
            }
        }

        return result;
    }

    /// <summary>Splits what is left among the percent columns or rows, proportionally, or evenly when none asked for a share.</summary>
    private static void DistributePercent(int[] sizes, (SizeType SizeType, float Value)[] styles, int remaining)
    {
        var percentIndexes = new List<int>();
        var totalPercent = 0f;

        for (var i = 0; i < styles.Length; i++)
        {
            if (styles[i].SizeType != SizeType.Percent) continue;

            percentIndexes.Add(i);
            totalPercent += styles[i].Value;
        }

        if (percentIndexes.Count == 0) return;

        if (totalPercent <= 0)
        {
            var share = remaining / percentIndexes.Count;
            for (var i = 0; i < percentIndexes.Count; i++)
            {
                sizes[percentIndexes[i]] = i == percentIndexes.Count - 1 ? remaining - (share * (percentIndexes.Count - 1)) : share;
            }

            return;
        }

        var assigned = 0;

        for (var i = 0; i < percentIndexes.Count; i++)
        {
            var isLast = i == percentIndexes.Count - 1;
            var size = isLast ? remaining - assigned : (int)MathF.Round(remaining * (styles[percentIndexes[i]].Value / totalPercent));
            size = Math.Max(size, 0);

            sizes[percentIndexes[i]] = size;
            assigned += size;
        }
    }

    // --- Placing a child inside the cell it was given: Dock fills it, Anchor positions or stretches within it ---

    private static Rectangle PlaceInCell(IArrangedElement element, Rectangle cellBounds)
    {
        var margin = element is Control control ? control.Margin : Padding.Empty;
        var inner = new Rectangle(
            cellBounds.X + margin.Left,
            cellBounds.Y + margin.Top,
            Math.Max(cellBounds.Width - margin.Horizontal, 0),
            Math.Max(cellBounds.Height - margin.Vertical, 0));

        var store = element.LayoutStore;

        if (store.Dock == DockStyle.Fill) return inner;

        if (store.Dock != DockStyle.None)
        {
            var size = store.AutoSize
                ? element.GetPreferredSize(store.Dock is DockStyle.Top or DockStyle.Bottom ? new Size(inner.Width, 0) : new Size(0, inner.Height))
                : element.Bounds.Size;

            return store.Dock switch
            {
                DockStyle.Top => new Rectangle(inner.X, inner.Y, inner.Width, size.Height),
                DockStyle.Bottom => new Rectangle(inner.X, inner.Bottom - size.Height, inner.Width, size.Height),
                DockStyle.Left => new Rectangle(inner.X, inner.Y, size.Width, inner.Height),
                DockStyle.Right => new Rectangle(inner.Right - size.Width, inner.Y, size.Width, inner.Height),
                _ => inner,
            };
        }

        return AnchorInCell(element, inner, store);
    }

    /// <summary>
    /// The same anchor flags as everywhere else, measured from the cell
    /// instead of the container: Left and Right together stretch, one alone
    /// pins that edge, neither centres - which is exactly how WinForms
    /// centres a control whose Anchor is None inside its cell.
    /// </summary>
    private static Rectangle AnchorInCell(IArrangedElement element, Rectangle inner, LayoutSettingsStore store)
    {
        var anchor = store.Anchor;
        var stretchesHorizontally = anchor.HasFlag(AnchorStyles.Left) && anchor.HasFlag(AnchorStyles.Right);
        var stretchesVertically = anchor.HasFlag(AnchorStyles.Top) && anchor.HasFlag(AnchorStyles.Bottom);

        var size = element.Bounds.Size;
        if (store.AutoSize)
        {
            var proposed = new Size(stretchesHorizontally ? inner.Width : 0, stretchesVertically ? inner.Height : 0);
            size = element.GetPreferredSize(proposed);
        }

        var width = stretchesHorizontally ? inner.Width : size.Width;
        var height = stretchesVertically ? inner.Height : size.Height;

        int x;
        if (stretchesHorizontally || anchor.HasFlag(AnchorStyles.Left)) x = inner.X;
        else if (anchor.HasFlag(AnchorStyles.Right)) x = inner.Right - width;
        else x = inner.X + ((inner.Width - width) / 2);

        int y;
        if (stretchesVertically || anchor.HasFlag(AnchorStyles.Top)) y = inner.Y;
        else if (anchor.HasFlag(AnchorStyles.Bottom)) y = inner.Bottom - height;
        else y = inner.Y + ((inner.Height - height) / 2);

        return new Rectangle(x, y, width, height);
    }
}
