using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>DataGridView: headers, rows and gridlines, with no row or cell selected.</summary>
public static partial class VisualScenarios
{
    private static readonly Size GridSize = new(180, 90);

    private static IReadOnlyList<Scenario> GridScenarios() =>
    [
        Create<System.Windows.Forms.DataGridView>("datagridview-headers-and-rows", GridSize, grid =>
        {
            AsEditControl(grid);
            grid.Columns.Add("Name", "Name");
            grid.Columns.Add("Size", "Size");
            grid.Rows.Add("File.txt", "1 KB");
            grid.Rows.Add("Other.txt", "2 KB");
        }, FullArea(GridSize)),
        Create<System.Windows.Forms.DataGridView>("datagridview-no-gridlines", GridSize, grid =>
        {
            AsEditControl(grid);
            grid.GridColor = grid.BackgroundColor;
            grid.Columns.Add("Name", "Name");
            grid.Rows.Add("File.txt");
            grid.Rows.Add("Other.txt");
        }, FullArea(GridSize)),
    ];
}
