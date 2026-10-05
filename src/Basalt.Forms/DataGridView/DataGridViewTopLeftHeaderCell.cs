using System.Drawing;

namespace System.Windows.Forms;

/// <summary>The header at the intersection of the grid's row and column headers.</summary>
public class DataGridViewTopLeftHeaderCell : DataGridViewColumnHeaderCell
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    public DataGridViewTopLeftHeaderCell()
    {
    }

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        base.GetContentBounds(graphics, cellStyle, rowIndex);

    protected override Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        base.GetErrorIconBounds(graphics, cellStyle, rowIndex);

    protected override Size GetPreferredSize(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex, Size constraintSize) =>
        base.GetPreferredSize(graphics, cellStyle, rowIndex, constraintSize);

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex,
        DataGridViewElementStates cellState, object? value, object? formattedValue, string? errorText,
        DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts) =>
        base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText,
            cellStyle, advancedBorderStyle, paintParts);

    protected override void PaintBorder(Graphics graphics, Rectangle clipBounds, Rectangle bounds,
        DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle) =>
        base.PaintBorder(graphics, clipBounds, bounds, cellStyle, advancedBorderStyle);

    public override string ToString() => base.ToString();
}
