using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

/// <summary>
/// The whole classic look: a toolbar strip, a two-column grid with a
/// Control-coloured margin for the +/- glyphs, a draggable splitter between
/// the name and value columns, category rows spanning the full width in
/// bold on the margin's own colour, and a help pane with the selected
/// item's name in bold followed by its DescriptionAttribute text.
/// </summary>
public partial class PropertyGrid
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;

        using (var face = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(face, ClientRectangle);
        }

        if (_toolbarVisible) PaintToolbar(graphics);
        PaintGrid(graphics);
        if (_helpVisible) PaintHelp(graphics);

        base.OnPaint(e);
    }

    private void PaintGrid(Graphics graphics)
    {
        var bounds = GridBounds;

        using (var background = new SolidBrush(ViewBackColor))
        {
            graphics.FillRectangle(background, bounds);
        }

        ControlPaint.DrawBorder(graphics, bounds, ViewBorderColor, ButtonBorderStyle.Solid);

        var rows = VisibleRowsWithDepth();
        for (var i = 0; i < rows.Count; i++)
        {
            if (RowBounds(i).Top >= bounds.Bottom) break;

            PaintRow(graphics, i, rows[i].Item, rows[i].Depth);
        }

        using var splitterPen = new Pen(LineColor);
        graphics.DrawLine(splitterPen, bounds.Left + SplitterX, bounds.Top, bounds.Left + SplitterX, bounds.Bottom);
    }

    private void PaintRow(Graphics graphics, int rowIndex, GridItem item, int depth)
    {
        var row = RowBounds(rowIndex);
        var isCategory = item.GridItemType == GridItemType.Category;
        var isSelected = ReferenceEquals(item, SelectedGridItem);

        if (isCategory)
        {
            using var band = new SolidBrush(CategorySplitterColor);
            graphics.FillRectangle(band, row);

            using var categoryFont = new Font(Font, Font.Style | FontStyle.Bold);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(graphics, item.Label, categoryFont, new Rectangle(row.Left + 4, row.Top, row.Width - 4, row.Height), CategoryForeColor, flags);
        }
        else
        {
            var margin = MarginBounds(rowIndex);
            using (var marginBrush = new SolidBrush(SystemColors.Control))
            {
                graphics.FillRectangle(marginBrush, margin);
            }

            var name = NameBounds(rowIndex);
            var value = ValueBounds(rowIndex);

            var nameColor = Enabled ? ViewForeColor : DisabledItemForeColor;
            var nameBack = isSelected && Focused ? SelectedItemWithFocusBackColor : ViewBackColor;
            var textColor = isSelected && Focused ? SelectedItemWithFocusForeColor : nameColor;

            using (var nameBrush = new SolidBrush(nameBack))
            {
                graphics.FillRectangle(nameBrush, name);
            }

            var textFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(graphics, item.Label, Font, Pad(name), textColor, textFlags);

            if (EditingRowIndex != rowIndex)
            {
                var text = ValueText(item);
                TextRenderer.DrawText(graphics, text, Font, Pad(value), nameColor, textFlags);
            }
        }

        if (item.Expandable)
        {
            var plusMinus = PlusMinusBounds(rowIndex, depth);
            PaintPlusMinus(graphics, plusMinus, item.Expanded);
        }

        using var gridLine = new Pen(LineColor);
        graphics.DrawLine(gridLine, row.Left, row.Bottom - 1, row.Right, row.Bottom - 1);
    }

    private static Rectangle Pad(Rectangle rectangle) => new(rectangle.Left + 3, rectangle.Top, Math.Max(rectangle.Width - 6, 0), rectangle.Height);

    /// <summary>Blank for a multi-selection that disagrees, else the value's own text (through its TypeConverter when it has one).</summary>
    private string ValueText(GridItem item)
    {
        if (item is PropertyGridEntry { HasMixedValue: true }) return string.Empty;

        var value = item.Value;
        if (value is null) return string.Empty;

        var descriptor = item.PropertyDescriptor;
        if (descriptor is not null && descriptor.Converter.CanConvertTo(typeof(string)))
        {
            return descriptor.Converter.ConvertToString(value) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    private static void PaintPlusMinus(Graphics graphics, Rectangle bounds, bool expanded)
    {
        using var border = new Pen(SystemColors.ControlDark);
        using var fill = new SolidBrush(SystemColors.Window);
        using var sign = new Pen(SystemColors.WindowText);

        graphics.FillRectangle(fill, bounds);
        graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

        var centerY = bounds.Top + bounds.Height / 2;
        var centerX = bounds.Left + bounds.Width / 2;
        graphics.DrawLine(sign, bounds.Left + 2, centerY, bounds.Right - 3, centerY);
        if (!expanded) graphics.DrawLine(sign, centerX, bounds.Top + 2, centerX, bounds.Bottom - 3);
    }

    /// <summary>The selected item's name, bold, then its DescriptionAttribute text, wrapped under it.</summary>
    private void PaintHelp(Graphics graphics)
    {
        var bounds = HelpBounds;

        using (var background = new SolidBrush(HelpBackColor))
        {
            graphics.FillRectangle(background, bounds);
        }

        ControlPaint.DrawBorder(graphics, bounds, HelpBorderColor, ButtonBorderStyle.Solid);

        var item = SelectedGridItem;
        if (item?.Label is null) return;

        var inner = new Rectangle(bounds.Left + 4, bounds.Top + 3, bounds.Width - 8, bounds.Height - 6);

        using var boldFont = new Font(Font, Font.Style | FontStyle.Bold);
        var titleFlags = TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(graphics, item.Label, boldFont, inner, HelpForeColor, titleFlags);

        var description = item.PropertyDescriptor?.Description ?? string.Empty;
        if (description.Length == 0) return;

        var textTop = inner.Top + Font.Height + 2;
        var textBounds = new Rectangle(inner.Left, textTop, inner.Width, Math.Max(inner.Bottom - textTop, 0));
        var bodyFlags = TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(graphics, description, Font, textBounds, HelpForeColor, bodyFlags);
    }
}
