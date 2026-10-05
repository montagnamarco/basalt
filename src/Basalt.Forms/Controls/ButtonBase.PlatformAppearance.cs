using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public abstract partial class ButtonBase
{
    private void PaintPortableButton(Graphics graphics, Rectangle bounds)
    {
        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        PortableVisualStyles.PaintButton(graphics, bounds, palette, PortableVisualStylePlatform,
            _mouseInside, Pushed, Enabled, _isDefault);
        var textBounds = Rectangle.Inflate(bounds, -3, -3);
        if (Pushed)
        {
            textBounds.Offset(1, 1);
        }

        var flags = ContentAlignments.ToTextFormat(_textAlign) | TextFormatFlags.WordBreak;
        if (!_useMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }

        var textColor = !Enabled ? palette.DisabledText
            : _isDefault && PortableVisualStylePlatform == PortableVisualStylePlatform.MacOS && !HasExplicitAppearanceForeColor
                ? palette.SelectedText : ForeColor;
        TextRenderer.DrawText(graphics, Text, Font, textBounds, textColor, flags);
        if (Focused && ShowFocusCuesInternal)
        {
            DrawFocusDots(graphics, textBounds);
        }
    }
}
