namespace Basalt.Tests;

/// <summary>
/// The MSBuild props the packages put into a user's project.
/// </summary>
/// <remarks>
/// They decide which files reach the generators, and a wrong glob fails
/// quietly: the project builds, and the pages are simply not in it.
/// </remarks>
public class PackagePropsTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basalt.slnx")))
                directory = directory.Parent;

            Assert.NotNull(directory);

            return directory.FullName;
        }
    }

    [Theory]
    [InlineData("src/Basalt.Razor.Vb.Generator/build/Basalt.Razor.Vb.props")]
    [InlineData("src/Basalt.Razor.Vb.AspNetCore/build/Basalt.Razor.Vb.AspNetCore.props")]
    public void HandsEveryPageToTheGeneratorAndNoVisualBasic6Project(string props)
    {
        // The glob was "**/*.vbp": the extension of a Visual Basic 6 project,
        // not of a page. The page generator only takes .vbpage.
        var text = Flattened(Path.Combine(RepositoryRoot, props));

        Assert.Contains("Include=\"**/*.vbpage\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Include=\"**/*.vbp\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A project referencing both packages — the MVC, Razor Pages and Pages
    /// templates do — collects every template exactly once, with its
    /// project-relative path. Each package's props imports the shared
    /// collection only when it is the one that claimed it; a condition that
    /// compared two properties inside the collection instead saw both
    /// importers' final values and collected nothing.
    /// </summary>
    [Fact]
    public void BothPackagesTogetherCollectEveryTemplateOnceWithItsRelativePath()
    {
        var root = Directory.CreateTempSubdirectory("basalt-props-both-").FullName;
        try
        {
            // The NuGet layout: each package's build/ folder holds its own
            // props beside its own copy of the shared collection.
            var templates = Path.Combine(RepositoryRoot, "src", "Basalt.Razor.Vb.Generator", "build", "Basalt.Razor.Vb.Templates.props");
            var generatorBuild = Directory.CreateDirectory(Path.Combine(root, "packages", "basalt.razor.vb", "build")).FullName;
            var aspNetCoreBuild = Directory.CreateDirectory(Path.Combine(root, "packages", "basalt.razor.vb.aspnetcore", "build")).FullName;
            File.Copy(Path.Combine(RepositoryRoot, "src", "Basalt.Razor.Vb.Generator", "build", "Basalt.Razor.Vb.props"),
                Path.Combine(generatorBuild, "Basalt.Razor.Vb.props"));
            File.Copy(templates, Path.Combine(generatorBuild, "Basalt.Razor.Vb.Templates.props"));
            File.Copy(Path.Combine(RepositoryRoot, "src", "Basalt.Razor.Vb.AspNetCore", "build", "Basalt.Razor.Vb.AspNetCore.props"),
                Path.Combine(aspNetCoreBuild, "Basalt.Razor.Vb.AspNetCore.props"));
            File.Copy(templates, Path.Combine(aspNetCoreBuild, "Basalt.Razor.Vb.Templates.props"));

            var project = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            Directory.CreateDirectory(Path.Combine(project, "Views", "Home"));
            File.WriteAllText(Path.Combine(project, "Views", "Home", "Index.vbhtml"), "<p>Hi</p>");
            File.WriteAllText(Path.Combine(project, "App.proj"), $"""
                <Project>
                  <Import Project="{Path.Combine(generatorBuild, "Basalt.Razor.Vb.props")}" />
                  <Import Project="{Path.Combine(aspNetCoreBuild, "Basalt.Razor.Vb.AspNetCore.props")}" />
                  <Target Name="Build" />
                </Project>
                """);

            var start = new System.Diagnostics.ProcessStartInfo("dotnet",
                ["msbuild", Path.Combine(project, "App.proj"), "-getItem:AdditionalFiles", "-nologo"])
            {
                RedirectStandardOutput = true,
                WorkingDirectory = project,
            };
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, output);

            using var json = System.Text.Json.JsonDocument.Parse(output);
            var items = json.RootElement.GetProperty("Items").GetProperty("AdditionalFiles").EnumerateArray().ToList();

            var index = Assert.Single(items, item => item.GetProperty("Identity").GetString()!.EndsWith("Index.vbhtml", StringComparison.Ordinal));
            Assert.Equal("Views/Home/Index.vbhtml",
                index.GetProperty("BasaltRelativePath").GetString()!.Replace('\\', '/'));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A .props file's own text, plus that of every sibling file it imports.
    /// </summary>
    /// <remarks>
    /// Basalt.Razor.Vb.props and Basalt.Razor.Vb.AspNetCore.props both import
    /// Basalt.Razor.Vb.Templates.props rather than repeating what it collects
    /// — see that file for why — so a glob this test looks for can live in
    /// either place.
    /// </remarks>
    private static string Flattened(string path)
    {
        var text = File.ReadAllText(path);
        var directory = Path.GetDirectoryName(path)!;

        // The physical file sits only under the generator's build/ folder in
        // this checkout: Basalt.Razor.Vb.AspNetCore.props imports it under
        // the same name, but only NuGet, packing both into their own build/
        // folder (see each .csproj), puts a copy beside it to resolve.
        var generatorBuild = Path.Combine(
            RepositoryRoot, "src", "Basalt.Razor.Vb.Generator", "build");

        foreach (System.Text.RegularExpressions.Match match in
            System.Text.RegularExpressions.Regex.Matches(text, "<Import Project=\"([^\"]+)\"[^>]*/>"))
        {
            var imported = Path.Combine(directory, match.Groups[1].Value);
            if (!File.Exists(imported)) imported = Path.Combine(generatorBuild, match.Groups[1].Value);

            text += Environment.NewLine + File.ReadAllText(imported);
        }

        return text;
    }
}
