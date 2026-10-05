using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public partial class DataGridView
{
    private bool _portableBackgroundColorExplicit;
    private bool _portableGridColorExplicit;

    internal Color PortableGridBackgroundColor => PortableVisualStylesEnabled && !_portableBackgroundColorExplicit
        ? PortableVisualStyles.Palette(PortableVisualStylePlatform).Field
        : BackgroundColor;

    internal Color PortableGridLineColor => PortableVisualStylesEnabled && !_portableGridColorExplicit
        ? PortableVisualStyles.Palette(PortableVisualStylePlatform).Border
        : GridColor;

    internal Color PortableHeaderBackColor => PortableVisualStylesEnabled && EnableHeadersVisualStyles
        ? PortableVisualStyles.Palette(PortableVisualStylePlatform).Face
        : SystemColors.Control;

    internal Color PortableHeaderTextColor => PortableVisualStylesEnabled && EnableHeadersVisualStyles
        ? Enabled ? PortableVisualStyles.Palette(PortableVisualStylePlatform).Text
            : PortableVisualStyles.Palette(PortableVisualStylePlatform).DisabledText
        : SystemColors.ControlText;

    internal DataGridViewCellStyle GetPortableDefaultCellStyle()
    {
        var style = DefaultCellStyle;
        if (!PortableVisualStylesEnabled)
        {
            return style;
        }

        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        var resolved = new DataGridViewCellStyle(style);
        if (!style.HasExplicitBackColor)
        {
            resolved.BackColor = palette.Field;
        }
        if (!style.HasExplicitForeColor)
        {
            resolved.ForeColor = Enabled ? palette.Text : palette.DisabledText;
        }
        if (!style.HasExplicitSelectionBackColor)
        {
            resolved.SelectionBackColor = palette.Selection;
        }
        if (!style.HasExplicitSelectionForeColor)
        {
            resolved.SelectionForeColor = Enabled ? palette.SelectedText : palette.DisabledText;
        }
        return resolved;
    }

    internal bool PaintPortableCellButton(Graphics graphics, Rectangle bounds, DataGridViewCellStyle style,
        ButtonState state, bool arrow)
    {
        if (!PortableVisualStylesEnabled || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }
        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        if (style.BackColor != palette.Field)
        {
            palette = palette with
            {
                Face = style.BackColor,
                HotFace = style.BackColor,
                PressedFace = style.BackColor
            };
        }
        PortableVisualStyles.PaintButton(graphics, bounds, palette, PortableVisualStylePlatform,
            hot: false, pressed: (state & ButtonState.Pushed) != 0,
            enabled: Enabled && (state & ButtonState.Inactive) == 0, isDefault: false);
        if (arrow)
        {
            var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            using var mark = new SolidBrush(Enabled && (state & ButtonState.Inactive) == 0 ? style.ForeColor : palette.DisabledText);
            graphics.FillPolygon(mark,
                [new Point(center.X - 3, center.Y - 1), new Point(center.X + 3, center.Y - 1), new Point(center.X, center.Y + 2)]);
        }
        return true;
    }

    internal bool PaintPortableCellCheck(Graphics graphics, Rectangle bounds, DataGridViewCellStyle style,
        CheckState state, ButtonState buttonState)
    {
        if (!PortableVisualStylesEnabled)
        {
            return false;
        }
        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        if (style.ForeColor != palette.Text && style.ForeColor != palette.DisabledText)
        {
            palette = palette with { Accent = style.ForeColor };
        }
        PortableVisualStyles.PaintGlyph(graphics, bounds, palette,
            radio: false, selected: state == CheckState.Checked, mixed: state == CheckState.Indeterminate,
            pressed: (buttonState & ButtonState.Pushed) != 0, enabled: Enabled && (buttonState & ButtonState.Inactive) == 0);
        return true;
    }

    internal bool PaintPortableHeader(Graphics graphics, Rectangle bounds, DataGridViewCellStyle style,
        ButtonState state, DataGridViewPaintParts parts)
    {
        if (!PortableVisualStylesEnabled || !EnableHeadersVisualStyles)
        {
            return false;
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return true;
        }

        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        var pressed = (state & ButtonState.Pushed) != 0;
        var face = pressed && style.BackColor == palette.Face ? palette.PressedFace : style.BackColor;
        if ((parts & DataGridViewPaintParts.Background) != 0)
        {
            using Brush brush = PortableVisualStylePlatform == PortableVisualStylePlatform.Windows
                && (face == palette.Face || face == palette.PressedFace)
                ? new LinearGradientBrush(bounds, Color.FromArgb((face.R + 255) / 2,
                    (face.G + 255) / 2, (face.B + 255) / 2), face, LinearGradientMode.Vertical)
                : new SolidBrush(face);
            graphics.FillRectangle(brush, bounds);
        }
        if ((parts & DataGridViewPaintParts.Border) != 0 && bounds.Width > 0 && bounds.Height > 0)
        {
            using var border = new Pen(palette.Border);
            graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }
        return true;
    }
}
