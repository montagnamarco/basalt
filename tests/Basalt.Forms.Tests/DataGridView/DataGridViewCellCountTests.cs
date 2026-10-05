using System.ComponentModel;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewCellCountTests
{
    [Fact]
    public void Counts_cells_matching_all_inherited_state_flags()
    {
        using var grid = new System.Windows.Forms.DataGridView { AllowUserToAddRows = false };
        grid.Columns.Add("a", "A");
        grid.Columns.Add("b", "B");
        grid.Rows.Add("one", "two");
        grid.Rows.Add("three", "four");
        grid.Rows[0].Cells[0].Selected = true;
        grid.Columns[0].ReadOnly = true;

        Assert.Equal(4, grid.GetCellCount(DataGridViewElementStates.None));
        Assert.Equal(2, grid.GetCellCount(DataGridViewElementStates.ReadOnly));
        Assert.Equal(1, grid.GetCellCount(DataGridViewElementStates.Selected | DataGridViewElementStates.ReadOnly));
        Assert.Throws<InvalidEnumArgumentException>(() => grid.GetCellCount((DataGridViewElementStates)128));
    }
}
