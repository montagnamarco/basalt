using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public partial class ComboBox
{
    /// <summary>Half the dropdown glyph's width: an 8px-wide triangle, the size a native
    /// combo box's down-arrow glyph renders at regardless of the button's own size.</summary>
    private const int ArrowGlyphHalfWidth = 4;

    /// <summary>Half the dropdown glyph's height: a 4px-tall triangle, matching the 2:1
    /// width-to-height proportion of the native combo box down-arrow glyph.</summary>
    private const int ArrowGlyphHalfHeight = 2;

    private void PaintPortableButton(Graphics graphics)
    {
        var bounds = ButtonBounds;
        var palette = CollectionVisualAppearance.Palette(this);
        PortableVisualStyles.PaintButton(graphics, bounds, palette, PortableVisualStylePlatform,
            hot: Focused, pressed: _droppedDown, enabled: Enabled, isDefault: false);

        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2;
        using var arrow = new SolidBrush(Enabled ? palette.Text : palette.DisabledText);
        graphics.FillPolygon(arrow,
        [
            new Point(centerX - ArrowGlyphHalfWidth, centerY - ArrowGlyphHalfHeight),
            new Point(centerX + ArrowGlyphHalfWidth, centerY - ArrowGlyphHalfHeight),
            new Point(centerX, centerY + ArrowGlyphHalfHeight),
        ]);
    }
}
