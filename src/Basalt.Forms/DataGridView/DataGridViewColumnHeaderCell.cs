using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A column's own header cell: the raised button that shows HeaderText and, when the column is sorted, a glyph.</summary>
public class DataGridViewColumnHeaderCell : DataGridViewHeaderCell
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    public DataGridViewColumnHeaderCell()
    {
    }

    public SortOrder SortGlyphDirection { get; set; } = SortOrder.None;

    public override object Clone()
    {
        var clone = (DataGridViewColumnHeaderCell)base.Clone();
        clone.SortGlyphDirection = SortGlyphDirection;

        return clone;
    }

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => Rectangle.Empty;

    protected override object? GetClipboardContent(int rowIndex, bool firstCell, bool lastCell,
        bool inFirstRow, bool inLastRow, string format) =>
        FormatClipboardText(OwningColumn?.HeaderText ?? string.Empty, firstCell, lastCell, inLastRow, format);

    /// <summary>This cell's own menu, or its column's, or the grid's - the column header has a column to fall back on that a plain header cell does not.</summary>
    public override ContextMenuStrip? GetInheritedContextMenuStrip(int rowIndex) => ContextMenuStrip ?? OwningColumn?.ContextMenuStrip ?? DataGridView?.ContextMenuStrip;

    public override DataGridViewCellStyle GetInheritedStyle(DataGridViewCellStyle? inheritedCellStyle, int rowIndex, bool includeColors)
    {
        var resolved = inheritedCellStyle ?? new DataGridViewCellStyle();
        if (DataGridView is { } grid) resolved.ApplyStyle(grid.ColumnHeadersDefaultCellStyle);
        if (OwningColumn is { HasDefaultCellStyle: true } column) resolved.ApplyStyle(column.DefaultCellStyle);
        if (HasStyle) resolved.ApplyStyle(Style);

        if (resolved.Font is null) resolved.Font = DataGridView?.Font;
        if (resolved.BackColor.IsEmpty) resolved.BackColor = DataGridView?.PortableHeaderBackColor ?? SystemColors.Control;
        if (resolved.ForeColor.IsEmpty) resolved.ForeColor = DataGridView?.PortableHeaderTextColor ?? SystemColors.ControlText;
        if (resolved.Alignment == DataGridViewContentAlignment.NotSet) resolved.Alignment = DataGridViewContentAlignment.MiddleLeft;

        return resolved;
    }

    protected override Size GetPreferredSize(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex, Size constraintSize) => new(-1, -1);

    protected override object? GetValue(int rowIndex) => OwningColumn?.HeaderText ?? base.GetValue(rowIndex);

    /// <summary>The button face, the header text, and a triangular glyph on the right edge when sorted.</summary>
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
        var glyphWidth = SortGlyphDirection == SortOrder.None ? 0 : 12;
        var textBounds = cellBounds with { Width = Math.Max(cellBounds.Width - glyphWidth, 0) };

        base.Paint(graphics, clipBounds, textBounds, rowIndex, dataGridViewElementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
        if ((paintParts & DataGridViewPaintParts.Border) != 0) PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);

        if (SortGlyphDirection == SortOrder.None) return;

        var glyphBounds = new Rectangle(textBounds.Right, cellBounds.Y, glyphWidth, cellBounds.Height);
        if (DataGridView?.PaintPortableHeader(graphics, glyphBounds, cellStyle, ButtonState.Normal, paintParts) != true)
        {
            ControlPaint.DrawButton(graphics, glyphBounds, ButtonState.Normal);
        }
        PaintSortGlyph(graphics, new Rectangle(textBounds.Right + 2, cellBounds.Y + cellBounds.Height / 2 - 2, glyphWidth - 4, 4), cellStyle.ForeColor);
    }

    /// <summary>A small filled triangle: pointing up for an ascending sort, down for a descending one.</summary>
    private void PaintSortGlyph(Graphics graphics, Rectangle bounds, Color color)
    {
        using var brush = new SolidBrush(color);

        var points = SortGlyphDirection == SortOrder.Ascending
            ? new[]
            {
                new Point(bounds.Left, bounds.Bottom),
                new Point(bounds.Right, bounds.Bottom),
                new Point(bounds.Left + bounds.Width / 2, bounds.Top),
            }
            : new[]
            {
                new Point(bounds.Left, bounds.Top),
                new Point(bounds.Right, bounds.Top),
                new Point(bounds.Left + bounds.Width / 2, bounds.Bottom),
            };

        graphics.FillPolygon(brush, points);
    }

    protected override bool SetValue(int rowIndex, object? value)
    {
        if (OwningColumn is null) return base.SetValue(rowIndex, value);

        var changed = !Equals(OwningColumn.HeaderText, value);
        OwningColumn.HeaderText = value?.ToString() ?? string.Empty;

        return changed;
    }

    public override string ToString() => $"{GetType().Name} {{ ColumnIndex={ColumnIndex} }}";
}
