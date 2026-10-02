using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A row's own header cell: the raised button at its left edge, with a current-row triangle when it is the current row.</summary>
public class DataGridViewRowHeaderCell : DataGridViewHeaderCell
{
    public DataGridViewRowHeaderCell()
    {
    }

    public override object Clone() => base.Clone();

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => Rectangle.Empty;

    protected override Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => Rectangle.Empty;

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
        graphics.FillPolygon(brush,
        [
            new Point(left, centerY - 4),
            new Point(left, centerY + 4),
            new Point(left + 6, centerY),
        ]);
    }

    protected override bool SetValue(int rowIndex, object? value) => base.SetValue(rowIndex, value);

    public override string ToString() => $"{GetType().Name} {{ RowIndex={RowIndex} }}";
}
