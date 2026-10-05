namespace System.Windows.Forms;

/// <summary>
/// The ComboBox a DataGridViewComboBoxCell edits with: DropDownList style (the user picks one of
/// the listed items, never types a value of their own), one instance pooled and reused for every
/// combo cell exactly as DataGridViewTextBoxEditingControl is for text cells. The cell fills Items
/// (or DataSource/DisplayMember/ValueMember) right before PrepareEditingControlForEdit runs.
/// </summary>
public class DataGridViewComboBoxEditingControl : ComboBox, IDataGridViewEditingControl
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    public DataGridViewComboBoxEditingControl()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
    }

    public virtual DataGridView? EditingControlDataGridView { get; set; }

    public virtual object EditingControlFormattedValue
    {
        get => Text;
        set => Text = value as string ?? value?.ToString() ?? string.Empty;
    }

    public virtual int EditingControlRowIndex { get; set; }

    public virtual bool EditingControlValueChanged { get; set; }

    public virtual Cursor EditingPanelCursor => Cursors.Default;

    public virtual bool RepositionEditingControlOnValueChange => false;

    public virtual void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewCellStyle);

        Font = dataGridViewCellStyle.Font ?? Font;
        BackColor = dataGridViewCellStyle.BackColor;
        ForeColor = dataGridViewCellStyle.ForeColor;
    }

    /// <summary>Up/Down/Home/End/PageUp/PageDown already change which item is selected (ComboBox.OnKeyDown claims them before the grid ever sees them unhandled), so the combo keeps wanting them regardless of what the grid would otherwise do with them.</summary>
    public virtual bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey)
    {
        return (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown => true,
            _ => !dataGridViewWantsInputKey,
        };
    }

    public virtual object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) => Text;

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);

        EditingControlValueChanged = true;
        EditingControlDataGridView?.NotifyCurrentCellDirty(true);
    }

    public virtual void PrepareEditingControlForEdit(bool selectAll)
    {
        EditingControlValueChanged = false;
    }
}
