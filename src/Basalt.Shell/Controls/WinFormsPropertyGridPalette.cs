using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace Basalt.Shell.Controls;

/// <summary>
/// The colours of the Visual Studio style property grid and the events grid,
/// read from the IDE theme (Theme.axaml) rather than a palette of their own.
/// </summary>
/// <remarks>
/// The grid used to carry a Light and a Dark palette picked by comparing the
/// variant with <see cref="ThemeVariant.Dark"/>. A custom variant such as High
/// Contrast Dark is a distinct object that merely falls back to Dark, so the
/// comparison failed and the grid rendered the light palette on a black
/// window. Resolving the same keys every other panel uses, against the
/// control's own variant, makes every built-in theme work and keeps the grid
/// from drifting when a theme is retuned.
/// </remarks>
internal sealed class WinFormsPropertyGridPalette
{
    /// <summary>One height for property rows, category rows and event rows.</summary>
    public const double RowHeight = 22;

    /// <summary>The body text size shared with the other panels.</summary>
    public const double FontSize = 12;

    // The values Theme.axaml gives the light theme. Used only when a control is
    // not attached yet, so the first frame is already close to the final one.
    private static readonly Color FallbackBackground = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color FallbackForeground = Color.FromRgb(0x1F, 0x1F, 0x1F);

    private WinFormsPropertyGridPalette(Func<string, Color?, Color> resolve)
    {
        BackgroundColor = resolve("EditorBackground", FallbackBackground);
        ForegroundColor = resolve("EditorForeground", FallbackForeground);
        GridLineColor = resolve("PanelBorder", Color.FromRgb(0xD0, 0xD0, 0xD0));
        CategoryBackgroundColor = resolve("SideBarBackground", Color.FromRgb(0xF8, 0xF8, 0xF8));
        SelectionBackgroundColor = resolve("ListActiveSelectionBackground", Color.FromRgb(0x00, 0x60, 0xC0));
        SelectionForegroundColor = Readable(
            resolve("ListActiveSelectionForeground", Color.FromRgb(0xFF, 0xFF, 0xFF)), SelectionBackgroundColor);
        InactiveSelectionBackgroundColor = resolve("ListInactiveSelectionBackground", Color.FromRgb(0xE4, 0xE6, 0xF1));
        ReadOnlyForegroundColor = resolve("DescriptionForeground", Color.FromRgb(0x5A, 0x5A, 0x5A));
        FocusColor = resolve("FocusBorder", Color.FromRgb(0x00, 0x5F, 0xB8));
        InputBackgroundColor = resolve("InputBackground", Color.FromRgb(0xFF, 0xFF, 0xFF));
        InputBorderColor = resolve("InputBorder", Color.FromRgb(0xB0, 0xB0, 0xB0));
        ButtonBackgroundColor = resolve("ButtonSecondaryBackground", Color.FromRgb(0xDD, 0xDD, 0xDD));
        ButtonForegroundColor = resolve("ButtonSecondaryForeground", FallbackForeground);

        BackgroundBrush = new ImmutableSolidColorBrush(BackgroundColor);
        ForegroundBrush = new ImmutableSolidColorBrush(ForegroundColor);
        GridLineBrush = new ImmutableSolidColorBrush(GridLineColor);
        CategoryBackgroundBrush = new ImmutableSolidColorBrush(CategoryBackgroundColor);
        SelectionBackgroundBrush = new ImmutableSolidColorBrush(SelectionBackgroundColor);
        SelectionForegroundBrush = new ImmutableSolidColorBrush(SelectionForegroundColor);
        InactiveSelectionBackgroundBrush = new ImmutableSolidColorBrush(InactiveSelectionBackgroundColor);
        ReadOnlyForegroundBrush = new ImmutableSolidColorBrush(ReadOnlyForegroundColor);
        FocusBrush = new ImmutableSolidColorBrush(FocusColor);
        InputBackgroundBrush = new ImmutableSolidColorBrush(InputBackgroundColor);
        InputBorderBrush = new ImmutableSolidColorBrush(InputBorderColor);
        ButtonBackgroundBrush = new ImmutableSolidColorBrush(ButtonBackgroundColor);
        ButtonForegroundBrush = new ImmutableSolidColorBrush(ButtonForegroundColor);
    }

    /// <summary>
    /// The theme's selection text colour, unless it falls under 4.5:1 on the
    /// selection fill (Visual Studio Blue's white on #3399FF is 2.8:1), in which
    /// case black or white, whichever reads better, so the selected name stays legible.
    /// </summary>
    private static Color Readable(Color preferred, Color background)
    {
        if (ContrastRatio(preferred, background) >= 4.5)
            return preferred;
        var white = Color.FromRgb(0xFF, 0xFF, 0xFF);
        var black = Color.FromRgb(0, 0, 0);
        return ContrastRatio(white, background) >= ContrastRatio(black, background) ? white : black;
    }

    internal static double ContrastRatio(Color a, Color b)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var (hi, lo) = (Luminance(a), Luminance(b));
        if (hi < lo) (hi, lo) = (lo, hi);
        return (hi + 0.05) / (lo + 0.05);
    }

    public Color BackgroundColor { get; }
    public Color ForegroundColor { get; }
    public Color GridLineColor { get; }
    /// <summary>Category rows and the help pane: the side bar surface, so they read as chrome.</summary>
    public Color CategoryBackgroundColor { get; }
    /// <summary>The selected row while the grid has keyboard focus.</summary>
    public Color SelectionBackgroundColor { get; }
    public Color SelectionForegroundColor { get; }
    /// <summary>The selected row while focus is elsewhere; its text keeps the normal foreground.</summary>
    public Color InactiveSelectionBackgroundColor { get; }
    /// <summary>Read-only names and values, and hovered toolbar buttons use the secondary text token.</summary>
    public Color ReadOnlyForegroundColor { get; }
    public Color FocusColor { get; }
    public Color InputBackgroundColor { get; }
    public Color InputBorderColor { get; }
    public Color ButtonBackgroundColor { get; }
    public Color ButtonForegroundColor { get; }

    public IBrush BackgroundBrush { get; }
    public IBrush ForegroundBrush { get; }
    public IBrush GridLineBrush { get; }
    public IBrush CategoryBackgroundBrush { get; }
    public IBrush SelectionBackgroundBrush { get; }
    public IBrush SelectionForegroundBrush { get; }
    public IBrush InactiveSelectionBackgroundBrush { get; }
    public IBrush ReadOnlyForegroundBrush { get; }
    public IBrush FocusBrush { get; }
    public IBrush InputBackgroundBrush { get; }
    public IBrush InputBorderBrush { get; }
    public IBrush ButtonBackgroundBrush { get; }
    public IBrush ButtonForegroundBrush { get; }

    /// <summary>The palette of the theme <paramref name="owner"/> is currently shown in.</summary>
    public static WinFormsPropertyGridPalette For(Control owner) => new((key, fallback) =>
        owner.TryFindResource(key, owner.ActualThemeVariant, out var value) ? AsColor(value) ?? fallback!.Value
            : fallback!.Value);

    private static Color? AsColor(object? value) => value switch
    {
        Color color => color,
        ISolidColorBrush brush => brush.Color,
        _ => null,
    };

    /// <summary>Whether another palette would paint exactly the same.</summary>
    public bool SameAs(WinFormsPropertyGridPalette other) =>
        BackgroundColor == other.BackgroundColor && ForegroundColor == other.ForegroundColor
        && GridLineColor == other.GridLineColor && CategoryBackgroundColor == other.CategoryBackgroundColor
        && SelectionBackgroundColor == other.SelectionBackgroundColor
        && SelectionForegroundColor == other.SelectionForegroundColor
        && InactiveSelectionBackgroundColor == other.InactiveSelectionBackgroundColor
        && ReadOnlyForegroundColor == other.ReadOnlyForegroundColor && FocusColor == other.FocusColor
        && InputBackgroundColor == other.InputBackgroundColor && InputBorderColor == other.InputBorderColor
        && ButtonBackgroundColor == other.ButtonBackgroundColor && ButtonForegroundColor == other.ButtonForegroundColor;

    // What this palette has already written to a root, so a rebuild that finds
    // the same palette there writes nothing.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, WinFormsPropertyGridPalette>
        Installed = new();

    /// <summary>
    /// Writes the resources every editor and flat button in a grid shares to the
    /// grid's root, once per palette. Fluent paints the hovered, pressed and
    /// focused states from resource keys, which a local Background does not
    /// reach; they used to be written to each row's controls (10 to 16 entries a
    /// row, on every rebuild) and are now looked up through the tree from here.
    /// </summary>
    public void InstallOn(Control root)
    {
        if (Installed.TryGetValue(root, out var current) && ReferenceEquals(current, this))
            return;

        var resources = root.Resources;
        var transparent = Brushes.Transparent;

        foreach (var key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver",
            "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
            "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused",
        })
            resources[key] = transparent;

        resources["TextControlBackgroundFocused"] = InputBackgroundBrush;
        resources["ComboBoxDropDownBackground"] = InputBackgroundBrush;
        resources["ComboBoxDropDownBorderBrush"] = InputBorderBrush;

        foreach (var key in new[]
        {
            "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused",
            "ComboBoxForeground", "ComboBoxForegroundPointerOver", "ComboBoxForegroundPressed",
            "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed", "ComboBoxDropDownGlyphForeground",
            "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
        })
            resources[key] = ForegroundBrush;

        resources["ButtonBackgroundPointerOver"] = InactiveSelectionBackgroundBrush;
        resources["ButtonBackgroundPressed"] = InactiveSelectionBackgroundBrush;
        foreach (var key in new[] { "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            resources[key] = transparent;

        Installed.AddOrUpdate(root, this);
    }

    /// <summary>
    /// Paints a flat button: its own fill and text, and the hover and pressed
    /// states from the shared resources <see cref="InstallOn"/> put on the root
    /// the button sits under. Only what differs per button is written here.
    /// </summary>
    public void StyleButton(Button button, IBrush background, IBrush? foreground = null)
    {
        button.Background = background;
        button.Foreground = foreground ?? ForegroundBrush;
        button.Resources["ButtonBackground"] = background;

        // The common case inherits the shared foreground entries.
        if (foreground is null) return;

        button.Resources["ButtonForeground"] = foreground;
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonForegroundPressed"] = foreground;
    }

    /// <summary>Makes the object selector combo above the grid follow the palette.</summary>
    public void StyleSelector(ComboBox selector)
    {
        selector.Background = InputBackgroundBrush;
        selector.Foreground = ForegroundBrush;
        selector.BorderBrush = InputBorderBrush;
        foreach (var key in new[]
        {
            "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
            "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused", "ComboBoxDropDownBackground",
        })
            selector.Resources[key] = InputBackgroundBrush;
        foreach (var key in new[]
        {
            "ComboBoxForeground", "ComboBoxForegroundPointerOver", "ComboBoxForegroundPressed",
            "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed", "ComboBoxDropDownGlyphForeground",
        })
            selector.Resources[key] = ForegroundBrush;
        foreach (var key in new[]
        {
            "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed",
            "ComboBoxBorderBrushFocused", "ComboBoxDropDownBorderBrush",
        })
            selector.Resources[key] = InputBorderBrush;
    }
}
