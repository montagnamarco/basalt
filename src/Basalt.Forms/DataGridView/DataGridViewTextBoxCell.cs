using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A cell that shows and edits its value as plain text. Editing uses a plain TextBox placed over
/// the cell rather than Microsoft's DataGridViewTextBoxEditingControl/IDataGridViewEditingControl
/// pair, which the next round can add without changing this type's public members.
/// </summary>
public class DataGridViewTextBoxCell : DataGridViewCell
{
    public DataGridViewTextBoxCell()
    {
    }

    public override Type FormattedValueType => typeof(string);

    public virtual int MaxInputLength { get; set; } = 32767;

    public override Type ValueType => base.ValueType ?? typeof(string);

    public override object Clone() => base.Clone();

    public override void DetachEditingControl()
    {
        if (DataGridView?.EditingControl is TextBox textBox) textBox.Visible = false;
    }

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => Rectangle.Empty;

    protected override Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => Rectangle.Empty;

    protected override Size GetPreferredSize(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex, Size constraintSize) => new(-1, -1);

    public override void InitializeEditingControl(int rowIndex, object? initialFormattedValue, DataGridViewCellStyle dataGridViewCellStyle)
    {
        base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);

        if (DataGridView?.EditingControl is not TextBox textBox) return;

        textBox.MaxLength = MaxInputLength;
        textBox.Text = initialFormattedValue?.ToString() ?? string.Empty;
    }

    public override bool KeyEntersEditMode(KeyEventArgs e) =>
        !e.Alt && !e.Control &&
        (e.KeyCode is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or Keys.Space || e.KeyCode is >= Keys.NumPad0 and <= Keys.NumPad9);

    protected override void OnEnter(int rowIndex, bool throughMouseClick)
    {
    }

    protected override void OnLeave(int rowIndex, bool throughMouseClick)
    {
    }

    protected override void OnMouseClick(DataGridViewCellMouseEventArgs e)
    {
    }

    protected override void Paint(
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle cellBounds,
        int rowIndex,
        DataGridViewElementStates cellState,
        object? value,
        object? formattedValue,
        string? errorText,
        DataGridViewCellStyle cellStyle,
        DataGridViewAdvancedBorderStyle advancedBorderStyle,
        DataGridViewPaintParts paintParts)
    {
        base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
    }

    public override void PositionEditingControl(
        bool setLocation,
        bool setSize,
        Rectangle cellBounds,
        Rectangle cellClip,
        DataGridViewCellStyle cellStyle,
        bool singleVerticalBorderAdded,
        bool singleHorizontalBorderAdded,
        bool isFirstDisplayedColumn,
        bool isFirstDisplayedRow)
    {
        base.PositionEditingControl(setLocation, setSize, cellBounds, cellClip, cellStyle, singleVerticalBorderAdded, singleHorizontalBorderAdded, isFirstDisplayedColumn, isFirstDisplayedRow);
    }

    public override string ToString() => $"{GetType().Name} {{ ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";
}
