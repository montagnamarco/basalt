using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A row's own header cell: the raised button at its left edge, with a current-row triangle when it is the current row.</summary>
public class DataGridViewRowHeaderCell : DataGridViewHeaderCell
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    public DataGridViewRowHeaderCell()
    {
    }

    public override object Clone() => base.Clone();

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => Rectangle.Empty;

    protected override object? GetClipboardContent(int rowIndex, bool firstCell, bool lastCell,
        bool inFirstRow, bool inLastRow, string format) =>
        FormatClipboardText(Value?.ToString() ?? string.Empty, firstCell, lastCell, inLastRow, format);

    protected override Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => GetStandardErrorIconBounds(cellStyle, rowIndex);

    protected override string GetErrorText(int rowIndex) => OwningRow?.GetErrorText(rowIndex) ?? base.GetErrorText(rowIndex);

    /// <summary>This cell's own menu, or its row's, or the grid's - the row header has a row to fall back on that a plain header cell does not.</summary>
    public override ContextMenuStrip? GetInheritedContextMenuStrip(int rowIndex) => ContextMenuStrip ?? OwningRow?.GetContextMenuStrip(rowIndex) ?? DataGridView?.ContextMenuStrip;

    public override DataGridViewCellStyle GetInheritedStyle(DataGridViewCellStyle? inheritedCellStyle, int rowIndex, bool includeColors)
    {
        var resolved = inheritedCellStyle ?? new DataGridViewCellStyle();
        if (DataGridView is { } grid) resolved.ApplyStyle(grid.RowHeadersDefaultCellStyle);
        if (OwningRow is { HasDefaultCellStyle: true } row) resolved.ApplyStyle(row.DefaultCellStyle);
        if (HasStyle) resolved.ApplyStyle(Style);

        if (resolved.Font is null) resolved.Font = DataGridView?.Font;
        if (resolved.BackColor.IsEmpty) resolved.BackColor = SystemColors.Control;
        if (resolved.ForeColor.IsEmpty) resolved.ForeColor = SystemColors.ControlText;

        return resolved;
    }

    protected override Size GetPreferredSize(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex, Size constraintSize) => new(-1, -1);

    protected override object? GetValue(int rowIndex) => base.GetValue(rowIndex);

    /// <summary>The button face, and a small right-pointing triangle for the current row (a filled diamond on the row being edited is left out).</summary>
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
        ControlPaint.DrawButton(graphics, cellBounds, ButtonState);

        if (DataGridView is not { } grid || rowIndex != grid.CurrentCellAddress.Y) return;

        var centerY = cellBounds.Y + cellBounds.Height / 2;
        var left = cellBounds.X + cellBounds.Width / 2 - 3;

        using var brush = new SolidBrush(cellStyle.ForeColor);
        if (grid.ShowEditingIcon && grid.IsCurrentCellInEditMode)
        {
            graphics.FillPolygon(brush, [new Point(left, centerY), new Point(left + 4, centerY - 4), new Point(left + 8, centerY), new Point(left + 4, centerY + 4)]);
            return;
        }
        graphics.FillPolygon(brush,
        [
            new Point(left, centerY - 4),
            new Point(left, centerY + 4),
            new Point(left + 6, centerY),
        ]);
    }

    private static void PaintHeaderBorders(Graphics graphics, Rectangle bounds, DataGridViewAdvancedBorderStyle borderStyle)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        DrawEdge(borderStyle.Top, bounds.Left, bounds.Top, bounds.Right - 1, bounds.Top);
        DrawEdge(borderStyle.Bottom, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
        DrawEdge(borderStyle.Left, bounds.Left, bounds.Top, bounds.Left, bounds.Bottom - 1);
        DrawEdge(borderStyle.Right, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);

        void DrawEdge(DataGridViewAdvancedCellBorderStyle style, int x1, int y1, int x2, int y2)
        {
            if (style is DataGridViewAdvancedCellBorderStyle.None or DataGridViewAdvancedCellBorderStyle.NotSet) return;

            var color = style switch
            {
                DataGridViewAdvancedCellBorderStyle.Single => SystemColors.ControlDarkDark,
                DataGridViewAdvancedCellBorderStyle.Inset or DataGridViewAdvancedCellBorderStyle.InsetDouble => SystemColors.ControlDark,
                _ => SystemColors.ControlLightLight,
            };
            using var pen = new Pen(color);
            graphics.DrawLine(pen, x1, y1, x2, y2);

            if (style is not (DataGridViewAdvancedCellBorderStyle.InsetDouble or DataGridViewAdvancedCellBorderStyle.OutsetDouble)) return;

            // Double styles include a second line toward the inside of the header.
            var innerX1 = x1 == x2 ? (x1 == bounds.Left ? x1 + 1 : x1 - 1) : x1;
            var innerX2 = x1 == x2 ? innerX1 : x2;
            var innerY1 = y1 == y2 ? (y1 == bounds.Top ? y1 + 1 : y1 - 1) : y1;
            var innerY2 = y1 == y2 ? innerY1 : y2;
            using var innerPen = new Pen(style == DataGridViewAdvancedCellBorderStyle.InsetDouble
                ? SystemColors.ControlLightLight : SystemColors.ControlDark);
            graphics.DrawLine(innerPen, innerX1, innerY1, innerX2, innerY2);
        }
    }

    protected override bool SetValue(int rowIndex, object? value) => base.SetValue(rowIndex, value);

    public override string ToString() => $"{GetType().Name} {{ RowIndex={RowIndex} }}";
}
