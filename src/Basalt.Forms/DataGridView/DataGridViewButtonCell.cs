using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A cell painted as a push button (ControlPaint.DrawButton); OnMouseDown/OnMouseUp below - called
/// straight from the grid's hit-testing, not through a grid event this cell subscribes to - track
/// whether the mouse is currently down over it, to paint the pushed state.
/// </summary>
public class DataGridViewButtonCell : DataGridViewCell
{
    private bool _pressed;

    public DataGridViewButtonCell()
    {
    }

    public override Type? EditType => null;

    internal override bool UsesTextEditorInternal => false;

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle { get; set; } = FlatStyle.Standard;

    public override Type FormattedValueType => typeof(string);

    [DefaultValue(false)]
    public bool UseColumnTextForButtonValue { get; set; }

    public override Type ValueType => base.ValueType ?? typeof(object);

    public override object Clone()
    {
        var clone = (DataGridViewButtonCell)base.Clone();
        clone.FlatStyle = FlatStyle;
        clone.UseColumnTextForButtonValue = UseColumnTextForButtonValue;

        return clone;
    }

    protected override object? GetValue(int rowIndex) =>
        UseColumnTextForButtonValue && OwningColumn is DataGridViewButtonColumn { Text: { Length: > 0 } text } ? text : base.GetValue(rowIndex);

    protected override void OnMouseDown(DataGridViewCellMouseEventArgs e)
    {
        _pressed = true;
        DataGridView?.InvalidateCellCore(this);
    }

    protected override void OnMouseUp(DataGridViewCellMouseEventArgs e)
    {
        if (!_pressed) return;

        _pressed = false;
        DataGridView?.InvalidateCellCore(this);
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
        if ((paintParts & DataGridViewPaintParts.Background) != 0)
        {
            var selected = (cellState & DataGridViewElementStates.Selected) != 0;
            using var back = new SolidBrush(selected ? cellStyle.SelectionBackColor : cellStyle.BackColor);
            graphics.FillRectangle(back, cellBounds);
        }

        if ((paintParts & DataGridViewPaintParts.ContentBackground) != 0)
        {
            var state = (ReadOnly || (DataGridView?.ReadOnly ?? false)) ? ButtonState.Inactive : _pressed ? ButtonState.Pushed : ButtonState.Normal;
            ControlPaint.DrawButton(graphics, cellBounds, state);
        }

        if ((paintParts & DataGridViewPaintParts.ContentForeground) != 0 && formattedValue is { } text)
        {
            var flags = TextAlignmentFlags(cellStyle.Alignment);
            TextRenderer.DrawText(graphics, text.ToString(), cellStyle.Font ?? DataGridView?.Font, cellBounds, cellStyle.ForeColor, flags);
        }

        if ((paintParts & DataGridViewPaintParts.Border) != 0) PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);
    }

    public override string ToString() => $"DataGridViewButtonCell {{ ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";
}
