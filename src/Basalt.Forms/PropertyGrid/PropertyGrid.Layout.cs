using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The toolbar strip, the two-column grid and the help pane, stacked top to
/// bottom; within the grid, a fixed-width margin for the +/- glyphs, a name
/// column, a splitter the user can drag, and a value column. Also the one
/// flattening every painter and every mouse handler shares: the rows
/// actually on screen, in order, each with how deep it is nested.
/// </summary>
public partial class PropertyGrid
{
    private const int MarginWidth = 16;
    private const int IndentPerLevel = 12;
    private const int ToolbarHeightSmall = 24;
    private const int ToolbarHeightLarge = 32;

    private int _rowHeight = 16;
    private int _labelColumnWidth = 100;
    private bool _draggingSplitter;

    private void InitializeLayoutState()
    {
        RecomputeRowHeight();
    }

    private void RecomputeRowHeight() => _rowHeight = Math.Max(Font.Height + 5, 16);

    private int ToolbarHeight => !_toolbarVisible ? 0 : _largeButtons ? ToolbarHeightLarge : ToolbarHeightSmall;

    private int HelpHeight => !_helpVisible ? 0 : Font.Height * 2 + 14;

    internal Rectangle ToolbarBounds => new(0, 0, ClientSize.Width, ToolbarHeight);

    internal Rectangle GridBounds
    {
        get
        {
            var top = ToolbarHeight;
            var bottom = ClientSize.Height - HelpHeight;

            return new Rectangle(0, top, ClientSize.Width, Math.Max(bottom - top, 0));
        }
    }

    internal Rectangle HelpBounds
    {
        get
        {
            if (!_helpVisible) return Rectangle.Empty;

            var top = ClientSize.Height - HelpHeight;

            return new Rectangle(0, top, ClientSize.Width, HelpHeight);
        }
    }

    /// <summary>Where the splitter between the name and the value columns sits, in the grid's own client coordinates.</summary>
    internal int SplitterX => Math.Clamp(_labelColumnWidth, MarginWidth + 20, Math.Max(GridBounds.Width - 20, MarginWidth + 20));

    /// <summary>Every row currently on screen, flattened in display order, with how many ancestors (categories, expanded properties) it is nested under.</summary>
    internal List<(GridItem Item, int Depth)> VisibleRowsWithDepth()
    {
        var rows = new List<(GridItem Item, int Depth)>();
        foreach (GridItem item in Root.GridItems) CollectRows(item, 0, rows);

        return rows;
    }

    internal IEnumerable<GridItem> VisibleRows()
    {
        foreach (var row in VisibleRowsWithDepth()) yield return row.Item;
    }

    private static void CollectRows(GridItem item, int depth, List<(GridItem Item, int Depth)> rows)
    {
        rows.Add((item, depth));
        if (!item.Expandable || !item.Expanded) return;

        foreach (GridItem child in item.GridItems) CollectRows(child, depth + 1, rows);
    }

    /// <summary>The row's full-width band within the grid (margin, name and value together), by index into VisibleRowsWithDepth.</summary>
    internal Rectangle RowBounds(int rowIndex)
    {
        var grid = GridBounds;

        return new Rectangle(grid.Left, grid.Top + rowIndex * _rowHeight, grid.Width, _rowHeight);
    }

    internal Rectangle MarginBounds(int rowIndex) => new(GridBounds.Left, RowBounds(rowIndex).Top, MarginWidth, _rowHeight);

    internal Rectangle NameBounds(int rowIndex)
    {
        var row = RowBounds(rowIndex);

        return new Rectangle(row.Left + MarginWidth, row.Top, SplitterX - MarginWidth, row.Height);
    }

    internal Rectangle ValueBounds(int rowIndex)
    {
        var row = RowBounds(rowIndex);

        return new Rectangle(row.Left + SplitterX, row.Top, row.Width - SplitterX, row.Height);
    }

    /// <summary>The +/- glyph's own small square, indented by the row's depth, for an expandable row only.</summary>
    internal Rectangle PlusMinusBounds(int rowIndex, int depth)
    {
        var margin = MarginBounds(rowIndex);
        var size = Math.Min(_rowHeight - 6, 9);
        var x = margin.Left + depth * IndentPerLevel + 2;
        var y = margin.Top + (margin.Height - size) / 2;

        return new Rectangle(x, y, size, size);
    }

    /// <summary>The row index under a point in client coordinates, or -1 outside the grid or past the last row.</summary>
    internal int RowIndexAt(Point clientPoint)
    {
        var grid = GridBounds;
        if (!grid.Contains(clientPoint)) return -1;

        var index = (clientPoint.Y - grid.Top) / _rowHeight;
        var rows = VisibleRowsWithDepth();

        return index >= 0 && index < rows.Count ? index : -1;
    }

    /// <summary>Within a few pixels of the splitter's x, with the mouse anywhere in the grid vertically.</summary>
    internal bool IsOverSplitter(Point clientPoint)
    {
        var grid = GridBounds;
        if (clientPoint.Y < grid.Top || clientPoint.Y >= grid.Bottom) return false;

        return Math.Abs(clientPoint.X - (grid.Left + SplitterX)) <= 3;
    }
}
