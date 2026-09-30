using System.Reflection;
using System.Xml.Linq;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Basalt.Drawing names its own SkiaSharp so that it works without Avalonia.
/// An application using both loads one native Skia library, so the two
/// versions have to be the same one.
/// </summary>
public class SkiaVersionTests
{
    [Fact]
    public void SkiaSharp_is_the_version_Avalonia_depends_on()
    {
        var avalonia = Metadata("AvaloniaVersion");
        var ours = Metadata("SkiaSharpVersion");
        var nuspec = Path.Combine(Metadata("NuGetPackageRoot"), "avalonia.skia", avalonia, "avalonia.skia.nuspec");

        Assert.True(File.Exists(nuspec), $"Avalonia.Skia {avalonia} is not in the NuGet cache at {nuspec}: build the solution first.");

        var theirs = XDocument.Load(nuspec).Descendants()
            .Where(element => element.Name.LocalName == "dependency" && (string?)element.Attribute("id") == "SkiaSharp")
            .Select(element => (string)element.Attribute("version")!)
            .Distinct()
            .ToList();

        Assert.Equal([ours], theirs);
    }

    private static string Metadata(string key) =>
        typeof(SkiaVersionTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == key).Value!;
}
