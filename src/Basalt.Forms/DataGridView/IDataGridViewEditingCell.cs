namespace System.Windows.Forms;

/// <summary>
/// A cell that edits itself in place rather than through a separate IDataGridViewEditingControl
/// (DataGridViewCheckBoxCell is the one shipped this round): it tracks its own edited value and
/// commits straight from PrepareEditingCellForEdit/GetEditingCellFormattedValue.
/// </summary>
public interface IDataGridViewEditingCell
{
    object? EditingCellFormattedValue { get; set; }

    bool EditingCellValueChanged { get; set; }

    object GetEditingCellFormattedValue(DataGridViewDataErrorContexts context);

    void PrepareEditingCellForEdit(bool selectAll);
}
