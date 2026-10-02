using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>GetColumnDisplayRectangle/GetRowDisplayRectangle and DisplayedRowCount/DisplayedColumnCount.</summary>
public class DataGridViewLayoutQueriesTests
{
    private static System.Windows.Forms.DataGridView ThreeByThreeGrid()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(140, 90) };
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Columns.Add("C", "C");
        grid.Columns[0].Width = 50;
        grid.Columns[1].Width = 50;
        grid.Columns[2].Width = 50;
        grid.Rows.Add();
        grid.Rows.Add();
        grid.Rows.Add();

        return grid;
    }

    [Fact]
    public void GetColumnDisplayRectangle_matches_the_column_s_own_cell_display_rectangle_at_row_minus_one()
    {
        var grid = ThreeByThreeGrid();

        var expected = grid.GetCellDisplayRectangle(1, -1, cutOverflow: false);
        var actual = grid.GetColumnDisplayRectangle(1, cutOverflow: false);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetRowDisplayRectangle_matches_the_row_s_own_cell_display_rectangle_at_column_minus_one()
    {
        var grid = ThreeByThreeGrid();

        var expected = grid.GetCellDisplayRectangle(-1, 2, cutOverflow: false);
        var actual = grid.GetRowDisplayRectangle(2, cutOverflow: false);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DisplayedColumnCount_counts_only_whole_columns_unless_told_to_include_the_partial_one()
    {
        var grid = ThreeByThreeGrid();

        // Row headers (41px) + 3*50px = 191px needed, but the grid is only 140px wide: two whole
        // columns (41 + 100 = 141... actually 41+50+50=141 > 140) - just one whole column fits.
        Assert.Equal(1, grid.DisplayedColumnCount(false));
        Assert.Equal(2, grid.DisplayedColumnCount(true));
    }

    [Fact]
    public void DisplayedRowCount_counts_only_whole_rows_unless_told_to_include_the_partial_one()
    {
        var grid = ThreeByThreeGrid();

        // Column headers (23px) + 3*22px = 89px needed; the grid is 90px tall, so all three whole
        // rows already fit and there is no partial row left over.
        Assert.Equal(3, grid.DisplayedRowCount(false));
        Assert.Equal(3, grid.DisplayedRowCount(true));
    }

    [Fact]
    public void DisplayedRowCount_counts_a_partial_row_only_when_asked_to()
    {
        var grid = ThreeByThreeGrid();
        grid.Height = 60;

        // Column headers (23px) leave 37px: one whole 22px row fits, and 15px of a second.
        Assert.Equal(1, grid.DisplayedRowCount(false));
        Assert.Equal(2, grid.DisplayedRowCount(true));
    }
}
