using System.Drawing;
using System.Drawing.Imaging;
using Basalt.Drawing.Goldens;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Each scenario drawn here and by Windows' own GDI+ (the images in Goldens,
/// made by the Basalt.Drawing.Goldens executable on Windows), compared pixel by
/// pixel.
/// </summary>
/// <remarks>
/// Until the images from Windows are in the Goldens folder, each scenario is
/// still drawn, so that none of them throws, and the comparison is skipped.
/// </remarks>
public class GoldenTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Goldens");

    public static TheoryData<string> Names() => [.. Scenarios.All.Select(scenario => scenario.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void A_scenario_draws_as_GDI_plus_draws_it(string name)
    {
        var scenario = Scenarios.All.Single(candidate => candidate.Name == name);

        using var ours = new Bitmap(scenario.Width, scenario.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(ours))
        {
            scenario.Draw(graphics);
        }

        var reference = Path.Combine(Folder, name + ".png");
        if (!File.Exists(reference)) Assert.Skip("No reference image from Windows yet: see tests/Basalt.Drawing.Goldens/README.md.");

        using var windows = new Bitmap(reference);
        Assert.Equal(windows.Size, ours.Size);

        var (differing, worst) = Compare(windows, ours);
        var share = (double)differing / (ours.Width * ours.Height);

        Assert.True(
            share <= 0.02,
            $"{differing} of {ours.Width * ours.Height} pixels differ from Windows ({share:P1}), the worst by {worst} levels.");
    }

    /// <summary>Pixels whose colour differs by more than a few levels in any channel, and the largest difference.</summary>
    private static (int Differing, int Worst) Compare(Bitmap expected, Bitmap actual)
    {
        var differing = 0;
        var worst = 0;

        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var a = expected.GetPixel(x, y);
                var b = actual.GetPixel(x, y);
                var difference = Math.Max(Math.Max(Math.Abs(a.A - b.A), Math.Abs(a.R - b.R)), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

                // Fully transparent pixels count as equal whatever colour they carry.
                if (a.A == 0 && b.A == 0) difference = 0;

                worst = Math.Max(worst, difference);
                if (difference > 8) differing++;
            }
        }

        return (differing, worst);
    }
}
