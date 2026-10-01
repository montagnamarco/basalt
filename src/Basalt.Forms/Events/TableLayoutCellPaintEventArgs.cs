using System.Drawing;

namespace System.Windows.Forms;

/// <summary>The graphics and bounds of one cell of a <see cref="TableLayoutPanel"/> that is being painted.</summary>
public class TableLayoutCellPaintEventArgs : PaintEventArgs
{
    public TableLayoutCellPaintEventArgs(Graphics g, Rectangle clipRectangle, Rectangle cellBounds, int column, int row)
        : base(g, clipRectangle)
    {
        CellBounds = cellBounds;
        Column = column;
        Row = row;
    }

    public Rectangle CellBounds { get; }

    public int Column { get; }

    public int Row { get; }
}
