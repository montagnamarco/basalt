using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// What the solution explorer shows.
///
/// The tree used to list only source files and XAML, so a web project appeared
/// to hold three files out of nine: views, configuration and static content
/// were dropped without a trace. Anything the project carries is shown.
/// </summary>
public sealed class SolutionExplorerContentTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-explorer", Guid.NewGuid().ToString("N"));

    public SolutionExplorerContentTests() => Directory.CreateDirectory(_root);

    /// <summary>Builds a project directory holding the given relative files.</summary>
    private async Task<string> CreateProjectAsync(string projectFile, params string[] files)
    {
        var projectPath = Path.Combine(_root, projectFile);

        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        foreach (var file in files)
        {
            var full = Path.Combine(_root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, "content");
        }

        return projectPath;
    }

    private static IEnumerable<SolutionTreeNode> Flatten(IEnumerable<SolutionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    private async Task<List<SolutionTreeNode>> LoadAsync(string projectFile, params string[] files)
    {
        var projectPath = await CreateProjectAsync(projectFile, files);

        var explorer = new SolutionExplorerViewModel();
        explorer.Load(projectPath);

        return Flatten(explorer.Roots).ToList();
    }

    [Fact]
    public async Task ShowsRazorViewsForBothLanguages()
    {
        var nodes = await LoadAsync("Web.csproj",
            "Views/Home/Index.cshtml",
            "Views/Home/About.vbhtml");

        Assert.Contains(nodes, n => n.Name == "Index.cshtml" && n.Kind == NodeKind.RazorFile);
        Assert.Contains(nodes, n => n.Name == "About.vbhtml" && n.Kind == NodeKind.RazorFile);
    }

    [Fact]
    public async Task ShowsTheFoldersThatHoldViews()
    {
        // A folder is only kept when it has children; views must count.
        var nodes = await LoadAsync("Web.csproj", "Views/Home/Index.cshtml");

        Assert.Contains(nodes, n => n.Name == "Views" && n.Kind == NodeKind.Folder);
        Assert.Contains(nodes, n => n.Name == "Home" && n.Kind == NodeKind.Folder);
    }

    [Fact]
    public async Task ShowsConfigurationFiles()
    {
        var nodes = await LoadAsync("Web.csproj",
            "appsettings.json",
            "Properties/launchSettings.json");

        Assert.Contains(nodes, n => n.Name == "appsettings.json" && n.Kind == NodeKind.ConfigFile);
        Assert.Contains(nodes, n => n.Name == "launchSettings.json" && n.Kind == NodeKind.ConfigFile);
    }

    [Fact]
    public async Task ShowsStaticWebContent()
    {
        var nodes = await LoadAsync("Web.csproj",
            "wwwroot/site.css",
            "wwwroot/app.js",
            "wwwroot/index.html");

        Assert.Contains(nodes, n => n.Name == "site.css" && n.Kind == NodeKind.WebAsset);
        Assert.Contains(nodes, n => n.Name == "app.js" && n.Kind == NodeKind.WebAsset);
        Assert.Contains(nodes, n => n.Name == "index.html" && n.Kind == NodeKind.WebAsset);
    }

    [Fact]
    public async Task ShowsFilesItDoesNotRecognise()
    {
        // Hiding an unknown file leaves the user unable to tell it is there.
        var nodes = await LoadAsync("Web.csproj", "notes.txt", "logo.png");

        Assert.Contains(nodes, n => n.Name == "notes.txt");
        Assert.Contains(nodes, n => n.Name == "logo.png");
    }

    [Fact]
    public async Task DoesNotListTheProjectFileTwice()
    {
        // The root already stands for the project file itself.
        var nodes = await LoadAsync("Web.csproj", "Program.cs");

        Assert.Single(nodes, n => n.Name == "Web.csproj");
    }

    [Fact]
    public async Task SkipsBuildOutputDirectories()
    {
        var nodes = await LoadAsync("Web.csproj",
            "Program.cs",
            "bin/Debug/Web.dll",
            "obj/project.assets.json");

        Assert.DoesNotContain(nodes, n => n.Name == "Web.dll");
        Assert.DoesNotContain(nodes, n => n.Name == "project.assets.json");
    }

    [Fact]
    public async Task SkipsToolingFilesThatOnlyAddNoise()
    {
        var nodes = await LoadAsync("Web.csproj",
            "Program.cs",
            "Web.csproj.user",
            ".DS_Store");

        Assert.DoesNotContain(nodes, n => n.Name == "Web.csproj.user");
        Assert.DoesNotContain(nodes, n => n.Name == ".DS_Store");
    }

    [Fact]
    public async Task KeepsTheEditorConfigDespiteItsLeadingDot()
    {
        // It configures the project's own formatting, so it belongs in the tree.
        var nodes = await LoadAsync("Web.csproj", "Program.cs", ".editorconfig");

        Assert.Contains(nodes, n => n.Name == ".editorconfig");
    }

    [Fact]
    public async Task MarksEveryShownFileAsOpenable()
    {
        var nodes = await LoadAsync("Web.csproj",
            "Views/Home/Index.cshtml",
            "appsettings.json",
            "wwwroot/site.css",
            "notes.txt");

        var files = nodes.Where(n => n.Kind is not (NodeKind.Folder or NodeKind.Project));

        Assert.All(files, node => Assert.True(node.IsOpenable,
            $"'{node.Name}' is shown but cannot be opened"));
    }

    [Fact]
    public async Task GivesEachKindItsOwnIcon()
    {
        var nodes = await LoadAsync("Web.csproj",
            "Program.cs",
            "Views/Home/Index.cshtml",
            "appsettings.json",
            "wwwroot/site.css");

        var icons = nodes
            .Where(n => n.Kind is NodeKind.SourceFile or NodeKind.RazorFile
                              or NodeKind.ConfigFile or NodeKind.WebAsset)
            .Select(n => n.Icon)
            .ToList();

        Assert.Equal(icons.Count, icons.Distinct().Count());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
