using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A cell painted as a push button (ControlPaint.DrawButton); OnMouseDown/OnMouseUp below - called
/// straight from the grid's hit-testing, not through a grid event this cell subscribes to - track
/// whether the mouse is currently down over it, to paint the pushed state.
/// </summary>
public partial class DataGridViewButtonCell : DataGridViewCell
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
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

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        DataGridView is not null && ColumnIndex >= 0 && rowIndex >= 0
            ? new Rectangle(Point.Empty, GetSize(rowIndex))
            : Rectangle.Empty;

    protected override Size GetPreferredSize(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex, Size constraintSize)
    {
        var text = FormattedValue?.ToString() ?? string.Empty;
        var measured = TextRenderer.MeasureText(text, cellStyle.Font ?? DataGridView?.Font);
        return new Size(Math.Max(18, measured.Width + 8 + cellStyle.Padding.Horizontal),
            Math.Max(18, measured.Height + 6 + cellStyle.Padding.Vertical));
    }

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
        var selected = (cellState & DataGridViewElementStates.Selected) != 0;
        if ((paintParts & DataGridViewPaintParts.Background) != 0)
        {
            using var back = new SolidBrush(selected ? cellStyle.SelectionBackColor : cellStyle.BackColor);
            graphics.FillRectangle(back, cellBounds);
        }

        if ((paintParts & DataGridViewPaintParts.ContentBackground) != 0)
        {
            var state = (ReadOnly || (DataGridView?.ReadOnly ?? false)) ? ButtonState.Inactive : _pressed ? ButtonState.Pushed : ButtonState.Normal;
            var buttonBounds = selected && DataGridView is { PortableVisualStylesEnabled: true }
                && cellBounds.Width > 4 && cellBounds.Height > 4
                ? Rectangle.Inflate(cellBounds, -2, -2)
                : cellBounds;
            if (DataGridView?.PaintPortableCellButton(graphics, buttonBounds, cellStyle, state, arrow: false) != true)
            {
                ControlPaint.DrawButton(graphics, cellBounds, state);
            }
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
