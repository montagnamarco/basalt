using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewElementEventCompatibilityTests
{
    [Fact]
    public void Cell_raised_data_error_and_mouse_wheel_reach_owning_grid()
    {
        using var grid = new System.Windows.Forms.DataGridView { AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewColumn(new EventCell()));
        grid.Rows.Add("value");
        var cell = Assert.IsType<EventCell>(grid.Rows[0].Cells[0]);
        DataGridViewDataErrorEventArgs? observedError = null;
        MouseEventArgs? observedWheel = null;
        grid.DataError += (_, e) => observedError = e;
        grid.MouseWheel += (_, e) => observedWheel = e;
        var error = new DataGridViewDataErrorEventArgs(new FormatException("bad value"), 0, 0,
            DataGridViewDataErrorContexts.Formatting);
        var wheel = new MouseEventArgs(MouseButtons.None, 0, 4, 5, 120);

        cell.ReportError(error);
        cell.ReportWheel(wheel);

        Assert.Same(error, observedError);
        Assert.Same(wheel, observedWheel);
    }

    private sealed class EventCell : DataGridViewTextBoxCell
    {
        public void ReportError(DataGridViewDataErrorEventArgs e) => RaiseDataError(e);

        public void ReportWheel(MouseEventArgs e) => RaiseMouseWheel(e);
    }
}
