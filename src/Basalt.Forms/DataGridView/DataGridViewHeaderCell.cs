using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What a column header, row header and the top-left corner cell share: never editable, drawn as a raised button.</summary>
public class DataGridViewHeaderCell : DataGridViewCell
{
    public DataGridViewHeaderCell()
    {
    }

    protected ButtonState ButtonState { get; private set; } = ButtonState.Normal;

    public override bool Displayed => base.Displayed;

    public override Type FormattedValueType => typeof(string);

    public override bool Frozen => true;

    public override bool ReadOnly
    {
        get => true;
        set { }
    }

    public override bool Resizable => false;

    public override bool Selected
    {
        get => base.Selected;
        set => base.Selected = value;
    }

    public override Type? ValueType
    {
        get => base.ValueType ?? typeof(object);
        set => base.ValueType = value;
    }

    public override bool Visible => true;

    public override object Clone() => base.Clone();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    public override DataGridViewElementStates GetInheritedState(int rowIndex) => DataGridViewElementStates.Visible | (Selected ? DataGridViewElementStates.Selected : DataGridViewElementStates.None);

    protected override Size GetSize(int rowIndex) => new(OwningColumn?.Width ?? DataGridView?.RowHeadersWidth ?? 0, OwningRow?.Height ?? DataGridView?.ColumnHeadersHeight ?? 0);

    protected override object? GetValue(int rowIndex) => base.GetValue(rowIndex);

    protected override bool MouseDownUnsharesRow(DataGridViewCellMouseEventArgs e) => false;

    protected override bool MouseEnterUnsharesRow(int rowIndex) => false;

    protected override bool MouseLeaveUnsharesRow(int rowIndex) => false;

    protected override bool MouseUpUnsharesRow(DataGridViewCellMouseEventArgs e) => false;

    protected override void OnMouseDown(DataGridViewCellMouseEventArgs e)
    {
        ButtonState = ButtonState.Pushed;
        DataGridView?.InvalidateCellCore(this);
    }

    protected override void OnMouseEnter(int rowIndex)
    {
    }

    protected override void OnMouseLeave(int rowIndex)
    {
        ButtonState = ButtonState.Normal;
    }

    protected override void OnMouseUp(DataGridViewCellMouseEventArgs e)
    {
        ButtonState = ButtonState.Normal;
        DataGridView?.InvalidateCellCore(this);
    }

    /// <summary>Draws the raised button face and its text; a column header adds the sort glyph on top of this.</summary>
    protected override void Paint(
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle cellBounds,
        int rowIndex,
        DataGridViewElementStates dataGridViewElementState,
        object? value,
        object? formattedValue,
        string? errorText,
        DataGridViewCellStyle cellStyle,
        DataGridViewAdvancedBorderStyle advancedBorderStyle,
        DataGridViewPaintParts paintParts)
    {
        ControlPaint.DrawButton(graphics, cellBounds, ButtonState);

        if (formattedValue is null) return;

        var textBounds = new Rectangle(cellBounds.X + 3, cellBounds.Y, Math.Max(cellBounds.Width - 6, 0), cellBounds.Height);
        var flags = TextAlignmentFlags(cellStyle.Alignment);
        TextRenderer.DrawText(graphics, formattedValue.ToString(), cellStyle.Font ?? DataGridView?.Font, textBounds, cellStyle.ForeColor, flags);
    }

    public override string ToString() => $"{GetType().Name} {{ ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";
}
