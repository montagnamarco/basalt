using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Forms;
using Basalt.Forms.Goldens;

namespace Basalt.Forms.Tests;

/// <summary>
/// Each entry of VisualScenarios, painted here with Basalt.Forms and compared, pixel by pixel,
/// with the PNG Microsoft's own System.Windows.Forms painted on real Windows under the classic
/// theme (the goldens/visual folder the Basalt.Forms.Goldens executable writes there, copied into
/// tests/Basalt.Forms.Tests/Goldens/Visual: see tests/Basalt.Forms.Goldens/README.md).
/// </summary>
/// <remarks>
/// Outside a scenario's text rectangles, every pixel must match: GDI and Skia agree exactly on
/// how a solid fill, a 3D edge or a system-coloured border land on the bitmap. Inside a text
/// rectangle, the two rasterise glyphs differently, so a mismatch there is only reported, in the
/// written summary, never asserted. Until manifest.json is in the Goldens/Visual folder, every
/// scenario still renders, so that painting it never throws, and the comparison is skipped.
/// </remarks>
public class VisualGoldenTests
{
    private static readonly string GoldenVisualDirectory = Path.Combine(AppContext.BaseDirectory, "Goldens", "Visual");
    private static readonly string ManifestFile = Path.Combine(GoldenVisualDirectory, "manifest.json");
    private static readonly string DiffDirectory = Path.Combine(AppContext.BaseDirectory, "VisualGoldenDiffs");
    private static readonly string SummaryFile = Path.Combine(DiffDirectory, "summary.txt");
    private static readonly object SummaryLock = new();

    /// <summary>One manifest.json entry: a scenario's painted size and its text rectangles.</summary>
    private sealed record ManifestEntry(string Name, int Width, int Height, ManifestRectangle[] TextRectangles);

    /// <summary>A text rectangle in control coordinates, as plain integers so it round trips through JSON.</summary>
    private sealed record ManifestRectangle(int X, int Y, int Width, int Height);

    public static TheoryData<string> Names() => [.. VisualScenarios.All.Select(scenario => scenario.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void A_scenario_paints_pixel_for_pixel_like_windows_outside_its_text(string name)
    {
        if (!File.Exists(ManifestFile))
        {
            Assert.Skip("No recorded visual goldens yet: see tests/Basalt.Forms.Goldens/README.md.");
        }

        var manifest = LoadManifest();
        if (!manifest.TryGetValue(name, out var entry))
        {
            Assert.Fail($"manifest.json has no entry for scenario '{name}'; regenerate it from the current VisualScenarios.");
            return;
        }

        var scenario = VisualScenarios.All.Single(candidate => candidate.Name == name);
        using var control = scenario.Create();
        using var ours = new Bitmap(entry.Width, entry.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(ours, new Rectangle(0, 0, entry.Width, entry.Height));

        using var expected = new Bitmap(Path.Combine(GoldenVisualDirectory, $"{name}.png"));

        var textRectangles = entry.TextRectangles
            .Select(rectangle => new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height))
            .ToArray();

        var comparison = Compare(ours, expected, entry.Width, entry.Height, textRectangles);

        WriteSummaryLine(name, comparison.OutsideMismatches, comparison.InsideMismatches);

        if (comparison.OutsideMismatches > 0 || comparison.InsideMismatches > 0)
        {
            WriteDiffArtifacts(name, ours, expected, comparison.Diff);
        }
        else
        {
            comparison.Diff.Dispose();
        }

        Assert.Equal(0, comparison.OutsideMismatches);
    }

    private readonly record struct ComparisonResult(int OutsideMismatches, int InsideMismatches, Bitmap Diff);

    /// <summary>
    /// Counts differing pixels outside and inside the text rectangles, and paints a diff bitmap:
    /// green where the two agree, red for a mismatch outside text, yellow for one inside it.
    /// </summary>
    private static ComparisonResult Compare(Bitmap ours, Bitmap expected, int width, int height, Rectangle[] textRectangles)
    {
        var diff = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var outsideMismatches = 0;
        var insideMismatches = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var same = ours.GetPixel(x, y).ToArgb() == expected.GetPixel(x, y).ToArgb();
                if (same)
                {
                    diff.SetPixel(x, y, Color.FromArgb(32, 160, 32));
                    continue;
                }

                var insideText = Array.Exists(textRectangles, rectangle => rectangle.Contains(x, y));
                if (insideText)
                {
                    insideMismatches++;
                    diff.SetPixel(x, y, Color.Yellow);
                }
                else
                {
                    outsideMismatches++;
                    diff.SetPixel(x, y, Color.Red);
                }
            }
        }

        return new ComparisonResult(outsideMismatches, insideMismatches, diff);
    }

    private static void WriteDiffArtifacts(string name, Bitmap ours, Bitmap expected, Bitmap diff)
    {
        Directory.CreateDirectory(DiffDirectory);

        ours.Save(Path.Combine(DiffDirectory, $"{name}.ours.png"), ImageFormat.Png);
        expected.Save(Path.Combine(DiffDirectory, $"{name}.expected.png"), ImageFormat.Png);
        diff.Save(Path.Combine(DiffDirectory, $"{name}.diff.png"), ImageFormat.Png);
        diff.Dispose();
    }

    /// <summary>Appends one line per scenario; several scenarios can run in parallel, hence the lock.</summary>
    private static void WriteSummaryLine(string name, int outsideMismatches, int insideMismatches)
    {
        Directory.CreateDirectory(DiffDirectory);
        var line = $"{name}: differing-outside-text={outsideMismatches} differing-inside-text={insideMismatches}{Environment.NewLine}";

        lock (SummaryLock)
        {
            File.AppendAllText(SummaryFile, line);
        }
    }

    private static Dictionary<string, ManifestEntry> LoadManifest()
    {
        var json = File.ReadAllText(ManifestFile);
        var entries = JsonSerializer.Deserialize<ManifestEntry[]>(json)
            ?? throw new InvalidOperationException($"{ManifestFile} did not deserialize to a manifest entry array.");

        return entries.ToDictionary(entry => entry.Name);
    }
}
