using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Basalt.Forms.Goldens;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("The reference behaviour comes from Windows' own System.Windows.Forms: run this on Windows.");
    return 1;
}

// The classic (Windows 2000) theme, not a visual-styles one: Basalt.Forms repaints the classic
// look, so the two sides can only agree pixel for pixel while Windows itself paints that way.
// EnableVisualStyles is never called.
Application.VisualStyleState = VisualStyleState.NoneEnabled;

var output = Path.Combine(AppContext.BaseDirectory, "goldens");
Directory.CreateDirectory(output);

RecordVisualScenarios(output);

var behaviour = new Dictionary<string, IReadOnlyList<string>>();

foreach (var scenario in Scenarios.All)
{
    try
    {
        behaviour[scenario.Name] = scenario.Run();
    }
    catch (Exception exception)
    {
        // A scenario that throws on real Windows is still a result: the test compares the
        // exception's type the same way it compares any other recorded line.
        behaviour[scenario.Name] = [$"throws {exception.GetType().Name}"];
    }

    Console.WriteLine(scenario.Name);
}

File.WriteAllText(
    Path.Combine(output, "behaviour.json"),
    JsonSerializer.Serialize(behaviour, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Written to {output}");

return 0;

/// <summary>
/// Renders every VisualScenarios entry with classic-theme System.Windows.Forms and writes, under
/// <paramref name="outputRoot"/>/visual, one PNG per scenario plus a manifest.json the tests read
/// to know each PNG's size and which of its rectangles hold rasterised text.
/// </summary>
static void RecordVisualScenarios(string outputRoot)
{
    var visualOutput = Path.Combine(outputRoot, "visual");
    Directory.CreateDirectory(visualOutput);

    var manifest = new List<VisualManifestEntry>();

    foreach (var scenario in VisualScenarios.All)
    {
        using var control = scenario.Create();
        using var bitmap = new Bitmap(scenario.Size.Width, scenario.Size.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, scenario.Size));
        bitmap.Save(Path.Combine(visualOutput, $"{scenario.Name}.png"), ImageFormat.Png);

        var textRectangles = scenario.TextRectangles
            .Select(rectangle => new VisualManifestRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height))
            .ToArray();
        manifest.Add(new VisualManifestEntry(scenario.Name, scenario.Size.Width, scenario.Size.Height, textRectangles));

        Console.WriteLine($"visual/{scenario.Name}");
    }

    File.WriteAllText(
        Path.Combine(visualOutput, "manifest.json"),
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>One VisualScenarios entry, as manifest.json records it: no Func, no Control, just data.</summary>
internal sealed record VisualManifestEntry(string Name, int Width, int Height, VisualManifestRectangle[] TextRectangles);

/// <summary>A text rectangle in control coordinates, plain enough to round trip through JSON.</summary>
internal sealed record VisualManifestRectangle(int X, int Y, int Width, int Height);
