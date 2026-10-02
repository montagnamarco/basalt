using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>CellMouseClick/DoubleClick/Enter/Leave/Move, the column/row header equivalents, and the divider double-click auto-fit.</summary>
public class DataGridViewMouseEventsTests
{
    private static Form ShowWith(System.Windows.Forms.DataGridView grid)
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        grid.Focus();

        return form;
    }

    private static System.Windows.Forms.DataGridView TwoByTwoGrid()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(260, 160) };
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Rows.Add((object)1, (object)2);
        grid.Rows.Add((object)3, (object)4);

        return grid;
    }

    private static void Click(Form form, Control control, Point clientPoint, int clicks = 1)
    {
        var point = new Avalonia.Point(control.Left + clientPoint.X, control.Top + clientPoint.Y);
        for (var i = 0; i < clicks; i++)
        {
            form.Host!.MouseDown(point, MouseButton.Left);
            form.Host!.MouseUp(point, MouseButton.Left);
        }
    }

    private static void MoveTo(Form form, Control control, Point clientPoint)
    {
        var point = new Avalonia.Point(control.Left + clientPoint.X, control.Top + clientPoint.Y);
        form.Host!.MouseMove(point);
    }

    [AvaloniaFact]
    public void Clicking_a_cell_raises_CellMouseClick_with_its_column_and_row()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        var raised = new List<(int Column, int Row)>();
        grid.CellMouseClick += (_, e) => raised.Add((e.ColumnIndex, e.RowIndex));

        var bounds = grid.GetCellDisplayRectangle(1, 0, cutOverflow: false);
        Click(form, grid, new Point(bounds.X + 3, bounds.Y + 3));

        Assert.Equal([(1, 0)], raised);

        form.Close();
    }

    [AvaloniaFact]
    public void Double_clicking_a_cell_raises_CellMouseDoubleClick_after_CellMouseClick()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        var order = new List<string>();
        grid.CellMouseClick += (_, _) => order.Add("click");
        grid.CellMouseDoubleClick += (_, _) => order.Add("doubleClick");

        var bounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        Click(form, grid, new Point(bounds.X + 3, bounds.Y + 3), clicks: 2);

        Assert.Equal(["click", "click", "doubleClick"], order);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_column_header_raises_ColumnHeaderMouseClick_not_CellMouseClick()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        var headerRaised = new List<int>();
        var cellRaised = new List<int>();
        grid.ColumnHeaderMouseClick += (_, e) => headerRaised.Add(e.ColumnIndex);
        grid.CellMouseClick += (_, e) => cellRaised.Add(e.ColumnIndex);

        var bounds = grid.GetCellDisplayRectangle(1, -1, cutOverflow: false);
        Click(form, grid, new Point(bounds.X + 3, bounds.Y + 3));

        Assert.Equal([1], headerRaised);
        Assert.Empty(cellRaised);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_row_header_raises_RowHeaderMouseClick()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        var raised = new List<int>();
        grid.RowHeaderMouseClick += (_, e) => raised.Add(e.RowIndex);

        var bounds = grid.GetCellDisplayRectangle(-1, 1, cutOverflow: false);
        Click(form, grid, new Point(bounds.X + 3, bounds.Y + 3));

        Assert.Equal([1], raised);

        form.Close();
    }

    [AvaloniaFact]
    public void Moving_the_mouse_between_two_cells_raises_Leave_for_the_first_then_Enter_for_the_second()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        var order = new List<string>();
        grid.CellMouseEnter += (_, e) => order.Add($"enter {e.ColumnIndex},{e.RowIndex}");
        grid.CellMouseLeave += (_, e) => order.Add($"leave {e.ColumnIndex},{e.RowIndex}");

        var first = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        MoveTo(form, grid, new Point(first.X + 3, first.Y + 3));

        var second = grid.GetCellDisplayRectangle(1, 1, cutOverflow: false);
        MoveTo(form, grid, new Point(second.X + 3, second.Y + 3));

        Assert.Equal(["enter 0,0", "leave 0,0", "enter 1,1"], order);

        form.Close();
    }

    [AvaloniaFact]
    public void Moving_the_mouse_within_a_cell_raises_CellMouseMove_with_its_coordinates()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        var raised = new List<(int Column, int Row)>();
        grid.CellMouseMove += (_, e) => raised.Add((e.ColumnIndex, e.RowIndex));

        var bounds = grid.GetCellDisplayRectangle(0, 1, cutOverflow: false);
        MoveTo(form, grid, new Point(bounds.X + 3, bounds.Y + 3));

        Assert.Contains((0, 1), raised);

        form.Close();
    }

    [AvaloniaFact]
    public void Double_clicking_a_column_header_divider_auto_fits_the_column_to_its_widest_cell()
    {
        var grid = TwoByTwoGrid();
        grid.Columns[0].Width = 30;
        grid.Rows[0].Cells[0].Value = "A much wider value than thirty pixels";
        var form = ShowWith(grid);

        var dividerX = grid.GetCellDisplayRectangle(0, -1, cutOverflow: false).Right;
        Click(form, grid, new Point(dividerX, 5), clicks: 2);

        Assert.True(grid.Columns[0].Width > 30);

        form.Close();
    }

    [AvaloniaFact]
    public void A_handler_can_mark_ColumnDividerDoubleClick_Handled_to_suppress_the_default_auto_fit()
    {
        var grid = TwoByTwoGrid();
        grid.Columns[0].Width = 30;
        grid.Rows[0].Cells[0].Value = "A much wider value than thirty pixels";
        grid.ColumnDividerDoubleClick += (_, e) => e.Handled = true;
        var form = ShowWith(grid);

        var dividerX = grid.GetCellDisplayRectangle(0, -1, cutOverflow: false).Right;
        Click(form, grid, new Point(dividerX, 5), clicks: 2);

        Assert.Equal(30, grid.Columns[0].Width);

        form.Close();
    }
}
