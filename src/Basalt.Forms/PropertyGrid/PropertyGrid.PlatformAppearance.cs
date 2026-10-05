using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public partial class PropertyGrid
{
    /// <summary>
    /// Every colour below follows the same rule: an explicitly-set value wins,
    /// otherwise the portable palette supplies it when portable visual styles
    /// are on, otherwise a <see cref="SystemColors"/> fallback. Centralising
    /// that here keeps each property itself to a getter and a setter line.
    /// </summary>
    private Color ResolvePortableColor(Color? explicitColor, Func<PortableVisualStylePalette, Color> fromPalette, Color systemFallback) =>
        explicitColor ?? (PortableVisualStylesEnabled ? fromPalette(PortableVisualStyles.Palette(PortableVisualStylePlatform)) : systemFallback);

    /// <summary>Stores an explicitly-set colour and invalidates, but only when the value actually changes.</summary>
    private void SetPortableColor(ref Color? backing, Color value)
    {
        if (backing == value)
        {
            return;
        }

        backing = value;
        Invalidate();
    }

    private Color? _portableLineColor;

    public Color LineColor
    {
        get => ResolvePortableColor(_portableLineColor, palette => palette.Border, SystemColors.InactiveBorder);
        set => SetPortableColor(ref _portableLineColor, value);
    }

    private Color? _portableCategoryForeColor;

    public Color CategoryForeColor
    {
        get => ResolvePortableColor(_portableCategoryForeColor, palette => palette.Text, SystemColors.ControlText);
        set => SetPortableColor(ref _portableCategoryForeColor, value);
    }

    private Color? _portableCategorySplitterColor;

    public Color CategorySplitterColor
    {
        get => ResolvePortableColor(_portableCategorySplitterColor, palette => palette.Surface, SystemColors.Control);
        set => SetPortableColor(ref _portableCategorySplitterColor, value);
    }

    private Color? _portableDisabledItemForeColor;

    public Color DisabledItemForeColor
    {
        get => ResolvePortableColor(_portableDisabledItemForeColor, palette => palette.DisabledText, SystemColors.GrayText);
        set => SetPortableColor(ref _portableDisabledItemForeColor, value);
    }

    private Color? _portableHelpBackColor;

    public Color HelpBackColor
    {
        get => ResolvePortableColor(_portableHelpBackColor, palette => palette.Surface, SystemColors.Control);
        set => SetPortableColor(ref _portableHelpBackColor, value);
    }

    private Color? _portableHelpBorderColor;

    public Color HelpBorderColor
    {
        get => ResolvePortableColor(_portableHelpBorderColor, palette => palette.Border, SystemColors.ControlDark);
        set => SetPortableColor(ref _portableHelpBorderColor, value);
    }

    private Color? _portableHelpForeColor;

    public Color HelpForeColor
    {
        get => ResolvePortableColor(_portableHelpForeColor, palette => palette.Text, SystemColors.ControlText);
        set => SetPortableColor(ref _portableHelpForeColor, value);
    }

    private Color? _portableSelectedItemWithFocusBackColor;

    public Color SelectedItemWithFocusBackColor
    {
        get => ResolvePortableColor(_portableSelectedItemWithFocusBackColor, palette => palette.Selection, SystemColors.Highlight);
        set => SetPortableColor(ref _portableSelectedItemWithFocusBackColor, value);
    }

    private Color? _portableSelectedItemWithFocusForeColor;

    public Color SelectedItemWithFocusForeColor
    {
        get => ResolvePortableColor(_portableSelectedItemWithFocusForeColor, palette => palette.SelectedText, SystemColors.HighlightText);
        set => SetPortableColor(ref _portableSelectedItemWithFocusForeColor, value);
    }

    private Color? _portableViewBackColor;

    public Color ViewBackColor
    {
        get => ResolvePortableColor(_portableViewBackColor, palette => palette.Field, SystemColors.Window);
        set => SetPortableColor(ref _portableViewBackColor, value);
    }

    private Color? _portableViewBorderColor;

    public Color ViewBorderColor
    {
        get => ResolvePortableColor(_portableViewBorderColor, palette => palette.Border, SystemColors.ControlDark);
        set => SetPortableColor(ref _portableViewBorderColor, value);
    }

    private Color? _portableViewForeColor;

    public Color ViewForeColor
    {
        get => ResolvePortableColor(_portableViewForeColor, palette => palette.Text, SystemColors.WindowText);
        set => SetPortableColor(ref _portableViewForeColor, value);
    }
}
