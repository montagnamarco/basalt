using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Projects;

namespace Basalt.Tests;

/// <summary>
/// The project system read from the solution file, not from the folders
/// around it: which projects a solution lists, which of them run, and where
/// the Solution Explorer shows them.
/// </summary>
public sealed class SolutionFileProjectSystemFixTests : IDisposable
{
    private const string VisualBasicType = "F184B08F-C81C-45F6-A57F-5ABD9991F28F";
    private const string SolutionFolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    private readonly string _root = Directory.CreateTempSubdirectory("basalt-sln-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>
    /// Outer.sln sits in sln/ and lists App and Lib, both beside it rather than
    /// under it. Stray is on disk and not listed.
    /// </summary>
    private (string Solution, string App, string Lib, string Stray) WriteSolutionOutsideItsFolders()
    {
        var app = WriteProject("App", "Exe");
        var lib = WriteProject("Lib", "Library");
        var stray = WriteProject("Stray", "Exe");

        var sln = Path.Combine(_root, "sln", "Outer.sln");
        Directory.CreateDirectory(Path.GetDirectoryName(sln)!);
        File.WriteAllText(sln,
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            $"Project(\"{{{VisualBasicType}}}\") = \"App\", \"..\\App\\App.vbproj\", \"{{11111111-1111-1111-1111-111111111111}}\"\nEndProject\n" +
            $"Project(\"{{{SolutionFolderType}}}\") = \"Solution Items\", \"Solution Items\", \"{{33333333-3333-3333-3333-333333333333}}\"\nEndProject\n" +
            $"Project(\"{{{VisualBasicType}}}\") = \"Lib\", \"..\\Lib\\Lib.vbproj\", \"{{22222222-2222-2222-2222-222222222222}}\"\nEndProject\n");

        return (sln, app, lib, stray);
    }

    private string WriteProject(string name, string outputType)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, name + ".vbproj");
        File.WriteAllText(project,
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>{outputType}</OutputType>" +
            "<TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(directory, "Program.vb"), "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
        return project;
    }

    [Fact]
    public void SolutionFileListsTheProjectsItNamesWhereverTheyAreAndNotTheOthers()
    {
        var (sln, app, lib, stray) = WriteSolutionOutsideItsFolders();

        var projects = SolutionFile.ProjectsOf(sln);

        Assert.Equal([app, lib], projects);
        Assert.DoesNotContain(stray, projects);
    }

    [Fact]
    public void SlnxFileListsProjectsInsideFolderGroups()
    {
        var app = WriteProject("App", "Exe");
        var slnx = Path.Combine(_root, "Modern.slnx");
        File.WriteAllText(slnx,
            "<Solution><Folder Name=\"/src/\"><Project Path=\"App\\App.vbproj\" /></Folder></Solution>");

        Assert.Equal([app], SolutionFile.ProjectsOf(slnx));
    }

    [Fact]
    public void StartupProjectIsTheExecutableTheSolutionListsOutsideItsFolder()
    {
        var (sln, app, _, _) = WriteSolutionOutsideItsFolders();

        Assert.Equal(app, ProjectCatalog.StartupProjectOf(sln));
        Assert.Equal([app], ProjectCatalog.RunnableProjectsOf(sln));
    }

    [Fact]
    public void ABadEntryInASolutionFileIsSkippedAndTheOtherProjectsStillOpen()
    {
        // A web-site entry and an entry no path may hold: neither may abort the open.
        var app = WriteProject("App", "Exe");
        var sln = Path.Combine(_root, "Mixed.sln");
        File.WriteAllText(sln,
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            $"Project(\"{{{VisualBasicType}}}\") = \"Site\", \"http://localhost:50123\", \"{{44444444-4444-4444-4444-444444444444}}\"\nEndProject\n" +
            $"Project(\"{{{VisualBasicType}}}\") = \"Broken\", \"bad\0name.vbproj\", \"{{55555555-5555-5555-5555-555555555555}}\"\nEndProject\n" +
            $"Project(\"{{{VisualBasicType}}}\") = \"App\", \"App\\App.vbproj\", \"{{11111111-1111-1111-1111-111111111111}}\"\nEndProject\n");

        Assert.Equal([app], SolutionFile.ProjectsOf(sln));
    }

    [Fact]
    public void ProjectsInNestedSolutionFoldersAreListed()
    {
        var app = WriteProject("App", "Exe");
        var slnx = Path.Combine(_root, "Nested.slnx");
        File.WriteAllText(slnx,
            "<Solution><Folder Name=\"/src/\"><Folder Name=\"/src/core/\"><Project Path=\"App/App.vbproj\" /></Folder></Folder></Solution>");
        Assert.Equal([app], SolutionFile.ProjectsOf(slnx));

        // Classic format: a solution folder is a group, and the project inside it is listed.
        var sln = Path.Combine(_root, "Nested.sln");
        File.WriteAllText(sln,
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            $"Project(\"{{{SolutionFolderType}}}\") = \"src\", \"src\", \"{{66666666-6666-6666-6666-666666666666}}\"\nEndProject\n" +
            $"Project(\"{{{VisualBasicType}}}\") = \"App\", \"App\\App.vbproj\", \"{{11111111-1111-1111-1111-111111111111}}\"\nEndProject\n" +
            "Global\n\tGlobalSection(NestedProjects) = preSolution\n\t\t{11111111-1111-1111-1111-111111111111} = {66666666-6666-6666-6666-666666666666}\n\tEndGlobalSection\nEndGlobal\n");
        Assert.Equal([app], SolutionFile.ProjectsOf(sln));
    }

    [Fact]
    public void ASolutionFileIsReadAgainWhenItChanges()
    {
        var (sln, app, lib, _) = WriteSolutionOutsideItsFolders();
        Assert.Equal([app, lib], SolutionFile.ProjectsOf(sln));

        // Shorter content: the write time and length both change, so the cached list must not survive.
        File.WriteAllText(sln,
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            $"Project(\"{{{VisualBasicType}}}\") = \"Lib\", \"..\\Lib\\Lib.vbproj\", \"{{22222222-2222-2222-2222-222222222222}}\"\nEndProject\n");

        Assert.Equal([lib], SolutionFile.ProjectsOf(sln));
    }

    [Fact]
    public void SolutionExplorerShowsTheListedProjectsAndNotTheStrayOne()
    {
        var (sln, _, _, _) = WriteSolutionOutsideItsFolders();
        var explorer = new SolutionExplorerViewModel();

        explorer.Load(sln);

        var projects = explorer.Roots[0].Children
            .Where(node => node.Kind == NodeKind.Project)
            .Select(node => node.Name)
            .ToList();
        Assert.Equal(["App", "Lib"], projects);
    }
}

/// <summary>
/// Where launchSettings.json lives: the AppDesignerFolder the SDK reads it from.
/// </summary>
public sealed class LaunchSettingsLocationFixTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-launch-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string WriteVbProject(string extraProperties = "")
    {
        var project = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(project,
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType>{extraProperties}</PropertyGroup></Project>");
        return project;
    }

    [Fact]
    public void LaunchSettingsOfAVisualBasicProjectLiveInMyProject()
    {
        var project = WriteVbProject();

        Assert.Equal(Path.Combine(_root, "My Project", "launchSettings.json"), LaunchSettingsLocation.PathOf(project));
    }

    [Fact]
    public void AnExplicitAppDesignerFolderIsRespected()
    {
        var project = WriteVbProject("<AppDesignerFolder>Settings</AppDesignerFolder>");

        Assert.Equal(Path.Combine(_root, "Settings", "launchSettings.json"), LaunchSettingsLocation.PathOf(project));
    }

    [Fact]
    public async Task SavingMigratesTheOlderPropertiesFileIntoMyProject()
    {
        var project = WriteVbProject();
        var legacyDirectory = Path.Combine(_root, "Properties");
        Directory.CreateDirectory(legacyDirectory);
        var legacy = Path.Combine(legacyDirectory, "launchSettings.json");
        File.WriteAllText(legacy, """
            {
              "profiles": {
                "App": { "commandName": "Project", "applicationUrl": "http://localhost:7001" }
              }
            }
            """);

        var settings = LaunchSettings.Load(project);
        Assert.Equal("http://localhost:7001", settings.ApplicationUrl);

        await settings.SaveAsync();

        Assert.True(File.Exists(LaunchSettingsLocation.PathOf(project)));
        Assert.False(File.Exists(legacy));
        Assert.Equal("http://localhost:7001", LaunchSettings.Load(project).ApplicationUrl);
    }
}

/// <summary>
/// Package references need a version to be pinned.
/// </summary>
public sealed class ProjectPropertiesPackageFixTests
{
    [Fact]
    public void AddingAPackageWithoutAVersionIsRefusedRatherThanWrittenAsAWildcard()
    {
        var project = Path.Combine(Directory.CreateTempSubdirectory("basalt-pkg-").FullName, "App.vbproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup /></Project>");
        var properties = ProjectProperties.Load(project);

        Assert.Throws<ArgumentException>(() => properties.AddPackage("Newtonsoft.Json", " "));
        Assert.DoesNotContain("PackageReference", properties.ToXml());
    }

    [Fact]
    public void UnderCentralPackageManagementTheVersionIsLeftOutNotRefused()
    {
        var project = Path.Combine(Directory.CreateTempSubdirectory("basalt-cpm-").FullName, "App.vbproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup /></Project>");
        var properties = ProjectProperties.Load(project);

        properties.AddPackage("Newtonsoft.Json", "", centralPackageManagement: true);

        var xml = properties.ToXml();
        Assert.Contains("Include=\"Newtonsoft.Json\"", xml);
        Assert.DoesNotContain("Version=", xml);
    }

    [Fact]
    public void AProjectReferenceEditorWithoutCentralManagementRefusesABlankVersion()
    {
        const string xml = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup />\n</Project>";

        Assert.Throws<ArgumentException>(() =>
            ProjectReferenceEditor.AddPackageReference(xml, "Newtonsoft.Json", ""));

        var managed = ProjectReferenceEditor.AddPackageReference(xml, "Newtonsoft.Json", "", centralPackageManagement: true);
        Assert.Contains("Include=\"Newtonsoft.Json\"", managed);
        Assert.DoesNotContain("Version=\"*\"", managed);
    }
}

/// <summary>
/// A project-level build error still says which project it is about.
/// </summary>
public sealed class BuildDiagnosticFixTests
{
    [Fact]
    public void AProjectLevelNuGetErrorNamesTheProjectFileItWasReportedAgainst()
    {
        const string output =
            "/percorso/App.vbproj : error NU1102: Unable to find package Newtonsoft.Json with version (>= 99.0.0) [/percorso/App.vbproj]";

        var d = Assert.Single(MsBuildBuildService.ParseDiagnostics(output));

        Assert.Equal("NU1102", d.Id);
        Assert.Null(d.FilePath);
        Assert.Equal("/percorso/App.vbproj", d.ProjectPath);
        Assert.Equal("/percorso/App.vbproj", d.LocationPath);
    }

    [Fact]
    public void TheSameLocationlessErrorFromTwoProjectsIsListedForEach()
    {
        // Two projects reporting the same NuGet error are two problems, not one.
        const string output = """
            /percorso/App.vbproj : error NU1102: Unable to find package Newtonsoft.Json [/percorso/App.vbproj]
            /percorso/Lib.vbproj : error NU1102: Unable to find package Newtonsoft.Json [/percorso/Lib.vbproj]
            """;

        var diagnostics = MsBuildBuildService.ParseDiagnostics(output);

        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(["/percorso/App.vbproj", "/percorso/Lib.vbproj"], diagnostics.Select(d => d.ProjectPath));
    }
}

/// <summary>
/// A Visual Basic web template writes launch settings where dotnet run reads them.
/// </summary>
public sealed class TemplateLaunchSettingsFixTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-template-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task AWebTemplateWritesItsLaunchSettingsToMyProject()
    {
        var created = await SolutionTemplates.CreateAsync(_root, "WebSite", ProjectTemplate.WebApi);
        var directory = Path.GetDirectoryName(created.ProjectPath)!;

        Assert.True(File.Exists(Path.Combine(directory, "My Project", "launchSettings.json")));
        Assert.False(File.Exists(Path.Combine(directory, "Properties", "launchSettings.json")));
    }
}

/// <summary>
/// Launch settings an older IDE left in Properties/ move to where dotnet run
/// reads them as soon as the solution opens, not only when the user saves.
/// </summary>
public sealed class LegacyLaunchSettingsOnOpenFixTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-legacy-open-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [AvaloniaFact]
    public async Task OpeningASolutionMovesAVisualBasicProjectsLegacyLaunchSettings()
    {
        var project = Path.Combine(_root, "App", "App.vbproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(_root, "App", "Program.vb"), "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
        var legacyDirectory = Path.Combine(_root, "App", "Properties");
        Directory.CreateDirectory(legacyDirectory);
        var legacy = Path.Combine(legacyDirectory, "launchSettings.json");
        File.WriteAllText(legacy, "{ \"profiles\": { \"App\": { \"commandName\": \"Project\", \"applicationUrl\": \"http://localhost:7002\" } } }");
        var solution = Path.Combine(_root, "App.sln");
        File.WriteAllText(solution,
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            $"Project(\"{{F184B08F-C81C-45F6-A57F-5ABD9991F28F}}\") = \"App\", \"App\\App.vbproj\", \"{{11111111-1111-1111-1111-111111111111}}\"\nEndProject\n");

        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(solution);

        Assert.True(File.Exists(LaunchSettingsLocation.PathOf(project)));
        Assert.False(File.Exists(legacy));
    }
}

/// <summary>
/// Opening a Problems row goes to the project file for a project-level error.
/// </summary>
public sealed class ProblemNavigationFixTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-problem-nav-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void AProjectLevelErrorOpensItsProjectFileWithNoPosition()
    {
        var project = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var diagnostic = new IdeDiagnostic("NU1102", "Unable to find package", DiagnosticSeverity.Error, null, 0, 0, project);

        Assert.Equal((project, 0, 0), ProblemNavigation.TargetOf(diagnostic));
    }

    [Fact]
    public void ASourceErrorOpensItsFileAtTheLine()
    {
        var file = Path.Combine(_root, "Program.vb");
        File.WriteAllText(file, "Module Program\nEnd Module\n");
        var diagnostic = new IdeDiagnostic("BC30451", "Not declared", DiagnosticSeverity.Error, file, 2, 5);

        Assert.Equal((file, 2, 5), ProblemNavigation.TargetOf(diagnostic));
    }
}
