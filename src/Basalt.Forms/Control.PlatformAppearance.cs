using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public partial class Control
{
    // Designer previews opt in per form rather than changing the IDE process.
    private bool? _visualStylesOverride;
    private PortableVisualStylePlatform? _visualStylePlatformOverride;

    internal bool? VisualStylesOverride
    {
        get => _visualStylesOverride;
        set
        {
            if (_visualStylesOverride == value)
            {
                return;
            }
            var previousFont = Font;
            _visualStylesOverride = value;
            RefreshPortableAppearance(previousFont);
        }
    }

    internal PortableVisualStylePlatform? VisualStylePlatformOverride
    {
        get => _visualStylePlatformOverride;
        set
        {
            if (_visualStylePlatformOverride == value)
            {
                return;
            }
            var previousFont = Font;
            _visualStylePlatformOverride = value;
            RefreshPortableAppearance(previousFont);
        }
    }

    private void RefreshPortableAppearance(System.Drawing.Font previousFont)
    {
        Invalidate(invalidateChildren: true);
        if (!previousFont.Equals(Font))
        {
            OnFontChanged(EventArgs.Empty);
        }
    }

    internal bool PortableVisualStylesEnabled => VisualStylesOverride
        ?? Parent?.PortableVisualStylesEnabled
        ?? Application.UseVisualStyles;

    internal bool HasExplicitAppearanceBackColor => ShouldSerializeBackColor()
        || Parent?.HasExplicitAppearanceBackColor == true;

    internal bool HasExplicitAppearanceForeColor => ShouldSerializeForeColor()
        || Parent?.HasExplicitAppearanceForeColor == true;

    internal PortableVisualStylePlatform PortableVisualStylePlatform => VisualStylePlatformOverride
        ?? Parent?.PortableVisualStylePlatform
        ?? PortableVisualStyles.CurrentPlatform;

}
