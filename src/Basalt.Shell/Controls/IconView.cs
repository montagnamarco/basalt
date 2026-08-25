using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Basalt.Shell.Controls;

/// <summary>
/// Draws one of the interface icons.
///
/// A control rather than an image so the icon follows the theme: it is
/// stroked in the current foreground colour unless the kind carries a colour
/// of its own, which is what keeps a dark theme from showing black glyphs on
/// a dark panel.
/// </summary>
public sealed class IconView : Control
{
    /// <summary>The size every icon is drawn in before scaling.</summary>
    private const double DesignSize = 16;

    public static readonly StyledProperty<IconKind> KindProperty =
        AvaloniaProperty.Register<IconView, IconKind>(nameof(Kind));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<IconView, double>(nameof(IconSize), 16);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<IconView, IBrush?>(nameof(Foreground));

    static IconView()
    {
        AffectsRender<IconView>(KindProperty, IconSizeProperty, ForegroundProperty);
        AffectsMeasure<IconView>(IconSizeProperty);
    }

    public IconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>Overrides the colour; unset follows the icon or the theme.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(IconSize, IconSize);

    public override void Render(DrawingContext context)
    {
        if (IdeIcons.PathFor(Kind) is not { } geometry) return;

        var brush = Foreground
                 ?? (IdeIcons.AccentFor(Kind) is { } accent
                        ? new SolidColorBrush(accent)
                        : Brushes.Gray);

        var scale = IconSize / DesignSize;

        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale));

        // Stroked rather than filled: the paths are outlines, and a stroke of
        // just over a pixel reads clearly at the sizes these are shown at.
        context.DrawGeometry(null, new Pen(brush, 1.25), geometry);
    }

    /// <summary>
    /// The icon as a bitmap, for places that cannot take a control.
    ///
    /// The macOS system menu bar is drawn by the operating system: it takes a
    /// <see cref="Bitmap"/> and nothing else, so the same paths the managed
    /// menu strokes have to be rendered once into pixels.
    ///
    /// Cached: the menu is rebuilt whenever the language or the theme
    /// changes, and rasterising the same handful of icons each time is work
    /// for nothing.
    /// </summary>
    public static Bitmap? Rasterize(IconKind kind, IBrush? foreground = null, int size = 18)
    {
        if (kind == IconKind.None) return null;

        var key = (kind, (foreground as ISolidColorBrush)?.Color, size);

        if (Rasterized.TryGetValue(key, out var cached)) return cached;

        if (IdeIcons.PathFor(kind) is not { } geometry) return null;

        var brush = foreground
                 ?? (IdeIcons.AccentFor(kind) is { } accent
                        ? new SolidColorBrush(accent)
                        : Brushes.Gray);

        // Twice the logical size, so the icon stays sharp on a retina screen.
        var pixels = new PixelSize(size * 2, size * 2);
        var target = new RenderTargetBitmap(pixels, new Vector(192, 192));

        using (var context = target.CreateDrawingContext())
        {
            var scale = size / DesignSize;

            using var _ = context.PushTransform(Matrix.CreateScale(scale, scale));

            context.DrawGeometry(null, new Pen(brush, 1.25), geometry);
        }

        Rasterized[key] = target;

        return target;
    }

    private static readonly Dictionary<(IconKind, Color?, int), Bitmap> Rasterized = [];
}
