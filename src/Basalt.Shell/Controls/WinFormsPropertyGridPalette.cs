using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace Basalt.Shell.Controls;

/// <summary>The property and event grids share the same light and dark surfaces.</summary>
internal sealed class WinFormsPropertyGridPalette
{
    public static WinFormsPropertyGridPalette Light { get; } = new(
        Color.FromRgb(255, 255, 255),
        Color.FromRgb(0, 0, 0),
        Color.FromRgb(210, 210, 210),
        Color.FromRgb(238, 238, 238),
        Color.FromRgb(0, 120, 215),
        Color.FromRgb(255, 255, 255),
        Color.FromRgb(128, 128, 128));

    public static WinFormsPropertyGridPalette Dark { get; } = new(
        Color.FromRgb(37, 37, 38),
        Color.FromRgb(241, 241, 241),
        Color.FromRgb(63, 63, 70),
        Color.FromRgb(61, 61, 64),
        Color.FromRgb(0, 122, 204),
        Color.FromRgb(255, 255, 255),
        Color.FromRgb(153, 153, 153));

    private WinFormsPropertyGridPalette(
        Color background,
        Color foreground,
        Color gridLine,
        Color categoryBackground,
        Color selectionBackground,
        Color selectionForeground,
        Color readOnlyForeground)
    {
        BackgroundColor = background;
        ForegroundColor = foreground;
        GridLineColor = gridLine;
        CategoryBackgroundColor = categoryBackground;
        SelectionBackgroundColor = selectionBackground;
        SelectionForegroundColor = selectionForeground;
        ReadOnlyForegroundColor = readOnlyForeground;

        BackgroundBrush = new ImmutableSolidColorBrush(background);
        ForegroundBrush = new ImmutableSolidColorBrush(foreground);
        GridLineBrush = new ImmutableSolidColorBrush(gridLine);
        CategoryBackgroundBrush = new ImmutableSolidColorBrush(categoryBackground);
        SelectionBackgroundBrush = new ImmutableSolidColorBrush(selectionBackground);
        SelectionForegroundBrush = new ImmutableSolidColorBrush(selectionForeground);
        ReadOnlyForegroundBrush = new ImmutableSolidColorBrush(readOnlyForeground);
    }

    public Color BackgroundColor { get; }
    public Color ForegroundColor { get; }
    public Color GridLineColor { get; }
    public Color CategoryBackgroundColor { get; }
    public Color SelectionBackgroundColor { get; }
    public Color SelectionForegroundColor { get; }
    public Color ReadOnlyForegroundColor { get; }

    public IBrush BackgroundBrush { get; }
    public IBrush ForegroundBrush { get; }
    public IBrush GridLineBrush { get; }
    public IBrush CategoryBackgroundBrush { get; }
    public IBrush SelectionBackgroundBrush { get; }
    public IBrush SelectionForegroundBrush { get; }
    public IBrush ReadOnlyForegroundBrush { get; }

    public static WinFormsPropertyGridPalette For(ThemeVariant variant) =>
        variant == ThemeVariant.Dark ? Dark : Light;
}
