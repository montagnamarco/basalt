using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewDimensionTests
{
    [Fact]
    public void ColumnCount_grows_cells_and_removes_trailing_columns_preserving_existing_values()
    {
        using var grid = new System.Windows.Forms.DataGridView { AllowUserToAddRows = false, ColumnCount = 2 };
        grid.Rows.Add("retained", "removed");
        var original = grid.Columns[0];
        grid.ColumnCount = 3;
        Assert.Equal(3, grid.Rows[0].Cells.Count);
        Assert.IsType<DataGridViewTextBoxColumn>(grid.Columns[2]);
        grid.ColumnCount = 1;
        Assert.Same(original, grid.Columns[0]);
        Assert.Single(grid.Rows[0].Cells.Cast<DataGridViewCell>());
        Assert.Equal("retained", grid.Rows[0].Cells[0].Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.ColumnCount = -1);
    }

    [Fact]
    public void ColumnCount_rejects_changes_to_a_bound_grid()
    {
        using var grid = new System.Windows.Forms.DataGridView { DataSource = new[] { "value" } };
        Assert.Throws<InvalidOperationException>(() => grid.ColumnCount = 2);
    }
}
