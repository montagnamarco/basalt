using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>How a cell's InheritedStyle layers the grid's, the column's, the row's and the cell's own style.</summary>
public class DataGridViewStyleTests
{
    [Fact]
    public void A_cell_without_any_style_of_its_own_inherits_the_grid_s_DefaultCellStyle()
    {
        var grid = new System.Windows.Forms.DataGridView { DefaultCellStyle = { BackColor = Color.Yellow } };
        grid.Columns.Add("A", "A");
        grid.Rows.Add();

        Assert.Equal(Color.Yellow, grid.Rows[0].Cells[0].InheritedStyle.BackColor);
    }

    [Fact]
    public void A_column_s_DefaultCellStyle_wins_over_the_grid_s_for_its_own_cells()
    {
        var grid = new System.Windows.Forms.DataGridView { DefaultCellStyle = { BackColor = Color.Yellow } };
        grid.Columns.Add("A", "A");
        grid.Columns[0].DefaultCellStyle.BackColor = Color.Blue;
        grid.Rows.Add();

        Assert.Equal(Color.Blue, grid.Rows[0].Cells[0].InheritedStyle.BackColor);
    }

    [Fact]
    public void A_cell_s_own_Style_wins_over_everything_else()
    {
        var grid = new System.Windows.Forms.DataGridView { DefaultCellStyle = { BackColor = Color.Yellow } };
        grid.Columns.Add("A", "A");
        grid.Columns[0].DefaultCellStyle.BackColor = Color.Blue;
        grid.Rows.Add();
        grid.Rows[0].Cells[0].Style.BackColor = Color.Red;

        Assert.Equal(Color.Red, grid.Rows[0].Cells[0].InheritedStyle.BackColor);
    }

    [Fact]
    public void A_row_s_DefaultCellStyle_wins_over_the_column_s_for_that_row_s_cells()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns[0].DefaultCellStyle.BackColor = Color.Blue;
        grid.Rows.Add();
        grid.Rows[0].DefaultCellStyle.BackColor = Color.Green;

        Assert.Equal(Color.Green, grid.Rows[0].Cells[0].InheritedStyle.BackColor);
    }

    [Fact]
    public void ColumnHeadersDefaultCellStyle_reaches_a_column_header_cell_but_not_a_data_cell()
    {
        var grid = new System.Windows.Forms.DataGridView { ColumnHeadersDefaultCellStyle = { ForeColor = Color.Purple } };
        grid.Columns.Add("A", "A");
        grid.Rows.Add();

        Assert.Equal(Color.Purple, grid.Columns[0].HeaderCell.InheritedStyle.ForeColor);
        Assert.NotEqual(Color.Purple, grid.Rows[0].Cells[0].InheritedStyle.ForeColor);
    }

    [Fact]
    public void RowHeadersDefaultCellStyle_reaches_a_row_header_cell()
    {
        var grid = new System.Windows.Forms.DataGridView { RowHeadersDefaultCellStyle = { BackColor = Color.Orange } };
        grid.Columns.Add("A", "A");
        grid.Rows.Add();

        Assert.Equal(Color.Orange, grid.Rows[0].HeaderCell.InheritedStyle.BackColor);
    }

    [Fact]
    public void AlternatingRowsDefaultCellStyle_reaches_only_odd_rows_and_wins_over_RowsDefaultCellStyle()
    {
        var grid = new System.Windows.Forms.DataGridView
        {
            RowsDefaultCellStyle = { BackColor = Color.Red },
            AlternatingRowsDefaultCellStyle = { BackColor = Color.Blue },
        };
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.Rows.Add();
        grid.Rows.Add();

        Assert.Equal(Color.Red, grid.Rows[0].Cells[0].InheritedStyle.BackColor);
        Assert.Equal(Color.Blue, grid.Rows[1].Cells[0].InheritedStyle.BackColor);
        Assert.Equal(Color.Red, grid.Rows[2].Cells[0].InheritedStyle.BackColor);
    }

    [Fact]
    public void RowsDefaultCellStyle_wins_over_the_column_s_DefaultCellStyle()
    {
        var grid = new System.Windows.Forms.DataGridView { RowsDefaultCellStyle = { BackColor = Color.Red } };
        grid.Columns.Add("A", "A");
        grid.Columns[0].DefaultCellStyle.BackColor = Color.Blue;
        grid.Rows.Add();

        // Order: grid.DefaultCellStyle < Column.DefaultCellStyle < grid.RowsDefaultCellStyle < ...
        // so RowsDefaultCellStyle, applied after the column's, must win.
        Assert.Equal(Color.Red, grid.Rows[0].Cells[0].InheritedStyle.BackColor);
    }

    [Fact]
    public void Changing_DefaultCellStyle_raises_DefaultCellStyleChanged()
    {
        var grid = new System.Windows.Forms.DataGridView();
        var raised = 0;
        grid.DefaultCellStyleChanged += (_, _) => raised++;

        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.Pink };

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ApplyStyle_only_copies_values_the_source_style_actually_set()
    {
        var target = new DataGridViewCellStyle { BackColor = Color.Blue, ForeColor = Color.White };
        var source = new DataGridViewCellStyle { BackColor = Color.Red };

        target.ApplyStyle(source);

        Assert.Equal(Color.Red, target.BackColor);
        // ForeColor was never set on source, so the target's own value survives.
        Assert.Equal(Color.White, target.ForeColor);
    }

    [Fact]
    public void Clone_produces_an_equal_but_distinct_style()
    {
        var original = new DataGridViewCellStyle { BackColor = Color.Blue };

        var clone = original.Clone();
        clone.BackColor = Color.Red;

        Assert.Equal(Color.Blue, original.BackColor);
        Assert.Equal(Color.Red, clone.BackColor);
    }
}
