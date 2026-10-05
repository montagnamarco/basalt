using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewTopLeftHeaderCellTests
{
    [Fact]
    public void Top_left_header_uses_the_specialized_cell_and_still_paints_its_corner()
    {
        using var grid = new System.Windows.Forms.DataGridView { Size = new Size(160, 90), AllowUserToAddRows = false };
        grid.Columns.Add("A", "A");
        grid.Rows.Add("value");
        using var bitmap = new Bitmap(grid.Width, grid.Height);

        grid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

        var header = Assert.IsType<DataGridViewTopLeftHeaderCell>(grid.TopLeftHeaderCell);
        Assert.Contains(nameof(DataGridViewTopLeftHeaderCell), header.ToString());
        Assert.NotEqual(grid.BackgroundColor.ToArgb(), bitmap.GetPixel(5, 5).ToArgb());
    }
}
