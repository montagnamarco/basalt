using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewPaintingConstructorTests
{
    [Fact]
    public void Public_constructor_forwards_its_error_text_to_custom_cell_painting()
    {
        using var grid = new System.Windows.Forms.DataGridView { AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewColumn(new ObservingCell()));
        grid.Rows.Add();
        using var bitmap = new Bitmap(20, 20);
        using var graphics = Graphics.FromImage(bitmap);
        var bounds = new Rectangle(0, 0, 20, 20);
        var args = new DataGridViewCellPaintingEventArgs(grid, graphics, bounds, bounds, 0, 0,
            DataGridViewElementStates.Visible, null, null, "provided error", new DataGridViewCellStyle(),
            new DataGridViewAdvancedBorderStyle(), DataGridViewPaintParts.ErrorIcon);
        args.Paint(bounds, DataGridViewPaintParts.ErrorIcon);
        Assert.Equal("provided error", Assert.IsType<ObservingCell>(grid.Rows[0].Cells[0]).PaintedError);
    }

    [Fact]
    public void Public_constructor_resolves_the_cell_and_paints_only_inside_the_clip()
    {
        using var grid = new System.Windows.Forms.DataGridView { AllowUserToAddRows = false, ColumnCount = 1 };
        grid.Rows.Add("value");
        using var bitmap = new Bitmap(30, 30);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        var bounds = new Rectangle(2, 2, 20, 20);
        var clip = new Rectangle(8, 8, 6, 6);
        var style = new DataGridViewCellStyle { BackColor = Color.Red };
        var args = new DataGridViewCellPaintingEventArgs(grid, graphics, clip, bounds, 0, 0,
            DataGridViewElementStates.Visible, "value", "value", null, style,
            new DataGridViewAdvancedBorderStyle(), DataGridViewPaintParts.Background);
        args.Paint(clip, DataGridViewPaintParts.Background);
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(4, 4).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(25, 25).ToArgb());
    }

    private sealed class ObservingCell : DataGridViewTextBoxCell
    {
        public ObservingCell() { }
        internal string? PaintedError { get; private set; }
        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds,
            int rowIndex, DataGridViewElementStates cellState, object? value, object? formattedValue,
            string? errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle,
            DataGridViewPaintParts paintParts) => PaintedError = errorText;
    }
}
