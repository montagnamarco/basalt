using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The colours <see cref="ToolStripProfessionalRenderer"/> paints with: a
/// blue-accented gradient scheme in the spirit of Office's "blue" theme, but
/// every shade is derived from <see cref="SystemColors"/> (mainly Highlight
/// and Control), not a literal RGB triplet, so a different system palette
/// still looks coherent.
/// </summary>
public class ProfessionalColorTable
{
    public ProfessionalColorTable()
    {
    }

    /// <summary>When true a derived renderer should prefer plain system colours; this table always derives from them already.</summary>
    public bool UseSystemColors { get; set; }

    private static Color Accent => SystemColors.Highlight;

    private static Color Light(float fraction) => ControlPaint.Light(Accent, fraction);

    private static Color Dark(float fraction) => ControlPaint.Dark(Accent, fraction);

    public virtual Color ButtonCheckedGradientBegin => Light(0.65f);

    public virtual Color ButtonCheckedGradientEnd => Light(0.45f);

    public virtual Color ButtonCheckedGradientMiddle => Light(0.55f);

    public virtual Color ButtonCheckedHighlight => Light(0.6f);

    public virtual Color ButtonCheckedHighlightBorder => Dark(0.25f);

    public virtual Color ButtonPressedBorder => Dark(0.3f);

    public virtual Color ButtonPressedGradientBegin => Light(0.5f);

    public virtual Color ButtonPressedGradientEnd => Light(0.2f);

    public virtual Color ButtonPressedGradientMiddle => Light(0.35f);

    public virtual Color ButtonPressedHighlight => Light(0.4f);

    public virtual Color ButtonPressedHighlightBorder => Dark(0.3f);

    public virtual Color ButtonSelectedBorder => Dark(0.2f);

    public virtual Color ButtonSelectedGradientBegin => Light(0.85f);

    public virtual Color ButtonSelectedGradientEnd => Light(0.55f);

    public virtual Color ButtonSelectedGradientMiddle => Light(0.7f);

    public virtual Color ButtonSelectedHighlight => Light(0.8f);

    public virtual Color ButtonSelectedHighlightBorder => Dark(0.2f);

    public virtual Color CheckBackground => ButtonCheckedHighlight;

    public virtual Color CheckPressedBackground => ButtonPressedHighlight;

    public virtual Color CheckSelectedBackground => ButtonSelectedHighlight;

    public virtual Color GripDark => SystemColors.ControlDark;

    public virtual Color GripLight => SystemColors.ControlLightLight;

    public virtual Color ImageMarginGradientBegin => SystemColors.Control;

    public virtual Color ImageMarginGradientEnd => SystemColors.ControlLight;

    public virtual Color ImageMarginGradientMiddle => ControlPaint.Light(SystemColors.Control, 0.3f);

    public virtual Color ImageMarginRevealedGradientBegin => Light(0.9f);

    public virtual Color ImageMarginRevealedGradientEnd => Light(0.7f);

    public virtual Color ImageMarginRevealedGradientMiddle => Light(0.8f);

    public virtual Color MenuBorder => SystemColors.ControlDark;

    public virtual Color MenuItemBorder => Dark(0.2f);

    public virtual Color MenuItemPressedGradientBegin => ButtonPressedGradientBegin;

    public virtual Color MenuItemPressedGradientEnd => ButtonPressedGradientEnd;

    public virtual Color MenuItemPressedGradientMiddle => ButtonPressedGradientMiddle;

    public virtual Color MenuItemSelected => ButtonSelectedHighlight;

    public virtual Color MenuItemSelectedGradientBegin => ButtonSelectedGradientBegin;

    public virtual Color MenuItemSelectedGradientEnd => ButtonSelectedGradientEnd;

    public virtual Color MenuStripGradientBegin => SystemColors.Control;

    public virtual Color MenuStripGradientEnd => SystemColors.ControlLight;

    public virtual Color OverflowButtonGradientBegin => SystemColors.ControlLight;

    public virtual Color OverflowButtonGradientEnd => SystemColors.ControlDark;

    public virtual Color OverflowButtonGradientMiddle => SystemColors.Control;

    public virtual Color RaftingContainerGradientBegin => SystemColors.Control;

    public virtual Color RaftingContainerGradientEnd => SystemColors.ControlLight;

    public virtual Color SeparatorDark => SystemColors.ControlDark;

    public virtual Color SeparatorLight => SystemColors.ControlLightLight;

    public virtual Color StatusStripBorder => SystemColors.ControlDark;

    public virtual Color StatusStripGradientBegin => SystemColors.Control;

    public virtual Color StatusStripGradientEnd => SystemColors.ControlLight;

    public virtual Color ToolStripBorder => SystemColors.ControlDark;

    public virtual Color ToolStripContentPanelGradientBegin => SystemColors.Control;

    public virtual Color ToolStripContentPanelGradientEnd => SystemColors.ControlLightLight;

    public virtual Color ToolStripDropDownBackground => SystemColors.Window;

    public virtual Color ToolStripGradientBegin => SystemColors.Control;

    public virtual Color ToolStripGradientEnd => SystemColors.ControlLight;

    public virtual Color ToolStripGradientMiddle => ControlPaint.Light(SystemColors.Control, 0.2f);

    public virtual Color ToolStripPanelGradientBegin => SystemColors.Control;

    public virtual Color ToolStripPanelGradientEnd => SystemColors.ControlLight;
}
