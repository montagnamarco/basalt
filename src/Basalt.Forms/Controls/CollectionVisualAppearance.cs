using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

/// <summary>Colors shared by the portable collection controls without changing their classic paint paths.</summary>
internal static class CollectionVisualAppearance
{
    internal static Color Field(Control control, Color classic)
    {
        if (!control.PortableVisualStylesEnabled)
        {
            return classic;
        }

        if (control.HasExplicitAppearanceBackColor)
        {
            return control.BackColor;
        }

        var palette = Palette(control);
        return control.Enabled ? palette.Field : palette.Face;
    }

    internal static Color Text(Control control, Color classic)
    {
        if (!control.PortableVisualStylesEnabled)
        {
            return classic;
        }

        if (!control.Enabled)
        {
            return Palette(control).DisabledText;
        }

        if (control.HasExplicitAppearanceForeColor)
        {
            return control.ForeColor;
        }

        return Palette(control).Text;
    }

    internal static Color SelectionField(Control control, bool focused)
    {
        if (!control.PortableVisualStylesEnabled)
        {
            return SystemColors.Highlight;
        }

        var palette = Palette(control);
        return !control.Enabled ? palette.PressedFace : focused ? palette.Selection : palette.HotFace;
    }

    internal static Color SelectionText(Control control, bool focused)
    {
        if (!control.PortableVisualStylesEnabled)
        {
            return SystemColors.HighlightText;
        }

        var palette = Palette(control);
        return !control.Enabled ? palette.DisabledText : focused ? palette.SelectedText : Text(control, palette.Text);
    }

    internal static PortableVisualStylePalette Palette(Control control) =>
        PortableVisualStyles.Palette(control.PortableVisualStylePlatform);
}
