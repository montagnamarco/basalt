using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Layout;

/// <summary>
/// <see cref="TableLayoutPanel"/>'s grid: column and row sizing, cell
/// assignment and growth, children placed inside their cell.
/// </summary>
public class TableLayoutTests
{
    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void Absolute_columns_take_their_width_and_percent_columns_split_what_is_left()
    {
        var panel = new TableLayoutPanel { ColumnCount = 3, Size = new Size(300, 100) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        panel.Controls.Add(new Control(), 0, 0);
        panel.Controls.Add(new Control(), 1, 0);
        panel.Controls.Add(new Control(), 2, 0);

        var widths = panel.GetColumnWidths();

        Assert.Equal([50, 62, 188], widths);
    }

    [Fact]
    public void An_autosize_column_is_sized_to_its_single_span_content_and_its_margin()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, Size = new Size(300, 100) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var content = new Control { Size = new Size(40, 20) };
        panel.Controls.Add(content, 0, 0);

        var widths = panel.GetColumnWidths();

        Assert.Equal(46, widths[0]); // 40 plus the control's default 3-pixel margin on each side.
        Assert.Equal(254, widths[1]); // What is left of 300, since no other column asked for a percentage.
    }

    [Fact]
    public void A_column_span_merges_the_spanned_columns_and_the_border_between_them()
    {
        var panel = new TableLayoutPanel { ColumnCount = 3, Size = new Size(300, 60), CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        var spanning = new Control { Dock = DockStyle.Fill, Margin = Padding.Empty };
        panel.Controls.Add(spanning, 0, 0);
        panel.SetColumnSpan(spanning, 2);

        // Two 50-pixel columns plus the 1-pixel border that would otherwise separate them.
        Assert.Equal(101, spanning.Width);
        Assert.Equal(2, panel.GetColumnSpan(spanning));
    }

    [Fact]
    public void Unassigned_controls_are_placed_in_the_next_free_cell_in_the_order_they_were_added()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, Size = new Size(200, 100) };
        var a = new Control();
        var b = new Control();
        var c = new Control();
        panel.Controls.Add(a);
        panel.Controls.Add(b);
        panel.Controls.Add(c);

        Assert.Equal(new TableLayoutPanelCellPosition(0, 0), panel.GetPositionFromControl(a));
        Assert.Equal(new TableLayoutPanelCellPosition(1, 0), panel.GetPositionFromControl(b));
        Assert.Equal(new TableLayoutPanelCellPosition(0, 1), panel.GetPositionFromControl(c));
        Assert.Same(a, panel.GetControlFromPosition(0, 0));
        Assert.Same(c, panel.GetControlFromPosition(0, 1));
    }

    [Fact]
    public void Automatic_placement_skips_a_cell_an_explicit_control_already_occupies()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, Size = new Size(200, 100) };
        var fixedControl = new Control();
        var automatic = new Control();
        panel.Controls.Add(fixedControl, 0, 0);
        panel.Controls.Add(automatic);

        Assert.Equal(new TableLayoutPanelCellPosition(1, 0), panel.GetPositionFromControl(automatic));
    }

    [Fact]
    public void GrowStyle_AddRows_grows_the_grid_downward_as_controls_are_added()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, GrowStyle = TableLayoutPanelGrowStyle.AddRows, Size = new Size(200, 100) };

        for (var i = 0; i < 5; i++) panel.Controls.Add(new Control());

        var last = panel.Controls[4];
        Assert.Equal(new TableLayoutPanelCellPosition(0, 2), panel.GetPositionFromControl(last));
    }

    [Fact]
    public void GrowStyle_FixedSize_throws_once_every_cell_is_taken()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 1, GrowStyle = TableLayoutPanelGrowStyle.FixedSize, Size = new Size(100, 50) };
        panel.Controls.Add(new Control());

        Assert.Throws<ArgumentException>(() => panel.Controls.Add(new Control()));
    }

    [Fact]
    public void A_filled_child_takes_the_whole_cell_minus_its_margin()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 1, Size = new Size(200, 100) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var filled = new Control { Dock = DockStyle.Fill, Margin = new Padding(5) };

        panel.Controls.Add(filled, 0, 0);

        Assert.Equal(new Rectangle(5, 5, 190, 90), filled.Bounds);
    }

    [Fact]
    public void A_child_anchored_to_no_side_is_centred_in_its_cell()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 1, Size = new Size(200, 100) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var centred = new Control { Size = new Size(40, 20), Anchor = AnchorStyles.None };

        panel.Controls.Add(centred, 0, 0);

        // The cell is the whole 200x100 client area; the control's own 3-pixel margin shrinks it before centring.
        Assert.Equal(new Rectangle(80, 40, 40, 20), centred.Bounds);
    }

    [Fact]
    public void A_default_anchored_child_sits_at_the_top_left_of_its_cell()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 1, Size = new Size(200, 100) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var topLeft = new Control { Size = new Size(40, 20), Margin = Padding.Empty };

        panel.Controls.Add(topLeft, 0, 0);

        Assert.Equal(new Rectangle(0, 0, 40, 20), topLeft.Bounds);
    }

    [Fact]
    public void An_autosize_panel_shrinks_to_its_absolute_and_autosize_columns_and_rows()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var content = new Control { Size = new Size(60, 25) };
        panel.Controls.Add(content, 1, 0);

        var preferred = panel.GetPreferredSize(Size.Empty);

        // Column 0 is the fixed 40; column 1 is the content's 60 plus its 3-pixel margin on each side; the row follows the same rule.
        Assert.Equal(new Size(106, 31), preferred);
    }

    [Fact]
    public void CellBorderStyle_Single_draws_a_line_between_cells()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Size = new Size(100, 40),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = Color.White,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var widths = panel.GetColumnWidths();
        var borderX = 1 + widths[0]; // 1 pixel for the outer border, then the first column's width.

        using var bitmap = Draw(panel);

        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(2, 20).ToArgb());
        Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(borderX, 20).ToArgb());
    }

    [Fact]
    public void SetColumnSpan_and_SetRowSpan_reject_a_span_smaller_than_one()
    {
        var panel = new TableLayoutPanel();
        var control = new Control();
        panel.Controls.Add(control);

        Assert.Throws<ArgumentOutOfRangeException>(() => panel.SetColumnSpan(control, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.SetRowSpan(control, 0));
        Assert.Equal(1, panel.GetColumnSpan(control));
        Assert.Equal(1, panel.GetRowSpan(control));
    }

    [Fact]
    public void SetCellPosition_moves_a_control_to_the_given_cell()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, Size = new Size(200, 100) };
        var control = new Control();
        panel.Controls.Add(control, 0, 0);

        panel.SetCellPosition(control, new TableLayoutPanelCellPosition(1, 1));

        Assert.Equal(new TableLayoutPanelCellPosition(1, 1), panel.GetCellPosition(control));
    }

    [Fact]
    public void GetControlFromPosition_outside_the_grid_returns_null()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 1, Size = new Size(50, 50) };
        panel.Controls.Add(new Control());

        Assert.Null(panel.GetControlFromPosition(5, 5));
    }
}
