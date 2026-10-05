using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewRowBorderCompatibilityTests
{
    [Fact]
    public void Header_border_adjustment_copies_input_and_adds_requested_outer_edges()
    {
        var row = new DataGridViewRow();
        var input = new DataGridViewAdvancedBorderStyle
        {
            Left = DataGridViewAdvancedCellBorderStyle.None,
            Top = DataGridViewAdvancedCellBorderStyle.None,
            Right = DataGridViewAdvancedCellBorderStyle.Inset,
            Bottom = DataGridViewAdvancedCellBorderStyle.None,
        };
        var placeholder = new DataGridViewAdvancedBorderStyle();

        var result = row.AdjustRowHeaderBorderStyle(input, placeholder, true, true, true, true);

        Assert.Same(placeholder, result);
        Assert.Equal(DataGridViewAdvancedCellBorderStyle.Single, result.Left);
        Assert.Equal(DataGridViewAdvancedCellBorderStyle.Single, result.Top);
        Assert.Equal(DataGridViewAdvancedCellBorderStyle.Inset, result.Right);
        Assert.Equal(DataGridViewAdvancedCellBorderStyle.Single, result.Bottom);
        Assert.Equal(DataGridViewAdvancedCellBorderStyle.None, input.Left);
    }

    [Fact]
    public void Row_border_override_reaches_cell_painting_and_raster_output()
    {
        using var grid = new System.Windows.Forms.DataGridView { Size = new Size(180, 90), AllowUserToAddRows = false, ColumnCount = 1 };
        var row = new BorderRow();
        grid.Rows.Add(row);
        DataGridViewAdvancedCellBorderStyle? paintedStyle = null;
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex == 0 && e.ColumnIndex == -1) paintedStyle = e.AdvancedBorderStyle?.Right;
        };
        using var bitmap = new Bitmap(grid.Width, grid.Height);

        grid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

        var bounds = grid.GetCellDisplayRectangle(-1, 0, false);
        Assert.True(row.AdjustCalls > 0);
        Assert.Equal(DataGridViewAdvancedCellBorderStyle.Single, paintedStyle);
        Assert.Equal(SystemColors.ControlDarkDark.ToArgb(), bitmap.GetPixel(bounds.Right - 1, bounds.Y + bounds.Height / 2).ToArgb());
    }

    [Fact]
    public void Row_paint_cells_override_composes_with_default_cell_rendering()
    {
        using var grid = new System.Windows.Forms.DataGridView { Size = new Size(180, 90), AllowUserToAddRows = false, ColumnCount = 1 };
        var row = new PaintingRow();
        grid.Rows.Add(row);
        using var bitmap = new Bitmap(grid.Width, grid.Height);

        grid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

        var bounds = grid.GetCellDisplayRectangle(0, 0, false);
        Assert.Equal(1, row.PaintCellsCalls);
        Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(bounds.X + 3, bounds.Y + 3).ToArgb());
    }

    private sealed class BorderRow : DataGridViewRow
    {
        public int AdjustCalls { get; private set; }

        public override DataGridViewAdvancedBorderStyle AdjustRowHeaderBorderStyle(
            DataGridViewAdvancedBorderStyle input,
            DataGridViewAdvancedBorderStyle placeholder,
            bool singleVerticalBorderAdded,
            bool singleHorizontalBorderAdded,
            bool isFirstDisplayedRow,
            bool isLastVisibleRow)
        {
            AdjustCalls++;
            var result = base.AdjustRowHeaderBorderStyle(input, placeholder, singleVerticalBorderAdded,
                singleHorizontalBorderAdded, isFirstDisplayedRow, isLastVisibleRow);
            result.Right = DataGridViewAdvancedCellBorderStyle.Single;
            return result;
        }
    }

    private sealed class PaintingRow : DataGridViewRow
    {
        public int PaintCellsCalls { get; private set; }

        protected override void PaintCells(Graphics graphics, Rectangle clipBounds, Rectangle rowBounds,
            int rowIndex, DataGridViewElementStates rowState, bool isFirstDisplayedRow, bool isLastVisibleRow,
            DataGridViewPaintParts paintParts)
        {
            PaintCellsCalls++;
            base.PaintCells(graphics, clipBounds, rowBounds, rowIndex, rowState, isFirstDisplayedRow,
                isLastVisibleRow, paintParts);
            var bounds = DataGridView!.GetCellDisplayRectangle(0, rowIndex, false);
            using var brush = new SolidBrush(Color.Magenta);
            graphics.FillRectangle(brush, bounds.X + 2, bounds.Y + 2, 3, 3);
        }
    }
}
