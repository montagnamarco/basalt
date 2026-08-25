using Avalonia.Headless.XUnit;
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

        // The same volcano the IDE draws for IconKind.Application: if the two
        // drift, the dock and the menu show different icons.
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("crater", svg, StringComparison.Ordinal);
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
}
