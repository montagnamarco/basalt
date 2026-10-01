using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Basalt.Drawing.Goldens;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("The reference images come from Windows' own GDI+: run this on Windows.");
    return 1;
}

var output = Path.Combine(AppContext.BaseDirectory, "goldens");
Directory.CreateDirectory(output);

foreach (var scenario in Scenarios.All)
{
    using var bitmap = new Bitmap(scenario.Width, scenario.Height, PixelFormat.Format32bppArgb);

    using (var graphics = Graphics.FromImage(bitmap))
    {
        scenario.Draw(graphics);
    }

    bitmap.Save(Path.Combine(output, scenario.Name + ".png"), ImageFormat.Png);
    Console.WriteLine(scenario.Name);
}

var measurements = Scenarios.Measure();
measurements["environment"] = new Dictionary<string, object>
{
    ["os"] = RuntimeInformation.OSDescription,
    ["framework"] = RuntimeInformation.FrameworkDescription,
    ["generated"] = DateTime.UtcNow.ToString("o"),
};

File.WriteAllText(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Written to {output}");

return 0;
