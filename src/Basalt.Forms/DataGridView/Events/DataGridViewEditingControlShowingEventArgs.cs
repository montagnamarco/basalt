namespace System.Windows.Forms;

/// <summary>Carries the control the grid is about to show over the current cell, and the style it is about to apply to it, to an EditingControlShowing handler.</summary>
public class DataGridViewEditingControlShowingEventArgs : EventArgs
{
    public DataGridViewEditingControlShowingEventArgs(Control control, DataGridViewCellStyle cellStyle)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(cellStyle);

        Control = control;
        CellStyle = cellStyle;
    }

    /// <summary>A handler may replace members of this style; the grid applies it to Control right after raising the event.</summary>
    public DataGridViewCellStyle CellStyle { get; set; }

    public Control Control { get; }
}
