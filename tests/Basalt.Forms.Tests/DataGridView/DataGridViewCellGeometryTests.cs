using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewCellGeometryTests
{
    [Fact]
    public void Content_bounds_use_cell_local_coordinates_even_beyond_the_first_row_and_column()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(300, 150) };
        grid.Columns.Add(new DataGridViewCheckBoxColumn());
        grid.Columns.Add(new DataGridViewCheckBoxColumn());
        grid.Rows.Add(false, false);
        grid.Rows.Add(false, false);

        var first = grid.Rows[0].Cells[0];
        var second = grid.Rows[1].Cells[1];
        var cellSize = grid.GetCellDisplayRectangle(1, 1, false).Size;
        var expected = new Rectangle((cellSize.Width - 13) / 2,
            (cellSize.Height - 13) / 2, 13, 13);

        Assert.Equal(expected, first.ContentBounds);
        Assert.Equal(expected, second.ContentBounds);
    }

    [Fact]
    public void Check_box_content_bounds_enclose_every_pixel_changed_by_its_check_mark()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewCheckBoxColumn());
        grid.Rows.Add(false);
        var cell = grid.Rows[0].Cells[0];
        var content = cell.ContentBounds;
        Assert.Equal(new Size(13, 13), content.Size);
        Assert.True(new Rectangle(Point.Empty, cell.Size).Contains(content));

        using var uncheckedBitmap = Draw(grid);
        cell.Value = true;
        using var checkedBitmap = Draw(grid);

        var changedPixels = 0;
        var cellBounds = grid.GetCellDisplayRectangle(0, 0, false);
        for (var y = cellBounds.Top; y < cellBounds.Bottom; y++)
        {
            for (var x = cellBounds.Left; x < cellBounds.Right; x++)
            {
                if (uncheckedBitmap.GetPixel(x, y).ToArgb() == checkedBitmap.GetPixel(x, y).ToArgb()) continue;
                Assert.True(content.Contains(x - cellBounds.X, y - cellBounds.Y),
                    $"Check mark changed pixel ({x}, {y}) outside local bounds {content}.");
                changedPixels++;
            }
        }

        Assert.True(changedPixels > 0);
        Assert.Equal(new Size(17, 17), cell.PreferredSize);
    }

    [Theory]
    [InlineData(DataGridViewImageCellLayout.Normal, 10, 6)]
    [InlineData(DataGridViewImageCellLayout.Zoom, 36, 22)]
    [InlineData(DataGridViewImageCellLayout.Stretch, 100, 22)]
    public void Image_content_bounds_match_the_painted_image(DataGridViewImageCellLayout layout, int width, int height)
    {
        using var image = new Bitmap(10, 6, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Red);

        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewImageColumn());
        grid.Rows.Add((object)image);
        var cell = (DataGridViewImageCell)grid.Rows[0].Cells[0];
        cell.ImageLayout = layout;

        var content = cell.ContentBounds;
        Assert.Equal(new Size(width, height), content.Size);
        Assert.Equal(new Size(14, 10), cell.PreferredSize);

        using var painted = Draw(grid);
        var display = grid.GetCellDisplayRectangle(0, 0, false);
        Assert.Equal(Color.Red.ToArgb(), painted.GetPixel(display.X + content.X + content.Width / 2,
            display.Y + content.Y + content.Height / 2).ToArgb());
    }

    [AvaloniaFact]
    public void Clicking_outside_the_painted_check_box_glyph_does_not_toggle_its_value()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewCheckBoxColumn());
        grid.Rows.Add(false);
        using var form = new Form { ClientSize = new Size(220, 120) };
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        var cell = grid.Rows[0].Cells[0];
        var bounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        var outsideGlyph = new Point(bounds.X + 3, bounds.Y + 3);
        var point = new Avalonia.Point(grid.Left + outsideGlyph.X, grid.Top + outsideGlyph.Y);
        var cellClicks = 0;
        var contentClicks = 0;
        grid.CellClick += (_, _) => cellClicks++;
        grid.CellContentClick += (_, _) => contentClicks++;

        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.Equal(1, cellClicks);
        Assert.Equal(0, contentClicks);
        Assert.Equal(false, cell.Value);
        Assert.False(cell.ContentBounds.Contains(3, 3));
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }
}
