using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Avalonia.Platform;

namespace Basalt.Tests;

/// <summary>
/// The application icon, in the forms each platform wants.
///
/// A missing or malformed resource shows up only when a window opens, and
/// the window opening is the one thing a headless test suite does not do by
/// accident. These load the bytes and check the containers are real.
/// </summary>
public class ApplicationIconTests
{
    private static byte[] Resource(string name)
    {
        using var stream = AssetLoader.Open(
            new Uri($"avares://Basalt.Shell/Assets/{name}"));

        using var memory = new MemoryStream();

        stream.CopyTo(memory);

        return memory.ToArray();
    }

    [AvaloniaFact]
    public void CarriesTheVectorSourceInTheAssembly()
    {
        var svg = System.Text.Encoding.UTF8.GetString(Resource("basalt.svg"));

        // The same basalt columns the IDE draws for IconKind.Application: if
        // the two drift, the dock and the menu show different icons.
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("hexagonal", svg, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void CarriesAPngForLinux()
    {
        var png = Resource("basalt.png");

        // The PNG signature.
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
    }

    [AvaloniaFact]
    public void CarriesAnIcoForWindows()
    {
        var ico = Resource("basalt.ico");

        // ICONDIR: reserved 0, type 1, then the count.
        Assert.Equal(0, ico[0] | ico[1]);
        Assert.Equal(1, ico[2] | (ico[3] << 8));

        var count = ico[4] | (ico[5] << 8);

        // Several sizes, so Windows can pick one rather than scaling.
        Assert.True(count >= 5, $"the .ico holds only {count} images");
    }

    [AvaloniaFact]
    public void TheIcoHoldsTheSmallestSizeWindowsAsksFor()
    {
        var ico = Resource("basalt.ico");
        var count = ico[4] | (ico[5] << 8);

        var widths = new List<int>();

        for (var i = 0; i < count; i++)
        {
            // 0 means 256 in this format.
            var width = ico[6 + i * 16];

            widths.Add(width == 0 ? 256 : width);
        }

        Assert.Contains(16, widths);
        Assert.Contains(256, widths);
    }

    [Fact]
    public void TheMacIconIsBesideTheAssembly()
    {
        // Copied to the output rather than embedded: a bundle carries it as
        // a file, named by Info.plist.
        var icns = Path.Combine(AppContext.BaseDirectory, "Assets", "basalt.icns");

        Assert.True(File.Exists(icns), $"{icns} was not copied to the output.");

        var header = new byte[4];

        using (var stream = File.OpenRead(icns))
            Assert.Equal(4, stream.Read(header, 0, 4));

        Assert.Equal("icns", System.Text.Encoding.ASCII.GetString(header));
    }

    [AvaloniaFact]
    public void TheDrawnIconIsAHoneycombWhoseCellsStayApart()
    {
        // Seven hexagonal cells, the middle one and the ring around it, with a
        // gap between them: drawn without one, at sixteen pixels the ring
        // runs into a single grey blot.
        var geometry = IdeIcons.PathFor(IconKind.Application)!;

        Assert.True(geometry.FillContains(new Avalonia.Point(8, 8)), "no middle cell");
        Assert.True(geometry.FillContains(new Avalonia.Point(12.64, 8)), "no cell to its right");
        Assert.True(geometry.FillContains(new Avalonia.Point(5.68, 12)), "no cell below left");

        // Between the middle cell and the one to its right.
        Assert.False(geometry.FillContains(new Avalonia.Point(10.32, 8)), "the cells have merged into one shape");
    }

    [AvaloniaFact]
    public void TheGapBetweenCellsSurvivesRasterisingAtSixteenPixels()
    {
        // Read from the pixels, as they are drawn for the macOS menu: the
        // geometry can keep its cells apart while the rendering fills the gap.
        var bitmap = (Avalonia.Media.Imaging.RenderTargetBitmap)IconView.Rasterize(IconKind.Application, size: 16)!;

        // Rasterised at twice the size, for a retina screen.
        var pixels = new byte[32 * 32 * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, 32, 32), handle.AddrOfPinnedObject(), pixels.Length, 32 * 4);
        }
        finally
        {
            handle.Free();
        }

        byte Alpha(int x, int y) => pixels[(y * 32 + x) * 4 + 3];

        Assert.True(Alpha(16, 16) > 200, "the middle cell is not drawn");
        Assert.True(Alpha(25, 16) > 200, "the cell to its right is not drawn");
        Assert.True(Alpha(20, 16) < 60, $"the gap between them is filled (alpha {Alpha(20, 16)})");
    }

    [AvaloniaFact]
    public void TheDrawnIconFillsItsBox()
    {
        // A path scaled to nothing also "exists": the volcano it replaced was
        // checked only for being non-null, and an icon that draws a speck in
        // the corner passes that.
        var bounds = IdeIcons.PathFor(IconKind.Application)!.Bounds;

        Assert.True(bounds.Width >= 8, $"only {bounds.Width:0.0} wide");
        Assert.True(bounds.Height >= 8, $"only {bounds.Height:0.0} tall");
        Assert.True(bounds.Right <= 16 && bounds.Bottom <= 16, "it overflows the 16 by 16 box");
    }
}
