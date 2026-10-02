using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>AutoResizeColumn(s)/Row(s)/ColumnHeadersHeight/RowHeadersWidth, AutoSizeRowsMode/AutoSizeColumnsMode and AllowUserToOrderColumns.</summary>
public class DataGridViewAutoSizeTests
{
    private static System.Windows.Forms.DataGridView TwoByTwoGrid()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(260, 160) };
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Rows.Add((object)1, (object)2);
        grid.Rows.Add((object)3, (object)4);

        return grid;
    }

    [Fact]
    public void AutoResizeColumn_widens_the_column_to_its_widest_cell()
    {
        var grid = TwoByTwoGrid();
        grid.Columns[0].Width = 30;
        grid.Rows[0].Cells[0].Value = "A much wider value than thirty pixels";

        grid.AutoResizeColumn(0);

        Assert.True(grid.Columns[0].Width > 30);
    }

    [Fact]
    public void AutoResizeColumn_with_ColumnHeader_mode_ignores_cell_content()
    {
        var grid = TwoByTwoGrid();
        grid.Columns[0].Width = 300;
        grid.Rows[0].Cells[0].Value = "A much wider value than three hundred pixels could ever need to be";

        grid.AutoResizeColumn(0, DataGridViewAutoSizeColumnMode.ColumnHeader);

        // Only "A" (the header) was measured, so the column must have shrunk back down, not grown.
        Assert.True(grid.Columns[0].Width < 300);
    }

    [Fact]
    public void AutoResizeColumns_widens_every_column()
    {
        var grid = TwoByTwoGrid();
        grid.Columns[0].Width = 30;
        grid.Columns[1].Width = 30;
        grid.Rows[0].Cells[0].Value = "A much wider value than thirty pixels";
        grid.Rows[0].Cells[1].Value = "Another much wider value than thirty pixels";

        grid.AutoResizeColumns();

        Assert.True(grid.Columns[0].Width > 30);
        Assert.True(grid.Columns[1].Width > 30);
    }

    [Fact]
    public void AutoResizeRow_heightens_the_row_to_its_tallest_cell()
    {
        var grid = TwoByTwoGrid();
        grid.Columns[0].Width = 20;
        grid.Rows[0].Height = 10;
        grid.Rows[0].Cells[0].Value = "A value that must wrap across several lines in a narrow column";

        grid.AutoResizeRow(0);

        Assert.True(grid.Rows[0].Height > 10);
    }

    [Fact]
    public void AutoResizeRows_heightens_every_row()
    {
        var grid = TwoByTwoGrid();
        grid.Columns[0].Width = 20;
        grid.Rows[0].Height = 10;
        grid.Rows[1].Height = 10;
        grid.Rows[0].Cells[0].Value = "A value that must wrap across several lines in a narrow column";
        grid.Rows[1].Cells[0].Value = "Another value that must wrap across several lines too";

        grid.AutoResizeRows();

        Assert.True(grid.Rows[0].Height > 10);
        Assert.True(grid.Rows[1].Height > 10);
    }

    [Fact]
    public void AutoResizeColumnHeadersHeight_grows_to_fit_a_header_that_wraps()
    {
        var grid = TwoByTwoGrid();
        grid.Columns[0].Width = 20;
        grid.Columns[0].HeaderText = "A header long enough to need several lines when wrapped this narrow";
        var before = grid.ColumnHeadersHeight;

        grid.AutoResizeColumnHeadersHeight();

        Assert.True(grid.ColumnHeadersHeight > before);
    }

    [Fact]
    public void AutoSizeRowsMode_raises_AutoSizeRowsModeChanged_only_when_it_actually_changes()
    {
        var grid = TwoByTwoGrid();
        var raised = 0;
        grid.AutoSizeRowsModeChanged += (_, _) => raised++;

        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        Assert.Equal(1, raised);
    }

    [Fact]
    public void AutoSizeColumnsMode_raises_AutoSizeColumnsModeChanged_with_each_column_s_previous_mode()
    {
        var grid = TwoByTwoGrid();
        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        DataGridViewAutoSizeColumnMode[]? previous = null;
        grid.AutoSizeColumnsModeChanged += (_, e) => previous = e.PreviousModes;

        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;

        Assert.Equal([DataGridViewAutoSizeColumnMode.Fill, DataGridViewAutoSizeColumnMode.NotSet], previous);
    }

    [Fact]
    public void AllowUserToOrderColumns_raises_its_Changed_event_only_when_it_actually_changes()
    {
        var grid = TwoByTwoGrid();
        var raised = 0;
        grid.AllowUserToOrderColumnsChanged += (_, _) => raised++;

        grid.AllowUserToOrderColumns = true;
        grid.AllowUserToOrderColumns = true;

        Assert.True(grid.AllowUserToOrderColumns);
        Assert.Equal(1, raised);
    }
}
