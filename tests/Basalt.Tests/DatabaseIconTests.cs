using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The icons of the Database Explorer's tree: each node kind has a drawing of its own, the four engines are
/// told apart, and one colour reads on both a light and a dark panel.
/// </summary>
public sealed class DatabaseIconTests
{
    private static readonly IconKind[] ConnectionKinds =
        [IconKind.DatabaseSqlite, IconKind.DatabaseSqlServer, IconKind.DatabasePostgreSql, IconKind.DatabaseMySql];

    private static readonly IconKind[] TreeKinds =
    [
        .. ConnectionKinds,
        IconKind.FolderTables, IconKind.FolderViews,
        IconKind.Table, IconKind.View, IconKind.Column, IconKind.PrimaryKeyColumn, IconKind.ForeignKeyColumn,
        IconKind.Index,
    ];

    [Fact]
    public void EveryNodeKindHasADrawingOfItsOwn()
    {
        foreach (var kind in TreeKinds) Assert.False(string.IsNullOrEmpty(IdeIcons.PathDataFor(kind)), kind + " has no drawing.");

        // Two kinds with one drawing could not be told apart in the tree, which is what the icons are for.
        var duplicated = TreeKinds.GroupBy(IdeIcons.PathDataFor).Where(g => g.Count() > 1)
            .Select(g => string.Join(" = ", g)).ToList();

        Assert.True(duplicated.Count == 0, "Icons drawn alike: " + string.Join("; ", duplicated));
    }

    [Fact]
    public void TheFourEnginesAreToldApartAndShareTheCylinder()
    {
        var drawings = ConnectionKinds.Select(IdeIcons.PathDataFor).ToList();

        Assert.Equal(4, drawings.Distinct().Count());

        // Same cylinder, different mark: they read as connections first and as engines second.
        var common = drawings[0]!.Split(' ', 2)[0];

        foreach (var drawing in drawings) Assert.StartsWith(common, drawing);
    }

    [Fact]
    public void EveryColourReadsOnALightAndOnADarkPanel()
    {
        // WCAG 1.4.11: 3:1 for a graphic. The solution tree's folder is the same amber on both themes; these
        // icons are held to the measure instead of to taste.
        foreach (var kind in TreeKinds)
        {
            var accent = IdeIcons.AccentFor(kind);

            Assert.True(accent is not null, kind + " has no colour, so it would be drawn grey.");

            Assert.True(Contrast(accent.Value, Colors.White) >= 3.0, $"{kind} is too pale on a light panel.");
            Assert.True(Contrast(accent.Value, Color.FromRgb(0x1E, 0x1E, 0x1E)) >= 3.0, $"{kind} is too dark on a dark panel.");
        }
    }

    [AvaloniaFact]
    public void EveryDrawingParsesAndPutsInkOnTheCanvas()
    {
        foreach (var kind in TreeKinds)
        {
            Assert.NotNull(IdeIcons.PathFor(kind));

            var bounds = IdeIcons.PathFor(kind)!.Bounds;

            Assert.True(bounds.Width >= 5 && bounds.Height >= 5, $"{kind} is only {bounds.Width:0.0} by {bounds.Height:0.0}.");
            Assert.True(bounds.Right <= 16 && bounds.Bottom <= 16 && bounds.Left >= 0 && bounds.Top >= 0, $"{kind} leaves the 16 by 16 box.");

            Assert.True(InkOf(kind) > 20, $"{kind} rasterises to nothing.");
        }
    }

    [AvaloniaFact]
    public void ConnectionMarksDifferInTheirPixels()
    {
        // The mark sits in the lower right of the cylinder; measured in pixels, because path strings that
        // differ could still draw alike once the stroke is applied.
        var marks = ConnectionKinds.Select(kind => MarkPixels(kind)).ToList();

        for (var a = 0; a < marks.Count; a++)
            for (var b = a + 1; b < marks.Count; b++)
                Assert.NotEqual(marks[a], marks[b]);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;

            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var (high, low) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));

        return (high + 0.05) / (low + 0.05);
    }

    private static byte[] Pixels(IconKind kind)
    {
        var bitmap = (RenderTargetBitmap)IconView.Rasterize(kind, size: 16)!;
        var pixels = new byte[32 * 32 * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 32, 32), handle.AddrOfPinnedObject(), pixels.Length, 32 * 4);
        }
        finally
        {
            handle.Free();
        }

        return pixels;
    }

    private static int InkOf(IconKind kind)
    {
        var pixels = Pixels(kind);

        return Enumerable.Range(0, 32 * 32).Count(i => pixels[i * 4 + 3] > 128);
    }

    /// <summary>The opaque pixels in the lower right quarter, as a string of 0 and 1.</summary>
    private static string MarkPixels(IconKind kind)
    {
        var pixels = Pixels(kind);
        var text = new System.Text.StringBuilder();

        for (var y = 18; y < 32; y++)
            for (var x = 18; x < 32; x++)
                text.Append(pixels[(y * 32 + x) * 4 + 3] > 128 ? '1' : '0');

        return text.ToString();
    }
}
