namespace System.Windows.Forms;

/// <summary>
/// What a control placed over the current cell must offer the grid: a way back to the grid and
/// the row it edits, the formatted value it currently holds, whether that value has changed since
/// editing began, and the few operations the grid calls at the right moment (style, selection,
/// which keys the control itself wants to keep). DataGridViewTextBoxEditingControl and
/// DataGridViewComboBoxEditingControl are the two controls that implement it this round.
/// </summary>
public interface IDataGridViewEditingControl
{
    /// <summary>The grid currently showing this control as its editor; null while the control sits unused in the grid's pool.</summary>
    DataGridView? EditingControlDataGridView { get; set; }

    /// <summary>What GetEditingControlFormattedValue would return right now; settable so the grid can push a value into the control when editing begins.</summary>
    object EditingControlFormattedValue { get; set; }

    /// <summary>The row this control is currently editing a cell of.</summary>
    int EditingControlRowIndex { get; set; }

    /// <summary>Whether the control's value has changed since PrepareEditingControlForEdit was last called.</summary>
    bool EditingControlValueChanged { get; set; }

    /// <summary>The cursor to show over the editing panel surrounding this control.</summary>
    Cursor EditingPanelCursor { get; }

    /// <summary>Whether a value change while editing should also reposition the control (true for a control whose preferred size depends on its content).</summary>
    bool RepositionEditingControlOnValueChange { get; }

    /// <summary>Copies the cell style's font and colors onto the control.</summary>
    void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle);

    /// <summary>Whether the control itself wants keyData rather than letting the grid treat it as cell navigation.</summary>
    bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey);

    /// <summary>The value the control currently holds, formatted for the cell's FormattedValueType.</summary>
    object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context);

    /// <summary>Called right after the grid makes the control visible over a cell: resets EditingControlValueChanged and optionally selects all of the control's content.</summary>
    void PrepareEditingControlForEdit(bool selectAll);
}
