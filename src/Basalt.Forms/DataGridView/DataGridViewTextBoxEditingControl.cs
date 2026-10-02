using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// The TextBox a DataGridViewTextBoxCell edits with: one instance, created the first time any
/// text cell is edited and reused for every one after that, exactly as the grid pools every other
/// editing control by type. ApplyCellStyleToEditingControl copies the cell's font and colors onto
/// it; ProcessKeyEventArgs lets Up/Down/PageUp/PageDown escape to the grid (so they move the
/// current cell to another row) while Left/Right/Home/End stay local to move the caret.
/// </summary>
public class DataGridViewTextBoxEditingControl : TextBox, IDataGridViewEditingControl
{
    public DataGridViewTextBoxEditingControl()
    {
        BorderStyle = BorderStyle.None;
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

    /// <summary>Left/Right/Home/End move the caret within the single line of text; Up/Down/PageUp/PageDown are the grid's to move the current cell with instead.</summary>
    public virtual bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left or Keys.Right or Keys.Home or Keys.End:
                return true;

            case Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown:
                return false;

            default:
                return !dataGridViewWantsInputKey;
        }
    }

    public virtual object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) => Text;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // The grid scrolls on a wheel turn over the editing control this round, so the text box itself leaves the event alone.
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);

        EditingControlValueChanged = true;
        EditingControlDataGridView?.NotifyCurrentCellDirty(true);
    }

    public virtual void PrepareEditingControlForEdit(bool selectAll)
    {
        EditingControlValueChanged = false;

        if (selectAll) SelectAll();
        else SelectionStart = Text.Length;
    }

    /// <summary>
    /// Only Up/Down/PageUp/PageDown are ever asked about here (Enter, Escape, typed characters and
    /// the rest always reach the text box normally, exactly as they do without this override): when
    /// EditingControlWantsInputKey turns one of them down, the key is left unhandled so it can
    /// bubble up as the dialog key that moves the grid's current cell to another row instead.
    /// </summary>
    protected override bool ProcessKeyEventArgs(ref Message m)
    {
        if (WindowMessages.IsKeyDown(m.Msg))
        {
            var keyCode = (Keys)(int)m.WParam & Keys.KeyCode;

            if (keyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown)
            {
                var keyData = keyCode | ModifierKeys;
                if (!EditingControlWantsInputKey(keyData, dataGridViewWantsInputKey: true)) return false;
            }
        }

        return base.ProcessKeyEventArgs(ref m);
    }
}
