using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewErrorIconTests
{
    [Fact]
    public void Error_icon_is_cell_local_and_paints_over_a_stretched_image()
    {
        using var image = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Blue);
        using var grid = new System.Windows.Forms.DataGridView { Size = new Size(240, 120) };
        grid.Columns.Add(new DataGridViewImageColumn { ImageLayout = DataGridViewImageCellLayout.Stretch });
        grid.Rows.Add(image);
        var cell = grid.Rows[0].Cells[0];
        using var withoutError = Draw(grid);
        cell.ErrorText = "Invalid value";
        var localBounds = cell.ErrorIconBounds;
        Assert.Equal(new Size(12, 12), localBounds.Size);
        Assert.True(new Rectangle(Point.Empty, cell.Size).Contains(localBounds));
        var paintedBounds = localBounds;
        paintedBounds.Offset(grid.GetCellDisplayRectangle(0, 0, false).Location);
        using var withError = Draw(grid);
        AssertPixelsChangedOnlyInside(withoutError, withError, paintedBounds);
        grid.ShowCellErrors = false;
        Assert.Equal(Rectangle.Empty, cell.ErrorIconBounds);
        using var hiddenError = Draw(grid);
        Assert.Equal(withoutError.GetPixel(paintedBounds.Left + 2, paintedBounds.Top + 2),
            hiddenError.GetPixel(paintedBounds.Left + 2, paintedBounds.Top + 2));
    }

    [Fact]
    public void Row_error_visibility_is_independent_from_cell_error_visibility()
    {
        using var grid = new System.Windows.Forms.DataGridView { Size = new Size(240, 120) };
        grid.Columns.Add("value", "Value");
        grid.Rows.Add("");
        using var before = Draw(grid);
        grid.Rows[0].ErrorText = "Invalid row";
        grid.ShowCellErrors = false;
        var local = grid.Rows[0].HeaderCell.ErrorIconBounds;
        Assert.False(local.IsEmpty);
        var painted = local;
        painted.Offset(grid.GetCellDisplayRectangle(-1, 0, false).Location);
        using var after = Draw(grid);
        AssertPixelsChangedOnlyInside(before, after, painted);
        grid.ShowRowErrors = false;
        Assert.True(grid.Rows[0].HeaderCell.ErrorIconBounds.IsEmpty);
    }

    private static Bitmap Draw(System.Windows.Forms.DataGridView grid)
    {
        var bitmap = new Bitmap(grid.Width, grid.Height);
        grid.DrawToBitmap(bitmap, grid.ClientRectangle);
        return bitmap;
    }

    private static void AssertPixelsChangedOnlyInside(Bitmap before, Bitmap after, Rectangle expected)
    {
        var changed = 0;
        for (var y = 0; y < before.Height; y++)
        for (var x = 0; x < before.Width; x++)
        {
            if (before.GetPixel(x, y) == after.GetPixel(x, y)) continue;
            Assert.True(expected.Contains(x, y), $"Error pixel outside its bounds: {x}, {y}");
            changed++;
        }
        Assert.True(changed > 0);
    }
}
